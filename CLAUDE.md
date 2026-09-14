# CLAUDE.md

Guidance for working in this repository.

## What this is

A port of the ASP.NET stack — **WebForms, MVC 4, Razor, Web Pages, Web API**, plus `.svc` (WCF) on
CoreWCF, out-of-proc session state and .NET Remoting/AppDomain hosting — to modern .NET, hosted under Kestrel
instead of IIS/`AppDomain`.

- Upstream source is the `mono/` submodule (and its nested `mono/external/aspnetwebstack`, which holds
  MVC/Razor/Web Pages/Web API). Clone with `git submodule update --init --recursive`.
- Upstream files are **compiled in place, never copied**, so the port stays diffable. This repo holds only
  the machinery that makes that tree build and run on CoreCLR.
- **Never edit anything under `mono/`.**

## Commands

```powershell
dotnet build AspNetCore.Web.slnx                     # whole port
dotnet build AspNetCore.Web/AspNetCore.Web.csproj

# Generators — edit the generator/inputs, never the outputs
powershell -ExecutionPolicy Bypass -File Tools/gen-sources.ps1            # after ANY change to port-exclusions.txt, port-patches.txt, Overrides/, Build/*.sources
powershell -ExecutionPolicy Bypass -File Tools/gen-config.ps1             # embedded machine.config / root-web.config from mono/data/net_4_5
powershell -ExecutionPolicy Bypass -File Tools/gen-monotodo-inventory.ps1 # MONOTODO.md

# Hygiene
powershell -ExecutionPolicy Bypass -File Tools/lint-port-code.ps1         # fails on any warning in Port/, Overrides/, Shims/
powershell -ExecutionPolicy Bypass -File Tools/prune-feed.ps1 [-Delete]   # stale AspNetCore.* packages in Packages/

# Diagnostics / tools
dotnet run --project Tools/verify-config [-- <app-dir>]     # boot config, dump full exception chain (default: Samples/WebFormsSample)
dotnet run --project Tools/port-project -- <app> [--apply]  # convert a legacy app; previews unless --apply
dotnet run --project Samples/WebFormsSample                 # also WebFormsSampleVB, MvcSample, WebApiSample, WebPagesSample, WcfSample, SessionStateSample, DynamicDataSample, RemotingSample
dotnet run --project Tools/state-server [-- --port 42424]    # remote StateServer for UseWebFormsRemoteStateServer

# Tests
dotnet test AspNetCore.Web.slnx
dotnet test Tests/AspNetCore.Web.FunctionalTests --filter "FullyQualifiedName~PostbackTests"
```

## Projects

| Directory | Assembly | Namespace root | TFMs | Upstream |
|---|---|---|---|---|
| `AspNetCore.Web` | `Core.Web` | `System.Web` | 6/8/10 | mono |
| `AspNetCore.Configuration` | `Core.Configuration` | `System.Configuration` | 6/8/10 | mono |
| `AspNetCore.Web.Extensions` | `Core.Web.Extensions` | `System.Web` | 6/8/10 | mono |
| `AspNetCore.Web.Services` | `Core.Web.Services` | `System.Web.Services` | 6/8/10 | mono (referencesource) |
| `AspNetCore.Web.DynamicData` | `Core.Web.DynamicData` | `System.Web.DynamicData` | 6/8/10 | mono |
| `AspNetCore.Web.Razor` | `Core.Web.Razor` | `System.Web.Razor` | 6/8/10 | aspnetwebstack |
| `AspNetCore.Web.WebPages` (+`.Razor`, `.Deployment`) | `Core.Web.WebPages*` | `System.Web.WebPages*` | 6/8/10 | aspnetwebstack |
| `AspNetCore.Web.Mvc` | `Core.Web.Mvc` | `System.Web.Mvc` | **10 only** | aspnetwebstack (MVC 4, via `Build/System.Web.Mvc4.sources`) |
| `AspNetCore.Web.Http` (+`.WebHost`) | `Core.Web.Http*` | `System.Web.Http*` | 6/8/10 | aspnetwebstack |
| `AspNetCore.Net.Http.Formatting` | `Core.Net.Http.Formatting` | `System.Net.Http.Formatting` | 6/8/10 | aspnetwebstack |
| `AspNetCore.Web.Infrastructure` | `Core.Web.Infrastructure` | `Microsoft.Web.Infrastructure` | 6/8/10 | aspnetwebstack |
| `AspNetCore.Web.Optimization` | `Core.Web.Optimization` | `System.Web.Optimization` | 6/8/10 | port-authored |
| `AspNetCore.Web.Hosting.Kestrel` | `Core.Web.Hosting.Kestrel` | `System.Web.Hosting.Kestrel` | 6/8/10 | port-authored |
| `AspNetCore.Web.ConfigBridge` | `Core.Web.ConfigBridge` | `System.Web.Configuration.Bridge` | 6/8/10 | port-authored |
| `AspNetCore.Web.ServiceModel` | `Core.Web.ServiceModel` | `System.Web.ServiceModel` | 6/8/10 | port-authored (CoreWCF) |
| `AspNetCore.Web.SessionState` | `Core.Web.SessionState` | `System.Web.SessionState` | **10 only** | port-authored |
| `AspNetCore.Web.Remoting` | `Core.Web.Remoting` | `System.Web` | 6/8/10 | mono (`Build/System.Web.Remoting.sources`) + port-authored, on the Net4x.Runtime.Remoting packages |

