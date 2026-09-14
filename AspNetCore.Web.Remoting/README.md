# AspNetCore.Web.Remoting

What ASP.NET built on .NET Remoting and AppDomains, on
[Net4x.Runtime.Remoting](https://www.nuget.org/packages/Core.Runtime.Remoting) and
[Net4x.AppDomain](https://www.nuget.org/packages/Core.AppDomain.Library).

```xml
<PackageReference Include="Core.AspNet.Web.Remoting" Version="1.0.0" />
```

Assembly `Core.Web.Remoting`; namespaces unchanged.

| Feature | How |
|---|---|
| `*.rem` endpoints | Nothing to call. Root web.config maps `*.rem`/`*.soap` to `HttpRemotingHandlerFactory`, which applies `web.config`'s `<system.runtime.remoting>` on the first request |
| `mode="StateServer"` over remoting | `app.UseWebFormsRemoteStateServer ()` before `UseWebForms`; run `Tools/state-server` (or `RemoteStateServerHost.Start ()`) |
| `ApplicationHost.CreateApplicationHost` | Works as written; the domain is a child process |
| `ApplicationManager` | Works as written; one child process per application id |
| Several applications, recycled | `app.UseWebFormsApplications (apps => { apps.Add ("/a", pathA); ... })` |

## Several applications behind one Kestrel

```csharp
app.UseWebFormsApplications (apps => {
	apps.Add ("/sales", @"C:\sites\sales");
	apps.Add ("/hr",    @"C:\sites\hr", o => o.PreloadEnabled = true);
});
```

* Each application runs in its own process with its own `HttpRuntime`, configuration and compiled pages.
* **Recycling:** editing `web.config`, `Global.asax`, `bin` or `App_Code` — or `HttpRuntime.UnloadAppDomain ()` —
  replaces the process. The old one finishes its requests (`ShutdownTimeout`, default 90 s); new requests go to
  the new one. `WebFormsApplicationDomain.Recycle ()` does the same on demand.
* A crashed application is restarted on the next request; the request it was serving gets 503.

## Limits

| | |
|---|---|
| Both ends must be this port | The wire format is Net4x.Runtime.Remoting's, not .NET Framework's. `aspnet_state.exe` and Framework remoting clients cannot connect |
| No SOAP | `*.soap` / `text/xml` requests get 415 |
| Class contracts need `virtual` members | A host type, registered object or remoted class is used through a generated proxy; the proxy factory names any member it cannot override. Interfaces need nothing |
| Child-domain requests are buffered | No streaming responses, no WebSockets, no ASP.NET Core authentication inside the application |
| Startup cost | A domain takes roughly a second to start and initialise; use `PreloadEnabled` for the first request |
| State server is unauthenticated | Loopback only unless `allowRemoteConnection`, as `aspnet_state` |
