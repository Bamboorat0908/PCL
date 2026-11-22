# Component regression test only: creates no application window and sends no input.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationFramework
Add-Type -AssemblyName System.Drawing
$projectRoot = Join-Path $PSScriptRoot '..\Plain Craft Launcher 2'
[void][Reflection.Assembly]::LoadFrom((Join-Path $projectRoot 'Resources\Newtonsoft.Json.dll'))
$assembly = [Reflection.Assembly]::LoadFrom((Join-Path $projectRoot 'bin\BRCL.exe'))
if ($null -eq [Windows.Application]::Current) { $application = New-Object Windows.Application }
else { $application = [Windows.Application]::Current }
[Windows.Application]::ResourceAssembly = $assembly
$application.Resources['ColorBrushSemiTransparent'] = [Windows.Media.Brushes]::Transparent
$application.Resources['ColorBrush8'] = New-Object Windows.Media.SolidColorBrush ([Windows.Media.Color]::FromRgb(19,17,32))
$application.Resources['ColorBrush5'] = [Windows.Media.Brushes]::MediumPurple
$animation = $assembly.GetType('BRCL.ModAnimation', $true)
$animation.GetProperty('AniControlEnabled').SetValue($null, 1, $null)
$buttonType = $assembly.GetType('BRCL.MyIconButton', $true)
$themeType = $assembly.GetType('BRCL.MyIconButton+Themes', $true)
$button = [Activator]::CreateInstance($buttonType)
$pathProperty = $buttonType.GetProperty('Path', [Reflection.BindingFlags]'Instance, Public, NonPublic')
$icon = $pathProperty.GetValue($button, $null)
if ($null -eq $icon) { throw 'Icon component was not initialized' }
function Set-TestTheme([string]$name) {
    $buttonType.GetProperty('Theme').SetValue($button, [Enum]::Parse($themeType, $name), $null)
}
Set-TestTheme 'White'
if ($icon.Fill.Color -ne [Windows.Media.Colors]::White) { throw 'White icon follows the dark background resource' }
Write-Output 'PASS white icon on dark palette'
$application.Resources['ColorBrush8'] = [Windows.Media.Brushes]::White
$button.RefreshAnim()
if ($icon.Fill.Color -ne [Windows.Media.Colors]::White) { throw 'White icon changed with the background resource' }
Write-Output 'PASS white icon independent of background resource'
Set-TestTheme 'Black'
if ($icon.Fill.Color.R -ne 0 -or $icon.Fill.Color.A -eq 0) { throw 'Theme setter did not refresh the icon immediately' }
Write-Output 'PASS black theme refresh'
Set-TestTheme 'White'
if ($icon.Fill.Color -ne [Windows.Media.Colors]::White) { throw 'Returning to white did not refresh the icon' }
Write-Output 'PASS returning to white theme'
$button.IsEnabled = $false
$application.Resources['ColorBrushGray5'] = [Windows.Media.Brushes]::Gray
$button.RefreshAnim()
if ($icon.Fill.Color -ne [Windows.Media.Colors]::Gray) { throw 'Disabled icon lost its disabled styling' }
Write-Output 'PASS disabled styling'
$button.IsEnabled = $true
$button.RefreshAnim()
if ($icon.Fill.Color -ne [Windows.Media.Colors]::White) { throw 'Enabled icon did not restore its theme' }
Write-Output 'PASS enabled theme restoration'
Write-Output '6 icon theme regression cases passed.'
