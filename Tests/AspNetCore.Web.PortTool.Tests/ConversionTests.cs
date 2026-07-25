//
// Plan-level tests: what the tool decides, without building anything.
//

using System;
using System.IO;
using System.Linq;
using PortProject;
using Xunit;

namespace WebFormsPort.PortToolTests
{
	public class ConversionTests
	{
		static ConversionPlan PlanFor (FixtureCopy fixture, string projectFile, PlannerOptions options = null)
		{
			return Planner.Plan (fixture.Path (projectFile), options ?? new PlannerOptions ());
		}

		[Fact]
		public void The_old_project_is_renamed_and_the_new_one_takes_its_name ()
		{
			using var fixture = FixtureCopy.Of ("WebFormsCSharp");
			ConversionPlan plan = PlanFor (fixture, "WebFormsCSharp.csproj");

			PlanExecutor.Execute (plan);

			Assert.True (fixture.Exists ("WebFormsCSharp.csproj.old"), "the original was not kept");
			Assert.True (fixture.Exists ("WebFormsCSharp.csproj"), "no new project was written");

			// The backup has to be the ORIGINAL, not a second copy of the generated file.
			Assert.Contains ("ToolsVersion=\"4.0\"", fixture.Read ("WebFormsCSharp.csproj.old"));
			Assert.Contains ("Sdk=\"Microsoft.NET.Sdk.Web\"", fixture.Read ("WebFormsCSharp.csproj"));
		}

		[Fact]
		public void Planning_writes_nothing_at_all ()
		{
			// The dry-run guarantee, asserted rather than assumed: planning touches the filesystem for
			// reading only. Compared by name AND length, so a rewrite in place would be caught.
			using var fixture = FixtureCopy.Of ("MvcWithBundling");
			string [] before = fixture.Snapshot ();

			PlanFor (fixture, "MvcWithBundling.csproj");

			Assert.Equal (before, fixture.Snapshot ());
		}

		[Fact]
		public void Dry_run_and_apply_describe_the_same_changes ()
		{
			// The invariant that keeps --apply honest. Both modes consume one ConversionPlan, so a
			// preview that disagreed with the thing it previews would be a structural bug.
			using var preview = FixtureCopy.Of ("MvcWithBundling");
			using var applied = FixtureCopy.Of ("MvcWithBundling");

			ConversionPlan previewPlan = PlanFor (preview, "MvcWithBundling.csproj");
			ConversionPlan appliedPlan = PlanFor (applied, "MvcWithBundling.csproj");
			PlanExecutor.Execute (appliedPlan);

			Assert.Equal (Describe (previewPlan, preview.Directory), Describe (appliedPlan, applied.Directory));
		}

		static string [] Describe (ConversionPlan plan, string root)
		{
			return plan.Actions
				.Select (a => a.Kind + " " + Path.GetRelativePath (root, a.Path).Replace ('\\', '/') +
					      (a.Destination == null ? "" : " -> " + Path.GetFileName (a.Destination)))
				.ToArray ();
		}

		[Fact]
		public void Detected_packages_match_the_stack_and_cite_their_evidence ()
		{
			using var fixture = FixtureCopy.Of ("MvcWithBundling");
			ConversionPlan plan = PlanFor (fixture, "MvcWithBundling.csproj");

			string project = fixture.Read ("MvcWithBundling.csproj");
			PlanExecutor.Execute (plan);
			project = fixture.Read ("MvcWithBundling.csproj");

			Assert.Contains ("AspNetCore.Web.Hosting.Kestrel", project);
			Assert.Contains ("AspNetCore.Web.Mvc", project);
			Assert.Contains ("AspNetCore.Web.Optimization", project);
			Assert.Contains ("AspNetCore.Web.Razor", project);

			// Not a WCF or session-state application: those must NOT be dragged in.
			Assert.DoesNotContain ("AspNetCore.Web.ServiceModel", project);
			Assert.DoesNotContain ("AspNetCore.Web.SessionState", project);

			// Every inference is auditable - a detection nobody can check is a guess.
			Finding mvc = plan.Findings.First (f => f.Title == "Package AspNetCore.Web.Mvc");
			Assert.Equal (Severity.Info, mvc.Severity);
			Assert.Contains ("System.Web.Mvc", mvc.Detail);
			Assert.StartsWith ("MvcWithBundling.csproj:", mvc.Evidence);
		}

