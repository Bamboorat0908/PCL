Imports System.Diagnostics
Imports System.IO
Imports System.Net
Imports System.Text.RegularExpressions
Imports System.Threading
Imports System.Threading.Tasks

''' <summary>Java 专用下载源：仅映射经过确认的运行时资源，测速不下载完整安装包。</summary>
Public Module ModJavaDownloadSources

    Friend Function JavaDownloadHostName(Host As String) As String
        Select Case Host.ToLowerInvariant()
            Case "github.com" : Return "Adoptium 官方"
            Case "piston-data.mojang.com", "launcher.mojang.com" : Return "Mojang 官方"
            Case "bmclapi2.bangbang93.com" : Return "BMCLAPI"
            Case "mirrors.tuna.tsinghua.edu.cn" : Return "清华 TUNA"
            Case "mirrors.nju.edu.cn" : Return "南京 NJU"
            Case Else : Return Host
        End Select
    End Function

    '设置值必须与启动设置页一致：0 自动，1 官方，2 BMCLAPI，3 TUNA，4 NJU。
    Public Function JavaDownloadSourceName(SourceId As Integer) As String
        Select Case SourceId
            Case 0 : Return "自动测速"
            Case 1 : Return "官方源"
            Case 2 : Return "BMCLAPI"
            Case 3 : Return "清华大学 TUNA"
            Case 4 : Return "南京大学 NJU"
            Case Else : Throw New ArgumentOutOfRangeException(NameOf(SourceId))
        End Select
    End Function

    Public Function MapMojangJavaUrl(OfficialUrl As String, SourceId As Integer) As String
        Dim Address As Uri = Nothing
        If Not Uri.TryCreate(OfficialUrl, UriKind.Absolute, Address) OrElse Address.Scheme <> Uri.UriSchemeHttps OrElse
            Address.UserInfo <> "" OrElse Not Address.IsDefaultPort OrElse
            Not {"piston-meta.mojang.com", "piston-data.mojang.com", "launchermeta.mojang.com", "launcher.mojang.com"}.Contains(Address.Host.ToLowerInvariant()) Then
            Throw New ArgumentException("Java 下载地址必须来自 Mojang HTTPS 服务。", NameOf(OfficialUrl))
        End If
        Select Case SourceId
            Case 1 : Return OfficialUrl
            Case 2 : Return "https://bmclapi2.bangbang93.com" & Address.PathAndQuery
            Case Else : Throw New NotSupportedException("所选下载源不提供 Mojang Java 文件。")
        End Select
    End Function

    Public Function GetMojangJavaDownloadUrls(OfficialUrl As String, SourceId As Integer) As String()
        If SourceId = 0 Then Return {MapMojangJavaUrl(OfficialUrl, 1), MapMojangJavaUrl(OfficialUrl, 2)}
        Return {MapMojangJavaUrl(OfficialUrl, SourceId)}
    End Function

    Public Function MapTemurinJavaUrl(OfficialUrl As String, Major As Integer, SourceId As Integer) As String
        Dim Address As Uri = Nothing
        If Major < 8 OrElse Not Uri.TryCreate(OfficialUrl, UriKind.Absolute, Address) OrElse Address.Scheme <> Uri.UriSchemeHttps OrElse
            Address.UserInfo <> "" OrElse Not Address.IsDefaultPort OrElse Address.Host.ToLowerInvariant() <> "github.com" OrElse
            Not Regex.IsMatch(Address.AbsolutePath, "^/adoptium/temurin" & Major & "-binaries/releases/download/[^/]+/OpenJDK" & Major & "U-jre_x64_windows_hotspot_[A-Za-z0-9._+-]+\.zip$") OrElse
            Address.Query <> "" OrElse Address.Fragment <> "" Then
            Throw New ArgumentException("Java 安装包必须是对应版本的 Adoptium 官方 Windows x64 JRE ZIP。", NameOf(OfficialUrl))
        End If
        Dim FileName = Path.GetFileName(Address.AbsolutePath)
        Select Case SourceId
            Case 1 : Return OfficialUrl
            Case 3 : Return $"https://mirrors.tuna.tsinghua.edu.cn/Adoptium/{Major}/jre/x64/windows/{FileName}"
            Case 4 : Return $"https://mirrors.nju.edu.cn/adoptium/{Major}/jre/x64/windows/{FileName}"
            Case Else : Throw New NotSupportedException("所选下载源不提供 Temurin Java 安装包。")
        End Select
    End Function

    Public Class JavaArchiveDownloadInfo
        Public Property JavaVersion As Version
        Public Property FileName As String
        Public Property Size As Long
        Public Property Sha256 As String
        Public Property Urls As String()
    End Class

    ''' <summary>以 Adoptium 官方元数据为依据，返回同一份 ZIP 的官方及国内镜像；没有满足范围的包时返回 Nothing。</summary>
    Public Function ResolveTemurinJavaArchive(Major As Integer, Range As ValueRange(Of Version), SourceId As Integer,
                                             Optional c As CancellationToken = Nothing) As JavaArchiveDownloadInfo
        c.ThrowIfCancellationRequested()
        If Major < 8 OrElse SourceId = 2 Then Return Nothing
        If SourceId < 0 OrElse SourceId > 4 Then Throw New ArgumentOutOfRangeException(NameOf(SourceId))
        Dim ApiUrl = $"https://api.adoptium.net/v3/assets/latest/{Major}/hotspot?architecture=x64&image_type=jre&os=windows&vendor=eclipse"
        Dim Response = JArray.Parse(ReadJavaSourceText(ApiUrl, c))
        For Each Entry In Response
            Dim Metadata = TryCast(Entry, JObject)
            If Metadata Is Nothing Then Continue For
            Dim Candidate = ParseTemurinJavaArchive(Metadata("binary"), Metadata("version"), Major, Range, SourceId, c)
            If Candidate IsNot Nothing Then Return Candidate
        Next
        '旧 Forge 等可能限制补丁上界。查官方历史 GA 元数据，不能只在最新版与 Mojang 8u51 中二选一。
        'API 使用 Java 版本范围；略放宽到下一补丁，再由本地 Range 精确校验所有端点与四段版本。
        If Not Range.HasUpper OrElse Range.Upper.Major <> Major Then Return Nothing
        Dim Upper = Range.Upper
        Dim ApiUpper = $"{Major}.{Math.Max(0, Upper.Minor)}.{CLng(Math.Max(0, Upper.Build)) + 1}"
        Dim ApiRange = Uri.EscapeDataString($"[{Major},{ApiUpper})")
        ApiUrl = $"https://api.adoptium.net/v3/assets/version/{ApiRange}?architecture=x64&image_type=jre&os=windows&vendor=eclipse&jvm_impl=hotspot&release_type=ga&sort_order=DESC&page_size=20"
        Response = JArray.Parse(ReadJavaSourceText(ApiUrl, c))
        For Each Entry In Response
            Dim Metadata = TryCast(Entry, JObject)
            Dim Binaries = TryCast(Metadata?("binaries"), JArray)
            If Binaries Is Nothing Then Continue For
            For Each Binary In Binaries
                Dim Candidate = ParseTemurinJavaArchive(Binary, Metadata("version_data"), Major, Range, SourceId, c)
                If Candidate IsNot Nothing Then Return Candidate
            Next
        Next
        Return Nothing
    End Function

    Private Function ParseTemurinJavaArchive(Binary As JToken, VersionInfo As JToken, Major As Integer,
                                             Range As ValueRange(Of Version), SourceId As Integer, c As CancellationToken) As JavaArchiveDownloadInfo
        c.ThrowIfCancellationRequested()
        Dim Runtime = TryCast(Binary, JObject)
        Dim VersionObject = TryCast(VersionInfo, JObject)
        If Runtime Is Nothing OrElse VersionObject Is Nothing OrElse Runtime("architecture")?.ToString() <> "x64" OrElse
            Runtime("os")?.ToString() <> "windows" OrElse Runtime("image_type")?.ToString() <> "jre" OrElse
            Runtime("jvm_impl")?.ToString() <> "hotspot" Then Return Nothing

        Dim Parts(3) As Integer
        Dim Names = {"major", "minor", "security", "patch"}
        For i = 0 To Names.Length - 1
            Dim Part = VersionObject(Names(i))
            If i = 3 AndAlso (Part Is Nothing OrElse Part.Type = JTokenType.Null) Then Exit For
            If Part Is Nothing OrElse Part.Type <> JTokenType.Integer OrElse Not Integer.TryParse(Part.ToString(), Parts(i)) OrElse Parts(i) < 0 Then Return Nothing
        Next
        Dim JavaVersion As New Version(Parts(0), Parts(1), Parts(2), Parts(3))
        If JavaVersion.Major <> Major OrElse Not Range.Contains(JavaVersion) Then Return Nothing

        Dim Package = TryCast(Runtime("package"), JObject)
        Dim OfficialUrl = Package?("link")?.ToString()
        Dim FileName = Package?("name")?.ToString()
        Dim Hash = Package?("checksum")?.ToString()
        Dim Size As Long
        If Not Long.TryParse(Package?("size")?.ToString(), Size) OrElse Size < 1024 * 1024 OrElse Size > 1024L * 1024 * 1024 OrElse
            String.IsNullOrEmpty(FileName) OrElse Not Regex.IsMatch(If(Hash, ""), "\A[0-9a-fA-F]{64}\z") Then Return Nothing
        Try
            MapTemurinJavaUrl(OfficialUrl, Major, 1)
        Catch ex As ArgumentException
            Return Nothing
        End Try
        If FileName <> Path.GetFileName(New Uri(OfficialUrl).AbsolutePath) Then Return Nothing
        Dim SourceIds = If(SourceId = 0, New Integer() {1, 3, 4}, New Integer() {SourceId})
        Dim Urls = SourceIds.Select(Function(Id) MapTemurinJavaUrl(OfficialUrl, Major, Id)).ToArray()
        Return New JavaArchiveDownloadInfo With {
            .JavaVersion = JavaVersion, .FileName = FileName, .Size = Size, .Sha256 = Hash,
            .Urls = OrderJavaDownloadUrls(Urls, c, ExpectZip:=True)}
    End Function

    Private Class JavaProbeResult
        Public Property Url As String
        Public Property BytesPerSecond As Double
    End Class
    Private Class JavaProbeCacheEntry
        Public Property Expires As DateTime
        Public Property Urls As String()
    End Class
    Private ReadOnly ProbeCacheLock As New Object
    Private ReadOnly ProbeCache As New Dictionary(Of String, JavaProbeCacheEntry)(StringComparer.Ordinal)

    ''' <summary>并发读取至多 256 KiB，按包含建连耗时的实测吞吐排序。失败的源保留在末尾供下载器重试。</summary>
    Public Function OrderJavaDownloadUrls(Urls As IEnumerable(Of String), Optional c As CancellationToken = Nothing,
                                         Optional ExpectZip As Boolean = False) As String()
        c.ThrowIfCancellationRequested()
        Dim Candidates = Urls.Distinct(StringComparer.Ordinal).ToArray()
        If Candidates.Length = 0 Then Throw New ArgumentException("Java 下载源列表为空。", NameOf(Urls))
        For Each Url In Candidates
            Dim Address As Uri = Nothing
            If Not Uri.TryCreate(Url, UriKind.Absolute, Address) OrElse Address.Scheme <> Uri.UriSchemeHttps Then
                Throw New ArgumentException("Java 下载测速仅支持 HTTPS。", NameOf(Urls))
            End If
        Next
        If Candidates.Length = 1 Then Return Candidates '手动选择严格使用该源。
        Dim Key = ExpectZip.ToString() & String.Join(vbLf, Candidates)
        SyncLock ProbeCacheLock
            Dim Cached As JavaProbeCacheEntry = Nothing
            If ProbeCache.TryGetValue(Key, Cached) AndAlso Cached.Expires > DateTime.UtcNow Then Return CType(Cached.Urls.Clone(), String())
        End SyncLock
        Logger.Info("正在并发测试 Java 下载源速度……")
        Dim Tasks = Candidates.Select(Function(Url) Task.Run(Function() ProbeJavaSource(Url, ExpectZip, c), c)).ToArray()
        Dim Results = Task.WhenAll(Tasks).GetAwaiter().GetResult()
        c.ThrowIfCancellationRequested()
        Dim Ordered = Results.OrderByDescending(Function(Result) Result.BytesPerSecond).Select(Function(Result) Result.Url).ToArray()
        For Each Result In Results.OrderByDescending(Function(Item) Item.BytesPerSecond)
            Logger.Info($"Java 下载源测速：{New Uri(Result.Url).Host}，{If(Result.BytesPerSecond > 0, Math.Round(Result.BytesPerSecond / 1024).ToString() & " KiB/s", "暂不可用")}")
        Next
        SyncLock ProbeCacheLock
            If ProbeCache.Count > 32 Then ProbeCache.Clear()
            ProbeCache(Key) = New JavaProbeCacheEntry With {.Expires = DateTime.UtcNow.AddMinutes(If(Results.Any(Function(Result) Result.BytesPerSecond > 0), 10, 1)), .Urls = Ordered}
        End SyncLock
        Return CType(Ordered.Clone(), String())
    End Function

    Private Function ProbeJavaSource(Url As String, ExpectZip As Boolean, c As CancellationToken) As JavaProbeResult
        Const SampleSize As Integer = 256 * 1024
        Dim Result As New JavaProbeResult With {.Url = Url}
        Dim Request = CreateJavaSourceRequest(Url, 6000)
        Request.AddRange(0, SampleSize - 1)
        Dim Watch = Stopwatch.StartNew()
        Try
            Using Registration = c.Register(Sub() Request.Abort()), Deadline As New Timer(Sub(State) Request.Abort(), Nothing, 6000, Timeout.Infinite)
                Using Response = DirectCast(Request.GetResponse(), HttpWebResponse)
                    If Response.ResponseUri.Scheme <> Uri.UriSchemeHttps OrElse
                        (Response.StatusCode <> HttpStatusCode.OK AndAlso Response.StatusCode <> HttpStatusCode.PartialContent) OrElse
                        If(Response.ContentType, "").IndexOf("text/html", StringComparison.OrdinalIgnoreCase) >= 0 Then Return Result
                    Using Stream = Response.GetResponseStream()
                        Dim Buffer(16383) As Byte
                        Dim Total As Integer = 0
                        Dim Prefix As New List(Of Byte)
                        While Total < SampleSize
                            c.ThrowIfCancellationRequested()
                            Dim Read = Stream.Read(Buffer, 0, Math.Min(Buffer.Length, SampleSize - Total))
                            If Read = 0 Then Exit While
                            If Prefix.Count < 64 Then Prefix.AddRange(Buffer.Take(Math.Min(Read, 64 - Prefix.Count)))
                            Total += Read
                        End While
                        If Total = 0 Then Return Result
                        If ExpectZip AndAlso (Prefix.Count < 4 OrElse Prefix(0) <> &H50 OrElse Prefix(1) <> &H4B OrElse Prefix(2) <> 3 OrElse Prefix(3) <> 4) Then Return Result
                        Dim TextPrefix = Text.Encoding.ASCII.GetString(Prefix.ToArray()).TrimStart()
                        If TextPrefix.StartsWith("<", StringComparison.Ordinal) Then Return Result
                        Result.BytesPerSecond = Total / Math.Max(Watch.Elapsed.TotalSeconds, 0.001)
                    End Using
                End Using
            End Using
        Catch ex As Exception
            c.ThrowIfCancellationRequested()
            Logger.Info($"Java 下载源测速未完成：{New Uri(Url).Host}（{ex.GetType().Name}）")
        End Try
        Return Result
    End Function

    Private Function CreateJavaSourceRequest(Url As String, TimeoutMs As Integer) As HttpWebRequest
        Dim Request = DirectCast(WebRequest.Create(Url), HttpWebRequest)
        Request.UserAgent = "BRCL/" & BrclReleaseVersion
        Request.Timeout = TimeoutMs
        Request.ReadWriteTimeout = TimeoutMs
        Request.AllowAutoRedirect = True
        Request.MaximumAutomaticRedirections = 5
        Request.KeepAlive = False
        Return Request
    End Function

    ''' <summary>有硬超时、取消和体积上限的 Java 元数据读取。</summary>
    Public Function ReadJavaSourceText(Url As String, Optional c As CancellationToken = Nothing) As String
        c.ThrowIfCancellationRequested()
        Dim Address As Uri = Nothing
        If Not Uri.TryCreate(Url, UriKind.Absolute, Address) OrElse Address.Scheme <> Uri.UriSchemeHttps Then Throw New ArgumentException("Java 元数据仅支持 HTTPS。", NameOf(Url))
        Dim Request = CreateJavaSourceRequest(Url, 12000)
        Try
            Using Registration = c.Register(Sub() Request.Abort()), Deadline As New Timer(Sub(State) Request.Abort(), Nothing, 12000, Timeout.Infinite)
                Using Response = DirectCast(Request.GetResponse(), HttpWebResponse)
                    If Response.ResponseUri.Scheme <> Uri.UriSchemeHttps OrElse Response.StatusCode <> HttpStatusCode.OK Then Throw New InvalidDataException("Java 元数据返回无效响应。")
                    If Response.ContentLength > 4 * 1024 * 1024 Then Throw New InvalidDataException("Java 元数据超过大小限制。")
                    Using Stream = Response.GetResponseStream(), Output As New MemoryStream()
                        Dim Buffer(16383) As Byte
                        While True
                            c.ThrowIfCancellationRequested()
                            Dim Read = Stream.Read(Buffer, 0, Buffer.Length)
                            If Read = 0 Then Exit While
                            If Output.Length + Read > 4 * 1024 * 1024 Then Throw New InvalidDataException("Java 元数据超过大小限制。")
                            Output.Write(Buffer, 0, Read)
                        End While
                        Return Text.Encoding.UTF8.GetString(Output.ToArray())
                    End Using
                End Using
            End Using
        Catch
            c.ThrowIfCancellationRequested()
            Throw
        End Try
    End Function

End Module
