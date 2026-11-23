# Bank session lifecycle against a loopback stand-in for services/bank: an expired or revoked
# bank session is renewed transparently, while a rejected Minecraft login stays final.
param([string]$OutputDirectory = 'bin')
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http
# The launcher disables Expect: 100-continue at startup; match it so requests are sent in one piece.
[Net.ServicePointManager]::Expect100Continue = $false
$project = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\Plain Craft Launcher 2'))
[void][Reflection.Assembly]::LoadFrom((Join-Path $project 'Resources\Newtonsoft.Json.dll'))
$assembly = [Reflection.Assembly]::LoadFrom((Join-Path $project "$OutputDirectory\BRCL.exe"))
Add-Type -Language CSharp @'
using System; using System.Collections.Generic; using System.IO; using System.Net; using System.Net.Sockets; using System.Text; using System.Threading;
public sealed class FakeBank : IDisposable {
    readonly TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
    readonly HashSet<string> sessions = new HashSet<string>();
    readonly object gate = new object();
    public string Player = "11111111-1111-1111-1111-111111111111";
    public string Account = "22222222-2222-2222-2222-222222222222";
    public string MinecraftToken = "minecraft-access-token";
    public volatile bool RejectLogin;
    public volatile int SessionSeconds = 1800;
    public volatile bool DelayNextRejection;
    public readonly ManualResetEventSlim RejectionHeld = new ManualResetEventSlim();
    public readonly ManualResetEventSlim ReleaseRejection = new ManualResetEventSlim();
    public int SessionsIssued, Unauthorized, Reads;
    public int Port { get { return ((IPEndPoint)listener.LocalEndpoint).Port; } }
    public FakeBank() { listener.Start(); new Thread(Serve) { IsBackground = true }.Start(); }
    public void Revoke() { lock (gate) sessions.Clear(); }
    void Serve() {
        while (true) {
            TcpClient client;
            try { client = listener.AcceptTcpClient(); } catch { return; }
            ThreadPool.QueueUserWorkItem(state => {
                using (var connection = (TcpClient)state) using (var stream = connection.GetStream()) {
                    try { Handle(stream); } catch (IOException) { }
                }
            }, client);
        }
    }
    static string ReadLine(Stream s) { var b = new List<byte>(); int c; while ((c = s.ReadByte()) >= 0) { if (c == '\n') break; if (c != '\r') b.Add((byte)c); } return Encoding.ASCII.GetString(b.ToArray()); }
    void Handle(Stream stream) {
        var request = ReadLine(stream).Split(' ');
        string auth = "", line; int length = 0;
        while ((line = ReadLine(stream)).Length > 0) {
            var i = line.IndexOf(':'); if (i < 0) continue;
            var name = line.Substring(0, i).Trim().ToLowerInvariant(); var value = line.Substring(i + 1).Trim();
            if (name == "authorization" && value.StartsWith("Bearer ")) auth = value.Substring(7);
            if (name == "content-length") length = int.Parse(value);
        }
        for (int i = 0; i < length; i++) stream.ReadByte();
        var path = request[1];
        int status = 200; string body; bool delayResponse = false;
        lock (gate) {
            if (request[0] == "POST" && path == "/bank/v1/session") {
                if (RejectLogin || auth != MinecraftToken) { status = 401; body = "{\"error\":\"unauthorized\"}"; Unauthorized++; }
                else {
                    var token = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
                    sessions.Add(token); SessionsIssued++;
                    var expires = DateTimeOffset.UtcNow.AddSeconds(SessionSeconds).ToUnixTimeMilliseconds();
                    body = "{\"token\":\"" + token + "\",\"expiresAt\":" + expires + ",\"player\":{\"uuid\":\"" + Player + "\"}}";
                }
            } else if (!sessions.Contains(auth)) {
                status = 401; body = "{\"error\":\"unauthorized\"}"; Unauthorized++;
                delayResponse = DelayNextRejection;
                DelayNextRejection = false;
            }
            else if (request[0] == "GET" && path == "/bank/v1/accounts") {
                Reads++;
                var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                body = "{\"currency\":\"sdmcoin\",\"unit\":\"copper\",\"capturedAt\":" + now + ",\"accounts\":[{\"id\":\"" + Account + "\",\"type\":\"team\",\"nativeType\":\"BLAZE_BANKER\",\"label\":\"Test\",\"balance\":\"100\",\"visibleBalance\":\"100\",\"reserveBalance\":\"0\",\"grants\":[{\"principalId\":\"" + Player + "\",\"role\":\"trusted\"}]}]}";
            } else if (request[0] == "GET" && path.StartsWith("/bank/v1/accounts/" + Account + "/history")) {
                Reads++;
                var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                body = "{\"accountId\":\"" + Account + "\",\"currency\":\"sdmcoin\",\"unit\":\"copper\",\"source\":\"observed_snapshots\",\"points\":[{\"capturedAt\":" + (now - 1000) + ",\"balance\":\"90\"},{\"capturedAt\":" + now + ",\"balance\":\"100\"}]}";
            } else { status = 404; body = "{}"; }
        }
        if (delayResponse) {
            RejectionHeld.Set();
            if (!ReleaseRejection.Wait(10000)) return;
        }
        var payload = Encoding.UTF8.GetBytes(body);
        var head = Encoding.ASCII.GetBytes("HTTP/1.1 " + status + (status == 200 ? " OK" : " Error") + "\r\nContent-Type: application/json\r\nContent-Length: " + payload.Length + "\r\nConnection: close\r\n\r\n");
        stream.Write(head, 0, head.Length); stream.Write(payload, 0, payload.Length); stream.Flush();
    }
    public void Dispose() { ReleaseRejection.Set(); listener.Stop(); }
}
'@
function Require([bool]$value, [string]$message) { if (-not $value) { throw $message } }
$clientType = $assembly.GetType('BRCL.BrclBank+BankClient', $true)
$server = New-Object FakeBank
$client = $null
try {
    $client = [Activator]::CreateInstance($clientType, @([Uri]"http://127.0.0.1:$($server.Port)/bank/v1/"))
    $none = [Threading.CancellationToken]::None
    $accounts = $client.GetAccounts($server.MinecraftToken, $server.Player, $none).GetAwaiter().GetResult()
    Require ($accounts.Accounts.Count -eq 1 -and $server.SessionsIssued -eq 1) 'Initial account read failed.'
    $history = $client.GetHistory($server.MinecraftToken, $server.Player, $server.Account, 7, $none).GetAwaiter().GetResult()
    Require ($history.Count -eq 2 -and $server.SessionsIssued -eq 1) 'A valid session was not reused for the history read.'
    Write-Output 'PASS a valid bank session is reused across account and history reads'

    $client.Dispose()
    $client = [Activator]::CreateInstance($clientType, @([Uri]"http://127.0.0.1:$($server.Port)/bank/v1/"))
    $server.SessionSeconds = 5
    [void]$client.GetAccounts($server.MinecraftToken, $server.Player, $none).GetAwaiter().GetResult()
    $server.SessionSeconds = 1800
    $before = $server.Unauthorized
    $history = $client.GetHistory($server.MinecraftToken, $server.Player, $server.Account, 7, $none).GetAwaiter().GetResult()
    Require ($history.Count -eq 2 -and $server.SessionsIssued -eq 3 -and $server.Unauthorized -eq $before) 'An expired session was not renewed before the history read.'
    Write-Output 'PASS a session near expiry is renewed before reading history'

    $server.Revoke()
    $history = $client.GetHistory($server.MinecraftToken, $server.Player, $server.Account, 30, $none).GetAwaiter().GetResult()
    Require ($history.Count -eq 2 -and $server.SessionsIssued -eq 4) 'A revoked session was not renewed transparently.'
    $server.Revoke()
    $accounts = $client.GetAccounts($server.MinecraftToken, $server.Player, $none).GetAwaiter().GetResult()
    Require ($accounts.Accounts.Count -eq 1 -and $server.SessionsIssued -eq 5) 'A revoked session was not renewed for the account read.'
    Write-Output 'PASS a session revoked by a service restart is renewed once for account and history reads'

    $server.Revoke()
    $server.DelayNextRejection = $true
    $issued = $server.SessionsIssued
    $overlap = $client.GetHistory($server.MinecraftToken, $server.Player, $server.Account, 7, $none)
    try {
        Require ($server.RejectionHeld.Wait(5000)) 'The first stale-session response was not held.'
        $accounts = $client.GetAccounts($server.MinecraftToken, $server.Player, $none).GetAwaiter().GetResult()
        Require ($accounts.Accounts.Count -eq 1 -and $server.SessionsIssued -eq $issued + 1) 'The overlapping read did not renew the stale session.'
    } finally { $server.ReleaseRejection.Set() }
    $history = $overlap.GetAwaiter().GetResult()
    Require ($history.Count -eq 2 -and $server.SessionsIssued -eq $issued + 1) 'The late rejection discarded a newer session.'
    Write-Output 'PASS overlapping stale reads share one renewal even when an old rejection arrives late'

    $server.Revoke(); $server.RejectLogin = $true
    $issued = $server.SessionsIssued; $rejected = $server.Unauthorized
    $code = $null
    try { [void]$client.GetHistory($server.MinecraftToken, $server.Player, $server.Account, 7, $none).GetAwaiter().GetResult() } catch { $code = $_.Exception.InnerException.Code; if (-not $code) { $code = $_.Exception.Code } }
    Require ($code -eq 'unauthorized') "A rejected Minecraft login did not surface as unauthorized (got '$code')."
    Require ($server.SessionsIssued -eq $issued -and $server.Unauthorized -eq $rejected + 2) 'A rejected login was retried more than once.'
    Write-Output 'PASS a rejected Minecraft login stays final after a single renewal attempt'
    Write-Output '5 bank session checks passed.'
} finally {
    if ($client) { $client.Dispose() }
    $server.Dispose()
}
