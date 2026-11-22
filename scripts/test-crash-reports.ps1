param([string]$FixtureDirectory, [string]$ExePath = (Join-Path $PSScriptRoot '..\Plain Craft Launcher 2\bin\BRCL.exe'))
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot
$src = Join-Path $repo 'Plain Craft Launcher 2'
$out = Join-Path $env:TEMP ('BRCL-InkTest-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $out | Out-Null
[AppDomain]::CurrentDomain.SetData('APP_CONTEXT_BASE_DIRECTORY', $out + '\')
[Reflection.Assembly]::LoadFrom((Join-Path $src 'Resources\Newtonsoft.Json.dll')) | Out-Null
[Reflection.Assembly]::LoadFrom((Join-Path $src 'Resources\ThrottleDebounce.dll')) | Out-Null
# Reflection does not execute the launcher's embedded assembly resolver. Resolve
# its unchanged build dependencies explicitly when verifying a copied single EXE.
foreach ($dependency in 'MeloongCore.dll','MeloongCore.Wpf.dll','BRCL.Core.dll') {
    [Reflection.Assembly]::LoadFrom((Join-Path $src ('bin\' + $dependency))) | Out-Null
}
$a = [Reflection.Assembly]::LoadFrom([IO.Path]::GetFullPath($ExePath))
$diag = $a.GetType('BRCL.ModCrashReports',$true)
$activity = $a.GetType('BRCL.ModExperience',$true)
$settings = $a.GetType('BRCL.Settings').GetField('Entries').GetValue($null)
$flags = [Reflection.BindingFlags]'Public,NonPublic,Static'
foreach ($key in 'BrclCrashMode','SystemBrclCrashUpload') {
    # Registry-backed client settings stay in the isolated test profile.
    $settings[$key].Source = [Enum]::Parse($settings[$key].Source.GetType(), 'Normal')
}
foreach ($name in 'DraftFolder','Pending','LinkedPending','Sessions','Base') {
    $diag.GetField($name,$flags).SetValue($null,(Join-Path $out $name))
}
$diag.GetField('SessionFile',$flags).SetValue($null,(Join-Path $out 'Sessions\test-session.json'))
foreach ($name in 'ActivityFolder','ActivityQueue','IdentityFile') {
    $activity.GetField($name,$flags).SetValue($null,(Join-Path $out $name))
}
# Suppress worker creation: all HTTP checks below use an in-memory handler.
$diag.GetField('Uploading',$flags).SetValue($null,1)
$activity.GetField('Sending',$flags).SetValue($null,1)
function Call-Diag([string]$name,[object[]]$values) {
    $result = $diag.GetMethod($name,$flags).Invoke($null,$values)
    if ($null -ne $result) { return ,$result }
}
function Call-Activity([string]$name,[object[]]$values) { $activity.GetMethod($name,$flags).Invoke($null,$values) }
function Initialize-Diagnostics {
    Call-Diag 'InitializeCrashReports' @()
    $timer = $diag.GetField('RetryTimer',$flags).GetValue($null)
    if ($timer) { $timer.Dispose() }
}
function New-LegacyDraft([bool]$linked = $false) {
    $report = [Newtonsoft.Json.Linq.JObject]::Parse('{"kind":"launcher","version":"1.0.2.11","reason":"synthetic_test","files":[{"name":"exception.log","text":"BRCL.SyntheticInkAdminSmokeException: synthetic fixture"}]}')
    $report['id'] = [Newtonsoft.Json.Linq.JValue]::new([guid]::NewGuid().ToString('N'))
    $report['created_utc'] = [Newtonsoft.Json.Linq.JValue]::new([DateTime]::UtcNow.ToString('O'))
    if ($linked) { $report['install'] = [Newtonsoft.Json.Linq.JValue]::new([string](Call-Activity 'InstallId' @())) }
    $folder = Join-Path $out 'DraftFolder'
    [IO.Directory]::CreateDirectory($folder) | Out-Null
    $path = Join-Path $folder (([string]$report['id']) + '.draft')
    $bytes = [Security.Cryptography.ProtectedData]::Protect([Text.Encoding]::UTF8.GetBytes($report.ToString()),$null,[Security.Cryptography.DataProtectionScope]::CurrentUser)
    [IO.File]::WriteAllBytes($path,$bytes)
    return $path
}
$settings['BrclUsageEnabled'].Set($false,$null)
foreach ($mode in 0,1,2,3) {
    $settings['BrclCrashMode'].Set($mode,$null)
    if ((Call-Diag 'CrashMode' @()) -ne 0) {throw 'Plan opt-out did not override crash mode'}
}
$settings['BrclUsageEnabled'].Set($true,$null)
$settings['BrclCrashMode'].Set(3,$null)
$settings['SystemBrclCrashUpload'].Set($false,$null)
if ((Call-Diag 'CrashMode' @()) -ne 0) {throw 'Migration ignored old opt-out'}
$settings['SystemBrclCrashUpload'].Set($true,$null)
if ((Call-Diag 'CrashMode' @()) -ne 2) {throw 'Legacy enabled mode did not select automatic upload'}
$settings['BrclCrashMode'].Set(1,$null)
Initialize-Diagnostics
if ($settings['BrclCrashMode'].GetCache($null) -ne 2 -or (Call-Diag 'CrashMode' @()) -ne 2) {throw 'Legacy ask mode did not migrate to automatic upload'}
$settings['BrclCrashMode'].Set(0,$null)
Initialize-Diagnostics
if ((Call-Diag 'CrashMode' @()) -ne 0) {throw 'Restart changed an explicit diagnostic opt-out'}
$settings['BrclUsageEnabled'].Set($false,$null)

$raw = "Authorization: Bearer fake-access --accessToken fake-token password=fake-pass C:\Users\DemoPerson\game me@example.com --username Player01" + [Environment]::NewLine + '[CHAT] private-message'
$redacted = Call-Diag 'RedactCrashText' @($raw)
foreach ($secret in 'fake-access','fake-token','fake-pass','DemoPerson','me@example.com','Player01','private-message') {
    if ($redacted.Contains($secret)) { throw 'Redaction leaked fixture data' }
}
$files = [Collections.Generic.List[Newtonsoft.Json.Linq.JObject]]::new()
$files.Add([Newtonsoft.Json.Linq.JObject]::Parse('{"name":"exception.log","text":"BRCL.SyntheticInkAdminSmokeException: synthetic fixture"}'))
function Queue-Sample { $diag.GetMethod('QueueReport',$flags).Invoke($null,[object[]]@('launcher','synthetic_test','',$files,$null,'','','')) }
Queue-Sample
if (@(Get-ChildItem (Join-Path $out 'Pending') -Filter '*.json' -ErrorAction SilentlyContinue).Count) {throw 'Disabled mode wrote a report'}
Call-Activity 'SaveUsageChoice' @($true)
if (-not (Call-Activity 'UsageEnabled' @()) -or (Call-Diag 'CrashMode' @()) -ne 2) {throw 'Consent did not enable automatic crash upload'}
Queue-Sample
if (@(Get-ChildItem (Join-Path $out 'LinkedPending') -Filter '*.json').Count -ne 1 -or @(Get-ChildItem (Join-Path $out 'DraftFolder') -Filter '*.draft' -ErrorAction SilentlyContinue).Count) {throw 'Consent did not queue a crash without a confirmation draft'}
Call-Activity 'SaveUsageChoice' @($false)

$draft = New-LegacyDraft
if ([Text.Encoding]::UTF8.GetString([IO.File]::ReadAllBytes($draft)).Contains('SyntheticInkAdminSmokeException')) {throw 'Legacy draft contains plaintext'}
Call-Diag 'QueueAutomaticDrafts' @([Threading.CancellationToken]::None)
if (-not (Test-Path $draft)) {throw 'Legacy draft sent without plan consent'}
$settings['BrclUsageEnabled'].Set($true,$null)
$settings['BrclCrashMode'].Set(0,$null)
Call-Diag 'QueueAutomaticDrafts' @([Threading.CancellationToken]::None)
if (-not (Test-Path $draft)) {throw 'Legacy draft sent while diagnostics disabled'}
$settings['BrclCrashMode'].Set(2,$null)
$brokenDraft = Join-Path $out 'DraftFolder\broken.draft'
[IO.File]::WriteAllText($brokenDraft,'invalid DPAPI fixture')
Call-Diag 'QueueAutomaticDrafts' @([Threading.CancellationToken]::None)
$pending = @(Get-ChildItem (Join-Path $out 'Pending') -Filter '*.json')
if ($pending.Count -ne 1 -or (Test-Path $draft)) {throw 'Legacy automatic draft handoff failed'}
if (-not (Test-Path $brokenDraft)) {throw 'Unreadable legacy draft was not retained'}
$forged = [Newtonsoft.Json.Linq.JObject]::Parse('{"ok":true,"id":"x","receipt":"AAAA"}')
if (Call-Diag 'VerifyCrashReceipt' @($forged,'x')) {throw 'Untrusted receipt accepted'}
$settings['BrclCrashMode'].Set(0,$null)
Call-Diag 'CrashUploadSettingChanged' @()
if (@(Get-ChildItem (Join-Path $out 'Pending') -File).Count) {throw 'Opt-out did not erase queue'}

$settings['BrclCrashMode'].Set(2,$null)
$settings['BrclUsageEnabled'].Set($true,$null)
$session = [Guid]::NewGuid().ToString('N')
Call-Activity 'RecordUsage' @($session)
Call-Activity 'RecordUsage' @($null)
Call-Activity 'RecordUsage' @($null)
if (@(Get-ChildItem (Join-Path $out 'ActivityQueue') -Filter '*.json').Count -ne 2) {throw 'Activity queue did not coalesce daily events'}
$diag.GetMethod('QueueReport',$flags).Invoke($null,[object[]]@('minecraft','synthetic_test','',$files,$session,'1.21.1','21.0','TRM'))
$pending = @(Get-ChildItem (Join-Path $out 'LinkedPending') -Filter '*.json')
if ($pending.Count -ne 1) {throw 'Automatic mode did not queue one report'}
$client = Call-Activity 'CreateInkClient' @(12)
try {
    if ($client.DefaultRequestHeaders.UserAgent.ToString() -notmatch '^BRCL/[0-9.]+$') {throw 'Collector client lacks the BRCL User-Agent'}
    if ($client.Timeout.TotalSeconds -ne 12) {throw 'Collector timeout changed'}
} finally { $client.Dispose() }

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
$linkedDraft = [string](New-LegacyDraft $true)
if (-not (Call-Diag 'ReadDraft' @($linkedDraft))['install']) {throw 'Missing fixture identity'}
if ($FixtureDirectory) {
    [IO.Directory]::CreateDirectory($FixtureDirectory) | Out-Null
    Copy-Item -LiteralPath $pending[0].FullName -Destination (Join-Path $FixtureDirectory 'envelope.json')
    Copy-Item -LiteralPath (Join-Path $out "ActivityQueue\$session.json") -Destination (Join-Path $FixtureDirectory 'game.json')
    [IO.File]::WriteAllText((Join-Path $FixtureDirectory 'id.txt'),$pending[0].BaseName)
}
Call-Activity 'SaveUsageChoice' @($false)
if ((Test-Path (Join-Path $out 'IdentityFile')) -or @(Get-ChildItem (Join-Path $out 'ActivityQueue') -File).Count) {throw 'Usage opt-out left identifiers or events'}
foreach ($folder in 'Pending','LinkedPending','DraftFolder','Sessions') {
    if (@(Get-ChildItem (Join-Path $out $folder) -File -ErrorAction SilentlyContinue).Count) {throw "Plan opt-out left files in $folder"}
}
if ((Call-Diag 'CrashMode' @()) -ne 0) {throw 'Plan opt-out did not disable diagnostics'}
Call-Activity 'SaveUsageChoice' @($true)
if ((Call-Diag 'CrashMode' @()) -ne 2) {throw 'Rejoining did not enable automatic upload'}
Call-Activity 'SaveUsageChoice' @($false)
Write-Output 'PASS release executable: redaction, opt-in automatic uploads, legacy mode migration, preserved diagnostic opt-out, DPAPI draft migration, BRCL User-Agent, invalid receipt, retries, daily deduplication and complete opt-out cleanup'
