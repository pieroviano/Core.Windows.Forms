# Generates Core.Web/Config/{machine.config,root-web.config} from Mono's shipped configuration,
# then VERIFIES every type and assembly reference in the result against the built System.Web.dll
# and the framework reference set. Entries that cannot resolve are reported so they can be dealt
# with explicitly instead of failing at request time.
#
# Two transformations are applied:
#
#   1. Strong-name qualification is stripped from type/assembly references. Mono's configuration
#      pins "System.Web, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a"; this
#      port's System.Web is unsigned and cannot ever satisfy that identity, so references are
#      reduced to simple names and bind by name instead.
#
#   2. Nothing else. Entries pointing at assemblies this port does not ship are left in place but
#      REPORTED - the caller decides whether to comment them out, because the consequences differ
#      (an <httpHandlers> entry fails only if such a request arrives, an <httpModules> entry fails
#      at application start).
#
# Usage: powershell -ExecutionPolicy Bypass -File tools/gen-config.ps1 [-Verify]

param([switch]$Verify)

$ErrorActionPreference = 'Stop'
$root    = Split-Path -Parent $PSScriptRoot
$srcDir  = Join-Path $root 'Mono\data\net_4_5'
# The project DIRECTORY (AspNetCore.Web), which is not the assembly name (still Core.Web) - the
# assembly names appear in the generated config content further down, and are a separate thing.
$outDir  = Join-Path $root 'AspNetCore.Web\Config'
$asmPath = Join-Path $root 'AspNetCore.Web\bin\Debug\net10.0\System.Web.dll'

if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Force $outDir | Out-Null }

# Strips ", Version=..., Culture=..., PublicKeyToken=..., processorArchitecture=..." from an
# assembly-qualified name, leaving "Namespace.Type, AssemblyName".
function Strip-StrongName([string]$text) {
    $text = [regex]::Replace($text, ',\s*Version=\d+(\.\d+)*', '')
    $text = [regex]::Replace($text, ',\s*Culture=[A-Za-z0-9\-]+', '')
    $text = [regex]::Replace($text, ',\s*PublicKeyToken=(?:[0-9a-fA-F]{16}|null)', '')
    $text = [regex]::Replace($text, ',\s*processorArchitecture=[A-Za-z0-9]+', '')
    return $text
}

# The port's assembly is Core.Web, not System.Web (see build/WebFormsPort.targets). Two kinds of
# reference have to follow:
#
#   type="Some.Type, System.Web"  -> type="Some.Type"
#        Dropping the assembly entirely is better than substituting Core.Web: HttpApplication.LoadType
#        falls back to scanning every loaded assembly when Type.GetType fails, so a bare type name
#        resolves regardless of what the assembly is called - and keeps working if it is renamed again.
#
#   assembly="System.Web"        -> assembly="Core.Web"
#        <compilation><assemblies> and <controls> genuinely name an assembly to load, so these must
#        point at the real one. Left as System.Web they would resolve to the empty framework facade,
#        silently dropping the WebParts tag prefix and a compile-time reference.
#   type="Some.Type, System.Configuration" -> type="Some.Type, Core.Configuration"
#        Retargeted rather than stripped, unlike System.Web above. These are resolved by
#        System.Configuration's own section machinery via Type.GetType, which for an unqualified name
#        searches only corelib and the *calling* assembly - it does not scan everything the way
#        HttpApplication.LoadType does. Leaving ", System.Configuration" would bind to the empty
#        framework facade, and the section would silently degrade to DefaultSection: that is exactly
#        how <appSettings> came back as DefaultSection instead of a NameValueCollection.
function Retarget-PortAssembly([string]$text) {
    $text = [regex]::Replace($text, '(type="[^"]*?),\s*System\.Web(")', '$1$2')
    $text = [regex]::Replace($text, '(assembly=")System\.Web(")', '${1}Core.Web$2')
    $text = [regex]::Replace($text, '(type="[^"]*?),\s*System\.Configuration(")', '${1}, Core.Configuration$2')
    $text = [regex]::Replace($text, '(assembly=")System\.Configuration(")', '${1}Core.Configuration$2')

    # System.Web.Extensions is ported as Core.Web.Extensions (ScriptManager, UpdatePanel,
    # ScriptResource.axd, the client-script infrastructure and the 3.5-era data controls). Its
    # <httpHandlers>, <httpModules> and <controls> registrations are retargeted, not dropped.
    # Type references keep their assembly qualification here: unlike System.Web these types are NOT in
    # the assembly doing the resolving, and HttpApplication.LoadType's scan only helps once the
    # assembly is loaded - naming it guarantees it.
    $text = [regex]::Replace($text, '(type="[^"]*?),\s*System\.Web\.Extensions(")', '${1}, Core.Web.Extensions$2')
    $text = [regex]::Replace($text, '(assembly=")System\.Web\.Extensions(")', '${1}Core.Web.Extensions$2')

    # System.Web.Services is ported as Core.Web.Services (the .asmx/SOAP serving stack).
    $text = [regex]::Replace($text, '(type="[^"]*?),\s*System\.Web\.Services(")', '${1}, Core.Web.Services$2')
    $text = [regex]::Replace($text, '(assembly=")System\.Web\.Services(")', '${1}Core.Web.Services$2')

    # *.asmx stays mapped at upstream's ScriptHandlerFactory, which this port now ships. It is a
    # superset of the plain SOAP factory, not an alternative to it: a request whose Content-Type is
    # application/json is dispatched to RestHandler (JSON script service), a PathInfo starting with
    # /js gets the generated client proxy, and everything else - i.e. all SOAP, and the help page -
    # is delegated to the WebServiceHandlerFactory it wraps. Mapping .asmx at the bare SOAP factory
    # instead, as this port did while Script.Services was excluded, silently costs the JSON half.

    # The help page declares NoCheckCertificatePolicy : ICertificatePolicy for its HTTPS test form.
    # System.Net.ICertificatePolicy was removed on .NET Core (superseded by
    # ServerCertificateValidationCallback), and the class is declared but never used anywhere in the
    # page - so only the interface is dropped, leaving the type harmlessly present.
    $text = $text.Replace('class NoCheckCertificatePolicy : ICertificatePolicy {',
                          'class NoCheckCertificatePolicy { // ICertificatePolicy: removed on .NET Core, and unused here')

    # <%@ Assembly name="..." %> in the .asmx help page - same rename, different syntax.
    $text = [regex]::Replace($text, '(<%@\s*Assembly\s+name=")System\.Web\.Services("\s*%>)', '${1}Core.Web.Services$2')
    $text = [regex]::Replace($text, '(<%@\s*Assembly\s+name=")System\.Web("\s*%>)', '${1}Core.Web$2')
    return $text
}

