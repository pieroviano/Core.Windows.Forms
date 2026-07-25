# AspNetCore.Web.DynamicData

**ASP.NET Dynamic Data** on .NET 10 — the scaffolding that builds list, detail, edit and insert pages
from a data model, with `[ScaffoldTable]`, `[ScaffoldColumn]`, field templates and metadata attributes
working as they did.

```xml
<PackageReference Include="AspNetCore.Web.DynamicData" Version="1.0.0" />
```

> **Assembly vs package name.** The package is `AspNetCore.Web.DynamicData`; the assembly inside it is
> `Core.Web.DynamicData`. .NET ships an empty `System.Web.dll` facade and the host gives the shared
> framework precedence, so the port cannot use the original assembly names. **Namespaces are
> unchanged** — `System.Web.DynamicData.MetaModel` is still `System.Web.DynamicData.MetaModel`.

## Registration

`Global.asax` is unchanged, except for what the context is:

```csharp
void Application_Start (object sender, EventArgs e)
{
    var model = new MetaModel ();
    model.RegisterContext (() => new ShopContext (), new ContextConfiguration { ScaffoldAllTables = true });

    RouteTable.Routes.Add (new DynamicDataRoute ("{table}/{action}.aspx") {
        Constraints = new RouteValueDictionary (new { action = "List|Details|Edit|Insert" }),
        Model = model,
    });
}
```

## The one substitution: the model provider

Dynamic Data reads its model through an abstract `DataModelProvider` / `TableProvider` /
`ColumnProvider` triple. Upstream ships exactly one implementation of it — over **LINQ to SQL**, which
does not exist on .NET and is not coming. That was the only thing stopping Dynamic Data being ported;
the scaffolding, routing, field templates and metadata are all data-layer agnostic.

So this package replaces the triple with **`QueryableDataModelProvider`**, which reads any context
exposing `IQueryable` or `IEnumerable` members:

```csharp
public class ShopContext : DbContext            // ...or a repository, or a class of List<T>
{
    public DbSet<Product> Products { get; set; }
    public DbSet<Category> Categories { get; set; }
}
```

It is the default, so `RegisterContext` needs no argument naming it. Nothing here references EF Core —
the port must not force a database stack on an application that only wants a `GridView` — so the shape
is found by reflection. That means EF Core, a hand-rolled unit of work, and an in-memory list of
objects all work, and the last of those makes the whole thing testable without a database.

### Conventions it applies

| | |
|---|---|
| **Tables** | every public `IQueryable`/`IEnumerable` member of the context. `string` is excluded explicitly — it is `IEnumerable<char>` |
| **Primary key** | `[Key]`, else a property named `Id`, else `<Type>Id`. No match leaves the table listable but not editable, rather than failing the model |
| **Store-generated key** | `[DatabaseGenerated]` if present; otherwise an `int`, `long` or `Guid` key is assumed generated — the EF Core convention |
| **Read-only columns** | a property with no setter is treated as generated |
| **Max length** | `[StringLength]` or `[MaxLength]` |

### What "generated" means for scaffolding

Both rows above end at `ColumnProvider.IsGenerated`, and `MetaColumn.Scaffold` turns that into
**hidden**, not "shown but read-only" — so an identity `Id` and a computed property appear on neither
the list page nor the edit page. That is upstream Dynamic Data's rule, not this port's; `MetaColumn`
reads `IsGenerated || IsCustomProperty` and returns false, exactly as it does on .NET Framework, where
a LINQ to SQL identity column behaves the same way.

If you want a computed column on the list page, give it `[UIHint]` — a UI hint wins over the generated
check — or add a private setter, which makes it an ordinary column.

The key is still reachable regardless of scaffolding: `MetaTable.PrimaryKeyColumns` and
`GetActionPath (action, row)` do not consult `Scaffold`, which is how `Samples/DynamicDataSample`
builds `DataKeyNames` and its Details links for a table whose key is never rendered.

## `ScaffoldTableAttribute`

.NET Core's `System.ComponentModel.Annotations` kept `ScaffoldColumnAttribute` and dropped
`ScaffoldTableAttribute` — scaffolding a whole table was a Dynamic Data concept, and Dynamic Data was
not ported. This package declares it again, in its original `System.ComponentModel.DataAnnotations`
namespace, so `[ScaffoldTable(true)]` on an entity keeps compiling and keeps meaning what it meant.

## What is not here

* **LINQ to SQL support.** `DLinqDataModelProvider` and its table/column/association providers are
  excluded. A `DataContext` cannot be a Dynamic Data context here; use EF Core or any queryable.
* **The Visual Studio project templates.** The scaffolding pages themselves (`List.aspx`,
  `Details.aspx`, the `FieldTemplates` folder) are content in your application, not in this package —
  copy them from an existing Dynamic Data project, or from `Samples/DynamicDataSample`. Without a
  `DynamicData/FieldTemplates/` folder every scaffolded cell renders empty, and nothing says why.
* **Foreign-key and children templates.** `QueryableDataModelProvider` reads `IQueryable` members, not
  a relational schema, so it produces no association metadata. `ForeignKey.ascx` and `Children.ascx`
  have nothing to bind to, and `PopulateListControl`, `ExtractForeignKey` and `LoadWithForeignKeys`
  throw rather than invent a relationship the model does not describe.

## Documentation

`PORTING-GUIDE.md` and `LIMITATIONS.md` ship in the repository.

## Licence

MIT, as the upstream Mono sources this is built from and the code written for this port.
`THIRD-PARTY-NOTICES.md` ships in the package and says which part is which.
