//
// A browser context and page, watched for the failures a user would see but an assertion might not:
// uncaught script errors, console errors and 5xx responses.
//

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Playwright;
using Xunit;

namespace WebFormsPort.SamplesBrowserTests
{
	sealed class BrowserSession : IAsyncDisposable
	{
		readonly SampleFixture fixture;
		readonly List<string> problems = new List<string> ();

		public IBrowserContext Context { get; private set; }
		public IPage Page { get; private set; }

		BrowserSession (SampleFixture fixture)
		{
			this.fixture = fixture;
		}

		public static async Task<BrowserSession> StartAsync (SampleFixture fixture, bool javaScriptEnabled, bool allowServerErrors)
		{
			var session = new BrowserSession (fixture);

			session.Context = await fixture.Browser.NewContextAsync (new BrowserNewContextOptions {
				BaseURL = fixture.BaseAddress,
				JavaScriptEnabled = javaScriptEnabled,
			});
			session.Page = await session.Context.NewPageAsync ();

			session.Page.PageError += (s, message) => session.Add ("script error: " + message);
			session.Page.Console += (s, message) => {
				// A missing favicon is a console error in Chromium; it says nothing about the page.
				if (message.Type == "error" && !message.Text.StartsWith ("Failed to load resource", StringComparison.Ordinal))
					session.Add ("console error: " + message.Text);
			};
			session.Page.Response += (s, response) => {
				if (response.Status >= 500 && !allowServerErrors)
					session.Add ("HTTP " + response.Status + " " + response.Request.Method + " " + response.Url);
			};

			return session;
		}

		void Add (string problem)
		{
			lock (problems)
				problems.Add (problem);
		}

		public void AssertHealthy ()
		{
			lock (problems) {
				if (problems.Count > 0)
					throw new Xunit.Sdk.XunitException (Describe ("The page was not healthy."));
			}
		}

		/// <summary>A failure message carrying what the browser and the sample process saw.</summary>
		public string Describe (string message)
		{
			string[] seen;
			lock (problems)
				seen = problems.ToArray ();

			string output = fixture.Sample.Output;
			if (output.Length > 4000)
				output = "..." + output.Substring (output.Length - 4000);

			return message
				+ (seen.Length == 0 ? "" : "\n\nBrowser problems:\n  " + String.Join ("\n  ", seen))
				+ "\n\nPage URL: " + Page.Url
				+ "\n\nSample output (tail):\n" + output;
		}

		public async ValueTask DisposeAsync ()
		{
			await Context.CloseAsync ();
		}
	}
}
