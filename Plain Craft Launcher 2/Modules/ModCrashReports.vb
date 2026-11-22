Imports System.IO
Imports System.Net.Http
Imports System.Security.Cryptography
Imports System.Text
Imports System.Text.RegularExpressions

Friend Module ModCrashReports
    Private Const Endpoint As String = InkBase & "crash"
    Private Const UploadPublicKey As String = "<RSAKeyValue><Modulus>ns76tqdMBk4GUZy3B816KrRatdDD6z8AUNs22VVujYfDukZaGkMFVUmH6RAy8mv7nNoNnAMWvziqpKBxh2/3Vutdin+quUgKB9dvdvmoj+VpRsVJhizlAIFKJaFxnYI3QT9o6WeSgugdHrJSr6JCXRhaCfOawI4L33YAp4Dt5L3aTwuX/ahnABcOlIIyYE7+4P+Neyp4hDAUOaaQnMjSX7W6KbVWrmmaMUqIQB96YlvAdQyT/DGwXxCp9kNrnQrXT0AIx+TbYprBlH/Y9JD3syw4j5Oa46mHpUpLZjwu6AXyqMu0duYoBukpOgBW69Ezz+EUXAxQqtDgGrHCufqHljh2O6CV78uJR63kEnpmlV1RhrS2D7yPzMAGsjHnxksfdoIZZBBvpIiqBpRzY3ox5wAzZC9dduyrdceUXjPMR+CoLWq1F8XXmfWipRWtOW/EO/JuKJDMHPZMIczzitX1X+/hZlU7UVyA/7MvyvGjo+5NPBnyFTrLE2ynDKLJ0luf</Modulus><Exponent>AQAB</Exponent></RSAKeyValue>"
    Private ReadOnly Gate As New Object
    Private ReadOnly Base As String = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "BRCL", "CrashReports")
    Private ReadOnly Pending As String = Path.Combine(Base, "InkPending")
    Private ReadOnly LinkedPending As String = Path.Combine(Base, "LinkedPending")
    Private ReadOnly DraftFolder As String = Path.Combine(Base, "Review")
    Private ReadOnly Sessions As String = Path.Combine(Base, "Sessions")
    Private ReadOnly SessionFile As String = Path.Combine(Sessions, Process.GetCurrentProcess().Id.ToString() & "-" & Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks.ToString() & ".json")
    Private NextAttempt As DateTime
    Private FailureCount As Integer
    Private UploadCancel As New Threading.CancellationTokenSource
    Private Uploading As Integer
    Private LauncherQueued As Integer
    Private RetryTimer As Threading.Timer

    Public Function CrashUploadsEnabled() As Boolean
        Try
            Return CrashMode() <> 0
        Catch
            Return False
        End Try
    End Function

    Public Function CrashMode() As Integer
        If Not UsageEnabled() Then Return 0
        Dim Mode = Settings.Get(Of Integer)("BrclCrashMode")
        If Mode = 3 Then Return If(Settings.Get(Of Boolean)("SystemBrclCrashUpload"), 2, 0)
        Return If(Mode = 1 OrElse Mode = 2, 2, 0)
    End Function

    Public Sub InitializeCrashReports()
        Try
            AddHandler AppDomain.CurrentDomain.UnhandledException,
                Sub(Sender, Args) QueueLauncherCrash(TryCast(Args.ExceptionObject, Exception)?.ToString(), "unhandled_exception")
            If Settings.Get(Of Integer)("BrclCrashMode") <> CrashMode() Then
                Settings.Set("BrclCrashMode", CrashMode())
            End If
            DeleteQueueFiles(Path.Combine(Base, "Pending"))
            If CrashUploadsEnabled() Then
                Directory.CreateDirectory(Sessions)
                For Each Previous In Directory.GetFiles(Sessions, "*.json")
                    If Previous = SessionFile Then Continue For
                    Dim Parts = Path.GetFileNameWithoutExtension(Previous).Split("-"c)
                    Dim PreviousPid As Integer, Ticks As Long
                    If Parts.Length <> 2 OrElse Not Integer.TryParse(Parts(0), PreviousPid) OrElse PreviousPid < 1 OrElse Not Long.TryParse(Parts(1), Ticks) Then Continue For
                    Try
                        Using Other = Process.GetProcessById(PreviousPid)
                            If Other.StartTime.ToUniversalTime().Ticks = Ticks Then Continue For
                        End Using
                    Catch ex As ArgumentException
                        'The previous process has exited.
                    Catch ex As System.ComponentModel.Win32Exception
                        Continue For
                    End Try
                    Dim Claimed = Previous & "." & Guid.NewGuid().ToString("N") & ".tmp"
                    Try
                        File.Move(Previous, Claimed)
                    Catch ex As IOException
                        Continue For
                    End Try
                    Try
                        QueueReport("launcher", "unexpected_exit", "", New List(Of JObject) From {
                            LogItem("session.log", "上次会话未正常结束，原因待确认。")})
                    Finally
                        File.Delete(Claimed)
                    End Try
                Next
                WriteSession()
            Else
                CrashUploadSettingChanged()
            End If
            RetryTimer = New Threading.Timer(Sub(State)
                                                FlushCrashReports()
                                                FlushUsage()
                                            End Sub, Nothing, 10000, 60000)
        Catch
            'Crash reporting must never prevent startup or cause recursive failures.
        End Try
    End Sub

    Private Sub WriteSession()
        Directory.CreateDirectory(Sessions)
        File.WriteAllText(SessionFile, New JObject From {{"pid", Process.GetCurrentProcess().Id}, {"started_utc", DateTime.UtcNow.ToString("O")}}.ToString())
    End Sub

    Public Sub CrashUploadSettingChanged()
        Try
            SyncLock Gate
                UploadCancel.Cancel()
                UploadCancel = New Threading.CancellationTokenSource
                NextAttempt = DateTime.MinValue
                FailureCount = 0
                If CrashUploadsEnabled() Then
                    WriteSession()
                Else
                    DeleteQueueFiles(Pending)
                    DeleteQueueFiles(LinkedPending)
                    DeleteQueueFiles(DraftFolder)
                    DeleteQueueFiles(Sessions)
                End If
            End SyncLock
            If CrashUploadsEnabled() Then FlushCrashReports()
        Catch
        End Try
    End Sub

    Public Sub CompleteCrashSession()
        Try
            If File.Exists(SessionFile) Then File.Delete(SessionFile)
        Catch
        End Try
    End Sub

    Public Sub QueueLauncherCrash(Details As String, Optional Reason As String = "fatal_error")
        If Not CrashUploadsEnabled() OrElse Threading.Interlocked.Exchange(LauncherQueued, 1) <> 0 Then Return
        Try
            Try
                Logger.Instance.Flush()
            Catch
            End Try
            QueueReport("launcher", Reason, "", New List(Of JObject) From {
                LogItem("exception.log", Details),
                LogItem("launcher.log", ReadTail(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "BRCL", "Log1.txt")))})
            CompleteCrashSession()
        Catch
        End Try
    End Sub

    Public Sub QueueMinecraftCrash(Instance As McInstance, ConsoleLines As IList(Of String), Started As DateTime, Pid As Integer, Session As String, ExitCode As String, SharedInstance As Boolean, JavaInfo As String)
        If Not CrashUploadsEnabled() Then Return
        Try
            Dim Logs As New List(Of JObject) From {LogItem("console.log", String.Join(vbLf, ConsoleLines))}
            Logs.Add(LogItem("process.log", "退出码 " & ExitCode))
            Dim Root = Instance.PathIndie
            If Not SharedInstance Then
                For Each Name In {"latest.log"}
                    Dim Filename = Path.Combine(Root, "logs", Name)
                    If File.Exists(Filename) AndAlso File.GetLastWriteTimeUtc(Filename) >= Started Then Logs.Add(LogItem(Name, ReadTail(Filename)))
                Next
                Dim Crashes = Path.Combine(Root, "crash-reports")
                If Directory.Exists(Crashes) Then
                    Dim Number = 0
                    For Each Info In New DirectoryInfo(Crashes).EnumerateFiles("*.txt").Where(Function(F) F.LastWriteTimeUtc >= Started).OrderByDescending(Function(F) F.LastWriteTimeUtc).Take(1)
                        Logs.Add(LogItem("crash-" & Number & ".txt", ReadTail(Info.FullName)))
                        Number += 1
                    Next
                End If
            End If
            Dim Jvm = New DirectoryInfo(Root).EnumerateFiles("hs_err_pid" & Pid & ".log").Where(Function(F) F.LastWriteTimeUtc >= Started).OrderByDescending(Function(F) F.LastWriteTimeUtc).FirstOrDefault()
            If Jvm IsNot Nothing Then Logs.Add(LogItem("jvm-crash.log", ReadTail(Jvm.FullName)))
            Dim McName = If(Instance.Version?.VanillaName, "")
            If Not Regex.IsMatch(McName, "\A[0-9][a-zA-Z0-9.\-]{0,39}\z") Then McName = ""
            QueueReport("minecraft", "game_crash", "", Logs, Session, McName, JavaInfo, If(IsTrmInstanceName(Instance.Name), "TRM", "其他"))
        Catch
        End Try
    End Sub

    Private Function LogItem(Name As String, Text As String) As JObject
        Text = RedactCrashText(If(Text, ""))
        If Text.Length > 65536 Then Text = Text.Substring(0, 32768) & vbLf & "[中间内容已截断]" & vbLf & Text.Substring(Text.Length - 32768)
        Return New JObject From {{"name", Name}, {"text", Text}}
    End Function

    Public Function ReadTail(Filename As String) As String
        Try
            Dim Source As New FileInfo(Filename)
            If Not Source.Exists OrElse (Source.Attributes And FileAttributes.ReparsePoint) <> 0 Then Return "[日志不可读取]"
            Dim Parent = Source.Directory
            While Parent IsNot Nothing
                If (Parent.Attributes And FileAttributes.ReparsePoint) <> 0 Then Return "[日志不可读取]"
                Parent = Parent.Parent
            End While
            Using Stream As New FileStream(Filename, FileMode.Open, FileAccess.Read, FileShare.ReadWrite Or FileShare.Delete)
                Dim Offset = Math.Max(0, Stream.Length - 131072)
                Dim Head As String = ""
                If Offset > 0 Then
                    Using First As New StreamReader(Stream, Encoding.UTF8, True, 4096, True)
                        Dim Chars(16383) As Char
                        Dim Count = First.ReadBlock(Chars, 0, Chars.Length)
                        Head = New String(Chars, 0, Count)
                        Dim LineEnd = Head.LastIndexOf(ChrW(10))
                        Head = If(LineEnd >= 0, Head.Substring(0, LineEnd + 1), "")
                    End Using
                End If
                Stream.Seek(Offset, SeekOrigin.Begin)
                Using Reader As New StreamReader(Stream, Encoding.UTF8, True)
                    If Offset > 0 Then Reader.ReadLine()
                    Return Head & If(Offset > 0, "[中间内容已截断]" & vbLf, "") & Reader.ReadToEnd()
                End Using
            End Using
        Catch
            Return "[日志不可读取]"
        End Try
    End Function

    Public Function RedactCrashText(Text As String) As String
        Text = If(Text, "")
        Text = Regex.Replace(Text, "(?im)^.*(?:\[CHAT\]|\[Not Secure\]|\[CHAT/[^\]]*\]).*$", "[聊天内容已省略]")
        Text = RedactBrclCredentials(Text, "[REDACTED]")
        Text = Regex.Replace(Text, "\beyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\b", "[REDACTED_JWT]")
        Text = Regex.Replace(Text, "(?i)[A-Z]:[\\/]Users[\\/][^\\/\r\n]+", "<USERPROFILE>")
        Text = Regex.Replace(Text, "(?i)(--(?:username|uuid|xuid)\s+)[^\s]+", "$1[PLAYER]")
        Text = Regex.Replace(Text, "(?i)(\b(?:username|playername|uuid|xuid)""?\s*[:=]\s*""?)[^\s,"";']+", "$1[PLAYER]")
        Text = Regex.Replace(Text, "\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b", "[EMAIL]", RegexOptions.IgnoreCase)

        Try
            Text = FilterUserName(FilterAccessToken(Text, "*"c), "*"c)
        Catch
        End Try
        Return Text
    End Function

    Private Sub QueueReport(Kind As String, Reason As String, InstanceName As String, Logs As List(Of JObject), Optional Session As String = Nothing, Optional Mc As String = "", Optional JavaInfo As String = "", Optional Pack As String = "")
        If Not CrashUploadsEnabled() Then Return
        Dim Report As New JObject From {
            {"id", Guid.NewGuid().ToString("N")}, {"kind", Kind}, {"version", BrclReleaseVersion},
            {"created_utc", DateTime.UtcNow.ToString("O")}, {"instance", RedactCrashText(InstanceName)},
            {"reason", Reason}, {"os", Environment.OSVersion.VersionString}, {"files", New JArray(Logs)}}
        If Kind = "minecraft" Then
            Report("mc") = Mc
            Report("java") = JavaInfo
            Report("pack") = Pack
        End If
        SyncLock Gate
            If Not CrashUploadsEnabled() Then Return
            If UsageEnabled() Then
                Report("install") = InstallId()
                If Session IsNot Nothing Then Report("session") = Session
            End If
            Persist(If(Report("install") Is Nothing, Pending, LinkedPending), CStr(Report("id")), ".json", Encoding.UTF8.GetBytes(SealCrashReport(Report)))
        End SyncLock
        FlushCrashReports()
    End Sub

    Private Sub Persist(Folder As String, Id As String, Extension As String, Bytes As Byte())
        Directory.CreateDirectory(Folder)
        For Each Old In New DirectoryInfo(Folder).GetFiles("*" & Extension).OrderByDescending(Function(F) F.LastWriteTimeUtc).Skip(49)
            Old.Delete()
        Next
        Dim Target = Path.Combine(Folder, Id & Extension)
        If File.Exists(Target) Then Return
        Dim Temp = Path.Combine(Folder, Guid.NewGuid().ToString("N") & ".tmp")
        File.WriteAllBytes(Temp, Bytes)
        File.Move(Temp, Target)
    End Sub

    Public Function ReviewReports() As String()
        If Not Directory.Exists(DraftFolder) Then Return New String() {}
        Dim Result As New List(Of String)
        For Each Info In New DirectoryInfo(DraftFolder).GetFiles("*.draft").OrderBy(Function(F) F.LastWriteTimeUtc)
            If Info.LastWriteTimeUtc >= DateTime.UtcNow.AddDays(-14) Then
                Result.Add(Info.FullName)
            Else
                Info.Delete()
            End If
        Next
        Return Result.ToArray()
    End Function

    Public Function ReadDraft(Filename As String) As JObject
        Return JObject.Parse(Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(Filename), Nothing, DataProtectionScope.CurrentUser)))
    End Function

    Public Sub ApproveDraft(Filename As String, Report As JObject)
        SyncLock Gate
            If Not CrashUploadsEnabled() OrElse Not File.Exists(Filename) Then Return
            If Not UsageEnabled() Then
                Report.Remove("install")
                Report.Remove("session")
            End If
            Persist(If(Report("install") Is Nothing, Pending, LinkedPending), CStr(Report("id")), ".json", Encoding.UTF8.GetBytes(SealCrashReport(Report)))
            File.Delete(Filename)
        End SyncLock
        FlushCrashReports()
    End Sub

    Private Sub QueueAutomaticDrafts(Token As Threading.CancellationToken)
        For Each Draft In ReviewReports()
            If Token.IsCancellationRequested OrElse CrashMode() <> 2 Then Return
            Try
                ApproveDraft(Draft, ReadDraft(Draft))
            Catch ex As Exception
                Logger.Warn(ex, "转换旧版诊断报告失败，已保留报告")
            End Try
        Next
    End Sub

    Public Sub RevokeUsageLinkage()
        SyncLock Gate
            UploadCancel.Cancel()
            UploadCancel = New Threading.CancellationTokenSource
            DeleteQueueFiles(LinkedPending)
            For Each Draft In ReviewReports()
                Dim Report = ReadDraft(Draft)
                If Report.Remove("install") Then
                    Report.Remove("session")
                    Dim Temp = Draft & ".tmp"
                    File.WriteAllBytes(Temp, ProtectedData.Protect(Encoding.UTF8.GetBytes(Report.ToString()), Nothing, DataProtectionScope.CurrentUser))
                    File.Replace(Temp, Draft, Nothing)
                End If
            Next
        End SyncLock
    End Sub

    Public Function SealCrashReport(Report As JObject) As String
        Dim Raw = Encoding.UTF8.GetBytes(Report.ToString(Newtonsoft.Json.Formatting.None))
        If Raw.Length > 2097152 Then Throw New IOException("Crash report too large")
        Dim Keys(63) As Byte
        Using Rng = RandomNumberGenerator.Create()
            Rng.GetBytes(Keys)
        End Using
        Dim IV As Byte(), Ciphertext As Byte(), Wrapped As Byte(), Mac As Byte()
        Using Aes = Security.Cryptography.Aes.Create()
            Aes.Key = Keys.Take(32).ToArray()
            Aes.GenerateIV()
            IV = Aes.IV
            Using Encryptor = Aes.CreateEncryptor()
                Ciphertext = Encryptor.TransformFinalBlock(Raw, 0, Raw.Length)
            End Using
        End Using
        Using Rsa As New RSACryptoServiceProvider
            Rsa.PersistKeyInCsp = False
            Rsa.FromXmlString(UploadPublicKey)
            Wrapped = Rsa.Encrypt(Keys, True)
        End Using
        Using Signer As New HMACSHA256(Keys.Skip(32).ToArray())
            Mac = Signer.ComputeHash(Encoding.UTF8.GetBytes("BRCL-REPORT-V1").Concat(IV).Concat(Ciphertext).ToArray())
        End Using
        Array.Clear(Keys, 0, Keys.Length)
        Return New JObject From {{"v", 1}, {"key", Convert.ToBase64String(Wrapped)}, {"iv", Convert.ToBase64String(IV)},
            {"data", Convert.ToBase64String(Ciphertext)}, {"mac", Convert.ToBase64String(Mac)}}.ToString(Newtonsoft.Json.Formatting.None)
    End Function

    Public Function VerifyCrashReceipt(Response As JObject, Id As String) As Boolean
        Try
            If Not Response.Value(Of Boolean?)("ok").GetValueOrDefault(False) OrElse CStr(Response("id")) <> Id Then Return False
            Using Rsa As New RSACryptoServiceProvider
                Rsa.PersistKeyInCsp = False
                Rsa.FromXmlString(UploadPublicKey)
                Return Rsa.VerifyData(Encoding.UTF8.GetBytes("BRCL-RECEIPT-V1|" & Id), CryptoConfig.MapNameToOID("SHA256"), Convert.FromBase64String(CStr(Response("receipt"))))
            End Using
        Catch
            Return False
        End Try
    End Function

    Private Sub SendQueuedReports(Client As HttpClient, Token As Threading.CancellationToken)
        Dim Folders = If(UsageEnabled(), {Pending, LinkedPending}, {Pending})
        Dim Reports = Folders.Where(Function(D) Directory.Exists(D)).SelectMany(Function(D) New DirectoryInfo(D).GetFiles("*.json")).
            Where(Function(F) Regex.IsMatch(F.Name, "\A[a-f0-9]{32}\.json\z")).OrderBy(Function(F) F.LastWriteTimeUtc).ToArray()
        Dim HadFailure As Boolean = False
        For Each Info In Reports
            If Not CrashUploadsEnabled() OrElse Token.IsCancellationRequested Then Return
            If Info.Length > 3145728 OrElse Info.LastWriteTimeUtc < DateTime.UtcNow.AddDays(-14) Then
                Info.Delete()
                Continue For
            End If
            Dim Id = Path.GetFileNameWithoutExtension(Info.Name)
            Try
                Using Body As New StringContent(File.ReadAllText(Info.FullName), Encoding.UTF8, "application/json"),
                      Response = Client.PostAsync(Endpoint, Body, Token).GetAwaiter().GetResult()
                    If CInt(Response.StatusCode) = 429 OrElse CInt(Response.StatusCode) >= 500 Then Throw New TimeoutException("诊断接收服务暂不可用")
                    Response.EnsureSuccessStatusCode()
                    Dim Receipt = JObject.Parse(Response.Content.ReadAsStringAsync().GetAwaiter().GetResult())
                    If Not VerifyCrashReceipt(Receipt, Id) Then Throw New IOException("回执签名校验失败")
                    Info.Delete()
                End Using
            Catch ex As HttpRequestException When Not Token.IsCancellationRequested
                HadFailure = True
                If ex.InnerException IsNot Nothing Then Exit For
            Catch ex As IOException When Not Token.IsCancellationRequested
                HadFailure = True
            Catch ex As Newtonsoft.Json.JsonException
                HadFailure = True
            End Try
        Next
        If HadFailure Then Throw New IOException("部分报告尚未收到有效回执")
    End Sub

    Public Sub FlushCrashReports()
        If DateTime.UtcNow < NextAttempt OrElse Not CrashUploadsEnabled() OrElse Threading.Interlocked.Exchange(Uploading, 1) <> 0 Then Return
        RunInNewThread(
            Sub()
                Try
                    Dim Token As Threading.CancellationToken
                    SyncLock Gate
                        Token = UploadCancel.Token
                    End SyncLock
                    QueueAutomaticDrafts(Token)
                    Using Client = CreateInkClient(15)
                        SendQueuedReports(Client, Token)
                    End Using
                    FailureCount = 0
                Catch ex As OperationCanceledException When Not CrashUploadsEnabled()
                    'Opting out cancels uploads and erases pending reports.
                Catch ex As Exception
                    FailureCount = Math.Min(8, FailureCount + 1)
                    NextAttempt = DateTime.UtcNow.AddSeconds(15 * Math.Pow(2, FailureCount))
                    Logger.Warn(ex, "诊断日志上传失败，已保留并将在后台重试")
                    'Leave encrypted reports queued; a later timer/startup retries.
                Finally
                    Threading.Interlocked.Exchange(Uploading, 0)
                End Try
            End Sub, "BRCL Crash Report Upload", Threading.ThreadPriority.BelowNormal)
    End Sub
End Module
