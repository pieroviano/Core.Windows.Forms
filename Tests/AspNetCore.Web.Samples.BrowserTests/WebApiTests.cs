//
// Samples/WebApiSample: a Web API controller used from a browser - both by navigating to it, where
// content negotiation honours the browser's Accept header, and from script with fetch.
//

using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Playwright;
using Xunit;

namespace WebFormsPort.SamplesBrowserTests
{
	[Collection (WebApiCollection.Name)]
	public class WebApiTests
	{
		readonly WebApiFixture fixture;

		public WebApiTests (WebApiFixture fixture)
		{
			this.fixture = fixture;
		}

		const string Fetch = @"async ([url, init]) => {
			const r = await fetch(url, init);
			return { status: r.status, type: r.headers.get('content-type') || '', body: await r.text() };
		}";

		[Fact]
		public Task Navigating_to_the_api_negotiates_xml_from_the_browser_Accept_header () => fixture.RunAsync (async page => {
			IResponse response = await page.GotoAsync ("/api/widgets");

			// Chromium sends Accept: text/html,application/xhtml+xml,application/xml;q=0.9,... - the
			// XML formatter is the first that matches, exactly as it was on ASP.NET.
			Assert.Equal (200, response.Status);
			Assert.Contains ("xml", response.Headers ["content-type"]);
			Assert.Contains ("sprocket", await response.TextAsync ());
		});

		[Fact]
		public Task Fetch_gets_json_lists_and_single_items () => fixture.RunAsync (async page => {
			await page.GotoAsync ("/api/widgets");

			JsonElement all = await page.EvaluateAsync<JsonElement> (Fetch, new object [] { "/api/widgets", new { headers = new { Accept = "application/json" } } });
			Assert.Equal (200, all.GetProperty ("status").GetInt32 ());
			Assert.StartsWith ("application/json", all.GetProperty ("type").GetString ());
			Assert.Contains ("\"Name\":\"grommet\"", all.GetProperty ("body").GetString ());

			JsonElement one = await page.EvaluateAsync<JsonElement> (Fetch, new object [] { "/api/widgets/2", new { headers = new { Accept = "application/json" } } });
			Assert.Contains ("\"Name\":\"flange\"", one.GetProperty ("body").GetString ());

			JsonElement missing = await page.EvaluateAsync<JsonElement> (Fetch, new object [] { "/api/widgets/99", new { } });
			Assert.Equal (404, missing.GetProperty ("status").GetInt32 ());
		});

		[Fact]
		public Task Posted_json_with_content_type_is_bound_and_created () => fixture.RunAsync (async page => {
			await page.GotoAsync ("/api/widgets");

			JsonElement created = await page.EvaluateAsync<JsonElement> (Fetch, new object [] { "/api/widgets", new {
				method = "POST",
				headers = new Dictionary { ["Content-Type"] = "application/json", ["Accept"] = "application/json" },
				body = "{\"Name\":\"from-browser\",\"Price\":1.5}",
			} });
			Assert.Equal (201, created.GetProperty ("status").GetInt32 ());
			Assert.Contains ("\"Name\":\"from-browser\"", created.GetProperty ("body").GetString ());

			JsonElement rejected = await page.EvaluateAsync<JsonElement> (Fetch, new object [] { "/api/widgets", new {
				method = "POST",
				headers = new Dictionary { ["Content-Type"] = "application/json" },
				body = "{\"Price\":1.5}",
			} });
			Assert.Equal (400, rejected.GetProperty ("status").GetInt32 ());
		});

		sealed class Dictionary : System.Collections.Generic.Dictionary<string, string> { }
	}
}
