Imports Microsoft.Web.WebView2.Core
Imports System.IO
Imports System.Threading
Imports System.Windows.Threading

Public Class PageMap
    Implements IRefreshable

    Private Const MapUrl As String = "http://t42.sjcmc.cn:25277/"
    Private IsInitializing As Boolean
    Private HasStarted As Boolean
    Private ActiveNavigationId As ULong
    Private ConnectionVersion As Integer
    Private Timeout As DispatcherTimer
    Private BrowserActivity As BrclWebViewActivity

    Private Sub SetBrowserVisibility(Value As Visibility)
        If BrowserActivity Is Nothing Then
            ViewMap.Visibility = Value
        Else
            BrowserActivity.SetVisibility(Value)
        End If
    End Sub

    Private Sub EnterPage() Handles Me.Loaded
        If BrowserActivity Is Nothing Then BrowserActivity = New BrclWebViewActivity(Me, ViewMap)
        BrowserActivity.Refresh()
        ApplyWebTheme()
        If Not HasStarted Then
            HasStarted = True
            Connect()
        End If
    End Sub

    Private Async Sub Connect()
        If IsInitializing Then Return
        IsInitializing = True
        ConnectionVersion += 1
        Dim Version = ConnectionVersion
        ShowLoading()
        Try
            If ViewMap.CoreWebView2 Is Nothing Then
                If Not Await BrclWebViewRuntime.WaitForPageTransitionAsync(Me) Then
                    HasStarted = False
                    Return
                End If
                Dim Environment = Await BrclWebViewRuntime.GetEnvironmentAsync()
                Dim Options = Environment.CreateCoreWebView2ControllerOptions()
                Options.ProfileName = "BRCLMap"
                Await ViewMap.EnsureCoreWebView2Async(Environment, Options)
                AddHandler ViewMap.CoreWebView2.NavigationStarting, AddressOf NavigationStarting
                AddHandler ViewMap.CoreWebView2.NavigationCompleted, AddressOf NavigationCompleted
                AddHandler ViewMap.CoreWebView2.NewWindowRequested, AddressOf NewWindowRequested
                AddHandler ViewMap.CoreWebView2.ProcessFailed, AddressOf ProcessFailed
                ViewMap.CoreWebView2.Settings.IsStatusBarEnabled = False
                ViewMap.CoreWebView2.Settings.AreDevToolsEnabled = False
            End If
            If Version <> ConnectionVersion Then Return '初始化超时后不得再覆盖失败提示。
            ApplyWebTheme()
            ViewMap.CoreWebView2.Navigate(MapUrl)
            BrowserActivity?.Refresh()
        Catch ex As WebView2RuntimeNotFoundException
            ShowUnavailable("请安装 Microsoft WebView2 运行时后重新连接。", "缺少地图运行环境")
        Catch ex As Exception
            Logger.Warn(ex, "连接卫星地图失败")
            ShowUnavailable("地图服务可能仍在部署中，或网络暂时不可用。请稍后重新连接。")
        Finally
            IsInitializing = False
        End Try
    End Sub

    Private Sub NavigationStarting(sender As Object, e As CoreWebView2NavigationStartingEventArgs)
        ActiveNavigationId = e.NavigationId
        ShowLoading()
    End Sub

    Private Sub NavigationCompleted(sender As Object, e As CoreWebView2NavigationCompletedEventArgs)
        If e.NavigationId <> ActiveNavigationId Then Return
        StopTimeout()
        If e.IsSuccess AndAlso e.HttpStatusCode < 400 Then
            '地图可能完全由 Canvas/WebGL 渲染，不能按正文长度判定为空白。
            CardLoading.Visibility = Visibility.Collapsed
            CardUnavailable.Visibility = Visibility.Collapsed
            SetBrowserVisibility(Visibility.Visible)
        Else
            Dim Detail As String = If(e.HttpStatusCode >= 400, "HTTP " & e.HttpStatusCode.ToString(), e.WebErrorStatus.ToString())
            Logger.Info("卫星地图暂不可用：" & Detail)
            ShowUnavailable("地图服务正在部署中或暂时无法连接。请稍后重试。（" & Detail & "）")
        End If
    End Sub

    Private Sub ProcessFailed(sender As Object, e As CoreWebView2ProcessFailedEventArgs)
        ShowUnavailable("地图浏览器进程异常。请重新连接；若仍无法恢复，请重新打开启动器。")
    End Sub

    Private Sub NewWindowRequested(sender As Object, e As CoreWebView2NewWindowRequestedEventArgs)
        e.Handled = True
        Dim Target As Uri = Nothing
        If Not Uri.TryCreate(e.Uri, UriKind.Absolute, Target) Then Return
        If Target.Scheme <> Uri.UriSchemeHttp AndAlso Target.Scheme <> Uri.UriSchemeHttps Then Return
        Try
            OpenWebsite(Target.AbsoluteUri)
        Catch ex As Exception
            Logger.Warn(ex, "打开地图外部链接失败")
            Hint("打开浏览器失败：" & ex.Message)
        End Try
    End Sub

    Public Sub ApplyWebTheme()
        Dim IsLight As Boolean = Settings.Get(Of Integer)("UiThemeMode") = 1
        ViewMap.DefaultBackgroundColor = If(IsLight, Global.System.Drawing.Color.FromArgb(231, 232, 227), Global.System.Drawing.Color.FromArgb(17, 20, 22))
        Try
            If ViewMap.CoreWebView2 IsNot Nothing Then
                ViewMap.CoreWebView2.Profile.PreferredColorScheme = If(IsLight, CoreWebView2PreferredColorScheme.Light, CoreWebView2PreferredColorScheme.Dark)
            End If
        Catch ex As Exception
            Logger.Warn(ex, "更新卫星地图主题失败")
        End Try
    End Sub

    Private Sub ShowLoading()
        StopTimeout()
        SetBrowserVisibility(Visibility.Hidden)
        CardUnavailable.Visibility = Visibility.Collapsed
        CardLoading.Visibility = Visibility.Visible
        Timeout = New DispatcherTimer With {.Interval = TimeSpan.FromSeconds(15)}
        AddHandler Timeout.Tick, AddressOf TimedOut
        Timeout.Start()
    End Sub

    Private Sub TimedOut(sender As Object, e As EventArgs)
        '停止旧导航，避免超时后仍有旧页面完成事件覆盖下一次重试。
        ActiveNavigationId = 0
        Try
            ViewMap.CoreWebView2?.Stop()
        Catch ex As Exception
        End Try
        ShowUnavailable("15 秒内未连接到地图服务。服务正在部署中，请稍后重试。")
    End Sub

    Private Sub StopTimeout()
        If Timeout Is Nothing Then Return
        Timeout.Stop()
        RemoveHandler Timeout.Tick, AddressOf TimedOut
        Timeout = Nothing
    End Sub

    Private Sub ShowUnavailable(Message As String, Optional Title As String = "卫星地图暂未就绪")
        StopTimeout()
        ActiveNavigationId = 0
        ConnectionVersion += 1
        Try
            ViewMap.CoreWebView2?.Stop()
        Catch
        End Try
        SetBrowserVisibility(Visibility.Hidden)
        CardLoading.Visibility = Visibility.Collapsed
        LabTitle.Text = Title
        LabMessage.Text = Message
        CardUnavailable.Visibility = Visibility.Visible
    End Sub

    Private Sub Retry() Handles BtnRetry.Click
        Connect()
    End Sub

    Private Sub OpenInBrowser() Handles BtnBrowser.Click
        Try
            OpenWebsite(MapUrl)
        Catch ex As Exception
            Logger.Warn(ex, "打开卫星地图浏览器失败")
            Hint("打开浏览器失败，请手动访问 " & MapUrl)
        End Try
    End Sub

    Public Sub RefreshPage() Implements IRefreshable.Refresh
        If CardUnavailable.Visibility = Visibility.Visible OrElse ViewMap.CoreWebView2 Is Nothing Then
            Connect()
            Return
        End If
        Try
            ViewMap.CoreWebView2.Reload()
        Catch ex As Exception
            Logger.Warn(ex, "刷新卫星地图失败")
            ShowUnavailable("地图暂时无法刷新，请重新连接。")
        End Try
    End Sub
End Class
