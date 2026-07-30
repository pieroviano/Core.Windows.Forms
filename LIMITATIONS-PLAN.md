# Limitations — triage

[LIMITATIONS.md](LIMITATIONS.md) says what differs from ASP.NET on IIS. This file says **which of those
differences are permanent and which are simply unfinished**, so a migration decision can tell the two
apart. Every entry in `LIMITATIONS.md` appears below exactly once, under one of three verdicts.

| Verdict | Meaning |
|---|---|
| **Inherent** | The platform, not the port. No amount of work in this repository removes it. |
| **Deliberate** | Removable, and deliberately not removed. The reason is stated; a different owner could decide otherwise. |
| **Candidate** | Would be removed by doing the work. What the work is, is stated. |

The useful question when reading a limitation is which of its two halves is doing the constraining:
the *feature*, or the *implementation the feature happened to use*. Almost everything in the
**Candidate** list is there because the implementation is replaceable while the public surface is not.

---

## Inherent — will not change

### There is no AppDomain

CoreCLR has one `AppDomain` and no unloading. Everything downstream follows: no application recycling,
no shadow copying, no `AppDomain.Unload` on a configuration change, no two applications in one process.
`WebFormsRuntimeHost.Initialize` returning early on a second call is not a shortcut — it is the only
honest behaviour available.

The deployment shape is process-per-application, which is what every ASP.NET Core deployment already
does.

### `BinaryFormatter` is gone

Removed from .NET for a reason that has not stopped being true: deserialising an attacker-influenced
payload constructs arbitrary types and runs their code, and a session store is exactly such a payload
once it lives in Redis or a database. `ObjectGraphStateSerializer` reproduces the *semantics* — private
fields, cycles, shared references, polymorphism — behind an allow-list, without reproducing the
vulnerability. `AllowAnyType` exists for a store nothing else can write to.

What does not come back is unconstrained round-tripping of any `[Serializable]` graph.

### LINQ to SQL

`System.Data.Linq` does not exist on .NET and is not coming, so a `DataContext` cannot be constructed.

Note what that does *not* constrain: `LinqDataSource` and Dynamic Data both run on `IQueryable`, because
what they needed was a queryable and a change-tracking convention rather than LINQ to SQL specifically.
The inherent part is `DataContext` itself and `Linq.Binary` model binding.

### WSDL and DISCO code generation

.NET Core never received the XML serialization codegen story that `wsdl.exe` and "Add Web Reference"
depend on. **Serving** `.asmx` — SOAP, JSON, `?wsdl`, the help page — works. Generating client proxies
on the server does not, and cannot be fixed from here. Generate proxies on .NET Framework, or at build
time.

### `<system.webServer>` and IIS-native features

Kestrel is not IIS. `<handlers>`, `<modules>`, `<rewrite>`, IIS compression and IIS static-file settings
are inert because the component that read them is absent. The `<system.web>` equivalents
(`<httpHandlers>`, `<httpModules>`) are honoured, which is why the conversion tool carries those across
and reports the rest.

### .NET Remoting

Removed from .NET, not ported by anyone, and superseded twice over — WCF, then gRPC. `.rem` and `.soap`
paths are not served. This is the one entry in `LIMITATIONS.md` whose migration is a redesign rather
than a port: re-expose the contract as Web API or gRPC.

### COM+ and `System.EnterpriseServices`

Shimmed as an enum so that code compiles; every call throws `PlatformNotSupportedException`. The COM+
catalog underneath is Windows-only unmanaged infrastructure with no CoreCLR surface.

### Assemblies carry this port's identity

.NET ships an empty `System.Web.dll` in `Microsoft.NETCore.App` and the host gives the shared framework
precedence for assemblies it owns, so an app-local `System.Web.dll` is never loaded. Hence `Core.Web`.
Namespaces are untouched, so application code and generated page classes are unaffected — but a
third-party control compiled against `System.Web, PublicKeyToken=b03f5f7f11d50a3a` cannot bind, because
.NET relaxes version on binding and never the public key.

---

## Deliberate — removable, not being removed

### Mobile controls (`System.Web.Mobile`)

The only entry here whose migration is a rewrite, and the reason is unusual: **there is nothing to
port.** Mono's assembly is a stub whose entire source manifest is `Consts.cs` and `AssemblyInfo.cs`.
Supporting `MobilePage`, `ObjectList` and the device adapters means writing them from documentation, for
a technology deprecated in 2005 that emitted WML for feature phones.

Reversible in principle. `Tools/port-project` blocks conversion on a `System.Web.Mobile` reference
rather than letting the discovery happen page by page.

### `System.Web.Entity` (`EntityDataSource`)

