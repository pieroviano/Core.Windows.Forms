//
// Samples/WebPagesSample: standalone .cshtml pages.
//

using System.Threading.Tasks;
using Xunit;

namespace WebFormsPort.SamplesBrowserTests
{
	[Collection (WebPagesCollection.Name)]
	public class WebPagesTests
	{
		readonly WebPagesFixture fixture;

		public WebPagesTests (WebPagesFixture fixture)
		{
			this.fixture = fixture;
		}

		[Fact]
		public Task Default_page_renders_through_its_layout () => fixture.RunAsync (async page => {
			await page.GotoAsync ("/");

			Assert.Equal ("Home - Web Pages on Kestrel", await page.TitleAsync ());
			Assert.Equal ("[webpages layout header]", await page.TextAsync ("#chrome-header"));
			Assert.Equal ("42", await page.TextAsync ("#sum"));
			Assert.Equal ("Web Pages on Kestrel", await page.TextAsync ("#setting"));
			Assert.Equal (3, await page.Locator ("#widgets li").CountAsync ());
			Assert.Equal ("<script>alert(1)</script>", await page.TextAsync ("#encoded"));
			Assert.Equal (0, await page.Locator ("#greeting").CountAsync ());
		});

		[Fact]
		public Task Posting_the_form_greets_and_keeps_the_value () => fixture.RunAsync (async page => {
			await page.GotoAsync ("/Default.cshtml");

			await page.FillAsync ("#who", "piero");
			await page.ClickAndWaitAsync ("#send");

			Assert.Equal ("Hello, piero!", await page.TextAsync ("#greeting"));
			Assert.Equal ("piero", await page.InputValueAsync ("#who"));
		});

		[Fact]
		public Task Second_page_is_reachable_with_and_without_extension () => fixture.RunAsync (async page => {
			await page.GotoAsync ("/Helpers");
			Assert.Equal ("Helpers - Web Pages on Kestrel", await page.TitleAsync ());
			Assert.Equal ("GET", await page.TextAsync ("#method"));

			await page.GotoAsync ("/Helpers.cshtml");
			Assert.Equal ("/Helpers.cshtml", await page.TextAsync ("#path"));
		});

		[Fact]
		public Task Missing_page_is_404_and_layout_is_not_served_directly () => fixture.RunAsync (async page => {
			Assert.Equal (404, (await page.GotoAsync ("/NoSuchPage")).Status);
			Assert.NotEqual (200, (await page.GotoAsync ("/_Layout.cshtml")).Status);
		}, allowServerErrors: true);
	}
}
