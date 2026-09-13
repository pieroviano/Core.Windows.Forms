//
// The WebFormsSample pages that existed before Events/: navigated and used the way a visitor would.
//

using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Playwright;
using Xunit;

namespace WebFormsPort.SamplesBrowserTests.WebForms
{
	[Collection (WebFormsCollection.Name)]
	public class SitePagesTests
	{
		readonly WebFormsFixture fixture;

		public SitePagesTests (WebFormsFixture fixture)
		{
			this.fixture = fixture;
		}

		[Fact]
		public Task Default_page_greets_after_a_valid_postback () => fixture.RunAsync (async page => {
			await page.GotoAsync ("/Default.aspx");

			Assert.Equal ("hello from Page_Load", await page.TextAsync ("#message"));
			Assert.Contains ("42", await page.ContentAsync ());
			Assert.Equal ("0", await page.TextAsync ("#counter"));

			await page.FillAsync ("#who", "piero & \"co\"");
			await page.ClickAndWaitAsync ("#greet");

			// Server.HtmlEncode: the characters arrive as text, not as markup.
			Assert.Equal ("Hello, piero & \"co\"!", await page.TextAsync ("#greeting"));
			Assert.Equal ("hello again (postback)", await page.TextAsync ("#message"));
			Assert.Equal ("1", await page.TextAsync ("#counter"));

			// Bound only on the first request: the grid rows come back from view state.
			Assert.Equal ("flange", await page.Locator ("#grid tr").Nth (2).Locator ("td").Nth (1).InnerTextAsync ());
		});

		[Fact]
		public Task Posting_markup_is_refused_by_request_validation () => fixture.RunAsync (async page => {
			await page.GotoAsync ("/Default.aspx");

			await page.FillAsync ("#who", "<b>piero</b>");
			IResponse response = await page.RunAndWaitForResponseAsync (
				() => page.ClickAsync ("#greet"),
				r => r.Request.IsNavigationRequest && r.Request.Method == "POST" && r.Url.StartsWith (fixture.BaseAddress));

			// ASP.NET 4 validates form input before the page runs (HttpRequest.ValidateInput, called from
			// the generated FrameworkInitialize): HttpRequestValidationException, a 500.
			Assert.Equal (500, response.Status);
			await page.WaitForLoadStateAsync ();
			Assert.Contains ("potentially dangerous Request.Form value", await page.ContentAsync ());
		}, allowServerErrors: true);

		[Fact]
		public Task Default_page_blocks_an_empty_name_in_the_browser () => fixture.RunAsync (async page => {
			await page.GotoAsync ("/Default.aspx");

			await page.AssertNoNavigationAsync (() => page.ClickAsync ("#greet"));

			Assert.True (await page.IsVisibleAsync ("#whoRequired"));
			Assert.Equal ("0", await page.TextAsync ("#counter"));
		});

		[Fact]
		public Task Default_page_validates_on_the_server_without_script () => fixture.RunAsync (async page => {
			await page.GotoAsync ("/Default.aspx");

			await page.ClickAndWaitAsync ("#greet");

			Assert.Equal ("(validation failed: a name is required)", await page.TextAsync ("#greeting"));
			Assert.Equal ("1", await page.TextAsync ("#counter"));
		}, javaScriptEnabled: false);

		[Fact]
		public Task Content_page_composes_master_user_controls_and_App_Code () => fixture.RunAsync (async page => {
			await page.GotoAsync ("/Content.aspx");

			Assert.Equal ("Content page", await page.TitleAsync ());
			Assert.Equal ("[master header]", await page.TextAsync ("#chrome-header"));
			Assert.Equal (2, await page.Locator (".widget").CountAsync ());
			Assert.Contains ("shouted=SECOND!", await page.Locator (".widget").Nth (1).InnerTextAsync ());
			Assert.Contains ("APP_CODE WORKS!", await page.TextAsync ("body"));
		});

		[Fact]
		public Task Features_page_applies_theme_resources_and_output_cache () => fixture.RunAsync (async page => {
			await page.GotoAsync ("/Features.aspx");

			string stamp = await page.TextAsync ("#stamp");
			Assert.Equal ("rgb(0, 128, 0)", await page.EvalOnSelectorAsync<string> ("#themed", "e => getComputedStyle(e).color"));
			Assert.Equal ("700", await page.EvalOnSelectorAsync<string> ("#themed", "e => getComputedStyle(e).fontWeight"));
			Assert.False (String.IsNullOrWhiteSpace (await page.TextAsync ("#res")));
			Assert.Contains ("alpha x 1", await page.TextAsync ("#list"));

			await page.ReloadAsync ();
			Assert.Equal (stamp, await page.TextAsync ("#stamp"));
		});

