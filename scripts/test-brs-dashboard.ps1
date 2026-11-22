# Run with Windows PowerShell 5.1 after the Release build.
param([string]$BuildDirectory)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationFramework,System.Net.Http
$project = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\Plain Craft Launcher 2'))
[void][Reflection.Assembly]::LoadFrom((Join-Path $project 'Resources\Newtonsoft.Json.dll'))
if (-not $BuildDirectory) { $BuildDirectory = Join-Path $project 'bin' }
$assembly = [Reflection.Assembly]::LoadFrom((Join-Path $BuildDirectory 'BRCL.exe'))
$build = $assembly.GetType('BRCL.BrsDashboardData',$true).GetMethod('Build')
$missing = [string][char]0x2014
function Get-Dashboard([string]$json) {
    $parsed = [Newtonsoft.Json.Linq.JObject]::Parse($json)
    return $build.Invoke($null,[object[]](,$parsed))
}
function Assert-Field($data,[string]$key,[string]$expected) {
    if ($data[$key] -ne $expected) { throw "$key expected $expected, got $($data[$key])" }
}
$data = Get-Dashboard '{"status":{"alive":true,"uptime_sec":32153,"rss_kb":2097152,"entities":522,"world_size_kb":1048576,"tps_overall_ms":40,"tps_overworld_ms":10.089},"today":"2026-11-13","history":{"2026-11-13":5},"activity":{"peak_month":{"2026-11-13":2}},"season":{"start":"2026-10-22","day":23},"server":{"season_label":"Season Nine"},"achievements":{"unlocked":675,"total_achievements":1086},"playtime_players":51,"playtime_total_hours":1565.1,"weekly":{"ach":47,"top":{"name":"zhenyu_05","seconds":109845}},"updated_at":"2026-11-13T12:56:20","activity_updated_at":"2026-11-13T12:52:57"}'
Assert-Field $data Uptime '8小时55分'
Assert-Field $data Memory '2.0 GiB'
Assert-Field $data WorldSize '1.0 GiB'
Assert-Field $data Entities '522'
Assert-Field $data Mspt '40.0 ms'
Assert-Field $data OverworldMspt '10.1 ms'
Assert-Field $data EstimatedTps '20.0'
Assert-Field $data TodayActive '5'
Assert-Field $data TodayPeak '2'
Assert-Field $data SeasonStart '2026-10-22'
Assert-Field $data SeasonDay '第 23 天'
Assert-Field $data SeasonLabel 'Season Nine'
Assert-Field $data AchievementPercent '62.2'
Assert-Field $data Alive 'true'
Assert-Field $data SeasonPlayers '51'
Assert-Field $data SeasonHours '1,565.1 小时'
Assert-Field $data WeekAchievements '47 项'
Assert-Field $data WeeklyTopName 'zhenyu_05'
Assert-Field $data WeeklyTopHours '30.5 小时'
if (-not $data.DataHint.Contains('12:56') -or -not $data.DataHint.Contains('12:52')) { throw 'Independent update times missing' }
Write-Output 'PASS complete dashboard values, units, source dates and independent timestamps'

$data = Get-Dashboard '{"status":{"alive":true,"uptime_sec":0,"rss_kb":0,"entities":0,"world_size_kb":0,"tps_overall_ms":0,"tps_overworld_ms":0},"today":"2026-11-13","history":{"2026-11-13":0},"activity":{"peak_month":{"2026-11-13":0}},"playtime_players":0,"playtime_total_hours":0,"weekly":{"ach":0,"top":{"seconds":0}}}'
Assert-Field $data Uptime '0秒'
Assert-Field $data Memory '0.0 GiB'
Assert-Field $data EstimatedTps '20.0'
Assert-Field $data TodayActive '0'
Assert-Field $data TodayPeak '0'
Assert-Field $data SeasonPlayers '0'
Assert-Field $data WeekAchievements '0 项'
Write-Output 'PASS valid zero metrics and zero-duration tick estimate'

$data = Get-Dashboard '{"status":{"alive":false,"uptime_sec":1000,"rss_kb":1024,"entities":5,"tps_overall_ms":1,"tps_overworld_ms":2,"world_size_kb":1048576},"weekly":{"ach":3},"playtime_players":51}'
foreach ($key in @('Uptime','Memory','Entities','EstimatedTps','Mspt','OverworldMspt')) { Assert-Field $data $key $missing }
Assert-Field $data WorldSize '1.0 GiB'
Assert-Field $data SeasonPlayers '51'
Write-Output 'PASS offline runtime values withheld while persistent statistics remain'

foreach ($json in @('{}','{"status":[],"history":true,"activity":[],"season":false,"weekly":12,"playtime_players":null}','{"status":{"alive":"yes","entities":12},"today":"invalid","weekly":{"top":[]}}')) {
    $data = Get-Dashboard $json
    foreach ($key in @('Uptime','Memory','Entities','WorldSize','EstimatedTps','Mspt','OverworldMspt','TodayActive','TodayPeak','SeasonStart','SeasonPlayers','SeasonHours','WeekAchievements','WeeklyTopName','WeeklyTopHours')) { Assert-Field $data $key $missing }
    Assert-Field $data SeasonDay $missing
    Assert-Field $data AchievementPercent '0'
    Assert-Field $data Alive 'false'
}
Write-Output 'PASS missing sections, wrong types and invalid dates degrade independently'

$data = Get-Dashboard '{"status":{"alive":true,"uptime_sec":2147483648,"rss_kb":NaN,"entities":1.5,"tps_overall_ms":Infinity,"tps_overworld_ms":-1,"world_size_kb":"1024"},"today":"2026-11-13","history":{"2026-11-13":-1},"daily":{"2026-11-13":2},"activity":{"peak_month":{"2026-11-13":NaN},"peak_week":{"2026-11-13":1}},"weekly":{"ach":-1,"top":{"name":false,"seconds":-2}},"playtime_players":Infinity,"playtime_total_hours":NaN}'
foreach ($key in @('Uptime','Memory','Entities','WorldSize','EstimatedTps','Mspt','OverworldMspt','SeasonPlayers','SeasonHours','WeekAchievements','WeeklyTopName','WeeklyTopHours')) { Assert-Field $data $key $missing }
Assert-Field $data TodayActive '2'
Assert-Field $data TodayPeak '1'
Write-Output 'PASS invalid numbers, fractional counts, overflow and daily/weekly fallback'

$data = Get-Dashboard '{"status":{"alive":true,"tps_overall_ms":80},"stale":true}'
Assert-Field $data EstimatedTps '12.5'
if (-not $data.LoadHint.Contains('50 ms')) { throw 'Load warning missing' }
$freshHint = (Get-Dashboard '{}').DataHint
if ($data.DataHint -eq $freshHint -or (Get-Dashboard '{"ok":false}').DataHint -eq $freshHint) { throw 'Stale/failed source warning missing' }
Write-Output 'PASS service-side load estimate and stale/failed source warnings'
foreach ($json in @('{"season":{"day":-1},"achievements":{"unlocked":11,"total_achievements":10}}','{"season":{"day":1.5},"achievements":{"unlocked":NaN,"total_achievements":10}}','{"achievements":{"unlocked":1,"total_achievements":0}}')) {
    $data = Get-Dashboard $json
    Assert-Field $data SeasonDay $missing
    Assert-Field $data AchievementPercent '0'
}
Write-Output 'PASS invalid season days and out-of-range achievement progress'
Write-Output '7 dashboard regression groups passed.'