		[Fact]
		public void A_wcf_and_session_application_gets_both_stacks ()
		{
			using var fixture = FixtureCopy.Of ("WcfAndSessionState");
			ConversionPlan plan = PlanFor (fixture, "WcfAndSessionState.csproj");
			PlanExecutor.Execute (plan);

			string project = fixture.Read ("WcfAndSessionState.csproj");
			Assert.Contains ("AspNetCore.Web.ServiceModel", project);
			Assert.Contains ("AspNetCore.Web.SessionState", project);

			// And the host wires both, in the order that matters.
			string program = fixture.Read ("Program.cs");
			Assert.Contains ("AddSvcEndpoints", program);
			Assert.Contains ("UseSvcEndpoints", program);
			Assert.Contains ("UseWebFormsSessionState", program);
			Assert.True (program.IndexOf ("UseSvcEndpoints", StringComparison.Ordinal) <
				     program.IndexOf ("UseWebForms (options", StringComparison.Ordinal),
				     "UseSvcEndpoints must be generated before UseWebForms");
		}

		[Fact]
		public void Compile_and_content_items_are_not_carried_over ()
		{
			// The whole point of step 2: the SDK globs and the package's content globs replace them.
			// Re-emitting them is what produces NETSDK1022 duplicate-item errors.
			using var fixture = FixtureCopy.Of ("WebFormsCSharp");
			PlanExecutor.Execute (PlanFor (fixture, "WebFormsCSharp.csproj"));

			string project = fixture.Read ("WebFormsCSharp.csproj");

			Assert.DoesNotContain ("<Compile", project);
			Assert.DoesNotContain ("<Content", project);
			Assert.DoesNotContain ("Default.aspx", project);
			Assert.DoesNotContain ("App_Code", project);
		}

		[Fact]
		public void An_unrecognised_package_is_carried_over_and_reported ()
		{
            // Silently dropping a dependency would be the worst failure this tool could have.
			using var fixture = FixtureCopy.Of ("MvcWithBundling");
			ConversionPlan plan = PlanFor (fixture, "MvcWithBundling.csproj");
			PlanExecutor.Execute (plan);

			Assert.Contains ("Newtonsoft.Json", fixture.Read ("MvcWithBundling.csproj"));

			Finding carried = plan.Findings.First (f => f.Title.Contains ("Newtonsoft.Json"));
			Assert.Equal (Severity.NeedsAttention, carried.Severity);
			Assert.Contains ("net10.0", carried.Detail);
		}

		[Fact]
		public void A_superseded_package_is_dropped_and_reported ()
		{
			using var fixture = FixtureCopy.Of ("MvcWithBundling");
			ConversionPlan plan = PlanFor (fixture, "MvcWithBundling.csproj");
			PlanExecutor.Execute (plan);

			Assert.DoesNotContain ("Microsoft.AspNet.Mvc", fixture.Read ("MvcWithBundling.csproj"));
			Assert.Contains (plan.Findings, f => f.Title == "Dropped Microsoft.AspNet.Mvc");
			Assert.Contains (plan.Findings, f => f.Title == "Dropped WebGrease");
		}

		[Fact]
		public void A_vb_project_gets_an_empty_RootNamespace_and_a_csharp_one_does_not ()
		{
			// VB prepends RootNamespace to every declaration, so the default breaks Inherits=.
			using var vb = FixtureCopy.Of ("WebFormsVisualBasic");
			PlanExecutor.Execute (PlanFor (vb, "WebFormsVisualBasic.vbproj"));
			Assert.Contains ("<RootNamespace></RootNamespace>", vb.Read ("WebFormsVisualBasic.vbproj"));

			using var cs = FixtureCopy.Of ("WebFormsCSharp");
			PlanExecutor.Execute (PlanFor (cs, "WebFormsCSharp.csproj"));
			Assert.DoesNotContain ("<RootNamespace></RootNamespace>", cs.Read ("WebFormsCSharp.csproj"));
		}

