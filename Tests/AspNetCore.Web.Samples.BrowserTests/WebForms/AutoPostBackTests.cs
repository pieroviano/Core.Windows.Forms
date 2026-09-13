//
// Events/AutoPostBack.aspx: controls that post back on their own.
//
// Semantics from the reference source: CheckBox.LoadPostData (unchecked posts nothing, yet the control
// registers for LoadPostData in OnPreRender, so unchecking still raises CheckedChanged);
// RadioButton.LoadPostData (only the button being checked raises CheckedChanged); ListControl
// SelectedIndexChanged once per postback.
//

using System.Threading.Tasks;
using Xunit;

namespace WebFormsPort.SamplesBrowserTests.WebForms
{
	[Collection (WebFormsCollection.Name)]
	public class AutoPostBackTests
	{
		readonly WebFormsFixture fixture;

		public AutoPostBackTests (WebFormsFixture fixture)
		{
			this.fixture = fixture;
		}

		[Fact]
		public Task TextBox_posts_back_when_it_loses_focus_after_a_change () => fixture.RunAsync (async page => {
			await page.GotoAsync ("/Events/AutoPostBack.aspx");
			Assert.Equal ("(no events yet)", await page.TextAsync ("#log"));

			await page.FillAsync ("#name", "piero");
			await page.PostBackAsync (() => page.PressAsync ("#name", "Tab"));

			Assert.Equal ("name.TextChanged(piero)", await page.TextAsync ("#log"));
			Assert.Equal ("1", await page.TextAsync ("#postbacks"));
			Assert.Equal ("piero", await page.InputValueAsync ("#name"));
		});

		[Fact]
		public Task DropDownList_posts_back_on_selection () => fixture.RunAsync (async page => {
			await page.GotoAsync ("/Events/AutoPostBack.aspx");

			await page.PostBackAsync (() => page.SelectOptionAsync ("#size", "L"));
			await page.PostBackAsync (() => page.SelectOptionAsync ("#size", "M"));

			Assert.Equal ("size.SelectedIndexChanged(L) | size.SelectedIndexChanged(M)", await page.TextAsync ("#log"));
			Assert.Equal ("M", await page.InputValueAsync ("#size"));
		});

		[Fact]
		public Task CheckBox_raises_CheckedChanged_when_checked_and_when_unchecked () => fixture.RunAsync (async page => {
			await page.GotoAsync ("/Events/AutoPostBack.aspx");

			await page.PostBackAsync (() => page.ClickAsync ("#agree"));
			Assert.True (await page.IsCheckedAsync ("#agree"));

			await page.PostBackAsync (() => page.ClickAsync ("#agree"));
			Assert.False (await page.IsCheckedAsync ("#agree"));

			Assert.Equal ("agree.CheckedChanged(True) | agree.CheckedChanged(False)", await page.TextAsync ("#log"));
		});

		[Fact]
		public Task RadioButton_raises_CheckedChanged_only_for_the_newly_checked_button () => fixture.RunAsync (async page => {
			await page.GotoAsync ("/Events/AutoPostBack.aspx");
			Assert.True (await page.IsCheckedAsync ("#ship"));

			await page.PostBackAsync (() => page.ClickAsync ("#pickup"));

			Assert.Equal ("pickup.CheckedChanged(True)", await page.TextAsync ("#log"));
			Assert.True (await page.IsCheckedAsync ("#pickup"));
			Assert.False (await page.IsCheckedAsync ("#ship"));

			await page.PostBackAsync (() => page.ClickAsync ("#ship"));
			Assert.Equal ("pickup.CheckedChanged(True) | ship.CheckedChanged(True)", await page.TextAsync ("#log"));
		});

		[Fact]
		public Task CheckBoxList_reports_the_full_selection_on_each_change () => fixture.RunAsync (async page => {
			await page.GotoAsync ("/Events/AutoPostBack.aspx");

			await page.PostBackAsync (() => page.ClickAsync ("#toppings_0"));
			await page.PostBackAsync (() => page.ClickAsync ("#toppings_2"));
			await page.PostBackAsync (() => page.ClickAsync ("#toppings_0"));

			Assert.Equal (
				"toppings.SelectedIndexChanged(cheese) | toppings.SelectedIndexChanged(cheese,olives)" +
				" | toppings.SelectedIndexChanged(olives)",
				await page.TextAsync ("#log"));
			Assert.False (await page.IsCheckedAsync ("#toppings_0"));
			Assert.True (await page.IsCheckedAsync ("#toppings_2"));
		});

		[Fact]
		public Task RadioButtonList_posts_back_on_selection () => fixture.RunAsync (async page => {
			await page.GotoAsync ("/Events/AutoPostBack.aspx");

			await page.PostBackAsync (() => page.ClickAsync ("#pay_1"));

			Assert.Equal ("pay.SelectedIndexChanged(cash)", await page.TextAsync ("#log"));
			Assert.True (await page.IsCheckedAsync ("#pay_1"));
		});

		[Fact]
		public Task Multi_select_ListBox_reports_every_selected_value () => fixture.RunAsync (async page => {
			await page.GotoAsync ("/Events/AutoPostBack.aspx");

			await page.PostBackAsync (() => page.SelectOptionAsync ("#tags", new [] { "red", "blue" }));

			Assert.Equal ("tags.SelectedIndexChanged(red,blue)", await page.TextAsync ("#log"));
		});

		[Fact]
		public Task Changing_one_control_does_not_raise_events_for_the_others () => fixture.RunAsync (async page => {
			await page.GotoAsync ("/Events/AutoPostBack.aspx");

			await page.FillAsync ("#name", "x");
			await page.PostBackAsync (() => page.PressAsync ("#name", "Tab"));
			await page.PostBackAsync (() => page.ClickAsync ("#agree"));
			// Exactly one event per control that changed - the untouched ones stay silent.
			Assert.Equal ("name.TextChanged(x) | agree.CheckedChanged(True)", await page.TextAsync ("#log"));
		});
	}
}
