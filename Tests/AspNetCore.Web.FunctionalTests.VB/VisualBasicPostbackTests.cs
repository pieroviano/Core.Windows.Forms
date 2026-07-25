//
// Postbacks, forms authentication and VB page methods. These exercise the same runtime paths the C#
// suite does; what differs is that every compiled unit involved came out of the VB backend.
//

using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace WebFormsPort.FunctionalTests.VB
{
	[Collection (WebFormsVBCollection.Name)]
	public class VisualBasicPostbackTests
	{
		readonly SampleAppVBFixture fixture;

		public VisualBasicPostbackTests (SampleAppVBFixture fixture)
		{
			this.fixture = fixture;
		}

		[Fact]
		public async Task Button_click_handler_written_in_VB_fires ()
		{
			using HttpClient client = fixture.CreateClient ();

			string page = await client.GetStringAsync ("/Default.aspx");

			string body = await (await client.PostAsync ("/Default.aspx",
				WebForm.Postback (page, ("who", "piero"), ("greet", "Greet")))).Content.ReadAsStringAsync ();

			Assert.Contains ("Hello, piero!", body);
			Assert.Contains ("hello again (postback)", body);
		}

		[Fact]
		public async Task View_state_property_written_in_VB_survives_the_postback ()
		{
			using HttpClient client = fixture.CreateClient ();

			string page = await client.GetStringAsync ("/Default.aspx");
			Assert.Contains ("<span id=\"counter\">0</span>", page);

			string first = await (await client.PostAsync ("/Default.aspx",
				WebForm.Postback (page, ("who", "a"), ("greet", "Greet")))).Content.ReadAsStringAsync ();
			Assert.Contains ("<span id=\"counter\">1</span>", first);

			string second = await (await client.PostAsync ("/Default.aspx",
				WebForm.Postback (first, ("who", "a"), ("greet", "Greet")))).Content.ReadAsStringAsync ();
			Assert.Contains ("<span id=\"counter\">2</span>", second);
		}

		[Fact]
		public async Task Server_side_validation_blocks_the_VB_click_handler ()
		{
			using HttpClient client = fixture.CreateClient ();

			string page = await client.GetStringAsync ("/Default.aspx");

			string body = await (await client.PostAsync ("/Default.aspx",
				WebForm.Postback (page, ("who", ""), ("greet", "Greet")))).Content.ReadAsStringAsync ();

			Assert.Contains ("(validation failed: a name is required)", body);
		}

		[Theory]
		[InlineData ("Echo", "{\"text\":\"hi\"}", "{\"d\":\"vb page method echo: hi\"}")]
		[InlineData ("Multiply", "{\"a\":6,\"b\":7}", "{\"d\":42}")]
		// Option Strict Off again: "41" + 1 is late-bound arithmetic that must produce 42, not "411".
		[InlineData ("LateBound", "{\"text\":\"41\"}", "{\"d\":42}")]
		public async Task Shared_VB_page_method_is_reachable_as_json (string method, string request, string expected)
		{
			using HttpClient client = fixture.CreateClient ();

			HttpResponseMessage response = await client.PostAsync ("/PageMethods.aspx/" + method,
				new StringContent (request, Encoding.UTF8, "application/json"));
			string body = await response.Content.ReadAsStringAsync ();

			Assert.Equal (HttpStatusCode.OK, response.StatusCode);
			Assert.Equal (expected, body);
		}

		[Fact]
		public async Task Anonymous_request_to_the_protected_directory_redirects_to_the_login_page ()
		{
			using HttpClient client = fixture.CreateClient ();

			HttpResponseMessage response = await client.GetAsync ("/Secure/Secret.aspx");

			Assert.Equal (HttpStatusCode.Redirect, response.StatusCode);
			Assert.Contains ("Login.aspx", response.Headers.Location.OriginalString);
		}

		[Fact]
		public async Task Signing_in_unlocks_the_protected_VB_page ()
		{
			using HttpClient client = fixture.CreateClient ();

			const string loginUrl = "/Login.aspx?ReturnUrl=%2fSecure%2fSecret.aspx";

			string login = await client.GetStringAsync (loginUrl);

			HttpResponseMessage posted = await client.PostAsync (loginUrl,
				WebForm.Postback (login, ("user", "piero"), ("pass", "secret"), ("go", "Sign in")));

			Assert.Equal (HttpStatusCode.Redirect, posted.StatusCode);

			HttpResponseMessage secret = await client.GetAsync ("/Secure/Secret.aspx");
			string body = await secret.Content.ReadAsStringAsync ();

			Assert.Equal (HttpStatusCode.OK, secret.StatusCode);
			Assert.Contains ("authenticated=True", body);
			Assert.Contains ("name=piero", body);
		}
	}
}
