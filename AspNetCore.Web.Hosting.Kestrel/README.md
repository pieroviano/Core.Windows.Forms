# AspNetCore.Web.Hosting.Kestrel

**Start here.** Hosts a classic ASP.NET application — WebForms, MVC, Razor, Web Pages, Web API — on
.NET 10 under Kestrel, instead of IIS.

```xml
<PackageReference Include="AspNetCore.Web.Hosting.Kestrel" Version="1.0.0" />
```

```csharp
using Microsoft.AspNetCore.Builder;
using System.Web.Hosting.Kestrel;

var builder = WebApplication.CreateBuilder (args);
var app = builder.Build ();

app.UseStaticFiles ();          // before UseWebForms, so Kestrel serves css/js/images directly

app.UseWebForms (options => {
    options.PhysicalPath = app.Environment.ContentRootPath;   // the directory holding web.config
    options.VirtualPath  = "/";
    options.SiteName     = "MyApp";
});

app.Run ();
```

That is the whole host, whatever stack the application uses. Your `.aspx`, `.ascx`, `.master`,
`.cshtml`, code-behind, controllers and `web.config` stay as they are, and namespaces are unchanged —
`System.Web.UI.Page` is still `System.Web.UI.Page`.

## What comes with it

`AspNetCore.Web.Base`, `AspNetCore.Configuration`, `AspNetCore.Web.Services`, `AspNetCore.Web.Extensions`
and `AspNetCore.Web.ConfigBridge` arrive transitively. Add one more package per stack you use — MVC,
Web Pages, Web API, WCF, bundling, out-of-process session state.

Two build assets are imported automatically:

* **`WebFormsPort.targets`** — removes the empty .NET `System.Web.dll` / `System.Configuration.dll`
  facades. **Not optional**: without it they collide with the port, giving `CS0433` at build time and
  `TypeLoadException` on the first request. It also asserts that `Core.Web.dll` reached your output
  directory, turning that runtime failure into a build error.
* **Project-tree nesting** — puts `Default.aspx.cs` and `Default.aspx.designer.cs` under
  `Default.aspx` in Solution Explorer, for `.aspx`, `.ascx`, `.master`, `.ashx`, `.asmx` and `.asax`,
  in C# and VB.

## Options

| Option | Purpose |
|---|---|
| `PhysicalPath` | The application directory — where `web.config` lives. |
| `VirtualPath` | `"/"` unless hosted under a sub-path. |
| `SiteName` | Reported through `HostingEnvironment.SiteName`. |
| `ShouldHandle` | Filter deciding which requests reach System.Web. Prefer middleware ordering. |
| `StateSerializer` | Serializer for view state / session objects with no native encoding. |
| `UseAspNetCoreAuthentication` | Flow the ASP.NET Core principal into `HttpContext.User`. |
| `MachineConfigPath` | Use an operator-supplied `machine.config` instead of the embedded copy. |
| `ApplicationAssemblies` | Assemblies to load up front so `Inherits=` types resolve. |
| `TemporaryFilesPath` | Where generated pages are compiled. Set it for side-by-side instances. |

## Things that bite

* **Order matters.** `UseWebForms` handles everything by default and lets `<httpHandlers>` and the
  route table decide the outcome — including 403 for `.cs`/`.config`. Anything you want handled
  outside System.Web goes **before** it.
* **One application per process.** There is no AppDomain and no app-pool equivalent.
* **`App_Code` is compiled at runtime**, so your project must `<Compile Remove="App_Code\**\*.cs" />`
  and ship it as content instead.
* **Assembly names in `web.config` change.** `assembly="System.Web"` becomes `assembly="Core.Web"`;
  left alone it binds to the empty framework facade and the registration is silently dropped.

## Documentation

`PORTING-GUIDE.md` (step-by-step migration from IIS) and `LIMITATIONS.md` (what differs from ASP.NET
on .NET Framework, and why) ship in the repository.

## Licence

MIT, as the upstream Mono sources this is built from and the code written for this port.
`THIRD-PARTY-NOTICES.md` ships in the package and says which part is which.
