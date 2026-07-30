# README.md

This file provides guidance when working with code in this repository.

## What this repository is

A port of **the ASP.NET stack — WebForms, MVC 4, Razor, Web Pages and Web API — to .NET 10**, plus
`.svc` (WCF) hosting on CoreWCF and out-of-process session state, hosted under Kestrel instead of
IIS/`AppDomain`. Almost none of the source lives here: the upstream Mono tree is the `Mono/` git
submodule — plus `Mono/external/aspnetwebstack` nested inside it, which is where the Razor sources
live, so `git submodule update --init --recursive` is required — and its files are **compiled in
place, never copied**, so the port stays diffable against
upstream. What this repo actually contains is the machinery that makes that tree compile and run on
CoreCLR.

`Mono/` is upstream and read-only — never edit files under it.

## Commands

```powershell
dotnet build AspNetCore.Web.slnx          # whole port (~35s)
dotnet build AspNetCore.Web/AspNetCore.Web.csproj

# Regenerate the compile lists from Mono's .sources manifests. Run after ANY change to
# Tools/port-exclusions.txt, Tools/port-patches.txt, or an Overrides/ directory.
powershell -ExecutionPolicy Bypass -File Tools/gen-sources.ps1

# Regenerate the embedded machine.config / root-web.config from Mono/data/net_4_5
powershell -ExecutionPolicy Bypass -File Tools/gen-config.ps1

# Regenerate MONOTODO.md, the inventory of upstream members that compile and do nothing.
powershell -ExecutionPolicy Bypass -File Tools/gen-monotodo-inventory.ps1
# Report packages on the local feed that no project produces any more (add -Delete to remove them).
powershell -ExecutionPolicy Bypass -File Tools/prune-feed.ps1
# Fail if any code written for this port (Port/, Overrides/, Shims/) produces a compiler warning.
powershell -ExecutionPolicy Bypass -File Tools/lint-port-code.ps1

# Regenerate MONOTODO.md, the inventory of upstream members that compile and do nothing.
powershell -ExecutionPolicy Bypass -File Tools/gen-monotodo-inventory.ps1

# Diagnostics
dotnet run --project Tools/verify-config              # defaults to Samples/WebFormsSample
dotnet run --project Tools/verify-config -- <app-dir> # boot config + dump the FULL exception chain

# Run a sample app (C# and VB WebForms)
dotnet run --project Samples/WebFormsSample
dotnet run --project Samples/WebFormsSampleVB

# Tests (445 across fifteen projects, ~100s)
dotnet test AspNetCore.Web.slnx
dotnet test Tests/AspNetCore.Web.FunctionalTests
dotnet test Tests/AspNetCore.Web.FunctionalTests --filter "FullyQualifiedName~PostbackTests"
dotnet test Tests/AspNetCore.Web.FunctionalTests --filter "Server_side_validation_blocks_the_click_handler"
```

## Tests

Fifteen projects under `Tests/`, and the split between them is not cosmetic — **the ported runtime hosts
one application per process**. `WebFormsRuntimeHost.Initialize` writes `.appPath` as AppDomain data
and returns early on any later call, so two applications in one process would silently share the
first one's physical path, configuration and compiled-page cache. `dotnet test` runs each project in
its own process, which is the isolation that buys.

| Project | Covers | Why separate |
|---|---|---|
| `AspNetCore.Web.FunctionalTests` | Core.Web, Hosting.Kestrel, Extensions, Services — via `Samples/WebFormsSample` | hosts the C# application |
| `AspNetCore.Web.FunctionalTests.VB` | the VB compilation path — via `Samples/WebFormsSampleVB` | hosts a *different* application |
| `AspNetCore.Configuration.Tests` | Core.Configuration, machine.config extraction, gen-config retargeting | needs a cold configuration system |
| `AspNetCore.Web.ConfigBridge.Tests` | the packaged `ConfigurationManager` bridge | `SetConfigurationSystem` is one-shot per process |
| `AspNetCore.Web.HostingTests` | ASP.NET Core auth bridge, host-supplied `machine.config` | both are set before `Initialize` and cannot change after |
| `AspNetCore.Web.BrowserTests` | real Chromium via Playwright (headed by default) | drives the sample as a user would |
| `AspNetCore.Web.Razor.Tests` | `Core.Web.Razor` parser and code generator | no server, no runtime init at all |
| `AspNetCore.Web.Mvc.BrowserTests` | MVC routing and Razor views via Playwright, plus HTTP suites for attribute routing, bundling and `@await` | hosts `Samples/MvcSample` |
| `AspNetCore.Web.WebPages.Tests` | standalone `.cshtml` routing and rendering | hosts `Samples/WebPagesSample` |
| `AspNetCore.Web.Http.Tests` | Web API routing, negotiation and model binding | hosts `Samples/WebApiSample` |
| `AspNetCore.Web.ServiceModel.Tests` | `.svc` (WCF) discovery and SOAP hosting on CoreWCF | hosts `Samples/WcfSample`; the `.svc` scan runs once per process |
| `AspNetCore.Web.SessionState.Tests` | out-of-proc session: `StateServer` over HTTP, `SQLServer` against ASPState | hosts `Samples/SessionStateSample`; `SessionStateHostServices` freezes on first use |
| `AspNetCore.Web.LegacyStacks.Tests` | System.Web.Mail over a real SMTP socket, the object-graph state serializer, LinqDataSource and Dynamic Data on IQueryable | hosts no application |
| `AspNetCore.Web.LegacyStacks.HttpTests` | Dynamic Data scaffolding and `LinqDataSource` through a `GridView`, over real HTTP | hosts `Samples/DynamicDataSample` |
| `AspNetCore.Web.PortTool.Tests` | `Tools/port-project` against hand-written legacy fixtures, including really running `dotnet build` on the converted output | hosts no application; needs the local package feed populated |

