//
// The ASP.NET Core authentication bridge, and the host-supplied machine.config.
//

using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Xunit;

namespace WebFormsPort.HostingTests
{
	[Collection (HostingCollection.Name)]
	public class AspNetCoreAuthenticationTests
	{
		readonly AuthenticatedHostFixture fixture;

		public AspNetCoreAuthenticationTests (AuthenticatedHostFixture fixture)
		{
			this.fixture = fixture;
		}

		[Fact]
		public async Task Authenticated_caller_reaches_a_directory_that_denies_anonymous ()
		{
			using HttpClient client = fixture.CreateClient (AuthenticatedHostFixture.UserName);

			// Secure/web.config denies '?'. Without the bridge System.Web sees an anonymous caller
			// and UrlAuthorizationModule bounces this to the login page, exactly as it does for the
			// unauthenticated case below.
			HttpResponseMessage response = await client.GetAsync ("/Secure/Secret.aspx");
			string body = await response.Content.ReadAsStringAsync ();

			Assert.Equal (HttpStatusCode.OK, response.StatusCode);
			Assert.Contains ("authenticated=True", body);
		}

		[Fact]
		public async Task Page_sees_the_name_ASP_NET_Core_authenticated ()
		{
			using HttpClient client = fixture.CreateClient (AuthenticatedHostFixture.UserName);

			string body = await client.GetStringAsync ("/Secure/Secret.aspx");

			// User.Identity.Name inside the page comes from the ClaimsPrincipal the ASP.NET Core
			// handler produced - no forms authentication ticket is involved anywhere here.
			Assert.Contains ("name=" + AuthenticatedHostFixture.UserName, body);
		}

		[Fact]
		public async Task Different_callers_are_seen_as_different_principals ()
		{
			using HttpClient first = fixture.CreateClient ("alice");
			using HttpClient second = fixture.CreateClient ("bob");

			Assert.Contains ("name=alice", await first.GetStringAsync ("/Secure/Secret.aspx"));
			Assert.Contains ("name=bob", await second.GetStringAsync ("/Secure/Secret.aspx"));
		}

		[Fact]
		public async Task Unauthenticated_caller_is_still_denied ()
		{
			// No X-Test-User header, so the ASP.NET Core handler returns NoResult and the bridge must
			// leave the context anonymous. Turning authorization off for everyone would be the
			// obvious way for this feature to go wrong.
			using HttpClient client = fixture.CreateClient ();

			HttpResponseMessage response = await client.GetAsync ("/Secure/Secret.aspx");

			Assert.Equal (HttpStatusCode.Redirect, response.StatusCode);
			Assert.Contains ("Login.aspx", response.Headers.Location.OriginalString);
		}

		[Fact]
		public async Task Anonymous_pages_are_unaffected ()
		{
			using HttpClient client = fixture.CreateClient ();

			HttpResponseMessage response = await client.GetAsync ("/Simple.aspx");

			Assert.Equal (HttpStatusCode.OK, response.StatusCode);
			Assert.Contains ("<h1>Simple page</h1>", await response.Content.ReadAsStringAsync ());
		}

		[Fact]
		public async Task Authenticated_caller_can_still_use_ordinary_pages_and_postbacks ()
		{
			using HttpClient client = fixture.CreateClient (AuthenticatedHostFixture.UserName);

			// The bridge assigns a principal on every request; the rest of the pipeline must not care.
			string page = await client.GetStringAsync ("/Default.aspx");

			string body = await (await client.PostAsync ("/Default.aspx",
				WebForm.Postback (page, ("who", "piero"), ("greet", "Greet")))).Content.ReadAsStringAsync ();

			Assert.Contains ("Hello, piero!", body);
		}
	}
}
