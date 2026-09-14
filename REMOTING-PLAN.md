# Remoting support — plan

Implements the four remoting-dependent gaps on top of `D:\CommonLibrary\Net4x.Runtime.Remoting`
(packages `Core.Runtime.Remoting`, `Core.AppDomain.Library`, `Core.AppDomain.Host`).

## Blocking verifications (before coding)

| # | Check | How | Result |
|---|---|---|---|
| B1 | A process can list its loaded shared frameworks | `AppContext.GetData ("APP_CONTEXT_DEPS_FILES")` in a `Microsoft.NET.Sdk.Web` exe | **Verified** — `;`-separated deps.json paths, `shared/<name>/<version>/` per framework |
| B2 | `dotnet exec --runtimeconfig <generated> Net4x.AppDomain.Host.dll` starts the host with extra frameworks | run it by hand, expect `READY` | before L2 |
| B3 | Mono `System.Web.Caching.Cache` constructs outside a hosted app (state server process) | `new Cache ()` in a console exe referencing `Core.Web` | before W3 |
| B4 | `HttpListener` + `HttpClient` available on netstandard2.0 | build the library | before L1 |

## Decisions (user)

| Decision | Consequence |
|---|---|
| Scope: `.rem` endpoints, remote StateServer, `CreateApplicationHost`/`ApplicationManager`, multi-app + recycling | four work items, W1–W4 |
| HTTP channel added **to the remoting library** | changes in a second repo (L1); URLs `http://host/app/x.rem` survive |
| `PackageReference` to the library packages | versions in `Directory.Nuget.Props`; library must be packed to `Packages/` first |
| New project `AspNetCore.Web.Remoting` (`Core.Web.Remoting`) | `Core.Web` gains no dependency; reached by type name, like `Core.Web.SessionState` |

## Decisions (made here, generic rules)

| Decision | Why |
|---|---|
| Remote contracts are **public interfaces**, never the upstream internal classes | `ProxyFactory.Validate` rejects non-public contracts (a dynamic assembly cannot subclass them) |
| Unknown-scheme client channels auto-register (`tcp`, `ipc`, `http`) | .NET Framework delay-loaded client channels from machine.config; Mono's `SessionStateServerHandler` relies on it |
| Child domains inherit the parent's shared frameworks (default on) | a child with fewer frameworks cannot load the parent's types — same failure class as a too-old host |
| Remote StateServer is **opt-in** (`app.UseWebFormsRemoteStateServer ()`); `mode="StateServer"` default stays `IDistributedCache` | `web.config` unchanged; no hidden heuristic picks the store |
| `HttpRuntime.DoUnload` → overridable hook (default: current behaviour) | upstream's own file watchers (`bin`, `App_Code`, `web.config`, `Global.asax`) drive recycling in a child domain |
| Multi-app requests are **buffered** across the process boundary | remoting is request/response; no streaming, no WebSockets, no `UseAspNetCoreAuthentication` in a child (rejected at startup) |
| `ISAPIRuntime`/`IISAPIRuntime`/`ICustomLoader` stay excluded | IIS-native entry points; nothing on Kestrel calls them |
| SOAP formatter (`.soap`, `text/xml`) not provided | library has no SOAP serializer; request answered 415 naming the binary formatter |

## L — Net4x.Runtime.Remoting repo

| # | Change | Files |
|---|---|---|
| L1 | `HttpClientChannel`, `HttpServerChannel` (`HttpListener`, or hooked), `HttpChannel`; `IChannelReceiverHook` (`ChannelScheme`, `WantsToListen`, `AddHookChannelUri`, `ProcessHookedRequest (byte[], string requestPath) → byte[]`). Hooked dispatch rewrites `MethodCall.Uri` to the object uri relative to the hook uri's path | new `Channels/Http/HttpChannel.cs`; `Internal/Config/RemotingConfigFile.cs:133` (`ref="http"` registers it) |
| L2 | `AppDomainSetup2.InheritSharedFrameworks` (default `true`): generate `<temp>/<hash>.runtimeconfig.json` with the parent's frameworks, launch via muxer `exec --runtimeconfig` | `Net4x.AppDomain/Internal/DomainProcess.cs:38`, `HostLocator.cs`, new `Internal/SharedFrameworks.cs` |
| L3 | `ChannelServices.CreateMessageSink` registers a client channel for `tcp://`/`ipc://`/`http://` when none accepts the url | `Channels/ChannelServices.cs:71` |
| L4 | Tests: http channel end-to-end (listener + hooked), config `ref="http"`, auto client channel, framework inheritance | `Net4x.Runtime.Remoting.Tests`, `Net4x.AppDomain.Tests` |
| L5 | Docs: `README.md`, `docs/STATUS.md`, `docs/deviations.md` | |

