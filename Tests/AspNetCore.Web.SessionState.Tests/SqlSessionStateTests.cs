//
// mode="SQLServer" against a real ASPState database.
//
// Driven through SqlSessionStateStore directly rather than over HTTP, and that is forced rather than
// chosen: the ported runtime hosts one application per process, the hosted application has already
// declared mode="StateServer" in its web.config, and a mode cannot change under a running runtime. The
// store is the whole of what mode="SQLServer" adds, so exercising it directly loses nothing except the
// module's own plumbing - which the StateServer suite covers over HTTP.
//
// Every test that needs a server carries [SqlFact] and is SKIPPED, with a reason, when none is
// reachable. Set WEBFORMSPORT_SQL_CONNECTIONSTRING to point at one.
//

using System;
using System.Linq;
using System.Web;
using System.Web.SessionState;
using Microsoft.Data.SqlClient;
using Xunit;

namespace WebFormsPort.SessionStateTests
{
	[Collection (SessionStateCollection.Name)]
	public class SqlSessionStateTests
	{
		readonly SessionStateFixture fixture;

		public SqlSessionStateTests (SessionStateFixture fixture)
		{
			// Needed even for the schema-script test: HostingEnvironment and the configuration system
			// only exist once an application is running.
			this.fixture = fixture;
		}

		// -----------------------------------------------------------------------------------------
		// No server required.
		// -----------------------------------------------------------------------------------------

		[Fact]
		public void The_schema_script_defines_every_procedure_the_provider_calls ()
		{
			// The script is an embedded resource and the provider calls the procedures by name, so
			// nothing but this test connects the two. A rename on one side would otherwise surface as a
			// runtime failure on a machine that has SQL Server - i.e. not on most of them.
			string script = SqlSessionStateStore.GetSchemaScript ();

			string [] called = {
				"TempGetAppID", "TempGetStateItem3", "TempGetStateItemExclusive3",
				"TempReleaseStateItemExclusive", "TempInsertUninitializedItem",
				"TempInsertStateItemShort", "TempInsertStateItemLong",
				"TempUpdateStateItemShortNullLong", "TempUpdateStateItemLongNullShort",
				"TempRemoveStateItem", "TempResetTimeout",
			};

			foreach (string procedure in called)
				Assert.Contains ("PROCEDURE dbo." + procedure, script);

			Assert.Contains ("ASPStateTempSessions", script);
			Assert.Contains ("ASPStateTempApplications", script);
		}

		[Fact]
		public void The_schema_script_is_batched_so_it_can_be_executed ()
		{
			// CREATE OR ALTER PROCEDURE must be the first statement in its batch, so the GO separators
			// are load-bearing, not cosmetic - EnsureSchema splits on them.
			string script = SqlSessionStateStore.GetSchemaScript ();
			int batches = script.Split ('\n').Count (line => line.Trim () == "GO");

			Assert.True (batches >= 12, "expected one GO per object, found " + batches);
		}

		// -----------------------------------------------------------------------------------------
		// Server required.
		// -----------------------------------------------------------------------------------------

		[SqlFact]
		public void EnsureSchema_creates_the_database_objects_and_is_idempotent ()
		{
			// Run twice: a deployment step that cannot be repeated safely is one people are afraid to
			// run at all.
			SqlSessionStateStore.EnsureSchema (SqlAvailability.ConnectionString);
			SqlSessionStateStore.EnsureSchema (SqlAvailability.ConnectionString);

			Assert.Equal (1, Scalar<int> ("SELECT COUNT(*) FROM sys.tables WHERE name = 'ASPStateTempSessions'"));
			Assert.Equal (1, Scalar<int> ("SELECT COUNT(*) FROM sys.procedures WHERE name = 'TempGetStateItemExclusive3'"));
		}

		[SqlFact]
		public void A_session_written_through_the_store_reads_back ()
		{
			SqlSessionStateStore store = CreateStore ();
			string id = NewSessionId ();

			var items = new SessionStateItemCollection ();
			items ["colour"] = "blue";
			items ["count"] = 42;

			store.SetAndReleaseItemExclusive (null, id, Data (items), lockId: null, newItem: true);

			SessionStateStoreData read = store.GetItem (null, id, out bool locked, out TimeSpan lockAge,
								    out object lockId, out SessionStateActions actions);

			Assert.False (locked);
			Assert.NotNull (read);
			Assert.Equal ("blue", read.Items ["colour"]);
			Assert.Equal (42, read.Items ["count"]);
			Assert.Equal (SessionStateActions.None, actions);
		}

