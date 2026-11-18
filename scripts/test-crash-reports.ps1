param([string]$FixtureDirectory)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot
$src = Join-Path $repo 'Plain Craft Launcher 2'
[Reflection.Assembly]::LoadFrom((Join-Path $src 'Resources\Newtonsoft.Json.dll')) | Out-Null
[Reflection.Assembly]::LoadFrom((Join-Path $src 'Resources\ThrottleDebounce.dll')) | Out-Null
$a = [Reflection.Assembly]::LoadFrom((Join-Path $src 'bin\BRCL.exe'))
$diag = $a.GetType('PCL.ModCrashReports',$true)
$activity = $a.GetType('PCL.ModExperience',$true)
$settings = $a.GetType('PCL.Settings').GetField('Entries').GetValue($null)
$flags = [Reflection.BindingFlags]'Public,NonPublic,Static'
$out = Join-Path $env:TEMP ('BRCL-InkTest-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $out | Out-Null
[AppDomain]::CurrentDomain.SetData('APP_CONTEXT_BASE_DIRECTORY', $out + '\')
foreach ($key in 'BrclCrashMode','SystemBrclCrashUpload') {
    # These are Registry-backed in the client. Keep fixture writes in the test profile.
    $settings[$key].Source = [Enum]::Parse($settings[$key].Source.GetType(), 'Normal')
}
foreach ($name in 'DraftFolder','Pending','LinkedPending','Sessions','SessionFile','Base') {
    $diag.GetField($name,$flags).SetValue($null,(Join-Path $out $name))
}
foreach ($name in 'ActivityFolder','ActivityQueue','IdentityFile') {
    $activity.GetField($name,$flags).SetValue($null,(Join-Path $out $name))
}
$diag.GetField('Uploading',$flags).SetValue($null,1)
$activity.GetField('Sending',$flags).SetValue($null,1)
$settings['BrclUsageEnabled'].Set($false,$null)
$settings['BrclCrashMode'].Set(3,$null)
$settings['SystemBrclCrashUpload'].Set($false,$null)
if($diag.GetMethod('CrashMode',$flags).Invoke($null,@()) -ne 0) {throw 'Migration ignored old opt-out'}
$settings['SystemBrclCrashUpload'].Set($true,$null)
if($diag.GetMethod('CrashMode',$flags).Invoke($null,@()) -ne 1) {throw 'Migration selected auto-upload'}
$settings['BrclCrashMode'].Set(0,$null)
function Call-Diag([string]$name,[object[]]$values) { $diag.GetMethod($name,$flags).Invoke($null,$values) }
$raw = "Authorization: Bearer fake-access --accessToken fake-token password=fake-pass C:\Users\DemoPerson\game me@example.com --username Player01" + [Environment]::NewLine + '[CHAT] private-message'
$redacted = Call-Diag 'RedactCrashText' @($raw)
foreach ($secret in 'fake-access','fake-token','fake-pass','DemoPerson','me@example.com','Player01','private-message') {
    if ($redacted.Contains($secret)) { throw "脱敏测试失败" }
}
$files = [Collections.Generic.List[Newtonsoft.Json.Linq.JObject]]::new()
$files.Add([Newtonsoft.Json.Linq.JObject]::Parse('{"name":"exception.log","text":"BRCL.SyntheticInkAdminSmokeException: synthetic fixture"}'))
function Queue-Sample { $diag.GetMethod('QueueReport',$flags).Invoke($null,[object[]]@('launcher','synthetic_test','',$files,$null,'','','')) }
Queue-Sample
if (Test-Path (Join-Path $out 'Pending')) {throw 'Disabled mode wrote a report'}
$settings['BrclCrashMode'].Set(1,$null)
Queue-Sample
$draft = @(Get-ChildItem (Join-Path $out 'DraftFolder') -Filter '*.draft')
if($draft.Count -ne 1 -or (Test-Path (Join-Path $out 'Pending'))) {throw 'Ask mode uploaded without approval'}
$report = $diag.GetMethod('ReadDraft',$flags).Invoke($null,@($draft[0].FullName))
if ($report['install'] -or $report['session']) {throw 'Manual report was linked without consent'}
if ([Text.Encoding]::UTF8.GetString([IO.File]::ReadAllBytes($draft[0].FullName)).Contains('SyntheticInkAdminSmokeException')) {throw 'Draft contains plaintext'}
Call-Diag 'ApproveDraft' @($draft[0].FullName,$report)
$pending = @(Get-ChildItem (Join-Path $out 'Pending') -Filter '*.json')
if ($pending.Count -ne 1 -or (Test-Path $draft[0].FullName)) {throw 'Approval handoff failed'}
$forged = [Newtonsoft.Json.Linq.JObject]::Parse('{"ok":true,"id":"x","receipt":"AAAA"}')
if (Call-Diag 'VerifyCrashReceipt' @($forged,'x')) {throw 'Untrusted receipt accepted'}
$settings['BrclCrashMode'].Set(0,$null)
Call-Diag 'CrashUploadSettingChanged' @()
if (@(Get-ChildItem (Join-Path $out 'Pending') -File).Count) {throw 'Opt-out did not erase queue'}
$settings['BrclCrashMode'].Set(2,$null)
$settings['BrclUsageEnabled'].Set($true,$null)
$session = [Guid]::NewGuid().ToString('N')
$activity.GetMethod('RecordUsage',$flags).Invoke($null,@($session))
$activity.GetMethod('RecordUsage',$flags).Invoke($null,@($null))
$activity.GetMethod('RecordUsage',$flags).Invoke($null,@($null))
if (@(Get-ChildItem (Join-Path $out 'ActivityQueue') -Filter '*.json').Count -ne 2) {throw 'Activity queue did not coalesce daily events'}
$diag.GetMethod('QueueReport',$flags).Invoke($null,[object[]]@('minecraft','synthetic_test','',$files,$session,'1.21.1','21.0','TRM'))
$pending = @(Get-ChildItem (Join-Path $out 'LinkedPending') -Filter '*.json')
if ($pending.Count -ne 1) {throw 'Automatic mode did not queue one report'}
Add-Type -AssemblyName System.Net.Http
Add-Type -ReferencedAssemblies System.Net.Http.dll -TypeDefinition @"
using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
public sealed class DiagnosticFixtureHandler : HttpMessageHandler {
    public int Calls;
    public int Status = 400;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) {
        Calls++;
        return Task.FromResult(new HttpResponseMessage((HttpStatusCode)Status){Content = new StringContent("{}")});
    }
}
"@
$extra=Join-Path $out ('LinkedPending\'+[Guid]::NewGuid().ToString('N')+'.json')
Copy-Item -LiteralPath $pending[0].FullName -Destination $extra
$handler=[DiagnosticFixtureHandler]::new()
$http=[Net.Http.HttpClient]::new($handler)
try {
    foreach ($code in 400,503,200) {
        $handler.Calls=0
        $handler.Status=$code
        try { Call-Diag 'SendQueuedReports' @($http,[Threading.CancellationToken]::None) } catch [System.Management.Automation.MethodInvocationException] {}
        $expected=if($code -eq 503){1}else{2}
        if($handler.Calls -ne $expected -or @(Get-ChildItem (Join-Path $out 'LinkedPending') -File).Count -ne 2){throw "Retry case $code did not retain and isolate reports"}
    }
} finally { $http.Dispose() }
$settings['BrclCrashMode'].Set(1,$null)
Queue-Sample
$linkedDraft=@(Get-ChildItem (Join-Path $out 'DraftFolder') -Filter '*.draft')[0]
if (-not ($diag.GetMethod('ReadDraft',$flags).Invoke($null,@($linkedDraft.FullName)))['install']) {throw 'Missing fixture identity'}
if ($FixtureDirectory) {
    [IO.Directory]::CreateDirectory($FixtureDirectory) | Out-Null
    Copy-Item -LiteralPath $pending[0].FullName -Destination (Join-Path $FixtureDirectory 'envelope.json')
    Copy-Item -LiteralPath (Join-Path $out "ActivityQueue\$session.json") -Destination (Join-Path $FixtureDirectory 'game.json')
    [IO.File]::WriteAllText((Join-Path $FixtureDirectory 'id.txt'),$pending[0].BaseName)
}
$settings['BrclUsageEnabled'].Set($false,$null)
$activity.GetMethod('UsageChanged',$flags).Invoke($null,@())
if ((Test-Path (Join-Path $out 'IdentityFile')) -or @(Get-ChildItem (Join-Path $out 'ActivityQueue') -File).Count) {throw 'Usage opt-out left identifiers or events'}
if (@(Get-ChildItem (Join-Path $out 'LinkedPending') -File).Count) {throw 'Usage opt-out left linked diagnostic report'}
if (($diag.GetMethod('ReadDraft',$flags).Invoke($null,@($linkedDraft.FullName)))['install']) {throw 'Usage opt-out left linkable draft'}
$settings['BrclCrashMode'].Set(0,$null)
Call-Diag 'CrashUploadSettingChanged' @()
Write-Output 'PASS release executable: redaction, consent modes, DPAPI preview, approved queue, invalid receipt, daily deduplication and opt-out cleanup'
