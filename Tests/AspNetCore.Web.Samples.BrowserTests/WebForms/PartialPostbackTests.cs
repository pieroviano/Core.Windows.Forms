//
// Events/Partial.aspx and Ajax.aspx: server events raised by async postbacks.
//
// Reference source (System.Web.Extensions): PageRequestManager renders only the panels that must update
// (UpdateMode Conditional: the panel containing the source, AsyncPostBackTrigger targets); a
// PostBackTrigger forces a full postback; the server runs the whole page lifecycle either way.
//

using System.Threading.Tasks;
using Microsoft.Playwright;
using Xunit;

namespace WebFormsPort.SamplesBrowserTests.WebForms
{
	[Collection (WebFormsCollection.Name)]
	public class PartialPostbackTests
	{
		readonly WebFormsFixture fixture;

		public PartialPostbackTests (WebFormsFixture fixture)
		{
			this.fixture = fixture;
		}

		static Task WaitForLogAsync (IPage page, string expected)
		{
			return Assertions.Expect (page.Locator ("#log")).ToHaveTextAsync (expected);
		}

		[Fact]
		public Task Button_inside_the_panel_raises_Click_in_an_async_postback () => fixture.RunAsync (async page => {
			await page.GotoAsync ("/Events/Partial.aspx");
			Assert.Equal ("request #1", await page.TextAsync ("#stamp"));

			await page.AssertNoNavigationAsync (async () => {
				await page.ClickAsync ("#inside");
				await WaitForLogAsync (page, "inside.Click(async=True)");
			});

			// Rendered as "request #2" on the server, but outside the panel - never sent to the browser.
			Assert.Equal ("request #1", await page.TextAsync ("#stamp"));
			Assert.Equal ("1", await page.TextAsync ("#postbacks"));
		});

		[Fact]
		public Task AutoPostBack_control_inside_the_panel_posts_asynchronously () => fixture.RunAsync (async page => {
			await page.GotoAsync ("/Events/Partial.aspx");

			await page.AssertNoNavigationAsync (async () => {
				await page.SelectOptionAsync ("#choice", "b");
				await WaitForLogAsync (page, "choice.SelectedIndexChanged(b,async=True)");
			});

			Assert.Equal ("b", await page.InputValueAsync ("#choice"));
		});

		[Fact]
		public Task AsyncPostBackTrigger_outside_the_panel_updates_the_panel () => fixture.RunAsync (async page => {
			await page.GotoAsync ("/Events/Partial.aspx");

			await page.AssertNoNavigationAsync (async () => {
				await page.ClickAsync ("#outside");
				await WaitForLogAsync (page, "outside.Click(async=True)");
			});
		});

		[Fact]
		public Task PostBackTrigger_inside_the_panel_forces_a_full_postback () => fixture.RunAsync (async page => {
			await page.GotoAsync ("/Events/Partial.aspx");

			await page.ClickAsync ("#inside");
			await WaitForLogAsync (page, "inside.Click(async=True)");

			await page.ClickAndWaitAsync ("#full");

			Assert.Equal ("inside.Click(async=True) | full.Click(async=False)", await page.TextAsync ("#log"));
			Assert.Equal ("request #3", await page.TextAsync ("#stamp"));
		});

		[Fact]
		public Task Ajax_page_updates_only_the_panel_and_counts_in_view_state () => fixture.RunAsync (async page => {
			await page.GotoAsync ("/Ajax.aspx");
			string outside = await page.TextAsync ("#outside");

			await page.AssertNoNavigationAsync (async () => {
				await page.ClickAsync ("#go");
				await Assertions.Expect (page.Locator ("#inside")).ToContainTextAsync ("partial update #1");
				await page.ClickAsync ("#go");
				await Assertions.Expect (page.Locator ("#inside")).ToContainTextAsync ("partial update #2");
			});

			Assert.Equal (outside, await page.TextAsync ("#outside"));
		});
	}
}
