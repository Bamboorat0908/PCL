# Run with Windows PowerShell 5.1 against the built .NET Framework assembly.
param([string]$BuildDirectory)
$ErrorActionPreference = 'Stop'
$projectRoot = Join-Path $PSScriptRoot '..\Plain Craft Launcher 2'
Add-Type -AssemblyName PresentationFramework
Add-Type -AssemblyName System.Net.Http
[void][Reflection.Assembly]::LoadFrom((Join-Path $projectRoot 'Resources\Newtonsoft.Json.dll'))
if (-not $BuildDirectory) { $BuildDirectory = Join-Path $projectRoot 'bin' }
$assembly = [Reflection.Assembly]::LoadFrom((Join-Path $BuildDirectory 'BRCL.exe'))
$pageType = $assembly.GetType('BRCL.PageBRS', $true)
$method = $pageType.GetMethod('ValidateData', [Reflection.BindingFlags]'NonPublic, Static')
if ($null -eq $method) { throw 'ValidateData was not found in the built assembly' }
$cases = @(
    @{ Name='online status'; Json='{"status":{"alive":true,"player_count":2,"max_players":100,"tps_overall_ms":22.965}}'; Valid=$true },
    @{ Name='offline status'; Json='{"status":{"alive":false}}'; Valid=$true },
    @{ Name='zero values'; Json='{"status":{"alive":true,"player_count":0,"tps_overall_ms":0,"world_size_kb":0}}'; Valid=$true },
    @{ Name='optional null'; Json='{"status":{"alive":false,"uptime_sec":null}}'; Valid=$true },
    @{ Name='missing status'; Json='{}'; Valid=$false },
    @{ Name='invalid status type'; Json='{"status":[]}'; Valid=$false },
    @{ Name='missing required alive'; Json='{"status":{}}'; Valid=$false },
    @{ Name='invalid alive type'; Json='{"status":{"alive":"yes"}}'; Valid=$false },
    @{ Name='optional negative player count accepted'; Json='{"status":{"alive":true,"player_count":-1}}'; Valid=$true },
    @{ Name='optional invalid numeric type accepted'; Json='{"status":{"alive":true,"entities":"oops"}}'; Valid=$true },
    @{ Name='optional fractional player count accepted'; Json='{"status":{"alive":true,"player_count":1.5}}'; Valid=$true },
    @{ Name='optional integer overflow accepted'; Json='{"status":{"alive":true,"uptime_sec":2147483648}}'; Valid=$true },
    @{ Name='optional NaN metric accepted'; Json='{"status":{"alive":true,"tps_overall_ms":NaN}}'; Valid=$true },
    @{ Name='optional infinite metric accepted'; Json='{"status":{"alive":true,"world_size_kb":Infinity}}'; Valid=$true },
    @{ Name='optional invalid maximum players accepted'; Json='{"status":{"alive":true,"max_players":"unknown"}}'; Valid=$true },
    @{ Name='optional invalid metric containers accepted'; Json='{"status":{"alive":false,"player_count":[],"max_players":false,"rss_kb":{},"threads":null}}'; Valid=$true }
)
foreach ($case in $cases) {
    $parsed = [Newtonsoft.Json.Linq.JObject]::Parse($case.Json)
    $accepted = $true
    try { [void]$method.Invoke($null, [object[]](,$parsed)) }
    catch {
        $failure = $_.Exception
        while ($failure.InnerException) { $failure = $failure.InnerException }
        if ($failure -isnot [FormatException]) { throw }
        $accepted = $false
    }
    if ($accepted -ne $case.Valid) { throw "Unexpected validation result: $($case.Name)" }
    Write-Output "PASS $($case.Name)"
}
Write-Output "$($cases.Count) BRS data regression cases passed; optional metrics don't reject whole response."
$formatMethod = $pageType.GetMethod('FormatUpdatedAt', [Reflection.BindingFlags]'NonPublic, Static')
$originalCulture = [Threading.Thread]::CurrentThread.CurrentCulture
try {
    foreach ($culture in @('zh-CN', 'en-US')) {
        [Threading.Thread]::CurrentThread.CurrentCulture = [Globalization.CultureInfo]::GetCultureInfo($culture)
        $date = [Newtonsoft.Json.Linq.JObject]::Parse('{"updated_at":"2026-11-12T19:25:17"}')
        $formatted = $formatMethod.Invoke($null, [object[]](,$date['updated_at']))
        if ($formatted -ne '11-12 19:25') { throw "Incorrect timestamp in $culture : $formatted" }
        Write-Output "PASS parsed timestamp in $culture"
    }
    foreach ($raw in @('{"updated_at":null}', '{"updated_at":"invalid"}')) {
        $date = [Newtonsoft.Json.Linq.JObject]::Parse($raw)
        $formatted = $formatMethod.Invoke($null, [object[]](,$date['updated_at']))
        if ($formatted -ne [string][char]0x2014) { throw 'Invalid timestamp must show a placeholder' }
        Write-Output 'PASS unavailable timestamp'
    }
}
finally { [Threading.Thread]::CurrentThread.CurrentCulture = $originalCulture }
Write-Output '4 timestamp regression cases passed.'