		[SqlFact]
		public void An_unknown_session_reads_as_absent_rather_than_empty ()
		{
			// The module distinguishes "no such session" (start a new one) from "a session with nothing
			// in it". Returning an empty collection for a missing id would make every expired session
			// look alive.
			SqlSessionStateStore store = CreateStore ();

			SessionStateStoreData read = store.GetItem (null, NewSessionId (), out bool locked,
								    out TimeSpan lockAge, out object lockId,
								    out SessionStateActions actions);

			Assert.Null (read);
			Assert.False (locked);
		}

		[SqlFact]
		public void An_exclusive_read_locks_out_the_next_one ()
		{
			// This is what SQL Server mode buys over the distributed-cache store: the lock is a row
			// update inside a stored procedure, so it is genuinely atomic.
			SqlSessionStateStore store = CreateStore ();
			string id = NewSessionId ();

			store.SetAndReleaseItemExclusive (null, id, Data (new SessionStateItemCollection ()),
							  lockId: null, newItem: true);

			SessionStateStoreData first = store.GetItemExclusive (null, id, out bool firstLocked,
									      out TimeSpan _, out object firstLockId,
									      out SessionStateActions _);

			Assert.NotNull (first);
			Assert.False (firstLocked);

			SessionStateStoreData second = store.GetItemExclusive (null, id, out bool secondLocked,
									       out TimeSpan lockAge, out object secondLockId,
									       out SessionStateActions _);

			Assert.Null (second);
			Assert.True (secondLocked);
			Assert.Equal (firstLockId, secondLockId);
			Assert.True (lockAge >= TimeSpan.Zero);
		}

		[SqlFact]
		public void Releasing_the_lock_lets_the_next_reader_in ()
		{
			SqlSessionStateStore store = CreateStore ();
			string id = NewSessionId ();

			store.SetAndReleaseItemExclusive (null, id, Data (new SessionStateItemCollection ()),
							  lockId: null, newItem: true);
			store.GetItemExclusive (null, id, out bool _, out TimeSpan _, out object lockId,
						out SessionStateActions _);

			store.ReleaseItemExclusive (null, id, lockId);

			SessionStateStoreData after = store.GetItemExclusive (null, id, out bool locked, out TimeSpan _,
									      out object _, out SessionStateActions _);

			Assert.False (locked);
			Assert.NotNull (after);
		}

		[SqlFact]
		public void A_write_without_the_lock_does_not_clobber_the_holder ()
		{
			SqlSessionStateStore store = CreateStore ();
			string id = NewSessionId ();

			var original = new SessionStateItemCollection ();
			original ["owner"] = "holder";
			store.SetAndReleaseItemExclusive (null, id, Data (original), lockId: null, newItem: true);

			store.GetItemExclusive (null, id, out bool _, out TimeSpan _, out object lockId,
						out SessionStateActions _);

			var intruder = new SessionStateItemCollection ();
			intruder ["owner"] = "intruder";

			// A stale cookie - what a request whose lock has already been taken over would carry.
			store.SetAndReleaseItemExclusive (null, id, Data (intruder),
							  lockId: Convert.ToInt32 (lockId) + 99, newItem: false);

			store.ReleaseItemExclusive (null, id, lockId);
			SessionStateStoreData read = store.GetItem (null, id, out bool _, out TimeSpan _, out object _,
								    out SessionStateActions _);

			Assert.Equal ("holder", read.Items ["owner"]);
		}

		[SqlFact]
		public void A_payload_past_the_short_item_limit_uses_the_long_column_and_round_trips ()
		{
			SqlSessionStateStore store = CreateStore ();
			string id = NewSessionId ();

			string big = new string ('x', 20000);
			var items = new SessionStateItemCollection ();
			items ["blob"] = big;

			store.SetAndReleaseItemExclusive (null, id, Data (items), lockId: null, newItem: true);

			// Read back through the store, and separately confirm it really went to SessionItemLong -
			// the two halves of the claim, and only the second one catches a silent truncation to 7000.
			SessionStateStoreData read = store.GetItem (null, id, out bool _, out TimeSpan _, out object _,
								    out SessionStateActions _);

			Assert.Equal (big, read.Items ["blob"]);
			Assert.Equal (1, Scalar<int> (
				"SELECT COUNT(*) FROM ASPStateTempSessions " +
				"WHERE SessionId LIKE '" + id + "%' AND SessionItemLong IS NOT NULL AND SessionItemShort IS NULL"));
		}

