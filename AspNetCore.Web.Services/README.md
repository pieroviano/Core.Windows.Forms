# AspNetCore.Web.Services

`System.Web.Services` — serving `.asmx` SOAP and JSON web services.

```xml
<PackageReference Include="AspNetCore.Web.Services" Version="1.0.0" />
```

It arrives automatically with `AspNetCore.Web.Hosting.Kestrel`.

> **Assembly vs package name.** The package is `AspNetCore.Web.Services`; the assembly inside it is `Core.Web.Services`.
> .NET ships an empty `System.Web.dll` facade in `Microsoft.NETCore.App` and the host gives the shared
> framework precedence, so an app-local `System.Web.dll` is never loaded — the port therefore cannot
> use the original assembly names. **Namespaces are unchanged**, so your code and `Inherits=`
> attributes are unaffected; only `web.config` entries that name an *assembly* need updating.

## What works

* `[WebService]` / `[WebMethod]` classes served from `.asmx`
* SOAP 1.1 and 1.2, and JSON responses for `[ScriptService]` endpoints
* `?wsdl` and the HTML help page
* SOAP headers, `SoapException`, and the `System.Web.Services.Protocols` surface

## What does not

**Client proxy generation.** The WSDL/DISCO *code generation* half — `ServiceDescriptionImporter`,
`WebReference`, what `wsdl.exe` and "Add Web Reference" used to drive — is not ported, because .NET
Core never shipped the CodeDom compile half it depends on.

Serving is unaffected. To *call* a SOAP service, generate the client with `dotnet-svcutil` or use
`System.ServiceModel.Primitives`.

For `.svc` (WCF) endpoints rather than `.asmx`, see **`AspNetCore.Web.ServiceModel`**.

## Documentation

`PORTING-GUIDE.md` (step-by-step migration from IIS) and `LIMITATIONS.md` (what differs from ASP.NET
on .NET Framework, and why) ship in the repository.

## Licence

MIT, as the upstream Mono sources this is built from and the code written for this port.
`THIRD-PARTY-NOTICES.md` ships in the package and says which part is which.
