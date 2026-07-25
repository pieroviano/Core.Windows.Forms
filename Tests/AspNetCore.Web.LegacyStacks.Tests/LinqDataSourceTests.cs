//
// LinqDataSource over IQueryable, and the dynamic-query syntax the markup is written in.
//
// The context here is a plain class holding List<T>, which is exactly the point: nothing references
// EF Core, so a context is anything with queryable members - and that is what makes this testable
// without a database.
//

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Web.UI;
using System.Web.UI.WebControls;
using Xunit;

namespace WebFormsPort.LegacyStacksTests
{
	public class LinqDataSourceTests
	{
		public class Product
		{
			public int Id { get; set; }
			public string Name { get; set; }
			public int CategoryId { get; set; }
			public decimal Price { get; set; }
		}

		/// <summary>The shape EF Core has, without EF Core: queryable members plus SaveChanges.</summary>
		public class ShopContext
		{
			public List<Product> Products { get; } = new List<Product> {
				new Product { Id = 1, Name = "Anvil",   CategoryId = 1, Price = 10m },
				new Product { Id = 2, Name = "Rope",    CategoryId = 1, Price = 5m },
				new Product { Id = 3, Name = "Rocket",  CategoryId = 2, Price = 99m },
				new Product { Id = 4, Name = "Birdseed",CategoryId = 2, Price = 2m },
			};

			public int Saved { get; private set; }

			public int SaveChanges ()
			{
				Saved++;
				return 1;
			}
		}

		/// <summary>The context every test shares, so mutations are observable across a call.</summary>
		static ShopContext shared;

		static LinqDataSource Source (Action<LinqDataSource> configure = null)
		{
			shared = new ShopContext ();

			var source = new LinqDataSource { ID = "Source", TableName = "Products" };
			source.ContextCreating += (s, e) => e.ObjectInstance = shared;

			configure?.Invoke (source);
			return source;
		}

		/// <summary>
		/// Goes through DataSourceView's PUBLIC Select rather than a test-only seam, so what is
		/// exercised is the same path a GridView takes.
		/// </summary>
		static IEnumerable SelectRaw (LinqDataSource source, DataSourceSelectArguments arguments = null)
		{
			DataSourceView view = ((IDataSource) source).GetView ("DefaultView");

			IEnumerable captured = null;
			view.Select (arguments ?? DataSourceSelectArguments.Empty, data => captured = data);

			return captured;
		}

		static List<Product> Select (LinqDataSource source, DataSourceSelectArguments arguments = null)
		{
			return SelectRaw (source, arguments).Cast<object> ().Cast<Product> ().ToList ();
		}

		static void Insert (LinqDataSource source, IDictionary values)
		{
			DataSourceView view = ((IDataSource) source).GetView ("DefaultView");
			Exception failure = null;

			view.Insert (values, (affected, e) => { failure = e; return true; });

			if (failure != null)
				throw failure;
		}

		static void Delete (LinqDataSource source, IDictionary keys)
		{
			DataSourceView view = ((IDataSource) source).GetView ("DefaultView");
			Exception failure = null;

			view.Delete (keys, null, (affected, e) => { failure = e; return true; });

			if (failure != null)
				throw failure;
		}

		static void Update (LinqDataSource source, IDictionary keys, IDictionary values)
		{
			DataSourceView view = ((IDataSource) source).GetView ("DefaultView");
			Exception failure = null;

			view.Update (keys, values, null, (affected, e) => { failure = e; return true; });

			if (failure != null)
				throw failure;
		}

		[Fact]
		public void It_selects_everything_by_default ()
		{
			Assert.Equal (4, Select (Source ()).Count);
		}

		[Fact]
		public void Where_filters_with_a_parameter ()
		{
			LinqDataSource source = Source (s => {
				s.Where = "CategoryId == @Category";
				s.WhereParameters.Add (new Parameter ("Category", TypeCode.Int32, "2"));
			});

			List<Product> results = Select (source);

			Assert.Equal (2, results.Count);
			Assert.All (results, p => Assert.Equal (2, p.CategoryId));
		}

		[Fact]
		public void A_parameter_value_is_data_and_never_syntax ()
		{
			// The reason parameter values are captured as constants rather than substituted into the
			// text: a value containing operators must be compared, not parsed. This is the same
			// property that makes a SQL parameter not string concatenation.
			LinqDataSource source = Source (s => {
				s.Where = "Name == @Name";
				s.WhereParameters.Add (new Parameter ("Name", TypeCode.String, "Rope\" || true || \""));
			});

			Assert.Empty (Select (source));
		}

		[Fact]
		public void Where_supports_the_comparison_and_logical_operators ()
		{
			LinqDataSource source = Source (s => s.Where = "Price > 4 && Price < 50");

			List<Product> results = Select (source);

			Assert.Equal (2, results.Count);
			Assert.Contains (results, p => p.Name == "Anvil");
			Assert.Contains (results, p => p.Name == "Rope");
		}

		[Fact]
		public void A_string_parameter_is_coerced_to_the_property_type ()
		{
			// A QueryStringParameter arrives as a string and the property is an int. That is the normal
			// case, not the exception.
			LinqDataSource source = Source (s => {
				s.Where = "Id == @Id";
				s.WhereParameters.Add (new Parameter ("Id", TypeCode.String, "3"));
			});

			Assert.Equal ("Rocket", Assert.Single (Select (source)).Name);
		}

		[Fact]
		public void OrderBy_sorts_ascending_and_descending ()
		{
			Assert.Equal ("Anvil", Select (Source (s => s.OrderBy = "Name")) [0].Name);      // A < B < R
			Assert.Equal ("Rope", Select (Source (s => s.OrderBy = "Name DESC")) [0].Name);
		}

