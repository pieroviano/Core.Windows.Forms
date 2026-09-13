//
// Events/Buttons.aspx: every way of raising a postback event.
//
// Reference source: Button.RaisePostBackEvent (Validate, OnClick, then OnCommand, which bubbles);
// ImageButton posts name.x/name.y; Page.RaisePostBackEvent routes __EVENTTARGET; Page.IsPostBack and
// DeterminePostBackMode for cross-page posts (the target is not a postback, its PreviousPage is).
//

using System.Threading.Tasks;
using Microsoft.Playwright;
using Xunit;

namespace WebFormsPort.SamplesBrowserTests.WebForms
{
	[Collection (WebFormsCollection.Name)]
	public class ButtonTests
	{
		readonly WebFormsFixture fixture;

		public ButtonTests (WebFormsFixture fixture)
		{
			this.fixture = fixture;
		}

		Task Single (string selector, string expected) => fixture.RunAsync (async page => {
			await page.GotoAsync ("/Events/Buttons.aspx");
			await page.ClickAndWaitAsync (selector);
			Assert.Equal (expected, await page.TextAsync ("#log"));
			Assert.Equal ("1", await page.TextAsync ("#postbacks"));
		});

		[Fact]
		public Task Button_raises_Click () => Single ("#plain", "plain.Click");

		[Fact]
		public Task Button_raises_Click_then_Command_which_bubbles_to_the_page ()
			=> Single ("#cmd", "cmd.Click | cmd.Command(Add,42) | Page.BubbleEvent(cmd,Add)");

		[Fact]
		public Task LinkButton_posts_back_through_doPostBack () => Single ("#link", "link.Click");

		[Fact]
		public Task Button_without_submit_behaviour_posts_back_through_script () => Single ("#noSubmit", "noSubmit.Click");

		[Fact]
		public Task HtmlButton_raises_ServerClick () => Single ("#htmlButton", "htmlButton.ServerClick");

		[Fact]
		public Task HtmlInputButton_raises_ServerClick () => Single ("#htmlSubmit", "htmlSubmit.ServerClick");

		[Fact]
		public Task HtmlAnchor_raises_ServerClick () => Single ("#anchor", "anchor.ServerClick");

		[Fact]
		public Task ImageButton_reports_the_click_coordinates () => fixture.RunAsync (async page => {
			await page.GotoAsync ("/Events/Buttons.aspx");

			await page.PostBackAsync (() => page.ClickAsync ("#img", new PageClickOptions {
				Position = new Position { X = 5, Y = 7 },
			}));

			Assert.Equal ("img.Click(5,7)", await page.TextAsync ("#log"));
		});

		[Fact]
		public Task OnClientClick_returning_false_cancels_the_postback () => fixture.RunAsync (async page => {
			await page.GotoAsync ("/Events/Buttons.aspx");

			await page.AssertNoNavigationAsync (() => page.ClickAsync ("#cancelled"));

			Assert.Equal ("(no events yet)", await page.TextAsync ("#log"));
			Assert.Equal ("0", await page.TextAsync ("#postbacks"));
		});

		[Fact]
		public Task Enter_inside_a_panel_clicks_its_DefaultButton () => fixture.RunAsync (async page => {
			await page.GotoAsync ("/Events/Buttons.aspx");

			await page.FillAsync ("#entry", "hello");
			await page.PostBackAsync (() => page.PressAsync ("#entry", "Enter"));

			// Without Panel.DefaultButton the browser would submit with the form's FIRST submit button.
			Assert.Equal ("second.Click(hello)", await page.TextAsync ("#log"));
		});

		[Fact]
		public Task Events_accumulate_across_postbacks_from_different_buttons () => fixture.RunAsync (async page => {
			await page.GotoAsync ("/Events/Buttons.aspx");

			await page.ClickAndWaitAsync ("#plain");
			await page.ClickAndWaitAsync ("#link");
			await page.ClickAndWaitAsync ("#anchor");

			Assert.Equal ("plain.Click | link.Click | anchor.ServerClick", await page.TextAsync ("#log"));
			Assert.Equal ("3", await page.TextAsync ("#postbacks"));
		});

		[Fact]
		public Task PostBackUrl_posts_the_form_to_another_page_which_reads_it_through_PreviousPage () => fixture.RunAsync (async page => {
			await page.GotoAsync ("/Events/Buttons.aspx");

			await page.FillAsync ("#carry", "carried value");
			await page.ClickAndWaitAsync ("#cross");

			Assert.EndsWith ("/Events/CrossPageTarget.aspx", page.Url);
			Assert.Equal (
				"previous=~/Events/Buttons.aspx carry=carried value previous.IsCrossPagePostBack=True" +
				" IsPostBack=False IsCrossPagePostBack=False",
				await page.TextAsync ("#result"));
		});
	}
}
