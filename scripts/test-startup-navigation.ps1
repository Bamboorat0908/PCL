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
$source = @'
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using PCL;
public static class StartupNavigationChecks {
    static void Require(bool condition,string message) { if(!condition) throw new Exception(message); }
    static void Pump(int milliseconds) {
        var frame=new DispatcherFrame();
        var timer=new DispatcherTimer(DispatcherPriority.Background) {Interval=TimeSpan.FromMilliseconds(milliseconds)};
        timer.Tick+=(s,e)=>frame.Continue=false; timer.Start(); Dispatcher.PushFrame(frame); timer.Stop();
    }
    public static string[] Run() {
        var result=new List<string>();
        Settings.Entries["UiReduceMotion"].Set(false,null);
        Settings.Entries["SystemDebugAnim"].Set(9,null);
        ModAnimation.AniStart();
        var page=new MyPageRight(); var content=new StackPanel();
        var controls=new List<TextBlock>();
        for(int i=0;i<30;i++) {var control=new TextBlock {Text="Item "+i,Height=100}; controls.Add(control); content.Children.Add(control);}
        page.Child=content;
        page.Measure(new Size(400,300)); page.Arrange(new Rect(0,0,400,300)); page.UpdateLayout();
        page.PageState=MyPageRight.PageStates.ContentEnter;
        page.TriggerEnterAnimation(content);
        var animation=ModAnimation.AniGroups["PageRight PageChange "+page.PageUuid];
        Require(animation.Data.Count<=10,"Page transition animated all off-screen controls");
        Require(controls[29].Opacity==1 && !(controls[29].RenderTransform is System.Windows.Media.TranslateTransform),"Off-screen card did not start in its final visual state");
        Pump(450);
        Require(page.PageState==MyPageRight.PageStates.ContentStay,"Enter animation did not complete its page state");
        foreach(var control in controls) Require(Math.Abs(control.Opacity-1)<0.00001,"Card stayed transparent after page entry: "+control.Text+" opacity="+control.Opacity+" state="+page.PageState);
        result.Add("PASS long pages animate only controls near the viewport and leave off-screen cards ready");
        page.PageState=MyPageRight.PageStates.PageExit;
        page.TriggerExitAnimation(content); Pump(250);
        Require(page.PageState==MyPageRight.PageStates.Empty && content.Visibility==Visibility.Collapsed,"Page exit did not complete its state");
        page.PageState=MyPageRight.PageStates.ContentEnter;
        page.TriggerEnterAnimation(content); Pump(450);
        foreach(var control in controls) Require(control.IsHitTestVisible && Math.Abs(control.Opacity-1)<0.00001,"Returning page retained hidden or disabled controls: "+control.Text+" opacity="+control.Opacity+" hit="+control.IsHitTestVisible+" state="+page.PageState);
        result.Add("PASS leaving and returning restores every card and preserves page lifecycle callbacks");

        foreach(string name in new[]{"PCL.PageStock","PCL.PageMap"}) {
            var shell=(FrameworkElement)Activator.CreateInstance(typeof(MyPageRight).Assembly.GetType(name,true));
            string viewName=name.EndsWith("PageStock") ? "ViewWeb" : "ViewMap";
            var view=shell.FindName(viewName);
            Require(!shell.IsLoaded,"Prewarmed page was attached before navigation");
            Require(view.GetType().GetProperty("CoreWebView2").GetValue(view,null)==null,"Prewarming started a browser renderer");
        }
        result.Add("PASS prewarming browser shells does not load pages or initialize WebView2");
        Settings.Entries["UiReduceMotion"].Set(true,null);
        page.PageState=MyPageRight.PageStates.ContentEnter;
        page.TriggerEnterAnimation(content);
        Require(ModAnimation.AniGroups["PageRight PageChange "+page.PageUuid].Data.Count==1,"Reduced motion still allocated card animations");
        Pump(150);
        Require(page.PageState==MyPageRight.PageStates.ContentStay,"Reduced motion dropped completion callback");
        result.Add("PASS reduced motion applies final card states without allocating visual animations");
        return result.ToArray();
    }
}
'@
$temp = Join-Path $env:TEMP ('BRCL-StartupChecks-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temp | Out-Null
$sourceFile = Join-Path $temp 'StartupNavigationChecks.cs'
$harnessFile = Join-Path $temp 'StartupNavigationChecks.dll'
[IO.File]::WriteAllText($sourceFile,$source)
$references=@($assembly.Location,[Windows.Application].Assembly.Location,[Windows.Media.Visual].Assembly.Location,[Windows.Threading.Dispatcher].Assembly.Location,[System.Xaml.XamlReader].Assembly.Location,(Join-Path $BuildDirectory 'MeloongCore.dll')) | ForEach-Object {'/reference:'+$_}
& "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:library "/out:$harnessFile" @references $sourceFile
if ($LASTEXITCODE -ne 0) {throw 'Startup/navigation harness compilation failed'}
[void][Reflection.Assembly]::LoadFrom($harnessFile)
[StartupNavigationChecks]::Run() | Write-Output
$app.Shutdown()
