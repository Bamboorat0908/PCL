# Run with Windows PowerShell 5.1 against the built .NET Framework assembly.
param([string]$OutputDirectory = 'bin')
$ErrorActionPreference = 'Stop'
$projectRoot = Join-Path $PSScriptRoot '..\Plain Craft Launcher 2'
Add-Type -AssemblyName PresentationFramework
Add-Type -AssemblyName System.Net.Http
[void][Reflection.Assembly]::LoadFrom((Join-Path $projectRoot 'Resources\Newtonsoft.Json.dll'))
$assembly = [Reflection.Assembly]::LoadFrom((Join-Path $projectRoot "$OutputDirectory\BRCL.exe"))
$page = $assembly.GetType('PCL.PageLaunchRight', $true)
$validate = $page.GetMethod('ValidateHomeData', [Reflection.BindingFlags]'NonPublic, Static')
if ($null -eq $validate) { throw 'Home validation entry point missing' }
$valid = '{"server":{"pack":"TRM","season_label":"Season 9"},"season":{"day":23},"status":{"alive":true,"player_count":0,"max_players":100}}'
$cases = @(
    @{ Name='live metadata with zero players'; Json=$valid; Valid=$true },
    @{ Name='offline with optional counts missing'; Json='{"server":{"pack":"TRM","season_label":"Season 9"},"status":{"alive":false}}'; Valid=$true },
    @{ Name='missing alive'; Json=$valid.Replace('"alive":true,',''); Valid=$false },
    @{ Name='string alive'; Json=$valid.Replace('"alive":true','"alive":"yes"'); Valid=$false },
    @{ Name='negative count'; Json=$valid.Replace('"player_count":0','"player_count":-1'); Valid=$false },
    @{ Name='fractional day'; Json=$valid.Replace('"day":23','"day":1.5'); Valid=$false },
    @{ Name='overflow count'; Json=$valid.Replace('"player_count":0','"player_count":2147483648'); Valid=$false },
    @{ Name='missing pack'; Json=$valid.Replace('"pack":"TRM",',''); Valid=$false },
    @{ Name='blank season'; Json=$valid.Replace('"Season 9"','" "'); Valid=$false },
    @{ Name='empty response'; Json='{}'; Valid=$false }
)
foreach ($case in $cases) {
    $accepted = $true
    try { [void]$validate.Invoke($null, [object[]](,[Newtonsoft.Json.Linq.JObject]::Parse($case.Json))) }
    catch {
        $failure = $_.Exception
        while ($failure.InnerException) { $failure = $failure.InnerException }
        if ($failure -isnot [FormatException]) { throw }
        $accepted = $false
    }
    if ($accepted -ne $case.Valid) { throw "Unexpected home result: $($case.Name)" }
    Write-Output "PASS $($case.Name)"
}
Write-Output "$($cases.Count) home metadata regression cases passed."
