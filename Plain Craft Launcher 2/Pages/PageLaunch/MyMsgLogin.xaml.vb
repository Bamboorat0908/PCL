Imports System.Windows.Threading

Public Class MyMsgLogin
    Private ReadOnly MyConverter As MyMsgBoxConverter
    Private ReadOnly Uuid As Integer = GetUuid()
    Private ReadOnly Lifetime As Stopwatch = Stopwatch.StartNew()
    Private Session As MicrosoftDeviceLogin
    Private StatusTimer As DispatcherTimer
    Private HasStarted As Boolean
    Private NetworkStatus As String = "等待浏览器授权"

    Public Sub New(Converter As MyMsgBoxConverter)
        MyConverter = Converter
        Try
            InitializeComponent()
            Session = New MicrosoftDeviceLogin(CType(Converter.Content, JObject))
            LabCode.Text = Session.UserCode
            LabWebsite.Text = Session.Website
            LabCaption.Text = "在浏览器中输入下方代码并完成授权，启动器会自动继续。也可以用手机或其他设备打开登录网址。"
            LabHelp.Text = "网页无法打开时，请检查网络或系统代理，再点「打开登录网页」。取消后不会继续使用本次授权结果。"
            '构造时不启动浏览器和线程，确保初始化失败或离线预览不会留下后台请求。
        Catch ex As Exception
            MyConverter.Result = New Exception("$无法准备微软登录，请重新登录。", ex)
            MyConverter.IsExited = True
            Logger.Error(ex, "登录弹窗初始化失败")
        End Try
    End Sub

    Private Sub Load(sender As Object, e As EventArgs) Handles MyBase.Loaded
        If MyConverter.IsExited Then
            Close()
            Return
        End If
        UpdateBounds()
        AddHandler FrmMain.PanMsg.SizeChanged, AddressOf OwnerSizeChanged
        Btn1.Focus()
        Opacity = 0
        AniStart(AaColor(FrmMain.PanMsg, Grid.BackgroundProperty, New MyColor(160, 0, 0, 0) - FrmMain.PanMsg.Background, 160), "PanMsg Background")
        AniStart({
            AaOpacity(Me, 1, If(SystemParameters.ClientAreaAnimation, 150, 1)),
            AaDouble(Sub(i) TransformPos.Y += i, -TransformPos.Y, If(SystemParameters.ClientAreaAnimation, 180, 1),, New AniEaseOutFluent)
        }, "MyMsgBox " & Uuid)
        If HasStarted Then Return
        HasStarted = True
        StatusTimer = New DispatcherTimer With {.Interval = TimeSpan.FromSeconds(1)}
        AddHandler StatusTimer.Tick, AddressOf UpdateStatus
        StatusTimer.Start()
        UpdateStatus()
        RunInNewThread(AddressOf WorkThread, "MyMsgLogin")
        Logger.Info("微软设备登录弹窗已显示")
    End Sub

    Private Sub OwnerSizeChanged(Sender As Object, E As SizeChangedEventArgs)
        UpdateBounds()
    End Sub

    Private Sub UpdateBounds()
        If FrmMain?.PanMsg Is Nothing Then Return
        Width = Math.Max(280, Math.Min(560, FrmMain.PanMsg.ActualWidth - 40))
        MinWidth = Math.Min(340, Width)
        MaxHeight = Math.Max(210, FrmMain.PanMsg.ActualHeight - 40)
        PanCaption.MaxHeight = Math.Max(60, MaxHeight - 165)
    End Sub

    Private Sub UpdateStatus(Optional Sender As Object = Nothing, Optional E As EventArgs = Nothing)
        If MyConverter.IsExited Then
            Close()
            Return
        End If
        If Session Is Nothing Then Return
        Dim Remaining = Session.RemainingSeconds(Lifetime.Elapsed.TotalSeconds)
        LabStatus.Text = NetworkStatus
        LabCountdown.Text = $"代码有效期 {Remaining \ 60:00}:{Remaining Mod 60:00}"
        If Remaining = 0 Then Finished(New Exception("$登录代码已过期，请重新登录。"))
    End Sub

    Private Sub SetStatus(Text As String)
        RunInUi(Sub()
                    If MyConverter.IsExited Then Return
                    NetworkStatus = Text
                    UpdateStatus()
                End Sub)
    End Sub

    Private Sub StopStatusTimer() Handles Me.Unloaded
        If StatusTimer IsNot Nothing Then
            StatusTimer.Stop()
            RemoveHandler StatusTimer.Tick, AddressOf UpdateStatus
            StatusTimer = Nothing
        End If
        If FrmMain?.PanMsg IsNot Nothing Then RemoveHandler FrmMain.PanMsg.SizeChanged, AddressOf OwnerSizeChanged
        If Not MyConverter.IsExited Then Finished(New OperationCanceledException())
    End Sub

    Private Sub Close()
        StopStatusTimer()
        AniStart({
            AaCode(Sub()
                       If Not WaitingMyMsgBox.Any() AndAlso FrmMain?.PanMsg IsNot Nothing Then
                           AniStart(AaColor(FrmMain.PanMsg, Grid.BackgroundProperty, New MyColor(0, 0, 0, 0) - FrmMain.PanMsg.Background, 160))
                       End If
                   End Sub, 30),
            AaOpacity(Me, -Opacity, If(SystemParameters.ClientAreaAnimation, 100, 1)),
            AaDouble(Sub(i) TransformPos.Y += i, 6 - TransformPos.Y, If(SystemParameters.ClientAreaAnimation, 100, 1),, New AniEaseOutFluent),
            AaCode(Sub()
                       Dim Host = TryCast(Parent, Grid)
                       Host?.Children.Remove(Me)
                   End Sub,, True)
        }, "MyMsgBox " & Uuid)
    End Sub

    Public Sub Btn1_Click() Handles Btn1.Click
        If MyConverter.IsExited OrElse Session Is Nothing Then Return
        Try
            OpenWebsite(Session.Website)
            LabHelp.Text = "已请求打开浏览器。请完成网页授权，然后返回启动器等待登录结果。"
        Catch
            LabHelp.Text = "无法打开浏览器。请手动打开 " & Session.Website & " 并输入设备代码。"
        End Try
    End Sub

    Public Sub Btn2_Click() Handles Btn2.Click
        If MyConverter.IsExited OrElse Session Is Nothing Then Return
        Try
            Clipboard.SetText(Session.UserCode)
            Btn2.Text = "已复制代码"
        Catch
            LabHelp.Text = "剪贴板暂不可用，请手动输入上方设备代码。"
        End Try
    End Sub

    Public Sub Btn3_Click() Handles Btn3.Click
        Finished(New OperationCanceledException())
    End Sub

    Private Sub Drag(sender As Object, e As MouseButtonEventArgs) Handles LabTitle.MouseLeftButtonDown
        Try
            FrmMain.DragMove()
        Catch
        End Try
    End Sub

    Private Function Finished(Result As Object) As Boolean
        '取消、到期与网络结果可能同时到达，只允许一个结果唤醒等待方。
        SyncLock MyConverter
            If MyConverter.IsExited Then Return False
            MyConverter.Result = Result
            MyConverter.IsExited = True
        End SyncLock
        RunInUi(Sub()
                    Close()
                    If TypeOf Result Is String() Then
                        Hint("网页登录成功！", HintType.Green)
                        FrmMain?.ShowWindowToTop()
                    End If
                End Sub)
        Return True
    End Function

    Private Sub WorkThread()
        Try
            If MyConverter.IsExited Then Return
            RunInUi(Sub()
                        Btn2_Click()
                        Btn1_Click()
                    End Sub)
            Do While Not MyConverter.IsExited
                Dim WaitUntil = Lifetime.Elapsed.TotalSeconds + Session.PollSeconds
                Do While Lifetime.Elapsed.TotalSeconds < WaitUntil
                    If MyConverter.IsExited Then Return
                    If Session.RemainingSeconds(Lifetime.Elapsed.TotalSeconds) = 0 Then
                        Finished(New Exception("$登录代码已过期，请重新登录。"))
                        Return
                    End If
                    Thread.Sleep(100)
                Loop
                If MyConverter.IsExited Then Return
                Dim Remaining = Session.ExpiresSeconds - Lifetime.Elapsed.TotalSeconds
                If Remaining <= 0 Then
                    Finished(New Exception("$登录代码已过期，请重新登录。"))
                    Return
                End If
                Try
                    Dim Result = NetRequestByClient(
                        "https://login.microsoftonline.com/consumers/oauth2/v2.0/token", HttpMethod.Post,
                        Content:="grant_type=urn:ietf:params:oauth:grant-type:device_code&client_id=" & OAuthClientId &
                            "&device_code=" & StringUtils.FormUrlEscape(Session.DeviceCode) & "&scope=XboxLive.signin%20offline_access",
                        ContentType:="application/x-www-form-urlencoded",
                        Timeout:=CInt(Math.Max(1, Math.Min(20000, Math.Ceiling(Remaining * 1000)))),
                        MakeLog:=False, RequireJson:=True)
                    If MyConverter.IsExited Then Return
                    Dim Tokens = MicrosoftDeviceLogin.ReadTokens(JObject.Parse(Result))
                    Finished(Tokens)
                    Return
                Catch ex As HttpRequestCodeException
                    If MyConverter.IsExited Then Return
                    Dim Response = If(ex.Response, "")
                    Select Case MicrosoftDeviceLogin.ErrorCode(Response)
                        Case "authorization_pending"
                            Session.AuthorizationPending()
                            SetStatus("等待浏览器授权")
                            Continue Do
                        Case "slow_down"
                            Session.SlowDown()
                            SetStatus("微软要求稍候，正在降低查询频率")
                            Continue Do
                        Case "authorization_declined", "access_denied"
                            Finished(New Exception("$你已拒绝微软登录授权，可以重新登录。"))
                            Return
                        Case "expired_token", "code_expired"
                            Finished(New Exception("$登录代码已过期，请重新登录。"))
                            Return
                        Case "invalid_client", "unauthorized_client"
                            Finished(New Exception("$微软应用 Client ID 无效或未启用设备代码登录，请检查应用配置。"))
                            Return
                    End Select
                    '保留上游账号安全兼容处理，但不向日志写入原始响应。
                    If Response.Contains("Account security interrupt") Then
                        Finished(New Exception("$该账号需要完成安全验证，请前往微软账户页查看。"))
                        Return
                    ElseIf Response.Contains("service abuse") Then
                        Finished(New Exception("$该账号已被微软限制，请前往微软账户页查看。"))
                        Return
                    ElseIf Response.Contains("AADSTS70000") Then
                        Finished(New RestartException())
                        Return
                    End If
                    Dim Status = CInt(ex.StatusCode)
                    If Status <> 408 AndAlso Status <> 429 AndAlso Status < 500 Then
                        Finished(New Exception("$微软拒绝了登录请求，请重新登录或检查应用配置。"))
                        Return
                    End If
                    If Not RetryNetwork() Then Return
                Catch ex As FormatException
                    Finished(New Exception("$微软登录响应不完整，请重新登录。"))
                    Return
                Catch ex As Exception
                    If MyConverter.IsExited Then Return
                    If Not RetryNetwork() Then Return
                End Try
            Loop
        Catch ex As Exception
            '防止线程初始化或异常处理分支出错后，调用方永久等不到结果。
            Finished(New Exception("$微软登录暂时无法继续，请重新登录。"))
        End Try
    End Sub

    Private Function RetryNetwork() As Boolean
        If Not Session.TransientFailure() Then
            Finished(New Exception("$连续多次无法连接微软登录服务。请检查网络和系统代理后重新登录。"))
            Return False
        End If
        Logger.Info($"微软登录轮询第 {Session.FailureCount} 次暂时失败，等待后重试")
        SetStatus($"连接暂时中断，{Session.PollSeconds:0} 秒后重试（{Session.FailureCount}/3）")
        Return True
    End Function
End Class