The functional suites start a **real Kestrel server** on a loopback port (`http://127.0.0.1:0`) and
drive it with a real `HttpClient`, rather than using `TestServer` — `AspNetCoreWorkerRequest` is
written against real chunked responses and Kestrel's synchronous-IO guard. Each test class joins one
xunit collection, which serialises them; session state, `Application["requests"]` and the page cache
are shared mutable state.

The applications under test are read from their **source** directories, not copies, so `MapPath` and
`PhysicalApplicationPath` assertions describe the real layout. Suites that host an application whose assembly is not the
entry assembly name it through `options.ApplicationAssemblies`, and each sets its own
`options.TemporaryFilesPath` — the default is keyed on the application *path*, so parallel suites
hosting the same sample would otherwise corrupt each other's generated pages. See
[LIMITATIONS.md](LIMITATIONS.md).

Test projects must import `Build/WebFormsPort.targets` like any other consumer.

## The shared staging feed

`Packages/` is not this repository's build output directory in the usual sense. **Every repository
in the tree links the same folder**, so they all publish into and restore from one place, and every
packable project here sets:

```xml
<PackageOutputPath>$(SolutionDir)Packages/</PackageOutputPath>
```

`NuGet.Config` also declares it as the `Local` source. So it is simultaneously where this port
publishes and where the tree restores from, and that is deliberate.

**Why.** A package should not have to go to nuget.org the moment it exists. Building here puts the new
version straight into the feed; a sibling repository that depends on it can be moved to that version,
built and tested against it immediately; and only once the whole set is green does any of it get
pushed. The alternative is publishing versions that turn out to be wrong and unlisting them afterwards.

**What follows from it.** Two things, and neither is a defect — they are the cost of the arrangement,
and both are easy to mistake for bugs:

* **It accumulates, and nothing prunes it.** A package id outlives the project that produced it, so a
  rename or a retirement leaves an artefact behind that still satisfies restores. Everything keeps
  working here and fails on a machine that has never built this port — the failure whoever caused it
  cannot reproduce. `Tests/AspNetCore.Web.PortTool.Tests` is the suite most exposed to it, since it
  restores generated projects against this feed. Run before a release:

  ```powershell
  powershell -ExecutionPolicy Bypass -File Tools/prune-feed.ps1          # report
  powershell -ExecutionPolicy Bypass -File Tools/prune-feed.ps1 -Delete  # remove
  ```

  It only ever considers `AspNetCore.*`. The feed holds other products' packages, which are none of
  this repository's business.

* **It is rewritten while it is read.** Each `.nupkg` is deleted and recreated on every build, so a
  test restoring from the feed can find a package briefly absent. `Tests/AspNetCore.Web.PortTool.Tests`
  copies what it needs into a private feed first, retrying while the build settles — which is why that
  suite has a retry loop that would otherwise look like superstition.

**Publishing.** When the set is green, push from this folder. Nothing in the build does it for you, and
nothing should: the whole point is that the push is a separate, deliberate step.

## Assemblies are renamed, namespaces are not

