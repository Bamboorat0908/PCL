$ErrorActionPreference='Stop'
Add-Type -AssemblyName PresentationFramework,PresentationCore,WindowsBase,System.Xaml
$assembly=[Reflection.Assembly]::LoadFrom((Resolve-Path (Join-Path $PSScriptRoot '..\Plain Craft Launcher 2\bin\BRCL.exe')))
[Windows.Application]::ResourceAssembly=$assembly
$app=[Activator]::CreateInstance($assembly.GetType('BRCL.Application',$true),$true)
$app.InitializeComponent()
$legacyType=$assembly.GetType('BRCL.MyBitmap',$true)
$imageType=$assembly.GetType('BRCL.MyImage',$true)
$convert=$legacyType.GetMethods()|Where-Object {$_.Name -eq 'op_Implicit' -and $_.ReturnType -eq [Windows.Media.ImageSource]}
function Render-Hash($source){
    $bitmap=[Windows.Media.Imaging.RenderTargetBitmap]::new(284,96,96,96,[Windows.Media.PixelFormats]::Pbgra32)
    $visual=[Windows.Media.DrawingVisual]::new()
    $context=$visual.RenderOpen()
    try{$context.DrawImage($source,[Windows.Rect]::new(0,0,284,96))}finally{$context.Close()}
    $bitmap.Render($visual)
    $pixels=New-Object byte[] (284*96*4)
    $bitmap.CopyPixels($pixels,284*4,0)
    $sha=[Security.Cryptography.SHA256]::Create()
    try{[Convert]::ToBase64String($sha.ComputeHash($pixels))}finally{$sha.Dispose()}
}
foreach($name in 'TitleLogo.png','Icons/NoIcon.png','Blocks/Grass.png'){
    $path="pack://application:,,,/images/$name"
    $legacy=[Activator]::CreateInstance($legacyType,[object[]]@($path))
    $reference=$convert.Invoke($null,[object[]]@($legacy))
    $image=[Activator]::CreateInstance($imageType)
    $image.ActualSource=$path
    $actual=$image.GetValue([Windows.Controls.Image]::SourceProperty)
    if(!$actual -or !$actual.IsFrozen -or (Render-Hash $reference) -ne (Render-Hash $actual)){throw "Rendered pixels differ for $name"}
    Write-Output "PASS unchanged embedded image pixels: $name"
}
