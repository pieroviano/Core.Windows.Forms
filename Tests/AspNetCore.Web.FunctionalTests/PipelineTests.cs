//
// Redirects, Server.Transfer, forms authentication and the status codes System.Web's own handler
// mapping produces for paths that must never be served.
//

using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Xunit;

namespace WebFormsPort.FunctionalTests
{
	[Collection (WebFormsCollection.Name)]
	public class PipelineTests
	{
		readonly SampleAppFixture fixture;

		public PipelineTests (SampleAppFixture fixture)
		{
			this.fixture = fixture;
		}

		[Fact]
		public async Task Response_Redirect_sends_a_302_with_a_resolved_location ()
		{
			using HttpClient client = fixture.CreateClient ();

			HttpResponseMessage response = await client.GetAsync ("/Redirect.aspx");

			Assert.Equal (HttpStatusCode.Redirect, response.StatusCode);
			// "~/Simple.aspx" must be resolved against the application root before it goes on the wire.
			Assert.Equal ("/Simple.aspx", response.Headers.Location.OriginalString);
		}

		[Fact]
		public async Task Server_Transfer_serves_the_target_page_without_a_round_trip ()
		{
			using HttpClient client = fixture.CreateClient ();

			HttpResponseMessage response = await client.GetAsync ("/Redirect.aspx?to=transfer");
			string body = await response.Content.ReadAsStringAsync ();

			Assert.Equal (HttpStatusCode.OK, response.StatusCode);
			Assert.Contains ("<h1>Simple page</h1>", body);
		}

		[Fact]
		public async Task Unmapped_page_returns_404 ()
		{
			using HttpClient client = fixture.CreateClient ();

			HttpResponseMessage response = await client.GetAsync ("/NoSuchPage.aspx");

			Assert.Equal (HttpStatusCode.NotFound, response.StatusCode);
		}

		[Theory]
		[InlineData ("/DefaultPage.cs")]
		[InlineData ("/web.config")]
		public async Task Protected_file_types_are_forbidden (string path)
		{
			using HttpClient client = fixture.CreateClient ();

			// These come from root-web.config's <httpHandlers>, mapped to HttpForbiddenHandler.
			// Applications depend on it: source and configuration must never be downloadable.
			HttpResponseMessage response = await client.GetAsync (path);

			Assert.Equal (HttpStatusCode.Forbidden, response.StatusCode);
		}

		[Fact]
		public async Task Anonymous_request_to_a_protected_directory_redirects_to_the_login_page ()
		{
			using HttpClient client = fixture.CreateClient ();

			// Secure/web.config denies '?'; UrlAuthorizationModule must turn that into a redirect to
			// the loginUrl from the root web.config, carrying the original path as ReturnUrl.
			HttpResponseMessage response = await client.GetAsync ("/Secure/Secret.aspx");

			Assert.Equal (HttpStatusCode.Redirect, response.StatusCode);
			Assert.Contains ("Login.aspx", response.Headers.Location.OriginalString);
			Assert.Contains ("ReturnUrl=%2fSecure%2fSecret.aspx", response.Headers.Location.OriginalString);
		}

		[Fact]
		public async Task Signing_in_issues_a_ticket_that_unlocks_the_protected_page ()
		{
			using HttpClient client = fixture.CreateClient ();

			const string loginUrl = "/Login.aspx?ReturnUrl=%2fSecure%2fSecret.aspx";

			string login = await client.GetStringAsync (loginUrl);

			HttpResponseMessage posted = await client.PostAsync (loginUrl,
				WebForm.Postback (login, ("user", "piero"), ("pass", "secret"), ("go", "Sign in")));

			// RedirectFromLoginPage sets the .WEBFORMSAUTH cookie and redirects to ReturnUrl.
			Assert.Equal (HttpStatusCode.Redirect, posted.StatusCode);
			Assert.Contains ("/Secure/Secret.aspx", posted.Headers.Location.OriginalString);

			// The cookie is in this client's jar now, so the protected page must serve.
			HttpResponseMessage secret = await client.GetAsync ("/Secure/Secret.aspx");
			string body = await secret.Content.ReadAsStringAsync ();

			Assert.Equal (HttpStatusCode.OK, secret.StatusCode);
			Assert.Contains ("authenticated=True", body);
			Assert.Contains ("name=piero", body);
		}

		[Fact]
		public async Task Bad_credentials_do_not_issue_a_ticket ()
		{
			using HttpClient client = fixture.CreateClient ();

			string login = await client.GetStringAsync ("/Login.aspx");

			HttpResponseMessage posted = await client.PostAsync ("/Login.aspx",
				WebForm.Postback (login, ("user", "piero"), ("pass", "wrong"), ("go", "Sign in")));
			string body = await posted.Content.ReadAsStringAsync ();

			Assert.Equal (HttpStatusCode.OK, posted.StatusCode);
			Assert.Contains ("bad credentials", body);

			// Still anonymous, so the protected page still bounces.
			HttpResponseMessage secret = await client.GetAsync ("/Secure/Secret.aspx");
			Assert.Equal (HttpStatusCode.Redirect, secret.StatusCode);
		}
	}
}
