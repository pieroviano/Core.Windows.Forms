# Reports - and optionally removes - AspNetCore.* packages on the local feed that no project in this
# solution produces any more.
#
# Why this exists
# ---------------
# PackageOutputPath is $(SolutionDir)Packages/ - the shared staging feed, by design, so a sibling
# repository can consume a package before it is pushed to nuget.org. Nothing prunes it, and that is the
# cost of the arrangement: a package id outlives the project that produced it. Rename or retire one and
# the old .nupkg stays there, still satisfying restores. Everything goes on working locally and fails
# on a machine that has never built this port - the worst kind of failure, because it cannot be
# reproduced by whoever caused it.
#
# That is not hypothetical. AspNetCore.Web.WebPages was renamed to AspNetCore.Web.WebPages.Base, and
# the stale artefact kept the port-tool build tests green against an id no project emitted.
#
#   powershell -ExecutionPolicy Bypass -File Tools/prune-feed.ps1            # report only
#   powershell -ExecutionPolicy Bypass -File Tools/prune-feed.ps1 -Delete    # remove them
#
# ONLY AspNetCore.* is ever considered, and that is essential rather than cautious: the feed is
# shared with every other product in the tree, whose packages are none of this script's business.

[CmdletBinding()]
param (
    [switch] $Delete
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$feed = Join-Path $root 'Packages'

if (-not (Test-Path $feed)) {
    Write-Output "No feed at $feed - nothing to prune."
    exit 0
}

# The ids this solution produces, read from the projects rather than listed here: a hand-kept list
# would be wrong the first time a package is renamed, which is the exact event this guards against.
$produced = @{}
foreach ($project in Get-ChildItem -Path $root -Directory -Filter 'AspNetCore.*') {
    $file = Join-Path $project.FullName "$($project.Name).csproj"
    if (-not (Test-Path $file)) { continue }

    $text = Get-Content $file -Raw
    $id = [regex]::Match($text, '<PackageId>\s*(?<id>[^<]+?)\s*</PackageId>')
    $assembly = [regex]::Match($text, '<AssemblyName>\s*(?<name>[^<]+?)\s*</AssemblyName>')
    if (-not $id.Success -or -not $assembly.Success) { continue }

    $produced[$id.Groups['id'].Value.Replace('AspNet$(AssemblyName)', "AspNet$($assembly.Groups['name'].Value)")] = $true
}

if ($produced.Count -eq 0) {
    Write-Error "Found no packable projects under $root - refusing to prune on the strength of that."
}

Write-Output "This solution produces $($produced.Count) packages."

$stale = @()
foreach ($file in Get-ChildItem -Path $feed -Filter 'AspNetCore.*' -File) {
    if ($file.Extension -notin '.nupkg', '.snupkg') { continue }

    # Strip the version: the first dot followed by a digit starts it. Splitting on the last three dots
    # would be wrong for a four-part version, and an id may itself contain digits.
    $name = [System.IO.Path]::GetFileNameWithoutExtension($file.Name)
    $version = [regex]::Match($name, '\.(?=\d)')
    $id = if ($version.Success) { $name.Substring(0, $version.Index) } else { $name }

    if (-not $produced.ContainsKey($id)) { $stale += [pscustomobject]@{ Id = $id; File = $file } }
}

if ($stale.Count -eq 0) {
    Write-Output 'The feed holds no stale AspNetCore.* packages.'
    exit 0
}

Write-Output ''
Write-Output "$($stale.Count) file(s) on the feed belong to no project in this solution:"
foreach ($entry in $stale) {
    Write-Output ("  {0,-46} {1}" -f $entry.File.Name, $entry.Id)
}

if (-not $Delete) {
    Write-Output ''
    Write-Output 'Re-run with -Delete to remove them. Nothing has been changed.'
    exit 1
}

foreach ($entry in $stale) { Remove-Item $entry.File.FullName -Force }

Write-Output ''
Write-Output "Removed $($stale.Count) file(s)."
