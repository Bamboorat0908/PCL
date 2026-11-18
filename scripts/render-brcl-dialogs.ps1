param([string]$OutputDirectory='bin\BrclDialogs', [string]$PreviewDirectory='C:\works\BRCL\design\dialogs')
$ErrorActionPreference='Stop'
Add-Type -AssemblyName PresentationFramework,PresentationCore,WindowsBase,System.Net.Http
$project=Join-Path $PSScriptRoot '..\Plain Craft Launcher 2'
$assembly=[Reflection.Assembly]::LoadFrom((Join-Path $project "$OutputDirectory\BRCL.exe"))
[void][Reflection.Assembly]::LoadFrom((Join-Path $project 'Resources\Newtonsoft.Json.dll'))
[Windows.Application]::ResourceAssembly=$assembly
$app=[Activator]::CreateInstance($assembly.GetType('PCL.Application',$true),$true)
$app.InitializeComponent()
$app.Resources['ColorBrushCardLine']=[Windows.Media.SolidColorBrush]::new([Windows.Media.Color]::FromRgb(55,64,65))
$assembly.GetType('PCL.ModAnimation',$true).GetProperty('AniControlEnabled').SetValue($null,1,$null)
$builder=$assembly.GetType('PCL.BrclErrorPresentation',$true).GetMethod('BuildBrclError')
$converterType=$assembly.GetType('PCL.ModMain+MyMsgBoxConverter',$true)
$controlType=$assembly.GetType('PCL.MyMsgText',$true)
New-Item -ItemType Directory -Path $PreviewDirectory -Force | Out-Null
function Render-Element($element,[double]$width,[string]$name) {
    $element.InvalidateMeasure()
    $element.InvalidateArrange()
    $element.Measure([Windows.Size]::new($width,[double]::PositiveInfinity))
    $height=[Math]::Ceiling($element.DesiredSize.Height)
    $element.Arrange([Windows.Rect]::new(0,0,$width,$height))
    $element.UpdateLayout()
    $image=[Windows.Media.Imaging.RenderTargetBitmap]::new([int]$width,[int]$height,96,96,[Windows.Media.PixelFormats]::Pbgra32)
    $image.Render($element)
    $encoder=[Windows.Media.Imaging.PngBitmapEncoder]::new()
    $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($image))
    $file=[IO.File]::Create((Join-Path $PreviewDirectory ($name+'.png')))
    try {$encoder.Save($file)} finally {$file.Dispose()}
    Write-Output "RENDER $name : $width x $height"
}
$content=$builder.Invoke($null,[object[]]@('server',[Net.WebException]::new('Example network diagnostic'),$null,$false))
$converter=[Activator]::CreateInstance($converterType)
$converter.Title=$content.Title
$converter.Text=$content.Summary
$converter.ErrorDetails=$content.Details
$converter.ErrorCode=$content.Code
$converter.RecoveryAdvice=$content.Advice
$converter.Button1='知道了'
function New-PreviewControl {
 $instance=[Activator]::CreateInstance($controlType,[object[]]@($converter))
 $instance.FindName('PanCaption').MaxHeight=270
 return $instance
}
$control=New-PreviewControl
Render-Element $control 640 'network-dark'
$control=New-PreviewControl
$control.FindName('PanDetails').IsExpanded=$true
Render-Element $control 640 'network-details-dark'
$control=New-PreviewControl
Render-Element $control 390 'network-narrow'
$oldDetails=$converter.ErrorDetails
$converter.ErrorDetails=([string]::Join([Environment]::NewLine,(1..100 | ForEach-Object {"Diagnostic line $_ : sample long diagnostic content"})))
$control=New-PreviewControl
$control.FindName('PanDetails').IsExpanded=$true
Render-Element $control 390 'long-details-narrow'
$converter.ErrorDetails=$oldDetails
foreach ($pair in @(@('ColorBrush1','#191D1F'),@('ColorBrush2','#555F60'),@('ColorBrush6','#E8EAE1'),@('ColorBrush7','#F5F5EF'),@('ColorBrush8','#E7E8E3'),@('ColorBrushCardLine','#C6CBC7'))) {
 $app.Resources[$pair[0]]=[Windows.Media.BrushConverter]::new().ConvertFromString($pair[1])
}
$control=New-PreviewControl
Render-Element $control 640 'network-light'
$script:callbackCount=0
$converter.Button1Action=[Action]{$script:callbackCount++}
$converter.Button2Action=[Action]{$script:callbackCount++}
$converter.Button3Action=[Action]{$script:callbackCount++}
$converter.Button2='取消'
$converter.Button3='保留'
$callbacks=New-PreviewControl
$callbacks.Btn1_Click()
$callbacks.Btn2_Click()
$callbacks.Btn3_Click()
if ($script:callbackCount -ne 3 -or $converter.IsExited) {throw 'Dialog action callback behavior changed'}
Write-Output 'PASS three existing callbacks retain their non-closing behavior'
$startup=$assembly.GetType('PCL.BrclErrorWindow',$true)
$fatal=$builder.Invoke($null,[object[]]@('startup',[InvalidOperationException]::new('Example fatal diagnostic'),$null,$true))
$constructor=$startup.GetConstructors([Reflection.BindingFlags]'NonPublic, Instance')[0]
$window=$constructor.Invoke([object[]]@($fatal,$true))
$frame=$window.Content
$frame.Width=600
Render-Element $frame 600 'fatal-fallback'
$window.Close()
$app.Shutdown()