Samples, tests and tools target `net10.0`.

**Why `Core.*`:** .NET ships empty `System.Web.dll` / `System.Configuration.dll` facades and the shared
framework wins over app-local copies — an assembly named `System.Web` compiles, then fails at startup with
`FileNotFoundException`. Namespaces are unchanged.

Name traps:
- `gen-sources.ps1` / `gen-config.ps1` key on the **directory** name; a wrong name silently creates a new
  directory and leaves the real project on a stale props file.
- Config files name the **assembly** (`Core.Web`) — `gen-config.ps1`'s `Retarget-PortAssembly` does this.

Packages: ids are `Net4x.AspNet<AssemblyName>` (`Core.Web` and `Core.Web.WebPages` add `.Base`;
`Core.Web.Http` is `Net4x.AspNetCore.Web.Http.Base`; Mvc and SessionState lack the `Net4x.` prefix).
Every project packs on build.

## Build infrastructure

| File | Role |
|---|---|
| `Directory.Build.props` | `SolutionDir` fallback (unset on `.slnx`/single-project builds → package lands in the wrong folder, NU1101 downstream); strong-naming with `SignKey.snk` (InternalsVisibleTo grants depend on it — `$(WebFormsPortPublicKey)`); `VersionPrefix`/`VersionSuffix` (`yyDDD`); `PortLicenseExpression` (MIT default) |
| `Directory.Build.targets` | Applies `PortLicenseExpression` last (Net4x.NuGetUtility would force Apache-2.0); removes solution README from packs (NU5118) |
| `Directory.Nuget.Props` | Package versions, incl. `NuGetUtilityVersion` (unset → "'.' is not a valid version string") |
| `Build/WebFormsPort.targets` | Strips overlapping framework refs; hides `ConfigurationManager` compile assets but keeps it deployed; asserts `Core.Web.dll` reached output. **Import in every project referencing `Core.Web`, directly or transitively** — consumers and tests too |
| `<project>/Directory.Build.Props/.Targets` (Http, ServiceModel, Hosting.Kestrel) | Chain to the root files and import Net4x.NuGetUtility |

aspnetwebstack-based packages override the licence to `Apache-2.0 AND MIT`; `THIRD-PARTY-NOTICES.md` ships
in every package.

## How upstream sources reach the compiler

`Tools/gen-sources.ps1` reads Mono `.dll.sources` manifests (or `Build/*.sources`) and writes
`<project>/Sources.generated.props` — **generated, committed, never hand-edited**. Each file takes one path:

| Path | Mechanism | Notes |
|---|---|---|
| Compiled in place | default | from `mono/mcs/class/...` or `mono/external/aspnetwebstack/src/...` |
| Excluded | substring in `Tools/port-exclusions.txt` (trailing `/` = directory) | ISAPI hosting, WSDL/DISCO importers, Sqlite providers, `external/Newtonsoft.Json/` |
| Patched | `Tools/port-patches.txt`: `path-filter<TAB>regex<TAB>replacement` → `<project>/patched/` | **Must preserve line count** (generator throws). Zero-match rule = error (usually a broken regex; sources are CRLF, so `$` must allow `\r`). `patched/` is committed |
| Overridden | `<project>/Overrides/<flattened>.cs` | leading `../` stripped, `/` → `__` (e.g. `Overrides/System.Web__HttpRuntime.cs`) |

