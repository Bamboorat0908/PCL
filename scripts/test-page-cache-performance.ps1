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
$static = [Reflection.BindingFlags]'NonPublic,Static'
$instance = [Reflection.BindingFlags]'NonPublic,Instance'
$assembly.GetType('PCL.ModAnimation',$true).GetProperty('AniControlEnabled').SetValue($null,1,$null)
$homeType = $assembly.GetType('PCL.PageLaunchRight',$true)
$prepareHome = $homeType.GetMethod('PrepareHomeData',$static)
$original = [Newtonsoft.Json.Linq.JObject]::Parse('{"server":{"pack":"TRM","season_label":"Season 9"},"status":{"alive":true,"player_count":2,"max_players":100,"players":["A","B"],"rss_kb":2097152},"season":{"day":23},"updated_at":"2026-11-13T12:00:00","history":{"2026-11-13":5},"playtime":[{"name":"A","seconds":100}]}')
$small = $prepareHome.Invoke($null,[object[]](,$original))
if ($null -ne $small['history'] -or $null -ne $small['playtime'] -or $null -ne $small['status']['players'] -or $null -ne $small['status']['rss_kb']) { throw 'Home cache retains unnecessary dashboard/ranking fields' }
if ($small['status']['alive'].Value -ne $true -or $small['status']['player_count'].Value -ne 2 -or $small['season']['day'].Value -ne 23) { throw 'Home projection lost displayed data' }
$small['server']['pack'] = [Newtonsoft.Json.Linq.JValue]::new('changed')
if ($original['server']['pack'].Value -ne 'TRM') { throw 'Home cache aliases source data' }
Write-Output 'PASS home cache retains only displayed metadata without aliasing the full response'

$pageType = $assembly.GetType('PCL.PageBRS',$true)
$page = [Activator]::CreateInstance($pageType)
$apply = $pageType.GetMethod('ApplyData',$instance)
$data = [Newtonsoft.Json.Linq.JObject]::Parse('{"status":{"alive":true,"player_count":2,"players":["A","B"],"motd":"Same MOTD"},"history":{"2026-11-13":5}}')
if (-not $apply.Invoke($page,[object[]]@($data,$true))) { throw 'Cached BRS status was rejected' }
$rows = $page.FindName('PanPlayers')
$first = $rows.Children[0]
$motd = $page.FindName('LabMotd').Inlines.FirstInline
if (-not $apply.Invoke($page,[object[]]@($data,$false))) { throw 'Live BRS status was rejected' }
$online = [string][char]0x5728 + [char]0x7EBF
if (-not [object]::ReferenceEquals($first,$rows.Children[0]) -or $rows.Children[0].Children[2].Text -ne $online) { throw 'Cached-to-live roster recreated its rows or retained stale labels' }
if (-not [object]::ReferenceEquals($motd,$page.FindName('LabMotd').Inlines.FirstInline)) { throw 'Unchanged MOTD recreated inline runs' }
Write-Output 'PASS matching cached/live BRS data reuses player rows, avatars and MOTD runs'

$prepare = $pageType.GetMethod('PrepareStatus',$static)
$queued = $prepare.Invoke($null,[object[]](,$data))
$queue = $pageType.GetMethod('QueueStatus',$instance)
$pending = $pageType.GetField('PendingData',$instance)
[void]$queue.Invoke($page,[object[]]@($queued,$false))
$olderData = [Newtonsoft.Json.Linq.JObject]::Parse('{"status":{"alive":false}}')
$older = $prepare.Invoke($null,[object[]](,$olderData))
[void]$queue.Invoke($page,[object[]]@($older,$true))
if (-not [object]::ReferenceEquals($queued,$pending.GetValue($page))) { throw 'Late disk snapshot replaced the pending live response' }
[void]$pageType.GetMethod('ApplyPendingStatus',$instance).Invoke($page,[object[]]@($null,[EventArgs]::Empty))
if (-not [object]::ReferenceEquals($queued,$pending.GetValue($page))) { throw 'Unloaded page applied or discarded pending data' }
$applyTimer = $pageType.GetField('ApplyTimer',$instance).GetValue($page)
if ($applyTimer.IsEnabled) { throw 'Unloaded page retained its pending-apply timer' }
Write-Output 'PASS late disk snapshots cannot overwrite live results or refresh unloaded controls'

$avatar = $assembly.GetType('PCL.BrsPlayerAvatar',$true)
$entryType = $avatar.GetNestedType('AvatarEntry',[Reflection.BindingFlags]::NonPublic)
$cache = $avatar.GetField('Cache',$static).GetValue($null)
$trim = $avatar.GetMethod('TrimCacheForInsert',$static)
$pendingTask = [Threading.Tasks.TaskCompletionSource[Windows.Media.Imaging.BitmapSource]]::new()
$finishedTask = [Threading.Tasks.TaskCompletionSource[Windows.Media.Imaging.BitmapSource]]::new()
$finishedTask.SetResult($null)
function Add-AvatarCacheEntry([string]$name,[bool]$finished,[int]$age) {
    $entry = [Activator]::CreateInstance($entryType,$true)
    $entryType.GetField('Created').SetValue($entry,[DateTime]::UtcNow.AddSeconds(-$age))
    $entryType.GetField('Picture').SetValue($entry,$(if ($finished) {$finishedTask.Task} else {$pendingTask.Task}))
    $cache.Add($name,$entry)
}
$cache.Clear()
foreach ($i in 0..129) { Add-AvatarCacheEntry "pending$i" $false $i }
[void]$trim.Invoke($null,@())
Add-AvatarCacheEntry 'new' $false 0
if ($cache.Count -ne 128) { throw "Pending avatar burst escaped cache bound: $($cache.Count)" }
$cache.Clear()
foreach ($i in 0..126) { Add-AvatarCacheEntry "pending$i" $false 100 }
Add-AvatarCacheEntry 'completed-old' $true 20
Add-AvatarCacheEntry 'completed-new' $true 10
[void]$trim.Invoke($null,@())
if ($cache.ContainsKey('completed-old') -or $cache.ContainsKey('completed-new') -or -not $cache.ContainsKey('pending126')) { throw 'Avatar cache evicted pending requests before completed entries' }
$cache.Clear()
Write-Output 'PASS avatar cache remains bounded for pending bursts and evicts completed entries first'
if ($app.Windows.Count -ne 0 -or $page.IsLoaded) { throw 'Cache checks unexpectedly opened a window' }
