//
// The @ServiceHost parser, on its own. No server: a .svc file is read at startup, so a malformed one
// has to produce a clear answer before anything is listening.
//

using System;
using System.IO;
using System.Linq;
using System.Web.ServiceModel;
using Xunit;

namespace WebFormsPort.ServiceModelTests
{
	public class ServiceHostDirectiveTests : IDisposable
	{
		readonly string root;

		public ServiceHostDirectiveTests ()
		{
			root = Path.Combine (Path.GetTempPath (), "svc-directive-tests", Guid.NewGuid ().ToString ("n"));
			Directory.CreateDirectory (root);
		}

		public void Dispose ()
		{
			if (Directory.Exists (root))
				Directory.Delete (root, recursive: true);
		}

		string WriteSvc (string relativePath, string contents)
		{
			string full = Path.Combine (root, relativePath.Replace ('/', Path.DirectorySeparatorChar));
			Directory.CreateDirectory (Path.GetDirectoryName (full));
			File.WriteAllText (full, contents);
			return full;
		}

		[Fact]
		public void Parses_the_service_attribute ()
		{
			string file = WriteSvc ("Echo.svc",
				"<%@ ServiceHost Language=\"C#\" Debug=\"true\" Service=\"MyApp.EchoService\" %>");

			ServiceHostDirective directive = ServiceHostDirective.Parse (file, "/Echo.svc");

			Assert.NotNull (directive);
			Assert.Equal ("MyApp.EchoService", directive.ServiceTypeName);
			Assert.Equal ("/Echo.svc", directive.VirtualPath);
			Assert.Null (directive.FactoryTypeName);
		}

		[Fact]
		public void Attribute_names_are_case_insensitive ()
		{
			// ASP.NET never cared about the casing here and neither did the tooling that wrote these
			// files, so both spellings turn up in real applications.
			string file = WriteSvc ("Echo.svc", "<%@ servicehost service=\"MyApp.EchoService\" %>");

			Assert.Equal ("MyApp.EchoService", ServiceHostDirective.Parse (file, "/Echo.svc").ServiceTypeName);
		}

		[Fact]
		public void Recognises_a_custom_factory ()
		{
			string file = WriteSvc ("Echo.svc",
				"<%@ ServiceHost Service=\"MyApp.EchoService\" Factory=\"MyApp.MyHostFactory\" %>");

			Assert.Equal ("MyApp.MyHostFactory", ServiceHostDirective.Parse (file, "/Echo.svc").FactoryTypeName);
		}

		[Fact]
		public void Keeps_every_attribute ()
		{
			string file = WriteSvc ("Echo.svc",
				"<%@ ServiceHost Language=\"C#\" Debug=\"true\" Service=\"MyApp.EchoService\" " +
				"CodeBehind=\"EchoService.svc.cs\" %>");

			ServiceHostDirective directive = ServiceHostDirective.Parse (file, "/Echo.svc");

			Assert.Equal ("C#", directive.Attributes ["Language"]);
			Assert.Equal ("true", directive.Attributes ["Debug"]);
			Assert.Equal ("EchoService.svc.cs", directive.Attributes ["CodeBehind"]);
		}

		[Fact]
		public void A_file_with_no_directive_is_not_a_service ()
		{
			// Null rather than an exception: a .svc with no @ServiceHost is not a broken service, it is
			// not a service at all, and discovery should walk past it.
			string file = WriteSvc ("NotAService.svc", "just some text");

			Assert.Null (ServiceHostDirective.Parse (file, "/NotAService.svc"));
		}

		[Fact]
		public void Directive_may_span_lines_and_carry_leading_content ()
		{
			string file = WriteSvc ("Echo.svc",
				"\r\n<%@ ServiceHost\r\n    Language=\"C#\"\r\n    Service=\"MyApp.EchoService\"\r\n%>\r\n");

			Assert.Equal ("MyApp.EchoService", ServiceHostDirective.Parse (file, "/Echo.svc").ServiceTypeName);
		}

		[Fact]
		public void Discover_walks_sub_directories_and_derives_the_virtual_path ()
		{
			WriteSvc ("Echo.svc", "<%@ ServiceHost Service=\"A\" %>");
			WriteSvc ("Api/Calculator.svc", "<%@ ServiceHost Service=\"B\" %>");
			WriteSvc ("Api/v2/Calculator.svc", "<%@ ServiceHost Service=\"C\" %>");

			string [] paths = ServiceHostDirective.Discover (root)
				.Select (d => d.VirtualPath).OrderBy (p => p, StringComparer.Ordinal).ToArray ();

			Assert.Equal (new [] { "/Api/Calculator.svc", "/Api/v2/Calculator.svc", "/Echo.svc" }, paths);
		}

		[Fact]
		public void Discover_skips_bin_and_obj ()
		{
			// Both hold a copy of the site once it has been built or published into them. Registering
			// those copies would bind the same service twice and CoreWCF would refuse the second.
			WriteSvc ("Echo.svc", "<%@ ServiceHost Service=\"A\" %>");
			WriteSvc ("bin/Echo.svc", "<%@ ServiceHost Service=\"A\" %>");
			WriteSvc ("obj/Debug/Echo.svc", "<%@ ServiceHost Service=\"A\" %>");

			Assert.Equal (new [] { "/Echo.svc" },
				      ServiceHostDirective.Discover (root).Select (d => d.VirtualPath).ToArray ());
		}

		[Fact]
		public void Discover_on_a_missing_directory_is_empty_rather_than_throwing ()
		{
			Assert.Empty (ServiceHostDirective.Discover (Path.Combine (root, "nope")));
		}
	}
}
