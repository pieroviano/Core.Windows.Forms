//
// System.Web.Optimization against Samples/MvcSample.
//

using System;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Xunit;

namespace WebFormsPort.MvcBrowserTests
{
	[Collection (MvcBrowserCollection.Name)]
	public class BundlingTests
	{
		readonly MvcBrowserFixture fixture;

		public BundlingTests (MvcBrowserFixture fixture)
		{
			this.fixture = fixture;
		}

		HttpClient Client ()
		{
			return new HttpClient { BaseAddress = new Uri (fixture.BaseAddress) };
		}

		async Task<string> GetAsync (string path)
		{
			using HttpClient client = Client ();
			return await client.GetStringAsync (path);
		}

		async Task<string> BundleUrlAsync (string pattern)
		{
			string html = await GetAsync ("/Home/Bundles");
			Match match = Regex.Match (html, pattern);

			Assert.True (match.Success, "no " + pattern + " in:\n" + html);
			return match.Groups [1].Value;
		}

		[Fact]
		public async Task A_script_bundle_renders_one_tag_pointing_at_the_bundle ()
		{
			string html = await GetAsync ("/Home/Bundles");

			Assert.Matches (@"<script src=""/bundles/app\?v=[A-Za-z0-9_-]+""></script>", html);
		}

		[Fact]
		public async Task A_style_bundle_renders_a_stylesheet_link ()
		{
			string html = await GetAsync ("/Home/Bundles");

			Assert.Matches (@"<link href=""/Content/css\?v=[A-Za-z0-9_-]+"" rel=""stylesheet""/>", html);
		}

		[Fact]
		public async Task The_script_bundle_serves_its_files_concatenated_in_order ()
		{
			string url = await BundleUrlAsync (@"<script src=""(/bundles/app\?v=[^""]+)""");

			using HttpClient client = Client ();
			HttpResponseMessage response = await client.GetAsync (url);
			string body = await response.Content.ReadAsStringAsync ();

			Assert.Equal (HttpStatusCode.OK, response.StatusCode);
			Assert.Equal ("text/javascript", response.Content.Headers.ContentType.MediaType);
			Assert.Contains ("bundleFirst", body);
			Assert.Contains ("bundleSecond", body);
			Assert.True (body.IndexOf ("bundleFirst", StringComparison.Ordinal) <
				     body.IndexOf ("bundleSecond", StringComparison.Ordinal),
				     "files came back out of include order");
		}

		[Fact]
		public async Task The_style_bundle_is_css_and_is_not_joined_with_semicolons ()
		{
			string url = await BundleUrlAsync (@"<link href=""(/Content/css\?v=[^""]+)""");

			using HttpClient client = Client ();
			HttpResponseMessage response = await client.GetAsync (url);
			string body = await response.Content.ReadAsStringAsync ();

			Assert.Equal ("text/css", response.Content.Headers.ContentType.MediaType);
			Assert.Contains ("site.css", body);
			Assert.Contains ("print.css", body);

			// ";" between two stylesheets is a syntax error browsers recover from by dropping the next
			// rule - a silent, maddening bug, hence the separate ConcatenationToken on StyleBundle.
			Assert.DoesNotContain ("}\n;", body.Replace ("\r\n", "\n"));
		}

		[Fact]
		public async Task A_wildcard_include_picks_up_every_matching_file ()
		{
			string url = await BundleUrlAsync (@"<script src=""(/bundles/all-scripts\?v=[^""]+)""");
			string body = await GetAsync (url);

			Assert.Contains ("bundleFirst", body);
			Assert.Contains ("bundleSecond", body);
		}

		[Fact]
		public async Task Scripts_Url_and_Styles_Url_agree_with_the_rendered_tags ()
		{
			string html = await GetAsync ("/Home/Bundles");

			string tag = Regex.Match (html, @"<script src=""(/bundles/app\?v=[^""]+)""").Groups [1].Value;
			string url = Regex.Match (html, @"<p id=""script-url"">([^<]+)</p>").Groups [1].Value;

			Assert.Equal (tag, url);
		}

		[Fact]
		public async Task The_version_hash_is_stable_across_requests ()
		{
			// It is derived from file content, so an unchanged application must produce an unchanged
			// URL - otherwise every request busts the client cache and bundling is worse than useless.
			string first = await BundleUrlAsync (@"<script src=""(/bundles/app\?v=[^""]+)""");
			string second = await BundleUrlAsync (@"<script src=""(/bundles/app\?v=[^""]+)""");

			Assert.Equal (first, second);
		}

		[Fact]
		public async Task Two_different_bundles_do_not_share_a_url ()
		{
			string html = await GetAsync ("/Home/Bundles");

			string scripts = Regex.Match (html, @"<script src=""(/bundles/app\?v=[^""]+)""").Groups [1].Value;
			string styles = Regex.Match (html, @"<link href=""(/Content/css\?v=[^""]+)""").Groups [1].Value;

			Assert.NotEqual (scripts, styles);
		}

		[Fact]
		public async Task A_versioned_bundle_is_cacheable ()
		{
			string url = await BundleUrlAsync (@"<script src=""(/bundles/app\?v=[^""]+)""");

			using HttpClient client = Client ();
			HttpResponseMessage response = await client.GetAsync (url);

			// The URL changes whenever the content does, so a long cache is correct rather than reckless.
			Assert.NotNull (response.Headers.CacheControl);
			Assert.True (response.Headers.CacheControl.Public, "bundle response was not publicly cacheable");
		}
	}
}
