//
// Master pages, user controls and App_Code - the pieces BuildManager compiles separately and stitches
// together at request time.
//

using System.Net.Http;
using System.Threading.Tasks;
using Xunit;

namespace WebFormsPort.FunctionalTests
{
	[Collection (WebFormsCollection.Name)]
	public class CompositionTests
	{
		readonly SampleAppFixture fixture;

		public CompositionTests (SampleAppFixture fixture)
		{
			this.fixture = fixture;
		}

		[Fact]
		public async Task Master_page_chrome_wraps_the_content_page ()
		{
			using HttpClient client = fixture.CreateClient ();

			string body = await client.GetStringAsync ("/Content.aspx");

			Assert.Contains ("<div id=\"chrome-header\">[master header]</div>", body);
			Assert.Contains ("<div id=\"chrome-footer\">[master footer]</div>", body);
			Assert.Contains ("<h1>Content from the child page</h1>", body);
			// The default placeholder content must be replaced, not appended.
			Assert.DoesNotContain ("(default master content)", body);
		}

		[Fact]
		public async Task Content_placeholder_in_the_head_is_filled ()
		{
			using HttpClient client = fixture.CreateClient ();

			string body = await client.GetStringAsync ("/Content.aspx");

			Assert.Contains ("Content page", body);
		}

		[Fact]
		public async Task User_control_renders_once_per_instance_with_its_own_properties ()
		{
			using HttpClient client = fixture.CreateClient ();

			string body = await client.GetStringAsync ("/Content.aspx");

			Assert.Contains ("<strong>first</strong>", body);
			Assert.Contains ("<span> count=3</span>", body);
			Assert.Contains ("<strong>second</strong>", body);
			Assert.Contains ("<span> count=7</span>", body);
		}

		[Fact]
		public async Task App_Code_is_compiled_at_runtime_and_usable_from_markup ()
		{
			using HttpClient client = fixture.CreateClient ();

			string body = await client.GetStringAsync ("/Content.aspx");

			// Helper.Shout lives in App_Code, which the SDK deliberately does NOT compile - it is
			// built by BuildManager's AppCodeCompiler through the Roslyn backend at first request.
			Assert.Contains ("<p>App_Code helper: APP_CODE WORKS!</p>", body);
			// Same helper called from inside the user control, i.e. from a separately compiled unit.
			Assert.Contains ("<span> shouted=FIRST!</span>", body);
			Assert.Contains ("<span> shouted=SECOND!</span>", body);
		}
	}
}
