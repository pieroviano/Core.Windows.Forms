# Porting an ASP.NET application from IIS to Kestrel

A step-by-step guide to taking a `System.Web` application off IIS and .NET Framework and running it on
.NET 10 under Kestrel, using this port from NuGet.

**This is not a rewrite guide.** Your `.aspx`, `.ascx`, `.master`, `.cshtml`, controllers, code-behind
and `web.config` stay as they are, and namespaces are unchanged — `System.Web.UI.Page` is still
`System.Web.UI.Page`, `System.Web.Mvc.Controller` is still `System.Web.Mvc.Controller`. What changes is
the *host*, the project file, and a short list of things IIS used to do for you.

Read [LIMITATIONS.md](LIMITATIONS.md) first. If your application depends on .NET Remoting, that is
worth knowing before you start rather than at step 6.

---

## Step 0 — Decide whether this is viable

Work through this before touching anything. Each "yes" is a blocker or a rewrite.

| Check | If yes |
|---|---|
| Hosts `.svc` (WCF) endpoints? | Works — served by CoreWCF at the path each `.svc` sits at, keeping your URLs and contracts. Your `[ServiceContract]` attributes move from `System.ServiceModel` to `CoreWCF`. See step 3. |
| Uses a custom `ServiceHostFactory` in a `.svc`? | Not supported — CoreWCF builds the host itself. Drop the `Factory=` attribute, or configure that service through CoreWCF directly. |
| Uses `.rem`/`.soap` (.NET Remoting)? | **Not portable at all** — transparent proxies are a CLR feature CoreCLR does not have. Re-expose as Web API. LIMITATIONS §2. |
| `<sessionState mode="StateServer">` or `"SQLServer"`? | Works, and `web.config` is unchanged — but StateServer is now an `IDistributedCache` (not `aspnet_state.exe`), and **existing session data does not carry across** either mode. See step 5. |
| Stores anything unusual in `Session`, and uses an out-of-process mode? | `InProc` never serialized; these do. A type that has worked for years can fail on the first request. See step 5. |
| Uses ASP.NET MVC? | Works — **MVC 4** with Razor v2, plus attribute routing, bundling and `@await` supplied by this port. No view components or tag helpers. See step 3. |
| Uses Dynamic Data or `LinqDataSource`? | Works, but on `IQueryable`/EF Core rather than LINQ to SQL — a `DataContext` cannot be the context. See step 3. |
| Uses `System.Web.Mail`? | Works unchanged. Obsolete, but your call sites do not have to move. |
| Uses Mobile controls (`System.Web.Mobile`)? | **Rewrite required.** Nothing exists to port — Mono's assembly is a stub, and the technology was deprecated in 2005. LIMITATIONS §4. |
| Stores non-DTO objects in `Session` or `ViewState`? | Works — `ObjectGraphStateSerializer` round-trips private fields, cycles and polymorphism. Types must be allow-listed. See step 5. |
| `<system.webServer>` doing real work? | Re-express as middleware or `<httpModules>`. See step 4. |
| Relies on Windows/Negotiate auth? | Supported, via ASP.NET Core authentication. See step 4. |
| Third-party controls compiled against strong-named `System.Web`? | They will not bind — the port has its own key, not Microsoft's. Check whether source is available. |

Also decide **where the application will live on disk**. The port keeps the classic layout: a directory
containing `web.config`, your content files, and a `bin/` of assemblies. The SDK builds into
`bin/Debug/net10.0/`. Step 2 reconciles these.

---

## Step 1 — Reference the port

One package reference is enough for a WebForms application:

```xml
<ItemGroup>
  <PackageReference Include="AspNetCore.Web.Hosting.Kestrel" Version="1.0.0" />
</ItemGroup>
```

It brings in `AspNetCore.Web.Base`, `AspNetCore.Configuration`, `AspNetCore.Web.Services`,
`AspNetCore.Web.Extensions` and `AspNetCore.Web.ConfigBridge` transitively, and carries in its `build/`
folder the whole of what a ported project file used to have to say for itself — NuGet imports it
automatically:

* **Facade removal** — takes the empty .NET `System.Web.dll` / `System.Configuration.dll` out of the
  reference set. Not optional: without it they collide with the port, giving `CS0433` at build time and
  `TypeLoadException` on the first request. It also asserts that `Core.Web.dll` reached your output
  directory, turning that runtime failure into a build error.
