$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$root = Join-Path $env:TEMP ('BRCL-UpdateTest-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root | Out-Null
$cs = Join-Path $root 'fixture.cs'
[IO.File]::WriteAllText($cs, 'class Test { static void Main() { System.IO.File.WriteAllText("started.txt", "ok"); } }')
$fixture = Join-Path $root 'fixture.exe'
& "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:winexe "/out:$fixture" $cs
if ($LASTEXITCODE -ne 0) { throw 'Fixture compilation failed.' }
Add-Type -AssemblyName System.IO.Compression.FileSystem
foreach ($case in @('success', 'traversal', 'rollback', 'single-exe', 'single-exe-rollback', 'single-exe-renamed', 'single-exe-renamed-rollback', 'single-exe-unicode', 'single-exe-unicode-rollback', 'invalid-exe-name')) {
    $singleExe = $case -like 'single-exe*'
    $mustRollback = $case -in @('rollback','single-exe-rollback','single-exe-renamed-rollback','single-exe-unicode-rollback')
    $unicode = $case -like 'single-exe-unicode*'
    $unicodeName = [string][char]0x6E38 + [char]0x620F
    $targetExeName = if ($unicode) { "BRCL $unicodeName.exe" } elseif ($case -like 'single-exe-renamed*') { 'BRCL (1).exe' } else { 'BRCL.exe' }
    $stage = Join-Path $root $case
    $target = Join-Path $stage $(if ($unicode) { $unicodeName } else { 'launcher' })
    $payload = Join-Path $stage 'package-source'
    New-Item -ItemType Directory -Path $target,$payload,(Join-Path $target '.minecraft') -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $target $targetExeName), 'old exe')
    [IO.File]::WriteAllText((Join-Path $target 'PCLCS.dll'), 'old dll')
    $originalConfig = '<configuration><startup><supportedRuntime version="v4.0" /></startup></configuration>'
    [IO.File]::WriteAllText((Join-Path $target 'BRCL.exe.config'), $originalConfig)
    [IO.File]::WriteAllText((Join-Path $target '.minecraft\keep.txt'), 'preserve game data')
    if ($mustRollback) { [IO.File]::WriteAllText((Join-Path $payload 'BRCL.exe'), 'invalid exe') }
    else { Copy-Item -LiteralPath $fixture -Destination (Join-Path $payload 'BRCL.exe') }
    if (-not $singleExe) { [IO.File]::WriteAllText((Join-Path $payload 'PCLCS.dll'), 'new dll') }
    $archive = Join-Path $stage 'release.zip'
    [IO.Compression.ZipFile]::CreateFromDirectory($payload, $archive)
    if ($case -eq 'traversal') {
        $zip = [IO.Compression.ZipFile]::Open($archive, [IO.Compression.ZipArchiveMode]::Update)
        $entry = $zip.CreateEntry('../escaped.txt')
        $zip.Dispose()
    }
    Copy-Item -LiteralPath (Join-Path $repo 'Plain Craft Launcher 2\Modules\BrclInstall.ps1') -Destination (Join-Path $stage 'install.ps1')
    $requestedExeName = if ($case -eq 'invalid-exe-name') { '..\escaped.exe' } else { $targetExeName }
    [IO.File]::WriteAllText((Join-Path $stage 'install.json'), (@{pid=2147483647; target=$target; exe=$requestedExeName} | ConvertTo-Json))
    & "$env:WINDIR\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -File (Join-Path $stage 'install.ps1') -Quiet
    $exitCode = $LASTEXITCODE
    if ($case -in @('success','single-exe','single-exe-renamed','single-exe-unicode')) {
        if ($exitCode -ne 0) { throw 'Successful install failed.' }
        $deadline = (Get-Date).AddSeconds(5)
        while (-not (Test-Path -LiteralPath (Join-Path $target 'started.txt')) -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 100 }
        if (-not (Test-Path -LiteralPath (Join-Path $target 'started.txt'))) { throw 'Launcher restart failed.' }
        $expectedDll = if ($singleExe) { 'old dll' } else { 'new dll' }
        if ([IO.File]::ReadAllText((Join-Path $target 'PCLCS.dll')) -ne $expectedDll) { throw 'DLL replacement/preservation failed.' }
        $backup = Get-ChildItem -LiteralPath (Join-Path $target 'BRCL\UpdateBackups') -Directory | Select-Object -First 1
        if ([IO.File]::ReadAllText((Join-Path $backup.FullName $targetExeName)) -ne 'old exe') { throw 'Backup failed.' }
    } else {
        if ($exitCode -eq 0) { throw "Expected rejection: $case" }
        $diagnostic = Get-Content -LiteralPath (Join-Path $stage 'error.txt') -Raw
        if ([string]::IsNullOrWhiteSpace($diagnostic) -or $diagnostic -match 'Rollback failed') { throw "Unexpected update recovery result: $case" }
        if ([IO.File]::ReadAllText((Join-Path $target $targetExeName)) -ne 'old exe' -or [IO.File]::ReadAllText((Join-Path $target 'PCLCS.dll')) -ne 'old dll') { throw "Original files changed: $case" }
        if (Test-Path -LiteralPath (Join-Path $stage 'escaped.txt')) { throw 'Path traversal escaped staging.' }
    }
    if ($targetExeName -ne 'BRCL.exe' -and (Test-Path -LiteralPath (Join-Path $target 'BRCL.exe'))) { throw 'Renamed launcher update wrote a different EXE instead of replacing the running file.' }
    if ([IO.File]::ReadAllText((Join-Path $target 'BRCL.exe.config')) -cne $originalConfig) { throw 'Existing application configuration was unexpectedly changed.' }
    if ([IO.File]::ReadAllText((Join-Path $target '.minecraft\keep.txt')) -ne 'preserve game data') { throw 'Game data changed.' }
    Write-Output "PASS $case"
}
Write-Output "Test artifacts: $root"
