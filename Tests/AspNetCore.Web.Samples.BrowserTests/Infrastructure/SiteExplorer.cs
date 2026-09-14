//
// Walks a sample application the way a curious visitor with a directory listing would: every file on
// disk is requested by its URL, and every link the served pages render is followed.
//
// What each file must answer is ASP.NET's rule for its kind, not whatever the port happens to do:
//
//   - pages, handlers, services and static assets are served (redirects followed - a protected page
//     ends on the login page);
//   - source, configuration, markup fragments and resources are refused by HttpForbiddenHandler
//     (root web.config <httpHandlers>) with 403;
//   - a Razor file whose name starts with "_" is refused with 404 (WebPageRoute: UnderscoreBlocked), and so
//     is anything under an MVC Views/ folder (Views/web.config maps "*" to HttpNotFoundHandler);
//   - special App_* folders are never served: 403 or 404 depending on which check refuses first.
//
// Files that are not addressable at all - Dynamic Data page templates, reached only through a route -
// and build or IDE leftovers are skipped; each skip says why.
//

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Playwright;
using Xunit;

namespace WebFormsPort.SamplesBrowserTests
{
	static class SiteExplorer
	{
		static readonly string [] Forbidden = {
			".asax", ".ascx", ".master", ".skin", ".sitemap", ".config", ".cs", ".vb",
			".csproj", ".vbproj", ".resx", ".browser",
		};

		static readonly string [] Served = {
			".aspx", ".asmx", ".ashx", ".svc", ".cshtml", ".css", ".js", ".gif", ".png", ".jpg", ".html", ".txt",
		};

		static readonly string [] Skipped = { ".json", ".user", ".md" };

		/// <summary>The status ASP.NET gives a file, or null when the file is not a URL to test.</summary>
		public static int [] Expected (string relative)
		{
			string path = relative.Replace ('\\', '/');
			string name = Path.GetFileName (path);
			string extension = Path.GetExtension (path).ToLowerInvariant ();
			string [] segments = path.Split ('/');

			if (segments.Any (s => s == "bin" || s == "obj" || s == "Properties" || s == "My Project"))
				return null;
			if (Skipped.Contains (extension))
				return null;
			// Reached through {table}/{action}.aspx only; requested directly there is no table.
			if (path.StartsWith ("DynamicData/PageTemplates/", StringComparison.OrdinalIgnoreCase))
				return null;

			// Views/web.config maps "*" to HttpNotFoundHandler, and a child directory's handlers are matched
			// before the ones it inherits - so even Views/web.config itself is a 404, not root's 403.
			if (segments [0].Equals ("Views", StringComparison.OrdinalIgnoreCase) && segments.Length > 1)
				return new [] { 404 };

			if (segments [0].StartsWith ("App_", StringComparison.OrdinalIgnoreCase) &&
			    !segments [0].Equals ("App_Themes", StringComparison.OrdinalIgnoreCase))
				return new [] { 403, 404 };

			if (Forbidden.Contains (extension) || name.EndsWith (".designer.cs", StringComparison.OrdinalIgnoreCase))
				return new [] { 403 };

			if (extension == ".cshtml") {
				if (name.StartsWith ("_") || segments [0].Equals ("Views", StringComparison.OrdinalIgnoreCase))
					return new [] { 404 };
				return new [] { 200 };
			}

			if (Served.Contains (extension))
				return new [] { 200 };

			return null;
		}

