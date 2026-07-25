//
// Starts Samples/WebFormsSample under a real Kestrel server on a loopback port, once for the whole
// assembly.
//
// Once, not per class, because the ported runtime is process-global: WebFormsRuntimeHost.Initialize
// is documented idempotent ("a process hosts one application"), it writes .appPath and friends as
// AppDomain data, and HttpRuntime, BuildManager and the configuration system all read that state.
// A second application in the same process would silently reuse the first one's. Every test class
// therefore joins the single collection defined below, which also serialises them - Application
// ["requests"], the session store and the compiled-page cache are shared mutable state.
//

using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
// UseWebForms lives here, not in Microsoft.AspNetCore.Builder.
using System.Web.Hosting.Kestrel;
using Xunit;

namespace WebFormsPort.FunctionalTests
{
	public sealed class SampleAppFixture : IAsyncLifetime
	{
		WebApplication app;

		/// <summary>Base address of the running server, e.g. http://127.0.0.1:49512.</summary>
		public string BaseAddress { get; private set; }

		/// <summary>Physical path the application was initialised against.</summary>
		public string AppPhysicalPath {
			get { return RepoPaths.SampleApp; }
		}

		public async Task InitializeAsync ()
		{
			// The application assembly is deliberately NOT force-loaded: WebFormsRuntimeHost's
			// assembly resolver probes the host's output directory as well as <app>/bin, which is
			// what lets Inherits= and the web.config type references resolve here.
			var builder = WebApplication.CreateBuilder (new WebApplicationOptions {
				ContentRootPath = RepoPaths.SampleApp,
				// Defaults to the entry assembly, which under `dotnet test` is the test host rather
				// than anything meaningful. Naming this assembly keeps hosting-startup discovery and
				// IHostEnvironment.ApplicationName sane.
				ApplicationName = typeof (SampleAppFixture).Assembly.GetName ().Name,
			});

			// Port 0: the OS picks a free port, so parallel test runs and a developer's own
			// `dotnet run` of the sample cannot collide.
			builder.WebHost.UseUrls ("http://127.0.0.1:0");
			builder.Logging.ClearProviders ();

			app = builder.Build ();

			// Same order as the sample's Program.cs: static files first, so .css/.js never reach the
			// System.Web pipeline.
			app.UseStaticFiles ();

			app.UseWebForms (options => {
				options.PhysicalPath = RepoPaths.SampleApp;
				options.VirtualPath = "/";
				options.SiteName = "FunctionalTests";
				// Its own compilation directory. The default is keyed on the application PATH, and
				// several suites host this same directory - in a solution-wide `dotnet test` they run
				// as parallel processes and would corrupt each other's generated pages.
				options.TemporaryFilesPath = System.IO.Path.Combine (
					System.IO.Path.GetTempPath (), "webforms-tests",
					typeof (SampleAppFixture).Assembly.GetName ().Name);
				// State.aspx round-trips a custom type through view state and session; without a
				// serializer the port refuses it by design.
				options.StateSerializer = new System.Web.JsonStateObjectSerializer ();
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

		/// <summary>
		/// A client with its own cookie jar, so session and forms-authentication tests do not leak
		/// into one another. Redirects are NOT followed: several tests assert on the 302 itself.
		/// </summary>
		public HttpClient CreateClient (bool followRedirects = false)
		{
			var handler = new HttpClientHandler {
				UseCookies = true,
				CookieContainer = new CookieContainer (),
				AllowAutoRedirect = followRedirects,
			};

			return new HttpClient (handler) {
				BaseAddress = new Uri (BaseAddress),
			};
		}
	}

	/// <summary>
	/// Every test class joins this collection. See the note on SampleAppFixture for why the suite is
	/// deliberately single-collection (and therefore serialised).
	/// </summary>
	[CollectionDefinition (Name)]
	public sealed class WebFormsCollection : ICollectionFixture<SampleAppFixture>
	{
		public const string Name = "webforms-sample";
	}
}
