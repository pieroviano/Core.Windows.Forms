//
// mode="StateServer" over real HTTP, against a real IDistributedCache.
//

using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web.SessionState;
using Xunit;

namespace WebFormsPort.SessionStateTests
{
	[Collection (SessionStateCollection.Name)]
	public class StateServerTests
	{
		readonly SessionStateFixture fixture;

		public StateServerTests (SessionStateFixture fixture)
		{
			this.fixture = fixture;
		}

		[Fact]
		public async Task Session_reports_the_configured_mode ()
		{
			// If the module had fallen back to SessionInProcHandler this would say InProc, and every
			// other test in this file would still pass. It is the cheapest possible guard against
			// testing the wrong store entirely.
			using HttpClient client = fixture.CreateClient ();
			SessionResponse response = await fixture.RequestAsync (client);

			Assert.Equal ("StateServer", response.Mode);
		}

		[Fact]
		public async Task Session_id_and_contents_survive_across_requests ()
		{
			using HttpClient client = fixture.CreateClient ();

			SessionResponse first = await fixture.RequestAsync (client, "?op=set&key=colour&value=blue");
			SessionResponse second = await fixture.RequestAsync (client, "?op=get&key=colour");

			Assert.Equal (first.Id, second.Id);
			Assert.Equal ("blue", second.Result);
		}

		[Fact]
		public async Task Each_request_sees_the_incremented_counter ()
		{
			using HttpClient client = fixture.CreateClient ();

			Assert.Equal ("1", (await fixture.RequestAsync (client)).Hits);
			Assert.Equal ("2", (await fixture.RequestAsync (client)).Hits);
			Assert.Equal ("3", (await fixture.RequestAsync (client)).Hits);
		}

		[Fact]
		public async Task Two_clients_get_two_separate_sessions ()
		{
			using HttpClient first = fixture.CreateClient ();
			using HttpClient second = fixture.CreateClient ();

			SessionResponse written = await fixture.RequestAsync (first, "?op=set&key=owner&value=first");
			SessionResponse read = await fixture.RequestAsync (second, "?op=get&key=owner");

			Assert.NotEqual (written.Id, read.Id);
			Assert.Equal ("(null)", read.Result);
		}

		[Fact]
		public async Task The_distributed_cache_is_genuinely_the_backing_store ()
		{
			// The strongest available proof that this is out-of-process state and not an InProc store
			// wearing its name: reach past HTTP, delete the entry the cache holds for this session, and
			// watch the session come back empty. An in-process store would be untouched.
			using HttpClient client = fixture.CreateClient ();

			SessionResponse before = await fixture.RequestAsync (client, "?op=set&key=colour&value=blue");
			Assert.Equal ("1", before.Hits);

			string key = DistributedCacheSessionStateStore.GetCacheKey ("/", before.Id);
			Assert.NotNull (fixture.Cache.Get (key));

			fixture.Cache.Remove (key);

			SessionResponse after = await fixture.RequestAsync (client, "?op=get&key=colour");
			Assert.Equal ("(null)", after.Result);
			Assert.Equal ("1", after.Hits);
		}

		[Fact]
		public async Task Abandon_discards_the_session ()
		{
			using HttpClient client = fixture.CreateClient ();

			await fixture.RequestAsync (client, "?op=set&key=colour&value=blue");
			await fixture.RequestAsync (client, "?op=abandon");

			SessionResponse after = await fixture.RequestAsync (client, "?op=get&key=colour");
			Assert.Equal ("(null)", after.Result);
		}

		[Fact]
		public async Task Remove_drops_one_key_and_leaves_the_rest ()
		{
			using HttpClient client = fixture.CreateClient ();

			await fixture.RequestAsync (client, "?op=set&key=keep&value=yes");
			await fixture.RequestAsync (client, "?op=set&key=drop&value=yes");
			await fixture.RequestAsync (client, "?op=remove&key=drop");

			Assert.Equal ("(null)", (await fixture.RequestAsync (client, "?op=get&key=drop")).Result);
			Assert.Equal ("yes", (await fixture.RequestAsync (client, "?op=get&key=keep")).Result);
		}

		[Fact]
		public async Task Values_keep_their_type_across_the_round_trip ()
		{
			// Serialization is where out-of-process state can quietly lose fidelity - a value coming
			// back as its ToString () would satisfy every test above and break real code.
			using HttpClient client = fixture.CreateClient ();

			await fixture.RequestAsync (client, "?op=setint&key=count&value=42");
			Assert.Equal ("System.Int32", (await fixture.RequestAsync (client, "?op=type&key=count")).Result);
			Assert.Equal ("42", (await fixture.RequestAsync (client, "?op=get&key=count")).Result);

			await fixture.RequestAsync (client, "?op=setdate&key=when&value=2026-07-26T13:45:00");
			Assert.Equal ("System.DateTime", (await fixture.RequestAsync (client, "?op=type&key=when")).Result);
		}

		[Fact]
		public async Task A_payload_larger_than_the_short_item_limit_round_trips_intact ()
		{
			// 20 KB is well past ASPState's 7000-byte SessionItemShort column. The distributed store has
			// no such split, but the same session has to work under either mode, so it is checked here
			// too - and "intact" means every byte, not merely non-null.
			using HttpClient client = fixture.CreateClient ();

			await fixture.RequestAsync (client, "?op=big&key=blob&size=20000");
			SessionResponse verified = await fixture.RequestAsync (client, "?op=verifybig&key=blob&size=20000");

			Assert.Equal ("intact 20000", verified.Result);
		}

		[Fact]
		public async Task An_unserializable_value_fails_with_a_message_naming_the_mode ()
		{
			// This is the migration hazard the docs warn about: InProc never serialised, so a type that
			// worked for years fails the day the mode changes. The error has to say so - "cannot
			// serialize NotSerializable" alone would send someone hunting the wrong change.
			using HttpClient client = fixture.CreateClient ();
			SessionResponse response = await fixture.RequestAsync (client, "?op=unserializable&key=bad");

			Assert.Equal (HttpStatusCode.InternalServerError, response.StatusCode);
			Assert.Contains ("could not be serialized for out-of-process storage", response.Body);
			Assert.Contains ("StateServer", response.Body);
			Assert.Contains ("NotSerializable", response.Body);
		}

		[Fact]
		public void Cache_keys_are_namespaced_by_application ()
		{
			// Two applications sharing one Redis must not read each other's sessions; session ids are
			// only unique within an application.
			string root = DistributedCacheSessionStateStore.GetCacheKey ("/", "ABC123");
			string child = DistributedCacheSessionStateStore.GetCacheKey ("/shop", "ABC123");

			Assert.NotEqual (root, child);
			Assert.EndsWith ("ABC123", root);
			Assert.Contains ("shop", child);
		}
	}
}
