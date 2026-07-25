//
// The expensive tests: convert a fixture and then really build the result.
//
// This is the only thing that proves the generated project file is VALID rather than merely
// well-formed. A project can satisfy every structural assertion in ConversionTests and still fail to
// restore, or build into an empty output directory.
//
// They need the port's packages, and PORT-TOOL-PLAN.md 0.1 established (by measurement) that a project
// outside this repository cannot restore them: the repo's NuGet.Config declares its sources as RELATIVE
// paths, which mean nothing from a temp directory. Each test therefore writes a NuGet.config naming the
// folder absolutely - option (a) from the plan.
//
// The source is Packages/, the shared staging feed every repository under d:\CommonLibrary links - see
// NuGet.Config. It is both where this port publishes and where sibling repositories restore from, which
// is what lets a package be consumed before it is pushed to nuget.org. It also means the directory
// holds other products' packages and accumulates ids nothing produces any more, so the copy below takes
// only what this solution currently builds.
//
// Filter them out while iterating with:  dotnet test --filter Category!=Build
//

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using PortProject;
using Xunit;

namespace WebFormsPort.PortToolTests
{
	[Trait ("Category", "Build")]
	public class GeneratedProjectBuildsTests
	{
		/// <summary>
		/// The version the packages in Packages/ actually carry. Directory.Build.props composes
		/// "$(VersionPrefix).$(VersionSuffix)" = 1.0.0.0, which NuGet normalises to 1.0.0 - so this is
		/// the string that resolves, and it is worth having in one place when it changes.
		/// </summary>
		const string PortPackageVersion = "1.0.0";

