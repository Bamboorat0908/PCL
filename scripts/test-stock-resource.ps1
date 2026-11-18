$ErrorActionPreference = 'Stop'
$projectRoot = Join-Path $PSScriptRoot '..\Plain Craft Launcher 2'
$assembly = [Reflection.Assembly]::LoadFrom((Join-Path $projectRoot 'bin\BRCL.exe'))
$stream = $assembly.GetManifestResourceStream('BRCL.StockEmbedded.js')
if ($null -eq $stream) { throw 'Stock layout adapter is missing from the executable' }
$reader = New-Object IO.StreamReader $stream
try { $embedded = $reader.ReadToEnd() }
finally { $reader.Dispose() }
$source = [IO.File]::ReadAllText((Join-Path $projectRoot 'Pages\PageStock\StockEmbedded.js'))
if ($embedded -ne $source) { throw 'Executable contains an outdated stock layout adapter; rebuild it' }
Write-Output 'PASS stock adapter embedded and up to date'
