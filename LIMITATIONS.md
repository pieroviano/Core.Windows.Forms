# Limitations

Where this port differs from ASP.NET on .NET Framework / IIS.

Everything here is a deliberate design decision or a verified consequence of running on CoreCLR — not
a to-do list. Each entry says what differs, when you find out, and what to do instead. Entries are
grouped by how likely they are to affect a real migration.

Everything targets **net10.0** and is strong-named. Which of these limitations are being removed, and
which are inherent, is triaged in [LIMITATIONS-PLAN.md](LIMITATIONS-PLAN.md).

This file covers what the **port** changed. What **upstream** left unfinished is a separate and much
larger surface - Mono's tree carries members that compile, are callable and do nothing - and it is
inventoried per assembly in [MONOTODO.md](MONOTODO.md), generated so it cannot drift.

---

## 1. What is usable today

| Stack | State |
|---|---|
| **WebForms** (`.aspx`, `.ascx`, `.master`, `.ashx`) | Working, C# and VB |
| **`.asmx` web services** (SOAP + JSON) | Working, including `?wsdl` and the help page |
| **AJAX / `ScriptManager` / `UpdatePanel`** | Working, including the partial-rendering delta protocol |
| **ASP.NET MVC 4** | Working — routing, controllers, Razor views, model binding, `TempData`, async actions |
| **Attribute routing** (`[Route]`) | Working — inline constraints, precedence, named routes. See §2 |
| **Bundling** (`System.Web.Optimization`) | Working, **without minification**. See §2 |
| **Async views** (`@await`) | Working. See §2 |
| **Razor views and Web Pages** | Working — layouts, sections, `@model`, `@Html.*`, standalone `.cshtml` |
| **ASP.NET Web API** | Working — routing, `ApiController`, content negotiation, JSON and XML binding |
| **WCF** (`.svc`) | Working, on CoreWCF — with the caveats in §2 |
| **Out-of-process session state** | Working — `StateServer` on `IDistributedCache`, `SQLServer` on ASPState. See §2 |
| **Arbitrary objects in `Session`** | Working — `ObjectGraphStateSerializer`, allow-listed. See §2 |
| **Dynamic Data** | Working, on `IQueryable`/EF Core instead of LINQ to SQL. See §2 |
| **`LinqDataSource`** | Working, on `IQueryable`/EF Core instead of LINQ to SQL. See §2 |
| **`System.Web.Mail`** | Working — Mono's managed SMTP, unchanged |
| **Mobile controls** (`System.Web.Mobile`) | **Not ported, and not planned.** See §4 |
| **.NET Remoting** (`.rem`, `.soap`) | **Not portable.** See §2 |

All of it is covered by 445 tests, against C# and VB applications, over HTTP and through a real browser.

MVC is version **4**, Razor is **v2** and Web Pages is **2** — the generation the aspnetwebstack
submodule carries, consistently across all three. If you find a reference anywhere to MVC 3 or Razor v1,
it is wrong: the compile list is `Build/System.Web.Mvc4.sources`, and Razor and Web Pages are built from
aspnetwebstack.

What MVC 4 does not have, and this port does not add: view components, tag helpers, and MVC 5's
filter overrides. Attribute routing, bundling and `@await` were all MVC 5-or-later features and are
supplied by this port rather than by upstream — see §2 for what that costs.

---

## 2. The stacks that needed a substitution

Several pieces of the ASP.NET surface could not be ported the way everything else was — either because
what they depend on is not a library that could be recompiled, or because upstream never shipped them
in the generation this port is built from. Most are provided anyway, by something other than the
original; one has no path forward at all. Every claim here was checked against net10.0 rather than
assumed.

Read this section before planning a cutover. Each substitution is transparent to your *code* and
visible in your *operations* — and in two of the three cases it decides whether existing data survives
the move.

### .NET Remoting is not portable at all

`System.Runtime.Remoting.Proxies.RealProxy`, `System.Runtime.Remoting.Channels` and
`RemotingServices` do not exist on .NET 10 — compiling against any of them fails with `CS0234`.

That is not a missing library. Remoting's transparent proxies are a **CLR feature**: the runtime
manufactures a `__TransparentProxy` whose every member dispatch is intercepted and forwarded. CoreCLR
has no such machinery. Mono's `System.Runtime.Remoting` sources exist in the tree, but porting them
would produce an assembly with nothing underneath it — the hooks it compiles against are in the
runtime, not in a reference.

So `.rem` and `.soap` endpoints have no path forward here. Re-expose that surface as HTTP: a Web API
`ApiController` is the closest equivalent and is available now.

### `.svc` endpoints are served, but by CoreWCF rather than a ported `System.ServiceModel`

`.svc` files work: `Core.Web.ServiceModel` reads each one's `@ServiceHost` directive, resolves the type
it names, and registers it **at the address the file's own path implies**, so existing endpoint URLs
and contracts survive. `?wsdl` and `?singleWsdl` serve, and services coexist with `.aspx` pages in one
application on one port. `Samples/WcfSample` is the worked example.

