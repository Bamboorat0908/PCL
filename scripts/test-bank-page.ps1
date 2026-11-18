# Native banking integration, privacy and visual QA. Windows PowerShell 5.1 / STA.
param([string]$PreviewDirectory = 'C:\works\BRCL\bank-20261118-checks', [string]$BuildDirectory)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationFramework,PresentationCore,WindowsBase,System.Net.Http,System.Numerics
$project = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\Plain Craft Launcher 2'))
[void][Reflection.Assembly]::LoadFrom((Join-Path $project 'Resources\Newtonsoft.Json.dll'))
if (-not $BuildDirectory) { $BuildDirectory = Join-Path $project 'bin' }
$assembly = [Reflection.Assembly]::LoadFrom((Join-Path $BuildDirectory 'BRCL.exe'))
[Windows.Application]::ResourceAssembly = $assembly
$app = [Activator]::CreateInstance($assembly.GetType('PCL.Application', $true), $true)
$app.InitializeComponent()
$assembly.GetType('PCL.ModAnimation').GetProperty('AniControlEnabled').SetValue($null, 1, $null)
$module = $assembly.GetType('PCL.BrclBank', $true)
$type = $assembly.GetType('PCL.PageBank', $true)
$flags = [Reflection.BindingFlags]'NonPublic,Instance'
$page = [Activator]::CreateInstance($type)
$player = '11111111-1111-1111-1111-111111111111'
$account = '22222222-2222-2222-2222-222222222222'
$now = [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()
$fixture = @{
    currency = 'sdmcoin'; unit = 'copper'; capturedAt = $now
    accounts = @(
        @{id=$account; type='team'; nativeType='BLAZE_BANKER'; label='曙光工业联合体'; balance='1286550'; visibleBalance='1286550'; reserveBalance='0'; grants=@(@{principalId=$player;role='trusted'},@{principalId='33333333-3333-3333-3333-333333333333';role='trusted'})},
        @{id='44444444-4444-4444-4444-444444444444';type='team';nativeType='BLAZE_BANKER';label='铁路建设基金';balance='78500';visibleBalance='78500';reserveBalance='0';grants=@(@{principalId=$player;role='trusted'})}
    )
}
function Parse-Accounts($data) {
    $json = [Newtonsoft.Json.Linq.JObject]::Parse(($data | ConvertTo-Json -Depth 12 -Compress))
    return $module.GetMethod('ParseBankAccounts').Invoke($null, [object[]]@($json,$player))
}
function Invoke-Page([string]$method, [object[]]$arguments = @()) {
    return $type.GetMethod($method, $flags).Invoke($page, $arguments)
}
function Require([bool]$value, [string]$message) { if (-not $value) { throw $message } }
function Assert-Rejected($data) {
    try { $null = Parse-Accounts $data } catch { return }
    throw 'Invalid or unauthorized bank account was accepted'
}
$data = Parse-Accounts $fixture
Require ($data.Accounts.Count -eq 2) 'Team accounts were lost'
Invoke-Page 'ApplyAccounts' @($data)
Require ($page.FindName('LabBalance').Text -eq '1,286,550') 'Initial balance was not rendered'
Require ($page.FindName('PanAccountChoices').Children.Count -eq 2) 'Multiple accounts have no visible list'
Require ($page.FindName('PanDetails').Visibility -eq 'Collapsed') 'Technical account details are expanded by default'
$page.FindName('PanAccountChoices').Children[1].RaiseEvent([Windows.RoutedEventArgs]::new([Windows.Controls.Primitives.ButtonBase]::ClickEvent))
Require ($page.FindName('LabBalance').Text -eq '78,500') 'Account switching retained previous balance'
$page.FindName('PanAccountChoices').Children[0].RaiseEvent([Windows.RoutedEventArgs]::new([Windows.Controls.Primitives.ButtonBase]::ClickEvent))
Write-Output 'PASS account discovery, direct grants and account switching'

# Integer precision and authorization are data boundaries, not UI-only formatting checks.
$large = ($fixture | ConvertTo-Json -Depth 12 | ConvertFrom-Json)
$large.accounts[0].balance = '9007199254740993'
$large.accounts[0].visibleBalance = '1073741823'
$large.accounts[0].reserveBalance = '9007198180999170'
$parsed = Parse-Accounts $large
Require ($parsed.Accounts[0].Balance.ToString() -eq '9007199254740993') 'JS-unsafe integer precision was lost'
foreach ($badValue in @('01','1e9','-1',100,'1.5','+1')) {
    $bad = ($fixture | ConvertTo-Json -Depth 12 | ConvertFrom-Json)
    $bad.accounts[0].balance = $badValue
    Assert-Rejected $bad
}
$bad = ($fixture | ConvertTo-Json -Depth 12 | ConvertFrom-Json)
$bad.accounts[0].grants = @(@{principalId='99999999-9999-9999-9999-999999999999';role='trusted'})
Assert-Rejected $bad
$bad = ($fixture | ConvertTo-Json -Depth 12 | ConvertFrom-Json)
$bad.accounts[0].reserveBalance = '1'
Assert-Rejected $bad
$bad.accounts[0].reserveBalance = '0'
$bad.accounts[0].type = 'personal'
Assert-Rejected $bad
foreach ($url in @('http://example.com/bank/v1','https://user:pass@example.com/bank/v1','https://example.com/bank/v1?token=secret','https://example.com/bank/v1#fragment')) {
    $rejected = $false
    try { $null = $module.GetMethod('ValidateBankAddress').Invoke($null,@($url)) } catch { $rejected = $true }
    Require $rejected 'Insecure service address was accepted'
}
Write-Output 'PASS arbitrary precision, money/schema invariants, identity isolation and secure service URLs'

$history = @{accountId=$account;currency='sdmcoin';unit='copper';source='observed_snapshots';points=@()}
$values = @(890000,1040000,980000,1120000,1080000,1210000,1190000,1286550)
for($i=0;$i -lt $values.Count;$i++) { $history.points += @{capturedAt=($now - (7-$i)*86400000);balance=[string]$values[$i]} }
function Set-History {
    $json = [Newtonsoft.Json.Linq.JObject]::Parse(($history | ConvertTo-Json -Depth 10 -Compress))
    $points = $module.GetMethod('ParseBankHistory').Invoke($null,[object[]]@($json,$account))
    $type.GetField('Points',$flags).SetValue($page,$points)
    Invoke-Page 'RenderHistory'
}
Set-History
Require ($page.FindName('LabChange').Text -eq '+396,550') 'Sampled net change is incorrect'
Invoke-Page 'Privacy_Click'
foreach ($name in @('LabBalance','LabVisible','LabReserve','LabChange','LabHigh','LabLow')) {
    Require ($page.FindName($name).Text -eq '••••••') "Privacy exposed $name"
}
Require ($page.FindName('LabChartEmpty').Text -eq '金额已隐藏') 'Privacy left the trend visible'
foreach($row in $page.FindName('PanAccountChoices').Children) {
    Require ($row.Content.Children[1].Children[0].Text -eq '••••••') 'Privacy exposed an account list balance'
}
Invoke-Page 'Privacy_Click'
Require ($page.FindName('LabBalance').Text -eq '1,286,550') 'Unhiding did not restore the amount'
Invoke-Page 'ApplyFailure' @('unauthorized')
Require ($page.FindName('PanAccountChoices').Children.Count -eq 0) 'Authorization failure retained account selection'
Require ($page.FindName('LabAccountId').Text -eq '') 'Authorization failure retained an account identifier'
Require ($page.FindName('LabBalance').Text -eq '— —') 'Authorization failure retained a balance'
Require ($page.FindName('CardBalance').Visibility -eq 'Collapsed') 'Authorization failure left an empty balance card'
Require (-not $page.FindName('TabAnalysis').IsEnabled -and -not $page.FindName('TabTransactions').IsEnabled) 'Account-only views remain enabled without an account'
Require ($type.GetField('Points',$flags).GetValue($page).Count -eq 0) 'Authorization failure retained history'
Write-Output 'PASS amount and chart privacy, precise sampled analytics and failure data clearing'

function Set-Theme([string]$theme) {
    $colors=if($theme -eq 'dark'){@('#F1F2E9','#B3BAB9','#F2D649','#272C2E','#1C2022','#111416','#374041')}else{@('#191D1F','#555F60','#745900','#E8EAE1','#F5F5EF','#E7E8E3','#C6CBC7')}
    $keys=@('ColorBrush1','ColorBrush2','ColorBrush3','ColorBrush6','ColorBrush7','ColorBrush8','ColorBrushCardLine')
    for($i=0;$i -lt $keys.Count;$i++){$app.Resources[$keys[$i]]=[Windows.Media.BrushConverter]::new().ConvertFromString($colors[$i])}
}
function Render-Page([int]$width,[string]$name) {
    $height = 1050
    $border = [Windows.Controls.Border]::new()
    $border.Background = $app.Resources['ColorBrush8']
    $border.Child = $page
    $border.Measure([Windows.Size]::new($width,$height))
    $border.Arrange([Windows.Rect]::new(0,0,$width,$height))
    $border.UpdateLayout()
    $border.Measure([Windows.Size]::new($width,$height))
    $border.Arrange([Windows.Rect]::new(0,0,$width,$height))
    $border.UpdateLayout()
    $scroll = $page.FindName('ScrollMain')
    Require ($scroll.ExtentWidth -le $scroll.ViewportWidth+1) "Bank page scrolls horizontally at $width"
    foreach ($nameToCheck in @('BtnRefresh','BtnPrivacy','TabAccount','TabTransactions','TabAnalysis','BtnDays90','BtnCopy')) {
        $control = $page.FindName($nameToCheck)
        if ($control.IsVisible -and $control.ActualWidth -gt 0) {
            $origin = $control.TranslatePoint([Windows.Point]::new(0,0), $page)
            Require ($origin.X -ge -1 -and $origin.X+$control.ActualWidth -le $width+1) "$nameToCheck clipped at $width"
        }
    }
    $bitmap=[Windows.Media.Imaging.RenderTargetBitmap]::new($width,$height,96,96,[Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($border)
    $encoder=[Windows.Media.Imaging.PngBitmapEncoder]::new()
    $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $stream=[IO.File]::Create((Join-Path $PreviewDirectory ($name+'.png')))
    try{$encoder.Save($stream)}finally{$stream.Dispose()}
    $border.Child = $null
    Write-Output "RENDER $name"
}
New-Item -ItemType Directory -Force -Path $PreviewDirectory | Out-Null
foreach($theme in @('dark','light')) {
    Set-Theme $theme
    Invoke-Page 'ApplyAccounts' @($data)
    Set-History
    $page.FindName('LabWelcome').Text = '界面验证 · 示例数据，非真实银行账户'
    foreach($tab in @('account','transactions','analysis')) {
        $type.GetField('SelectedTab',$flags).SetValue($page,$tab)
        Invoke-Page 'UpdateTabs'
        foreach($width in @(380,830)) { Render-Page $width "bank-$tab-$theme-$width" }
    }
    Invoke-Page 'ClearData'
    Invoke-Page 'ShowNotice' @('登录后，自动关联你的团队银行','使用启动器的微软正版账号登录，即可查看你获得直接授权的团队账户。',$true)
    $type.GetField('SelectedTab',$flags).SetValue($page,'account')
    Invoke-Page 'UpdateTabs'
    Render-Page 830 "bank-login-$theme"
}
Require ($app.Windows.Count -eq 0 -and -not $page.IsLoaded) 'Offscreen checks unexpectedly mounted a live bank page'
Write-Output 'PASS dark/light and narrow/wide native layouts without live account access'
$app.Shutdown()
