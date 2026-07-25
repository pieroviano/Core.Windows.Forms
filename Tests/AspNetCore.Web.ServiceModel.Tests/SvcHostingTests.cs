//
// .svc endpoints served over real HTTP by Samples/WcfSample.
//

using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Xunit;

namespace WebFormsPort.ServiceModelTests
{
	[Collection (SvcCollection.Name)]
	public class SvcHostingTests
	{
		const string Ns = "http://webformsport.example/";

		readonly SvcFixture fixture;

		public SvcHostingTests (SvcFixture fixture)
		{
			this.fixture = fixture;
		}

		[Fact]
		public async Task Svc_file_is_reachable_at_its_own_path ()
		{
			// The whole point of the .svc convention: the address is the file's location. Nothing in
			// the application configures /Echo.svc as a route.
			HttpResponseMessage response = await fixture.PostSoapAsync (
				"/Echo.svc", Ns + "IEcho/Say",
				$"<Say xmlns=\"{Ns}\"><text>hello</text></Say>");

			string body = await response.Content.ReadAsStringAsync ();

			Assert.Equal (HttpStatusCode.OK, response.StatusCode);
			Assert.Contains ("<SayResult>wcf echo: hello</SayResult>", body);
		}

		[Fact]
		public async Task Soap_response_is_a_soap_envelope ()
		{
			HttpResponseMessage response = await fixture.PostSoapAsync (
				"/Echo.svc", Ns + "IEcho/Add",
				$"<Add xmlns=\"{Ns}\"><a>17</a><b>25</b></Add>");

			string body = await response.Content.ReadAsStringAsync ();

			Assert.Equal (HttpStatusCode.OK, response.StatusCode);
			Assert.Contains ("http://schemas.xmlsoap.org/soap/envelope/", body);
			Assert.Contains ("<AddResult>42</AddResult>", body);
			Assert.StartsWith ("text/xml", response.Content.Headers.ContentType.MediaType);
		}

		[Fact]
		public async Task Svc_in_a_sub_directory_is_reachable_at_that_sub_path ()
		{
			// Api/Calculator.svc - the address follows the directory, and the contract is declared on
			// the service class itself rather than on an interface.
			HttpResponseMessage response = await fixture.PostSoapAsync (
				"/Api/Calculator.svc", Ns + "CalculatorService/Multiply",
				$"<Multiply xmlns=\"{Ns}\"><a>6</a><b>7</b></Multiply>");

			string body = await response.Content.ReadAsStringAsync ();

			Assert.Equal (HttpStatusCode.OK, response.StatusCode);
			Assert.Contains ("<MultiplyResult>42</MultiplyResult>", body);
		}

		[Fact]
		public async Task Wsdl_is_served_from_the_query_string ()
		{
			// A .svc has always answered ?wsdl, and it is how clients generate proxies. If this
			// regressed, an existing client's build would break rather than its calls.
			using HttpClient client = fixture.CreateClient ();
			HttpResponseMessage response = await client.GetAsync ("/Echo.svc?wsdl");
			string body = await response.Content.ReadAsStringAsync ();

			Assert.Equal (HttpStatusCode.OK, response.StatusCode);
			Assert.Contains ("wsdl:definitions", body);
			Assert.Contains ("IEcho", body);
		}

		[Fact]
		public async Task Wsdl_advertises_the_address_the_request_arrived_on ()
		{
			// A generated client reads its endpoint address out of the WSDL. If the port or scheme
			// there is not the one the metadata was fetched from, every generated proxy points
			// somewhere that does not answer.
			using HttpClient client = fixture.CreateClient ();
			string body = await client.GetStringAsync ("/Echo.svc?singleWsdl");

			Assert.Contains (fixture.BaseAddress.TrimEnd ('/') + "/Echo.svc", body);
		}

		[Fact]
		public async Task Aspx_pages_in_the_same_application_still_serve ()
		{
			// UseSvcEndpoints goes in front of UseWebForms, so it has the first look at every request.
			// This asserts it only claims the paths it discovered and passes the rest along.
			using HttpClient client = fixture.CreateClient ();
			HttpResponseMessage response = await client.GetAsync ("/Default.aspx");
			string body = await response.Content.ReadAsStringAsync ();

			Assert.Equal (HttpStatusCode.OK, response.StatusCode);
			Assert.Contains ("webforms alive", body);
		}

		[Fact]
		public async Task Unknown_paths_are_not_swallowed_by_the_svc_middleware ()
		{
			using HttpClient client = fixture.CreateClient ();
			HttpResponseMessage response = await client.GetAsync ("/NoSuchService.svc");

			Assert.NotEqual (HttpStatusCode.OK, response.StatusCode);
		}

		[Fact]
		public async Task Both_discovered_services_are_registered ()
		{
			Assert.Equal (
				new [] { "/Api/Calculator.svc", "/Echo.svc" },
				System.Web.ServiceModel.ServiceHostDirective
					.Discover (RepoPaths.Sample ("WcfSample"))
					.Select (d => d.VirtualPath)
					.OrderBy (p => p)
					.ToArray ());
		}
	}
}
