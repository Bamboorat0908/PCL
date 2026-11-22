$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationFramework,System.Net.Http
$project = Join-Path $PSScriptRoot '..\Plain Craft Launcher 2'
[void][Reflection.Assembly]::LoadFrom((Join-Path $project 'Resources\Newtonsoft.Json.dll'))
[void][Reflection.Assembly]::LoadFrom((Join-Path $project 'Resources\ThrottleDebounce.dll'))
$assembly = [Reflection.Assembly]::LoadFrom((Join-Path $project 'bin\BRCL.exe'))
$method = $assembly.GetType('BRCL.ModMinecraft',$true).GetMethod('McLibListGetWithJson')
$root = Join-Path $env:TEMP ('BRCL-LibraryPaths-' + [guid]::NewGuid().ToString('N'))
$root += '\'
function Check($condition, [string]$message) {
    if (-not $condition) { throw $message }
    Write-Output "PASS $message"
}
function Parse-Library([hashtable]$library) {
    $json = [Newtonsoft.Json.Linq.JObject]::Parse((@{libraries=@($library)} | ConvertTo-Json -Depth 12 -Compress))
    return $method.Invoke($null, [object[]]@($json, $false, $root))
}
$valid = Parse-Library @{name='org.example:library:1.0:client'}
Check ($valid.Count -eq 1 -and $valid[0].LocalPath -eq ($root + 'libraries\org\example\library\1.0\library-1.0-client.jar')) 'valid Maven coordinates keep classifier and version paths'
$valid = Parse-Library @{name='org.example:library:1.0';downloads=@{artifact=@{path='org/example/library/1.0/library.jar';size=1;url='https://example.invalid/library.jar'}}}
Check ($valid.Count -eq 1 -and $valid[0].LocalPath.EndsWith('org\example\library\1.0\library.jar')) 'valid manifest artifact paths remain supported'
$i=0
foreach ($path in @('../../escaped.jar', '..\..\escaped.jar', 'org/library.jar:payload', '../libraries-other/escaped.jar')) {
    $i++
    $blocked = $false
    try { Parse-Library @{name='org.example:library:1.0';downloads=@{artifact=@{path=$path;size=1;url='https://example.invalid/library.jar'}}} | Out-Null } catch { $failure=$_.Exception.GetBaseException(); $blocked = $failure -is [IO.IOException] -or $failure -is [NotSupportedException] }
    Check $blocked "unsafe artifact path is rejected: $path"
}
$blocked = $false
try { Parse-Library @{name='org.example:library:1.0';natives=@{windows='natives-windows'};downloads=@{classifiers=@{'natives-windows'=@{path='../../escaped.jar';size=1;sha1='a';url='https://example.invalid/native.jar'}}}} | Out-Null } catch { $blocked = $_.Exception.GetBaseException() -is [IO.IOException] }
Check $blocked 'native library manifest paths cannot leave the library directory'
$blocked = $false
try { Parse-Library @{name='org.example:../../../../escape:1.0'} | Out-Null } catch { $blocked = $_.Exception.GetBaseException() -is [IO.IOException] }
Check $blocked 'generated Maven coordinate paths cannot leave the library directory'
Check (-not (Test-Path $root)) 'library metadata validation writes no files and performs no downloads'
