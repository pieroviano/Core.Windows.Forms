//
// Events/Lifecycle.aspx: the order of every page and control lifecycle stage.
//
// Expected sequences are ASP.NET 4.x's, from the reference source under
// mono/mcs/class/referencesource/System.Web: Page.ProcessRequestMain drives the stages, and
// Control.InitRecursive raises Init on children BEFORE their parent, while LoadRecursive and
// PreRenderRecursiveInternal raise the parent first.
//

using System.Threading.Tasks;
using Xunit;

namespace WebFormsPort.SamplesBrowserTests.WebForms
{
	[Collection (WebFormsCollection.Name)]
	public class LifecycleTests
	{
		readonly WebFormsFixture fixture;

		public LifecycleTests (WebFormsFixture fixture)
		{
			this.fixture = fixture;
		}

		[Fact]
		public Task First_request_runs_every_stage_in_order () => fixture.RunAsync (async page => {
			await page.GotoAsync ("/Events/Lifecycle.aspx");

			Assert.Equal (
				"Page.PreInit | box.Init | uc.inner.Init | uc.Init | Page.Init | Page.InitComplete | Page.PreLoad" +
				" | Page.Load(IsPostBack=False) | box.Load(Text=) | uc.Load | uc.inner.Load" +
				" | Page.LoadComplete | Page.PreRender | box.PreRender | uc.PreRender | uc.inner.PreRender" +
				" | Page.PreRenderComplete | Page.SaveStateComplete",
				await page.TextAsync ("#trace"));
		});

		[Fact]
		public Task Postback_raises_changed_and_click_events_between_Load_and_LoadComplete () => fixture.RunAsync (async page => {
			await page.GotoAsync ("/Events/Lifecycle.aspx");

			await page.FillAsync ("#box", "abc");
			await page.ClickAndWaitAsync ("#go");

			// Posted data is loaded before PreLoad (ProcessPostData, first pass), so the TextBox already
			// holds the new value in its Load handler; TextChanged waits for RaiseChangedEvents.
			Assert.Equal (
				"Page.PreInit | box.Init | uc.inner.Init | uc.Init | Page.Init | Page.InitComplete | Page.PreLoad" +
				" | Page.Load(IsPostBack=True) | box.Load(Text=abc) | uc.Load | uc.inner.Load" +
				" | box.TextChanged | go.Click" +
				" | Page.LoadComplete | Page.PreRender | box.PreRender | uc.PreRender | uc.inner.PreRender" +
				" | Page.PreRenderComplete | Page.SaveStateComplete",
				await page.TextAsync ("#trace"));
		});

		[Fact]
		public Task Unchanged_value_on_a_second_postback_raises_no_changed_event () => fixture.RunAsync (async page => {
			await page.GotoAsync ("/Events/Lifecycle.aspx");

			await page.FillAsync ("#box", "abc");
			await page.ClickAndWaitAsync ("#go");
			await page.ClickAndWaitAsync ("#go");

			string trace = await page.TextAsync ("#trace");
			Assert.DoesNotContain ("box.TextChanged", trace);
			Assert.Contains ("box.Load(Text=abc) | uc.Load | uc.inner.Load | go.Click | Page.LoadComplete", trace);
		});
	}
}