## W — AspNetCore.Web repo

### W0 — project `AspNetCore.Web.Remoting`

- `Core.Web.Remoting`, namespace root `System.Web`, TFMs 6/8/10 (library packages cover net8/net10 + netstandard2.0 → net6 via netstandard). Package id `Net4x.AspNetCore.Web.Remoting`.
- Refs: `Core.Web`, `Core.Web.Hosting.Kestrel`, `Core.Runtime.Remoting`, `Core.AppDomain.Library`, `Core.AppDomain.Host`; imports `Build/WebFormsPort.targets`.
- `Build/System.Web.Remoting.sources` → `gen-sources.ps1` → `Sources.generated.props`.
- `Core.Web` `Generated/PortAssemblyInfo.cs`: `InternalsVisibleTo ("Core.Web.Remoting, PublicKey=…")`.
- Add to `AspNetCore.Web.slnx`; CLAUDE.md project table.

### W1 — `.rem` endpoints

| Change | Where |
|---|---|
| `System.Runtime.Remoting.Channels.Http.HttpRemotingHandlerFactory` (+ `HttpRemotingHandler`): first request → `RemotingConfiguration.Configure (<app>/web.config)`, find/register a hook `HttpChannel`, `AddHookChannelUri (scheme://authority + ApplicationPath)`; each request → `ProcessHookedRequest`; non-binary content type → 415 | `AspNetCore.Web.Remoting/Port/HttpRemotingHandlerFactory.cs` |
| root web.config `*.rem`/`*.soap` retargeted to `Core.Web.Remoting` | `Tools/gen-config.ps1` `Retarget-PortAssembly`; regenerate `Config/root-web.config` |
| Sample + tests: `Samples/RemotingSample` (web.config `<system.runtime.remoting>` wellknown service), `Tests/AspNetCore.Web.Remoting.Tests` (client `RemotingServices.Connect` over http) | new |

### W2 — Remote StateServer

| Change | Where |
|---|---|
| Compile in place `SessionState_2.0/RemoteStateServer.cs`, `SessionStateServerHandler.cs` in `Core.Web.Remoting`; remove from `port-exclusions.txt:36-37` for `Core.Web` (scope exclusion to that project) | `Build/System.Web.Remoting.sources`, `Tools/port-exclusions.txt` |
| Public contract `System.Web.SessionState.IRemoteStateServer` (items as `object`); patches: `RemoteStateServer` implements it, handler field/`Activator.GetObject` → `RemotingServices.Connect`, `RemotingConfiguration.Configure (null)` dropped (L3 supplies the client channel) — line count preserved | `Port/IRemoteStateServer.cs`, `Tools/port-patches.txt` |
| Store selection: `SessionStateModule` patch (`port-patches.txt:60`) reads `PortSessionState.StateServerProviderType` (default = current `DistributedCacheSessionStateStore` string) | `AspNetCore.Web/Port/PortSessionState.cs` |
| `app.UseWebFormsRemoteStateServer ()` sets it; `StateServerHost.Start (port, allowRemoteConnection)` publishes `RemoteStateServer` at `StateServer` on a `TcpServerChannel` (loopback unless allowed — aspnet_state default) | `Port/RemoteStateServerExtensions.cs`, `Port/StateServerHost.cs` |
| `Tools/state-server` exe (`aspnet_state` equivalent, `--port 42424 --allow-remote`) | new |
| Tests: two app processes share a session through one state server | `AspNetCore.Web.Remoting.Tests` |

### W3 — `ApplicationHost.CreateApplicationHost` / `ApplicationManager`

