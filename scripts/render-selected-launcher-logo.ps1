# Scheme A selected by the user. Preserve the artwork; use native geometry for alpha.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore,PresentationFramework,WindowsBase
$images = Join-Path $PSScriptRoot '..\Plain Craft Launcher 2\Images'
$source = [Windows.Media.Imaging.BitmapImage]::new()
$source.BeginInit()
$source.CacheOption = [Windows.Media.Imaging.BitmapCacheOption]::OnLoad
$source.UriSource = [uri](Join-Path $images 'Brand\LauncherSelected-A.png')
$source.EndInit()
$source.Freeze()
$brush = [Windows.Media.ImageBrush]::new($source)
$brush.Stretch = [Windows.Media.Stretch]::Fill
$drawing = [Windows.Media.DrawingVisual]::new()
$context = $drawing.RenderOpen()
$context.DrawRoundedRectangle($brush,$null,[Windows.Rect]::new(0,0,1024,1024),180,180)
$context.Close()
$bitmap = [Windows.Media.Imaging.RenderTargetBitmap]::new(1024,1024,96,96,[Windows.Media.PixelFormats]::Pbgra32)
$bitmap.Render($drawing)
$encoder = [Windows.Media.Imaging.PngBitmapEncoder]::new()
$encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
$output = [IO.File]::Create((Join-Path $images 'Brand\LauncherSelected.png'))
try { $encoder.Save($output) } finally { $output.Dispose() }
Write-Output 'Rendered selected Scheme A with clean geometric rounded corners.'