		[SqlFact]
		public void A_session_that_shrinks_back_under_the_limit_clears_the_long_column ()
		{
			// The reason SetAndReleaseItemExclusive always uses the *Null* update variants. Leaving a
			// stale SessionItemLong behind would let a later read pick up the old, larger session.
			SqlSessionStateStore store = CreateStore ();
			string id = NewSessionId ();

			var big = new SessionStateItemCollection ();
			big ["blob"] = new string ('x', 20000);
			store.SetAndReleaseItemExclusive (null, id, Data (big), lockId: null, newItem: true);

			store.GetItemExclusive (null, id, out bool _, out TimeSpan _, out object lockId,
						out SessionStateActions _);

			var small = new SessionStateItemCollection ();
			small ["blob"] = "tiny";
			store.SetAndReleaseItemExclusive (null, id, Data (small), lockId, newItem: false);

			SessionStateStoreData read = store.GetItem (null, id, out bool _, out TimeSpan _, out object _,
								    out SessionStateActions _);

			Assert.Equal ("tiny", read.Items ["blob"]);
			Assert.Equal (1, Scalar<int> (
				"SELECT COUNT(*) FROM ASPStateTempSessions " +
				"WHERE SessionId LIKE '" + id + "%' AND SessionItemLong IS NULL"));
		}

		[SqlFact]
		public void An_uninitialized_item_reports_InitializeItem_once ()
		{
			// The cookieless-session placeholder. The flag has to survive the round trip so the first
			// real request raises Session_Start - and has to be consumed, so a retry does not raise it
			// a second time.
			SqlSessionStateStore store = CreateStore ();
			string id = NewSessionId ();

			store.CreateUninitializedItem (null, id, 20);

			store.GetItemExclusive (null, id, out bool _, out TimeSpan _, out object lockId,
						out SessionStateActions first);
			Assert.Equal (SessionStateActions.InitializeItem, first);

			store.ReleaseItemExclusive (null, id, lockId);

			store.GetItemExclusive (null, id, out bool _, out TimeSpan _, out object _,
						out SessionStateActions second);
			Assert.Equal (SessionStateActions.None, second);
		}

		[SqlFact]
		public void RemoveItem_deletes_the_row ()
		{
			SqlSessionStateStore store = CreateStore ();
			string id = NewSessionId ();

			store.SetAndReleaseItemExclusive (null, id, Data (new SessionStateItemCollection ()),
							  lockId: null, newItem: true);
			store.GetItemExclusive (null, id, out bool _, out TimeSpan _, out object lockId,
						out SessionStateActions _);

			store.RemoveItem (null, id, lockId, null);

			Assert.Equal (0, Scalar<int> (
				"SELECT COUNT(*) FROM ASPStateTempSessions WHERE SessionId LIKE '" + id + "%'"));
		}

		[SqlFact]
		public void Two_applications_sharing_a_database_do_not_share_sessions ()
		{
			// ASPState keys rows on session id + application id precisely so this cannot happen; the
			// suffix comes from TempGetAppID. Same id, two suffixes, two rows.
			SqlSessionStateStore store = CreateStore ();
			string id = NewSessionId ();

			store.SetAndReleaseItemExclusive (null, id, Data (new SessionStateItemCollection ()),
							  lockId: null, newItem: true);

			string stored = Scalar<string> (
				"SELECT TOP 1 SessionId FROM ASPStateTempSessions WHERE SessionId LIKE '" + id + "%'");

			Assert.NotNull (stored);
			Assert.Equal (id.Length + 8, stored.Length);
			Assert.StartsWith (id, stored);
		}

		// -----------------------------------------------------------------------------------------

		static SqlSessionStateStore CreateStore ()
		{
			// Initialize reads SessionStateHostServices.SqlConnectionString, which the fixture set at
			// startup - the only moment it can be set, since the first store built freezes it.
			var store = new SqlSessionStateStore ();
			store.Initialize (null, null);
			return store;
		}

		static SessionStateStoreData Data (SessionStateItemCollection items)
		{
			// Null static objects: those are declared by the application in Global.asax and reconstructed
			// from the context, never stored. There is no context here and nothing needs one.
			return new SessionStateStoreData (items, null, 20);
		}

		static string NewSessionId ()
		{
			// Same shape as a real one - 24 upper-case characters - so the nvarchar(88) key and the
			// 8-character application suffix behave exactly as they would in production.
			return Guid.NewGuid ().ToString ("N").Substring (0, 24).ToUpperInvariant ();
		}

		static T Scalar<T> (string sql)
		{
			using (var connection = new SqlConnection (SqlAvailability.ConnectionString)) {
				connection.Open ();
				using (var command = new SqlCommand (sql, connection)) {
					object value = command.ExecuteScalar ();
					return value == null || value == DBNull.Value ? default : (T) value;
				}
			}
		}
	}
}
