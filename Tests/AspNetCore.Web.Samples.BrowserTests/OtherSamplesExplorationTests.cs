//
// Every other sample, explored: each file and rendered link against ASP.NET's rules (SiteExplorer), a
// crawl from the entry points of the route-based applications, and the handlers, services and behaviours
// the per-sample suites do not reach through their pages.
//

using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Playwright;
using Xunit;

namespace WebFormsPort.SamplesBrowserTests
{
	static class Browser
	{
		public const string Fetch = @"async ([url, init]) => {
			const r = await fetch(url, init);
			return { status: r.status, type: r.headers.get('content-type') || '', body: await r.text() };
		}";

		public static async Task<(int Status, string Type, string Body)> FetchAsync (this IPage page, string url, object init)
		{
			JsonElement r = await page.EvaluateAsync<JsonElement> (Fetch, new object [] { url, init });
			return (r.GetProperty ("status").GetInt32 (), r.GetProperty ("type").GetString (), r.GetProperty ("body").GetString ());
		}
	}

	sealed class Headers : System.Collections.Generic.Dictionary<string, string> { }

	[Collection (WebFormsVBCollection.Name)]
	public class WebFormsVBExplorationTests
	{
		readonly WebFormsVBFixture fixture;

		public WebFormsVBExplorationTests (WebFormsVBFixture fixture)
		{
			this.fixture = fixture;
		}

		[Fact]
		public Task Every_file_and_every_rendered_link_answers_as_ASP_NET_would ()
			=> fixture.RunAsync (page => SiteExplorer.ExploreAsync (fixture, page));

		[Fact]
		public Task VB_Global_asax_decorates_every_response () => fixture.RunAsync (async page => {
			IResponse response = await page.GotoAsync ("/Default.aspx");

			Assert.Equal ("begin-request", response.Headers ["x-global-asax-vb"]);
		});

		[Fact]
		public Task Bad_credentials_are_reported_and_issue_no_ticket () => fixture.RunAsync (async page => {
			await page.GotoAsync ("/Login.aspx");

			await page.FillAsync ("#user", "piero");
			await page.FillAsync ("#pass", "wrong");
			await page.ClickAndWaitAsync ("#go");

			Assert.Equal ("bad credentials", await page.TextAsync ("#msg"));
			Assert.DoesNotContain (await page.Context.CookiesAsync (), c => c.Name == ".VBWEBFORMSAUTH");

			await page.GotoAsync ("/Secure/Secret.aspx");
			Assert.Contains ("/Login.aspx", page.Url);
		});

		[Fact]
		public Task Postbacks_count_through_view_state_and_keep_the_grid () => fixture.RunAsync (async page => {
			await page.GotoAsync ("/Default.aspx");

			await page.FillAsync ("#who", "a");
			await page.ClickAndWaitAsync ("#greet");
			await page.ClickAndWaitAsync ("#greet");

			Assert.Equal ("2", await page.TextAsync ("#counter"));
			Assert.Equal ("hello again (postback)", await page.TextAsync ("#message"));
			Assert.Equal (4, await page.Locator ("#grid tr").CountAsync ());
		});
	}

	[Collection (MvcCollection.Name)]
	public class MvcExplorationTests
	{
		readonly MvcFixture fixture;

		public MvcExplorationTests (MvcFixture fixture)
		{
			this.fixture = fixture;
		}

		[Fact]
		public Task Every_file_answers_as_ASP_NET_would () => fixture.RunAsync (page => SiteExplorer.ExploreAsync (fixture, page));

		[Fact]
		public Task Every_page_reachable_from_the_home_page_is_healthy ()
			=> fixture.RunAsync (page => SiteExplorer.CrawlAsync (fixture, page, 30, "/", "/Home/Bundles", "/async-view", "/catalog"));

		[Fact]
		public Task Details_page_carries_its_own_title_and_the_layout () => fixture.RunAsync (async page => {
			await page.GotoAsync ("/Home/Details/3");

			Assert.Equal ("Details - MVC on Kestrel", await page.TitleAsync ());
			Assert.Equal ("3 / grommet / 3.75", await page.TextAsync ("#detail"));
			Assert.Equal ("[layout footer]", await page.TextAsync ("#chrome-footer"));
			Assert.Equal (0, await page.Locator ("#section-footer").CountAsync ());
		});

		[Fact]
		public Task HttpPost_action_is_not_reachable_with_get () => fixture.RunAsync (async page => {
			Assert.Equal (404, (await page.APIRequest.GetAsync ("/Home/Echo?message=x")).Status);
		});

		[Fact]
		public Task Non_numeric_id_fails_model_binding_with_a_server_error () => fixture.RunAsync (async page => {
			IAPIResponse response = await page.APIRequest.GetAsync ("/Home/Details/abc");

			// MVC 4: ControllerActionInvoker refuses a null for the non-nullable int parameter.
			Assert.Equal (500, response.Status);
			Assert.Contains ("parameters dictionary contains a null entry", await response.TextAsync ());
		}, allowServerErrors: true);

		[Fact]
		public Task Style_bundle_and_wildcard_script_bundle_concatenate_their_inputs () => fixture.RunAsync (async page => {
			await page.GotoAsync ("/Home/Bundles");

			IAPIResponse css = await page.APIRequest.GetAsync (await page.TextAsync ("#style-url"));
			Assert.Equal (200, css.Status);
			Assert.StartsWith ("text/css", css.Headers ["content-type"]);
			string styles = await css.TextAsync ();
			Assert.Contains ("sans-serif", styles);
			Assert.Contains ("@media print", styles);

			string all = await page.EvalOnSelectorAllAsync<string> ("script[src]", "s => s.map(e => e.getAttribute('src')).join('|')");
			Assert.Contains ("bundles/all-scripts", all);
		});

		[Fact]
		public Task Views_are_never_served_directly () => fixture.RunAsync (async page => {
			Assert.Equal (404, (await page.APIRequest.GetAsync ("/Views/Home/Index.cshtml")).Status);
			Assert.Equal (404, (await page.APIRequest.GetAsync ("/Views/Shared/_Layout.cshtml")).Status);
		});
	}

	[Collection (WebPagesCollection.Name)]
	public class WebPagesExplorationTests
	{
		readonly WebPagesFixture fixture;

		public WebPagesExplorationTests (WebPagesFixture fixture)
		{
			this.fixture = fixture;
		}

		[Fact]
		public Task Every_file_answers_as_ASP_NET_would () => fixture.RunAsync (page => SiteExplorer.ExploreAsync (fixture, page));

		[Fact]
		public Task Extensionless_urls_resolve_to_their_pages () => fixture.RunAsync (async page => {
			await page.GotoAsync ("/Default");
			Assert.Equal ("Web Pages", await page.TextAsync ("#heading"));
		});

		[Fact]
		public Task A_post_to_a_page_sees_the_method_and_an_empty_field () => fixture.RunAsync (async page => {
			await page.GotoAsync ("/Helpers");

			var helpers = await page.FetchAsync ("/Helpers", new { method = "POST", body = "" });
			Assert.Equal (200, helpers.Status);
			Assert.Contains ("<p id=\"method\">POST</p>", helpers.Body);

			await page.GotoAsync ("/");
			await page.ClickAndWaitAsync ("#send");
			Assert.Equal ("Hello, !", await page.TextAsync ("#greeting"));
		});
	}

	[Collection (WebApiCollection.Name)]
	public class WebApiExplorationTests
	{
		readonly WebApiFixture fixture;

		public WebApiExplorationTests (WebApiFixture fixture)
		{
			this.fixture = fixture;
		}

		[Fact]
		public Task Every_file_answers_as_ASP_NET_would () => fixture.RunAsync (page => SiteExplorer.ExploreAsync (fixture, page));

		[Fact]
		public Task Unsupported_verbs_are_405 () => fixture.RunAsync (async page => {
			await page.GotoAsync ("/api/widgets/1");

			Assert.Equal (405, (await page.FetchAsync ("/api/widgets/1", new { method = "PUT", headers = new Headers { ["Content-Type"] = "application/json" }, body = "{}" })).Status);
			Assert.Equal (405, (await page.FetchAsync ("/api/widgets/1", new { method = "DELETE" })).Status);
		});

		[Fact]
		public Task Xml_is_negotiated_and_accepted () => fixture.RunAsync (async page => {
			await page.GotoAsync ("/api/widgets/1");

			var xml = await page.FetchAsync ("/api/widgets/1", new { headers = new Headers { ["Accept"] = "application/xml" } });
			Assert.StartsWith ("application/xml", xml.Type);
			Assert.Contains ("<Name>sprocket</Name>", xml.Body);

			var created = await page.FetchAsync ("/api/widgets", new {
				method = "POST",
				headers = new Headers { ["Content-Type"] = "application/xml", ["Accept"] = "application/json" },
				body = "<Widget xmlns=\"http://schemas.datacontract.org/2004/07/WebApiSample.Controllers\"><Name>xml-widget</Name><Price>2</Price></Widget>",
			});
			Assert.Equal (201, created.Status);
			Assert.Contains ("\"Name\":\"xml-widget\"", created.Body);
		});

		[Fact]
		public Task Unknown_controller_is_404 () => fixture.RunAsync (async page => {
			Assert.Equal (404, (await page.APIRequest.GetAsync ("/api/gadgets")).Status);
		});
	}

	[Collection (WcfCollection.Name)]
	public class WcfExplorationTests
	{
		readonly WcfFixture fixture;

		public WcfExplorationTests (WcfFixture fixture)
		{
			this.fixture = fixture;
		}

		static string Envelope (string body) =>
			"<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"><s:Body>" + body + "</s:Body></s:Envelope>";

		[Fact]
		public Task Every_file_and_every_rendered_link_answers_as_ASP_NET_would ()
			=> fixture.RunAsync (page => SiteExplorer.ExploreAsync (fixture, page));

		[Fact]
		public Task Both_services_publish_metadata_at_their_own_address () => fixture.RunAsync (async page => {
			await page.GotoAsync ("/Default.aspx");
			Assert.Equal ("Services: /Echo.svc, /Api/Calculator.svc", await page.TextAsync ("#hosted"));

			IAPIResponse wsdl = await page.APIRequest.GetAsync ("/Api/Calculator.svc?wsdl");
			Assert.Equal (200, wsdl.Status);
			string body = await wsdl.TextAsync ();
			Assert.Contains ("wsdl:definitions", body);
			Assert.Contains ("Multiply", body);
		});

		[Fact]
		public Task Every_operation_answers_and_an_unknown_action_faults () => fixture.RunAsync (async page => {
			await page.GotoAsync ("/Default.aspx");

			var add = await page.FetchAsync ("/Echo.svc", new {
				method = "POST",
				headers = new Headers { ["Content-Type"] = "text/xml; charset=utf-8", ["SOAPAction"] = "http://webformsport.example/IEcho/Add" },
				body = Envelope ("<Add xmlns=\"http://webformsport.example/\"><a>40</a><b>2</b></Add>"),
			});
			Assert.Equal (200, add.Status);
			Assert.Contains ("<AddResult>42</AddResult>", add.Body);

			var fault = await page.FetchAsync ("/Echo.svc", new {
				method = "POST",
				headers = new Headers { ["Content-Type"] = "text/xml; charset=utf-8", ["SOAPAction"] = "http://webformsport.example/IEcho/Nope" },
				body = Envelope ("<Nope xmlns=\"http://webformsport.example/\" />"),
			});
			Assert.Equal (500, fault.Status);
			Assert.Contains ("Fault", fault.Body);
		}, allowServerErrors: true);   // a SOAP fault travels as a 500 by definition
	}

	[Collection (SessionStateCollection.Name)]
	public class SessionStateExplorationTests
	{
		readonly SessionStateFixture fixture;

		public SessionStateExplorationTests (SessionStateFixture fixture)
		{
			this.fixture = fixture;
		}

		[Fact]
		public Task Every_file_and_every_rendered_link_answers_as_ASP_NET_would ()
			=> fixture.RunAsync (page => SiteExplorer.ExploreAsync (fixture, page));

		[Fact]
		public Task Session_cookie_is_http_only_and_the_id_is_stable () => fixture.RunAsync (async page => {
			await page.GotoAsync ("/Session.aspx");
			string id = await page.TextAsync ("p:has-text('id:')");
			await page.ReloadAsync ();

			Assert.Equal (id, await page.TextAsync ("p:has-text('id:')"));
			Assert.Equal ("result: none", await page.TextAsync ("p:has-text('result:')"));

			var cookie = (await page.Context.CookiesAsync ()).Single (c => c.Name == "ASP.NET_SessionId");
			Assert.True (cookie.HttpOnly);
			Assert.Equal ("id: " + cookie.Value, id);
		});

		[Fact]
		public Task Count_reflects_every_stored_key () => fixture.RunAsync (async page => {
			await page.GotoAsync ("/Session.aspx?op=set&key=a&value=1");
			await page.GotoAsync ("/Session.aspx?op=set&key=b&value=2");
			await page.GotoAsync ("/Session.aspx?op=count");

			// "hits" is written on every request, so the page's own bookkeeping is one of the keys.
			Assert.Equal ("result: 3", await page.TextAsync ("p:has-text('result:')"));
		});
	}

	[Collection (DynamicDataCollection.Name)]
	public class DynamicDataExplorationTests
	{
		readonly DynamicDataFixture fixture;

		public DynamicDataExplorationTests (DynamicDataFixture fixture)
		{
			this.fixture = fixture;
		}

		static ILocator RowWith (IPage page, string grid, string text) => page.Locator (grid + " tr", new PageLocatorOptions { HasText = text });

		[Fact]
		public Task Every_file_answers_as_ASP_NET_would () => fixture.RunAsync (page => SiteExplorer.ExploreAsync (fixture, page));

		[Fact]
		public Task Every_page_reachable_from_the_table_index_is_healthy ()
			=> fixture.RunAsync (page => SiteExplorer.CrawlAsync (fixture, page, 20, "/Default.aspx"));

		[Fact]
		public Task Details_pages_resolve_keys_for_every_table () => fixture.RunAsync (async page => {
			await page.GotoAsync ("/Categories/Details.aspx?Id=1");
			Assert.Equal ("Categories", await page.TextAsync ("#TableName"));
			Assert.Contains ("Hardware", await page.TextAsync ("#Details"));

			await page.GotoAsync ("/Products/Details.aspx?Id=abc");
			Assert.True (await page.IsVisibleAsync ("#NotFound"));
		});

		[Fact]
		public Task Unknown_scaffold_action_is_404 () => fixture.RunAsync (async page => {
			Assert.Equal (404, (await page.APIRequest.GetAsync ("/Products/Bogus.aspx")).Status);
		});

		[Fact]
		public Task Boolean_edit_template_updates_the_checkbox_value () => fixture.RunAsync (async page => {
			await page.GotoAsync ("/Products/List.aspx");

			await page.PostBackAsync (() => RowWith (page, "#Grid", "Anvil").Locator ("a:text-is('Edit')").ClickAsync ());
			ILocator editRow = page.Locator ("#Grid tr", new PageLocatorOptions { Has = page.Locator ("input[type=text][value='Anvil']") });
			ILocator box = editRow.Locator ("input[type=checkbox]");
			Assert.True (await box.IsEnabledAsync ());
			await box.CheckAsync ();
			await page.PostBackAsync (() => editRow.Locator ("a:text-is('Update')").ClickAsync ());

			Assert.True (await RowWith (page, "#Grid", "Anvil").Locator ("input[type=checkbox]").IsCheckedAsync ());

			// Put it back.
			await page.PostBackAsync (() => RowWith (page, "#Grid", "Anvil").Locator ("a:text-is('Edit')").ClickAsync ());
			await editRow.Locator ("input[type=checkbox]").UncheckAsync ();
			await page.PostBackAsync (() => editRow.Locator ("a:text-is('Update')").ClickAsync ());
			Assert.False (await RowWith (page, "#Grid", "Anvil").Locator ("input[type=checkbox]").IsCheckedAsync ());
		});

		[Fact]
		public Task LinqDataSource_grid_updates_a_row () => fixture.RunAsync (async page => {
			await page.GotoAsync ("/Products.aspx");

			await page.PostBackAsync (() => RowWith (page, "#ProductGrid", "Anvil").Locator ("a:text-is('Edit')").ClickAsync ());
			ILocator price = page.Locator ("#ProductGrid input[type=text][value='49.95']");
			Assert.Equal (1, await price.CountAsync ());

			await price.FillAsync ("51.25");
			await page.PostBackAsync (() => page.Locator ("#ProductGrid a:text-is('Update')").ClickAsync ());
			Assert.Contains ("51.25", await RowWith (page, "#ProductGrid", "Anvil").InnerTextAsync ());

			await page.PostBackAsync (() => RowWith (page, "#ProductGrid", "Anvil").Locator ("a:text-is('Edit')").ClickAsync ());
			await page.Locator ("#ProductGrid input[type=text][value='51.25']").FillAsync ("49.95");
			await page.PostBackAsync (() => page.Locator ("#ProductGrid a:text-is('Update')").ClickAsync ());
			Assert.Contains ("49.95", await RowWith (page, "#ProductGrid", "Anvil").InnerTextAsync ());
		});
	}
}
