# Native WPF integration checks: create controls and apply data without showing a window.
param([string]$BuildDirectory)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationFramework,PresentationCore,WindowsBase,System.Net.Http
$project = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\Plain Craft Launcher 2'))
if (-not $BuildDirectory) { $BuildDirectory = Join-Path $project 'bin' }
[void][Reflection.Assembly]::LoadFrom((Join-Path $project 'Resources\Newtonsoft.Json.dll'))
$assembly = [Reflection.Assembly]::LoadFrom((Join-Path $BuildDirectory 'BRCL.exe'))
[Windows.Application]::ResourceAssembly = $assembly
$app = [Activator]::CreateInstance($assembly.GetType('PCL.Application',$true),$true)
$app.InitializeComponent()
$assembly.GetType('PCL.ModAnimation',$true).GetProperty('AniControlEnabled').SetValue($null,1,$null)
$pageType = $assembly.GetType('PCL.PageBRS',$true)
$apply = $pageType.GetMethod('ApplyData',[Reflection.BindingFlags]'NonPublic,Instance')
$page = [Activator]::CreateInstance($pageType)
$missing = [string][char]0x2014
function Set-PageData([string]$json,[bool]$fromCache=$false) {
    $data = [Newtonsoft.Json.Linq.JObject]::Parse($json)
    if (-not $apply.Invoke($page,[object[]]@($data,$fromCache))) { throw 'PageBRS.ApplyData returned false' }
}
function Assert-Text([string]$name,[string]$expected) {
    $control = $page.FindName($name)
    if ($null -eq $control) { throw "Missing page control $name" }
    if ($control.Text -ne $expected) { throw "$name expected $expected, got $($control.Text)" }
}
Set-PageData '{"status":{"alive":true,"player_count":2,"max_players":100,"players":["A","B"],"motd":"BRS","uptime_sec":32153,"rss_kb":2097152,"entities":522,"world_size_kb":1048576,"tps_overall_ms":40,"tps_overworld_ms":10},"server":{"name":"BRS","season_label":"Season Nine","pack":"TRM","host":"example.test"},"qq":{"group":"123456"},"season":{"day":23,"start":"2026-10-22"},"today":"2026-11-13","history":{"2026-11-13":5},"activity":{"week":{"2026-11-13":5},"peak_month":{"2026-11-13":2}},"weekly":{"players":18,"hours":172.6,"newcomers":2,"ach":47,"top":{"name":"A","seconds":3600}},"achievements":{"unlocked":675,"total_achievements":1086,"count":51},"playtime_players":51,"playtime_total_hours":1565.1,"updated_at":"2026-11-13T12:56:20","activity_updated_at":"2026-11-13T12:52:57"}'
Assert-Text LabPlayers '2'
Assert-Text LabPlayersMax '/100'
Assert-Text LabMemory '2.0 GiB'
Assert-Text LabWorldSize '1.0 GiB'
Assert-Text LabDashboardMspt '40.0 ms'
Assert-Text LabDashboardTps '20.0'
Assert-Text LabWeekAchievements '47 项'
if ($page.FindName('BarAchievement') -isnot [Windows.Controls.ProgressBar] -or [Math]::Abs($page.FindName('BarAchievement').Value - 62.2) -gt 0.001) { throw 'Native achievement progress bar did not receive valid progress' }
if ($page.FindName('PanPlayers').Children.Count -ne 2) { throw 'Online player rows missing' }
Write-Output 'PASS complete data reaches native page controls and achievement progress bar'

Set-PageData '{"status":{"alive":false}}'
foreach ($name in @('LabPlayers','LabMemory','LabDashboardMspt','LabDashboardTps','LabUptime','LabEntities','LabWorldSize')) { Assert-Text $name $missing }
Assert-Text LabPlayersMax ('/' + $missing)
if ($page.FindName('BarAchievement').Value -ne 0) { throw 'Unavailable achievements retained previous progress' }
Write-Output 'PASS minimal offline response clears prior runtime and progress values'

