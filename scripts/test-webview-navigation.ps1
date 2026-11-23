# Embedded browser navigation policy: the exchange and map views may only navigate within their own site.
param([string]$OutputDirectory = 'bin')
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationFramework,PresentationCore,WindowsBase,System.Net.Http
$project = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\Plain Craft Launcher 2'))
[void][Reflection.Assembly]::LoadFrom((Join-Path $project 'Resources\Newtonsoft.Json.dll'))
$assembly = [Reflection.Assembly]::LoadFrom((Join-Path $project "$OutputDirectory\BRCL.exe"))
[Windows.Application]::ResourceAssembly = $assembly
$app = [Activator]::CreateInstance($assembly.GetType('BRCL.Application', $true), $true)
$app.InitializeComponent()
$assembly.GetType('BRCL.ModAnimation').GetProperty('AniControlEnabled').SetValue($null, 1, $null)
$flags = [Reflection.BindingFlags]'NonPublic,Public,Static,Instance'
function Require([bool]$value, [string]$message) { if (-not $value) { throw $message } }

$stockType = $assembly.GetType('BRCL.PageStock', $true)
$stock = [Activator]::CreateInstance($stockType)
$isStock = $stockType.GetMethod('IsStockSource', $flags)
$previous = $env:BRCL_STOCK_URL
try {
    $env:BRCL_STOCK_URL = $null
    foreach ($url in @('https://zhushu.homes/stock/', 'https://zhushu.homes/stock/login', 'https://zhushu.homes/stock/wallet#formCard', 'https://ZHUSHU.homes/stock/account?tab=1')) {
        Require ([bool]$isStock.Invoke($stock, @($url))) "Exchange page was blocked: $url"
    }
    foreach ($url in @('https://zhushu.homes/BRS/', 'https://zhushu.homes/stockx/', 'http://zhushu.homes/stock/', 'https://zhushu.homes.evil.example/stock/', 'https://evil.example/stock/', 'file:///C:/Windows/win.ini', 'javascript:alert(1)', 'not a url')) {
        Require (-not [bool]$isStock.Invoke($stock, @($url))) "Foreign exchange navigation was allowed: $url"
    }
    Write-Output 'PASS exchange view stays on https://zhushu.homes/stock/ and leaves other sites to the browser'
    $env:BRCL_STOCK_URL = 'http://127.0.0.1:8123/stock/'
    Require ([bool]$isStock.Invoke($stock, @('http://127.0.0.1:8123/stock/login'))) 'Offline test origin was blocked'
    Require (-not [bool]$isStock.Invoke($stock, @('http://127.0.0.1:9999/stock/'))) 'A different test port was allowed'
    Write-Output 'PASS offline test origin is honoured only for its exact origin'
} finally { $env:BRCL_STOCK_URL = $previous }

$mapType = $assembly.GetType('BRCL.PageMap', $true)
$isMap = $mapType.GetMethod('IsMapSource', $flags)
foreach ($url in @('http://t42.sjcmc.cn:25277/', 'http://t42.sjcmc.cn:25277/#world:0:64:0', 'http://T42.SJCMC.CN:25277/tiles/a.png')) {
    Require ([bool]$isMap.Invoke($null, @($url))) "Map page was blocked: $url"
}
foreach ($url in @('http://t42.sjcmc.cn:25278/', 'http://t42.sjcmc.cn/', 'https://t42.sjcmc.cn:25277/', 'http://evil.example/', 'http://t42.sjcmc.cn.evil.example:25277/')) {
    Require (-not [bool]$isMap.Invoke($null, @($url))) "Foreign map navigation was allowed: $url"
}
Write-Output 'PASS map view stays on its own origin'

$runtime = $assembly.GetType('BRCL.BrclWebViewRuntime', $true)
Require ($null -ne $runtime.GetMethod('RedirectForeignNavigation', $flags)) 'Shared navigation guard is missing'
function Get-CalledMethods([Reflection.MethodInfo]$method) {
    # Resolve every call/callvirt operand in the handler body; a skipped guard would not appear here.
    $il = $method.GetMethodBody().GetILAsByteArray()
    $names = New-Object 'Collections.Generic.HashSet[string]'
    for ($i = 0; $i -le $il.Length - 5; $i++) {
        if ($il[$i] -ne 0x28 -and $il[$i] -ne 0x6F) { continue }
        try { $callee = $method.Module.ResolveMethod([BitConverter]::ToInt32($il, $i + 1)); [void]$names.Add($callee.DeclaringType.Name + '.' + $callee.Name) } catch { }
    }
    return $names
}
foreach ($page in @(@{ Type = $stockType; Handler = 'OnNavigationStarting' }, @{ Type = $mapType; Handler = 'NavigationStarting' })) {
    $calls = Get-CalledMethods $page.Type.GetMethod($page.Handler, $flags)
    Require ($calls.Contains('BrclWebViewRuntime.RedirectForeignNavigation')) "$($page.Type.Name).$($page.Handler) does not apply the navigation guard"
}
Write-Output 'PASS both embedded views route navigations through the shared guard'
Write-Output '4 embedded navigation checks passed.'
