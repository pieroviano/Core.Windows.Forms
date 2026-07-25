# AspNetCore.Web.WebPages.Razor

`System.Web.WebPages.Razor` — the glue between `BuildManager` and Razor: the build provider that
compiles a `.cshtml` at request time, the Razor host configuration, and the
`<system.web.webPages.razor>` configuration section that carries `@using` imports and the page base
type.

```xml
<PackageReference Include="AspNetCore.Web.WebPages.Razor" Version="1.0.0" />
```

> **Assembly vs package name.** The package is `AspNetCore.Web.WebPages.Razor`; the assembly inside it is `Core.Web.WebPages.Razor`.
> .NET ships an empty `System.Web.dll` facade in `Microsoft.NETCore.App` and the host gives the shared
> framework precedence, so an app-local `System.Web.dll` is never loaded — the port therefore cannot
> use the original assembly names. **Namespaces are unchanged**, so your code and `Inherits=`
> attributes are unaffected; only `web.config` entries that name an *assembly* need updating.

## What is inside

* `RazorBuildProvider` — compiles `.cshtml` on first request, the way `.aspx` is compiled
* `WebPageRazorHost` / `WebRazorHostFactory` — how a view becomes a class
* `System.Web.WebPages.Razor.Configuration` — the `<host>`, `<pages>` and `<namespaces>` elements a
  `Views/web.config` uses

## Async views

This package supplies the second half of `@await` support. Razor generates
`public override void Execute ()`, and you cannot await inside a synchronous void method. Views that
contain an `await` are rewritten into an `async` method with a small synchronous `Execute ()` that
runs it; views without one are left exactly as Razor generated them.

The bridge **blocks** the request thread while the view's awaits complete. There is no deadlock risk —
ASP.NET Core installs no `SynchronizationContext` — but a view awaiting slow I/O holds a thread-pool
thread. Prefer awaiting in the action and passing the result to the view. See LIMITATIONS.

## Documentation

`PORTING-GUIDE.md` (step-by-step migration from IIS) and `LIMITATIONS.md` (what differs from ASP.NET
on .NET Framework, and why) ship in the repository.

## Licence

**Apache-2.0 AND MIT.** Microsoft released the ASP.NET Web Stack - the sources this is built
from - under the Apache License 2.0. Mono's `AssemblyInfo.cs` and the code written for this port
are MIT. Both are permissive and compatible; the compound expression means the assembly contains
code under both, not that you may pick one. `THIRD-PARTY-NOTICES.md` ships in the package and
says which part is which.