What is *underneath* is not Mono's WCF. Mono's `System.ServiceModel` is **912 source files**, and its
`LIB_REFS` pull in `System.Runtime.Serialization`, `System.IdentityModel` and
`System.ServiceModel.Internals`; `.svc` activation needs `System.ServiceModel.Activation` on top. That
is larger than every assembly ported here combined, and it is a different kind of work — a transport
and channel stack rather than a request pipeline. **CoreWCF** is the supported server-side WCF for
.NET and already does that job, so the port supplies the one thing CoreWCF deliberately does not have:
the ASP.NET `.svc` convention.

That choice has consequences, and they are the reason this sits in §2 rather than §1:

| | Consequence |
|---|---|
| **Attributes** | `[ServiceContract]`, `[OperationContract]`, `[DataContract]` come from `CoreWCF`, not `System.ServiceModel`. Your service code changes by a `using` line; the SOAP on the wire does not. |
| **`<system.serviceModel>` is not read** | Bindings, behaviours, quotas and endpoint configuration in `web.config` are ignored entirely — no error, they simply do nothing. Re-express them on `SvcEndpointOptions.Binding` or through CoreWCF. |
| **Custom `ServiceHostFactory`** | Not supported. CoreWCF builds the host itself, so a `Factory=` attribute is rejected at startup with a message naming the file rather than silently ignored. |
| **Bindings** | Whatever CoreWCF implements. `BasicHttpBinding` is the default and is what an `.svc` over `http://` almost always was; `NetTcpBinding`, WS-* and MSMQ availability follow CoreWCF's, not `System.ServiceModel`'s. |
| **Client side** | Out of scope here. `System.ServiceModel.Primitives` on NuGet is the client stack; this port only hosts. |
| **Discovery is once, at startup** | The `.svc` scan runs during `AddSvcEndpoints`. A `.svc` file dropped into the directory later is not picked up until the process restarts — unlike IIS, which activated on first request. |

A `.svc` whose `Service=` type cannot be resolved throws at **startup** by default rather than failing
on first call: discovery only happens once, so a silently skipped service would look exactly like one
that is merely not being called yet. `ThrowOnUnresolvedService` turns that off.

### Attribute routing, bundling and `@await` are this port's, not upstream's

All three arrived in ASP.NET after the generation the aspnetwebstack submodule carries, so there was
nothing to port — they are written here. They behave as the originals do; what differs is underneath,
and in one case that has a cost worth knowing before you rely on it.

**`[Route]` / `[RoutePrefix]`** — an MVC 5 feature. `MapMvcAttributeRoutes ()` scans the application's
controllers and turns every attribute into an ordinary `System.Web.Routing.Route` whose defaults pin
`controller` and `action`. Everything downstream — action selection, model binding, filters, link
generation — is then the stock MVC pipeline, so an attribute route behaves exactly as a conventionally
mapped one does. Inline constraints (`{id:int}`, `{n:range(1,10)}`, `{s:alpha:minlength(3)}`, `regex`,
catch-alls, inline defaults), route names, `Order`, and `~/` to escape the prefix all work.

Two differences from MVC 5:

* **Call order matters.** MVC 5 keeps attribute routes in a separate collection that is always
  consulted first. Here they are ordinary routes, so `MapMvcAttributeRoutes ()` must be called
  **before** your conventional routes — `{controller}/{action}/{id}` registered first would swallow
  everything behind it.
* **There is no "direct route" concept.** MVC 5 records extra state on a matched attribute route and
  runs a second action-selection pass over it. Nothing outside MVC 5's own internals observes that,
  and nothing here reproduces it.

The MVC 5 rule that *a controller with attribute routes is no longer reachable through conventional
routes* **is** reproduced — without it `/catalog/search/ab` would still reach `Search` through
`{controller}/{action}/{id}` and quietly bypass the `minlength(3)` the attribute declared. It is
enforced during action selection rather than during routing, which is the only place available here.

**Bundling** — `System.Web.Optimization` was a separate package that never shipped in this tree, and
it minified through WebGrease, which is .NET Framework only and long dead. The API is reproduced
faithfully enough that an existing `App_Start/BundleConfig.cs` and existing `@Scripts.Render` /
`@Styles.Render` calls compile and run unchanged, including wildcard and `{version}` includes, CDN
paths, and content-hash cache busting.

**Bundles are concatenated, not minified.** That is a deliberate choice: a substitute minifier that
mangles one edge case in someone's JavaScript is far worse than a bundle that is merely larger, and
the failure would be silent and intermittent. If you need minified output, minify at build time and
point the bundle at the result, or use `CdnPath`.

Serving also differs mechanically: the original installs an `IHttpModule` through a
`PreApplicationStartMethod`; here each bundle registers a route. The observable behaviour is the same
— no `web.config` edit, the URL works once `RegisterBundles` has run — with one wrinkle handled for
you: bundle routes are inserted at the *front* of the route table so a conventional
`{controller}/{action}/{id}` cannot claim `/bundles/app`, and they refuse to generate outgoing URLs so
`Html.ActionLink` cannot accidentally resolve to one.

**`@await`** — classic ASP.NET never supported it in any version. Two things were missing and both are
supplied: Razor v2's implicit-expression parser stopped at the keyword (so `@await Foo ()` rendered the
literal text `" Foo ()"` — no error, just wrong output), and the generated `Execute ()` was a
synchronous `void`. Views that contain an `await` now compile to an `async` method with a small
synchronous `Execute ()` that runs it.