		/// <summary>
		/// Gives the fixture its OWN copy of the feed and its OWN package cache.
		/// </summary>
		/// <remarks>
		/// Both halves are needed, and both were learned the hard way from running this suite as part of
		/// `dotnet test AspNetCore.Web.slnx`:
		///
		/// The private FEED, because that solution run rebuilds the port and rewrites Packages/ while
		/// these tests are reading it - so a package can vanish mid-run.
		///
		/// The private CACHE (RestorePackagesPath), because the port repacks the *same version number*
		/// on every build. NuGet caches by id+version, so a rewritten 1.0.0 races with a restore that is
		/// extracting it, and the result is a half-populated cache entry and
		/// "CS0006: Metadata file ...\Core.Web.dll could not be found" - a failure that looks like a bug
		/// in the generated project and is not.
		/// </remarks>
		static void IsolateFeed (FixtureCopy fixture, ConversionPlan plan)
		{
			string source = Path.Combine (FixtureCopy.RepositoryRoot, "Packages");
			string feed = fixture.Path ("local-feed");
			Directory.CreateDirectory (feed);

			// Every package this repository CURRENTLY produces, and nothing else.
			//
			// Not "everything in Packages/", which is what this used to copy. That folder is a shared
			// output directory for the whole CommonLibrary tree: it accumulates, nothing prunes it, and it
			// holds artefacts of products this solution knows nothing about. Copying it wholesale means a
			// package id that NO project produces any more still restores - so the tool can emit a
			// reference to a package that no longer exists and this suite passes anyway, against a
			// months-old file. That is not a hypothetical: it is the failure mode this isolation exists to
			// prevent, and copying indiscriminately reintroduced it one directory further in.
			//
			// Restricting the copy to ProducedPackageIds turns a stale reference into NU1101 here, on the
			// machine of whoever introduced it, instead of on a clean checkout months later.
			//
			// Still ALL of them rather than just the plan's direct references: the hosting package depends
			// on AspNetCore.Web.Base and friends, and a feed with holes is worse than no feed.
			var wanted = ProducedPackageIds ();
			var copied = new HashSet<string> (StringComparer.OrdinalIgnoreCase);

			// Retried, because Packages/ is not stable while this runs. Under
			// `dotnet test AspNetCore.Web.slnx` the projects still packing delete each .nupkg and write it
			// again, so an id can be absent for a moment through no fault of anyone's. Retrying makes the
			// difference between "not built yet" and "being rebuilt right now", which a single pass cannot
			// tell apart - and getting that wrong turns a green suite red at random.
			// 60s, not 10s. AspNetCore.Web.Base wraps Core.Web - by far the largest assembly here - and
			// packing it takes longer than the rest put together, so it is reliably the one still missing
			// when a shorter window expires. Waiting only happens when something is genuinely absent, so
			// the ceiling costs nothing on a build that has already finished.
			for (int attempt = 0; attempt < 120 && copied.Count < wanted.Count; attempt++) {
				if (attempt > 0)
					System.Threading.Thread.Sleep (500);

				foreach (string nupkg in Directory.GetFiles (source, "*.nupkg")) {
					string id = PackageIdOf (Path.GetFileNameWithoutExtension (nupkg));
					if (!wanted.Contains (id) || copied.Contains (id))
						continue;

					try {
						File.Copy (nupkg, Path.Combine (feed, Path.GetFileName (nupkg)), overwrite: true);
						copied.Add (id);
					} catch (IOException) {
						// Being rewritten as we read it. The next attempt gets it.
					}
				}
			}

			Assert.True (copied.Count >= wanted.Count,
				     "Packages/ holds " + copied.Count + " of the " + wanted.Count + " packages this " +
				     "solution produces, after 60s of retries. Missing: " +
				     String.Join (", ", wanted.Except (copied)) +
				     ". Run `dotnet build AspNetCore.Web.slnx` to pack them before running this suite.");

			// <clear /> so a machine-level NuGet.config cannot pull a DIFFERENT AspNetCore.Web.* from
			// somewhere else and make the result depend on who ran it.
			File.WriteAllText (fixture.Path ("NuGet.config"),
				"<?xml version=\"1.0\" encoding=\"utf-8\"?>" + Environment.NewLine +
				"<configuration>" + Environment.NewLine +
				"  <packageSources>" + Environment.NewLine +
				"    <clear />" + Environment.NewLine +
				"    <add key=\"port\" value=\"" + feed + "\" />" + Environment.NewLine +
				"    <add key=\"nuget.org\" value=\"https://api.nuget.org/v3/index.json\" protocolVersion=\"3\" />" + Environment.NewLine +
				"  </packageSources>" + Environment.NewLine +
				"  <config>" + Environment.NewLine +
				"    <add key=\"globalPackagesFolder\" value=\"" + fixture.PackageCache + "\" />" + Environment.NewLine +
				"  </config>" + Environment.NewLine +
				"</configuration>" + Environment.NewLine);
		}

		/// <summary>
		/// The package ids this solution produces, read from the projects rather than listed here.
		/// </summary>
		/// <remarks>
		/// A hand-maintained list would be wrong the first time a package is added or renamed, and wrong
		/// silently - the suite would keep passing against whatever was already on the feed, which is the
		/// exact failure this is meant to close. So the ids are derived: every packable project declares
		/// <c>&lt;PackageId&gt;AspNet$(AssemblyName)&lt;/PackageId&gt;</c>, optionally with a
		/// <c>.Base</c> suffix, and <c>AssemblyName</c> is right there in the same file.
		/// </remarks>
		static HashSet<string> ProducedPackageIds ()
		{
			var ids = new HashSet<string> (StringComparer.OrdinalIgnoreCase);

			foreach (string project in Directory.GetFiles (FixtureCopy.RepositoryRoot, "*.csproj",
								      SearchOption.AllDirectories)) {
				// Only the top-level project directories: Mono/ is upstream, and bin/obj hold copies.
				string relative = Path.GetRelativePath (FixtureCopy.RepositoryRoot, project);
				if (relative.Contains ("Mono" + Path.DirectorySeparatorChar) ||
				    relative.Contains ("obj" + Path.DirectorySeparatorChar) ||
				    relative.Contains ("bin" + Path.DirectorySeparatorChar))
					continue;

				string text = File.ReadAllText (project);

				Match id = Regex.Match (text, @"<PackageId>\s*(?<id>[^<]+?)\s*</PackageId>");
				if (!id.Success)
					continue;

				Match assembly = Regex.Match (text, @"<AssemblyName>\s*(?<name>[^<]+?)\s*</AssemblyName>");
				if (!assembly.Success)
					continue;

				ids.Add (id.Groups ["id"].Value
					   .Replace ("AspNet$(AssemblyName)", "AspNet" + assembly.Groups ["name"].Value));
			}

			Assert.True (ids.Count > 0,
				     "No packable projects found under " + FixtureCopy.RepositoryRoot +
				     " - the id derivation has broken, not the feed.");
			return ids;
		}

