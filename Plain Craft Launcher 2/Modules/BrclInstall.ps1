param([switch]$Quiet)
$ErrorActionPreference = 'Stop'
$stage = $PSScriptRoot
$settings = Get-Content -LiteralPath (Join-Path $stage 'install.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$target = [IO.Path]::GetFullPath($settings.target)
$backup = Join-Path $target ('BRCL\UpdateBackups\' + (Get-Date -Format 'yyyyMMdd-HHmmss-ffff'))
$changed = @()
$restartName = $null
try {
    # Direct downloads may be renamed by the browser (for example BRCL (1).exe).
    # Only remap the launcher's EXE within its existing installation directory.
    $launcherName = [string]$settings.exe
    if ([string]::IsNullOrWhiteSpace($launcherName) -or [IO.Path]::GetFileName($launcherName) -cne $launcherName -or [IO.Path]::GetExtension($launcherName) -ine '.exe' -or $launcherName.IndexOfAny([IO.Path]::GetInvalidFileNameChars()) -ge 0) { throw 'Invalid launcher executable name.' }
    $restartName = $launcherName
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $payload = Join-Path $stage 'payload'
    New-Item -ItemType Directory -Path $payload -Force | Out-Null
    $allowed = @('BRCL.exe', 'BRCL.exe.config', 'PCLCS.dll', 'MeloongCore.dll', 'MeloongCore.Wpf.dll', 'Microsoft.Web.WebView2.Core.dll', 'Microsoft.Web.WebView2.Wpf.dll', 'WebView2Loader.dll', 'Microsoft.Win32.Registry.dll', 'LICENCE')
    $zip = [IO.Compression.ZipFile]::OpenRead((Join-Path $stage 'release.zip'))
    try {
        $seen = @{}
        foreach ($entry in $zip.Entries) {
            if ($entry.FullName -cnotin $allowed -or $seen.ContainsKey($entry.FullName) -or $entry.Length -gt 268435456) { throw 'Invalid update package entry.' }
            $seen[$entry.FullName] = $true
            $outputName = if ($entry.FullName -ceq 'BRCL.exe') { $launcherName } else { $entry.FullName }
            [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, (Join-Path $payload $outputName))
        }
        if (-not $seen.ContainsKey('BRCL.exe')) { throw 'Missing BRCL.exe.' }
    } finally { $zip.Dispose() }
    if (Get-Process -Id $settings.pid -ErrorAction SilentlyContinue) { Wait-Process -Id $settings.pid -Timeout 120 -ErrorAction Stop }
    New-Item -ItemType Directory -Path $backup -Force | Out-Null
    foreach ($file in Get-ChildItem -LiteralPath $payload -File) {
        $destination = Join-Path $target $file.Name
        $existed = Test-Path -LiteralPath $destination
        if ($existed) { Copy-Item -LiteralPath $destination -Destination (Join-Path $backup $file.Name) }
        $changed += [pscustomobject]@{Name=$file.Name; Existed=$existed}
        Copy-Item -LiteralPath $file.FullName -Destination $destination -Force
    }
    Start-Process -FilePath (Join-Path $target $launcherName) -WorkingDirectory $target
    # The update is complete. Housekeeping below must never turn a successful update into a rollback.
    try {
        # Keep the newest five backups; each holds a full launcher EXE and would otherwise accumulate forever.
        $backupRoot = Split-Path -Parent $backup
        Get-ChildItem -LiteralPath $backupRoot -Directory -ErrorAction Stop |
            Where-Object { $_.Name -match '^\d{8}-\d{6}-\d{4}$' -and $_.FullName -ne $backup } |
            Sort-Object Name -Descending | Select-Object -Skip 4 |
            ForEach-Object { Remove-Item -LiteralPath $_.FullName -Recurse -Force -ErrorAction SilentlyContinue }
    } catch { }
    try {
        # The staged archive and extracted payload are no longer needed once the files are in place.
        Remove-Item -LiteralPath (Join-Path $stage 'release.zip') -Force -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath $payload -Recurse -Force -ErrorAction SilentlyContinue
    } catch { }
} catch {
    $failure = $_.ToString()
    $restoreFailures = foreach ($item in $changed) {
        $destination = Join-Path $target $item.Name
        try {
            if ($item.Existed) { Copy-Item -LiteralPath (Join-Path $backup $item.Name) -Destination $destination -Force }
            elseif (Test-Path -LiteralPath $destination) { Remove-Item -LiteralPath $destination -Force }
        } catch {
            "Rollback failed for ${destination}: $($_.Exception.Message)"
        }
    }
    @($failure) + @($restoreFailures) | Set-Content -LiteralPath (Join-Path $stage 'error.txt') -Encoding UTF8
    if (-not $Quiet) {
        Add-Type -AssemblyName PresentationFramework
        $status = if ($restoreFailures) { '部分文件恢复失败，请保留更新备份。' } else { '原文件已保留或恢复。' }
        [System.Windows.MessageBox]::Show("BRCL 更新失败。$status 错误记录：$stage\error.txt", 'BRCL 更新失败') | Out-Null
        # The launcher closed itself to be replaced; reopen the restored copy so the user is not left without it.
        if ($restartName -and -not $restoreFailures) {
            try {
                $restored = Join-Path $target $restartName
                if (Test-Path -LiteralPath $restored -PathType Leaf) { Start-Process -FilePath $restored -WorkingDirectory $target -ErrorAction Stop }
            } catch { }
        }
    }
    exit 1
}
