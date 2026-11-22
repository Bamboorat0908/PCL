param([int]$Parallel = 16, [switch]$LowPreset, [string]$BaseUrl, [switch]$NoProxy)
$ErrorActionPreference = 'Stop'
if ($Parallel -lt 1 -or $Parallel -gt 64) { throw 'Parallel must be 1..64' }
Add-Type -AssemblyName System.Net.Http
$code = @'
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Threading.Tasks;
public static class BrclDownloadProbe {
    public static async Task<string> Run(string output, int parallel, long total, string url, string expectedHash, bool useProxy) {

        ServicePointManager.DefaultConnectionLimit = parallel;
        ServicePointManager.Expect100Continue = false;
        using (var handler = new HttpClientHandler { AllowAutoRedirect = false, MaxConnectionsPerServer = parallel, UseProxy = useProxy })
        using (var client = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(5) }) {
            var tasks = Enumerable.Range(0, parallel).Select(async i => {
                long start = total * i / parallel;
                long end = total * (i + 1) / parallel - 1;
                using (var request = new HttpRequestMessage(HttpMethod.Get, url)) {
                    request.Headers.Range = new RangeHeaderValue(start, end);
                    using (var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false)) {
                        if (response.StatusCode != HttpStatusCode.PartialContent) throw new Exception("Expected 206: " + response.StatusCode);
                        var range = response.Content.Headers.ContentRange;
                        if (range == null || range.From != start || range.To != end || range.Length != total) throw new Exception("Wrong Content-Range");
                        var data = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                        if (data.LongLength != end - start + 1) throw new Exception("Truncated response: " + i);
                        return data;
                    }
                }
            }).ToArray();
            var parts = await Task.WhenAll(tasks).ConfigureAwait(false);
            using (var stream = File.Create(output)) foreach (var part in parts) stream.Write(part, 0, part.Length);
        }
        using (var sha = SHA256.Create())
        using (var stream = File.OpenRead(output)) {
            var hash = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
            if (hash != expectedHash) throw new Exception("ZIP SHA-256 mismatch");
            return hash;
        }
    }
}
'@
Add-Type -TypeDefinition $code -ReferencedAssemblies System.Net.Http
$out = Join-Path $env:TEMP ('BRCL-public-download-' + [Guid]::NewGuid().ToString('N') + '.zip')
$watch = [Diagnostics.Stopwatch]::StartNew()
$assembly = [Reflection.Assembly]::LoadFrom((Join-Path $PSScriptRoot '..\Plain Craft Launcher 2\bin\BRCL.exe'))
$module = $assembly.GetType('BRCL.ModBrsPack', $true)
$preset = $module.GetMethod('GetBrsPackPreset', [Reflection.BindingFlags]'Public,NonPublic,Static').Invoke($null, @([bool]$LowPreset))
$url = if ($BaseUrl) { $BaseUrl.TrimEnd('/') + '/modpacks/' + $preset.FileName } else { $preset.Url }
$hash = [BrclDownloadProbe]::Run($out, $Parallel, $preset.Size, $url, $preset.Hash, (-not $NoProxy)).GetAwaiter().GetResult()
Write-Output ("PASS .NET Framework HttpClient: {0} parallel Range requests, complete ZIP, SHA-256 {1}, {2:N1}s" -f $Parallel, $hash, $watch.Elapsed.TotalSeconds)
Write-Output $out
