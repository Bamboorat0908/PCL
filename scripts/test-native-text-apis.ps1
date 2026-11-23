# Win32 text APIs must use their Unicode entry points: window titles contain characters (U+3000 in the
# launcher title, CJK in custom game titles) that ANSI code pages such as Windows-1252 cannot represent.
param([string]$OutputDirectory = 'bin')
$ErrorActionPreference = 'Stop'
$project = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\Plain Craft Launcher 2'))
# Reflection runs before the launcher's embedded-assembly resolver is installed.
foreach ($name in 'NAudio', 'Newtonsoft.Json') {
    [void][Reflection.Assembly]::LoadFrom((Join-Path $project "Resources\$name.dll"))
}
$assembly = [Reflection.Assembly]::LoadFrom((Join-Path $project "$OutputDirectory\BRCL.exe"))
$flags = [Reflection.BindingFlags]'Public,NonPublic,Static,Instance,DeclaredOnly'
function Require([bool]$value, [string]$message) { if (-not $value) { throw $message } }
$checked = 0
foreach ($type in $assembly.GetTypes()) {
    foreach ($method in $type.GetMethods($flags)) {
        if (($method.Attributes -band [Reflection.MethodAttributes]::PinvokeImpl) -eq 0) { continue }
        $import = [Attribute]::GetCustomAttribute($method, [Runtime.InteropServices.DllImportAttribute])
        if (-not $import -or $import.Value -notmatch '^(?i)user32(\.dll)?$') { continue }
        $takesText = @($method.GetParameters() | Where-Object {
            $parameterType = $_.ParameterType
            if ($parameterType.IsByRef) { $parameterType = $parameterType.GetElementType() }
            $parameterType -eq [string] -or $parameterType -eq [Text.StringBuilder]
        }).Count -gt 0
        if (-not $takesText) { continue }
        $checked++
        Require ($import.CharSet -eq [Runtime.InteropServices.CharSet]::Unicode -and $import.EntryPoint -match 'W$') "$($type.FullName).$($method.Name) calls $($import.EntryPoint) with $($import.CharSet) strings."
    }
}
Require ($checked -ge 4) "Expected at least four user32 text imports, found $checked."
Write-Output "PASS all $checked user32 imports that pass text use Unicode entry points"
# Exercise the actual watcher against a real Win32 window without starting Minecraft.
Add-Type @'
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
public static class WatcherWindowFixture {
    public const string ClassName = "GLFW30";
    public delegate IntPtr WindowProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    private static readonly WindowProc callback = DefWindowProc;
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowClass {
        public uint Style;
        public WindowProc Proc;
        public int ExtraClass, ExtraWindow;
        public IntPtr Instance, Icon, Cursor, Background;
        public string Menu, Name;
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string module);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr DefWindowProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClass(ref WindowClass windowClass);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool UnregisterClass(string name, IntPtr instance);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(uint exStyle, string className, string title, uint style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr window);
    public static IntPtr Create(string title) {
        var wc = new WindowClass { Name = ClassName, Proc = callback, Instance = GetModuleHandle(null) };
        if (RegisterClass(ref wc) == 0) throw new Win32Exception();
        var window = CreateWindowEx(0, wc.Name, title, 0, 0, 0, 64, 64, IntPtr.Zero, IntPtr.Zero, wc.Instance, IntPtr.Zero);
        if (window == IntPtr.Zero) { var error = new Win32Exception(); UnregisterClass(wc.Name, wc.Instance); throw error; }
        return window;
    }
    public static void Close(IntPtr window) {
        DestroyWindow(window);
        UnregisterClass(ClassName, GetModuleHandle(null));
    }
}
'@
$watcherType = $assembly.GetType('BRCL.ModWatcher+Watcher', $true)
$watcher = [Runtime.Serialization.FormatterServices]::GetUninitializedObject($watcherType)
$process = [Diagnostics.Process]::GetCurrentProcess()
$watcher.GameProcess = $process
$findWindow = $watcherType.GetMethod('TryGetMinecraftWindow', $flags)
$setTitle = $watcherType.GetMethod('SetWindowText', $flags)
$firstTitle = 'Minecraft ' + [char]0x4E16 + [char]0x754C
$secondTitle = 'Minecraft ' + [char]0x542F + [char]0x52A8
$window = [IntPtr]::Zero
try {
    $window = [WatcherWindowFixture]::Create($firstTitle)
    $result = $findWindow.Invoke($watcher, @())
    Require ($result.Key -eq $window -and $result.Value -ceq $firstTitle) 'The watcher did not find the fixture or lost its Unicode title.'
    Require ([bool]$setTitle.Invoke($null, @($window, $secondTitle))) 'The watcher failed to set the fixture title.'
    $result = $findWindow.Invoke($watcher, @())
    Require ($result.Key -eq $window -and $result.Value -ceq $secondTitle) 'The watcher did not preserve the updated Unicode title.'
    Write-Output 'PASS the watcher finds a Win32 game window and reads and writes its Unicode title'

    $parentId = (Get-CimInstance Win32_Process -Filter "ProcessId=$PID").ParentProcessId
    $otherProcess = [Diagnostics.Process]::GetProcessById($parentId)
    try {
        $watcher.GameProcess = $otherProcess
        $result = $findWindow.Invoke($watcher, @())
        Require ($null -eq $result -or $result.Key -ne $window) 'The watcher associated an unrelated game window with its process.'
        Write-Output 'PASS game windows remain associated with their owning process'
    } finally { $otherProcess.Dispose() }
} finally {
    if ($window -ne [IntPtr]::Zero) { [WatcherWindowFixture]::Close($window) }
    $process.Dispose()
}
