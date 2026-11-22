param([string]$BuildDirectory)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationFramework,PresentationCore,WindowsBase,System.Net.Http
$project=Join-Path $PSScriptRoot '..\Plain Craft Launcher 2'
if (-not $BuildDirectory) { $BuildDirectory = Join-Path $project 'bin' }
$assembly=[Reflection.Assembly]::LoadFrom((Join-Path $BuildDirectory 'BRCL.exe'))
[Windows.Application]::ResourceAssembly=$assembly
$app=[Activator]::CreateInstance($assembly.GetType('BRCL.Application',$true),$true)
$app.InitializeComponent()
$assembly.GetType('BRCL.ModAnimation').GetProperty('AniControlEnabled').SetValue($null,1,$null)
$type=$assembly.GetType('BRCL.MyButton',$true)
$button=[Activator]::CreateInstance($type)
$button.Text='Keyboard test'
$script:clicks=0
$button.add_Click({param($sender,$eventArgs) if ($null -eq $eventArgs) {throw 'Missing compatibility event args'}; $script:clicks++})
$down=$type.GetMethod('Button_KeyDown',[Reflection.BindingFlags]'NonPublic,Instance')
$up=$type.GetMethod('Button_KeyUp',[Reflection.BindingFlags]'NonPublic,Instance')
$cancel=$type.GetMethod('Button_CancelPress',[Reflection.BindingFlags]'NonPublic,Instance')
$source=[Windows.Interop.HwndSource]::new([Windows.Interop.HwndSourceParameters]::new('BRCL isolated keyboard test'))
function Key-Event($key) { return [Windows.Input.KeyEventArgs]::new([Windows.Input.Keyboard]::PrimaryDevice,$source,0,$key) }
function Press($method,$key) {
    $eventArgs=Key-Event $key
    $eventArgs.RoutedEvent=if ($method -eq $down) {[Windows.Input.Keyboard]::KeyDownEvent} else {[Windows.Input.Keyboard]::KeyUpEvent}
    [void]$method.Invoke($button,[object[]]@($button,$eventArgs))
    if (-not $eventArgs.Handled) {throw 'Activation key leaked to main window'}
}
foreach ($key in @([Windows.Input.Key]::Space,[Windows.Input.Key]::Enter)) {
    $before=$script:clicks
    Press $down $key
    Press $down $key
    if ($script:clicks -ne $before) {throw 'Button activated before key release'}
    Press $up $key
    Press $up $key
    if ($script:clicks -ne ($before+1)) {throw 'Duplicate or missing keyboard activation'}
    Write-Output "PASS $key activates exactly once and consumes window shortcut"
}
$before=$script:clicks
Press $down ([Windows.Input.Key]::Space)
[void]$cancel.Invoke($button,@())
Press $up ([Windows.Input.Key]::Space)
if ($script:clicks -ne $before) {throw 'Focus loss still activated button'}
Write-Output 'PASS focus loss cancels pending key activation'
Press $down ([Windows.Input.Key]::Enter)
$button.IsEnabled=$false
Press $up ([Windows.Input.Key]::Enter)
if ($script:clicks -ne $before) {throw 'Disabled button activated'}
Write-Output 'PASS disabling button cancels pending activation'
$button.IsEnabled=$true
Press $down ([Windows.Input.Key]::Space)
Press $up ([Windows.Input.Key]::Enter)
if ($script:clicks -ne $before) {throw 'Mismatched key release activated button'}
[void]$cancel.Invoke($button,@())
Write-Output 'PASS mismatched key releases do not activate button'
$scrollHost = [Windows.Controls.ScrollViewer]::new()
$scrollHost.Content = $button
$source.RootVisual = $scrollHost
$scrollHost.Measure([Windows.Size]::new(300,100))
$scrollHost.Arrange([Windows.Rect]::new(0,0,300,100))
$button.Focus() | Out-Null
$before = $script:clicks
$pointerDown = [Windows.Input.MouseButtonEventArgs]::new([Windows.Input.Mouse]::PrimaryDevice,0,[Windows.Input.MouseButton]::Left)
$pointerDown.RoutedEvent = [Windows.Input.Mouse]::MouseDownEvent
$button.RaiseEvent($pointerDown)
if (-not $pointerDown.Handled) { throw 'Mouse down escaped to the scrolling container' }
$pointerUp = [Windows.Input.MouseButtonEventArgs]::new([Windows.Input.Mouse]::PrimaryDevice,0,[Windows.Input.MouseButton]::Left)
$pointerUp.RoutedEvent = [Windows.Input.Mouse]::MouseUpEvent
$button.RaiseEvent($pointerUp)
if ($script:clicks -ne $before+1) { throw 'Button in scrolling container lost its pointer activation' }
Write-Output 'PASS pointer click inside a scrolling container is consumed and activates once'
$source.Dispose()
$app.Shutdown()
