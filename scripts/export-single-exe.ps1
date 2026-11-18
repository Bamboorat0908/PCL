param(
    [string]$Exe = (Join-Path $PSScriptRoot '..\Plain Craft Launcher 2\bin\BRCL.exe'),
    [Parameter(Mandatory=$true)][string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
$Exe = [IO.Path]::GetFullPath($Exe)
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$assembly = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes($Exe))
$license = $assembly.GetManifestResourceStream('BRCL.LICENCE')
if ($null -eq $license) { throw 'Single-file EXE must contain the embedded BRCL.LICENCE resource.' }
try {
    $copy = New-Object IO.MemoryStream
    try { $license.CopyTo($copy); $licenseBytes = $copy.ToArray() } finally { $copy.Dispose() }
} finally { $license.Dispose() }
$licenseSource = [IO.File]::ReadAllBytes([IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\LICENCE')))
if ([Convert]::ToBase64String($licenseBytes) -cne [Convert]::ToBase64String($licenseSource)) { throw 'Embedded license differs from the repository license.' }
if ((Get-Item -LiteralPath $Exe).Extension -ine '.exe') { throw 'Source must be an EXE.' }
if (Test-Path -LiteralPath $OutputDirectory) {
    $otherFiles = @(Get-ChildItem -LiteralPath $OutputDirectory -Force | Where-Object Name -cne 'BRCL.exe')
    if ($otherFiles.Count -gt 0) { throw 'Single-file output directory must contain only BRCL.exe.' }
} else { New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null }
$destination = Join-Path $OutputDirectory 'BRCL.exe'
if ($Exe -ine $destination) { Copy-Item -LiteralPath $Exe -Destination $destination -Force }
$sourceHash = (Get-FileHash -LiteralPath $Exe -Algorithm SHA256).Hash
if ((Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash -ne $sourceHash) { throw 'Export changed the executable bytes.' }
if (@(Get-ChildItem -LiteralPath $OutputDirectory -File -Force).Count -ne 1) { throw 'Single-file distribution is not a single file.' }
Write-Output 'PASS single EXE export contains the original PE/icon resources and embedded license'
Write-Output $destination