		/// <summary>Strips the version off a nupkg file name: "AspNetCore.Web.Mvc.1.0.0" -> the id.</summary>
		static string PackageIdOf (string fileName)
		{
			// The first segment that starts with a digit begins the version. Splitting on the last three
			// dots would be wrong for a four-part version, and a regex on the whole name would be wrong
			// for an id that contains a digit.
			Match version = Regex.Match (fileName, @"\.(?=\d)");
			return version.Success ? fileName.Substring (0, version.Index) : fileName;
		}

		static (int ExitCode, string Output) Build (string directory, string project)
		{
			var start = new ProcessStartInfo ("dotnet") {
				WorkingDirectory = directory,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				UseShellExecute = false,
			};

			start.ArgumentList.Add ("build");
			start.ArgumentList.Add (project);
			start.ArgumentList.Add ("--nologo");
			start.ArgumentList.Add ("-v");
			start.ArgumentList.Add ("quiet");

			// The parent's MSBuild environment must not reach this child.
			//
			// When the suite runs on its own these are unset and nothing happens. When it runs as part of
			// `dotnet test AspNetCore.Web.slnx` the test host has inherited a full MSBuild environment
			// from the outer build, and the child picks it up: MSBUILD_EXE_PATH and MSBuildSDKsPath then
			// point it at the OUTER build's SDK resolution, and every build test fails while passing
			// perfectly when run alone - which reads as flakiness rather than as inheritance.
			//
			// MSBuildProjectExtensionsPath is cleared for a different reason: it would otherwise point the
			// converted fixture at obj/ inside this repository, where Directory.Build.props signs
			// assemblies and imports the port's conventions. A converted application has to build the way
			// a customer's would.
			foreach (string variable in new [] {
				"MSBUILD_EXE_PATH",
				"MSBuildSDKsPath",
				"MSBuildExtensionsPath",
				"MSBuildExtensionsPath32",
				"MSBuildExtensionsPath64",
				"MSBuildLoadMicrosoftTargetsReadOnly",
				"MSBuildProjectExtensionsPath",
				"MSBuildStartupDirectory",
				"DOTNET_HOST_PATH",
				"VSINSTALLDIR",
				"VisualStudioVersion",
			})
				start.Environment.Remove (variable);

			// Both pipes are drained CONCURRENTLY, and this is not a style preference.
			//
			// Reading one stream to completion and then the other deadlocks whenever the child fills the
			// pipe the parent is not reading: the child blocks on a full stderr buffer, the parent blocks
			// on a stdout read that will never complete, and neither moves until the 300s timeout. That is
			// exactly the shape of the intermittent failures this suite showed under
			// `dotnet test AspNetCore.Web.slnx` - two to four of the six build tests failing, a different
			// two to four each run, all six passing when the suite runs alone. Running alone, `dotnet
			// build` says almost nothing and fits in the buffer; under fifteen parallel test assemblies it
			// emits restore chatter and NuGet warnings and does not.
			//
			// It read as flakiness, which is why it survived: the previous fix in this method (the MSBuild
			// environment scrub above) made the failures rarer without making them stop, and rarer looks
			// like fixed.
			var stdout = new StringBuilder ();
			var stderr = new StringBuilder ();

			using Process process = Process.Start (start);

			process.OutputDataReceived += (_, e) => { if (e.Data != null) stdout.AppendLine (e.Data); };
			process.ErrorDataReceived += (_, e) => { if (e.Data != null) stderr.AppendLine (e.Data); };
			process.BeginOutputReadLine ();
			process.BeginErrorReadLine ();

			if (!process.WaitForExit (milliseconds: 300000)) {
				// ExitCode throws on a process that has not exited, so killing it first is what makes the
				// failure a readable assertion instead of an InvalidOperationException from the harness.
				try { process.Kill (entireProcessTree: true); } catch { }
				return (-1, "TIMED OUT after 300s.\n" + stdout + stderr);
			}

			// The parameterless overload after the timed one: it also waits for the async readers to
			// finish, which the timed overload does not, so without it the tail of the output can be
			// missing from the assertion message.
			process.WaitForExit ();

			return (process.ExitCode, stdout.ToString () + stderr.ToString ());
		}

