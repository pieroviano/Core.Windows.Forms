# Regenerates <project>/Sources.generated.props for every project in this port, from Mono's .sources
# manifests.
#
# Rules (identical for every project):
#   * every listed path is compiled in place from the mono tree (never copied) so the port stays
#     diffable against upstream;
#   * a path matching any substring in tools/port-exclusions.txt is dropped;
#   * a path matching a rule in tools/port-patches.txt is copied to <project>/patched/,
#     regex-patched, and the patched copy compiled instead. Patches must preserve the line count so
#     upstream line numbers stay valid;
#   * if <project>/Overrides/<flattened-path> exists the upstream file is dropped and the override
#     compiled instead (the csproj globs Overrides/**/*.cs).
#     Flattened path = upstream path with leading ../ stripped and / replaced by __,
#     e.g. System.Web/HttpRuntime.cs -> Overrides/System.Web__HttpRuntime.cs
#
# Usage: powershell -ExecutionPolicy Bypass -File Tools/gen-sources.ps1

$ErrorActionPreference = 'Stop'
$root      = Split-Path -Parent $PSScriptRoot
$classDir  = Join-Path $root 'Mono\mcs\class'
$exclFile  = Join-Path $root 'Tools\port-exclusions.txt'
$patchFile = Join-Path $root 'Tools\port-patches.txt'

