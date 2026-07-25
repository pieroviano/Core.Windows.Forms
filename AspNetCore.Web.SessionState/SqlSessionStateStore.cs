//
// <sessionState mode="SQLServer">, on the stock ASPState database.
//
// Why not Mono's SessionSQLServerHandler
// --------------------------------------
// It is still in the tree and still compiled - you can name it explicitly through mode="Custom" - but
// it is not what mode="SQLServer" resolves to any more, for two reasons. It targets a Mono-invented
// `Sessions` table rather than ASPState, so a migrating application's existing database does not fit
// it; and it reaches its schema through raw SQL over a DbProviderFactory that defaults to
// Mono.Data.Sqlite, an assembly this port excludes.
//
// This talks to the same stored procedures the real System.Web provider does - TempGetStateItem3,
// TempGetStateItemExclusive3, TempUpdateStateItem*, TempReleaseStateItemExclusive and friends - so a
// database provisioned by aspnet_regsql.exe works with no DDL changes at all. Locking is therefore a
// row update inside a stored procedure: genuinely atomic, unlike the distributed-cache store.
//
// What still does not carry across is the CONTENT of the blob. SessionItemShort/SessionItemLong
// written by .NET Framework hold BinaryFormatter payloads, and there is no BinaryFormatter on .NET 9+
// to read them. Point the ported application at its own ASPState database, or accept that existing
// rows are unreadable and will be replaced as users come back.
//
// The 7000-byte split between SessionItemShort (varbinary) and SessionItemLong (image) is ASPState's,
// not ours, and is preserved because the stored procedures enforce it.
//

using System;
using System.Collections.Specialized;
using System.Data;
using System.Web;
using System.Web.Configuration;
using System.Web.Hosting;
using Microsoft.Data.SqlClient;

namespace System.Web.SessionState
{
	/// <summary>
	/// Session store for <c>mode="SQLServer"</c>, using the stored procedures of the standard
	/// <c>ASPState</c> database.
	/// </summary>
	public class SqlSessionStateStore : SessionStateStoreProviderBase
	{
		/// <summary>ASPState stores payloads up to this size in <c>SessionItemShort</c>.</summary>
		internal const int ShortItemLimit = 7000;

		/// <summary>
		/// The value <c>&lt;sessionState&gt;</c> carries when nobody has set sqlConnectionString.
		/// Duplicated rather than read off SessionStateSection, whose own copy is internal to Core.Web.
		/// </summary>
		internal const string PlaceholderConnectionString = "data source=localhost;Integrated Security=SSPI";

		string connectionString;
		string appSuffix;
		int commandTimeoutSeconds;
		int timeoutMinutes;
		bool compress;

		public override void Initialize (string name, NameValueCollection providerConfig)
		{
			if (String.IsNullOrEmpty (name))
				name = "SqlSessionStateStore";

			base.Initialize (name, providerConfig ?? new NameValueCollection ());

			var config = WebConfigurationManager.GetWebApplicationSection ("system.web/sessionState")
				as SessionStateSection;

			connectionString = SessionStateHostServices.SqlConnectionString;
			if (String.IsNullOrEmpty (connectionString) && config != null)
				connectionString = config.SqlConnectionString;

			// The framework default is a placeholder ("data source=localhost;Integrated Security=SSPI")
			// that only ever worked because IIS ran next to the database. Reaching a real server by
			// accident is worse than failing here.
			if (String.IsNullOrEmpty (connectionString) ||
			    String.Equals (connectionString, PlaceholderConnectionString,
					   StringComparison.OrdinalIgnoreCase))
				throw new HttpException (
					"<sessionState mode=\"SQLServer\"> needs a sqlConnectionString pointing at an " +
					"ASPState database. Set it on the sessionState element, or supply it at startup " +
					"through SessionStateHostServices.SqlConnectionString when the credentials come " +
					"from a secret store. Provision the database with the script returned by " +
					"SqlSessionStateStore.GetSchemaScript () - aspnet_regsql.exe does not exist on .NET 10.");

			commandTimeoutSeconds = config != null ? (int) config.SqlCommandTimeout.TotalSeconds : 30;
			if (commandTimeoutSeconds <= 0)
				commandTimeoutSeconds = 30;

			timeoutMinutes = config != null ? (int) config.Timeout.TotalMinutes : 20;
			if (timeoutMinutes <= 0)
				timeoutMinutes = 20;

			compress = config != null && config.CompressionEnabled;

			SessionStateHostServices.Freeze ();

			appSuffix = GetApplicationSuffix ();
		}

