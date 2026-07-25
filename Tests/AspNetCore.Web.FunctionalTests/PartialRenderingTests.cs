//
// Core.Web.Extensions: ScriptManager, UpdatePanel partial rendering and ScriptResource.axd.
//
// This is the stack that was silently dead when Core.Web.Extensions was not deployed - and because
// root-web.config registers ScriptModule in <httpModules>, which is instantiated at application
// start, the failure took down EVERY request rather than only the pages using an UpdatePanel. The
// assertions here go past "the page renders" and check that a partial postback really is partial.
//

using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Xunit;

namespace WebFormsPort.FunctionalTests
{
	[Collection (WebFormsCollection.Name)]
	public class PartialRenderingTests
	{
		// Ajax.aspx: <asp:ScriptManager ID="sm">, <asp:UpdatePanel ID="up">, <asp:Button ID="go">.
		const string ScriptManager = "sm";
		const string UpdatePanel = "up";
		const string Button = "go";

		readonly SampleAppFixture fixture;

		public PartialRenderingTests (SampleAppFixture fixture)
		{
			this.fixture = fixture;
		}

		[Fact]
		public async Task ScriptManager_emits_client_script_references ()
		{
			using HttpClient client = fixture.CreateClient ();

			string body = await client.GetStringAsync ("/Ajax.aspx");

			Assert.Contains ("ScriptResource.axd", body);
		}

		[Fact]
		public async Task ScriptResource_axd_serves_the_ajax_client_library ()
		{
			using HttpClient client = fixture.CreateClient ();

			string page = await client.GetStringAsync ("/Ajax.aspx");

			// The URL carries an encrypted resource id, so it has to come from the rendered page -
			// it cannot be constructed.
			Match match = Regex.Match (page, @"ScriptResource\.axd\?[^""]+");
			Assert.True (match.Success, "the page rendered no ScriptResource.axd reference");

			string url = WebUtility.HtmlDecode (match.Value);
			HttpResponseMessage response = await client.GetAsync ("/" + url);
			string script = await response.Content.ReadAsStringAsync ();

			Assert.Equal (HttpStatusCode.OK, response.StatusCode);
			Assert.Equal ("application/x-javascript", response.Content.Headers.ContentType.MediaType);
			// The embedded MS AJAX client library, served out of Core.Web.Extensions' resources.
			Assert.Contains ("Copyright (C) Microsoft Corporation", script);
			Assert.True (script.Length > 1000, "ScriptResource.axd returned a suspiciously small body");
		}

		[Theory]
		[InlineData ("/ScriptResource.axd")]
		[InlineData ("/ScriptResource.axd?d=")]
		[InlineData ("/ScriptResource.axd?d=not-a-real-resource")]
		[InlineData ("/WebResource.axd")]
		public async Task Resource_handler_rejects_a_malformed_request_with_404 (string path)
		{
			using HttpClient client = fixture.CreateClient ();

			// A missing or unparseable "d" is a bad request for a resource that does not exist, and the
			// handler already says so - it throws HttpException (404). It used to arrive as a 500
			// because the value was split before being null-checked, so the intended 404 was preceded
			// by a NullReferenceException.
			HttpResponseMessage response = await client.GetAsync (path);

			Assert.Equal (HttpStatusCode.NotFound, response.StatusCode);
		}

		[Fact]
		public async Task Async_postback_returns_a_delta_not_a_full_page ()
		{
			using HttpClient client = fixture.CreateClient ();

			string page = await client.GetStringAsync ("/Ajax.aspx");

			HttpResponseMessage response = await client.PostAsync ("/Ajax.aspx",
				WebForm.AsyncPostback (page, ScriptManager, UpdatePanel, Button,
						       (Button, "Update panel only")));
			string body = await response.Content.ReadAsStringAsync ();

			Assert.Equal (HttpStatusCode.OK, response.StatusCode);

			// The whole point of partial rendering: no document, just records.
			Assert.DoesNotContain ("<!DOCTYPE", body);
			Assert.DoesNotContain ("<html", body);

			List<WebForm.DeltaSegment> segments = WebForm.ParseDelta (body);
			Assert.Contains (segments, s => s.Type == "updatePanel" && s.Id == UpdatePanel);
		}

