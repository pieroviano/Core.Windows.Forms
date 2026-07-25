# AspNetCore.Web.Mvc

**ASP.NET MVC 4** on .NET 10 — controllers, Razor views, model binding, filters, `TempData`, async
actions — plus attribute routing supplied by this port.

```xml
<PackageReference Include="AspNetCore.Web.Mvc" Version="1.0.0" />
```

Brings `AspNetCore.Web.Razor`, `.WebPages`, `.WebPages.Razor` and `.Infrastructure` with it. You still
need `AspNetCore.Web.Hosting.Kestrel` to host the application.

> **Assembly vs package name.** The package is `AspNetCore.Web.Mvc`; the assembly inside it is `Core.Web.Mvc`.
> .NET ships an empty `System.Web.dll` facade in `Microsoft.NETCore.App` and the host gives the shared
> framework precedence, so an app-local `System.Web.dll` is never loaded — the port therefore cannot
> use the original assembly names. **Namespaces are unchanged**, so your code and `Inherits=`
> attributes are unaffected; only `web.config` entries that name an *assembly* need updating.

## Registration

Unchanged from what you already have, in `Global.asax`:

```csharp
void Application_Start (object sender, EventArgs e)
{
    RouteTable.Routes.MapMvcAttributeRoutes ();     // BEFORE the conventional routes

    RouteTable.Routes.MapRoute ("Default", "{controller}/{action}/{id}",
                                new { controller = "Home", action = "Index", id = UrlParameter.Optional });
}
```

Razor views additionally need a `Views/web.config` naming the host factory — copy it from the MVC
sample in the repository.

## Attribute routing

`[Route]` and `[RoutePrefix]` are an MVC 5 feature and are written by this port rather than ported:

```csharp
[RoutePrefix ("catalog")]
public class CatalogController : Controller
{
    [Route ("")]                              // /catalog
    public ActionResult Index () { ... }

    [Route ("{id:int}")]                      // /catalog/42
    public ActionResult Details (int id) { ... }

    [HttpPost, Route ("{id:int}")]            // same URL, POST only
    public ActionResult Update (int id) { ... }

    [Route ("~/legacy")]                      // "~/" escapes the prefix
    public ActionResult Legacy () { ... }
}
```

Inline constraints: `int`, `long`, `bool`, `guid`, `decimal`, `double`, `float`, `datetime`, `alpha`,
`required`, `length`, `minlength`, `maxlength`, `min`, `max`, `range`, `regex`. Several may apply to
one parameter (`{s:alpha:minlength(3)}`), and you can add your own to
`InlineRouteConstraintResolver.Constraints`.

Ordering within attribute routes is by specificity — literal beats constrained parameter beats plain
parameter beats catch-all — so `/catalog/new` reaches `New ()` wherever it sits in the file.

**`MapMvcAttributeRoutes ()` must be called before your conventional routes.** Routes match in order,
and `{controller}/{action}/{id}` matches nearly everything.

As in MVC 5, once a controller has attribute routes its actions are **no longer reachable through
conventional routes** — otherwise the constraints you declared could be bypassed.

## Version

MVC **4** with Razor **v2**. No view components, no tag helpers, no MVC 5 filter overrides. For
bundling, add **`AspNetCore.Web.Optimization`**.

## Documentation

`PORTING-GUIDE.md` (step-by-step migration from IIS) and `LIMITATIONS.md` (what differs from ASP.NET
on .NET Framework, and why) ship in the repository.

## Licence

**Apache-2.0 AND MIT.** Microsoft released the ASP.NET Web Stack - the sources this is built
from - under the Apache License 2.0. Mono's `AssemblyInfo.cs` and the code written for this port
are MIT. Both are permissive and compatible; the compound expression means the assembly contains
code under both, not that you may pick one. `THIRD-PARTY-NOTICES.md` ships in the package and
says which part is which.
