//
// Generating the new project file and the host.
//
// Both write text rather than building an XDocument: the output is a fixed shape that a human is going
// to read and hand-edit afterwards, and generated XML formatting is uniformly worse than a template.
//

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PortProject
{
	static class ProjectFileWriter
	{
		/// <summary>
		/// The shape from PORTING-GUIDE.md step 2. Deliberately NOT carried over from the legacy
		/// project: Compile, Content, None and Folder items. The SDK's globs and the hosting package's
		/// content globs replace all of them - re-emitting them is what produces NETSDK1022 duplicates.
		/// </summary>
		public static string Write (LegacyProject project, IReadOnlyList<Detection> detections,
					    IReadOnlyList<PackageReference> carriedPackages, string packageVersion,
					    bool hostGenerated, bool hasAssemblyInfo)
		{
			var text = new StringBuilder ();

			text.AppendLine ("<Project Sdk=\"Microsoft.NET.Sdk.Web\">");
			text.AppendLine ();
			text.AppendLine ("  <PropertyGroup>");
			text.AppendLine ("    <TargetFrameworks>net8.0;net10.0</TargetFrameworks>");
			text.AppendLine ("    <LangVersion>latest</LangVersion>");
			text.AppendLine ("    <Nullable>disable</Nullable>");

			// Always explicit, because Microsoft.NET.Sdk.Web defaults OutputType to Exe - and an Exe
			// with no Main does not build. When no host was generated (a VB project, or one that
			// already had a Program file skipped) the project stays the Library the legacy one was,
			// so it still compiles and the user adds the host when they are ready.
			text.AppendLine (hostGenerated
				? "    <OutputType>Exe</OutputType>"
				: "    <OutputType>Library</OutputType>");

			if (project.Language == Language.VisualBasic) {
				// Empty on purpose. VB prepends RootNamespace to every declaration, so the default
				// turns "Namespace MyApp" into MyApp.MyApp and Inherits="MyApp.DefaultPage" in the
				// .aspx stops resolving. PORTING-GUIDE.md step 2.
				text.AppendLine ("    <RootNamespace></RootNamespace>");
				text.AppendLine ("    <OptionStrict>Off</OptionStrict>");
				text.AppendLine ("    <OptionExplicit>On</OptionExplicit>");
				text.AppendLine ("    <OptionInfer>On</OptionInfer>");
			} else if (!String.IsNullOrEmpty (project.RootNamespace)) {
				text.AppendLine ("    <RootNamespace>" + Escape (project.RootNamespace) + "</RootNamespace>");
			}

			if (!String.IsNullOrEmpty (project.AssemblyName))
				text.AppendLine ("    <AssemblyName>" + Escape (project.AssemblyName) + "</AssemblyName>");

			// An SDK-style project SYNTHESISES [AssemblyTitle], [AssemblyVersion] and the rest, and a
			// legacy project brought its own Properties/AssemblyInfo.cs saying the same things. Leave
			// both and the build dies with CS0579 "Duplicate 'System.Reflection.AssemblyTitleAttribute'
			// attribute" - the single most common failure when converting a project of this age.
			//
			// Off rather than deleting their file: the existing AssemblyInfo is the one that carries
			// the company, copyright and version this application has always shipped with.
			if (hasAssemblyInfo)
				text.AppendLine ("    <GenerateAssemblyInfo>false</GenerateAssemblyInfo>");

			text.AppendLine ("  </PropertyGroup>");
			text.AppendLine ();

			text.AppendLine ("  <ItemGroup>");
			foreach (Detection detection in detections)
				text.AppendLine ("    <PackageReference Include=\"" + detection.Package +
						 "\" Version=\"" + packageVersion + "\" />");
			text.AppendLine ("  </ItemGroup>");

			if (carriedPackages.Count > 0) {
				text.AppendLine ();
				text.AppendLine ("  <ItemGroup>");
				text.AppendLine ("    <!-- Carried over from the original project. Versions are the ones it used;");
				text.AppendLine ("         check each still has a net10.0-compatible release. -->");
				foreach (PackageReference package in carriedPackages) {
					text.Append ("    <PackageReference Include=\"" + Escape (package.Id) + "\"");
					if (!String.IsNullOrEmpty (package.Version))
						text.Append (" Version=\"" + Escape (package.Version) + "\"");
					text.AppendLine (" />");
				}
				text.AppendLine ("  </ItemGroup>");
			}

			if (project.ProjectReferences.Count > 0) {
				text.AppendLine ();
				text.AppendLine ("  <ItemGroup>");
				foreach (string reference in project.ProjectReferences)
					text.AppendLine ("    <ProjectReference Include=\"" + Escape (reference) + "\" />");
				text.AppendLine ("  </ItemGroup>");
			}

			text.AppendLine ();
			text.AppendLine ("</Project>");

			return text.ToString ();
		}

		static string Escape (string value)
		{
			return value.Replace ("&", "&amp;").Replace ("<", "&lt;").Replace (">", "&gt;")
				    .Replace ("\"", "&quot;");
		}
	}

	static class ProgramWriter
	{
		/// <summary>
		/// The host from PORTING-GUIDE.md step 3, with the .svc and session-state variants composed in
		/// when those stacks were detected. Ordering is the part that matters and the part a person
		/// gets wrong, so the comments explaining it are generated too.
		/// </summary>
		public static string Write (LegacyProject project, IReadOnlyList<Detection> detections,
					    string siteName, ApplicationFiles files)
		{
			bool wcf = detections.Any (d => d.Package == "AspNetCore.Web.ServiceModel");
			bool sessionState = detections.Any (d => d.Package == "AspNetCore.Web.SessionState");

			var text = new StringBuilder ();

			text.AppendLine ("//");
			text.AppendLine ("// Generated by port-project from " + project.FileName + ".");
			text.AppendLine ("//");
			text.AppendLine ("// This replaces what IIS used to do. Middleware order is load-bearing: anything that");
			text.AppendLine ("// should be handled outside System.Web has to run BEFORE UseWebForms, which otherwise");
			text.AppendLine ("// takes every request and lets <httpHandlers> and the route table decide the outcome.");
			text.AppendLine ("//");
			text.AppendLine ();
			text.AppendLine ("using Microsoft.AspNetCore.Builder;");

			if (wcf || sessionState)
				text.AppendLine ("using Microsoft.Extensions.DependencyInjection;");

			text.AppendLine ("using System.Web.Hosting.Kestrel;");

			if (wcf)
				text.AppendLine ("using System.Web.ServiceModel;");
			if (sessionState)
				text.AppendLine ("using System.Web.SessionState;");

			text.AppendLine ();
			text.AppendLine ("var builder = WebApplication.CreateBuilder (args);");

			if (sessionState) {
				text.AppendLine ();
				text.AppendLine ("// mode=\"StateServer\" maps onto IDistributedCache here, NOT aspnet_state.exe. This is the");
				text.AppendLine ("// single-instance choice; swap in AddStackExchangeRedisCache to share sessions across");
				text.AppendLine ("// instances. Not needed for mode=\"SQLServer\".");
				text.AppendLine ("builder.Services.AddDistributedMemoryCache ();");
			}

			if (wcf) {
				text.AppendLine ();
				text.AppendLine ("// Before Build (): CoreWCF is configured through DI, so this has to run while the");
				text.AppendLine ("// service collection is still open.");
				text.AppendLine ("builder.Services.AddSvcEndpoints (builder.Environment.ContentRootPath);");
			}

			text.AppendLine ();
			text.AppendLine ("var app = builder.Build ();");
			text.AppendLine ();
			text.AppendLine ("// Static files first, so Kestrel serves .css/.js/images directly instead of routing every");
			text.AppendLine ("// one of them through the System.Web pipeline.");
			text.AppendLine ("app.UseStaticFiles ();");

			if (sessionState) {
				text.AppendLine ();
				text.AppendLine ("// BEFORE UseWebForms. The session store is built once, by the provider model, on the");
				text.AppendLine ("// first request that touches Session - this is the only chance to hand it the cache.");
				text.AppendLine ("app.UseWebFormsSessionState ();");
			}

			if (wcf) {
				text.AppendLine ();
				text.AppendLine ("// BEFORE UseWebForms. System.Web's handler mapping has no idea what a .svc file is and");
				text.AppendLine ("// would serve the directive as text or refuse it outright.");
				text.AppendLine ("app.UseSvcEndpoints ();");
			}

			text.AppendLine ();
			text.AppendLine ("app.UseWebForms (options => {");
			text.AppendLine ("\toptions.PhysicalPath = app.Environment.ContentRootPath;");
			text.AppendLine ("\toptions.VirtualPath = \"/\";");
			text.AppendLine ("\toptions.SiteName = \"" + siteName + "\";");
			text.AppendLine ("});");
			text.AppendLine ();
			text.AppendLine ("app.Run ();");

			return text.ToString ();
		}
	}
}
