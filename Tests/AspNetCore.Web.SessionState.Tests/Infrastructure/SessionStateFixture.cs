//
// Starts Samples/SessionStateSample on a real Kestrel server, once for the assembly.
//
// The sample's web.config says mode="StateServer", so this fixture is what supplies the store behind
// it - an in-memory IDistributedCache, kept on the fixture so tests can reach past HTTP and assert on
// what actually landed there. Proving the session is IN the cache is the only way to distinguish
// working out-of-process state from an InProc fallback that happens to behave the same.
//
// The SQL connection string is handed over here too, before anything freezes: SessionStateHostServices
// is one-shot per process, so a test cannot supply it later.
//

using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Web.Hosting.Kestrel;
using System.Web.SessionState;
using Xunit;

namespace WebFormsPort.SessionStateTests
{
	public sealed class SessionStateFixture : IAsyncLifetime
	{
		WebApplication app;

		public string BaseAddress { get; private set; }

		/// <summary>The store behind <c>mode="StateServer"</c> for this run.</summary>
		public IDistributedCache Cache { get; private set; }

		public async Task InitializeAsync ()
		{
			string appPath = RepoPaths.Sample ("SessionStateSample");

			var builder = WebApplication.CreateBuilder (new WebApplicationOptions {
				ContentRootPath = appPath,
				ApplicationName = typeof (SessionStateFixture).Assembly.GetName ().Name,
			});

			builder.WebHost.UseUrls ("http://127.0.0.1:0");
			builder.Logging.ClearProviders ();
			builder.Services.AddDistributedMemoryCache ();

			app = builder.Build ();

			Cache = app.Services.GetRequiredService<IDistributedCache> ();

			app.UseWebFormsSessionState (options => {
				// Null when no server is reachable, which is fine - the StateServer store never looks
				// at it, and the SQL tests are skipped in that case anyway.
				options.SqlConnectionString = SqlAvailability.ConnectionString;
			});

			app.UseWebForms (options => {
				options.PhysicalPath = appPath;
				options.VirtualPath = "/";
				options.SiteName = "SessionStateTests";
				options.TemporaryFilesPath = Path.Combine (Path.GetTempPath (), "webforms-tests",
									   typeof (SessionStateFixture).Assembly.GetName ().Name);
				options.ApplicationAssemblies = new [] {
					typeof (global::SessionStateSample.SessionPage).Assembly };
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
		/// A client with its own cookie container - which is what makes it a distinct browser, and the
		/// only way to get a second session out of the application.
		/// </summary>
		public HttpClient CreateClient ()
		{
			var handler = new HttpClientHandler { CookieContainer = new CookieContainer (), UseCookies = true };
			return new HttpClient (handler) { BaseAddress = new Uri (BaseAddress) };
		}

		/// <summary>Runs one operation against Session.aspx and returns the parsed response fields.</summary>
		public async Task<SessionResponse> RequestAsync (HttpClient client, string query = "")
		{
			HttpResponseMessage response = await client.GetAsync ("/Session.aspx" + query);
			string body = await response.Content.ReadAsStringAsync ();

			return new SessionResponse {
				StatusCode = response.StatusCode,
				Body = body,
				Mode = Extract (body, "mode"),
				Id = Extract (body, "id"),
				Result = Extract (body, "result"),
				Hits = Extract (body, "hits"),
			};
		}

		static string Extract (string body, string label)
		{
			// The page renders "<p>label: value</p>"; deliberately not an HTML parse, because the
			// assertion should fail loudly if the page shape changes rather than quietly return null.
			var match = System.Text.RegularExpressions.Regex.Match (
				body, @"<p>" + label + @":\s*(?<value>.*?)</p>",
				System.Text.RegularExpressions.RegexOptions.Singleline);

			return match.Success ? match.Groups ["value"].Value.Trim () : null;
		}
	}

	public sealed class SessionResponse
	{
		public HttpStatusCode StatusCode;
		public string Body;
		public string Mode;
		public string Id;
		public string Result;
		public string Hits;
	}

	[CollectionDefinition (Name)]
	public sealed class SessionStateCollection : ICollectionFixture<SessionStateFixture>
	{
		public const string Name = "session-state-sample";
	}
}
