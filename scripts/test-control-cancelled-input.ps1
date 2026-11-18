param([string]$BuildDirectory)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationFramework,PresentationCore,WindowsBase,System.Xaml,System.Net.Http
$project = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\Plain Craft Launcher 2'))
if (-not $BuildDirectory) { $BuildDirectory = Join-Path $project 'bin' }
$assembly = [Reflection.Assembly]::LoadFrom((Join-Path $BuildDirectory 'BRCL.exe'))
[Windows.Application]::ResourceAssembly = $assembly
$app = [Activator]::CreateInstance($assembly.GetType('PCL.Application',$true),$true)
$app.InitializeComponent()
$startup = $app.GetType().GetMethod('Application_Startup',[Reflection.BindingFlags]'NonPublic,Instance')
$app.remove_Startup([Delegate]::CreateDelegate([Windows.StartupEventHandler],$app,$startup))
[Windows.Application].GetField('_startupUri',[Reflection.BindingFlags]'NonPublic,Instance').SetValue($app,$null)
$assembly.GetType('PCL.ModAnimation',$true).GetProperty('AniControlEnabled').SetValue($null,1,$null)
function Mouse-Event($control,[bool]$up,[bool]$preview=$false,[bool]$right=$false) {
    $button = if ($right) { [Windows.Input.MouseButton]::Right } else { [Windows.Input.MouseButton]::Left }
    $event = [Windows.Input.MouseButtonEventArgs]::new([Windows.Input.Mouse]::PrimaryDevice,0,$button)
    $event.RoutedEvent = if ($preview) {
        if ($up) { [Windows.UIElement]::PreviewMouseLeftButtonUpEvent } else { [Windows.UIElement]::PreviewMouseLeftButtonDownEvent }
    } elseif ($right) {
        if ($up) { [Windows.UIElement]::MouseRightButtonUpEvent } else { [Windows.UIElement]::MouseRightButtonDownEvent }
    } else {
        if ($up) { [Windows.UIElement]::MouseLeftButtonUpEvent } else { [Windows.UIElement]::MouseLeftButtonDownEvent }
    }
    $control.RaiseEvent($event)
}
try {
    foreach ($name in @('MyCheckBox','MyRadioBox','MyRadioButton')) {
        foreach ($cancel in @('disable','unload')) {
            $panel = [Windows.Controls.StackPanel]::new()
            $control = [Activator]::CreateInstance($assembly.GetType('PCL.'+$name,$true))
            [void]$panel.Children.Add($control)
            Mouse-Event $control $false
            if ($cancel -eq 'disable') {
                $control.IsEnabled = $false
                $control.IsEnabled = $true
            } else {
                $control.RaiseEvent([Windows.RoutedEventArgs]::new([Windows.FrameworkElement]::UnloadedEvent))
            }
            Mouse-Event $control $true
            if ($control.Checked) { throw "$name activated a mouse press cancelled by $cancel" }
            Write-Output "PASS $name ignores mouse release after $cancel"
        }
    }
    foreach ($name in @('MyButton','MyIconButton','MyIconTextButton','MyTextButton','MyExtraButton','MyExtraTextButton')) {
        foreach ($cancel in @('disable','unload')) {
            $control = [Activator]::CreateInstance($assembly.GetType('PCL.'+$name,$true))
            $script:clickCount = 0
            $control.add_Click({ $script:clickCount += 1 })
            $target = if ($name -in @('MyExtraButton','MyExtraTextButton')) { $control.FindName('PanClick') } else { $control }
            $preview = $name -eq 'MyTextButton'
            Mouse-Event $target $false $preview
            if ($cancel -eq 'disable') {
                $control.IsEnabled = $false
                $control.IsEnabled = $true
            } else {
                $control.RaiseEvent([Windows.RoutedEventArgs]::new([Windows.FrameworkElement]::UnloadedEvent))
            }
            Mouse-Event $target $true $preview
            if ($script:clickCount -ne 0) { throw "$name clicked after a press cancelled by $cancel" }
            Mouse-Event $target $false $preview
            Mouse-Event $target $true $preview
            if ($script:clickCount -ne 1) { throw "$name stopped responding to a fresh click after $cancel" }
            Write-Output "PASS $name ignores a cancelled $cancel click and accepts one fresh click"
        }
    }
    $icon = [Activator]::CreateInstance($assembly.GetType('PCL.MyIconButton',$true))
    $script:clickCount = 0
    $icon.add_Click({ $script:clickCount += 1 })
    Mouse-Event $icon $false
    $icon.CanClick = $false
    $icon.CanClick = $true
    Mouse-Event $icon $true
    if ($script:clickCount -ne 0) { throw 'Icon CanClick change retained its previous press' }
    Write-Output 'PASS temporarily disallowing icon clicks cancels the old press'

    $extra = [Activator]::CreateInstance($assembly.GetType('PCL.MyExtraButton',$true))
    $extra.CanRightClick = $true
    $script:clickCount = 0
    $extra.add_RightClick({ $script:clickCount += 1 })
    $target = $extra.FindName('PanClick')
    Mouse-Event $target $false $false $true
    $extra.CanRightClick = $false
    if ($extra.FindName('PanScale').RenderTransform.ScaleX -ne 1) { throw 'Cancelling right clicks left its pressed visual behind' }
    $extra.CanRightClick = $true
    Mouse-Event $target $true $false $true
    if ($script:clickCount -ne 0) { throw 'Extra button retained a cancelled right press' }
    Write-Output 'PASS temporarily disallowing right clicks cancels the old press'

    foreach ($cancel in @('disable','unload')) {
        $slider = [Activator]::CreateInstance($assembly.GetType('PCL.MySlider',$true))
        $slider.Value = 30
        $slider.GetHintText = [Func[int,string]] { param($value) "$value%" }
        $slider.RefreshPopup()
        $assembly.GetType('PCL.ModMain',$true).GetField('DragControl').SetValue($null,$slider)
        if ($cancel -eq 'disable') {
            $slider.IsEnabled = $false
        } else {
            $slider.RaiseEvent([Windows.RoutedEventArgs]::new([Windows.FrameworkElement]::UnloadedEvent))
        }
        if ($null -ne $assembly.GetType('PCL.ModMain',$true).GetField('DragControl').GetValue($null)) { throw "Slider retained a drag cancelled by $cancel" }
        if ($slider.FindName('Popup').IsOpen) { throw "Slider retained its popup after $cancel" }
        if ($slider.Value -ne 30) { throw "Cancelling slider drag changed its value" }
        Write-Output "PASS slider $cancel releases global drag and hint without changing its value"
    }
    $slider = [Activator]::CreateInstance($assembly.GetType('PCL.MySlider',$true))
    $slider.Value = 30
    $slider.add_PreviewChange({ param($sender,$event) $event.Handled = $true })
    $assembly.GetType('PCL.ModMain',$true).GetField('DragControl').SetValue($null,$slider)
    $assembly.GetType('PCL.ModAnimation',$true).GetProperty('AniControlEnabled').SetValue($null,0,$null)
    $slider.Value = 60
    $assembly.GetType('PCL.ModAnimation',$true).GetProperty('AniControlEnabled').SetValue($null,1,$null)
    if ($slider.Value -ne 30 -or $null -ne $assembly.GetType('PCL.ModMain',$true).GetField('DragControl').GetValue($null)) { throw 'Rejected slider preview retained its value or global drag' }
    Write-Output 'PASS a rejected slider preview restores its previous value and ends the drag'
    if ($app.Windows.Count -ne 0) { throw 'Cancelled-input fixtures unexpectedly showed a window' }
    Write-Output 'PASS all fixtures use unloaded controls without settings keys or user configuration writes'
} finally {
    $assembly.GetType('PCL.ModMain',$true).GetField('DragControl').SetValue($null,$null)
    $app.Shutdown()
}