| Project directory | Assembly | Namespace root |
|---|---|---|
| `AspNetCore.Web` | `Core.Web` | `System.Web` |
| `AspNetCore.Configuration` | `Core.Configuration` | `System.Configuration` |
| `AspNetCore.Web.Services` | `Core.Web.Services` | `System.Web.Services` |
| `AspNetCore.Web.Extensions` | `Core.Web.Extensions` | `System.Web.Extensions` |
| `AspNetCore.Web.Hosting.Kestrel` | `Core.Web.Hosting.Kestrel` | `System.Web.Hosting.Kestrel` |
| `AspNetCore.Web.ConfigBridge` | `Core.Web.ConfigBridge` | `System.Web.Configuration.Bridge` |
| `AspNetCore.Web.Razor` | `Core.Web.Razor` | `System.Web.Razor` |
| `AspNetCore.Web.Optimization` | `Core.Web.Optimization` | `System.Web.Optimization` |
| `AspNetCore.Web.ServiceModel` | `Core.Web.ServiceModel` | `System.Web.ServiceModel` |
| `AspNetCore.Web.SessionState` | `Core.Web.SessionState` | `System.Web.SessionState` |
| `AspNetCore.Web.DynamicData` | `Core.Web.DynamicData` | `System.Web.DynamicData` |

.NET ships **empty** `System.Web.dll` and `System.Configuration.dll` facades in
`Microsoft.NETCore.App`, and the host gives the shared framework precedence for assemblies it owns —
an app-local `System.Web.dll` is never loaded. Naming the port `System.Web` compiles and then dies at
startup with `FileNotFoundException`. Hence `Core.*`. Namespaces are unchanged, so application code
and generated page classes are unaffected.

Two directory-vs-assembly-name traps:
- `Tools/gen-sources.ps1` and `gen-config.ps1` key on the **directory** name (`AspNetCore.Web`). Getting
  it wrong silently creates a new directory and leaves the real project building against a stale props file.
- Config files must name the **assembly** (`Core.Web`), which is what `gen-config.ps1`'s
  `Retarget-PortAssembly` does.

`Build/WebFormsPort.targets` strips the overlapping framework references and must be imported by
**every** project that references `Core.Web`, directly or transitively — including consumer apps.
It also asserts at build time that `Core.Web.dll` reached the output directory, because the failure
mode otherwise is a `TypeLoadException` on the first request.

## How upstream sources reach the compiler

`Tools/gen-sources.ps1` reads Mono's `.dll.sources` manifests and writes
`<project>/Sources.generated.props` (a `<Compile Include>` list — **generated, never hand-edit**).
Each upstream file goes down exactly one of four paths:

1. **Compiled in place** from `Mono/mcs/class/...` — the default.
2. **Excluded** — a substring match in `Tools/port-exclusions.txt` drops it (a trailing `/` drops a
   directory). Used for out-of-scope features: `System.Web.Mail`, AppDomain/ISAPI hosting, out-of-proc
   session state, WSDL/DISCO codegen, Sqlite providers.
3. **Patched** — a rule in `Tools/port-patches.txt` (3 tab-separated fields:
   `path-filter <TAB> regex <TAB> replacement`) produces a copy in `<project>/patched/` which is
   compiled instead. **Patches must preserve line count** — the generator throws otherwise — so
   upstream line numbers stay valid in stack traces. `patched/` is committed, not under `obj/`:
   `Sources.generated.props` is committed and names those files, so under `obj/` a fresh clone would
   fail with `CS2001` until someone ran the generator. A patch rule matching zero files is a build
   error (it's almost always a broken regex; note the Mono sources are CRLF, so a trailing `$` needs
   to allow for `\r`).
4. **Overridden** — `<project>/Overrides/<flattened>.cs` shadows the upstream file entirely. Flattened
   = upstream path with leading `../` stripped and `/` → `__`, e.g. `System.Web/HttpRuntime.cs`
   becomes `Overrides/System.Web__HttpRuntime.cs`.

Choose the lightest mechanism that works: patch for mechanical/repetitive edits, override only when a
file needs real surgery (there are currently four in `AspNetCore.Web/Overrides/`).

Other per-project directories: `Port/` (new code written for this port), `Shims/` (stand-ins for
framework types that no longer exist), `Generated/` (`Consts.cs`, emitted from Mono's `Consts.cs.in`).

## The configuration story

This is the most intricate part of the port, and the source of the most confusing failures.

- The ported `System.Web` is written against **Mono's** `System.Configuration` semantics (config-path
  model, `SaveStart`/`SaveEnd`, `FindLocationConfiguration`, friend access to `protected internal`
  members). The shipping `System.Configuration.ConfigurationManager` package cannot substitute — it
  rejects Mono's `WebConfigurationHost` outright. Hence `Core.Configuration`. Dropping that
  ProjectReference produces ~2000 `CS0246` errors.
