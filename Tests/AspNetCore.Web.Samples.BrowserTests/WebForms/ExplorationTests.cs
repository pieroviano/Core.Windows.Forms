//
// Samples/WebFormsSample beyond its pages: every file and link, the .asmx services and their help
// pages, the AJAX application services, the pipeline's own contributions (Global.asax, the module, error
// pages) and the client scripts of the navigation controls.
//

using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Playwright;
using Xunit;

namespace WebFormsPort.SamplesBrowserTests.WebForms
{
	[Collection (WebFormsCollection.Name)]
	public class ExplorationTests
	{
		readonly WebFormsFixture fixture;

		public ExplorationTests (WebFormsFixture fixture)
		{
			this.fixture = fixture;
		}

		const string Fetch = @"async ([url, init]) => {
			const r = await fetch(url, init);
			return { status: r.status, type: r.headers.get('content-type') || '', body: await r.text() };
		}";

		[Fact]
		public Task Every_file_and_every_rendered_link_answers_as_ASP_NET_would ()
			=> fixture.RunAsync (page => SiteExplorer.ExploreAsync (fixture, page));

		[Fact]
		public Task Global_asax_and_the_http_module_decorate_every_response () => fixture.RunAsync (async page => {
			IResponse response = await page.GotoAsync ("/Default.aspx");

			Assert.Equal ("begin-request", response.Headers ["x-global-asax"]);
			Assert.Equal ("begin", response.Headers ["x-custom-module"]);
			Assert.Equal ("yes", response.Headers ["x-custom-module-saw-items"]);
		});

		[Fact]
		public Task Unknown_page_shows_the_resource_cannot_be_found_error () => fixture.RunAsync (async page => {
			IAPIResponse response = await page.APIRequest.GetAsync ("/NoSuchPage.aspx");

			Assert.Equal (404, response.Status);
			Assert.Contains ("The resource cannot be found", await response.TextAsync ());
		});

		[Fact]
		public Task Asmx_help_page_lists_the_operations_and_describes_each () => fixture.RunAsync (async page => {
			await page.GotoAsync ("/Calc.asmx");

			string help = await page.TextAsync ("body");
			Assert.Contains ("Add", help);
			Assert.Contains ("Echo", help);

			await page.GotoAsync ("/Calc.asmx?op=Add");
			Assert.Contains ("Add", await page.TextAsync ("body"));
		});

		[Fact]
		public Task Asmx_answers_http_post_from_localhost_with_xml () => fixture.RunAsync (async page => {
			await page.GotoAsync ("/Calc.asmx");

			// machine.config enables HttpPostLocalhost: the help page's test form posts like this.
			JsonElement add = await page.EvaluateAsync<JsonElement> (Fetch, new object [] { "/Calc.asmx/Add", new {
				method = "POST",
				headers = new HeaderMap { ["Content-Type"] = "application/x-www-form-urlencoded" },
				body = "a=2&b=3",
			} });

			Assert.Equal (200, add.GetProperty ("status").GetInt32 ());
			Assert.Contains ("xml", add.GetProperty ("type").GetString ());
			Assert.Contains (">5</int>", add.GetProperty ("body").GetString ());
		});

		[Fact]
		public Task Asmx_soap_call_from_the_browser () => fixture.RunAsync (async page => {
			await page.GotoAsync ("/Calc.asmx");

			JsonElement echo = await page.EvaluateAsync<JsonElement> (Fetch, new object [] { "/Calc.asmx", new {
				method = "POST",
				headers = new HeaderMap { ["Content-Type"] = "text/xml; charset=utf-8", ["SOAPAction"] = "\"http://webformsport.example/Echo\"" },
				body = "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"><s:Body>" +
				       "<Echo xmlns=\"http://webformsport.example/\"><text>from the browser</text></Echo></s:Body></s:Envelope>",
			} });

			Assert.Equal (200, echo.GetProperty ("status").GetInt32 ());
			Assert.Contains ("<EchoResult>echo: from the browser</EchoResult>", echo.GetProperty ("body").GetString ());
		});

		[Fact]
		public Task Script_service_methods_answer_json_for_post_get_and_session () => fixture.RunAsync (async page => {
			await page.GotoAsync ("/Ajax.aspx");
			await page.AddScriptTagAsync (new PageAddScriptTagOptions { Url = "/Ajax.asmx/js" });

			Assert.Equal (5, await page.EvaluateAsync<int> (
				"() => new Promise((ok, fail) => WebFormsSample.AjaxService.AddUp(2, 3, ok, e => fail(e.get_message())))"));
			// [ScriptMethod (UseHttpGet = true)]: the proxy issues a GET.
			Assert.Equal ("x:ok", await page.EvaluateAsync<string> (
				"() => new Promise((ok, fail) => WebFormsSample.AjaxService.Now('x', ok, e => fail(e.get_message())))"));
			int first = await page.EvaluateAsync<int> ("() => new Promise((ok, fail) => WebFormsSample.AjaxService.Bump(ok, e => fail(e.get_message())))");
			int second = await page.EvaluateAsync<int> ("() => new Promise((ok, fail) => WebFormsSample.AjaxService.Bump(ok, e => fail(e.get_message())))");
			Assert.Equal (first + 1, second);

			// The raw wire format: a JSON body in, {"d": ...} out.
			JsonElement raw = await page.EvaluateAsync<JsonElement> (Fetch, new object [] { "/Ajax.asmx/Greet", new {
				method = "POST",
				headers = new HeaderMap { ["Content-Type"] = "application/json; charset=utf-8" },
				body = "{\"name\":\"raw\"}",
			} });
			Assert.Equal (200, raw.GetProperty ("status").GetInt32 ());
			Assert.StartsWith ("application/json", raw.GetProperty ("type").GetString ());
			Assert.Equal ("{\"d\":\"hello raw (from a script service)\"}", raw.GetProperty ("body").GetString ());
		});

