//
// Which port packages this application needs, and why.
//
// Every rule produces its evidence alongside its answer, because a detection nobody can audit is
// indistinguishable from a guess. The report prints "added AspNetCore.Web.Mvc (MyApp.csproj:41
// references System.Web.Mvc)" rather than a bare package list.
//
// The transitive packages - AspNetCore.Web, .Configuration, .Services, .Extensions, .ConfigBridge -
// are deliberately NOT emitted. PORTING-GUIDE.md step 1 says the hosting package brings them, so
// listing them would be noise that drifts out of date the moment that dependency graph changes.
//

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PortProject
{
	sealed class Detection
	{
		public Detection (string package, string reason, string evidence)
		{
			Package = package;
			Reason = reason;
			Evidence = evidence;
		}

		public string Package { get; }

		public string Reason { get; }

		public string Evidence { get; }
	}

	static class StackDetector
	{
		public const string HostingPackage = "AspNetCore.Web.Hosting.Kestrel";

		/// <summary>The Razor view engine is five packages that always travel together.</summary>
		static readonly string [] RazorPackages = {
			"AspNetCore.Web.WebPages.Base",
			"AspNetCore.Web.WebPages.Razor",
			"AspNetCore.Web.WebPages.Deployment",
			"AspNetCore.Web.Razor",
			"AspNetCore.Web.Infrastructure",
		};

		public static IReadOnlyList<Detection> Detect (LegacyProject project, ApplicationFiles files,
							       WebConfigFacts webConfig)
		{
			var detections = new List<Detection> ();
			var added = new HashSet<string> (StringComparer.OrdinalIgnoreCase);

			void Add (string package, string reason, string evidence)
			{
				if (added.Add (package))
					detections.Add (new Detection (package, reason, evidence));
			}

			void AddRazor (string reason, string evidence)
			{
				foreach (string package in RazorPackages)
					Add (package, reason, evidence);
			}

			// Always. It is the entry point, and without it nothing else is reachable.
			Add (HostingPackage, "the host and the build assets - always required", null);

			foreach (Reference reference in project.References) {
				string evidence = project.FileName + ":" + reference.Line;

				switch (reference.Name) {
				case "System.Web.Mvc":
					Add ("AspNetCore.Web.Mvc", "references System.Web.Mvc", evidence);
					AddRazor ("MVC views are Razor", evidence);
					break;
				case "System.Web.Optimization":
					Add ("AspNetCore.Web.Optimization", "references System.Web.Optimization", evidence);
					break;
				case "System.Web.Http":
				case "System.Web.Http.WebHost":
					Add ("AspNetCore.Web.Http", "references System.Web.Http", evidence);
					Add ("AspNetCore.Web.Http.WebHost", "Web API needs its System.Web host", evidence);
					Add ("AspNetCore.Net.Http.Formatting", "Web API needs the media-type formatters", evidence);
					break;
				case "System.Web.WebPages":
				case "System.Web.Razor":
					AddRazor ("references " + reference.Name, evidence);
					break;
				case "System.Web.DynamicData":
					Add ("AspNetCore.Web.DynamicData", "references System.Web.DynamicData", evidence);
					AddRazor ("Dynamic Data scaffolds through Web Pages", evidence);
					break;
				}
			}

			foreach (PackageReference package in project.PackageReferences) {
				switch (package.Id) {
				case "Microsoft.AspNet.Mvc":
					Add ("AspNetCore.Web.Mvc", "package Microsoft.AspNet.Mvc", package.Evidence);
					AddRazor ("MVC views are Razor", package.Evidence);
					break;
				case "Microsoft.AspNet.Web.Optimization":
					Add ("AspNetCore.Web.Optimization", "package Microsoft.AspNet.Web.Optimization",
					     package.Evidence);
					break;
				case "Microsoft.AspNet.WebApi":
				case "Microsoft.AspNet.WebApi.Core":
				case "Microsoft.AspNet.WebApi.WebHost":
					Add ("AspNetCore.Web.Http", "package " + package.Id, package.Evidence);
					Add ("AspNetCore.Web.Http.WebHost", "Web API needs its System.Web host", package.Evidence);
					Add ("AspNetCore.Net.Http.Formatting", "Web API needs the media-type formatters",
					     package.Evidence);
					break;
				case "Microsoft.AspNet.Razor":
				case "Microsoft.AspNet.WebPages":
					AddRazor ("package " + package.Id, package.Evidence);
					break;
				}
			}

			// On-disk evidence. A file is harder to argue with than a reference, and some
			// applications carry stacks the project file never mentions.
			if (files.RazorViews.Count > 0)
				AddRazor ("Razor views on disk", files.RazorViews [0]);

			if (files.ServiceFiles.Count > 0)
				Add ("AspNetCore.Web.ServiceModel", ".svc endpoints on disk", files.ServiceFiles [0]);

			if (files.BundleConfig != null)
				Add ("AspNetCore.Web.Optimization", "App_Start/BundleConfig", files.BundleConfig);

			if (files.ViewsWebConfig != null) {
				Add ("AspNetCore.Web.Mvc", "Views/web.config names an MVC host factory", files.ViewsWebConfig);
				AddRazor ("MVC views are Razor", files.ViewsWebConfig);
			}

			// Dynamic Data leaves its scaffolding pages on disk even when the project file does not
			// mention the assembly, which is common once someone has edited the templates by hand.
			if (files.DynamicDataPages != null)
				Add ("AspNetCore.Web.DynamicData", "Dynamic Data scaffolding on disk", files.DynamicDataPages);

			if (webConfig.OutOfProcessSessionMode != null)
				Add ("AspNetCore.Web.SessionState",
				     "sessionState mode=\"" + webConfig.OutOfProcessSessionMode + "\"",
				     webConfig.Evidence);

			return detections;
		}
	}

	/// <summary>What is actually on disk under the application directory.</summary>
	sealed class ApplicationFiles
	{
		ApplicationFiles ()
		{
		}

		public IReadOnlyList<string> RazorViews { get; private set; } = Array.Empty<string> ();

		public IReadOnlyList<string> ServiceFiles { get; private set; } = Array.Empty<string> ();

		public IReadOnlyList<string> MarkupFiles { get; private set; } = Array.Empty<string> ();

		public IReadOnlyList<string> RemotingEndpoints { get; private set; } = Array.Empty<string> ();

		public string BundleConfig { get; private set; }

		public string ViewsWebConfig { get; private set; }

		public string GlobalAsax { get; private set; }

		/// <summary>
		/// EVERY web.config under the application, not just the root one - relative paths, in
		/// depth order. A ported MVC application keeps a Views/web.config naming the Razor host
		/// factory, and that file names System.Web.Mvc just as the root one does; rewriting only the
		/// root leaves every view failing to compile with a type it cannot find.
		/// </summary>
		public IReadOnlyList<string> WebConfigs { get; private set; } = Array.Empty<string> ();

		public bool HasProgramFile { get; private set; }

		/// <summary>
		/// A hand-written AssemblyInfo, relative path, or null. Its presence changes the generated
		/// project: an SDK-style project synthesises assembly attributes, and the two sets collide.
		/// </summary>
		public string AssemblyInfo { get; private set; }

		/// <summary>A file under a DynamicData/ folder, or null. The scaffolding's on-disk signature.</summary>
		public string DynamicDataPages { get; private set; }

		public static ApplicationFiles Scan (string directory)
		{
			var files = new ApplicationFiles ();

			// bin/ and obj/ hold build output, including a published copy of the site. Counting those
			// would double every detection and, worse, make an application look like it has views it
			// does not have.
			var all = SafeEnumerate (directory)
				.Where (p => !IsUnderOutputDirectory (directory, p))
				.ToArray ();

			string Relative (string p) => Path.GetRelativePath (directory, p).Replace ('\\', '/');

			files.RazorViews = all.Where (p => Has (p, ".cshtml") || Has (p, ".vbhtml"))
					       .Select (Relative).ToArray ();
			files.ServiceFiles = all.Where (p => Has (p, ".svc")).Select (Relative).ToArray ();
			files.MarkupFiles = all.Where (p => Has (p, ".aspx") || Has (p, ".ascx") || Has (p, ".master") ||
							    Has (p, ".ashx") || Has (p, ".asmx"))
					        .Select (Relative).ToArray ();
			files.RemotingEndpoints = all.Where (p => Has (p, ".rem") || Has (p, ".soap"))
						     .Select (Relative).ToArray ();

			files.BundleConfig = all.Where (p => Path.GetFileName (p).Equals ("BundleConfig.cs", StringComparison.OrdinalIgnoreCase) ||
							     Path.GetFileName (p).Equals ("BundleConfig.vb", StringComparison.OrdinalIgnoreCase))
						.Select (Relative).FirstOrDefault ();

			files.ViewsWebConfig = all.Where (p => Path.GetFileName (p).Equals ("web.config", StringComparison.OrdinalIgnoreCase) &&
							       Path.GetFileName (Path.GetDirectoryName (p)).Equals ("Views", StringComparison.OrdinalIgnoreCase))
						  .Select (Relative).FirstOrDefault ();

			files.GlobalAsax = all.Where (p => Path.GetFileName (p).Equals ("Global.asax", StringComparison.OrdinalIgnoreCase))
					      .Select (Relative).FirstOrDefault ();

			// The folder the Dynamic Data project template creates, and the surest on-disk sign of it.
			files.DynamicDataPages = all.Where (p => Path.GetFileName (Path.GetDirectoryName (p))
								   .Equals ("DynamicData", StringComparison.OrdinalIgnoreCase) ||
								 Relative (p).StartsWith ("DynamicData/", StringComparison.OrdinalIgnoreCase))
						    .Select (Relative).FirstOrDefault ();

			files.WebConfigs = all.Where (p => Path.GetFileName (p).Equals ("web.config", StringComparison.OrdinalIgnoreCase))
					      .Select (Relative)
					      .OrderBy (p => p.Count (c => c == '/'))
					      .ThenBy (p => p, StringComparer.OrdinalIgnoreCase)
					      .ToArray ();

			files.AssemblyInfo = all.Where (p => Path.GetFileName (p).Equals ("AssemblyInfo.cs", StringComparison.OrdinalIgnoreCase) ||
							     Path.GetFileName (p).Equals ("AssemblyInfo.vb", StringComparison.OrdinalIgnoreCase))
						.Select (Relative).FirstOrDefault ();

			files.HasProgramFile = all.Any (p => Path.GetFileName (p).Equals ("Program.cs", StringComparison.OrdinalIgnoreCase) ||
							     Path.GetFileName (p).Equals ("Program.vb", StringComparison.OrdinalIgnoreCase));

			return files;
		}

		static bool Has (string path, string extension)
		{
			return path.EndsWith (extension, StringComparison.OrdinalIgnoreCase);
		}

		static bool IsUnderOutputDirectory (string root, string path)
		{
			string relative = Path.GetRelativePath (root, path);
			return relative.StartsWith ("bin" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
			       relative.StartsWith ("obj" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
			       relative.StartsWith ("packages" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
		}

		static IEnumerable<string> SafeEnumerate (string directory)
		{
			try {
				return Directory.EnumerateFiles (directory, "*", SearchOption.AllDirectories);
			} catch (Exception) {
				return Array.Empty<string> ();
			}
		}
	}
}
