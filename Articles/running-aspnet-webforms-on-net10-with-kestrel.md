# Running ASP.NET WebForms, MVC and Web API on .NET 10 with Kestrel

## Introduction

If you maintain a classic ASP.NET application — WebForms pages, MVC controllers, `.asmx` web
services, maybe a couple of `.svc` WCF endpoints — you have heard the same advice for years:
rewrite it. `System.Web` never made the jump to .NET Core, Microsoft has said it never will, and
the official migration story is a gradual rewrite behind a YARP proxy.

This article presents a different option: a port of the ASP.NET stack itself —
**WebForms, MVC 4, Razor, Web Pages, Web API, `.asmx`, and `.svc` hosting** — to **.NET 10**,
running under **Kestrel** instead of IIS. Your `.aspx`, `.ascx`, `.master`, `.cshtml`, code-behind,
controllers, and `web.config` stay exactly as they are. `System.Web.UI.Page` is still
`System.Web.UI.Page`; `System.Web.Mvc.Controller` is still `System.Web.Mvc.Controller`. What
changes is the *host* and the project file — not your application code.

The port is built from Mono's open-source implementation of `System.Web` and Microsoft's
open-source `aspnetwebstack` (MVC, Razor, Web API), compiled in place against .NET 10, with the
IIS/AppDomain-specific machinery replaced by an ASP.NET Core middleware. The source code is
available on GitHub: <https://github.com/pieroviano/Core.Windows.Forms>.

## The Problem: Why System.Web Never Came Along

Three hard blockers kept `System.Web` off .NET Core, and each needed a real answer:

1. **The assembly identity is taken.** Modern .NET ships an *empty* `System.Web.dll` facade inside
   the shared framework, and the runtime gives the shared framework precedence. An app-local
   `System.Web.dll` compiles fine and then dies at startup with `FileNotFoundException`. The port
   therefore ships its assemblies under new names (`Core.Web`, `Core.Configuration`, and so on)
   while keeping the **namespaces** untouched — so your code and your generated page classes never
   notice.

2. **Runtime page compilation used CodeDOM.** On .NET Core, `CodeDomProvider`'s compile half
   throws `PlatformNotSupportedException`. The port keeps CodeDOM for *code generation* (that half
   still works) and hands the result to **Roslyn** for compilation. Both C# and VB.NET pages
   compile at runtime, just as they did under IIS.

3. **`BinaryFormatter` is gone.** View state and out-of-process session state used it for
   arbitrary object graphs. Rather than silently swapping in something insecure, the port *refuses*
   a type with no native encoding and tells you which type it was — and you opt in to a serializer
   (a JSON-based one is included, or you plug in your own).

## Hosting: One Extension Method

The entire hosting model is a single ASP.NET Core extension method, `UseWebForms`. Here is the
complete `Program.cs` of a working WebForms application:

```csharp
using Microsoft.AspNetCore.Builder;
using System.Web.Hosting.Kestrel;

var builder = WebApplication.CreateBuilder (args);
var app = builder.Build ();

// Static files first: let Kestrel serve .css/.js/images directly rather than
// routing them through the System.Web pipeline.
app.UseStaticFiles ();

app.UseWebForms (options => {
    options.PhysicalPath = app.Environment.ContentRootPath;
    options.VirtualPath  = "/";
    options.SiteName     = "WebFormsSample";

    // Opt in to a serializer for state objects with no native encoding.
    options.StateSerializer = new System.Web.JsonStateObjectSerializer ();
});

app.Run ();
```

That is the whole host. Behind `UseWebForms`, the port initializes the `System.Web` runtime
(`HttpRuntime`, `BuildManager`, the module and handler pipeline, configuration) inside the single
process — there are no AppDomains anymore — and an adapter maps each incoming ASP.NET Core request
onto an `HttpWorkerRequest`, exactly the abstraction IIS used to fill.

Everything downstream of that is the ASP.NET you already know:

- `web.config` is read with full `<location>`, inheritance, and `<httpModules>` /
  `<httpHandlers>` semantics.
- `Global.asax`, `HttpApplication` events, custom modules and handlers run unchanged.
- Postbacks, `__VIEWSTATE`, validators, master pages, user controls, `GridView`, `Repeater`,
  `UpdatePanel` and `ScriptManager` all work.
- `App_Code` is still compiled at runtime by `BuildManager`.

## Your Pages Don't Change

To make the point concrete, here is code-behind from the sample application. There is nothing to
see — and that is the point. This is ordinary WebForms code, running on .NET 10:

```csharp
public class DefaultPage : Page
{
    protected Label message;
    protected Label counter;
    protected Repeater items;
    protected GridView grid;

    // Survives postbacks through __VIEWSTATE, not a field.
    int PostbackCount {
        get {
            object v = ViewState ["postbacks"];
            return v == null ? 0 : (int) v;
        }
        set { ViewState ["postbacks"] = value; }
    }

    protected override void OnLoad (EventArgs e)
    {
        base.OnLoad (e);

        message.Text = IsPostBack ? "hello again (postback)" : "hello from Page_Load";

        if (IsPostBack)
            PostbackCount = PostbackCount + 1;
        counter.Text = PostbackCount.ToString ();

        if (!IsPostBack) {
            items.DataSource = new List<string> { "alpha", "beta", "gamma" };
            items.DataBind ();
        }
    }
}
```

## MVC, Web API, and the Rest of the Stack

MVC is not a separate host — under classic ASP.NET it was always just an `HttpModule` and a
handler registered inside `System.Web`, and the same is true here. The MVC sample's host is
almost identical to the WebForms one:

