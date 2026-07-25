//
// @await in Razor views, against Samples/MvcSample's Views/AsyncView/Index.cshtml.
//
// The failure mode this guards against is not an exception. Before the parser patch, "@await Foo ()"
// parsed as the expression "await" followed by the LITERAL TEXT " Foo ()" - so the page rendered, with
// 200, showing the source of the call instead of its result. Every assertion here therefore checks the
// rendered VALUE, never merely that the request succeeded.
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
	public class AsyncViewTests
	{
		readonly MvcBrowserFixture fixture;

		public AsyncViewTests (MvcBrowserFixture fixture)
		{
			this.fixture = fixture;
		}

		async Task<string> GetAsync (string path)
		{
			using var client = new HttpClient { BaseAddress = new Uri (fixture.BaseAddress) };
			return await client.GetStringAsync (path);
		}

		static string Paragraph (string html, string id)
		{
			Match match = Regex.Match (html, "<p id=\"" + id + "\">(?<value>.*?)</p>", RegexOptions.Singleline);
			Assert.True (match.Success, "no <p id=\"" + id + "\"> in:\n" + html);
			return match.Groups ["value"].Value.Trim ();
		}

		[Fact]
		public async Task An_await_in_an_implicit_expression_renders_its_result ()
		{
			string html = await GetAsync ("/async-view");

			Assert.Equal ("loaded:implicit", Paragraph (html, "implicit"));
		}

		[Fact]
		public async Task An_await_inside_a_code_block_works ()
		{
			// This one needs the generated method to be async; the parser fix alone is not enough.
			string html = await GetAsync ("/async-view");

			Assert.Equal ("count:7", Paragraph (html, "block"));
		}

		[Fact]
		public async Task The_parser_keeps_reading_past_the_awaited_call ()
		{
			// "@await SlowData.LoadAsync("chained")" - member access and an argument after the keyword.
			string html = await GetAsync ("/async-view");

			Assert.Equal ("loaded:chained", Paragraph (html, "chained"));
		}

		[Fact]
		public async Task The_awaited_expression_is_not_emitted_as_literal_text ()
		{
			// The precise regression the parser patch fixes. If it came back, the page would still be
			// 200 and would contain the source text - so assert the source text is ABSENT.
			string html = await GetAsync ("/async-view");

			Assert.DoesNotContain ("SlowData.LoadAsync", html);
			Assert.DoesNotContain ("await ", Paragraph (html, "implicit"));
		}

		[Fact]
		public async Task The_word_await_in_literal_text_is_left_alone ()
		{
			// AsyncViewSupport only rewrites views that contain a real await, and decides by word
			// boundary - "awaiting" and "awaited" must not count, and must not be mangled either.
			string html = await GetAsync ("/async-view");

			Assert.Equal ("awaiting and awaited are just words", Paragraph (html, "literal"));
		}

		[Fact]
		public async Task Views_without_await_still_render ()
		{
			// The other half of the word-boundary decision: an ordinary view must be left exactly as
			// Razor generated it, with no async transform and no blocking call.
			using var client = new HttpClient { BaseAddress = new Uri (fixture.BaseAddress) };
			HttpResponseMessage response = await client.GetAsync ("/Home/Index");

			Assert.Equal (HttpStatusCode.OK, response.StatusCode);
			Assert.Contains ("Widgets", await response.Content.ReadAsStringAsync ());
		}
	}
}