The cost, stated plainly: **that bridge blocks.** `Execute ()` waits on the task. There is no deadlock
risk — the classic one needs a `SynchronizationContext` to marshal the continuation back, and ASP.NET
Core installs none — but the request's thread is held while the view's awaits complete, so a view
awaiting slow I/O occupies a thread-pool thread it would not occupy under ASP.NET Core's own view
engine. The alternative was making the whole render path async, which the port's pipeline deliberately
is not (§5). Views with no `await` are left exactly as Razor generated them and pay nothing.

---

### Arbitrary objects in `Session` work, behind an allow-list

`mode="InProc"` never wrote a session down, so an application could keep anything in it. Out-of-process
modes have to serialize every value, and the types that break are precisely the ones JSON cannot
express: private fields, object cycles, shared references, and a field declared as a base type holding
a derived one.

`ObjectGraphStateSerializer` handles all four, with `BinaryFormatter`'s semantics — it walks instance
fields, public and private, inherited included; tracks references so a cycle terminates and a shared
object stays shared; and records the concrete runtime type. `[NonSerialized]` is honoured.

```csharp
app.UseWebForms (o => o.StateSerializer = new ObjectGraphStateSerializer {
    AllowedTypes = { typeof (Basket), typeof (BasketLine) },
});
```

**The allow-list is not optional decoration.** `BinaryFormatter` was removed from .NET because
deserialising an attacker-influenced payload can construct arbitrary types and run their code, and a
session store is exactly such a payload once it lives in Redis or a database something else can write
to. So nothing is reconstructed unless its type was allowed:

| | |
|---|---|
| `AllowedTypes` | the types you keep in `Session` |
| `AllowedNamespaces` | a whole namespace, e.g. `"MyApp.Models"` |
| `AllowAnyType` | no check at all — only when the store cannot be written by anything but this application |

Primitives, `string`, `DateTime`, `Guid`, `decimal` and the BCL collections are always allowed: they
have no fields to populate and no behaviour to trigger, and an allow-list that also demanded `int` and
`List<T>` is one nobody would keep accurate. **Generic arguments are checked too** —
`List<Evil>` is a single type name, so checking only the outer `List` would let a payload name anything
it liked as the element type.

Deliberately unsupported: `[OnDeserializing]`/`ISerializable` (hooks for running code during
deserialization, which is the thing being defended against), delegates, and runtime handles like
`Stream`. Those fail with a message naming the field.

### Dynamic Data and `LinqDataSource` work, on `IQueryable` rather than LINQ to SQL

Both were listed as not ported for one reason: they were built on **LINQ to SQL**, which does not exist
on .NET and is not coming. Neither actually needs it.

**Dynamic Data** reads its model through an abstract `DataModelProvider` / `TableProvider` /
`ColumnProvider` triple; upstream ships exactly one implementation of it, over LINQ to SQL. Replacing
the triple with `QueryableDataModelProvider` — which reads any object with `IQueryable` or
`IEnumerable` members — leaves the scaffolding, routing, field templates and metadata attributes
working unchanged. An EF Core `DbContext` is such an object, and so is a class holding `List<T>`.

`ScaffoldTableAttribute`, which .NET Core's `System.ComponentModel.Annotations` dropped, is declared
again in its original namespace so `[ScaffoldTable(true)]` keeps compiling.

**`LinqDataSource`** is written fresh rather than ported. Upstream's view is built *around* LINQ to
SQL's change tracking — `ITable`, `DataContext` and the attach/submit model appear in fifteen places —
so compiling it would have meant faking a change-tracking model that EF Core expresses completely
differently. The markup surface is reproduced against `IQueryable` instead: `ContextTypeName`,
`TableName`, `Where`, `OrderBy`, `Select`, the parameter collections, `AutoPage`/`AutoSort`,
`AutoGenerateWhereClause`, and `EnableInsert`/`Update`/`Delete` all behave as they did.

Two things follow from the substitution:

* **Change tracking is by convention.** `Add`/`Remove`/`Update` then `SaveChanges`, found by reflection
  on the collection or the context. That is EF Core's shape, a repository's, and most hand-rolled units
  of work. Nothing references EF Core — the port must not force a database stack on an application that
  only wants a `GridView`. A context with none of those methods fails with a message naming what was
  looked for.
* **`StoreOriginalValuesInViewState` optimistic concurrency is gone.** It was defined in terms of a
  `DataContext`.

The `Where` syntax is a small expression language: comparison, logical and arithmetic operators, member
paths, literals and `@parameters`. It supports **no method calls** — an expression language in markup
that can invoke methods is one an attacker reaches through anything that writes markup — and parameter
values are captured as constants, never re-parsed, so a value containing `|| 1 == 1` is a value.

#### Field templates need a `FieldTemplates/` folder, and it is not optional

Scaffolding renders a column through a **field template**: an `.ascx` under
`DynamicData/FieldTemplates/`, chosen by the column's type. `Text.ascx` for a string, `Boolean.ascx`
for a `bool`, `Text_Edit.ascx` when the row is being edited, and so on down a fallback chain (`int`,
`decimal`, `Guid`, `DateTime` and `TimeSpan` all end at `Text`).

