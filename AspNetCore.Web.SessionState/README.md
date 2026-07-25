# AspNetCore.Web.SessionState

Out-of-process session state: `<sessionState mode="StateServer">` and `mode="SQLServer"`, with the
`web.config` you already have.

```xml
<PackageReference Include="AspNetCore.Web.SessionState" Version="1.0.0" />
```

Not needed for `mode="InProc"`, which `AspNetCore.Web.Base` handles on its own.

> **Assembly vs package name.** The package is `AspNetCore.Web.SessionState`; the assembly inside it is `Core.Web.SessionState`.
> .NET ships an empty `System.Web.dll` facade in `Microsoft.NETCore.App` and the host gives the shared
> framework precedence, so an app-local `System.Web.dll` is never loaded — the port therefore cannot
> use the original assembly names. **Namespaces are unchanged**, so your code and `Inherits=`
> attributes are unaffected; only `web.config` entries that name an *assembly* need updating.

## Usage

```csharp
var builder = WebApplication.CreateBuilder (args);

// mode="StateServer" only. AddDistributedMemoryCache () for a single instance.
builder.Services.AddStackExchangeRedisCache (o => o.Configuration = "localhost:6379");

var app = builder.Build ();

app.UseWebFormsSessionState ();     // BEFORE UseWebForms

app.UseWebForms (options => { /* ... */ });
```

`UseWebFormsSessionState` is a no-op for `InProc` and `Off`, so it is safe to call unconditionally.
It exists because the session store is built by the provider model — `Activator.CreateInstance` on a
type named in configuration — which has no access to dependency injection.

## Neither store is the one the words used to mean

| Mode | What actually stores the session |
|---|---|
| `StateServer` | The `IDistributedCache` you registered — Redis, SQL Server, NCache, in-memory. **Not `aspnet_state.exe`**; `stateConnectionString` is ignored. |
| `SQLServer` | The stock `ASPState` database, through its standard stored procedures. An existing `aspnet_regsql`-provisioned database needs no DDL. |

Mono's own implementations could not be used: its StateServer handler is built on .NET Remoting, a CLR
feature CoreCLR does not have, and its SQL handler targets a Mono-invented table through a SQLite
factory this port excludes.

For a **new** ASPState database, `aspnet_regsql.exe` does not exist on .NET 10 — the script ships in
the assembly:

```csharp
Console.WriteLine (SqlSessionStateStore.GetSchemaScript ());   // for a DBA to review
SqlSessionStateStore.EnsureSchema (connectionString);          // or run it directly; idempotent
```

## Four things to know

* **Existing session data does not migrate.** Sessions written by .NET Framework contain
  `BinaryFormatter` payloads and .NET 9+ cannot read them. Reuse the Redis instance or the database —
  the *infrastructure* — but treat the cutover as a session flush.
* **Everything in `Session` must now serialize.** `InProc` never wrote a session down, so a type that
  has worked for years can fail the first time the mode changes, with no other edit. The failure names
  both the type and the mode.
* **StateServer locking is best-effort.** `IDistributedCache` has no compare-and-swap, so two
  simultaneous requests for one session can both believe they hold the lock; the result is
  last-writer-wins, not corruption. `SQLServer` has no such window.
* **`Session_End` never fires** under either mode. Only `InProc` could ever raise it.

Under `SQLServer`, schedule `dbo.DeleteExpiredSessions` yourself — nothing reads expired rows, so
skipping it costs disk rather than correctness.

## Documentation

`PORTING-GUIDE.md` (step-by-step migration from IIS) and `LIMITATIONS.md` (what differs from ASP.NET
on .NET Framework, and why) ship in the repository.

## Licence

MIT, as the upstream Mono sources this is built from and the code written for this port.
`THIRD-PARTY-NOTICES.md` ships in the package and says which part is which.