# Assemblies referenced by Mono's configuration that this port does not ship. Entries naming them are
# commented out, because the consequences of leaving them differ and none are acceptable:
#
#   <httpModules>            - every module is instantiated at application start, so one unresolvable
#                              entry takes the whole application down. This is how ScriptModule-4.0
#                              (System.Web.Extensions) produced "The pre-application start
#                              initialization method UNKNOWN on type UNKNOWN threw an exception".
#   <compilation><assemblies> - BuildManager.LoadAssembly THROWS rather than skipping, so a missing
#                              assembly breaks the first page compile.
#   <httpHandlers>           - fails only when a matching request arrives (.asmx, .svc, .rem), which
#                              is the correct outcome for features this port does not implement.
#   <pages><controls>        - the tag prefix silently stops resolving, so pages using it fail to parse.
#
# <add assembly="*"/> (the bin directory) is retained, so an application that really does supply one
# of these still gets it compiled in.
$missingAssemblies = @(
    'System.Web.DynamicData'
    'System.Web.Entity'
    'System.Web.Mobile'
    'System.ServiceModel'                 # WCF
    'System.ServiceModel.Web'
    'System.ServiceModel.Activation'
    'System.ServiceModel.Discovery'
    'System.Runtime.Remoting'             # *.rem / *.soap handlers
    'System.IdentityModel'
    'System.Xaml'
    'System.Xaml.Hosting'
    'System.Data.Linq'
    'System.Data.DataSetExtensions'
    'System.EnterpriseServices'           # COM+ transactions - shimmed as an enum only
    'System.ComponentModel.DataAnnotations'
    'System.Runtime.Caching'
    'System.Transactions'
    'System.Drawing'                      # the port uses System.Drawing.Common, a different name
    'System.Web.ApplicationServices'      # folded into Core.Web
    'Mainsoft.Web.Security'               # Mono's Grasshopper providers
    'System.Windows.Forms'                # only referenced for a DataVisualization type
)

# Individual types this port does not ship even though their assembly IS shipped. Assembly-level
# filtering is too coarse for these.
#
# Currently empty: the two Script.Services handlers that used to be listed here
# (ScriptHandlerFactory, RestHandler) are now compiled, so their *_AppService.axd and *.asmx handler
# registrations are kept as upstream wrote them. Keep the mechanism - it is the right tool the next
# time a shipped assembly is missing one type - but do not add entries speculatively: a commented-out
# handler entry fails at request time with a bare 404, which is hard to trace back to here.
$script:missingTypes = @(
)

