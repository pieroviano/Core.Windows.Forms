//
// Starts Samples/MvcSample on a real Kestrel server and a real Chromium, once for the assembly.
//
// Headed by default so the test can be watched; set WEBFORMS_HEADLESS=1 to hide it.
//

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using System.Web.Hosting.Kestrel;
using Xunit;

namespace WebFormsPort.MvcBrowserTests
{
	public sealed class MvcBrowserFixture : IAsyncLifetime
	{
		WebApplication app;
		IPlaywright playwright;
		IBrowser browser;

		public string BaseAddress { get; private set; }

		public static bool Headless {
			get { return Environment.GetEnvironmentVariable ("WEBFORMS_HEADLESS") == "1"; }
		}

		public async Task InitializeAsync ()
		{
			string appPath = RepoPaths.Sample ("MvcSample");

			// MVC caches its controller-type scan to disk as MVC-ControllerTypeCache.xml under the
			// compilation directory. A cache written before <compilation><assemblies> listed the
			// application's own assembly is never re-scanned, and every route then fails with "the
			// controller for path '/...' was not found". Starting from a clean directory removes that
			// as a source of confusing, order-dependent failures.
			string temp = Path.Combine (Path.GetTempPath (), "webforms-tests",
						    typeof (MvcBrowserFixture).Assembly.GetName ().Name);
			if (Directory.Exists (temp))
				Directory.Delete (temp, recursive: true);

			var builder = WebApplication.CreateBuilder (new WebApplicationOptions {
				ContentRootPath = appPath,
				ApplicationName = typeof (MvcBrowserFixture).Assembly.GetName ().Name,
			});

			builder.WebHost.UseUrls ("http://127.0.0.1:0");
			builder.Logging.ClearProviders ();

			app = builder.Build ();
			app.UseStaticFiles ();
			app.UseWebForms (options => {
				options.PhysicalPath = appPath;
				options.VirtualPath = "/";
				options.SiteName = "MvcBrowserTests";
				options.TemporaryFilesPath = temp;
				// Controllers are found through BuildManager's referenced assemblies, which web.config
				// drives; this makes sure the assembly is loaded before the first request either way.
				options.ApplicationAssemblies = new [] { typeof (global::MvcSample.Controllers.HomeController).Assembly };
			});

			await app.StartAsync ();
			BaseAddress = app.Urls.First ();

			playwright = await Microsoft.Playwright.Playwright.CreateAsync ();
			browser = await playwright.Chromium.LaunchAsync (new BrowserTypeLaunchOptions {
				Headless = Headless,
				SlowMo = Headless ? 0 : 250,
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

		public async Task<(IBrowserContext Context, IPage Page)> NewPageAsync ()
		{
			IBrowserContext context = await browser.NewContextAsync (new BrowserNewContextOptions {
				BaseURL = BaseAddress,
			});

			return (context, await context.NewPageAsync ());
		}
	}

	[CollectionDefinition (Name, DisableParallelization = true)]
	public sealed class MvcBrowserCollection : ICollectionFixture<MvcBrowserFixture>
	{
		public const string Name = "mvc-browser";
	}
}
