# AspNetCore.Web.ServiceModel

Hosts **`.svc` (WCF) endpoints** on .NET 10, keeping the URLs and contracts an existing application
already has.

```xml
<PackageReference Include="AspNetCore.Web.ServiceModel" Version="1.0.0" />
```

> **Assembly vs package name.** The package is `AspNetCore.Web.ServiceModel`; the assembly inside it is `Core.Web.ServiceModel`.
> .NET ships an empty `System.Web.dll` facade in `Microsoft.NETCore.App` and the host gives the shared
> framework precedence, so an app-local `System.Web.dll` is never loaded — the port therefore cannot
> use the original assembly names. **Namespaces are unchanged**, so your code and `Inherits=`
> attributes are unaffected; only `web.config` entries that name an *assembly* need updating.

## Usage

```csharp
var builder = WebApplication.CreateBuilder (args);

// Before Build (): CoreWCF is configured through DI.
builder.Services.AddSvcEndpoints (builder.Environment.ContentRootPath, options => {
    options.ServiceAssemblies = new [] { typeof (MyApp.Services.EchoService).Assembly };
});

var app = builder.Build ();

app.UseSvcEndpoints ();     // BEFORE UseWebForms

app.UseWebForms (options => { /* ... */ });
```

Your `.svc` files are unchanged and stay content, not compiled:

```aspx
<%@ ServiceHost Language="C#" Debug="true" Service="MyApp.Services.EchoService" %>
```

Each one is registered at the address its own path implies, so existing endpoint URLs keep working
with no route configured anywhere. `?wsdl` and `?singleWsdl` serve, and services coexist with `.aspx`
pages in one application on one port.

## This is not a port of System.ServiceModel

Mono's is 912 source files and a whole transport and channel stack, and WCF's server side does not
exist on .NET at all. **CoreWCF** is the supported server-side WCF and is what actually serves SOAP
here; this package adds the piece CoreWCF deliberately does not have — the ASP.NET `.svc` convention.

Consequences worth knowing before you rely on it:

| | |
|---|---|
| **Attributes** | `[ServiceContract]`, `[OperationContract]`, `[DataContract]` come from `CoreWCF`, not `System.ServiceModel`. Your service code changes by a `using` line; the SOAP on the wire does not. |
| **`<system.serviceModel>` is not read** | Bindings, behaviours and quotas in `web.config` are ignored entirely — no error, they simply do nothing. Re-express them on `SvcEndpointOptions.Binding`. |
| **Custom `ServiceHostFactory`** | Not supported. A `Factory=` attribute is rejected at startup with a message naming the file. |
| **Client side** | Out of scope; this package only hosts. Use `System.ServiceModel.Primitives`. |
| **Discovery is once, at startup** | A `.svc` dropped in later is not picked up until the process restarts, unlike IIS activation. |

## Options

| Option | Purpose |
|---|---|
| `PhysicalPath` | Directory scanned for `.svc` files. `bin/` and `obj/` are skipped. |
| `ServiceAssemblies` | Assemblies searched for the types named by `Service=`. |
| `Binding` | Defaults to `BasicHttpBinding` — SOAP 1.1 over HTTP. |
| `EnableWsdl` | `?wsdl` and `?singleWsdl`. On by default. |
| `ThrowOnUnresolvedService` | A `.svc` naming an unresolvable type fails at **startup**. On by default. |

## Documentation

`PORTING-GUIDE.md` (step-by-step migration from IIS) and `LIMITATIONS.md` (what differs from ASP.NET
on .NET Framework, and why) ship in the repository.

## Licence

MIT, as the upstream Mono sources this is built from and the code written for this port.
`THIRD-PARTY-NOTICES.md` ships in the package and says which part is which.
