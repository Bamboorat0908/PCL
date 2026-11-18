# Run with Windows PowerShell 5.1 against the built .NET Framework assembly.
# A process-local fake HttpMessageHandler intercepts every request; no account is used.
param([string]$AssemblyPath)
$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot '..\Plain Craft Launcher 2'
if (-not $AssemblyPath) { $AssemblyPath = Join-Path $project 'bin\BRCL.exe' }
Add-Type -AssemblyName PresentationFramework,PresentationCore,WindowsBase,System.Xaml,System.Net.Http
[void][Reflection.Assembly]::LoadFrom((Join-Path $project 'Resources\Newtonsoft.Json.dll'))
$assembly = [Reflection.Assembly]::LoadFrom($AssemblyPath)
[Windows.Application]::ResourceAssembly = $assembly
$app = [Activator]::CreateInstance($assembly.GetType('PCL.Application', $true), $true)
$app.InitializeComponent()

$source = @'
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

public static class BrclMicrosoftLoginPerformance
{
    private const string ProfileId = "0123456789abcdef0123456789abcdef";
    private const string ProfileName = "OfflinePlayer";
    private static readonly string ProfileJson = "{\"id\":\"" + ProfileId + "\",\"name\":\"" + ProfileName + "\",\"skins\":[],\"capes\":[]}";

    private sealed class FakeHandler : HttpMessageHandler
    {
        public readonly string Scenario;
        public int Requests;
        public int EntitlementRequests;
        public int ProfileRequests;
        public int UnexpectedRequests;
        public int ProfileRefreshBeforeEntitlementCompleted;
        private int entitlementCompleted;
        private int activeEntitlements;
        private int activeProfiles;
        private readonly TaskCompletionSource<bool> bothStarted = new TaskCompletionSource<bool>();

        public FakeHandler(string scenario) { Scenario = scenario; }
        public bool RequestsOverlap { get { return bothStarted.Task.IsCompleted; } }

        private static HttpResponseMessage Response(int status, string body)
        {
            return new HttpResponseMessage((HttpStatusCode)status) {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            int attempt = Interlocked.Increment(ref Requests);
            string path = request.RequestUri.AbsolutePath;
            if (Scenario.StartsWith("combined", StringComparison.Ordinal)) {
                bool entitlement = path == "/entitlements/mcstore";
                bool profile = path == "/minecraft/profile";
                if (request.RequestUri.Host != "api.minecraftservices.com" || (!entitlement && !profile)) {
                    Interlocked.Increment(ref UnexpectedRequests);
                    return Task.FromResult(Response(400, "{\"error\":\"unexpected_offline_request\"}"));
                }
                if (entitlement) {
                    Interlocked.Increment(ref EntitlementRequests);
                    Interlocked.Increment(ref activeEntitlements);
                }
                int profileAttempt = profile ? Interlocked.Increment(ref ProfileRequests) : 0;
                if (profile) Interlocked.Increment(ref activeProfiles);
                if (Scenario == "combined-xgp-refresh" && profileAttempt > 1 &&
                    Interlocked.CompareExchange(ref entitlementCompleted, 0, 0) == 0)
                    Interlocked.Increment(ref ProfileRefreshBeforeEntitlementCompleted);
                if (Interlocked.CompareExchange(ref activeEntitlements, 0, 0) > 0 &&
                    Interlocked.CompareExchange(ref activeProfiles, 0, 0) > 0) bothStarted.TrySetResult(true);
                return CombinedResponse(entitlement, profileAttempt, cancellationToken);
            }
            if (request.RequestUri.Host != "login.microsoftonline.com" || path != "/offline-test/login") {
                Interlocked.Increment(ref UnexpectedRequests);
                return Task.FromResult(Response(400, "{\"error\":\"unexpected_offline_request\"}"));
            }
            int status = Scenario == "400" ? 400 : Scenario == "429" ? 429 :
                Scenario == "503" || (Scenario == "503-then-success" && attempt == 1) ? 503 : 200;
            return Task.FromResult(Response(status, status == 200 ? "{\"ok\":true}" : "{\"error\":\"offline_test\"}"));
        }

        private async Task<HttpResponseMessage> CombinedResponse(bool entitlement, int profileAttempt, CancellationToken cancellationToken)
        {
            try {
                // Both requests must be active before either response is released. A serial
                // implementation is released after one second so it fails without hanging.
                await Task.WhenAny(bothStarted.Task, Task.Delay(1000, cancellationToken)).ConfigureAwait(false);
                await Task.Delay(Scenario == "combined-xgp-refresh" ? (entitlement ? 120 : 10) : 30, cancellationToken).ConfigureAwait(false);
                if (entitlement && Scenario == "combined-entitlement-failure")
                    return Response(500, "{\"error\":\"offline_entitlement_failure\"}");
                if (!entitlement && Scenario == "combined-profile-failure")
                    return Response(401, "{\"error\":\"offline_profile_failure\"}");
                // The first profile response precedes the entitlement/XGP refresh.
                if (!entitlement && Scenario == "combined-xgp-refresh" && profileAttempt == 1)
                    return Response(404, "{\"error\":\"offline_profile_not_ready\"}");
                if (entitlement) Interlocked.Exchange(ref entitlementCompleted, 1);
                return Response(200, entitlement ? "{\"items\":[{\"name\":\"game_minecraft\"}]}" : ProfileJson);
            }
            finally {
                if (entitlement) Interlocked.Decrement(ref activeEntitlements);
                else Interlocked.Decrement(ref activeProfiles);
            }
        }
    }

