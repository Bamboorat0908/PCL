$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationFramework,PresentationCore,WindowsBase,System.Net.Http
$project = Join-Path $PSScriptRoot '..\Plain Craft Launcher 2'
$assembly = [Reflection.Assembly]::LoadFrom((Join-Path $project 'bin\BRCL.exe'))
[Windows.Application]::ResourceAssembly = $assembly
$app = [Activator]::CreateInstance($assembly.GetType('PCL.Application',$true),$true)
$app.InitializeComponent()

# A real HTTP server bound only to loopback; no Microsoft account or external API is used.
Add-Type -TypeDefinition @'
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

public sealed class BrclResponseServer : IDisposable {
    private readonly TcpListener listener;
    private readonly Thread acceptThread;
    private readonly ConcurrentDictionary<string, int> counts = new ConcurrentDictionary<string, int>();
    private readonly ConcurrentDictionary<string, Stopwatch> clocks = new ConcurrentDictionary<string, Stopwatch>();
    private volatile bool stopped;
    public int Port { get; private set; }
    public BrclResponseServer() {
        listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        Port = ((IPEndPoint)listener.LocalEndpoint).Port;
        acceptThread = new Thread(Accept) { IsBackground = true };
        acceptThread.Start();
    }
    private void Accept() {
        while (!stopped) {
            try {
                var client = listener.AcceptTcpClient();
                ThreadPool.QueueUserWorkItem(_ => Respond(client));
            } catch (SocketException) { if (!stopped) throw; }
            catch (ObjectDisposedException) { return; }
        }
    }
    private void Respond(TcpClient client) {
        try {
            using (client)
            using (var stream = client.GetStream())
            using (var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, true)) {
                var request = reader.ReadLine();
                if (request == null) return;
                string line;
                do { line = reader.ReadLine(); } while (!String.IsNullOrEmpty(line));
                string path = request.Split(' ')[1];
                int number = counts.AddOrUpdate(path, 1, (_, value) => value + 1);
                bool failure = path.StartsWith("/fail", StringComparison.Ordinal);
                if (path.StartsWith("/hedge/", StringComparison.Ordinal)) Thread.Sleep(number == 1 ? 900 : 15);
                if (path.StartsWith("/cancel/", StringComparison.Ordinal)) Thread.Sleep(1500);
                if (path.StartsWith("/fail-together/", StringComparison.Ordinal)) {
                    var clock = clocks.GetOrAdd(path, _ => Stopwatch.StartNew());
                    int remaining = 950 - (int)clock.ElapsedMilliseconds;
                    if (remaining > 0) Thread.Sleep(remaining);
                }
                string body = failure ? "{\"error\":\"offline-test\"}" : "{\"ok\":true}";
                byte[] bytes = Encoding.UTF8.GetBytes("HTTP/1.1 " + (failure ? "500 Test Failure" : "200 OK") +
                    "\r\nContent-Type: application/json\r\nCache-Control: no-store\r\nConnection: close\r\nContent-Length: " +
                    Encoding.UTF8.GetByteCount(body) + "\r\n\r\n" + body);
                stream.Write(bytes, 0, bytes.Length);
            }
        } catch (IOException) { } catch (SocketException) { }
    }
    public int Count(string path) { int value; return counts.TryGetValue(path, out value) ? value : 0; }
    public static string Invoke(MethodInfo method, object[] arguments) {
        var task = Task.Factory.StartNew(() => (string)method.Invoke(null, arguments));
        try {
            if (!task.Wait(6000)) throw new TimeoutException("Network response test hung for six seconds.");
            return task.Result;
        } catch (AggregateException ex) {
            throw ex.GetBaseException();
        }
    }
    public bool CancellationStopsCaller(MethodInfo method, object[] arguments, string path) {
        Exception failure = null;
        var caller = new Thread(() => {
            try { method.Invoke(null, arguments); } catch (Exception ex) { failure = ex.GetBaseException(); }
        }) { IsBackground = true };
        caller.Start();
        var waiting = Stopwatch.StartNew();
        while (Count(path) == 0 && waiting.ElapsedMilliseconds < 4000) Thread.Sleep(5);
        if (Count(path) == 0) return false;
        caller.Interrupt();
        return caller.Join(2000) && failure is ThreadInterruptedException && Count(path) == 1;
    }
    public void Dispose() { stopped = true; listener.Stop(); acceptThread.Join(1000); }
}
'@

