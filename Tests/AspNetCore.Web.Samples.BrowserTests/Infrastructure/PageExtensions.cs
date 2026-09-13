//
// Navigation helpers for WebForms pages.
//

using System;
using System.Threading.Tasks;
using Microsoft.Playwright;
using Xunit;

// RunAndWaitForNavigationAsync is [Obsolete], and its replacements (WaitForURLAsync and friends) are the
// wrong tool: a postback navigates back to the SAME URL, and WaitForURL returns at once when the URL
// already matches, racing the postback instead of waiting for it. "Wait for the next navigation" is
// exactly what is needed, so the obsolete API stays.
#pragma warning disable CS0612, CS0618

namespace WebFormsPort.SamplesBrowserTests
{
	static class PageExtensions
	{
		/// <summary>Performs <paramref name="action"/> and waits for the full-page navigation it causes.</summary>
		public static async Task PostBackAsync (this IPage page, Func<Task> action)
		{
			await page.RunAndWaitForNavigationAsync (action);
		}

		/// <summary>Clicks and waits for the resulting postback to load.</summary>
		public static Task ClickAndWaitAsync (this IPage page, string selector)
		{
			return page.PostBackAsync (() => page.ClickAsync (selector));
		}

		/// <summary>
		/// Performs <paramref name="action"/> and asserts that it did NOT replace the document - a
		/// client-side cancellation or an async postback. A marker is planted on window first; a
		/// navigation would discard it.
		/// </summary>
		public static async Task AssertNoNavigationAsync (this IPage page, Func<Task> action, int settleMilliseconds = 750)
		{
			await page.EvaluateAsync ("() => { window.__noNavigationMarker = 42; }");
			await action ();
			await page.WaitForTimeoutAsync (settleMilliseconds);
			Assert.Equal (42, await page.EvaluateAsync<int> ("() => window.__noNavigationMarker || 0"));
		}

		public static Task<string> TextAsync (this IPage page, string selector)
		{
			return page.Locator (selector).InnerTextAsync ();
		}
	}
}
