# AspNetCore.Web.Razor

`System.Web.Razor` — the Razor **parser and code generator**, version 2, from the ASP.NET Web Stack
sources.

```xml
<PackageReference Include="AspNetCore.Web.Razor" Version="1.0.0" />
```

This is the compiler front end only. To *render* `.cshtml` you also need `AspNetCore.Web.WebPages.Base`
and `AspNetCore.Web.WebPages.Razor`; for MVC views, `AspNetCore.Web.Mvc` brings all of them.

> **Assembly vs package name.** The package is `AspNetCore.Web.Razor`; the assembly inside it is `Core.Web.Razor`.
> .NET ships an empty `System.Web.dll` facade in `Microsoft.NETCore.App` and the host gives the shared
> framework precedence, so an app-local `System.Web.dll` is never loaded — the port therefore cannot
> use the original assembly names. **Namespaces are unchanged**, so your code and `Inherits=`
> attributes are unaffected; only `web.config` entries that name an *assembly* need updating.

## What is inside

* `RazorTemplateEngine`, `RazorEngineHost`, `GeneratedClassContext`
* The C# and VB parsers, tokenizers and the document tree
* The code generators that turn a parsed template into a `CodeCompileUnit`

## One addition beyond upstream

The C# parser accepts **`await` in an implicit expression**. Razor v2 stopped at the keyword, so
`@await Foo ()` parsed as the expression `await` followed by the literal text `" Foo ()"` — no error,
just wrong output on the page. Razor 3 fixed this upstream; the same fix is applied here.

Making the *generated method* able to hold an `await` is the other half, and lives in
`AspNetCore.Web.WebPages.Razor`. See LIMITATIONS for the cost.

## Documentation

`PORTING-GUIDE.md` (step-by-step migration from IIS) and `LIMITATIONS.md` (what differs from ASP.NET
on .NET Framework, and why) ship in the repository.

## Licence

**Apache-2.0 AND MIT.** Microsoft released the ASP.NET Web Stack - the sources this is built
from - under the Apache License 2.0. Mono's `AssemblyInfo.cs` and the code written for this port
are MIT. Both are permissive and compatible; the compound expression means the assembly contains
code under both, not that you may pick one. `THIRD-PARTY-NOTICES.md` ships in the package and
says which part is which.
