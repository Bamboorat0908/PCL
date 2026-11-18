$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationFramework,PresentationCore,WindowsBase
$project = Join-Path $PSScriptRoot '..\Plain Craft Launcher 2'
[void][Reflection.Assembly]::LoadFrom((Join-Path $project 'Resources\Newtonsoft.Json.dll'))
[void][Reflection.Assembly]::LoadFrom((Join-Path $project 'Resources\ThrottleDebounce.dll'))
$assembly = [Reflection.Assembly]::LoadFrom((Join-Path $project 'bin\BRCL.exe'))
[Windows.Application]::ResourceAssembly = $assembly
$app = [Activator]::CreateInstance($assembly.GetType('PCL.Application',$true),$true)
$app.InitializeComponent()
$generic = $assembly.GetType('PCL.ModLoader+LoaderTask`2',$true).MakeGenericType([object],[object])
$inputField = $generic.GetField('InputDelegate',[Reflection.BindingFlags]'NonPublic,Instance')
function Check($condition, [string]$message) {
    if (-not $condition) { throw $message }
    Write-Output "PASS $message"
}
try {
    $loader = [Activator]::CreateInstance($generic)
    $inputField.SetValue($loader, [Func[object]]{ throw [InvalidOperationException]::new('fixture-input-failure') })
    $arguments = [object[]]@($null, $true, $false)
    $shouldStart = $generic.GetMethod('ShouldStart').Invoke($loader, $arguments)
    Check (-not $shouldStart) 'input failure prevents even a forced task start'
    Check ($loader.State.ToString() -eq 'Failed' -and $loader.Error.ToString().Contains('fixture-input-failure')) 'input failure preserves the original failure state and exception'
    $generic.GetMethod('Start').Invoke($loader, [object[]]@($null, $true))
    Check ($null -eq $loader.LastRunningThread -and $loader.State.ToString() -eq 'Failed') 'input failure creates no worker thread'
    $inputField.SetValue($loader, [Func[object]]{ return 'recovered-input' })
    $arguments = [object[]]@($null, $false, $false)
    $shouldStart = $generic.GetMethod('ShouldStart').Invoke($loader, $arguments)
    Check ($shouldStart -and $arguments[0] -eq 'recovered-input') 'a later successful input read can retry the failed task'
} finally { $app.Shutdown() }
