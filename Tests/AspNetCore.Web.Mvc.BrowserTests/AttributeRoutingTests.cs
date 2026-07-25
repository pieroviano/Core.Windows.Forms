//
// [Route] / [RoutePrefix] over real HTTP, against Samples/MvcSample's CatalogController.
//
// HTTP rather than Playwright: a route either matches or it does not, and that is a status code and a
// response body. A browser would add twenty seconds and observe nothing extra.
//

using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Xunit;

namespace WebFormsPort.MvcBrowserTests
{
	[Collection (MvcBrowserCollection.Name)]
	public class AttributeRoutingTests
	{
		readonly MvcBrowserFixture fixture;

		public AttributeRoutingTests (MvcBrowserFixture fixture)
		{
			this.fixture = fixture;
		}

		HttpClient Client ()
		{
			return new HttpClient { BaseAddress = new Uri (fixture.BaseAddress) };
		}

		async Task<(HttpStatusCode Status, string Body)> GetAsync (string path)
		{
			using HttpClient client = Client ();
			HttpResponseMessage response = await client.GetAsync (path);
			return (response.StatusCode, await response.Content.ReadAsStringAsync ());
		}

		[Fact]
		public async Task A_prefixed_action_with_an_empty_template_is_the_prefix_itself ()
		{
			// [RoutePrefix ("catalog")] + [Route ("")] means /catalog, not /catalog/.
			var result = await GetAsync ("/catalog");

			Assert.Equal (HttpStatusCode.OK, result.Status);
			Assert.Equal ("catalog index", result.Body);
		}

		[Fact]
		public async Task A_literal_segment_wins_over_a_constrained_parameter ()
		{
			// The whole reason precedence ordering exists. CatalogController declares New AFTER
			// Details, so declaration order would send /catalog/new to Details - which would then fail
			// to bind "new" to an int and 404. Specificity has to decide, not source order.
			var result = await GetAsync ("/catalog/new");

			Assert.Equal (HttpStatusCode.OK, result.Status);
			Assert.Equal ("catalog new", result.Body);
		}

		[Fact]
		public async Task An_int_constraint_binds_and_a_non_int_does_not_match ()
		{
			Assert.Equal ("catalog details 42", (await GetAsync ("/catalog/42")).Body);

			// "notanint" fails {id:int}, and nothing else in the application claims that URL.
			Assert.Equal (HttpStatusCode.NotFound, (await GetAsync ("/catalog/notanint")).Status);
		}

		[Fact]
		public async Task An_inline_default_applies_when_the_segment_is_absent ()
		{
			Assert.Equal ("catalog reviews 42 page 1", (await GetAsync ("/catalog/42/reviews")).Body);
			Assert.Equal ("catalog reviews 42 page 3", (await GetAsync ("/catalog/42/reviews/3")).Body);
		}

		[Fact]
		public async Task Constraints_compose_on_one_parameter ()
		{
			// {term:alpha:minlength(3)} - both have to hold.
			Assert.Equal ("catalog search widgets", (await GetAsync ("/catalog/search/widgets")).Body);
			Assert.Equal (HttpStatusCode.NotFound, (await GetAsync ("/catalog/search/ab")).Status);      // too short
			Assert.Equal (HttpStatusCode.NotFound, (await GetAsync ("/catalog/search/wid9ets")).Status); // not alpha
		}

		[Fact]
		public async Task A_named_route_round_trips_back_to_its_own_url ()
		{
			// Url.RouteUrl ("catalog-named") has to regenerate the URL the request arrived on -
			// generation and matching using the same template is what makes names worth having.
			var result = await GetAsync ("/catalog/named");

			Assert.Equal ("catalog named -> /catalog/named", result.Body);
		}

		[Fact]
		public async Task A_tilde_template_escapes_the_controller_prefix ()
		{
			Assert.Equal ("catalog legacy", (await GetAsync ("/legacy-catalog")).Body);

			// And is NOT reachable under the prefix it escaped.
			Assert.Equal (HttpStatusCode.NotFound, (await GetAsync ("/catalog/legacy-catalog")).Status);
		}

		[Fact]
		public async Task A_catch_all_captures_the_remaining_path_including_slashes ()
		{
			Assert.Equal ("catalog files a/b/c.txt", (await GetAsync ("/catalog/files/a/b/c.txt")).Body);
		}

		[Fact]
		public async Task Verb_attributes_become_a_method_constraint ()
		{
			// GET /catalog/42 and POST /catalog/42 are two different actions sharing one template.
			using HttpClient client = Client ();

			HttpResponseMessage posted = await client.PostAsync ("/catalog/42", new StringContent (""));
			Assert.Equal (HttpStatusCode.OK, posted.StatusCode);
			Assert.Equal ("catalog update 42", await posted.Content.ReadAsStringAsync ());

			Assert.Equal ("catalog details 42", (await GetAsync ("/catalog/42")).Body);
		}

		[Fact]
		public async Task An_async_action_returning_Task_of_ActionResult_works ()
		{
			// MVC 4's TaskAsyncActionDescriptor - one of the things the MVC 3 to 4 upgrade bought.
			Assert.Equal ("catalog async", (await GetAsync ("/catalog/async")).Body);
		}

		[Fact]
		public async Task Conventional_routes_still_work_alongside_attribute_routes ()
		{
			// MapMvcAttributeRoutes runs first and must not shadow {controller}/{action}/{id}.
			var result = await GetAsync ("/Home/Plain");

			Assert.Equal (HttpStatusCode.OK, result.Status);
			Assert.Equal ("plain content result", result.Body);
		}
	}
}
