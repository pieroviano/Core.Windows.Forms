# AspNetCore.Web

`System.Web` itself, ported to .NET 10 from the Mono sources: pages and controls, the HTTP pipeline,
handlers and modules, session, caching, membership, and the WebForms page framework.

```xml
<PackageReference Include="AspNetCore.Web.Base" Version="1.0.0" />
```

Most applications should reference **`AspNetCore.Web.Hosting.Kestrel`** instead, which brings this in
along with the host and the build assets that make it work.

> **Assembly vs package name.** The package is `AspNetCore.Web.Base`; the assembly inside it is `Core.Web`.
> .NET ships an empty `System.Web.dll` facade in `Microsoft.NETCore.App` and the host gives the shared
> framework precedence, so an app-local `System.Web.dll` is never loaded — the port therefore cannot
> use the original assembly names. **Namespaces are unchanged**, so your code and `Inherits=`
> attributes are unaffected; only `web.config` entries that name an *assembly* need updating.

## What is inside

* `System.Web.UI` — `Page`, `Control`, `UserControl`, `MasterPage`, view state, postback
* `System.Web.UI.WebControls` / `.HtmlControls` — the server-control library
* `System.Web` — `HttpContext`, `HttpRequest`/`HttpResponse`, `HttpApplication`, modules, handlers
* `System.Web.Caching`, `System.Web.SessionState` (in-process), `System.Web.Security`
* `System.Web.Routing` — `Route`, `RouteTable`, `UrlRoutingModule`
* `System.Web.Compilation` — `BuildManager`, the build providers, `App_Code`

## Notable replacements

* **Compilation.** `CodeDomProvider`'s *compile* half throws `PlatformNotSupportedException` on .NET
  Core, so a Roslyn-based compiler supplies it. The codegen half is untouched. Both C# and VB pages
  compile.
* **`BinaryFormatter`** is gone on .NET 8+, so view state and session objects with no native encoding
  are **refused with a diagnostic naming the type** rather than silently re-encoded. Callers opt in
  through `WebFormsOptions.StateSerializer`.
* **`machine.config` and the root `web.config`** are embedded as resources and extracted at runtime.
  The root configuration is what maps `*.aspx` to `PageHandlerFactory`; without it nothing is served.

This assembly requires `AspNetCore.Configuration` — the shipping
`System.Configuration.ConfigurationManager` package cannot substitute for it.

## Documentation

`PORTING-GUIDE.md` (step-by-step migration from IIS) and `LIMITATIONS.md` (what differs from ASP.NET
on .NET Framework, and why) ship in the repository.

## Licence

MIT, as the upstream Mono sources this is built from and the code written for this port.
`THIRD-PARTY-NOTICES.md` ships in the package and says which part is which.