Those files are **content in your application**, not in the package - Visual Studio's project templates
put them there, and a package cannot, because the point of them is that you change them. If the folder
is missing the factory finds no template, the cell renders empty and nothing is logged. That is
upstream behaviour, and it is the first thing to check when a scaffolded page comes back blank.

`Samples/DynamicDataSample/DynamicData/FieldTemplates/` has a working set of four to copy.

Two members remain unimplemented, both because `QueryableDataModelProvider` reads `IQueryable` members
rather than a relational schema and so produces no association metadata: `ForeignKey` and `Children`
templates have nothing to bind to, and `PopulateListControl`, `ExtractForeignKey`,
`FindOtherFieldTemplate` and `LoadWithForeignKeys` throw rather than invent a relationship.

### `System.Web.Mail` is ported as-is

Obsolete since .NET 2.0, and a ported application did not choose that. Mono's implementation is pure
managed SMTP with no CDO or COM anywhere, so it compiles on CoreCLR unchanged and every existing
`SmtpMail.Send` call site keeps working. Set the port through the CDO field
`http://schemas.microsoft.com/cdo/configuration/smtpserverport`, as it always was — `SmtpServer` is a
host name, not `host:port`.

New code should still use `System.Net.Mail`.

### Out-of-process session state works, but neither store is the one the words used to mean

`<sessionState mode="StateServer">` and `mode="SQLServer"` both work now, and the `web.config` does not
change. What changed is what sits behind each of them, and in one case that is a data-migration
question rather than a configuration one.

Mono's implementations could not be used. Its StateServer handler is built on .NET Remoting —
`Activator.GetObject` against a `MarshalByRefObject` — the same CLR feature that makes `.rem`
impossible above, and it was never wire-compatible with Microsoft's `aspnet_state.exe` anyway. Its SQL
handler targets a Mono-invented `Sessions` table through a `Mono.Data.Sqlite` factory this port
excludes, so a migrating application's existing database does not fit it.

**`mode="StateServer"` is an `IDistributedCache`.** You register one — `AddStackExchangeRedisCache`,
`AddDistributedSqlServerCache`, or `AddDistributedMemoryCache` for a single instance — and call
`app.UseWebFormsSessionState ()` before `UseWebForms`. `aspnet_state.exe` is not spoken and
`stateConnectionString` is ignored; it is left in place because removing it is a `web.config` edit for
no benefit, but nothing reads it. Speaking the real State Server protocol was considered and rejected:
the payload inside it is `BinaryFormatter`, which .NET 9+ cannot read, so it would have bought a
Windows-only dependency on a service Microsoft no longer develops and *still* not shared a session with
a .NET Framework application.

**`mode="SQLServer"` is the stock ASPState database.** The same tables and the same stored procedures
`aspnet_regsql.exe` provisioned — `TempGetStateItemExclusive3`, `TempUpdateStateItem*`,
`TempReleaseStateItemExclusive` — so an existing database should need no DDL at all. `aspnet_regsql.exe`
is .NET Framework only, so for a *new* deployment the script ships inside the assembly:
`SqlSessionStateStore.GetSchemaScript ()`, or `EnsureSchema (connectionString)` to run it.

One honest caveat about that "should": the test suite exercises the provider against a database built
from the shipped script, not against one built by `aspnet_regsql.exe` — that tool cannot run here, so
there is no way to produce the comparison in CI. The procedure names and parameter signatures are the
documented ones and the shipped script is deliberately written to match them, but if you are pointing
at an inherited ASPState database, verify against a copy before you cut over.

Four things to know before relying on either:

| | |
|---|---|
| **Existing session data does not carry across** | Sessions written by .NET Framework contain `BinaryFormatter` payloads and .NET 9+ has no `BinaryFormatter` to read them. You can reuse the Redis instance or the ASPState database — the *infrastructure* — but not the rows already in it. Give the ported application its own key prefix or its own database, and treat the cutover as a session flush. |
| **Anything in `Session` must now serialize** | `InProc` never wrote a session down, so a type that has worked for years can fail the first time the mode changes, with no other edit. The failure is loud and names both the offending type and the mode. See §5. |
| **StateServer locking is best-effort** | `IDistributedCache` has `Get` and `Set` and no compare-and-swap, so taking the session lock is read-modify-write. Two requests for the *same* session id arriving simultaneously can both believe they hold it; the result is last-writer-wins, not corruption. `mode="SQLServer"` has no such window — there the lock is a row update inside a stored procedure. |
| **`Session_End` never fires** | Both stores return `false` from `SetItemExpireCallback`. A distributed cache drops the key with nobody to notify, and the ASPState sweeper is a scheduled job rather than a callback. Only `InProc` could ever raise it; returning `false` is the contract's way of saying so, and the module then skips the event rather than pretending it fired. |

`mode="Custom"` is untouched and remains the right answer for a store neither of these covers. Mono's
`SessionSQLServerHandler` is still compiled and can be named that way if its portable `Sessions` table
is what you actually want.

---

## 3. Deployment differences