* **Content items** — the dozen globs that copy `.aspx`, `.cshtml`, `web.config` and the rest beside
  your assembly, `App_Code` handled as the runtime expects, and `bin\`/`obj\` excluded from all of it.
  See step 2.
* **Project-tree nesting** — puts `Default.aspx.cs` and `Default.aspx.designer.cs` under `Default.aspx`
  in Solution Explorer. See step 8.

Those assets are imported by file name — NuGet looks for `build/AspNetCore.Web.Hosting.Kestrel.props`
and `.targets` and nothing else. If you ever repackage the port under a different id, rename them to
match, or consumers get a build with none of the above and a `CS0433` they cannot act on.

Add one more package per stack you use:

| Package | Contents |
|---|---|
| `AspNetCore.Web.Hosting.Kestrel` | `UseWebForms`, the middleware, the build assets. **Start here.** |
| `AspNetCore.Web.Base` | `System.Web` itself — pages, controls, session, caching, security |
| `AspNetCore.Configuration` | Mono's `System.Configuration`, which the port is written against |
| `AspNetCore.Web.Services` | `.asmx` / SOAP serving |
| `AspNetCore.Web.Extensions` | `ScriptManager`, `UpdatePanel`, page methods |
| `AspNetCore.Web.ConfigBridge` | lets third-party libraries' `ConfigurationManager` see `web.config` |
| `AspNetCore.Web.Mvc` | ASP.NET MVC 4 — controllers, Razor views, `[Route]` attribute routing |
| `AspNetCore.Web.Optimization` | `System.Web.Optimization` — script and style bundling |
| `AspNetCore.Web.WebPages.Base`, `AspNetCore.Web.WebPages.Razor`, `AspNetCore.Web.WebPages.Deployment`, `AspNetCore.Web.Razor`, `AspNetCore.Web.Infrastructure` | the Razor view engine and Web Pages runtime |
| `AspNetCore.Web.Http`, `AspNetCore.Web.Http.WebHost`, `AspNetCore.Net.Http.Formatting` | ASP.NET Web API |
| `AspNetCore.Web.ServiceModel` | `.svc` (WCF) hosting — brings CoreWCF with it |
| `AspNetCore.Web.SessionState` | `<sessionState>` `StateServer` and `SQLServer` — brings Microsoft.Data.SqlClient with it |
| `AspNetCore.Web.DynamicData` | ASP.NET Dynamic Data scaffolding, on `IQueryable`/EF Core |

**Package names and assembly names differ, deliberately.** You reference `AspNetCore.Web.Base`; the
assembly inside it is `Core.Web`. The reason is in the next section but one — .NET ships an empty
`System.Web.dll` and the port cannot reuse that name. Namespaces are untouched either way; the only
place the *assembly* name matters is a `web.config` entry that names one (step 4).

**And two ids carry a `.Base` suffix**, which nothing else in the naming scheme explains, so: the two
packages whose id would otherwise be a *prefix of every other id in the set* — `AspNetCore.Web` and
`AspNetCore.Web.WebPages` — are published as `AspNetCore.Web.Base` and `AspNetCore.Web.WebPages.Base`.
A bare `AspNetCore.Web` reads as an umbrella meta-package rather than one assembly among twenty, and
on a public feed it is the id most likely to be squatted or confused with the repository name. The
other seventeen ids are `AspNet` + the assembly name with no suffix. If you are searching a feed and
find only `AspNetCore.Web.Base`, that is the one.

### Building the port from source instead

Only needed if you are changing the port itself:

```powershell
git clone <this repo>
cd AspNetCore.Web
git submodule update --init --recursive     # Mono, AND the nested submodules inside it
dotnet build AspNetCore.Web.slnx
dotnet test AspNetCore.Web.slnx             # 445 tests
```

`--recursive` is not optional: Razor, WebPages, MVC and Web API come from
`Mono/external/aspnetwebstack`, and Json.NET from `Mono/external/Newtonsoft.Json` — submodules inside a
submodule. Without it the generator drops every path and those assemblies build empty rather than
failing.

---

## Step 2 — Convert the project file

> **There is a tool for this.** `Tools/port-project` does steps 1 to 4 mechanically — renames your
> project to `.old`, writes the file below with the right packages detected from your references and
> file tree, generates the host, and applies the `web.config` assembly renames — then reports what it
> could not decide. It previews by default:
>
> ```powershell
> dotnet run --project Tools/port-project -- C:\src\MyApp            # preview
> dotnet run --project Tools/port-project -- C:\src\MyApp --apply    # do it
> ```
>
> See [Tools/port-project/README.md](Tools/port-project/README.md). The rest of this step is what it
> generates and why, which is worth reading either way.

Replace the old `.csproj` with an SDK-style one. In full:

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">

  <PropertyGroup>
    <TargetFrameworks>net8.0;net10.0</TargetFrameworks>
    <LangVersion>latest</LangVersion>
    <Nullable>disable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="AspNetCore.Web.Hosting.Kestrel" Version="1.0.0" />
  </ItemGroup>

</Project>
```

That is the whole file. There is no list of content globs, no `App_Code` handling, no
`EnableDefaultContentItems`, and no `Import` — the package's `build/` assets do all of it, and NuGet
imports them for you.

`Samples/WebFormsSample` is exactly this shape: **31 lines**, of which the only reason it is not
shorter is that it uses a `ProjectReference` and therefore has to import those assets by hand.

### What the package does on your behalf

