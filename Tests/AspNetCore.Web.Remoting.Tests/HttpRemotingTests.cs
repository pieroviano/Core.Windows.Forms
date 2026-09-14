//
// *.rem endpoints: web.config's <system.runtime.remoting> section served through the WebForms pipeline.
//
// The remoting calls are made from a child domain - another process - because a reference used in the
// process that published it resolves to the real object without touching any channel, and would prove
// nothing about http.
//

using System;
using System.Net;
using System.Net.Http;
using System.Runtime.Remoting;
using System.Runtime.Remoting.Activation;
using System.Text;
using System.Threading.Tasks;
using RemotingSample;
using Xunit;

namespace WebFormsPort.RemotingTests
{
	[Collection (RemotingCollection.Name)]
	public class HttpRemotingTests : IDisposable
	{
		readonly RemotingSampleFixture fixture;
		readonly ChildAppDomain client;
		readonly RemotingClient remote;

		public HttpRemotingTests (RemotingSampleFixture fixture)
		{
			this.fixture = fixture;
			client = AppDomain.CurrentDomain.CreateChildDomain ("remoting-client", new AppDomainSetup2 {
				ApplicationBase = AppContext.BaseDirectory,
				StartupTimeout = TimeSpan.FromSeconds (60),
			});
			remote = client.CreateInstanceAndUnwrap<RemotingClient> ();
		}

		public void Dispose () => client.Unload ();

		[Fact]
		public void A_well_known_object_from_web_config_answers_over_http_from_another_process ()
		{
			string url = fixture.BaseAddress + "/Calculator.rem";

			Assert.Equal (5, remote.Add (url, 2, 3));
			Assert.Equal (Environment.ProcessId + " /", remote.WhereAmI (url));   // served by the web application
			Assert.NotEqual (Environment.ProcessId, remote.ProcessId);          // called from elsewhere
		}

		[Fact]
		public void A_serializable_result_and_a_server_exception_cross_the_wire ()
		{
			string url = fixture.BaseAddress + "/Calculator.rem";

			Assert.Equal ("3 r1", remote.Divide (url, 10, 3));
			Assert.Equal (typeof (DivideByZeroException).FullName, remote.DivideFailure (url, 1, 0));
		}

		[Fact]
		public void A_client_activated_type_from_web_config_is_activated_through_the_application_url ()
		{
			Assert.Equal ("11 12 101", remote.Count (fixture.BaseAddress, 10, 100));
		}

		[Fact]
		public void An_object_nobody_published_is_reported_by_the_server ()
		{
			string message = remote.AddFailure (fixture.BaseAddress + "/Missing.rem");

			Assert.Contains ("Missing.rem", message);
		}

		[Fact]
		public async Task A_get_request_is_refused ()
		{
			using HttpClient http = fixture.CreateClient ();

			HttpResponseMessage response = await http.GetAsync ("/Calculator.rem");

			Assert.Equal (HttpStatusCode.MethodNotAllowed, response.StatusCode);
		}

		[Fact]
		public async Task A_soap_request_is_refused_naming_the_supported_format ()
		{
			using HttpClient http = fixture.CreateClient ();

			HttpResponseMessage response = await http.PostAsync ("/Calculator.soap",
				new StringContent ("<soap:Envelope/>", Encoding.UTF8, "text/xml"));
			string text = await response.Content.ReadAsStringAsync ();

			Assert.Equal (HttpStatusCode.UnsupportedMediaType, response.StatusCode);
			Assert.Contains ("application/octet-stream", text);
		}
	}

	/// <summary>Runs in the child domain and makes the remoting calls from there.</summary>
	public class RemotingClient : MarshalByRefObject
	{
		public virtual int ProcessId => Environment.ProcessId;

		static ICalculator Calculator (string url)
			=> (ICalculator) RemotingServices.Connect (typeof (ICalculator), url);

		public virtual int Add (string url, int a, int b) => Calculator (url).Add (a, b);

		public virtual string WhereAmI (string url) => Calculator (url).WhereAmI ();

		public virtual string Divide (string url, int a, int b)
		{
			DivisionResult result = Calculator (url).Divide (a, b);
			return result.Quotient + " r" + result.Remainder;
		}

		public virtual string DivideFailure (string url, int a, int b)
		{
			try {
				Calculator (url).Divide (a, b);
				return "no exception";
			} catch (Exception e) {
				return e.GetType ().FullName;
			}
		}

		public virtual string AddFailure (string url)
		{
			try {
				Calculator (url).Add (1, 1);
				return "no exception";
			} catch (RemotingException e) {
				return e.Message;
			}
		}

		public virtual string Count (string applicationUrl, int firstStart, int secondStart)
		{
			RemotingConfiguration.AllowAssembly (typeof (Counter).Assembly);
			Counter first = RemotingActivator.CreateInstanceAt<Counter> (applicationUrl, firstStart);
			Counter second = RemotingActivator.CreateInstanceAt<Counter> (applicationUrl, secondStart);

			if (!RemotingServices.IsTransparentProxy (first))
				return "not a proxy";

			return first.Next () + " " + first.Next () + " " + second.Next ();
		}
	}
}
