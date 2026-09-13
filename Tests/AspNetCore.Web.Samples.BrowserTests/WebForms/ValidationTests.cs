//
// Events/Validation.aspx: validation in the browser, and on the server when script is off.
//
// Reference source: BaseValidator.EvaluateIsValid implementations (only RequiredFieldValidator fails an
// empty value; CustomValidator.ServerValidate is not raised for one unless ValidateEmptyText),
// Button.RaisePostBackEvent (Page.Validate (ValidationGroup) before OnClick, only when CausesValidation),
// Page.IsValid (every validator's IsValid, whether or not its group was validated).
//

using System.Threading.Tasks;
using Microsoft.Playwright;
using Xunit;

namespace WebFormsPort.SamplesBrowserTests.WebForms
{
	[Collection (WebFormsCollection.Name)]
	public class ValidationTests
	{
		readonly WebFormsFixture fixture;

		public ValidationTests (WebFormsFixture fixture)
		{
			this.fixture = fixture;
		}

		static async Task FillValidAsync (IPage page)
		{
			await page.FillAsync ("#name", "Ann");
			await page.FillAsync ("#age", "30");
			await page.FillAsync ("#email", "ann@example.org");
			await page.FillAsync ("#pwd", "pw");
			await page.FillAsync ("#confirm", "pw");
			await page.FillAsync ("#code", "ab");
		}

		// --- with script: validators run in the browser --------------------------------------------

		[Fact]
		public Task Client_validation_blocks_an_invalid_submit () => fixture.RunAsync (async page => {
			await page.GotoAsync ("/Events/Validation.aspx");

			await page.AssertNoNavigationAsync (() => page.ClickAsync ("#submit"));

			Assert.True (await page.IsVisibleAsync ("#nameRequired"));
			Assert.Contains ("Name is required", await page.TextAsync ("#summary"));
			Assert.Equal ("(no events yet)", await page.TextAsync ("#log"));
		});

		[Fact]
		public Task Client_validation_checks_range_format_comparison_and_custom_function () => fixture.RunAsync (async page => {
			await page.GotoAsync ("/Events/Validation.aspx");

			await page.FillAsync ("#name", "Ann");
			await page.FillAsync ("#age", "12");
			await page.FillAsync ("#email", "not-an-email");
			await page.FillAsync ("#pwd", "a");
			await page.FillAsync ("#confirm", "b");
			await page.FillAsync ("#code", "abc");

			await page.AssertNoNavigationAsync (() => page.ClickAsync ("#submit"));

			Assert.False (await page.IsVisibleAsync ("#nameRequired"));
			Assert.True (await page.IsVisibleAsync ("#ageRange"));
			Assert.True (await page.IsVisibleAsync ("#emailFormat"));
			Assert.True (await page.IsVisibleAsync ("#confirmMatches"));
			Assert.True (await page.IsVisibleAsync ("#codeEven"));
		});

		[Fact]
		public Task Valid_form_posts_and_the_server_validates_again_before_Click () => fixture.RunAsync (async page => {
			await page.GotoAsync ("/Events/Validation.aspx");

			await FillValidAsync (page);
			await page.ClickAndWaitAsync ("#submit");

			Assert.Equal ("code.ServerValidate(ab) | submit.Click(IsValid=True)", await page.TextAsync ("#log"));
		});

		[Fact]
		public Task CausesValidation_false_posts_without_validating () => fixture.RunAsync (async page => {
			await page.GotoAsync ("/Events/Validation.aspx");

			await page.ClickAndWaitAsync ("#skip");

			Assert.Equal ("skip.Click", await page.TextAsync ("#log"));
			Assert.False (await page.IsVisibleAsync ("#nameRequired"));
		});

		[Fact]
		public Task A_validation_group_validates_only_its_own_validators () => fixture.RunAsync (async page => {
			await page.GotoAsync ("/Events/Validation.aspx");

			await page.AssertNoNavigationAsync (() => page.ClickAsync ("#find"));
			Assert.True (await page.IsVisibleAsync ("#searchRequired"));
			Assert.False (await page.IsVisibleAsync ("#nameRequired"));

			// The main form is still empty and invalid - it is not in the "search" group.
			await page.FillAsync ("#search", "widgets");
			await page.ClickAndWaitAsync ("#find");

			Assert.Equal ("find.Click(IsValid=True)", await page.TextAsync ("#log"));
		});

		// --- without script: the form always posts, the server is the gate -------------------------

		[Fact]
		public Task Server_validation_fails_an_empty_required_field () => fixture.RunAsync (async page => {
			await page.GotoAsync ("/Events/Validation.aspx");

			await page.ClickAndWaitAsync ("#submit");

			// Empty values are valid for every validator except RequiredFieldValidator, and
			// CustomValidator does not raise ServerValidate for them at all.
			Assert.Equal ("submit.Click(IsValid=False)", await page.TextAsync ("#log"));
			Assert.True (await page.IsVisibleAsync ("#nameRequired"));
			Assert.False (await page.IsVisibleAsync ("#ageRange"));
			Assert.Contains ("Name is required", await page.TextAsync ("#summary"));
		}, javaScriptEnabled: false);

		[Fact]
		public Task Server_validation_runs_every_validator_and_the_summary_lists_each_failure () => fixture.RunAsync (async page => {
			await page.GotoAsync ("/Events/Validation.aspx");

			await page.FillAsync ("#name", "Ann");
			await page.FillAsync ("#age", "12");
			await page.FillAsync ("#email", "not-an-email");
			await page.FillAsync ("#pwd", "a");
			await page.FillAsync ("#confirm", "b");
			await page.FillAsync ("#code", "abc");
			await page.ClickAndWaitAsync ("#submit");

			Assert.Equal ("code.ServerValidate(abc) | submit.Click(IsValid=False)", await page.TextAsync ("#log"));
			Assert.False (await page.IsVisibleAsync ("#nameRequired"));
			Assert.True (await page.IsVisibleAsync ("#ageRange"));
			Assert.True (await page.IsVisibleAsync ("#emailFormat"));
			Assert.True (await page.IsVisibleAsync ("#confirmMatches"));
			Assert.True (await page.IsVisibleAsync ("#codeEven"));

			string summary = await page.TextAsync ("#summary");
			Assert.DoesNotContain ("Name is required", summary);
			foreach (string message in new [] { "Age must be 18-99", "Email is malformed", "Passwords differ", "Code must have an even length" })
				Assert.Contains (message, summary);

			// Posted values are re-rendered, so the user can correct them.
			Assert.Equal ("12", await page.InputValueAsync ("#age"));
		}, javaScriptEnabled: false);

		[Fact]
		public Task Server_validates_only_the_group_of_the_button_pressed () => fixture.RunAsync (async page => {
			await page.GotoAsync ("/Events/Validation.aspx");

			await page.ClickAndWaitAsync ("#find");

			Assert.Equal ("find.Click(IsValid=False)", await page.TextAsync ("#log"));
			Assert.True (await page.IsVisibleAsync ("#searchRequired"));
			Assert.False (await page.IsVisibleAsync ("#nameRequired"));
		}, javaScriptEnabled: false);
	}
}
