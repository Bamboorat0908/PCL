param([string]$BuildDirectory)
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName PresentationFramework,PresentationCore,WindowsBase,System.Xaml,System.Net.Http
$project = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\Plain Craft Launcher 2'))
if (-not $BuildDirectory) { $BuildDirectory = Join-Path $project 'bin' }
[void][Reflection.Assembly]::LoadFrom((Join-Path $project 'Resources\Newtonsoft.Json.dll'))
[void][Reflection.Assembly]::LoadFrom((Join-Path $project 'Resources\ThrottleDebounce.dll'))
$assembly = [Reflection.Assembly]::LoadFrom((Join-Path $BuildDirectory 'BRCL.exe'))
$flags = [Reflection.BindingFlags]'Public,NonPublic,Static'
$instanceFlags = [Reflection.BindingFlags]'NonPublic,Instance'
$pack = $assembly.GetType('BRCL.ModBrsPack', $true)
$minecraft = $assembly.GetType('BRCL.ModMinecraft', $true)
$instanceType = $assembly.GetType('BRCL.ModMinecraft+McInstance', $true)
$stateType = $assembly.GetType('BRCL.ModMinecraft+McInstanceState', $true)
$pageType = $assembly.GetType('BRCL.PageLaunchLeft', $true)

function Invoke-Pack([string]$Name, [object[]]$Arguments) {
    $pack.GetMethod($Name, $flags).Invoke($null, $Arguments)
}

