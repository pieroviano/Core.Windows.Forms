//
// @Scripts.Render (...) and @Styles.Render (...).
//
// These are what views actually call, so their output is the compatibility surface that matters:
// with optimizations on, one tag per bundle pointing at the versioned bundle URL; with them off, one
// tag per file, so the browser's sources pane shows real file names and a debugger can break in them.
// That switch is the entire reason bundling is tolerable to develop against, and it is preserved.
//

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Web;

namespace System.Web.Optimization
{
	public static class Scripts
	{
		/// <summary>Overrides the tag format. Must contain exactly one <c>{0}</c>, the URL.</summary>
		public static string DefaultTagFormat { get; set; } = "<script src=\"{0}\"></script>";

		public static IHtmlString Render (params string [] paths)
		{
			return RenderFormat (DefaultTagFormat, paths);
		}

		public static IHtmlString RenderFormat (string tagFormat, params string [] paths)
		{
			return BundleRenderer.Render (tagFormat, paths, styles: false);
		}

		/// <summary>The bundle's URL, for a tag you are writing yourself.</summary>
		public static IHtmlString Url (string virtualPath)
		{
			return new HtmlString (BundleRenderer.UrlFor (virtualPath) ?? String.Empty);
		}
	}

	public static class Styles
	{
		public static string DefaultTagFormat { get; set; } = "<link href=\"{0}\" rel=\"stylesheet\"/>";

		public static IHtmlString Render (params string [] paths)
		{
			return RenderFormat (DefaultTagFormat, paths);
		}

		public static IHtmlString RenderFormat (string tagFormat, params string [] paths)
		{
			return BundleRenderer.Render (tagFormat, paths, styles: true);
		}

		public static IHtmlString Url (string virtualPath)
		{
			return new HtmlString (BundleRenderer.UrlFor (virtualPath) ?? String.Empty);
		}
	}

	static class BundleRenderer
	{
		public static IHtmlString Render (string tagFormat, string [] paths, bool styles)
		{
			if (tagFormat == null)
				throw new ArgumentNullException (nameof (tagFormat));
			if (paths == null)
				throw new ArgumentNullException (nameof (paths));

			var html = new StringBuilder ();

			foreach (string path in paths) {
				foreach (string url in UrlsFor (path))
					html.AppendFormat (CultureInfo.InvariantCulture, tagFormat, HttpUtility.HtmlAttributeEncode (url))
					    .AppendLine ();
			}

			return new HtmlString (html.ToString ());
		}

		public static string UrlFor (string virtualPath)
		{
			return BundleTable.Bundles.ResolveBundleUrl (virtualPath)
				?? Absolute (virtualPath);
		}

		static IEnumerable<string> UrlsFor (string path)
		{
			Bundle bundle = BundleTable.Bundles.GetBundleFor (path);

			if (bundle == null) {
				// Not a bundle: render the path as given. Real System.Web.Optimization does the same,
				// which is what lets a view mix @Scripts.Render ("~/bundles/x") and a plain file path.
				yield return Absolute (path);
				yield break;
			}

			if (BundleTable.EnableOptimizations) {
				yield return BundleTable.Bundles.ResolveBundleUrl (bundle.Path);
				yield break;
			}

			var context = new BundleContext (Current (), BundleTable.Bundles, bundle.Path) {
				EnableOptimizations = false,
			};

			foreach (BundleFile file in bundle.EnumerateFiles (context))
				yield return Absolute (file.VirtualFile);
		}

		static HttpContextBase Current ()
		{
			HttpContext current = HttpContext.Current;
			return current != null ? new HttpContextWrapper (current) : null;
		}

		static string Absolute (string virtualPath)
		{
			if (String.IsNullOrEmpty (virtualPath))
				return virtualPath;

			try {
				return VirtualPathUtility.ToAbsolute (virtualPath);
			} catch {
				// ToAbsolute throws for a path that is already absolute or is a full URL - both of
				// which are perfectly reasonable things to hand these helpers.
				return virtualPath;
			}
		}
	}
}
