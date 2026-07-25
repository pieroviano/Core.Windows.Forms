//
// Hosts the sample application with ASP.NET Core authentication in front of UseWebForms(), and a
// machine.config supplied by the host rather than extracted from Core.Web.
//
// One fixture covers both because the runtime initialises once per process, so this is the only
// configuration this assembly can ever have.
//
// The authentication scheme is a stand-in for whatever a real deployment uses - Negotiate, OIDC, a
// JWT bearer. What matters to the port is only that ASP.NET Core produced an authenticated
// ClaimsPrincipal before the WebForms pipeline ran; where it came from is irrelevant.
//

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Web.Hosting.Kestrel;
using Xunit;

namespace WebFormsPort.HostingTests
{
	public sealed class AuthenticatedHostFixture : IAsyncLifetime
	{
		public const string Scheme = "Test";
		public const string UserName = "piero";
		public const string HeaderName = "X-Test-User";

		/// <summary>An appSetting that exists ONLY in the host-supplied machine.config.</summary>
		public const string MachineSettingKey = "machine.only.setting";
		public const string MachineSettingValue = "from-the-host-supplied-machine-config";

		WebApplication app;

		public string BaseAddress { get; private set; }

		/// <summary>Path of the machine.config this process was told to use.</summary>
		public string MachineConfigPath { get; private set; }

		public async Task InitializeAsync ()
		{
			MachineConfigPath = WriteMachineConfig ();

			var builder = WebApplication.CreateBuilder (new WebApplicationOptions {
				ContentRootPath = RepoPaths.SampleApp,
				ApplicationName = typeof (AuthenticatedHostFixture).Assembly.GetName ().Name,
			});

			builder.WebHost.UseUrls ("http://127.0.0.1:0");
			builder.Logging.ClearProviders ();

			builder.Services.AddAuthentication (Scheme)
				.AddScheme<AuthenticationSchemeOptions, HeaderAuthenticationHandler> (Scheme, _ => { });
			builder.Services.AddAuthorization ();

			app = builder.Build ();

			// BEFORE UseWebForms, or there is no authentication result to flow across.
			app.UseAuthentication ();

			app.UseWebForms (options => {
				options.PhysicalPath = RepoPaths.SampleApp;
				options.VirtualPath = "/";
				options.SiteName = "HostingTests";
				// Its own compilation directory. The default is keyed on the application PATH, and
				// several suites host this same directory - in a solution-wide `dotnet test` they run
				// as parallel processes and would corrupt each other's generated pages.
				options.TemporaryFilesPath = System.IO.Path.Combine (
					System.IO.Path.GetTempPath (), "webforms-tests",
					typeof (AuthenticatedHostFixture).Assembly.GetName ().Name);
				options.StateSerializer = new System.Web.JsonStateObjectSerializer ();
				options.UseAspNetCoreAuthentication = true;
				options.MachineConfigPath = MachineConfigPath;
			});

			await app.StartAsync ();
			BaseAddress = app.Urls.First ();
		}

		public async Task DisposeAsync ()
		{
			if (app == null)
				return;

			await app.StopAsync ();
			await app.DisposeAsync ();
		}

		public HttpClient CreateClient (string user = null)
		{
			var handler = new HttpClientHandler {
				UseCookies = true,
				CookieContainer = new CookieContainer (),
				AllowAutoRedirect = false,
			};

			var client = new HttpClient (handler) { BaseAddress = new Uri (BaseAddress) };

			if (!String.IsNullOrEmpty (user))
				client.DefaultRequestHeaders.Add (HeaderName, user);

			return client;
		}

