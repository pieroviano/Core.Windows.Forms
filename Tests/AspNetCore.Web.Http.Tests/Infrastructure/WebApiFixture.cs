//
// Starts Samples/WebApiSample on a real Kestrel server, once for the assembly.
//

using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Web.Hosting.Kestrel;
using Xunit;

namespace WebFormsPort.WebApiTests
{
	public sealed class WebApiFixture : IAsyncLifetime
	{
		WebApplication app;

		public string BaseAddress { get; private set; }

		public async Task InitializeAsync ()
		{
			string appPath = RepoPaths.Sample ("WebApiSample");

			// Web API caches its controller-type scan to disk exactly as MVC does. Start clean so a
			// cache written before <compilation><assemblies> was right cannot survive into a run.
			string temp = Path.Combine (Path.GetTempPath (), "webforms-tests",
						    typeof (WebApiFixture).Assembly.GetName ().Name);
			if (Directory.Exists (temp))
				Directory.Delete (temp, recursive: true);

			var builder = WebApplication.CreateBuilder (new WebApplicationOptions {
				ContentRootPath = appPath,
				ApplicationName = typeof (WebApiFixture).Assembly.GetName ().Name,
			});

			builder.WebHost.UseUrls ("http://127.0.0.1:0");
			builder.Logging.ClearProviders ();

			app = builder.Build ();
			app.UseWebForms (options => {
				options.PhysicalPath = appPath;
				options.VirtualPath = "/";
				options.SiteName = "WebApiTests";
				options.TemporaryFilesPath = temp;
				options.ApplicationAssemblies = new [] {
					typeof (global::WebApiSample.Controllers.WidgetsController).Assembly };
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

		public HttpClient CreateClient ()
		{
			return new HttpClient { BaseAddress = new Uri (BaseAddress) };
		}
	}

	[CollectionDefinition (Name)]
	public sealed class WebApiCollection : ICollectionFixture<WebApiFixture>
	{
		public const string Name = "webapi-sample";
	}
}
