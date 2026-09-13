//
// One sample application plus one browser, shared by every test class of that sample.
//
// The browser is HEADED: these tests are exploratory as much as they are regression tests, and watching
// the page being driven is the point. WEBFORMS_HEADLESS=1 hides it (CI, or a machine with no desktop);
// WEBFORMS_SLOWMO sets the delay between interactions in milliseconds (default 150).
//

using System;
using System.Threading.Tasks;
using Microsoft.Playwright;
using Xunit;

[assembly: CollectionBehavior (DisableTestParallelization = true)]

namespace WebFormsPort.SamplesBrowserTests
{
	public abstract class SampleFixture : IAsyncLifetime
	{
		IPlaywright playwright;

		protected abstract string SampleName { get; }

		public SampleProcess Sample { get; private set; }

		public IBrowser Browser { get; private set; }

		public string BaseAddress {
			get { return Sample.BaseAddress; }
		}

		static bool Headless {
			get { return Environment.GetEnvironmentVariable ("WEBFORMS_HEADLESS") == "1"; }
		}

		static float SlowMo {
			get {
				float value;
				return Single.TryParse (Environment.GetEnvironmentVariable ("WEBFORMS_SLOWMO"), out value) ? value : 150;
			}
		}

		public async Task InitializeAsync ()
		{
			Sample = await SampleProcess.StartAsync (SampleName);

			playwright = await Microsoft.Playwright.Playwright.CreateAsync ();
			Browser = await playwright.Chromium.LaunchAsync (new BrowserTypeLaunchOptions {
				Headless = Headless,
				SlowMo = Headless ? 0 : SlowMo,
			});
		}

		public async Task DisposeAsync ()
		{
			if (Browser != null)
				await Browser.CloseAsync ();

			playwright?.Dispose ();

			if (Sample != null)
				await Sample.DisposeAsync ();
		}

		/// <summary>
		/// Runs <paramref name="body"/> against a fresh browser context (its own cookies), then fails the
		/// test if the page raised a script error or the server answered any request with a 5xx (unless the
		/// test expects one: <paramref name="allowServerErrors"/>) - problems a test asserting on one element
		/// would otherwise walk straight past.
		/// </summary>
		public async Task RunAsync (Func<IPage, Task> body, bool javaScriptEnabled = true, bool allowServerErrors = false)
		{
			await using var session = await BrowserSession.StartAsync (this, javaScriptEnabled, allowServerErrors);
			try {
				await body (session.Page);
			} catch (Exception e) when (!(e is Xunit.Sdk.XunitException)) {
				throw new InvalidOperationException (session.Describe (e.Message), e);
			} catch (Xunit.Sdk.XunitException e) {
				throw new Xunit.Sdk.XunitException (session.Describe (e.Message));
			}

			session.AssertHealthy ();
		}
	}

	public sealed class WebFormsFixture : SampleFixture { protected override string SampleName => "WebFormsSample"; }
	public sealed class WebFormsVBFixture : SampleFixture { protected override string SampleName => "WebFormsSampleVB"; }
	public sealed class MvcFixture : SampleFixture { protected override string SampleName => "MvcSample"; }
	public sealed class WebPagesFixture : SampleFixture { protected override string SampleName => "WebPagesSample"; }
	public sealed class WebApiFixture : SampleFixture { protected override string SampleName => "WebApiSample"; }
	public sealed class WcfFixture : SampleFixture { protected override string SampleName => "WcfSample"; }
	public sealed class SessionStateFixture : SampleFixture { protected override string SampleName => "SessionStateSample"; }
	public sealed class DynamicDataFixture : SampleFixture { protected override string SampleName => "DynamicDataSample"; }

	[CollectionDefinition (Name)] public sealed class WebFormsCollection : ICollectionFixture<WebFormsFixture> { public const string Name = "WebFormsSample"; }
	[CollectionDefinition (Name)] public sealed class WebFormsVBCollection : ICollectionFixture<WebFormsVBFixture> { public const string Name = "WebFormsSampleVB"; }
	[CollectionDefinition (Name)] public sealed class MvcCollection : ICollectionFixture<MvcFixture> { public const string Name = "MvcSample"; }
	[CollectionDefinition (Name)] public sealed class WebPagesCollection : ICollectionFixture<WebPagesFixture> { public const string Name = "WebPagesSample"; }
	[CollectionDefinition (Name)] public sealed class WebApiCollection : ICollectionFixture<WebApiFixture> { public const string Name = "WebApiSample"; }
	[CollectionDefinition (Name)] public sealed class WcfCollection : ICollectionFixture<WcfFixture> { public const string Name = "WcfSample"; }
	[CollectionDefinition (Name)] public sealed class SessionStateCollection : ICollectionFixture<SessionStateFixture> { public const string Name = "SessionStateSample"; }
	[CollectionDefinition (Name)] public sealed class DynamicDataCollection : ICollectionFixture<DynamicDataFixture> { public const string Name = "DynamicDataSample"; }
}
