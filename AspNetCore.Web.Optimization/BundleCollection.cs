//
// BundleCollection and BundleTable - the registry, and how a bundle URL gets served.
//
// How serving works here, and why
// -------------------------------
// Real System.Web.Optimization installs an IHttpModule (BundleModule) through a
// PreApplicationStartMethod, and that module intercepts requests whose path matches a registered
// bundle. This port registers a System.Web.Routing Route per bundle instead.
//
// The reason is that the outcome for the application author is identical - no web.config edit, the
// bundle URL just works after BundleConfig.RegisterBundles runs - while the mechanism is one the port
// already supports and already tests. PreApplicationStartMethod scanning is a corner of the runtime
// that would need its own work to be trustworthy, and it would buy nothing observable.
//
// The visible consequence: UrlRoutingModule has to be registered, which the port's generated root
// configuration already does for every application.
//

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web;
using System.Web.Routing;

namespace System.Web.Optimization
{
	public class BundleCollection : IEnumerable<Bundle>
	{
		readonly Dictionary<string, Bundle> bundles = new Dictionary<string, Bundle> (StringComparer.OrdinalIgnoreCase);

		/// <summary>Patterns never included by a directory or wildcard include.</summary>
		public IList<string> IgnoreList { get; } = new List<string> ();

		public bool UseCdn { get; set; }

		public int Count {
			get { return bundles.Count; }
		}

		public void Add (Bundle bundle)
		{
			if (bundle == null)
				throw new ArgumentNullException (nameof (bundle));

			if (bundles.ContainsKey (bundle.Path))
				throw new InvalidOperationException (
					"A bundle is already registered at '" + bundle.Path + "'. Two bundles cannot share a " +
					"URL - the second would be unreachable.");

			bundles [bundle.Path] = bundle;
			RegisterRoute (bundle);
		}

		public Bundle GetBundleFor (string bundleVirtualPath)
		{
			if (String.IsNullOrEmpty (bundleVirtualPath))
				return null;

			Bundle bundle;
			return bundles.TryGetValue (Normalize (bundleVirtualPath), out bundle) ? bundle : null;
		}

		public bool Remove (Bundle bundle)
		{
			return bundle != null && bundles.Remove (bundle.Path);
		}

		public void Clear ()
		{
			bundles.Clear ();
		}

		public IEnumerator<Bundle> GetEnumerator ()
		{
			return bundles.Values.GetEnumerator ();
		}

		System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator ()
		{
			return GetEnumerator ();
		}

		/// <summary>
		/// The URL to request this bundle at, including the content hash that busts caches.
		/// </summary>
		public string ResolveBundleUrl (string bundleVirtualPath)
		{
			return ResolveBundleUrl (bundleVirtualPath, includeContentHash: true);
		}

		public string ResolveBundleUrl (string bundleVirtualPath, bool includeContentHash)
		{
			Bundle bundle = GetBundleFor (bundleVirtualPath);
			if (bundle == null)
				return null;

			if (UseCdn && !String.IsNullOrEmpty (bundle.CdnPath))
				return bundle.CdnPath;

			string url = VirtualPathUtility.ToAbsolute (bundle.Path);
			if (!includeContentHash)
				return url;

			// Content hash rather than a build number: a deployment that does not change a file must
			// not invalidate its cached copy, and one that does must invalidate it immediately.
			return url + "?v=" + ComputeHash (bundle);
		}

		internal string ComputeHash (Bundle bundle)
		{
			var context = new BundleContext (CurrentHttpContext (), this, bundle.Path);
			var content = new StringBuilder ();

			foreach (BundleFile file in bundle.EnumerateFiles (context)) {
				content.Append (file.VirtualFile);
				try {
					content.Append (System.IO.File.GetLastWriteTimeUtc (file.PhysicalPath)
						.ToString ("O", CultureInfo.InvariantCulture));
					content.Append (new System.IO.FileInfo (file.PhysicalPath).Length);
				} catch {
					// An unreadable file still has to produce a stable token rather than throw from a
					// view; it will fail loudly when the bundle is actually served.
				}
			}

			using (var sha = SHA256.Create ()) {
				byte [] hash = sha.ComputeHash (Encoding.UTF8.GetBytes (content.ToString ()));
				return Convert.ToBase64String (hash)
					.TrimEnd ('=').Replace ('+', '-').Replace ('/', '_');
			}
		}

		static HttpContextBase CurrentHttpContext ()
		{
			HttpContext current = HttpContext.Current;
			return current != null ? new HttpContextWrapper (current) : null;
		}

		static string Normalize (string virtualPath)
		{
			if (virtualPath.StartsWith ("~/", StringComparison.Ordinal))
				return virtualPath;

			string application = HttpRuntime.AppDomainAppVirtualPath ?? "/";
			if (application != "/" && virtualPath.StartsWith (application, StringComparison.OrdinalIgnoreCase))
				virtualPath = virtualPath.Substring (application.Length);

			return "~/" + virtualPath.TrimStart ('/');
		}

		void RegisterRoute (Bundle bundle)
		{
			string template = bundle.Path.Substring (2);

			try {
				// Inserted at the FRONT, not appended. Real System.Web.Optimization intercepts bundle
				// URLs in an IHttpModule that runs before routing at all, so registration order does
				// not matter there. Here bundles are routes, and appending would put them behind the
				// conventional "{controller}/{action}/{id}" - which matches "/bundles/app" quite
				// happily and answers it with "the controller for path '/bundles/app' was not found".
				//
				// Front-insertion also means BundleConfig.RegisterBundles can stay where every ASP.NET
				// application already has it: after RegisterRoutes in Application_Start.
				RouteTable.Routes.Insert (0, new BundleRoute (template, new BundleRouteHandler (this, bundle)));
			} catch (InvalidOperationException) {
				// RouteTable is read-locked while a request is in flight. Bundles are registered from
				// Application_Start, so this only happens if someone registers one later - in which
				// case the bundle is still resolvable, just not servable, and that is worth surfacing
				// where it happens rather than as a 404 much later.
				throw;
			}
		}
	}

	public static class BundleTable
	{
		static bool? enableOptimizations;

		public static BundleCollection Bundles { get; } = new BundleCollection ();

		/// <summary>
		/// Whether bundles render as one URL or as their individual files. Defaults to the inverse of
		/// <c>&lt;compilation debug="true"&gt;</c>, which is what makes a debug build show real file
		/// names in the browser's sources pane.
		/// </summary>
		public static bool EnableOptimizations {
			get {
				if (enableOptimizations.HasValue)
					return enableOptimizations.Value;

				return !HttpContext.Current?.IsDebuggingEnabled ?? true;
			}
			set { enableOptimizations = value; }
		}

		/// <summary>Test seam: forget an explicit EnableOptimizations and go back to the debug flag.</summary>
		internal static void ResetOptimizations ()
		{
			enableOptimizations = null;
		}
	}
}
