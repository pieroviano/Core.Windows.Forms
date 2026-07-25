//
// Dynamic Data's model, read off an IQueryable context instead of a LINQ to SQL DataContext.
//
// The model provider is the whole of what had to be replaced, so it is what is tested: the tables it
// discovers, the columns, the key convention, and the fact that a plain class of List<T> is a valid
// context - which is what makes Dynamic Data usable at all without LINQ to SQL.
//

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Web.DynamicData.ModelProviders;
using Xunit;

namespace WebFormsPort.LegacyStacksTests
{
	public class DynamicDataTests
	{
		public class Product
		{
			public int Id { get; set; }
			public string Name { get; set; }
			public decimal Price { get; set; }
			public string Computed { get { return Name + " (" + Price + ")"; } }
		}

		public class Category
		{
			[Key]
			public Guid CategoryKey { get; set; }
			public string Title { get; set; }
		}

		public class Note
		{
			// No [Key], no Id, no NoteId: a table with no discoverable key.
			public string Text { get; set; }
		}

		public class Assigned
		{
			[DatabaseGenerated (DatabaseGeneratedOption.None)]
			public int Id { get; set; }
		}

		public class ShopContext
		{
			public List<Product> Products { get; } = new List<Product> {
				new Product { Id = 1, Name = "Anvil", Price = 10m },
				new Product { Id = 2, Name = "Rope", Price = 5m },
			};

			public List<Category> Categories { get; } = new List<Category> ();
			public List<Note> Notes { get; } = new List<Note> ();
			public List<Assigned> Assigned { get; } = new List<Assigned> ();

			/// <summary>Not a table: string is IEnumerable&lt;char&gt;, which must not be scaffolded.</summary>
			public string ConnectionName { get; set; } = "shop";

			public int SaveChanges () => 0;
		}

		static QueryableDataModelProvider Model ()
		{
			return new QueryableDataModelProvider (() => new ShopContext ());
		}

		static TableProvider Table (string name)
		{
			return Model ().Tables.Single (t => t.Name == name);
		}

		[Fact]
		public void Every_queryable_member_becomes_a_table ()
		{
			string [] tables = Model ().Tables.Select (t => t.Name).OrderBy (n => n).ToArray ();

			Assert.Equal (new [] { "Assigned", "Categories", "Notes", "Products" }, tables);
		}

		[Fact]
		public void A_string_property_is_not_a_table ()
		{
			// string is IEnumerable<char>. A context with a ConnectionName must not scaffold a table of
			// characters - which is what a naive "is it enumerable" test would do.
			Assert.DoesNotContain (Model ().Tables, t => t.Name == "ConnectionName");
		}

		[Fact]
		public void A_table_knows_its_entity_type_and_its_columns ()
		{
			TableProvider products = Table ("Products");

			Assert.Equal (typeof (Product), products.EntityType);
			Assert.Equal (new [] { "Computed", "Id", "Name", "Price" },
				      products.Columns.Select (c => c.Name).OrderBy (n => n).ToArray ());
		}

		[Fact]
		public void GetQuery_returns_the_rows ()
		{
			var context = new ShopContext ();
			TableProvider products = Model ().Tables.Single (t => t.Name == "Products");

			var rows = products.GetQuery (context).Cast<Product> ().ToList ();

			Assert.Equal (2, rows.Count);
			Assert.Contains (rows, p => p.Name == "Anvil");
		}

		[Fact]
		public void A_property_named_Id_is_the_key ()
		{
			ColumnProvider key = Table ("Products").Columns.Single (c => c.IsPrimaryKey);

			Assert.Equal ("Id", key.Name);
		}

		[Fact]
		public void A_Key_attribute_wins_over_the_naming_convention ()
		{
			ColumnProvider key = Table ("Categories").Columns.Single (c => c.IsPrimaryKey);

			Assert.Equal ("CategoryKey", key.Name);
		}

		[Fact]
		public void A_table_with_no_discoverable_key_is_still_listed ()
		{
			// Listable but not editable beats failing the whole model - one un-keyed table must not stop
			// the other twenty being scaffolded.
			TableProvider notes = Table ("Notes");

			Assert.NotEmpty (notes.Columns);
			Assert.DoesNotContain (notes.Columns, c => c.IsPrimaryKey);
		}

		[Fact]
		public void An_integral_key_is_assumed_store_generated ()
		{
			// The EF Core convention, and true of essentially every table with an int Id. A generated key
			// must not be offered on the insert page, or the database rejects the row.
			ColumnProvider key = Table ("Products").Columns.Single (c => c.IsPrimaryKey);

			Assert.True (key.IsGenerated);
		}

		[Fact]
		public void DatabaseGenerated_None_overrides_that_assumption ()
		{
			// An assigned key - a natural key, or one the application allocates - IS offered for edit.
			ColumnProvider key = Table ("Assigned").Columns.Single (c => c.IsPrimaryKey);

			Assert.False (key.IsGenerated);
		}

		[Fact]
		public void A_read_only_property_is_not_offered_for_edit ()
		{
			ColumnProvider computed = Table ("Products").Columns.Single (c => c.Name == "Computed");

			Assert.True (computed.IsGenerated);
		}

		[Fact]
		public void Nullability_follows_the_clr_type ()
		{
			var columns = Table ("Products").Columns.ToDictionary (c => c.Name);

			Assert.False (columns ["Id"].Nullable);        // int
			Assert.True (columns ["Name"].Nullable);       // string
		}

		[Fact]
		public void The_column_type_is_the_property_type ()
		{
			var columns = Table ("Products").Columns.ToDictionary (c => c.Name);

			Assert.Equal (typeof (decimal), columns ["Price"].ColumnType);
			Assert.Equal (typeof (string), columns ["Name"].ColumnType);
		}

		[Fact]
		public void The_provider_creates_a_fresh_context_each_time ()
		{
			// Dynamic Data opens a context per request; handing back the same instance would leak state
			// between users and, with EF Core, across threads.
			QueryableDataModelProvider model = Model ();

			Assert.NotSame (model.CreateContext (), model.CreateContext ());
		}

		[Fact]
		public void A_null_context_factory_result_is_reported_at_construction ()
		{
			var e = Assert.Throws<InvalidOperationException> (
				() => new QueryableDataModelProvider (() => null));

			Assert.Contains ("null", e.Message);
		}

		[Fact]
		public void ScaffoldTableAttribute_exists_again_in_its_original_namespace ()
		{
			// .NET Core's System.ComponentModel.Annotations kept ScaffoldColumn and dropped ScaffoldTable.
			// A ported entity carrying [ScaffoldTable(true)] has to keep compiling.
			var attribute = new ScaffoldTableAttribute (true);

			Assert.True (attribute.Scaffold);
			Assert.Equal ("System.ComponentModel.DataAnnotations",
				      typeof (ScaffoldTableAttribute).Namespace);
		}
	}
}
