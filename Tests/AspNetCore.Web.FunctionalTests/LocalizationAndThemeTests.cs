//
// Culture handling, the App_GlobalResources/App_LocalResources compilers, themes/skins and the
// 3.5-era data controls.
//

using System.Net.Http;
using System.Threading.Tasks;
using Xunit;

namespace WebFormsPort.FunctionalTests
{
	[Collection (WebFormsCollection.Name)]
	public class LocalizationAndThemeTests
	{
		readonly SampleAppFixture fixture;

		public LocalizationAndThemeTests (SampleAppFixture fixture)
		{
			this.fixture = fixture;
		}

		[Fact]
		public async Task Page_Culture_directive_sets_the_request_culture ()
		{
			using HttpClient client = fixture.CreateClient ();

			string body = await client.GetStringAsync ("/Locale.aspx");

			Assert.Contains ("de-DE / de-DE", body);
		}

		[Fact]
		public async Task Numbers_and_dates_format_in_the_page_culture ()
		{
			using HttpClient client = fixture.CreateClient ();

			string body = await client.GetStringAsync ("/Locale.aspx");

			// Set on the request thread by the Culture/UICulture directives, not by the process locale.
			Assert.Contains ("1.234,50", body);
			Assert.Contains ("04.03.2026", body);
		}

		[Fact]
		public async Task Local_resources_bind_through_meta_resourcekey ()
		{
			using HttpClient client = fixture.CreateClient ();

			string body = await client.GetStringAsync ("/Locale.aspx");

			// App_LocalResources/Locale.aspx.resx, compiled at runtime and applied by the parser.
			Assert.Contains ("text from App_LocalResources", body);
		}

		[Fact]
		public async Task Global_resources_are_compiled_and_reachable_from_code ()
		{
			using HttpClient client = fixture.CreateClient ();

			string body = await client.GetStringAsync ("/Features.aspx");

			// App_GlobalResources/Strings.resx becomes a strongly typed Resources.Strings class.
			Assert.Contains ("Hello from a global resource", body);
		}

		[Fact]
		public async Task Theme_skin_is_applied_to_controls ()
		{
			using HttpClient client = fixture.CreateClient ();

			string body = await client.GetStringAsync ("/Features.aspx");

			// App_Themes/Basic/Basic.skin sets ForeColor=Green and Font-Bold on every Label.
			Assert.Contains ("color:Green", body);
			Assert.Contains ("font-weight:bold", body);
		}

		[Fact]
		public async Task ListView_FormView_and_DetailsView_render ()
		{
			using HttpClient client = fixture.CreateClient ();

			string body = await client.GetStringAsync ("/Locale.aspx");

			Assert.Contains ("<li>alpha=1</li>", body);
			Assert.Contains ("<li>beta=2</li>", body);
			Assert.Contains ("<li>gamma=3</li>", body);
			Assert.Contains ("FormView: alpha", body);
		}

		[Fact]
		public async Task DataList_and_sitemap_navigation_render ()
		{
			using HttpClient client = fixture.CreateClient ();

			string body = await client.GetStringAsync ("/Features.aspx");

			Assert.Contains ("alpha x 1", body);
			Assert.Contains ("beta x 2", body);
		}

		[Fact]
		public async Task Output_cached_page_serves_the_same_stamp_twice ()
		{
			using HttpClient client = fixture.CreateClient ();

			// <%@ OutputCache Duration="3600" VaryByParam="none" %>: the second request must be
			// served from the cache, so the tick stamped during the first render is reused.
			string first = await client.GetStringAsync ("/Features.aspx");
			string second = await client.GetStringAsync ("/Features.aspx");

			Assert.Equal (Stamp (first), Stamp (second));
		}

		static string Stamp (string html)
		{
			var match = System.Text.RegularExpressions.Regex.Match (html, @"rendered at tick (\d+)");
			Assert.True (match.Success, "no output-cache stamp in: " + html);
			return match.Groups [1].Value;
		}
	}
}
