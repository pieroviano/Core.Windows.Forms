//
// UseWebFormsApplications: two applications behind one Kestrel, each in a child domain, recycled on a
// web.config change, on request, and after a crash.
//
// Each application is a private copy of the sample's content, so touching its web.config disturbs
// neither the repository nor the sample the rest of this suite runs in-process.
//

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Hosting.Kestrel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Xunit;

namespace WebFormsPort.RemotingTests
{
	[Collection (RemotingCollection.Name)]
	public class WebFormsApplicationsTests : IAsyncLifetime
	{
		WebApplication host;
		WebFormsApplications applications;
		HttpClient client;
		string root;

		WebFormsApplicationDomain One => applications.Domains [0];
		WebFormsApplicationDomain Two => applications.Domains [1];

		public async Task InitializeAsync ()
		{
			root = Path.Combine (Path.GetTempPath (), "webforms-tests", "AspNetCore.Web.Remoting.Tests", "apps-" + Guid.NewGuid ().ToString ("N"));
			string one = CopySample (Path.Combine (root, "one"));
			string two = CopySample (Path.Combine (root, "two"));

			applications = new WebFormsApplications ();
			applications.Add ("/one", one, o => o.TemporaryFilesPath = Path.Combine (root, "tmp-one"));
			applications.Add ("/two", two, o => o.TemporaryFilesPath = Path.Combine (root, "tmp-two"));

			var builder = WebApplication.CreateBuilder ();
			builder.WebHost.UseUrls ("http://127.0.0.1:0");
			builder.Logging.ClearProviders ();
			host = builder.Build ();
			host.UseWebFormsApplications (applications);
			host.Run (context => {
				context.Response.StatusCode = 404;
				return context.Response.WriteAsync ("not an application");
			});

			await host.StartAsync ();
			client = new HttpClient { BaseAddress = new Uri (host.Urls.First ()) };
		}

		public async Task DisposeAsync ()
		{
			client?.Dispose ();
			applications?.Dispose ();
			if (host != null) {
				await host.StopAsync ();
				await host.DisposeAsync ();
			}
			try {
				Directory.Delete (root, recursive: true);
			} catch (IOException) {
			} catch (UnauthorizedAccessException) {
			}
		}

		static string CopySample (string destination)
		{
			Directory.CreateDirectory (destination);
			foreach (string file in new [] { "Default.aspx", "web.config" })
				File.Copy (Path.Combine (RepoPaths.Sample ("RemotingSample"), file), Path.Combine (destination, file));
			return destination;
		}

		async Task<(HttpStatusCode Status, int Pid, string VirtualPath, string Body)> GetAsync (string path)
		{
			HttpResponseMessage response = await client.GetAsync (path);
			string body = await response.Content.ReadAsStringAsync ();
			Match pid = Regex.Match (body, @"<p>pid: (\d+)</p>");
			Match vpath = Regex.Match (body, @"<p>vpath: (.*?)</p>");
			return (response.StatusCode, pid.Success ? Int32.Parse (pid.Groups [1].Value) : 0, vpath.Groups [1].Value, body);
		}

		[Fact]
		public async Task Each_application_runs_in_its_own_process_under_its_own_virtual_path ()
		{
			var one = await GetAsync ("/one/Default.aspx");
			var two = await GetAsync ("/two/Default.aspx");

			Assert.Equal (HttpStatusCode.OK, one.Status);
			Assert.Equal (HttpStatusCode.OK, two.Status);
			Assert.Equal ("/one", one.VirtualPath.TrimEnd ('/'));
			Assert.Equal ("/two", two.VirtualPath.TrimEnd ('/'));
			Assert.NotEqual (one.Pid, two.Pid);
			Assert.NotEqual (Environment.ProcessId, one.Pid);
			Assert.Equal (one.Pid, One.ProcessId);
		}

		[Fact]
		public async Task Method_form_body_and_response_headers_cross_the_boundary ()
		{
			HttpResponseMessage response = await client.PostAsync ("/two/Default.aspx",
				new FormUrlEncodedContent (new [] { new System.Collections.Generic.KeyValuePair<string, string> ("x", "a & b") }));
			string body = await response.Content.ReadAsStringAsync ();

			Assert.True (response.StatusCode == HttpStatusCode.OK, body);
			Assert.Contains ("<p>method: POST</p>", body);
			Assert.Contains ("<p>echo: a &amp; b</p>", body);
			Assert.Equal ("/two", response.Headers.GetValues ("X-Application").Single ().TrimEnd ('/'));
		}

		[Fact]
		public async Task A_path_outside_every_application_goes_to_the_next_middleware ()
		{
			HttpResponseMessage response = await client.GetAsync ("/three/Default.aspx");

			Assert.Equal (HttpStatusCode.NotFound, response.StatusCode);
			Assert.Equal ("not an application", await response.Content.ReadAsStringAsync ());
		}

		/// <summary>
		/// Upstream's own watcher sees web.config change and calls HttpRuntime.UnloadAppDomain; the host
		/// replaces that application's process and leaves the other alone.
		/// </summary>
		[Fact]
		public async Task Changing_web_config_recycles_only_that_application ()
		{
			int before = (await GetAsync ("/one/Default.aspx")).Pid;
			int other = (await GetAsync ("/two/Default.aspx")).Pid;

			string config = Path.Combine (One.PhysicalPath, "web.config");
			File.WriteAllText (config, File.ReadAllText (config) + Environment.NewLine + "<!-- touched -->");

			int after = await WaitForNewProcessAsync ("/one/Default.aspx", before);

			Assert.NotEqual (before, after);
			Assert.Equal (other, (await GetAsync ("/two/Default.aspx")).Pid);
			Assert.True (One.Generations >= 2);
		}

		[Fact]
		public async Task Recycle_replaces_the_process ()
		{
			int before = (await GetAsync ("/two/Default.aspx")).Pid;

			Two.Recycle ();

			var after = await GetAsync ("/two/Default.aspx");
			Assert.Equal (HttpStatusCode.OK, after.Status);
			Assert.NotEqual (before, after.Pid);
		}

		[Fact]
		public async Task A_crashed_application_is_restarted ()
		{
			int before = (await GetAsync ("/one/Default.aspx")).Pid;

			using (Process process = Process.GetProcessById (before)) {
				process.Kill ();
				process.WaitForExit ();
			}

			Assert.NotEqual (before, await WaitForNewProcessAsync ("/one/Default.aspx", before));
		}

		// Recycling is asynchronous - a file notification, then a process start - so poll. Every answer on the
		// way must be a success or a 503: a 503 is what a request inside a process that died gets, and anything
		// else means a recycle was visible to a client.
		async Task<int> WaitForNewProcessAsync (string path, int previous)
		{
			var clock = Stopwatch.StartNew ();
			while (clock.Elapsed < TimeSpan.FromSeconds (60)) {
				var response = await GetAsync (path);
				if (response.Status == HttpStatusCode.OK && response.Pid != previous)
					return response.Pid;
				if (response.Status != HttpStatusCode.OK && response.Status != HttpStatusCode.ServiceUnavailable)
					Assert.Fail ("Unexpected " + (int) response.Status + " while recycling: " + response.Body);
				await Task.Delay (250);
			}
			Assert.Fail ("The application at " + path + " was not replaced within 60 seconds.");
			return 0;
		}
	}
}