# Manifests are folded into one output assembly per project.
#
# Name is the project's DIRECTORY, which is also where Sources.generated.props, Generated\Consts.cs
# and patched\ are written. It is not the assembly name - the directories are AspNetCore.*, the
# assemblies are still Core.*. Get these wrong and the failure is confusing rather than loud: the
# generator happily creates a brand new directory and the real project keeps building against a stale
# props file, or fails with CS2001 on patched files that were written somewhere else.
#
# AspNetCore.Web - System.Web plus System.Web.ApplicationServices, which is a separate assembly on
#                  .NET Framework (the Membership/Roles types) but has no reason to stay split here.
# AspNetCore.Configuration - Mono's System.Configuration, plus the System.Configuration types that
#                  live in Mono's System.dll (ConfigurationSettings, IConfigurationSystem, the 1.x
#                  section handlers, the settings-provider family). The ported System.Web is written
#                  against Mono's configuration semantics - config paths,
#                  Configuration.SaveStart/SaveEnd, FindLocationConfiguration, friend access to
#                  protected internal members - none of which the shipping
#                  System.Configuration.ConfigurationManager provides.
$projects = @(
    @{
        Name = 'AspNetCore.Web'
        Manifests = @(
            @{ List = 'System.Web\System.Web.dll.sources';                                         Base = 'System.Web' },
            @{ List = 'System.Web.ApplicationServices\System.Web.ApplicationServices.dll.sources';  Base = 'System.Web.ApplicationServices' }
        )
        Globs = @()
    },
    @{
        Name = 'AspNetCore.Web.Services'
        Manifests = @(
            @{ List = 'System.Web.Services\System.Web.Services.dll.sources'; Base = 'System.Web.Services' }
        )
        Globs = @()
    },
    @{
        Name = 'AspNetCore.Web.Extensions'
        Manifests = @(
            @{ List = 'System.Web.Extensions\System.Web.Extensions.dll.sources'; Base = 'System.Web.Extensions' }
        )
        Globs = @()
    },
    @{
        Name = 'AspNetCore.Configuration'
        Manifests = @(
            @{ List = 'System.Configuration\System.Configuration.dll.sources'; Base = 'System.Configuration' }
        )
        Globs = @('System\System.Configuration\*.cs')
    },
    # AspNetCore.Web.Razor - the Razor parser and code generator. Note the sources do NOT live in
    # mono's own tree: the manifest points at mono/external/aspnetwebstack, a NESTED submodule
    # carrying Microsoft's ASP.NET Web Stack. It must be checked out, or every path is dropped
    # "(not on disk)" and the project builds to an empty assembly rather than failing:
    #
    #     git submodule update --init --recursive
    #
    # This assembly has no System.Web dependency at all (LIB_REFS = System System.Core), which is why
    # it is ported first.
    @{
        Name = 'AspNetCore.Web.Razor'
        Manifests = @(
            @{ List = 'System.Web.Razor\System.Web.Razor.dll.sources'; Base = 'System.Web.Razor' }
        )
        Globs = @()
    },
    # The rest of the Web Stack, all from mono/external/aspnetwebstack. Dependency order is
    # Infrastructure -> WebPages.Deployment -> WebPages -> WebPages.Razor -> Mvc, and each needs the
    # one before it, so they are listed in that order for readability rather than necessity.
    @{
        Name = 'AspNetCore.Web.Infrastructure'
        Manifests = @(
            @{ List = 'Microsoft.Web.Infrastructure\Microsoft.Web.Infrastructure.dll.sources'; Base = 'Microsoft.Web.Infrastructure' }
        )
        Globs = @()
    },
    @{
        Name = 'AspNetCore.Web.WebPages.Deployment'
        Manifests = @(
            @{ List = 'System.Web.WebPages.Deployment\System.Web.WebPages.Deployment.dll.sources'; Base = 'System.Web.WebPages.Deployment' }
        )
        Globs = @()
    },
    @{
        Name = 'AspNetCore.Web.WebPages'
        Manifests = @(
            @{ List = 'System.Web.WebPages\System.Web.WebPages.dll.sources'; Base = 'System.Web.WebPages' }
        )
        Globs = @()
    },
    @{
        Name = 'AspNetCore.Web.WebPages.Razor'
        Manifests = @(
            @{ List = 'System.Web.WebPages.Razor\System.Web.WebPages.Razor.dll.sources'; Base = 'System.Web.WebPages.Razor' }
        )
        Globs = @()
    },
    # ASP.NET MVC 4, from mono/external/aspnetwebstack - NOT mono's own System.Web.Mvc3 copy.
    #
    # Mono vendors MVC 3 under mcs/class/System.Web.Mvc3 and MVC 4 inside the aspnetwebstack
    # submodule, and this port used to compile the former. That was the wrong half: Razor, Web Pages
    # and Web API here all come from aspnetwebstack and are the MVC 4 generation, so MVC 3 was the
    # odd one out - controllers a major version behind the view engine they render through.
    #
    # MVC 4 adds Task-returning async actions, [AllowAnonymous], HttpPatch/Head/Options, the cached
    # metadata providers and CancellationToken model binding. The manifest is ours because Mono has
    # none for it; see Build/System.Web.Mvc4.sources.
    @{
        Name = 'AspNetCore.Web.Mvc'
        Manifests = @(
            @{ ListPath = 'Build/System.Web.Mvc4.sources'; BasePath = 'Mono/external/aspnetwebstack/src/System.Web.Mvc' }
        )
        Globs = @()
    },
    # Web API. System.Net.Http.Formatting compiles Newtonsoft.Json's SOURCE in place from
    # mono/external/Newtonsoft.Json - a third nested submodule - rather than referencing a package, so
    # `git submodule update --init --recursive` matters here too.
    @{
        Name = 'AspNetCore.Net.Http.Formatting'
        Manifests = @(
            @{ List = 'System.Net.Http.Formatting\System.Net.Http.Formatting.dll.sources'; Base = 'System.Net.Http.Formatting' }
        )
        Globs = @()
    },
    @{
        Name = 'AspNetCore.Web.Http'
        Manifests = @(
            @{ List = 'System.Web.Http\System.Web.Http.dll.sources'; Base = 'System.Web.Http' }
        )
        Globs = @()
    },
    # Dynamic Data. Its DLinq* model providers are excluded (LINQ to SQL does not exist on .NET);
    # Port/QueryableDataModelProvider supplies the same abstract triple over IQueryable instead.
    @{
        Name = 'AspNetCore.Web.DynamicData'
        Manifests = @(
            @{ List = 'System.Web.DynamicData\System.Web.DynamicData.dll.sources'; Base = 'System.Web.DynamicData' }
        )
        Globs = @()
    },
    @{
        Name = 'AspNetCore.Web.Http.WebHost'
        Manifests = @(
            @{ List = 'System.Web.Http.WebHost\System.Web.Http.WebHost.dll.sources'; Base = 'System.Web.Http.WebHost' }
        )
        Globs = @()
    }
)

$patterns = @()
if (Test-Path $exclFile) {
    $patterns = Get-Content $exclFile |
        ForEach-Object { ($_ -replace '#.*$', '').Trim() } |
        Where-Object { $_ -ne '' }
}

