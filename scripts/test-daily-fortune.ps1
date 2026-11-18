param([string]$OutputDirectory = 'C:\works\BRCL\daily-fortune-20261118-checks')
$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot '..\Plain Craft Launcher 2'
[void][Reflection.Assembly]::LoadFrom((Join-Path $project 'Resources\Newtonsoft.Json.dll'))
$assembly = [Reflection.Assembly]::LoadFrom((Join-Path $project 'bin\BRCL.exe'))
$store = $assembly.GetType('PCL.BrclDailyFortune', $true)
$read = $store.GetMethod('ReadForDate')
$draw = $store.GetMethod('DrawForDate')
$choose = $store.GetMethod('ChooseGrade', [Reflection.BindingFlags]'NonPublic,Static')
$day = [DateTime]::Today
$testRoot = Join-Path $OutputDirectory ('store-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
[string]$record = Join-Path $testRoot 'fortune.json'
if ($null -ne $read.Invoke($null, @($record, $day)) -or (Test-Path -LiteralPath $record)) { throw 'Reading an undrawn day consumed the draw' }
$grades = @{}
for ($roll = 0; $roll -lt 100; $roll++) {
    $grade = [string]$choose.Invoke($null, @($roll))
    if (-not $grades.ContainsKey($grade)) { $grades[$grade] = 0 }
    $grades[$grade]++
}
if ($grades.Count -ne 6 -or (@($grades.Values | Sort-Object) -join ',') -ne '2,8,15,20,25,30') { throw 'Fortune outcome boundaries are incorrect' }
Write-Output 'PASS six possible grades and all probability boundaries'
$first = $draw.Invoke($null, @($record, $day))
if (-not $grades.ContainsKey($first.Grade) -or [string]::IsNullOrWhiteSpace($first.Blessing) -or $first.Day -ne $day) { throw 'Draw did not return a grade and blessing for today' }
$saved = [IO.File]::ReadAllText($record)
for ($i = 0; $i -lt 12; $i++) {
    $repeat = $draw.Invoke($null, @($record, $day))
    if ($repeat.Grade -ne $first.Grade -or $repeat.Blessing -ne $first.Blessing -or [IO.File]::ReadAllText($record) -cne $saved) { throw 'Repeated draw changed the saved result' }
}
$reopened = $read.Invoke($null, @($record, $day))
if ($reopened.Grade -ne $first.Grade -or $reopened.Blessing -ne $first.Blessing) { throw 'Reopening lost the grade or blessing' }
Write-Output 'PASS once per day, repeated clicks and saved grade/blessing restoration'
if ($null -ne $read.Invoke($null, @($record, $day.AddDays(1)))) { throw 'Previous day result blocks a new day' }
$next = $draw.Invoke($null, @($record, $day.AddDays(1)))
if ($next.Day -ne $day.AddDays(1) -or $null -eq $read.Invoke($null, @($record, $day.AddDays(1)))) { throw 'New day cannot draw and persist' }
Write-Output 'PASS new date unlocks a new saved draw'
$lockedBytes = [IO.File]::ReadAllText($record)
$lock = [IO.FileStream]::new($record + '.lock', [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
$blocked = $false
try { $draw.Invoke($null, @($record, $day.AddDays(2))) | Out-Null }
catch { $blocked = $true }
finally { $lock.Dispose() }
if (-not $blocked -or [IO.File]::ReadAllText($record) -cne $lockedBytes) { throw 'Competing draw bypassed the file lock' }
$afterLock = $draw.Invoke($null, @($record, $day.AddDays(2)))
if ($afterLock.Day -ne $day.AddDays(2)) { throw 'Lock contention consumed the draw' }
if (@(Get-ChildItem -LiteralPath $testRoot -Filter '*.tmp').Count -ne 0) { throw 'Atomic save left temporary files' }
Write-Output 'PASS competing draw is blocked safely and can retry without consuming the day'
[string]$invalid = Join-Path $testRoot 'invalid.json'
[IO.File]::WriteAllText($invalid, '{"version":1,"date":"broken","grade":"unknown","blessing":""}')
$rejected = $false
try { $draw.Invoke($null, @($invalid, $day)) | Out-Null }
catch { $rejected = $true }
if (-not $rejected -or [IO.File]::ReadAllText($invalid) -notlike '*broken*') { throw 'Invalid saved draw was overwritten silently' }
$blockedParent = Join-Path $testRoot 'file-not-directory'
[IO.File]::WriteAllText($blockedParent, 'fixture')
$failed = $false
try { $draw.Invoke($null, @([string](Join-Path $blockedParent 'fortune.json'), $day)) | Out-Null }
catch { $failed = $true }
if (-not $failed) { throw 'Save failure returned an unsaved result' }
Write-Output 'PASS invalid records and save failures never reveal an unsaved replacement'
