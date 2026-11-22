param([string]$OutputDirectory=(Join-Path $env:TEMP 'BRCL-Pack-Layout'))
$ErrorActionPreference='Stop'
Add-Type -AssemblyName PresentationFramework,PresentationCore,WindowsBase,System.Net.Http
New-Item -ItemType Directory -Path $OutputDirectory -Force|Out-Null
$assembly=[Reflection.Assembly]::LoadFrom((Join-Path $PSScriptRoot '..\Plain Craft Launcher 2\bin\BRCL.exe'))
[Windows.Application]::ResourceAssembly=$assembly
$app=[Activator]::CreateInstance($assembly.GetType('BRCL.Application',$true),$true);$app.InitializeComponent()
$assembly.GetType('BRCL.ModAnimation').GetProperty('AniControlEnabled').SetValue($null,1,$null)
$page=[Activator]::CreateInstance($assembly.GetType('BRCL.PageDownloadLeft',$true))
if($page.FindName('ItemBrsPack').Title -ne 'TRM 1.11 整合包'){throw 'Missing preset entry'}
Write-Output 'PASS download page has the TRM 1.11 preset entry'
foreach($theme in @('dark','light')) {
 foreach($width in @(660,390)) {
  $window=[Activator]::CreateInstance($assembly.GetType('BRCL.BrsPackPresetWindow',$true),[object[]]@('C:\Games\Minecraft'))
  $window.Width=$width;$window.Height=if($width -lt 520){710}else{520}
  $root=$window.Content
  if($theme -eq 'light'){
   $colors=@{PackSurface='#F4F5EF';PackCard='#FFFFFF';PackInk='#202A2C';PackMuted='#637071';PackLine='#D3DAD5';PackSelected='#F8F5DF'}
   foreach($key in $colors.Keys){$root.Resources[$key]=[Windows.Media.BrushConverter]::new().ConvertFromString($colors[$key])}
  }
  $window.Measure([Windows.Size]::new($width,$window.Height));$window.Arrange([Windows.Rect]::new(0,0,$width,$window.Height));$window.UpdateLayout()
  $root.Measure([Windows.Size]::new($width,$window.Height));$root.Arrange([Windows.Rect]::new(0,0,$width,$window.Height));$root.UpdateLayout()
  $high=$root.FindName('HighChoice');$low=$root.FindName('LowChoice')
  if($window.LowPreset -or -not $high.IsChecked){throw 'Wrong initial preset'}
  $low.IsChecked=$true
  if(-not $window.LowPreset -or $high.IsChecked){throw 'Low preset selection failed'}
  if($root.FindName('SelectionLabel').Text -ne '已选择 · 低预设'){throw 'Selection summary stale'}
  $high.IsChecked=$true
  foreach($name in @('HighChoice','LowChoice','DownloadButton','CancelButton')){
   $control=$root.FindName($name);$point=$control.TranslatePoint([Windows.Point]::new(0,0),$root)
   if($control.ActualWidth -le 0 -or $point.X -lt 0 -or $point.X+$control.ActualWidth -gt $width+1){throw "Clipped control $name at $width"}
  }
  $bitmap=[Windows.Media.Imaging.RenderTargetBitmap]::new($width,[int]$window.Height,96,96,[Windows.Media.PixelFormats]::Pbgra32);$bitmap.Render($root)
  $encoder=[Windows.Media.Imaging.PngBitmapEncoder]::new();$encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
  $stream=[IO.File]::Create((Join-Path $OutputDirectory "preset-$theme-$width.png"));try{$encoder.Save($stream)}finally{$stream.Dispose()}
  $window.Close()
  Write-Output "PASS $theme/$width layout, exclusive selection, summary and visible actions"
 }
}
$app.Shutdown()
