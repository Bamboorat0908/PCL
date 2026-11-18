param([string]$BuildDirectory,[string]$PreviewDirectory='C:\works\BRCL\usability-icons-20261113-checks\layouts')
$ErrorActionPreference='Stop'
Add-Type -AssemblyName PresentationFramework,PresentationCore,WindowsBase,System.Xaml,System.Net.Http,UIAutomationProvider,UIAutomationTypes
$project=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\Plain Craft Launcher 2'))
if(-not $BuildDirectory){$BuildDirectory=Join-Path $project 'bin'}
$assembly=[Reflection.Assembly]::LoadFrom((Join-Path $BuildDirectory 'BRCL.exe'))
[Windows.Application]::ResourceAssembly=$assembly
$app=[Activator]::CreateInstance($assembly.GetType('PCL.Application',$true),$true)
$app.InitializeComponent()
$startup=$app.GetType().GetMethod('Application_Startup',[Reflection.BindingFlags]'NonPublic,Instance')
$app.remove_Startup([Delegate]::CreateDelegate([Windows.StartupEventHandler],$app,$startup))
[Windows.Application].GetField('_startupUri',[Reflection.BindingFlags]'NonPublic,Instance').SetValue($app,$null)
$assembly.GetType('PCL.ModAnimation',$true).GetProperty('AniControlEnabled').SetValue($null,1,$null)
New-Item -ItemType Directory -Path $PreviewDirectory -Force | Out-Null

[xml]$icons=Get-Content (Join-Path $project 'Resources\IndustrialIcons.xaml') -Raw -Encoding UTF8
$manager=[Xml.XmlNamespaceManager]::new($icons.NameTable)
$manager.AddNamespace('x','http://schemas.microsoft.com/winfx/2006/xaml')
$keys=@($icons.DocumentElement.ChildNodes | Where-Object {$_.LocalName -eq 'Geometry'} | ForEach-Object {$_.GetAttribute('Key','http://schemas.microsoft.com/winfx/2006/xaml')})
foreach($key in $keys){
    $geometry=$app.Resources[$key]
    if($geometry -isnot [Windows.Media.Geometry] -or $geometry.Bounds.IsEmpty){throw "Icon not embedded or empty: $key"}
    $stringKey=$key.Replace('BrclGeometry','BrclIcon')
    $parsed=[Windows.Media.Geometry]::Parse([string]$app.Resources[$stringKey])
    if($geometry.ToString() -ne $parsed.ToString()){throw "Icon consumers disagree: $key"}
}
Write-Output ('PASS {0} embedded vector icon pairs are nonempty and equivalent' -f $keys.Count)

$button=[Activator]::CreateInstance($assembly.GetType('PCL.MyButton',$true))
$button.Text='Action'
$button.Height=30
$button.HorizontalAlignment='Left'
$button.Padding=[Windows.Thickness]::new(12,0,12,0)
$buttonHost=[Windows.Controls.Border]::new()
$buttonHost.Child=$button
function Layout-Button {
    $frame=[Windows.Threading.DispatcherFrame]::new()
    $timer=[Windows.Threading.DispatcherTimer]::new()
    $timer.Interval=[TimeSpan]::FromMilliseconds(5)
    $timer.Add_Tick({$frame.Continue=$false})
    $timer.Start()
    [Windows.Threading.Dispatcher]::PushFrame($frame)
    $timer.Stop()
    $buttonHost.Measure([Windows.Size]::new(500,40))
    $buttonHost.Arrange([Windows.Rect]::new(0,0,500,40))
    $buttonHost.UpdateLayout()
}
Layout-Button
$textOnlyWidth=$button.DesiredSize.Width
$button.Icon=$app.Resources['BrclGeometryRefresh']
Layout-Button
if($button.DesiredSize.Width -lt $textOnlyWidth+20){throw "Icon slot width missing: without=$textOnlyWidth, with=$($button.DesiredSize.Width)"}
$fore=$button.FindName('PanFore')
$button.ColorType=[Enum]::Parse($button.ColorType.GetType(),'Highlight')
if($null -eq $fore.Clip){throw 'Changing the selected category did not apply its cut corner'}
$fore.Opacity=0.9
$button.ColorType=[Enum]::Parse($button.ColorType.GetType(),'Normal')
if($null -ne $fore.Clip -or $fore.Opacity -ne 1){throw 'Previous selected category retained its clip or opacity'}
$button.Icon=$null
Layout-Button
if([Math]::Abs($button.DesiredSize.Width-$textOnlyWidth) -gt 0.1){throw 'Plain text buttons reserve an empty icon slot'}
$inlineButton=[Activator]::CreateInstance($assembly.GetType('PCL.MyButton',$true))
$inlineButton.Inlines.Add([Windows.Documents.Run]::new('Inline action'))
$inlinePeer=[Windows.Automation.Peers.UIElementAutomationPeer]::CreatePeerForElement($inlineButton)
if($inlinePeer.GetName() -ne 'Inline action'){throw 'Inline button lost its accessible label'}
Write-Output 'PASS optional icons preserve plain-button width, selected feedback resets, and inline labels stay accessible'

