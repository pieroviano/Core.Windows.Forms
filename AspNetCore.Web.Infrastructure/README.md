# AspNetCore.Web.Infrastructure

`Microsoft.Web.Infrastructure` — a small support assembly the ASP.NET Web Stack depends on for
dynamic module registration.

```xml
<PackageReference Include="AspNetCore.Web.Infrastructure" Version="1.0.0" />
```

You almost certainly do not reference this directly; it arrives with `AspNetCore.Web.WebPages.Base` and
`AspNetCore.Web.Mvc`, which need it.

> **Assembly vs package name.** The package is `AspNetCore.Web.Infrastructure`; the assembly inside it is `Core.Web.Infrastructure`.
> .NET ships an empty `System.Web.dll` facade in `Microsoft.NETCore.App` and the host gives the shared
> framework precedence, so an app-local `System.Web.dll` is never loaded — the port therefore cannot
> use the original assembly names. **Namespaces are unchanged**, so your code and `Inherits=`
> attributes are unaffected; only `web.config` entries that name an *assembly* need updating.

## What is inside

`Microsoft.Web.Infrastructure.DynamicModuleHelper.DynamicModuleUtility` — the helper Web Pages and MVC
use to register an `IHttpModule` from a `PreApplicationStartMethod` rather than from `web.config`.

It is tiny, and exists as its own package only because that is how upstream factored it and because
the Web Stack assemblies reference it by name.

## Documentation

`PORTING-GUIDE.md` (step-by-step migration from IIS) and `LIMITATIONS.md` (what differs from ASP.NET
on .NET Framework, and why) ship in the repository.

## Licence

MIT, as the upstream Mono sources this is built from and the code written for this port.
`THIRD-PARTY-NOTICES.md` ships in the package and says which part is which.
