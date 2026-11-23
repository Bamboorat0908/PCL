$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$libraries = @('Plain Craft Launcher 2\Resources\Newtonsoft.Json.dll', 'Plain Craft Launcher 2\Resources\ThrottleDebounce.dll', 'MeloongCore\Shared\bin\MeloongCore.dll', 'MeloongCore\Wpf\bin\MeloongCore.Wpf.dll', 'PCLCS\bin\BRCL.Core.dll')
foreach ($library in $libraries) {
    [Reflection.Assembly]::LoadFrom((Join-Path $repo $library)) | Out-Null
}
$stage = Join-Path $env:TEMP ('BRCL-PatchTest-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($stage) | Out-Null
$logical = 'Launch\JavaWrapper.jar'
$expected = [BRCL.Core.LaunchUtils].Assembly.GetManifestResourceStream($logical)
$buffer = New-Object IO.MemoryStream
try { $expected.CopyTo($buffer); $bytes = $buffer.ToArray() } finally { $expected.Dispose(); $buffer.Dispose() }
$target = [BRCL.Core.LaunchUtils]::ExtractPatch($logical, $stage)
if ([Convert]::ToBase64String([IO.File]::ReadAllBytes($target)) -ne [Convert]::ToBase64String($bytes)) { throw 'Normal patch extraction differs from the embedded resource.' }
Write-Output 'PASS launch patch extracts byte-for-byte from the assembly'
[IO.File]::WriteAllText($target, 'stale resource fixture')
$locked = [IO.File]::Open($target, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
try {
    $alternate = [BRCL.Core.LaunchUtils]::ExtractPatch($logical, $stage)
    if ($alternate -eq $target -or [IO.Path]::GetFileName($alternate) -ne 'JavaWrapper2.jar') { throw 'Locked patch did not select the alternate destination.' }
    if ([Convert]::ToBase64String([IO.File]::ReadAllBytes($alternate)) -ne [Convert]::ToBase64String($bytes)) { throw 'Alternate patch differs from the original embedded resource.' }
    if ([IO.File]::ReadAllText($target) -ne 'stale resource fixture') { throw 'Locked file was modified.' }
} finally { $locked.Dispose() }
Write-Output 'PASS locked stale patch falls back to another filename and preserves the resource identity'

# Exercise actual Win32 argument decoding with a synthetic command, without starting Java.
Add-Type @'
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
public static class LaunchArgumentChecks {
    [DllImport("shell32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
    static extern IntPtr CommandLineToArgvW(string command, out int count);
    [DllImport("kernel32.dll")]
    static extern IntPtr LocalFree(IntPtr data);
    public static string DecodeOne(string argument) {
        int count;
        IntPtr data = CommandLineToArgvW("java.exe " + argument, out count);
        if(data == IntPtr.Zero) throw new Win32Exception();
        try {
            if(count != 2) throw new Exception("One launch argument changed cardinality.");
            return Marshal.PtrToStringUni(Marshal.ReadIntPtr(data, IntPtr.Size));
        } finally { LocalFree(data); }
    }
}
'@
Add-Type -AssemblyName PresentationFramework,PresentationCore,WindowsBase,System.Net.Http
$assembly = [Reflection.Assembly]::LoadFrom((Join-Path $repo 'Plain Craft Launcher 2\bin\BRCL.exe'))
$format = $assembly.GetType('BRCL.ModLaunch',$true).GetMethod('FormatLaunchArgument',[Reflection.BindingFlags]'NonPublic,Static')
foreach ($argument in @('', 'plain', 'C:\Portable Game\', "tab`tseparated", 'literal \"inside text" suffix')) {
    $encoded = $format.Invoke($null,[object[]]@($argument))
    if ([LaunchArgumentChecks]::DecodeOne($encoded) -cne $argument) { throw 'A Windows process argument failed to round trip.' }
}
$preQuoted = '"C:\Already Quoted\file.jar"'
if ($format.Invoke($null,[object[]]@($preQuoted)) -cne $preQuoted) { throw 'A caller-supplied quoted launch argument was changed.' }
Write-Output 'PASS Windows launch parameters preserve empty values, spaces, tabs, quotes and trailing backslashes without starting Java'
