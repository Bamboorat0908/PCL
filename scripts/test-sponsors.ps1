# Run in Windows PowerShell 5.1, STA, against the Release assembly.
param([string]$BuildDirectory)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationFramework,System.Net.Http
$project = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\Plain Craft Launcher 2'))
[void][Reflection.Assembly]::LoadFrom((Join-Path $project 'Resources\Newtonsoft.Json.dll'))
if (-not $BuildDirectory) { $BuildDirectory = Join-Path $project 'bin' }
$assembly = [Reflection.Assembly]::LoadFrom((Join-Path $BuildDirectory 'BRCL.exe'))
$pageType = $assembly.GetType('PCL.PageSetupSponsors',$true)
$build = $pageType.GetMethod('BuildSponsors',[Reflection.BindingFlags]'NonPublic,Static')
function Parse-Sponsors([string]$json) {
    $data = [Newtonsoft.Json.Linq.JObject]::Parse($json)
    return ,$build.Invoke($null,[object[]](,$data))
}
$items = Parse-Sponsors '{"sponsors":[{"name":" ericzedd ","note":"Thanks","date":"2026-11-02","has_avatar":true},{"name":"Guest","has_avatar":false}]}'
if ($items.Count -ne 2 -or $items[0]['name'].ToString() -ne 'ericzedd' -or $items[0]['note'].ToString() -ne 'Thanks' -or $items[0]['date'].ToString() -ne '2026-11-02') { throw 'Valid records, metadata or original order were lost' }
if ($items[1]['has_avatar'].Value -ne $false) { throw 'Explicit no-avatar flag was lost' }
Write-Output 'PASS sponsor identities, metadata and avatar flags work without amounts'
$items = Parse-Sponsors '{"sponsors":[null,false,{}, {"name":" "}, {"name":42}, {"name":"A","amount":-1}, {"name":"B","amount":NaN}, {"name":"C","amount":"99"}, {"name":"D","amount":Infinity}, {"name":"E","amount":null}]}'
if ($items.Count -ne 5) { throw 'Invalid identities must be skipped' }
foreach ($item in $items) { if ($null -ne $item['amount']) { throw 'Amounts must not enter the displayed sponsor data' } }
$itemsWithAmounts = Parse-Sponsors '{"sponsors":[{"name":"A","amount":1065},{"name":"B","amount":0}]}'
foreach ($item in $itemsWithAmounts) { if ($null -ne $item['amount']) { throw 'Source amounts must be omitted from the displayed sponsor data' } }
Write-Output 'PASS malformed identities are skipped and source amounts are ignored'
$items = Parse-Sponsors '{"sponsors":[]}'
if ($items.Count -ne 0) { throw 'Empty roster must remain empty' }
Write-Output 'PASS empty roster'
foreach ($json in @('{}','{"sponsors":null}','{"sponsors":{}}')) {
    $rejected = $false
    try { $null = Parse-Sponsors $json } catch {
        $errorDetail = $_.Exception
        while ($errorDetail.InnerException) { $errorDetail = $errorDetail.InnerException }
        if ($errorDetail -isnot [FormatException]) { throw }
        $rejected = $true
    }
    if (-not $rejected) { throw 'Malformed response could replace a useful cached roster' }
}
Write-Output 'PASS invalid responses rejected before replacing cached data'
$avatarType = $assembly.GetType('PCL.BrsPlayerAvatar',$true)
$canLoad = $avatarType.GetField('CanLoad',[Reflection.BindingFlags]'NonPublic,Instance')
foreach ($case in @(
    @{Name='ABDSEN'; HasAvatar=$true; Expected=$true},
    @{Name='_MY_S_'; HasAvatar=$true; Expected=$true},
    @{Name='Guest'; HasAvatar=$false; Expected=$false},
    @{Name='afdian user'; HasAvatar=$true; Expected=$false},
    @{Name='../outside'; HasAvatar=$true; Expected=$false},
    @{Name='12345678901234567'; HasAvatar=$true; Expected=$false}
)) {
    $avatar = [Activator]::CreateInstance($avatarType,[object[]]@($case.Name,[double]28,$case.HasAvatar))
    if ($canLoad.GetValue($avatar) -ne $case.Expected) { throw "Incorrect avatar eligibility for $($case.Name)" }
    if ($avatar.Width -ne 28 -or $avatar.Height -ne 28 -or $avatar.Child.Children.Count -ne 2) { throw 'Avatar placeholder or reserved dimensions missing' }
}
Write-Output 'PASS player avatars, non-player fallbacks and safe cache names'
Write-Output '5 sponsor and avatar regression groups passed.'
