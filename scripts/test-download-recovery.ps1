param([string]$BuildDirectory)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName PresentationFramework,PresentationCore,WindowsBase,System.Xaml,System.Net.Http
$project=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\Plain Craft Launcher 2'))
if(-not $BuildDirectory){$BuildDirectory=Join-Path $project 'bin'}
$assembly=[Reflection.Assembly]::LoadFrom((Join-Path $BuildDirectory 'BRCL.exe'))
[Windows.Application]::ResourceAssembly=$assembly
$app=[Activator]::CreateInstance($assembly.GetType('PCL.Application',$true),$true)
$app.InitializeComponent()
$startup=$app.GetType().GetMethod('Application_Startup',[Reflection.BindingFlags]'NonPublic,Instance')
$app.remove_Startup([Delegate]::CreateDelegate([Windows.StartupEventHandler],$app,$startup))
[Windows.Application].GetField('_startupUri',[Reflection.BindingFlags]'NonPublic,Instance').SetValue($app,$null)
$stage=Join-Path $env:TEMP ('BRCL-Recovery-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($stage)|Out-Null
$source=@'
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Threading;
using PCL;

public sealed class RecoveryServer : IDisposable {
    private readonly TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
    private volatile bool stopped;
    public int BrokenRequests;
    public int BackupRequests;
    public int RateLimitedRequests;
    public int RateBackupRequests;
    public long UpdateResumeOffset;
    public long BulkBytes;
    public int Port {get;private set;}
    public RecoveryServer() {
        listener.Start(); Port=((IPEndPoint)listener.LocalEndpoint).Port;
        new Thread(() => {while(!stopped) {try {var client=listener.AcceptTcpClient(); ThreadPool.QueueUserWorkItem(_=>Respond(client));} catch(SocketException) {if(!stopped) throw;} catch(ObjectDisposedException) {return;}}}) {IsBackground=true}.Start();
    }
    private void Respond(TcpClient client) {
        try {
            using(client) using(var stream=client.GetStream()) using(var reader=new StreamReader(stream,Encoding.ASCII,false,1024,true)) {
                string request=reader.ReadLine(); if(request==null) return;
                string path=request.Split(' ')[1], line; long start=0;
                while(!String.IsNullOrEmpty(line=reader.ReadLine())) {
                    if(line.StartsWith("Range: bytes=",StringComparison.OrdinalIgnoreCase)) long.TryParse(line.Substring(13).Split('-')[0],out start);
                }
                if(path.Contains("bmclapi-rate-limited")) {
                    Interlocked.Increment(ref RateLimitedRequests);
                    byte[] limited=Encoding.ASCII.GetBytes("HTTP/1.1 429 Too Many Requests\r\nRetry-After: 60\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
                    stream.Write(limited,0,limited.Length); return;
                }
                bool bulk=path.Contains("bmclapi-bulk");
                bool broken=path.Contains("broken");
                if(path.Contains("update-backup")) Interlocked.Exchange(ref UpdateResumeOffset,start);
                else if(broken) Interlocked.Increment(ref BrokenRequests);
                else if(path.Contains("rate-backup")) Interlocked.Increment(ref RateBackupRequests);
                else if(!bulk) Interlocked.Increment(ref BackupRequests);
                long total=bulk ? 64*1024*1024 : 2*1024*1024;
                string headers="HTTP/1.1 "+(start>0?"206 Partial Content":"200 OK")+"\r\nConnection: close\r\nContent-Type: application/octet-stream\r\nContent-Length: "+(total-start)+"\r\n";
                if(start>0) headers+="Content-Range: bytes "+start+"-"+(total-1)+"/"+total+"\r\n";
                byte[] head=Encoding.ASCII.GetBytes(headers+"\r\n"); stream.Write(head,0,head.Length);
                byte[] block=new byte[32768]; for(int i=0;i<block.Length;i++) block[i]=0x5a;
                long remaining=broken ? 65536 : total-start;
                while(remaining>0 && !stopped) {
                    int count=(int)Math.Min(block.Length,remaining); stream.Write(block,0,count); remaining-=count;
                    if(bulk) {Interlocked.Add(ref BulkBytes,count); Thread.Sleep(10);}
                }
                // The primary deliberately closes after a partial body.
            }
        } catch(IOException) {} catch(SocketException) {} catch(ObjectDisposedException) {}
    }
    public void Dispose() {stopped=true; listener.Stop();}
}

public static class DownloadRecoveryChecks {
    private static void Pump(int milliseconds) {
        var frame=new DispatcherFrame();
        var timer=new DispatcherTimer(DispatcherPriority.Background) {Interval=TimeSpan.FromMilliseconds(milliseconds)};
        timer.Tick+=(s,e)=>frame.Continue=false; timer.Start(); Dispatcher.PushFrame(frame); timer.Stop();
    }
    private static void Require(bool value,string message) {if(!value) throw new Exception(message);}
    public static string[] Run(string stage) {
        var passed=new List<string>();
        AppDomain.CurrentDomain.SetData("APP_CONTEXT_BASE_DIRECTORY",stage+"\\");
        string cache=Path.Combine(stage,"cache")+"\\";
        Directory.CreateDirectory(cache);
        Settings.Entries["SystemSystemCache"].Set(cache,null);
        Settings.Entries["ToolDownloadThread"].Set(3,null);
        Settings.Entries["ToolDownloadSpeed"].Set(42,null);
        Settings.Entries["SystemDebugDelay"].Set(false,null);
        Settings.Entries["SystemBrclCrashUpload"].Source=Settings.Sources.Normal;
        Settings.Entries["SystemBrclCrashUpload"].Set(false,null);
        ModBase.PathTemp=cache; // Never let a fixture clear a running launcher's shared cache.
        ModNet.NetTaskSpeedLimitLow=256*1024;
        ModNet.LoaderDownload bulk=null, critical=null;
        using(var server=new RecoveryServer()) {
            try {
                string root="http://127.0.0.1:"+server.Port;
                var bulkFile=new ModNet.NetFile(new[]{root+"/bmclapi-bulk"},Path.Combine(stage,"bulk.bin"),null,false);
                bulk=new ModNet.LoaderDownload("Loopback recovery bulk",new[]{bulkFile}); bulk.Start(null,false);
                var waiting=Stopwatch.StartNew();
                while((ModNet.NetManager.Speed<512*1024 || Interlocked.Read(ref server.BulkBytes)<1024*1024) && waiting.ElapsedMilliseconds<6000) Pump(10);
                Require(ModNet.NetManager.Speed>=512*1024,"Loopback bulk transfer did not establish real background traffic");
                passed.Add("PASS an isolated loopback bulk download keeps global speed above the retry throttle");
                var file=new ModNet.NetFile(new[]{root+"/broken",root+"/backup"},Path.Combine(stage,"critical.bin"),null,false);
                critical=new ModNet.LoaderDownload("Loopback critical dependency",new[]{file}); critical.Start(null,false);
                waiting.Restart();
                while(server.BrokenRequests==0 && waiting.ElapsedMilliseconds<3000) Pump(10);
                Require(server.BrokenRequests==1,"Critical primary request did not start exactly once");
                while((file.Threads==null || file.Threads.State!=ModNet.NetState.Canceled) && server.BackupRequests==0 && waiting.ElapsedMilliseconds<3000) Pump(10);
                Require(server.BackupRequests>0 || (file.Threads!=null && file.Threads.State==ModNet.NetState.Canceled),"Primary did not create the abandoned in-progress download state");
                waiting.Restart();
                while(server.BackupRequests==0 && waiting.ElapsedMilliseconds<2500) Pump(10);
                Require(server.BackupRequests>0,"An abandoned dependency did not try its backup while another file kept global speed high");
                Require(bulk.State==ModBase.LoadState.Loading,"Backup was deferred until the bulk transfer finished");
                passed.Add("PASS a partial primary failure resumes from its backup while other downloads remain busy");
                waiting.Restart();
                while(critical.State==ModBase.LoadState.Loading && waiting.ElapsedMilliseconds<4000) Pump(10);
                Require(critical.State==ModBase.LoadState.Finished,"Recovered dependency did not finish");
                byte[] bytes=File.ReadAllBytes(file.LocalPath);
                Require(bytes.Length==2*1024*1024,"Partial fallback merged the wrong file length");
                foreach(byte value in bytes) Require(value==0x5a,"Partial fallback corrupted the merged body");
                Require(server.BrokenRequests==1 && server.BackupRequests==1,"Retry recovery duplicated a live transfer");
                passed.Add("PASS fallback preserves the partial bytes, merges the full body and starts no duplicate transfer");
                var limitedFile=new ModNet.NetFile(new[]{root+"/bmclapi-rate-limited",root+"/rate-backup"},Path.Combine(stage,"rate-limited.bin"),null,false);
                var limitedTask=new ModNet.LoaderDownload("Loopback rate-limited dependency",new[]{limitedFile});
                try {
                    limitedTask.Start(null,false); waiting.Restart();
                    while(limitedTask.State==ModBase.LoadState.Loading && waiting.ElapsedMilliseconds<5000) Pump(10);
                    Require(limitedTask.State==ModBase.LoadState.Finished,"A rate-limited mirror did not recover through its backup");
                    Require(server.RateLimitedRequests==1 && server.RateBackupRequests==1,"A rate-limited mirror was retried instead of switching to its available backup");
                    Require(new FileInfo(limitedFile.LocalPath).Length==2*1024*1024,"Rate-limit fallback produced an incomplete file");
                    passed.Add("PASS a mirror returning HTTP 429 switches to its backup without repeated rate-limited requests");
                } finally {limitedTask.Cancel();}
                byte[] expected=new byte[2*1024*1024];
                for(int i=0;i<expected.Length;i++) expected[i]=0x5a;
                string digest;
                using(var sha=SHA256.Create()) digest=BitConverter.ToString(sha.ComputeHash(expected)).Replace("-","");
                var updater=typeof(ModNet).Assembly.GetType("PCL.ModBrclUpdate",true);
                var download=updater.GetMethod("DownloadUpdatePackage",BindingFlags.Static|BindingFlags.NonPublic);
                string updateFile=Path.Combine(stage,"update.zip");
                download.Invoke(null,new object[]{new[]{root+"/update-broken/",root+"/update-backup/"},"release.zip",updateFile,(long)expected.Length,digest});
                Require(Interlocked.Read(ref server.UpdateResumeOffset)==65536,"Updater did not resume at the exact failed-source offset");
                Require(new FileInfo(updateFile).Length==expected.Length,"Updater produced an incomplete file");
                passed.Add("PASS updater resumes from its backup at the exact Range offset and verifies the assembled SHA-256");
            } finally {
                if(critical!=null) critical.Cancel(); if(bulk!=null) bulk.Cancel();
                var waiting=Stopwatch.StartNew(); while(ModNet.NetTaskThreadCount>0 && waiting.ElapsedMilliseconds<3000) Pump(10);
            }
        }
        Require(ModNet.NetTaskThreadCount==0,"Recovery fixtures left active transfers behind");
        passed.Add("PASS cancellation releases the bulk transfer and all fixture network threads");
        return passed.ToArray();
    }
}
'@
$sourceFile=Join-Path $stage 'DownloadRecoveryChecks.cs'
$harness=Join-Path $stage 'DownloadRecoveryChecks.dll'
[IO.File]::WriteAllText($sourceFile,$source,[Text.UTF8Encoding]::new($false))
$facade=[Reflection.Assembly]::Load('netstandard, Version=2.0.0.0, Culture=neutral, PublicKeyToken=cc7b13ffcd2ddd51').Location
$references=@($assembly.Location,[Windows.Application].Assembly.Location,[Windows.Threading.Dispatcher].Assembly.Location,[Windows.Media.Visual].Assembly.Location,[System.Xaml.XamlReader].Assembly.Location,(Join-Path $BuildDirectory 'MeloongCore.dll'),$facade)|ForEach-Object {'/reference:'+$_}
& "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:library "/out:$harness" @references $sourceFile
if($LASTEXITCODE -ne 0){throw 'Download recovery harness compilation failed'}
[Reflection.Assembly]::LoadFrom($harness)|Out-Null
$exitCode=0
try {[DownloadRecoveryChecks]::Run($stage)|Write-Output} catch {Write-Output $_.Exception.GetBaseException().Message; $exitCode=1} finally {$app.Shutdown()}
# The production download manager owns foreground scheduler threads. This
# isolated PowerShell child must terminate them even when an assertion fails.
[Environment]::Exit($exitCode)
