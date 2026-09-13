//
// Events/DataControls.aspx: GridView, Repeater and DataList commands.
//
// Reference source: GridView.HandleEvent (RowCommand first, for every command including Page and Sort),
// HandleSelect/HandleEdit/HandleUpdate/HandleDelete/HandlePage/HandleSort (manual binding: the page
// handles the -ing events, SortDirection stays Ascending, PageIndexChanged and Sorted still fire),
// GridView.RaisePostBackEvent ("Sort$Name", "Page$2"), DataList.OnBubbleEvent (ItemCommand, then Select).
//

using System.Threading.Tasks;
using Microsoft.Playwright;
using Xunit;

namespace WebFormsPort.SamplesBrowserTests.WebForms
{
	[Collection (WebFormsCollection.Name)]
	public class DataControlTests
	{
		readonly WebFormsFixture fixture;

		public DataControlTests (WebFormsFixture fixture)
		{
			this.fixture = fixture;
		}

		static ILocator Row (IPage page, int index) => page.Locator ("#grid tr").Nth (index);   // 0 is the header

		static Task<string> CellAsync (IPage page, int row, int cell) => Row (page, row).Locator ("td").Nth (cell).InnerTextAsync ();

		[Fact]
		public Task Grid_renders_the_first_page_with_a_pager () => fixture.RunAsync (async page => {
			await page.GotoAsync ("/Events/DataControls.aspx");

			Assert.Equal ("alpha", await CellAsync (page, 1, 1));
			Assert.Equal ("charlie", await CellAsync (page, 3, 1));
			Assert.Equal (0, await page.Locator ("#grid td:text-is('delta')").CountAsync ());
			Assert.Equal (1, await page.Locator ("#grid a:text-is('2')").CountAsync ());
		});

		[Fact]
		public Task Pager_raises_RowCommand_then_PageIndexChanging_and_PageIndexChanged () => fixture.RunAsync (async page => {
			await page.GotoAsync ("/Events/DataControls.aspx");

			await page.ClickAndWaitAsync ("#grid a:text-is('2')");

			Assert.Equal ("grid.RowCommand(Page,2) | grid.PageIndexChanging(1) | grid.PageIndexChanged(1)",
				await page.TextAsync ("#log"));
			Assert.Equal ("delta", await CellAsync (page, 1, 1));
			Assert.Equal ("echo", await CellAsync (page, 2, 1));
		});

		[Fact]
		public Task Sort_header_raises_Sorting_with_Ascending_every_time_when_bound_manually () => fixture.RunAsync (async page => {
			await page.GotoAsync ("/Events/DataControls.aspx");

			await page.ClickAndWaitAsync ("#grid a:text-is('Name')");
			await page.ClickAndWaitAsync ("#grid a:text-is('Name')");

			Assert.Equal (
				"grid.RowCommand(Sort,Name) | grid.Sorting(Name,Ascending) | grid.Sorted" +
				" | grid.RowCommand(Sort,Name) | grid.Sorting(Name,Ascending) | grid.Sorted",
				await page.TextAsync ("#log"));
		});

		[Fact]
		public Task Select_raises_RowCommand_SelectedIndexChanging_and_SelectedIndexChanged () => fixture.RunAsync (async page => {
			await page.GotoAsync ("/Events/DataControls.aspx");

			await page.PostBackAsync (() => Row (page, 2).Locator ("a:text-is('Select')").ClickAsync ());

			Assert.Equal ("grid.RowCommand(Select,1) | grid.SelectedIndexChanging(1) | grid.SelectedIndexChanged(1,key=2)",
				await page.TextAsync ("#log"));
			Assert.Equal ("selected", await Row (page, 2).GetAttributeAsync ("class"));
		});