- The `ConfigurationManager` package still arrives transitively (via `System.Data.SqlClient`,
  `System.Data.OleDb`, etc.) and its types collide by name with `Core.Configuration`'s. `WebFormsPort.targets`
  removes its **compile** assets while keeping it **deployed**, because third-party libraries were
  compiled against its strong-named identity.
- `AspNetCore.Web.ConfigBridge` then installs an `IInternalConfigSystem` into that package at startup,
  so a third-party library reading `ConfigurationManager.AppSettings` sees `web.config`. It is the
  **only** project compiled against the package's config types, and everything crossing its boundary
  is a primitive or shared framework type so the two `System.Configuration.*` type sets never meet in
  one signature. `Tools/thirdparty-probe` exists to prove this works.
- Type-forwarding facades and `AssemblyLoadContext.Resolving` hooks were both tried and are both
  impossible; the reasons are recorded in `Build/WebFormsPort.targets` — don't re-attempt them.
- `machine.config` / `root-web.config` are generated by `Tools/gen-config.ps1`, embedded as resources
  in `Core.Web`, and extracted at runtime by `AspNetCore.Web/Port/MachineConfig.cs`. Edit the
  **generator**, not the generated files. The root `web.config` is what maps `*.aspx` →
  `PageHandlerFactory`; without it nothing is served.

## Hosting

`app.UseWebForms(...)` (`AspNetCore.Web.Hosting.Kestrel/WebFormsApplicationBuilderExtensions.cs`) is the
public entry point. `WebFormsRuntimeHost` replaces `ApplicationHost.CreateApplicationHost`: there is
one AppDomain, so it sets `.appPath` and friends as AppDomain data directly and `HttpRuntime` reads
them back unmodified. `AspNetCoreWorkerRequest` adapts `HttpWorkerRequest` onto ASP.NET Core's
`HttpContext`. Put `UseStaticFiles()` *before* `UseWebForms()` rather than filtering requests.

Note the namespace trap in that project: it is nested under `System.Web`, so an unqualified
`HttpContext` binds to `System.Web.HttpContext`. ASP.NET Core's is reached via the
`AspNetCoreHttpContext` alias.

## Notable replacements

- **Compilation**: `CodeDomProvider`'s *compile* half throws `PlatformNotSupportedException` on .NET
  Core, so `AspNetCore.Web/Port/RoslynCompiler.cs` supplies it (`CodeCompileUnit` → generated `.cs` →
  `CSharpCompilation` → `Assembly`). The codegen half is untouched and works. Both C# and VB pages
  compile.
- **`BinaryFormatter`** is gone on .NET 8+, so view state / session objects with no native encoding
  are **refused with a diagnostic naming the type** rather than silently re-encoded. Callers opt in via
  `WebFormsOptions.StateSerializer` (`JsonStateObjectSerializer`, or their own `IStateObjectSerializer`).
- **`ICalls`** — Mono's `InternalCall` methods have no CoreCLR entry point, and merely *loading* such a
  type throws `SecurityException`. Overridden with managed equivalents.
- **`App_Code`** is compiled at runtime by `BuildManager`, so consumer projects must
  `<Compile Remove="App_Code\**\*.cs" />` and ship it as content instead.

## Conventions

- Every project targets **net10.0** with `LangVersion latest`. The upstream tree was written for
  Mono-era C# and long pinned `7.3`, but .NET 10 forces the issue: `ICustomTypeDescriptor` gained
  default interface members, and implementing an interface that has them requires C# 8 or later. The
  full suite passes at `latest`, so the old warning about overload resolution was theoretical.
  `WarningLevel 0` throughout; the upstream tree is not warning-clean and never will be. That
  setting is per project, so it covers `Port/`, `Overrides/` and `Shims/` too — code written for
  this port, which *is* clean. `Tools/lint-port-code.ps1` rebuilds with warnings restored and
  fails on any warning in those directories, so it stays that way. That
  setting is per project, so it covers `Port/`, `Overrides/` and `Shims/` too — code written for
  this port, which *is* clean. `Tools/lint-port-code.ps1` rebuilds with warnings restored and
  fails on any warning in those directories, so it stays that way.
- Mono's brace-and-space style (`Method (arg)`) is used throughout, including in the new `Port/` code.
- Comments here carry the *why*, at length, including approaches that were tried and failed. That is
  deliberate — preserve and extend it rather than trimming.
- Consumer web projects need `EnableDefaultContentItems=false` and must exclude `bin\**;obj\**` from
  every content glob, or each build copies the site one directory deeper until `Rebuild` fails
  with MSB3030.