		/// <summary>
		/// Every package the plan is about to reference has to be on the local feed, or the build fails
		/// with a bare NU1101 that reads like a bug in the tool. It is not: it means the solution has
		/// not been packed. Checking up front turns an environment problem into an actionable message.
		/// </summary>
		static void RequirePackagesOnFeed (FixtureCopy fixture, ConversionPlan plan)
		{
			// The PRIVATE feed, not the repository's Packages/ - and the distinction is not pedantic.
			// Packages/ is a shared output directory that the rest of the solution is still rewriting
			// while this suite runs, so a package can be absent from it for a fraction of a second and
			// present in the copy that will actually serve the restore. Checking the shared folder made
			// this suite fail at random, in a way that looked like the tool emitting a bad reference.
			string feed = fixture.Path ("local-feed");

			var missing = plan.Findings
				.Where (f => f.Severity == Severity.Info && f.Title.StartsWith ("Package ", StringComparison.Ordinal))
				.Select (f => f.Title.Substring ("Package ".Length))
				.Where (id => !File.Exists (Path.Combine (feed, id + "." + PortPackageVersion + ".nupkg")))
				.ToArray ();

			Assert.True (missing.Length == 0,
				     "These packages are not on the private feed at " + feed + ":" + Environment.NewLine +
				     "  " + String.Join (Environment.NewLine + "  ", missing) + Environment.NewLine +
				     "Build the solution first so they are packed:  dotnet build AspNetCore.Web.slnx" +
				     Environment.NewLine +
				     "(If one still does not appear, its project may be writing to a project-local " +
				     "Packages/ because $(SolutionDir) was empty - pass -p:SolutionDir=<repo>\\ .)");
		}

		static FixtureCopy Convert (string fixtureName, string projectFile)
		{
			FixtureCopy fixture = FixtureCopy.Of (fixtureName);

			try {
				ConversionPlan plan = Planner.Plan (fixture.Path (projectFile), new PlannerOptions {
					PackageVersion = PortPackageVersion,
				});

				Assert.False (plan.HasBlockers, "the fixture was blocked: " + PlanPrinter.Summary (plan, false));

				IsolateFeed (fixture, plan);
				RequirePackagesOnFeed (fixture, plan);
				PlanExecutor.Execute (plan);
				return fixture;
			} catch {
				fixture.Dispose ();
				throw;
			}
		}

		[Fact]
		public void A_converted_webforms_project_builds ()
		{
			using FixtureCopy fixture = Convert ("WebFormsCSharp", "WebFormsCSharp.csproj");

			(int exitCode, string output) = Build (fixture.Directory, "WebFormsCSharp.csproj");

			Assert.True (exitCode == 0, "build failed:" + Environment.NewLine + output);
		}

