Imports System.IO
Imports System.Security.Cryptography
Imports Microsoft.Web.WebView2.Core

'托管 SDK 从 EXE 资源加载；原生 Loader 放入版本化的 BRCL 子目录。
'不会下载或静默安装 WebView2 Runtime；运行时缺失仍由页面给出提示。
Friend Module BrclWebViewRuntime
    Private ReadOnly Prepared As New Lazy(Of Boolean)(AddressOf PrepareLoader, Threading.LazyThreadSafetyMode.ExecutionAndPublication)
    Private SharedEnvironment As Task(Of CoreWebView2Environment)
    Private ReadOnly EnvironmentLock As New Object

    Public Sub EnsureLoader()
        Dim Ready = Prepared.Value
    End Sub

    Public Function GetEnvironmentAsync() As Task(Of CoreWebView2Environment)
        SyncLock EnvironmentLock
            If SharedEnvironment Is Nothing OrElse SharedEnvironment.IsFaulted OrElse SharedEnvironment.IsCanceled Then
                EnsureLoader()
                'Preserve the existing exchange profile; the map uses a separate
                'profile in this environment and shares its browser/GPU processes.
                Dim Folder = Path.Combine(Paths.Base, "BRCL", "WebView2")
                Directory.CreateDirectory(Folder)
                SharedEnvironment = CoreWebView2Environment.CreateAsync(Nothing, Folder)
            End If
            Return SharedEnvironment
        End SyncLock
    End Function

    Public Async Function WaitForPageTransitionAsync(Page As MyPageRight) As Task(Of Boolean)
        'WebView2's first COM/controller initialization has a synchronous prefix.
        'Present the native loading card and finish the transition before it runs.
        Dim Clock = Diagnostics.Stopwatch.StartNew()
        Do While Page.IsLoaded
            If Page.PageState = MyPageRight.PageStates.PageExit OrElse Page.PageState = MyPageRight.PageStates.ContentExit Then Return False
            Dim Owner = Window.GetWindow(Page)
            If Page.IsVisible AndAlso (Owner Is Nothing OrElse Owner.WindowState <> WindowState.Minimized) AndAlso
                (Page.PageState = MyPageRight.PageStates.ContentStay OrElse Clock.ElapsedMilliseconds >= 750) Then Return True
            Await Task.Delay(If(Owner IsNot Nothing AndAlso Owner.WindowState = WindowState.Minimized, 500, 25))
        Loop
        Return False
    End Function

    Private Function PrepareLoader() As Boolean
        Dim Architecture As String = If(Environment.Is64BitProcess, "x64", "x86")
        Dim ResourceKey As String = If(Environment.Is64BitProcess, "WebView2Loader", "WebView2Loader.x86")
        Dim Bytes As Byte() = Application.ReadPackedBytes(ResourceKey)
        If Bytes Is Nothing OrElse Bytes.Length = 0 Then Throw New FileNotFoundException("启动器缺少内嵌浏览器加载组件")
        Dim Hash As String
        Using Sha = SHA256.Create()
            Hash = BitConverter.ToString(Sha.ComputeHash(Bytes)).Replace("-", "").Substring(0, 16)
        End Using
        Dim Folder = Path.Combine(Paths.Base, "BRCL", "Runtime", "WebView2", Architecture & "-" & Hash)
        Dim LoaderPath = Path.Combine(Folder, "WebView2Loader.dll")
        Directory.CreateDirectory(Folder)
        If Not File.Exists(LoaderPath) OrElse Not File.ReadAllBytes(LoaderPath).SequenceEqual(Bytes) Then
            File.WriteAllBytes(LoaderPath, Bytes)
        End If
        CoreWebView2Environment.SetLoaderDllFolderPath(Folder)
        Return True
    End Function
End Module

