//
// mode="StateServer" through Mono's SessionStateServerHandler, against Tools/state-server running as
// its own process.
//

using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Xunit;

namespace WebFormsPort.RemotingTests
{
	[Collection (RemotingCollection.Name)]
	public class RemoteStateServerTests
	{
		readonly RemotingSampleFixture fixture;

		public RemoteStateServerTests (RemotingSampleFixture fixture)
		{
			this.fixture = fixture;
		}

		[Fact]
		public async Task Session_reports_state_server_mode_and_keeps_values_across_requests ()
		{
			using HttpClient client = fixture.CreateClient ();

			string first = await Page (client, "?op=set&key=colour&value=blue");
			string second = await Page (client, "?op=get&key=colour");

			Assert.Equal ("StateServer", Field (first, "mode"));
			Assert.Equal (Field (first, "id"), Field (second, "id"));
			Assert.Equal ("blue", Field (second, "result"));
			Assert.Equal ("2", Field (second, "hits"));
		}

		[Fact]
		public async Task Two_clients_have_separate_sessions ()
		{
			using HttpClient first = fixture.CreateClient ();
			using HttpClient second = fixture.CreateClient ();

			await Page (first, "?op=set&key=owner&value=first");
			string read = await Page (second, "?op=get&key=owner");

			Assert.Equal ("(null)", Field (read, "result"));
		}

		/// <summary>
		/// The sessions live in the state server process, not in the application: replace that process
		/// and they are gone, while the application itself was never touched.
		/// </summary>
		[Fact]
		public async Task Sessions_live_in_the_state_server_process ()
		{
			using HttpClient client = fixture.CreateClient ();
			await Page (client, "?op=set&key=colour&value=green");
			Assert.Equal ("green", Field (await Page (client, "?op=get&key=colour"), "result"));

			fixture.RestartStateServer ();

			string after = await Page (client, "?op=get&key=colour");
			Assert.Equal ("(null)", Field (after, "result"));
			Assert.Equal ("1", Field (after, "hits"));
		}

		static async Task<string> Page (HttpClient client, string query)
		{
			HttpResponseMessage response = await client.GetAsync ("/Session.aspx" + query);
			string body = await response.Content.ReadAsStringAsync ();
			Assert.True (response.IsSuccessStatusCode, body);
			return body;
		}

		static string Field (string body, string label)
		{
			Match match = Regex.Match (body, "<p>" + label + @":\s*(?<value>.*?)</p>", RegexOptions.Singleline);
			Assert.True (match.Success, "no '" + label + "' in: " + body);
			return match.Groups ["value"].Value.Trim ();
		}
	}
}
