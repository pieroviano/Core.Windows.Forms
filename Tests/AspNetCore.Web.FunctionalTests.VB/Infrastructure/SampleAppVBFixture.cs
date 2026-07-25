//
// Starts Samples/WebFormsSampleVB under a real Kestrel server on a loopback port, once for the whole
// assembly. See the project file for why the VB application gets its own test project rather than
// sharing the C# one's process.
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

namespace WebFormsPort.FunctionalTests.VB
{
	public sealed class SampleAppVBFixture : IAsyncLifetime
	{
		WebApplication app;

		/// <summary>Base address of the running server, e.g. http://127.0.0.1:49512.</summary>
		public string BaseAddress { get; private set; }

		/// <summary>Physical path the application was initialised against.</summary>
		public string AppPhysicalPath {
			get { return RepoPaths.SampleAppVB; }
		}

		public async Task InitializeAsync ()
		{
			// No force-load of the application assembly here, deliberately. Default.aspx declares
			// Inherits="WebFormsSampleVB.DefaultPage", and nothing in this process has touched that
			// assembly - WebFormsRuntimeHost's resolver is what finds it, by probing the host's own
			// output directory as well as the classic <app>/bin. If that resolver regresses, the VB
			// pages stop compiling and this suite says so.
			var builder = WebApplication.CreateBuilder (new WebApplicationOptions {
				ContentRootPath = RepoPaths.SampleAppVB,
				ApplicationName = typeof (SampleAppVBFixture).Assembly.GetName ().Name,
			});

			builder.WebHost.UseUrls ("http://127.0.0.1:0");
			builder.Logging.ClearProviders ();

			app = builder.Build ();
			app.UseStaticFiles ();

			// Identical to the C# sample's call: the host is language-agnostic. What makes this a VB
			// application is <compilation defaultLanguage="vb"> and the Language="VB" page directives.
			app.UseWebForms (options => {
				options.PhysicalPath = RepoPaths.SampleAppVB;
				options.VirtualPath = "/";
				options.SiteName = "FunctionalTestsVB";
				// Its own compilation directory. The default is keyed on the application PATH, and
				// several suites host this same directory - in a solution-wide `dotnet test` they run
				// as parallel processes and would corrupt each other's generated pages.
				options.TemporaryFilesPath = System.IO.Path.Combine (
					System.IO.Path.GetTempPath (), "webforms-tests",
					typeof (SampleAppVBFixture).Assembly.GetName ().Name);
				options.StateSerializer = new System.Web.JsonStateObjectSerializer ();
				// Default.aspx declares Inherits="WebFormsSampleVB.DefaultPage", which names no
				// assembly - so it is resolved by scanning loaded assemblies, and nothing here would
				// otherwise have loaded this one. A deployed application does not need this: its
				// assembly is the host's own, or sits in <app>/bin, and both are loaded already.
				options.ApplicationAssemblies = new [] { typeof (global::WebFormsSampleVB.DefaultPage).Assembly };
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

	[CollectionDefinition (Name)]
	public sealed class WebFormsVBCollection : ICollectionFixture<SampleAppVBFixture>
	{
		public const string Name = "webforms-sample-vb";
	}
}
