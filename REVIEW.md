# Repository review

A review of `AspNetCore.Web` as it stands: 19 shipping packages, 8 sample applications, 15 test
projects, **445 tests, all passing** across three consecutive whole-solution runs, none skipped, and a
build clean of warnings beyond the expected obsolete-API notices.

The engineering standard here is high, and establishing that first changes how the findings should be
read. 9,711 lines of port-authored code across 51 files produce **zero compiler warnings at level 4
with every suppression lifted** — measured, and enforced by `Tools/lint-port-code.ps1`. No `TODO`,
`FIXME` or `HACK` anywhere in it. No mutable static state. The whole sync-over-async surface is one
call site, which the documentation names. The error page HTML-encodes every value it renders, including
the exception message, the stack trace, the requested URL and each source line.

The findings below are of two kinds: one place where a safety check does not hold as tightly as it
reads, and four places where a decision has been made well but not written down.

---

## Findings

Ordered by what would cost a user the most.

| # | Finding | Severity |
|---|---|---|
| 1 | The application-root containment check is a bare prefix match | Medium |
| 2 | One assembly is 17% overridden, and the rule governing overrides is stated as a stale count | Medium |
| 3 | The port adds public API back to `System.Web` with nothing recording what | Medium |
| 4 | The staging feed's one documented cost has no step that catches it | Low |
| 5 | Nothing asserts the diagnostic error page stays off for remote clients | Low |

---

### 1. The application-root containment check is a bare prefix match — Medium

`WebFormsRuntimeHost.MapPath` resolves a virtual path and then refuses to leave the application:

```csharp
string mapped = Path.GetFullPath (Path.Combine (physical_path, path.Replace ('/', Path.DirectorySeparatorChar)));

// Refuse to escape the application root: a request path must never map outside it.
if (!mapped.StartsWith (physical_path, StringComparison.OrdinalIgnoreCase))
    throw new HttpException (403, "Path is outside the application root: " + virtualPath);
```

Normalising with `GetFullPath` before comparing is exactly right, and it is the part most
implementations get wrong. The comparison itself is the problem: `physical_path` is stored verbatim
from the caller and **is not required to end with a directory separator**, so `StartsWith` matches a
*sibling directory that shares the prefix*:

```
root   = 'D:\apps\site'
mapped = 'D:\apps\site-backup\secrets.txt'
guard says 'inside the application root': True      <-- prefix match succeeds

root   = 'D:\apps\site\'
mapped = 'D:\apps\site-backup\secrets.txt'
guard says 'inside the application root': False     <-- with a separator, correct
```

This is not hypothetical for this repository. `Initialize` stores `physical_path = physicalPath`
unchanged, and immediately below it computes `appPathWithSlash` for the AppDomain data — so the author
knew the value often arrives without one, and the normalised form simply was not used for the check.
Every test in the suite runs in that state: the fixtures pass `Path.Combine (Root, "Samples", name)`,
which never produces a trailing separator, and `Path.Combine` is the obvious thing a consumer will
write too.

**What limits the severity.** A raw request URL cannot reach it: Kestrel normalises `..` out of the
path before the middleware sees it, so this is not a remote file-read on an unmodified request. What
does reach it is anything that maps a *constructed* path — `Server.MapPath`, `HostingEnvironment.MapPath`,
a `VirtualPathProvider`, or any application code combining user input into a virtual path. That is
precisely the population a defence-in-depth check exists to protect, which is why it is worth fixing
even though the front door is closed.

The fix is one line, and the value it needs already exists a few lines above:

```csharp
// compare against the root WITH a trailing separator, so a sibling directory
// that merely shares the prefix cannot satisfy the check
string root = physical_path.EndsWith (Path.DirectorySeparatorChar.ToString (), StringComparison.Ordinal)
        ? physical_path : physical_path + Path.DirectorySeparatorChar;

if (!mapped.StartsWith (root, StringComparison.OrdinalIgnoreCase) &&
    !String.Equals (mapped.TrimEnd (Path.DirectorySeparatorChar), root.TrimEnd (Path.DirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase))
    throw new HttpException (403, ...);
```

The second clause keeps `MapPath ("~")` — the root itself — working, which a naive separator fix would
break. Worth a test with a fixture rooted at a directory that has a same-prefix sibling; that is the
only arrangement in which the bug is visible, and it is not one that occurs by accident.

### 2. One assembly is 17% overridden, and the rule is stated as a stale count — Medium

The repository's founding decision is that upstream files are **compiled in place, never copied**, so
the tree stays diffable against Mono. Four mechanisms exist and `CLAUDE.md` ranks them: *"Choose the
lightest mechanism that works: patch for mechanical/repetitive edits, override only when a file needs
real surgery (there are currently four in `AspNetCore.Web/Overrides/`)."*

