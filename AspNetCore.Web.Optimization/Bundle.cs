//
// Bundle, ScriptBundle, StyleBundle and the transform/orderer contracts around them.
//
// Naming and shape follow System.Web.Optimization exactly, because the point of this assembly is that
// an application's existing BundleConfig.cs compiles against it untouched.
//

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Web;
using System.Web.Hosting;

namespace System.Web.Optimization
{
	/// <summary>One file inside a bundle, after virtual paths have been resolved.</summary>
	public class BundleFile
	{
		public BundleFile (string virtualFile, string physicalPath)
		{
			VirtualFile = virtualFile;
			PhysicalPath = physicalPath;
		}

		public string VirtualFile { get; }

		public string PhysicalPath { get; }

		public IList<IItemTransform> Transforms { get; } = new List<IItemTransform> ();
	}

	/// <summary>A transform applied to one file before it joins the bundle.</summary>
	public interface IItemTransform
	{
		string Process (string includedVirtualPath, string input);
	}

	/// <summary>A transform applied to the whole bundle once it has been assembled.</summary>
	public interface IBundleTransform
	{
		void Process (BundleContext context, BundleResponse response);
	}

	/// <summary>Decides the order files appear in the bundle.</summary>
	public interface IBundleOrderer
	{
		IEnumerable<BundleFile> OrderFiles (BundleContext context, IEnumerable<BundleFile> files);
	}

	/// <summary>
	/// Include order, unchanged. The real implementation applies FileSetOrderList here - jquery before
	/// jquery-ui and so on - which only matters for the framework-supplied bundles it also shipped.
	/// Declaration order is what an author can actually see and reason about.
	/// </summary>
	public class DefaultBundleOrderer : IBundleOrderer
	{
		public virtual IEnumerable<BundleFile> OrderFiles (BundleContext context, IEnumerable<BundleFile> files)
		{
			return files;
		}
	}

	public class BundleContext
	{
		public BundleContext (HttpContextBase context, BundleCollection collection, string bundleVirtualPath)
		{
			HttpContext = context;
			BundleCollection = collection;
			BundleVirtualPath = bundleVirtualPath;
		}

		public HttpContextBase HttpContext { get; set; }

		public BundleCollection BundleCollection { get; set; }

		public string BundleVirtualPath { get; set; }

		/// <summary>
		/// False when rendering individual files for debugging. Transforms that minify are expected to
		/// check this; nothing here minifies, so it is carried for compatibility with custom transforms.
		/// </summary>
		public bool EnableOptimizations { get; set; } = true;

		public bool EnableInstrumentation { get; set; }
	}

	public class BundleResponse
	{
		public BundleResponse ()
		{
		}

		public BundleResponse (string content, IEnumerable<BundleFile> files)
		{
			Content = content;
			Files = files;
		}

		public string Content { get; set; }

		public IEnumerable<BundleFile> Files { get; set; }

		public string ContentType { get; set; }

		public HttpCacheability Cacheability { get; set; } = HttpCacheability.Public;
	}

	public class Bundle
	{
		readonly List<string> includes = new List<string> ();

		public Bundle (string virtualPath)
			: this (virtualPath, null)
		{
		}

		public Bundle (string virtualPath, string cdnPath)
		{
			if (String.IsNullOrEmpty (virtualPath))
				throw new ArgumentException ("A bundle needs a virtual path, e.g. \"~/bundles/site\".",
							     nameof (virtualPath));

			// Normalised to "~/..." on the way in so GetBundleFor can compare against a request path
			// without every caller having to agree on a convention.
			Path = virtualPath.StartsWith ("~/", StringComparison.Ordinal)
				? virtualPath
				: "~/" + virtualPath.TrimStart ('/');

			CdnPath = cdnPath;
		}

		public string Path { get; }

		public string CdnPath { get; set; }

		public string CdnFallbackExpression { get; set; }

		public IList<IBundleTransform> Transforms { get; } = new List<IBundleTransform> ();

		public IBundleOrderer Orderer { get; set; } = new DefaultBundleOrderer ();

		public string ConcatenationToken { get; set; } = ";" + Environment.NewLine;

		public bool EnableFileExtensionReplacement { get; set; }

		public ReadOnlyCollection<string> Includes {
			get { return new ReadOnlyCollection<string> (includes); }
		}

		public virtual Bundle Include (params string [] virtualPaths)
		{
			if (virtualPaths == null)
				throw new ArgumentNullException (nameof (virtualPaths));

			includes.AddRange (virtualPaths);
			return this;
		}

		public virtual Bundle Include (string virtualPath, params IItemTransform [] transforms)
		{
			// Per-file transforms are recorded against the include pattern and re-attached when the
			// pattern is expanded, since one pattern can produce many files.
			includes.Add (virtualPath);
			if (transforms != null && transforms.Length > 0)
				itemTransforms [virtualPath] = transforms;

			return this;
		}

		readonly Dictionary<string, IItemTransform []> itemTransforms =
			new Dictionary<string, IItemTransform []> (StringComparer.OrdinalIgnoreCase);

		public virtual Bundle IncludeDirectory (string directoryVirtualPath, string searchPattern)
		{
			return IncludeDirectory (directoryVirtualPath, searchPattern, searchSubdirectories: false);
		}

