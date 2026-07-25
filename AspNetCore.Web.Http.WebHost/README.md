# AspNetCore.Web.Http.WebHost

`System.Web.Http.WebHost` — hosts ASP.NET Web API inside the `System.Web` pipeline.

```xml
<PackageReference Include="AspNetCore.Web.Http.WebHost" Version="1.0.0" />
```

Pairs with `AspNetCore.Web.Http`, which it brings with it. Without this package a Web API application
compiles and starts, and then every `api/...` URL 404s because nothing routes it.

> **Assembly vs package name.** The package is `AspNetCore.Web.Http.WebHost`; the assembly inside it is `Core.Web.Http.WebHost`.
> .NET ships an empty `System.Web.dll` facade in `Microsoft.NETCore.App` and the host gives the shared
> framework precedence, so an app-local `System.Web.dll` is never loaded — the port therefore cannot
> use the original assembly names. **Namespaces are unchanged**, so your code and `Inherits=`
> attributes are unaffected; only `web.config` entries that name an *assembly* need updating.

## What is inside

* `HttpControllerHandler` — the `IHttpAsyncHandler` that runs a Web API request
* `HttpControllerRouteHandler` — the `IRouteHandler` `MapHttpRoute` installs
* `GlobalConfiguration` — the `HttpConfiguration` a web-hosted application configures
* The bridge between `HttpContext` and `HttpRequestMessage`

`UrlRoutingModule` must be registered, which the port's generated root configuration already does.

## Documentation

`PORTING-GUIDE.md` (step-by-step migration from IIS) and `LIMITATIONS.md` (what differs from ASP.NET
on .NET Framework, and why) ship in the repository.

## Licence

**Apache-2.0 AND MIT.** Microsoft released the ASP.NET Web Stack - the sources this is built
from - under the Apache License 2.0. Mono's `AssemblyInfo.cs` and the code written for this port
are MIT. Both are permissive and compatible; the compound expression means the assembly contains
code under both, not that you may pick one. `THIRD-PARTY-NOTICES.md` ships in the package and
says which part is which.