| | |
|---|---|
| **Content** | `.aspx`, `.ascx`, `.master`, `.cshtml`, `.vbhtml`, `.ashx`, `.asmx`, `.asax`, `.svc`, `.skin`, `.sitemap`, `.browser`, `.config`, `.resx`, `.css`, `.js` are copied beside the built assembly with `PreserveNewest` |
| **`bin\` and `obj\` excluded** | from every one of those globs |
| **`App_Code`** | removed from `Compile`, added as content |
| **The SDK's own content items** | turned off, so nothing is included twice |
| **`ImplicitUsings`** | defaulted to `disable` |
| **The framework facades** | `System.Web.dll` / `System.Configuration.dll` removed from the reference set |
| **Project-tree nesting** | `Default.aspx.cs` and `.designer.cs` shown under `Default.aspx` |

Each of those used to be something you copied into every project and got subtly wrong in one of them.

### Why those defaults, in one paragraph each

**Markup is content, not compile input.** `.aspx` and `.cshtml` are compiled *by the runtime*, at
request time, through `BuildManager` — exactly as they were under IIS. If the SDK compiles them too
you get two definitions of every page class, and a `.cshtml` in particular gets handed to the ASP.NET
Core Razor compiler, which fails on `Page`, `Request` and `IsPost` — names that do not exist in that
world.

**`bin\` and `obj\` have to be excluded, always.** The globs are recursive, so once a build has
copied the site into `bin\` they match those copies as well. The output then nests one level deeper on
every build, and a `Rebuild` fails outright with `MSB3030` because items are evaluated before the
`Clean` and the copy step looks for files that have just been deleted. An incremental `dotnet build`
hides it, which is what makes it a bad afternoon rather than a quick fix.

**`App_Code` is compiled at runtime,** into its own assembly, the way ASP.NET always did it. The SDK
must not also compile it into your assembly — that defines every type twice and makes them ambiguous
to generated pages, which surfaces as a page-compile error naming a type that looks perfectly fine.

**Implicit usings collide.** They put `Microsoft.AspNetCore.Builder` and friends in global scope, where
`HttpContext`, `IHttpHandler` and `RouteData` clash with the `System.Web` types of the same name —
turning code that was correct on .NET Framework into a compile error.

### Adjusting it

Every default is a property, and all of them are overridable:

```xml
<!-- Add an extension; do not replace the list unless you mean to drop the rest -->
<WebFormsContentGlobs>$(WebFormsContentGlobs);**\*.myext</WebFormsContentGlobs>

<!-- Exclude more than bin\ and obj\ -->
<WebFormsContentExclude>bin\**;obj\**;node_modules\**</WebFormsContentExclude>

<!-- Opt out entirely and declare your own <None Include> items -->
<WebFormsIncludeContent>false</WebFormsIncludeContent>

<!-- Keep App_Code as ordinary compile input (only if the runtime does not also compile it) -->
<WebFormsAppCodeAsContent>false</WebFormsAppCodeAsContent>

<!-- Keep the SDK's content items as well -->
<WebFormsDisableDefaultContentItems>false</WebFormsDisableDefaultContentItems>

<!-- Flat project tree; see step 8 for the nesting switches -->
<WebFormsNestCodeBehind>false</WebFormsNestCodeBehind>
```

Adoption is incremental: the content globs skip anything you have already declared as a `None` item,
so a project part-way through the conversion keeps its own globs and gets only what is missing,
instead of failing with `NETSDK1022` duplicate-item errors.

### The two things still left to you

* **Keep code-behind and controllers as `<Compile>`.** They are compiled by the SDK like any other
  class. `App_Code` is the only exception, and it is handled for you.
* **VB applications need `<RootNamespace></RootNamespace>`** — empty. VB otherwise prepends the project
  name to every declaration, so a file declaring `Namespace MyApp` produces `MyApp.MyApp.DefaultPage`
  and `Inherits="MyApp.DefaultPage"` in the `.aspx` stops resolving.

### If a type is not found

Two different mechanisms, and they fail differently:

* **`Inherits="MyApp.Page"` on a page** is answered by scanning *loaded* assemblies. If your
  application directory and your assemblies are in different places, name them explicitly:

  ```csharp
  options.ApplicationAssemblies = new [] { typeof (MyApp.SomePage).Assembly };
  ```

* **MVC and Web API controllers** are found by scanning `BuildManager.GetReferencedAssemblies()`, which
  is `<compilation><assemblies>` in `web.config`. Add your own assembly there:

  ```xml
  <compilation><assemblies><add assembly="MyApp" /></assemblies></compilation>
  ```

  and then **delete the compilation directory** — the controller scan is cached to disk as
  `MVC-ControllerTypeCache.xml`, and a cache written before the assembly was listed is never re-scanned.

---

## Step 3 — Create the host

Your application becomes a .NET 10 executable that starts Kestrel. Create `Program.cs` at the root of
the application directory:

```csharp
using Microsoft.AspNetCore.Builder;
using System.Web.Hosting.Kestrel;

var builder = WebApplication.CreateBuilder (args);
var app = builder.Build ();

// Static files FIRST: let Kestrel serve .css/.js/images directly rather than routing
// every one of them through the System.Web pipeline.
app.UseStaticFiles ();

app.UseWebForms (options => {
    options.PhysicalPath = app.Environment.ContentRootPath;  // the directory holding web.config
    options.VirtualPath  = "/";                              // "/" unless hosted under a sub-path
    options.SiteName     = "MyApp";
});

app.Run ();
```

That is the entire host, whether the application is WebForms, MVC, Web Pages, Web API or a mix.
`UseWebForms` initialises the runtime, registers shutdown against
`IHostApplicationLifetime.ApplicationStopping`, and inserts the middleware that hands each request to
`HttpRuntime`.

**Order matters.** `UseWebForms` handles everything by default and lets `<httpHandlers>` and the route
table decide the outcome — including 403 for `.cs`/`.config` and 404 for unmapped `.axd`, which
applications depend on. Anything you want handled outside System.Web goes **before** it.

### The full option set

| Option | Purpose |
|---|---|
| `PhysicalPath` | The application directory — where `web.config` lives. |
| `VirtualPath` | `"/"` unless hosted under a sub-path. |
| `SiteName` | Reported through `HostingEnvironment.SiteName`. |
| `ShouldHandle` | Filter deciding which requests reach System.Web. Prefer middleware ordering. |
| `StateSerializer` | Serializer for view state / session objects with no native encoding. Step 5. |
| `UseAspNetCoreAuthentication` | Flow the ASP.NET Core principal into `HttpContext.User`. Step 4. |
| `MachineConfigPath` | Use an operator-supplied `machine.config` instead of the embedded copy. Step 4. |
| `ApplicationAssemblies` | Assemblies to load up front so `Inherits=` types resolve. Step 2. |
| `TemporaryFilesPath` | Where generated pages are compiled. Set it for side-by-side instances. Step 9. |

### MVC and Web API registration

Unchanged from what you already have — both register in `Global.asax`:

```csharp
// MVC
RouteTable.Routes.MapRoute ("Default", "{controller}/{action}/{id}",
                            new { controller = "Home", action = "Index", id = UrlParameter.Optional });

