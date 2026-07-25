//
// LinqDataSource through a GridView, over HTTP - the only way it was ever actually used, and the one
// thing the unit suite could not exercise.
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
	public class LinqDataSourceTests
	{
		readonly DynamicDataFixture fixture;

		public LinqDataSourceTests (DynamicDataFixture fixture)
		{
			this.fixture = fixture;
		}

		[Fact]
		public async Task The_Where_expression_filters_and_OrderBy_sorts ()
		{
			using HttpClient client = fixture.CreateClient ();
			string html = await client.GetStringAsync ("/Products.aspx");

			// Where="Discontinued == false" - Rocket is discontinued.
			Assert.Contains ("Anvil", html);
			Assert.Contains ("Birdseed", html);
			Assert.Contains ("Dynamite", html);

			// OrderBy="Name". Asserted as a sequence rather than as three Contains, because three
			// Contains pass on any order at all.
			string [] cells = Postback.Cells (html);
			int anvil = Array.IndexOf (cells, "Anvil");
			int birdseed = Array.IndexOf (cells, "Birdseed");
			int dynamite = Array.IndexOf (cells, "Dynamite");

			Assert.True (anvil >= 0 && birdseed > anvil && dynamite > birdseed,
				     "expected Anvil, Birdseed, Dynamite in that order; got: " + String.Join (", ", cells));
		}

		[Fact]
		public async Task A_discontinued_row_is_excluded_by_the_filter ()
		{
			using HttpClient client = fixture.CreateClient ();
			string html = await client.GetStringAsync ("/Products.aspx");

			// Rocket IS on the page - the second grid has no Discontinued filter - so a bare
			// DoesNotContain would be wrong. Assert on the first grid's rows instead.
			string firstGrid = html.Substring (0, html.IndexOf ("Filtered by category", StringComparison.Ordinal));
			Assert.DoesNotContain ("Rocket", firstGrid);
		}

		[Fact]
		public async Task A_where_parameter_comes_from_the_query_string ()
		{
			using HttpClient client = fixture.CreateClient ();

			string category2 = await client.GetStringAsync ("/Products.aspx?cat=2");
			string second = category2.Substring (category2.IndexOf ("Filtered by category", StringComparison.Ordinal));

			Assert.Contains ("Birdseed", second);
			Assert.DoesNotContain ("Anvil", second);
		}

		[Fact]
		public async Task The_parameter_default_applies_when_the_query_string_is_absent ()
		{
			using HttpClient client = fixture.CreateClient ();
			string html = await client.GetStringAsync ("/Products.aspx");
			string second = html.Substring (html.IndexOf ("Filtered by category", StringComparison.Ordinal));

			// DefaultValue="1" - the hardware category.
			Assert.Contains ("Anvil", second);
			Assert.DoesNotContain ("Birdseed", second);
		}

		[Fact]
		public async Task A_parameter_value_is_a_value_and_not_a_fragment_of_expression ()
		{
			// The security property the Where syntax exists to have. Parameter values are captured as
			// constants and never re-parsed, so this cannot widen the filter. If it were substituted into
			// the expression text, the second grid would show every product.
			using HttpClient client = fixture.CreateClient ();
			HttpResponseMessage response = await client.GetAsync ("/Products.aspx?cat=1%20%7C%7C%201%20%3D%3D%201");
			string html = await response.Content.ReadAsStringAsync ();

			// Either the value fails to convert to Int32 and the grid is empty, or it converts and
			// filters. What must NOT happen is every row appearing.
			if (response.StatusCode == HttpStatusCode.OK) {
				string second = html.Substring (html.IndexOf ("Filtered by category", StringComparison.Ordinal));
				Assert.DoesNotContain ("Birdseed", second);
			}
		}

		[Fact]
		public async Task The_grid_round_trips_its_view_state ()
		{
			using HttpClient client = fixture.CreateClient ();
			string html = await client.GetStringAsync ("/Products.aspx");

			HttpResponseMessage postback = await client.PostAsync (
				"/Products.aspx", Postback.For (html, target: ""));
			string returned = await postback.Content.ReadAsStringAsync ();

			Assert.Equal (HttpStatusCode.OK, postback.StatusCode);
			Assert.Contains ("Anvil", returned);
		}

		[Fact]
		public async Task An_edit_command_puts_the_row_into_edit_mode ()
		{
			// EnableUpdate="true" plus AutoGenerateEditButton renders an Edit link per row, which calls
			// __doPostBack ("ProductGrid", "Edit$0"). Getting into edit mode at all exercises the view's
			// CanUpdate, the data key lookup and the whole postback path.
			using HttpClient client = fixture.CreateClient ();
			string html = await client.GetStringAsync ("/Products.aspx");

			HttpResponseMessage response = await client.PostAsync (
				"/Products.aspx", Postback.For (html, target: "ProductGrid", argument: "Edit$0"));
			string edit = await response.Content.ReadAsStringAsync ();

			Assert.Equal (HttpStatusCode.OK, response.StatusCode);
			// An editable row renders text boxes and Update/Cancel in place of Edit.
			Assert.Contains ("type=\"text\"", edit);
			Assert.Contains ("Update", edit);
		}

		[Fact]
		public async Task Cancelling_an_edit_returns_the_grid_to_read_only ()
		{
			using HttpClient client = fixture.CreateClient ();
			string html = await client.GetStringAsync ("/Products.aspx");

			HttpResponseMessage editing = await client.PostAsync (
				"/Products.aspx", Postback.For (html, target: "ProductGrid", argument: "Edit$0"));
			string edit = await editing.Content.ReadAsStringAsync ();

			HttpResponseMessage cancelled = await client.PostAsync (
				"/Products.aspx", Postback.For (edit, target: "ProductGrid", argument: "Cancel$0"));
			string back = await cancelled.Content.ReadAsStringAsync ();

			Assert.Equal (HttpStatusCode.OK, cancelled.StatusCode);
			Assert.Contains ("Anvil", back);
			Assert.DoesNotContain ("type=\"text\"", back);
		}
	}
}