$patchRules = @()
if (Test-Path $patchFile) {
    foreach ($line in Get-Content $patchFile) {
        if ($line.Trim() -eq '' -or $line.TrimStart().StartsWith('#')) { continue }
        $f = $line -split "`t"
        if ($f.Count -lt 3) { throw "port-patches.txt: expected 3 tab-separated fields, got $($f.Count) in: $line" }
        $patchRules += [pscustomobject]@{ Filter = $f[0]; Pattern = $f[1]; Replacement = $f[2]; Hits = 0 }
    }
}

# Consts.cs is generated by mono's build from Consts.cs.in (it carries the framework version
# constants the sources reference). Emit it into every project rather than checking in copies that
# can drift apart.
$constsIn = Join-Path $root 'Mono/mcs/build/common/Consts.cs.in'
if (-not (Test-Path $constsIn)) { throw "missing $constsIn" }
$constsText = [System.IO.File]::ReadAllText($constsIn).
    Replace('@MONO_VERSION@', '6.12.0').
    Replace('@MONO_CORLIB_VERSION@', '1A5E0066-58DC-428A-B21C-0AD6CDAE2789')
foreach ($proj in $projects) {
    $genDir = Join-Path (Join-Path $root $proj.Name) 'Generated'
    if (-not (Test-Path $genDir)) { New-Item -ItemType Directory -Force $genDir | Out-Null }
    [System.IO.File]::WriteAllText((Join-Path $genDir 'Consts.cs'), $constsText)
}