    private static Exception Unwrap(Exception error)
    {
        while (true) {
            TargetInvocationException invocation = error as TargetInvocationException;
            if (invocation != null && invocation.InnerException != null) { error = invocation.InnerException; continue; }
            AggregateException aggregate = error as AggregateException;
            if (aggregate != null && aggregate.InnerExceptions.Count == 1) { error = aggregate.InnerExceptions[0]; continue; }
            return error;
        }
    }

    private static object Invoke(MethodInfo method, object[] arguments)
    {
        try { return method.Invoke(null, arguments); }
        catch (Exception error) { throw Unwrap(error); }
    }

    private static object OnWorker(Func<object> action)
    {
        object result = null;
        Exception failure = null;
        Thread worker = new Thread(delegate() {
            try { result = action(); }
            catch (Exception error) { failure = Unwrap(error); }
        });
        worker.IsBackground = true;
        worker.Name = "Offline Microsoft login regression";
        worker.Start();
        if (!worker.Join(15000)) throw new Exception("Offline Microsoft login case exceeded its 15-second bound.");
        if (failure != null) throw failure;
        return result;
    }

    private static void Check(bool condition, string name, List<string> results)
    {
        if (!condition) throw new Exception("FAIL " + name);
        results.Add("PASS " + name);
    }

    private static object Request(MethodInfo request)
    {
        return Invoke(request, new object[] { "https://login.microsoftonline.com/offline-test/login", HttpMethod.Post,
            "offline=1", "application/x-www-form-urlencoded", null });
    }

    private static Exception RequestFailure(MethodInfo request)
    {
        try { Request(request); return null; }
        catch (Exception failure) { return Unwrap(failure); }
    }

    private static int StatusCode(Exception failure)
    {
        if (failure == null) return 0;
        PropertyInfo property = failure.GetType().GetProperty("StatusCode");
        return property == null ? 0 : Convert.ToInt32(property.GetValue(failure, null));
    }

    private static object Combined(MethodInfo combined, bool canceled = false)
    {
        Type loaderType = combined.GetParameters()[1].ParameterType;
        object loader = Activator.CreateInstance(loaderType);
        // IsCanceled is meaningful only on the loader's owning worker thread.
        loaderType.GetField("LastRunningThread").SetValue(loader, Thread.CurrentThread);
        if (canceled) {
            FieldInfo state = loaderType.BaseType.GetField("_State", BindingFlags.NonPublic | BindingFlags.Instance);
            state.SetValue(loader, Enum.Parse(state.FieldType, "Canceled"));
        }
        return Invoke(combined, new object[] { "offline-test-access-token", loader });
    }

    private static string TupleField(object tuple, string field)
    {
        return (string)tuple.GetType().GetField(field).GetValue(tuple);
    }

