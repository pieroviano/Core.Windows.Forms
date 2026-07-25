//
// The data model for the sample.
//
// Deliberately NOT an EF Core DbContext. Dynamic Data's QueryableDataModelProvider and LinqDataSource
// both find their shape by reflection - any public member returning IQueryable or IEnumerable is a
// table - so an in-memory context proves the substitution without a database, and proves that the
// port has not quietly made EF Core mandatory.
//
// Change tracking is by convention too: Add/Remove/Update on the collection or the context, then
// SaveChanges. That is EF Core's shape, a repository's, and most hand-rolled units of work. Here the
// collections are List<T>, which already has Add and Remove.
//

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;

namespace DynamicDataSample.Models
{
	[ScaffoldTable (true)]
	public class Product
	{
		[Key]
		public int Id { get; set; }

		[StringLength (60)]
		public string Name { get; set; }

		public decimal Price { get; set; }

		public bool Discontinued { get; set; }

		public DateTime Added { get; set; }

		public int CategoryId { get; set; }

		// No setter: scaffolded, but never editable. The read-only-column convention, visible.
		public string Summary {
			get { return Name + " (" + Price.ToString ("0.00") + ")"; }
		}
	}

	[ScaffoldTable (true)]
	public class Category
	{
		[Key]
		[DatabaseGenerated (DatabaseGeneratedOption.None)]
		public int Id { get; set; }

		[StringLength (40)]
		public string Name { get; set; }
	}

	public class ShopContext
	{
		// One instance per process. A real application would scope this per request; the sample keeps
		// it static so that an insert on one request is visible to the next, which is what makes the
		// LinqDataSource insert/update/delete path observable over HTTP at all.
		static readonly List<Product> products = new List<Product> {
			new Product { Id = 1, Name = "Anvil",    Price = 49.95m,  Discontinued = false, CategoryId = 1, Added = new DateTime (2020, 1, 15) },
			new Product { Id = 2, Name = "Birdseed", Price = 3.20m,   Discontinued = false, CategoryId = 2, Added = new DateTime (2021, 6, 1) },
			new Product { Id = 3, Name = "Rocket",   Price = 199.00m, Discontinued = true,  CategoryId = 1, Added = new DateTime (2019, 3, 9) },
			new Product { Id = 4, Name = "Dynamite", Price = 12.50m,  Discontinued = false, CategoryId = 1, Added = new DateTime (2022, 11, 30) },
		};

		static readonly List<Category> categories = new List<Category> {
			new Category { Id = 1, Name = "Hardware" },
			new Category { Id = 2, Name = "Consumables" },
		};

		// The two shapes a context can have, one each, so both are exercised by a real application
		// rather than only by a unit test.
		//
		// Products is the collection ITSELF, which is what a DbSet<T> is: enumerable, and owning
		// Add/Remove. LinqDataSource looks for change-tracking methods on this raw member first, so
		// insert and delete work with no extra code.
		public List<Product> Products {
			get { return products; }
		}

		// Categories is a QUERYABLE over a collection, which is what a repository usually exposes.
		// Readable and filterable, but AsQueryable returns an EnumerableQuery wrapper that has no Add -
		// so this table is read-only, and that is a property of the model, not a gap in the port.
		public IQueryable<Category> Categories {
			get { return categories.AsQueryable (); }
		}

		// Not a table: string is IEnumerable<char>, and the provider excludes it explicitly. Present so
		// that exclusion is exercised by a real model rather than only by a unit test.
		public string ConnectionString {
			get { return "in-memory"; }
		}

		// The change-tracking convention. LinqDataSource looks for Add/Remove/Update on the raw
		// collection first and then on the context; List<T> supplies Add and Remove, so only Update
		// and SaveChanges are needed here.
		public void Update (Product product)
		{
			Product existing = products.FirstOrDefault (p => p.Id == product.Id);
			if (existing == null)
				return;

			existing.Name = product.Name;
			existing.Price = product.Price;
			existing.Discontinued = product.Discontinued;
			existing.CategoryId = product.CategoryId;
		}

		public void SaveChanges ()
		{
			// Nothing to flush: the lists ARE the store. Present because the convention requires it,
			// and because its absence should be what fails, not its emptiness.
		}

		public static int NextProductId {
			get { return products.Count == 0 ? 1 : products.Max (p => p.Id) + 1; }
		}
	}
}
