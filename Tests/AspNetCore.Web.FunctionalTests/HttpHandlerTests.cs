//
// The M2 gate, as the sample's HelloHandler calls it: if these pass then AppDomain data, the
// configuration system, <httpHandlers> resolution, HttpApplication, HttpRequest/HttpResponse and the
// Kestrel worker-request bridge are all working.
//

using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Xunit;

namespace WebFormsPort.FunctionalTests
{
	[Collection (WebFormsCollection.Name)]
	public class HttpHandlerTests
	{
		readonly SampleAppFixture fixture;

		public HttpHandlerTests (SampleAppFixture fixture)
		{
			this.fixture = fixture;
		}

		[Fact]
		public async Task Custom_handler_registered_in_web_config_serves_the_request ()
		{
			using HttpClient client = fixture.CreateClient ();

			HttpResponseMessage response = await client.GetAsync ("/hello.hello?q=abc");
			string body = await response.Content.ReadAsStringAsync ();

			Assert.Equal (HttpStatusCode.OK, response.StatusCode);
			Assert.Equal ("text/plain", response.Content.Headers.ContentType.MediaType);
			Assert.Contains ("Hello from the ported System.Web running under Kestrel.", body);
		}

		[Fact]
		public async Task Handler_sees_request_line_and_query_string ()
		{
			using HttpClient client = fixture.CreateClient ();

			string body = await client.GetStringAsync ("/hello.hello?q=abc");

			Assert.Contains ("Method:      GET", body);
			Assert.Contains ("Path:        /hello.hello", body);
			Assert.Contains ("RawUrl:      /hello.hello?q=abc", body);
			Assert.Contains ("Query q:     abc", body);
		}

		[Fact]
		public async Task Handler_response_headers_survive_the_worker_request_bridge ()
		{
			using HttpClient client = fixture.CreateClient ();

			HttpResponseMessage response = await client.GetAsync ("/hello.hello");

			Assert.Equal ("m2", Assert.Single (response.Headers.GetValues ("X-WebForms-Port")));
		}

		[Fact]
		public async Task Http_module_from_web_config_runs_on_begin_and_end_request ()
		{
			using HttpClient client = fixture.CreateClient ();

			HttpResponseMessage response = await client.GetAsync ("/hello.hello");

			// BeginRequest set the header; EndRequest confirms HttpContext.Items survived between
			// the two events, which is the part that would break if the context were per-event.
			Assert.Equal ("begin", Assert.Single (response.Headers.GetValues ("X-Custom-Module")));
			Assert.Equal ("yes", Assert.Single (response.Headers.GetValues ("X-Custom-Module-Saw-Items")));
		}

		[Fact]
		public async Task Global_asax_application_begin_request_runs ()
		{
			using HttpClient client = fixture.CreateClient ();

			HttpResponseMessage response = await client.GetAsync ("/hello.hello");

			// global.asax is compiled by BuildManager into an HttpApplication subclass and its
			// handlers are wired by NAME, not by an interface - so this also covers that discovery.
			Assert.Equal ("begin-request", Assert.Single (response.Headers.GetValues ("X-Global-Asax")));
		}

		[Fact]
		public async Task Server_MapPath_resolves_against_the_application_physical_path ()
		{
			using HttpClient client = fixture.CreateClient ();

			string body = await client.GetStringAsync ("/hello.hello");

			Assert.Contains ("PhysicalApp: " + fixture.AppPhysicalPath, body);
			Assert.Contains ("MapPath(~/): " + fixture.AppPhysicalPath, body);
		}

		[Fact]
		public async Task Third_party_library_reading_ConfigurationManager_sees_web_config ()
		{
			using HttpClient client = fixture.CreateClient ();

			string body = await client.GetStringAsync ("/hello.hello");

			// thirdparty-probe is compiled against the PACKAGED
			// System.Configuration.ConfigurationManager, not Core.Configuration. That it resolves
			// from that assembly and still returns web.config's values is the whole point of
			// Core.Web.ConfigBridge.
			Assert.Contains ("resolvedFrom=System.Configuration.ConfigurationManager", body);
			Assert.Contains ("probeSetting=seen-by-third-party-library", body);
			Assert.Contains ("connectionString=Server=.;Database=probe", body);
			Assert.DoesNotContain ("ThirdParty:  FAILED", body);
		}
	}
}
