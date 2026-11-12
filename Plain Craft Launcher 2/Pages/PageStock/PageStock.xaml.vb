Imports Microsoft.Web.WebView2.Core

''' <summary>
''' 交易所页面：内嵌浏览器显示 zhushu.homes/stock。
''' </summary>
Public Class PageStock
    Implements IRefreshable

    Private Const StockUrl As String = "https://zhushu.homes/stock/"
    Private IsInited As Boolean = False
    Private IsLoadFailed As Boolean = False

    Private Sub PageStock_Loaded(sender As Object, e As RoutedEventArgs) Handles Me.Loaded
        If IsInited Then
            '回到页面时恢复被挂起的渲染进程
            Try
                ViewWeb.CoreWebView2?.Resume()
            Catch ex As Exception
            End Try
            ApplyWebTheme()
            Return
        End If
        IsInited = True
        StartLoad()
    End Sub

    Private Sub PageStock_Unloaded(sender As Object, e As RoutedEventArgs) Handles Me.Unloaded
        '离开页面：暂停网页音视频并挂起渲染进程，避免后台占用 CPU/内存
        Try
            ViewWeb.CoreWebView2?.ExecuteScriptAsync("document.querySelectorAll('audio,video').forEach(function(m){try{m.pause()}catch(e){}})")
        Catch ex As Exception
        End Try
        Try
            ViewWeb.CoreWebView2?.TrySuspendAsync()
        Catch ex As Exception
        End Try
    End Sub

    Private Sub StartLoad()
        CardFail.Visibility = Visibility.Collapsed
        CardLoad.Visibility = Visibility.Visible
        LabLoad.Text = "正在加载交易所页面"
        LabLoadSub.Text = "首次打开需要几秒"
        IsLoadFailed = False
        InitWebView()
    End Sub

    Private Async Sub InitWebView()
        Try
            If ViewWeb.CoreWebView2 Is Nothing Then
                Dim DataFolder As String = Paths.Base & "BRCL\WebView2"
                DirectoryUtils.Create(DataFolder)
                Dim Env As CoreWebView2Environment = Await CoreWebView2Environment.CreateAsync(Nothing, DataFolder)
                Await ViewWeb.EnsureCoreWebView2Async(Env)
                AddHandler ViewWeb.CoreWebView2.NavigationCompleted, AddressOf OnNavigationCompleted
                AddHandler ViewWeb.CoreWebView2.ProcessFailed, AddressOf OnProcessFailed
                ViewWeb.CoreWebView2.Settings.AreDefaultContextMenusEnabled = False
                ViewWeb.CoreWebView2.Settings.IsStatusBarEnabled = False
                ViewWeb.CoreWebView2.Settings.AreDevToolsEnabled = False
            End If
            ApplyWebTheme()
            ViewWeb.Source = New Uri(StockUrl)
        Catch ex As WebView2RuntimeNotFoundException
            ShowFail("本机没有安装 WebView2 运行时，无法内嵌显示交易所。" & vbCrLf & "可以点下面的按钮在浏览器中打开，或用 Microsoft 官网的 Evergreen 安装包补装后重试。")
        Catch ex As Exception
            ShowFail(ex.Message)
        End Try
    End Sub

    Private Sub OnNavigationCompleted(sender As Object, e As CoreWebView2NavigationCompletedEventArgs)
        RunInUi(
        Sub()
            If e.IsSuccess Then
                InjectPageTheme()
                CardLoad.Visibility = Visibility.Collapsed
                CardFail.Visibility = Visibility.Collapsed
            Else
                ShowFail("加载失败：" & e.WebErrorStatus.ToString() & vbCrLf & "请检查网络后重试。")
            End If
        End Sub)
    End Sub

    Private Sub OnProcessFailed(sender As Object, e As CoreWebView2ProcessFailedEventArgs)
        RunInUi(Sub() ShowFail("浏览器进程异常：" & e.ProcessFailedKind.ToString() & vbCrLf & "点「重试」可以重新拉起。"))
    End Sub

    ''' <summary>让网页跟随启动器深浅色：优先用系统级 color-scheme，站点自带的深色样式会生效。</summary>
    Private Sub ApplyWebTheme()
        Try
            If ViewWeb.CoreWebView2 Is Nothing Then Return
            Dim IsLight As Boolean = (Settings.Get(Of Integer)("UiThemeMode") = 1)
            ViewWeb.DefaultBackgroundColor = If(IsLight, Global.System.Drawing.Color.FromArgb(&HF4, &HF3, &HF9), Global.System.Drawing.Color.FromArgb(&H13, &H11, &H20))
            ViewWeb.CoreWebView2.Profile.PreferredColorScheme = If(IsLight, CoreWebView2PreferredColorScheme.Light, CoreWebView2PreferredColorScheme.Dark)
        Catch ex As Exception
            Logger.Warn(ex, "设置网页配色失败")
        End Try
    End Sub

    ''' <summary>跟随主题：只切换 color-scheme，让站点用自己的深色样式，避免强行改色导致半深半浅。</summary>
    Private Sub InjectPageTheme()
        Try
            If ViewWeb.CoreWebView2 Is Nothing Then Return
            Dim IsLight As Boolean = (Settings.Get(Of Integer)("UiThemeMode") = 1)
            ViewWeb.CoreWebView2.ExecuteScriptAsync("document.documentElement.style.colorScheme='" & If(IsLight, "light", "dark") & "'")
        Catch ex As Exception
        End Try
    End Sub

    Private Sub ShowFail(Message As String)
        IsLoadFailed = True
        LabFail.Text = Message
        CardLoad.Visibility = Visibility.Collapsed
        CardFail.Visibility = Visibility.Visible
    End Sub

    Public Sub BtnRetry_Click() Handles BtnRetry.Click
        IsLoadFailed = False
        If ViewWeb.CoreWebView2 IsNot Nothing Then
            CardFail.Visibility = Visibility.Collapsed
            CardLoad.Visibility = Visibility.Visible
            Try
                ViewWeb.CoreWebView2.Navigate(StockUrl)
            Catch ex As Exception
                StartLoad()
            End Try
        Else
            StartLoad()
        End If
    End Sub

    ''' <summary>
    ''' F5 刷新（实现 IRefreshable）。
    ''' </summary>
    Public Sub RefreshPage() Implements IRefreshable.Refresh
        BtnReload_Click()
    End Sub

    Public Sub BtnReload_Click() Handles BtnReload.Click
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

    Public Sub BtnHome_Click() Handles BtnHome.Click
        Try
            If ViewWeb.CoreWebView2 Is Nothing Then
                StartLoad()
            Else
                ViewWeb.CoreWebView2.Navigate(StockUrl)
            End If
        Catch ex As Exception
            Hint("返回首页失败：" & ex.Message)
        End Try
    End Sub

    Public Sub BtnBrowser_Click() Handles BtnBrowser.Click, BtnBrowserTop.Click
        Try
            OpenWebsite(StockUrl)
        Catch ex As Exception
            Hint("打开浏览器失败：" & ex.Message)
        End Try
    End Sub

End Class