function New-Instance([string]$Root, [string]$Name) {
    $path = Join-Path $Root ('versions\' + $Name)
    [void][IO.Directory]::CreateDirectory($path)
    $json = @{
        id = $Name
        clientVersion = '1.21.1'
        mainClass = 'net.minecraft.client.main.Main'
        type = 'release'
        releaseTime = '2024-08-08T00:00:00Z'
        libraries = @()
    } | ConvertTo-Json -Compress
    [IO.File]::WriteAllText((Join-Path $path ($Name + '.json')), $json)
    $instance = $instanceType.GetConstructor([type[]]@([string])).Invoke([object[]]@([string]$path))
    $instanceType.GetField('State').SetValue($instance, [Enum]::Parse($stateType, 'Original'))
    $instance
}

function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

[Windows.Application]::ResourceAssembly = $assembly
$app = [Activator]::CreateInstance($assembly.GetType('BRCL.Application', $true), $true)
$app.InitializeComponent()
$startup = $app.GetType().GetMethod('Application_Startup', $instanceFlags)
$app.remove_Startup([Delegate]::CreateDelegate([Windows.StartupEventHandler], $app, $startup))
[Windows.Application].GetField('_startupUri', $instanceFlags).SetValue($app, $null)

$baseTemp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')
$tempName = 'BRCL-TRM-Selection-' + [Guid]::NewGuid().ToString('N')
$temp = [IO.Path]::GetFullPath((Join-Path $baseTemp $tempName))
Assert-True (([IO.Path]::GetDirectoryName($temp)) -eq $baseTemp -and ([IO.Path]::GetFileName($temp)) -eq $tempName) 'Unexpected temporary directory.'
try {
    $root = (Join-Path $temp 'Main Game') + '\'
    $otherRoot = (Join-Path $temp 'Other Game') + '\'
    $old = New-Instance $root 'TRM 1.10 高预设'
    $high = New-Instance $root 'TRM 1.11 高预设'
    $low = New-Instance $root '我的机械工'
    $plain = New-Instance $root 'Minecraft 1.21.1'
    $foreign = New-Instance $otherRoot 'TRM 9.9 高预设'
    Invoke-Pack 'WriteBrsMarker' @($low.PathVersion, '1.11', 'low')

    $lists = $minecraft.GetField('McInstanceList', $flags).GetValue($null)
    $all = [Activator]::CreateInstance($lists.GetType().GetGenericArguments()[1])
    foreach ($instance in @($old, $foreign, $low, $high, $plain)) { $all.Add($instance) }
    $cardType = $lists.GetType().GetGenericArguments()[0]
    $lists.Add([Enum]::Parse($cardType, 'API'), $all)
    $minecraft.GetField('_McFolderSelected', $flags).SetValue($null, $root)
    $generalSelection = $minecraft.GetField('_McInstanceSelected', $flags)
    $generalSelection.SetValue($null, $old)

    $page = [Activator]::CreateInstance($pageType)
    $pageType.GetField('IsLoadFinished', $instanceFlags).SetValue($page, $true)
    $shownTarget = $pageType.GetField('LaunchInstance', $instanceFlags)
    $button = $page.FindName('BtnLaunch')
    $versionLabel = $page.FindName('LabVersion')
    $instanceLabel = $page.FindName('LabLaunchInstance')
    $upgrade = $page.FindName('BtnUpgrade')
    $page.RefreshButtonsUI()
    Assert-True ($shownTarget.GetValue($page).PathVersion -eq $high.PathVersion) 'The first listed or foreign instance displaced the newest local version.'
    Assert-True ($versionLabel.Text -eq 'TRM 1.11 · 高预设' -and $instanceLabel.Text -eq ('实例 · ' + $high.Name)) 'Home labels disagree with the launch target.'
    [void]$page.Measure([Windows.Size]::new(260, 417))
    [void]$page.Arrange([Windows.Rect]::new(0, 0, 260, 417))
    $page.UpdateLayout()
    Assert-True ($versionLabel.ActualHeight -gt 0 -and $instanceLabel.ActualHeight -gt 0) 'A home label has no layout space.'
    Write-Output 'PASS home defaults to the newest local TRM and shows its version, preset and instance name'

    $recorderCode = @'
using BRCL;

public sealed class TrmRecordingLoader : ModLoader.LoaderTask<ModLaunch.McLaunchOptions, object> {
    public ModLaunch.McLaunchOptions Options;
    public override void Start(object input, bool isForceRestart) {
        Options = (ModLaunch.McLaunchOptions)input;
    }
}
'@
    $recorderSource = Join-Path $temp 'TrmRecordingLoader.cs'
    $recorderDll = Join-Path $temp 'TrmRecordingLoader.dll'
    [IO.File]::WriteAllText($recorderSource, $recorderCode)
    & "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:library "/reference:$($assembly.Location)" "/out:$recorderDll" $recorderSource
    if ($LASTEXITCODE -ne 0) { throw 'Launch recording loader failed to compile.' }
    [void][Reflection.Assembly]::Load([IO.File]::ReadAllBytes($recorderDll))
    $mainModule = $assembly.GetType('BRCL.ModMain', $true)
    $mainField = $mainModule.GetField('FrmMain', $flags)
    $mainType = $mainField.FieldType
    $main = [Runtime.Serialization.FormatterServices]::GetUninitializedObject($mainType)
    $stack = $mainType.GetField('PageStack')
    $stack.SetValue($main, [Activator]::CreateInstance($stack.FieldType))
    $mainField.SetValue($null, $main)
    $mainModule.GetField('IsAprilGiveup', $flags).SetValue($null, $true)
    $generalSelection.SetValue($null, $high)
    $launch = $assembly.GetType('BRCL.ModLaunch', $true).GetField('McLaunchLoader', $flags)
    $originalLoader = $launch.GetValue($null)
    $recorder = [TrmRecordingLoader]::new()
    try {
        $launch.SetValue($null, $recorder)
        $page.LaunchButtonClick()
        Assert-True ($recorder.Options.Instance.PathVersion -eq $shownTarget.GetValue($page).PathVersion) 'The launch command did not carry the displayed instance.'
    } finally {
        $launch.SetValue($null, $originalLoader)
        $mainField.SetValue($null, $null)
    }
    Write-Output 'PASS the production home click passes the displayed instance to the launch loader'

    Invoke-Pack 'RememberBrsSelection' @($root, $old)
    Invoke-Pack 'RememberBrsSelection' @($root, $plain)
    Invoke-Pack 'RememberBrsSelection' @($otherRoot, $foreign)
    $reopened = [Activator]::CreateInstance($pageType)
    $pageType.GetField('IsLoadFinished', $instanceFlags).SetValue($reopened, $true)
    $reopened.RefreshButtonsUI()
    Assert-True ($shownTarget.GetValue($reopened).PathVersion -eq $old.PathVersion) 'Reopening the home page lost the explicitly selected old instance.'
    Assert-True ($reopened.FindName('BtnUpgrade').Visibility -eq 'Visible') 'The old instance does not offer an upgrade.'
    Assert-True ((Invoke-Pack 'ReadBrsSelection' @($otherRoot)) -eq $foreign.Name) 'Selection was shared across game folders.'
    Write-Output 'PASS explicit selection survives reopening and is stored separately for each game directory'

    Invoke-Pack 'RememberBrsSelection' @($root, $low)
    $page.RefreshButtonsUI()
    Assert-True ($shownTarget.GetValue($page).PathVersion -eq $low.PathVersion) 'Switching between presets did not change the launch target.'
    Assert-True ($versionLabel.Text -eq 'TRM 1.11 · 低预设' -and $instanceLabel.Text -eq ('实例 · ' + $low.Name)) 'A renamed instance lost its recorded version or preset.'
    $instanceType.GetField('State').SetValue($low, [Enum]::Parse($stateType, 'Error'))
    $page.RefreshButtonsUI()
    Assert-True ($null -eq $shownTarget.GetValue($page) -and $button.Text -eq '重新选择实例') 'An unavailable saved instance silently fell back to another pack.'
    Assert-True ($upgrade.Visibility -eq 'Visible') 'An unavailable saved instance has no install action.'
    Write-Output 'PASS renamed low preset is shown correctly and unavailable selection requires confirmation'

    $savePath = Join-Path $high.PathVersion 'saves\旧世界\level.dat'
    [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($savePath))
    [IO.File]::WriteAllBytes($savePath, [byte[]]@(1, 3, 5, 7))
    $oldSaveHash = (Get-FileHash -LiteralPath $savePath).Hash
    [IO.File]::WriteAllText((Join-Path $root ('versions\' + $high.Name + ' (2)')), 'occupied')
    $newPath = Invoke-Pack 'CreateBrsInstallDirectory' @($root, $high.Name)
    Assert-True (([IO.Path]::GetFileName($newPath)) -eq ($high.Name + ' (3)')) 'An install reused an occupied name.'
    Assert-True ([IO.Directory]::Exists($newPath)) 'The install path was not reserved.'
    Invoke-Pack 'WriteBrsMarker' @($newPath, '1.11', 'high')
    $overrideSave = Join-Path $temp 'overrides\saves\旧世界\level.dat'
    [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($overrideSave))
    [IO.File]::WriteAllBytes($overrideSave, [byte[]]@(2, 4, 6))
    $modpack = $assembly.GetType('BRCL.ModModpack', $true)
    $modpack.GetMethod('CopyOverrideDirectory', $flags).Invoke($null, [object[]]@([string](Join-Path $temp 'overrides'), [string]$newPath, $null))
    Assert-True ((Get-FileHash -LiteralPath $savePath).Hash -eq $oldSaveHash) 'Reserving the upgrade and writing its marker changed the existing save.'
    Assert-True ((Get-FileHash -LiteralPath (Join-Path $newPath 'saves\旧世界\level.dat')).Hash -eq (Get-FileHash -LiteralPath $overrideSave).Hash) 'The upgrade did not use its own save directory.'
    Write-Output 'PASS the upgrade reserves a fresh directory without changing the old instance save'

    Invoke-Pack 'RememberBrsSelection' @($root, $old)
    Invoke-Pack 'PreserveBrsMetadataBeforeRename' @($old)
    $renamedPath = Join-Path $root 'versions\怀旧实例'
    [IO.Directory]::Move($old.PathVersion.TrimEnd('\'), $renamedPath)
    Invoke-Pack 'RenameBrsSelection' @($root, $old.Name, '怀旧实例')
    $renamed = New-Instance $root '怀旧实例'
    $all.Add($renamed)
    $page.RefreshButtonsUI()
    Assert-True ($shownTarget.GetValue($page).PathVersion -eq $renamed.PathVersion -and $versionLabel.Text -eq 'TRM 1.10 · 高预设') 'Renaming a legacy TRM instance lost its selection or metadata.'
    Write-Output 'PASS renaming a legacy TRM keeps its selected instance, version and preset'
} finally {
    if ([IO.Directory]::Exists($temp)) { [IO.Directory]::Delete($temp, $true) }
    $app.Shutdown()
}
