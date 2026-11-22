$ErrorActionPreference='Stop'
Add-Type -AssemblyName PresentationFramework,PresentationCore,WindowsBase,System.Net.Http
$project=Join-Path $PSScriptRoot '..\Plain Craft Launcher 2'
$assembly=[Reflection.Assembly]::LoadFrom((Join-Path $project 'bin\BRCL.exe'))
[Windows.Application]::ResourceAssembly=$assembly
$app=[Activator]::CreateInstance($assembly.GetType('BRCL.Application',$true),$true)
$app.InitializeComponent()
$flags=[Reflection.BindingFlags]'NonPublic,Instance'
foreach ($case in @(@('BRCL.PageStock','ShowFail','NavigationVersion'),@('BRCL.PageMap','ShowUnavailable','ConnectionVersion'))) {
    $type=$assembly.GetType($case[0],$true)
    $page=[Activator]::CreateInstance($type)
    $navigation=$type.GetField('ActiveNavigationId',$flags)
    $generation=$type.GetField($case[2],$flags)
    $navigation.SetValue($page,[UInt64]42)
    $generation.SetValue($page,5)
    $method=$type.GetMethod($case[1],$flags)
    $arguments=if ($case[1] -eq 'ShowFail') {,[object[]]@('Offline test')} else {,[object[]]@('Offline test','Offline')}
    [void]$method.Invoke($page,$arguments)
    if ($navigation.GetValue($page) -ne 0 -or $generation.GetValue($page) -ne 6) {throw 'Failed navigation still active'}
    Write-Output "PASS $($case[0]) failure invalidates old navigation and async generation"
    $view=$page.FindName($(if ($case[1] -eq 'ShowFail') {'ViewWeb'} else {'ViewMap'}))
    if ($view.Visibility -ne [Windows.Visibility]::Hidden) {throw 'Failed webview still visible'}
    Write-Output "PASS $($case[0]) failure hides webview and keeps recovery state"
    [void]$method.Invoke($page,$arguments)
    if ($navigation.GetValue($page) -ne 0) {throw 'Repeated failure restored old navigation'}
    Write-Output "PASS $($case[0]) repeated failure is safe without initialized browser"
}
$app.Shutdown()
