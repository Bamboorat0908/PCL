# Update installer housekeeping: old backups are bounded and staged files are released after success.
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$root = Join-Path $env:TEMP ('BRCL-UpdateHousekeeping-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root | Out-Null
function Require([bool]$value, [string]$message) { if (-not $value) { throw $message } }
try {
    $cs = Join-Path $root 'fixture.cs'
    [IO.File]::WriteAllText($cs, 'class Test { static void Main() { System.IO.File.WriteAllText("started.txt", "ok"); } }')
    $fixture = Join-Path $root 'fixture.exe'
    & "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:winexe "/out:$fixture" $cs
    if ($LASTEXITCODE -ne 0) { throw 'Fixture compilation failed.' }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $stage = Join-Path $root 'stage'
    $target = Join-Path $root 'launcher'
    $source = Join-Path $root 'package-source'
    New-Item -ItemType Directory -Path $stage, $target, $source | Out-Null
    [IO.File]::WriteAllText((Join-Path $target 'BRCL.exe'), 'old exe')
    $backups = Join-Path $target 'BRCL\UpdateBackups'
    $old = @('20250101-000000-0001','20250102-000000-0001','20250103-000000-0001','20250104-000000-0001','20250105-000000-0001','20250106-000000-0001','20250107-000000-0001')
    foreach ($name in $old) { New-Item -ItemType Directory -Path (Join-Path $backups $name) -Force | Out-Null; [IO.File]::WriteAllText((Join-Path $backups "$name\BRCL.exe"), $name) }
    New-Item -ItemType Directory -Path (Join-Path $backups 'manual-keep') | Out-Null
    Copy-Item -LiteralPath $fixture -Destination (Join-Path $source 'BRCL.exe')
    [IO.Compression.ZipFile]::CreateFromDirectory($source, (Join-Path $stage 'release.zip'))
    Copy-Item -LiteralPath (Join-Path $repo 'Plain Craft Launcher 2\Modules\BrclInstall.ps1') -Destination (Join-Path $stage 'install.ps1')
    [IO.File]::WriteAllText((Join-Path $stage 'install.json'), (@{pid=2147483647; target=$target; exe='BRCL.exe'} | ConvertTo-Json))
    & "$env:WINDIR\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -File (Join-Path $stage 'install.ps1') -Quiet
    Require ($LASTEXITCODE -eq 0) 'Install failed.'
    $deadline = (Get-Date).AddSeconds(5)
    while (-not (Test-Path -LiteralPath (Join-Path $target 'started.txt')) -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 100 }
    Require (Test-Path -LiteralPath (Join-Path $target 'started.txt')) 'Launcher restart failed.'
    Write-Output 'PASS update installs and restarts the launcher'

    $timestamped = @(Get-ChildItem -LiteralPath $backups -Directory | Where-Object Name -match '^\d{8}-\d{6}-\d{4}$' | Sort-Object Name -Descending)
    Require ($timestamped.Count -eq 5) "Expected five retained backups, found $($timestamped.Count)."
    Require ([IO.File]::ReadAllText((Join-Path $timestamped[0].FullName 'BRCL.exe')) -eq 'old exe') 'The backup of this update was not kept.'
    Require ((@($timestamped | Select-Object -Skip 1 | ForEach-Object Name) -join ',') -eq '20250107-000000-0001,20250106-000000-0001,20250105-000000-0001,20250104-000000-0001') 'The newest older backups were not the ones kept.'
    Require (Test-Path -LiteralPath (Join-Path $backups 'manual-keep')) 'A folder the installer did not create was removed.'
    Write-Output 'PASS backups are bounded to the newest five and unrelated folders are untouched'

    Require (-not (Test-Path -LiteralPath (Join-Path $stage 'release.zip'))) 'The staged update archive was left behind.'
    Require (-not (Test-Path -LiteralPath (Join-Path $stage 'payload'))) 'The extracted payload was left behind.'
    Write-Output 'PASS staged archive and payload are released after a successful update'
    Write-Output '3 update housekeeping checks passed.'
} finally {
    Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
}
