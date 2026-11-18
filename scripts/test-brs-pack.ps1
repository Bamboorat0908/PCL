param([string]$Archive = 'C:\Users\BRPC\Downloads\TRM 1.11 高预设.zip', [switch]$LowPreset)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
[Reflection.Assembly]::LoadFrom((Join-Path $env:USERPROFILE '.nuget\packages\newtonsoft.json\13.0.3\lib\net45\Newtonsoft.Json.dll')) | Out-Null
[Reflection.Assembly]::LoadFrom((Join-Path $repo 'Plain Craft Launcher 2\Resources\ThrottleDebounce.dll')) | Out-Null
$assembly = [Reflection.Assembly]::LoadFrom((Join-Path $repo 'Plain Craft Launcher 2\bin\BRCL.exe'))
$module = $assembly.GetType('PCL.ModBrsPack', $true)
$method = $module.GetMethod('IsTrmInstanceName', [Reflection.BindingFlags]'Public,NonPublic,Static')
foreach ($name in @('TRM','TRM 1.10 高预设','BRS-TRM-1.10','trm_1.10','The Retired Machinist 1.10','退休机械工')) {
    if (-not $method.Invoke($null, @($name))) { throw "TRM instance not detected: $name" }
}
foreach ($name in @('','1.21.1','NeoForge 21.1.252','ATM10','NOTRM','TRMfoo')) {
    if ($method.Invoke($null, @($name))) { throw "Unrelated instance incorrectly detected: $name" }
}
Write-Output 'PASS compiled TRM instance name detection and unrelated instance rejection'
$checkerType = [Reflection.Assembly]::LoadFrom((Join-Path $repo 'Plain Craft Launcher 2\bin\MeloongCore.dll')).GetType('MeloongCore.FileChecker')
if (-not $checkerType) { $checkerType = [AppDomain]::CurrentDomain.GetAssemblies() | ForEach-Object { $_.GetTypes() } | Where-Object Name -eq 'FileChecker' | Select-Object -First 1 }
$checker = [Activator]::CreateInstance($checkerType)
$preset = $module.GetMethod('GetBrsPackPreset', [Reflection.BindingFlags]'Public,NonPublic,Static').Invoke($null, @([bool]$LowPreset))
$checker.Hash = $preset.Hash
$checker.ActualSize = $preset.Size
$validation = $checker.Check($Archive)
if ($null -ne $validation) { throw "Original archive failed validation: $validation" }
$badFile = Join-Path $env:TEMP ('BRCL-bad-pack-' + [Guid]::NewGuid().ToString('N') + '.zip')
[IO.File]::WriteAllText($badFile, 'corrupted archive')
if ($null -eq $checker.Check($badFile)) { throw 'Corrupt archive accepted.' }
Write-Output 'PASS installer file checker accepts original and rejects corrupt archive'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [IO.Compression.ZipFile]::OpenRead($Archive)
try {
    $entry = $zip.GetEntry('modrinth.index.json')
    $reader = [IO.StreamReader]::new($entry.Open())
    $index = $reader.ReadToEnd() | ConvertFrom-Json
    $reader.Dispose()
    if ($index.name -ne 'TRM' -or $index.versionId -ne '1.11' -or $index.dependencies.minecraft -ne '1.21.1' -or $index.dependencies.neoforge -ne '21.1.252') { throw 'Unexpected pack metadata.' }
    foreach ($path in @($zip.Entries.FullName) + @($index.files.path)) {
        if ($path -match '(^[/\\]|:|(^|[/\\])\.\.([/\\]|$))') { throw "Unsafe archive path: $path" }
    }
    foreach ($file in $index.files) {
        if ($file.hashes.sha1 -notmatch '^[a-fA-F0-9]{40}$' -or -not $file.downloads.Count) { throw 'Missing mod download or checksum.' }
    }
    Write-Output ("PASS Modrinth metadata, NeoForge dependencies, safe paths and {0} hashed mod downloads" -f $index.files.Count)
} finally { $zip.Dispose() }
