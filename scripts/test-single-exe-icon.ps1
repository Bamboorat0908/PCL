param(
    [string]$ExePath = (Join-Path $PSScriptRoot '..\Plain Craft Launcher 2\bin\BRCL.exe'),
    [string]$IconPath = (Join-Path $PSScriptRoot '..\Plain Craft Launcher 2\Images\icon.ico')
)
$ErrorActionPreference = 'Stop'
$ExePath = [IO.Path]::GetFullPath($ExePath)
$IconPath = [IO.Path]::GetFullPath($IconPath)
& (Join-Path $PSScriptRoot 'test-exe-icon.ps1') -ExePath $ExePath -IconPath $IconPath
Add-Type -AssemblyName System.Drawing,PresentationCore,WindowsBase
$references = @([Drawing.Bitmap].Assembly.Location)
Add-Type -ReferencedAssemblies $references -TypeDefinition @'
using System;
using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
public static class BrclStandaloneIconChecks {
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]
    struct SHFILEINFO {
        public IntPtr Icon;
        public int IconIndex;
        public uint Attributes;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=260)] public string DisplayName;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=80)] public string TypeName;
    }
    [DllImport("shell32.dll",EntryPoint="SHGetFileInfoW",CharSet=CharSet.Unicode,ExactSpelling=true,SetLastError=true)]
    static extern IntPtr SHGetFileInfo(string path,uint attributes,ref SHFILEINFO info,uint size,uint flags);
    [DllImport("user32.dll",EntryPoint="PrivateExtractIconsW",CharSet=CharSet.Unicode,ExactSpelling=true,SetLastError=true)]
    static extern uint PrivateExtractIcons(string path,int index,int width,int height,[Out] IntPtr[] icons,[Out] uint[] ids,uint count,uint flags);
    [DllImport("user32.dll",ExactSpelling=true)] static extern bool DestroyIcon(IntPtr icon);
    static Bitmap CopyIcon(IntPtr handle) {
        if(handle==IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(),"Icon extraction returned a null handle");
        using(var icon=Icon.FromHandle(handle)) return icon.ToBitmap();
    }
    static Bitmap Extract(string path,int size) {
        var icons=new IntPtr[1]; var ids=new uint[1];
        uint count=PrivateExtractIcons(path,0,size,size,icons,ids,1,0);
        try {
            if(count==0 || count==uint.MaxValue) throw new Win32Exception(Marshal.GetLastWin32Error(),"PrivateExtractIcons failed");
            return CopyIcon(icons[0]);
        } finally { if(icons[0]!=IntPtr.Zero) DestroyIcon(icons[0]); }
    }
    static void RequireSame(Bitmap actual,Bitmap expected,string context) {
        if(actual.Width!=expected.Width || actual.Height!=expected.Height) throw new Exception(context+": dimensions differ");
        int visible=0,colored=0;
        for(int y=0;y<actual.Height;y++) for(int x=0;x<actual.Width;x++) {
            var a=actual.GetPixel(x,y); var b=expected.GetPixel(x,y);
            if(a.A!=b.A || (a.A!=0 && (a.R!=b.R || a.G!=b.G || a.B!=b.B))) throw new Exception(context+": displayed pixels differ from selected BRCL icon");
            if(a.A!=0) { visible++; if(Math.Max(a.R,Math.Max(a.G,a.B))-Math.Min(a.R,Math.Min(a.G,a.B))>16) colored++; }
        }
        if(visible<actual.Width*actual.Height/10 || colored==0) throw new Exception(context+": blank or unrecognizable icon");
    }
    public static void CheckSizes(string exe,string source) {
        foreach(int size in new[]{16,20,24,32,40,48,64,128,256}) {
            using(var actual=Extract(exe,size)) using(var expected=Extract(source,size)) RequireSame(actual,expected,"Native "+size+"px");
        }
    }
    static Bitmap ShellIcon(string path,bool small) {
        var info=new SHFILEINFO();
        IntPtr result=SHGetFileInfo(path,0,ref info,(uint)Marshal.SizeOf(typeof(SHFILEINFO)),0x100u|(small?1u:0u));
        try {
            if(result==IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(),"SHGetFileInfo failed");
            return CopyIcon(info.Icon);
        } finally { if(info.Icon!=IntPtr.Zero) DestroyIcon(info.Icon); }
    }
    public static void CheckShell(string exe,string source,bool small) {
        using(var actual=ShellIcon(exe,small)) using(var expected=ShellIcon(source,small)) RequireSame(actual,expected,small?"Explorer small icon":"Explorer large icon");
    }
}
'@