The count is stale, and the shape has changed more than the count suggests:

| Project | Upstream files compiled | Overridden | Share |
|---|---:|---:|---:|
| `AspNetCore.Web` | 1,425 | 4 | 0.3% |
| `AspNetCore.Web.WebPages.Deployment` | 16 | 1 | 6.2% |
| **`AspNetCore.Web.DynamicData`** | **30** | **5** | **16.7%** |

`AspNetCore.Web` is exemplary — four overrides in fourteen hundred files, exactly the discipline the
rule describes. `AspNetCore.Web.DynamicData` is a different situation: one file in six is a local copy,
and they are the load-bearing ones — `DynamicControl`, `DynamicField`, `DynamicDataExtensions`,
`DynamicDataRouteHandler`. For that assembly "diffable against upstream" is materially weaker than the
rule claims, and nothing in the documentation would tell a reader.

There is a good reason — Mono's Dynamic Data was substantially unfinished, so more of it had to be
written than adapted — and the right response is to record it rather than undo it:

* Stop hard-coding a number that goes stale. `Tools/gen-sources.ps1` already prints `shadowed` counts
  per project on every run; the documentation can point at that.
* Say what the threshold is. If one assembly reaching a sixth overridden is acceptable, that is a
  decision worth writing down; if it is the point at which a fork should be acknowledged, that is worth
  writing down too. Today neither is stated, so the next override gets judged against a rule that no
  longer describes the code.

### 3. The port adds public API back to `System.Web` with nothing recording what — Medium

`HttpResponse` in this port has a `HeadersWritten` property. .NET Framework has had it since 4.5; Mono
never did; the port adds it, because once responses stream an application has no other way to ask
whether it is still safe to set a header — the alternative is catching `HttpException` and using it as
control flow. `DynamicDataExtensions` likewise gained `SetMetaTable`, `GetMetaTable` and
`TryGetMetaTable`: present on .NET Framework, absent from Mono, and required here because a grid bound
to a plain `IQueryable` has no `IDynamicDataSource` for `FindMetaTable` to discover.

Both are good additions. Neither is written down anywhere, and the documentation set otherwise covers
every direction:

| Document | Answers |
|---|---|
| `LIMITATIONS.md` | what the port **removed or changed** |
| `MONOTODO.md` | what **upstream left unfinished** |
| `LIMITATIONS-PLAN.md` | which limitations are **permanent** |
| *(nothing)* | what the port **added back** |

The asymmetry is unhelpful in a specific way: a migrator who assumes an API is missing writes a
workaround they did not need, and one who assumes the port is Mono-shaped never looks. Since these
additions are the port restoring *.NET Framework* behaviour, the people most likely to want them are
exactly those migrating from .NET Framework.

A short "APIs this port restores" table — member, why it was needed, which upstream lacks it — costs a
page. It would also make the implementations reviewable: `HeadersWritten` is a one-line patch sharing a
line with the field it exposes, which is a neat trick precisely because the patch mechanism forbids
changing the line count, and a reader ought to be able to find that rather than rediscover it.

### 4. The staging feed's one documented cost has no step that catches it — Low

`Packages/` is deliberately both input and output: every repository in the tree links the same folder,
every packable project publishes into it, and `NuGet.Config` declares it as a source. That is what lets
a new package be consumed and tested by dependent repositories before anything is pushed to nuget.org,
so a whole dependent set can be moved to a new version and verified before any of it is published. It
is documented under "The shared staging feed" in `README.md`, including its costs.

The first cost is that the folder accumulates and a package id outlives the project that produced it.
When `AspNetCore.Web.WebPages` was renamed to `AspNetCore.Web.WebPages.Base`, the old `.nupkg` stayed
and kept satisfying restores — the port-tool build tests passed against an id no project emitted, and
would have failed on a clean machine. That is the characteristic failure of this arrangement: it cannot
be reproduced by whoever caused it.

`Tools/prune-feed.ps1` reports and removes exactly those, and considers only `AspNetCore.*` because the
feed holds other products' work. What is missing is anything that *runs* it. The natural moment is the
one the design already defines — the deliberate push to nuget.org — and that is also the moment most
likely to be done from memory. Naming it as a step wherever the publish is described turns a tool that
must be remembered into one that is hard to skip.

### 5. Nothing asserts the diagnostic error page stays off for remote clients — Low

The port ships a detailed error page: exception type and message, full stack with real file and line
numbers, and the offending source line for a failed page compile. It is genuinely useful, it is
correctly HTML-encoded throughout, and `PORTING-GUIDE.md` step 6 rightly tells people to set
`customErrors mode="Off"` while porting so they get it.

