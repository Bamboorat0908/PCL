# Background pictures: a real launcher must apply BRCL\Pictures at startup without a UI exception.
# v1.1.6 froze the ImageBrush on the worker thread and then set its Stretch/TileMode on the UI thread,
# which throws InvalidOperationException and ends in the "unknown error" crash dialog.
param([string]$ExePath = (Join-Path $PSScriptRoot '..\Plain Craft Launcher 2\bin\BRCL.exe'))
$ErrorActionPreference = 'Stop'
$script:Failed = $false
Add-Type -AssemblyName System.Drawing
$exe = (Resolve-Path -LiteralPath $ExePath).Path
# Not under %TEMP%: the launcher refuses to run from the temp folder ("extract BRCL first") and blocks on that dialog.
$fixture = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ('..\artifacts\background-test\' + [Guid]::NewGuid().ToString('N'))))
$saved = @{ Registry = $env:BRCL_TEST_REGISTRY_ID; Preview = $env:BRCL_UI_PREVIEW; Temp = $env:TEMP; Tmp = $env:TMP }
$script:CurrentLog = $null
function Require([bool]$value, [string]$message) {
    if ($value) { return }
    # Keep the evidence: attach the end of the launcher log and leave the fixture in place.
    $script:Failed = $true
    $tail = if ($script:CurrentLog -and (Test-Path -LiteralPath $script:CurrentLog)) { (Get-Content -LiteralPath $script:CurrentLog -Encoding UTF8 -Tail 40) -join [Environment]::NewLine } else { '(no launcher log)' }
    throw "$message$([Environment]::NewLine)--- launcher log tail ---$([Environment]::NewLine)$tail"
}
function Wait-Log($Process, [string]$Log, [string]$Text, [int]$Seconds = 25) {
    $watch = [Diagnostics.Stopwatch]::StartNew()
    do {
        if ((Test-Path -LiteralPath $Log) -and (Select-String -LiteralPath $Log -SimpleMatch $Text -Quiet -ErrorAction SilentlyContinue)) { return $true }
        if ($Process.HasExited) { return $false }
        Start-Sleep -Milliseconds 100
    } while ($watch.Elapsed.TotalSeconds -lt $Seconds)
    return $false
}
function Stop-TestLauncher($Process) {
    if ($Process -and -not $Process.HasExited) { [void]$Process.CloseMainWindow(); if (-not $Process.WaitForExit(4000)) { $Process.Kill(); $Process.WaitForExit() } }
}
try {
    $env:BRCL_UI_PREVIEW = '1'
    $env:TEMP = $env:TMP = [IO.Directory]::CreateDirectory((Join-Path $fixture 'cache')).FullName
    # 1920x1080 uses the fitted (UniformToFill) branch; 64x64 uses the smart tiling branch with an absolute viewport.
    foreach ($case in @(@{ Name = 'large'; Width = 1920; Height = 1080 }, @{ Name = 'tile'; Width = 64; Height = 64 }, @{ Name = 'portrait'; Width = 256; Height = 4608 })) {
        $env:BRCL_TEST_REGISTRY_ID = [Guid]::NewGuid().ToString('N')
        $keyPath = 'Software\BRCLRegression\' + $env:BRCL_TEST_REGISTRY_ID
        $key = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey("$keyPath\Software\BRCL")
        try { $key.SetValue('SystemEulaVersion', '3') } finally { $key.Dispose() }
        $directory = [IO.Directory]::CreateDirectory((Join-Path $fixture $case.Name)).FullName
        $pictures = [IO.Directory]::CreateDirectory((Join-Path $directory 'BRCL\Pictures')).FullName
        $bitmap = New-Object System.Drawing.Bitmap $case.Width, $case.Height
        try {
            $bitmap.SetResolution(96, 96)
            $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
            try { $graphics.Clear([System.Drawing.Color]::FromArgb(255, 40, 90, 140)) } finally { $graphics.Dispose() }
            $bitmap.Save((Join-Path $pictures 'background.png'), [System.Drawing.Imaging.ImageFormat]::Png)
        } finally { $bitmap.Dispose() }
        $installed = Join-Path $directory 'BRCL.exe'
        Copy-Item -LiteralPath $exe -Destination $installed
        $log = Join-Path $directory 'BRCL\Log1.txt'
        $script:CurrentLog = $log
        $process = $null
        try {
            $process = Start-Process -FilePath $installed -WorkingDirectory $directory -WindowStyle Hidden -PassThru
            Require (Wait-Log $process $log '启动首屏可交互') "Launcher did not reach the home screen ($($case.Name)). See $log."
            $applied = Wait-Log $process $log '已应用背景图片'
            Start-Sleep -Milliseconds 800
            $errors = @(Select-String -LiteralPath $log -Pattern 'UI 未处理异常|程序出现未知错误|InvalidOperationException|ImageBrush' -ErrorAction SilentlyContinue)
            Require ($errors.Count -eq 0) "Applying the $($case.Name) background raised: $($errors[0].Line)"
            Require $applied "The $($case.Name) background was never applied. See $log."
            Require (-not $process.HasExited) "The launcher exited after applying the $($case.Name) background."
            if ($case.Name -eq 'portrait') {
                Require (Select-String -LiteralPath $log -SimpleMatch '已应用背景图片：213 x 3840' -Quiet) 'A tall wallpaper was not downsampled with its aspect ratio intact.'
            }
            Write-Output "PASS $($case.Name) background picture is applied at startup without a UI exception"
        } finally {
            Stop-TestLauncher $process
            [Microsoft.Win32.Registry]::CurrentUser.DeleteSubKeyTree($keyPath, $false)
        }
    }
    Write-Output '3 background picture checks passed.'
} finally {
    $env:BRCL_TEST_REGISTRY_ID = $saved.Registry; $env:BRCL_UI_PREVIEW = $saved.Preview; $env:TEMP = $saved.Temp; $env:TMP = $saved.Tmp
    if ($script:Failed) { Write-Output "Fixture kept for inspection: $fixture" }
    else { Remove-Item -LiteralPath $fixture -Recurse -Force -ErrorAction SilentlyContinue }
}
