//
// Events/Dynamic.aspx and Events/MasterContent.aspx: controls whose events are routed through something
// other than a flat, declared control tree.
//
// Reference source: Control.AddedControl (catch-up of Init/view state/Load for late controls),
// Page.ProcessPostData's second pass over _leftoverPostData after LoadRecursive, and ClientIDMode
// Predictable (the 4.0 default) for ids inside a master page's ContentPlaceHolder.
//

using System.Threading.Tasks;
using Xunit;

namespace WebFormsPort.SamplesBrowserTests.WebForms
{
	[Collection (WebFormsCollection.Name)]
	public class DynamicAndMasterTests
	{
		readonly WebFormsFixture fixture;

		public DynamicAndMasterTests (WebFormsFixture fixture)
		{
			this.fixture = fixture;
		}

		[Fact]
		public Task Control_created_in_Init_raises_its_events () => fixture.RunAsync (async page => {
			await page.GotoAsync ("/Events/Dynamic.aspx");

			await page.FillAsync ("#dynBox", "typed");
			await page.ClickAndWaitAsync ("#dynButton");

			Assert.Equal ("dynBox.TextChanged(typed) | dynButton.Click", await page.TextAsync ("#log"));
			Assert.Equal ("typed", await page.InputValueAsync ("#dynBox"));
		});

		[Fact]
		public Task Control_created_in_Load_still_raises_its_click () => fixture.RunAsync (async page => {
			await page.GotoAsync ("/Events/Dynamic.aspx");

			await page.ClickAndWaitAsync ("#lateButton");

			Assert.Equal ("lateButton.Click", await page.TextAsync ("#log"));
		});

		[Fact]
		public Task Dynamic_control_view_state_survives_postbacks () => fixture.RunAsync (async page => {
			await page.GotoAsync ("/Events/Dynamic.aspx");
			Assert.Equal ("label text from the first request", await page.TextAsync ("#dynLabel"));

			await page.ClickAndWaitAsync ("#dynButton");
			await page.ClickAndWaitAsync ("#lateButton");

			Assert.Equal ("label text from the first request", await page.TextAsync ("#dynLabel"));
			Assert.Equal ("2", await page.TextAsync ("#postbacks"));
		});

		[Fact]
		public Task Content_page_controls_render_mangled_names_and_predictable_ids () => fixture.RunAsync (async page => {
			await page.GotoAsync ("/Events/MasterContent.aspx");

			Assert.Equal ("[master header]", await page.TextAsync ("#chrome-header"));
			Assert.Equal ("ctl00$MainContent$who", await page.GetAttributeAsync ("#MainContent_who", "name"));
			Assert.Equal ("Events in a content page", await page.TitleAsync ());
		});

		[Fact]
		public Task Content_page_controls_raise_changed_click_and_autopostback_events () => fixture.RunAsync (async page => {
			await page.GotoAsync ("/Events/MasterContent.aspx");

			await page.FillAsync ("#MainContent_who", "inside");
			await page.ClickAndWaitAsync ("#MainContent_go");
			Assert.Equal ("who.TextChanged(inside) | go.Click", await page.TextAsync ("#MainContent_log"));

			await page.PostBackAsync (() => page.SelectOptionAsync ("#MainContent_pick", "two"));
			Assert.Equal ("who.TextChanged(inside) | go.Click | pick.SelectedIndexChanged(two)",
				await page.TextAsync ("#MainContent_log"));
			Assert.Equal ("2", await page.TextAsync ("#MainContent_postbacks"));
		});
	}
}
