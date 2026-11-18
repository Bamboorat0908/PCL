param([string]$OutputFile = (Join-Path $PSScriptRoot '..\Plain Craft Launcher 2\Resources\IndustrialIcons.xaml'))
$ErrorActionPreference='Stop'
Add-Type -AssemblyName PresentationCore,WindowsBase
$icons=[ordered]@{
 Launch='M7 4 L20 12 L7 20 Z'
 News='M4 4 H17 V8 H21 V20 H4 Z M17 8 V20 M8 8 H13 M8 12 H13 M8 16 H13'
 Document='M5 3 H15 L20 8 V21 H5 Z M15 3 V8 H20 M9 12 H16 M9 16 H16'
 Server='M4 4 H20 V10 H4 Z M4 14 H20 V20 H4 Z M7 7 H7.1 M7 17 H7.1 M11 7 H17 M11 17 H17'
 Market='M4 4 V20 H21 M7 15 L11 11 L14 13 L20 6 M16 6 H20 V10'
 Map='M3 5 L9 3 L15 5 L21 3 V19 L15 21 L9 19 L3 21 Z M9 3 V19 M15 5 V21'
 Settings='M10 3 H14 L15 6 L18 8 H21 L23 12 L21 16 H18 L15 18 L14 21 H10 L9 18 L6 16 H3 L1 12 L3 8 H6 L9 6 Z M12 8.5 A3.5 3.5 0 1 0 12 15.5 A3.5 3.5 0 1 0 12 8.5'
 Sun='M12 7 A5 5 0 1 0 12 17 A5 5 0 1 0 12 7 M12 2 V4 M12 20 V22 M2 12 H4 M20 12 H22 M5 5 L6.5 6.5 M17.5 17.5 L19 19 M5 19 L6.5 17.5 M17.5 6.5 L19 5'
 Moon='M20 15 A8.5 8.5 0 1 1 9 4 A7 7 0 0 0 20 15 Z'
 Back='M20 12 H4 M10 6 L4 12 L10 18'
 ArrowLeft='M20 12 H4 M10 6 L4 12 L10 18'
 ArrowRight='M4 12 H20 M14 6 L20 12 L14 18'
 ArrowUp='M12 20 V4 M6 10 L12 4 L18 10'
 Minimize='M5 12 H19'
 Maximize='M5 5 H19 V19 H5 Z'
 Restore='M8 4 H20 V16 M4 8 H16 V20 H4 Z'
 Close='M6 6 L18 18 M18 6 L6 18'
 Refresh='M20 5 V11 H14 M4 19 V13 H10 M19 9 A7.5 7.5 0 0 0 6 6 M5 15 A7.5 7.5 0 0 0 18 18'
 Retry='M20 5 V11 H14 M4 19 V13 H10 M19 9 A7.5 7.5 0 0 0 6 6 M5 15 A7.5 7.5 0 0 0 18 18'
 External='M14 4 H20 V10 M20 4 L11 13 M10 5 H4 V20 H19 V14'
 Pin='M8 3 H16 L15 9 L19 13 H5 L9 9 Z M12 13 V21'
 Volume='M3 9 H7 L12 5 V19 L7 15 H3 Z M16 8 A6 6 0 0 1 16 16 M19 5 A10 10 0 0 1 19 19'
 Package='M3 7 L12 3 L21 7 V17 L12 21 L3 17 Z M3 7 L12 12 L21 7 M12 12 V21 M8 5 L17 9'
 Changelog='M5 3 H19 V21 H5 Z M9 7 H15 M9 12 H15 M9 17 H13'
 Heart='M12 20 L4 12 V7 L7 4 H10 L12 6 L14 4 H17 L20 7 V12 Z'
 Info='M12 3 A9 9 0 1 0 12 21 A9 9 0 1 0 12 3 M12 11 V17 M12 7 V7.1'
 Help='M12 3 A9 9 0 1 0 12 21 A9 9 0 1 0 12 3 M8.5 8 A3.5 3.5 0 0 1 15.5 8 C15.5 10 12 10 12 13 M12 17 V17.1'
 Mods='M4 4 H9 V7 H15 V4 H20 V9 H17 V15 H20 V20 H15 V17 H9 V20 H4 V15 H7 V9 H4 Z'
 Export='M5 5 H11 M5 5 V21 H19 V15 M12 12 L21 3 M14 3 H21 V10'
 Save='M4 3 H17 L21 7 V21 H4 Z M8 3 V9 H16 V3 M8 21 V14 H17 V21'
 Resources='M3 7 L12 3 L21 7 V17 L12 21 L3 17 Z M3 7 L12 12 L21 7 M12 12 V21'
 Shader='M12 2 L14.5 9.5 L22 12 L14.5 14.5 L12 22 L9.5 14.5 L2 12 L9.5 9.5 Z'
 Camera='M3 7 H8 L10 4 H15 L17 7 H21 V20 H3 Z M12 10 A3.5 3.5 0 1 0 12 17 A3.5 3.5 0 1 0 12 10'
 Blueprint='M5 3 H16 L20 7 V21 H5 Z M9 11 H16 M9 15 H16 M9 19 H13 M16 3 V7 H20'
 Download='M12 3 V15 M6 9 L12 15 L18 9 M4 17 V21 H20 V17'
 Search='M10 3 A7 7 0 1 0 10 17 A7 7 0 1 0 10 3 M15 15 L21 21'
 Folder='M3 6 H10 L12 8 H21 V20 H3 Z M3 11 H21'
 Link='M9 15 L15 9 M8 13 L5 16 A3 3 0 0 0 9 20 L13 16 M11 8 L15 4 A3 3 0 0 1 19 8 L16 11'
 Feedback='M4 4 H20 V16 H11 L6 20 V16 H4 Z M8 8 H16 M8 12 H13'
 Vote='M4 20 H21 M6 16 V9 H9 V16 Z M12 16 V5 H15 V16 Z M18 16 V3 H21 V16 Z'
 Toolbox='M3 8 H21 V20 H3 Z M8 8 V4 H16 V8 M3 12 H21 M10 12 V15 H14 V12'
 Check='M4 12 L10 18 L21 6'
 Warning='M12 3 L22 21 H2 Z M12 9 V14 M12 17 V17.1'
 Shield='M12 3 L20 6 V12 L17 18 L12 21 L7 18 L4 12 V6 Z M8 12 L11 15 L16 9'
 Reset='M4 6 V11 H9 M5 10 A7.5 7.5 0 1 1 6 18'
 Add='M12 4 V20 M4 12 H20'
 Copy='M8 3 H21 V16 M3 8 H16 V21 H3 Z'
 Trash='M3 6 H21 M9 6 V3 H15 V6 M6 6 V21 H18 V6 M10 10 V17 M14 10 V17'
 Clock='M12 3 A9 9 0 1 0 12 21 A9 9 0 1 0 12 3 M12 7 V12 L16 15'
 User='M12 3 A4 4 0 1 0 12 11 A4 4 0 1 0 12 3 M5 21 V18 L8 14 H16 L19 18 V21'
}
$pen=[Windows.Media.Pen]::new([Windows.Media.Brushes]::Black,1.8)
$pen.StartLineCap='Square'; $pen.EndLineCap='Square'; $pen.LineJoin='Miter'; $pen.MiterLimit=2
$rows=[Collections.Generic.List[string]]::new()
$rows.Add('<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" xmlns:sys="clr-namespace:System;assembly=mscorlib">')
$rows.Add('    <!-- BRCL industrial icons: original 24-unit outlines, 1.8-unit square/miter stroke widened to filled geometry. Color belongs to the consuming control. -->')
$rows.Add('    <!-- BrclGeometry* is for Path.Data / MyButton.Icon. BrclIcon* is the equivalent path string for legacy Logo properties. -->')
foreach($entry in $icons.GetEnumerator()){
 $geometry=[Windows.Media.Geometry]::Parse($entry.Value).GetWidenedPathGeometry($pen)
 $data=$geometry.ToString([Globalization.CultureInfo]::InvariantCulture)
 $data=[regex]::Replace($data,'-?\d+\.\d+',{param($m) ([math]::Round([double]::Parse($m.Value,[Globalization.CultureInfo]::InvariantCulture),3)).ToString([Globalization.CultureInfo]::InvariantCulture)})
 $safe=[Security.SecurityElement]::Escape($data)
 $rows.Add('    <Geometry x:Key="BrclGeometry'+$entry.Key+'">'+$safe+'</Geometry>')
 $rows.Add('    <sys:String x:Key="BrclIcon'+$entry.Key+'">'+$safe+'</sys:String>')
}
$rows.Add('</ResourceDictionary>')
[IO.File]::WriteAllLines([IO.Path]::GetFullPath($OutputFile),$rows,[Text.UTF8Encoding]::new($false))
Write-Output ('Wrote '+$icons.Count+' icon families')
