# AspNetCore.Configuration

Mono's `System.Configuration`, ported to .NET 10. This is what the ported `System.Web` is written
against, and it is not interchangeable with the `System.Configuration.ConfigurationManager` package.

```xml
<PackageReference Include="AspNetCore.Configuration" Version="1.0.0" />
```

It arrives automatically with `AspNetCore.Web.Base`; you rarely reference it directly.

> **Assembly vs package name.** The package is `AspNetCore.Configuration`; the assembly inside it is `Core.Configuration`.
> .NET ships an empty `System.Web.dll` facade in `Microsoft.NETCore.App` and the host gives the shared
> framework precedence, so an app-local `System.Web.dll` is never loaded — the port therefore cannot
> use the original assembly names. **Namespaces are unchanged**, so your code and `Inherits=`
> attributes are unaffected; only `web.config` entries that name an *assembly* need updating.

## Why a separate implementation exists

The ported `System.Web` depends on Mono's configuration *semantics*, not merely its API: the
config-path model, `SaveStart`/`SaveEnd`, `FindLocationConfiguration`, and friend access to
`protected internal` members. The shipping `ConfigurationManager` package rejects Mono's
`WebConfigurationHost` outright. Dropping this reference produces roughly 2000 `CS0246` errors.

## Living beside the real package

`System.Configuration.ConfigurationManager` still arrives transitively (through `System.Data.SqlClient`
and friends) and its types collide by name with these. `WebFormsPort.targets` removes that package's
**compile** assets while keeping it **deployed**, because third-party libraries were compiled against
its strong-named identity and nothing else can satisfy them.

If a third-party library reads `ConfigurationManager.AppSettings` and should see your `web.config`,
add **`AspNetCore.Web.ConfigBridge`**.

## Documentation

`PORTING-GUIDE.md` (step-by-step migration from IIS) and `LIMITATIONS.md` (what differs from ASP.NET
on .NET Framework, and why) ship in the repository.

## Licence

MIT, as the upstream Mono sources this is built from and the code written for this port.
`THIRD-PARTY-NOTICES.md` ships in the package and says which part is which.
