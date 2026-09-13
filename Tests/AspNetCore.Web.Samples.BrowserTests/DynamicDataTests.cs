//
// Samples/DynamicDataSample: scaffolded List/Details pages and LinqDataSource through GridViews.
//
// The sample's data is static for the life of the process, so each test that changes it uses a row no
// other test reads (Birdseed is renamed and put back; Dynamite is deleted).
//

using System.Threading.Tasks;
using Microsoft.Playwright;
using Xunit;

namespace WebFormsPort.SamplesBrowserTests
{
	[Collection (DynamicDataCollection.Name)]
	public class DynamicDataTests
	{
		readonly DynamicDataFixture fixture;

		public DynamicDataTests (DynamicDataFixture fixture)
		{
			this.fixture = fixture;
		}

		static ILocator RowWith (IPage page, string grid, string text) => page.Locator (grid + " tr", new PageLocatorOptions { HasText = text });

		[Fact]
		public Task Table_index_links_to_scaffolded_list_pages () => fixture.RunAsync (async page => {
			await page.GotoAsync ("/Default.aspx");

			Assert.Equal (2, await page.Locator (".table-entry a").CountAsync ());

			await page.ClickAndWaitAsync (".table-entry a:text-is('Categories')");
			Assert.EndsWith ("/Categories/List.aspx", page.Url);
			Assert.Equal ("Categories", await page.TextAsync ("h1"));
			Assert.Contains ("Consumables", await page.TextAsync ("#Grid"));

			await page.ClickAndWaitAsync ("a:text-is('All tables')");
			Assert.EndsWith ("/Default.aspx", page.Url);
		});

		[Fact]
		public Task List_page_renders_field_templates () => fixture.RunAsync (async page => {
			await page.GotoAsync ("/Products/List.aspx");

			Assert.Equal ("Products", await page.TextAsync ("h1"));
			Assert.Contains ("49.95", await RowWith (page, "#Grid", "Anvil").InnerTextAsync ());
			// Boolean.ascx: a disabled checkbox, checked for the discontinued Rocket.
			Assert.True (await RowWith (page, "#Grid", "Rocket").Locator ("input[type=checkbox]").IsCheckedAsync ());
		});

		[Fact]
		public Task List_page_edits_and_updates_a_row_through_the_field_templates () => fixture.RunAsync (async page => {
			await page.GotoAsync ("/Products/List.aspx");

			await page.PostBackAsync (() => RowWith (page, "#Grid", "Birdseed").Locator ("a:text-is('Edit')").ClickAsync ());
			ILocator name = page.Locator ("#Grid input[type=text][value='Birdseed']");
			Assert.Equal (1, await name.CountAsync ());

			await name.FillAsync ("Birdseed Deluxe");
			await page.PostBackAsync (() => page.Locator ("#Grid a:text-is('Update')").ClickAsync ());
			Assert.Equal (1, await RowWith (page, "#Grid", "Birdseed Deluxe").CountAsync ());
			Assert.Equal (0, await page.Locator ("#Grid input[type=text]").CountAsync ());

			// Put it back, through the same UI.
			await page.PostBackAsync (() => RowWith (page, "#Grid", "Birdseed Deluxe").Locator ("a:text-is('Edit')").ClickAsync ());
			await page.Locator ("#Grid input[type=text][value='Birdseed Deluxe']").FillAsync ("Birdseed");
			await page.PostBackAsync (() => page.Locator ("#Grid a:text-is('Update')").ClickAsync ());
			Assert.Equal (0, await RowWith (page, "#Grid", "Deluxe").CountAsync ());
		});

		[Fact]
		public Task List_page_cancel_leaves_the_row_unchanged () => fixture.RunAsync (async page => {
			await page.GotoAsync ("/Products/List.aspx");

			await page.PostBackAsync (() => RowWith (page, "#Grid", "Anvil").Locator ("a:text-is('Edit')").ClickAsync ());
			await page.Locator ("#Grid input[type=text][value='Anvil']").FillAsync ("Changed my mind");
			await page.PostBackAsync (() => page.Locator ("#Grid a:text-is('Cancel')").ClickAsync ());

			Assert.Equal (1, await RowWith (page, "#Grid", "Anvil").CountAsync ());
			Assert.Equal (0, await RowWith (page, "#Grid", "Changed my mind").CountAsync ());
		});

		[Fact]
		public Task Details_page_shows_one_row_or_says_there_is_none () => fixture.RunAsync (async page => {
			await page.GotoAsync ("/Products/Details.aspx?Id=1");
			Assert.Contains ("Anvil", await page.TextAsync ("#Details"));

			await page.GotoAsync ("/Products/Details.aspx?Id=999");
			Assert.True (await page.IsVisibleAsync ("#NotFound"));
		});

		[Fact]
		public Task LinqDataSource_filters_orders_and_reads_query_string_parameters () => fixture.RunAsync (async page => {
			await page.GotoAsync ("/Products.aspx");

			string all = await page.TextAsync ("#ProductGrid");
			Assert.DoesNotContain ("Rocket", all);   // Where="Discontinued == false"
			Assert.True (all.IndexOf ("Anvil") < all.IndexOf ("Birdseed"));   // OrderBy="Name"

			Assert.Contains ("Anvil", await page.TextAsync ("#CategoryGrid"));   // DefaultValue="1"

			await page.GotoAsync ("/Products.aspx?cat=2");
			string consumables = await page.TextAsync ("#CategoryGrid");
			Assert.Contains ("Birdseed", consumables);
			Assert.DoesNotContain ("Anvil", consumables);
		});

		[Fact]
		public Task LinqDataSource_grid_deletes_a_row () => fixture.RunAsync (async page => {
			await page.GotoAsync ("/Products.aspx");
			Assert.Equal (1, await RowWith (page, "#ProductGrid", "Dynamite").CountAsync ());

			await page.PostBackAsync (() => RowWith (page, "#ProductGrid", "Dynamite").Locator ("a:text-is('Delete')").ClickAsync ());

			Assert.Equal (0, await RowWith (page, "#ProductGrid", "Dynamite").CountAsync ());
			Assert.Equal (1, await RowWith (page, "#ProductGrid", "Anvil").CountAsync ());
		});
	}
}