		/// <summary>
		/// The DDL for the <c>ASPState</c> database, as <c>aspnet_regsql.exe -ssadd</c> would have
		/// produced it. That tool is .NET Framework only, so the script ships here instead.
		/// </summary>
		/// <remarks>Run it against a database you have already created. It is idempotent.</remarks>
		public static string GetSchemaScript ()
		{
			// Located by suffix rather than by full name: the logical resource name is built from the
			// ROOT NAMESPACE (System.Web.SessionState), not the assembly name (Core.Web.SessionState),
			// and this port renames every assembly - so the two differ here and hard-coding either one
			// is a trap for whoever touches the project file next.
			System.Reflection.Assembly assembly = typeof (SqlSessionStateStore).Assembly;
			string name = Array.Find (assembly.GetManifestResourceNames (),
						  n => n.EndsWith ("ASPState.sql", StringComparison.Ordinal));

			if (name == null)
				throw new InvalidOperationException (
					"The ASPState schema script is missing from " + assembly.GetName ().Name +
					". It is an EmbeddedResource in the project file; a build that dropped it would " +
					"otherwise fail much later, at deployment time.");

			using (var stream = assembly.GetManifestResourceStream (name))
			using (var reader = new System.IO.StreamReader (stream))
				return reader.ReadToEnd ();
		}

		/// <summary>
		/// Runs <see cref="GetSchemaScript"/> against an existing database. Idempotent.
		/// </summary>
		/// <remarks>
		/// The script is batched with <c>GO</c>, which is a SQL Server Management Studio convention and
		/// not T-SQL - SqlCommand rejects it. Splitting here is what <c>sqlcmd</c> does, and is why this
		/// helper exists at all rather than leaving callers to run the file themselves.
		/// </remarks>
		public static void EnsureSchema (string connectionString)
		{
			if (String.IsNullOrEmpty (connectionString))
				throw new ArgumentException ("A connection string is required.", nameof (connectionString));

			string [] batches = System.Text.RegularExpressions.Regex.Split (
				GetSchemaScript (), @"^\s*GO\s*$",
				System.Text.RegularExpressions.RegexOptions.Multiline |
				System.Text.RegularExpressions.RegexOptions.IgnoreCase);

			using (var connection = new SqlConnection (connectionString)) {
				connection.Open ();

				foreach (string batch in batches) {
					if (String.IsNullOrWhiteSpace (batch))
						continue;

					using (var command = new SqlCommand (batch, connection))
						command.ExecuteNonQuery ();
				}
			}
		}

		/// <summary>
		/// ASPState keys rows by session id + application id, so two applications sharing one database
		/// do not collide on ids that are only unique within an application. TempGetAppID interns the
		/// application name and hands back the integer, which the real provider formats as 8 hex digits.
		/// </summary>
		string GetApplicationSuffix ()
		{
			string applicationName = HostingEnvironment.ApplicationVirtualPath;
			if (String.IsNullOrEmpty (applicationName))
				applicationName = "/";

			using (SqlConnection connection = Open ())
			using (var command = new SqlCommand ("dbo.TempGetAppID", connection)) {
				command.CommandType = CommandType.StoredProcedure;
				command.CommandTimeout = commandTimeoutSeconds;
				command.Parameters.Add (new SqlParameter ("@appName", SqlDbType.VarChar, 280) {
					Value = applicationName,
				});

				var appId = new SqlParameter ("@appId", SqlDbType.Int) { Direction = ParameterDirection.Output };
				command.Parameters.Add (appId);

				command.ExecuteNonQuery ();
				return ((int) appId.Value).ToString ("X8");
			}
		}

		public override void Dispose ()
		{
		}

		public override void InitializeRequest (HttpContext context)
		{
		}

		public override void EndRequest (HttpContext context)
		{
		}