		[Fact]
		public Task Sitemap_TreeView_navigates_to_a_child_page () => fixture.RunAsync (async page => {
			await page.GotoAsync ("/Features.aspx");

			await page.ClickAndWaitAsync ("#tree a:text-is('Simple')");

			Assert.EndsWith ("/Simple.aspx", page.Url);
			Assert.Equal ("Simple page", await page.TextAsync ("h1"));
		});

		[Fact]
		public Task Sitemap_Menu_navigates_to_a_child_page () => fixture.RunAsync (async page => {
			await page.GotoAsync ("/Features.aspx");

			// The child items are a flyout: hovering the root shows them (Menu's client script).
			await page.HoverAsync ("#menu a:text-is('Home')");
			await page.ClickAndWaitAsync ("#menu a:text-is('Content')");

			Assert.EndsWith ("/Content.aspx", page.Url);
		});

		[Fact]
		public Task Locale_page_formats_in_german () => fixture.RunAsync (async page => {
			await page.GotoAsync ("/Locale.aspx");

			Assert.Equal ("de-DE / de-DE", await page.TextAsync ("#culture"));
			Assert.Equal ("1.234,50", await page.TextAsync ("#money"));
			Assert.Equal ("04.03.2026", await page.TextAsync ("#when"));
			Assert.Equal (3, await page.Locator ("#f li").CountAsync ());
			Assert.Contains ("FormView: alpha", await page.TextAsync ("#fv"));
		});

		[Fact]
		public Task Protected_page_redirects_to_login_and_back_after_signing_in () => fixture.RunAsync (async page => {
			await page.GotoAsync ("/Secure/Secret.aspx");

			Assert.Contains ("/Login.aspx", page.Url);
			Assert.Contains ("ReturnUrl=", page.Url);

			await page.FillAsync ("#user", "piero");
			await page.FillAsync ("#pass", "wrong");
			await page.ClickAndWaitAsync ("#go");
			Assert.Equal ("bad credentials", await page.TextAsync ("#msg"));

			await page.FillAsync ("#pass", "secret");
			await page.ClickAndWaitAsync ("#go");

			Assert.EndsWith ("/Secure/Secret.aspx", page.Url);
			Assert.Equal ("authenticated=True name=piero", await page.TextAsync ("#who"));

			// The ticket is a cookie: a second visit goes straight in.
			await page.GotoAsync ("/Secure/Secret.aspx");
			Assert.EndsWith ("/Secure/Secret.aspx", page.Url);
		});

		[Fact]
		public Task Redirect_lands_on_the_target_and_Transfer_keeps_the_url () => fixture.RunAsync (async page => {
			await page.GotoAsync ("/Redirect.aspx");
			Assert.EndsWith ("/Simple.aspx", page.Url);
			Assert.Equal ("Simple page", await page.TextAsync ("h1"));

			await page.GotoAsync ("/Redirect.aspx?to=transfer");
			Assert.Contains ("/Redirect.aspx", page.Url);
			Assert.Equal ("Simple page", await page.TextAsync ("h1"));
		});

		[Fact]
		public Task Session_counts_visits_per_browser () => fixture.RunAsync (async page => {
			await page.GotoAsync ("/Session.aspx");
			Assert.StartsWith ("visits=1 ", await page.TextAsync ("#info"));

			await page.ReloadAsync ();
			string info = await page.TextAsync ("#info");
			Assert.StartsWith ("visits=2 ", info);
			Assert.Contains ("isNew=False", info);
			Assert.Contains ("appStarted=True", info);
		});

		[Fact]
		public Task State_page_round_trips_a_custom_type_through_view_state_and_session () => fixture.RunAsync (async page => {
			await page.GotoAsync ("/State.aspx");
			Assert.Equal ("viewstate: owner=piero items=1 total=4.25 | session items=1", await page.TextAsync ("#info"));

			await page.ClickAndWaitAsync ("#go");
			await page.ClickAndWaitAsync ("#go");

			Assert.Equal ("viewstate: owner=piero items=3 total=12.75 | session items=3", await page.TextAsync ("#info"));
		});

		[Fact]
		public Task Upload_posts_a_file_and_a_field_together () => fixture.RunAsync (async page => {
			await page.GotoAsync ("/Upload.aspx");

			await page.SetInputFilesAsync ("#up", new FilePayload {
				Name = "note.txt",
				MimeType = "text/plain",
				Buffer = System.Text.Encoding.UTF8.GetBytes ("hello upload"),
			});
			await page.FillAsync ("#note", "with a field");
			await page.ClickAndWaitAsync ("#go");

			string result = await page.TextAsync ("#result");
			Assert.StartsWith ("name=note.txt len=12 type=text/plain sha=", result);
			Assert.EndsWith (" field=with a field", result);
		});