```csharp
app.UseWebForms (options => {
    options.PhysicalPath = app.Environment.ContentRootPath;
    options.VirtualPath  = "/";
    options.SiteName     = "MvcSample";

    // Controllers are discovered by scanning loaded assemblies, so name
    // the assembly that contains them.
    options.ApplicationAssemblies =
        new [] { typeof (MvcSample.Controllers.HomeController).Assembly };
});
```

What you get per stack:

| Stack | Status on .NET 10 |
|---|---|
| WebForms (`.aspx`, `.ascx`, `.master`) | Works, C# and VB.NET, runtime-compiled by Roslyn |
| ASP.NET MVC | **MVC 4** with Razor v2, plus attribute routing (`[Route]`), bundling, and `@await` in views |
| Web Pages (standalone `.cshtml`) | Works |
| Web API | Works — routing, content negotiation, model binding |
| `.asmx` SOAP services | Works |
| `.svc` WCF services | Served by **CoreWCF** at the same URLs; `[ServiceContract]` moves from `System.ServiceModel` to `CoreWCF` |
| Session state | `InProc`, plus `StateServer` (reimplemented over `IDistributedCache`) and `SQLServer` against a real ASPState database — `web.config` unchanged |
| Dynamic Data / `LinqDataSource` | Works, on `IQueryable`/EF Core instead of LINQ to SQL |
| Script/style bundling | `System.Web.Optimization` works |
| `System.Web.Mail` | Works unchanged (yes, really) |

## Getting Started

One package reference is enough for a WebForms application:

```xml
<ItemGroup>
  <PackageReference Include="AspNetCore.Web.Hosting.Kestrel" Version="1.0.0" />
</ItemGroup>
```

It transitively brings in the core of the port (`AspNetCore.Web.Base`, which contains the
`Core.Web` assembly, plus configuration, services, and extensions), and — importantly — its
build assets do the fiddly project-file work for you:

- **Facade removal.** The empty .NET `System.Web.dll` and `System.Configuration.dll` are removed
  from the reference set. Without this you get `CS0433` type collisions at build time. The targets
  also *assert* at build time that `Core.Web.dll` reached your output directory, turning what
  would be a confusing runtime `TypeLoadException` into a clear build error.
- **Content handling.** The globs that copy `.aspx`, `.cshtml`, `web.config` and friends next to
  your assembly, with `App_Code` shipped as content (the runtime compiles it, not MSBuild) and
  `bin\`/`obj\` excluded.

Then add one package per additional stack you use — `AspNetCore.Web.Mvc` for MVC,
`AspNetCore.Web.Http` (with `AspNetCore.Web.Http.WebHost` and `AspNetCore.Net.Http.Formatting`)
for Web API, `AspNetCore.Web.ServiceModel` for `.svc`, `AspNetCore.Web.SessionState` for
out-of-process session, and so on.

Note the deliberate naming split: the **package** is `AspNetCore.Web.Base`, the **assembly**
inside it is `Core.Web`, and the **namespace** is still `System.Web`. The only place the
assembly name ever surfaces in your application is a `web.config` line that names an assembly
explicitly.

## Honest Limitations

A port is only trustworthy if it is clear about what it does *not* do:

- **.NET Remoting (`.rem`/`.soap`) is not portable at all.** Transparent proxies are a CLR
  feature CoreCLR simply does not have. Re-expose those endpoints as Web API.
- **Mobile controls (`System.Web.Mobile`)** require a rewrite — deprecated since 2005, and
  there is no implementation to port.
- **MVC is MVC 4**, not ASP.NET Core MVC: no tag helpers, no view components. (Attribute routing
  and `@await` are supplied by the port.)
- **Custom `ServiceHostFactory`** in `.svc` files is not supported — CoreWCF builds the host.
- **Third-party server controls** compiled against the strong-named Microsoft `System.Web` will
  not bind; you need the source or a recompiled build.
- **Out-of-process session data does not carry across** from your existing StateServer/ASPState —
  and types that "worked for years" under `InProc` (which never serialized anything) will now be
  checked on first use.
- **`<system.webServer>`** content (IIS-native modules) must be re-expressed as ASP.NET Core
  middleware or classic `<httpModules>`.

## How Well Is It Tested?

The port ships with **445 tests across fifteen projects**, and they are not unit-test theatre:
the functional suites boot a *real* Kestrel server on a loopback port and drive it with a real
`HttpClient`; the browser suites drive the sample sites through **Playwright** in real Chromium —
clicking buttons, following postbacks, asserting on rendered validators. Separate suites cover
the VB.NET compilation path, MVC routing and Razor views, Web API negotiation, WCF SOAP calls
over CoreWCF, StateServer and SQL Server session state, and Dynamic Data scaffolding over HTTP.

## Conclusion

"Rewrite it" is good advice when you can afford it. But plenty of teams are sitting on large,
stable WebForms and MVC applications where the only thing actually wrong with them is the
platform they are pinned to: .NET Framework, IIS, Windows. This port removes that pin. The same
pages, the same controllers, the same `web.config` — hosted by Kestrel, on .NET 10, on any
platform .NET 10 runs on, with modern tooling and a supported runtime underneath.

The migration is a host swap, not a rewrite — and a host swap is a weekend, not a year.

The full source code, samples, tests, and a step-by-step porting guide are in the repository:
<https://github.com/pieroviano/Core.Windows.Forms>

Happy coding!
