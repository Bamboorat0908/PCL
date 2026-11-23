param([string]$OutputDirectory = 'bin')
# Guards the embedded image budget produced by scripts/prepare-brand-assets.py:
# every bitmap must still cover its largest on-screen use at 250% DPI, and none
# may silently grow back to the master resolution.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore, WindowsBase
$exe = Join-Path $PSScriptRoot "..\Plain Craft Launcher 2\$OutputDirectory\BRCL.exe"
$assembly = [Reflection.Assembly]::LoadFile((Resolve-Path $exe).Path)
$stream = $assembly.GetManifestResourceStream('BRCL.g.resources')
if ($null -eq $stream) { throw 'Missing compiled WPF resources' }
$reader = [Resources.ResourceReader]::new($stream)
$images = @{}
try {
    $entries = $reader.GetEnumerator()
    while ($entries.MoveNext()) {
        $key = [string]$entries.Key
        if ($key -match '\.(png|jpg|ico)$') {
            $source = [IO.Stream]$entries.Value
            $bytes = New-Object byte[] $source.Length
            [void]$source.Read($bytes, 0, $bytes.Length)
            $images[$key] = $bytes
        }
    }
} finally { $reader.Dispose() }
function Frame([string]$key) {
    $memory = New-Object IO.MemoryStream(, $images[$key])
    [Windows.Media.Imaging.BitmapDecoder]::Create($memory, 'PreservePixelFormat', 'OnLoad').Frames[0]
}
# key, minimum pixel width (largest DIP use x 2.5), maximum pixel width, byte budget
$rules = @(
    @('images/launcherlogo.png', 240, 384, 65536),
    @('images/brsserverlogo.png', 115, 192, 16384),
    @('images/launchersplash.png', 128, 128, 16384)
)
foreach ($rule in $rules) {
    $key, $minimum, $maximum, $budget = $rule
    if (-not $images.ContainsKey($key)) { throw "Missing packaged image: $key" }
    $frame = Frame $key
    if ($frame.PixelWidth -ne $frame.PixelHeight -or $frame.PixelWidth -lt $minimum -or $frame.PixelWidth -gt $maximum) {
        throw "$key is $($frame.PixelWidth)x$($frame.PixelHeight); expected a square between $minimum and $maximum px."
    }
    if ($images[$key].Length -gt $budget) { throw "$key is $($images[$key].Length) bytes; budget is $budget." }
    if ($frame.Format.BitsPerPixel -gt 8 -and $frame.Format -ne [Windows.Media.PixelFormats]::Bgra32) { throw "$key decodes to an unexpected format: $($frame.Format)" }
    Write-Output "PASS $key $($frame.PixelWidth) px, $($images[$key].Length) bytes"
}
$hero = Frame 'images/brsmachinist.jpg'
if ($hero.PixelWidth -ne 1672 -or $hero.PixelHeight -ne 941) { throw 'Home artwork must keep the 1672x941 size PageLaunchRight decodes against.' }
if ($images['images/brsmachinist.jpg'].Length -gt 614400) { throw 'Home artwork exceeds its 600 KiB budget.' }
Write-Output "PASS images/brsmachinist.jpg 1672x941, $($images['images/brsmachinist.jpg'].Length) bytes"
$total = ($images.Values | Measure-Object -Property Length -Sum).Sum
if ($total -gt 1153434) { throw "Embedded images total $total bytes; budget is 1.1 MiB." }
Write-Output "PASS $($images.Count) embedded images total $total bytes"
Write-Output '5 image budget checks passed.'