		public virtual Bundle IncludeDirectory (string directoryVirtualPath, string searchPattern,
						        bool searchSubdirectories)
		{
			if (String.IsNullOrEmpty (searchPattern))
				throw new ArgumentException ("A search pattern is required.", nameof (searchPattern));

			string physical = MapPath (directoryVirtualPath);
			if (physical == null || !Directory.Exists (physical))
				return this;

			var option = searchSubdirectories ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
			string prefix = directoryVirtualPath.TrimEnd ('/');

			foreach (string file in Directory.EnumerateFiles (physical, searchPattern, option).OrderBy (f => f, StringComparer.OrdinalIgnoreCase)) {
				string relative = file.Substring (physical.Length).Replace (Path2.DirectorySeparator, '/').TrimStart ('/');
				includes.Add (prefix + "/" + relative);
			}

			return this;
		}

		/// <summary>
		/// Expands every include into concrete files, resolving <c>{version}</c> and <c>*</c> patterns.
		/// </summary>
		public virtual IEnumerable<BundleFile> EnumerateFiles (BundleContext context)
		{
			var files = new List<BundleFile> ();
			var seen = new HashSet<string> (StringComparer.OrdinalIgnoreCase);

			foreach (string include in includes) {
				foreach (BundleFile file in Expand (include)) {
					// A file pulled in by two patterns must appear once. Including jquery twice is not
					// a build error anywhere, but it does break jQuery.
					if (seen.Add (file.PhysicalPath))
						files.Add (file);
				}
			}

			return Orderer != null ? Orderer.OrderFiles (context, files) : files;
		}

		IEnumerable<BundleFile> Expand (string include)
		{
			IItemTransform [] transforms;
			itemTransforms.TryGetValue (include, out transforms);

			// "{version}" is System.Web.Optimization's own token, not a glob: it exists so
			// "~/Scripts/jquery-{version}.js" keeps working across a NuGet upgrade that renames the
			// file. Translated to a glob here, then matched most-recent-first.
			string pattern = include.Replace ("{version}", "*");

			int lastSlash = pattern.LastIndexOf ('/');
			string directoryVirtual = lastSlash >= 0 ? pattern.Substring (0, lastSlash) : "~";
			string fileSpec = lastSlash >= 0 ? pattern.Substring (lastSlash + 1) : pattern;

			string physicalDirectory = MapPath (directoryVirtual);
			if (physicalDirectory == null || !Directory.Exists (physicalDirectory))
				yield break;

			if (fileSpec.IndexOf ('*') < 0) {
				string single = System.IO.Path.Combine (physicalDirectory, fileSpec);
				if (File.Exists (single)) {
					var file = new BundleFile (directoryVirtual + "/" + fileSpec, single);
					AddTransforms (file, transforms);
					yield return file;
				}

				yield break;
			}

			var matches = Directory.EnumerateFiles (physicalDirectory, fileSpec, SearchOption.TopDirectoryOnly)
				.OrderBy (f => f, StringComparer.OrdinalIgnoreCase)
				.ToArray ();

			foreach (string match in matches) {
				var file = new BundleFile (
					directoryVirtual + "/" + System.IO.Path.GetFileName (match), match);
				AddTransforms (file, transforms);
				yield return file;
			}
		}

		static void AddTransforms (BundleFile file, IItemTransform [] transforms)
		{
			if (transforms == null)
				return;

			foreach (IItemTransform transform in transforms)
				file.Transforms.Add (transform);
		}

		/// <summary>
		/// Reads every file, applies the transforms and returns the bundle's content.
		/// </summary>
		public virtual BundleResponse GenerateBundleResponse (BundleContext context)
		{
			IEnumerable<BundleFile> files = EnumerateFiles (context).ToArray ();
			var content = new StringBuilder ();

			foreach (BundleFile file in files) {
				string text = File.ReadAllText (file.PhysicalPath);

				foreach (IItemTransform transform in file.Transforms)
					text = transform.Process (file.VirtualFile, text);

				content.Append (text);
				content.Append (ConcatenationToken);
			}

			var response = new BundleResponse (content.ToString (), files);

			foreach (IBundleTransform transform in Transforms)
				transform.Process (context, response);

			return response;
		}

		internal static string MapPath (string virtualPath)
		{
			try {
				return HostingEnvironment.MapPath (virtualPath);
			} catch {
				// MapPath throws outside a hosted application. A bundle that cannot resolve its
				// directory contributes no files, which is the same outcome as an empty directory.
				return null;
			}
		}

		static class Path2
		{
			public static readonly char DirectorySeparator = System.IO.Path.DirectorySeparatorChar;
		}
	}

	/// <summary>A bundle of JavaScript. Rendered as one <c>&lt;script&gt;</c> tag.</summary>
	public class ScriptBundle : Bundle
	{
		public ScriptBundle (string virtualPath)
			: base (virtualPath)
		{
		}

		public ScriptBundle (string virtualPath, string cdnPath)
			: base (virtualPath, cdnPath)
		{
		}
	}

	/// <summary>A bundle of CSS. Rendered as one <c>&lt;link&gt;</c> tag.</summary>
	public class StyleBundle : Bundle
	{
		public StyleBundle (string virtualPath)
			: base (virtualPath)
		{
			// CSS is concatenated with a newline, never a semicolon: ";" between two stylesheets is a
			// syntax error that browsers recover from by discarding the following rule.
			ConcatenationToken = Environment.NewLine;
		}

		public StyleBundle (string virtualPath, string cdnPath)
			: base (virtualPath, cdnPath)
		{
			ConcatenationToken = Environment.NewLine;
		}
	}
}
