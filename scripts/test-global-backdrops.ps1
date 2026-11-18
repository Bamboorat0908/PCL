# Native global backdrop coverage and offscreen visual checks. Run in Windows PowerShell 5.1, STA.
param(
    [string]$BuildDirectory,
    [string]$PreviewDirectory='C:\works\BRCL\global-ui-20261113-checks\backdrops',
    [string]$LightBaselineDirectory,
    [switch]$MeasureFrames
)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName PresentationFramework,PresentationCore,WindowsBase,System.Xaml,System.Net.Http
Add-Type -TypeDefinition @'
public static class BackdropPixelChecks {
    public static int CountYellow(byte[] pixels) {
        int count = 0;
        for (int i = 0; i < pixels.Length; i += 4) {
            int blue = pixels[i], green = pixels[i + 1], red = pixels[i + 2];
            if (red - blue >= 12 && green - blue >= 9 && red >= green - 1) count++;
        }
        return count;
    }
}
'@
$project=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\Plain Craft Launcher 2'))
if(-not $BuildDirectory){$BuildDirectory=Join-Path $project 'bin'}
$assembly=[Reflection.Assembly]::LoadFrom((Join-Path $BuildDirectory 'BRCL.exe'))
[Windows.Application]::ResourceAssembly=$assembly
$app=[Activator]::CreateInstance($assembly.GetType('PCL.Application',$true),$true)
$app.InitializeComponent()
$startup=$app.GetType().GetMethod('Application_Startup',[Reflection.BindingFlags]'NonPublic,Instance')
if($startup){$app.remove_Startup([Delegate]::CreateDelegate([Windows.StartupEventHandler],$app,$startup))}
$unhandled=$app.GetType().GetMethod('Application_DispatcherUnhandledException',[Reflection.BindingFlags]'NonPublic,Instance')
if($unhandled){$app.remove_DispatcherUnhandledException([Delegate]::CreateDelegate([Windows.Threading.DispatcherUnhandledExceptionEventHandler],$app,$unhandled))}
[Windows.Application].GetField('_startupUri',[Reflection.BindingFlags]'NonPublic,Instance').SetValue($app,$null)
$type=$assembly.GetType('PCL.IndustrialBackdrop',$true)
$pageType=$assembly.GetType('PCL.MyPageRight',$true)
$backdrop=[Activator]::CreateInstance($type)
$lightThemeProperty=$type.GetField('IsLightThemeProperty',[Reflection.BindingFlags]'Public,Static')
$transitionProperty=$type.GetField('TransitionProgressProperty',[Reflection.BindingFlags]'Public,Static')
if($null -eq $lightThemeProperty -or $null -eq $transitionProperty){throw 'Backdrop theme or transition dependency property missing'}
if($backdrop.TransitionProgress -ne 1){throw 'A new backdrop must show its final theme without an initial transition'}
$resolve=$type.GetMethod('ResolveScene',[Reflection.BindingFlags]'Public,Static')
if($null -eq $resolve){throw 'Backdrop scene resolution API missing'}
if($backdrop.IsHitTestVisible -or $backdrop.Focusable){throw 'Decorative backdrop intercepts input or keyboard focus'}
foreach($binding in @(@('LineBrush','ColorBrush2'),@('AccentBrush','IndustrialAccent'),@('BaseBrush','ColorBrush8'))){
    $property=$type.GetField(($binding[0]+'Property'),[Reflection.BindingFlags]'Public,Static')
    if($null -eq $property){throw ('Missing brush dependency property: '+$binding[0])}
    $backdrop.SetResourceReference($property.GetValue($null),$binding[1])
}
Write-Output 'PASS decorative background is noninteractive and binds live theme resources'

