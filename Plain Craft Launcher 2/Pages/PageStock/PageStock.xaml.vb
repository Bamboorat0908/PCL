Imports Microsoft.Web.WebView2.Core

''' <summary>
''' 交易所页面：内嵌浏览器显示 zhushu.homes/stock。
''' </summary>
Public Class PageStock
    Implements IRefreshable

    Private Const StockUrlDefault As String = "https://zhushu.homes/stock/"

    ''' <summary>实际加载地址：默认是交易所，设了 BRCL_STOCK_URL 环境变量时用覆盖值（离线自检用）。</summary>
    Private ReadOnly Property StockUrl As String
        Get
            Dim Override As String = Environment.GetEnvironmentVariable("BRCL_STOCK_URL")
            If Not String.IsNullOrEmpty(Override) Then Return Override
            Return StockUrlDefault
        End Get
    End Property
    Private IsInited As Boolean = False
    Private IsLoadFailed As Boolean = False
    Private TimeoutTimer As Threading.DispatcherTimer
    Private IsSyncingTheme As Boolean = False
    Private SyncPending As Boolean = False
    Private IsInitializing As Boolean = False
    Private NavigationVersion As Integer = 0
    Private ActiveNavigationId As ULong
    Private ReadyNavigationId As ULong
    Private HasReadyNavigation As Boolean = False
    Private ContentCheckVersion As Integer = -1
    Private BrowserActivity As BrclWebViewActivity

    Private Sub SetBrowserVisibility(Value As Visibility)
        If BrowserActivity Is Nothing Then
            ViewWeb.Visibility = Value
        Else
            BrowserActivity.SetVisibility(Value)
        End If
    End Sub

    Private Shared ReadOnly EmbedScript As String = LoadEmbedScript()

    Private Shared Function LoadEmbedScript() As String
        Using Reader As New Global.System.IO.StreamReader(GetType(PageStock).Assembly.GetManifestResourceStream("BRCL.StockEmbedded.js"))
            Return Reader.ReadToEnd()
        End Using
    End Function

    Private Function IsStockSource(Source As String) As Boolean
        Dim Target As Uri = Nothing
        If Not Uri.TryCreate(Source, UriKind.Absolute, Target) Then Return False
        If Target.Scheme = Uri.UriSchemeHttps AndAlso Target.Host.Equals("zhushu.homes", StringComparison.OrdinalIgnoreCase) AndAlso
            Target.AbsolutePath.StartsWith("/stock/", StringComparison.OrdinalIgnoreCase) Then Return True
        Dim TestSource As String = Environment.GetEnvironmentVariable("BRCL_STOCK_URL")
        Dim TestUri As Uri = Nothing
        Return Not String.IsNullOrEmpty(TestSource) AndAlso Uri.TryCreate(TestSource, UriKind.Absolute, TestUri) AndAlso
            Target.Scheme = TestUri.Scheme AndAlso Target.Authority = TestUri.Authority
    End Function

    Private Sub PageStock_Loaded(sender As Object, e As RoutedEventArgs) Handles Me.Loaded
        If BrowserActivity Is Nothing Then BrowserActivity = New BrclWebViewActivity(Me, ViewWeb)
        BrowserActivity.Refresh()
        If IsInited Then
            ApplyWebTheme()
            Return
        End If
        IsInited = True
        StartLoad()
    End Sub

    Private Sub StartLoad()
        ActiveNavigationId = 0
        NavigationVersion += 1
        PanCover.Visibility = Visibility.Visible
        SetBrowserVisibility(Visibility.Hidden)
        CardFail.Visibility = Visibility.Collapsed
        CardLoad.Visibility = Visibility.Visible
        LabLoad.Text = "正在加载交易所页面"
        LabLoadSub.Text = "首次打开需要几秒"
        IsLoadFailed = False
        StartTimeout()
        InitWebView()
    End Sub

    Private Async Sub InitWebView()
        If IsInitializing Then Return
        IsInitializing = True
        Try
            If ViewWeb.CoreWebView2 Is Nothing Then
                If Not Await BrclWebViewRuntime.WaitForPageTransitionAsync(Me) Then
                    IsInited = False
                    Return
                End If
                Dim Env As CoreWebView2Environment = Await BrclWebViewRuntime.GetEnvironmentAsync()
                Await ViewWeb.EnsureCoreWebView2Async(Env)
                AddHandler ViewWeb.CoreWebView2.NavigationCompleted, AddressOf OnNavigationCompleted
                AddHandler ViewWeb.CoreWebView2.NavigationStarting, AddressOf OnNavigationStarting
                AddHandler ViewWeb.CoreWebView2.NewWindowRequested, AddressOf OnNewWindowRequested
                AddHandler ViewWeb.CoreWebView2.ProcessFailed, AddressOf OnProcessFailed
                AddHandler ViewWeb.CoreWebView2.WebMessageReceived, AddressOf OnWebMessage
                AddHandler ViewWeb.CoreWebView2.DOMContentLoaded, AddressOf OnDomContentLoaded
                ViewWeb.CoreWebView2.Settings.AreDefaultContextMenusEnabled = False
                ViewWeb.CoreWebView2.Settings.IsStatusBarEnabled = False
                ViewWeb.CoreWebView2.Settings.AreDevToolsEnabled = False
                ViewWeb.CoreWebView2.Settings.AreHostObjectsAllowed = False
                '布局脚本在文档创建时注册，避免先显示桌面站点再跳变。
                Dim Script As String = EmbedScript.Replace("__BRCL_ALLOW_TEST__", If(String.IsNullOrEmpty(Environment.GetEnvironmentVariable("BRCL_STOCK_URL")), "false", "true"))
                Await ViewWeb.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(Script)
            End If
            If IsLoadFailed Then Return '运行时初始化期间可能已经触发总超时。
            ApplyWebTheme()
            ViewWeb.CoreWebView2.Navigate(StockUrl)
            BrowserActivity?.Refresh()
        Catch ex As WebView2RuntimeNotFoundException
            ShowFail("本机没有安装 WebView2 运行时，无法内嵌显示交易所。" & vbCrLf & "可以点下面的按钮在浏览器中打开，或用 Microsoft 官网的 Evergreen 安装包补装后重试。")
        Catch ex As Exception
            Logger.Warn(ex, "初始化交易所失败")
            ShowFail(ex.Message)
        Finally
            IsInitializing = False
        End Try
    End Sub

    Private Sub OnNavigationStarting(sender As Object, e As CoreWebView2NavigationStartingEventArgs)
        ActiveNavigationId = e.NavigationId
        HasReadyNavigation = False
        NavigationVersion += 1
        IsLoadFailed = False
        PanCover.Visibility = Visibility.Visible
        CardFail.Visibility = Visibility.Collapsed
        CardLoad.Visibility = Visibility.Visible
        SetBrowserVisibility(Visibility.Hidden)
        StartTimeout()
    End Sub

    Private Sub OnNewWindowRequested(sender As Object, e As CoreWebView2NewWindowRequestedEventArgs)
        e.Handled = True
        Dim Target As Uri = Nothing
        If Not Uri.TryCreate(e.Uri, UriKind.Absolute, Target) Then Return
        If Target.Scheme <> Uri.UriSchemeHttps AndAlso Target.Scheme <> Uri.UriSchemeHttp Then Return
        Try
            OpenWebsite(Target.AbsoluteUri)
        Catch ex As Exception
            Logger.Warn(ex, "打开交易所外部链接失败")
            Hint("打开浏览器失败：" & ex.Message)
        End Try
    End Sub

    Private Sub OnNavigationCompleted(sender As Object, e As CoreWebView2NavigationCompletedEventArgs)
        If e.NavigationId <> ActiveNavigationId Then Return
        RunInUi(
        Sub()
            If e.NavigationId <> ActiveNavigationId Then Return
            If e.IsSuccess AndAlso e.HttpStatusCode < 400 Then
                InjectPageTheme()
                SyncSiteTheme()
                VerifyPageContent()
            ElseIf e.HttpStatusCode >= 400 Then
                ShowFail("交易所暂时无法访问（HTTP " & e.HttpStatusCode.ToString() & "），请稍后重试。")
            Else
                ShowFail("加载失败：" & e.WebErrorStatus.ToString() & vbCrLf & "请检查网络后重试。")
            End If
        End Sub)
    End Sub

    Private Sub OnDomContentLoaded(sender As Object, e As CoreWebView2DOMContentLoadedEventArgs)
        If e.NavigationId <> ActiveNavigationId OrElse IsLoadFailed Then Return
        'DOM 就绪即可展示，不再等待图片等资源全部完成后额外延迟 750ms。
        VerifyPageContent()
    End Sub

    Private Sub ShowContent()
        If IsLoadFailed Then Return
        StopTimeout()
        ReadyNavigationId = ActiveNavigationId
        HasReadyNavigation = True
        SetBrowserVisibility(Visibility.Visible)
        PanCover.Visibility = Visibility.Collapsed
        CardLoad.Visibility = Visibility.Collapsed
        CardFail.Visibility = Visibility.Collapsed
    End Sub

    Private Sub OnProcessFailed(sender As Object, e As CoreWebView2ProcessFailedEventArgs)
        RunInUi(Sub() ShowFail("浏览器进程异常：" & e.ProcessFailedKind.ToString() & vbCrLf & "请重试；若仍无法恢复，请重新打开启动器。"))
    End Sub

    ''' <summary>让网页跟随启动器深浅色：优先用系统级 color-scheme，站点自带的深色样式会生效。</summary>
    Public Sub ApplyWebTheme()
        Try
            If ViewWeb.CoreWebView2 Is Nothing Then Return
            Dim IsLight As Boolean = (Settings.Get(Of Integer)("UiThemeMode") = 1)
            ViewWeb.DefaultBackgroundColor = If(IsLight, Global.System.Drawing.Color.FromArgb(&HE7, &HE8, &HE3), Global.System.Drawing.Color.FromArgb(&H11, &H14, &H16))
            ViewWeb.CoreWebView2.Profile.PreferredColorScheme = If(IsLight, CoreWebView2PreferredColorScheme.Light, CoreWebView2PreferredColorScheme.Dark)
            '页面已加载时同步站点自带的明暗切换按钮
            SyncSiteTheme()
        Catch ex As Exception
            Logger.Warn(ex, "设置网页配色失败")
        End Try
    End Sub

    ''' <summary>跟随主题：只切换 color-scheme，让站点用自己的深色样式，避免强行改色导致半深半浅。</summary>
    Private Sub InjectPageTheme()
        Try
            If ViewWeb.CoreWebView2 Is Nothing OrElse Not IsStockSource(ViewWeb.CoreWebView2.Source) Then Return
            Dim IsLight As Boolean = (Settings.Get(Of Integer)("UiThemeMode") = 1)
            ViewWeb.CoreWebView2.ExecuteScriptAsync("document.documentElement.style.colorScheme='" & If(IsLight, "light", "dark") & "'")
        Catch ex As Exception
        End Try
    End Sub

    ''' <summary>
    ''' 构造站点明暗同步脚本：检测页面实际渲染出的明暗（取 body/html 计算样式的背景亮度），
    ''' 若不一致，只寻找明确标记的主题控件并最多点击一次；有限重试覆盖 SPA 延迟渲染。
    ''' 新同步会取消旧同步的计时器，并使旧回调失效。
    ''' </summary>
    Private Function BuildThemeSyncScript() As String
        Dim Want As String = If(Settings.Get(Of Integer)("UiThemeMode") = 1, "true", "false")
        Return "
(function(){
try {
  var previous = window.__brclThemeSync;
  if (previous) clearTimeout(previous.timer);
  var state = { timer: null, tries: 0, clicked: false };
  window.__brclThemeSync = state;
  var wantLight = WANT;
  function isLightNow() {
    var els = [document.body, document.documentElement];
    for (var i = 0; i < els.length; i++) {
      if (!els[i]) continue;
      var match = /rgba?\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)(?:\s*,\s*([\d.]+)\s*)?\)/.exec(getComputedStyle(els[i]).backgroundColor);
      if (!match || (match[4] !== undefined && +match[4] < 0.2)) continue;
      return 0.299 * +match[1] + 0.587 * +match[2] + 0.114 * +match[3] > 140;
    }
    return null;
  }
  function report(status) {
    try { chrome.webview.postMessage('brcl-theme:' + status); } catch (e) {}
  }
  function attempt() {
    if (window.__brclThemeSync !== state) return;
    state.tries++;
    var current = isLightNow();
    if (current === wantLight) { report('synced'); return; }
    if (current !== null && !state.clicked) {
      // Only explicit theme controls are eligible; never click arbitrary page content.
      var candidates = document.querySelectorAll('#themeToggle,#theme-toggle,[data-theme-toggle],button[aria-label*=主题],button[aria-label*=theme],button[title*=主题]');
      for (var i = 0; i < candidates.length; i++) {
        var button = candidates[i], rect = button.getBoundingClientRect();
        if (button.disabled || rect.width < 4 || rect.height < 4) continue;
        state.clicked = true;
        button.click();
        break;
      }
    }
    if (state.tries < 10) state.timer = setTimeout(attempt, 400);
    else report('unavailable');
  }
  state.timer = setTimeout(attempt, 200);
  return 'scheduled';
} catch (e) { return 'error'; }
})();".Replace("WANT", Want)
    End Function

    ''' <summary>
    ''' 同步站点自身的明暗切换：页面实际渲染色与启动器主题不一致时，
    ''' 自动点一下站点自带的切换按钮。用户之后在站点里手动切换不会被抢回。
    ''' 宿主串行提交脚本，页面内的版本状态负责取消旧回调，最终跟随最新主题。
    ''' </summary>
    Public Async Sub SyncSiteTheme()
        If IsSyncingTheme Then
            SyncPending = True
            Return
        End If
        IsSyncingTheme = True
        Try
            If ViewWeb.CoreWebView2 Is Nothing Then Return
            Dim Src As String = ""
            Try
                Src = ViewWeb.CoreWebView2.Source
            Catch ex As Exception
            End Try
            If Not IsStockSource(Src) Then Return
            Await ViewWeb.CoreWebView2.ExecuteScriptAsync(BuildThemeSyncScript())
        Catch ex As Exception
        Finally
            IsSyncingTheme = False
            If SyncPending Then
                SyncPending = False
                SyncSiteTheme()
            End If
        End Try
    End Sub

    ''' <summary>接收页面内脚本回传的结果（主题同步状态），只用于诊断日志。</summary>
    Private Sub OnWebMessage(sender As Object, e As CoreWebView2WebMessageReceivedEventArgs)
        Try
            If Not IsStockSource(e.Source) Then Return
            Dim Msg As String = e.TryGetWebMessageAsString()
            Select Case Msg
                Case "brcl-theme:synced"
                    Logger.Info("交易所明暗同步：synced")
                Case "brcl-theme:unavailable"
                    Logger.Info("交易所明暗同步：unavailable")
            End Select
        Catch ex As Exception
        End Try
    End Sub

    ''' <summary>15 秒未完成导航就降级为失败卡，避免一直停在加载态。</summary>
    Private Sub StartTimeout()
        StopTimeout()
        TimeoutTimer = New Threading.DispatcherTimer With {.Interval = TimeSpan.FromSeconds(15)}
        AddHandler TimeoutTimer.Tick,
            Sub()
                StopTimeout()
                If CardLoad.Visibility = Visibility.Visible Then
                    ShowFail("加载超时：15 秒内没有收到页面响应。" & vbCrLf & "可能是网络波动或站点暂时不可用，可以重试，或在浏览器中打开。")
                End If
            End Sub
        TimeoutTimer.Start()
    End Sub

    ''' <summary>立即检查正文；仅空页面进行有限重试，不给正常页面增加固定等待。</summary>
    Private Async Sub VerifyPageContent()
        Dim Version As Integer = NavigationVersion
        If HasReadyNavigation AndAlso ReadyNavigationId = ActiveNavigationId Then Return
        If ContentCheckVersion = Version Then Return
        ContentCheckVersion = Version
        Try
            'SPA 可能在导航完成后才渲染；在总超时范围内重试，旧导航不得改写新导航状态。
            For Attempt As Integer = 1 To 8
                If Attempt > 1 Then Await Task.Delay(350)
                If Version <> NavigationVersion OrElse IsLoadFailed OrElse ViewWeb.CoreWebView2 Is Nothing Then Return
                Dim Raw As String = Await ViewWeb.CoreWebView2.ExecuteScriptAsync(
                    "(document.body ? document.body.innerText.trim().length : 0)")
                If Version <> NavigationVersion OrElse IsLoadFailed Then Return
                Dim Length As Integer
                If Integer.TryParse(Raw, Length) AndAlso Length >= 20 Then
                    ShowContent()
                    Return
                End If
            Next
            'DOMContentLoaded 不表示 SPA 已完成异步渲染，最终失败由总超时统一判定。
        Catch ex As Exception
            If Version <> NavigationVersion OrElse IsLoadFailed Then Return
            Logger.Warn(ex, "检查交易所页面内容失败")
            ShowFail("无法确认页面是否正常加载，请重试。")
        Finally
            If ContentCheckVersion = Version Then ContentCheckVersion = -1
        End Try
    End Sub

    Private Sub StopTimeout()
        Try
            TimeoutTimer?.Stop()
            TimeoutTimer = Nothing
        Catch ex As Exception
        End Try
    End Sub

    Private Sub ShowFail(Message As String)
        StopTimeout()
        ActiveNavigationId = 0
        NavigationVersion += 1
        Try
            ViewWeb.CoreWebView2?.Stop()
        Catch
        End Try
        PanCover.Visibility = Visibility.Visible
        SetBrowserVisibility(Visibility.Hidden)
        IsLoadFailed = True
        LabFail.Text = Message
        CardLoad.Visibility = Visibility.Collapsed
        CardFail.Visibility = Visibility.Visible
    End Sub

    Public Sub BtnRetry_Click() Handles BtnRetry.Click
        StartLoad()
    End Sub

    ''' <summary>
    ''' F5 刷新（实现 IRefreshable）。
    ''' </summary>
    Public Sub RefreshPage() Implements IRefreshable.Refresh
        ReloadPage()
    End Sub

    Private Sub ReloadPage()
        Try
            If ViewWeb.CoreWebView2 Is Nothing Then
                StartLoad()
            Else
                ViewWeb.CoreWebView2.Reload()
            End If
        Catch ex As Exception
            Hint("刷新失败：" & ex.Message)
        End Try
    End Sub

    Public Sub BtnBrowser_Click() Handles BtnBrowser.Click
        Try
            OpenWebsite(StockUrl)
        Catch ex As Exception
            Hint("打开浏览器失败：" & ex.Message)
        End Try
    End Sub

End Class
