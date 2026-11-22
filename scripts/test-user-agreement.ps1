param(
    [string]$ExePath = (Join-Path $PSScriptRoot '..\Plain Craft Launcher 2\bin\BRCL.exe'),
    [string]$FixtureDirectory = (Join-Path $PSScriptRoot ('..\artifacts\agreement-test\' + [Guid]::NewGuid().ToString('N')))
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes
$exe = (Resolve-Path -LiteralPath $ExePath).Path
$fixture = [IO.Path]::GetFullPath($FixtureDirectory)
if (Test-Path -LiteralPath $fixture) { throw 'Use a new directory for the agreement test.' }
$savedRegistryId = $env:BRCL_TEST_REGISTRY_ID
$savedPreview = $env:BRCL_UI_PREVIEW
$savedTemp = $env:TEMP
$savedTmp = $env:TMP

function Wait-Log($Process, $Log, [string]$Text) {
    $watch = [Diagnostics.Stopwatch]::StartNew()
    do {
        if ((Test-Path -LiteralPath $Log) -and (Select-String -LiteralPath $Log -SimpleMatch $Text -Quiet -ErrorAction SilentlyContinue)) { return }
        if ($Process.HasExited) { break }
        Start-Sleep -Milliseconds 100
    } while ($watch.Elapsed.TotalSeconds -lt 18)
    throw "Launcher did not reach '$Text'. See $Log."
}

function Invoke-AgreementButton($Process, [string]$Caption) {
    $watch = [Diagnostics.Stopwatch]::StartNew()
    $name = [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::NameProperty, $Caption)
    $button = [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty,
        [Windows.Automation.ControlType]::Button)
    $query = [Windows.Automation.AndCondition]::new($name, $button)
    do {
        $Process.Refresh()
        if ($Process.HasExited) { throw "Launcher exited before showing '$Caption'." }
        if ($Process.MainWindowHandle -ne [IntPtr]::Zero) {
            try {
                $window = [Windows.Automation.AutomationElement]::FromHandle($Process.MainWindowHandle)
                $element = $window.FindFirst([Windows.Automation.TreeScope]::Descendants, $query)
                if ($element -and $element.Current.IsEnabled -and -not $element.Current.IsOffscreen) {
                    $element.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
                    return
                }
            } catch [Windows.Automation.ElementNotAvailableException] { }
        }
        Start-Sleep -Milliseconds 100
    } while ($watch.Elapsed.TotalSeconds -lt 12)
    throw "Could not invoke '$Caption' in the test launcher."
}

function Stop-TestLauncher($Process) {
    if ($Process -and -not $Process.HasExited) {
        [void]$Process.CloseMainWindow()
        if (-not $Process.WaitForExit(3000)) { $Process.Kill(); $Process.WaitForExit() }
    }
}

try {
    $env:BRCL_UI_PREVIEW = '1'
    $env:TEMP = $env:TMP = [IO.Directory]::CreateDirectory((Join-Path $fixture 'cache')).FullName
    foreach ($oldVersion in 0,2) {
        $env:BRCL_TEST_REGISTRY_ID = [Guid]::NewGuid().ToString('N')
        $keyPath = 'Software\BRCLRegression\' + $env:BRCL_TEST_REGISTRY_ID
        $directory = [IO.Directory]::CreateDirectory((Join-Path $fixture "version-$oldVersion")).FullName
        $installed = Join-Path $directory 'BRCL.exe'
        Copy-Item -LiteralPath $exe -Destination $installed
        $log = Join-Path $directory 'BRCL\Log1.txt'
        $old = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey("$keyPath\Software\PCLDebug")
        try { $old.SetValue('SystemEulaVersion',[string]$oldVersion) } finally { $old.Dispose() }
        $process = $null
        try {
            $process = Start-Process -FilePath $installed -WorkingDirectory $directory -WindowStyle Hidden -PassThru
            Wait-Log $process $log 'BRCL 弹窗：BRCL 用户协议与免责声明'
            if (Select-String -LiteralPath $log -SimpleMatch '一、适用范围' -Quiet) { throw 'Startup confirmation opened the full agreement without being asked.' }
            if ($oldVersion -eq 0) {
                Invoke-AgreementButton $process '拒绝并退出'
                if (-not $process.WaitForExit(6000)) { throw 'Declining the agreement did not exit the launcher.' }
                Write-Output 'PASS fresh installs show a short confirmation and declining exits'
                continue
            }
            Invoke-AgreementButton $process '查看协议'
            Wait-Log $process $log '一、适用范围'
            Invoke-AgreementButton $process '返回'
            Invoke-AgreementButton $process '同意并继续'
            $current = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey("$keyPath\Software\BRCL")
            try {
                $watch = [Diagnostics.Stopwatch]::StartNew()
                while ([int]$current.GetValue('SystemEulaVersion',0) -lt 3 -and $watch.Elapsed.TotalSeconds -lt 5) {
                    Start-Sleep -Milliseconds 100
                }
                if ([int]$current.GetValue('SystemEulaVersion',0) -ne 3) { throw 'Agreement confirmation was not persisted in the new registry.' }
            } finally { $current.Dispose() }
            Write-Output 'PASS an upgrade can read the built-in agreement, return and persist acceptance'
            Stop-TestLauncher $process
            Move-Item -LiteralPath $log -Destination (Join-Path $directory 'accepted-session.txt')
            $process = Start-Process -FilePath $installed -WorkingDirectory $directory -WindowStyle Hidden -PassThru
            Wait-Log $process $log '启动首屏可交互'
            Wait-Log $process $log ' · 初始化> [ModJava]'
            if (Select-String -LiteralPath $log -SimpleMatch 'BRCL 弹窗：BRCL 用户协议与免责声明' -Quiet) { throw 'Agreement reappeared after acceptance.' }
            Write-Output 'PASS a restart after acceptance enters the launcher without prompting again'
        } finally {
            Stop-TestLauncher $process
            [Microsoft.Win32.Registry]::CurrentUser.DeleteSubKeyTree($keyPath,$false)
        }
    }
} finally {
    $env:BRCL_TEST_REGISTRY_ID = $savedRegistryId
    $env:BRCL_UI_PREVIEW = $savedPreview
    $env:TEMP = $savedTemp
    $env:TMP = $savedTmp
}