$pageNames=@($assembly.GetTypes() | Where-Object {$_.IsSubclassOf($pageType) -and -not $_.IsAbstract} | ForEach-Object Name | Sort-Object -Unique)
if($pageNames.Count -lt 8){throw 'Actual launcher page coverage unexpectedly incomplete'}
$coverage=@()
foreach($name in $pageNames){
    $family=[string]$resolve.Invoke($null,[object[]]@([string]$name))
    if([string]::IsNullOrWhiteSpace($family)){throw ('No background family for actual page '+$name)}
    $coverage+=[pscustomobject]@{Page=$name;Family=$family}
}
$primary=@('PageLaunchRight','PageNews','PageBRS','PageStock','PageMap','PageSetupUI','PageSelectRight','PageOtherAbout')
$primaryFamilies=@($primary | ForEach-Object {[string]$resolve.Invoke($null,[object[]]@([string]$_))})
if(@($primaryFamilies | Sort-Object -Unique).Count -ne $primary.Count){throw 'Required launcher areas share the same background family'}
Write-Output ('PASS {0} actual native page names resolve; eight primary areas use distinct background families' -f $pageNames.Count)

function Set-BackdropTheme([string]$theme){
    $palette=if($theme -eq 'dark'){@('#B3BAB9','#F2D649','#111416')}else{@('#555F60','#F2D649','#E7E8E3')}
    $keys=@('ColorBrush2','IndustrialAccent','ColorBrush8')
    for($i=0;$i -lt $keys.Count;$i++){
        # An unmounted FrameworkElement has no application-window resource notification path.
        # Own the isolated fixture's palette locally so replacing a resource invalidates its bindings.
        $backdrop.Resources[$keys[$i]]=[Windows.Media.BrushConverter]::new().ConvertFromString($palette[$i])
    }
    $backdrop.IsLightTheme=($theme -eq 'light')
    if($backdrop.LineBrush.Color -ne $backdrop.Resources['ColorBrush2'].Color){throw 'Backdrop did not follow a theme resource replacement'}
    if($backdrop.IsLightTheme -ne ($theme -eq 'light')){throw 'Backdrop did not follow its explicit theme mode'}
}
function Render-Backdrop([string]$scene,[int]$width,[int]$height){
    $backdrop.SceneKey=$scene
    $backdrop.InvalidateVisual()
    $backdrop.Measure([Windows.Size]::new($width,$height))
    if($backdrop.DesiredSize.Width -gt $width+0.1 -or $backdrop.DesiredSize.Height -gt $height+0.1){throw ('Background forces overflow for '+$scene)}
    $backdrop.Arrange([Windows.Rect]::new(0,0,$width,$height))
    $backdrop.UpdateLayout()
    $bitmap=[Windows.Media.Imaging.RenderTargetBitmap]::new($width,$height,96,96,[Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($backdrop)
    $pixels=New-Object byte[] ($width*$height*4)
    $bitmap.CopyPixels($pixels,$width*4,0)
    $sha=[Security.Cryptography.SHA256]::Create()
    try{$hash=[Convert]::ToBase64String($sha.ComputeHash($pixels))}finally{$sha.Dispose()}
    return [pscustomobject]@{Bitmap=$bitmap;Hash=$hash;Pixels=$pixels;Width=$width;Height=$height}
}
function Measure-YellowAccents($render){
    # Paper and gray geometry are nearly neutral. Warm yellow marks have a lower blue channel.
    $count=[BackdropPixelChecks]::CountYellow($render.Pixels)
    return [pscustomobject]@{Count=$count;Fraction=$count/([double]$render.Width*$render.Height)}
}
function Save-Backdrop($render,[string]$filename){
    $encoder=[Windows.Media.Imaging.PngBitmapEncoder]::new()
    $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($render.Bitmap))
    $stream=[IO.File]::Create((Join-Path $PreviewDirectory $filename))
    try{$encoder.Save($stream)}finally{$stream.Dispose()}
    if($LightBaselineDirectory -and $filename -like '*-light-*.png'){
        $baseline=Join-Path $LightBaselineDirectory $filename
        if(-not (Test-Path -LiteralPath $baseline -PathType Leaf)){throw ('Missing light baseline image: '+$baseline)}
        if((Get-FileHash -LiteralPath $baseline -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath (Join-Path $PreviewDirectory $filename) -Algorithm SHA256).Hash){throw ('Light backdrop changed from its approved baseline: '+$filename)}
        $script:lightBaselineChecks++
    }
}

New-Item -ItemType Directory -Path $PreviewDirectory -Force | Out-Null
$representatives=@('PageLaunchRight','PageNews','PageBRS','PageStock','PageMap','PageSetupSound','PageSetupSystem','PageSetupSponsors','PageOtherAbout','PageSetupChangelog')
$primaryHashes=@{dark=@{};light=@{}}
$accentMeasurements=@()
$lightBaselineChecks=0
$renderCount=0
foreach($theme in @('dark','light')){
    Set-BackdropTheme $theme
    foreach($width in @(360,960)){
        $height=if($width -lt 500){480}else{540}
        foreach($scene in $pageNames){
            $render=Render-Backdrop $scene $width $height
            $renderCount++
            if($width -eq 960 -and $primary -contains $scene){$primaryHashes[$theme][$scene]=$render.Hash}
            if($width -eq 960 -and $representatives -contains $scene){Save-Backdrop $render ($scene+'-'+$theme+'-'+$width+'.png')}
            if($scene -eq 'PageNews' -and $theme -eq 'light' -and $width -ne 960){Save-Backdrop $render ($scene+'-'+$theme+'-'+$width+'.png')}
            if($width -eq 960 -and $representatives -contains $scene){
                $yellow=Measure-YellowAccents $render
                if($yellow.Count -lt 16){throw ($theme+' backdrop lacks visible yellow accents: '+$scene)}
                if($yellow.Fraction -gt 0.12){throw ('Yellow accents cover too much of the '+$theme+' backdrop: '+$scene)}
                $accentMeasurements+=[pscustomobject]@{Theme=$theme;Page=$scene;YellowPixels=$yellow.Count;Fraction=$yellow.Fraction}
            }
        }
    }
}
foreach($theme in @('dark','light')){
    if($primaryHashes[$theme].Count -ne $primary.Count -or @($primaryHashes[$theme].Values | Sort-Object -Unique).Count -ne $primary.Count){throw ($theme+' primary background motifs render identically')}
}
Write-Output ('PASS {0} dark/light narrow/wide renders cover every actual native page; primary motifs differ visually' -f $renderCount)
Write-Output ('PASS {0} representative dark/light scenes contain restrained, visible yellow accents' -f $accentMeasurements.Count)
if($LightBaselineDirectory){Write-Output ('PASS {0} light preview PNGs match the approved baseline byte for byte' -f $lightBaselineChecks)}

# Hold all brush colors constant: a difference now proves a new composition, not merely recoloring.
Set-BackdropTheme light
$lightComposition=Render-Backdrop 'PageNews' 960 540
$backdrop.IsLightTheme=$false
$samePaletteDarkComposition=Render-Backdrop 'PageNews' 960 540
if($samePaletteDarkComposition.Hash -eq $lightComposition.Hash){throw 'Light and dark mode only recolor the same background composition'}
if($backdrop.TransitionProgress -ne 1 -or $backdrop.HasAnimatedProperties){throw 'An unmounted backdrop started a theme animation'}
Write-Output 'PASS light/dark compositions differ with an identical palette; initial/offscreen theme changes remain immediate'

Set-BackdropTheme dark
$first=Render-Backdrop 'PageNews' 960 540
$second=Render-Backdrop 'PageNews' 960 540
if($first.Hash -ne $second.Hash){throw 'Unchanged background has nondeterministic rendering'}
Set-BackdropTheme light
$light=Render-Backdrop 'PageNews' 960 540
if($light.Hash -eq $first.Hash){throw 'Theme replacement did not redraw the background'}
$narrow=Render-Backdrop 'PageNews' 360 480
$wideAgain=Render-Backdrop 'PageNews' 960 540
if($wideAgain.Hash -ne $light.Hash){throw 'Resizing retained stale drawing geometry'}
if($backdrop.IsHitTestVisible -or $backdrop.Focusable){throw 'Theme or resize changed the input behavior'}
Write-Output 'PASS unchanged renders remain stable; live palette changes and resizing redraw correctly'

$lineProperty=$type.GetField('LineBrushProperty').GetValue($null)
$mutable=$backdrop.LineBrush.CloneCurrentValue()
$backdrop.LineBrush=$mutable
$beforeMutation=Render-Backdrop 'PageNews' 960 540
$mutable.Color=[Windows.Media.Colors]::Crimson
$mutated=Render-Backdrop 'PageNews' 960 540
$backdrop.LineBrush=$mutable.CloneCurrentValue()
$replaced=Render-Backdrop 'PageNews' 960 540
if($mutated.Hash -eq $beforeMutation.Hash -or $mutated.Hash -ne $replaced.Hash){throw 'Mutating a live brush retained stale derived scene geometry'}
$backdrop.SetResourceReference($lineProperty,'ColorBrush2')
Write-Output 'PASS an in-place palette change matches a fresh scene drawn with the same brush'

foreach($browserPage in @(@('PCL.PageStock','ViewWeb'),@('PCL.PageMap','ViewMap'))){
    $page=[Activator]::CreateInstance($assembly.GetType($browserPage[0],$true))
    $view=$page.FindName($browserPage[1])
    if($null -eq $view -or $view.CoreWebView2 -ne $null -or $page.IsLoaded){throw ('Browser frame started network initialization while unmounted: '+$browserPage[0])}
    if($browserPage[0] -eq 'PCL.PageStock' -and $null -eq $page.FindName('PanCover')){throw 'Stock loading cover was removed'}
}
if($app.Windows.Count -ne 0 -or $backdrop.IsLoaded){throw 'Offscreen verification opened a visible application window'}
Write-Output 'PASS framed stock and map retain their browser controls without starting WebView2'

function Wait-RenderDispatcher([int]$milliseconds){
    $frame=[Windows.Threading.DispatcherFrame]::new()
    $timer=[Windows.Threading.DispatcherTimer]::new([Windows.Threading.DispatcherPriority]::Background)
    $timer.Interval=[TimeSpan]::FromMilliseconds($milliseconds)
    $callback=[EventHandler]({param($sender,$args) $sender.Stop();$frame.Continue=$false}.GetNewClosure())
    $timer.add_Tick($callback)
    try{$timer.Start();[Windows.Threading.Dispatcher]::PushFrame($frame)}finally{$timer.Stop();$timer.remove_Tick($callback)}
}
function Wait-BackdropSettled {
    $deadline=[Diagnostics.Stopwatch]::StartNew()
    do{
        Wait-RenderDispatcher 40
        # A screen-external HWND can be compositor-occluded. Explicit render requests keep
        # the fixture advancing just as a visible launcher's invalidated background would.
        $null=Render-Backdrop 'PageNews' 960 540
    }while(($backdrop.HasAnimatedProperties -or $backdrop.TransitionProgress -lt 0.999) -and $deadline.ElapsedMilliseconds -lt 1800)
}
# The PCL Startup handler and StartupUri were removed above. Only a pure background is mounted;
# no FormMain, browser page, update action or diagnostic-upload component is loaded by this fixture.
$hostWindow=$null
try{
    $hostWindow=[Windows.Window]::new()
    $hostWindow.Title='BRCL backdrop verification'
    $hostWindow.WindowStyle=[Windows.WindowStyle]::None
    $hostWindow.ResizeMode=[Windows.ResizeMode]::NoResize
    $hostWindow.ShowInTaskbar=$false
    $hostWindow.ShowActivated=$false
    $hostWindow.Left=-20000;$hostWindow.Top=-20000
    $hostWindow.Width=960;$hostWindow.Height=540
    # Stay outside every display and out of the taskbar, but retain a renderable visual tree.
    # WPF may skip OnRender entirely beneath a zero-opacity Window.
    $hostWindow.Opacity=1
    Set-BackdropTheme dark
    $baselineDark=Render-Backdrop 'PageNews' 960 540
    $hostWindow.Content=$backdrop
    $hostWindow.Show()
    Wait-RenderDispatcher 50
    if(-not $backdrop.IsLoaded){throw 'Isolated backdrop did not reach Loaded'}
    # An offscreen host can skip its normal compositor render. Capture after
    # Loaded/initial layout so the transition has the same preceding drawing as a visible window.
    $baselineDark=Render-Backdrop 'PageNews' 960 540
    $motionProperty=$assembly.GetType('PCL.ModAnimation',$true).GetProperty('UiMotionEnabled',[Reflection.BindingFlags]'Public,Static')
    $motionEnabled=[bool]$motionProperty.GetValue($null,$null)
    Set-BackdropTheme light
    if($motionEnabled){
        if(-not $backdrop.HasAnimatedProperties){throw 'Loaded theme switch did not begin a transition'}
        # BeginAnimation takes effect on the first WPF clock tick, not synchronously in GetValue.
        Wait-RenderDispatcher 90
        $progress=[double]$backdrop.TransitionProgress
        if($progress -le 0 -or $progress -ge 1){throw ('No intermediate theme-animation frame: '+$progress)}
        # Bitmap encoding can exceed the short animation duration on a busy machine. Confirm
        # the real clock above, then freeze this fixture's progress to inspect all three images.
        $backdrop.BeginAnimation($transitionProperty.GetValue($null),$null)
        $backdrop.TransitionProgress=0
        $start=Render-Backdrop 'PageNews' 960 540
        $backdrop.TransitionProgress=0.5
        $middle=Render-Backdrop 'PageNews' 960 540
        if($MeasureFrames){
            $method=$type.GetMethod('OnRender',[Reflection.BindingFlags]'Instance,NonPublic')
            $draw=[Delegate]::CreateDelegate([Action[Windows.Media.DrawingContext]],$backdrop,$method)
            $sink=[Windows.Media.DrawingGroup]::new()
            function Draw-SampleFrames([int]$count){
                for($index=0;$index -lt $count;$index++){
                    $context=$sink.Open()
                    try{$draw.Invoke($context)}finally{$context.Close()}
                }
            }
            Draw-SampleFrames 30
            $watch=[Diagnostics.Stopwatch]::StartNew()
            $allocated=[GC]::GetAllocatedBytesForCurrentThread()
            Draw-SampleFrames 300
            $bytes=[GC]::GetAllocatedBytesForCurrentThread()-$allocated
            $watch.Stop()
            Write-Output ('BENCH 300 backdrop drawing passes: {0:F2} ms, {1} UI-thread allocated bytes' -f $watch.Elapsed.TotalMilliseconds,$bytes)
        }
        $backdrop.TransitionProgress=1
        $final=Render-Backdrop 'PageNews' 960 540
        Save-Backdrop $start 'theme-transition-start.png'
        Save-Backdrop $middle 'theme-transition-middle.png'
        Save-Backdrop $final 'theme-transition-final.png'
        if($start.Hash -ne $baselineDark.Hash -or $middle.Hash -eq $final.Hash -or $middle.Hash -eq $start.Hash){throw 'Controlled transition progress did not preserve and blend its two endpoint drawings'}
        if($final.Hash -ne $light.Hash){throw 'Animated switch did not settle on the expected light rendering'}
        # Start a fresh real transition after the controlled image inspection and let it finish.
        Set-BackdropTheme dark
        Wait-RenderDispatcher 20
        Set-BackdropTheme light
        Wait-BackdropSettled
        if($backdrop.TransitionProgress -lt 0.999 -or $backdrop.HasAnimatedProperties){throw 'Theme animation did not release its completed clock'}
        $settled=Render-Backdrop 'PageNews' 960 540
        if($settled.Hash -ne $light.Hash){throw 'Naturally completed transition differs from the final light rendering'}
        Set-BackdropTheme dark
        Wait-RenderDispatcher 60
        Set-BackdropTheme light
        Wait-RenderDispatcher 60
        Set-BackdropTheme dark
        Wait-BackdropSettled
        $rapid=Render-Backdrop 'PageNews' 960 540
        if($backdrop.IsLightTheme -or $backdrop.TransitionProgress -lt 0.999 -or $backdrop.HasAnimatedProperties -or $rapid.Hash -ne $baselineDark.Hash){throw ('Rapid reversal failed: IsLight={0}; Progress={1}; Animated={2}; ExpectedImage={3}' -f $backdrop.IsLightTheme,$backdrop.TransitionProgress,$backdrop.HasAnimatedProperties,($rapid.Hash -eq $baselineDark.Hash))}
        Set-BackdropTheme light
        Wait-RenderDispatcher 60
        $null=Render-Backdrop 'PageNews' 360 480
        if($backdrop.TransitionProgress -lt 0.999 -or $backdrop.HasAnimatedProperties){throw 'Resizing did not finish and clear the theme transition'}
        $null=Render-Backdrop 'PageNews' 960 540
        Set-BackdropTheme dark
        $hostWindow.Content=$null
        Wait-RenderDispatcher 50
        if($backdrop.IsLoaded -or $backdrop.HasAnimatedProperties -or $backdrop.TransitionProgress -lt 0.999){throw 'Unloading left a running backdrop transition'}
        Write-Output ('PASS real loaded clock reaches intermediate progress ({0:F3}); controlled 0/0.5/1 frames blend; natural completion, reversal, resize and unload release animation state' -f $progress)
    }else{
        if($backdrop.TransitionProgress -ne 1 -or $backdrop.HasAnimatedProperties){throw 'Background animates while reduced motion or system animations are disabled'}
        Write-Output 'PASS reduced/system motion preference disables theme animation; timed-animation branch skipped without changing user preferences'
    }
    $setThemeBrush=$assembly.GetType('PCL.ModSecret',$true).GetMethod('SetThemeBrush',[Reflection.BindingFlags]'NonPublic,Static')
    if($null -eq $setThemeBrush){throw 'Global theme brush transition helper missing'}
    # Application resources freeze publishable Freezables as they are inserted.
    # A detached dictionary misses this production behavior and used to let the
    # publish-before-BeginAnimation bug pass despite live theme switches failing.
    $palette=$app.Resources
    $paletteKey='BrclThemeRegressionSurface'
    $darkColor=[Windows.Media.ColorConverter]::ConvertFromString('#111416')
    $lightColor=[Windows.Media.ColorConverter]::ConvertFromString('#E7E8E3')
    $palette[$paletteKey]=[Windows.Media.SolidColorBrush]::new($darkColor)
    if(-not $palette[$paletteKey].IsFrozen){throw 'Application palette fixture did not reproduce WPF resource freezing'}
    $paletteConsumer=[Windows.Controls.Border]::new()
    $paletteConsumer.SetResourceReference([Windows.Controls.Border]::BackgroundProperty,$paletteKey)
    $hostWindow.Content=$paletteConsumer
    Wait-RenderDispatcher 30
    if(-not $paletteConsumer.IsLoaded -or -not [object]::ReferenceEquals($paletteConsumer.Background,$palette[$paletteKey])){throw 'Loaded palette consumer did not resolve its application DynamicResource'}
    $setThemeBrush.Invoke($null,[object[]]@($palette,$paletteKey,$lightColor,$true)) | Out-Null
    $animatedBrush=$palette[$paletteKey]
    if($animatedBrush.IsFrozen -or -not $animatedBrush.HasAnimatedProperties){throw 'Application palette animation did not start on an unfrozen replacement'}
    # DispatcherTimer intervals are lower bounds; under concurrent test load a
    # single 110 ms wait can resume after the whole 320 ms transition. Observe
    # the first real intermediate frame instead of assuming one sampling time.
    $paletteWatch=[Diagnostics.Stopwatch]::StartNew()
    $sawIntermediate=$false
    do{
        Wait-RenderDispatcher 10
        $observedColor=$animatedBrush.Color
        if($observedColor -ne $darkColor -and $observedColor -ne $lightColor){$sawIntermediate=$true;break}
    }while($animatedBrush.HasAnimatedProperties -and $paletteWatch.ElapsedMilliseconds -lt 1500)
    if(-not $sawIntermediate){throw ('Global palette animation lacks an intermediate color; observed {0} after {1} ms' -f $observedColor,$paletteWatch.ElapsedMilliseconds)}
    if(-not [object]::ReferenceEquals($paletteConsumer.Background,$animatedBrush) -or $paletteConsumer.Background.Color -ne $observedColor){throw 'Loaded DynamicResource consumer did not display the animated application palette'}
    # Reverse from an in-flight color, rather than jumping back to the previous endpoint.
    $setThemeBrush.Invoke($null,[object[]]@($palette,$paletteKey,$darkColor,$true)) | Out-Null
    $reversedBrush=$palette[$paletteKey]
    if(-not $reversedBrush.HasAnimatedProperties){throw 'In-flight global theme reversal did not create its replacement transition'}
    $settleWatch=[Diagnostics.Stopwatch]::StartNew()
    $probe=[Windows.Media.Imaging.RenderTargetBitmap]::new(16,16,96,96,[Windows.Media.PixelFormats]::Pbgra32)
    do{
        Wait-RenderDispatcher 25
        $probe.Render($paletteConsumer)
    }while(($reversedBrush.HasAnimatedProperties -or $animatedBrush.HasAnimatedProperties) -and $settleWatch.ElapsedMilliseconds -lt 1800)
    if($reversedBrush.Color -ne $darkColor -or $reversedBrush.HasAnimatedProperties -or $animatedBrush.HasAnimatedProperties){throw 'Global palette animation left a stale color or completed clock'}
    if(-not [object]::ReferenceEquals($paletteConsumer.Background,$reversedBrush) -or $paletteConsumer.Background.Color -ne $darkColor){throw 'Loaded palette consumer retained the superseded transition'}
    $setThemeBrush.Invoke($null,[object[]]@($palette,$paletteKey,$lightColor,$false)) | Out-Null
    if($palette[$paletteKey].Color -ne $lightColor -or $palette[$paletteKey].HasAnimatedProperties){throw 'Disabled global palette animation did not apply immediately'}
    $setThemeBrush.Invoke($null,[object[]]@($palette,$paletteKey,$lightColor,$true)) | Out-Null
    if($palette[$paletteKey].Color -ne $lightColor -or $palette[$paletteKey].HasAnimatedProperties){throw 'Equal-color application palette replacement unexpectedly animated'}
    Wait-RenderDispatcher 20
    if(-not [object]::ReferenceEquals($paletteConsumer.Background,$palette[$paletteKey]) -or $paletteConsumer.Background.Color -ne $lightColor){throw 'Immediate palette replacement did not update its loaded consumer'}
    $palette.Remove($paletteKey)
    Write-Output 'PASS application palette freezing, loaded DynamicResource updates, intermediate colors, reversal and completed clock cleanup; disabled and equal-color replacements apply immediately'
}finally{
    if($hostWindow){$hostWindow.Content=$null;$hostWindow.Close()}
}
if($app.Windows.Count -ne 0){throw 'Verification left a window open'}
$coverage | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $PreviewDirectory 'page-coverage.json') -Encoding UTF8
$accentMeasurements | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $PreviewDirectory 'yellow-coverage.json') -Encoding UTF8
Write-Output ('Backdrop references saved: '+$PreviewDirectory)
$app.Shutdown()