# A fresh filename tests the Shell's view of a new distribution, independently
# of any stale cached icon for an older Desktop\BRCL.exe at the same path.
$freshDirectory = Join-Path $env:TEMP ('BRCL-SingleIcon-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $freshDirectory | Out-Null
$freshExe = Join-Path $freshDirectory 'BRCL.exe'
Copy-Item -LiteralPath $ExePath -Destination $freshExe
if (@(Get-ChildItem -LiteralPath $freshDirectory -File).Count -ne 1) { throw 'Icon fixture is not a single-file distribution.' }
[BrclStandaloneIconChecks]::CheckSizes($freshExe,$IconPath)
Write-Output 'PASS single EXE native extraction matches the selected BRCL icon at 16/20/24/32/40/48/64/128/256 px'
[BrclStandaloneIconChecks]::CheckShell($freshExe,$IconPath,$true)
[BrclStandaloneIconChecks]::CheckShell($freshExe,$IconPath,$false)
Write-Output 'PASS Explorer small and large icons match the selected BRCL artwork without adjacent files'

$assembly = [Reflection.Assembly]::LoadFrom($freshExe)
$resourceName = @($assembly.GetManifestResourceNames() | Where-Object { $_ -like '*.g.resources' })
if ($resourceName.Count -ne 1) { throw 'Expected one WPF resource dictionary.' }
$reader = New-Object Resources.ResourceReader($assembly.GetManifestResourceStream($resourceName[0]))
$windowIcon = $null
try {
    $entries = $reader.GetEnumerator()
    while ($entries.MoveNext()) {
        if ($entries.Key -ceq 'images/icon.ico') {
            $iconStream = $entries.Value
            $iconStream.Position = 0
            $memory = New-Object IO.MemoryStream
            try { $iconStream.CopyTo($memory); $windowIcon=$memory.ToArray() }
            finally { $memory.Dispose() }
            break
        }
    }
} finally { $reader.Dispose() }
if ($null -eq $windowIcon) { throw 'Window icon is not embedded in the single EXE.' }
$sha = [Security.Cryptography.SHA256]::Create()
try {
    if ([Convert]::ToBase64String($sha.ComputeHash($windowIcon)) -ne [Convert]::ToBase64String($sha.ComputeHash([IO.File]::ReadAllBytes($IconPath)))) { throw 'WPF window icon differs from the native EXE icon.' }
} finally { $sha.Dispose() }
$stream = New-Object IO.MemoryStream(,$windowIcon)
try {
    $decoder = [Windows.Media.Imaging.BitmapDecoder]::Create($stream,[Windows.Media.Imaging.BitmapCreateOptions]::PreservePixelFormat,[Windows.Media.Imaging.BitmapCacheOption]::OnLoad)
    $sizes = @($decoder.Frames | ForEach-Object { $_.PixelWidth } | Sort-Object -Unique)
    if (($sizes -join ',') -ne '16,24,32,48,64,128,256') { throw 'WPF icon is missing expected display/DPI sizes.' }
} finally { $stream.Dispose() }
Write-Output 'PASS embedded WPF window/taskbar icon decodes all seven sizes and matches the native EXE artwork'
[xml]$form = Get-Content -LiteralPath (Join-Path $PSScriptRoot '..\Plain Craft Launcher 2\FormMain.xaml') -Raw -Encoding UTF8
if ($form.DocumentElement.GetAttribute('Icon') -cne '/BRCL;component/Images/icon.ico') { throw 'Main window does not use the embedded launcher icon.' }
Write-Output 'PASS main window uses the embedded BRCL icon URI'