$method = $assembly.GetType('PCL.ModNet',$true).GetMethod('NetRequestByClientMultiple')
$server = [BrclResponseServer]::new()
$script:passed = 0
function Check($condition, [string]$message) {
    if (-not $condition) { throw $message }
    $script:passed++
    Write-Output "PASS $message"
}
function Request([string]$path, [int]$threads = 3) {
    $arguments = [object[]]@("http://127.0.0.1:$($server.Port)$path", [Net.Http.HttpMethod]::Get, $threads,
        $null, $null, '*/*', $null, $true, 2000, $null, $false)
    return [BrclResponseServer]::Invoke($method, $arguments)
}
try {
    # Warm the assembly, cache handler and JIT before measuring the fixed 250 ms delay.
    Request '/warm/1' | Out-Null
    Request '/warm/2' | Out-Null
    $durations = @()
    for ($i = 0; $i -lt 3; $i++) {
        $watch = [Diagnostics.Stopwatch]::StartNew()
        $result = Request "/fast/$i"
        $watch.Stop()
        $durations += $watch.ElapsedMilliseconds
        Check ($result -eq '{"ok":true}' -and $server.Count("/fast/$i") -eq 1) "fast response $i returns the result without starting backup requests"
    }
    $median = ($durations | Sort-Object)[1]
    Check ($median -lt 220) "fast-response median ${median}ms has no fixed 250ms sleep"
    $watch = [Diagnostics.Stopwatch]::StartNew()
    Request '/hedge/1' | Out-Null
    $watch.Stop()
    Check ($watch.ElapsedMilliseconds -ge 230 -and $watch.ElapsedMilliseconds -lt 650 -and $server.Count('/hedge/1') -eq 2) "backup success wakes the caller promptly ($($watch.ElapsedMilliseconds)ms)"
    $watch = [Diagnostics.Stopwatch]::StartNew()
    $singleFailure = $null
    try { Request '/fail-single/1' 1 | Out-Null } catch { $singleFailure = $_.Exception }
    $watch.Stop()
    Check ($null -ne $singleFailure -and $server.Count('/fail-single/1') -eq 1 -and $watch.ElapsedMilliseconds -lt 220) 'single failed request completes without a fixed sleep'
    $failure = $null
    try { Request '/fail-fast/1' | Out-Null } catch { $failure = $_.Exception }
    Check ($null -ne $failure -and $server.Count('/fail-fast/1') -eq 3 -and $failure.ToString().Contains('500')) 'all failed attempts preserve the HTTP error and complete'
    for ($i = 0; $i -lt 4; $i++) {
        $watch = [Diagnostics.Stopwatch]::StartNew()
        $failure = $null
        try { Request "/fail-together/$i" | Out-Null } catch { $failure = $_.Exception }
        $watch.Stop()
        Check ($null -ne $failure -and $server.Count("/fail-together/$i") -eq 3 -and $watch.ElapsedMilliseconds -lt 2500 -and $failure.ToString().Contains('500')) "concurrent failure accounting completes without hanging (run $i)"
    }
    $invalid = $null
    try { Request '/invalid' 0 | Out-Null } catch { $invalid = $_.Exception }
    Check ($null -ne $invalid -and $invalid.ToString().Contains('ArgumentOutOfRangeException') -and $server.Count('/invalid') -eq 0) 'invalid thread count fails before issuing a request'
    $cancelArguments = [object[]]@("http://127.0.0.1:$($server.Port)/cancel/1", [Net.Http.HttpMethod]::Get, 3,
        $null, $null, '*/*', $null, $true, 2000, $null, $false)
    Check ($server.CancellationStopsCaller($method, $cancelArguments, '/cancel/1')) 'cancellation releases the waiting caller and does not start additional requests'
    $retry = $assembly.GetType('PCL.ModNet',$true).GetMethod('NetRequestByClientRetry')
    $retryArguments = [object[]]@("http://127.0.0.1:$($server.Port)/fail-primary", [Net.Http.HttpMethod]::Get,
        "http://127.0.0.1:$($server.Port)/backup", $null, $null, '*/*', $null, $true, $null, $false)
    $result = [BrclResponseServer]::Invoke($retry, $retryArguments)
    Check ($result -eq '{"ok":true}' -and $server.Count('/fail-primary') -eq 1 -and $server.Count('/backup') -eq 1) 'failed primary request switches to its configured backup URL'
    $download = $assembly.GetType('PCL.ModNet',$true).GetMethod('NetDownloadByClient')
    $target = [IO.Path]::GetTempFileName()
    try {
        [IO.File]::WriteAllText($target, 'previous copy')
        $failed = $false
        try { $download.Invoke($null, [object[]]@("http://127.0.0.1:$($server.Port)/fail-download", $target, $false)) }
        catch { $failed = $true }
        Check ($failed -and [IO.File]::ReadAllText($target) -eq 'previous copy') 'failed replacement preserves the previous downloaded file'
        [void]$download.Invoke($null, [object[]]@("http://127.0.0.1:$($server.Port)/replacement", $target, $false))
        Check ([IO.File]::ReadAllText($target) -eq '{"ok":true}') 'successful replacement publishes the new complete file'
    } finally { [IO.File]::Delete($target) }
    $revocation = $assembly.GetType('PCL.ModNet',$true).GetMethod('UpdateCertificateRevocationCheck')
    foreach ($enabled in @($false, $true)) {
        [void]$revocation.Invoke($null, [object[]]@($enabled))
        Check ([Net.ServicePointManager]::CheckCertificateRevocationList -eq $enabled -and
               $null -eq [Net.ServicePointManager]::ServerCertificateValidationCallback) "revocation preference ($enabled) leaves Windows certificate trust validation in place"
    }
    Write-Output "Network response: $script:passed checks passed; all requests stayed on loopback."
} finally {
    $server.Dispose()
    $app.Shutdown()
}
