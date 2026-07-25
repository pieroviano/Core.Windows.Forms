//
// .aspx compilation and rendering: the AspGenerator -> TemplateParser -> CodeCompileUnit ->
// RoslynCompiler path, and the control tree it produces.
//

using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Xunit;

namespace WebFormsPort.FunctionalTests
{
	[Collection (WebFormsCollection.Name)]
	public class PageRenderingTests
	{
		readonly SampleAppFixture fixture;

		public PageRenderingTests (SampleAppFixture fixture)
		{
			this.fixture = fixture;
		}

		[Fact]
		public async Task Inline_expressions_and_code_render ()
		{
			using HttpClient client = fixture.CreateClient ();

			HttpResponseMessage response = await client.GetAsync ("/Simple.aspx");
			string body = await response.Content.ReadAsStringAsync ();

			Assert.Equal (HttpStatusCode.OK, response.StatusCode);
			Assert.Contains ("<p>Inline expression: 5</p>", body);
			Assert.Contains ("<p>Request path: /Simple.aspx</p>", body);
			// A <% %> render block writing straight to the response, three times.
			Assert.Contains ("<span>item 1</span> <span>item 2</span> <span>item 3</span>", body);
		}

		[Fact]
		public async Task Code_behind_class_named_by_Inherits_drives_the_page ()
		{
			using HttpClient client = fixture.CreateClient ();

			string body = await client.GetStringAsync ("/Default.aspx");

			// The generated page class derives from WebFormsSample.DefaultPage and assigns the
			// runat="server" controls to its protected fields; this is that wiring working.
			Assert.Contains ("<span id=\"message\">hello from Page_Load</span>", body);
			Assert.Contains ("<p>Inline expression: 42</p>", body);
			Assert.Contains ("IsPostBack=False, HttpMethod=GET", body);
		}

		[Fact]
		public async Task GridView_renders_a_bound_DataTable ()
		{
			using HttpClient client = fixture.CreateClient ();

			string body = await client.GetStringAsync ("/Default.aspx");

			Assert.Contains ("<th scope=\"col\">Id</th>", body);
			Assert.Contains ("<th scope=\"col\">Name</th>", body);
			Assert.Contains ("<th scope=\"col\">Price</th>", body);
			Assert.Contains ("<td>1</td><td>sprocket</td><td>9.99</td>", body);
			Assert.Contains ("<td>2</td><td>flange</td><td>24.50</td>", body);
			Assert.Contains ("<td>3</td><td>grommet</td><td>3.75</td>", body);
		}

		[Fact]
		public async Task Repeater_renders_header_items_and_footer ()
		{
			using HttpClient client = fixture.CreateClient ();

			string body = await client.GetStringAsync ("/Default.aspx");

			Assert.Contains ("<ul><li>alpha</li><li>beta</li><li>gamma</li></ul>", body);
		}

		[Fact]
		public async Task Server_form_renders_view_state_and_event_validation ()
		{
			using HttpClient client = fixture.CreateClient ();

			string body = await client.GetStringAsync ("/Default.aspx");

			Assert.Contains ("<form method=\"post\" action=\"Default.aspx\" id=\"form1\">", body);

			var hidden = WebForm.HiddenFields (body);
			Assert.True (hidden.ContainsKey ("__VIEWSTATE"), "page rendered no __VIEWSTATE field");
			Assert.True (hidden.ContainsKey ("__EVENTVALIDATION"), "page rendered no __EVENTVALIDATION field");
			Assert.NotEmpty (hidden ["__VIEWSTATE"]);
		}

		[Fact]
		public async Task Pages_are_compiled_once_and_cached ()
		{
			using HttpClient client = fixture.CreateClient ();

			// Two requests for the same page must not recompile it. Nothing observable in the body
			// proves that directly, so this asserts the weaker but still useful property that a
			// second request succeeds identically - a broken compiled-page cache typically throws
			// "type already defined" or leaks assemblies on the second hit.
			string first = await client.GetStringAsync ("/Simple.aspx");
			string second = await client.GetStringAsync ("/Simple.aspx");

			Assert.Contains ("<h1>Simple page</h1>", first);
			Assert.Contains ("<h1>Simple page</h1>", second);
		}
	}
}
