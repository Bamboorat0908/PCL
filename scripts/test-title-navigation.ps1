param(
    [string]$OutputDirectory = 'bin\HeaderFix',
    [string]$PreviewDirectory = 'C:\works\BRCL\design\title-navigation'
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationFramework,PresentationCore,WindowsBase,System.Net.Http
$project = Join-Path $PSScriptRoot '..\Plain Craft Launcher 2'
$assembly = [Reflection.Assembly]::LoadFrom((Join-Path $project "$OutputDirectory\BRCL.exe"))
[void][Reflection.Assembly]::LoadFrom((Join-Path $project 'Resources\Newtonsoft.Json.dll'))
[Windows.Application]::ResourceAssembly = $assembly
$app = [Activator]::CreateInstance($assembly.GetType('BRCL.Application', $true), $true)
$app.InitializeComponent()
$assembly.GetType('BRCL.ModAnimation', $true).GetProperty('AniControlEnabled').SetValue($null, 1, $null)
[xml]$source = Get-Content -LiteralPath (Join-Path $project 'FormMain.xaml') -Raw -Encoding UTF8
$namespaces = [Xml.XmlNamespaceManager]::new($source.NameTable)
$namespaces.AddNamespace('w', 'http://schemas.microsoft.com/winfx/2006/xaml/presentation')
$namespaces.AddNamespace('x', 'http://schemas.microsoft.com/winfx/2006/xaml')
$primaryMarkup = $source.SelectSingleNode('//w:TextBlock[@x:Name="LabTitlePage"]', $namespaces).OuterXml
$innerMarkup = $source.SelectSingleNode('//w:Grid[@x:Name="PanTitleInner"]', $namespaces).OuterXml.Replace('clr-namespace:BRCL"', 'clr-namespace:BRCL;assembly=BRCL"')
$markup = '<Grid xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" xmlns:local="clr-namespace:BRCL;assembly=BRCL" x:Name="PanTitle" Width="540" Height="48" Background="{DynamicResource ColorBrush8}">' + $primaryMarkup + $innerMarkup + '</Grid>'
New-Item -ItemType Directory -Path $PreviewDirectory -Force | Out-Null
$title = -join [char[]]@(0x7248, 0x672C, 0x9009, 0x62E9)

function Layout-Header($header) {
    $header.Measure([Windows.Size]::new(540, 48))
    $header.Arrange([Windows.Rect]::new(0, 0, 540, 48))
    $header.UpdateLayout()
}
function Save-Header($header, [string]$name) {
    $bitmap = [Windows.Media.Imaging.RenderTargetBitmap]::new(540, 48, 96, 96, [Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($header)
    $encoder = [Windows.Media.Imaging.PngBitmapEncoder]::new()
    $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $file = [IO.File]::Create((Join-Path $PreviewDirectory "$name.png"))
    try { $encoder.Save($file) } finally { $file.Dispose() }
}
foreach ($theme in @('dark', 'light')) {
    $foreground = if ($theme -eq 'dark') { '#EFF1EA' } else { '#191D1F' }
    $background = if ($theme -eq 'dark') { '#191F20' } else { '#E7E8E3' }
    $app.Resources['ColorBrush1'] = [Windows.Media.BrushConverter]::new().ConvertFromString($foreground)
    $app.Resources['ColorBrush8'] = [Windows.Media.BrushConverter]::new().ConvertFromString($background)
    $header = [Windows.Markup.XamlReader]::Parse($markup)
    $primary = $header.FindName('LabTitlePage')
    $inner = $header.FindName('PanTitleInner')
    $innerTitle = $header.FindName('LabTitleInner')
    $back = $header.FindName('BtnTitleInner')
    $back.Theme = [Enum]::Parse($back.Theme.GetType(), $(if ($theme -eq 'light') { 'Black' } else { 'White' }))
    $primary.Text = $title
    $innerTitle.Text = $title
    Layout-Header $header
    if ($primary.Visibility -ne 'Visible' -or $inner.Visibility -ne 'Collapsed') { throw "$theme main title is not exclusive" }
    Save-Header $header "$theme-main"
    # Reproduce the existing navigation animation, including its transparent endpoints.
    $inner.Visibility = 'Visible'
    $inner.Margin = [Windows.Thickness]::new(0)
    foreach ($opacity in @(0.0, 0.5, 1.0, 0.5, 0.0)) {
        $inner.Opacity = $opacity
        Layout-Header $header
        if ($primary.Visibility -ne 'Collapsed') { throw "$theme duplicate title at subpage opacity $opacity" }
        if ($innerTitle.Foreground.Color -ne $app.Resources['ColorBrush1'].Color) { throw "$theme subpage text uses the wrong foreground" }
        if ($opacity -eq 1.0) { Save-Header $header "$theme-subpage" }
    }
    $inner.Visibility = 'Collapsed'
    Layout-Header $header
    if ($primary.Visibility -ne 'Visible') { throw "$theme main title was not restored after returning" }
    Write-Output "PASS $theme : main / enter / subpage / exit / return; one title; readable text"
}
$app.Shutdown()
