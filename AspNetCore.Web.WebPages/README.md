# AspNetCore.Web.WebPages

`System.Web.WebPages` — the Web Pages 2 runtime: the `WebPage` base class, layouts and sections,
`@Html` helpers, validation, and standalone `.cshtml` routing.

```xml
<PackageReference Include="AspNetCore.Web.WebPages.Base" Version="1.0.0" />
```

Add `AspNetCore.Web.WebPages.Razor` and `AspNetCore.Web.WebPages.Deployment` alongside it for
runtime-compiled `.cshtml`. `AspNetCore.Web.Mvc` brings all of them.

> **Assembly vs package name.** The package is `AspNetCore.Web.WebPages.Base`; the assembly inside it is `Core.Web.WebPages`.
> .NET ships an empty `System.Web.dll` facade in `Microsoft.NETCore.App` and the host gives the shared
> framework precedence, so an app-local `System.Web.dll` is never loaded — the port therefore cannot
> use the original assembly names. **Namespaces are unchanged**, so your code and `Inherits=`
> attributes are unaffected; only `web.config` entries that name an *assembly* need updating.

## What works

* Standalone `.cshtml` pages, extensionless URLs and the default document
* Layouts, `@RenderBody`, `@RenderSection`, `@RenderPage`, the `Page` dictionary
* `@Html.*` helpers, automatic HTML encoding, `IHtmlString`
* Underscore-prefixed files refused, as the framework always did
* Self-posting forms, `IsPost`, `Request.Unvalidated`
* Display modes (`DisplayModeProvider`) for mobile and device-specific views

## Documentation

`PORTING-GUIDE.md` (step-by-step migration from IIS) and `LIMITATIONS.md` (what differs from ASP.NET
on .NET Framework, and why) ship in the repository.

## Licence

**Apache-2.0 AND MIT.** Microsoft released the ASP.NET Web Stack - the sources this is built
from - under the Apache License 2.0. Mono's `AssemblyInfo.cs` and the code written for this port
are MIT. Both are permissive and compatible; the compound expression means the assembly contains
code under both, not that you may pick one. `THIRD-PARTY-NOTICES.md` ships in the package and
says which part is which.
