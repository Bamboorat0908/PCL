param([switch]$Quiet)
$ErrorActionPreference = 'Stop'
$stage = $PSScriptRoot
$settings = Get-Content -LiteralPath (Join-Path $stage 'install.json') -Raw | ConvertFrom-Json
$target = [IO.Path]::GetFullPath($settings.target)
$backup = Join-Path $target ('BRCL\UpdateBackups\' + (Get-Date -Format 'yyyyMMdd-HHmmss-ffff'))
$changed = @()
try {
    # Direct downloads may be renamed by the browser (for example BRCL (1).exe).
    # Only remap the launcher's EXE within its existing installation directory.
    $launcherName = [string]$settings.exe
    if ([string]::IsNullOrWhiteSpace($launcherName) -or [IO.Path]::GetFileName($launcherName) -cne $launcherName -or [IO.Path]::GetExtension($launcherName) -ine '.exe' -or $launcherName.IndexOfAny([IO.Path]::GetInvalidFileNameChars()) -ge 0) { throw 'Invalid launcher executable name.' }
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
    Start-Process -FilePath (Join-Path $target $settings.exe) -WorkingDirectory $target
} catch {
    $failure = $_.ToString()
    foreach ($item in $changed) {
        $destination = Join-Path $target $item.Name
        if ($item.Existed) { Copy-Item -LiteralPath (Join-Path $backup $item.Name) -Destination $destination -Force -ErrorAction Continue }
        else { Remove-Item -LiteralPath $destination -Force -ErrorAction Continue }
    }
    $failure | Set-Content -LiteralPath (Join-Path $stage 'error.txt')
    if (-not $Quiet) {
        Add-Type -AssemblyName PresentationFramework
        [System.Windows.MessageBox]::Show("BRCL 更新失败，已尝试恢复原文件。错误记录：$stage\error.txt", 'BRCL 更新失败') | Out-Null
    }
    exit 1
}