| Change | Where |
|---|---|
| `CreateApplicationHost` delegates to `Type.GetType ("System.Web.Hosting.RemotingApplicationHostFactory, Core.Web.Remoting")`; absent → `PlatformNotSupportedException` naming the package | `AspNetCore.Web/Overrides/System.Web.Hosting__ApplicationHost.cs:49` |
| Factory: `CreateChildDomain` (ApplicationBase = host base dir, parent `AssemblyResolve` from loaded assemblies), `ApplicationDomainInitializer.Initialize (phys, vpath, …)` → `WebFormsRuntimeHost.Initialize` in the child, then `CreateInstanceAndUnwrap (hostType)` | `AspNetCore.Web.Remoting/Port/RemotingApplicationHostFactory.cs` |
| `ApplicationManager` (public API of upstream; per-appId child domain; `CreateObject`, `GetObject`, `GetRunningApplications`, `ShutdownApplication`, `ShutdownAll`, `StopObject`, `Open`/`Close`) and `ApplicationInfo` compiled in place; un-exclude them for this project | `Overrides/System.Web.Hosting__ApplicationManager.cs`, `port-exclusions.txt:19-20` |
| Hook for `HttpRuntime.DoUnload` (`HttpRuntime.cs:547`): patch → `PortApplicationLifetime.Unload ()`; child domains set it to "ask parent to recycle" | `port-patches.txt`, `AspNetCore.Web/Port/PortApplicationLifetime.cs` |
| Tests: host type in child returns its pid + `HttpRuntime.AppDomainAppVirtualPath`; `SimpleWorkerRequest` renders an `.aspx` in the child | `AspNetCore.Web.Remoting.Tests` |

### W4 — multi-app hosting and recycling

| Change | Where |
|---|---|
| `app.UseWebFormsApplications (o => o.Add ("/a", physA).Add ("/b", physB))`: path-prefix routing to one `WebApplicationDomain` per app | `Port/WebFormsApplicationsExtensions.cs` |
| Parent: buffer request → `[Serializable] ForwardedRequest` (method, scheme, host, pathBase, path, query, protocol, headers, local/remote endpoint, body) → child `IApplicationWorker.Process` → `ForwardedResponse` (status, reason, headers, body) | `Port/ForwardedRequest.cs`, `Port/WebApplicationDomain.cs` |
| Child: `UseWebForms` on a private `ApplicationBuilder`; each request runs against a `DefaultHttpContext` built from features → reuses `AspNetCoreWorkerRequest` unchanged | `Port/ApplicationWorker.cs` |
| Recycling: child's `PortApplicationLifetime.Unload` → parent callback → new domain takes new requests, old drains (in-flight counter) then `Unload ()`; crash → restart on next request; `Recycle (appVPath)` API | `Port/WebApplicationDomain.cs` |
| Tests: two apps, distinct pids; touching `web.config` recycles (pid changes, requests keep succeeding); killed child restarts | `AspNetCore.Web.Remoting.Tests` |

### W5 — docs

`LIMITATIONS.md` (§2 remoting, §3 AppDomain), `LIMITATIONS-PLAN.md`, `README.md`, `PORTING-GUIDE.md`, `CLAUDE.md`, project `README.md`.

## Order and commits

L1–L5 → pack library → W0 → W1 → W2 → W3 → W4 → W5. Commit after each item, both repos, no push.

## Verification

| Step | Command |
|---|---|
| Library | `dotnet test` both test projects (net8 + net10); both sample scripts exit 0 |
| Port build | `dotnet build AspNetCore.Web.slnx`; `Tools/lint-port-code.ps1` clean |
| Generators | `gen-sources.ps1`, `gen-config.ps1` produce no unexpected diff |
| Feature | `dotnet test Tests/AspNetCore.Web.Remoting.Tests` |
| Regression | `FunctionalTests`, `SessionState.Tests`, `HostingTests` still green |

## Risks

| Risk | Mitigation |
|---|---|
| Framework-inheritance default changes how existing child domains launch (apphost → muxer) | only when parent has frameworks beyond `Microsoft.NETCore.App`; AppDomain test suite runs both paths |
| Child process startup 50–100 ms + `Core.Web` init per recycle | overlapped recycle: old domain serves until new is ready |
| Buffered forwarding: large uploads/downloads held in memory | documented; `MaxFrameLength` bounds a single payload |
| Remote StateServer is not wire-compatible with `aspnet_state.exe` | documented; both ends must be this port |
| `Cache` in the state server needs hosted runtime (B3) | fall back to a `MemoryCache`-backed override of `RemoteStateServer` |
| Package feed staleness (`Packages/` rewritten on build) | pack library before `restore`; pin exact versions |