		[Fact]
		public Task Upload_without_a_file_reports_none () => fixture.RunAsync (async page => {
			await page.GotoAsync ("/Upload.aspx");

			await page.ClickAndWaitAsync ("#go");

			Assert.StartsWith ("no file", await page.TextAsync ("#result"));
		});

		[Fact]
		public Task Page_methods_are_callable_through_the_generated_client_proxy () => fixture.RunAsync (async page => {
			await page.GotoAsync ("/PageMethods.aspx");
			Assert.Equal ("page lifecycle ran", await page.TextAsync ("#lifecycle"));

			string echo = await page.EvaluateAsync<string> (
				"() => new Promise((ok, fail) => PageMethods.Echo('hi', ok, e => fail(e.get_message())))");
			int product = await page.EvaluateAsync<int> (
				"() => new Promise((ok, fail) => PageMethods.Multiply(6, 7, ok, e => fail(e.get_message())))");
			int first = await page.EvaluateAsync<int> ("() => new Promise((ok, fail) => PageMethods.Visit(ok, fail))");
			int second = await page.EvaluateAsync<int> ("() => new Promise((ok, fail) => PageMethods.Visit(ok, fail))");

			Assert.Equal ("page method echo: hi", echo);
			Assert.Equal (42, product);
			Assert.Equal (first + 1, second);
		});

		[Fact]
		public Task Script_service_proxy_is_served_and_callable () => fixture.RunAsync (async page => {
			await page.GotoAsync ("/Ajax.aspx");
			await page.AddScriptTagAsync (new PageAddScriptTagOptions { Url = "/Ajax.asmx/js" });

			string greeting = await page.EvaluateAsync<string> (
				"() => new Promise((ok, fail) => WebFormsSample.AjaxService.Greet('browser', ok, e => fail(e.get_message())))");
			JsonElement widget = await page.EvaluateAsync<JsonElement> (
				"() => new Promise((ok, fail) => WebFormsSample.AjaxService.Describe(7, ok, e => fail(e.get_message())))");
			string failure = await page.EvaluateAsync<string> (
				"() => new Promise(ok => WebFormsSample.AjaxService.Boom(() => ok('no error'), e => ok(e.get_message())))");

			Assert.Equal ("hello browser (from a script service)", greeting);
			Assert.Equal ("widget-7", widget.GetProperty ("Name").GetString ());
			Assert.Equal ("deliberate failure", failure);
		}, allowServerErrors: true);   // Boom throws by design: the service answers 500 with the error as JSON

		[Fact]
		public Task Streaming_page_and_handler_render_in_the_browser () => fixture.RunAsync (async page => {
			await page.GotoAsync ("/Streaming.aspx");
			Assert.Equal ("chunk0\nchunk1\nchunk2", (await page.TextAsync ("body")).Trim ().Replace ("\r", ""));

			await page.GotoAsync ("/hello.hello?q=browser");
			string hello = await page.TextAsync ("body");
			Assert.Contains ("Query q:     browser", hello);
			Assert.Contains ("seen-by-third-party-library", hello);
		});

		[Fact]
		public Task Download_handler_delivers_a_file_to_the_browser () => fixture.RunAsync (async page => {
			await page.GotoAsync ("/Simple.aspx");

			IDownload download = await page.RunAndWaitForDownloadAsync (
				() => page.EvaluateAsync ("() => { const a = document.createElement('a'); a.href = '/Download.ashx?size=5000'; a.download = 'x.bin'; document.body.appendChild(a); a.click(); }"));

			string path = await download.PathAsync ();
			byte [] bytes = File.ReadAllBytes (path);
			Assert.Equal (5000, bytes.Length);
			Assert.Equal ((byte) (4999 % 251), bytes [4999]);
		});

		[Fact]
		public Task Every_link_on_the_events_index_opens_a_healthy_page () => fixture.RunAsync (async page => {
			await page.GotoAsync ("/Events/Default.aspx");
			var hrefs = await page.EvalOnSelectorAllAsync<string []> ("#pages a", "links => links.map(a => a.href)");

			Assert.Equal (10, hrefs.Length);
			foreach (string href in hrefs) {
				IResponse response = await page.GotoAsync (href);
				Assert.True (response.Ok, href + " answered " + response.Status);
				Assert.False (String.IsNullOrWhiteSpace (await page.TitleAsync ()), href + " has no title");
			}
		});
	}
}