		[Fact]
		public void A_converted_webforms_project_puts_the_port_and_the_markup_in_its_output ()
		{
			// Two separate claims, and the second is the one a structural assertion cannot make:
			// Core.Web.dll present means the facade removal ran, and Default.aspx present means the
			// package's content globs were imported. Neither is visible in the project file.
			using FixtureCopy fixture = Convert ("WebFormsCSharp", "WebFormsCSharp.csproj");

			(int exitCode, string output) = Build (fixture.Directory, "WebFormsCSharp.csproj");
			Assert.True (exitCode == 0, "build failed:" + Environment.NewLine + output);

			string outputDirectory = fixture.Path ("bin/Debug/net10.0");

			Assert.True (File.Exists (Path.Combine (outputDirectory, "Core.Web.dll")),
				     "Core.Web.dll did not reach the output directory - the package's build assets were not imported");
			Assert.True (File.Exists (Path.Combine (outputDirectory, "Default.aspx")),
				     "Default.aspx was not copied - the content globs were not applied");
			Assert.True (File.Exists (Path.Combine (outputDirectory, "Web.config")),
				     "Web.config was not copied");
			Assert.True (File.Exists (Path.Combine (outputDirectory, "App_Code", "Helper.cs")),
				     "App_Code was not treated as content");
		}

		[Fact]
		public void App_Code_is_not_compiled_into_the_assembly ()
		{
			// If the SDK compiled App_Code as well, every type in it would exist twice once
			// BuildManager compiled it at runtime - and the failure would be a page-compile error
			// naming a type that looks perfectly fine.
			using FixtureCopy fixture = Convert ("WebFormsCSharp", "WebFormsCSharp.csproj");

			(int exitCode, string output) = Build (fixture.Directory, "WebFormsCSharp.csproj");
			Assert.True (exitCode == 0, "build failed:" + Environment.NewLine + output);

			// Helper is only in App_Code, so its presence in the assembly would prove it was compiled.
			string assembly = Path.Combine (fixture.Path ("bin/Debug/net10.0"), "LegacyWebForms.dll");
			Assert.True (File.Exists (assembly));

			byte [] bytes = File.ReadAllBytes (assembly);
			Assert.DoesNotContain ("LegacyWebForms.Helper", Encoding.UTF8.GetString (bytes), StringComparison.Ordinal);
		}

		[Fact]
		public void A_converted_mvc_project_builds ()
		{
			// The controllers here compile against System.Web.Mvc, which now comes from the port -
			// so this also proves the package detection picked the right ones.
			using FixtureCopy fixture = Convert ("MvcWithBundling", "MvcWithBundling.csproj");

			(int exitCode, string output) = Build (fixture.Directory, "MvcWithBundling.csproj");

			Assert.True (exitCode == 0, "build failed:" + Environment.NewLine + output);
		}

		[Fact]
		public void A_converted_mvc_project_copies_its_views_and_bundle_inputs ()
		{
			using FixtureCopy fixture = Convert ("MvcWithBundling", "MvcWithBundling.csproj");

			(int exitCode, string output) = Build (fixture.Directory, "MvcWithBundling.csproj");
			Assert.True (exitCode == 0, "build failed:" + Environment.NewLine + output);

			string outputDirectory = fixture.Path ("bin/Debug/net10.0");

			Assert.True (File.Exists (Path.Combine (outputDirectory, "Views", "Home", "Index.cshtml")),
				     ".cshtml was not copied as content");
			Assert.True (File.Exists (Path.Combine (outputDirectory, "Views", "Web.config")),
				     "Views/Web.config was not copied");
			Assert.True (File.Exists (Path.Combine (outputDirectory, "Scripts", "site.js")),
				     "bundle input was not copied");
			Assert.True (File.Exists (Path.Combine (outputDirectory, "Global.asax")),
				     "Global.asax was not copied");
		}

