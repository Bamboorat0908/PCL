param([string]$AssemblyPath, [string]$ClientId)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationFramework
Add-Type -AssemblyName System.Net.Http
$root = Join-Path (Split-Path $PSScriptRoot) 'Plain Craft Launcher 2'
[void][Reflection.Assembly]::LoadFrom((Join-Path $root 'Resources\Newtonsoft.Json.dll'))
if (-not $AssemblyPath) { $AssemblyPath = Join-Path $root 'bin\BRCL.exe' }
$assembly = [Reflection.Assembly]::LoadFrom($AssemblyPath)
$type = $assembly.GetType('PCL.ModSecret', $true)
$field = $type.GetField('OAuthClientId', [Reflection.BindingFlags]'Public,Static')
$method = $type.GetMethod('EnsureMicrosoftClientId', [Reflection.BindingFlags]'Public,Static')
$field.SetValue($null, '')
$method.Invoke($null, @())
if ($field.GetValue($null) -cne 'c36a9fb6-4f2a-41ff-90bd-ae7cc92031eb') { throw 'Bundled Client ID fallback failed' }
Write-Output 'PASS: clean installation uses bundled Client ID without prompting'
if ($ClientId) {
    $field.SetValue($null, $ClientId)
    $method.Invoke($null, @())
    if ($field.GetValue($null) -ne $ClientId) { throw 'Configured Client ID rejected' }
    Write-Output 'PASS: build accepts the configured Client ID'
}
$field.SetValue($null, ' 12345678-1234-1234-1234-123456789ABC ')
$method.Invoke($null, @())
if ($field.GetValue($null) -cne '12345678-1234-1234-1234-123456789abc') { throw 'Client ID normalization failed' }
Write-Output 'PASS: configured Client ID is normalized without prompting'
$field.SetValue($null, 'not-a-client-id')
$rejected = $false
try { $method.Invoke($null, @()) }
catch {
    if (($_.Exception.ToString()) -notmatch 'Client ID') { throw }
    $rejected = $true
}
if (-not $rejected) { throw 'Invalid Client ID accepted' }
Write-Output 'PASS: invalid configured Client ID is rejected before network requests'