foreach ($proj in $projects) {
    $projDir    = Join-Path $root $proj.Name
    if (-not (Test-Path $projDir)) { throw "project directory missing: $projDir" }
    $out        = Join-Path $projDir 'Sources.generated.props'
    $overrides  = Join-Path $projDir 'Overrides'
    # patched\, NOT obj\patched\: Sources.generated.props is committed, and it names these files.
    # Under obj\ they are gitignored, so a fresh clone has a props file pointing at sources that do
    # not exist and the build fails with CS2001 until someone happens to run this script. Generated
    # output that a committed file references has to be committed too.
    $patchedDir = Join-Path $projDir 'patched'

    $overrideNames = @{}
    if (Test-Path $overrides) {
        Get-ChildItem $overrides -Filter *.cs -Recurse | ForEach-Object { $overrideNames[$_.Name] = $true }
    }
    if (Test-Path $patchedDir) { Remove-Item $patchedDir -Recurse -Force }

    $kept     = New-Object System.Collections.Generic.List[string]
    $dropped  = New-Object System.Collections.Generic.List[string]
    $shadowed = New-Object System.Collections.Generic.List[string]
    $patched  = New-Object System.Collections.Generic.List[string]
    $seen     = @{}

    # (relative-label, absolute-path) pairs from manifests first, then extra globs
    $inputs = New-Object System.Collections.Generic.List[object]

    foreach ($m in $proj.Manifests) {
        # List/Base are resolved under Mono\mcs\class - the normal case, an upstream manifest.
        # ListPath/BasePath are resolved from the REPOSITORY root, for the one manifest this port has
        # to author itself: Mono ships no compile list for MVC 4, and Mono/ is read-only.
        $listPath = if ($m.ListPath) { Join-Path $root $m.ListPath } else { Join-Path $classDir $m.List }
        if (-not (Test-Path $listPath)) { throw "missing manifest $listPath" }
        $baseDir = if ($m.BasePath) { Join-Path $root $m.BasePath } else { Join-Path $classDir $m.Base }
        foreach ($line in Get-Content $listPath) {
            $rel = $line.Trim()
            if ($rel -eq '' -or $rel.StartsWith('#')) { continue }
            $abs = [System.IO.Path]::GetFullPath((Join-Path $baseDir ($rel -replace '/', '\')))
            $inputs.Add([pscustomobject]@{ Rel = $rel; Abs = $abs })
        }
    }

    foreach ($glob in $proj.Globs) {
        $globPath = Join-Path $classDir $glob
        # Rel must be the file's directory + name, NOT the glob pattern: it becomes the flattened
        # Overrides/patched file name, and a '*' in there is an illegal path character.
        $globDir = (Split-Path -Parent $glob).Replace('\','/')
        foreach ($file in Get-ChildItem -Path $globPath -ErrorAction SilentlyContinue) {
            $inputs.Add([pscustomobject]@{ Rel = "$globDir/$($file.Name)"; Abs = $file.FullName })
        }
    }

    foreach ($item in $inputs) {
        $rel = $item.Rel
        $abs = $item.Abs

        if (-not (Test-Path $abs)) { $dropped.Add("$rel (not on disk)"); continue }
        # manifests overlap (Consts.cs, MonoTODOAttribute.cs, Locale.cs, ...)
        if ($seen.ContainsKey($abs)) { continue }
        $seen[$abs] = $true

        $key = $abs -replace '\\', '/'
        $skip = $false
        foreach ($p in $patterns) { if ($key.Contains($p)) { $skip = $true; break } }
        if ($skip) { $dropped.Add($rel); continue }

        $flat = ($rel -replace '^\./', '' -replace '^(\.\./)+', '') -replace '/', '__'
        if ($overrideNames.ContainsKey($flat)) { $shadowed.Add($rel); continue }

        $rules = @($patchRules | Where-Object { $key.Contains($_.Filter) })
        if ($rules.Count -gt 0) {
            $original = [System.IO.File]::ReadAllText($abs)
            $text = $original
            foreach ($r in $rules) {
                $applied = [regex]::Replace($text, $r.Pattern, $r.Replacement)
                if ($applied -ne $text) { $r.Hits++ }
                $text = $applied
            }
            # A broad filter (e.g. ".cs") usually finds nothing in a given file. Only materialise a
            # patched copy when the content actually changed, so unaffected files keep compiling
            # straight from the mono tree and diagnostics keep pointing at upstream paths.
            if ($text -ne $original) {
                $before = ($original -split "`n").Count
                $after  = ($text -split "`n").Count
                if ($before -ne $after) { throw "patch changed line count of ${rel} ($before -> $after); patches must be line-preserving" }
                if (-not (Test-Path $patchedDir)) { New-Item -ItemType Directory -Force $patchedDir | Out-Null }
                [System.IO.File]::WriteAllText((Join-Path $patchedDir $flat), $text)
                $patched.Add($rel)
                $kept.Add("patched\$flat")
                continue
            }
        }

        # emit a path relative to the project directory (all inputs live under $root)
        if (-not $abs.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) {
            throw "source outside repo root, cannot make relative: $abs"
        }
        $kept.Add('..\' + $abs.Substring($root.Length).TrimStart('\'))
    }

    $sb = New-Object System.Text.StringBuilder
    [void]$sb.AppendLine('<!-- GENERATED by tools/gen-sources.ps1 - do not edit by hand. -->')
    [void]$sb.AppendLine('<Project>')
    [void]$sb.AppendLine('  <ItemGroup>')
    foreach ($k in $kept) { [void]$sb.AppendLine("    <Compile Include=`"$k`" />") }
    [void]$sb.AppendLine('  </ItemGroup>')
    [void]$sb.AppendLine('</Project>')
    [System.IO.File]::WriteAllText($out, $sb.ToString())

    Write-Output "$($proj.Name):"
    Write-Output ("  compiled : {0}  (of which patched: {1})" -f $kept.Count, $patched.Count)
    Write-Output ("  excluded : {0}" -f $dropped.Count)
    Write-Output ("  shadowed : {0}" -f $shadowed.Count)
    foreach ($s in $shadowed) { Write-Output "    override -> $s" }
}

# A patch rule that matches nothing is almost always a broken regex, not an obsolete rule - and it
# fails silently, leaving the compile errors it was meant to fix. Make that loud. Hits are counted
# across all projects, so this runs once at the end.
$dead = @($patchRules | Where-Object { $_.Hits -eq 0 })
if ($dead.Count -gt 0) {
    Write-Output ''
    Write-Output "ERROR: $($dead.Count) patch rule(s) matched no files:"
    foreach ($d in $dead) { Write-Output "    filter='$($d.Filter)' pattern='$($d.Pattern)'" }
    Write-Output '  Fix or remove them (note: the mono sources use CRLF, so a trailing $ anchor'
    Write-Output "  will not match unless you allow for the \r)."
    exit 1
}
Write-Output ''
Write-Output 'patch rules: all matched at least one file'
