//
// Starts the sample application on a real Kestrel server AND a real Chromium, once for the assembly.
//
// The browser is launched HEADED by default - you can watch the test drive the page, which is the
// point of having it. Set WEBFORMS_HEADLESS=1 to run it hidden (CI, or a machine with no desktop).
//

using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using System.Web.Hosting.Kestrel;
using Xunit;

namespace WebFormsPort.BrowserTests
{
	public sealed class BrowserAppFixture : IAsyncLifetime
	{
		WebApplication app;
		IPlaywright playwright;
		IBrowser browser;

		/// <summary>Base address of the running server, e.g. http://127.0.0.1:49512.</summary>
		public string BaseAddress { get; private set; }

		public IBrowser Browser {
			get { return browser; }
		}

		/// <summary>Headed unless WEBFORMS_HEADLESS is set.</summary>
		public static bool Headless {
			get { return Environment.GetEnvironmentVariable ("WEBFORMS_HEADLESS") == "1"; }
		}

		public async Task InitializeAsync ()
		{
			var builder = WebApplication.CreateBuilder (new WebApplicationOptions {
				ContentRootPath = RepoPaths.SampleApp,
				ApplicationName = typeof (BrowserAppFixture).Assembly.GetName ().Name,
			});

			builder.WebHost.UseUrls ("http://127.0.0.1:0");
			builder.Logging.ClearProviders ();

			app = builder.Build ();
			app.UseStaticFiles ();
			app.UseWebForms (options => {
				options.PhysicalPath = RepoPaths.SampleApp;
				options.VirtualPath = "/";
				options.SiteName = "BrowserTests";
				// Its own compilation directory. The default is keyed on the application PATH, and
				// several suites host this same directory - in a solution-wide `dotnet test` they run
				// as parallel processes and would corrupt each other's generated pages.
				options.TemporaryFilesPath = System.IO.Path.Combine (
					System.IO.Path.GetTempPath (), "webforms-tests",
					typeof (BrowserAppFixture).Assembly.GetName ().Name);
				options.StateSerializer = new System.Web.JsonStateObjectSerializer ();
			});

			await app.StartAsync ();
			BaseAddress = app.Urls.First ();

			playwright = await Microsoft.Playwright.Playwright.CreateAsync ();
			browser = await playwright.Chromium.LaunchAsync (new BrowserTypeLaunchOptions {
				Headless = Headless,
				// Visible runs are for watching, so slow the interactions enough to follow.
				SlowMo = Headless ? 0 : 300,
			});
		}

		public async Task DisposeAsync ()
		{
			if (browser != null)
				await browser.CloseAsync ();

			playwright?.Dispose ();

			if (app != null) {
				await app.StopAsync ();
				await app.DisposeAsync ();
			}
		}

		/// <summary>A fresh browser context (its own cookies and storage) and one page in it.</summary>
		public async Task<(IBrowserContext Context, IPage Page)> NewPageAsync ()
		{
			IBrowserContext context = await browser.NewContextAsync (new BrowserNewContextOptions {
				BaseURL = BaseAddress,
			});

			return (context, await context.NewPageAsync ());
		}
	}

	[CollectionDefinition (Name, DisableParallelization = true)]
	public sealed class BrowserCollection : ICollectionFixture<BrowserAppFixture>
	{
		public const string Name = "webforms-browser";
	}
}