		/// <summary>
		/// Always false. The real provider only fires Session_End when the SQL Agent job that sweeps
		/// expired rows is installed and running; nothing here can guarantee that, and a callback that
		/// silently never fires is worse than a documented false.
		/// </summary>
		public override bool SetItemExpireCallback (SessionStateItemExpireCallback expireCallback)
		{
			return false;
		}

		public override SessionStateStoreData CreateNewStoreData (HttpContext context, int timeout)
		{
			return SessionStateSerializer.CreateStoreData (context, new SessionStateItemCollection (), timeout);
		}

		public override void CreateUninitializedItem (HttpContext context, string id, int timeout)
		{
			byte [] payload = SessionStateSerializer.SerializeItems (new SessionStateItemCollection (), compress);

			using (SqlConnection connection = Open ())
			using (SqlCommand command = Procedure ("dbo.TempInsertUninitializedItem", connection)) {
				command.Parameters.Add (IdParameter (id));
				command.Parameters.Add (new SqlParameter ("@itemShort", SqlDbType.VarBinary, ShortItemLimit) {
					Value = payload,
				});
				command.Parameters.Add (new SqlParameter ("@timeout", SqlDbType.Int) { Value = timeout });
				command.ExecuteNonQuery ();
			}
		}

		public override SessionStateStoreData GetItem (HttpContext context, string id, out bool locked,
							       out TimeSpan lockAge, out object lockId,
							       out SessionStateActions actions)
		{
			return Get (context, "dbo.TempGetStateItem3", id, out locked, out lockAge, out lockId, out actions);
		}

		public override SessionStateStoreData GetItemExclusive (HttpContext context, string id, out bool locked,
								       out TimeSpan lockAge, out object lockId,
								       out SessionStateActions actions)
		{
			return Get (context, "dbo.TempGetStateItemExclusive3", id, out locked, out lockAge, out lockId,
				    out actions);
		}

		SessionStateStoreData Get (HttpContext context, string procedure, string id, out bool locked,
					   out TimeSpan lockAge, out object lockId, out SessionStateActions actions)
		{
			locked = false;
			lockAge = TimeSpan.Zero;
			lockId = null;
			actions = SessionStateActions.None;

			using (SqlConnection connection = Open ())
			using (SqlCommand command = Procedure (procedure, connection)) {
				command.Parameters.Add (IdParameter (id));

				var itemShort = new SqlParameter ("@itemShort", SqlDbType.VarBinary, ShortItemLimit) {
					Direction = ParameterDirection.Output,
				};
				var lockedOut = new SqlParameter ("@locked", SqlDbType.Bit) { Direction = ParameterDirection.Output };
				var lockAgeOut = new SqlParameter ("@lockAge", SqlDbType.Int) { Direction = ParameterDirection.Output };
				var lockCookie = new SqlParameter ("@lockCookie", SqlDbType.Int) { Direction = ParameterDirection.Output };
				var actionFlags = new SqlParameter ("@actionFlags", SqlDbType.Int) { Direction = ParameterDirection.Output };

				command.Parameters.Add (itemShort);
				command.Parameters.Add (lockedOut);
				command.Parameters.Add (lockAgeOut);
				command.Parameters.Add (lockCookie);
				command.Parameters.Add (actionFlags);

				// The procedure returns the payload one of two ways: through @itemShort when it fits in
				// 7000 bytes, and otherwise as a single-row result set carrying SessionItemLong. The
				// reader has to be drained and CLOSED before the output parameters are populated - that
				// ordering is not optional, and reading them early silently yields nulls.
				byte [] longPayload = null;
				using (SqlDataReader reader = command.ExecuteReader ()) {
					if (reader.Read () && !reader.IsDBNull (0))
						longPayload = (byte []) reader.GetValue (0);
				}

				if (lockedOut.Value is bool && (bool) lockedOut.Value) {
					locked = true;
					lockAge = TimeSpan.FromSeconds (lockAgeOut.Value is int ? (int) lockAgeOut.Value : 0);
					lockId = lockCookie.Value is int ? lockCookie.Value : null;
					return null;
				}

				byte [] payload = itemShort.Value as byte [] ?? longPayload;
				if (payload == null)
					return null;

				lockId = lockCookie.Value is int ? lockCookie.Value : null;
				actions = actionFlags.Value is int
					? (SessionStateActions) (int) actionFlags.Value
					: SessionStateActions.None;

				SessionStateItemCollection items = actions == SessionStateActions.InitializeItem
					? new SessionStateItemCollection ()
					: SessionStateSerializer.DeserializeItems (payload, compress);

				return SessionStateSerializer.CreateStoreData (context, items, timeoutMinutes);
			}
		}

