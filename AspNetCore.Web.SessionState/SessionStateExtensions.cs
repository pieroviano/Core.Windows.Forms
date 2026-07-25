//
// Public entry point: UseWebFormsSessionState.
//
// One call, on the built application, placed before UseWebForms. It exists because the stores are
// built by the provider model - Activator.CreateInstance on a type named in configuration - which has
// no access to dependency injection. This resolves what they need out of the container and hands it
// over through SessionStateHostServices while there is still time.
//
// It is a no-op for mode="InProc" and mode="Off", so it is safe to call unconditionally: the mode is
// read from web.config, and an application that is not using an out-of-process store never touches
// any of this.
//

using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;

namespace System.Web.SessionState
{
	public sealed class WebFormsSessionStateOptions
	{
		/// <summary>
		/// Backing store for <c>mode="StateServer"</c>. Defaults to the <see cref="IDistributedCache"/>
		/// registered in the container.
		/// </summary>
		public IDistributedCache DistributedCache { get; set; }

		/// <summary>
		/// Connection string for <c>mode="SQLServer"</c>, overriding
		/// <c>&lt;sessionState sqlConnectionString&gt;</c>. Set this when the credentials come from a
		/// secret store rather than from <c>web.config</c>.
		/// </summary>
		public string SqlConnectionString { get; set; }

		/// <summary>
		/// Run the ASPState DDL at startup if the tables are not there. Off by default: creating schema
		/// is a deployment step, and an application that can do it holds rights in production it has no
		/// other reason to hold. Convenient for tests and for a local first run.
		/// </summary>
		public bool EnsureSqlSchema { get; set; }
	}

	public static class SessionStateExtensions
	{
		/// <summary>
		/// Supplies the out-of-process session stores with what the provider model cannot give them.
		/// Call BEFORE <c>UseWebForms ()</c>.
		/// </summary>
		public static IApplicationBuilder UseWebFormsSessionState (this IApplicationBuilder app,
									   Action<WebFormsSessionStateOptions> configure = null)
		{
			if (app == null)
				throw new ArgumentNullException (nameof (app));

			var options = new WebFormsSessionStateOptions ();
			configure?.Invoke (options);

			// GetService, not GetRequiredService: an application using mode="SQLServer" has no reason
			// to have registered a distributed cache, and demanding one would break it.
			SessionStateHostServices.DistributedCache =
				options.DistributedCache ?? app.ApplicationServices.GetService<IDistributedCache> ();

			if (!String.IsNullOrEmpty (options.SqlConnectionString))
				SessionStateHostServices.SqlConnectionString = options.SqlConnectionString;

			if (options.EnsureSqlSchema) {
				string connectionString = options.SqlConnectionString;
				if (String.IsNullOrEmpty (connectionString))
					throw new InvalidOperationException (
						"EnsureSqlSchema was set but no SqlConnectionString was supplied. The schema has " +
						"to be created before the runtime starts, which is earlier than web.config is " +
						"read - so this one cannot fall back to <sessionState sqlConnectionString>.");

				SqlSessionStateStore.EnsureSchema (connectionString);
			}

			return app;
		}
	}
}
