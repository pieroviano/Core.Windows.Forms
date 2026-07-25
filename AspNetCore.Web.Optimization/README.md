# AspNetCore.Web.Optimization

`System.Web.Optimization` — script and style bundling. Your existing `App_Start/BundleConfig.cs` and
`@Scripts.Render` / `@Styles.Render` calls work unchanged.

```xml
<PackageReference Include="AspNetCore.Web.Optimization" Version="1.0.0" />
```

> **Assembly vs package name.** The package is `AspNetCore.Web.Optimization`; the assembly inside it is `Core.Web.Optimization`.
> .NET ships an empty `System.Web.dll` facade in `Microsoft.NETCore.App` and the host gives the shared
> framework precedence, so an app-local `System.Web.dll` is never loaded — the port therefore cannot
> use the original assembly names. **Namespaces are unchanged**, so your code and `Inherits=`
> attributes are unaffected; only `web.config` entries that name an *assembly* need updating.

## Usage

```csharp
public class BundleConfig
{
    public static void RegisterBundles (BundleCollection bundles)
    {
        bundles.Add (new ScriptBundle ("~/bundles/jquery").Include ("~/Scripts/jquery-{version}.js"));
        bundles.Add (new StyleBundle ("~/Content/css").Include ("~/Content/site.css"));
    }
}
```

```cshtml
@Styles.Render("~/Content/css")
@Scripts.Render("~/bundles/jquery")
```

Call `BundleConfig.RegisterBundles (BundleTable.Bundles)` from `Application_Start`. No `web.config`
edit is needed.

## What works

Wildcard and `{version}` includes, `IncludeDirectory`, per-file and per-bundle transforms, custom
orderers, CDN paths, content-hash cache busting, and the debug/release switch — with
`BundleTable.EnableOptimizations` off, views render each file individually so the browser's sources
pane shows real file names.

## Bundles are concatenated, not minified

Deliberate. The original minified through WebGrease, which is .NET Framework only and long dead, and a
substitute minifier that mangles one edge case in your JavaScript fails **silently and
intermittently** — far worse than a larger file. Minify at build time and point the bundle at the
output, or use `CdnPath`.

## One mechanical difference

The original intercepts bundle URLs in an `IHttpModule` registered through a
`PreApplicationStartMethod`; here each bundle registers a route. Bundle routes are inserted at the
front of the route table so a conventional `{controller}/{action}/{id}` cannot claim `/bundles/app`,
and they refuse to generate outgoing URLs so `Html.ActionLink` cannot accidentally resolve to one.

This means `UrlRoutingModule` must be registered — which the port's generated root configuration
already does for every application.

## Documentation

`PORTING-GUIDE.md` (step-by-step migration from IIS) and `LIMITATIONS.md` (what differs from ASP.NET
on .NET Framework, and why) ship in the repository.

## Licence

MIT, as the upstream Mono sources this is built from and the code written for this port.
`THIRD-PARTY-NOTICES.md` ships in the package and says which part is which.
