//
// Parse errors, and the embedded resources their messages come from.
//
// The resource assertions matter more than they look. Upstream declares the manifest names in its
// Makefile (RESOURCE_DEFS) and the Designer classes look them up at runtime - RazorResources by an
// exact literal, CommonResources by a suffix search that calls .Single(). The port has to reproduce
// both in MSBuild, and getting it wrong yields a MissingManifestResourceException the first time a
// view fails to parse: the error path breaks exactly when you need it.
//

using System.IO;
using System.Linq;
using System.Reflection;
using System.Web.Razor;
using System.Web.Razor.Parser.SyntaxTree;
using Xunit;

namespace WebFormsPort.RazorTests
{
	public class RazorDiagnosticsTests
	{
		static GeneratorResults Generate (string template)
		{
			var host = new RazorEngineHost (new CSharpRazorCodeLanguage ()) {
				DefaultNamespace = "Generated",
				DefaultClassName = "RazorView",
				DefaultBaseClass = "System.Object",
			};

			using var reader = new StringReader (template);
			return new RazorTemplateEngine (host).GenerateCode (reader);
		}

		[Fact]
		public void Unterminated_code_block_is_reported_as_a_parse_error ()
		{
			GeneratorResults results = Generate ("@{ var x = 1;");

			Assert.False (results.Success);
			Assert.NotEmpty (results.ParserErrors);
		}

		[Fact]
		public void Parse_error_carries_a_real_message_from_the_resources ()
		{
			GeneratorResults results = Generate ("@{ var x = 1;");

			RazorError error = results.ParserErrors.First ();

			// If RazorResources were embedded under the wrong manifest name, constructing the message
			// would throw rather than return this - so a non-empty message that is not a bare resource
			// key is the assertion.
			Assert.False (string.IsNullOrWhiteSpace (error.Message), "parse error had no message");
			Assert.Contains ("}", error.Message);
		}

		[Fact]
		public void Parse_error_carries_a_source_location ()
		{
			GeneratorResults results = Generate ("<p>ok</p>\r\n@{ var x = 1;");

			RazorError error = results.ParserErrors.First ();

			// Line numbers are what make a view compile error actionable.
			Assert.True (error.Location.LineIndex >= 1,
				     "expected the error on the second line, got line index " + error.Location.LineIndex);
		}

		[Fact]
		public void Valid_template_reports_no_errors ()
		{
			GeneratorResults results = Generate ("<p>@name</p>");

			Assert.True (results.Success);
			Assert.Empty (results.ParserErrors);
		}

		[Fact]
		public void Razor_resources_are_embedded_under_the_name_the_designer_looks_up ()
		{
			Assembly razor = typeof (RazorTemplateEngine).Assembly;

			// RazorResources.Designer.cs constructs a ResourceManager for this exact literal.
			Assert.Contains ("System.Web.Razor.Resources.RazorResources.resources",
					 razor.GetManifestResourceNames ());
		}

		[Fact]
		public void Exactly_one_CommonResources_is_embedded ()
		{
			Assembly razor = typeof (RazorTemplateEngine).Assembly;

			// CommonResources.Designer.cs searches for a name ENDING in this and calls .Single().
			// Two matches would throw InvalidOperationException instead of returning a message.
			Assert.Single (razor.GetManifestResourceNames ()
				.Where (n => n.EndsWith ("CommonResources.resources", System.StringComparison.OrdinalIgnoreCase)));
		}

		[Fact]
		public void Ported_assembly_is_Core_Web_Razor_with_upstream_namespaces ()
		{
			Assembly razor = typeof (RazorTemplateEngine).Assembly;

			Assert.Equal ("Core.Web.Razor", razor.GetName ().Name);
			// The rename is an assembly-identity change only; type names are untouched, which is what
			// lets upstream sources compile in place.
			Assert.Equal ("System.Web.Razor.RazorTemplateEngine", typeof (RazorTemplateEngine).FullName);
		}
	}
}