		[Fact]
		public void No_host_is_generated_for_a_vb_project_and_it_says_so ()
		{
			using var fixture = FixtureCopy.Of ("WebFormsVisualBasic");
			ConversionPlan plan = PlanFor (fixture, "WebFormsVisualBasic.vbproj");
			PlanExecutor.Execute (plan);

			Assert.False (fixture.Exists ("Program.cs"));
			Assert.Contains (plan.Findings,
					 f => f.Severity == Severity.NeedsAttention && f.Title.Contains ("VB project"));
		}

		[Fact]
		public void An_existing_host_is_never_overwritten ()
		{
			using var fixture = FixtureCopy.Of ("WebFormsCSharp");
			File.WriteAllText (fixture.Path ("Program.cs"), "// mine, hand-written\n");

			ConversionPlan plan = PlanFor (fixture, "WebFormsCSharp.csproj");
			PlanExecutor.Execute (plan);

			Assert.Equal ("// mine, hand-written\n", fixture.Read ("Program.cs").Replace ("\r\n", "\n"));
			Assert.Contains (plan.Actions,
					 a => a.Kind == ActionKind.Skip && a.Path.EndsWith ("Program.cs", StringComparison.Ordinal));
		}

		[Fact]
		public void An_already_converted_project_is_refused ()
		{
			using var fixture = FixtureCopy.Of ("AlreadyConverted");
			ConversionPlan plan = PlanFor (fixture, "AlreadyConverted.csproj");

			Assert.True (plan.HasBlockers);
			Assert.Empty (plan.Actions);
			Assert.Contains (plan.Findings, f => f.Title.Contains ("Already an SDK-style project"));
		}

		[Fact]
		public void A_remoting_application_is_blocked ()
		{
			using var fixture = FixtureCopy.Of ("RemotingBlocker");
			ConversionPlan plan = PlanFor (fixture, "RemotingBlocker.csproj");

			Assert.True (plan.HasBlockers);

			var blockers = plan.Findings.Where (f => f.Severity == Severity.Blocker).ToArray ();
			Assert.Contains (blockers, f => f.Title.Contains ("System.Runtime.Remoting"));
			Assert.Contains (blockers, f => f.Title.Contains ("Calculator.rem"));
			Assert.Contains (blockers, f => f.Title.Contains ("system.runtime.remoting"));
			Assert.Contains (blockers, f => f.Title.Contains ("System.Web.Mobile"));
		}

		[Fact]
		public void A_dynamic_data_application_converts_rather_than_being_refused ()
		{
			// A regression guard with teeth. System.Web.DynamicData and System.Data.Linq were both on the
			// BLOCKER list, which writes nothing and exits 2 - so an application the port had started
			// supporting was still being told to rewrite itself. A stale blocker is worse than a missing
			// one: it refuses confidently.
			using var fixture = FixtureCopy.Of ("DynamicDataApp");
			ConversionPlan plan = PlanFor (fixture, "DynamicDataApp.csproj");

			Assert.False (plan.HasBlockers, PlanPrinter.Summary (plan, false));

			PlanExecutor.Execute (plan);
			Assert.Contains ("AspNetCore.Web.DynamicData", fixture.Read ("DynamicDataApp.csproj"));
		}

		[Fact]
		public void A_System_Data_Linq_reference_is_reported_rather_than_blocking ()
		{
			// LINQ to SQL genuinely does not exist on .NET, so the user has to know - but LinqDataSource
			// and Dynamic Data run on IQueryable now, so it is not fatal.
			using var fixture = FixtureCopy.Of ("DynamicDataApp");
			ConversionPlan plan = PlanFor (fixture, "DynamicDataApp.csproj");

			Finding linq = plan.Findings.First (f => f.Title.Contains ("System.Data.Linq"));

			Assert.Equal (Severity.NeedsAttention, linq.Severity);
			Assert.Contains ("IQueryable", linq.Detail);
		}

