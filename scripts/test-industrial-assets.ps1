param([string]$OutputDirectory = 'bin')
$ErrorActionPreference = 'Stop'
$exe = Join-Path $PSScriptRoot "..\Plain Craft Launcher 2\$OutputDirectory\BRCL.exe"
$assembly = [Reflection.Assembly]::LoadFile((Resolve-Path $exe).Path)
$stream = $assembly.GetManifestResourceStream('BRCL.g.resources')
if ($null -eq $stream) { throw 'Missing compiled WPF resources' }
$reader = [Resources.ResourceReader]::new($stream)
$entries = $reader.GetEnumerator()
$keys = @()
while ($entries.MoveNext()) { $keys += [string]$entries.Key }
foreach ($required in @('images/launcherlogo.png','images/launchersplash.png','images/brsserverlogo.png','images/brsmachinist.jpg','images/icon.ico','formmain.baml','pages/pagelaunch/pagelaunchright.baml')) {
    if ($required -notin $keys) { throw "Missing packaged asset: $required. Run a full Rebuild." }
    Write-Output "PASS $required"
}
if ('images/brsmachinist.png' -in $keys) { throw 'The original home image should remain in source only.' }
$reader.Dispose()
Write-Output '7 packaged asset checks passed.'
