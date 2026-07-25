//
// Core.Web.Razor: text in, CodeCompileUnit out.
//
// The assertions are made against the GENERATED SOURCE rather than the CodeDom tree. That is the
// form the rest of the port actually consumes - RoslynCompiler takes a CodeCompileUnit, renders it
// with CSharpCodeProvider and compiles the text - so asserting on it tests what will really be fed
// to the compiler, and reads far better than walking CodeDom members.
//

using System.CodeDom;
using System.CodeDom.Compiler;
using System.IO;
using System.Linq;
using System.Web.Razor;
using Microsoft.CSharp;
using Xunit;

namespace WebFormsPort.RazorTests
{
	public class RazorTemplateEngineTests
	{
		const string Namespace = "Generated";
		const string ClassName = "RazorView";

		static GeneratorResults Generate (string template)
		{
			var host = new RazorEngineHost (new CSharpRazorCodeLanguage ()) {
				DefaultNamespace = Namespace,
				DefaultClassName = ClassName,
				DefaultBaseClass = "System.Object",
			};

			var engine = new RazorTemplateEngine (host);

			using var reader = new StringReader (template);
			return engine.GenerateCode (reader);
		}

		static string GenerateSource (string template)
		{
			GeneratorResults results = Generate (template);

			Assert.True (results.Success,
				     "Razor reported parse errors: " +
				     string.Join ("; ", results.ParserErrors.Select (e => e.Message)));

			using var provider = new CSharpCodeProvider ();
			using var writer = new StringWriter ();
			provider.GenerateCodeFromCompileUnit (results.GeneratedCode, writer, new CodeGeneratorOptions ());
			return writer.ToString ();
		}

		[Fact]
		public void Plain_markup_parses_and_generates_a_class ()
		{
			string source = GenerateSource ("<h1>Hello</h1>");

			Assert.Contains ("namespace Generated", source);
			Assert.Contains ("class RazorView", source);
			// Literal markup is written out, not interpreted.
			Assert.Contains ("<h1>Hello</h1>", source);
		}

		[Fact]
		public void Implicit_expression_is_emitted_as_a_write ()
		{
			string source = GenerateSource ("<p>@name</p>");

			// The default host writes expressions through Write(...); the literal text around it goes
			// through WriteLiteral(...). Both must appear or the template renders nothing.
			Assert.Contains ("Write(name)", source.Replace (" ", ""));
			Assert.Contains ("WriteLiteral", source);
		}

		[Fact]
		public void Explicit_expression_is_emitted_as_a_write ()
		{
			string source = GenerateSource ("<p>@(1 + 2)</p>");

			Assert.Contains ("Write(1+2)", source.Replace (" ", ""));
		}

		[Fact]
		public void Code_block_is_emitted_verbatim ()
		{
			string source = GenerateSource ("@{ var total = 6 * 7; }<p>@total</p>");

			Assert.Contains ("var total = 6 * 7;", source);
			Assert.Contains ("Write(total)", source.Replace (" ", ""));
		}

		[Fact]
		public void Control_flow_wraps_the_markup_inside_it ()
		{
			string source = GenerateSource ("@if (ok) {<b>yes</b>} else {<i>no</i>}");

			Assert.Contains ("if (ok)", source);
			Assert.Contains ("else", source);
			Assert.Contains ("<b>yes</b>", source);
			Assert.Contains ("<i>no</i>", source);
		}

		[Fact]
		public void Foreach_over_a_collection_generates_a_loop ()
		{
			string source = GenerateSource ("@foreach (var x in items) {<li>@x</li>}");

			Assert.Contains ("foreach (var x in items)", source);
			Assert.Contains ("<li>", source);
			Assert.Contains ("Write(x)", source.Replace (" ", ""));
		}

		[Fact]
		public void Email_address_is_not_treated_as_an_expression ()
		{
			// The classic Razor ambiguity. "@example.com" following text must stay literal, or every
			// mailto: in a view becomes a compile error.
			string source = GenerateSource ("<p>piero@example.com</p>");

			Assert.Contains ("piero@example.com", source);
		}

		[Fact]
		public void Escaped_at_sign_renders_a_literal_at ()
		{
			string source = GenerateSource ("<p>@@twitter</p>");

			Assert.Contains ("@twitter", source);
		}

		[Fact]
		public void Using_directive_is_hoisted_into_the_generated_namespace ()
		{
			string source = GenerateSource ("@using System.Text\r\n<p>ok</p>");

			Assert.Contains ("using System.Text;", source);
		}

		[Fact]
		public void Functions_block_adds_members_to_the_generated_class ()
		{
			string source = GenerateSource ("@functions { public int Double(int n) { return n * 2; } }<p>ok</p>");

			Assert.Contains ("public int Double(int n)", source);
		}

		[Fact]
		public void Base_class_and_class_name_come_from_the_host ()
		{
			var host = new RazorEngineHost (new CSharpRazorCodeLanguage ()) {
				DefaultNamespace = "My.Views",
				DefaultClassName = "IndexView",
				DefaultBaseClass = "System.Web.Razor.Test.BaseView",
			};

			using var reader = new StringReader ("<p>ok</p>");
			GeneratorResults results = new RazorTemplateEngine (host).GenerateCode (reader);

			Assert.True (results.Success);

			var ns = Assert.Single (results.GeneratedCode.Namespaces.Cast<CodeNamespace> ());
			Assert.Equal ("My.Views", ns.Name);

			var type = Assert.Single (ns.Types.Cast<CodeTypeDeclaration> ());
			Assert.Equal ("IndexView", type.Name);
			Assert.Contains (type.BaseTypes.Cast<CodeTypeReference> (),
					 b => b.BaseType == "System.Web.Razor.Test.BaseView");
		}
	}
}
