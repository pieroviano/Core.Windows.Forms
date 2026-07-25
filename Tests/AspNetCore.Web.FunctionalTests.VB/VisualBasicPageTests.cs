//
// The VB compilation path: pages generated as VB source by VBCodeProvider and compiled by the Roslyn
// VB backend, plus the VB App_Code compiler and Option Strict Off semantics.
//

using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Xunit;

namespace WebFormsPort.FunctionalTests.VB
{
	[Collection (WebFormsVBCollection.Name)]
	public class VisualBasicPageTests
	{
		readonly SampleAppVBFixture fixture;

		public VisualBasicPageTests (SampleAppVBFixture fixture)
		{
			this.fixture = fixture;
		}

		[Fact]
		public async Task Page_with_VB_code_behind_renders ()
		{
			using HttpClient client = fixture.CreateClient ();

			HttpResponseMessage response = await client.GetAsync ("/Default.aspx");
			string body = await response.Content.ReadAsStringAsync ();

			Assert.Equal (HttpStatusCode.OK, response.StatusCode);
			Assert.Contains ("<h1>VB WebForms on Kestrel</h1>", body);
			Assert.Contains ("<span id=\"message\">hello from OnLoad</span>", body);
			Assert.Contains ("<p>Inline VB expression: 42</p>", body);
		}

		[Fact]
		public async Task App_Code_compiled_in_the_site_default_language_is_usable ()
		{
			using HttpClient client = fixture.CreateClient ();

			string body = await client.GetStringAsync ("/Default.aspx");

			// App_Code/Helper.vb is compiled at runtime by BuildManager using the site's
			// defaultLanguage ("vb"), not by the SDK.
			Assert.Contains ("<p>App_Code (VB): APP_CODE WORKS!</p>", body);
		}

		[Fact]
		public async Task Inline_VB_page_compiles_with_Option_Strict_Off ()
		{
			using HttpClient client = fixture.CreateClient ();

			HttpResponseMessage response = await client.GetAsync ("/Inline.aspx");
			string body = await response.Content.ReadAsStringAsync ();

			Assert.Equal (HttpStatusCode.OK, response.StatusCode);
			// A String assigned to an Integer: legal only because <compilation strict="false">
			// reaches the VB compiler. 123 + 1 = 124.
			Assert.Contains ("late-bound conversion gave 124", body);
		}

		[Fact]
		public async Task Inline_VB_page_reads_appSettings_from_web_config ()
		{
			using HttpClient client = fixture.CreateClient ();

			string body = await client.GetStringAsync ("/Inline.aspx");

			Assert.Contains ("appSetting=VB WebForms on Kestrel", body);
		}

		[Fact]
		public async Task VB_render_block_writes_to_the_response ()
		{
			using HttpClient client = fixture.CreateClient ();

			string body = await client.GetStringAsync ("/Inline.aspx");

			Assert.Contains ("<span>item 1</span> <span>item 2</span> <span>item 3</span>", body);
		}

		[Fact]
		public async Task VB_global_asax_runs_on_every_request ()
		{
			using HttpClient client = fixture.CreateClient ();

			HttpResponseMessage response = await client.GetAsync ("/Default.aspx");

			Assert.Equal ("begin-request", Assert.Single (response.Headers.GetValues ("X-Global-Asax-VB")));
		}

		[Fact]
		public async Task GridView_and_Repeater_render_from_VB_code_behind ()
		{
			using HttpClient client = fixture.CreateClient ();

			string body = await client.GetStringAsync ("/Default.aspx");

			Assert.Contains ("<td>1</td><td>sprocket</td><td>9.99</td>", body);
			// 24.5, not 24.50: the VB sample's DataTable is built with the literal 24.5D, so the
			// decimal carries one place. (The C# sample writes 24.50m and renders two.)
			Assert.Contains ("<td>2</td><td>flange</td><td>24.5</td>", body);
			Assert.Contains ("<ul><li>alpha</li><li>beta</li><li>gamma</li></ul>", body);
		}
	}
}
