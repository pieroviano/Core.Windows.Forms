//
// Flows the ASP.NET Core authentication result into System.Web's HttpContext.User.
//
// This is the bridge that makes Windows/Negotiate, OIDC, JWT and every other ASP.NET Core
// authentication scheme usable from WebForms. None of them were ever "missing": the middleware runs
// before UseWebForms() and puts a ClaimsPrincipal on ASP.NET Core's HttpContext.User. What was
// missing is anything copying it across, so System.Web saw an anonymous caller and <authorization>,
// User.Identity.Name and role checks all behaved as though nobody had signed in.
//
// Registered only when WebFormsOptions.UseAspNetCoreAuthentication is set. It is opt-in on purpose:
// an application using forms authentication must not have its principal replaced from underneath it.
//

using System;
using System.Security.Principal;
using System.Web;

namespace System.Web.Hosting.Kestrel
{
	/// <summary>
	/// Assigns the ASP.NET Core <c>ClaimsPrincipal</c> to <see cref="HttpContext.User"/> during
	/// AuthenticateRequest.
	/// </summary>
	public sealed class AspNetCoreAuthenticationModule : IHttpModule
	{
		public void Init (HttpApplication application)
		{
			if (application == null)
				throw new ArgumentNullException (nameof (application));

			// AuthenticateRequest, not BeginRequest: this must land before UrlAuthorizationModule
			// decides whether the caller may have the requested path, and before any application
			// code reads User. DefaultAuthenticationModule fills in an anonymous principal at the
			// END of AuthenticateRequest, so assigning here is not overwritten by it.
			application.AuthenticateRequest += OnAuthenticateRequest;
		}

		public void Dispose ()
		{
		}

		static void OnAuthenticateRequest (object sender, EventArgs e)
		{
			HttpContext context = ((HttpApplication) sender).Context;
			if (context == null)
				return;

			Microsoft.AspNetCore.Http.HttpContext core = WebFormsRuntimeHost.CurrentCoreContext;
			IPrincipal principal = core?.User;

			// Only when ASP.NET Core actually authenticated somebody. An unauthenticated
			// ClaimsPrincipal is not nobody - it is a principal with no identity - and assigning it
			// would mask forms authentication's own result later in the pipeline.
			if (principal?.Identity == null || !principal.Identity.IsAuthenticated)
				return;

			context.User = principal;
		}
	}
}
