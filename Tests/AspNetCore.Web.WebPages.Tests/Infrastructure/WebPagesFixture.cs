//
// Starts Samples/WebPagesSample on a real Kestrel server, once for the assembly.
//

using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Web.Hosting.Kestrel;
using Xunit;

namespace WebFormsPort.WebPagesTests
{
	public sealed class WebPagesFixture : IAsyncLifetime
	{
		WebApplication app;

		public string BaseAddress { get; private set; }

		public async Task InitializeAsync ()
		{
			string appPath = RepoPaths.Sample ("WebPagesSample");

			string temp = Path.Combine (Path.GetTempPath (), "webforms-tests",
						    typeof (WebPagesFixture).Assembly.GetName ().Name);
			if (Directory.Exists (temp))
				Directory.Delete (temp, recursive: true);

			var builder = WebApplication.CreateBuilder (new WebApplicationOptions {
				ContentRootPath = appPath,
				ApplicationName = typeof (WebPagesFixture).Assembly.GetName ().Name,
			});

			builder.WebHost.UseUrls ("http://127.0.0.1:0");
			builder.Logging.ClearProviders ();

			app = builder.Build ();
			app.UseStaticFiles ();
			app.UseWebForms (options => {
				options.PhysicalPath = appPath;
				options.VirtualPath = "/";
				options.SiteName = "WebPagesTests";
				options.TemporaryFilesPath = temp;
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
			return new HttpClient (new HttpClientHandler {
				UseCookies = true,
				CookieContainer = new CookieContainer (),
				AllowAutoRedirect = false,
			}) {
				BaseAddress = new Uri (BaseAddress),
			};
		}
	}

	[CollectionDefinition (Name)]
	public sealed class WebPagesCollection : ICollectionFixture<WebPagesFixture>
	{
		public const string Name = "webpages-sample";
	}
}
