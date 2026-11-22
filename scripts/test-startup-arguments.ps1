param([string]$BuildDirectory)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationFramework,PresentationCore,WindowsBase,System.Net.Http
$project = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\Plain Craft Launcher 2'))
if (-not $BuildDirectory) { $BuildDirectory = Join-Path $project 'bin' }
$assembly = [Reflection.Assembly]::LoadFrom((Join-Path $BuildDirectory 'BRCL.exe'))
$validate = $assembly.GetType('BRCL.Application',$true).GetMethod('HasValidStartupArguments',[Reflection.BindingFlags]'NonPublic,Static')
if ($null -eq $validate) { throw 'Startup argument validator is missing' }
$cases = @(
    @{Args=[string[]]@(); Valid=$true},
    @{Args=[string[]]@('--wait'); Valid=$true},
    @{Args=[string[]]@('--memory'); Valid=$true},
    @{Args=[string[]]@('--update'); Valid=$false},
    @{Args=[string[]]@('--update','42','old.exe','new.exe'); Valid=$false},
    @{Args=[string[]]@('--update','oops','old.exe','new.exe','True'); Valid=$false},
    @{Args=[string[]]@('--update','0','old.exe','new.exe','True'); Valid=$false},
    @{Args=[string[]]@('--update','42','""','new.exe','True'); Valid=$false},
    @{Args=[string[]]@('--update','42','old.exe','new.exe','oops'); Valid=$false},
    @{Args=[string[]]@('--update','42','old.exe','new.exe','True'); Valid=$true},
    @{Args=[string[]]@('--update','42','old.exe','new.exe','False'); Valid=$true},
    @{Args=[string[]]@('--gpu'); Valid=$false},
    @{Args=[string[]]@('--gpu',''); Valid=$false},
    @{Args=[string[]]@('--gpu','""'); Valid=$false},
    @{Args=[string[]]@('--gpu','C:\Program Files\Java\javaw.exe'); Valid=$true}
)
foreach ($case in $cases) {
    $actual = $validate.Invoke($null,[object[]](,$case.Args))
    if ($actual -ne $case.Valid) { throw ('Wrong startup validation for '+($case.Args -join ' ')) }
}
Write-Output 'PASS missing, empty and malformed update/GPU arguments fail before command execution'
Write-Output 'PASS valid update/GPU commands and ordinary wait/memory startup remain accepted'
Write-Output 'PASS validator checks run without starting the application or writing registry/configuration'
