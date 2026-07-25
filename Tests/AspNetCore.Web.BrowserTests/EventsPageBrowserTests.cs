//
// Events.aspx driven by a real browser.
//
// The page raises three server-side events from ONE postback. Neither input sets AutoPostBack, so
// nothing is submitted until the button is pressed; the changed events are then raised during
// RaiseChangedEvents and the click after them, which is the order asserted below.
//

using System.Threading.Tasks;
using Microsoft.Playwright;
using Xunit;

namespace WebFormsPort.BrowserTests
{
	[Collection (BrowserCollection.Name)]
	public class EventsPageBrowserTests
	{
		readonly BrowserAppFixture fixture;

		public EventsPageBrowserTests (BrowserAppFixture fixture)
		{
			this.fixture = fixture;
		}

		[Fact]
		public async Task Changing_the_inputs_and_submitting_raises_all_three_events ()
		{
			var (context, page) = await fixture.NewPageAsync ();
			await using (context) {
				await page.GotoAsync ("/Events.aspx");

				Assert.Equal ("(no events yet)", await page.InnerTextAsync ("#log"));

				// A real user's interaction: type, pick, submit.
				await page.FillAsync ("#who", "piero");
				await page.SelectOptionAsync ("#colour", "blue");
				await page.RunAndWaitForNavigationAsync (async () => await page.ClickAsync ("#go"));

				string log = await page.InnerTextAsync ("#log");

				Assert.Contains ("TextChanged(who=piero)", log);
				Assert.Contains ("SelectedIndexChanged(colour=blue)", log);
				Assert.Contains ("Click(go)", log);
				Assert.Equal ("3", await page.InnerTextAsync ("#count"));
			}
		}

		[Fact]
		public async Task Events_are_raised_in_page_lifecycle_order ()
		{
			var (context, page) = await fixture.NewPageAsync ();
			await using (context) {
				await page.GotoAsync ("/Events.aspx");

				await page.FillAsync ("#who", "piero");
				await page.SelectOptionAsync ("#colour", "green");
				await page.RunAndWaitForNavigationAsync (async () => await page.ClickAsync ("#go"));

				// The changed events run during RaiseChangedEvents, which the page executes BEFORE the
				// postback event. A click that appeared first would mean the lifecycle is wrong.
				Assert.Equal ("TextChanged(who=piero) | SelectedIndexChanged(colour=green) | Click(go)",
					      await page.InnerTextAsync ("#log"));
			}
		}

		[Fact]
		public async Task Only_controls_whose_value_changed_raise_a_changed_event ()
		{
			var (context, page) = await fixture.NewPageAsync ();
			await using (context) {
				await page.GotoAsync ("/Events.aspx");

				await page.FillAsync ("#who", "piero");
				await page.SelectOptionAsync ("#colour", "blue");
				await page.RunAndWaitForNavigationAsync (async () => await page.ClickAsync ("#go"));

				// Second submit: change only the dropdown. LoadPostData compares each posted value
				// with the one restored from view state, so the unchanged textbox must stay silent.
				await page.SelectOptionAsync ("#colour", "green");
				await page.RunAndWaitForNavigationAsync (async () => await page.ClickAsync ("#go"));

				string log = await page.InnerTextAsync ("#log");

				Assert.Equal (
					"TextChanged(who=piero) | SelectedIndexChanged(colour=blue) | Click(go)" +
					" | SelectedIndexChanged(colour=green) | Click(go)", log);

				// Third submit: change nothing. Only the click survives.
				await page.RunAndWaitForNavigationAsync (async () => await page.ClickAsync ("#go"));

				Assert.EndsWith ("Click(go) | Click(go)", await page.InnerTextAsync ("#log"));
				Assert.Equal ("6", await page.InnerTextAsync ("#count"));
			}
		}

		[Fact]
		public async Task Posted_values_survive_the_round_trip_and_are_re_rendered ()
		{
			var (context, page) = await fixture.NewPageAsync ();
			await using (context) {
				await page.GotoAsync ("/Events.aspx");

				await page.FillAsync ("#who", "piero");
				await page.SelectOptionAsync ("#colour", "blue");
				await page.RunAndWaitForNavigationAsync (async () => await page.ClickAsync ("#go"));

				// After the postback the browser is showing a freshly rendered page; the controls must
				// come back carrying what was submitted, not their initial values.
				Assert.Equal ("piero", await page.InputValueAsync ("#who"));
				Assert.Equal ("blue", await page.InputValueAsync ("#colour"));
			}
		}

		[Fact]
		public async Task Event_log_accumulates_through_view_state_not_session ()
		{
			var (firstContext, firstPage) = await fixture.NewPageAsync ();
			await using (firstContext) {
				await firstPage.GotoAsync ("/Events.aspx");
				await firstPage.FillAsync ("#who", "piero");
				await firstPage.RunAndWaitForNavigationAsync (async () => await firstPage.ClickAsync ("#go"));

				Assert.Contains ("TextChanged(who=piero)", await firstPage.InnerTextAsync ("#log"));
			}

			// A second browser context is a different visitor. The log lives in view state, which is
			// per-page-instance, so it must start empty - if it carried over, the page would be
			// leaking one user's input into another's session.
			var (secondContext, secondPage) = await fixture.NewPageAsync ();
			await using (secondContext) {
				await secondPage.GotoAsync ("/Events.aspx");

				Assert.Equal ("(no events yet)", await secondPage.InnerTextAsync ("#log"));
			}
		}
	}
}