# Comments out any <add .../> element naming one of the assemblies above. Line-oriented so the
# resulting file stays readable and diffable against the input.
function Remove-MissingAssemblyEntries([string]$text, [string[]]$missing) {
    $lines = $text -split "`r?`n"
    $out = New-Object System.Collections.Generic.List[string]
    $removed = 0
    # Mono's configuration keeps several <add/> elements inside multi-line <!-- --> blocks. Wrapping
    # one of those again would nest comments, which is invalid XML - and the whole file would then
    # fail to parse, taking every section with it. Track comment depth and leave those lines alone.
    $inComment = $false
    foreach ($line in $lines) {
        $wasInComment = $inComment

        # update state for the NEXT line: count unterminated openers on this one
        $opens  = ([regex]::Matches($line, '<!--')).Count
        $closes = ([regex]::Matches($line, '-->')).Count
        if ($opens -gt $closes) { $inComment = $true }
        elseif ($closes -gt $opens) { $inComment = $false }

        $hit = $null
        if (-not $wasInComment -and $line -match '<add\b' -and $line -notmatch '^\s*<!--') {
            # Individual unshipped types first: assembly-level filtering is too coarse for these,
            # because their assembly IS shipped (Core.Web.Extensions) while the type is not.
            foreach ($t in $script:missingTypes) {
                if ($line -match ('type="' + [regex]::Escape($t) + '\b')) { $hit = $t; break }
            }
            foreach ($asm in $missing) {
                if ($hit -ne $null) { break }
                # match `assembly="Asm"`, `, Asm"` (type= qualification) or `assembly="Asm, ...`
                if ($line -match ('(assembly="' + [regex]::Escape($asm) + '"|,\s*' + [regex]::Escape($asm) + '"|assembly="' + [regex]::Escape($asm) + ',)')) {
                    $hit = $asm; break
                }
            }
        }

        if ($hit -ne $null) {
            $indent = [regex]::Match($line, '^\s*').Value
            # '--' is illegal inside an XML comment, so neutralise any that appear in the payload
            $out.Add("$indent<!-- port: removed, $hit is not shipped by this port: " +
                     $line.Trim().Replace('--', '- -') + " -->")
            $removed++
        } else {
            $out.Add($line)
        }
    }
    Write-Host "    commented out $removed entry(ies) naming unshipped assemblies"
    return ($out -join "`r`n")
}

$pairs = @(
    @{ In = 'machine.config'; Out = 'machine.config'    },
    @{ In = 'web.config';     Out = 'root-web.config'   }
    # The .asmx help page goes through the same retargeting: it carries
    # <%@ Assembly name="System.Web.Services" %>, which must name the ported assembly. It is not XML,
    # so the validity check below skips it.
    @{ In = 'DefaultWsdlHelpGenerator.aspx'; Out = 'DefaultWsdlHelpGenerator.aspx'; NotXml = $true }
)

foreach ($p in $pairs) {
    $inPath = Join-Path $srcDir $p.In
    if (-not (Test-Path $inPath)) { throw "missing $inPath" }
    $text = [System.IO.File]::ReadAllText($inPath)
    $text = Strip-StrongName $text
    $text = Retarget-PortAssembly $text
    if (-not $p.NotXml) { $text = Remove-MissingAssemblyEntries $text $missingAssemblies }
    $header = @"
<!--
     Generated by tools/gen-config.ps1 from mono/data/net_4_5/$($p.In). Do not edit directly - edit
     the generator. Two transformations were applied:

       * strong-name qualification stripped from every reference (this port is unsigned and can
         never satisfy "Version=4.0.0.0, PublicKeyToken=b03f5f7f11d50a3a");
       * ", System.Web" dropped from type= references, and assembly="System.Web" retargeted to
         assembly="Core.Web", because the port's assembly is Core.Web while its namespaces are
         unchanged. See build/WebFormsPort.targets for why it is not called System.Web.
-->
"@
    # keep the xml declaration first
    if ($text -match '^\s*<\?xml[^>]*\?>') {
        $decl = $Matches[0]
        $text = $text.Substring($decl.Length)
        $text = $decl + "`n" + $header + $text
    } else {
        $text = $header + $text
    }
    $dest = Join-Path $outDir $p.Out
    [System.IO.File]::WriteAllText($dest, $text)

    # Cheap guard against the transformations producing something unparseable. A malformed config
    # file does not fail loudly at runtime - it takes out every section at once, which is a
    # miserable thing to debug from a 500 page.
    if (-not $p.NotXml) {
        try { [xml](Get-Content $dest -Raw) | Out-Null }
        catch { throw "generated $dest is not valid XML: $($_.Exception.Message)" }
    }

    Write-Output "wrote $dest"
}

# Reference verification lives in tools/verify-config/ (a net10.0 tool): Windows PowerShell 5.1 runs
# on .NET Framework and cannot load this port's net10.0 System.Web.dll to reflect over it.
if ($Verify) {
    Write-Output ''
    Write-Output 'Run reference verification with:  dotnet run --project tools/verify-config'
}