Set-PageData '{"status":{"alive":true,"player_count":"bad","max_players":null,"players":null,"uptime_sec":NaN,"rss_kb":"bad","entities":-1,"tps_overall_ms":Infinity,"tps_overworld_ms":null,"world_size_kb":-1},"season":{"day":"bad"},"weekly":false,"achievements":false}'
foreach ($name in @('LabPlayers','LabMemory','LabDashboardMspt','LabDashboardTps','LabUptime','LabEntities','LabWorldSize','LabSeasonProgress','LabWeekAchievements')) { Assert-Text $name $missing }
if ($page.FindName('BarAchievement').Value -ne 0) { throw 'Invalid achievements retained progress' }
Write-Output 'PASS invalid optional fields degrade independently in the actual page'

Set-PageData '{"status":{"alive":false}}' $true
Assert-Text LabState '上次离线'
$offlineText = $page.FindName('PanPlayers').Children[0].Text
if (-not $offlineText.Contains('上次') -or $offlineText.Contains('当前')) { throw "Cached offline status presented as current: $offlineText" }
Set-PageData '{"status":{"alive":true,"players":[]}}' $true
$emptyText = $page.FindName('PanPlayers').Children[0].Text
if (-not $emptyText.Contains('上次') -or $emptyText.Contains('当前')) { throw "Cached empty roster presented as current: $emptyText" }
Write-Output 'PASS cached offline and empty online rosters clearly identify the previous record'

$applyError = $pageType.GetMethod('ApplyError',[Reflection.BindingFlags]'NonPublic,Instance')
Set-PageData '{"status":{"alive":true,"player_count":1,"players":["PlayerA"]}}'
[void]$applyError.Invoke($page,[object[]]@('test network failure'))
Assert-Text LabOnlineCaption '上次在线'
$row = $page.FindName('PanPlayers').Children[0]
if ($row.Children[2].Text -ne '上次在线') { throw 'Refresh failure kept a current-online player label' }
Write-Output 'PASS refresh failure preserves roster as a clearly marked snapshot'

$sponsorType = $assembly.GetType('PCL.PageSetupSponsors',$true)
$sponsors = [Activator]::CreateInstance($sponsorType)
$sponsorData = [Newtonsoft.Json.Linq.JObject]::Parse('{"sponsors":[{"name":"PlayerA","amount":987654.32,"note":"Thank you","date":"2026-11-13","has_avatar":false},{"name":"Guest","amount":123456.78,"has_avatar":false}]}')
[void]$sponsorType.GetMethod('ApplyData',[Reflection.BindingFlags]'NonPublic,Instance').Invoke($sponsors,[object[]]@($sponsorData,$false))
function Add-PageTexts($node,$texts) {
    if ($node -is [Windows.Controls.TextBlock]) { $texts.Add($node.Text) }
    if ($node -is [Windows.DependencyObject]) {
        foreach ($child in [Windows.LogicalTreeHelper]::GetChildren($node)) {
            if ($child -is [Windows.DependencyObject]) { Add-PageTexts $child $texts }
        }
    }
}
$texts = [Collections.Generic.List[string]]::new()
Add-PageTexts $sponsors $texts
$shown = $texts -join '|'
if ($shown -notmatch 'PlayerA' -or $shown -notmatch 'Guest') { throw 'Sponsor identities disappeared while hiding amounts' }
if ($shown -match '987|123,?456|654\.32|456\.78|¥|累计赞助') { throw 'Sponsor amount leaked into visible TextBlocks' }
if ($sponsors.FindName('PanSponsors').Children[0].ColumnDefinitions.Count -ne 2) { throw 'Unused monetary column remains in sponsor rows' }
Write-Output 'PASS sponsor names remain visible while individual and total amounts are absent'
if ($app.Windows.Count -ne 0 -or $page.IsLoaded -or $sponsors.IsLoaded) { throw 'Integration checks unexpectedly opened or loaded a window' }
Write-Output '6 native page integration groups passed without opening a window.'
