param([string]$BuildDirectory)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName PresentationFramework,PresentationCore,WindowsBase,System.Xaml,System.Net.Http
$project=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\Plain Craft Launcher 2'))
if (-not $BuildDirectory) { $BuildDirectory=Join-Path $project 'bin' }
$assembly=[Reflection.Assembly]::LoadFrom((Join-Path $BuildDirectory 'BRCL.exe'))
[Windows.Application]::ResourceAssembly=$assembly
$app=[Activator]::CreateInstance($assembly.GetType('PCL.Application',$true),$true)
$app.InitializeComponent()
$startup=$app.GetType().GetMethod('Application_Startup',[Reflection.BindingFlags]'NonPublic,Instance')
$app.remove_Startup([Delegate]::CreateDelegate([Windows.StartupEventHandler],$app,$startup))
[Windows.Application].GetField('_startupUri',[Reflection.BindingFlags]'NonPublic,Instance').SetValue($app,$null)
$assembly.GetType('PCL.ModAnimation',$true).GetProperty('AniControlEnabled').SetValue($null,1,$null)
[xml]$xaml=Get-Content -LiteralPath (Join-Path $project 'FormMain.xaml') -Raw -Encoding UTF8
$ns=[Xml.XmlNamespaceManager]::new($xaml.NameTable)
$ns.AddNamespace('w','http://schemas.microsoft.com/winfx/2006/xaml/presentation')
$ns.AddNamespace('x','http://schemas.microsoft.com/winfx/2006/xaml')
$buttons=@('BtnTitleMin','BtnTitleMax','BtnTitleClose') | ForEach-Object { $node=$xaml.SelectSingleNode("//*[@x:Name='$_']",$ns); if (-not $node) {throw "Missing caption button $_"}; $node.OuterXml.Replace('clr-namespace:PCL"','clr-namespace:PCL;assembly=BRCL"') }
$markup='<Grid xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" xmlns:local="clr-namespace:PCL;assembly=BRCL" Width="400" Height="48">'+($buttons -join '')+'</Grid>'
$title=[Windows.Markup.XamlReader]::Parse($markup)
$source=@'
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using PCL;
public static class WindowMaximizeChecks {
    [StructLayout(LayoutKind.Sequential)] struct Point {public int X,Y;}
    [StructLayout(LayoutKind.Sequential)] struct Rect {public int Left,Top,Right,Bottom;}
    [StructLayout(LayoutKind.Sequential)] struct MinMax {public Point Reserved,MaxSize,MaxPosition,MinTrack,MaxTrack;}
    [StructLayout(LayoutKind.Sequential)] class MonitorInfo {
        public int Size=Marshal.SizeOf(typeof(MonitorInfo)); public Rect Monitor,Work; public int Flags;
    }
    [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr handle,int flags);
    [DllImport("user32.dll",EntryPoint="GetMonitorInfoW",CharSet=CharSet.Unicode,ExactSpelling=true)] static extern bool GetMonitorInfo(IntPtr monitor,[In,Out] MonitorInfo info);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr handle,out Rect rect);
    [DllImport("user32.dll",EntryPoint="GetWindowLongW")] static extern int GetWindowLong(IntPtr handle,int index);
    [DllImport("user32.dll",EntryPoint="SetWindowLongW")] static extern int SetWindowLong(IntPtr handle,int index,int value);
    static void Require(bool condition,string message) {if(!condition) throw new Exception(message);}
    static void Pump(int ms) {
        var frame=new DispatcherFrame(); var timer=new DispatcherTimer(DispatcherPriority.Background) {Interval=TimeSpan.FromMilliseconds(ms)};
        timer.Tick+=(s,e)=>frame.Continue=false; timer.Start(); Dispatcher.PushFrame(frame); timer.Stop();
    }
    static bool Near(double a,double b) {return Math.Abs(a-b)<=2;}
    public static string[] Run(Grid title) {
        var results=new List<string>();
        title.Measure(new Size(400,48)); title.Arrange(new System.Windows.Rect(0,0,400,48)); title.UpdateLayout();
        var min=(MyIconButton)title.FindName("BtnTitleMin"); var max=(MyIconButton)title.FindName("BtnTitleMax"); var close=(MyIconButton)title.FindName("BtnTitleClose");
        var minX=min.TranslatePoint(new System.Windows.Point(),title).X;
        var maxX=max.TranslatePoint(new System.Windows.Point(),title).X;
        var closeX=close.TranslatePoint(new System.Windows.Point(),title).X;
        Require(minX+min.ActualWidth<=maxX && maxX+max.ActualWidth<=closeX,"Caption buttons overlap or have the wrong order");
        Require(max.Focusable && System.Windows.Input.KeyboardNavigation.GetIsTabStop(max),"Maximize button is not keyboard focusable");
        var presentation=typeof(FormMain).GetMethod("UpdateMaximizeButton",BindingFlags.NonPublic|BindingFlags.Static);
        presentation.Invoke(null,new object[]{max,false}); string normal=max.Logo;
        Require((string)max.ToolTip=="\u6700\u5927\u5316" && System.Windows.Automation.AutomationProperties.GetName(max)==(string)max.ToolTip,"Normal caption accessibility is incorrect");
        presentation.Invoke(null,new object[]{max,true});
        Require((string)max.ToolTip=="\u8fd8\u539f" && System.Windows.Automation.AutomationProperties.GetName(max)==(string)max.ToolTip && max.Logo!=normal,"Maximized caption presentation did not switch to restore");
        presentation.Invoke(null,new object[]{max,false});
        Require(max.Logo==normal,"Restore did not recover the maximize icon");
        results.Add("PASS minimize, maximize and close buttons do not overlap; maximize/restore icon, tooltip and accessibility name follow state");

        // This transparent, non-activating fixture exercises the actual HWND hook
        // without touching the user's launcher or foreground window.
        var window=new Window {Title="BRCL Window State Test",WindowStyle=WindowStyle.None,AllowsTransparency=true,ResizeMode=ResizeMode.CanResize,
            Width=1000,Height=650,Left=80,Top=80,ShowInTaskbar=false,ShowActivated=false,Opacity=0,Content=title};
        window.Show(); Pump(80);
        var handle=new WindowInteropHelper(window).Handle;
        SetWindowLong(handle,-20,GetWindowLong(handle,-20)|0x08000000); // WS_EX_NOACTIVATE
        var resizer=new MyResizer(window); // Deliberately after SourceInitialized, matching FormMain.
        var flags=BindingFlags.NonPublic|BindingFlags.Instance;
        var hook=typeof(MyResizer).GetField("hs",flags);
        Require(hook.GetValue(resizer)!=null,"Late-created resizer did not attach its HWND hook");
        typeof(MyResizer).GetMethod("AttachSourceHook",flags).Invoke(resizer,null);
        var original=window.RestoreBounds;
        try {
            var info=new MonitorInfo(); Require(GetMonitorInfo(MonitorFromWindow(handle,2),info),"Cannot inspect current monitor work area");
            var data=new MinMax {MinTrack=new Point {X=903,Y=707},MaxTrack=new Point {X=7777,Y=8888}};
            var pointer=Marshal.AllocHGlobal(Marshal.SizeOf(typeof(MinMax)));
            try {
                Marshal.StructureToPtr(data,pointer,false);
                typeof(MyResizer).GetMethod("WmGetMinMaxInfo",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{handle,pointer});
                var updated=(MinMax)Marshal.PtrToStructure(pointer,typeof(MinMax));
                Require(updated.MinTrack.X==903 && updated.MinTrack.Y==707 && updated.MaxTrack.X==7777 && updated.MaxTrack.Y==8888,"Work area hook overwrote incoming track size constraints");
                Require(updated.MaxSize.X==info.Work.Right-info.Work.Left && updated.MaxPosition.X==Math.Abs(info.Work.Left-info.Monitor.Left),"Work area hook lost the monitor width or origin");
            } finally {Marshal.FreeHGlobal(pointer);}
            window.WindowState=WindowState.Maximized; Pump(150);
            Rect bounds; Require(GetWindowRect(handle,out bounds),"Cannot inspect maximized window bounds");
            int expectedBottom=info.Work.Bottom-(info.Work.Bottom-info.Work.Top==info.Monitor.Bottom-info.Monitor.Top ? 2 : 0);
            Require(window.WindowState==WindowState.Maximized && Near(bounds.Left,info.Work.Left) && Near(bounds.Top,info.Work.Top) && Near(bounds.Right,info.Work.Right) && Near(bounds.Bottom,expectedBottom),
                "Maximized window did not fit its monitor work area: "+bounds.Left+","+bounds.Top+","+bounds.Right+","+bounds.Bottom+" expected "+info.Work.Left+","+info.Work.Top+","+info.Work.Right+","+expectedBottom);
            results.Add("PASS late-attached HWND hook maximizes to the real monitor work area and preserves taskbar and incoming minimum sizes");
            window.WindowState=WindowState.Minimized; Pump(60); window.WindowState=WindowState.Maximized; Pump(100);
            Require(window.WindowState==WindowState.Maximized,"Restoring a minimized maximized window lost its state");
            window.WindowState=WindowState.Normal; Pump(150);
            Require(Near(window.ActualWidth,1000) && Near(window.ActualHeight,650) && Near(window.RestoreBounds.Width,original.Width) && Near(window.RestoreBounds.Height,original.Height),"Restoring changed the original window dimensions");
            results.Add("PASS maximize/minimize/maximize/restore preserves the original normal window size");
        } finally {window.Close();}
        Require(hook.GetValue(resizer)==null,"Closed window retained its HWND hook");
        results.Add("PASS closing a window removes its native hook");
        return results.ToArray();
    }
}
'@
$temp=Join-Path $env:TEMP ('BRCL-WindowChecks-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temp | Out-Null
$sourceFile=Join-Path $temp 'WindowMaximizeChecks.cs'
$harnessFile=Join-Path $temp 'WindowMaximizeChecks.dll'
[IO.File]::WriteAllText($sourceFile,$source)
$references=@($assembly.Location,[Windows.Application].Assembly.Location,[Windows.Media.Visual].Assembly.Location,[Windows.Threading.Dispatcher].Assembly.Location,[System.Xaml.XamlReader].Assembly.Location,(Join-Path $BuildDirectory 'MeloongCore.dll')) | ForEach-Object {'/reference:'+$_}
& "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:library "/out:$harnessFile" @references $sourceFile
if ($LASTEXITCODE -ne 0) {throw 'Window state harness compilation failed'}
[void][Reflection.Assembly]::LoadFrom($harnessFile)
[WindowMaximizeChecks]::Run($title) | Write-Output
$app.Shutdown()