		[Fact]
		public Task Edit_update_round_trip_passes_keys_and_new_values () => fixture.RunAsync (async page => {
			await page.GotoAsync ("/Events/DataControls.aspx");

			await page.PostBackAsync (() => Row (page, 3).Locator ("a:text-is('Edit')").ClickAsync ());
			Assert.Equal ("grid.RowCommand(Edit,2) | grid.RowEditing(2)", await page.TextAsync ("#log"));

			// The edited row now renders a TextBox for Name (Id is ReadOnly) and Update/Cancel links.
			ILocator editor = Row (page, 3).Locator ("input[type=text]");
			Assert.Equal (1, await editor.CountAsync ());
			Assert.Equal ("charlie", await editor.InputValueAsync ());

			await editor.FillAsync ("charles");
			await page.PostBackAsync (() => Row (page, 3).Locator ("a:text-is('Update')").ClickAsync ());

			Assert.EndsWith (" | grid.RowCommand(Update,2) | grid.RowUpdating(2,key=3,Name=charles)", await page.TextAsync ("#log"));
			Assert.Equal ("charles", await CellAsync (page, 3, 1));
			Assert.Equal (0, await page.Locator ("#grid input[type=text]").CountAsync ());
		});

		[Fact]
		public Task Cancel_raises_RowCancelingEdit () => fixture.RunAsync (async page => {
			await page.GotoAsync ("/Events/DataControls.aspx");

			await page.PostBackAsync (() => Row (page, 1).Locator ("a:text-is('Edit')").ClickAsync ());
			await page.PostBackAsync (() => Row (page, 1).Locator ("a:text-is('Cancel')").ClickAsync ());

			Assert.Equal ("grid.RowCommand(Edit,0) | grid.RowEditing(0) | grid.RowCommand(Cancel,0) | grid.RowCancelingEdit(0)",
				await page.TextAsync ("#log"));
			Assert.Equal ("alpha", await CellAsync (page, 1, 1));
		});

		[Fact]
		public Task Delete_raises_RowDeleting_with_the_data_key () => fixture.RunAsync (async page => {
			await page.GotoAsync ("/Events/DataControls.aspx");

			await page.PostBackAsync (() => Row (page, 1).Locator ("a:text-is('Delete')").ClickAsync ());

			Assert.Equal ("grid.RowCommand(Delete,0) | grid.RowDeleting(0,key=1)", await page.TextAsync ("#log"));
			Assert.Equal ("bravo", await CellAsync (page, 1, 1));
		});

		[Fact]
		public Task ButtonField_custom_command_reaches_RowCommand_with_the_row_index () => fixture.RunAsync (async page => {
			await page.GotoAsync ("/Events/DataControls.aspx");

			await page.PostBackAsync (() => Row (page, 2).Locator ("input[value=Bump]").ClickAsync ());

			Assert.Equal ("grid.RowCommand(Bump,1)", await page.TextAsync ("#log"));
			Assert.Equal ("bravo+", await CellAsync (page, 2, 1));
		});

		[Fact]
		public Task Repeater_LinkButton_command_bubbles_to_ItemCommand () => fixture.RunAsync (async page => {
			await page.GotoAsync ("/Events/DataControls.aspx");

			await page.ClickAndWaitAsync ("#rep_pick_3");

			Assert.Equal ("rep.ItemCommand(Pick,4,item=3)", await page.TextAsync ("#log"));

			// The items were bound once, on the first request; they are re-created from view state and
			// still carry their CommandArgument on the next postback.
			await page.ClickAndWaitAsync ("#rep_pick_0");
			Assert.EndsWith ("rep.ItemCommand(Pick,1,item=0)", await page.TextAsync ("#log"));
		});

		[Fact]
		public Task DataList_Select_command_raises_ItemCommand_then_SelectedIndexChanged () => fixture.RunAsync (async page => {
			await page.GotoAsync ("/Events/DataControls.aspx");

			await page.ClickAndWaitAsync ("#dl_choose_2");

			Assert.Equal ("dl.ItemCommand(Select,item=2) | dl.SelectedIndexChanged(2,key=3)", await page.TextAsync ("#log"));
			Assert.Equal ("[charlie]", await page.TextAsync ("#dl .chosen"));
		});
	}
}
