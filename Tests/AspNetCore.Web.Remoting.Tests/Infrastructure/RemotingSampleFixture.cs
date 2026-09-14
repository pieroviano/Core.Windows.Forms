//
// Starts a state server process and Samples/RemotingSample on a real Kestrel, once for the assembly.
//
// Order matters: the sample's web.config says mode="StateServer" against 127.0.0.1:42424, and its
// SessionStateModule connects on the first request, so the state server has to be listening first.
//

using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using System.Web.Hosting.Kestrel;
using Xunit;

namespace WebFormsPort.RemotingTests
{
	public sealed class RemotingSampleFixture : IAsyncLifetime
	{
		WebApplication app;

		public string BaseAddress { get; private set; }

		public StateServerProcess StateServer { get; private set; }

		public async Task InitializeAsync ()
		{
			StateServer = StateServerProcess.Start (port: 42424);

			string appPath = RepoPaths.Sample ("RemotingSample");

			var builder = WebApplication.CreateBuilder (new WebApplicationOptions {
				ContentRootPath = appPath,
				ApplicationName = typeof (RemotingSampleFixture).Assembly.GetName ().Name,
			});

			builder.WebHost.UseUrls ("http://127.0.0.1:0");
			builder.Logging.ClearProviders ();

			app = builder.Build ();

			app.UseWebFormsRemoteStateServer ();
			app.UseWebForms (options => {
				options.PhysicalPath = appPath;
				options.VirtualPath = "/";
				options.SiteName = "RemotingTests";
				options.TemporaryFilesPath = TestPaths.Temporary ("sample");
				options.ApplicationAssemblies = new [] { typeof (global::RemotingSample.Calculator).Assembly };
			});

			await app.StartAsync ();
			BaseAddress = app.Urls.First ();
		}

		public async Task DisposeAsync ()
		{
			if (app != null) {
				await app.StopAsync ();
				await app.DisposeAsync ();
			}

			StateServer?.Dispose ();
		}

		/// <summary>Replaces the state server process with a fresh one on the same port.</summary>
		public void RestartStateServer ()
		{
			int port = StateServer.Port;
			StateServer.Dispose ();
			StateServer = StateServerProcess.Start (port);
		}

		/// <summary>A client with its own cookie container, i.e. its own session.</summary>
		public HttpClient CreateClient ()
		{
			var handler = new HttpClientHandler { CookieContainer = new CookieContainer (), UseCookies = true };
			return new HttpClient (handler) { BaseAddress = new Uri (BaseAddress) };
		}
	}

	static class TestPaths
	{
		/// <summary>A compilation directory private to this test run and purpose.</summary>
		public static string Temporary (string purpose)
			=> Path.Combine (Path.GetTempPath (), "webforms-tests", "AspNetCore.Web.Remoting.Tests", purpose);
	}

	[CollectionDefinition (Name)]
	public sealed class RemotingCollection : ICollectionFixture<RemotingSampleFixture>
	{
		public const string Name = "remoting-sample";
	}
}