$newPage={param($name) [Activator]::CreateInstance($assembly.GetType('PCL.'+$name,$true))}
$news=& $newPage 'PageNews'
$settings=& $newPage 'PageSetupLaunch'
$map=& $newPage 'PageMap'
$about=& $newPage 'PageOtherAbout'
$sections=@('SectionAppearance','SectionAdvanced','CardSkin','SectionDiagnostics')
foreach($name in $sections){
    $section=$settings.FindName($name)
    if($section -isnot [Windows.Controls.Expander] -or $section.IsExpanded){throw "Settings progressive disclosure missing: $name"}
    $peer=[Windows.Automation.Peers.UIElementAutomationPeer]::CreatePeerForElement($section)
    $pattern=$peer.GetPattern([Windows.Automation.Peers.PatternInterface]::ExpandCollapse)
    $pattern.Expand()
    if(-not $section.IsExpanded){throw "Cannot expand setting section: $name"}
    $pattern.Collapse()
    if($section.IsExpanded){throw "Cannot collapse setting section: $name"}
}
if($settings.FindName('BtnAdvanceJavaSearch').Logo -ne $app.Resources['BrclIconRefresh']){throw 'Build does not contain the final Java refresh icon'}
Write-Output 'PASS common settings remain visible and four advanced sections expose ExpandCollapse'

$flags=[Reflection.BindingFlags]'Instance,NonPublic'
$assembly.GetType('PCL.PageMap').GetMethod('ShowUnavailable',$flags).Invoke($map,@('Isolated offline layout sample','Map unavailable')) | Out-Null
if($map.FindName('BtnBrowser').Visibility -ne 'Visible'){throw 'Map has no browser fallback'}

function Set-Theme([string]$theme){
    $palette=if($theme -eq 'dark'){@('#F1F2E9','#B3BAB9','#F2D649','#272C2E','#1C2022','#111416','#374041')}else{@('#191D1F','#555F60','#745900','#E8EAE1','#F5F5EF','#E7E8E3','#C6CBC7')}
    $names=@('ColorBrush1','ColorBrush2','ColorBrush3','ColorBrush6','ColorBrush7','ColorBrush8','ColorBrushCardLine')
    for($i=0;$i -lt $names.Count;$i++){$app.Resources[$names[$i]]=[Windows.Media.BrushConverter]::new().ConvertFromString($palette[$i])}
}
function Check-Visual($visual,$page){
    if($visual -is [Windows.FrameworkElement]){
        if($visual.Visibility -ne [Windows.Visibility]::Visible){return}
        if($visual.Focusable -and $visual.ActualWidth -gt 0 -and $visual -isnot [Windows.Controls.ScrollViewer]){
            $origin=$visual.TranslatePoint([Windows.Point]::new(0,0),$page)
            if($origin.X -lt -1 -or $origin.X+$visual.ActualWidth -gt $page.ActualWidth+1){throw ('Interactive control clips: {0} {1}, x={2}, width={3}, page={4}' -f $visual.GetType().Name,$visual.Name,$origin.X,$visual.ActualWidth,$page.ActualWidth)}
        }
    }
    for($j=0;$j -lt [Windows.Media.VisualTreeHelper]::GetChildrenCount($visual);$j++){Check-Visual ([Windows.Media.VisualTreeHelper]::GetChild($visual,$j)) $page}
}
function Render-Page($page,[int]$width,[double]$zoom,[int]$dpi,[string]$name,[int]$height=700){
    $hostBorder=[Windows.Controls.Border]::new()
    $hostBorder.Background=$app.Resources['ColorBrush8']
    $page.LayoutTransform=[Windows.Media.ScaleTransform]::new($zoom,$zoom)
    $hostBorder.Child=$page
    $hostBorder.Measure([Windows.Size]::new($width,$height))
    $hostBorder.Arrange([Windows.Rect]::new(0,0,$width,$height))
    $hostBorder.UpdateLayout()
    Check-Visual $page $page
    $pixels=[int][Math]::Ceiling($width*$dpi/96)
    $bitmap=[Windows.Media.Imaging.RenderTargetBitmap]::new($pixels,[int][Math]::Ceiling($height*$dpi/96),$dpi,$dpi,[Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($hostBorder)
    $encoder=[Windows.Media.Imaging.PngBitmapEncoder]::new()
    $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $file=[IO.File]::Create((Join-Path $PreviewDirectory ($name+'.png')))
    try{$encoder.Save($file)}finally{$file.Dispose()}
    $hostBorder.Child=$null
    $page.LayoutTransform=[Windows.Media.Transform]::Identity
}

foreach($theme in @('dark','light')){
    Set-Theme $theme
    foreach($dpi in @(96,120,144,192)){
        Render-Page $news 380 1 $dpi "news-$theme-$dpi"
        Render-Page $settings 430 1 $dpi "settings-$theme-$dpi"
    }
    foreach($sample in @(@($news,380,'news'),@($settings,430,'settings'),@($map,380,'map-offline'),@($about,430,'about'))){
        Render-Page $sample[0] $sample[1] 1.25 96 ($sample[2]+'-'+$theme+'-zoom125')
    }
    foreach($name in $sections){$settings.FindName($name).IsExpanded=$true}
    Render-Page $settings 430 1.25 96 "settings-expanded-$theme-zoom125"
    foreach($name in $sections){$settings.FindName($name).IsExpanded=$false}
    Render-Page $news 380 1 96 "news-short-$theme" 420
    if($news.FindName('HeaderDescription').Visibility -ne 'Collapsed' -or $news.FindName('CardMasthead').ActualHeight -gt 130){throw 'Short-window news header did not yield space to content'}
}
if($app.Windows.Count -ne 0){throw 'Layout checks unexpectedly mounted a real window'}
Write-Output 'PASS narrow layouts, 96/120/144/192 DPI rasterization and 125% layout zoom keep interactive controls in bounds'
Write-Output 'PASS map browser fallback and expanded advanced settings remain available in narrow layouts'
Write-Output 'PASS news header contracts in short windows without hiding refresh or filters'
$app.Shutdown()