'Keep one browser instance for fast navigation, but stop its renderer while it
'is off-screen. Both tab changes and minimizing/hiding the window use this path.
Friend NotInheritable Class BrclWebViewActivity
    Private ReadOnly Host As FrameworkElement
    Private ReadOnly View As Microsoft.Web.WebView2.Wpf.WebView2
    Private ReadOnly SuspendTimer As New Threading.DispatcherTimer(Threading.DispatcherPriority.Background) With {.Interval = TimeSpan.FromSeconds(2)}
    Private Owner As Window
    Private HiddenForWindow As Boolean
    Private Refreshing As Boolean
    Private Suspending As Boolean

    Public Sub New(Host As FrameworkElement, View As Microsoft.Web.WebView2.Wpf.WebView2)
        Me.Host = Host
        Me.View = View
        AddHandler Host.Loaded, AddressOf HostChanged
        AddHandler Host.Unloaded, AddressOf HostChanged
        AddHandler Host.IsVisibleChanged, AddressOf VisibilityChanged
        AddHandler View.IsVisibleChanged, AddressOf VisibilityChanged
        AddHandler SuspendTimer.Tick, AddressOf SuspendBrowser
        Refresh()
    End Sub

    Private ReadOnly Property Active As Boolean
        Get
            Return Host.IsLoaded AndAlso Host.IsVisible AndAlso Owner IsNot Nothing AndAlso Owner.IsVisible AndAlso Owner.WindowState <> WindowState.Minimized
        End Get
    End Property

    Private Sub HostChanged(sender As Object, e As RoutedEventArgs)
        Refresh()
    End Sub

    Private Sub VisibilityChanged(sender As Object, e As DependencyPropertyChangedEventArgs)
        Refresh()
    End Sub

    Private Sub WindowChanged(sender As Object, e As EventArgs)
        Refresh()
    End Sub

    Public Sub SetVisibility(Value As Visibility)
        'A loading/error state supersedes the visibility saved on minimization.
        HiddenForWindow = False
        View.Visibility = Value
        Refresh()
    End Sub

    Public Sub Refresh()
        If Refreshing Then Return
        Refreshing = True
        Try
            Dim CurrentOwner = If(Host.IsLoaded, Window.GetWindow(Host), Nothing)
            If CurrentOwner IsNot Owner Then
                If Owner IsNot Nothing Then
                    RemoveHandler Owner.StateChanged, AddressOf WindowChanged
                    RemoveHandler Owner.IsVisibleChanged, AddressOf VisibilityChanged
                End If
                Owner = CurrentOwner
                If Owner IsNot Nothing Then
                    AddHandler Owner.StateChanged, AddressOf WindowChanged
                    AddHandler Owner.IsVisibleChanged, AddressOf VisibilityChanged
                End If
            End If
            SuspendTimer.Stop()
            If Active Then
                If HiddenForWindow Then
                    HiddenForWindow = False
                    If View.Visibility = Visibility.Hidden Then View.Visibility = Visibility.Visible
                End If
                If View.CoreWebView2 IsNot Nothing Then
                    If View.CoreWebView2.IsSuspended Then View.CoreWebView2.Resume()
                    SetMemoryTarget(CoreWebView2MemoryUsageTargetLevel.Normal)
                End If
            Else
                'TrySuspend requires an invisible controller. WPF does not always
                'update it just because the top-level window was minimized.
                If Host.IsLoaded AndAlso View.Visibility = Visibility.Visible Then
                    HiddenForWindow = True
                    View.Visibility = Visibility.Hidden
                End If
                If View.CoreWebView2 IsNot Nothing Then
                    SetMemoryTarget(CoreWebView2MemoryUsageTargetLevel.Low)
                    SuspendTimer.Start()
                End If
            End If
        Catch ex As Exception
            Logger.Warn(ex, "更新内嵌网页活动状态失败")
        Finally
            Refreshing = False
        End Try
    End Sub

    Private Sub SetMemoryTarget(Level As CoreWebView2MemoryUsageTargetLevel)
        Try
            If View.CoreWebView2.MemoryUsageTargetLevel <> Level Then View.CoreWebView2.MemoryUsageTargetLevel = Level
        Catch ex As NotImplementedException
            'Older Evergreen runtimes still support the suspend/resume path.
        Catch ex As Runtime.InteropServices.COMException
            'The runtime may be shutting down or lack this optional interface.
        End Try
    End Sub

    Private Async Sub SuspendBrowser(sender As Object, e As EventArgs)
        SuspendTimer.Stop()
        If Active OrElse Suspending OrElse View.CoreWebView2 Is Nothing Then Return
        Suspending = True
        Try
            Await View.CoreWebView2.ExecuteScriptAsync("document.querySelectorAll('audio,video').forEach(function(m){try{m.pause()}catch(e){}})")
            If Active Then Return
            Dim Suspended = Await View.CoreWebView2.TrySuspendAsync()
            If Active Then
                View.CoreWebView2.Resume() 'A rapid return may race the async suspend.
            ElseIf Suspended Then
                Logger.Info("内嵌网页已进入后台休眠")
            End If
        Catch ex As Exception
            Logger.Warn(ex, "挂起内嵌网页失败")
        Finally
            Suspending = False
        End Try
    End Sub
End Class
