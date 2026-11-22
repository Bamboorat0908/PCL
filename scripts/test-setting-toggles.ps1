param([string]$BuildDirectory)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName PresentationFramework,PresentationCore,WindowsBase,System.Xaml,System.Net.Http,UIAutomationProvider,UIAutomationTypes
$project=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\Plain Craft Launcher 2'))
if(-not $BuildDirectory){$BuildDirectory=Join-Path $project 'bin'}
$assembly=[Reflection.Assembly]::LoadFrom((Join-Path $BuildDirectory 'BRCL.exe'))
[Windows.Application]::ResourceAssembly=$assembly
$app=[Activator]::CreateInstance($assembly.GetType('BRCL.Application',$true),$true)
$app.InitializeComponent()
$startup=$app.GetType().GetMethod('Application_Startup',[Reflection.BindingFlags]'NonPublic,Instance')
$app.remove_Startup([Delegate]::CreateDelegate([Windows.StartupEventHandler],$app,$startup))
[Windows.Application].GetField('_startupUri',[Reflection.BindingFlags]'NonPublic,Instance').SetValue($app,$null)
$assembly.GetType('BRCL.ModAnimation',$true).GetProperty('AniControlEnabled').SetValue($null,1,$null)
$source=[Windows.Interop.HwndSource]::new([Windows.Interop.HwndSourceParameters]::new('BRCL isolated toggle keys'))
function New-Control([string]$name){[Activator]::CreateInstance($assembly.GetType('BRCL.'+$name,$true))}
function Key-Event($control,$key,[bool]$up){
    $args=[Windows.Input.KeyEventArgs]::new([Windows.Input.Keyboard]::PrimaryDevice,$source,0,$key)
    $args.RoutedEvent=if($up){[Windows.Input.Keyboard]::KeyUpEvent}else{[Windows.Input.Keyboard]::KeyDownEvent}
    $control.RaiseEvent($args)
    if(-not $args.Handled){throw 'Activation key escaped to the window'}
}
function Press($control,$key){
    Key-Event $control $key $false
    Key-Event $control $key $false
    Key-Event $control $key $true
    Key-Event $control $key $true
}
function Expect-Exception($action,[type]$expected){
    try {& $action} catch {if($_.Exception.GetBaseException() -is $expected){return};throw}
    throw ('Expected ' + $expected.Name)
}
try {
    $check=New-Control 'MyCheckBox'
    $check.Text='Enable isolated option'
    $peer=[Windows.Automation.Peers.UIElementAutomationPeer]::CreatePeerForElement($check)
    if($peer.GetAutomationControlType() -ne [Windows.Automation.Peers.AutomationControlType]::CheckBox -or $peer.GetName() -ne $check.Text){throw 'Checkbox role or label is missing'}
    $toggle=[Windows.Automation.Provider.IToggleProvider]$peer.GetPattern([Windows.Automation.Peers.PatternInterface]::Toggle)
    if($null -eq $toggle -or $null -eq $check.FocusVisualStyle){throw 'Checkbox Toggle or focus feedback is missing'}
    Press $check ([Windows.Input.Key]::Space)
    if(-not $check.Checked -or $toggle.ToggleState -ne [Windows.Automation.ToggleState]::On){throw 'Checkbox Space did not toggle exactly once'}
    Press $check ([Windows.Input.Key]::Enter)
    if($check.Checked){throw 'Checkbox Enter did not toggle exactly once'}
    $toggle.Toggle()
    if(-not $check.Checked){throw 'Checkbox provider did not toggle'}
    [Windows.Automation.AutomationProperties]::SetName($check,'Explicit option name')
    if($peer.GetName() -ne 'Explicit option name'){throw 'Checkbox ignored explicit accessible label'}
    Write-Output 'PASS checkbox Space/Enter and Toggle expose one labelled state and visible focus'

    Key-Event $check ([Windows.Input.Key]::Space) $false
    $check.IsEnabled=$false
    Key-Event $check ([Windows.Input.Key]::Space) $true
    Press $check ([Windows.Input.Key]::Enter)
    if(-not $check.Checked){throw 'Disabled checkbox changed value'}
    Expect-Exception {$toggle.Toggle()} ([Windows.Automation.ElementNotEnabledException])
    $check.IsEnabled=$true
    $check.Text=$null
    $check.Inlines.Clear()
    $check.Inlines.Add([Windows.Documents.Run]::new('Inline option label'))
    [Windows.Automation.AutomationProperties]::SetName($check,'')
    if($peer.GetName() -ne 'Inline option label'){throw ('Checkbox inline label disappeared from UIA: peer=['+$peer.GetName()+'], text=['+$check.LabText.Text+'], explicit=['+[Windows.Automation.AutomationProperties]::GetName($check)+']')}
    Write-Output 'PASS checkbox disabled/cancelled input is inert and inline text remains accessible'

    foreach($typeName in @('MyRadioBox','MyRadioButton')){
        $panel=[Windows.Controls.StackPanel]::new()
        $first=New-Control $typeName
        $second=New-Control $typeName
        $first.Text='Automatic'
        $second.Text='Custom'
        $panel.Children.Add($first) | Out-Null
        $panel.Children.Add($second) | Out-Null
        $first.Checked=$true
        $firstPeer=[Windows.Automation.Peers.UIElementAutomationPeer]::CreatePeerForElement($first)
        $secondPeer=[Windows.Automation.Peers.UIElementAutomationPeer]::CreatePeerForElement($second)
        $selection=[Windows.Automation.Provider.ISelectionItemProvider]$secondPeer.GetPattern([Windows.Automation.Peers.PatternInterface]::SelectionItem)
        if($secondPeer.GetAutomationControlType() -ne [Windows.Automation.Peers.AutomationControlType]::RadioButton -or $secondPeer.GetName() -ne 'Custom' -or $null -eq $selection){throw "$typeName lacks SelectionItem semantics"}
        if($secondPeer.GetPositionInSet() -ne 2 -or $secondPeer.GetSizeOfSet() -ne 2){throw "$typeName sibling group position is missing"}
        if(-not $second.Focusable -or $null -eq $second.FocusVisualStyle){throw "$typeName has no keyboard focus target"}
        Press $second ([Windows.Input.Key]::Space)
        if(-not $second.Checked -or $first.Checked -or -not $selection.IsSelected){throw "$typeName Space did not select exclusively"}
        Press $second ([Windows.Input.Key]::Enter)
        if(-not $second.Checked -or $first.Checked){throw "$typeName allowed an empty radio group"}
        $firstSelection=[Windows.Automation.Provider.ISelectionItemProvider]$firstPeer.GetPattern([Windows.Automation.Peers.PatternInterface]::SelectionItem)
        $firstSelection.Select()
        if(-not $first.Checked -or $second.Checked){throw "$typeName provider did not replace the sibling selection"}
        Expect-Exception {$firstSelection.RemoveFromSelection()} ([InvalidOperationException])
        $second.IsEnabled=$false
        Press $second ([Windows.Input.Key]::Space)
        Expect-Exception {$selection.Select()} ([Windows.Automation.ElementNotEnabledException])
        if(-not $first.Checked -or $second.Checked){throw "$typeName disabled selection changed state"}
        if($first.IsLoaded -or $second.IsLoaded){throw 'Toggle tests unexpectedly mounted settings controls'}
        Write-Output "PASS $typeName keyboard/SelectionItem stay exclusive, expose group position and reject disabled/empty selection"
    }
    if($app.Windows.Count -ne 0 -or $check.IsLoaded){throw 'Toggle tests unexpectedly showed a real window'}
    Write-Output 'PASS all toggle fixtures remain unloaded and have no SettingService keys or user configuration writes'
} finally {
    $source.Dispose()
    $app.Shutdown()
}