### There is no AppDomain, and no application recycling

The AppDomain hosting family is out of scope (`Tools/port-exclusions.txt`: `AppDomainFactory`,
`ApplicationManager`, `ISAPIRuntime`, `IISAPIRuntime`). `WebFormsRuntimeHost` sets `.appPath`,
`.appVPath`, `.appId` and friends directly on the single AppDomain the process has.

* **One application per process.** `WebFormsRuntimeHost.Initialize` is idempotent and silently returns
  on a second call. Run two processes to host two applications.
* **No shadow copying and no auto-restart on file change.** Editing `web.config` or dropping a new
  assembly does not recycle the app — restart the process. `.aspx`/`.cshtml` edits *are* picked up,
  because those are compiled per file at request time.
* **No `<processModel>`, no IIS app-pool recycling.** Process lifetime belongs to your host.
* Shutdown runs through `IHostApplicationLifetime.ApplicationStopping` → `WebFormsRuntimeHost.Shutdown`.
  `AppDomain.DomainUnload` never fires.

### Compiled pages are cached per application path, not per process

Generated sources and compiled page assemblies go to a directory keyed on the application's physical
path, so a restart reuses what it compiled last time. Two processes hosting the **same** directory
share that directory and will corrupt each other's output. Fine in production, where an application
directory belongs to one process; side-by-side instances and parallel test suites must each set their
own:

```csharp
options.TemporaryFilesPath = "/var/cache/myapp/instance-1";
```

MVC and Web API also cache their **controller-type scan** to that directory
(`MVC-ControllerTypeCache.xml`). A cache written before `<compilation><assemblies>` listed your
assembly is never re-scanned, so "the controller for path '/…' was not found" survives the fix until
that directory is cleared.

### Assemblies are renamed, and signed with this port's own key

The port ships `Core.Web`, `Core.Configuration`, `Core.Web.Services`, `Core.Web.Extensions`,
`Core.Web.Razor`, `Core.Web.WebPages`, `Core.Web.Mvc`, `Core.Web.Http` and friends. It cannot be
called `System.Web`: .NET ships an **empty** `System.Web.dll` facade in `Microsoft.NETCore.App`, and
the host gives the shared framework precedence for assemblies it owns, so an app-local
`System.Web.dll` is never loaded.

* **Namespaces are unchanged**, so `.aspx`, `.cshtml`, code-behind, controllers and `Inherits=` are
  unaffected.
* **Configuration and markup naming an assembly must be updated.** `assembly="System.Web"` binds to
  the empty facade and silently drops the registration.
* **Strong-named with `SignKey.snk`** (public key token `21eedd2f354dfe57`, set centrally in
  `Directory.Build.props`). That gives the port its own identity; it does **not** let it impersonate
  Microsoft's. Third-party code compiled against `System.Web, PublicKeyToken=b03f5f7f11d50a3a` still
  cannot bind: .NET relaxes version when binding but never the public key.
* Every consuming project needs `Build/WebFormsPort.targets`, or the empty facades collide with the
  port: `CS0433` at build, `TypeLoadException` at the first request. A `PackageReference` gets it
  automatically — it ships in the `Core.Web.Hosting.Kestrel` package's `build/` folder.

### Assembly discovery follows the classic layout

The runtime loads every assembly in the application's `bin` directory at startup, and installs a
resolver that probes `<app>/bin` and then the host's own output directory. That covers a classic
deployment, and the normal case where the application assembly is the host's entry assembly.

It does **not** cover the case where the application directory and the running assemblies are
different places — the SDK builds to `bin/Debug/net10.0`, which is not the `bin/` the probe expects.
`Inherits="MyApp.Page"` names no assembly, so it is answered by scanning *already loaded* assemblies.
Name such assemblies explicitly:

```csharp
options.ApplicationAssemblies = new [] { typeof (MyApp.SomePage).Assembly };
```

MVC and Web API are separate: they discover controllers through
`BuildManager.GetReferencedAssemblies()`, which is `<compilation><assemblies>` — so your own assembly
must be listed there.

### `<system.webServer>` and IIS-native features do nothing

There is no IIS. The port reads `<system.web>`; it does not read `<system.webServer>`.

| IIS feature | Status |
|---|---|
| IIS-native modules and handlers | Not run. Re-express `<system.webServer><modules>` as `<system.web><httpModules>`. |
| URL Rewrite module | Not present. Use ASP.NET Core middleware ahead of `UseWebForms()`. |
| Static files, compression, caching headers | Kestrel's. Put `UseStaticFiles()` **before** `UseWebForms()`. |
| Request limits, connection limits, queueing | Kestrel's, not `<httpRuntime>`'s. |
| `applicationHost.config`, virtual directories, app pools | Nothing equivalent. |

Windows / Negotiate authentication is **not** in this table — see §5.

---

## 4. Features that are not ported

