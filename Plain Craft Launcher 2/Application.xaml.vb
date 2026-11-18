Imports System.Reflection
Imports System.Windows.Threading

Public Class Application

    '开始
    Private Sub Application_Startup(sender As Object, e As StartupEventArgs) Handles Me.Startup
        Try
            '核心初始化
            MeloongCore.Main.Init("BRCL")
            MeloongCore.Wpf.Main.Init()
            Logger.Instance = New PclLogger With {.logFolder = Paths.BaseThenName, .MinLevel = If(ModeDebug, LogLevel.Trace, LogLevel.Info)}
            If Not HasValidStartupArguments(e.Args) Then
                Logger.Warn("启动参数不完整或格式无效：" & e.Args(0))
                Environment.Exit(ProcessReturnValues.Fail)
                Return
            End If
            If Not ConfigureBundledNetworkSettings() Then Logger.Warn("未能应用内置网络兼容设置")
            '提升主线程优先级
            Thread.CurrentThread.Priority = ThreadPriority.Highest
            '执行开发版测试
            If BuildType = BuildTypes.Debug Then
                Try
                    ModDevelop.Start()
                Catch ex As Exception
                    Logger.Error(ex, "开发者模式测试出错", LogBehavior.Alert)
                End Try
            End If
            '检查 .NET Framework 版本
            Try
                If CInt(RegistryUtils.TryRead("HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full", "Release", 999999)) < 528040 Then OldEnvironmentAssert()
            Catch ex As Exception
                Logger.Warn(ex, "检查 .NET Framework 版本失败")
            End Try
            '修复 WPF 字体加载（#3467）
            Try
                Dim _unused As New FormattedText("", Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Fonts.SystemTypefaces.First, 96, New MyColor, DPI)
            Catch ex As UriFormatException
                Environment.SetEnvironmentVariable("windir", Environment.GetEnvironmentVariable("SystemRoot"), EnvironmentVariableTarget.User)
                Dim _unused As New FormattedText("", Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Fonts.SystemTypefaces.First, 96, New MyColor, DPI)
            End Try
            '检查参数调用
            If e.Args.Length > 0 Then
                If e.Args(0) = "--update" Then
                    '自动更新
                    UpdateReplace(e.Args(1), e.Args(2).Trim(""""), e.Args(3).Trim(""""), e.Args(4))
                    Environment.Exit(ProcessReturnValues.TaskDone)
                ElseIf e.Args(0) = "--gpu" Then
                    '调整显卡设置
                    Try
                        SetGPUPreference(e.Args(1).Trim(""""))
                        Environment.Exit(ProcessReturnValues.TaskDone)
                    Catch ex As Exception
                        Environment.Exit(ProcessReturnValues.Fail)
                    End Try
                ElseIf e.Args(0).StartsWithF("--memory") Then
                    '内存优化
                    Dim Ram = My.Computer.Info.AvailablePhysicalMemory
                    Try
                        PageOtherTest.MemoryOptimizeInternal(False)
                    Catch ex As Exception
                        ShowBrclError("内存优化失败", ex)
                        Environment.Exit(-1)
                    End Try
                    If My.Computer.Info.AvailablePhysicalMemory < Ram Then '避免 ULong 相减出现负数
                        Environment.Exit(0)
                    Else
                        Environment.Exit((My.Computer.Info.AvailablePhysicalMemory - Ram) / 1024) '返回清理的内存量（K）
                    End If
                End If
            End If
            '从原 BRCL 数据目录一次性迁移配置（BRCL 更名后首次运行）
            Try
                Dim OldDir As String = Paths.Base & "PCL\"
                Dim NewDir As String = Paths.Base & "BRCL\"
                If Not Directory.Exists(NewDir) AndAlso Directory.Exists(OldDir) Then
                    Directory.CreateDirectory(NewDir)
                    For Each FileName As String In {"Setup.ini", "Logo.png"}
                        If File.Exists(OldDir & FileName) Then File.Copy(OldDir & FileName, NewDir & FileName, False)
                    Next
                End If
            Catch ex As Exception
                '迁移失败不影响启动
            End Try
            '初始化文件结构
            Try
                DirectoryUtils.Create(Paths.Base & "BRCL\Pictures\")
                DirectoryUtils.Create(Paths.Base & "BRCL\Musics\")
                CheckPermissionWithException(Paths.Base & "BRCL\")
            Catch ex As Exception
                ShowBrclError("启动目录访问失败", ex)
                Environment.[Exit](ProcessReturnValues.Cancel)
            End Try
RetryCacheCheck:
            Try
                DirectoryUtils.Create(PathTemp)
                CheckPermissionWithException(PathTemp)
            Catch ex As Exception
                If PathTemp = Path.GetTempPath() & "PCL\" Then
                    MyMsgBox("BRCL 无法访问缓存文件夹，可能导致程序出错或无法正常使用！" & vbCrLf & vbCrLf & "错误原因：" & ex.GetDisplay(True), "缓存文件夹不可用")
                Else
                    MyMsgBox("手动设置的缓存文件夹不可用，BRCL 将使用默认缓存文件夹。" & vbCrLf & vbCrLf & "错误原因：" & ex.GetDisplay(True), "缓存文件夹不可用")
                    Settings.Set("SystemSystemCache", "")
                    PathTemp = Path.GetTempPath() & "PCL\"
                    GoTo RetryCacheCheck
                End If
            End Try
            DirectoryUtils.Create(PathTemp & "Cache\")
            DirectoryUtils.Create(Paths.AppDataThenName)
            '要求单例
            WaitingMutex(e)
            InitializeCrashReports()
            '设置 ToolTipService 默认值
            ToolTipService.InitialShowDelayProperty.OverrideMetadata(GetType(DependencyObject), New FrameworkPropertyMetadata(100))
            '设置网络配置默认值
            ServicePointManager.Expect100Continue = False
            ServicePointManager.DefaultConnectionLimit = 10000
            ServicePointManager.UseNagleAlgorithm = False
            ServicePointManager.EnableDnsRoundRobin = True
            ServicePointManager.ReusePort = True
            If Environment.OSVersion.Version < New Version(6, 2) Then 'Windows 7 回退（#8519）
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Ssl3 Or SecurityProtocolType.Tls Or SecurityProtocolType.Tls11 Or SecurityProtocolType.Tls12
            End If
            '设置初始窗口
            If Settings.Get(Of Boolean)("UiLauncherLogo") Then
                FrmStart = New SplashScreen("Images\LauncherSplash.png")
                FrmStart.Show(False, True)
            End If
            '基础信息
            Logger.Info($"程序版本：{VersionDisplay} ({VersionCode}{If(CommitHash = "", "", $"，#{CommitHash}")})")
            If BuildType = BuildTypes.Snapshot Then
                Logger.Info($"识别码：{Identify}{If(ThemeCheckOne(9), "，已解锁反馈主题", "，未解锁反馈主题")}")
            Else
                Logger.Info($"识别码：{Identify}")
            End If
            Logger.Info($"程序路径：{PathExe}")
            Logger.Info($"系统编码：{Encoding.Default.HeaderName} ({Encoding.Default.CodePage}, GBK={IsGBKEncoding})")
            Logger.Info($"管理员权限：{WindowsUtils.HasAdminRole()}")
            '检测异常环境
            If Paths.Base.Contains(Path.GetTempPath()) OrElse Paths.Base.Contains("AppData\Local\Temp\") Then
                MyMsgBox("请将 BRCL 从压缩包中解压后再使用！" & vbCrLf & "如果不会解压，可以在网上寻找教程。", "需要解压！", "我知道了", IsWarn:=True, ForceWait:=True)
                FormMain.EndProgramForce(ProcessReturnValues.Cancel)
            End If
            If Not Environment.Is64BitOperatingSystem Then
                MyMsgBox("BRCL 和新版 Minecraft 均不再支持 32 位系统，请重装为 64 位系统后再进行游戏！", "环境警告", "我知道了", IsWarn:=True, ForceWait:=True)
                FormMain.EndProgramForce(ProcessReturnValues.Cancel)
            End If
            '计时
            Logger.Info($"第一阶段加载用时：{GetTimeMs() - ApplicationStartTick} ms")
            ApplicationStartTick = GetTimeMs()
            AniControlEnabled += 1
        Catch ex As Exception
            Dim FilePath As String = Nothing
            Try
                FilePath = PathExe
            Catch
            End Try
            ShowBrclError("BRCL 初始化错误", ex, ex.GetDisplay(True) & vbCrLf & "启动器路径：" & If(String.IsNullOrEmpty(FilePath), "无法获取", FilePath), Fatal:=True)
            FormMain.EndProgramForce(ProcessReturnValues.Exception)
        End Try
    End Sub
    Private Shared Function HasValidStartupArguments(Arguments As String()) As Boolean
        If Arguments Is Nothing OrElse Arguments.Length = 0 Then Return True
        Select Case Arguments(0)
            Case "--update"
                If Arguments.Length < 5 Then Return False
                Dim ProcessId As Integer, Restart As Boolean
                Return Integer.TryParse(Arguments(1), ProcessId) AndAlso ProcessId > 0 AndAlso
                    Not String.IsNullOrWhiteSpace(Arguments(2)?.Trim(""""c)) AndAlso
                    Not String.IsNullOrWhiteSpace(Arguments(3)?.Trim(""""c)) AndAlso
                    Boolean.TryParse(Arguments(4), Restart)
            Case "--gpu"
                Return Arguments.Length >= 2 AndAlso Not String.IsNullOrWhiteSpace(Arguments(1)?.Trim(""""c))
            Case Else
                Return True
        End Select
    End Function
    ''' <summary>
    ''' 要求程序以单例运行。
    ''' 如果已在运行，则将其窗口拖出来并播放提示音，然后结束当前进程。
    ''' </summary>
    Private Shared Sub WaitingMutex(e As StartupEventArgs)
        If BuildType = BuildTypes.Debug Then Return
        If Environment.GetEnvironmentVariable("BRCL_UI_PREVIEW") = "1" Then Return
        Try
            Dim MutexCreatedNew As Boolean
            PclMutex = New Mutex(True, "BRCL_SingletonMutex", MutexCreatedNew)
            If MutexCreatedNew Then Return
            Logger.Warn("已有一个 BRCL 实例正在运行")
            '等待已有的 BRCL 退出
            If e.Args.Length > 0 AndAlso e.Args(0) = "--wait" Then
                Try
                    If PclMutex.WaitOne(10000) Then Return '等待至多 10 秒
                Catch Ignored As AbandonedMutexException
                    Return '已有实例异常退出，继续启动
                End Try
            End If
            '将已有的 BRCL 窗口拖出来
            Dim WindowHwnd As IntPtr = FindWindow(Nothing, "BRCL　")
            If WindowHwnd = IntPtr.Zero Then WindowHwnd = FindWindow(Nothing, "BRCL")
            If WindowHwnd <> IntPtr.Zero Then ShowWindowToTop(WindowHwnd)
            '播放提示音并退出
            Beep()
            Environment.[Exit](ProcessReturnValues.Cancel)
        Catch ex As Exception
            Logger.Warn(ex, "单例检查失败")
        End Try
    End Sub
    Private Shared PclMutex As Mutex

    '结束
    Private Sub Application_SessionEnding(sender As Object, e As SessionEndingCancelEventArgs) Handles Me.SessionEnding
        FrmMain?.EndProgram(False)
    End Sub

    '异常
    Private Sub Application_DispatcherUnhandledException(sender As Object, e As DispatcherUnhandledExceptionEventArgs) Handles Me.DispatcherUnhandledException
        On Error Resume Next
        Logger.Warn(e.Exception, "UI 未处理异常")
        '触发页面的 Dispatcher
        If FrmMain?.PageLeft IsNot Nothing AndAlso TypeOf FrmMain.PageLeft Is IDispatcherUnhandledException Then
            CType(FrmMain.PageLeft, IDispatcherUnhandledException).DispatcherUnhandledException(sender, e)
            If e.Handled Then Return
        End If
        If FrmMain?.PageRight IsNot Nothing AndAlso TypeOf FrmMain.PageRight Is IDispatcherUnhandledException Then
            CType(FrmMain.PageRight, IDispatcherUnhandledException).DispatcherUnhandledException(sender, e)
            If e.Handled Then Return
        End If
        '正常处理
        e.Handled = True
        If IsProgramEnding Then Return
        FeedbackInfo()
        Dim Detail As String = e.Exception.GetDisplay(True)
        If Detail.Contains("System.Windows.Threading.Dispatcher.Invoke") OrElse Detail.Contains("MS.Internal.AppModel.ITaskbarList.HrInit") OrElse Detail.Contains("未能加载文件或程序集") OrElse
           Detail.Contains(".NET Framework") Then ' “自动错误判断” 的结果分析
            OldEnvironmentAssert(e.Exception)
        Else
            Logger.Error(e.Exception, "程序出现未知错误", LogBehavior.AlertThenCrash)
        End If
    End Sub
    Private Shared Sub OldEnvironmentAssert(Optional ex As Exception = Nothing)
        If ex Is Nothing Then
            Logger.Error("BRCL 所需的 .NET Framework 4.8 组件暂不可用。请检查系统组件后重新启动。", LogBehavior.AlertThenCrash)
        Else
            Logger.Error(ex, "BRCL 无法使用当前 .NET Framework 组件。请检查 .NET Framework 4.8 安装状态后重新启动。", LogBehavior.AlertThenCrash)
        End If
    End Sub

    '动态 DLL 加载
    Friend Shared Function ConfigureBundledNetworkSettings() As Boolean
        Try
            'Preserve the legacy App.config HTTP behavior in an EXE-only install.
            'This changes this process's Framework setting, never machine.config.
            Dim SettingsType = GetType(Global.System.Net.HttpWebRequest).Assembly.GetType("System.Net.Configuration.SettingsSectionInternal")
            If SettingsType Is Nothing Then Return False
            Dim SectionProperty = SettingsType.GetProperty("Section", BindingFlags.Static Or BindingFlags.NonPublic)
            Dim HeaderField = SettingsType.GetField("useUnsafeHeaderParsing", BindingFlags.Instance Or BindingFlags.NonPublic)
            If SectionProperty Is Nothing OrElse HeaderField Is Nothing Then Return False
            Dim Section = SectionProperty.GetValue(Nothing, Nothing)
            If Section Is Nothing Then Return False
            HeaderField.SetValue(Section, True)
            Return True
        Catch
            Return False
        End Try
    End Function

    Private Sub New() '这里必须尽早调用，且不能使用任何库，否则加载 MeloongCore 就会导致崩溃
        Static Prefixes As String() = {"NAudio", "Newtonsoft.Json", "Ookii.Dialogs.Wpf", "Imazen.WebP", "CacheCow.Common", "CacheCow.Client.FileStore", "CacheCow.Client", "ThrottleDebounce", "System.Net.Http.Formatting", "Microsoft.Web.WebView2.Core", "Microsoft.Web.WebView2.Wpf", "Microsoft.Win32.Registry", "PCLCS", "MeloongCore.Wpf", "MeloongCore"}
        Static LoadedAssemblies As New ConcurrentDictionary(Of String, Lazy(Of Assembly))(StringComparer.Ordinal) '缓存
        AddHandler AppDomain.CurrentDomain.AssemblyResolve,
        Function(sender As Object, Args As ResolveEventArgs) As Assembly
            Dim requested = New AssemblyName(Args.Name).Name
            Dim Prefix = Prefixes.FirstOrDefault(Function(item) item.Equals(requested, StringComparison.OrdinalIgnoreCase))
            If Prefix Is Nothing Then Return Nothing
            Dim LazyAssembly = LoadedAssemblies.GetOrAdd(Prefix, Function(p) New Lazy(Of Assembly)(
            Function()
                Dim LoadedAssembly = Assembly.Load(ReadPackedBytes(p))
                If p = "Imazen.WebP" Then ExtractLibwebp() 'WebP 特判
                Return LoadedAssembly
            End Function, LazyThreadSafetyMode.ExecutionAndPublication))
            Return LazyAssembly.Value
        End Function
    End Sub
    Friend Shared Function ReadPackedBytes(name As String) As Byte()
        Using packed = GetType(Application).Assembly.GetManifestResourceStream("BRCL.Packed." & name)
            If packed Is Nothing Then Throw New IO.FileNotFoundException(name)
            Using reader As New IO.BinaryReader(packed)
                Dim length = reader.ReadInt32()
                If length <= 0 Then Throw New IO.InvalidDataException(name)
                Dim bytes(length - 1) As Byte
                Using inflater As New IO.Compression.DeflateStream(packed, IO.Compression.CompressionMode.Decompress)
                    Dim offset = 0
                    While offset < length
                        Dim count = inflater.Read(bytes, offset, length - offset)
                        If count = 0 Then Throw New IO.EndOfStreamException(name)
                        offset += count
                    End While
                    If inflater.ReadByte() <> -1 Then Throw New IO.InvalidDataException(name)
                End Using
                Return bytes
            End Using
        End Using
    End Function
    Private Shared Sub ExtractLibwebp() '这个方法会调用 ModBase，进而调用 MeloongCore，所以不能放在 New 里
        SetDllDirectory(PathPure.Value.TrimEnd("\"c))
        Try
            ExtractResources(PathPure.Value & "libwebp.dll", "libwebp64")
        Catch ex As Exception
            Logger.Warn(ex, "写入 libwebp.dll 失败") '防止同时加载多个图片时，同时写入文件导致文件占用，进而导致崩溃
        End Try
    End Sub
    Private Declare Function SetDllDirectory Lib "kernel32" Alias "SetDllDirectoryA" (lpPathName As String) As Boolean

    '控件模板事件
    Private Sub MyIconButton_Click(sender As Object, e As EventArgs)
        Select Case Settings.Get(Of McLoginType)("LoginType")
            Case McLoginType.Ms
                '微软
                Dim MsJson As JObject = Settings.Get(Of String)("LoginMsJson").DeserializeJson()
                MsJson.Remove(sender.Tag)
                Settings.Set("LoginMsJson", MsJson.ToString(Newtonsoft.Json.Formatting.None))
                If FrmLoginMs.ComboAccounts.SelectedItem Is sender.Parent Then FrmLoginMs.ComboAccounts.SelectedIndex = 0
                FrmLoginMs.ComboAccounts.Items.Remove(sender.Parent)
            Case McLoginType.Legacy
                '离线
                Dim Names As New List(Of String)
                Names.AddRange(Settings.Get(Of String)("LoginLegacyName").ToString.Split("¨"))
                Names.Remove(sender.Tag)
                Settings.Set("LoginLegacyName", Names.Join("¨"c))
                FrmLoginLegacy.ComboName.ItemsSource = Names
                FrmLoginLegacy.ComboName.Text = If(Names.Any, Names(0), "")
            Case Else
                '第三方
                Dim Token As String = Settings.Get(Of McLoginType)("LoginType").ToString()
                Dim Dict As New Dictionary(Of String, String)
                Dim Names As New List(Of String)
                Dim Passs As New List(Of String)
                If Not Settings.Get(Of String)("Login" & Token & "Email") = "" Then Names.AddRange(Settings.Get(Of String)("Login" & Token & "Email").Split("¨"))
                If Not Settings.Get(Of String)("Login" & Token & "Pass") = "" Then Passs.AddRange(Settings.Get(Of String)("Login" & Token & "Pass").Split("¨"))
                For i = 0 To Names.Count - 1
                    Dict.Add(Names(i), Passs(i))
                Next
                Dict.Remove(sender.Tag)
                Settings.Set("Login" & Token & "Email", Dict.Keys.Join("¨"c))
                Settings.Set("Login" & Token & "Pass", Dict.Values.Join("¨"c))
                Select Case Token
                    Case "Nide"
                        FrmLoginNide.ComboName.ItemsSource = Dict.Keys
                        FrmLoginNide.ComboName.Text = If(Dict.Keys.Any, Dict.Keys(0), "")
                        FrmLoginNide.TextPass.Password = If(Dict.Values.Any, Dict.Values(0), "")
                    Case "Auth"
                        FrmLoginAuth.ComboName.ItemsSource = Dict.Keys
                        FrmLoginAuth.ComboName.Text = If(Dict.Keys.Any, Dict.Keys(0), "")
                        FrmLoginAuth.TextPass.Password = If(Dict.Values.Any, Dict.Values(0), "")
                End Select
        End Select
    End Sub

End Class