The same shape as `LinqDataSource`, and the same substitution would work — bind it to `IQueryable` over
an EF Core `DbContext`. It is not done because `LinqDataSource` already covers "declarative data source
over a queryable", and `EntityDataSource`'s distinguishing features (`CommandText` in Entity SQL,
`EntityKey`-based concurrency) are tied to the ObjectContext model EF Core dropped.

For an application that is `EntityDataSource`-heavy this is the most tractable item on the page:
`AspNetCore.Web.Extensions/Port/LinqDataSource*.cs` is the template.

### Sqlite membership, profile and role providers

Excluded in `Tools/port-exclusions.txt`. The SQL Server providers are kept and tested. Removing this
means taking on `Mono.Data.Sqlite` — superseded by `Microsoft.Data.Sqlite` — to serve a provider model
applications rarely combine with SQLite.

### Bundles are not minified

`System.Web.Optimization`'s minifiers were WebGrease, which is .NET Framework only and abandoned. The
port concatenates, hashes and serves. Taking a dependency on a JS/CSS minifier and silently changing
what your bundles emit is a worse default than doing less; `IBundleTransform` is the extension point
for an application that wants one.

### The pipeline is synchronous inside

`HttpApplication`'s execution steps, `Render`, and every `IHttpModule` in existence are synchronous. The
port is async at the Kestrel edge and synchronous within, which is why `@await` blocks a pooled thread
rather than yielding. Making the interior async means changing the shape of every upstream type — a
different project, not a fix to this one.

### `<httpRuntime>` throttling is inert

`appRequestQueueLimit`, `minFreeThreads` and `executionTimeout` are not enforced. Kestrel owns admission
control, and honouring both would mean queueing twice. Deliberate, and stated so at the patch site.

---

## Candidate — would be removed by doing the work

### Dynamic Data associations, and therefore foreign-key templates

`QueryableDataModelProvider` reads a context's `IQueryable` members, which is what makes Dynamic Data
work here at all. An `IQueryable<Product>` describes no relationships, so no association metadata is
produced: `ForeignKey.ascx` and `Children.ascx` have nothing to bind to, and `PopulateListControl`,
`ExtractForeignKey`, `FindOtherFieldTemplate` and `LoadWithForeignKeys` throw rather than invent a
relationship the model does not describe.

The work is inference from navigation properties: an `ICollection<T>` member plus a matching
`<Name>Id` scalar reconstructs most of what the LINQ to SQL provider read out of a schema. Ordinary work
against a functioning model. Worth pairing with a `MetaColumn`-level "this column has no usable
association" signal, so a page template can skip such a column instead of discovering it at render time.

### `StateServer` locking is best-effort

`IDistributedCache` has no compare-and-swap, so the payload and its lock share one entry and a racing
acquire has a window. Real `aspnet_state.exe` had a single-process lock table.

Removable by writing to a store that does have CAS — Redis `WATCH`/Lua, or a SQL row — behind the same
`SessionStateStoreProviderBase`. The `SQLServer` provider already locks correctly, which is the
recommendation when it matters.

### Sessions do not migrate between runtimes

`StateServer` and `SQLServer` here read and write *this port's* serialization rather than
`aspnet_state.exe`'s, so a .NET Framework farm cannot share a session store with a ported one and
cut-over is not gradual per-session.

Removable in principle by implementing the wire formats, which would mean re-implementing
`BinaryFormatter`'s payload format — the thing this port deliberately does not do. Listed as a candidate
rather than inherent because the obstacle is a decision, not the platform.

### Client-side `.asmx` proxy scripts

Distinct from WSDL codegen, and not inherent: the JavaScript proxy emitted at `Service.asmx/js` is
generated by the port itself, not by XML serialization codegen. Untested rather than known-broken, which
is the only reason it is here.

### `NU5026` on a clean first build

A package produced before its assembly exists, when `GeneratePackageOnBuild` races project ordering.
Recorded because the cause was never established rather than because it reproduces — it has not
reappeared since patched output moved out of `obj/`. Candidate for deletion from `LIMITATIONS.md` after
a few more clean builds, or for a fix if it returns.

---

## What to check before believing a limitation applies to you

`LIMITATIONS.md` covers what the **port** changed. Two neighbouring questions have their own documents,
and confusing them wastes the most time:

| Question | Document |
|---|---|
| What did the port change or remove? | [LIMITATIONS.md](LIMITATIONS.md) |
| Which of those is permanent? | this file |
| Is this upstream member implemented at all? | [MONOTODO.md](MONOTODO.md) |

The third is the one people miss. A member can be present, callable, and do nothing — 434 members carry
`[MonoTODO]`, and unlike the 922 that throw, those fail silently. When something returns `null` or
renders nothing on a page that reports HTTP 200, check there before assuming the limitation you are
reading about is the cause.
