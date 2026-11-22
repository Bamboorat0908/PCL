# Checks the shipping EXE resources; does not launch a window or install a browser runtime.
param([string]$Exe = (Join-Path $PSScriptRoot '..\Plain Craft Launcher 2\bin\BRCL.exe'))
$ErrorActionPreference = 'Stop'
$assembly = [Reflection.Assembly]::LoadFrom([IO.Path]::GetFullPath($Exe))
$resources = New-Object Resources.ResourceManager('PCL.Resources', $assembly)
$project = Join-Path $PSScriptRoot '..\Plain Craft Launcher 2'
$nativeBytes = @{}
[xml]$definition = Get-Content -LiteralPath (Join-Path $project 'BRCL.vbproj') -Raw -Encoding UTF8
$paths = @($definition.Project.ItemGroup.PackedDependency | Where-Object {$_} | ForEach-Object {$_.Include -split ';'})
if ($paths.Count -ne 18 -or @($assembly.GetManifestResourceNames() -like 'BRCL.Packed.*').Count -ne 18) { throw 'Expected fifteen managed and three native dependencies' }
foreach ($path in $paths) {
    $expected = Get-Item -LiteralPath (Join-Path $project $path)
    $name = $expected.BaseName
    if ($null -ne $resources.GetObject($name)) { throw "Duplicate raw dependency: $name" }
    $stream = $assembly.GetManifestResourceStream("BRCL.Packed.$name")
    if (!$stream -or $stream.Length -ge $expected.Length) { throw "Missing or oversized packed dependency: $name" }
    $reader = New-Object IO.BinaryReader($stream)
    try {
        if ($reader.ReadInt32() -ne $expected.Length) { throw "Incorrect embedded length: $name" }
        $inflater = [IO.Compression.DeflateStream]::new($stream,[IO.Compression.CompressionMode]::Decompress)
        try {
            if ($name -in @('WebView2Loader','WebView2Loader.x86')) {
                $copy = [IO.MemoryStream]::new()
                try { $inflater.CopyTo($copy); $nativeBytes[$name]=$copy.ToArray(); $copy.Position=0; $hash=(Get-FileHash -InputStream $copy).Hash } finally { $copy.Dispose() }
            } else { $hash = (Get-FileHash -InputStream $inflater).Hash }
        } finally { $inflater.Dispose() }
        if ($hash -ne (Get-FileHash -LiteralPath $expected.FullName).Hash) { throw "Embedded dependency differs from source: $name" }
    } finally { $reader.Dispose() }
    Write-Output "PASS lossless packed $name"
}
foreach ($entry in @(@{Key='WebView2Loader';Machine=0x8664}, @{Key='WebView2Loader.x86';Machine=0x14c})) {
    $bytes = $nativeBytes[$entry.Key]
    if ($bytes -isnot [byte[]] -or $bytes.Length -lt 128) { throw "Missing native loader: $($entry.Key)" }
    $offset = [BitConverter]::ToInt32($bytes, 0x3c)
    if ([BitConverter]::ToUInt32($bytes, $offset) -ne 0x4550) { throw 'Invalid PE signature' }
    if ([BitConverter]::ToUInt16($bytes, $offset + 4) -ne $entry.Machine) { throw "Incorrect architecture: $($entry.Key)" }
    Write-Output "PASS embedded $($entry.Key) architecture"
}
$resources.ReleaseAllResources()
if ($resources.GetObject('WebView2Loader64') -or $resources.GetObject('WebView2Loader32')) { throw 'Obsolete raw loader still bundled' }
Write-Output 'All dependency bundle checks passed.'