// Web API
GlobalConfiguration.Configuration.Routes.MapHttpRoute (
    name: "DefaultApi",
    routeTemplate: "api/{controller}/{id}",
    defaults: new { id = System.Web.Http.RouteParameter.Optional });
```

`RouteParameter` is deliberately fully qualified above: `Global.asax` imports
`System.Web.UI.WebControls` by default, which has a `RouteParameter` of its own, and the bare name is
ambiguous between the two.

Both need `UrlRoutingModule` registered, which the generated root configuration already does:

```xml
<system.web><httpModules><add name="UrlRoutingModule" type="System.Web.Routing.UrlRoutingModule" /></httpModules></system.web>
```

Razor views additionally need the build provider and, for MVC, a `Views/web.config` naming the host
factory — copy those from `Samples/MvcSample`.

### Attribute routing

`[Route]` and `[RoutePrefix]` work, and your controllers need no change:

```csharp
[RoutePrefix ("catalog")]
public class CatalogController : Controller
{
    [Route ("")]                              // /catalog
    public ActionResult Index () { ... }

    [Route ("{id:int}")]                      // /catalog/42
    public ActionResult Details (int id) { ... }

    [Route ("{id:int}/reviews/{page:int=1}")] // /catalog/42/reviews, /catalog/42/reviews/3
    public ActionResult Reviews (int id, int page) { ... }

    [HttpPost, Route ("{id:int}")]            // same URL, POST only
    public ActionResult Update (int id) { ... }

    [Route ("~/legacy-catalog")]              // "~/" escapes the prefix
    public ActionResult Legacy () { ... }
}
```

One line registers them, and **it must come first**:

```csharp
void Application_Start (object sender, EventArgs e)
{
    RouteTable.Routes.MapMvcAttributeRoutes ();     // BEFORE the conventional routes

    RouteTable.Routes.MapRoute ("Default", "{controller}/{action}/{id}",
                                new { controller = "Home", action = "Index", id = UrlParameter.Optional });
}
```

Before, not after: routes match in order and `{controller}/{action}/{id}` matches very nearly
everything, so registered first it would swallow every attribute route behind it. MVC 5 avoids this by
keeping attribute routes in a separate collection; here they are ordinary routes, so the call order is
the ordering. Within the attribute routes themselves, ordering is by specificity — a literal segment
beats a constrained parameter beats a plain parameter beats a catch-all — so `/catalog/new` reaches
`New ()` regardless of where it sits in the file.

Inline constraints available: `int`, `long`, `bool`, `guid`, `decimal`, `double`, `float`, `datetime`,
`alpha`, `required`, `length(n)`, `length(min,max)`, `minlength(n)`, `maxlength(n)`, `min(n)`,
`max(n)`, `range(min,max)`, `regex(...)`. Several may apply to one parameter (`{s:alpha:minlength(3)}`),
and you can add your own to `InlineRouteConstraintResolver.Constraints`.

Note one behaviour inherited from MVC 5: once a controller has attribute routes, its actions are **no
longer reachable through conventional routes**. That is deliberate — otherwise `/catalog/search/ab`
would reach `Search` via `{controller}/{action}/{id}` and bypass the `minlength(3)` you declared.

### Bundling

`App_Start/BundleConfig.cs` and your `@Scripts.Render` / `@Styles.Render` calls work unchanged. Add the
package, and register as you already do:

```csharp
void Application_Start (object sender, EventArgs e)
{
    // ... routes ...
    BundleConfig.RegisterBundles (BundleTable.Bundles);
}
```

Wildcards, `{version}` tokens, `IncludeDirectory`, CDN paths and content-hash cache busting all work.
No `web.config` edit is needed — each bundle registers its own route, inserted ahead of your
conventional routes so `/bundles/app` cannot be claimed by `{controller}/{action}/{id}`.

**Bundles are concatenated, not minified.** WebGrease is .NET Framework only and dead, and a substitute
minifier that mangles one edge case in your JavaScript would fail silently and intermittently — worse
than a larger file. Minify at build time and point the bundle at the output, or use `CdnPath`. See
LIMITATIONS §2.

### Async views (`@await`)

```cshtml
<p>@await ReportService.SummaryAsync (Model.Id)</p>

