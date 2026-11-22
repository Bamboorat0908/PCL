param([string]$PreviewDirectory = 'C:\works\BRCL\release-1.0.2.7-checks\sponsor-dialog')
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationFramework,PresentationCore,WindowsBase,System.Net.Http
$project = Join-Path $PSScriptRoot '..\Plain Craft Launcher 2'
$assembly = [Reflection.Assembly]::LoadFrom((Join-Path $project 'bin\BRCL.exe'))
[Windows.Application]::ResourceAssembly = $assembly
$app = [Activator]::CreateInstance($assembly.GetType('BRCL.Application', $true), $true)
$app.InitializeComponent()
$startup = $app.GetType().GetMethod('Application_Startup', [Reflection.BindingFlags]'NonPublic,Instance')
$app.remove_Startup([Delegate]::CreateDelegate([Windows.StartupEventHandler], $app, $startup))
[Windows.Application].GetField('_startupUri', [Reflection.BindingFlags]'NonPublic,Instance').SetValue($app, $null)
$assembly.GetType('BRCL.ModAnimation').GetProperty('AniControlEnabled').SetValue($null, 1, $null)
$module = $assembly.GetType('BRCL.ModLaunch', $true)
$compiled = @()
foreach ($type in @($module) + @($module.GetNestedTypes([Reflection.BindingFlags]'Public,NonPublic'))) {
    foreach ($method in $type.GetMethods([Reflection.BindingFlags]'Public,NonPublic,Static,Instance,DeclaredOnly')) {
        $body = $method.GetMethodBody()
        if ($null -eq $body) { continue }
        $bytes = $body.GetILAsByteArray()
        $strings = @()
        for ($i = 0; $i -lt $bytes.Length - 4; $i++) {
            if ($bytes[$i] -eq 0x72 -and $bytes[$i + 4] -eq 0x70) {
                try { $strings += $method.Module.ResolveString([BitConverter]::ToInt32($bytes, $i + 1)) } catch { }
            }
        }
        if (@($strings | Where-Object { $_.Contains('感谢你使用我们的启动器！') }).Count) { $compiled = $strings }
    }
}
$paragraph = $compiled | Where-Object { $_.Contains('感谢你使用我们的启动器！') } | Select-Object -First 1
if (-not $paragraph -or -not $paragraph.Contains('每一份赞助都将切实用于服务器的运营和维护！')) { throw 'New sponsorship copy missing from compiled launcher' }
if ('https://afdian.com/a/brszz' -cnotin $compiled -or '支持 BRCL！' -cnotin $compiled) { throw 'Support action is not wired to the BRS Afdian URL in the compiled callback' }
Write-Output 'PASS compiled sponsorship copy and BRS Afdian action target'
$paragraph = $paragraph.Substring($paragraph.IndexOf('感谢你使用我们的启动器！'))
New-Item -ItemType Directory -Path $PreviewDirectory -Force | Out-Null
$converterType = $assembly.GetType('BRCL.ModMain+MyMsgBoxConverter', $true)
foreach ($width in @(640, 390)) {
    $converter = [Activator]::CreateInstance($converterType)
    $converter.Title = '10 次启动！'
    $converter.Text = "BRCL 已经为你启动了 10 次游戏啦！`r`n" + $paragraph
    $converter.Button1 = '支持 BRCL！'
    $converter.Button2 = '但是我拒绝'
    $control = [Activator]::CreateInstance($assembly.GetType('BRCL.MyMsgText', $true), [object[]]@($converter))
    $control.FindName('PanCaption').MaxHeight = 600
    $control.Measure([Windows.Size]::new($width, [double]::PositiveInfinity))
    $height = [int][Math]::Ceiling($control.DesiredSize.Height)
    $control.Arrange([Windows.Rect]::new(0, 0, $width, $height))
    $control.UpdateLayout()
    if ($control.FindName('LabCaption').ActualHeight -lt 70 -or $control.FindName('Btn1').Text -ne '支持 BRCL！') { throw 'Sponsor dialog lost its complete copy or support button' }
    $bitmap = [Windows.Media.Imaging.RenderTargetBitmap]::new($width, $height, 96, 96, [Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($control)
    $encoder = [Windows.Media.Imaging.PngBitmapEncoder]::new()
    $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $file = [IO.File]::Create((Join-Path $PreviewDirectory "sponsor-$width.png"))
    try { $encoder.Save($file) } finally { $file.Dispose() }
    Write-Output "PASS sponsorship dialog layout $width"
}
$app.Shutdown()
