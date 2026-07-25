//
// The postback half of the page lifecycle: view state round-tripping, server-side validation and
// control events raised from a form POST.
//

using System.Net.Http;
using System.Threading.Tasks;
using Xunit;

namespace WebFormsPort.FunctionalTests
{
	[Collection (WebFormsCollection.Name)]
	public class PostbackTests
	{
		readonly SampleAppFixture fixture;

		public PostbackTests (SampleAppFixture fixture)
		{
			this.fixture = fixture;
		}

		[Fact]
		public async Task Button_click_event_fires_and_reads_the_posted_TextBox ()
		{
			using HttpClient client = fixture.CreateClient ();

			string page = await client.GetStringAsync ("/Default.aspx");

			HttpResponseMessage response = await client.PostAsync ("/Default.aspx",
				WebForm.Postback (page, ("who", "piero"), ("greet", "Greet")));
			string body = await response.Content.ReadAsStringAsync ();

			response.EnsureSuccessStatusCode ();
			Assert.Contains ("Hello, piero!", body);
			Assert.Contains ("hello again (postback)", body);
			Assert.Contains ("IsPostBack=True, HttpMethod=POST", body);
		}

		[Fact]
		public async Task View_state_carries_a_value_across_the_postback ()
		{
			using HttpClient client = fixture.CreateClient ();

			string page = await client.GetStringAsync ("/Default.aspx");
			Assert.Contains ("<span id=\"counter\">0</span>", page);

			// PostbackCount lives in ViewState, not a field, so surviving the round trip is the
			// whole assertion.
			string first = await (await client.PostAsync ("/Default.aspx",
				WebForm.Postback (page, ("who", "a"), ("greet", "Greet")))).Content.ReadAsStringAsync ();
			Assert.Contains ("<span id=\"counter\">1</span>", first);

			string second = await (await client.PostAsync ("/Default.aspx",
				WebForm.Postback (first, ("who", "a"), ("greet", "Greet")))).Content.ReadAsStringAsync ();
			Assert.Contains ("<span id=\"counter\">2</span>", second);
		}

		[Fact]
		public async Task Server_side_validation_blocks_the_click_handler ()
		{
			using HttpClient client = fixture.CreateClient ();

			string page = await client.GetStringAsync ("/Default.aspx");

			// Empty required field. Client script is an optimisation; the server is the gate.
			string body = await (await client.PostAsync ("/Default.aspx",
				WebForm.Postback (page, ("who", ""), ("greet", "Greet")))).Content.ReadAsStringAsync ();

			Assert.Contains ("(validation failed: a name is required)", body);
			Assert.DoesNotContain ("Hello, ", body);
		}

		[Fact]
		public async Task Data_bound_grid_survives_a_postback_without_rebinding ()
		{
			using HttpClient client = fixture.CreateClient ();

			string page = await client.GetStringAsync ("/Default.aspx");

			string body = await (await client.PostAsync ("/Default.aspx",
				WebForm.Postback (page, ("who", "piero"), ("greet", "Greet")))).Content.ReadAsStringAsync ();

			// The page binds the GridView only when !IsPostBack, so these rows can only be coming
			// back out of view state.
			Assert.Contains ("<td>1</td><td>sprocket</td><td>9.99</td>", body);
			Assert.Contains ("<td>3</td><td>grommet</td><td>3.75</td>", body);
		}

		[Fact]
		public async Task Html_encoding_is_applied_to_posted_input ()
		{
			using HttpClient client = fixture.CreateClient ();

			string page = await client.GetStringAsync ("/Default.aspx");

			// '&' passes request validation but still has to come back encoded.
			string body = await (await client.PostAsync ("/Default.aspx",
				WebForm.Postback (page, ("who", "a & b"), ("greet", "Greet")))).Content.ReadAsStringAsync ();

			Assert.Contains ("Hello, a &amp; b!", body);
		}

		[Fact]
		public async Task Request_validation_rejects_markup_in_a_posted_value ()
		{
			using HttpClient client = fixture.CreateClient ();

			string page = await client.GetStringAsync ("/Default.aspx");

			// ASP.NET's request validation refuses "<script>" in form data before the page's own code
			// runs, and does so by failing the request rather than sanitising it. Preserving that is
			// a security property, not a quirk: applications were written assuming it.
			HttpResponseMessage response = await client.PostAsync ("/Default.aspx",
				WebForm.Postback (page, ("who", "<script>"), ("greet", "Greet")));

			Assert.False (response.IsSuccessStatusCode,
				      "request validation did not reject markup in a posted value");
		}
	}
}
