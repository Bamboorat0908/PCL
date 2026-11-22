Imports System.IO
Imports System.Net.Http
Imports System.Security.Cryptography
Imports System.Text
Imports System.Windows.Controls

Friend Module ModExperience
    Public Const InkBase As String = "https://zhushu.homes/BRCL/InkAdmin/collect/"
    Private Const UsageSetting As String = "BrclUsageEnabled"
    Private ReadOnly UsageGate As New Object
    Private ReadOnly ActivityFolder As String = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "BRCL", "Experience")
    Private ReadOnly IdentityFile As String = Path.Combine(ActivityFolder, "install.id")
    Private ReadOnly ActivityQueue As String = Path.Combine(ActivityFolder, "Events")
    Private UsageCancel As New Threading.CancellationTokenSource
    Private Sending As Integer
    Private ReviewOpen As Integer

    Public Function UsageEnabled() As Boolean
        Return Settings.Get(Of Boolean)(UsageSetting)
    End Function

    Public Function UsageChoiceNeeded() As Boolean
        Return Not Settings.HasSaved(UsageSetting)
    End Function

    Public Sub SaveUsageChoice(Enabled As Boolean)
        Settings.Set(UsageSetting, Enabled)
        Settings.Set("BrclCrashMode", If(Enabled, 2, 0))
        'The first explicit refusal must be saved even though False is the default.
        If Not Enabled AndAlso Not Settings.HasSaved(UsageSetting) Then Settings.Entries(UsageSetting).Save()
        If Application.Current IsNot Nothing Then RunInUi(Sub() FrmSetupPrivacy?.RefreshUsagePreference())
    End Sub

    Public Function InstallId() As String
        SyncLock UsageGate
            If Not UsageEnabled() Then Return Nothing
            Directory.CreateDirectory(ActivityFolder)
            If Not File.Exists(IdentityFile) Then
                Dim Candidate = Guid.NewGuid().ToString("N")
                Dim Temporary = Path.Combine(ActivityFolder, Candidate & ".tmp")
                File.WriteAllText(Temporary, Candidate)
                Try
                    File.Move(Temporary, IdentityFile)
                Catch ex As IOException When File.Exists(IdentityFile)
                    File.Delete(Temporary)
                End Try
            End If
            Return Guid.ParseExact(File.ReadAllText(IdentityFile), "N").ToString("N")
        End SyncLock
    End Function

    Public Sub UsageChanged()
        Try
            'Cancel uploads even if an old queue file cannot be removed.
            Settings.Set("BrclCrashMode", If(UsageEnabled(), 2, 0))
            If Not UsageEnabled() Then CrashUploadSettingChanged()
            SyncLock UsageGate
                UsageCancel.Cancel()
                UsageCancel = New Threading.CancellationTokenSource
                If Not UsageEnabled() Then
                    DeleteQueueFiles(ActivityQueue)
                    File.Delete(IdentityFile)
                    File.Delete(Path.Combine(ActivityFolder, "last-open.txt"))
                End If
            End SyncLock
            If UsageEnabled() Then
                RecordUsage()
            Else
                RevokeUsageLinkage()
            End If
        Catch ex As Exception
            Logger.Warn(ex, "更新使用统计选项失败")
        End Try
    End Sub

    Public Sub DeleteQueueFiles(Folder As String)
        If Not Directory.Exists(Folder) Then Return
        For Each Pattern In {"*.json", "*.tmp", "*.draft"}
            For Each Filename In Directory.GetFiles(Folder, Pattern)
                File.Delete(Filename)
            Next
        Next
    End Sub

    Public Sub RecordUsage(Optional Session As String = Nothing)
        Try
            SyncLock UsageGate
                Dim Id = InstallId()
                If Id Is Nothing Then Return
                Dim Stamp = DateTime.UtcNow
                Dim Item As New JObject From {{"install", Id}, {"version", BrclReleaseVersion}, {"created_utc", Stamp.ToString("O")},
                    {"type", If(Session Is Nothing, "open", "game")}}
                Dim Key = Stamp.AddHours(8).ToString("yyyyMMdd") & "-" & BrclReleaseVersion
                If Session Is Nothing Then
                    Dim LastOpen = Path.Combine(ActivityFolder, "last-open.txt")
                    If File.Exists(LastOpen) AndAlso File.ReadAllText(LastOpen) = Key Then Return
                End If
                If Session IsNot Nothing Then
                    Key = Session
                    Item("session") = Session
                    Item("diagnostic") = (CrashMode() = 2)
                End If
                Directory.CreateDirectory(ActivityQueue)
                Dim Target = Path.Combine(ActivityQueue, Key & ".json")
                For Each Old In New DirectoryInfo(ActivityQueue).GetFiles("*.json").OrderByDescending(Function(F) F.LastWriteTimeUtc).Skip(999)
                    Old.Delete()
                Next
                Dim Temporary = Path.Combine(ActivityQueue, Guid.NewGuid().ToString("N") & ".tmp")
                File.WriteAllText(Temporary, Item.ToString(), New UTF8Encoding(False))
                Try
                    File.Move(Temporary, Target)
                Catch ex As IOException When File.Exists(Target)
                    File.Delete(Temporary)
                End Try
            End SyncLock
            FlushUsage()
        Catch ex As Exception
            Logger.Warn(ex, "保存体验改善计划统计失败")
        End Try
    End Sub

    Public Function CreateInkClient(TimeoutSeconds As Integer) As HttpClient
        Dim Client As New HttpClient(New HttpClientHandler With {.AllowAutoRedirect = False}) With {
            .Timeout = TimeSpan.FromSeconds(TimeoutSeconds), .MaxResponseContentBufferSize = 16384}
        'The HTTPS proxy rejects requests without a User-Agent.
        Client.DefaultRequestHeaders.UserAgent.ParseAdd("BRCL/" & BrclReleaseVersion)
        Return Client
    End Function

    Public Sub FlushUsage()
        If Not UsageEnabled() OrElse Threading.Interlocked.Exchange(Sending, 1) <> 0 Then Return
        RunInNewThread(Sub()
            Try
                Dim Token As Threading.CancellationToken
                SyncLock UsageGate
                    Token = UsageCancel.Token
                End SyncLock
                If Not Directory.Exists(ActivityQueue) Then Return
                Using Client = CreateInkClient(12)
                    For Each Info In New DirectoryInfo(ActivityQueue).GetFiles("*.json").OrderBy(Function(F) F.LastWriteTimeUtc).Take(24)
                        If Token.IsCancellationRequested OrElse Not UsageEnabled() Then Return
                        If Info.LastWriteTimeUtc < DateTime.UtcNow.AddDays(-14) Then
                            Info.Delete()
                            Continue For
                        End If
                        Using Body As New StringContent(File.ReadAllText(Info.FullName), Encoding.UTF8, "application/json"),
                            Response = Client.PostAsync(InkBase & "activity", Body, Token).GetAwaiter().GetResult()
                            Response.EnsureSuccessStatusCode()
                            Dim Ack = JObject.Parse(Response.Content.ReadAsStringAsync().GetAwaiter().GetResult())
                            If Not Ack.Value(Of Boolean?)("ok").GetValueOrDefault(False) Then Return
                            SyncLock UsageGate
                                If Token.IsCancellationRequested OrElse Not UsageEnabled() Then Return
                                If Info.Name.Contains("-") Then File.WriteAllText(Path.Combine(ActivityFolder, "last-open.txt"), Path.GetFileNameWithoutExtension(Info.Name))
                                Info.Delete()
                            End SyncLock
                        End Using
                    Next
                End Using
            Catch ex As OperationCanceledException When Not UsageEnabled()
                'Opting out cancels the in-flight request.
            Catch ex As Exception
                Logger.Warn(ex, "体验改善计划统计上传失败，已保留并将在后台重试")
            Finally
                Threading.Interlocked.Exchange(Sending, 0)
            End Try
        End Sub, "BRCL 使用统计")
    End Sub

    Public Sub OfferPendingReports(Optional Explicit As Boolean = False)
        If Not Explicit Then
            FlushCrashReports()
            Return
        End If
        If Threading.Interlocked.Exchange(ReviewOpen, 1) <> 0 Then Return
        Try
            If CrashMode() = 0 Then Return
            Dim Drafts = ReviewReports()
            If Drafts.Length = 0 Then
                If Explicit Then MyMsgBox("当前没有待确认的崩溃报告。", "诊断报告")
                Return
            End If
            For Each Draft In Drafts
                Dim Report = ReadDraft(Draft)
                Dim Files = DirectCast(Report("files"), JArray)
                Dim Answer = MyMsgBox("发生时间 " & CStr(Report("created_utc")) & vbCrLf &
                    "分类 " & CStr(Report("kind")) & " · " & CStr(Report("reason")) & vbCrLf &
                    "附件 " & String.Join("、", Files.Select(Function(F) CStr(F("name")))) & vbCrLf &
                    "可以先查看全部脱敏内容。关闭窗口会保留这份报告。", "BRCL 崩溃诊断", "预览并选择", "删除此报告", "稍后处理")
                If Answer = 2 Then
                    File.Delete(Draft)
                ElseIf Answer = 1 Then
                    RunInUiWait(Sub() PreviewReport(Draft, Report))
                Else
                    Exit For
                End If
            Next
        Catch ex As Exception
            Logger.Warn(ex, "读取待确认的诊断报告失败")
        Finally
            Threading.Interlocked.Exchange(ReviewOpen, 0)
        End Try
    End Sub

    Private Sub PreviewReport(Draft As String, Report As JObject)
        Dim Dialog As New Window With {.Title = "诊断报告 · 发送内容预览", .Owner = FrmMain, .Width = 700, .Height = 510,
            .WindowStartupLocation = WindowStartupLocation.CenterOwner}
        Dim Layout As New DockPanel With {.Margin = New Thickness(14)}
        Dim Buttons As New StackPanel With {.Orientation = Orientation.Horizontal, .HorizontalAlignment = HorizontalAlignment.Right}
        Dim Send As New Button With {.Content = "同意发送此报告", .Padding = New Thickness(12,5,12,5)}
        Dim Cancel As New Button With {.Content = "稍后处理", .Padding = New Thickness(12,5,12,5), .Margin = New Thickness(8,0,0,0)}
        Buttons.Children.Add(Send)
        Buttons.Children.Add(Cancel)
        DockPanel.SetDock(Buttons, Dock.Bottom)
        Layout.Children.Add(Buttons)
        Layout.Children.Add(New TextBox With {.Text = Report.ToString(), .IsReadOnly = True, .TextWrapping = TextWrapping.Wrap,
            .VerticalScrollBarVisibility = ScrollBarVisibility.Auto, .Margin = New Thickness(0,0,0,10)})
        AddHandler Send.Click, Sub()
                                  ApproveDraft(Draft, Report)
                                  Dialog.Close()
                              End Sub
        AddHandler Cancel.Click, Sub() Dialog.Close()
        Dialog.Content = Layout
        Dialog.ShowDialog()
    End Sub
End Module