@{
    var count = await ReportService.CountAsync ();
}
```

Nothing to configure. Views containing an `await` compile to an async method with a synchronous bridge;
views without one are untouched.

The bridge **blocks** the request thread while the view's awaits complete. There is no deadlock risk
(ASP.NET Core installs no `SynchronizationContext`), but a view awaiting slow I/O holds a thread-pool
thread. Prefer awaiting in the **action** and passing the result to the view; use `@await` for the cases
where restructuring the caller is not worth it. See LIMITATIONS §2.

### `.svc` (WCF) registration

`.svc` endpoints are the one thing that does **not** go through `UseWebForms`. The SOAP stack
underneath is [CoreWCF](https://github.com/CoreWCF/CoreWCF) — the supported server-side WCF for .NET;
`System.ServiceModel`'s server half does not exist there at all. What `Core.Web.ServiceModel` adds is
the part CoreWCF deliberately does not have: the ASP.NET `.svc` convention. It reads the
`@ServiceHost` directive out of every `.svc` under your application, resolves the type each one names,
and registers it with CoreWCF **at the address the file's own path implies** — so your existing
endpoint URLs keep working without a route being configured anywhere.

```csharp
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using System.Web.Hosting.Kestrel;
using System.Web.ServiceModel;

var builder = WebApplication.CreateBuilder (args);

// Before Build (): CoreWCF is configured through DI, so this has to run while the service
// collection is still open.
builder.Services.AddSvcEndpoints (builder.Environment.ContentRootPath, options => {
    options.ServiceAssemblies = new [] { typeof (MyApp.Services.EchoService).Assembly };
});

var app = builder.Build ();

app.UseSvcEndpoints ();     // BEFORE UseWebForms

app.UseWebForms (options => {
    options.PhysicalPath = app.Environment.ContentRootPath;
    options.VirtualPath  = "/";
    options.SiteName     = "MyApp";
});

app.Run ();
```

Two calls rather than one option on `UseWebForms`, and the split is forced: `AddServiceModelServices`
needs an open `IServiceCollection`, which only exists before `Build ()`, while middleware can only be
inserted after it. **`UseSvcEndpoints` must come before `UseWebForms`** — System.Web's handler mapping
would otherwise take the request first, and it has no idea what a `.svc` file is; it would serve the
directive as text or refuse it outright. It only claims the paths it discovered, so everything else,
including your `.aspx` pages in the same application, falls through untouched.

Your `.svc` files themselves are **unchanged** — they stay content, read at startup for their directive
and never compiled:

```aspx
<%@ ServiceHost Language="C#" Debug="true" Service="MyApp.Services.EchoService" %>
```

`**\*.svc` is already in the package's content globs, so the project file needs nothing added (step 2).

What does change is the service code, by two `using` lines:

| Before | After |
|---|---|
| `using System.ServiceModel;` | `using CoreWCF;` |
| `[ServiceContract]`, `[OperationContract]`, `[DataContract]` | same attributes, from `CoreWCF` |
| `web.config` `<system.serviceModel>` | `SvcEndpointOptions`, in code |

The contract may sit on an interface the class implements or on the class itself — both are found, as
WCF always allowed.

| Option | Purpose |
|---|---|
| `PhysicalPath` | Directory scanned for `.svc` files. `bin/` and `obj/` are skipped. |
| `ServiceAssemblies` | Assemblies searched for the types named by `Service=`. Name yours; loaded assemblies are searched anyway. |
| `Binding` | Defaults to `BasicHttpBinding` — SOAP 1.1 over HTTP, which is what an `.svc` served over `http://` almost always was. |
| `EnableWsdl` | `?wsdl` and `?singleWsdl` serve a metadata document. On by default. |
| `ThrowOnUnresolvedService` | A `.svc` naming a type that cannot be resolved fails at **startup**. On by default — discovery runs once, so there is no later chance to notice. |

`<system.serviceModel>` in `web.config` is **not read**. Bindings, behaviours and quotas configured
there have to be re-expressed on `SvcEndpointOptions.Binding` or through CoreWCF's own configuration.
A non-default binding is the usual case:

```csharp
builder.Services.AddSvcEndpoints (path, options => {
    options.Binding = new CoreWCF.BasicHttpBinding (CoreWCF.BasicHttpSecurityMode.Transport) {
        MaxReceivedMessageSize = 10 * 1024 * 1024,
    };
});
```

`Samples/WcfSample` is a complete working example — two services, one at `/Echo.svc` and one at
`/Api/Calculator.svc` in a sub-directory, alongside a WebForms page in the same application on the
same port.

---

## Step 4 — Fix `web.config`

### Assembly names

The port's assemblies are `Core.*`. Anything naming an assembly must be updated.

| Before | After | Why |
|---|---|---|
| `type="My.Handler, MyApp"` | unchanged | your own assembly is fine |
| `type="Some.Type, System.Web"` | `type="Some.Type"` | drop the qualification; `HttpApplication.LoadType` scans loaded assemblies |
| `assembly="System.Web"` | `assembly="Core.Web"` | `<compilation><assemblies>` and `<controls>` genuinely load an assembly; left alone this binds to the **empty facade** and silently drops the registration |
| `type="Some.Type, System.Configuration"` | `type="Some.Type, Core.Configuration"` | resolved by `Type.GetType`, which does **not** scan everything — leaving this binds to the empty facade and the section degrades to `DefaultSection` |
| `assembly="System.Web.Mvc"` | `assembly="Core.Web.Mvc"` | same, for MVC — including in `Views/web.config` |
| anything with `Version=4.0.0.0, PublicKeyToken=b03f5f7f11d50a3a` | strip it | that is Microsoft's key. The port is signed with its own, and .NET relaxes version when binding but never the public key |

Note *when* each kind fails:

