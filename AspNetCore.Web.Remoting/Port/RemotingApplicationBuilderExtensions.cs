//
// ASP.NET Core entry points for Core.Web.Remoting.
//

using System;
using System.Web.SessionState;
using Microsoft.AspNetCore.Builder;

namespace System.Web.Hosting.Kestrel
{
	public static class RemotingApplicationBuilderExtensions
	{
		/// <summary>
		/// Serves <c>&lt;sessionState mode="StateServer"&gt;</c> from a remote state server (see
		/// <see cref="RemoteStateServerHost"/>) addressed by <c>stateConnectionString</c>, instead of the
		/// default IDistributedCache store. Call before <c>UseWebForms</c>.
		/// </summary>
		public static IApplicationBuilder UseWebFormsRemoteStateServer (this IApplicationBuilder app)
		{
			if (app == null)
				throw new ArgumentNullException (nameof (app));

			RemoteStateServerHost.UseForStateServerMode ();
			return app;
		}
	}
}
