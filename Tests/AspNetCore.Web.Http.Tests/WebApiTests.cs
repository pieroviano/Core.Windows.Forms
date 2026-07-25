//
// ASP.NET Web API hosted inside the ported System.Web.
//
// The request path is: UrlRoutingModule matches the route MapHttpRoute registered and calls
// RemapHandler with an HttpControllerHandler; that adapts HttpContext onto an HttpRequestMessage;
// Web API selects a controller and action by verb and route; and a media-type formatter negotiates
// the result onto the wire.
//
// None of it worked before the RemapHandler fix in Tools/port-patches.txt - a routed URL 404'd
// because no such file existed on disk - so these are part of that regression net too.
//

using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace WebFormsPort.WebApiTests
{
	[Collection (WebApiCollection.Name)]
	public class WebApiTests
	{
		readonly WebApiFixture fixture;

		public WebApiTests (WebApiFixture fixture)
		{
			this.fixture = fixture;
		}

		[Fact]
		public async Task Get_collection_returns_json ()
		{
			using HttpClient client = fixture.CreateClient ();

			HttpResponseMessage response = await client.GetAsync ("/api/widgets");
			string body = await response.Content.ReadAsStringAsync ();

			Assert.Equal (HttpStatusCode.OK, response.StatusCode);
			Assert.Equal ("application/json", response.Content.Headers.ContentType.MediaType);
			Assert.Contains ("\"Name\":\"sprocket\"", body);
			Assert.Contains ("\"Name\":\"grommet\"", body);
		}

		[Fact]
		public async Task Route_parameter_selects_the_by_id_overload ()
		{
			using HttpClient client = fixture.CreateClient ();

			// Two Get methods differing only in parameters; {id} in the route picks between them.
			string body = await client.GetStringAsync ("/api/widgets/2");

			Assert.Contains ("\"Id\":2", body);
			Assert.Contains ("\"Name\":\"flange\"", body);
			Assert.DoesNotContain ("sprocket", body);
		}

		[Fact]
		public async Task HttpResponseException_becomes_its_status_code ()
		{
			using HttpClient client = fixture.CreateClient ();

			// The action throws HttpResponseException for an unknown id. Web API has to translate that
			// into the response rather than let it escape as a 500 - which is what happened while the
			// SRResources manifest name was wrong, because the exception's own constructor reads a
			// resource string and threw MissingManifestResourceException on the way out.
			HttpResponseMessage response = await client.GetAsync ("/api/widgets/99");

			Assert.Equal (HttpStatusCode.NotFound, response.StatusCode);
		}

		[Fact]
		public async Task Unknown_controller_is_a_404 ()
		{
			using HttpClient client = fixture.CreateClient ();

			HttpResponseMessage response = await client.GetAsync ("/api/nosuch");

			Assert.Equal (HttpStatusCode.NotFound, response.StatusCode);
		}

		[Fact]
		public async Task Content_negotiation_honours_an_xml_accept_header ()
		{
			using HttpClient client = fixture.CreateClient ();

			var request = new HttpRequestMessage (HttpMethod.Get, "/api/widgets/1");
			request.Headers.Accept.Add (new MediaTypeWithQualityHeaderValue ("application/xml"));

			HttpResponseMessage response = await client.SendAsync (request);
			string body = await response.Content.ReadAsStringAsync ();

			Assert.Equal ("application/xml", response.Content.Headers.ContentType.MediaType);
			Assert.Contains ("<Name>sprocket</Name>", body);
		}

		[Fact]
		public async Task Json_is_the_default_when_nothing_is_requested ()
		{
			using HttpClient client = fixture.CreateClient ();

			HttpResponseMessage response = await client.GetAsync ("/api/widgets/1");

			Assert.Equal ("application/json", response.Content.Headers.ContentType.MediaType);
		}

		[Fact]
		public async Task Post_binds_a_json_body_and_returns_created ()
		{
			using HttpClient client = fixture.CreateClient ();

			HttpResponseMessage response = await client.PostAsync ("/api/widgets",
				new StringContent ("{\"Name\":\"cog\",\"Price\":5.5}", Encoding.UTF8, "application/json"));
			string body = await response.Content.ReadAsStringAsync ();

			Assert.Equal (HttpStatusCode.Created, response.StatusCode);
			Assert.Contains ("\"Name\":\"cog\"", body);
			// The server assigned the id, so the body is the round-tripped entity, not the request.
			Assert.DoesNotContain ("\"Id\":0", body);
		}

		[Fact]
		public async Task Post_binds_an_xml_body_too ()
		{
			using HttpClient client = fixture.CreateClient ();

			const string xml =
				"<Widget xmlns=\"http://schemas.datacontract.org/2004/07/WebApiSample.Controllers\">" +
				"<Name>bracket</Name><Price>7.25</Price></Widget>";

			HttpResponseMessage response = await client.PostAsync ("/api/widgets",
				new StringContent (xml, Encoding.UTF8, "application/xml"));

			Assert.Equal (HttpStatusCode.Created, response.StatusCode);
			Assert.Contains ("bracket", await response.Content.ReadAsStringAsync ());
		}

		[Fact]
		public async Task Verb_selects_the_action ()
		{
			using HttpClient client = fixture.CreateClient ();

			// The controller has no Delete, so the route matches but no action does.
			HttpResponseMessage response = await client.DeleteAsync ("/api/widgets/1");

			Assert.Equal (HttpStatusCode.MethodNotAllowed, response.StatusCode);
		}

		[Fact]
		public async Task Empty_json_body_is_rejected_by_the_action ()
		{
			using HttpClient client = fixture.CreateClient ();

			// The action guards for a null or nameless model, so this exercises model binding
			// producing null rather than throwing.
			HttpResponseMessage response = await client.PostAsync ("/api/widgets",
				new StringContent ("{}", Encoding.UTF8, "application/json"));

			Assert.Equal (HttpStatusCode.BadRequest, response.StatusCode);
		}
	}
}
