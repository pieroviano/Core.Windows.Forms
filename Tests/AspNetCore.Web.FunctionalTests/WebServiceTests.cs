//
// Core.Web.Services (.asmx / SOAP) and the Script.Services half of Core.Web.Extensions (JSON page
// methods). Both are reached through handler registrations in the generated root-web.config, so
// these also cover gen-config.ps1's assembly retargeting.
//

using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace WebFormsPort.FunctionalTests
{
	[Collection (WebFormsCollection.Name)]
	public class WebServiceTests
	{
		readonly SampleAppFixture fixture;

		public WebServiceTests (SampleAppFixture fixture)
		{
			this.fixture = fixture;
		}

		[Fact]
		public async Task Asmx_help_page_renders ()
		{
			using HttpClient client = fixture.CreateClient ();

			// This is DefaultWsdlHelpGenerator.aspx, extracted next to machine.config by
			// Port/MachineConfig.cs and compiled like any other page.
			HttpResponseMessage response = await client.GetAsync ("/Calc.asmx");

			Assert.Equal (HttpStatusCode.OK, response.StatusCode);
		}

		[Fact]
		public async Task Asmx_serves_a_wsdl_document ()
		{
			using HttpClient client = fixture.CreateClient ();

			string wsdl = await client.GetStringAsync ("/Calc.asmx?wsdl");

			Assert.Contains ("<wsdl:definitions", wsdl);
			Assert.Contains ("targetNamespace=\"http://webformsport.example/\"", wsdl);
			Assert.Contains ("name=\"Add\"", wsdl);
			Assert.Contains ("name=\"Echo\"", wsdl);
		}

		[Fact]
		public async Task Soap_call_invokes_the_web_method ()
		{
			using HttpClient client = fixture.CreateClient ();

			var request = new HttpRequestMessage (HttpMethod.Post, "/Calc.asmx") {
				Content = new StringContent (Envelope ("<Add xmlns=\"http://webformsport.example/\"><a>17</a><b>25</b></Add>"),
							     Encoding.UTF8, "text/xml"),
			};
			request.Headers.Add ("SOAPAction", "\"http://webformsport.example/Add\"");

			HttpResponseMessage response = await client.SendAsync (request);
			string body = await response.Content.ReadAsStringAsync ();

			Assert.Equal (HttpStatusCode.OK, response.StatusCode);
			Assert.Contains ("<AddResult>42</AddResult>", body);
		}

		[Fact]
		public async Task Soap_call_round_trips_a_string ()
		{
			using HttpClient client = fixture.CreateClient ();

			var request = new HttpRequestMessage (HttpMethod.Post, "/Calc.asmx") {
				Content = new StringContent (Envelope ("<Echo xmlns=\"http://webformsport.example/\"><text>hi</text></Echo>"),
							     Encoding.UTF8, "text/xml"),
			};
			request.Headers.Add ("SOAPAction", "\"http://webformsport.example/Echo\"");

			string body = await (await client.SendAsync (request)).Content.ReadAsStringAsync ();

			Assert.Contains ("<EchoResult>echo: hi</EchoResult>", body);
		}

		[Theory]
		[InlineData ("Echo", "{\"text\":\"hi\"}", "{\"d\":\"page method echo: hi\"}")]
		[InlineData ("Multiply", "{\"a\":6,\"b\":7}", "{\"d\":42}")]
		public async Task Static_page_method_is_reachable_as_json (string method, string request, string expected)
		{
			using HttpClient client = fixture.CreateClient ();

			// ScriptModule intercepts the JSON POST to the page's own URL before the page lifecycle
			// runs. This is Core.Web.Extensions' RestHandler, reached via ScriptHandlerFactory.
			HttpResponseMessage response = await client.PostAsync ("/PageMethods.aspx/" + method,
				new StringContent (request, Encoding.UTF8, "application/json"));
			string body = await response.Content.ReadAsStringAsync ();

			Assert.Equal (HttpStatusCode.OK, response.StatusCode);
			Assert.Equal (expected, body);
		}

		[Fact]
		public async Task Instance_web_method_on_a_page_is_not_exposed ()
		{
			using HttpClient client = fixture.CreateClient ();

			// Only STATIC [WebMethod]s are page methods. Exposing the instance one would be a
			// remote-code-execution surface the framework never had.
			HttpResponseMessage response = await client.PostAsync ("/PageMethods.aspx/NotAPageMethod",
				new StringContent ("{}", Encoding.UTF8, "application/json"));

			Assert.False (response.IsSuccessStatusCode,
				"an instance [WebMethod] was exposed as a page method");
		}

		[Fact]
		public async Task Page_with_a_ScriptManager_and_UpdatePanel_renders ()
		{
			using HttpClient client = fixture.CreateClient ();

			HttpResponseMessage response = await client.GetAsync ("/Ajax.aspx");
			string body = await response.Content.ReadAsStringAsync ();

			Assert.Equal (HttpStatusCode.OK, response.StatusCode);
			// ScriptManager emits the client framework from Core.Web.Extensions via ScriptResource.axd.
			Assert.Contains ("ScriptResource.axd", body);
			Assert.Contains ("not updated yet", body);
		}

		static string Envelope (string body)
		{
			return "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
			       "<soap:Envelope xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" " +
			       "xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\" " +
			       "xmlns:soap=\"http://schemas.xmlsoap.org/soap/envelope/\">" +
			       "<soap:Body>" + body + "</soap:Body></soap:Envelope>";
		}
	}
}