		/// <summary>
		/// Requests every file under the sample, then every same-site link the served pages rendered.
		/// Fails listing every URL that answered differently from ASP.NET, not just the first.
		/// </summary>
		public static async Task ExploreAsync (SampleFixture fixture, IPage page, Func<string, bool> skip = null)
		{
			string root = RepoPaths.Sample (fixture.SampleNameForTests);
			var failures = new List<string> ();
			var links = new SortedSet<string> (StringComparer.Ordinal);
			int requested = 0;

			foreach (string file in Directory.EnumerateFiles (root, "*", SearchOption.AllDirectories).OrderBy (f => f)) {
				string relative = Path.GetRelativePath (root, file).Replace ('\\', '/');
				int [] expected = Expected (relative);
				if (expected == null || (skip != null && skip (relative)))
					continue;

				string url = "/" + String.Join ("/", relative.Split ('/').Select (Uri.EscapeDataString));
				requested++;

				// Redirects are NOT followed. Authorization runs before the handler is chosen, so anything under
				// a protected directory - its web.config included - answers an anonymous visitor with a
				// redirect to the login page; following it would report the login page's 200 for a file that
				// must never be served. A page that redirects by itself (Response.Redirect) is still "served".
				IAPIResponse response = await page.APIRequest.GetAsync (url, new APIRequestContextOptions { MaxRedirects = 0 });
				if (response.Status == 302 && response.Headers.TryGetValue ("location", out string location) &&
				    (location.IndexOf ("login", StringComparison.OrdinalIgnoreCase) >= 0 || expected.Contains (200)))
					continue;
				if (!expected.Contains (response.Status)) {
					failures.Add (url + " -> " + response.Status + " (ASP.NET: " + String.Join (" or ", expected) + ")" +
						      Excerpt (await response.TextAsync ()));
					continue;
				}

				// Pages that render markup are loaded in the browser too: their script must run cleanly,
				// and their links are the second half of the walk.
				string extension = Path.GetExtension (relative).ToLowerInvariant ();
				if (response.Status == 200 && (extension == ".aspx" || extension == ".cshtml") &&
				    (response.Headers.TryGetValue ("content-type", out string type) && type.StartsWith ("text/html"))) {
					await page.GotoAsync (url);
					foreach (string href in await page.EvalOnSelectorAllAsync<string []> ("a[href]", "links => links.map(a => a.href)"))
						if (href.StartsWith (fixture.BaseAddress + "/", StringComparison.Ordinal))
							links.Add (href.Split ('#') [0]);
				}
			}

			foreach (string link in links) {
				IAPIResponse response = await page.APIRequest.GetAsync (link);
				if (response.Status >= 400)
					failures.Add ("link " + link + " -> " + response.Status + Excerpt (await response.TextAsync ()));
			}

			Assert.True (requested > 0, "no servable files found under " + root);
			Assert.True (failures.Count == 0,
				failures.Count + " of " + (requested + links.Count) + " URLs did not answer as ASP.NET would:\n  " +
				String.Join ("\n  ", failures));
		}

		/// <summary>
		/// Follows same-site links breadth-first from <paramref name="seeds"/>, loading each HTML page in
		/// the browser, for applications whose URLs are routes rather than files. Every page reached must
		/// answer below 400.
		/// </summary>
		public static async Task CrawlAsync (SampleFixture fixture, IPage page, int maxPages, params string [] seeds)
		{
			var queue = new Queue<string> (seeds.Select (s => fixture.BaseAddress + s));
			var seen = new HashSet<string> (queue, StringComparer.Ordinal);
			var failures = new List<string> ();
			int visited = 0;

			while (queue.Count > 0 && visited < maxPages) {
				string url = queue.Dequeue ();
				visited++;

				IAPIResponse probe = await page.APIRequest.GetAsync (url);
				if (probe.Status >= 400) {
					failures.Add (url + " -> " + probe.Status + Excerpt (await probe.TextAsync ()));
					continue;
				}
				if (!probe.Headers.TryGetValue ("content-type", out string type) || !type.StartsWith ("text/html"))
					continue;

				await page.GotoAsync (url);
				foreach (string href in await page.EvalOnSelectorAllAsync<string []> ("a[href]", "links => links.map(a => a.href)")) {
					string link = href.Split ('#') [0];
					if (link.StartsWith (fixture.BaseAddress + "/", StringComparison.Ordinal) && seen.Add (link))
						queue.Enqueue (link);
				}
			}

			Assert.True (visited > 1, "the crawl reached no page beyond its seeds: " + String.Join (", ", seeds));
			Assert.True (failures.Count == 0,
				failures.Count + " of " + visited + " crawled URLs failed:\n  " + String.Join ("\n  ", failures));
		}

		static string Excerpt (string body)
		{
			// The yellow screen's exception line is what makes a failure actionable.
			int at = body.IndexOf ("<h1>", StringComparison.Ordinal);
			if (at < 0)
				return "";
			int end = body.IndexOf ("</h2>", at, StringComparison.Ordinal);
			string text = body.Substring (at, (end < 0 ? Math.Min (body.Length, at + 300) : end) - at);
			var plain = new StringBuilder ();
			bool tag = false;
			foreach (char c in text) {
				if (c == '<') tag = true;
				else if (c == '>') { tag = false; plain.Append (' '); }
				else if (!tag) plain.Append (c);
			}
			return " [" + System.Net.WebUtility.HtmlDecode (plain.ToString ()).Trim () + "]";
		}
	}
}