| Feature | What is missing | When it fails |
|---|---|---|
| **.NET Remoting** (`.rem`, `.soap`) | see §2 | no handler; the paths are not served |
| **Mobile controls** | `System.Web.Mobile` | tag prefix stops resolving; page fails to parse. See below |
| **LINQ to SQL** | `System.Data.Linq` does not exist on .NET | a `DataContext` cannot be used. `LinqDataSource` and Dynamic Data work on `IQueryable` instead — §2. `Linq.Binary` model binding is dropped |
| **Entity data source** | `System.Web.Entity` | tag prefix stops resolving |
| **Sqlite membership/profile/role providers** | Mono's `Mono.Data.Sqlite` providers | provider not found at startup. **SQL Server providers are kept.** |
| **COM+ / `System.EnterpriseServices`** | shimmed as an enum only | `PlatformNotSupportedException` |

### Mobile controls are not ported, and will not be

The other entries in this section are things that *could* be ported and were not worth it. This one is
different: **there is nothing to port.** Mono's `System.Web.Mobile` assembly is a stub — its entire
source manifest is `Consts.cs` and `AssemblyInfo.cs`. Mono never implemented it.

Supporting `MobilePage`, `MobileControl`, `Form`, `ObjectList` and the device adapters would therefore
mean writing them from scratch, from documentation, for a technology Microsoft deprecated in 2005 and
removed from the project templates long before that. What they did — emit WML and cHTML for feature
phones, and switch rendering per device from a `.browser` capabilities file — has no audience left;
every device that can reach an ASP.NET application today renders ordinary HTML.

If you have mobile pages, the migration is to rewrite them as ordinary `.aspx`. Nothing else in this
document is a rewrite; this one is, and pretending otherwise would waste your time.

### `.asmx` serves, but does not generate client proxies

Serving SOAP and JSON `[WebMethod]` endpoints works — including `?wsdl`, the help page, and
`{"d":...}` page methods. What is absent is the WSDL/DISCO **code generation** half: .NET Core never
got the XML serialization codegen story. Hosting a service is fine; `wsdl.exe` / "Add Web Reference"
*on the server* is not. Generate client proxies on .NET Framework, or at build time.

---

## 5. Runtime behaviour

### Authentication comes from ASP.NET Core, and must be opted into

Windows/Negotiate, OIDC, JWT and every other ASP.NET Core scheme work. The middleware runs before
`UseWebForms()` and produces a `ClaimsPrincipal`; the port assigns it to System.Web's
`HttpContext.User` during `AuthenticateRequest`, so `<authorization>`, `User.Identity.Name` and role
checks all see the caller.

```csharp
app.UseAuthentication ();          // BEFORE UseWebForms, or there is no result to flow
app.UseWebForms (options => {
    options.UseAspNetCoreAuthentication = true;
});
```

**Off by default**, deliberately: an application using forms authentication must not have its
principal replaced from underneath it. When on, the principal is assigned only when ASP.NET Core
actually authenticated the caller — an unauthenticated request stays anonymous and `<deny users="?">`
still denies it.

`WebFormsRuntimeHost.CurrentCoreContext` exposes the ASP.NET Core `HttpContext` for the current
request, for reaching `RequestServices`, connection info and features from inside application code.

### `BinaryFormatter` is gone — view state and session refuse unknown types

`BinaryFormatter` was disabled in .NET 8 and removed in .NET 9. `Port/StateSerializer.cs` replaces it:

* **Primitive-element arrays** are serialised natively and losslessly. Not an edge case —
  `ClientScriptManager`'s event-validation state is a primitive array, so this is on every page with
  `<form runat="server">`.
* **Everything else** is **refused with a diagnostic naming the type**, rather than re-encoded under a
  scheme with different round-trip behaviour.

```csharp
options.StateSerializer = new System.Web.JsonStateObjectSerializer ();   // or your own
```

`JsonStateObjectSerializer` is JSON, not binary: public data round-trips; private fields, cycles and
type identity do not. Affected paths: `ObjectStateFormatter`, `AltSerialization`,
`SerializationHelper`, output cache.

### The request pipeline is synchronous inside, async at the edge

`HttpRuntime.BeginProcessRequest` runs the `HttpApplication` pipeline synchronously. The middleware
uses the async Begin/End entry so no thread is pinned across a yield, and the request body is buffered
up front so Kestrel's synchronous-IO guard is never tripped on the way in.

**The response streams.** `Response.Flush ()` writes through to the client, `TransmitFile` and
`Response.WriteFile` stream in 64 KB chunks rather than materialising the file, and anything the
application does not explicitly flush is written once when the pipeline returns. Progressive rendering,
server-sent events and long row-by-row reports all work, and a large download costs one chunk of memory
rather than its own size.

Two consequences follow, and both match IIS rather than differing from it:

* **Flushing commits the headers.** Once anything has gone to the client the status line and headers
  are on the wire, so a later `Response.AppendHeader` throws `HttpException: Headers have been already
  sent`. Code in `EndRequest` that sets a header is the usual place this shows up — it only ever
  appeared safe because nothing had been sent yet. Guard it with `Response.HeadersWritten`, which this
  port adds because Mono lacked it.
* **Synchronous IO is enabled for the duration of each flush.** Narrowly, through
  `IHttpBodyControlFeature`, and restored afterwards — not switched on for the server.

* Transfer framing belongs to Kestrel. `GATEWAY_INTERFACE` is reported as `CGI/1.1` so that
  `HttpResponse` does not also write chunk headers into the body — without it a `TransmitFile`
  response arrives corrupted by its own framing.