		public override void ReleaseItemExclusive (HttpContext context, string id, object lockId)
		{
			using (SqlConnection connection = Open ())
			using (SqlCommand command = Procedure ("dbo.TempReleaseStateItemExclusive", connection)) {
				command.Parameters.Add (IdParameter (id));
				command.Parameters.Add (LockCookieParameter (lockId));
				command.ExecuteNonQuery ();
			}
		}

		public override void SetAndReleaseItemExclusive (HttpContext context, string id,
								 SessionStateStoreData item, object lockId, bool newItem)
		{
			byte [] payload;
			try {
				payload = SessionStateSerializer.SerializeItems (item.Items, compress);
			} catch (Exception ex) {
				throw SessionStateSerializer.DescribeSerializationFailure (ex, "SQLServer");
			}

			bool isShort = payload.Length <= ShortItemLimit;
			int timeout = item.Timeout > 0 ? item.Timeout : timeoutMinutes;

			string procedure;
			if (newItem)
				procedure = isShort ? "dbo.TempInsertStateItemShort" : "dbo.TempInsertStateItemLong";
			else
				// The *Null* variants clear the column the payload did NOT go into. The real provider
				// tracks which column the row previously used and skips the clear when it has not
				// changed; always clearing is one column write more and removes a whole class of bug
				// where a shrinking session keeps being read from a stale SessionItemLong.
				procedure = isShort ? "dbo.TempUpdateStateItemShortNullLong"
						    : "dbo.TempUpdateStateItemLongNullShort";

			using (SqlConnection connection = Open ())
			using (SqlCommand command = Procedure (procedure, connection)) {
				command.Parameters.Add (IdParameter (id));
				command.Parameters.Add (isShort
					? new SqlParameter ("@itemShort", SqlDbType.VarBinary, ShortItemLimit) { Value = payload }
					: new SqlParameter ("@itemLong", SqlDbType.Image) { Value = payload });
				command.Parameters.Add (new SqlParameter ("@timeout", SqlDbType.Int) { Value = timeout });

				if (!newItem)
					command.Parameters.Add (LockCookieParameter (lockId));

				command.ExecuteNonQuery ();
			}
		}

		public override void RemoveItem (HttpContext context, string id, object lockId, SessionStateStoreData item)
		{
			using (SqlConnection connection = Open ())
			using (SqlCommand command = Procedure ("dbo.TempRemoveStateItem", connection)) {
				command.Parameters.Add (IdParameter (id));
				command.Parameters.Add (LockCookieParameter (lockId));
				command.ExecuteNonQuery ();
			}
		}

		public override void ResetItemTimeout (HttpContext context, string id)
		{
			using (SqlConnection connection = Open ())
			using (SqlCommand command = Procedure ("dbo.TempResetTimeout", connection)) {
				command.Parameters.Add (IdParameter (id));
				command.ExecuteNonQuery ();
			}
		}

		SqlConnection Open ()
		{
			var connection = new SqlConnection (connectionString);
			connection.Open ();
			return connection;
		}

		SqlCommand Procedure (string name, SqlConnection connection)
		{
			var command = new SqlCommand (name, connection) {
				CommandType = CommandType.StoredProcedure,
				CommandTimeout = commandTimeoutSeconds,
			};

			return command;
		}

		SqlParameter IdParameter (string id)
		{
			// ASPState keys on session id + application id; the column is nvarchar(88) and the real
			// provider writes exactly this concatenation.
			return new SqlParameter ("@id", SqlDbType.NVarChar, 88) { Value = id + appSuffix };
		}

		static SqlParameter LockCookieParameter (object lockId)
		{
			return new SqlParameter ("@lockCookie", SqlDbType.Int) {
				Value = lockId == null ? 0 : Convert.ToInt32 (lockId),
			};
		}
	}
}
