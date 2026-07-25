# AspNetCore.Web.ConfigBridge

Lets third-party libraries that read `System.Configuration.ConfigurationManager` see your
`web.config`.

```xml
<PackageReference Include="AspNetCore.Web.ConfigBridge" Version="1.0.0" />
```

It arrives automatically with `AspNetCore.Web.Hosting.Kestrel`.

> **Assembly vs package name.** The package is `AspNetCore.Web.ConfigBridge`; the assembly inside it is `Core.Web.ConfigBridge`.
> .NET ships an empty `System.Web.dll` facade in `Microsoft.NETCore.App` and the host gives the shared
> framework precedence, so an app-local `System.Web.dll` is never loaded — the port therefore cannot
> use the original assembly names. **Namespaces are unchanged**, so your code and `Inherits=`
> attributes are unaffected; only `web.config` entries that name an *assembly* need updating.

## The problem it solves

The port reads configuration through `AspNetCore.Configuration` (Mono's implementation). A NuGet
library you did not write reads it through the shipping `System.Configuration.ConfigurationManager`
package. Those are two different type sets, and without a bridge the library sees an empty
configuration and silently falls back to its defaults — a connection string that is suddenly `null`,
an appSetting that is suddenly missing.

This package installs an `IInternalConfigSystem` into the shipping package at startup, pointing its
`GetSection` at the ported configuration system. `ConfigurationManager.AppSettings` and
`ConnectionStrings` then return what `web.config` says.

## How it stays out of trouble

It is the **only** project compiled against the shipping package's configuration types, and everything
crossing its boundary is a primitive or a shared-framework type — so the two `System.Configuration.*`
type sets never meet in one signature.

`SetConfigurationSystem` is one-shot per process, so the bridge installs once at startup and cannot be
reconfigured afterwards.

## Documentation

`PORTING-GUIDE.md` (step-by-step migration from IIS) and `LIMITATIONS.md` (what differs from ASP.NET
on .NET Framework, and why) ship in the repository.

## Licence

MIT, as the upstream Mono sources this is built from and the code written for this port.
`THIRD-PARTY-NOTICES.md` ships in the package and says which part is which.
