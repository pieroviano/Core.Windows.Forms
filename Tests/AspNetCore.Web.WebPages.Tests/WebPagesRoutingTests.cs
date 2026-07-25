//
// ASP.NET Web Pages: a .cshtml file served directly, with no controller and no route table.
//
// Nothing in the application registers this - WebPageHttpModule adds itself through a
// PreApplicationStartMethod on Core.Web.WebPages, and WebPageRoute maps the incoming URL to a file.
// That self-registration is the part most likely to break silently, because when it does not happen
// the request simply falls through to the ordinary handler mapping and 404s.
//

using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Xunit;

namespace WebFormsPort.WebPagesTests
{
	[Collection (WebPagesCollection.Name)]
	public class WebPagesRoutingTests
	{
		readonly WebPagesFixture fixture;

		public WebPagesRoutingTests (WebPagesFixture fixture)
		{
			this.fixture = fixture;
		}

		[Theory]
		[InlineData ("/Default.cshtml")]
		[InlineData ("/Default")]      // extensionless
		[InlineData ("/")]             // default document
		public async Task Standalone_page_is_served (string path)
		{
			using HttpClient client = fixture.CreateClient ();

			HttpResponseMessage response = await client.GetAsync (path);
			string body = await response.Content.ReadAsStringAsync ();

			Assert.Equal (HttpStatusCode.OK, response.StatusCode);
			Assert.Contains ("<h1 id=\"heading\">Web Pages</h1>", body);
		}

		[Theory]
		[InlineData ("/Helpers.cshtml")]
		[InlineData ("/Helpers")]
		public async Task Routing_is_per_file_rather_than_one_default_page (string path)
		{
			using HttpClient client = fixture.CreateClient ();

			string body = await client.GetStringAsync (path);

			Assert.Contains ("<h1 id=\"heading\">Helpers</h1>", body);
		}

		[Fact]
		public async Task Underscore_prefixed_file_is_never_served ()
		{
			using HttpClient client = fixture.CreateClient ();

			// A Web Pages rule, and a security one: _Layout.cshtml, _PageStart.cshtml and friends are
			// implementation files. They are reachable from other pages but never over HTTP.
			HttpResponseMessage response = await client.GetAsync ("/_Layout.cshtml");

			Assert.Equal (HttpStatusCode.NotFound, response.StatusCode);
		}

		[Fact]
		public async Task Unknown_page_is_a_404 ()
		{
			using HttpClient client = fixture.CreateClient ();

			HttpResponseMessage response = await client.GetAsync ("/NoSuchPage");

			Assert.Equal (HttpStatusCode.NotFound, response.StatusCode);
		}
	}
}