		[Fact]
		public void Mobile_controls_are_still_a_blocker ()
		{
			// The other half: unblocking the supported stacks must not unblock the one that genuinely
			// cannot be ported.
			using var fixture = FixtureCopy.Of ("RemotingBlocker");
			ConversionPlan plan = PlanFor (fixture, "RemotingBlocker.csproj");

			Assert.Contains (plan.Findings,
					 f => f.Severity == Severity.Blocker && f.Title.Contains ("System.Web.Mobile"));
		}

		[Fact]
		public void An_existing_backup_is_refused_unless_forced ()
		{
			using var fixture = FixtureCopy.Of ("WebFormsCSharp");
			File.WriteAllText (fixture.Path ("WebFormsCSharp.csproj.old"), "<Project />");

			Assert.True (PlanFor (fixture, "WebFormsCSharp.csproj").HasBlockers);

			ConversionPlan forced = PlanFor (fixture, "WebFormsCSharp.csproj", new PlannerOptions { Force = true });
			Assert.False (forced.HasBlockers);
		}

		[Fact]
		public void A_class_library_is_refused_rather_than_converted ()
		{
			using var fixture = FixtureCopy.Of ("WebFormsCSharp");

			// Strip everything that makes it a web project.
			foreach (string name in new [] { "Web.config", "Default.aspx", "Site.master" })
				File.Delete (fixture.Path (name));

			string project = fixture.Read ("WebFormsCSharp.csproj")
				.Replace ("<ProjectTypeGuids>{349c5851-65df-11da-9384-00065b846f21};{fae04ec0-301f-11d3-bf4b-00c04f79efbc}</ProjectTypeGuids>", "");
			File.WriteAllText (fixture.Path ("WebFormsCSharp.csproj"), project);

			ConversionPlan plan = PlanFor (fixture, "WebFormsCSharp.csproj");

			Assert.True (plan.HasBlockers);
			Assert.Contains (plan.Findings, f => f.Title.Contains ("does not look like a web project"));
		}

		[Fact]
		public void Options_can_turn_off_the_host_and_the_web_config_rewrite ()
		{
			using var fixture = FixtureCopy.Of ("MvcWithBundling");
			ConversionPlan plan = PlanFor (fixture, "MvcWithBundling.csproj",
						       new PlannerOptions { GenerateProgram = false, RewriteWebConfig = false });
			PlanExecutor.Execute (plan);

			Assert.False (fixture.Exists ("Program.cs"));
			Assert.False (fixture.Exists ("Web.config.old"));
			Assert.Contains ("System.Web.Mvc", fixture.Read ("Web.config"));
		}

		[Fact]
		public void The_package_version_is_configurable ()
		{
			using var fixture = FixtureCopy.Of ("WebFormsCSharp");
			PlanExecutor.Execute (PlanFor (fixture, "WebFormsCSharp.csproj",
						       new PlannerOptions { PackageVersion = "2.3.4" }));

			Assert.Contains ("Version=\"2.3.4\"", fixture.Read ("WebFormsCSharp.csproj"));
		}

		[Fact]
		public void Conditioned_item_groups_are_reported_rather_than_silently_misread ()
		{
			// The accepted cost of reading the document instead of evaluating it (PORT-TOOL-PLAN 0.4).
			// It has to be visible, or the limitation is indistinguishable from a bug.
			using var fixture = FixtureCopy.Of ("WebFormsCSharp");
			ConversionPlan plan = PlanFor (fixture, "WebFormsCSharp.csproj");

			Assert.Contains (plan.Findings,
					 f => f.Severity == Severity.NeedsAttention && f.Title.Contains ("conditioned item groups"));
		}
	}
}
