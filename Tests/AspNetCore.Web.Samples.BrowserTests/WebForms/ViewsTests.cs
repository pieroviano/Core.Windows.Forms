//
// Events/Views.aspx: MultiView, Wizard and Calendar.
//
// Reference source: MultiView.ActiveViewIndex setter (Deactivate old, Activate new, ActiveViewChanged;
// raised on the first request from OnInit via ShouldTriggerViewEvent, never for the declarative index on
// a postback), MultiView.OnBubbleEvent, Wizard.OnBubbleEvent (the *ButtonClick event, then
// ActiveStepChanged when the index moves), Calendar.RaisePostBackEvent.
//

using System.Threading.Tasks;
using Microsoft.Playwright;
using Xunit;

namespace WebFormsPort.SamplesBrowserTests.WebForms
{
	[Collection (WebFormsCollection.Name)]
	public class ViewsTests
	{
		readonly WebFormsFixture fixture;

		public ViewsTests (WebFormsFixture fixture)
		{
			this.fixture = fixture;
		}

		static string After (string before, string added) => before == "(no events yet)" ? added : before + " | " + added;

		[Fact]
		public Task First_request_activates_the_declared_view () => fixture.RunAsync (async page => {
			await page.GotoAsync ("/Events/Views.aspx");

			Assert.Equal ("v1.Activate | mv.ActiveViewChanged(0)", await page.TextAsync ("#log"));
			Assert.Equal ("View one", await page.TextAsync (".view"));
		});

		[Fact]
		public Task NextView_PrevView_SwitchViewByID_and_SwitchViewByIndex_navigate () => fixture.RunAsync (async page => {
			await page.GotoAsync ("/Events/Views.aspx");
			const string first = "v1.Activate | mv.ActiveViewChanged(0)";

			await page.ClickAndWaitAsync ("#toTwo");
			Assert.Equal ("View two", await page.TextAsync (".view"));
			Assert.Equal (first + " | v1.Deactivate | v2.Activate | mv.ActiveViewChanged(1)", await page.TextAsync ("#log"));

			await page.ClickAndWaitAsync ("#toThree");
			Assert.Equal ("View three", await page.TextAsync (".view"));
			Assert.EndsWith (" | v2.Deactivate | v3.Activate | mv.ActiveViewChanged(2)", await page.TextAsync ("#log"));

			await page.ClickAndWaitAsync ("#toFirst");
			Assert.Equal ("View one", await page.TextAsync (".view"));
			Assert.EndsWith (" | v3.Deactivate | v1.Activate | mv.ActiveViewChanged(0)", await page.TextAsync ("#log"));

			await page.ClickAndWaitAsync ("#toTwo");
			await page.ClickAndWaitAsync ("#backToOne");
			Assert.Equal ("View one", await page.TextAsync (".view"));
			Assert.EndsWith (" | v2.Deactivate | v1.Activate | mv.ActiveViewChanged(0)", await page.TextAsync ("#log"));
		});

		[Fact]
		public Task Wizard_raises_navigation_events_then_ActiveStepChanged () => fixture.RunAsync (async page => {
			await page.GotoAsync ("/Events/Views.aspx");
			Assert.Equal ("Step one", await page.TextAsync (".step"));

			string log = await page.TextAsync ("#log");
			await page.ClickAndWaitAsync ("#wiz input[value='Next']");
			Assert.Equal ("Step two", await page.TextAsync (".step"));
			Assert.Equal (After (log, "wiz.NextButtonClick(0->1) | wiz.ActiveStepChanged(1)"), log = await page.TextAsync ("#log"));

			await page.ClickAndWaitAsync ("#wiz input[value='Previous']");
			Assert.Equal ("Step one", await page.TextAsync (".step"));
			Assert.Equal (After (log, "wiz.PreviousButtonClick(1->0) | wiz.ActiveStepChanged(0)"), log = await page.TextAsync ("#log"));

			await page.ClickAndWaitAsync ("#wiz input[value='Next']");
			await page.ClickAndWaitAsync ("#wiz input[value='Next']");
			Assert.Equal ("Step three", await page.TextAsync (".step"));
			log = await page.TextAsync ("#log");

			// The last step is a Finish step: FinishButtonClick, and no step change.
			await page.ClickAndWaitAsync ("#wiz input[value='Finish']");
			Assert.Equal (After (log, "wiz.FinishButtonClick(2->2)"), await page.TextAsync ("#log"));
		});

		[Fact]
		public Task Calendar_raises_SelectionChanged_and_VisibleMonthChanged () => fixture.RunAsync (async page => {
			await page.GotoAsync ("/Events/Views.aspx");
			string log = await page.TextAsync ("#log");

			await page.ClickAndWaitAsync ("#cal a:text-is('15')");
			Assert.Equal (After (log, "cal.SelectionChanged(2026-03-15)"), log = await page.TextAsync ("#log"));

			// The next-month link is the last link in the title row.
			await page.PostBackAsync (() => page.Locator ("#cal tr").First.Locator ("a").Last.ClickAsync ());
			Assert.Equal (After (log, "cal.VisibleMonthChanged(2026-03->2026-04)"), await page.TextAsync ("#log"));
		});
	}
}