		[Fact]
		public void A_converted_vb_project_builds_as_a_library ()
		{
			// No host is generated for VB, so the project must stay a Library. Microsoft.NET.Sdk.Web
			// defaults OutputType to Exe, and an Exe with no Main does not build at all - which would
			// leave the user worse off than before they ran the tool.
			using FixtureCopy fixture = Convert ("WebFormsVisualBasic", "WebFormsVisualBasic.vbproj");

			Assert.Contains ("<OutputType>Library</OutputType>", fixture.Read ("WebFormsVisualBasic.vbproj"));

			(int exitCode, string output) = Build (fixture.Directory, "WebFormsVisualBasic.vbproj");

			Assert.True (exitCode == 0, "build failed:" + Environment.NewLine + output);
		}

		[Fact]
		public void A_converted_dynamic_data_project_builds ()
		{
			// The newest stack the tool learned to detect, and therefore the one whose package reference
			// has been proved least. A plan assertion can only say that "AspNetCore.Web.DynamicData"
			// appears in the generated project file; only a real restore says the id exists, the version
			// resolves, and the pages compile against what it contains.
			using FixtureCopy fixture = Convert ("DynamicDataApp", "DynamicDataApp.csproj");

			(int exitCode, string output) = Build (fixture.Directory, "DynamicDataApp.csproj");

			Assert.True (exitCode == 0, "build failed:" + Environment.NewLine + output);
		}

		[Fact]
		public void A_converted_wcf_and_session_project_builds_after_the_one_edit_the_tool_asks_for ()
		{
			// Two stacks in one fixture, which is the point: .svc hosting brings CoreWCF in and
			// out-of-process session brings Microsoft.Data.SqlClient, so this is where a bad transitive
			// dependency or a conflicting version would surface. The generated Program.cs also has to
			// compile - it calls AddSvcEndpoints, UseSvcEndpoints and UseWebFormsSessionState, and a
			// wrong using or a wrong overload is invisible to every plan-level assertion.
			//
			// This one does NOT build straight out of the tool, and that is correct rather than a defect:
			// port-project converts the project file and writes a host, it does not rewrite your source,
			// and a WCF contract has to move from System.ServiceModel to CoreWCF. The tool says so, as a
			// NeedsAttention finding.
			//
			// So the assertion is stronger than "it builds": it is that the tool's instruction is
			// SUFFICIENT. Apply exactly the edit the finding describes - nothing else - and the project
			// must then compile. An instruction that leaves a second error behind is a worse failure than
			// no instruction, because the user has already done what they were told.
			using FixtureCopy fixture = Convert ("WcfAndSessionState", "WcfAndSessionState.csproj");

			(int before, string firstOutput) = Build (fixture.Directory, "WcfAndSessionState.csproj");
			Assert.True (before != 0,
				     "The fixture compiled without moving its contracts to CoreWCF. If the tool now " +
				     "rewrites source, this test should assert that instead:" + Environment.NewLine +
				     firstOutput);

			string service = fixture.Path ("Services/EchoService.cs");
			File.WriteAllText (service,
				File.ReadAllText (service).Replace ("using System.ServiceModel;", "using CoreWCF;"));

			(int exitCode, string output) = Build (fixture.Directory, "WcfAndSessionState.csproj");

			Assert.True (exitCode == 0,
				     "build failed AFTER applying the only edit the tool asked for:" +
				     Environment.NewLine + output);
		}

		[Fact]
		public void The_tool_names_the_contract_change_a_wcf_project_needs ()
		{
			// The other half of the test above. The edit is only reasonable to require if the tool tells
			// you about it, in terms specific enough to act on.
			using FixtureCopy fixture = FixtureCopy.Of ("WcfAndSessionState");
			ConversionPlan plan = Planner.Plan (fixture.Path ("WcfAndSessionState.csproj"),
							    new PlannerOptions { PackageVersion = PortPackageVersion });

			Finding contracts = plan.Findings.First (f => f.Detail.Contains ("CoreWCF"));

			Assert.Equal (Severity.NeedsAttention, contracts.Severity);
			Assert.Contains ("System.ServiceModel", contracts.Detail);
		}
	}
}