		[Fact]
		public void OrderBy_supports_several_terms ()
		{
			List<Product> results = Select (Source (s => s.OrderBy = "CategoryId, Price DESC"));

			Assert.Equal ("Anvil", results [0].Name);     // category 1, 10
			Assert.Equal ("Rope", results [1].Name);      // category 1, 5
			Assert.Equal ("Rocket", results [2].Name);    // category 2, 99
		}

		[Fact]
		public void The_controls_sort_expression_beats_the_markups_OrderBy ()
		{
			// It is the column header the user just clicked; OrderBy is the default they clicked away
			// from.
			LinqDataSource source = Source (s => s.OrderBy = "Name");

			List<Product> results = Select (source, new DataSourceSelectArguments ("Price DESC"));

			Assert.Equal ("Rocket", results [0].Name);
		}

		[Fact]
		public void Paging_returns_the_requested_window_and_the_total ()
		{
			LinqDataSource source = Source (s => s.OrderBy = "Id");

			var arguments = new DataSourceSelectArguments (startRowIndex: 1, maximumRows: 2) {
				RetrieveTotalRowCount = true,
			};

			List<Product> results = Select (source, arguments);

			Assert.Equal (2, results.Count);
			Assert.Equal (2, results [0].Id);
			Assert.Equal (4, arguments.TotalRowCount);   // the count is BEFORE paging
		}

		[Fact]
		public void AutoGenerateWhereClause_compares_each_parameter_by_name ()
		{
			LinqDataSource source = Source (s => {
				s.AutoGenerateWhereClause = true;
				s.WhereParameters.Add (new Parameter ("CategoryId", TypeCode.Int32, "1"));
			});

			Assert.Equal (2, Select (source).Count);
		}

		[Fact]
		public void An_absent_auto_generated_parameter_is_skipped_rather_than_compared_to_null ()
		{
			// What makes an optional filter on a search page work without the markup saying so.
			LinqDataSource source = Source (s => {
				s.AutoGenerateWhereClause = true;
				s.WhereParameters.Add (new Parameter ("CategoryId", TypeCode.Object, null));
			});

			Assert.Equal (4, Select (source).Count);
		}

		[Fact]
		public void A_missing_TableName_says_which_members_were_available ()
		{
			LinqDataSource source = Source (s => s.TableName = "Widgets");

			var e = Assert.Throws<InvalidOperationException> (() => Select (source));

			Assert.Contains ("Widgets", e.Message);
			Assert.Contains ("Products", e.Message);       // the useful half
		}

		[Fact]
		public void Insert_adds_through_the_context_and_saves ()
		{
			LinqDataSource source = Source (s => s.EnableInsert = true);

			Insert (source, new Dictionary<string, object> {
				["Id"] = 5, ["Name"] = "Dynamite", ["CategoryId"] = 2, ["Price"] = 7m,
			});

			Assert.Contains (shared.Products, p => p.Name == "Dynamite");
			Assert.Equal (1, shared.Saved);
		}

		[Fact]
		public void Delete_removes_the_row_identified_by_its_key ()
		{
			LinqDataSource source = Source (s => s.EnableDelete = true);

			Delete (source, new Dictionary<string, object> { ["Id"] = 2 });

			Assert.DoesNotContain (shared.Products, p => p.Id == 2);
			Assert.Equal (1, shared.Saved);
		}

		[Fact]
		public void Update_applies_the_posted_values_to_the_existing_row ()
		{
			LinqDataSource source = Source (s => s.EnableUpdate = true);

			Update (source, new Dictionary<string, object> { ["Id"] = 1 },
				new Dictionary<string, object> { ["Name"] = "Big Anvil", ["Price"] = 20m });

			Product updated = shared.Products.Single (p => p.Id == 1);
			Assert.Equal ("Big Anvil", updated.Name);
			Assert.Equal (20m, updated.Price);
			Assert.Equal (1, shared.Saved);
		}

		[Fact]
		public void Insert_is_refused_when_it_was_not_enabled ()
		{
			LinqDataSource source = Source ();

			Assert.Throws<NotSupportedException> (
				() => Insert (source, new Dictionary<string, object> { ["Id"] = 9 }));
		}

		[Fact]
		public void Deleting_a_row_that_is_gone_says_so_rather_than_failing_obscurely ()
		{
			LinqDataSource source = Source (s => s.EnableDelete = true);

			var e = Assert.Throws<InvalidOperationException> (
				() => Delete (source, new Dictionary<string, object> { ["Id"] = 999 }));

			Assert.Contains ("no row matched", e.Message);
		}

		[Fact]
		public void The_Selected_event_carries_the_result_and_the_total ()
		{
			LinqDataSource source = Source ();
			LinqDataSourceStatusEventArgs seen = null;
			source.Selected += (s, e) => seen = e;

			Select (source, new DataSourceSelectArguments { RetrieveTotalRowCount = true });

			Assert.NotNull (seen);
			Assert.Null (seen.Exception);
			Assert.Equal (4, seen.TotalRowCount);
		}

		[Fact]
		public void A_failure_reaches_the_Selected_event_and_can_be_handled ()
		{
			LinqDataSource source = Source (s => s.Where = "NoSuchProperty == 1");
			source.Selected += (s, e) => e.ExceptionHandled = true;

			Assert.Empty (Select (source));
		}

		[Fact]
		public void A_projection_selects_the_named_members ()
		{
			LinqDataSource source = Source (s => s.Select = "new (Id, Name)");

			var rows = SelectRaw (source).Cast<Dictionary<string, object>> ().ToList ();

			Assert.Equal (4, rows.Count);
			Assert.Equal (new [] { "Id", "Name" }, rows [0].Keys);
		}
	}
}
