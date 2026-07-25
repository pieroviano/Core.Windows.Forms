//
// Session, application state, and the IStateObjectSerializer replacement for BinaryFormatter.
//

using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Xunit;

namespace WebFormsPort.FunctionalTests
{
	[Collection (WebFormsCollection.Name)]
	public class StateTests
	{
		readonly SampleAppFixture fixture;

		public StateTests (SampleAppFixture fixture)
		{
			this.fixture = fixture;
		}

		[Fact]
		public async Task Session_persists_across_requests_on_one_client ()
		{
			// A fresh cookie jar, so this client is a brand new visitor.
			using HttpClient client = fixture.CreateClient ();

			string first = await client.GetStringAsync ("/Session.aspx");
			string second = await client.GetStringAsync ("/Session.aspx");

			Assert.Contains ("visits=1", first);
			Assert.Contains ("isNew=True", first);

			Assert.Contains ("visits=2", second);
			Assert.Contains ("isNew=False", second);

			Assert.Equal (SessionId (first), SessionId (second));
		}

		[Fact]
		public async Task Separate_clients_get_separate_sessions ()
		{
			using HttpClient first = fixture.CreateClient ();
			using HttpClient second = fixture.CreateClient ();

			string a = await first.GetStringAsync ("/Session.aspx");
			string b = await second.GetStringAsync ("/Session.aspx");

			Assert.NotEqual (SessionId (a), SessionId (b));
			// Session_Start ran for each, so both are on their first visit.
			Assert.Contains ("visits=1", a);
			Assert.Contains ("visits=1", b);
		}

		[Fact]
		public async Task Application_state_is_shared_and_initialised_by_Application_Start ()
		{
			using HttpClient client = fixture.CreateClient ();

			string body = await client.GetStringAsync ("/Session.aspx");

			Assert.Contains ("appStarted=True", body);

			// Application ["requests"] is incremented under Application.Lock on every request, so a
			// later request must report a strictly higher count than an earlier one.
			int before = AppRequests (body);
			int after = AppRequests (await client.GetStringAsync ("/Session.aspx"));

			Assert.True (after > before, $"application request counter did not advance: {before} -> {after}");
		}

		[Fact]
		public async Task Custom_type_round_trips_through_view_state_and_session ()
		{
			using HttpClient client = fixture.CreateClient ();

			string first = await client.GetStringAsync ("/State.aspx");

			// Basket is a plain DTO - the kind upstream pushed through BinaryFormatter, which is gone
			// on .NET 8. It survives here only because the host installed a JsonStateObjectSerializer.
			Assert.Contains ("viewstate: owner=piero items=1 total=4.25 | session items=1", first);

			string second = await (await client.PostAsync ("/State.aspx",
				WebForm.Postback (first, ("go", "Post back")))).Content.ReadAsStringAsync ();

			Assert.Contains ("viewstate: owner=piero items=2 total=8.50 | session items=2", second);
		}

		[Fact]
		public async Task Unserialisable_state_object_is_refused_with_a_diagnostic_naming_its_type ()
		{
			using HttpClient client = fixture.CreateClient ();

			// ?reject=1 puts a delegate in view state. The contract is an explicit refusal naming the
			// offending type, NOT a silent re-encoding under some other scheme.
			HttpResponseMessage response = await client.GetAsync ("/State.aspx?reject=1");
			string body = await response.Content.ReadAsStringAsync ();

			Assert.False (response.IsSuccessStatusCode,
				"storing an unserialisable object in view state was accepted silently");
			Assert.Contains ("Action", body);
		}

		static string SessionId (string html)
		{
			Match match = Regex.Match (html, @"sessionId=(\w+)");
			Assert.True (match.Success, "no sessionId in: " + html);
			return match.Groups [1].Value;
		}

		static int AppRequests (string html)
		{
			Match match = Regex.Match (html, @"appRequests=(\d+)");
			Assert.True (match.Success, "no appRequests in: " + html);
			return int.Parse (match.Groups [1].Value);
		}
	}
}
