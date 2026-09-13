//
// Samples/SessionStateSample: mode="StateServer" backed by the host's IDistributedCache, driven through
// the page's query-string operations from a real browser (so the session cookie is the browser's).
//

using System.Threading.Tasks;
using Microsoft.Playwright;
using Xunit;

namespace WebFormsPort.SamplesBrowserTests
{
	[Collection (SessionStateCollection.Name)]
	public class SessionStateTests
	{
		readonly SessionStateFixture fixture;

		public SessionStateTests (SessionStateFixture fixture)
		{
			this.fixture = fixture;
		}

		static async Task<string> OpAsync (IPage page, string query)
		{
			await page.GotoAsync ("/Session.aspx?" + query);
			return await page.TextAsync ("p:has-text('result:')");
		}

		[Fact]
		public Task Values_persist_across_requests_in_one_browser () => fixture.RunAsync (async page => {
			Assert.Equal ("result: set colour", await OpAsync (page, "op=set&key=colour&value=blue"));
			Assert.Equal ("result: blue", await OpAsync (page, "op=get&key=colour"));
			Assert.Equal ("mode: StateServer", await page.TextAsync ("p:has-text('mode:')"));
			Assert.Equal ("hits: 2", await page.TextAsync ("p:has-text('hits:')"));
		});

		[Fact]
		public Task Types_round_trip_and_values_can_be_removed () => fixture.RunAsync (async page => {
			await OpAsync (page, "op=setint&key=n&value=41");
			Assert.Equal ("result: System.Int32", await OpAsync (page, "op=type&key=n"));

			await OpAsync (page, "op=setdate&key=d&value=2026-03-04");
			Assert.Equal ("result: System.DateTime", await OpAsync (page, "op=type&key=d"));

			await OpAsync (page, "op=remove&key=n");
			Assert.Equal ("result: (null)", await OpAsync (page, "op=get&key=n"));
		});

		[Fact]
		public Task Large_values_come_back_intact () => fixture.RunAsync (async page => {
			Assert.Equal ("result: wrote 20000", await OpAsync (page, "op=big&key=big"));
			Assert.Equal ("result: intact 20000", await OpAsync (page, "op=verifybig&key=big"));
		});

		[Fact]
		public Task Abandon_starts_a_new_session_on_the_next_request () => fixture.RunAsync (async page => {
			await OpAsync (page, "op=set&key=a&value=1");
			string id = await page.TextAsync ("p:has-text('id:')");

			await OpAsync (page, "op=abandon");
			Assert.Equal ("result: (null)", await OpAsync (page, "op=get&key=a"));
			Assert.NotEqual (id, await page.TextAsync ("p:has-text('id:')"));
		});

		[Fact]
		public Task Separate_browsers_have_separate_sessions () => fixture.RunAsync (async page => {
			await OpAsync (page, "op=set&key=who&value=first");

			await using IBrowserContext other = await fixture.Browser.NewContextAsync (new BrowserNewContextOptions { BaseURL = fixture.BaseAddress });
			IPage second = await other.NewPageAsync ();
			Assert.Equal ("result: (null)", await OpAsync (second, "op=get&key=who"));
		});

		[Fact]
		public Task Unserializable_value_is_refused_with_a_diagnostic () => fixture.RunAsync (async page => {
			IResponse response = await page.GotoAsync ("/Session.aspx?op=unserializable&key=bad");

			Assert.Equal (500, response.Status);
			Assert.Contains ("NotSerializable", await page.ContentAsync ());
		}, allowServerErrors: true);
	}
}
