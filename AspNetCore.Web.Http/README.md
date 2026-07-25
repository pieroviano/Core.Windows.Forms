# AspNetCore.Web.Http

**ASP.NET Web API** on .NET 10 — `ApiController`, routing, content negotiation, model binding,
filters and `HttpResponseException`.

```xml
<PackageReference Include="AspNetCore.Web.Http" Version="1.0.0" />
<PackageReference Include="AspNetCore.Web.Http.WebHost" Version="1.0.0" />
```

Both are needed: this package is the framework, `.WebHost` is what plugs it into the `System.Web`
pipeline. `AspNetCore.Net.Http.Formatting` arrives with it.

> **Assembly vs package name.** The package is `AspNetCore.Web.Http`; the assembly inside it is `Core.Web.Http`.
> .NET ships an empty `System.Web.dll` facade in `Microsoft.NETCore.App` and the host gives the shared
> framework precedence, so an app-local `System.Web.dll` is never loaded — the port therefore cannot
> use the original assembly names. **Namespaces are unchanged**, so your code and `Inherits=`
> attributes are unaffected; only `web.config` entries that name an *assembly* need updating.

## Registration

Unchanged, in `Global.asax`:

```csharp
GlobalConfiguration.Configuration.Routes.MapHttpRoute (
    name: "DefaultApi",
    routeTemplate: "api/{controller}/{id}",
    defaults: new { id = System.Web.Http.RouteParameter.Optional });
```

`RouteParameter` is deliberately fully qualified: `Global.asax` imports
`System.Web.UI.WebControls` by default, which has a `RouteParameter` of its own, and the bare name is
ambiguous between the two.

## What works

* `ApiController`, verb and route-based action selection
* Content negotiation to JSON and XML, and request-body model binding from both
* `HttpResponseMessage`, `IHttpActionResult`, `HttpResponseException` mapped to its status
* Action filters, exception filters, `HttpConfiguration` and dependency resolution

## Version

Web API **1**, the generation matching MVC 4. **Attribute routing (`[Route]` on an `ApiController`)
is a Web API 2 feature and is not included** — the port supplies attribute routing for **MVC** only.
Use conventional `MapHttpRoute` templates here.

## Documentation

`PORTING-GUIDE.md` (step-by-step migration from IIS) and `LIMITATIONS.md` (what differs from ASP.NET
on .NET Framework, and why) ship in the repository.

## Licence

**Apache-2.0 AND MIT.** Microsoft released the ASP.NET Web Stack - the sources this is built
from - under the Apache License 2.0. Mono's `AssemblyInfo.cs` and the code written for this port
are MIT. Both are permissive and compatible; the compound expression means the assembly contains
code under both, not that you may pick one. `THIRD-PARTY-NOTICES.md` ships in the package and
says which part is which.