# Activity statistics are optional and must degrade independently of server status.
$summaryMethod = $pageType.GetMethod('BuildActivitySummary', [Reflection.BindingFlags]'NonPublic, Static')
$summaryCases = @(
    @{ Name='complete activity'; Json='{"server":{"pack":"TRM"},"weekly":{"from":"2026-11-09","to":"2026-11-15","days":5,"data_days":4,"players":18,"hours":162.2,"newcomers":2,"ach":42},"achievements":{"unlocked":670,"total_achievements":1086},"today":"2026-11-13","history":{"2026-11-13":5},"activity":{"peak_month":{"2026-11-13":3}},"activity_updated_at":"2026-11-13T00:05:59"}'; Expected=@{ WeekPlayers='18'; WeekHours='162.2'; WeekNewcomers='2'; AchievementProgress='61.7%' }; Range='4/5'; Today='5|3' },
    @{ Name='missing activity'; Json='{}'; Expected=@{ WeekPlayers=$null; WeekHours=$null; WeekNewcomers=$null; AchievementProgress=$null } },
    @{ Name='null activity'; Json='{"weekly":null,"achievements":null,"activity":null,"today":null,"server":{"pack":null}}'; Expected=@{ WeekPlayers=$null; AchievementProgress=$null } },
    @{ Name='zero activity'; Json='{"weekly":{"players":0,"hours":0,"newcomers":0},"achievements":{"unlocked":0,"total_achievements":100},"today":"2026-11-13","history":{"2026-11-13":0},"activity":{"peak_month":{"2026-11-13":0}}}'; Expected=@{ WeekPlayers='0'; WeekHours='0.0'; WeekNewcomers='0'; AchievementProgress='0.0%' }; Today='0|0' },
    @{ Name='daily fallback'; Json='{"today":"2026-11-13","history":{"2026-11-12":99},"daily":{"2026-11-13":2},"activity":{"peak_month":{"2026-11-13":1}}}'; Expected=@{}; Today='2|1' },
    @{ Name='invalid numbers'; Json='{"weekly":{"players":1.5,"hours":-1,"newcomers":"2"},"achievements":{"unlocked":101,"total_achievements":100}}'; Expected=@{ WeekPlayers=$null; WeekHours=$null; WeekNewcomers=$null; AchievementProgress=$null } },
    @{ Name='nonfinite numbers'; Json='{"weekly":{"players":2147483648,"hours":NaN,"newcomers":Infinity},"achievements":{"unlocked":1,"total_achievements":0}}'; Expected=@{ WeekPlayers=$null; WeekHours=$null; WeekNewcomers=$null; AchievementProgress=$null } },
    @{ Name='wrong section types'; Json='{"server":[],"weekly":[],"achievements":true,"activity":false,"today":"invalid"}'; Expected=@{ WeekPlayers=$null; AchievementProgress=$null } },
    @{ Name='refresh resets old data'; Json='{"weekly":{"players":7},"achievements":{"unlocked":5}}'; Expected=@{ WeekPlayers='7'; WeekHours=$null; WeekNewcomers=$null; AchievementProgress=$null } }
)
foreach ($case in $summaryCases) {
    $parsed = [Newtonsoft.Json.Linq.JObject]::Parse($case.Json)
    $summary = $summaryMethod.Invoke($null, [object[]](,$parsed))
    foreach ($key in $case.Expected.Keys) {
        $expected = $case.Expected[$key]
        if ($null -eq $expected) { $expected = [string][char]0x2014 }
        if ($summary[$key] -ne $expected) { throw "Incorrect $key in $($case.Name): $($summary[$key])" }
    }
    if ($case.Range -and -not $summary['WeekRange'].Contains($case.Range)) { throw 'Incomplete weekly coverage must be visible' }
    if ($case.Today) {
        $numbers = [regex]::Matches($summary['TodayActivity'], '\d+') | ForEach-Object { $_.Value }
        if (($numbers -join '|') -ne $case.Today) { throw "Incorrect daily activity in $($case.Name)" }
    }
    Write-Output "PASS $($case.Name)"
}
Write-Output "$($summaryCases.Count) activity summary regression cases passed."