		/// <summary>
		/// Writes a complete machine.config carrying one identifiable appSetting.
		/// </summary>
		/// <remarks>
		/// It has to be COMPLETE, not a fragment: MachineConfigPath is a replacement, and everything
		/// the configuration system inherits comes from this file. The section definitions below are
		/// the minimum that lets &lt;appSettings&gt; and the system.web group resolve; the rest of the
		/// application's configuration still comes from its own web.config.
		/// </remarks>
		static string WriteMachineConfig ()
		{
			// Start from the embedded copy the port would otherwise extract, so every section
			// definition the runtime needs is present, and add one setting that identifies it.
			string source = Path.Combine (Path.GetTempPath (), "webforms-hosting-tests");
			Directory.CreateDirectory (source);

			string path = Path.Combine (source, "machine.config");

			// Extract the port's own machine.config by asking a throwaway initialisation for it would
			// require initialising the runtime, which is what this file is configuring - so read the
			// resource straight out of Core.Web instead.
			using Stream resource = typeof (System.Web.HttpContext).Assembly
				.GetManifestResourceStream ("System.Web.Config.machine.config");

			if (resource == null)
				throw new InvalidOperationException (
					"Core.Web does not carry System.Web.Config.machine.config - the embedded " +
					"configuration resources have been renamed.");

			string text;
			using (var reader = new StreamReader (resource))
				text = reader.ReadToEnd ();

			// Add one identifiable setting to the <appSettings> machine.config ALREADY HAS.
			//
			// Not a new section: a second <appSettings> is rejected with "defined more than once",
			// and putting one before </configSections> is rejected with "Unrecognized configuration
			// section" because that is where the section handler is declared. Extend the existing one.
			const string marker = "<appSettings>";
			int at = text.IndexOf (marker, StringComparison.Ordinal);
			if (at < 0)
				throw new InvalidOperationException (
					"the embedded machine.config has no <appSettings> element to extend");

			text = text.Insert (at + marker.Length,
					    "\r\n    <add key=\"" + MachineSettingKey +
					    "\" value=\"" + MachineSettingValue + "\" />");

			File.WriteAllText (path, text);

			// The root web.config must sit next to machine.config: WebConfigurationHost locates it as
			// "the web.config in machine.config's directory", and it is what maps *.aspx.
			using Stream rootWeb = typeof (System.Web.HttpContext).Assembly
				.GetManifestResourceStream ("System.Web.Config.root-web.config");
			using (var reader = new StreamReader (rootWeb))
				File.WriteAllText (Path.Combine (source, "web.config"), reader.ReadToEnd ());

			using Stream wsdl = typeof (System.Web.HttpContext).Assembly
				.GetManifestResourceStream ("System.Web.Config.DefaultWsdlHelpGenerator.aspx");
			using (var reader = new StreamReader (wsdl))
				File.WriteAllText (Path.Combine (source, "DefaultWsdlHelpGenerator.aspx"), reader.ReadToEnd ());

			return path;
		}
	}

	/// <summary>
	/// Authenticates whoever the X-Test-User header names. Stands in for Negotiate/OIDC/JWT.
	/// </summary>
	sealed class HeaderAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
	{
		public HeaderAuthenticationHandler (IOptionsMonitor<AuthenticationSchemeOptions> options,
						    ILoggerFactory logger, UrlEncoder encoder)
			: base (options, logger, encoder)
		{
		}

		protected override Task<AuthenticateResult> HandleAuthenticateAsync ()
		{
			if (!Request.Headers.TryGetValue (AuthenticatedHostFixture.HeaderName, out var user) ||
			    String.IsNullOrEmpty (user))
				return Task.FromResult (AuthenticateResult.NoResult ());

			var identity = new ClaimsIdentity (
				new [] { new Claim (ClaimTypes.Name, user.ToString ()) },
				AuthenticatedHostFixture.Scheme);

			return Task.FromResult (AuthenticateResult.Success (
				new AuthenticationTicket (new ClaimsPrincipal (identity),
							  AuthenticatedHostFixture.Scheme)));
		}
	}

	[CollectionDefinition (Name)]
	public sealed class HostingCollection : ICollectionFixture<AuthenticatedHostFixture>
	{
		public const string Name = "webforms-hosting";
	}
}
