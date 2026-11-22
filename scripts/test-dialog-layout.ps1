param([string]$PreviewDirectory)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName PresentationFramework,PresentationCore,WindowsBase,System.Net.Http
$project=Join-Path $PSScriptRoot '..\Plain Craft Launcher 2'
$assembly=[Reflection.Assembly]::LoadFrom((Join-Path $project 'bin\BRCL.exe'))
[Windows.Application]::ResourceAssembly=$assembly
$app=[Activator]::CreateInstance($assembly.GetType('BRCL.Application',$true),$true)
$app.InitializeComponent()
$assembly.GetType('BRCL.ModAnimation').GetProperty('AniControlEnabled').SetValue($null,1,$null)
$converterType=$assembly.GetType('BRCL.ModMain+MyMsgBoxConverter',$true)
foreach ($theme in @('dark','light')) {
    $colors=if ($theme -eq 'dark') {@('#E6E8E3','#B8C0BD','#252B2D','#191F21','#424B49')} else {@('#191D1F','#555F60','#E8EAE1','#F5F5EF','#C6CBC7')}
    $keys=@('ColorBrush1','ColorBrush2','ColorBrush6','ColorBrush7','ColorBrushCardLine')
    for ($i=0;$i -lt $keys.Count;$i++) {$app.Resources[$keys[$i]]=[Windows.Media.BrushConverter]::new().ConvertFromString($colors[$i])}
    foreach ($kind in @('MyMsgInput','MyMsgSelect')) {
        foreach ($size in @(@(390,430),@(680,430))) {
            $converter=[Activator]::CreateInstance($converterType)
            $converter.Title='设置客户端名称'
            $converter.Text=('请输入新的客户端名称，用于在版本列表中识别。' * 12)
            $converter.Button1='保存更改'
            $converter.Button2='取消'
            if ($kind -eq 'MyMsgInput') {
                $converter.Content='TRM 1.10 高预设'
            } else {
                $choices=@()
                foreach ($i in 1..12) {
                    $choice=[Activator]::CreateInstance($assembly.GetType('BRCL.MyRadioBox',$true))
                    $choice.Text="客户端选项 $i"
                    $choices+=,$choice
                }
                $converter.Content=$choices
            }
            $control=[Activator]::CreateInstance($assembly.GetType("BRCL.$kind",$true),[object[]]@($converter))
            if ($control.FindName('Btn1').Text -ne $converter.Button1 -or $control.FindName('Btn2').Text -ne $converter.Button2) {throw "$kind initialization did not finish"}
            $width=$size[0]; $height=$size[1]
            $control.Width=$width-40
            $control.MaxHeight=$height-40
            $scroll=$control.FindName($(if ($kind -eq 'MyMsgInput') {'PanText'} else {'PanCaption'}))
            $scroll.MaxHeight=[Math]::Max(50,$control.MaxHeight-210)
            $control.Measure([Windows.Size]::new($width,$height))
            $control.Arrange([Windows.Rect]::new(0,0,$width,$height))
            $control.UpdateLayout()
            foreach ($name in @('Btn1','Btn2')) {
                $button=$control.FindName($name)
                $origin=$button.TranslatePoint([Windows.Point]::new(0,0),$control)
                if ($origin.X -lt 0 -or ($origin.X+$button.ActualWidth) -gt ($width+1) -or $origin.Y -lt 0 -or ($origin.Y+$button.ActualHeight) -gt ($height+1)) {throw "$kind button clipped at $width x $height"}
            }
            if ([Windows.Input.KeyboardNavigation]::GetTabNavigation($control) -ne [Windows.Input.KeyboardNavigationMode]::Cycle) {throw 'Tab focus can escape modal dialog'}
            Write-Output "PASS $kind $theme ${width}x${height}: long content scrolls, buttons fit, modal tab navigation cycles"
            if ($PreviewDirectory) {
                New-Item -ItemType Directory -Path $PreviewDirectory -Force | Out-Null
                $bitmap=[Windows.Media.Imaging.RenderTargetBitmap]::new($width,$height,96,96,[Windows.Media.PixelFormats]::Pbgra32)
                $bitmap.Render($control)
                $encoder=[Windows.Media.Imaging.PngBitmapEncoder]::new()
                $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
                $stream=[IO.File]::Create((Join-Path $PreviewDirectory "$kind-$theme-$width.png"))
                try {$encoder.Save($stream)} finally {$stream.Dispose()}
            }
        }
    }
}
$app.Shutdown()
