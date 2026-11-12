Imports Microsoft.Web.WebView2.Core

''' <summary>
''' 交易所页面：内嵌浏览器显示 zhushu.homes/BRS。
''' </summary>
Public Class PageStock

    Private Const BrsUrl As String = "https://zhushu.homes/stock/"
    Private IsInited As Boolean = False
    Private IsLoadFailed As Boolean = False

    Private Sub PageStock_Loaded(sender As Object, e As RoutedEventArgs) Handles Me.Loaded
        If IsInited Then Return
        IsInited = True
        StartLoad()
    End Sub

    Private Sub PageStock_Unloaded(sender As Object, e As RoutedEventArgs) Handles Me.Unloaded
        '离开页面后停止播放网页音频，避免后台占用
        Try
            ViewWeb.CoreWebView2?.ExecuteScriptAsync("document.querySelectorAll('audio,video').forEach(function(m){try{m.pause()}catch(e){}})")
        Catch ex As Exception
        End Try
    End Sub

    Private Sub StartLoad()
        CardFail.Visibility = Visibility.Collapsed
        CardLoad.Visibility = Visibility.Visible
        LabLoad.Text = "正在加载 交易所页面"
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
            ViewWeb.Source = New Uri(BrsUrl)
        Catch ex As Exception
            ShowFail(ex.Message)
        End Try
    End Sub

    Private Sub OnNavigationCompleted(sender As Object, e As CoreWebView2NavigationCompletedEventArgs)
        RunInUi(
        Sub()
            If e.IsSuccess Then
                CardLoad.Visibility = Visibility.Collapsed
                CardFail.Visibility = Visibility.Collapsed
            Else
                ShowFail("错误代码：" & e.WebErrorStatus.ToString())
            End If
        End Sub)
    End Sub

    Private Sub OnProcessFailed(sender As Object, e As CoreWebView2ProcessFailedEventArgs)
        RunInUi(Sub() ShowFail("浏览器进程异常：" & e.ProcessFailedKind.ToString()))
    End Sub

    Private Sub ShowFail(Message As String)
        If IsLoadFailed Then Return
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
            ViewWeb.Source = New Uri(BrsUrl)
        Else
            StartLoad()
        End If
    End Sub

    Public Sub BtnBrowser_Click() Handles BtnBrowser.Click
        Try
            OpenWebsite(BrsUrl)
        Catch ex As Exception
            Hint("打开浏览器失败：" & ex.Message)
        End Try
    End Sub

End Class