		[Fact]
		public async Task Delta_carries_only_the_updated_panel ()
		{
			using HttpClient client = fixture.CreateClient ();

			string page = await client.GetStringAsync ("/Ajax.aspx");

			// The label outside the panel is stamped once per FULL page load. Its text must not come
			// back in the delta - if it does, the server re-rendered the whole control tree and
			// "partial" rendering is partial in name only.
			string outside = Regex.Match (page, @"id=""outside"">([^<]*)<").Groups [1].Value;
			Assert.NotEmpty (outside);

			string body = await (await client.PostAsync ("/Ajax.aspx",
				WebForm.AsyncPostback (page, ScriptManager, UpdatePanel, Button,
						       (Button, "Update panel only")))).Content.ReadAsStringAsync ();

			Assert.DoesNotContain (outside, body);

			WebForm.DeltaSegment panel = Assert.Single (
				WebForm.ParseDelta (body).FindAll (s => s.Type == "updatePanel"));
			Assert.Contains ("partial update #1", panel.Content);
		}

		[Fact]
		public async Task View_state_advances_across_successive_partial_postbacks ()
		{
			using HttpClient client = fixture.CreateClient ();

			string page = await client.GetStringAsync ("/Ajax.aspx");

			string first = await (await client.PostAsync ("/Ajax.aspx",
				WebForm.AsyncPostback (page, ScriptManager, UpdatePanel, Button,
						       (Button, "Update panel only")))).Content.ReadAsStringAsync ();
			Assert.Contains ("partial update #1", first);

			// The updated view state comes back as a hiddenField RECORD, not as markup, so the next
			// postback has to be built from the delta rather than by re-scraping a page.
			string second = await (await client.PostAsync ("/Ajax.aspx",
				WebForm.AsyncPostbackFromDelta (first, ScriptManager, UpdatePanel, Button,
								(Button, "Update panel only")))).Content.ReadAsStringAsync ();
			Assert.Contains ("partial update #2", second);

			string third = await (await client.PostAsync ("/Ajax.aspx",
				WebForm.AsyncPostbackFromDelta (second, ScriptManager, UpdatePanel, Button,
								(Button, "Update panel only")))).Content.ReadAsStringAsync ();
			Assert.Contains ("partial update #3", third);
		}

		[Fact]
		public async Task Delta_returns_refreshed_hidden_fields ()
		{
			using HttpClient client = fixture.CreateClient ();

			string page = await client.GetStringAsync ("/Ajax.aspx");

			string body = await (await client.PostAsync ("/Ajax.aspx",
				WebForm.AsyncPostback (page, ScriptManager, UpdatePanel, Button,
						       (Button, "Update panel only")))).Content.ReadAsStringAsync ();

			Dictionary<string, string> hidden = WebForm.DeltaHiddenFields (body);

			Assert.True (hidden.ContainsKey ("__VIEWSTATE"), "delta carried no __VIEWSTATE");
			Assert.True (hidden.ContainsKey ("__EVENTVALIDATION"), "delta carried no __EVENTVALIDATION");
			Assert.NotEqual (WebForm.HiddenFields (page) ["__VIEWSTATE"], hidden ["__VIEWSTATE"]);
		}

		[Fact]
		public async Task Ordinary_postback_to_the_same_page_still_renders_the_whole_document ()
		{
			using HttpClient client = fixture.CreateClient ();

			string page = await client.GetStringAsync ("/Ajax.aspx");

			// Without __ASYNCPOST the same button must produce a normal full-page render. This is the
			// no-JavaScript fallback, and it is what proves the delta above was a real behavioural
			// difference rather than the only thing this page can do.
			string body = await (await client.PostAsync ("/Ajax.aspx",
				WebForm.Postback (page, (Button, "Update panel only")))).Content.ReadAsStringAsync ();

			Assert.Contains ("<!DOCTYPE", body);
			Assert.Contains ("<html", body);
			Assert.Contains ("partial update #1", body);
		}
	}
}