    public static string[] Run(Assembly assembly)
    {
        BindingFlags privateStatic = BindingFlags.NonPublic | BindingFlags.Static;
        Type launch = assembly.GetType("PCL.ModLaunch", true);
        MethodInfo request = launch.GetMethod("MsLoginRequest", privateStatic);
        MethodInfo combined = launch.GetMethod("MsLoginStep5And6", privateStatic);
        if (request == null || combined == null) throw new Exception("Build is missing the Microsoft login performance entry points.");
        FieldInfo clientField = assembly.GetType("PCL.ModNet", true).GetField("RequestClient", privateStatic);
        if (clientField == null) throw new Exception("RequestClient injection point is missing.");
        // Initialize the launcher's UI-thread marker here, before executing network code on workers.
        assembly.GetType("PCL.ModBase", true).GetMethod("RunInUi", BindingFlags.Public | BindingFlags.Static,
            null, Type.EmptyTypes, null).Invoke(null, null);
        object previousClient = clientField.GetValue(null);
        List<string> results = new List<string>();
        try {
            foreach (string scenario in new string[] { "400", "429" }) {
                using (FakeHandler handler = new FakeHandler(scenario))
                using (HttpClient client = new HttpClient(handler)) {
                    clientField.SetValue(null, client);
                    Exception failure = (Exception)OnWorker(delegate { return RequestFailure(request); });
                    Check(StatusCode(failure) == Int32.Parse(scenario) && handler.Requests == 1 && handler.UnexpectedRequests == 0,
                        scenario + " returns after one request", results);
                }
            }
            using (FakeHandler handler = new FakeHandler("503-then-success"))
            using (HttpClient client = new HttpClient(handler)) {
                clientField.SetValue(null, client);
                string response = (string)OnWorker(delegate { return Request(request); });
                Check(response == "{\"ok\":true}" && handler.Requests == 2 && handler.UnexpectedRequests == 0,
                    "503 retries once and recovers", results);
            }
            using (FakeHandler handler = new FakeHandler("503"))
            using (HttpClient client = new HttpClient(handler)) {
                clientField.SetValue(null, client);
                Exception failure = (Exception)OnWorker(delegate { return RequestFailure(request); });
                Check(StatusCode(failure) == 503 && handler.Requests == 2 && handler.UnexpectedRequests == 0,
                    "persistent 503 stops after the second request", results);
            }
            using (FakeHandler handler = new FakeHandler("success"))
            using (HttpClient client = new HttpClient(handler)) {
                clientField.SetValue(null, client);
                double medianMs = (double)OnWorker(delegate {
                    Request(request); // Warm reflection, serialization, and request construction.
                    double[] samples = new double[5];
                    for (int i = 0; i < samples.Length; i++) {
                        Stopwatch watch = Stopwatch.StartNew();
                        Request(request);
                        samples[i] = watch.Elapsed.TotalMilliseconds;
                    }
                    Array.Sort(samples);
                    return samples[2];
                });
                Check(medianMs < 200 && handler.Requests == 6 && handler.UnexpectedRequests == 0,
                    "immediate responses have no fixed 250ms wait (median " + medianMs.ToString("0.0") + "ms)", results);
            }
            using (FakeHandler handler = new FakeHandler("combined-success"))
            using (HttpClient client = new HttpClient(handler)) {
                clientField.SetValue(null, client);
                object profile = OnWorker(delegate { return Combined(combined); });
                Check(handler.RequestsOverlap && handler.EntitlementRequests == 1 && handler.ProfileRequests == 1 && handler.UnexpectedRequests == 0,
                    "entitlement and profile requests overlap without duplicate requests", results);
                Check(TupleField(profile, "Item1") == ProfileId && TupleField(profile, "Item2") == ProfileName &&
                    TupleField(profile, "Item3") == ProfileJson,
                    "parallel validation preserves the complete player profile", results);
            }
            using (FakeHandler handler = new FakeHandler("combined-xgp-refresh"))
            using (HttpClient client = new HttpClient(handler)) {
                clientField.SetValue(null, client);
                object profile = OnWorker(delegate { return Combined(combined); });
                Check(handler.RequestsOverlap && handler.EntitlementRequests == 1 && handler.ProfileRequests == 2 &&
                    handler.ProfileRefreshBeforeEntitlementCompleted == 0 && handler.UnexpectedRequests == 0 &&
                    TupleField(profile, "Item1") == ProfileId && TupleField(profile, "Item2") == ProfileName,
                    "early profile 404 is rechecked after entitlement refresh and recovers", results);
            }
            foreach (string scenario in new string[] { "combined-entitlement-failure", "combined-profile-failure" }) {
                using (FakeHandler handler = new FakeHandler(scenario))
                using (HttpClient client = new HttpClient(handler)) {
                    clientField.SetValue(null, client);
                    Exception failure = (Exception)OnWorker(delegate {
                        try { Combined(combined); return null; }
                        catch (Exception error) { return Unwrap(error); }
                    });
                    bool entitlementFailure = scenario == "combined-entitlement-failure";
                    Check(failure != null && handler.RequestsOverlap && handler.UnexpectedRequests == 0 &&
                        StatusCode(failure) == (entitlementFailure ? 500 : 401) &&
                        handler.EntitlementRequests == (entitlementFailure ? 2 : 1) && handler.ProfileRequests == 1,
                        entitlementFailure ? "failed ownership validation cannot return a successful profile" :
                            "failed profile validation cannot return login success", results);
                }
            }
            using (FakeHandler handler = new FakeHandler("combined-success"))
            using (HttpClient client = new HttpClient(handler)) {
                clientField.SetValue(null, client);
                Exception failure = (Exception)OnWorker(delegate {
                    try { Combined(combined, true); return null; }
                    catch (Exception error) { return Unwrap(error); }
                });
                Check(failure is OperationCanceledException && handler.UnexpectedRequests == 0,
                    "canceled loader cannot return login success", results);
            }
        }
        finally { clientField.SetValue(null, previousClient); }
        results.Add("Microsoft login performance: " + results.Count + " checks passed; every HTTP response was generated offline.");
        return results.ToArray();
    }
}
'@

try {
    Add-Type -TypeDefinition $source -ReferencedAssemblies System.Net.Http,System.Core
    [BrclMicrosoftLoginPerformance]::Run($assembly)
} finally {
    $app.Shutdown()
}