* Throughput is lower than a native ASP.NET Core app. A deliberate lift-and-shift tradeoff, and the
  first thing to revisit (`WebFormsMiddleware.cs`).
* **Admission control and queueing belong to Kestrel.** `<httpRuntime>`'s `appRequestQueueLimit` and
  `executionTimeout` are not enforced as IIS enforced them.
* Async modules, `IHttpAsyncHandler` and `Page.RegisterAsyncTask` do work.

### Request validation is on, and it fails the request

Posting `<script>` in a form value is refused before your page code runs — the framework behaviour,
preserved deliberately. If you relied on `validateRequest="false"`, that setting still applies.

### Mono's implementation, except Razor, MVC and Web API

`System.Web` and `System.Configuration` are Mono's. Undocumented corner behaviour, exact rendering
whitespace, error-message text and obscure control edge cases can differ from Microsoft's. The port is
written against **Mono's configuration semantics** specifically, which is why `Core.Configuration`
exists and the shipping `System.Configuration.ConfigurationManager` cannot substitute for it.

Razor, WebPages, MVC and Web API are **Microsoft's own ASP.NET Web Stack source**, carried as the
nested `Mono/external/aspnetwebstack` submodule; Json.NET has a third nested submodule.
**`git submodule update --init --recursive` is required** or the generator silently drops every path
and those assemblies build empty rather than failing.

One exception to compiling upstream in place: `System.Net.Http.Formatting`'s manifest vendors a
decade-old Json.NET that does not compile on .NET 10 (`AppDomain.DefineDynamicAssembly`, CAS
permissions). The port excludes it and references the supported `Newtonsoft.Json` package instead,
which also means your application's Newtonsoft types are the same types the formatter produces.

### Configuration resolved by path

`WebConfigurationManager.GetSection (name, path)` resolves the hierarchy for the DIRECTORY containing
the path. Upstream passed the caller's path straight to `OpenWebConfiguration`, which only recognises
a directory written with a trailing slash — so `"/Secure/"` found the sub-directory's `web.config`
while `"/Secure"`, `"~/Secure"` and `"/Secure/Secret.aspx"` silently fell back to the application
root's, and the answer could change depending on what the process had already been asked. The port
normalises the path once for the whole method (`Port/ConfigPathNormalizer.cs`) and no longer answers a
path-scoped query from the path-less cache entry.

Consequence: a `<location path="Some/File.aspx">` is no longer matched by a path-scoped `GetSection`,
because the path is reduced to its directory first. Location sections still apply normally during a
request, which is how they are almost always used.

### Machine-level configuration

`machine.config` and the root `web.config` are generated by `Tools/gen-config.ps1`, embedded in
`Core.Web`, and extracted to a temp directory at first configuration read. There is no shared machine
configuration on the box. A host can supply its own:

```csharp
options.MachineConfigPath = "/etc/myapp/machine.config";
```

A **replacement, not an overlay** — a complete `machine.config`, with the root `web.config` beside it,
because that is what maps `*.aspx`. Validated at startup, because a malformed one disables every
configuration section at once.

`<machineKey>` defaults per process. For multi-instance deployments set an explicit `<machineKey>`, or
forms-auth tickets and view state MACs will not validate across instances.

### Third-party libraries reading `ConfigurationManager`

Libraries compiled against `System.Configuration.ConfigurationManager` do see your `web.config`, via
`Core.Web.ConfigBridge` — but only `appSettings` and `connectionStrings`. Any other section returns
`null`, because handing back a section object from the ported implementation would raise
`InvalidCastException` inside the library's own code.

### Runtime intrinsics and platform substitutions

Mono implemented some members in the runtime's C code (`MethodImplOptions.InternalCall`). CoreCLR has
no such entry points, and merely *loading* a type declaring one throws `SecurityException`. These are
replaced with managed equivalents (`Overrides/System.Web.Util__ICalls.cs`).

Three more substitutions worth knowing:

* `AppDomainHelper` inspected an application's `bin` in a child AppDomain. It now uses a **collectible
  `AssemblyLoadContext`** — the CoreCLR equivalent, which keeps the isolation upstream wanted rather
  than loading the application's `bin` permanently.
* `CryptoUtil` walked a Windows CNG → CAPI ladder for SHA-256. `SHA256.Create()` lets the platform
  choose, FIPS included.
* `BuildManager.GetType` compared assembly references against the full display name — version, culture
  and public key token — so an ordinary `type="X, Y"` never matched. Upstream's own comment on that
  line reads *"So dumb..."*. The port also matches on the simple name, which is how it binds
  everywhere else.

Performance counters are shimmed — none are published. Design-time types exist only so control
metadata compiles.

### An error that says "not found" often means "did not compile"

`BuildManager.GetObjectFactory` **swallows the compile exception and returns null**, and callers
translate that null into "not found". So a `.cshtml` that fails to compile is reported as a view that
does not exist, while `VirtualPathProvider.FileExists` happily returns true for the same path. When a
view or page is reported missing and the file is plainly there, ask for the real error:

```csharp
BuildManager.GetCompiledType ("~/Views/Home/Index.cshtml");
```

---

## 6. Known unknowns

