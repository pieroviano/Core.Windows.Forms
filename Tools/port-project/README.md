# port-project

Converts an existing ASP.NET application to run on this port, following
[PORTING-GUIDE.md](../../PORTING-GUIDE.md).

It renames your `.csproj` or `.vbproj` to `.old`, writes an SDK-style project in its place, generates a
Kestrel host, and applies the mechanical `web.config` assembly renames — then tells you everything it
could not decide for you.

**It previews by default.** Nothing is written until you pass `--apply`.

---

## Use it

```powershell
# 1. Look at what it would do. This is also the report you need to read.
dotnet run --project Tools/port-project -- C:\src\MyApp

# 2. Do it.
dotnet run --project Tools/port-project -- C:\src\MyApp --apply
```

Point it at the project file or at the directory holding it. A directory with two project files is an
error rather than a guess.

```
port-project <path-to-.csproj|.vbproj|directory> [options]

  --apply                 perform the conversion (without this it only previews)
  --force                 overwrite an existing .old backup
  --no-program            do not generate Program.cs
  --no-web-config         do not rewrite web.config
  --package-version <v>   port package version to reference (default 1.0.0)
  --json                  emit the plan and findings as JSON
  -h, --help              usage

Exit codes
  0  converted, or previewed with no blockers
  1  usage or I/O error
  2  blockers found - nothing was changed
```

---

## What it does

| | |
|---|---|
| **Project file** | `MyApp.csproj` → `MyApp.csproj.old`, and a new SDK-style `MyApp.csproj` with the right port packages |
| **Host** | `Program.cs`, with `UseSvcEndpoints` / `UseWebFormsSessionState` composed in when you need them, in the order that matters |
| **`web.config`** | assembly renames (`System.Web` → `Core.Web`), `System.Configuration` retargeting, Microsoft's strong name stripped — in **every** `web.config`, including `Views/web.config` |
| **Everything else** | reported, not edited |

Package selection is evidence-based. It reads your `<Reference>` list, your `packages.config`, and what
is actually on disk — and prints why it chose each one:

```
  - Package AspNetCore.Web.Mvc  [MyApp.csproj:41]
      references System.Web.Mvc
  - Package AspNetCore.Web.ServiceModel  [Services/Echo.svc]
      .svc endpoints on disk
```

| Port package | Chosen when |
|---|---|
| `AspNetCore.Web.Hosting.Kestrel` | always |
| `AspNetCore.Web.Mvc` | `System.Web.Mvc` referenced, `Microsoft.AspNet.Mvc` package, or a `Views/web.config` |
| `AspNetCore.Web.Optimization` | `System.Web.Optimization` referenced, or an `App_Start/BundleConfig` |
| the five Razor packages | any `.cshtml`/`.vbhtml`, or anything that implies MVC |
| `AspNetCore.Web.Http` + `.Http.WebHost` + `Net.Http.Formatting` | `System.Web.Http` referenced, or a Web API package |
| `AspNetCore.Web.ServiceModel` | any `.svc` on disk |
| `AspNetCore.Web.SessionState` | `<sessionState mode="StateServer">` or `"SQLServer"` |

`AspNetCore.Web.Base`, `.Configuration`, `.Services`, `.Extensions` and `.ConfigBridge` are not listed —
the hosting package brings them.

---

## What it does **not** do

These are deliberate. Each one is a place where a wrong guess would cost you more than doing it
yourself.

* **`<system.webServer>` is never edited.** It is read, and each child element is reported beside what
  the guide says it becomes. An IIS `<rewrite>` ruleset has no mechanical translation into ASP.NET Core
  middleware, and a half-translated one is worse than an untouched one because it looks finished.
* **No source is rewritten.** WCF contracts move from `System.ServiceModel` to `CoreWCF` by changing one
  `using` line; the tool tells you which files, and leaves them alone.
* **No VB host is generated.** The project is left as a `Library` so it still builds, and you add
  `Program.vb` and `<StartupObject>` — copy `Samples/WebFormsSampleVB`.
* **No dependency is ever dropped silently.** A NuGet package the port supersedes is dropped *with a
  finding*; anything else is carried over *with a finding* telling you to check it has a net10.0 build.

---

## Reading the report

Three severities. The summary line counts them.

| | Meaning |
|---|---|
| **Info** | Done, and here is why. Every inference the tool made appears here, so you can audit it. |
| **NEEDS ATTENTION** | Converted, but a human has to look. Does not fail the run. Printed last, because it is the part you must not skim. |
| **BLOCKERS** | Cannot be ported. **Nothing is written**, in either mode, and the exit code is 2. |

Blockers are the Step 0 viability table from the guide, automated: `.rem`/`.soap` remoting endpoints,
`<system.runtime.remoting>`, a `Factory=` on a `.svc`, and references to `System.Web.Mobile`,
`System.Web.DynamicData`, `System.Data.Linq` or `System.Web.Entity`.

`--apply` does not override a blocker. An application with a remoting endpoint does not become portable
because you passed a flag.

---

## After it runs

1. **Read the NEEDS ATTENTION list.** That is the conversion's real remainder.
2. `dotnet build` — the project should build. If it does not, see the guide's step 6 table.
3. `dotnet run`, then request a page. Expect problems at *application start* rather than per page —
   that is where `<httpModules>` and the configuration system fail.
4. Work through the guide from step 4 for anything the report flagged.

---

## If it went wrong

Every change is recoverable, because nothing is edited in place:

```powershell
# put the original project back
Remove-Item MyApp.csproj
Rename-Item MyApp.csproj.old MyApp.csproj

# and the original web.config, if it was rewritten
Remove-Item Web.config
Rename-Item Web.config.old Web.config

# the generated host was new, so deleting it is enough
Remove-Item Program.cs
```

The tool refuses to run when a `.old` file already exists, precisely so a second run cannot overwrite
the only copy of your original. `--force` overrides that; be sure you have the original somewhere else
first.

---

## Notes on how it reads your project

It parses the project file as XML and **does not evaluate MSBuild**. That is a deliberate trade: the
alternative pulls in `Microsoft.Build` and `MSBuildLocator` to read literal elements, and makes the
result depend on what Visual Studio happens to have installed on the machine doing the conversion.

The cost is that a `<Reference>` inside a `Condition`ed `ItemGroup` is read as written. When your
project has any, the tool says so as a NEEDS ATTENTION finding rather than pretending it read
everything.

---

## Tests

`Tests/AspNetCore.Web.PortTool.Tests` — 36 tests over six hand-written legacy fixtures. The expensive
ones convert a fixture and then really run `dotnet build` on the result, because a project file can
satisfy every structural assertion and still fail to restore.

```powershell
dotnet test Tests/AspNetCore.Web.PortTool.Tests

# skip the ones that build, while iterating
dotnet test Tests/AspNetCore.Web.PortTool.Tests --filter Category!=Build
```

The build tests need the port's packages on the local feed. If they fail with a message about missing
packages, run `dotnet build AspNetCore.Web.slnx` first.
