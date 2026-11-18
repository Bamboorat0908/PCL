param(
    [string]$ExePath = 'C:\Users\BRPC\Desktop\BRCL\BRCL.exe',
    [string]$IconPath = (Join-Path $PSScriptRoot '..\Plain Craft Launcher 2\Images\icon.ico')
)
$ErrorActionPreference = 'Stop'
Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
public static class BrclIconResources {
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)] public static extern IntPtr LoadLibraryEx(string path, IntPtr file, uint flags);
    [DllImport("kernel32.dll")] public static extern bool FreeLibrary(IntPtr module);
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode)] static extern IntPtr FindResource(IntPtr module, IntPtr name, IntPtr type);
    [DllImport("kernel32.dll")] static extern IntPtr LoadResource(IntPtr module, IntPtr resource);
    [DllImport("kernel32.dll")] static extern IntPtr LockResource(IntPtr resource);
    [DllImport("kernel32.dll")] static extern uint SizeofResource(IntPtr module, IntPtr resource);
    delegate bool EnumName(IntPtr module, IntPtr type, IntPtr name, IntPtr data);
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode)] static extern bool EnumResourceNames(IntPtr module, IntPtr type, EnumName callback, IntPtr data);
    public static int[] Ids(IntPtr module, int type) {
        var ids = new List<int>();
        EnumResourceNames(module, new IntPtr(type), (m,t,n,d) => { if (n.ToInt64() <= 65535) ids.Add(n.ToInt32()); return true; }, IntPtr.Zero);
        return ids.ToArray();
    }
    public static byte[] Read(IntPtr module, int type, int id) {
        var resource = FindResource(module,new IntPtr(id),new IntPtr(type));
        if (resource == IntPtr.Zero) throw new Exception("Icon resource missing");
        var bytes = new byte[SizeofResource(module,resource)];
        Marshal.Copy(LockResource(LoadResource(module,resource)),bytes,0,bytes.Length);
        return bytes;
    }
}
'@
$ico = [IO.File]::ReadAllBytes((Resolve-Path $IconPath).Path)
$count = [BitConverter]::ToUInt16($ico,4)
$expected = @{}
$sha = [Security.Cryptography.SHA256]::Create()
for ($i=0; $i -lt $count; $i++) {
    $entry = 6 + 16 * $i
    $width = if ($ico[$entry] -eq 0) {256} else {[int]$ico[$entry]}
    $length = [BitConverter]::ToUInt32($ico,$entry+8)
    $offset = [BitConverter]::ToUInt32($ico,$entry+12)
    $data = New-Object byte[] $length
    [Array]::Copy($ico,$offset,$data,0,$length)
    $expected[$width] = [Convert]::ToBase64String($sha.ComputeHash($data))
}
$module = [BrclIconResources]::LoadLibraryEx((Resolve-Path $ExePath).Path,[IntPtr]::Zero,2)
if ($module -eq [IntPtr]::Zero) { throw 'Could not read EXE resources' }
try {
    $groups = [BrclIconResources]::Ids($module,14)
    if ($groups.Count -eq 0) { throw 'EXE has no native icon group' }
    $group = [BrclIconResources]::Read($module,14,$groups[0])
    $entries = [BitConverter]::ToUInt16($group,4)
    if ($entries -ne $count) { throw "Native icon frame count differs: $entries / $count" }
    for ($i=0; $i -lt $entries; $i++) {
        $entry = 6 + 14 * $i
        $width = if ($group[$entry] -eq 0) {256} else {[int]$group[$entry]}
        $id = [BitConverter]::ToUInt16($group,$entry+12)
        $actual = [BrclIconResources]::Read($module,3,$id)
        $hash = [Convert]::ToBase64String($sha.ComputeHash($actual))
        if ($hash -ne $expected[$width]) { throw "EXE native icon differs from Scheme A at $width px" }
        Write-Output "PASS native EXE icon $width px matches selected Scheme A"
    }
} finally { [void][BrclIconResources]::FreeLibrary($module); $sha.Dispose() }