* `<httpModules>` — instantiated at **application start**, so one bad entry takes the whole app down.
* `<compilation><assemblies>` — `BuildManager.LoadAssembly` **throws**, breaking the first page compile.
* `<httpHandlers>` — fails only when a matching request arrives, usually the right outcome.
* `<pages><controls>` — the tag prefix silently stops resolving and pages fail to parse.

### What IIS was doing

Delete `<system.webServer>`, moving each piece:

| Was | Becomes |
|---|---|
| `<modules>` (IIS-native) | `<system.web><httpModules>` if it is a managed `IHttpModule`, otherwise ASP.NET Core middleware |
| `<handlers>` | `<system.web><httpHandlers>` |
| `<rewrite>` | ASP.NET Core rewrite middleware, before `UseWebForms()` |
| `<staticContent>`, `<httpCompression>` | `UseStaticFiles()` / `UseResponseCompression()`, before `UseWebForms()` |
| `<defaultDocument>` | `UseDefaultFiles()`, or route explicitly |
| `<httpErrors>` | `<system.web><customErrors>`, or ASP.NET Core exception handling |

### Windows, Negotiate, OIDC and JWT authentication

IIS used to provide these. Now ASP.NET Core does, and the port flows the result into System.Web:

```csharp
builder.Services.AddAuthentication (NegotiateDefaults.AuthenticationScheme).AddNegotiate ();

var app = builder.Build ();
app.UseAuthentication ();        // BEFORE UseWebForms, or there is no result to flow
app.UseWebForms (options => {
    options.PhysicalPath = app.Environment.ContentRootPath;
    options.UseAspNetCoreAuthentication = true;
});
```

`<authorization>`, `User.Identity.Name` and role checks then see the caller. The option is off by
default so forms authentication is never silently overridden; when on, the principal is assigned only
when ASP.NET Core actually authenticated the request, so `<deny users="?">` still denies anonymous
callers.

`WebFormsRuntimeHost.CurrentCoreContext` gives application code the ASP.NET Core `HttpContext` — useful
for `RequestServices` (dependency injection), connection info and features.

### Machine-level configuration and the machine key

There is no shared `machine.config` on the box; the port embeds one. To supply your own:

```csharp
options.MachineConfigPath = "/etc/myapp/machine.config";
```

A **replacement**, not an overlay: a complete `machine.config`, with the root `web.config` beside it.
Validated at startup, because a malformed one disables every configuration section at once.

Set `<machineKey>` explicitly if you will run more than one instance — it defaults per process, so
forms-auth tickets and view state MACs otherwise fail to validate across instances.

---

## Step 5 — Session state and serialization

### If you are staying on `mode="InProc"`

Nothing to do here beyond serialization, below. Sessions live in the process: they do not survive a
restart and are not shared between instances, so plan for one instance or sticky routing.

### Moving to `StateServer` or `SQLServer`

Both work, and **`web.config` does not change** — the `<sessionState>` element you already have is the
one that is read. Two things do change, one in your host and one in your operations.

In the host, register a backing store and hand it over before `UseWebForms`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using System.Web.SessionState;

var builder = WebApplication.CreateBuilder (args);

// mode="StateServer" only. Redis here; AddDistributedMemoryCache () for a single instance,
// AddDistributedSqlServerCache (...) if you would rather not run Redis.
builder.Services.AddStackExchangeRedisCache (o => o.Configuration = "localhost:6379");

var app = builder.Build ();

app.UseWebFormsSessionState ();     // BEFORE UseWebForms

app.UseWebForms (options => { ... });
```

`UseWebFormsSessionState` is a no-op for `InProc` and `Off`, so it is safe to call unconditionally. It
exists because the session store is built by the provider model — `Activator.CreateInstance` on a type
named in configuration — which has no access to dependency injection; this is the seam that gets the
cache to it. It must run before the first request, because the store is built once and never rebuilt.

In operations, the substitutions are the part to plan around:

| Mode | What actually stores the session |
|---|---|
| `StateServer` | The `IDistributedCache` you registered. **Not `aspnet_state.exe`** — that protocol is not spoken, and `stateConnectionString` is ignored. |
| `SQLServer` | The stock `ASPState` database, through the standard stored procedures. An existing `aspnet_regsql`-provisioned database should need no DDL — but that combination cannot be tested here, so verify against a copy first. LIMITATIONS §2. |

For a **new** ASPState database, `aspnet_regsql.exe` does not exist on .NET 10 — the script ships in
the assembly instead:

```csharp
// Print it for a DBA to review, which is the right default in production:
Console.WriteLine (SqlSessionStateStore.GetSchemaScript ());

// Or run it directly against a database you have already created. Idempotent.
SqlSessionStateStore.EnsureSchema (connectionString);
```

Point `sqlConnectionString` at that database in `web.config`, or supply it from a secret store:

```csharp
app.UseWebFormsSessionState (o => o.SqlConnectionString = builder.Configuration ["Session:Sql"]);
```

### The one that will surprise you: existing sessions do not migrate

Sessions written by ASP.NET on .NET Framework contain `BinaryFormatter` payloads, and .NET 9+ has no
`BinaryFormatter` to read them. You can point the ported application at the same Redis instance or the
same ASPState database — the *infrastructure* carries across — but the rows already in it cannot be
read. Give the ported application its own key prefix or its own database, and plan the cutover as a
session flush, the same as any deployment that changes machine keys.

### Everything in `Session` must now serialize

This is the sharpest edge of the whole change, because nothing about it is visible in a diff.
`mode="InProc"` never wrote a session down, so a type that has worked for a decade can fail on the
first request after one word in `web.config` changes. The error says exactly that and names the type:

> A value in Session could not be serialized for out-of-process storage. `<sessionState
> mode="StateServer">` has to write every session value to a store, which `mode="InProc"` never did …

The same applies to `ViewState`. There are two serializers, and which you want depends on what you
actually keep in `Session`:

```csharp
// JSON. Safe by construction, readable in the store, and enough for DTOs.
options.StateSerializer = new System.Web.JsonStateObjectSerializer ();