Use the lightest mechanism: patch for mechanical edits, override only for real surgery.

Other per-project dirs: `Port/` (new code), `Shims/` (stand-ins for removed framework types), `Generated/`
(`Consts.cs` etc.), `Config/` (`Core.Web` embedded config).

## Configuration

The most intricate part and the source of the most confusing failures.

- Ported `System.Web` needs **Mono's** `System.Configuration` semantics (config paths, `SaveStart`/`SaveEnd`,
  `FindLocationConfiguration`, friend access). The `ConfigurationManager` package rejects Mono's
  `WebConfigurationHost`, hence `Core.Configuration`. Dropping that reference → ~2000 `CS0246`.
- `ConfigurationManager` still arrives transitively (SqlClient, OleDb); `WebFormsPort.targets` hides its compile
  assets, keeps it deployed (third-party libs bind to its strong name).
- `AspNetCore.Web.ConfigBridge` installs an `IInternalConfigSystem` into that package so
  `ConfigurationManager.AppSettings` sees `web.config`. It is the **only** project compiled against the
  package's types; only primitives/shared types cross its boundary. Proven by `Tools/thirdparty-probe`.
- Type-forwarding facades and `AssemblyLoadContext.Resolving` hooks are impossible (reasons in
  `Build/WebFormsPort.targets`) — don't retry.
- `machine.config` / `root-web.config`: generated by `gen-config.ps1`, embedded in `Core.Web`, extracted by
  `AspNetCore.Web/Port/MachineConfig.cs`. Root `web.config` maps `*.aspx` → `PageHandlerFactory`; without it
  nothing is served.

## Hosting

- Entry point: `app.UseWebForms(...)` (`AspNetCore.Web.Hosting.Kestrel/WebFormsApplicationBuilderExtensions.cs`).
- `WebFormsRuntimeHost` replaces `ApplicationHost.CreateApplicationHost`: sets `.appPath` etc. as AppDomain
  data; `HttpRuntime` reads them unmodified. **One application per process** — later `Initialize` calls return
  early.
- `AspNetCoreWorkerRequest` adapts `HttpWorkerRequest` to ASP.NET Core `HttpContext`.
- `UseStaticFiles()` goes *before* `UseWebForms()`.
- Several applications: `app.UseWebFormsApplications(...)` (Core.Web.Remoting) — one child process per application
  (Net4x.AppDomain), requests buffered across, recycled via `Core.Web/Port/PortApplicationLifetime.cs`.
  `CreateApplicationHost`/`ApplicationManager` use the same child domains.
- The remoting library (`D:/CommonLibrary/Net4x.Runtime.Remoting`) is consumed from `Packages/` at a day-stamped
  version: after rebuilding it, delete `~/.nuget/packages/core.runtime.remoting`, `core.appdomain.library` and
  `core.appdomain.host`, or the stale same-version copy is restored.
- Namespace trap: the project lives under `System.Web`, so bare `HttpContext` is `System.Web.HttpContext`;
  use the `AspNetCoreHttpContext` alias for ASP.NET Core's.

## Notable replacements

- **Compilation** — `CodeDomProvider` compile throws on .NET Core; `AspNetCore.Web/Port/RoslynCompiler.cs`
  compiles the generated C#/VB. CodeDOM codegen is used as-is.
- **`BinaryFormatter`** gone — non-natively-encodable view state/session objects are **refused with a
  diagnostic naming the type**. Opt in via `WebFormsOptions.StateSerializer` (`JsonStateObjectSerializer` or a
  custom `IStateObjectSerializer`).
- **ICalls** — Mono `InternalCall` types throw `SecurityException` on load; overridden with managed code.
- **`App_Code`** — compiled at runtime by `BuildManager`; consumers `<Compile Remove="App_Code\**\*.cs" />`
  and ship it as content.

## Tests

Each project runs in its own process because the runtime hosts one application per process.

