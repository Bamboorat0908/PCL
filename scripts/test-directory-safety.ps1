$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression,System.IO.Compression.FileSystem
$project = Join-Path $PSScriptRoot '..\Plain Craft Launcher 2'
[void][Reflection.Assembly]::LoadFrom((Join-Path $project 'Resources\Newtonsoft.Json.dll'))
[void][Reflection.Assembly]::LoadFrom((Join-Path $project 'Resources\ThrottleDebounce.dll'))
[void][Reflection.Assembly]::LoadFrom((Join-Path $project 'bin\MeloongCore.dll'))
$root = Join-Path $env:TEMP ('BRCL-DirectorySafety-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root | Out-Null
function Check($condition, [string]$message) {
    if (-not $condition) { throw $message }
    Write-Output "PASS $message"
}
$parent = Join-Path $root 'parent'
$child = Join-Path $parent 'child'
New-Item -ItemType Directory -Path $child -Force | Out-Null
[IO.File]::WriteAllText((Join-Path $parent 'parent.txt'),'parent')
[IO.File]::WriteAllText((Join-Path $child 'child.txt'),'child')
foreach ($moveToParent in @($true, $false)) {
    $source = if ($moveToParent) { $child } else { $parent }
    $destination = if ($moveToParent) { $parent } else { $child }
    $blocked = $false
    try { [MeloongCore.DirectoryUtils]::Move($source, $destination) } catch { $blocked = $_.Exception.GetBaseException() -is [IO.IOException] }
    Check ($blocked -and [IO.File]::ReadAllText((Join-Path $parent 'parent.txt')) -eq 'parent' -and [IO.File]::ReadAllText((Join-Path $child 'child.txt')) -eq 'child') "overlapping move is rejected without removing source or destination ($moveToParent)"
}
$outside = Join-Path $root 'outside'
$deleteRoot = Join-Path $root 'delete-me'
New-Item -ItemType Directory -Path $outside,$deleteRoot | Out-Null
$sentinel = Join-Path $outside 'keep.txt'
[IO.File]::WriteAllText($sentinel,'preserve-external-data')
New-Item -ItemType Junction -Path (Join-Path $deleteRoot 'linked') -Target $outside | Out-Null
[MeloongCore.DirectoryUtils]::Delete($deleteRoot) | Out-Null
Check (-not (Test-Path -LiteralPath $deleteRoot)) 'recursive deletion removes the requested directory and its junction'
Check ((Test-Path -LiteralPath $sentinel) -and [IO.File]::ReadAllText($sentinel) -eq 'preserve-external-data') 'recursive deletion preserves files in the external junction target'
$directLink = Join-Path $root 'direct-link'
New-Item -ItemType Junction -Path $directLink -Target $outside | Out-Null
[MeloongCore.DirectoryUtils]::Delete($directLink) | Out-Null
Check (-not (Test-Path -LiteralPath $directLink) -and (Test-Path -LiteralPath $sentinel)) 'deleting a junction directly removes only the link'
$extractRoot = Join-Path $root 'extract'
New-Item -ItemType Directory -Path $extractRoot | Out-Null
New-Item -ItemType Junction -Path (Join-Path $extractRoot 'linked') -Target $outside | Out-Null
$zipPath = Join-Path $root 'linked.zip'
$zip = [IO.Compression.ZipFile]::Open($zipPath,[IO.Compression.ZipArchiveMode]::Create)
$entry = $zip.CreateEntry('linked/keep.txt')
$writer = [IO.StreamWriter]::new($entry.Open())
try { $writer.Write('overwritten') } finally { $writer.Dispose(); $zip.Dispose() }
$extract = [MeloongCore.FileUtils].GetMethod('ExtractToDirectory')
$blocked = $false
try { $extract.Invoke($null, [object[]]@($zipPath.psobject.BaseObject,$extractRoot.psobject.BaseObject,[Threading.CancellationToken]::None,$null)) } catch { $blocked = $_.Exception.GetBaseException() -is [IO.InvalidDataException] }
Check ($blocked -and [IO.File]::ReadAllText($sentinel) -eq 'preserve-external-data') 'ZIP extraction rejects a junction below the output directory and preserves external content'
$gzipPath = Join-Path $root 'keep.txt.gz'
$gzipFile = [IO.File]::Create($gzipPath)
$gzip = [IO.Compression.GZipStream]::new($gzipFile,[IO.Compression.CompressionMode]::Compress)
$writer = [IO.StreamWriter]::new($gzip)
try { $writer.Write('overwritten') } finally { $writer.Dispose(); $gzipFile.Dispose() }
$extractLink = Join-Path $root 'extract-link'
New-Item -ItemType Junction -Path $extractLink -Target $outside | Out-Null
$blocked = $false
try { $extract.Invoke($null, [object[]]@($gzipPath.psobject.BaseObject,$extractLink.psobject.BaseObject,[Threading.CancellationToken]::None,$null)) } catch { $blocked = $_.Exception.GetBaseException() -is [IO.InvalidDataException] }
Check ($blocked -and [IO.File]::ReadAllText($sentinel) -eq 'preserve-external-data') 'GZip extraction rejects an output directory that is itself a junction'
Write-Output "Fixtures: $root"
