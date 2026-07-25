//
// What a Web Pages .cshtml can do once it is being served: layouts, the Page dictionary, Razor
// expressions, configuration, and posting back to itself.
//

using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Xunit;

namespace WebFormsPort.WebPagesTests
{
	[Collection (WebPagesCollection.Name)]
	public class WebPagesRenderingTests
	{
		readonly WebPagesFixture fixture;

		public WebPagesRenderingTests (WebPagesFixture fixture)
		{
			this.fixture = fixture;
		}

		[Fact]
		public async Task Page_renders_through_its_layout ()
		{
			using HttpClient client = fixture.CreateClient ();

			string body = await client.GetStringAsync ("/Default.cshtml");

			Assert.Contains ("<div id=\"chrome-header\">[webpages layout header]</div>", body);
			Assert.Contains ("<div id=\"chrome-footer\">[webpages layout footer]</div>", body);
		}

		[Fact]
		public async Task Page_dictionary_carries_state_into_the_layout ()
		{
			using HttpClient client = fixture.CreateClient ();

			string body = await client.GetStringAsync ("/Default.cshtml");

			// The page sets Page.Title; the LAYOUT reads it. Web Pages' equivalent of ViewBag.
			Assert.Contains ("<title>Home - Web Pages on Kestrel</title>", body);
		}

		[Fact]
		public async Task Razor_expressions_and_loops_execute ()
		{
			using HttpClient client = fixture.CreateClient ();

			string body = await client.GetStringAsync ("/Default.cshtml");

			Assert.Contains ("<p id=\"sum\">42</p>", body);
			Assert.Contains ("<li>sprocket = 1</li>", body);
			Assert.Contains ("<li>grommet = 3</li>", body);
		}

		[Fact]
		public async Task Page_reads_appSettings_from_web_config ()
		{
			using HttpClient client = fixture.CreateClient ();

			string body = await client.GetStringAsync ("/Default.cshtml");

			Assert.Contains ("<p id=\"setting\">Web Pages on Kestrel</p>", body);
		}

		[Fact]
		public async Task Razor_html_encodes_by_default ()
		{
			using HttpClient client = fixture.CreateClient ();

			string body = await client.GetStringAsync ("/Default.cshtml");

			Assert.Contains ("&lt;script&gt;alert(1)&lt;/script&gt;", body);
			Assert.DoesNotContain ("<p id=\"encoded\"><script>", body);
		}

		[Fact]
		public async Task Page_posts_back_to_itself_and_sees_the_form ()
		{
			using HttpClient client = fixture.CreateClient ();

			// No controller and no action - a Web Pages file handles its own POST, and IsPost is how
			// it tells the two apart.
			HttpResponseMessage response = await client.PostAsync ("/Default.cshtml",
				new FormUrlEncodedContent (new Dictionary<string, string> { ["who"] = "piero" }));
			string body = await response.Content.ReadAsStringAsync ();

			Assert.Equal (HttpStatusCode.OK, response.StatusCode);
			Assert.Contains ("<p id=\"greeting\">Hello, piero!</p>", body);
			// And the posted value is echoed back into the field.
			Assert.Contains ("value=\"piero\"", body);
		}

		[Fact]
		public async Task Get_request_does_not_take_the_post_branch ()
		{
			using HttpClient client = fixture.CreateClient ();

			string body = await client.GetStringAsync ("/Default.cshtml");

			Assert.DoesNotContain ("id=\"greeting\"", body);
		}

		[Fact]
		public async Task Request_information_is_available_to_the_page ()
		{
			using HttpClient client = fixture.CreateClient ();

			string body = await client.GetStringAsync ("/Helpers.cshtml");

			Assert.Contains ("<p id=\"path\">/Helpers.cshtml</p>", body);
			Assert.Contains ("<p id=\"method\">GET</p>", body);
		}
	}
}