* **A clean build can fail on its first pass** with `NU5026` (package produced before the assembly
  exists) when `GeneratePackageOnBuild` races project ordering. It has not reproduced across three
  consecutive clean builds since patched output moved out of `obj/`, so it may already be resolved; it
  is recorded rather than deleted because the cause was never established.

---

## 7. What is verified to work

All covered by the 445 tests in `Tests/`, against C# and VB applications, over HTTP and through a real
browser:

**WebForms** — page compilation (C# and VB) through the Roslyn backend · code-behind via `Inherits=` ·
master pages · user controls · `App_Code` compiled at runtime ·
`App_GlobalResources`/`App_LocalResources` · themes and skins · view state round-tripping including
custom types · postbacks, control events and lifecycle ordering · server-side validation ·
`GridView`/`Repeater`/`ListView`/`FormView`/`DetailsView`/`DataList`/`Menu`/`TreeView` · in-proc
session and application state · `global.asax` handlers wired by name · custom `IHttpModule` and
`IHttpHandler` · forms authentication and directory authorization · `Response.Redirect` and
`Server.Transfer` · output caching · request validation · culture and localization.

**Services** — `.asmx` SOAP and `?wsdl` · JSON page methods · `ScriptManager` and `UpdatePanel` partial
rendering including the delta protocol · `ScriptResource.axd`, and a malformed resource request
rejected with 404.

**MVC and Razor** — routing, controller discovery and action results · Razor view rendering with
layouts, sections, strongly typed models and `@Html` helpers · route-parameter and form model binding ·
`TempData` across a redirect · automatic HTML encoding.

**Web Pages** — standalone `.cshtml`, extensionless URLs and the default document · per-file routing ·
underscore-prefixed files refused · layouts and the `Page` dictionary · self-posting forms.

**Attribute routing** — `[RoutePrefix]` + `[Route]`, empty templates, literal-beats-parameter
precedence, `int`/`alpha`/`minlength`/`range`/`regex` constraints (composed and singly), inline
defaults, catch-alls, verb constraints sharing one template, named-route URL round-tripping, `~/`
prefix escape, and conventional routes still working alongside · the template parser and every
constraint as units, including a colon inside a default and commas inside `regex(...)`.

**Bundling** — script and style tags rendered with content-hash URLs · bundles served concatenated in
include order with the right content type · CSS not joined with semicolons · wildcard includes ·
`Scripts.Url` agreeing with the rendered tag · stable hashes across requests · cacheability · bundle
routes not hijacking `Html.ActionLink`.

**Async views** — `@await` in an implicit expression, inside a code block, and with member access
after the keyword · the awaited expression not leaking as literal text · the word "await" inside
literal text leaving the view untransformed · views without `await` unaffected.

**Web API** — routing and `ApiController` · verb and route action selection · content negotiation to
JSON and XML · request-body model binding from both · `HttpResponseException` mapped to its status.

**WCF** — `.svc` served at the path the file sits at, including from a sub-directory · SOAP 1.1
request and response · contract on an interface or on the service class · `?wsdl` and `?singleWsdl`,
advertising the address the request arrived on · `.aspx` in the same application still served ·
directive parsing, case-insensitive attributes, `Factory=` detection, `bin/`+`obj/` skipped.

**Session state** — `mode="StateServer"` end to end over HTTP, including proof that the
`IDistributedCache` really is the backing store (deleting the entry empties the session) · session
identity and isolation between clients · `Abandon` and `Remove` · `Int32`/`DateTime` surviving with
their types · a 20 KB value round-tripping byte for byte · the unserializable-value diagnostic naming
the mode. `mode="SQLServer"` against a real ASPState database — insert, read, exclusive locking and
lock-cookie enforcement, release, the `SessionItemLong` path past 7000 bytes, a shrinking session
clearing the long column, the uninitialized-item flag consumed exactly once, and per-application row
keying.

**Dynamic Data and `LinqDataSource`** — the model provider's conventions, the query parser, change
tracking and the state serializer's allow-list as units; and, over HTTP against
`Samples/DynamicDataSample`, a scaffolded list page rendering for two unrelated tables from one
template · the model excluding a `string` member · a generated key and a computed property not being
scaffolded, while a `[DatabaseGenerated(None)]` key is · a details page addressing a row by a key that
is never rendered · a non-numeric key refused rather than throwing · route constraints holding ·
`LinqDataSource` filtering, ordering and parameter binding through a `GridView`, a parameter value
staying a value rather than becoming expression text, and edit and cancel round-tripping the grid's
view state.

Two bugs were found by the first page that suite rendered, and neither was reachable from a unit test:
`DynamicDataRouteHandler` never published the request's `MetaTable`, and `StateManagedCollection` saved
its state as a `Triplet` of `List<T>`, which the port's formatter cannot encode. Both are fixed; both
are why this section distinguishes "unit-tested" from "served".

**Hosting and configuration** — ASP.NET Core authentication bridged into `HttpContext.User` ·
host-supplied `machine.config` · path-scoped configuration lookup · third-party `ConfigurationManager`
reads · ported-assembly deployment and facade displacement · `HttpForbiddenHandler` on `.cs`/`.config`.
