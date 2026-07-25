//
// Starts Samples/WcfSample on a real Kestrel server, once for the assembly.
//
// The ordering here is the thing under test as much as anything else: AddSvcEndpoints on the open
// service collection, UseSvcEndpoints before UseWebForms. Get either wrong and requests to a .svc
// reach System.Web's handler mapping instead of CoreWCF.
//

using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Web.Hosting.Kestrel;
using System.Web.ServiceModel;
using Xunit;

namespace WebFormsPort.ServiceModelTests
{
	public sealed class SvcFixture : IAsyncLifetime
	{
		WebApplication app;

		public string BaseAddress { get; private set; }

		public async Task InitializeAsync ()
		{
			string appPath = RepoPaths.Sample ("WcfSample");

			var builder = WebApplication.CreateBuilder (new WebApplicationOptions {
				ContentRootPath = appPath,
				ApplicationName = typeof (SvcFixture).Assembly.GetName ().Name,
			});

			builder.WebHost.UseUrls ("http://127.0.0.1:0");
			builder.Logging.ClearProviders ();

			builder.Services.AddSvcEndpoints (appPath, options => {
				options.ServiceAssemblies = new [] {
					typeof (global::WcfSample.Services.EchoService).Assembly };
			});

			app = builder.Build ();

			app.UseSvcEndpoints ();

			app.UseWebForms (options => {
				options.PhysicalPath = appPath;
				options.VirtualPath = "/";
				options.SiteName = "ServiceModelTests";
				options.TemporaryFilesPath = System.IO.Path.Combine (
					System.IO.Path.GetTempPath (), "webforms-tests",
					typeof (SvcFixture).Assembly.GetName ().Name);
				options.ApplicationAssemblies = new [] {
					typeof (global::WcfSample.Services.EchoService).Assembly };
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

		/// <summary>
		/// Sends a SOAP 1.1 request the way any client would: the body element named after the
		/// operation, in the contract's namespace, and a SOAPAction of {namespace}{contract}/{operation}.
		/// </summary>
		public async Task<HttpResponseMessage> PostSoapAsync (string path, string action, string bodyXml)
		{
			string envelope =
				"<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
				"<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"><s:Body>" +
				bodyXml +
				"</s:Body></s:Envelope>";

			var request = new HttpRequestMessage (HttpMethod.Post, path) {
				Content = new StringContent (envelope, System.Text.Encoding.UTF8),
			};

			request.Content.Headers.ContentType =
				new System.Net.Http.Headers.MediaTypeHeaderValue ("text/xml") { CharSet = "utf-8" };
			request.Headers.TryAddWithoutValidation ("SOAPAction", "\"" + action + "\"");

			using (HttpClient client = CreateClient ())
				return await client.SendAsync (request);
		}
	}

	[CollectionDefinition (Name)]
	public sealed class SvcCollection : ICollectionFixture<SvcFixture>
	{
		public const string Name = "wcf-sample";
	}
}
