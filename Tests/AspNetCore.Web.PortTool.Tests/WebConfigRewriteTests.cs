//
// The web.config rewrites - the highest-risk part of the tool.
//
// A wrong web.config does not fail the build; it fails at APPLICATION START, after everything looked
// fine. So these test the exact boundary of what is rewritten, and just as importantly what is left
// alone.
//

using System;
using System.IO;
using System.Linq;
using PortProject;
using Xunit;

namespace WebFormsPort.PortToolTests
{
	public class WebConfigRewriteTests
	{
		static string Rewrite (string xml)
		{
			return WebConfigRewriter.Rewrite (xml).Content;
		}

		[Fact]
		public void An_assembly_attribute_is_renamed ()
		{
			string result = Rewrite ("<add assembly=\"System.Web.Mvc\" />");

			Assert.Contains ("assembly=\"Core.Web.Mvc\"", result);
		}

		[Fact]
		public void The_longest_assembly_name_wins ()
		{
			// "System.Web" is a prefix of "System.Web.Mvc". Match the short one first and you get
			// "Core.Web.Mvc" from one rule and "Core.Web" + ".Mvc" from the other, which is the same
			// string by luck rather than by design - and is NOT the same for WebPages.Razor.
			string result = Rewrite ("<add assembly=\"System.Web.WebPages.Razor\" />");

			Assert.Contains ("assembly=\"Core.Web.WebPages.Razor\"", result);
			Assert.DoesNotContain ("Core.Web.WebPages.WebPages", result);
		}

		[Fact]
		public void A_type_qualified_with_System_Configuration_is_repointed ()
		{
			// Type.GetType does NOT scan loaded assemblies. Left alone this binds to the empty facade
			// and the section silently degrades to DefaultSection.
			string result = Rewrite ("<section name=\"x\" type=\"My.Section, System.Configuration\" />");

			Assert.Contains ("My.Section, Core.Configuration", result);
		}

		[Fact]
		public void A_type_qualified_with_System_Web_loses_the_qualification ()
		{
			// HttpApplication.LoadType scans loaded assemblies, so the qualification is unnecessary
			// as well as wrong.
			string result = Rewrite ("<add name=\"UrlRoutingModule\" type=\"System.Web.Routing.UrlRoutingModule, System.Web\" />");

			Assert.Contains ("type=\"System.Web.Routing.UrlRoutingModule\"", result);
			Assert.DoesNotContain (", System.Web\"", result);
		}

		[Fact]
		public void Microsofts_strong_name_is_stripped ()
		{
			// .NET relaxes VERSION when binding but never the public key, and the port is signed with
			// its own - so a surviving token is a hard bind failure, not a warning.
			string result = Rewrite (
				"<add assembly=\"System.Web.Mvc, Version=5.2.7.0, Culture=neutral, PublicKeyToken=31BF3856AD364E35\" />");

			Assert.DoesNotContain ("PublicKeyToken", result);
			Assert.DoesNotContain ("Version=", result);
			Assert.DoesNotContain ("Culture=", result);
			Assert.Contains ("Core.Web.Mvc", result);
		}

		[Fact]
		public void processorArchitecture_is_stripped_too ()
		{
			string result = Rewrite (
				"<add assembly=\"System.Web.Mvc, Version=5.2.7.0, Culture=neutral, PublicKeyToken=31BF3856AD364E35, processorArchitecture=MSIL\" />");

			Assert.DoesNotContain ("processorArchitecture", result);
		}

		[Fact]
		public void An_application_type_is_left_alone ()
		{
			// "type=\"My.Handler, MyApp\"" is correct as it stands. Rewriting a user's own assembly
			// would be the tool inventing a problem.
			const string xml = "<add path=\"x.ashx\" type=\"MyApp.Handler, MyApp\" />";

			Assert.Equal (xml, Rewrite (xml));
		}

