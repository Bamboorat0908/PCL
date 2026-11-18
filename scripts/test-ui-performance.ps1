param([string]$BuildDirectory, [switch]$BenchmarkOnly, [switch]$Browser)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationFramework,PresentationCore,WindowsBase,System.Xaml,System.Net.Http
$project = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\Plain Craft Launcher 2'))
if (-not $BuildDirectory) { $BuildDirectory = Join-Path $project 'bin' }
$assembly = [Reflection.Assembly]::LoadFrom((Join-Path $BuildDirectory 'BRCL.exe'))
[Windows.Application]::ResourceAssembly = $assembly
$app = [Activator]::CreateInstance($assembly.GetType('PCL.Application',$true),$true)
$app.InitializeComponent()
# Pump only the test dispatcher; do not enter the launcher's real Startup path.
$startupMethod = $app.GetType().GetMethod('Application_Startup',[Reflection.BindingFlags]'NonPublic,Instance')
$startupHandler = [Delegate]::CreateDelegate([Windows.StartupEventHandler],$app,$startupMethod)
$app.remove_Startup($startupHandler)
[Windows.Application].GetField('_startupUri',[Reflection.BindingFlags]'NonPublic,Instance').SetValue($app,$null)
$source = @'
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using PCL;
public static class UiPerformanceChecks {
    static readonly Type Engine = typeof(ModAnimation);
    static readonly BindingFlags PrivateStatic = BindingFlags.NonPublic | BindingFlags.Static;
    static bool Attached { get { return (bool)Engine.GetField("FrameAttached",PrivateStatic).GetValue(null); } }
    static DispatcherTimer Fallback { get { return (DispatcherTimer)Engine.GetField("FrameFallback",PrivateStatic).GetValue(null); } }
    static void Pump(int milliseconds) {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (s,e) => frame.Continue = false;
        timer.Start(); Dispatcher.PushFrame(frame); timer.Stop();
    }
    static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    static void Clear() {
        foreach (var key in new List<string>(ModAnimation.AniGroups.Keys)) ModAnimation.AniStop(key);
    }
    public static string[] Run() {
        var passed = new List<string>();
        Settings.Entries["UiReduceMotion"].Set(false,null);
        Settings.Entries["SystemDebugAnim"].Set(9,null);
        ModAnimation.AniStart(); Clear();
        bool done=false; var elapsed=Stopwatch.StartNew();
        ModAnimation.AniStart(ModAnimation.AaCode(()=>done=true,400),"Delay");
        Require(!Attached,"Delayed callback kept compositor running");
        Require(Fallback.IsEnabled && Fallback.Interval.TotalMilliseconds>=300,"Delayed callback kept polling frequently");
        Pump(180);
        Require(!done && !Attached,"Delayed callback fired early or enabled rendering");
        Pump(330);
        Require(done && elapsed.ElapsedMilliseconds>=390,"Delayed callback stalled");
        Require(!Attached && !Fallback.IsEnabled,"Idle engine retained a frame source");
        passed.Add("PASS delayed callbacks sleep without rendering and release all frame sources");

        // Adding a new group after a long timer has slept must not inherit its elapsed time.
        ModAnimation.AniStart(ModAnimation.AaCode(()=>{},1000),"OldTimer");
        Pump(250); double value=0;
        ModAnimation.AniStart(ModAnimation.AaDouble(d=>value+=Convert.ToDouble(d),1,300),"FreshVisual");
        Pump(110);
        Require(value>0 && value<0.8,"New visual inherited an older group's elapsed time");
        Pump(280);
        Require(Math.Abs(value-1)<0.00001,"New visual did not finish correctly");
        ModAnimation.AniStop("OldTimer");
        Require(!Attached && !Fallback.IsEnabled,"Cancellation did not stop frame sources immediately");
        passed.Add("PASS each group keeps independent time when waking an idle scheduler");

        var order=new List<int>();
        ModAnimation.AniStart(new List<ModAnimation.AniData> {
            ModAnimation.AaCode(()=>order.Add(1),50),
            ModAnimation.AaCode(()=>order.Add(2),50,true),
            ModAnimation.AaCode(()=>order.Add(3),0,true)
        },"Sequence");
        Pump(300);
        Require(string.Join(",",order)=="1,2,3","Ordered callbacks changed order");
        bool replacement=false; int obsolete=0;
        ModAnimation.AniStart(ModAnimation.AaCode(()=> {
            ModAnimation.AniStart(ModAnimation.AaCode(()=>replacement=true,60),"Replace");
        },30),"Replace");
        ModAnimation.AniStart(ModAnimation.AaCode(()=>obsolete++,200),"Cancelled");
        ModAnimation.AniStop("Cancelled");
        Pump(300);
        Require(replacement && obsolete==0,"Reentrant replacement or cancellation failed");
        passed.Add("PASS ordered, cancelled and same-name reentrant callbacks");

        // Cancelling a different group during a frame must not skip a third group.
        int tail=0;
        ModAnimation.AniStart(ModAnimation.AaCode(()=>ModAnimation.AniStop("Victim")),"Canceller");
        ModAnimation.AniStart(ModAnimation.AaCode(()=>obsolete++),"Victim");
        ModAnimation.AniStart(ModAnimation.AaCode(()=>tail++),"Tail");
        Pump(180);
        Require(obsolete==0 && tail==1,"Dictionary mutation skipped or executed the wrong callback");
        passed.Add("PASS cross-group cancellation preserves the remaining frame");

        foreach (var pair in new [] { new [] {"PCL.PageSetupLaunch","RamTimer"},new [] {"PCL.PageInstanceSetup","RamTimer"},new [] {"PCL.PageSpeedLeft","WatcherTimer"} }) {
            var type=Engine.Assembly.GetType(pair[0],true);
            var page=(FrameworkElement)Activator.CreateInstance(type);
            var timer=(DispatcherTimer)type.GetField(pair[1],BindingFlags.NonPublic|BindingFlags.Instance).GetValue(page);
            timer.Start(); page.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
            Require(!timer.IsEnabled,"Unloaded page retained timer: "+pair[0]);
            page.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
        }
        passed.Add("PASS settings, instance settings and task monitor stop timers on unload");
        Clear();
        return passed.ToArray();
    }
    public static string Benchmark() {
        Settings.Entries["UiReduceMotion"].Set(false,null);
        Settings.Entries["SystemDebugAnim"].Set(9,null);
        // Keep groups alive throughout the benchmark and bypass wall-clock gating.
        int count=400, frames=300;
        for(int i=0;i<count;i++) {
            ModAnimation.AniStart(ModAnimation.AaDouble(d=>{},1,100000),"Bench"+i);
            ModAnimation.AniGroups["Bench"+i].StartTick=0;
        }
        for(int i=0;i<10;i++) ModAnimation.AniTimer(1);
        var clock=Stopwatch.StartNew();
        for(int i=0;i<frames;i++) ModAnimation.AniTimer(1);
        clock.Stop(); Clear();
        return "BENCH groups="+count+" frames="+frames+" elapsed_ms="+clock.Elapsed.TotalMilliseconds.ToString("F2",System.Globalization.CultureInfo.InvariantCulture);
    }
    static void Complete(Task task) {
        var clock=Stopwatch.StartNew();
        while(!task.IsCompleted && clock.ElapsedMilliseconds<20000) Pump(30);
        Require(task.IsCompleted,"Browser operation timed out");
        task.GetAwaiter().GetResult();
    }
    public static string[] CheckBrowser() {
        var passed=new List<string>();
        CoreWebView2Environment.SetLoaderDllFolderPath(Path.GetDirectoryName(Engine.Assembly.Location));
        var folder=Path.Combine(Path.GetTempPath(),"BRCL-Browser-"+Guid.NewGuid().ToString("N"));
        var env=CoreWebView2Environment.CreateAsync(null,folder,null); Complete(env);
        var view=new WebView2(); var host=new Grid(); host.Children.Add(view);
        var window=new Window {Title="BRCL Performance Browser Test",Width=400,Height=300,Left=-10000,Top=-10000,ShowActivated=false,Content=host};
        window.Show(); Pump(100);
        try {
            var init=view.EnsureCoreWebView2Async(env.Result); Complete(init);
            var activityType=Engine.Assembly.GetType("PCL.BrclWebViewActivity",true);
            var activity=Activator.CreateInstance(activityType,BindingFlags.Public|BindingFlags.Instance,null,new object[]{host,view},null);
            view.NavigateToString("<html><body>browser lifecycle test<script>window.marker='preserved';window.ticks=0;setInterval(function(){window.ticks++},50)</script></body></html>");
            Pump(300);
            window.WindowState=WindowState.Minimized; Pump(2400);
            Require(view.CoreWebView2.IsSuspended,"Minimized browser did not suspend");
            Require(view.CoreWebView2.MemoryUsageTargetLevel==CoreWebView2MemoryUsageTargetLevel.Low,"Background browser retained its normal memory target");
            window.WindowState=WindowState.Normal; Pump(150);
            Require(!view.CoreWebView2.IsSuspended && view.Visibility==Visibility.Visible,"Restored browser remained hidden or suspended");
            Require(view.CoreWebView2.MemoryUsageTargetLevel==CoreWebView2MemoryUsageTargetLevel.Normal,"Foreground browser did not restore its memory target");
            var marker=view.CoreWebView2.ExecuteScriptAsync("window.marker"); Complete(marker);
            Require(marker.Result=="\"preserved\"","Restoring browser lost its document");
            passed.Add("PASS minimizing suspends browser and restoring preserves the existing document");
            window.Content=null; Pump(100); window.Content=host; Pump(2400);
            Require(!view.CoreWebView2.IsSuspended,"Fast page return raced a stale suspend request");
            passed.Add("PASS rapid leave and return cancels pending browser suspension");
            window.Content=null; Pump(2400);
            Require(view.CoreWebView2.IsSuspended,"Unloaded browser did not suspend");
            window.Content=host; Pump(150);
            Require(!view.CoreWebView2.IsSuspended,"Reattached browser did not resume");
            passed.Add("PASS page unload suspends browser and reload resumes it");
            window.WindowState=WindowState.Minimized; Pump(100);
            activityType.GetMethod("SetVisibility").Invoke(activity,new object[]{Visibility.Hidden});
            window.WindowState=WindowState.Normal; Pump(100);
            Require(view.Visibility==Visibility.Hidden,"Restoring exposed a browser hidden by loading/error state");
            activityType.GetMethod("SetVisibility").Invoke(activity,new object[]{Visibility.Visible}); Pump(100);
            Require(view.Visibility==Visibility.Visible,"Ready browser did not become visible");
            passed.Add("PASS restoring a window respects a newer loading or error state");
            var runtime=Engine.Assembly.GetType("PCL.BrclWebViewRuntime",true);
            var environmentField=runtime.GetField("SharedEnvironment",BindingFlags.NonPublic|BindingFlags.Static);
            environmentField.SetValue(null,env);
            var getEnvironment=runtime.GetMethod("GetEnvironmentAsync");
            var firstRequest=(Task<CoreWebView2Environment>)getEnvironment.Invoke(null,null);
            var secondRequest=(Task<CoreWebView2Environment>)getEnvironment.Invoke(null,null);
            Require(object.ReferenceEquals(firstRequest,secondRequest),"Browser environment initialization was duplicated");
            var secondView=new WebView2(); host.Children.Add(secondView);
            try {
                var options=firstRequest.Result.CreateCoreWebView2ControllerOptions(); options.ProfileName="BRCLMap";
                Complete(secondView.EnsureCoreWebView2Async(secondRequest.Result,options));
                Require(secondView.CoreWebView2.BrowserProcessId==view.CoreWebView2.BrowserProcessId,"Exchange and map created separate browser processes");
                Require(secondView.CoreWebView2.Profile.ProfileName!=view.CoreWebView2.Profile.ProfileName,"Map did not retain its separate profile");
            } finally { secondView.Dispose(); host.Children.Remove(secondView); environmentField.SetValue(null,null); }
            passed.Add("PASS browser initialization is shared and map profile reuses the same browser process");
            var transitionPage=new MyPageRight();
            window.Content=null; transitionPage.Child=host; window.Content=transitionPage; Pump(50);
            transitionPage.PageState=MyPageRight.PageStates.ContentEnter;
            var waitForTransition=runtime.GetMethod("WaitForPageTransitionAsync");
            var ready=(Task<bool>)waitForTransition.Invoke(null,new object[]{transitionPage});
            Require(!ready.IsCompleted,"Browser initialization ran during the page transition");
            transitionPage.PageState=MyPageRight.PageStates.ContentStay; Complete(ready);
            Require(ready.Result,"Visible page did not become ready for browser initialization");
            transitionPage.PageState=MyPageRight.PageStates.ContentEnter;
            var cancelled=(Task<bool>)waitForTransition.Invoke(null,new object[]{transitionPage});
            window.Content=null; Complete(cancelled);
            Require(!cancelled.Result,"Removed page continued initializing a browser");
            transitionPage.Child=null; window.Content=host; Pump(50);
            passed.Add("PASS browser initialization waits for transition completion and cancels on page removal");
            GC.KeepAlive(activity);
        } finally { view.Dispose(); window.Close(); }
        return passed.ToArray();
    }
    public static string[] RunBrowserLoop() {
        string[] result=null; Exception failure=null;
        System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(()=> {
            try { result=CheckBrowser(); } catch(Exception ex) { failure=ex; }
            finally { System.Windows.Application.Current.Shutdown(); }
        }));
        System.Windows.Application.Current.Run();
        if(failure!=null) throw failure;
        return result;
    }
}
'@
$directory = Join-Path $env:TEMP ('BRCL-Performance-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $directory | Out-Null
$sourceFile = Join-Path $directory 'UiPerformanceChecks.cs'
$harnessFile = Join-Path $directory 'UiPerformanceChecks.dll'
[IO.File]::WriteAllText($sourceFile,$source)
$arguments = @('/nologo','/target:library',"/out:$harnessFile",('/reference:'+$assembly.Location),('/reference:'+([Windows.Application].Assembly.Location)),('/reference:'+([Windows.Media.Visual].Assembly.Location)),('/reference:'+([Windows.Threading.Dispatcher].Assembly.Location)),('/reference:'+([System.Xaml.XamlReader].Assembly.Location)),('/reference:'+(Join-Path $BuildDirectory 'MeloongCore.dll')),('/reference:'+(Join-Path $BuildDirectory 'Microsoft.Web.WebView2.Core.dll')),('/reference:'+(Join-Path $BuildDirectory 'Microsoft.Web.WebView2.Wpf.dll')),$sourceFile)
& "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" @arguments
if ($LASTEXITCODE -ne 0) { throw 'Performance harness compilation failed' }
[void][Reflection.Assembly]::LoadFrom($harnessFile)
if (-not $BenchmarkOnly) { [UiPerformanceChecks]::Run() | Write-Output }
[UiPerformanceChecks]::Benchmark() | Write-Output
if ($Browser) { [UiPerformanceChecks]::RunBrowserLoop() | Write-Output }
$app.Shutdown()
