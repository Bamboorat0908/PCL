# Single instance: launching BRCL while it is already running must bring the existing window back
# and exit, on every system code page. The window title is "BRCL" + U+3000; the ANSI FindWindowA
# cannot express U+3000 on code pages such as Windows-1252, so the running window was never found.
param([string]$ExePath = (Join-Path $PSScriptRoot '..\Plain Craft Launcher 2\bin\BRCL.exe'))
$ErrorActionPreference = 'Stop'
Add-Type @'
using System; using System.Runtime.InteropServices;
public static class SingleInstanceProbe {
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "FindWindowW")] public static extern IntPtr FindWide(string cls, string title);
    [DllImport("user32.dll", CharSet = CharSet.Ansi, EntryPoint = "FindWindowA")] public static extern IntPtr FindAnsi(string cls, string title);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hwnd, int cmd);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hwnd);
    [DllImport("kernel32.dll")] public static extern uint GetACP();
}
'@
$exe = (Resolve-Path -LiteralPath $ExePath).Path
$fixture = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ('..\artifacts\single-instance-test\' + [Guid]::NewGuid().ToString('N'))))
$saved = @{ Registry = $env:BRCL_TEST_REGISTRY_ID; Preview = $env:BRCL_UI_PREVIEW; Temp = $env:TEMP; Tmp = $env:TMP }
$script:Failed = $false
function Require([bool]$value, [string]$message) { if (-not $value) { $script:Failed = $true; throw $message } }
function Wait-Log($Process, [string]$Log, [string]$Text, [int]$Seconds = 30) {
    $watch = [Diagnostics.Stopwatch]::StartNew()
    do {
        if ((Test-Path -LiteralPath $Log) -and (Select-String -LiteralPath $Log -SimpleMatch $Text -Quiet -ErrorAction SilentlyContinue)) { return $true }
        if ($Process.HasExited) { return $false }
        Start-Sleep -Milliseconds 100
    } while ($watch.Elapsed.TotalSeconds -lt $Seconds)
    return $false
}
if (Get-Process BRCL -ErrorAction SilentlyContinue) { throw 'Close every BRCL window before running the single-instance check.' }
$title = 'BRCL' + [char]0x3000
$first = $null; $second = $null
try {
    $env:BRCL_UI_PREVIEW = $null   # the singleton mutex is skipped in preview mode
    $env:BRCL_TEST_REGISTRY_ID = [Guid]::NewGuid().ToString('N')
    $keyPath = 'Software\BRCLRegression\' + $env:BRCL_TEST_REGISTRY_ID
    $key = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey("$keyPath\Software\BRCL")
    try {
        $key.SetValue('SystemEulaVersion', '3')
        $key.SetValue('SystemSystemTelemetry', 'False')
        $key.SetValue('BrclCrashMode', '3')
    } finally { $key.Dispose() }
    $directory = [IO.Directory]::CreateDirectory($fixture).FullName
    $data = [IO.Directory]::CreateDirectory((Join-Path $directory 'BRCL')).FullName
    [IO.File]::WriteAllLines((Join-Path $data 'Setup.ini'), [string[]]@('SystemSystemUpdate:3','BrclUsageEnabled:False'))
    $env:TEMP = $env:TMP = [IO.Directory]::CreateDirectory((Join-Path $directory 'cache')).FullName
    $installed = Join-Path $directory 'BRCL.exe'
    Copy-Item -LiteralPath $exe -Destination $installed
    $log = Join-Path $directory 'BRCL\Log1.txt'
    $first = Start-Process -FilePath $installed -WorkingDirectory $directory -PassThru
    Require (Wait-Log $first $log '启动首屏可交互') "The first launcher did not reach the home screen. See $log."
    Start-Sleep -Milliseconds 800
    # [NullString]::Value: PowerShell would otherwise marshal $null as an empty class name, which matches nothing.
    $wide = [SingleInstanceProbe]::FindWide([NullString]::Value, $title)
    $ansi = [SingleInstanceProbe]::FindAnsi([NullString]::Value, $title)
    Write-Output ("INFO code page {0}; FindWindowW found {1}; FindWindowA found {2}" -f [SingleInstanceProbe]::GetACP(), ($wide -ne [IntPtr]::Zero), ($ansi -ne [IntPtr]::Zero))
    Require ($wide -ne [IntPtr]::Zero) 'The running launcher window has an unexpected title.'
    [void][SingleInstanceProbe]::ShowWindow($wide, 6)   # SW_MINIMIZE
    Start-Sleep -Milliseconds 800
    Require ([SingleInstanceProbe]::IsIconic($wide)) 'Could not minimise the first launcher for the check.'
    $second = Start-Process -FilePath $installed -WorkingDirectory $directory -PassThru
    Require ($second.WaitForExit(20000)) 'The second launcher did not exit while another instance was running.'
    Write-Output 'PASS a second launch exits while an instance is running'
    $watch = [Diagnostics.Stopwatch]::StartNew()
    while ([SingleInstanceProbe]::IsIconic($wide) -and $watch.Elapsed.TotalSeconds -lt 5) { Start-Sleep -Milliseconds 100 }
    Require (-not [SingleInstanceProbe]::IsIconic($wide)) "The existing launcher window was not restored by the second launch (code page $([SingleInstanceProbe]::GetACP()))."
    Require (Select-String -LiteralPath $log -SimpleMatch '收到置顶信息' -Quiet) 'The running launcher never received the bring-to-front message.'
    Write-Output 'PASS the second launch brings the existing window back on this code page'
    Write-Output '2 single-instance checks passed.'
} finally {
    foreach ($process in @($second, $first)) {
        if ($process -and -not $process.HasExited) { [void]$process.CloseMainWindow(); if (-not $process.WaitForExit(5000)) { $process.Kill(); $process.WaitForExit() } }
    }
    if ($keyPath) { [Microsoft.Win32.Registry]::CurrentUser.DeleteSubKeyTree($keyPath, $false) }
    $env:BRCL_TEST_REGISTRY_ID = $saved.Registry; $env:BRCL_UI_PREVIEW = $saved.Preview
    $env:TEMP = $saved.Temp; $env:TMP = $saved.Tmp
    if ($script:Failed) { Write-Output "Fixture kept for inspection: $fixture" } else { Remove-Item -LiteralPath $fixture -Recurse -Force -ErrorAction SilentlyContinue }
}
