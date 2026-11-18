param([string]$OutputDirectory = 'bin\BrclDialogs')
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationFramework,System.Net.Http
$project = Join-Path $PSScriptRoot '..\Plain Craft Launcher 2'
[void][Reflection.Assembly]::LoadFrom((Join-Path $project 'Resources\Newtonsoft.Json.dll'))
$assembly = [Reflection.Assembly]::LoadFrom((Join-Path $project "$OutputDirectory\BRCL.exe"))
$builder = $assembly.GetType('PCL.BrclErrorPresentation',$true).GetMethod('BuildBrclError')
$sanitize = $assembly.GetType('PCL.BrclErrorPresentation',$true).GetMethod('SanitizeBrclDetails')
$cases = @(
 @{Name='network failure'; Operation='status'; Ex=[Net.WebException]::new('connection closed'); Code='BRCL-NET-001'},
 @{Name='nested network failure'; Operation='status'; Ex=[Exception]::new('outer',[Net.WebException]::new('inner')); Code='BRCL-NET-001'},
 @{Name='timeout'; Operation='status'; Ex=[TimeoutException]::new('timeout'); Code='BRCL-NET-002'},
 @{Name='user cancellation'; Operation='request'; Ex=[OperationCanceledException]::new('cancelled'); Code='BRCL-REQ-001'},
 @{Name='permission'; Operation='files'; Ex=[UnauthorizedAccessException]::new('denied'); Code='BRCL-FILE-002'},
 @{Name='missing file'; Operation='files'; Ex=[IO.FileNotFoundException]::new('missing'); Code='BRCL-FILE-001'},
 @{Name='disk full'; Operation='files'; Ex=[IO.IOException]::new('full',-2147024784); Code='BRCL-FILE-004'},
 @{Name='checksum'; Operation='SHA-256'; Ex=[Exception]::new('bad hash'); Code='BRCL-DATA-001'},
 @{Name='unknown exception'; Operation='sample'; Ex=[InvalidOperationException]::new('diagnostic text'); Code='BRCL-APP-001'},
 @{Name='fatal'; Operation='sample'; Ex=[InvalidOperationException]::new('diagnostic text'); Code='BRCL-APP-999'; Fatal=$true}
)
foreach ($case in $cases) {
 $content = $builder.Invoke($null,[object[]]@($case.Operation,$case.Ex,$null,[bool]$case.Fatal))
 if ($content.Code -ne $case.Code) {throw "Wrong category: $($case.Name): $($content.Code)"}
 if (-not $content.Title -or -not $content.Summary -or -not $content.Advice -or -not $content.Details) {throw "Missing error content: $($case.Name)"}
 if ($content.Summary.Contains('System.') -or $content.Advice.Contains('PCL')) {throw "Technical or upstream copy leaked: $($case.Name)"}
 Write-Output "PASS $($case.Name)"
}
$sensitive = 'Authorization: Bearer token123.abc access_token=secretOne&refresh_token=secretTwo client_secret="secretThree" --accessToken commandToken --password commandPassword idToken=jsonIdToken api_key=apiSecret password="formPassword"'
$redacted = [string]$sanitize.Invoke($null,[object[]]@($sensitive))
foreach ($secret in @('token123.abc','secretOne','secretTwo','secretThree','commandToken','commandPassword','jsonIdToken','apiSecret','formPassword')) { if ($redacted.Contains($secret)) {throw "Secret retained: $secret"} }
Write-Output 'PASS diagnostic token redaction'
$converter = $assembly.GetType('PCL.ModMain+MyMsgBoxConverter',$true)
foreach ($field in @('ErrorDetails','ErrorCode','RecoveryAdvice','Button1Action','Button2Action','Button3Action')) { if (-not $converter.GetField($field)) {throw "Missing converter field $field"} }
Write-Output 'PASS error fields coexist with existing action callbacks'
Write-Output '12 BRCL error checks passed.'
