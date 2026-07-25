//
// Decides once whether a SQL Server is reachable, and provisions a scratch database if it is.
//
// The suite must not fail on a machine that has no SQL Server - that is the difference between a
// test suite people run and one they learn to ignore. But it must not silently pass either: a test
// that returns early with no assertions looks identical to one that verified something. So the
// SqlFact attribute below turns unavailability into a real xunit SKIP, with the reason attached, and
// the runner reports it as skipped rather than green.
//

using System;
using System.Web.SessionState;
using Microsoft.Data.SqlClient;
using Xunit;

namespace WebFormsPort.SessionStateTests
{
	static class SqlAvailability
	{
		/// <summary>Set this to point the suite at a specific server.</summary>
		public const string EnvironmentVariable = "WEBFORMSPORT_SQL_CONNECTIONSTRING";

		const string TestDatabase = "ASPStateTest";

		static readonly Lazy<Result> probe = new Lazy<Result> (Probe);

		public static bool IsAvailable {
			get { return probe.Value.ConnectionString != null; }
		}

		/// <summary>Connection string for the scratch ASPState database, or null when unavailable.</summary>
		public static string ConnectionString {
			get { return probe.Value.ConnectionString; }
		}

		public static string SkipReason {
			get { return probe.Value.SkipReason; }
		}

		sealed class Result
		{
			public string ConnectionString;
			public string SkipReason;
		}

		static Result Probe ()
		{
			string configured = Environment.GetEnvironmentVariable (EnvironmentVariable);

			// Tried in order, first one that answers wins. LocalDB first because it is the one a
			// developer machine is most likely to have and the least likely to be shared with anything
			// that matters; the default instance second, because plenty of machines have that and no
			// working LocalDB. Encrypt=False is needed for both: Microsoft.Data.SqlClient defaults to
			// encrypting, and a local instance has no certificate anyone has vouched for.
			string [] candidates = configured != null
				? new [] { configured }
				: new [] {
					@"Server=(localdb)\MSSQLLocalDB;Integrated Security=true;Encrypt=False",
					@"Server=.;Integrated Security=true;Encrypt=False;TrustServerCertificate=True",
				};

			string lastError = null;

			foreach (string candidate in candidates) {
				try {
					return Provision (candidate);
				} catch (Exception ex) {
					lastError = ex.GetType ().Name + ": " + ex.Message.Split ('\n') [0].Trim ();
				}
			}

			return new Result {
				SkipReason = "No SQL Server reachable (" + lastError + "). Set " + EnvironmentVariable +
					" to run the SQLServer session-state tests.",
			};
		}

		static Result Provision (string candidate)
		{
			var builder = new SqlConnectionStringBuilder (candidate) {
				InitialCatalog = "master",
				ConnectTimeout = 5,
			};

			using (var connection = new SqlConnection (builder.ConnectionString)) {
				connection.Open ();

				// A dedicated database rather than tables in master or in whatever the caller pointed
				// at: the schema script creates objects with fixed names, and dropping them into
				// someone's real database would be unforgivable.
				using (var command = new SqlCommand (
					"IF DB_ID(N'" + TestDatabase + "') IS NULL CREATE DATABASE [" + TestDatabase + "]",
					connection)) {
					command.CommandTimeout = 30;
					command.ExecuteNonQuery ();
				}
			}

			builder.InitialCatalog = TestDatabase;

			// Provisioned here rather than by a test, because xunit orders tests within a class
			// arbitrarily - any other SQL test can run first, and would then fail on a database that
			// has tables but no stored procedures. EnsureSchema is idempotent, which is what the test
			// of the same name goes on to verify.
			SqlSessionStateStore.EnsureSchema (builder.ConnectionString);

			return new Result { ConnectionString = builder.ConnectionString };
		}
	}

	/// <summary>
	/// A <see cref="FactAttribute"/> that skips when no SQL Server is reachable.
	/// </summary>
	/// <remarks>
	/// xunit resolves Skip at discovery, so the probe runs once per assembly and every SQL test shares
	/// the answer. That is also why the reason has to be computed in the constructor rather than
	/// checked inside the test body.
	/// </remarks>
	public sealed class SqlFactAttribute : FactAttribute
	{
		public SqlFactAttribute ()
		{
			if (!SqlAvailability.IsAvailable)
				Skip = SqlAvailability.SkipReason;
		}
	}
}
