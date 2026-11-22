param([string]$PreviewDirectory = 'C:\works\BRCL\calendar-20261118-checks')
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationFramework,PresentationCore,WindowsBase,System.Xaml,System.Net.Http
$project = Join-Path $PSScriptRoot '..\Plain Craft Launcher 2'
[void][Reflection.Assembly]::LoadFrom((Join-Path $project 'Resources\Newtonsoft.Json.dll'))
$assembly = [Reflection.Assembly]::LoadFrom((Join-Path $project 'bin\BRCL.exe'))
[Windows.Application]::ResourceAssembly = $assembly
$app = [Activator]::CreateInstance($assembly.GetType('BRCL.Application', $true), $true)
$app.InitializeComponent()
$startup = $app.GetType().GetMethod('Application_Startup', [Reflection.BindingFlags]'NonPublic,Instance')
$app.remove_Startup([Delegate]::CreateDelegate([Windows.StartupEventHandler], $app, $startup))
[Windows.Application].GetField('_startupUri', [Reflection.BindingFlags]'NonPublic,Instance').SetValue($app, $null)
$assembly.GetType('BRCL.ModAnimation').GetProperty('AniControlEnabled').SetValue($null, 1, $null)
$pageType = $assembly.GetType('BRCL.PageLaunchRight', $true)
$page = [Activator]::CreateInstance($pageType)
$flags = [Reflection.BindingFlags]'NonPublic,Instance'
$calendar = $pageType.GetMethod('UpdateCalendar', $flags)
$apply = $pageType.GetMethod('ApplyData', $flags)
$today = [DateTime]::Today
[Threading.SynchronizationContext]::SetSynchronizationContext([Windows.Threading.DispatcherSynchronizationContext]::new([Windows.Threading.Dispatcher]::CurrentDispatcher))
New-Item -ItemType Directory -Path $PreviewDirectory -Force | Out-Null
[string]$fortuneFixture = Join-Path $PreviewDirectory ('fortune-fixture-' + [Guid]::NewGuid().ToString('N') + '.json')
$pageType.GetField('FortunePath', $flags).SetValue($page, $fortuneFixture)
function Wait-Fortune($target) {
    $watch = [Diagnostics.Stopwatch]::StartNew()
    while ([bool]$pageType.GetField('FortuneBusy', $flags).GetValue($target)) {
        if ($watch.ElapsedMilliseconds -gt 5000) { throw 'Fortune UI did not finish its local work' }
        $frame = [Windows.Threading.DispatcherFrame]::new()
        $timer = [Windows.Threading.DispatcherTimer]::new()
        $timer.Interval = [TimeSpan]::FromMilliseconds(10)
        $timer.Add_Tick({$frame.Continue = $false})
        $timer.Start()
        [Windows.Threading.Dispatcher]::PushFrame($frame)
        $timer.Stop()
    }
}
function Wait-FortuneStage([scriptblock]$condition, [string]$label) {
    $watch = [Diagnostics.Stopwatch]::StartNew()
    while (-not (& $condition)) {
        if ($watch.ElapsedMilliseconds -gt 5000) { throw "Fortune stage timed out: $label" }
        $frame = [Windows.Threading.DispatcherFrame]::new()
        $timer = [Windows.Threading.DispatcherTimer]::new()
        $timer.Interval = [TimeSpan]::FromMilliseconds(10)
        $timer.Add_Tick({$frame.Continue = $false})
        $timer.Start()
        [Windows.Threading.Dispatcher]::PushFrame($frame)
        $timer.Stop()
    }
}
$motionHost = [Windows.Controls.Border]::new()
$motionHost.Background = $app.Resources['ColorBrush8']
function Save-FortuneStage([string]$name) {
    for ($settle = 0; $settle -lt 4; $settle++) {
        $motionHost.Measure([Windows.Size]::new(788, 548))
        $motionHost.Arrange([Windows.Rect]::new(0, 0, 788, 548))
        $motionHost.UpdateLayout()
    }
    $bitmap = [Windows.Media.Imaging.RenderTargetBitmap]::new(788, 548, 96, 96, [Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($motionHost)
    $encoder = [Windows.Media.Imaging.PngBitmapEncoder]::new()
    $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $output = [IO.File]::Create((Join-Path $PreviewDirectory ($name + '.png')))
    try { $encoder.Save($output) } finally { $output.Dispose() }
}

foreach ($dateText in @('2026-11-18', '2026-11-30', '2027-01-01', '2028-02-29', '2025-07-25', '2102-01-01')) {
    $date = [DateTime]::ParseExact($dateText, 'yyyy-MM-dd', [Globalization.CultureInfo]::InvariantCulture)
    $calendar.Invoke($page, @($date)) | Out-Null
    if ($page.FindName('LabCalendarDay').Text -ne $date.ToString('dd')) { throw "Wrong day: $dateText" }
    $tiles = $page.FindName('PanCalendarWeek').Children
    if ($tiles.Count -ne 7) { throw "Incomplete week: $dateText" }
    $monday = $date.AddDays(-(([int]$date.DayOfWeek + 6) % 7))
    $highlighted = 0
    for ($i = 0; $i -lt 7; $i++) {
        if (-not ([string]$tiles[$i].ToolTip).StartsWith($monday.AddDays($i).ToString('yyyy-MM-dd'))) { throw "Incorrect week across month/year: $dateText" }
        if ($null -ne $tiles[$i].Background) { $highlighted++ }
    }
    if ($highlighted -ne 1) { throw "Today is not uniquely highlighted: $dateText" }
    if ($dateText -eq '2025-07-25' -and $page.FindName('LabCalendarLunar').Text -ne '农历闰六月初一') { throw 'Leap lunar month is incorrect' }
    if ($dateText -eq '2102-01-01' -and $page.FindName('LabCalendarLunar').Text -ne '每天，都是新的存档。') { throw 'Unsupported lunar dates need a fallback' }
    Write-Output "PASS calendar date/week/lunar $dateText"
}
$calendar.Invoke($page, @($today)) | Out-Null
Wait-Fortune $page
if ($null -ne $page.FindName('BtnCalendarIdea')) { throw 'Calendar still contains a refresh button' }
if (-not $page.FindName('BtnDrawFortune').IsEnabled) { throw "Undrawn day does not allow a draw; page enabled=$($page.IsEnabled), busy=$($pageType.GetField('FortuneBusy', $flags).GetValue($page)), content=$($page.FindName('BtnDrawFortune').Content)" }
$motionHost.Child = $page
$drawClock = [Diagnostics.Stopwatch]::StartNew()
$page.FindName('BtnDrawFortune').RaiseEvent([Windows.RoutedEventArgs]::new([Windows.Controls.Button]::ClickEvent))
$page.FindName('BtnDrawFortune').RaiseEvent([Windows.RoutedEventArgs]::new([Windows.Controls.Button]::ClickEvent))
if ($page.FindName('PanFortuneDrawing').Visibility -ne 'Visible' -or $page.FindName('PanFortuneResult').Visibility -ne 'Collapsed' -or $page.FindName('LabCalendarHint').Visibility -ne 'Hidden') { throw 'Shake phase exposed the grade or blessing early' }
if (-not $page.FindName('FortuneShakeTransform').HasAnimatedProperties -or -not $page.FindName('FortuneStickRise').HasAnimatedProperties) { throw 'Draw has no active shaker/stick animation' }
Wait-FortuneStage { Test-Path -LiteralPath $fortuneFixture } 'save before reveal'
if ($page.FindName('PanFortuneDrawing').Visibility -ne 'Visible') { throw 'Saved result skipped the shaking ceremony' }
Save-FortuneStage 'fortune-1-shaking'
Wait-FortuneStage { $page.FindName('PanFortuneResult').Visibility -eq 'Visible' } 'grade reveal'
if ($page.FindName('LabCalendarHint').Visibility -ne 'Hidden' -or $drawClock.ElapsedMilliseconds -lt 1100) { throw 'Blessing appeared before the grade reveal pause' }
Wait-FortuneStage { $page.FindName('PanFortuneResult').Opacity -ge 0.8 } 'grade fade in'
if ($page.FindName('LabCalendarHint').Visibility -ne 'Hidden') { throw 'Blessing did not leave time to read the grade first' }
Save-FortuneStage 'fortune-2-grade-reveal'
Wait-FortuneStage { $page.FindName('LabCalendarHint').Visibility -eq 'Visible' } 'blessing reveal'
if ($page.FindName('PanFortuneResult').Visibility -ne 'Visible' -or $drawClock.ElapsedMilliseconds -lt 1550) { throw 'Blessing did not follow the revealed grade' }
Wait-Fortune $page
if ($page.FindName('FortuneShakeTransform').HasAnimatedProperties -or $page.FindName('LabCalendarHint').HasAnimatedProperties) { throw 'Completed draw left animation clocks running' }
Save-FortuneStage 'fortune-3-blessing'
$motionHost.Child = $null
Write-Output 'PASS shake, saved-before-reveal, grade-first reveal, delayed blessing and animation cleanup'
$initialIdea = $page.FindName('LabCalendarIdea').Text
$initialBlessing = $page.FindName('LabCalendarHint').Text
if (-not $initialIdea -or -not $initialBlessing -or $page.FindName('BtnDrawFortune').Visibility -ne 'Collapsed') { throw 'Saved draw was not revealed or repeat button remained visible' }
$savedFortune = [IO.File]::ReadAllText($fortuneFixture)
$page.FindName('BtnDrawFortune').RaiseEvent([Windows.RoutedEventArgs]::new([Windows.Controls.Button]::ClickEvent))
Wait-Fortune $page
if ([IO.File]::ReadAllText($fortuneFixture) -cne $savedFortune) { throw 'Repeated click rerolled the saved fortune' }
$reopenedPage = [Activator]::CreateInstance($pageType)
$pageType.GetField('FortunePath', $flags).SetValue($reopenedPage, $fortuneFixture)
$calendar.Invoke($reopenedPage, @($today)) | Out-Null
Wait-Fortune $reopenedPage
if ($reopenedPage.FindName('LabCalendarIdea').Text -ne $initialIdea -or $reopenedPage.FindName('LabCalendarHint').Text -ne $initialBlessing) { throw 'New page did not restore the exact saved grade and blessing' }
$calendar.Invoke($page, @($today.AddDays(1))) | Out-Null
Wait-Fortune $page
if ($page.FindName('BtnDrawFortune').Visibility -ne 'Visible' -or -not $page.FindName('BtnDrawFortune').IsEnabled) { throw 'Next day did not unlock the draw' }
$calendar.Invoke($page, @($today)) | Out-Null
Wait-Fortune $page
if ($page.FindName('LabCalendarIdea').Text -ne $initialIdea) { throw 'Previous day lost its saved fortune' }
Write-Output 'PASS daily draw, double-click guard, new page restoration and midnight reset'
$failingPage = [Activator]::CreateInstance($pageType)
$pageType.GetField('FortunePath', $flags).SetValue($failingPage, [string](Join-Path $fortuneFixture 'unwritable.json'))
$calendar.Invoke($failingPage, @($today)) | Out-Null
Wait-Fortune $failingPage
$failingPage.FindName('BtnDrawFortune').RaiseEvent([Windows.RoutedEventArgs]::new([Windows.Controls.Button]::ClickEvent))
Wait-Fortune $failingPage
if ($failingPage.FindName('PanFortuneResult').Visibility -ne 'Collapsed' -or -not $failingPage.FindName('BtnDrawFortune').IsEnabled -or $failingPage.FindName('BtnDrawFortune').Content -ne '重试抽签') { throw 'Save failure revealed an unsaved fortune or consumed the draw' }
Write-Output 'PASS save failure stays retryable and does not reveal a result'
$motionSetting = $assembly.GetType('BRCL.Settings').GetField('Entries').GetValue($null)['UiReduceMotion']
$previousMotion = [bool]$motionSetting.GetCache($null)
try {
    $motionSetting.Set($true, $null)
    $reducedPage = [Activator]::CreateInstance($pageType)
    $pageType.GetField('FortunePath', $flags).SetValue($reducedPage, [string](Join-Path $PreviewDirectory ('reduced-' + [Guid]::NewGuid().ToString('N') + '.json')))
    $calendar.Invoke($reducedPage, @($today)) | Out-Null
    Wait-Fortune $reducedPage
    $reducedPage.FindName('BtnDrawFortune').RaiseEvent([Windows.RoutedEventArgs]::new([Windows.Controls.Button]::ClickEvent))
    if ($reducedPage.FindName('PanFortuneDrawing').Visibility -ne 'Collapsed' -or $reducedPage.FindName('FortuneShakeTransform').HasAnimatedProperties) { throw 'Reduced motion still plays the shaker animation' }
    Wait-Fortune $reducedPage
    if ($reducedPage.FindName('PanFortuneResult').Visibility -ne 'Visible' -or $reducedPage.FindName('LabCalendarHint').Visibility -ne 'Visible') { throw 'Reduced motion did not reveal the saved result and blessing' }
} finally { $motionSetting.Set($previousMotion, $null) }
Write-Output 'PASS reduced motion reveals saved result without shaking or artificial delay'

function Set-Status([bool]$alive, $count, [bool]$stale) {
    $data = [Newtonsoft.Json.Linq.JObject]::Parse('{"server":{"pack":"The Retired Machinist（退休机械工）","season_label":"第九周目"},"season":{"day":28},"status":{"alive":true,"player_count":0}}')
    $data['status']['alive'] = [Newtonsoft.Json.Linq.JValue]::new($alive)
    if ($null -eq $count) { $data['status'].Remove('player_count') | Out-Null }
    else { $data['status']['player_count'] = [Newtonsoft.Json.Linq.JValue]::new([int]$count) }
    $data['stale'] = [Newtonsoft.Json.Linq.JValue]::new($stale)
    $data['updated_at'] = [Newtonsoft.Json.Linq.JValue]::new([DateTimeOffset]::Now.ToString('o'))
    $apply.Invoke($page, @($data, $false)) | Out-Null
}
Set-Status $true 0 $false
if ($page.FindName('LabHomeState').Text -ne '● 服务器在线' -or $page.FindName('LabHomePlayers').Text -ne '当前人数 0') { throw 'Zero online players displayed incorrectly' }
Set-Status $true 12 $false
if ($page.FindName('LabHomePlayers').Text -ne '当前人数 12') { throw 'Current count displayed incorrectly' }
Set-Status $true $null $false
if ($page.FindName('LabHomePlayers').Text -ne '当前人数 —') { throw 'Missing player count fabricated a value' }
Set-Status $false 12 $false
if ($page.FindName('LabHomeState').Text -ne '○ 服务器离线' -or $page.FindName('LabHomePlayers').Text -ne '当前人数 0') { throw 'Offline status displayed incorrectly' }
Set-Status $true 12 $true
if ($page.FindName('LabHomeState').Text -ne '● 服务器状态未知' -or $page.FindName('LabHomePlayers').Text -ne '当前人数 —') { throw 'Stale data presented as live status' }
$pageType.GetMethod('ShowUnavailable', $flags).Invoke($page, @()) | Out-Null
if ($page.FindName('LabCalendarDay').Text -ne $today.ToString('dd') -or $page.FindName('PanCalendarWeek').Children.Count -ne 7) { throw 'Network failure affected the calendar' }
Write-Output 'PASS live/zero/missing/offline/stale/unavailable server states'
Set-Status $true 6 $false

New-Item -ItemType Directory -Path $PreviewDirectory -Force | Out-Null
$hostBorder = [Windows.Controls.Border]::new()
$hostBorder.Child = $page
foreach ($theme in @('dark', 'light')) {
    $palette = if ($theme -eq 'dark') {
        @{ColorBrush1='#F1F2E9';ColorBrush2='#B3BAB9';ColorBrush3='#F2D649';ColorBrush6='#272C2E';ColorBrush7='#1C2022';ColorBrush8='#111416';ColorBrushCardLine='#374041';IndustrialAccent='#F2D649';IndustrialOnAccent='#191D1F'}
    } else {
        @{ColorBrush1='#242B2D';ColorBrush2='#626C6C';ColorBrush3='#7D6900';ColorBrush6='#E9EBE5';ColorBrush7='#F9FAF5';ColorBrush8='#F0F2EA';ColorBrushCardLine='#CDD2C9';IndustrialAccent='#F2D649';IndustrialOnAccent='#191D1F'}
    }
    foreach ($key in $palette.Keys) {
        $brush = [Windows.Media.BrushConverter]::new().ConvertFromString($palette[$key])
        $app.Resources[$key] = $brush
        # Unmounted pages need local resources for live palette invalidation.
        $page.Resources[$key] = $brush
    }
    [Windows.Documents.TextElement]::SetForeground($hostBorder, $app.Resources['ColorBrush1'])
    $hostBorder.Background = $app.Resources['ColorBrush8']
    foreach ($width in @(380, 628, 788, 900)) {
      foreach ($height in @(500, 548, 650)) {
       foreach ($fortuneState in @('undrawn', 'drawn')) {
        $sampleFortune = if ($fortuneState -eq 'drawn') {
            [Activator]::CreateInstance($assembly.GetType('BRCL.BrclFortuneResult'), [object[]]@($today, '大吉', '愿你总有重新出发的勇气，也有歇一歇的底气。'))
        } else { $null }
        $pageType.GetMethod('ShowFortune', $flags).Invoke($page, [object[]]@($sampleFortune, $null)) | Out-Null
       for ($settle = 0; $settle -lt 4; $settle++) {
        $hostBorder.Measure([Windows.Size]::new($width, $height))
        $hostBorder.Arrange([Windows.Rect]::new(0, 0, $width, $height))
        $hostBorder.UpdateLayout()
       }
        $state = $page.FindName('LabHomeState')
        $pack = $page.FindName('LabHomePack')
        if ($state.TranslatePoint([Windows.Point]::new(0, $state.ActualHeight), $page).Y -gt $pack.TranslatePoint([Windows.Point]::new(0, 0), $page).Y) { throw 'Server status is not above the pack title' }
        $season = $page.FindName('LabHomeSeason')
        if ($state.TranslatePoint([Windows.Point]::new(0, 0), $page).Y -lt $season.TranslatePoint([Windows.Point]::new(0, $season.ActualHeight), $page).Y) { throw "Server status overlaps season badge: $width $height" }
        $tagline = $page.FindName('LabHomeTagline')
        $seasonDay = $page.FindName('LabHomeDay')
        $english = $page.FindName('LabHomePackEnglish')
        if ($english.TranslatePoint([Windows.Point]::new(0, $english.ActualHeight), $page).Y + 8 -gt $tagline.TranslatePoint([Windows.Point]::new(0, 0), $page).Y) { throw "Hero title overlaps footer: $width $height" }
        if ($tagline.TranslatePoint([Windows.Point]::new($tagline.ActualWidth, 0), $page).X -gt $seasonDay.TranslatePoint([Windows.Point]::new(0, 0), $page).X) { throw "Tagline overlaps season day: $width $height" }
        $card = $page.FindName('CalendarCard')
        $hint = $page.FindName('LabCalendarHint')
        if ($hint.TranslatePoint([Windows.Point]::new(0, $hint.ActualHeight), $card).Y -gt $card.ActualHeight) { throw "Calendar overflows at $theme $width" }
        $scroll = $page.FindName('PanBack')
        if ($width -ge 620 -and $scroll.ScrollableHeight -gt 0.5) { throw "Unnecessary home scrollbar: $theme $width x $height ($($scroll.ScrollableHeight))" }
        if ($width -ge 620 -and [Windows.Controls.Grid]::GetColumn($page.FindName('CalendarIdea')) -ne 3) { throw 'Regular-width calendar did not use three columns' }
        if ($width -lt 620 -and [Windows.Controls.Grid]::GetRow($page.FindName('CalendarIdea')) -ne 2) { throw 'Narrow calendar did not reflow' }
        if ($width -ge 620 -and $page.FindName('PanCalendarWeek').ActualWidth -gt 260.5) { throw 'Week dates stretched too far' }
        $bitmap = [Windows.Media.Imaging.RenderTargetBitmap]::new($width, $height, 96, 96, [Windows.Media.PixelFormats]::Pbgra32)
        $bitmap.Render($hostBorder)
        $encoder = [Windows.Media.Imaging.PngBitmapEncoder]::new()
        $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
        $suffix = if ($height -eq 650) { '' } else { "-$height" }
        $file = [IO.File]::Create((Join-Path $PreviewDirectory "home-$theme-$width$suffix-$fortuneState.png"))
        try { $encoder.Save($file) } finally { $file.Dispose() }
        Write-Output "PASS compiled layout $theme $width x $height $fortuneState"
       }
      }
    }
}
$hero = $page.FindName('HeroImage')
$setHeroDpi = $pageType.GetMethod('UpdateHeroImage', $flags, $null, [type[]]@([Windows.DpiScale]), $null)
[void]$setHeroDpi.Invoke($page, [object[]]@([Windows.DpiScale]::new(1, 1)))
$standard = $hero.Source
if ($null -eq $standard -or $standard.PixelWidth -ge 1672 -or $standard.PixelWidth -lt $hero.ActualWidth) { throw 'Standard-DPI home did not decode to a suitable smaller size.' }
[void]$setHeroDpi.Invoke($page, [object[]]@([Windows.DpiScale]::new(1, 1)))
if (-not [object]::ReferenceEquals($standard, $hero.Source)) { throw 'Unchanged home dimensions decoded the hero again.' }
[void]$setHeroDpi.Invoke($page, [object[]]@([Windows.DpiScale]::new(3, 3)))
if ($hero.Source.PixelWidth -ne 1672 -or $hero.Source.PixelHeight -ne 941) { throw 'High-DPI home did not retain the native artwork dimensions.' }
Write-Output 'PASS homepage decodes at the displayed DPI, reuses the selected size and retains the full-size artwork'
$app.Shutdown()
