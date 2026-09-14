# Core.AspNet.Web.Extensions

`System.Web.Extensions` — ASP.NET AJAX: `ScriptManager`, `UpdatePanel`, page methods, and the
client-script bridge to `[WebMethod]` endpoints.

```xml
<PackageReference Include="Core.AspNet.Web.Extensions" Version="1.0.0" />
```

It arrives automatically with `Core.AspNet.Web.Hosting.Kestrel`. **A missing reference is not a compile
error** — it shows up as every request returning 500 once a page uses a `ScriptManager`.

> **Assembly vs package name.** The package is `Core.AspNet.Web.Extensions`; the assembly inside it is `Core.Web.Extensions`.
> .NET ships an empty `System.Web.dll` facade in `Microsoft.NETCore.App` and the host gives the shared
> framework precedence, so an app-local `System.Web.dll` is never loaded — the port therefore cannot
> use the original assembly names. **Namespaces are unchanged**, so your code and `Inherits=`
> attributes are unaffected; only `web.config` entries that name an *assembly* need updating.

## What works

* `ScriptManager`, `ScriptManagerProxy`, `ScriptReference`
* `UpdatePanel`, `UpdateProgress`, `Timer` — including the length-prefixed partial-rendering delta
  protocol that async postbacks use
* Page methods and `[ScriptService]` `.asmx` services returning `{"d": ...}`
* `System.Web.Script.Serialization.JavaScriptSerializer`

The Microsoft AJAX client library is embedded under the exact names the upstream build gave it,
because `ScriptReferenceBase` cross-checks every `[assembly: WebResource]` declaration against a real
embedded resource and throws if one is missing.

This package needs `Core.AspNet.Web.Services`, because the client-script bridge is built on
`WebMethodAttribute` and `WebServiceHandlerFactory`.

## Documentation

`PORTING-GUIDE.md` (step-by-step migration from IIS) and `LIMITATIONS.md` (what differs from ASP.NET
on .NET Framework, and why) ship in the repository.

## Licence

MIT, as the upstream Mono sources this is built from and the code written for this port.
`THIRD-PARTY-NOTICES.md` ships in the package and says which part is which.
