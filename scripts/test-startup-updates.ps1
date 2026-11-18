param([string]$BuildDirectory)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (-not $BuildDirectory) { $BuildDirectory = Join-Path $repo 'Plain Craft Launcher 2\bin' }
Add-Type -AssemblyName PresentationFramework,System.Net.Http
[void][Reflection.Assembly]::LoadFrom((Join-Path $repo 'Plain Craft Launcher 2\Resources\Newtonsoft.Json.dll'))
$assembly = [Reflection.Assembly]::LoadFrom((Join-Path $BuildDirectory 'BRCL.exe'))
$module = $assembly.GetType('PCL.ModBrclUpdate',$true)
$flags = [Reflection.BindingFlags]'NonPublic,Static'
$present = $module.GetMethod('ShouldPresentUpdate',$flags)
foreach ($manual in @($false,$true)) {
    foreach ($critical in @($false,$true)) {
        foreach ($preference in 0..3) {
            $expected = $manual -or $preference -lt 2 -or ($preference -eq 2 -and $critical)
            $actual = $present.Invoke($null,[object[]]@($manual,$preference,$critical))
            if ($actual -ne $expected) { throw "Wrong notification policy: manual=$manual preference=$preference critical=$critical" }
        }
    }
}
Write-Output 'PASS automatic and manual checks preserve ordinary/critical/silent notification preferences'

$reserve = $module.GetMethod('TryReserveStartupCheck',$flags)
$scheduled = $module.GetField('StartupCheckScheduled',$flags)
$scheduled.SetValue($null,0)
Add-Type -TypeDefinition @'
using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
public static class StartupUpdateRace {
    public static int Run(MethodInfo reserve) {
        int accepted=0;
        Parallel.For(0,1000,i=> { if((bool)reserve.Invoke(null,null)) Interlocked.Increment(ref accepted); });
        return accepted;
    }
}
'@
if ([StartupUpdateRace]::Run($reserve) -ne 1 -or $reserve.Invoke($null,@())) { throw 'Startup check was not reserved exactly once per process' }
Write-Output 'PASS concurrent startup requests reserve exactly one automatic check'

$previousPreview = $env:BRCL_UI_PREVIEW
try {
    $env:BRCL_UI_PREVIEW = '1'
    $scheduled.SetValue($null,0)
    $module.GetMethod('StartStartupUpdateCheck').Invoke($null,@())
    if ($scheduled.GetValue($null) -ne 0) { throw 'Preview reserved or started a real network check' }
} finally { $env:BRCL_UI_PREVIEW = $previousPreview }
Write-Output 'PASS UI preview avoids real update traffic and preserves the startup reservation'

$verify = $module.GetMethod('ReadVerifiedManifest',$flags)
$fixture = Join-Path $repo 'artifacts\updates\2026.11.13.9'
$bytes = [IO.File]::ReadAllBytes((Join-Path $fixture 'latest.json'))
$signature = [Convert]::FromBase64String([IO.File]::ReadAllText((Join-Path $fixture 'latest.sig')).Trim())
$manifest = $verify.Invoke($null,[object[]]@($bytes,$signature))
if ([string]$manifest['version'] -ne '2026.11.13.9') { throw 'A valid signed release manifest was rejected or changed' }
Write-Output 'PASS real previously published manifest verifies with the embedded public key'
foreach ($case in @('tampered-manifest','tampered-signature','empty','oversized')) {
    [byte[]]$testBytes = $bytes.Clone()
    [byte[]]$testSignature = $signature.Clone()
    switch ($case) {
        tampered-manifest { $testBytes[0] = $testBytes[0] -bxor 1 }
        tampered-signature { $testSignature[0] = $testSignature[0] -bxor 1 }
        empty { $testBytes = [byte[]]@() }
        oversized { $testBytes = New-Object byte[] 65537 }
    }
    $rejected = $false
    try { [void]$verify.Invoke($null,[object[]]@($testBytes,$testSignature)) } catch { $rejected = $true }
    if (-not $rejected) { throw "Unsafe manifest accepted: $case" }
}
Write-Output 'PASS altered signatures/data and unbounded manifests are rejected before processing'

$read = $module.GetMethod('ReadBoundedData',$flags)
foreach ($size in @(65535,65536,65537)) {
    $stream = New-Object IO.MemoryStream(,(New-Object byte[] $size))
    try {
        $rejected = $false
        try { $result = $read.Invoke($null,[object[]]@($stream.PSObject.BaseObject,65536)) } catch { $rejected = $true }
        if (($size -gt 65536) -ne $rejected -or (-not $rejected -and $result.Length -ne $size)) { throw "Response limit failed at boundary $size" }
    } finally { $stream.Dispose() }
}
Write-Output 'PASS response streaming bounds memory before manifest validation, including the exact size limit'

$copy = $module.GetMethod('CopyBoundedPackage',$flags)
foreach ($size in @(65535,65536,65537,131073)) {
    $inputStream = New-Object IO.MemoryStream(,(New-Object byte[] $size))
    $outputStream = New-Object IO.MemoryStream
    try {
        $rejected = $false
        try { $copy.Invoke($null,[object[]]@($inputStream.PSObject.BaseObject,$outputStream.PSObject.BaseObject,[long]65536)) } catch { $rejected = $true }
        if (($size -ne 65536) -ne $rejected -or $outputStream.Length -gt 65536) { throw "Package disk limit failed for $size bytes" }
        if ($size -gt 65536 -and $inputStream.Position -gt 65537) { throw 'Oversized package continued downloading after its signed boundary' }
        if (-not $rejected -and $outputStream.Length -ne $size) { throw 'Exact-size package lost bytes' }
    } finally { $inputStream.Dispose(); $outputStream.Dispose() }
}
Write-Output 'PASS exact-size packages copy fully; truncated/oversized responses stop at the signed boundary'

$clientType = $module.GetNestedType('UpdateClient',[Reflection.BindingFlags]::NonPublic)
$constructor = $clientType.GetConstructor([Type[]]@([int]))
$requestMethod = $clientType.GetMethod('GetWebRequest',[Reflection.BindingFlags]'NonPublic,Instance')
foreach ($timeout in @(15000,120000)) {
    $client = $constructor.Invoke([object[]]@($timeout))
    try {
        $request = $requestMethod.Invoke($client,[object[]]@([Uri]'http://127.0.0.1/latest.json'))
        if ($request.Timeout -ne $timeout -or $request.ReadWriteTimeout -ne $timeout -or $request.AllowAutoRedirect -or $request.CachePolicy.Level -ne [Net.Cache.RequestCacheLevel]::NoCacheNoStore) {
            throw "Incorrect fresh-check, redirect or timeout policy: $timeout"
        }
    } finally { $client.Dispose() }
}
Write-Output 'PASS update requests bypass stale caches, reject redirects and bound connection/read waits'
