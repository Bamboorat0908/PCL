param([string]$BuildDirectory)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName PresentationFramework,PresentationCore,WindowsBase,System.Xaml,System.Net.Http,UIAutomationProvider,UIAutomationTypes
$project=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\Plain Craft Launcher 2'))
if(-not $BuildDirectory){$BuildDirectory=Join-Path $project 'bin'}
$assembly=[Reflection.Assembly]::LoadFrom((Join-Path $BuildDirectory 'BRCL.exe'))
[Windows.Application]::ResourceAssembly=$assembly
$app=[Activator]::CreateInstance($assembly.GetType('PCL.Application',$true),$true)
$app.InitializeComponent()
$startup=$app.GetType().GetMethod('Application_Startup',[Reflection.BindingFlags]'NonPublic,Instance')
$app.remove_Startup([Delegate]::CreateDelegate([Windows.StartupEventHandler],$app,$startup))
[Windows.Application].GetField('_startupUri',[Reflection.BindingFlags]'NonPublic,Instance').SetValue($app,$null)
$assembly.GetType('PCL.ModAnimation',$true).GetProperty('AniControlEnabled').SetValue($null,1,$null)
$code=@'
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Threading;
using System.Windows.Media;
using System.Windows.Shapes;
using PCL;

public static class ControlAccessibilityChecks {
    static void Require(bool condition,string message) {if(!condition) throw new Exception(message);}
    static void Pump() {
        var frame=new DispatcherFrame();
        var timer=new DispatcherTimer(DispatcherPriority.Background) {Interval=TimeSpan.FromMilliseconds(35)};
        timer.Tick+=(s,e)=>frame.Continue=false; timer.Start(); Dispatcher.PushFrame(frame); timer.Stop();
    }
    static KeyEventArgs KeyEvent(HwndSource source,Key key,bool up) {
        return new KeyEventArgs(Keyboard.PrimaryDevice,source,Environment.TickCount,key) {
            RoutedEvent=up ? Keyboard.KeyUpEvent : Keyboard.KeyDownEvent
        };
    }
    static void Press(UIElement control,HwndSource source,Key key,bool up) {
        var args=KeyEvent(source,key,up); control.RaiseEvent(args);
        Require(args.Handled,"Activation or adjustment key escaped to the window");
    }
    static void Expect<T>(Action action,string label) where T:Exception {
        try {action();} catch(T) {return;} throw new Exception(label);
    }
    public static string[] Run() {
        var results=new List<string>();
        // A shown but transparent/off-screen, non-activating fixture supplies
        // IsVisible=true for logical focus tests without displaying a test UI.
        var fixture=new Window {Title="BRCL isolated control checks",Width=420,Height=260,
            Left=-10000,Top=-10000,ShowActivated=false,ShowInTaskbar=false,Opacity=0};
        var panel=new StackPanel(); FocusManager.SetIsFocusScope(panel,true);
        var input=new TextBox {Text="Keep this input focused"};
        var icon=new MyIconButton {Width=32,Height=30,ToolTip="Icon action"};
        var slider=new MySlider {Width=280,Height=24,MaxValue=100,Value=40};
        panel.Children.Add(input);panel.Children.Add(icon);panel.Children.Add(slider);fixture.Content=panel;
        fixture.Show();Pump();
        var source=(HwndSource)PresentationSource.FromVisual(fixture);
        panel.Measure(new Size(420,140));panel.Arrange(new Rect(0,0,420,140));panel.UpdateLayout();
        try {
            AutomationProperties.SetName(icon,"Test icon");
            var iconPeer=UIElementAutomationPeer.CreatePeerForElement(icon);
            Require(iconPeer.GetAutomationControlType()==AutomationControlType.Button && iconPeer.GetName()=="Test icon","Icon role or accessible name missing");
            Require(iconPeer.GetPattern(PatternInterface.Invoke) is IInvokeProvider,"Icon has no Invoke provider");
            Require(iconPeer.GetChildren()==null || iconPeer.GetChildren().Count==0,"Decorative path entered the control reading order");
            Require(icon.Focusable && KeyboardNavigation.GetIsTabStop(icon),"Icon is absent from keyboard navigation");
            results.Add("PASS icon exposes Button/Invoke, accessible name and one focus target");

            int clicks=0;icon.Click+=(s,e)=> {Require(e is RoutedEventArgs,"Legacy routed click handler compatibility was lost");clicks++;};
            ((IInvokeProvider)iconPeer.GetPattern(PatternInterface.Invoke)).Invoke();Pump();
            Require(clicks==1,"Invoke did not activate exactly once");
            icon.CanClick=false;
            Require(!iconPeer.IsEnabled(),"CanClick=false was not reflected to UIA");
            Expect<ElementNotEnabledException>(()=>((IInvokeProvider)iconPeer.GetPattern(PatternInterface.Invoke)).Invoke(),"Disabled icon Invoke was accepted");
            icon.CanClick=true;icon.IsEnabled=false;
            Expect<ElementNotEnabledException>(()=>((IInvokeProvider)iconPeer.GetPattern(PatternInterface.Invoke)).Invoke(),"IsEnabled=false Invoke was accepted");
            icon.IsEnabled=true;
            results.Add("PASS icon Invoke activates once and rejects disabled or unavailable actions");

            panel.Children.Remove(icon);var scroll=new ScrollViewer {Content=icon};panel.Children.Add(scroll);
            panel.Measure(new Size(420,140));panel.Arrange(new Rect(0,0,420,140));panel.UpdateLayout();
            int pointerBaseline=clicks;
            var mouseDown=new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left) {RoutedEvent=Mouse.MouseDownEvent};
            icon.RaiseEvent(mouseDown);
            Require(mouseDown.Handled,"Icon pointer press escaped to the scrolling container");
            icon.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left) {RoutedEvent=Mouse.MouseUpEvent});
            Require(clicks==pointerBaseline+1,"Icon pointer activation inside a scrolling container was lost or duplicated");
            results.Add("PASS icon pointer click in a scrolling container is consumed and activates once");

            foreach(var key in new[]{Key.Enter,Key.Space}) {
                int before=clicks;Press(icon,source,key,false);Press(icon,source,key,false);
                Require(clicks==before,"Repeated keydown activated the icon before release");
                Press(icon,source,key,true);Press(icon,source,key,true);
                Require(clicks==before+1,"Keyboard activation was missing or duplicated");
            }
            int baseline=clicks;
            Press(icon,source,Key.Space,false);icon.IsEnabled=false;icon.IsEnabled=true;Press(icon,source,Key.Space,true);
            Require(clicks==baseline,"Disabling did not cancel keyboard activation");
            results.Add("PASS icon Enter/Space activates once; disabling cancels a pending press");

            icon.KeyboardActivationHandledExternally=true;
            KeyEventHandler caption=(s,e)=>{if(e.Key==Key.Enter){e.Handled=true;clicks++;}};
            icon.KeyDown+=caption;
            int externalBaseline=clicks;
            Press(icon,source,Key.Enter,false);
            icon.RaiseEvent(KeyEvent(source,Key.Enter,true));
            Require(clicks==externalBaseline+1,"External caption handler received a duplicate activation");
            icon.KeyDown-=caption;
            results.Add("PASS explicit caption keyboard delegation avoids duplicate activation");

            var textButton=new MyButton {Text="Refresh content",Icon=Geometry.Parse("M2,2 H10 V10 H2 Z")};
            panel.Children.Add(textButton);
            var buttonPeer=UIElementAutomationPeer.CreatePeerForElement(textButton);
            Require(buttonPeer.GetAutomationControlType()==AutomationControlType.Button && buttonPeer.GetName()=="Refresh content","Text button role/name fallback missing");
            Require(buttonPeer.GetChildren()==null || buttonPeer.GetChildren().Count==0,"Button icon or text created duplicate reading targets");
            int buttonClicks=0;textButton.Click+=(s,e)=>{Require(e!=null,"Text button lost compatibility click args");buttonClicks++;};
            ((IInvokeProvider)buttonPeer.GetPattern(PatternInterface.Invoke)).Invoke();Pump();
            Require(buttonClicks==1,"Text button Invoke did not activate once");
            textButton.IsEnabled=false;
            Expect<ElementNotEnabledException>(()=>((IInvokeProvider)buttonPeer.GetPattern(PatternInterface.Invoke)).Invoke(),"Disabled text button Invoke was accepted");
            results.Add("PASS text button exposes one Button/Invoke target with Text fallback and disabled guard");

            var navigation=new MyNavItem {Focusable=true};navigation.Children.Add(new TextBlock {Text="News"});
            AutomationProperties.SetName(navigation,"News navigation");
            var navigationPeer=UIElementAutomationPeer.CreatePeerForElement(navigation);
            Require(navigationPeer.GetAutomationControlType()==AutomationControlType.Button && navigationPeer.GetName()=="News navigation","Navigation role/name missing");
            Require(navigationPeer.GetChildren()==null || navigationPeer.GetChildren().Count==0,"Navigation label was exposed as a separate reading stop");
            int navigations=0;navigation.MouseUp+=(s,e)=>{if(e.ChangedButton==MouseButton.Left)navigations++;};
            ((IInvokeProvider)navigationPeer.GetPattern(PatternInterface.Invoke)).Invoke();Pump();
            Require(navigations==1,"Navigation Invoke did not use the existing left-mouse route");
            navigation.IsEnabled=false;
            Expect<ElementNotEnabledException>(()=>((IInvokeProvider)navigationPeer.GetPattern(PatternInterface.Invoke)).Invoke(),"Disabled navigation Invoke was accepted");
            results.Add("PASS navigation exposes one named Button/Invoke target and reuses the existing click route");

            AutomationProperties.SetName(slider,"Test volume");
            var sliderPeer=UIElementAutomationPeer.CreatePeerForElement(slider);
            var range=(IRangeValueProvider)sliderPeer.GetPattern(PatternInterface.RangeValue);
            Require(sliderPeer.GetAutomationControlType()==AutomationControlType.Slider && sliderPeer.GetName()=="Test volume","Slider role or accessible name missing");
            Require(sliderPeer.GetOrientation()==AutomationOrientation.Horizontal,"Slider orientation missing");
            Require(range.Minimum==0 && range.Maximum==100 && range.Value==40 && !range.IsReadOnly,"Slider range values are incorrect");
            range.SetValue(65);Require(slider.Value==65,"RangeValue SetValue did not update the real control");
            slider.Value=12;Require(range.Value==12,"Provider retained a stale programmatic value");
            slider.IsEnabled=false;Require(range.IsReadOnly,"Disabled slider did not become read-only");
            Expect<ElementNotEnabledException>(()=>range.SetValue(20),"Disabled RangeValue SetValue was accepted");
            slider.IsEnabled=true;
            Expect<ArgumentOutOfRangeException>(()=>range.SetValue(-1),"Negative RangeValue was accepted");
            Expect<ArgumentOutOfRangeException>(()=>range.SetValue(101),"Out-of-range value was accepted");
            Expect<ArgumentOutOfRangeException>(()=>range.SetValue(double.NaN),"NaN was accepted");
            Expect<ArgumentOutOfRangeException>(()=>range.SetValue(double.PositiveInfinity),"Infinity was accepted");
            results.Add("PASS slider exposes live RangeValue data and rejects disabled/nonfinite/out-of-range input");

            slider.Value=80;slider.MaxValue=30;
            Require(slider.Value==30 && range.Maximum==30 && range.Value==30,"Shrinking the range left an invalid or stale value");
            slider.MaxValue=0;
            var lineFore=(Line)slider.FindName("LineFore");var lineBack=(Line)slider.FindName("LineBack");
            Require(slider.Value==0 && range.Maximum==0 && !double.IsNaN(lineFore.Width) && !double.IsNaN(lineBack.Width),"Zero range created invalid values or geometry");
            slider.DragDoing();
            slider.MaxValue=-4;Require(range.Maximum==0,"Negative maximum was not normalized");
            slider.MaxValue=100;slider.Value=50;
            results.Add("PASS dynamic and zero-width ranges keep value/geometry bounded and provider data current");

            Press(slider,source,Key.Right,false);Require(slider.Value==51,"Right did not increment");
            Press(slider,source,Key.Left,false);Require(slider.Value==50,"Left did not decrement");
            Press(slider,source,Key.Home,false);Require(slider.Value==0,"Home did not reach minimum");
            Press(slider,source,Key.Left,false);Require(slider.Value==0,"Keyboard crossed the lower bound");
            Press(slider,source,Key.End,false);Require(slider.Value==100,"End did not reach maximum");
            Press(slider,source,Key.Right,false);Require(slider.Value==100,"Keyboard crossed the upper bound");
            slider.ValueByKey=uint.MaxValue;
            Press(slider,source,Key.Left,false);Require(slider.Value==0,"Large decrement overflowed");
            Press(slider,source,Key.PageUp,false);Require(slider.Value==100,"Large increment overflowed");
            results.Add("PASS slider arrows/Home/End/Page keys consume input and clamp without overflow");

            FocusManager.SetFocusedElement(panel,input);
            var beforeFocus=FocusManager.GetFocusedElement(panel);
            slider.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice,Environment.TickCount) {RoutedEvent=Mouse.MouseEnterEvent});
            Require(FocusManager.GetFocusedElement(panel)==beforeFocus,"Pointer hover stole the form's logical focus");
            slider.Focus();
            Require(FocusManager.GetFocusedElement(panel)==slider,"Slider cannot receive logical focus for an explicit operation");
            results.Add("PASS hovering keeps input focus; explicit slider focus remains available");
        } finally {fixture.Close();}
        return results.ToArray();
    }
}
'@
$harnessDirectory=Join-Path $env:TEMP ('BRCL-ControlAccessibility-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $harnessDirectory | Out-Null
$sourceFile=Join-Path $harnessDirectory 'ControlAccessibilityChecks.cs'
$harnessFile=Join-Path $harnessDirectory 'ControlAccessibilityChecks.dll'
[IO.File]::WriteAllText($sourceFile,$code)
$references=@($assembly.Location,[Windows.Window].Assembly.Location,[Windows.UIElement].Assembly.Location,[Windows.DependencyObject].Assembly.Location,[System.Xaml.XamlReader].Assembly.Location,[Windows.Automation.Provider.IInvokeProvider].Assembly.Location,[Windows.Automation.RangeValuePatternIdentifiers].Assembly.Location,(Join-Path $BuildDirectory 'MeloongCore.dll')) | Select-Object -Unique | ForEach-Object {'/reference:'+$_}
& "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:library "/out:$harnessFile" @references $sourceFile
if($LASTEXITCODE -ne 0){throw 'Control accessibility harness compilation failed'}
[void][Reflection.Assembly]::LoadFrom($harnessFile)
[ControlAccessibilityChecks]::Run()
$app.Shutdown()
