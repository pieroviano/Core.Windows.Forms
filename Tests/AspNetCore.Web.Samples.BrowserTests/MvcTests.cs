//
// Samples/MvcSample: conventional and attribute routing, Razor views, forms, TempData and bundling.
//

using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Playwright;
using Xunit;

namespace WebFormsPort.SamplesBrowserTests
{
	[Collection (MvcCollection.Name)]
	public class MvcTests
	{
		readonly MvcFixture fixture;

		public MvcTests (MvcFixture fixture)
		{
			this.fixture = fixture;
		}

		[Fact]
		public Task Index_lists_widgets_and_links_to_details () => fixture.RunAsync (async page => {
			await page.GotoAsync ("/");

			Assert.Equal ("Index - MVC on Kestrel", await page.TitleAsync ());
			Assert.Equal ("Widgets", await page.TextAsync ("h1"));
			Assert.Equal ("3", await page.TextAsync ("#count"));
			Assert.Equal ("[index section]", await page.TextAsync ("#section-footer"));

			await page.ClickAndWaitAsync ("#widgets a:text-is('flange')");

			Assert.EndsWith ("/Home/Details/2", page.Url);
			Assert.Equal ("2 / flange / 24.50", await page.TextAsync ("#detail"));
			Assert.Equal ("<script>alert(1)</script>", await page.TextAsync ("#encoded"));

			await page.GoBackAsync ();
			Assert.Equal ("Widgets", await page.TextAsync ("h1"));
		});

		[Fact]
		public Task Posted_form_redirects_and_TempData_survives_exactly_one_request () => fixture.RunAsync (async page => {
			await page.GotoAsync ("/Home/Index");

			await page.FillAsync ("#message", "hello mvc");
			await page.ClickAndWaitAsync ("#send");

			// RedirectToAction ("Index") with every route value at its default is the application root.
			Assert.Equal (fixture.BaseAddress + "/", page.Url);
			Assert.Equal ("you said: hello mvc", await page.TextAsync ("#echo"));

			await page.ReloadAsync ();
			Assert.Equal (0, await page.Locator ("#echo").CountAsync ());
		});

		[Fact]
		public Task Content_and_json_results_render_in_the_browser () => fixture.RunAsync (async page => {
			IResponse plain = await page.GotoAsync ("/Home/Plain");
			Assert.StartsWith ("text/plain", plain.Headers ["content-type"]);
			Assert.Equal ("plain content result", await page.TextAsync ("body"));

			IResponse data = await page.GotoAsync ("/Home/Data");
			JsonElement json = JsonDocument.Parse (await data.TextAsync ()).RootElement;
			Assert.Equal (3, json.GetProperty ("total").GetInt32 ());
			Assert.Equal ("sprocket", json.GetProperty ("first").GetString ());
		});

		[Fact]
		public Task Unknown_widget_and_unknown_controller_are_404 () => fixture.RunAsync (async page => {
			// HttpNotFound () sets the status and writes no body - as on ASP.NET, where IIS supplied the
			// error page. Chromium turns an empty 404 into a navigation error, so ask without navigating.
			Assert.Equal (404, (await page.APIRequest.GetAsync ("/Home/Details/99")).Status);
			Assert.Equal (404, (await page.GotoAsync ("/Nope/Index")).Status);
		});

		[Theory]
		[InlineData ("/catalog", "catalog index")]
		[InlineData ("/catalog/new", "catalog new")]
		[InlineData ("/catalog/5", "catalog details 5")]
		[InlineData ("/catalog/5/reviews", "catalog reviews 5 page 1")]
		[InlineData ("/catalog/5/reviews/3", "catalog reviews 5 page 3")]
		[InlineData ("/catalog/search/bolts", "catalog search bolts")]
		[InlineData ("/catalog/named", "catalog named -> /catalog/named")]
		[InlineData ("/legacy-catalog", "catalog legacy")]
		[InlineData ("/catalog/files/a/b/c.txt", "catalog files a/b/c.txt")]
		[InlineData ("/catalog/async", "catalog async")]
		public Task Attribute_routes_resolve (string url, string expected) => fixture.RunAsync (async page => {
			IResponse response = await page.GotoAsync (url);

			Assert.Equal (200, response.Status);
			Assert.Equal (expected, (await page.TextAsync ("body")).Trim ());
		});

		[Fact]
		public Task Route_constraints_reject_non_matching_segments () => fixture.RunAsync (async page => {
			Assert.Equal (404, (await page.GotoAsync ("/catalog/search/ab")).Status);    // minlength(3)
			Assert.Equal (404, (await page.GotoAsync ("/catalog/search/abc1")).Status);  // alpha
		});

		[Fact]
		public Task HttpPost_attribute_route_answers_a_browser_post () => fixture.RunAsync (async page => {
			await page.GotoAsync ("/catalog");

			string body = await page.EvaluateAsync<string> ("() => fetch('/catalog/9', { method: 'POST' }).then(r => r.text())");

			Assert.Equal ("catalog update 9", body);
		});

		[Fact]
		public Task Async_view_awaits_inside_Razor () => fixture.RunAsync (async page => {
			await page.GotoAsync ("/async-view");

			Assert.Equal ("loaded:implicit", await page.TextAsync ("#implicit"));
			Assert.Equal ("count:7", await page.TextAsync ("#block"));
			Assert.Equal ("loaded:chained", await page.TextAsync ("#chained"));
		});

		[Fact]
		public Task Bundles_are_served_and_executed_by_the_browser () => fixture.RunAsync (async page => {
			await page.GotoAsync ("/Home/Bundles");

			Assert.Equal ("first", await page.EvaluateAsync<string> ("() => window.bundleFirst"));
			Assert.Equal ("second", await page.EvaluateAsync<string> ("() => window.bundleSecond"));
			Assert.Equal ("sans-serif", await page.EvalOnSelectorAsync<string> ("body", "e => getComputedStyle(e).fontFamily"));

			string scriptUrl = await page.TextAsync ("#script-url");
			IAPIResponse bundle = await page.APIRequest.GetAsync (scriptUrl);
			Assert.Equal (200, bundle.Status);
			Assert.Contains ("bundleFirst", await bundle.TextAsync ());
		});
	}
}
