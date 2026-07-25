//
// Dynamic Data scaffolding, over HTTP, against a context that is not EF Core.
//

using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Xunit;

namespace WebFormsPort.LegacyStacksHttpTests
{
	[Collection (DynamicDataCollection.Name)]
	public class DynamicDataScaffoldingTests
	{
		readonly DynamicDataFixture fixture;

		public DynamicDataScaffoldingTests (DynamicDataFixture fixture)
		{
			this.fixture = fixture;
		}

		[Fact]
		public async Task The_model_lists_queryable_members_and_nothing_else ()
		{
			using HttpClient client = fixture.CreateClient ();
			string html = await client.GetStringAsync ("/Default.aspx");

			Assert.Contains ("/Products/List.aspx", html);
			Assert.Contains ("/Categories/List.aspx", html);

			// ShopContext also has a string property. A string is IEnumerable<char>, so a provider that
			// merely looked for IEnumerable would scaffold it as a table of characters.
			Assert.DoesNotContain ("ConnectionString", html);
		}

		[Fact]
		public async Task A_scaffolded_list_page_renders_the_rows ()
		{
			// The regression guard for the route handler: GetRequestMetaTable used to return null on
			// every request, because nothing ever published the RouteContext. This page's first line
			// asks for it, so a null takes the whole page down with a NullReferenceException.
			using HttpClient client = fixture.CreateClient ();
			HttpResponseMessage response = await client.GetAsync ("/Products/List.aspx");
			string html = await response.Content.ReadAsStringAsync ();

			Assert.Equal (HttpStatusCode.OK, response.StatusCode);
			Assert.Contains ("Anvil", html);
			Assert.Contains ("Birdseed", html);
			Assert.Contains ("Rocket", html);
		}

		[Fact]
		public async Task One_page_template_serves_every_table ()
		{
			// The point of scaffolding: DynamicData/PageTemplates/List.aspx names no entity type, and
			// the two tables have nothing in common but being queryable.
			using HttpClient client = fixture.CreateClient ();
			string html = await client.GetStringAsync ("/Categories/List.aspx");

			Assert.Contains ("Hardware", html);
			Assert.Contains ("Consumables", html);
			Assert.DoesNotContain ("Anvil", html);
		}

		[Fact]
		public async Task A_generated_key_and_a_computed_property_are_not_scaffolded ()
		{
			// Upstream Dynamic Data's rule, not this port's: MetaColumn.Scaffold returns false for
			// IsGenerated, and the provider marks an int primary key generated (the EF Core convention)
			// and a property with no setter generated as well.
			using HttpClient client = fixture.CreateClient ();
			string html = await client.GetStringAsync ("/Products/List.aspx");

			Assert.Contains ("<th scope=\"col\">Name</th>", html);
			Assert.Contains ("<th scope=\"col\">Price</th>", html);
			Assert.DoesNotContain ("<th scope=\"col\">Id</th>", html);
			Assert.DoesNotContain ("<th scope=\"col\">Summary</th>", html);

			// And the other half of the rule, which is what makes it a convention rather than a bug:
			// Category.Id is [DatabaseGenerated(None)], so it IS scaffolded.
			string categories = await client.GetStringAsync ("/Categories/List.aspx");
			Assert.Contains ("<th scope=\"col\">Id</th>", categories);
		}

		[Fact]
		public async Task A_scaffolded_grid_round_trips_its_view_state ()
		{
			// The regression guard for StateManagedCollection. Its saved state is a Triplet of List<T>,
			// which the port's formatter cannot encode - and the columns here are built at Page_Init, so
			// they are dirty and the List is actually written. Posting the page back proves the state was
			// both written and read: a formatter that silently dropped it would fail on the way back.
			using HttpClient client = fixture.CreateClient ();
			string html = await client.GetStringAsync ("/Products/List.aspx");

			Assert.Contains ("__VIEWSTATE", html);

			HttpResponseMessage postback = await client.PostAsync (
				"/Products/List.aspx", Postback.For (html, target: ""));
			string returned = await postback.Content.ReadAsStringAsync ();

			Assert.Equal (HttpStatusCode.OK, postback.StatusCode);
			Assert.Contains ("Anvil", returned);
			Assert.Contains ("<th scope=\"col\">Price</th>", returned);
		}

		[Fact]
		public async Task A_details_page_finds_its_row_through_the_model_primary_key ()
		{
			// The key column is never rendered on the list page, so this asserts the other half of the
			// scaffolding rule: PrimaryKeyColumns does not consult Scaffold, and the row is still
			// addressable.
			using HttpClient client = fixture.CreateClient ();
			string html = await client.GetStringAsync ("/Products/Details.aspx?Id=2");

			Assert.Contains ("Birdseed", html);
			Assert.DoesNotContain ("No such row.", html);
		}

		[Fact]
		public async Task A_details_page_for_a_missing_row_says_so_rather_than_throwing ()
		{
			using HttpClient client = fixture.CreateClient ();
			HttpResponseMessage response = await client.GetAsync ("/Products/Details.aspx?Id=9999");
			string html = await response.Content.ReadAsStringAsync ();

			Assert.Equal (HttpStatusCode.OK, response.StatusCode);
			Assert.Contains ("No such row.", html);
		}

		[Fact]
		public async Task A_key_that_is_not_a_number_is_refused_rather_than_throwing ()
		{
			// Convert.ChangeType on route input is an unhandled FormatException waiting to happen, and
			// the route pattern cannot constrain it - {table}/{action}.aspx says nothing about the key.
			using HttpClient client = fixture.CreateClient ();
			HttpResponseMessage response = await client.GetAsync ("/Products/Details.aspx?Id=not-a-number");
			string html = await response.Content.ReadAsStringAsync ();

			Assert.Equal (HttpStatusCode.OK, response.StatusCode);
			Assert.Contains ("No such row.", html);
		}

		[Fact]
		public async Task An_action_outside_the_route_constraint_is_not_served ()
		{
			// The route constrains action to List|Details|Edit|Insert. Without the constraint every
			// two-segment URL in the application would resolve to a page template.
			using HttpClient client = fixture.CreateClient ();
			HttpResponseMessage response = await client.GetAsync ("/Products/Delete.aspx");

			Assert.NotEqual (HttpStatusCode.OK, response.StatusCode);
		}

		[Fact]
		public async Task An_unknown_table_is_not_served ()
		{
			using HttpClient client = fixture.CreateClient ();
			HttpResponseMessage response = await client.GetAsync ("/Nonexistent/List.aspx");

			Assert.NotEqual (HttpStatusCode.OK, response.StatusCode);
		}
	}
}