| Project | Covers | Why separate |
|---|---|---|
| `FunctionalTests` | Core.Web, Hosting.Kestrel, Extensions, Services via `Samples/WebFormsSample` | C# app |
| `FunctionalTests.VB` | VB compilation via `Samples/WebFormsSampleVB` | different app |
| `Configuration.Tests` | Core.Configuration, machine.config extraction, gen-config retargeting | cold config system |
| `ConfigBridge.Tests` | `ConfigurationManager` bridge | `SetConfigurationSystem` is one-shot |
| `HostingTests` | ASP.NET Core auth bridge, host-supplied `machine.config` | fixed before `Initialize` |
| `BrowserTests` | Playwright/Chromium (headed by default) on WebFormsSample | real browser |
| `Razor.Tests` | Razor parser/codegen | no server |
| `Mvc.BrowserTests` | MVC routing/views (Playwright) + attribute routing, bundling, `@await` | `Samples/MvcSample` |
| `WebPages.Tests` | standalone `.cshtml` | `Samples/WebPagesSample` |
| `Http.Tests` | Web API routing, negotiation, binding | `Samples/WebApiSample` |
| `ServiceModel.Tests` | `.svc` discovery, SOAP on CoreWCF | `Samples/WcfSample`; scan once per process |
| `SessionState.Tests` | `StateServer` over HTTP, `SQLServer` against ASPState | `Samples/SessionStateSample`; services freeze on first use |
| `Remoting.Tests` | `*.rem` from a child process, remote StateServer (`Tools/state-server`, port 42424), `CreateApplicationHost`, `ApplicationManager`, `UseWebFormsApplications` recycling | `Samples/RemotingSample`; other apps run in child processes |
| `LegacyStacks.Tests` | System.Web.Mail over SMTP, object-graph serializer, LinqDataSource/Dynamic Data on IQueryable | no app |
| `LegacyStacks.HttpTests` | Dynamic Data + `LinqDataSource` via `GridView` over HTTP | `Samples/DynamicDataSample` |
| `PortTool.Tests` | `Tools/port-project` on legacy fixtures, incl. real `dotnet build` of output | needs `Packages/` populated |
| `Samples.BrowserTests` | Headed Playwright over **all 8 samples**; WebForms events page by page (`Samples/WebFormsSample/Events/`) against reference-source semantics | launches each sample's built exe as a child process; `WEBFORMS_HEADLESS=1`, `WEBFORMS_SLOWMO=ms` |

(All prefixed `AspNetCore.Web.` except `AspNetCore.Configuration.Tests`.)

- Functional suites run a **real Kestrel** on `http://127.0.0.1:0` with `HttpClient`, not `TestServer`
  (worker request depends on real chunking and Kestrel's sync-IO guard).
- All classes in a project share one xunit collection (serialised): session, `Application["requests"]` and
  page cache are shared state.
- Apps are served from their **source** directories. Suites set `options.ApplicationAssemblies` when the app
  isn't the entry assembly, and their own `options.TemporaryFilesPath` (default is keyed on app path →
  parallel suites would corrupt generated pages).
- Test projects that reference `Core.Web` import `Build/WebFormsPort.targets`.
- Expected WebForms behaviour comes from Microsoft's reference source at
  `mono/mcs/class/referencesource/System.Web` — check it before encoding an expectation or fixing a divergence.

## Shared staging feed (`Packages/`)

- Shared by every repo under `D:\CommonLibrary`; `PackageOutputPath` = `$(SolutionDir)Packages/` and
  `Nuget.Config` source `Local`. Lets sibling repos build against new versions before anything is pushed.
- **Accumulates:** renamed/retired package ids keep satisfying restores locally and fail elsewhere. Run
  `prune-feed.ps1` before a release (only touches `AspNetCore.*`).
- **Rewritten while read:** `.nupkg`s are recreated each build; `PortTool.Tests` copies to a private feed with
  retries for this reason.
- Pushing is a manual, separate step.

## Conventions

- `LangVersion latest` (C# 8+ required: `ICustomTypeDescriptor` has default interface members). `Nullable`
  and `ImplicitUsings` disabled.
- `WarningLevel 0` per project (upstream is not warning-clean); port code (`Port/`, `Overrides/`, `Shims/`) is
  kept clean by `lint-port-code.ps1`.
- Mono style `Method (arg)` everywhere, including `Port/`.
- Code comments carry the *why* at length, incl. failed approaches — preserve and extend them.
- Consumer web projects: `EnableDefaultContentItems=false` and exclude `bin\**;obj\**` from content globs
  (otherwise nested copies until MSB3030).

## Docs

`README.md` (overview), `PORTING-GUIDE.md` (migrating an app), `LIMITATIONS.md` / `LIMITATIONS-PLAN.md`,
`MONOTODO.md` (generated), `REVIEW.md`, `THIRD-PARTY-NOTICES.md`, `Tools/port-project/README.md`.
