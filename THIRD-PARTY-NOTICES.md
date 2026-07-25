# Third-party notices

This port compiles upstream sources **in place** rather than copying them, so every assembly it
produces contains code from one or more third parties alongside code written for this port. An SPDX
licence expression on a package says *which* licences apply; this file says *what* each one applies to.

## Components

### Mono class libraries — MIT

Copyright (c) .NET Foundation and Contributors.

Source: <https://github.com/mono/mono> (`mcs/class/...`), vendored as the `Mono` submodule.

Supplies `System.Web`, `System.Configuration`, `System.Web.Services`, `System.Web.Extensions`,
`System.Web.DynamicData` and `System.Web.Infrastructure`. Mono's own `LICENSE` states: *"In general,
the runtime and its class libraries are licensed under the terms of the MIT license."*

### ASP.NET Web Stack — Apache License 2.0

Copyright (c) Microsoft Corporation. All rights reserved.

Source: <https://github.com/mono/aspnetwebstack>, vendored as `Mono/external/aspnetwebstack`.

Supplies ASP.NET MVC, Razor, Web Pages and Web API. Licensed under the Apache License, Version 2.0; a
copy is at <https://www.apache.org/licenses/LICENSE-2.0>.

### This port — MIT

Copyright (c) Piero Viano.

Everything under a project's `Port/`, `Overrides/`, `Shims/` and `Generated/` directory, the MSBuild
assets in `Build/`, and the tools in `Tools/`.

## Which licence applies to which package

| Package | Licence expression | Because |
|---|---|---|
| `AspNetCore.Web.Base` | `MIT` | Mono `System.Web` + this port |
| `AspNetCore.Configuration` | `MIT` | Mono `System.Configuration` + this port |
| `AspNetCore.Web.Services` | `MIT` | Mono `System.Web.Services` + this port |
| `AspNetCore.Web.Extensions` | `MIT` | Mono `System.Web.Extensions` + this port |
| `AspNetCore.Web.DynamicData` | `MIT` | Mono `System.Web.DynamicData` + this port |
| `AspNetCore.Web.Infrastructure` | `MIT` | Mono `System.Web.Infrastructure` |
| `AspNetCore.Web.Hosting.Kestrel` | `MIT` | written for this port |
| `AspNetCore.Web.ConfigBridge` | `MIT` | written for this port |
| `AspNetCore.Web.Optimization` | `MIT` | written for this port |
| `AspNetCore.Web.ServiceModel` | `MIT` | written for this port |
| `AspNetCore.Web.SessionState` | `MIT` | written for this port |
| `AspNetCore.Web.Mvc` | `Apache-2.0 AND MIT` | ASP.NET MVC + Mono `AssemblyInfo.cs` + this port |
| `AspNetCore.Web.Http` | `Apache-2.0 AND MIT` | ASP.NET Web API + this port |
| `AspNetCore.Web.Http.WebHost` | `Apache-2.0 AND MIT` | ASP.NET Web API + this port |
| `AspNetCore.Net.Http.Formatting` | `Apache-2.0 AND MIT` | ASP.NET Web API + this port |
| `AspNetCore.Web.Razor` | `Apache-2.0 AND MIT` | Razor + Mono `AssemblyInfo.cs` + this port |
| `AspNetCore.Web.WebPages.Base` | `Apache-2.0 AND MIT` | Web Pages + Mono `AssemblyInfo.cs` + this port |
| `AspNetCore.Web.WebPages.Razor` | `Apache-2.0 AND MIT` | Web Pages + Mono `AssemblyInfo.cs` + this port |
| `AspNetCore.Web.WebPages.Deployment` | `Apache-2.0 AND MIT` | Web Pages + Mono `AssemblyInfo.cs` + this port |

Both licences are permissive and compatible. `Apache-2.0 AND MIT` is a statement that the assembly
contains code under both, not a choice offered to you: comply with both.

Regenerate the classification behind this table with:

```powershell
# counts upstream files per package, by source tree
Get-ChildItem AspNetCore.*/Sources.generated.props | ForEach-Object {
    $x = [xml](Get-Content $_)
    $inc = $x.Project.ItemGroup.Compile.Include
    [pscustomobject]@{
        Project  = $_.Directory.Name
        Mono     = ($inc | Where-Object { $_ -match 'mcs' -and $_ -notmatch 'aspnetwebstack' }).Count
        WebStack = ($inc | Where-Object { $_ -match 'aspnetwebstack' }).Count
    }
}
```

A package whose `WebStack` count is non-zero must declare `Apache-2.0 AND MIT`.