// Object graphs. Private fields, cycles, shared references and polymorphism all round-trip -
// BinaryFormatter's semantics, which is what "it worked on InProc" usually depended on.
options.StateSerializer = new System.Web.ObjectGraphStateSerializer {
    AllowedTypes = { typeof (Basket), typeof (BasketLine) },
};
```

With neither, a type that has no built-in encoding is **refused with a diagnostic naming it** —
deliberately, rather than silently re-encoded.

`JsonStateObjectSerializer` gives you JSON semantics: public data round-trips; private fields, cycles
and type identity do not. `ObjectGraphStateSerializer` gives you all four, and in exchange asks you to
**allow-list the types it may reconstruct**. That is not ceremony: deserialising an attacker-influenced
payload into arbitrary types is the vulnerability `BinaryFormatter` was removed for, and a session store
in Redis or a shared database is exactly such a payload. Primitives and the BCL collections are always
allowed; `AllowedNamespaces` takes a whole namespace; `AllowAnyType` turns the check off, and is only
defensible when nothing else can write to the store. See LIMITATIONS §2.

### Two behaviours that differ from IIS

* **`Session_End` never fires** under either out-of-process mode. A distributed cache drops the key
  with nobody to notify, and the ASPState sweeper is a scheduled job rather than a callback. Only
  `InProc` could ever raise it. Move the cleanup somewhere explicit.
* **`StateServer` locking is best-effort.** `IDistributedCache` has no compare-and-swap, so two
  simultaneous requests for the same session id can both believe they hold the lock; the result is
  last-writer-wins, not corruption. `SQLServer` has no such window — the lock is a row update inside a
  stored procedure. If overlapping writes to one session must not be lost, use `SQLServer`.

Under `SQLServer`, schedule `dbo.DeleteExpiredSessions` — SQL Agent, cron, a hosted service — the way
`aspnet_regsql` used to create a SQL Agent job for you. Nothing reads expired rows, so skipping it
costs disk rather than correctness.

`Samples/SessionStateSample` is a complete working example.

---

## Step 6 — First run, and how to debug it

```powershell
dotnet run
```

Expect problems at application start rather than per page — that is where `<httpModules>` and the
configuration system fail.

**When configuration fails**, the error is wrapped in `An unexpected error occurred in
'Configuration::ctor'`, which hides the real cause. Use the diagnostic tool from a source checkout:

```powershell
dotnet run --project Tools/verify-config -- C:\path\to\your\app
```

| Symptom | Cause |
|---|---|
| `CS0433 ... exists in both` at build | the facade removal is not being applied — check the package reference resolved |
| `TypeLoadException` on first request | `Core.Web.dll` not in the output directory — same cause |
| `FileNotFoundException: Core.Web.Extensions` on every request | an `<httpModules>` entry needs an assembly you did not deploy |
| `Cannot find type` on a code-behind class | the assembly was never loaded — set `ApplicationAssemblies` (step 2) |
| `The controller for path '/…' was not found` | add your assembly to `<compilation><assemblies>`, then delete the compilation directory — the scan is cached to disk |
| `The view 'X' … was not found` | usually a view **compile** error, not a missing file: `BuildManager.GetObjectFactory` swallows it. Call `BuildManager.GetCompiledType("~/Views/…")` to see the real one |
| `appSettings` comes back as `DefaultSection` | a `type=` reference still says `, System.Configuration` |
| 404 on every `.aspx` | the root `web.config` mapping `*.aspx` → `PageHandlerFactory` is not being read |
| 404 on every routed URL | `UrlRoutingModule` is not registered in `<httpModules>` |

Set `<customErrors mode="Off">` while porting — the port's error page renders the full exception with
real file and line numbers, including the offending source line for a failed page or view compile.

### When it compiled, ran, and did nothing

Some failures produce no exception at all: a method returns `null`, a control renders an empty cell, a
collection comes back with no items — on a page that returns HTTP 200 with nothing in any log. That is
not a bug in your code, and it is worth recognising early, because it is the one failure mode this port
has that ASP.NET on .NET Framework does not.

The cause is that the port compiles **Mono's** sources in place, and Mono left members unfinished. Two
markers, with opposite ergonomics:

| Marker | What you see |
|---|---|
| `throw new NotImplementedException` | An immediate, unambiguous exception. 922 of these. |
| `[MonoTODO]` | **Nothing.** The member compiles, binds, is callable, and can return `null` for ever. 434 of these. |

[MONOTODO.md](MONOTODO.md) has the per-assembly counts, regenerated by
`Tools/gen-monotodo-inventory.ps1` so it cannot drift. Use it as a prior, not a lookup: a high count in
an assembly means "check before you rely on it", and the check is to read the upstream source, which is
sitting in `Mono/` unchanged.

```powershell
Select-String -Path Mono/mcs/class/System.Web/**/*.cs -Pattern 'MonoTODO' -Context 0,3 |
    Where-Object { $_.Context.PostContext -match 'YourMemberName' }