		[Fact]
		public void A_config_with_nothing_to_do_is_returned_byte_identical ()
		{
			const string xml = "<configuration><system.web><compilation debug=\"true\" /></system.web></configuration>";

			WebConfigRewriter.Result result = WebConfigRewriter.Rewrite (xml);

			Assert.False (result.Changed);
			Assert.Equal (xml, result.Content);
		}

		[Fact]
		public void Formatting_and_comments_survive ()
		{
			// The rewrite is done on TEXT, not through XDocument, precisely so the diff is the lines
			// that changed rather than the whole file reformatted.
			string xml = "<?xml version=\"1.0\"?>\r\n" +
				     "<configuration>\r\n" +
				     "  <!-- keep me -->\r\n" +
				     "  <system.web>\r\n" +
				     "    <httpModules>\r\n" +
				     "      <add name=\"M\" type=\"My.M, MyApp\" />\r\n" +
				     "    </httpModules>\r\n" +
				     "  </system.web>\r\n" +
				     "</configuration>\r\n";

			string result = Rewrite (xml);

			Assert.Contains ("<!-- keep me -->", result);
			Assert.Contains ("\r\n", result);
			Assert.Equal (xml, result);
		}

		[Fact]
		public void System_webServer_is_never_edited_and_is_always_reported ()
		{
			// The line the tool does not cross. An IIS <rewrite> ruleset has no mechanical translation,
			// and a half-translated one looks finished.
			using var fixture = FixtureCopy.Of ("WcfAndSessionState");
			ConversionPlan plan = Planner.Plan (fixture.Path ("WcfAndSessionState.csproj"), new PlannerOptions ());

			string before = fixture.Read ("Web.config");
			PlanExecutor.Execute (plan);
			string after = fixture.Read ("Web.config");

			// The rewrite section is preserved character for character.
			Assert.Contains ("<rewrite>", after);
			Assert.Contains ("Canonical host", after);
			Assert.Equal (Section (before, "<system.webServer>", "</system.webServer>"),
				      Section (after, "<system.webServer>", "</system.webServer>"));

			var reported = plan.Findings
				.Where (f => f.Severity == Severity.NeedsAttention && f.Title.Contains ("system.webServer"))
				.ToArray ();

			Assert.Contains (reported, f => f.Title.Contains ("rewrite"));
			Assert.Contains (reported, f => f.Title.Contains ("handlers"));
		}

		static string Section (string text, string open, string close)
		{
			int start = text.IndexOf (open, StringComparison.Ordinal);
			int end = text.IndexOf (close, StringComparison.Ordinal);
			return start < 0 || end < 0 ? null : text.Substring (start, end - start + close.Length);
		}

		[Fact]
		public void The_original_web_config_is_kept_as_old ()
		{
			using var fixture = FixtureCopy.Of ("MvcWithBundling");
			PlanExecutor.Execute (Planner.Plan (fixture.Path ("MvcWithBundling.csproj"), new PlannerOptions ()));

			Assert.True (fixture.Exists ("Web.config.old"));
			Assert.Contains ("System.Web.Mvc, Version=5.2.7.0", fixture.Read ("Web.config.old"));
			Assert.DoesNotContain ("PublicKeyToken", fixture.Read ("Web.config"));
		}

		[Fact]
		public void Views_web_config_is_rewritten_too ()
		{
			// PORTING-GUIDE.md step 4 calls this out explicitly. Rewrite only the root and the project
			// builds, starts, and then fails to compile any view with a type it cannot find.
			using var fixture = FixtureCopy.Of ("MvcWithBundling");
			PlanExecutor.Execute (Planner.Plan (fixture.Path ("MvcWithBundling.csproj"), new PlannerOptions ()));

			string views = fixture.Read ("Views/Web.config");

			Assert.Contains ("Core.Web.Mvc", views);
			Assert.Contains ("Core.Web.WebPages.Razor", views);
			Assert.DoesNotContain ("PublicKeyToken", views);
			Assert.True (fixture.Exists ("Views/Web.config.old"));
		}
	}
}
