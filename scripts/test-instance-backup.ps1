param([string]$BuildDirectory)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationFramework,PresentationCore,WindowsBase,System.Net.Http,System.IO.Compression,System.IO.Compression.FileSystem
$project = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\Plain Craft Launcher 2'))
if (-not $BuildDirectory) { $BuildDirectory = Join-Path $project 'bin' }
$assembly = [Reflection.Assembly]::LoadFrom((Join-Path $BuildDirectory 'BRCL.exe'))
$backup = $assembly.GetType('BRCL.PageInstanceContent',$true).GetMethod('CreateFolderBackup',[Reflection.BindingFlags]'NonPublic,Static')
if ($null -eq $backup) { throw 'Folder backup helper is missing' }
$fixture = Join-Path $env:TEMP ('BRCL-BackupChecks-' + [Guid]::NewGuid().ToString('N'))
$source = Join-Path $fixture 'world'
$outside = Join-Path $fixture 'outside'
$junction = Join-Path $source 'external-link'
New-Item -ItemType Directory -Path $source,$outside,(Join-Path $source 'empty') -Force | Out-Null
[IO.File]::WriteAllText((Join-Path $source 'level.dat'),'world fixture')
[IO.File]::WriteAllText((Join-Path $outside 'keep.txt'),'outside fixture')
try {
    $target = Join-Path $fixture 'world.zip'
    $backup.Invoke($null,[object[]]@([string]$source,[string]$target))
    $archive = [IO.Compression.ZipFile]::OpenRead($target)
    try {
        if ($null -eq $archive.GetEntry('level.dat') -or $null -eq $archive.GetEntry('empty/')) { throw 'Normal backup lost files or empty directories' }
    } finally { $archive.Dispose() }
    Write-Output 'PASS folder backups preserve files and empty directories'

    $original = [IO.File]::ReadAllBytes($target)
    $failed = $false
    try { $backup.Invoke($null,[object[]]@([string]$source,[string]$target)) } catch { $failed = $true }
    if (-not $failed -or [Convert]::ToBase64String([IO.File]::ReadAllBytes($target)) -ne [Convert]::ToBase64String($original)) { throw 'An existing backup was overwritten or deleted' }
    Write-Output 'PASS existing backups are never overwritten or removed by a failed export'

    $nestedTarget = Join-Path $source 'nested.zip'
    $failed = $false
    try { $backup.Invoke($null,[object[]]@([string]$source,[string]$nestedTarget)) } catch { $failed = $true }
    if (-not $failed -or [IO.File]::Exists($nestedTarget)) { throw 'Backup inside the selected folder was accepted' }
    Write-Output 'PASS backups inside their source folder are rejected before creating output'

    New-Item -ItemType Junction -Path $junction -Target $outside | Out-Null
    $unsafeTarget = Join-Path $fixture 'linked.zip'
    $failed = $false
    try { $backup.Invoke($null,[object[]]@([string]$source,[string]$unsafeTarget)) } catch { $failed = $true }
    if (-not $failed -or [IO.File]::Exists($unsafeTarget)) { throw 'A junction was followed or a partial linked archive survived' }
    if ([IO.File]::ReadAllText((Join-Path $outside 'keep.txt')) -ne 'outside fixture') { throw 'Junction export changed its external target' }
    Write-Output 'PASS linked subfolders are rejected, partial output is removed and external data is untouched'
} finally {
    $expectedRoot = [IO.Path]::GetFullPath($env:TEMP).TrimEnd('\') + '\'
    if (-not [IO.Path]::GetFullPath($fixture).StartsWith($expectedRoot,[StringComparison]::OrdinalIgnoreCase) -or -not [IO.Path]::GetFileName($fixture).StartsWith('BRCL-BackupChecks-')) { throw 'Unsafe backup fixture cleanup path' }
    if ([IO.Directory]::Exists($junction)) { [IO.Directory]::Delete($junction) }
    if ([IO.Directory]::Exists($fixture)) { [IO.Directory]::Delete($fixture,$true) }
}