```

The tell for a stub is an auto-property with a private setter that nothing in the file ever assigns.
That exact shape is what made Dynamic Data's field templates render blank cells before they were
implemented.

**If the port itself is what is unfinished**, that is a different list and it is in
[LIMITATIONS.md](LIMITATIONS.md) — deliberate decisions, with their costs stated. `MONOTODO.md` is
upstream's unfinished work; `LIMITATIONS.md` is this port's.

---

## Step 7 — Write functional tests before you tune anything

You now have an application whose behaviour you are about to change. Pin it down first. `Tests/` has
working templates for every shape: HTTP suites that start a real Kestrel server on a loopback port
(`AspNetCore.Web.FunctionalTests`, `.WebPages.Tests`, `.Http.Tests`, `.ServiceModel.Tests`,
`.SessionState.Tests`) and browser suites driving a real Chromium through Playwright
(`AspNetCore.Web.BrowserTests`, `.Mvc.BrowserTests`).

Four things to copy rather than reinvent:

* **One test project per application.** The runtime hosts one application per process; `dotnet test`
  gives each project its own.
* **Its own `TemporaryFilesPath` per suite.** The compiled-page cache is keyed on the application path,
  not the process, so suites hosting the same application in parallel corrupt each other's output.
* **`WebForm.Postback`** scrapes `__VIEWSTATE` and `__EVENTVALIDATION` out of the rendered page and
  posts them back. You cannot synthesise those — view state is MAC-signed.
* **`WebForm.AsyncPostback` / `ParseDelta`** do the same for `UpdatePanel` partial postbacks, whose
  response is a length-prefixed delta rather than HTML.
* **Test `.svc` endpoints with raw SOAP, not a generated client.** What has to be true is that a
  request to the URL the `.svc` file sits at gets a SOAP response. A client proxy builds its own
  address from configuration and would still pass if the `.svc` convention were doing nothing —
  `AspNetCore.Web.ServiceModel.Tests` posts the envelope by hand for exactly that reason.

---

## Step 8 — Make the project look like a web project

Nothing to do — the package's `build/` assets nest code-behind, designer and adjacent resource files
under their markup automatically:

```
Default.aspx
  +- Default.aspx.cs
  +- Default.aspx.designer.cs
```

Applies to `.aspx`, `.ascx`, `.master`, `.ashx`, `.asmx` and `.asax`, in C# and VB, plus a `.resx`
sitting beside its markup. To adjust:

```xml
<WebFormsMarkupExtensions>aspx;ascx;master;ashx;asmx;asax;myext</WebFormsMarkupExtensions>
<WebFormsNestCodeBehind>false</WebFormsNestCodeBehind>
```

`App_LocalResources\*.resx` is deliberately left flat: `DependentUpon` resolves relative to the item's
own directory and ASP.NET keeps local resources in a separate folder, so nesting them would name a
parent that is not there.

---

## Step 9 — Deploy

```powershell
dotnet publish -c Release
```

The published directory *is* the application directory: `web.config`, your content tree and every
assembly side by side. That satisfies both the SDK and the port's classic-layout expectations.

Operational differences from IIS:

* **Process lifetime is yours.** No app-pool recycling, no auto-restart on `web.config` change. Use
  systemd, a Windows Service, or a container.
* **One application per process.** No shared app pools.
* **Put a reverse proxy in front** (nginx, YARP, IIS as a pure proxy) for TLS termination, request
  limits and connection management — Kestrel's defaults are not IIS's, and `<httpRuntime>` limits are
  not enforced.
* **Sessions are in-process unless you configured otherwise.** With `mode="InProc"` they do not survive
  a restart and are not shared: one instance, or sticky routing. `StateServer` and `SQLServer` remove
  that constraint — see step 5.
* Set `<machineKey>` explicitly if there is more than one instance.
* **Give each instance its own `TemporaryFilesPath`** if two processes ever host the same application
  directory. The compiled-page cache is keyed on that path, not on the process.

---

## Quick reference

| Task | Command |
|---|---|
| Reference the port | `<PackageReference Include="AspNetCore.Web.Hosting.Kestrel" Version="1.0.0" />` |
| Build the port from source | `dotnet build AspNetCore.Web.slnx` |
| Run the port's tests | `dotnet test AspNetCore.Web.slnx` |
| Convert a project automatically | `dotnet run --project Tools/port-project -- <app-dir>` (add `--apply`) |
| Diagnose configuration | `dotnet run --project Tools/verify-config -- <app-dir>` |
| Reference apps | `Samples/WebFormsSample`, `WebFormsSampleVB`, `MvcSample`, `WebPagesSample`, `WebApiSample`, `WcfSample`, `SessionStateSample`, `DynamicDataSample` |

| Question | Document |
|---|---|
| What will not work? | [LIMITATIONS.md](LIMITATIONS.md) |
| How is the port built? | [README.md](README.md) |
| Why is the assembly called `Core.Web`? | `Build/WebFormsPort.targets` |
| Which limitations are being removed? | [LIMITATIONS-PLAN.md](LIMITATIONS-PLAN.md) |
| Is this upstream member actually implemented? | [MONOTODO.md](MONOTODO.md) |
