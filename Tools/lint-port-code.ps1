# Compiles the port-authored sources with warnings RESTORED and fails if any appear.
#
# Why this exists
# ---------------
# Every project sets WarningLevel 0 with a long NoWarn, for a good reason: the upstream Mono tree is
# not warning-clean and never will be, and a build that emits thousands of warnings emits none that
# anyone reads. But the setting is per PROJECT, so it covers Port/, Overrides/ and Shims/ too - the
# ~9,700 lines written FOR this port, which are not upstream and have no reason to be noisy.
#
# Measured when this was written: rebuilding the solution with warnings on produces ~160 warnings and
# ZERO of them in a port-authored file. The discipline is real. Nothing protected it - the next unused
# variable, unreachable branch or unawaited task in Port/ would have arrived in silence - and that is
# what this closes.
#
#   powershell -ExecutionPolicy Bypass -File Tools/lint-port-code.ps1
#
# It rebuilds the whole solution with -p:WarningLevel=4, which takes about as long as a clean build.
# CS1591 (missing XML doc comment) stays suppressed: this is not a public-API documentation exercise,
# and upstream would drown the signal.

[CmdletBinding()]
param (
    # Report without failing. Useful when raising the bar deliberately.
    [switch] $ReportOnly
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $root 'AspNetCore.Web.slnx'

Write-Output 'Rebuilding with warnings restored - this takes a minute...'

$output = & dotnet build $solution --nologo -v n -t:Rebuild `
    -p:WarningLevel=4 -p:NoWarn=1591 -p:TreatWarningsAsErrors=false 2>&1 | Out-String

# The directories that hold code written for this port. Everything else in the build is upstream,
# compiled in place on purpose, and its warnings are not this script's business.
$authored = '\\(Port|Overrides|Shims)\\'

$all = [regex]::Matches($output, '(?m)^.*warning [A-Z]+\d+.*$') | ForEach-Object { $_.Value.Trim() }
$mine = @($all | Where-Object { $_ -match $authored } | Sort-Object -Unique)

Write-Output ''
Write-Output ("total warnings in the build : {0}" -f $all.Count)
Write-Output ("of which port-authored      : {0}" -f $mine.Count)

if ($mine.Count -eq 0) {
    Write-Output ''
    Write-Output 'Port-authored code is warning-clean.'
    exit 0
}

Write-Output ''
foreach ($warning in $mine) { Write-Output "  $warning" }

Write-Output ''
Write-Output 'These are in code written for this port, not in the upstream tree, so WarningLevel 0'
Write-Output 'is not the answer - fix them, or add a targeted #pragma with a comment saying why.'

if ($ReportOnly) { exit 0 }
exit 1
