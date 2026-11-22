$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationFramework,System.IO.Compression,System.IO.Compression.FileSystem
$project = Join-Path $PSScriptRoot '..\Plain Craft Launcher 2'
[void][Reflection.Assembly]::LoadFrom((Join-Path $project 'Resources\Newtonsoft.Json.dll'))
[void][Reflection.Assembly]::LoadFrom((Join-Path $project 'Resources\ThrottleDebounce.dll'))
$assembly = [Reflection.Assembly]::LoadFrom((Join-Path $project 'bin\BRCL.exe'))
$method = $assembly.GetType('BRCL.ModLaunch',$true).GetMethod('ExtractNativesArchive', [Reflection.BindingFlags]'NonPublic,Static')
if (-not $method) { throw 'Build BRCL with native archive validation before running this test.' }
$root = Join-Path $env:TEMP ('BRCL-NativesTest-' + [guid]::NewGuid().ToString('N'))
$target = Join-Path $root 'natives'
New-Item -ItemType Directory -Path $target -Force | Out-Null
function Check($condition, [string]$message) {
    if (-not $condition) { throw $message }
    Write-Output "PASS $message"
}
function New-Archive([string]$name, [string]$entryName, [string]$body = 'native-fixture') {
    $path = Join-Path $root $name
    $zip = [IO.Compression.ZipFile]::Open($path, [IO.Compression.ZipArchiveMode]::Create)
    try {
        $entry = $zip.CreateEntry($entryName)
        $writer = [IO.StreamWriter]::new($entry.Open())
        try { $writer.Write($body) } finally { $writer.Dispose() }
    } finally { $zip.Dispose() }
    return $path
}
function Extract([string]$path) {
    $files = [Collections.Generic.List[string]]::new()
    [void]$method.Invoke($null, [object[]]@($path.psobject.BaseObject, $target.psobject.BaseObject, $files.psobject.BaseObject))
    return ,$files
}
$valid = New-Archive 'valid.jar' 'nested/library.DLL'
$files = Extract $valid
Check ((Test-Path (Join-Path $target 'nested\library.DLL')) -and $files.Count -eq 1) 'normal nested native DLL extracts inside the target'
$ignored = New-Archive 'ignored.jar' 'META-INF/MANIFEST.MF'
$files = Extract $ignored
Check ($files.Count -eq 0 -and -not (Test-Path (Join-Path $target 'META-INF'))) 'non-DLL archive entries are ignored'
$i = 0
foreach ($entryName in @('../escaped.dll', '..\escaped.dll', '/absolute.dll', '\absolute.dll', 'C:\escaped.dll', 'nested/file:payload.dll')) {
    $i++
    $path = New-Archive ("unsafe-$i.jar") $entryName
    $blocked = $false
    try { Extract $path | Out-Null } catch { $blocked = $_.Exception.GetBaseException() -is [IO.IOException] }
    Check $blocked "unsafe native path is rejected: $entryName"
    $stream = [IO.File]::Open($path, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    $stream.Dispose()
    Check $true "rejected native archive releases its file handle ($i)"
}
Check (-not (Test-Path (Join-Path $root 'escaped.dll'))) 'native traversal creates no file outside the extraction directory'
$outside = Join-Path $root 'outside'
New-Item -ItemType Directory -Path $outside | Out-Null
$link = Join-Path $target 'linked'
New-Item -ItemType Junction -Path $link -Target $outside | Out-Null
$linked = New-Archive 'linked.jar' 'linked/escape.dll'
$blocked = $false
try { Extract $linked | Out-Null } catch { $blocked = $_.Exception.GetBaseException() -is [IO.IOException] }
Check ($blocked -and -not (Test-Path (Join-Path $outside 'escape.dll'))) 'native extraction rejects a pre-existing junction inside the target'
Write-Output "Fixtures: $root"
