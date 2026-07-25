# AspNetCore.Web.WebPages.Deployment

`System.Web.WebPages.Deployment` — version resolution and the `PreApplicationStartMethod` that starts
the Web Pages runtime.

```xml
<PackageReference Include="AspNetCore.Web.WebPages.Deployment" Version="1.0.0" />
```

You do not reference this directly. It arrives with `AspNetCore.Web.WebPages.Base`, which needs it to
bootstrap.

> **Assembly vs package name.** The package is `AspNetCore.Web.WebPages.Deployment`; the assembly inside it is `Core.Web.WebPages.Deployment`.
> .NET ships an empty `System.Web.dll` facade in `Microsoft.NETCore.App` and the host gives the shared
> framework precedence, so an app-local `System.Web.dll` is never loaded — the port therefore cannot
> use the original assembly names. **Namespaces are unchanged**, so your code and `Inherits=`
> attributes are unaffected; only `web.config` entries that name an *assembly* need updating.

## What it does

Decides which Web Pages version an application should load and starts it before the first request.
On .NET Framework this mattered because several versions could be installed side by side in the GAC;
here there is exactly one, so its job reduces to running the startup hook — but the Web Stack
assemblies reference it by name and will not load without it.

## Documentation

`PORTING-GUIDE.md` (step-by-step migration from IIS) and `LIMITATIONS.md` (what differs from ASP.NET
on .NET Framework, and why) ship in the repository.

## Licence

**Apache-2.0 AND MIT.** Microsoft released the ASP.NET Web Stack - the sources this is built
from - under the Apache License 2.0. Mono's `AssemblyInfo.cs` and the code written for this port
are MIT. Both are permissive and compatible; the compound expression means the assembly contains
code under both, not that you may pick one. `THIRD-PARTY-NOTICES.md` ships in the package and
says which part is which.
