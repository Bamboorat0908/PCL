$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$libraries = @('Plain Craft Launcher 2\Resources\Newtonsoft.Json.dll', 'Plain Craft Launcher 2\Resources\ThrottleDebounce.dll', 'MeloongCore\Shared\bin\MeloongCore.dll', 'MeloongCore\Wpf\bin\MeloongCore.Wpf.dll', 'PCLCS\bin\BRCL.Core.dll')
foreach ($library in $libraries) {
    [Reflection.Assembly]::LoadFrom((Join-Path $repo $library)) | Out-Null
}
$stage = Join-Path $env:TEMP ('BRCL-PatchTest-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($stage) | Out-Null
$logical = 'Launch\JavaWrapper.jar'
$expected = [BRCL.Core.LaunchUtils].Assembly.GetManifestResourceStream($logical)
$buffer = New-Object IO.MemoryStream
try { $expected.CopyTo($buffer); $bytes = $buffer.ToArray() } finally { $expected.Dispose(); $buffer.Dispose() }
$target = [BRCL.Core.LaunchUtils]::ExtractPatch($logical, $stage)
if ([Convert]::ToBase64String([IO.File]::ReadAllBytes($target)) -ne [Convert]::ToBase64String($bytes)) { throw 'Normal patch extraction differs from the embedded resource.' }
Write-Output 'PASS launch patch extracts byte-for-byte from the assembly'
[IO.File]::WriteAllText($target, 'stale resource fixture')
$locked = [IO.File]::Open($target, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
try {
    $alternate = [BRCL.Core.LaunchUtils]::ExtractPatch($logical, $stage)
    if ($alternate -eq $target -or [IO.Path]::GetFileName($alternate) -ne 'JavaWrapper2.jar') { throw 'Locked patch did not select the alternate destination.' }
    if ([Convert]::ToBase64String([IO.File]::ReadAllBytes($alternate)) -ne [Convert]::ToBase64String($bytes)) { throw 'Alternate patch differs from the original embedded resource.' }
    if ([IO.File]::ReadAllText($target) -ne 'stale resource fixture') { throw 'Locked file was modified.' }
} finally { $locked.Dispose() }
Write-Output 'PASS locked stale patch falls back to another filename and preserves the resource identity'