		[Fact]
		public Task Ajax_authentication_service_reports_an_anonymous_visitor () => fixture.RunAsync (async page => {
			await page.GotoAsync ("/Ajax.aspx");

			JsonElement loggedIn = await page.EvaluateAsync<JsonElement> (Fetch, new object [] { "/Authentication_JSON_AppService.axd/IsLoggedIn", new {
				method = "POST",
				headers = new HeaderMap { ["Content-Type"] = "application/json; charset=utf-8" },
				body = "{}",
			} });

			Assert.Equal (200, loggedIn.GetProperty ("status").GetInt32 ());
			Assert.Equal ("{\"d\":false}", loggedIn.GetProperty ("body").GetString ());
		});

		[Fact]
		public Task Unserialisable_view_state_is_refused_with_a_diagnostic_naming_the_type () => fixture.RunAsync (async page => {
			IAPIResponse response = await page.APIRequest.GetAsync ("/State.aspx?reject=1");

			Assert.Equal (500, response.Status);
			Assert.Contains ("System.Action", await response.TextAsync ());
		}, allowServerErrors: true);

		[Fact]
		public Task TreeView_expands_and_collapses_in_the_browser () => fixture.RunAsync (async page => {
			await page.GotoAsync ("/Features.aspx");

			ILocator child = page.Locator ("#tree a:text-is('Simple')");
			Assert.True (await child.IsVisibleAsync ());

			// The expand/collapse image link runs TreeView_ToggleExpand from TreeView.js: no postback.
			ILocator toggle = page.Locator ("#tree a[href*='TreeView_ToggleExpand']").First;
			await page.AssertNoNavigationAsync (() => toggle.ClickAsync ());
			Assert.False (await child.IsVisibleAsync ());

			await page.AssertNoNavigationAsync (() => toggle.ClickAsync ());
			Assert.True (await child.IsVisibleAsync ());
		});

		[Fact]
		public Task Theme_stylesheet_is_linked_and_served () => fixture.RunAsync (async page => {
			await page.GotoAsync ("/Features.aspx");

			string href = await page.GetAttributeAsync ("head link[href*='App_Themes/Basic/site.css']", "href");
			Assert.NotNull (href);
			Assert.Equal (200, (await page.APIRequest.GetAsync (href.StartsWith ("/") ? href : "/" + href)).Status);
		});

		[Fact]
		public Task Http_handler_reports_the_request_it_received () => fixture.RunAsync (async page => {
			await page.GotoAsync ("/Simple.aspx");

			JsonElement post = await page.EvaluateAsync<JsonElement> (Fetch, new object [] { "/hello.hello?q=posted", new { method = "POST", body = "x=1" } });

			string body = post.GetProperty ("body").GetString ();
			Assert.Contains ("Method:      POST", body);
			Assert.Contains ("Query q:     posted", body);
			Assert.Equal ("m2", (await page.APIRequest.GetAsync ("/hello.hello")).Headers ["x-webforms-port"]);
		});

		[Fact]
		public Task Events_page_raises_changed_events_and_click_from_one_postback () => fixture.RunAsync (async page => {
			await page.GotoAsync ("/Events.aspx");

			await page.FillAsync ("#who", "piero");
			await page.SelectOptionAsync ("#colour", "blue");
			await page.ClickAndWaitAsync ("#go");

			Assert.Equal ("TextChanged(who=piero) | SelectedIndexChanged(colour=blue) | Click(go)", await page.TextAsync ("#log"));
			Assert.Equal ("3", await page.TextAsync ("#count"));
		});

		[Fact]
		public Task Login_page_keeps_the_return_url_through_a_failed_attempt () => fixture.RunAsync (async page => {
			await page.GotoAsync ("/Secure/Secret.aspx");

			await page.FillAsync ("#user", "nobody");
			await page.FillAsync ("#pass", "nothing");
			await page.ClickAndWaitAsync ("#go");

			Assert.Equal ("bad credentials", await page.TextAsync ("#msg"));
			Assert.Contains ("ReturnUrl=", page.Url);
			Assert.DoesNotContain (await page.Context.CookiesAsync (), c => c.Name == ".WEBFORMSAUTH");
		});

		sealed class HeaderMap : System.Collections.Generic.Dictionary<string, string> { }
	}
}