Every sample sets `<customErrors mode="Off" />` with a comment saying not to copy the line into
production — the right warning in the right place. What no test covers is the other half: that with
`customErrors` at its default a remote client gets the generic page instead. All 445 tests, including
several that deliberately provoke failures, run against samples that have it switched off, so the path
that matters in production is the one path never exercised.

The gap is narrow — this is upstream `HttpApplication` and `CustomErrorsSection` behaviour rather than
port code, and there is no particular reason to think it is broken. It is worth a test because of what
it protects against: a stack trace containing absolute filesystem paths appearing in somebody's
browser. One request to a deliberately failing page, against an application configured
`customErrors="RemoteOnly"`, asserting the response contains no file path and no stack frame.

---

## What is in good shape

* **The port-authored code is measurably clean, and stays that way by construction.** Zero warnings at
  level 4 with suppression lifted across 9,711 lines; no `TODO`/`FIXME`/`HACK`; no mutable static state
  — every `static` in `Port/` and `Overrides/` is a method or a `readonly` table.
  `Tools/lint-port-code.ps1` rebuilds with warnings restored and fails on any warning in those
  directories, so this is enforced rather than observed. For a runtime serving concurrent requests in
  one process — where upstream itself reached for a static `Dictionary<HttpContext, …>` behind a
  `ReaderWriterLockSlim` and leaked an entry per request — that discipline is worth naming.

* **The error page is careful with untrusted text.** Every value it renders goes through `HtmlEncode`:
  the exception message, description, stack trace, requested URL, the origin, and each individual
  source line of a failed compile. A diagnostic page that interpolates an exception message is a
  standard way to turn a crash into stored XSS, and this one does not.

* **The response path behaves like the runtime it replaces.** `Response.Flush ()` writes through, so
  progressive rendering, server-sent events and row-by-row reports work; `TransmitFile` streams in
  64 KB chunks rather than materialising the file; transfer framing is left to Kestrel. Each is
  asserted by a test that reads the response *as it arrives* — the only way the difference is visible,
  since a buffering server produces byte-identical output.

* **A client that goes away is an ordinary event.** The middleware distinguishes a cancellation
  explained by `RequestAborted` from one thrown by application code, and the flush in the `finally`
  cannot replace an in-flight exception with a disconnect — the failure mode where the interesting
  error is the one discarded.

* **The test suite earns its runtime.** 445 tests, none skipped, and the expensive ones are expensive
  for a reason: the port-tool suite really runs `dotnet build`, the session suite really talks to
  LocalDB, the WCF suite posts real SOAP, the mail suite speaks SMTP down a socket, two browser suites
  drive real Chromium. The fifteen-project split is not cosmetic — the runtime hosts one application
  per process, and `dotnet test` giving each project its own process is the isolation that buys.

* **Packaging metadata is accurate where it is easy to be wrong.** Each package carries a real
  description, and the licence expression reflects what the assembly contains: `MIT` for the eleven
  built from Mono or written here, `Apache-2.0 AND MIT` for the eight built from
  `Mono/external/aspnetwebstack`, which Microsoft released under Apache 2.0.
  `THIRD-PARTY-NOTICES.md` ships in every package and maps component to licence, because an SPDX
  expression says which licences apply but not to which files.

* **The diagnostics keep pace with the code.** `Tools/verify-config` resolves every configuration
  section against the newest sample, not just the oldest. `Tools/gen-monotodo-inventory.ps1`
  regenerates the upstream-stub inventory so it cannot drift, and `PORTING-GUIDE.md` names the failure
  mode it exists for — "it compiled, ran, and did nothing".

* **The documentation states costs rather than hiding them.** `LIMITATIONS.md` says the `StateServer`
  locking window exists, that `@await` blocks a thread, that bundles are not minified, that sessions do
  not migrate, that flushing commits the headers. `LIMITATIONS-PLAN.md` separates what is permanent
  from what is merely unfinished — the question a migration decision actually turns on, and the one
  most ports never answer.

---

## Recommended order

1. **Finding 1** — one line, plus a test with a same-prefix sibling directory. It is the only finding
   where the code does something other than what it says, and containment checks are worth holding to
   a higher standard than their current reachability suggests.
2. **Findings 2 and 3** — both document decisions already taken, and both are what a second maintainer
   would need and cannot currently get: which files have left upstream behind, and which APIs the port
   has added back.
3. **Finding 5** — one test, closing the distance between "turn this off while porting" and "we know
   what happens when it goes back on".
4. **Finding 4** — name the prune step wherever the publish is described.
