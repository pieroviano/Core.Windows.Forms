//
// The planner: everything the tool decides, and nothing it does.
//
// Plan () performs no writes. It reads the legacy project and the directory around it, and returns a
// ConversionPlan describing every action and every finding. PlanPrinter and PlanExecutor then consume
// the same object, which is what makes dry-run and --apply structurally incapable of disagreeing.
//
// The findings are as much the product as the files are. Every inference gets one - not only the
// failures - because the whole justification for automating a best-effort conversion is that the user
// can see what was assumed.
//

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PortProject
{
	sealed class PlannerOptions
	{
		public string PackageVersion { get; set; } = "1.0.0";

		public bool GenerateProgram { get; set; } = true;

		public bool RewriteWebConfig { get; set; } = true;

		public bool Force { get; set; }
	}

	static class Planner
	{
		/// <summary>
		/// NuGet packages the port supersedes. Anything on this list is dropped (with a finding);
		/// anything NOT on it is carried over (with a finding). Nothing is ever dropped silently -
		/// losing a dependency without saying so is the worst failure this tool could have.
		/// </summary>
		static readonly HashSet<string> SupersededPackages = new HashSet<string> (StringComparer.OrdinalIgnoreCase) {
			"Microsoft.AspNet.Mvc",
			"Microsoft.AspNet.Razor",
			"Microsoft.AspNet.WebPages",
			"Microsoft.AspNet.Web.Optimization",
			"Microsoft.AspNet.WebApi",
			"Microsoft.AspNet.WebApi.Core",
			"Microsoft.AspNet.WebApi.Client",
			"Microsoft.AspNet.WebApi.WebHost",
			"Microsoft.Web.Infrastructure",
			"WebGrease",
			"Antlr",
			"Microsoft.CodeDom.Providers.DotNetCompilerPlatform",
			"Microsoft.Net.Compilers",
		};

		/// <summary>
		/// References with no path forward at all. Each one is a Blocker - the run writes NOTHING and
		/// exits 2, in both modes.
		/// </summary>
		/// <remarks>
		/// Keep this list honest as the port grows. A blocker is not advice: it refuses to convert an
		/// application at all, so a stale entry tells someone to rewrite code that would have worked.
		/// System.Web.DynamicData and System.Data.Linq were both on this list after the port started
		/// supporting them - see BlockedButNowSupported, which turns exactly that mistake into a
		/// finding that says so.
		/// </remarks>
		static readonly Dictionary<string, string> BlockingReferences = new Dictionary<string, string> (StringComparer.OrdinalIgnoreCase) {
			["System.Runtime.Remoting"] = "Remoting's transparent proxies are a CLR feature CoreCLR does not have. Re-expose the surface as Web API.",
			["System.Web.Mobile"] = "Mobile controls are not ported, and will not be - Mono's assembly is a stub, so there is nothing to port and the technology was deprecated in 2005. Rewrite those pages as ordinary .aspx.",
			["System.Web.Entity"] = "The EntityDataSource control is not ported. Rewrite those pages, or use LinqDataSource against an EF Core context.",
		};

		/// <summary>
		/// References that USED to be blockers and are now supported, with the caveat that comes with
		/// them. Reported as NeedsAttention: the conversion proceeds, and the user is told what changed
		/// under their feet.
		/// </summary>
		static readonly Dictionary<string, string> BlockedButNowSupported = new Dictionary<string, string> (StringComparer.OrdinalIgnoreCase) {
			["System.Data.Linq"] = "LINQ to SQL itself does not exist on .NET, but LinqDataSource and Dynamic Data now run on IQueryable instead - point ContextTypeName at an EF Core DbContext, a repository, or anything with queryable members. A DataContext subclass will not work; the entity classes usually will.",
			["System.Web.DynamicData"] = "Dynamic Data works, through AspNetCore.Web.DynamicData. Its model comes from QueryableDataModelProvider rather than LINQ to SQL, so RegisterContext takes any context with IQueryable members.",
		};

		public static ConversionPlan Plan (string projectPath, PlannerOptions options)
		{
			projectPath = Path.GetFullPath (projectPath);
			string directory = Path.GetDirectoryName (projectPath);
			var plan = new ConversionPlan (projectPath, directory);

			LegacyProject project;
			try {
				project = LegacyProject.Load (projectPath);
			} catch (Exception e) {
				plan.Report (Severity.Blocker, "Could not read the project file", e.Message,
					     Path.GetFileName (projectPath));
				return plan;
			}

			if (project.IsSdkStyle) {
				plan.Report (Severity.Blocker, "Already an SDK-style project",
					     "This project has an Sdk attribute, so it has been converted already. Converting " +
					     "again would replace it with a generated file and discard any hand-tuning.",
					     project.FileName);
				return plan;
			}

			ApplicationFiles files = ApplicationFiles.Scan (directory);
			string webConfigPath = FindWebConfig (directory);
			WebConfigFacts webConfig = WebConfigFacts.Read (webConfigPath);

			if (!LooksLikeWebProject (project, files, webConfig)) {
				plan.Report (Severity.Blocker, "This does not look like a web project",
					     "Found no web.config, no .aspx/.ascx/.master/.cshtml markup and no web " +
					     "ProjectTypeGuid. port-project converts ASP.NET web applications; a class " +
					     "library needs no conversion beyond retargeting.",
					     project.FileName);
				return plan;
			}

			DetectBlockers (plan, project, files, webConfig);

			IReadOnlyList<Detection> detections = StackDetector.Detect (project, files, webConfig);
			foreach (Detection detection in detections)
				plan.Report (Severity.Info, "Package " + detection.Package, detection.Reason,
					     detection.Evidence);

			IReadOnlyList<PackageReference> carried = PlanPackages (plan, project);

			// The host is decided FIRST, because the project file's OutputType depends on whether
			// there will be a Main to run - and Microsoft.NET.Sdk.Web defaults that to Exe.
			bool hostGenerated = PlanProgram (plan, project, detections, files, options);

			PlanProjectFile (plan, project, detections, carried, options, hostGenerated, files);
			PlanWebConfigs (plan, files, webConfig, options);

			ReportManualWork (plan, project, files, webConfig, detections);

			return plan;
		}

		static void DetectBlockers (ConversionPlan plan, LegacyProject project, ApplicationFiles files,
					    WebConfigFacts webConfig)
		{
			foreach (Reference reference in project.References) {
				string why;
				if (BlockingReferences.TryGetValue (reference.Name, out why)) {
					plan.Report (Severity.Blocker, "References " + reference.Name, why,
						     project.FileName + ":" + reference.Line, "LIMITATIONS.md section 4");
					continue;
				}

				if (BlockedButNowSupported.TryGetValue (reference.Name, out why))
					plan.Report (Severity.NeedsAttention, "References " + reference.Name, why,
						     project.FileName + ":" + reference.Line, "LIMITATIONS.md section 2");
			}

			foreach (string endpoint in files.RemotingEndpoints)
				plan.Report (Severity.Blocker, ".NET Remoting endpoint " + endpoint,
					     "Remoting cannot be ported - transparent proxies are a CLR feature CoreCLR " +
					     "does not have. Re-expose this as Web API before converting.",
					     endpoint, "LIMITATIONS.md section 2");

			if (webConfig.HasRemotingSection)
				plan.Report (Severity.Blocker, "<system.runtime.remoting> in web.config",
					     "This application publishes remoting endpoints, which cannot be ported.",
					     webConfig.Evidence, "LIMITATIONS.md section 2");

			foreach (string svc in files.ServiceFiles) {
				string text;
				try {
					text = File.ReadAllText (Path.Combine (plan.ApplicationDirectory, svc));
				} catch (Exception) {
					continue;
				}

				if (text.IndexOf ("Factory=", StringComparison.OrdinalIgnoreCase) >= 0)
					plan.Report (Severity.Blocker, "Custom ServiceHostFactory in " + svc,
						     "CoreWCF builds the host itself, so a Factory= attribute has no " +
						     "equivalent. Remove it, or configure that service through CoreWCF directly.",
						     svc, "PORTING-GUIDE.md step 3");
			}
		}

		static IReadOnlyList<PackageReference> PlanPackages (ConversionPlan plan, LegacyProject project)
		{
			var carried = new List<PackageReference> ();

			foreach (PackageReference package in project.PackageReferences) {
				if (SupersededPackages.Contains (package.Id)) {
					plan.Report (Severity.Info, "Dropped " + package.Id,
						     "superseded by the port's own packages", package.Evidence);
					continue;
				}

				carried.Add (package);
				plan.Report (Severity.NeedsAttention, "Carried over " + package.Id,
					     "Kept at version " + (package.Version ?? "(unspecified)") +
					     ". Check it has a net10.0-compatible release - this tool cannot know.",
					     package.Evidence);
			}

			return carried;
		}

		static void PlanProjectFile (ConversionPlan plan, LegacyProject project,
					     IReadOnlyList<Detection> detections,
					     IReadOnlyList<PackageReference> carried, PlannerOptions options,
					     bool hostGenerated, ApplicationFiles files)
		{
			string backup = project.Path + ".old";

			if (File.Exists (backup) && !options.Force) {
				plan.Report (Severity.Blocker, "Backup already exists",
					     Path.GetFileName (backup) + " is already there, so this project looks " +
					     "converted already. Re-run with --force to overwrite the backup - but the " +
					     "backup is the only undo you have.",
					     Path.GetFileName (backup));
				return;
			}

			plan.Add (new FileAction (ActionKind.Rename, project.Path,
						  "keep the original as the undo", destination: backup));

			plan.Add (new FileAction (ActionKind.Write, project.Path,
						  "SDK-style project, per PORTING-GUIDE.md step 2",
						  ProjectFileWriter.Write (project, detections, carried,
									   options.PackageVersion, hostGenerated,
									   files.AssemblyInfo != null)));

			if (files.AssemblyInfo != null)
				plan.Report (Severity.Info, "Kept your AssemblyInfo",
					     "Set GenerateAssemblyInfo=false so " + files.AssemblyInfo + " stays the one " +
					     "source of the assembly attributes. Without it the build fails with CS0579, " +
					     "because an SDK-style project generates its own.",
					     files.AssemblyInfo);
		}

		/// <summary>Returns true when a host will be written - the project's OutputType depends on it.</summary>
		static bool PlanProgram (ConversionPlan plan, LegacyProject project,
					 IReadOnlyList<Detection> detections, ApplicationFiles files,
					 PlannerOptions options)
		{
			string programPath = Path.Combine (plan.ApplicationDirectory, "Program.cs");

			if (!options.GenerateProgram) {
				plan.Add (new FileAction (ActionKind.Skip, programPath, "--no-program was given"));
				return false;
			}

			if (project.Language == Language.VisualBasic) {
				// The generated host is C#. Emitting a VB one is possible but the sample
				// (Samples/WebFormsSampleVB) shows it needs StartupObject wiring the tool would have to
				// guess at, so this is reported rather than half-done.
				plan.Report (Severity.NeedsAttention, "No host generated for a VB project",
					     "Write Program.vb by hand and set <StartupObject>. See " +
					     "Samples/WebFormsSampleVB for a working one.",
					     project.FileName, "PORTING-GUIDE.md step 3");
				return false;
			}

			if (files.HasProgramFile) {
				plan.Add (new FileAction (ActionKind.Skip, programPath,
							  "a Program file already exists - not overwriting a host you wrote"));

				// It exists, so there IS a Main - the project stays an Exe.
				return true;
			}

			plan.Add (new FileAction (ActionKind.Write, programPath,
						  "Kestrel host, per PORTING-GUIDE.md step 3",
						  ProgramWriter.Write (project, detections,
								       project.AssemblyName, files)));
			return true;
		}

		/// <summary>
		/// EVERY web.config, not just the root one. Views/web.config names the MVC Razor host factory
		/// as "System.Web.Mvc, Version=5.2.7.0, ..."; leave it alone and the project builds, starts,
		/// and then fails to compile any view - PORTING-GUIDE.md step 4 calls this out explicitly.
		/// </summary>
		static void PlanWebConfigs (ConversionPlan plan, ApplicationFiles files, WebConfigFacts webConfig,
					    PlannerOptions options)
		{
			if (files.WebConfigs.Count == 0)
				return;

			if (!options.RewriteWebConfig) {
				foreach (string relative in files.WebConfigs)
					plan.Add (new FileAction (ActionKind.Skip,
								  Path.Combine (plan.ApplicationDirectory, relative),
								  "--no-web-config was given"));
				return;
			}

			bool anyChanged = false;

			foreach (string relative in files.WebConfigs) {
				string path = Path.Combine (plan.ApplicationDirectory, relative);

				string original;
				try {
					original = File.ReadAllText (path);
				} catch (Exception e) {
					plan.Report (Severity.NeedsAttention, "Could not read " + relative, e.Message, relative);
					continue;
				}

				WebConfigRewriter.Result rewrite = WebConfigRewriter.Rewrite (original);
				if (!rewrite.Changed)
					continue;

				anyChanged = true;

				plan.Add (new FileAction (ActionKind.Rename, path, "keep the original as the undo",
							  destination: path + ".old"));
				plan.Add (new FileAction (ActionKind.Write, path,
							  "assembly renames: " + String.Join ("; ", rewrite.Changes),
							  rewrite.Content));

				foreach (string change in rewrite.Changes)
					plan.Report (Severity.Info, relative + " rewrite", change, relative,
						     "PORTING-GUIDE.md step 4");
			}

			if (!anyChanged)
				plan.Report (Severity.Info, "No web.config needed an assembly rename",
					     "nothing matched the mechanical rules", webConfig.Evidence);
		}

		static void ReportManualWork (ConversionPlan plan, LegacyProject project, ApplicationFiles files,
					      WebConfigFacts webConfig, IReadOnlyList<Detection> detections)
		{
			// <system.webServer>: read, reported, never edited. There is no mechanical translation for
			// an IIS rewrite ruleset, and a half-translated one looks finished.
			foreach (string child in webConfig.SystemWebServerChildren)
				plan.Report (Severity.NeedsAttention, "<system.webServer><" + child + "> left untouched",
					     WebConfigRewriter.SystemWebServerAdvice (child), webConfig.Evidence,
					     "PORTING-GUIDE.md step 4");

			if (project.HasConditionedItems)
				plan.Report (Severity.NeedsAttention, "The project has conditioned item groups",
					     "port-project reads the project as written and does not evaluate MSBuild " +
					     "conditions, so a reference inside a Condition may have been missed. Check " +
					     "the .old file.",
					     project.FileName);

			if (detections.Any (d => d.Package == "AspNetCore.Web.ServiceModel")) {
				plan.Report (Severity.NeedsAttention, "WCF service code needs two using changes",
					     "Contracts move from System.ServiceModel to CoreWCF: change " +
					     "\"using System.ServiceModel;\" to \"using CoreWCF;\". The attributes keep " +
					     "their names and the SOAP on the wire does not change. port-project does not " +
					     "edit source files.",
					     files.ServiceFiles.FirstOrDefault (), "PORTING-GUIDE.md step 3");

				plan.Report (Severity.NeedsAttention, "<system.serviceModel> is not read",
					     "Bindings, behaviours and quotas configured there are ignored entirely - no " +
					     "error, they simply do nothing. Re-express them on SvcEndpointOptions.Binding.",
					     webConfig.Evidence, "PORTING-GUIDE.md step 3");
			}

			if (webConfig.OutOfProcessSessionMode != null) {
				plan.Report (Severity.NeedsAttention,
					     "sessionState mode=\"" + webConfig.OutOfProcessSessionMode + "\" changes meaning",
					     webConfig.OutOfProcessSessionMode == "StateServer"
						? "StateServer is an IDistributedCache here, not aspnet_state.exe, and " +
						  "stateConnectionString is ignored. Existing session data does not carry across."
						: "SQLServer uses the stock ASPState stored procedures. Existing rows contain " +
						  "BinaryFormatter payloads that .NET 9+ cannot read - plan the cutover as a " +
						  "session flush.",
					     webConfig.Evidence, "PORTING-GUIDE.md step 5");

				plan.Report (Severity.NeedsAttention, "Everything in Session must now serialize",
					     "mode=\"InProc\" never wrote a session down, so a type that has worked for " +
					     "years can fail on the first request. Consider " +
					     "options.StateSerializer = new JsonStateObjectSerializer ().",
					     webConfig.Evidence, "PORTING-GUIDE.md step 5");
			}

			if (files.GlobalAsax != null)
				plan.Report (Severity.Info, "Global.asax kept as-is",
					     "Route and bundle registration in Application_Start works unchanged.",
					     files.GlobalAsax);

			if (detections.Any (d => d.Package == "AspNetCore.Web.Mvc"))
				plan.Report (Severity.NeedsAttention, "MVC is version 4 here",
					     "Attribute routing, bundling and @await are supplied by the port; view " +
					     "components and tag helpers do not exist. A Views/web.config naming the MVC " +
					     "host factory is required - the assembly rename above covers it.",
					     files.ViewsWebConfig, "PORTING-GUIDE.md step 3");

			if (detections.Any (d => d.Package == "AspNetCore.Web.Optimization"))
				plan.Report (Severity.NeedsAttention, "Bundles are concatenated, not minified",
					     "WebGrease is .NET Framework only. Minify at build time and point the bundle " +
					     "at the output, or use CdnPath.",
					     files.BundleConfig, "LIMITATIONS.md section 2");

			plan.Report (Severity.NeedsAttention, "Check <compilation><assemblies> lists your assembly",
				     "MVC and Web API controllers are found by scanning that list. If controllers are " +
				     "not found at runtime, add <add assembly=\"" + project.AssemblyName + "\" /> and " +
				     "delete the compilation directory - the scan is cached to disk.",
				     webConfig.Evidence, "PORTING-GUIDE.md step 2");
		}

		static string FindWebConfig (string directory)
		{
			foreach (string name in new [] { "web.config", "Web.config", "Web.Config" }) {
				string candidate = Path.Combine (directory, name);
				if (File.Exists (candidate))
					return candidate;
			}

			// Case-insensitive on Windows, but the tool has to work on Linux too.
			return Directory.EnumerateFiles (directory, "*.config", SearchOption.TopDirectoryOnly)
				.FirstOrDefault (p => Path.GetFileName (p).Equals ("web.config", StringComparison.OrdinalIgnoreCase));
		}

		static bool LooksLikeWebProject (LegacyProject project, ApplicationFiles files, WebConfigFacts webConfig)
		{
			const string WebApplicationGuid = "{349c5851-65df-11da-9384-00065b846f21}";
			const string WebSiteGuid = "{E24C65DC-7377-472b-9ABA-BC803B73C61A}";

			if (webConfig.Exists || files.MarkupFiles.Count > 0 || files.RazorViews.Count > 0 ||
			    files.ServiceFiles.Count > 0)
				return true;

			return project.ProjectTypeGuids.Any (
				g => g.Equals (WebApplicationGuid, StringComparison.OrdinalIgnoreCase) ||
				     g.Equals (WebSiteGuid, StringComparison.OrdinalIgnoreCase));
		}
	}
}
