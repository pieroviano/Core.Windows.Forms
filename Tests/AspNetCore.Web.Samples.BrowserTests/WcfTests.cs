//
// Samples/WcfSample: a WebForms page and two .svc endpoints in one application.
//

using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Playwright;
using Xunit;

namespace WebFormsPort.SamplesBrowserTests
{
	[Collection (WcfCollection.Name)]
	public class WcfTests
	{
		readonly WcfFixture fixture;

		public WcfTests (WcfFixture fixture)
		{
			this.fixture = fixture;
		}

		const string Soap = @"async ([url, action, body]) => {
			const r = await fetch(url, { method: 'POST', headers: { 'Content-Type': 'text/xml; charset=utf-8', 'SOAPAction': action },
				body: '<s:Envelope xmlns:s=""http://schemas.xmlsoap.org/soap/envelope/""><s:Body>' + body + '</s:Body></s:Envelope>' });
			return { status: r.status, body: await r.text() };
		}";

		[Fact]
		public Task WebForms_page_is_served_next_to_the_services () => fixture.RunAsync (async page => {
			await page.GotoAsync ("/Default.aspx");

			Assert.Equal ("WCF sample", await page.TitleAsync ());
			Assert.Equal ("webforms alive", await page.TextAsync ("#Status"));
		});

		[Fact]
		public Task Service_metadata_is_browsable () => fixture.RunAsync (async page => {
			IResponse wsdl = await page.GotoAsync ("/Echo.svc?wsdl");

			Assert.Equal (200, wsdl.Status);
			string body = await wsdl.TextAsync ();
			Assert.Contains ("wsdl:definitions", body);
			Assert.Contains ("IEcho", body);
		});

		[Fact]
		public Task Soap_calls_from_the_browser_reach_both_services () => fixture.RunAsync (async page => {
			await page.GotoAsync ("/Default.aspx");

			JsonElement say = await page.EvaluateAsync<JsonElement> (Soap, new object [] {
				"/Echo.svc", "http://webformsport.example/IEcho/Say",
				"<Say xmlns=\"http://webformsport.example/\"><text>browser</text></Say>" });
			Assert.Equal (200, say.GetProperty ("status").GetInt32 ());
			Assert.Contains ("<SayResult>wcf echo: browser</SayResult>", say.GetProperty ("body").GetString ());

			JsonElement multiply = await page.EvaluateAsync<JsonElement> (Soap, new object [] {
				"/Api/Calculator.svc", "http://webformsport.example/CalculatorService/Multiply",
				"<Multiply xmlns=\"http://webformsport.example/\"><a>6</a><b>7</b></Multiply>" });
			Assert.Equal (200, multiply.GetProperty ("status").GetInt32 ());
			Assert.Contains ("<MultiplyResult>42</MultiplyResult>", multiply.GetProperty ("body").GetString ());
		});
	}
}
