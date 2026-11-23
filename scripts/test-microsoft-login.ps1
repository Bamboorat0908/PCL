param([string]$PreviewDirectory)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationFramework,PresentationCore,WindowsBase,System.Net.Http
$project = Join-Path $PSScriptRoot '..\Plain Craft Launcher 2'
[void][Reflection.Assembly]::LoadFrom((Join-Path $project 'Resources\Newtonsoft.Json.dll'))
$assembly = [Reflection.Assembly]::LoadFrom((Join-Path $project 'bin\BRCL.exe'))
[Windows.Application]::ResourceAssembly = $assembly
$app = [Activator]::CreateInstance($assembly.GetType('BRCL.Application',$true),$true)
$app.InitializeComponent()
$assembly.GetType('BRCL.ModAnimation').GetProperty('AniControlEnabled').SetValue($null,1,$null)
$type = $assembly.GetType('BRCL.MicrosoftDeviceLogin',$true)
$script:count = 0
function Check($condition, [string]$message) {
    if (-not $condition) { throw $message }
    $script:count++
    Write-Output "PASS $message"
}
function Json([string]$text) { return ,([Newtonsoft.Json.Linq.JObject]::Parse($text)) }
function New-Data { return ,(Json '{"user_code":"ABCD-EFGH","device_code":"offline-test-code","verification_uri":"https://www.microsoft.com/link","expires_in":900,"interval":5}') }
function New-Session($data) { return $type.GetConstructors()[0].Invoke([object[]](,$data)) }
function Rejected([scriptblock]$action) {
    try { & $action | Out-Null; return $false } catch { return $true }
}
$session = New-Session (New-Data)
Check ($session.PollSeconds -eq 5 -and $session.ExpiresSeconds -eq 900) 'server polling interval and expiry are honored'
Check ($session.RemainingSeconds(0) -eq 900 -and $session.RemainingSeconds(899.9) -eq 1 -and $session.RemainingSeconds(900) -eq 0 -and $session.RemainingSeconds(1000) -eq 0) 'expiry boundary and countdown never become negative'
Check ($session.TransientFailure() -and $session.PollSeconds -eq 10) 'first connection failure backs off'
Check ($session.TransientFailure() -and $session.PollSeconds -eq 20) 'second connection failure backs off'
$session.AuthorizationPending()
Check ($session.PollSeconds -eq 5 -and $session.FailureCount -eq 0) 'healthy pending response restores protocol interval'
$session.SlowDown()
$session.SlowDown()
Check ($session.PollSeconds -eq 15) 'repeated slow_down permanently adds five seconds'
[void]$session.TransientFailure()
$session.AuthorizationPending()
Check ($session.PollSeconds -eq 15) 'recovery retains server slow_down requirement'
$retry = New-Session (New-Data)
Check ($retry.TransientFailure() -and $retry.TransientFailure() -and $retry.TransientFailure() -and -not $retry.TransientFailure()) 'consecutive transient failures have a finite retry budget'
$data = New-Data
$data['interval'] = 120
$session = New-Session $data
[void]$session.TransientFailure()
Check ($session.PollSeconds -ge 120) 'backoff cap never polls faster than server interval'
$data = New-Data
[void]$data.Remove('interval')
[void]$data.Remove('expires_in')
$session = New-Session $data
Check ($session.PollSeconds -eq 5 -and $session.ExpiresSeconds -eq 900) 'missing optional timing uses safe defaults'
foreach ($name in @('interval','expires_in')) {
    foreach ($bad in @('0','-1','2147483648','"not-a-number"','null')) {
        $data = New-Data
        $data[$name] = [Newtonsoft.Json.Linq.JToken]::Parse($bad)
        Check (Rejected { New-Session $data }) "invalid $name timing rejected: $bad"
    }
}
foreach ($name in @('device_code','user_code','verification_uri')) {
    $data = New-Data
    [void]$data.Remove($name)
    Check (Rejected { New-Session $data }) "missing $name rejected before opening browser"
}
foreach ($url in @('http://www.microsoft.com/link','file:///C:/test','https://user:pass@example.com/')) {
    $data = New-Data
    $data['verification_uri'] = [Newtonsoft.Json.Linq.JValue]::new($url)
    Check (Rejected { New-Session $data }) 'unsafe verification URL rejected'
}
Check ($type::ErrorCode('{"error":"authorization_pending"}') -eq 'authorization_pending') 'OAuth error parsed from exact JSON field'
Check ($type::ErrorCode('{"error":"invalid_client","error_description":"authorization_pending"}') -eq 'invalid_client') 'description cannot masquerade as pending authorization'
Check ($type::ErrorCode('<html>502</html>') -eq '' -and $type::ErrorCode($null) -eq '') 'gateway and empty responses handled without JSON crash'
$tokens = $type::ReadTokens((Json '{"access_token":"fake-access","refresh_token":"fake-refresh"}'),$null)
Check ($tokens[0] -eq 'fake-access' -and $tokens[1] -eq 'fake-refresh') 'complete token result accepted'
$tokens = $type::ReadTokens((Json '{"access_token":"fake-access"}'),'previous-refresh')
Check ($tokens[1] -eq 'previous-refresh') 'refresh response without rotated refresh token retains previous token'
foreach ($text in @('{}','{"access_token":""}','{"access_token":"fake","refresh_token":null}','{"access_token":"fake","refresh_token":""}')) {
    Check (Rejected { $type::ReadTokens((Json $text),$null) }) 'incomplete token result rejected'
}
$needsAuthorization = $type.GetMethod('RequiresNewAuthorization',[Reflection.BindingFlags]'NonPublic,Static')
Check ($needsAuthorization.Invoke($null,[object[]]@('{"error":"invalid_grant"}'))) 'revoked Microsoft refresh grant requests new authorization'
Check (-not $needsAuthorization.Invoke($null,[object[]]@('{"error":"server_error","error_description":"invalid_grant upstream"}'))) 'transient Microsoft service errors do not discard authorization'
Check (-not $needsAuthorization.Invoke($null,[object[]]@('<html>gateway connection expired</html>'))) 'gateway timeout does not open an unnecessary authorization dialog'
$cacheCheck = $assembly.GetType('BRCL.ModLaunch',$true).GetMethod('CanReuseMicrosoftLogin',[Reflection.BindingFlags]'NonPublic,Static')
$profileId = '12f36facfa9a40c69dd36b187deed134'
$now = [long]1800000000
function Can-ReuseCache($requested,$cached,$access,$uuid,[long]$seconds) {
    return [bool]$cacheCheck.Invoke($null,[object[]]@($requested,$cached,$access,$uuid,($now+$seconds),$now))
}
Check (Can-ReuseCache 'Player' 'Player' 'fixture-access' $profileId 121) 'complete Microsoft cache beyond the renewal window is reused'
Check (-not (Can-ReuseCache 'Player' 'Player' 'fixture-access' $profileId 120)) 'Microsoft cache renews before the access token expires'
Check (-not (Can-ReuseCache 'Other' 'Player' 'fixture-access' $profileId 600)) 'a different account never reuses the Microsoft cache'
Check (-not (Can-ReuseCache 'Player' 'Player' '' $profileId 600)) 'partial Microsoft cache without an access token is refreshed'
Check (-not (Can-ReuseCache 'Player' 'Player' 'fixture-access' 'invalid' 600)) 'partial Microsoft cache without a valid profile ID is refreshed'
$converterType = $assembly.GetType('BRCL.ModMain+MyMsgBoxConverter',$true)
$controlType = $assembly.GetType('BRCL.MyMsgLogin',$true)
function New-Dialog($data) {
    $converter = [Activator]::CreateInstance($converterType)
    $converter.Content = $data
    $control = [Activator]::CreateInstance($controlType,[object[]]@($converter))
    return @{Converter=$converter; Control=$control}
}
$invalid = New-Dialog (Json '{}')
Check ($invalid.Converter.IsExited -and $invalid.Converter.Result -is [Exception]) 'dialog initialization failure releases waiting login caller'
$dialog = New-Dialog (New-Data)
Check (-not $dialog.Converter.IsExited -and -not $controlType.GetField('HasStarted',[Reflection.BindingFlags]'NonPublic,Instance').GetValue($dialog.Control)) 'constructing an offline dialog does not start network or browser'
$finish = $controlType.GetMethod('Finished',[Reflection.BindingFlags]'NonPublic,Instance')
Check ($finish.Invoke($dialog.Control,[object[]]@([OperationCanceledException]::new()))) 'cancellation wins first completion'
$cancelResult = $dialog.Converter.Result
Check (-not $finish.Invoke($dialog.Control,[object[]]@(,[string[]]@('late-access','late-refresh'))) -and [object]::ReferenceEquals($cancelResult,$dialog.Converter.Result)) 'late success cannot overwrite cancellation'
if ($PreviewDirectory) {
    New-Item -ItemType Directory -Path $PreviewDirectory -Force | Out-Null
    foreach ($theme in @('dark','light')) {
        $colors = if ($theme -eq 'dark') { @('#E6E8E3','#B8C0BD','#252B2D','#191F21','#424B49') } else { @('#191D1F','#555F60','#E8EAE1','#F5F5EF','#C6CBC7') }
        $keys = @('ColorBrush1','ColorBrush2','ColorBrush6','ColorBrush7','ColorBrushCardLine')
        for ($i=0;$i -lt $keys.Count;$i++) { $app.Resources[$keys[$i]]=[Windows.Media.BrushConverter]::new().ConvertFromString($colors[$i]) }
        foreach ($width in @(390,600)) {
            $dialog = New-Dialog (New-Data)
            $element = $dialog.Control
            $element.FindName('LabCountdown').Text = '代码有效期 14:59'
            $element.Measure([Windows.Size]::new($width,[double]::PositiveInfinity))
            $height = [Math]::Ceiling($element.DesiredSize.Height)
            $element.Arrange([Windows.Rect]::new(0,0,$width,$height))
            $element.UpdateLayout()
            $bitmap = [Windows.Media.Imaging.RenderTargetBitmap]::new($width,$height,96,96,[Windows.Media.PixelFormats]::Pbgra32)
            $bitmap.Render($element)
            $encoder = [Windows.Media.Imaging.PngBitmapEncoder]::new()
            $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
            $stream = [IO.File]::Create((Join-Path $PreviewDirectory "login-$theme-$width.png"))
            try { $encoder.Save($stream) } finally { $stream.Dispose() }
            foreach ($name in @('Btn1','Btn2','Btn3','LabCode')) {
                $child=$element.FindName($name)
                $origin=$child.TranslatePoint([Windows.Point]::new(0,0),$element)
                Check ($origin.X -ge 0 -and $origin.Y -ge 0 -and ($origin.X+$child.ActualWidth) -le ($width+1) -and ($origin.Y+$child.ActualHeight) -le ($height+1)) "$theme ${width}px $name remains inside dialog"
            }
        }
    }
}
Write-Output "Microsoft login: $script:count checks passed; no live authorization performed."
$app.Shutdown()
