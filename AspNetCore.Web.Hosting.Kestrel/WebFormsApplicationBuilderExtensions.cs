//
// Public entry point: app.UseWebForms (...).
//

using System;
using System.IO;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
// This namespace is nested under System.Web, so an unqualified `HttpContext` binds to
// System.Web.HttpContext - the enclosing namespace beats any using directive. Every reference to
// ASP.NET Core's HttpContext in this assembly goes through this alias.
using AspNetCoreHttpContext = Microsoft.AspNetCore.Http.HttpContext;

namespace System.Web.Hosting.Kestrel
{
	public sealed class WebFormsOptions
	{
		/// <summary>
		/// Physical directory of the WebForms application - the one containing web.config. Defaults
		/// to the host's content root.
		/// </summary>
		public string PhysicalPath { get; set; }

		/// <summary>Application virtual path. "/" unless the app is hosted under a sub-path.</summary>
		public string VirtualPath { get; set; } = "/";

		/// <summary>Site name reported through HostingEnvironment.SiteName.</summary>
		public string SiteName { get; set; } = "Kestrel";

		/// <summary>
		/// Optional filter deciding which requests reach System.Web. When null every request does,
		/// and System.Web's own &lt;httpHandlers&gt; mapping decides the outcome. Put
		/// UseStaticFiles() before UseWebForms() rather than filtering here.
		/// </summary>
		public Func<AspNetCoreHttpContext, bool> ShouldHandle { get; set; }

		/// <summary>
		/// Serializer for objects the built-in view state / session formatters have no native encoding
		/// for. Null (the default) means such an object is refused with a diagnostic naming its type,
		/// rather than being re-encoded by some other scheme with different round-trip behaviour -
		/// BinaryFormatter, which upstream used here, is gone on .NET 9+. Opt in with
		/// <c>new JsonStateObjectSerializer ()</c> or your own implementation.
		/// </summary>
		public System.Web.IStateObjectSerializer StateSerializer { get; set; }

		/// <summary>
		/// Assigns the ASP.NET Core authentication result to System.Web's
		/// <c>HttpContext.User</c>, so Windows/Negotiate, OIDC, JWT and any other ASP.NET Core
		/// scheme drive &lt;authorization&gt;, User.Identity and role checks.
		/// </summary>
		/// <remarks>
		/// Off by default, and deliberately so: an application using forms authentication must not
		/// have its principal replaced from underneath it. Turn it on when ASP.NET Core owns
		/// authentication - and then put the authentication middleware BEFORE UseWebForms(), or
		/// there is no result to flow.
		/// </remarks>
		public bool UseAspNetCoreAuthentication { get; set; }

		/// <summary>
		/// Full path to a machine.config for this process to use instead of the copy embedded in
		/// Core.Web.
		/// </summary>
		/// <remarks>
		/// There is no shared machine-level configuration on .NET Core - the port extracts its own to
		/// a temp directory - so without this, settings an operator would put in machine.config have
		/// nowhere to go. This is a REPLACEMENT, not an overlay: the file must be a complete
		/// machine.config, because everything the runtime inherits comes from it.
		/// </remarks>
		public string MachineConfigPath { get; set; }

		/// <summary>
		/// Assemblies to load before the first request, so types they declare can be found by name.
		/// </summary>
		/// <remarks>
		/// Usually unnecessary. ASP.NET loaded everything in the application's bin directory at
		/// startup and the port still does, so a classic layout needs nothing here; and in a normal
		/// deployment the application assembly is the host's own entry assembly, already loaded.
		///
		/// It is needed when the application DIRECTORY and the running assembly are different places
		/// - the SDK builds to bin/Debug/net10.0 rather than bin/ - because Inherits="MyApp.Page"
		/// names no assembly, and the type is resolved by scanning already loaded assemblies rather
		/// than by attempting a load. Nothing can resolve it if it was never loaded.
		///
		///     options.ApplicationAssemblies = new [] { typeof (MyApp.SomePage).Assembly };
		/// </remarks>
		public System.Collections.Generic.IEnumerable<System.Reflection.Assembly> ApplicationAssemblies { get; set; }

		/// <summary>
		/// Directory for generated page sources and compiled assemblies - the equivalent of
		/// "Temporary ASP.NET Files", and of <c>&lt;compilation tempDirectory=""&gt;</c>.
		/// </summary>
		/// <remarks>
		/// Defaults to a per-application directory under the system temp path, keyed on
		/// <see cref="PhysicalPath"/>. That key is the application, not the process - so two processes
		/// hosting the SAME directory share one compilation output directory and will fight over it.
		/// That is fine in production, where an application directory belongs to one process, but not
		/// for test suites or side-by-side instances, which should each set their own.
		/// </remarks>
		public string TemporaryFilesPath { get; set; }
	}

	public static class WebFormsApplicationBuilderExtensions
	{
		/// <summary>
		/// Initialises the ported System.Web runtime and inserts the WebForms request pipeline.
		/// </summary>
		public static IApplicationBuilder UseWebForms (this IApplicationBuilder app,
							      Action<WebFormsOptions> configure = null)
		{
			if (app == null)
				throw new ArgumentNullException (nameof (app));

			var options = new WebFormsOptions ();
			configure?.Invoke (options);

			if (String.IsNullOrEmpty (options.PhysicalPath)) {
				var env = app.ApplicationServices.GetService<IHostEnvironment> ();
				options.PhysicalPath = env?.ContentRootPath ?? Directory.GetCurrentDirectory ();
			}

			// Before Initialize: the first configuration read extracts (or, with this set, adopts)
			// machine.config, and Initialize performs that read.
			WebFormsRuntimeHost.MachineConfigOverride = options.MachineConfigPath;

			// Likewise before Initialize - HttpApplicationFactory reads the module list when it
			// builds the application, which is on the first request.
			WebFormsRuntimeHost.UseAspNetCoreAuthentication = options.UseAspNetCoreAuthentication;
			WebFormsRuntimeHost.ApplicationAssemblies = options.ApplicationAssemblies;
			WebFormsRuntimeHost.TemporaryFilesPath = options.TemporaryFilesPath;

			WebFormsRuntimeHost.Initialize (options.PhysicalPath, options.VirtualPath, options.SiteName);
			WebFormsRuntimeHost.StateSerializer = options.StateSerializer;

			// Replaces the AppDomain.DomainUnload handling that stopped registered objects.
			var lifetime = app.ApplicationServices.GetService<IHostApplicationLifetime> ();
			lifetime?.ApplicationStopping.Register (WebFormsRuntimeHost.Shutdown);

			return app.UseMiddleware<WebFormsMiddleware> (options);
		}
	}
}
