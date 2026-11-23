# Bank service address policy: an override must never send the player's Minecraft token to a third party.
param([string]$OutputDirectory = 'bin')
$ErrorActionPreference = 'Stop'
$project = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\Plain Craft Launcher 2'))
[void][Reflection.Assembly]::LoadFrom((Join-Path $project 'Resources\Newtonsoft.Json.dll'))
$assembly = [Reflection.Assembly]::LoadFrom((Join-Path $project "$OutputDirectory\BRCL.exe"))
$module = $assembly.GetType('BRCL.BrclBank', $true)
$validate = $module.GetMethod('ValidateBankAddress')
$trusted = $module.GetMethod('IsTrustedBankAddress')
function Require([bool]$value, [string]$message) { if (-not $value) { throw $message } }
function Trust([string]$url) { return [bool]$trusted.Invoke($null, @($validate.Invoke($null, @($url)))) }

foreach ($url in @('https://zhushu.homes/bank/v1', 'https://ZHUSHU.HOMES/bank/v2', 'http://127.0.0.1:8786/bank/v1', 'https://localhost/bank/v1', 'http://[::1]:8786/bank/v1')) {
    Require (Trust $url) "Trusted bank address was rejected: $url"
}
Write-Output 'PASS official HTTPS host and loopback development addresses are accepted'

foreach ($url in @('https://evil.example/bank/v1', 'https://zhushu.homes.evil.example/bank/v1', 'https://evilzhushu.homes/bank/v1', 'https://zhushu.homes:8443/bank/v1', 'https://xn--zhushu-9ra.homes/bank/v1')) {
    Require (-not (Trust $url)) "Untrusted bank address was accepted: $url"
}
Require (-not [bool]$trusted.Invoke($null, @($null))) 'A missing bank address was trusted'
Write-Output 'PASS third-party hosts, look-alike hosts and non-default ports are rejected'

$previous = $env:BRCL_BANK_SERVICE_URL
try {
    $env:BRCL_BANK_SERVICE_URL = 'https://evil.example/bank/v1'
    $rejected = $false
    try { $null = $module.GetMethod('BankServiceAddress').Invoke($null, @()) } catch { $rejected = $_.Exception.InnerException.Code -eq 'untrusted_service_address' }
    Require $rejected 'An untrusted override was not refused by BankServiceAddress'
    $env:BRCL_BANK_SERVICE_URL = 'http://127.0.0.1:8786/bank/v1'
    Require ($module.GetMethod('BankServiceAddress').Invoke($null, @()).AbsoluteUri -eq 'http://127.0.0.1:8786/bank/v1/') 'Loopback override was not applied'
} finally { $env:BRCL_BANK_SERVICE_URL = $previous }
Write-Output 'PASS overrides are filtered before any request is made'
Write-Output '3 bank address checks passed.'
