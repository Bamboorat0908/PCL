param([Parameter(Mandatory)][string]$Exe,[Parameter(Mandatory)][string]$Stage,[Parameter(Mandatory)][string]$Label,[int]$Seconds=15,[switch]$WakeProbe)
$ErrorActionPreference='Stop'
if(Get-Process BRCL -ErrorAction SilentlyContinue){throw 'Close the launcher before benchmarking.'}
$stagePath=(New-Item -ItemType Directory -Force $Stage).FullName
$target=Join-Path $stagePath 'BRCL.exe'
if((Resolve-Path $Exe).Path -eq $target){throw 'Use a separate benchmark directory.'}
$markerPath=Join-Path $stagePath '.brcl-idle-fixture'
if(!(Test-Path -LiteralPath $markerPath)){
    if(@(Get-ChildItem -LiteralPath $stagePath -Force).Count){throw 'Use an empty directory dedicated to this benchmark.'}
    'BRCL launcher-only measurement'|Set-Content -LiteralPath $markerPath
}
if($Seconds -lt 1 -or $Seconds -gt 600){throw 'Choose a sample window between 1 and 600 seconds.'}
Copy-Item -LiteralPath $Exe -Destination $target -Force
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class IdleWindow {
    delegate bool Visit(IntPtr window,IntPtr arg);
    [DllImport("user32.dll")] static extern bool EnumWindows(Visit visit,IntPtr arg);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr window,System.Text.StringBuilder text,int capacity);
    public static IntPtr FindMain(int id){
        IntPtr result=IntPtr.Zero;
        EnumWindows(delegate(IntPtr window,IntPtr arg){
            uint owner; Owner(window,out owner); if(owner!=id || !IsWindowVisible(window))return true;
            var title=new System.Text.StringBuilder(128);GetWindowText(window,title,title.Capacity);
            if(title.ToString().Trim()!="BRCL")return true;
            result=window;return false;
        },IntPtr.Zero);return result;
    }
    [DllImport("user32.dll",EntryPoint="GetWindowThreadProcessId")] static extern uint Owner(IntPtr window,out uint pid);
    [DllImport("kernel32.dll")] static extern bool QueryThreadCycleTime(IntPtr handle,out ulong cycles);
    [DllImport("kernel32.dll")] static extern bool QueryProcessCycleTime(IntPtr handle,out ulong cycles);
    public static ulong ProcessCycles(IntPtr handle){ulong cycles;return QueryProcessCycleTime(handle,out cycles)?cycles:0;}
    public static ulong Cycles(int id){
        var handle=OpenThread(0x800,false,id);if(handle==IntPtr.Zero)return 0;
        try{ulong cycles;return QueryThreadCycleTime(handle,out cycles)?cycles:0;}finally{CloseHandle(handle);}
    }
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, IntPtr pid);
    [DllImport("kernel32.dll")] static extern IntPtr OpenThread(uint access,bool inherit,int id);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll")] static extern int GetThreadDescription(IntPtr handle,out IntPtr text);
    [DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr memory);
    public static string Name(int id){
        var handle=OpenThread(0x800,false,id); if(handle==IntPtr.Zero)return "";
        try { IntPtr text; if(GetThreadDescription(handle,out text)!=0 || text==IntPtr.Zero)return "";
            try{return Marshal.PtrToStringUni(text);}finally{LocalFree(text);}
        }catch(EntryPointNotFoundException){return "";}finally{CloseHandle(handle);}
    }
    [DllImport("user32.dll")] public static extern bool ShowWindowAsync(IntPtr hwnd,int state);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
}
'@
function Sample($process,$minimized){
    $samples=@()
    $process.Refresh()
    $cpuById=@{}
    foreach($thread in $process.Threads){try{$cpuById[$thread.Id]=$thread.TotalProcessorTime.TotalMilliseconds}catch{}}
    $base=$process.TotalProcessorTime.TotalMilliseconds
    $ownerThread=[IdleWindow]::GetWindowThreadProcessId($window,[IntPtr]::Zero)
    $initialCycles=[IdleWindow]::Cycles($ownerThread)
    $initialProcessCycles=[IdleWindow]::ProcessCycles($process.Handle)
    $clock=[Diagnostics.Stopwatch]::StartNew()
    for($index=0;$index -lt $Seconds;$index++){
        Start-Sleep -Seconds 1
        $process.Refresh()
        if($process.HasExited -or [IdleWindow]::IsIconic($window) -ne $minimized -or ![IdleWindow]::IsWindowVisible($window)){throw 'Window changed state during measurement.'}
        $samples+=$process.WorkingSet64/1MB
    }
    $usage=100*($process.TotalProcessorTime.TotalMilliseconds-$base)/$clock.Elapsed.TotalMilliseconds/[Environment]::ProcessorCount
    $endCycles=[IdleWindow]::Cycles($ownerThread)
    $endProcessCycles=[IdleWindow]::ProcessCycles($process.Handle)
    $processCycles=if($initialProcessCycles -gt 0 -and $endProcessCycles -ge $initialProcessCycles){$endProcessCycles-$initialProcessCycles}
    $cycles=if($initialCycles -gt 0 -and $endCycles -ge $initialCycles){$endCycles-$initialCycles}
    $active=foreach($thread in $process.Threads){try{
        if(!$cpuById.ContainsKey($thread.Id)){continue}
        $used=$thread.TotalProcessorTime.TotalMilliseconds-$cpuById[$thread.Id]
        if($used -gt 0){[pscustomobject]@{threadId=$thread.Id;name=[IdleWindow]::Name($thread.Id);ui=($thread.Id -eq [IdleWindow]::GetWindowThreadProcessId($window,[IntPtr]::Zero));cpuMs=$used}}
    }catch{}}
    [ordered]@{cpuPct=[math]::Round($usage,3);workingSetMiB=[math]::Round(($samples|Measure-Object -Average).Average,2);privateMiB=[math]::Round($process.PrivateMemorySize64/1MB,2);uiCycles=$cycles;processCycles=$processCycles;activeThreads=@($active|Where-Object {$_})}
}
$process=Start-Process $target -WorkingDirectory $stagePath -WindowStyle Hidden -PassThru
try{
    $started=$process.StartTime
    $log=Join-Path $stagePath 'BRCL\Log1.txt'
    $watch=[Diagnostics.Stopwatch]::StartNew()
    do{
        Start-Sleep -Milliseconds 150
        if($process.HasExited -or $watch.Elapsed.TotalSeconds -gt 45){throw 'Timed out waiting for interactive home.'}
        $marker=if(Test-Path $log){Get-Content -LiteralPath $log -Encoding UTF8 -ErrorAction SilentlyContinue|Where-Object {$_ -match '启动首屏可交互'}|Select-Object -Last 1}
        if($marker){$ready=$started.Date+[TimeSpan]::Parse($marker.Substring(0,12)); if($ready -lt $started){$marker=$null}}
    }until($marker)
    $process.Refresh()
    $window=[IdleWindow]::FindMain($process.Id)
    if(!$window.ToInt64()){throw 'Missing main window.'}
    [void][IdleWindow]::ShowWindowAsync($window,9)
    Start-Sleep -Seconds 12
    $bursts=@()
    if($WakeProbe){
        $uiThread=[IdleWindow]::GetWindowThreadProcessId($window,[IntPtr]::Zero)
        $lastCycles=[IdleWindow]::Cycles($uiThread)
        if(!$lastCycles){throw 'UI thread cycle counter unavailable.'}
        $traceClock=[Diagnostics.Stopwatch]::StartNew()
        while($traceClock.Elapsed.TotalSeconds -lt 24){
            Start-Sleep -Milliseconds 25
            if([IdleWindow]::IsIconic($window) -or ![IdleWindow]::IsWindowVisible($window)){throw 'Window changed state during wake probe.'}
            $currentCycles=[IdleWindow]::Cycles($uiThread)
            if($currentCycles -gt $lastCycles){$bursts+=@{atMs=$traceClock.ElapsedMilliseconds;cycles=$currentCycles-$lastCycles}}
            $lastCycles=$currentCycles
        }
    }
    $homeSample=Sample $process $false
    [void][IdleWindow]::ShowWindowAsync($window,6)
    Start-Sleep -Seconds 4
    $mini=Sample $process $true
    $stages=[ordered]@{}
    foreach($line in (Get-Content -LiteralPath $log -Encoding UTF8)){
        if($line -match '(第一阶段加载用时|第二阶段加载用时|第三阶段加载用时|启动首帧呈现|启动首屏可交互)：(\d+) ms'){
            $stages[$Matches[1]]=@([math]::Round((($started.Date+[TimeSpan]::Parse($line.Substring(0,12)))-$started).TotalMilliseconds),[int]$Matches[2])
        }
    }
    [pscustomobject]@{label=$Label;sha256=(Get-FileHash $target).Hash;startupMs=[math]::Round(($ready-$started).TotalMilliseconds);seconds=$Seconds;cores=[Environment]::ProcessorCount;stages=$stages;home=$homeSample;minimized=$mini;wakeProbe=$bursts}|ConvertTo-Json -Depth 6 -Compress
}finally{
    if(!$process.HasExited){[void]$process.CloseMainWindow();if(!$process.WaitForExit(4000)){$process.Kill();$process.WaitForExit()}}
    $process.Dispose()
}
