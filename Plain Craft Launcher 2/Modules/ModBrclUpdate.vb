Imports System.Net
Imports System.Security.Cryptography
Imports System.Text

Friend Module ModBrclUpdate
    Public Const BrclReleaseVersion As String = "1.3.0"
    Public Const BrclUpdateRevision As String = "2026.11.23.4"
    Friend Const PrimaryDownloadBase As String = "https://zhushu.homes/BRCL/transfer/"
    Friend Const BackupDownloadBase As String = "https://zhushu.homes/BRCL/transfer-mirror/"
    Private Const PublicKey As String = "<RSAKeyValue><Modulus>6nS0PTj2VkIKGLjI9wbulA94ZVggaX0294sTyPmjOiaiFo5gSS03/P73ChiJNeZFwYSRYYWQNpNEXPVQvrcjZqJbQd/KFfJ8Atnyo/YRyIzsB9fNg6HBXlCG44ePm298uoLTbeVQun5MwrX7Sl+6w3u1PkkdguzgXdrOdrjrO/YcABguF4J+oEeiRDEuqjhf9VwZBbYnUAgXkDI5GBKoZp0+hG5EItkoeblOKZaeH1gI4pC4fPYgsTglMgunFn3UfaI9jBpERxVpWjWOA70ATA9BDculrT0KKGUKPlBCWGVapalUH45+AsB+LRD6n0n/3ZzXpfhuZHS0kNYM87QYQaj52dV50uUldwXLekYA9wyL0MACCtr5TTadxgcGiOqJMaozLEQeVqKFErRBWP4pVltE7bSmVYNxeQE4YecWFVOQe3Zs6YON3DL6I6YX1Q4rTPvZXta1dfMd8cTCa61bw2ZnRM1Pj79/qZYtK5OBIRTTVVnJJ0S9AAQZRUBscUo9</Modulus><Exponent>AQAB</Exponent></RSAKeyValue>"
    Private Checking As Integer
    Private StartupCheckScheduled As Integer

    ''' <summary>首屏就绪后独立启动一次更新检查，不依赖 Java 搜索等初始化任务。</summary>
    Public Sub StartStartupUpdateCheck()
        If Environment.GetEnvironmentVariable("BRCL_UI_PREVIEW") = "1" Then Return
        If Not TryReserveStartupCheck() Then Return
        Dim Worker As New Thread(
            Sub()
                Try
                    '全新安装先完成协议授权；等待只占用后台线程，不阻塞 UI。
                    Do While Settings.Get(Of Integer)("SystemEulaVersion") < 2
                        If Application.Current Is Nothing OrElse Application.Current.Dispatcher.HasShutdownStarted Then Return
                        Thread.Sleep(250)
                    Loop
                    Logger.Info("开始本次启动的自动更新检查")
                    CheckBrclUpdate(False)
                Catch ex As Exception
                    Logger.Warn(ex, "启动自动更新检查失败")
                End Try
            End Sub) With {.Name = "BRCL Startup Update", .IsBackground = True, .Priority = ThreadPriority.BelowNormal}
        Worker.Start()
    End Sub

    Private Function TryReserveStartupCheck() As Boolean
        Return Threading.Interlocked.CompareExchange(StartupCheckScheduled, 1, 0) = 0
    End Function

    Private Function ShouldPresentUpdate(Manual As Boolean, Preference As Integer, Critical As Boolean) As Boolean
        '检查与提示策略分离：关闭提示的用户也会在每次启动时检查签名清单。
        Return Manual OrElse Preference < 2 OrElse (Preference = 2 AndAlso Critical)
    End Function

    Public Sub CheckBrclUpdate(Manual As Boolean)
        Dim Preference = Settings.Get(Of Integer)("SystemSystemUpdate")
        If Threading.Interlocked.Exchange(Checking, 1) <> 0 Then
            If Manual Then RunInUi(Sub() Hint("正在检查 BRCL 更新，请稍候。"))
            Return
        End If
        Dim Stage As String = Nothing
        Dim InstallerStarted As Boolean = False
        Try
            Dim Sources = {PrimaryDownloadBase, BackupDownloadBase}
            Dim Manifest = ReadUpdateManifest(Sources)
            Dim Latest = Version.Parse(CStr(Manifest("version")))
            Dim DisplayVersion = If(CStr(Manifest("display_version")), Latest.ToString())
            If Latest <= Version.Parse(BrclUpdateRevision) Then
                Logger.Info("BRCL 已是最新版本（" & BrclReleaseVersion & "）")
                If Manual Then RunInUi(Sub() Hint("BRCL 已是最新版本（" & BrclReleaseVersion & "）。"))
                Return
            End If
            If Not ShouldPresentUpdate(Manual, Preference, CBool(If(Manifest("critical"), New JValue(False)))) Then
                Logger.Info("已检查到 BRCL " & DisplayVersion & "，按更新偏好保持静默")
                Return
            End If
            Dim Package = CStr(Manifest("package"))
            Dim ExpectedSize = CLng(Manifest("size"))
            If Manual OrElse Preference <> 0 Then
                Dim Accept = RunInUiWait(Function() MyMsgBox("发现 BRCL " & DisplayVersion & vbCrLf & CStr(Manifest("notes")), "BRCL 更新", "下载更新", "稍后"))
                If Accept <> 1 Then Return
            End If
            Stage = IO.Path.Combine(IO.Path.GetTempPath(), "BRCL-Update-" & Guid.NewGuid().ToString("N"))
            IO.Directory.CreateDirectory(Stage)
            Dim Archive = IO.Path.Combine(Stage, "release.zip")
            DownloadUpdatePackage(Sources, Package, Archive, ExpectedSize, CStr(Manifest("sha256")))
            Dim Install = RunInUiWait(Function() MyMsgBox("BRCL " & DisplayVersion & " 已下载并通过校验。安装后将重启启动器，原程序会自动备份。", "BRCL 更新", "安装并重启", "稍后"))
            If Install <> 1 Then Return
            If RunInUiWait(Function() HasDownloadingTask()) Then Throw New Exception("请等待下载任务结束后再安装更新。")
            Dim Script = IO.Path.Combine(Stage, "install.ps1")
            Using Stream = Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("PCL.BrclInstall.ps1")
                If Stream Is Nothing Then Throw New Exception("缺少更新安装程序。")
                Using Output = IO.File.Create(Script)
                    Stream.CopyTo(Output)
                End Using
            End Using
            Dim ArgsFile = IO.Path.Combine(Stage, "install.json")
            Dim Args As New JObject From {{"pid", Process.GetCurrentProcess().Id}, {"target", AppDomain.CurrentDomain.BaseDirectory}, {"exe", IO.Path.GetFileName(PathExe)}}
            IO.File.WriteAllText(ArgsFile, Args.ToString(), New UTF8Encoding(False))
            Dim Start As New ProcessStartInfo With {
                .FileName = IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell\v1.0\powershell.exe"),
                .Arguments = "-NoProfile -ExecutionPolicy Bypass -File """ & Script & """",
                .WorkingDirectory = Stage, .UseShellExecute = False, .CreateNoWindow = True}
            Process.Start(Start)
            InstallerStarted = True
            RunInUi(Sub() FrmMain.EndProgram(False))
        Catch Ex As Exception
            Logger.Warn(Ex, "BRCL 更新检查失败")
            If Manual Then RunInUi(Sub() ShowBrclError("BRCL 更新检查失败", Ex, "更新地址：" & PrimaryDownloadBase & vbCrLf & Ex.GetDisplay(True)))
        Finally
            'Only the detached installer needs to retain its staging files.
            'Canceling a prompt or rejecting a broken response must not leak a full ZIP.
            If Stage IsNot Nothing AndAlso Not InstallerStarted Then
                Try
                    If IO.Directory.Exists(Stage) Then IO.Directory.Delete(Stage, True)
                Catch Ex As Exception
                    Logger.Warn(Ex, "清理未安装的 BRCL 更新暂存目录失败")
                End Try
            End If
            Threading.Interlocked.Exchange(Checking, 0)
        End Try
    End Sub

    Private Function ReadUpdateManifest(Sources As IEnumerable(Of String)) As JObject
        Dim LastError As Exception = Nothing
        For Each Source In Sources
            Try
                Using Client As New UpdateClient
                    Dim ManifestBytes As Byte()
                    Using Input = Client.OpenRead(Source & "latest.json")
                        ManifestBytes = ReadBoundedData(Input, 65536)
                    End Using
                    Using Input = Client.OpenRead(Source & "latest.sig")
                        Dim Signature = Convert.FromBase64String(Encoding.UTF8.GetString(ReadBoundedData(Input, 4096)).Trim())
                        Return ReadVerifiedManifest(ManifestBytes, Signature)
                    End Using
                End Using
            Catch ex As Exception
                LastError = ex
                Logger.Warn(ex, $"更新清单来源不可用（{Source}）")
            End Try
        Next
        Throw New IO.IOException("无法获取有效的签名更新清单。", LastError)
    End Function

    Private Sub DownloadUpdatePackage(Sources As IEnumerable(Of String), Package As String, Archive As String, Size As Long, Hash As String)
        Dim LastError As Exception = Nothing
        For Each Source In Sources
            Dim Offset = If(IO.File.Exists(Archive), New IO.FileInfo(Archive).Length, 0L)
            Try
                Try
                    TransferUpdatePackage(Source & Package, Archive, Size, Offset)
                    VerifyPackageHash(Archive, Hash)
                Catch ex As IO.InvalidDataException When Offset > 0
                    '已有分片可能来自损坏的主源；在同一镜像重新下载一次。
                    TransferUpdatePackage(Source & Package, Archive, Size, 0)
                    VerifyPackageHash(Archive, Hash)
                End Try
                Return
            Catch ex As Exception
                LastError = ex
                If IO.File.Exists(Archive) AndAlso New IO.FileInfo(Archive).Length >= Size Then IO.File.Delete(Archive)
                Logger.Warn(ex, $"更新包来源不可用（{Source}）")
            End Try
        Next
        Throw New IO.IOException("无法下载通过校验的更新包。", LastError)
    End Sub

    Private Sub TransferUpdatePackage(Url As String, Archive As String, Size As Long, Offset As Long)
        Dim Request = DirectCast(WebRequest.Create(Url), HttpWebRequest)
        Request.AllowAutoRedirect = False
        Request.UserAgent = "BRCL/" & BrclReleaseVersion
        Request.Timeout = 30000
        Request.ReadWriteTimeout = 120000
        If Offset > 0 Then Request.AddRange(Offset)
        Using Response = DirectCast(Request.GetResponse(), HttpWebResponse)
            If Response.StatusCode = HttpStatusCode.OK Then
                Offset = 0
            ElseIf Offset <= 0 OrElse Response.StatusCode <> HttpStatusCode.PartialContent OrElse
                Response.Headers(HttpResponseHeader.ContentRange) <> FormattableString.Invariant($"bytes {Offset}-{Size - 1}/{Size}") Then
                Throw New IO.InvalidDataException("更新镜像返回了无效的续传区间。")
            End If
            If Response.ContentLength >= 0 AndAlso Response.ContentLength <> Size - Offset Then Throw New IO.InvalidDataException("更新包响应大小不符合签名清单。")
            Using Input = Response.GetResponseStream(), Output = IO.File.Open(Archive, If(Offset = 0, IO.FileMode.Create, IO.FileMode.Append), IO.FileAccess.Write)
                CopyBoundedPackage(Input, Output, Size - Offset)
            End Using
        End Using
    End Sub

    Private Sub VerifyPackageHash(Archive As String, ExpectedHash As String)
        Using Sha = SHA256.Create(), Stream = IO.File.OpenRead(Archive)
            Dim Hash = BitConverter.ToString(Sha.ComputeHash(Stream)).Replace("-", "")
            If Not String.Equals(Hash, ExpectedHash, StringComparison.OrdinalIgnoreCase) Then Throw New IO.InvalidDataException("更新包 SHA-256 校验失败。")
        End Using
    End Sub

    Private Sub CopyBoundedPackage(Input As IO.Stream, Output As IO.Stream, ExpectedSize As Long)
        If ExpectedSize <= 0 OrElse ExpectedSize > 268435456 Then Throw New ArgumentOutOfRangeException(NameOf(ExpectedSize))
        Dim Buffer(65535) As Byte
        Dim Total As Long = 0
        Do
            'Read one byte past the signed length to detect an oversized response,
            'without ever writing unverified extra bytes to disk.
            Dim Count = Input.Read(Buffer, 0, CInt(Math.Min(Buffer.Length, ExpectedSize - Total + 1)))
            If Count = 0 Then Exit Do
            If Total + Count > ExpectedSize Then Throw New IO.InvalidDataException("更新包超过清单声明的大小。")
            Output.Write(Buffer, 0, Count)
            Total += Count
        Loop
        If Total <> ExpectedSize Then Throw New IO.InvalidDataException("更新包大小校验失败。")
    End Sub

    Private Function ReadBoundedData(Input As IO.Stream, Limit As Integer) As Byte()
        'Enforce the limit while streaming, before a malformed response can grow
        'WebClient's download buffer without bound during startup.
        Using Output As New IO.MemoryStream
            Dim Buffer(4095) As Byte
            Do
                Dim Count = Input.Read(Buffer, 0, Buffer.Length)
                If Count = 0 Then Exit Do
                If Output.Length + Count > Limit Then Throw New Exception("更新服务响应过大。")
                Output.Write(Buffer, 0, Count)
            Loop
            Return Output.ToArray()
        End Using
    End Function

    Private Function ReadVerifiedManifest(ManifestBytes As Byte(), Signature As Byte()) As JObject
        If ManifestBytes Is Nothing OrElse ManifestBytes.Length = 0 OrElse ManifestBytes.Length > 65536 Then Throw New Exception("更新清单大小无效。")
        Using Rsa As New RSACryptoServiceProvider
            Rsa.PersistKeyInCsp = False
            Rsa.FromXmlString(PublicKey)
            If Not Rsa.VerifyData(ManifestBytes, CryptoConfig.MapNameToOID("SHA256"), Signature) Then Throw New Exception("更新清单签名无效。")
        End Using
        Dim Manifest = JObject.Parse(Encoding.UTF8.GetString(ManifestBytes))
        Dim Latest As Version = Nothing
        If Manifest("version")?.Type <> JTokenType.String OrElse Not Version.TryParse(CStr(Manifest("version")), Latest) OrElse Latest.Build < 0 OrElse Latest.Revision < 0 Then Throw New Exception("更新版本号无效。")
        If Manifest("package")?.Type <> JTokenType.String OrElse CStr(Manifest("package")) <> "releases/BRCL-" & Latest.ToString() & ".zip" Then Throw New Exception("更新包路径无效。")
        Dim Size As Long
        If Manifest("size")?.Type <> JTokenType.Integer OrElse Not Long.TryParse(CStr(Manifest("size")), Size) OrElse Size <= 0 OrElse Size > 268435456 Then Throw New Exception("更新包大小无效。")
        If Manifest("sha256")?.Type <> JTokenType.String OrElse Not Global.System.Text.RegularExpressions.Regex.IsMatch(CStr(Manifest("sha256")), "\A[0-9a-fA-F]{64}\z") Then Throw New Exception("更新包 SHA-256 无效。")
        If Manifest("critical") IsNot Nothing AndAlso Manifest("critical").Type <> JTokenType.Boolean Then Throw New Exception("更新标记无效。")
        Return Manifest
    End Function

    Private Class UpdateClient
        Inherits WebClient
        Private ReadOnly RequestTimeout As Integer

        Public Sub New(Optional TimeoutMilliseconds As Integer = 15000)
            RequestTimeout = TimeoutMilliseconds
        End Sub

        Protected Overrides Function GetWebRequest(Address As Uri) As WebRequest
            Dim Request = MyBase.GetWebRequest(Address)
            Request.Timeout = RequestTimeout
            Request.CachePolicy = New Global.System.Net.Cache.RequestCachePolicy(Global.System.Net.Cache.RequestCacheLevel.NoCacheNoStore)
            Dim Http = TryCast(Request, HttpWebRequest)
            If Http IsNot Nothing Then
                Http.AllowAutoRedirect = False
                Http.ReadWriteTimeout = RequestTimeout
                Http.UserAgent = "BRCL/" & BrclReleaseVersion
            End If
            Return Request
        End Function
    End Class
End Module
