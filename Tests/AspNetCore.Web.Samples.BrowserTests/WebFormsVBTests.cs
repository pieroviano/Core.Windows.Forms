//
// Samples/WebFormsSampleVB: the same WebForms behaviour, with every page compiled as VB.
//

using System.Threading.Tasks;
using Xunit;

namespace WebFormsPort.SamplesBrowserTests
{
	[Collection (WebFormsVBCollection.Name)]
	public class WebFormsVBTests
	{
		readonly WebFormsVBFixture fixture;

		public WebFormsVBTests (WebFormsVBFixture fixture)
		{
			this.fixture = fixture;
		}

		[Fact]
		public Task Default_page_renders_and_greets_after_a_postback () => fixture.RunAsync (async page => {
			await page.GotoAsync ("/Default.aspx");

			Assert.Equal ("VB WebForms on Kestrel", await page.TitleAsync ());
			Assert.Equal ("hello from OnLoad", await page.TextAsync ("#message"));
			Assert.Contains ("APP_CODE WORKS!", await page.TextAsync ("body"));
			Assert.Equal (3, await page.Locator ("body > ul li").CountAsync ());

			await page.FillAsync ("#who", "vb user");
			await page.ClickAndWaitAsync ("#greet");

			Assert.Equal ("Hello, vb user!", await page.TextAsync ("#greeting"));
			Assert.Equal ("1", await page.TextAsync ("#counter"));
			Assert.Equal ("grommet", await page.Locator ("#grid tr").Nth (3).Locator ("td").Nth (1).InnerTextAsync ());
		});

		[Fact]
		public Task Required_field_is_enforced_in_the_browser () => fixture.RunAsync (async page => {
			await page.GotoAsync ("/Default.aspx");

			await page.AssertNoNavigationAsync (() => page.ClickAsync ("#greet"));

			Assert.True (await page.IsVisibleAsync ("#whoRequired"));
		});

		[Fact]
		public Task Required_field_is_enforced_on_the_server () => fixture.RunAsync (async page => {
			await page.GotoAsync ("/Default.aspx");

			await page.ClickAndWaitAsync ("#greet");

			Assert.Equal ("(validation failed: a name is required)", await page.TextAsync ("#greeting"));
		}, javaScriptEnabled: false);

		[Fact]
		public Task Inline_VB_page_uses_late_binding_and_configuration () => fixture.RunAsync (async page => {
			await page.GotoAsync ("/Inline.aspx");

			Assert.Equal ("late-bound conversion gave 124; appSetting=VB WebForms on Kestrel; method=GET",
				await page.TextAsync ("#info"));
			Assert.Equal (3, await page.Locator ("span:has-text('item')").CountAsync ());
		});

		[Fact]
		public Task Forms_authentication_protects_the_secure_directory () => fixture.RunAsync (async page => {
			await page.GotoAsync ("/Secure/Secret.aspx");
			Assert.Contains ("/Login.aspx", page.Url);

			await page.FillAsync ("#user", "piero");
			await page.FillAsync ("#pass", "secret");
			await page.ClickAndWaitAsync ("#go");

			Assert.EndsWith ("/Secure/Secret.aspx", page.Url);
			Assert.Equal ("authenticated=True name=piero", await page.TextAsync ("#who"));
		});

		[Fact]
		public Task VB_page_methods_are_callable_from_script () => fixture.RunAsync (async page => {
			await page.GotoAsync ("/PageMethods.aspx");
			Assert.Equal ("vb page lifecycle ran", await page.TextAsync ("#lifecycle"));

			Assert.Equal ("vb page method echo: hi", await page.EvaluateAsync<string> (
				"() => new Promise((ok, fail) => PageMethods.Echo('hi', ok, e => fail(e.get_message())))"));
			Assert.Equal (42, await page.EvaluateAsync<int> (
				"() => new Promise((ok, fail) => PageMethods.Multiply(6, 7, ok, e => fail(e.get_message())))"));
			Assert.Equal (42, await page.EvaluateAsync<int> (
				"() => new Promise((ok, fail) => PageMethods.LateBound('41', ok, e => fail(e.get_message())))"));
		});
	}
}