$refreshSeconds = $pageType.GetField('RefreshIntervalSeconds', [Reflection.BindingFlags]'NonPublic, Static').GetRawConstantValue()
if ($refreshSeconds -ne 15) { throw 'BRS must refresh every 15 seconds' }
Write-Output 'PASS 15-second refresh interval'

$leaderboardMethod = $pageType.GetMethod('BuildLeaderboard', [Reflection.BindingFlags]'NonPublic, Static')
$rankSource = [Newtonsoft.Json.Linq.JArray]::Parse('[{"name":"B","seconds":3600,"sessions":2},{"name":"A","seconds":7200},{"name":"b","seconds":100},{"name":"bad","seconds":-1},{"name":" ","seconds":99},false,{"name":"nan","seconds":NaN}]')
$ranks = $leaderboardMethod.Invoke($null, [object[]]@($rankSource, $false))
if ($ranks.Count -ne 2 -or $ranks[0]['name'].ToString() -ne 'A' -or $ranks[1]['name'].ToString() -ne 'B') { throw 'Ranks must be sorted, unique and valid' }
if ($ranks[0]['display'].ToString() -notmatch '^2\.0 ' -or $ranks[1]['meta'].ToString() -notmatch '2') { throw 'Hours or session metadata are incorrect' }
Write-Output 'PASS playtime ranking normalization'
$rankSource = [Newtonsoft.Json.Linq.JArray]::Parse('[{"name":"A","real":12},{"name":"B","real":20},{"name":"invalid","real":1.5}]')
$ranks = $leaderboardMethod.Invoke($null, [object[]]@($rankSource, $true))
if ($ranks.Count -ne 2 -or $ranks[0]['name'].ToString() -ne 'B' -or $ranks[0]['display'].ToString() -notmatch '^20 ') { throw 'Achievement ranking is incorrect' }
Write-Output 'PASS achievement ranking normalization'
$ranks = $leaderboardMethod.Invoke($null, [object[]]@($null, $false))
if ($ranks.Count -ne 0) { throw 'Missing leaderboard must be empty' }
Write-Output 'PASS missing leaderboard'

$trendMethod = $pageType.GetMethod('SelectTrendHistory', [Reflection.BindingFlags]'NonPublic, Static')
$history = [Newtonsoft.Json.Linq.JObject]::Parse('{"today":"2026-11-13","activity":{"month":{"2026-10-13":5,"2026-10-15":4,"2026-11-06":3,"2026-11-07":2,"2026-11-13":0,"2026-11-14":99,"invalid":1,"2026-11-10":-1}}}')
$week = $trendMethod.Invoke($null, [object[]]@($history, 7))
$month = $trendMethod.Invoke($null, [object[]]@($history, 30))
if ($week.get_Count() -ne 2 -or $null -eq $week['2026-11-13'] -or $null -ne $week['2026-11-06']) { throw '7-day window or zero handling is incorrect' }
if ($month.get_Count() -ne 4 -or $null -eq $month['2026-10-15'] -or $null -ne $month['2026-10-13']) { throw '30-day window is incorrect' }
Write-Output 'PASS 7/30-day activity windows'
$history = [Newtonsoft.Json.Linq.JObject]::Parse('{"history":{"2026-11-13":2}}')
$week = $trendMethod.Invoke($null, [object[]]@($history, 7))
if ($week.get_Count() -ne 1 -or $week['2026-11-13'].ToString() -ne '2') { throw 'Legacy history fallback is incorrect' }
Write-Output 'PASS legacy activity fallback'
$history = [Newtonsoft.Json.Linq.JObject]::Parse('{"activity":[],"history":null}')
$week = $trendMethod.Invoke($null, [object[]]@($history, 7))
if ($week.get_Count() -ne 0) { throw 'Unavailable activity must be empty' }
Write-Output 'PASS unavailable activity window'
Write-Output '7 website content and refresh regression cases passed.'
