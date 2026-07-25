//
// MVC 3 on the port, driven by a real browser.
//
// What these cover is the request path a controller action goes through: UrlRoutingModule matching the
// route during PostResolveRequestCache, RemapHandler installing MvcHandler, the pipeline honouring
// that handler instead of mapping the URL to a file, DefaultControllerFactory finding the controller,
// and the action result being written to the response.
//
// Every one of those was broken until the RemapHandler fix - a routed URL simply 404'd because no such
// file existed on disk - so these are the regression net for it.
//
// View rendering has its own file, RazorViewBrowserTests; these cover the actions that return a
// result directly rather than through a view.
//

using System.Threading.Tasks;
using Microsoft.Playwright;
using Xunit;

namespace WebFormsPort.MvcBrowserTests
{
	[Collection (MvcBrowserCollection.Name)]
	public class MvcRoutingBrowserTests
	{
		readonly MvcBrowserFixture fixture;

		public MvcRoutingBrowserTests (MvcBrowserFixture fixture)
		{
			this.fixture = fixture;
		}

		[Fact]
		public async Task Routed_action_returning_content_is_served ()
		{
			var (context, page) = await fixture.NewPageAsync ();
			await using (context) {
				IResponse response = await page.GotoAsync ("/Home/Plain");

				// /Home/Plain is not a file. Reaching the action at all means routing remapped the
				// handler and the pipeline kept it.
				Assert.Equal (200, response.Status);
				Assert.Contains ("plain content result", await page.ContentAsync ());
			}
		}

		[Fact]
		public async Task Routed_action_returning_json_is_served ()
		{
			var (context, page) = await fixture.NewPageAsync ();
			await using (context) {
				IResponse response = await page.GotoAsync ("/Home/Data");

				Assert.Equal (200, response.Status);

				string body = await response.TextAsync ();
				Assert.Contains ("\"total\":3", body);
				Assert.Contains ("\"first\":\"sprocket\"", body);
			}
		}

		[Fact]
		public async Task Json_result_declares_a_json_content_type ()
		{
			var (context, page) = await fixture.NewPageAsync ();
			await using (context) {
				IResponse response = await page.GotoAsync ("/Home/Data");

				Assert.Contains ("json", response.Headers ["content-type"]);
			}
		}

		[Fact]
		public async Task Unknown_controller_is_a_404_rather_than_a_server_error ()
		{
			var (context, page) = await fixture.NewPageAsync ();
			await using (context) {
				// The route matches - {controller}/{action} accepts anything - so this exercises
				// controller resolution failing, which must surface as 404 and not 500.
				IResponse response = await page.GotoAsync ("/NoSuchController/Index");

				Assert.Equal (404, response.Status);
			}
		}

		[Fact]
		public async Task Unknown_action_on_a_real_controller_is_a_404 ()
		{
			var (context, page) = await fixture.NewPageAsync ();
			await using (context) {
				IResponse response = await page.GotoAsync ("/Home/NoSuchAction");

				Assert.Equal (404, response.Status);
			}
		}

		[Fact]
		public async Task Route_defaults_apply_when_the_action_is_omitted ()
		{
			var (context, page) = await fixture.NewPageAsync ();
			await using (context) {
				// The route's default action is Index. /Home therefore has to reach the same place
				// the empty path does.
				IResponse response = await page.GotoAsync ("/Home");

				Assert.Equal (200, response.Status);
				Assert.Equal ("Widgets", await page.InnerTextAsync ("h1"));
			}
		}
	}
}
