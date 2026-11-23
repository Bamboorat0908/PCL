Imports System.ComponentModel
Imports System.Windows.Interop

Public Class FormMain

#Region "基础"

    '更新日志
    Private Sub ShowUpdateLog(LastVersion As Integer)
        Dim FeatureCount As Integer = 0, BugCount As Integer = 0
        Dim FeatureList As New List(Of KeyValuePair(Of Integer, String))
        '统计更新日志条目
        If BuildType = BuildTypes.Release Then
            If LastVersion < 408 Then 'Release 2.13.1.1
                FeatureList.Add(New KeyValuePair(Of Integer, String)(2, "优化：版本设置中，添加打开截图文件夹的快捷方式"))
                FeatureList.Add(New KeyValuePair(Of Integer, String)(2, "优化：导出整合包时，可以勾选导出光影设置文件"))
                FeatureCount += 23
                BugCount += 28
            End If
            If LastVersion < 406 Then 'Release 2.13.0.1
                FeatureList.Add(New KeyValuePair(Of Integer, String)(4, "新增：重做 Java 管理与相关设置，允许调整 Java 优先级、指定 Java 版本范围等"))
                FeatureList.Add(New KeyValuePair(Of Integer, String)(3, "优化：导出整合包时允许自动导出版本文件夹中的 Java"))
                FeatureList.Add(New KeyValuePair(Of Integer, String)(3, "优化：重做弹出提示的样式以及动画，以更符合现代 UI 审美"))
                FeatureList.Add(New KeyValuePair(Of Integer, String)(1, "修复：可能无法安装 OptiFine 26.1.2"))
                FeatureCount += 35
                BugCount += 17
            End If
            If LastVersion < 404 Then 'Release 2.12.8.2
                FeatureList.Add(New KeyValuePair(Of Integer, String)(1, "修复：Windows 7 无法正常联网"))
                FeatureCount += 1
                BugCount += 2
            End If
            If LastVersion < 402 Then 'Release 2.12.8.1
                FeatureList.Add(New KeyValuePair(Of Integer, String)(2, "优化：升级框架到 .NET Framework 4.8"))
                FeatureCount += 35
                BugCount += 21
            End If
            If LastVersion < 398 Then 'Release 2.12.7.3
                FeatureList.Add(New KeyValuePair(Of Integer, String)(2, "优化：下载游戏、整合包的稳定性"))
                FeatureCount += 1
                BugCount += 5
            End If
            If LastVersion < 391 Then 'Release 2.12.6.2
                FeatureList.Add(New KeyValuePair(Of Integer, String)(1, "修复：会为部分 Forge / NeoForge 选择不支持的 Java"))
                BugCount += 4
            End If
            If LastVersion < 389 Then 'Release 2.12.6.1
                If LastVersion = 387 Then FeatureList.Add(New KeyValuePair(Of Integer, String)(2, "修复：无法使用 Java 25+ 启动 Forge"))
                FeatureCount += 4
                BugCount += 3
            End If
            If LastVersion < 387 Then 'Release 2.12.6
                FeatureList.Add(New KeyValuePair(Of Integer, String)(3, "优化：降低 Minecraft 的内存占用"))
                FeatureList.Add(New KeyValuePair(Of Integer, String)(2, "修复：Minecraft 使用 Java 25+ 时的一个性能问题"))
                FeatureList.Add(New KeyValuePair(Of Integer, String)(2, "修复：无法下载 NeoForge 26.1"))
                FeatureList.Add(New KeyValuePair(Of Integer, String)(2, "修复：部分关键词搜不到资源，特别是中文 Mod 搜索经常没有结果"))
                FeatureList.Add(New KeyValuePair(Of Integer, String)(1, "修复：老版本 Windows 无法启动游戏"))
                FeatureList.Add(New KeyValuePair(Of Integer, String)(1, "修复：连接 Mojang 的服务可能失败，提示错误码 421"))
                FeatureList.Add(New KeyValuePair(Of Integer, String)(1, "修复：无法访问爱发电"))
                FeatureCount += 24
                BugCount += 19
            End If
            If LastVersion < 383 Then 'Release 2.12.3
                FeatureList.Add(New KeyValuePair(Of Integer, String)(2, "修复：部分关键词搜不到资源，特别是中文 Mod 搜索经常没有结果"))
                FeatureList.Add(New KeyValuePair(Of Integer, String)(2, "修复：下载可能完全卡住，或是下载进度反复回退"))
                FeatureList.Add(New KeyValuePair(Of Integer, String)(1, "新增：内存管理设置，可以选择 G1GC、ZGC 或分代 ZGC"))
                FeatureList.Add(New KeyValuePair(Of Integer, String)(1, "优化：来自 Modrinth 的文件下载速度"))
                FeatureCount += 15
                BugCount += 16
            End If
            If LastVersion < 381 Then 'Release 2.12.2
                FeatureCount += 3
                BugCount += 13
            End If
            If LastVersion < 379 Then 'Release 2.12.1
                If LastVersion >= 376 Then
                    FeatureList.Add(New KeyValuePair(Of Integer, String)(5, "删除：暂时隐藏联机入口……不过只是暂时关闭，它还会回来的！"))
                End If
                FeatureList.Add(New KeyValuePair(Of Integer, String)(3, "新增：适配新的 Minecraft 版本号系统与 Unobfuscated 版本"))
                FeatureList.Add(New KeyValuePair(Of Integer, String)(2, "优化：巨幅优化各个下载页面和 Mod 管理页面的性能"))
                FeatureList.Add(New KeyValuePair(Of Integer, String)(1, "优化：更换披风时会显示当前使用的披风"))
                FeatureCount += 33
                BugCount += 29
            End If
            If LastVersion < 376 Then 'Release 2.11.2
                'FeatureList.Add(New KeyValuePair(Of Integer, String)(5, "新增：联机功能！"))
                FeatureCount += 32
                BugCount += 21
            End If
        Else
            '5：          FEAT+
            '4：     IMP+ FEAT*
            '3：BUG+ IMP* FEAT-
            '2：BUG* IMP-
            '1：BUG-
            If LastVersion < 410 Then 'Snapshot 2.13.1.1
                If LastVersion = 409 Then FeatureList.Add(New KeyValuePair(Of Integer, String)(1, "修复：更新 Mod 时可能更新成另一个 Mod 加载器的版本"))
                FeatureCount += 2
                BugCount += 6
            End If
            If LastVersion < 409 Then 'Snapshot 2.13.1.0
                FeatureList.Add(New KeyValuePair(Of Integer, String)(2, "优化：版本设置中，添加打开截图文件夹的快捷方式"))
                FeatureList.Add(New KeyValuePair(Of Integer, String)(2, "优化：导出整合包时，可以勾选导出光影设置文件"))
                FeatureCount += 21
                BugCount += 22
            End If
            If LastVersion < 407 Then 'Snapshot 2.13.0.1
                If LastVersion = 405 Then
                    FeatureList.Add(New KeyValuePair(Of Integer, String)(1, "修复：使用部分主页预设时崩溃"))
                    FeatureList.Add(New KeyValuePair(Of Integer, String)(1, "修复：无法删除部分文件夹"))
                    FeatureList.Add(New KeyValuePair(Of Integer, String)(1, "修复：版本设置中指定的 Java 无法被保存"))
                    FeatureCount += 1
                    BugCount += 3
                End If
                FeatureCount += 2
            End If
            If LastVersion < 405 Then 'Snapshot 2.13.0.0
                FeatureList.Add(New KeyValuePair(Of Integer, String)(4, "新增：重做 Java 管理与相关设置，允许调整 Java 优先级、指定 Java 版本范围等"))
                FeatureList.Add(New KeyValuePair(Of Integer, String)(3, "优化：导出整合包时允许自动导出版本文件夹中的 Java"))
                FeatureList.Add(New KeyValuePair(Of Integer, String)(3, "优化：重做弹出提示的样式以及动画，以更符合现代 UI 审美"))
                FeatureList.Add(New KeyValuePair(Of Integer, String)(1, "修复：可能无法安装 OptiFine 26.1.2"))
                FeatureCount += 35
                BugCount += 17
            End If
            If LastVersion < 403 Then 'Snapshot 2.12.8.2
                FeatureList.Add(New KeyValuePair(Of Integer, String)(1, "修复：Windows 7 无法正常联网"))
                FeatureCount += 1
                BugCount += 2
            End If
            If LastVersion < 401 Then 'Snapshot 2.12.8.1
                FeatureList.Add(New KeyValuePair(Of Integer, String)(1, "修复：可能无法更换披风"))
                FeatureCount += 8
                BugCount += 6
            End If
            If LastVersion < 399 Then 'Snapshot 2.12.8.0
                FeatureList.Add(New KeyValuePair(Of Integer, String)(2, "优化：升级框架到 .NET Framework 4.8"))
                FeatureCount += 28
                BugCount += 19
            End If
            If LastVersion < 397 Then 'Snapshot 2.12.7.3
                If LastVersion >= 393 AndAlso LastVersion < 397 Then FeatureList.Add(New KeyValuePair(Of Integer, String)(1, "修复：无法使用自定义隐藏主题"))
            End If
            If LastVersion < 395 Then 'Snapshot 2.12.7.2
                If LastVersion = 393 Then FeatureList.Add(New KeyValuePair(Of Integer, String)(2, "修复：部分个性化功能失效"))
            End If
            If LastVersion < 393 Then 'Snapshot 2.12.7.1
                FeatureList.Add(New KeyValuePair(Of Integer, String)(2, "优化：允许永久关闭启动页面的快照版提示"))
                FeatureList.Add(New KeyValuePair(Of Integer, String)(2, "优化：下载游戏、整合包的稳定性"))
                FeatureCount += 1
                BugCount += 5
            End If
            If LastVersion < 390 Then 'Snapshot 2.12.6.2
                FeatureList.Add(New KeyValuePair(Of Integer, String)(1, "修复：会为部分 Forge / NeoForge 选择不支持的 Java"))
                BugCount += 4
            End If
            If LastVersion < 388 Then 'Snapshot 2.12.6.1
                If LastVersion = 386 Then FeatureList.Add(New KeyValuePair(Of Integer, String)(2, "修复：无法使用 Java 25+ 启动 Forge"))
                FeatureCount += 4
                BugCount += 3
            End If
            If LastVersion < 386 Then 'Snapshot 2.12.6
                FeatureList.Add(New KeyValuePair(Of Integer, String)(2, "修复：Minecraft 使用 Java 25+ 时的一个性能问题"))
                If LastVersion = 384 OrElse LastVersion = 385 Then FeatureList.Add(New KeyValuePair(Of Integer, String)(2, "修复：无法正常重命名游戏版本"))
                If LastVersion = 385 Then FeatureList.Add(New KeyValuePair(Of Integer, String)(1, "修复：无法显示 Modrinth 整合包的版本列表"))
                BugCount += 1
            End If
            If LastVersion < 385 Then 'Snapshot 2.12.5
                FeatureList.Add(New KeyValuePair(Of Integer, String)(2, "修复：无法下载 NeoForge 26.1"))
                If LastVersion = 384 Then
                    FeatureList.Add(New KeyValuePair(Of Integer, String)(2, "修复：下载可能失败，提示任务管理刷新线程出错"))
                    FeatureList.Add(New KeyValuePair(Of Integer, String)(2, "修复：无法保存选择的 Minecraft 文件夹"))
                    FeatureList.Add(New KeyValuePair(Of Integer, String)(1, "修复：使用中文搜索 Mod 时，部分结果会忽略筛选条件"))
                End If
                FeatureCount += 3
                BugCount += 6
            End If
            If LastVersion < 384 Then 'Snapshot 2.12.4
                FeatureList.Add(New KeyValuePair(Of Integer, String)(3, "优化：降低 Minecraft 的内存占用"))
                FeatureList.Add(New KeyValuePair(Of Integer, String)(2, "修复：部分关键词搜不到资源，特别是中文 Mod 搜索经常没有结果"))
                FeatureList.Add(New KeyValuePair(Of Integer, String)(1, "修复：老版本 Windows 无法启动游戏"))
                FeatureList.Add(New KeyValuePair(Of Integer, String)(1, "修复：连接 Mojang 的服务可能失败，提示错误码 421"))
                FeatureList.Add(New KeyValuePair(Of Integer, String)(1, "修复：无法访问爱发电"))
                FeatureCount += 21
                BugCount += 14
            End If
            If LastVersion < 382 Then 'Snapshot 2.12.3
                FeatureList.Add(New KeyValuePair(Of Integer, String)(2, "修复：下载可能完全卡住，或是下载进度反复回退"))
                FeatureList.Add(New KeyValuePair(Of Integer, String)(1, "新增：内存管理设置，可以选择 G1GC、ZGC 或分代 ZGC"))
                FeatureList.Add(New KeyValuePair(Of Integer, String)(1, "优化：来自 Modrinth 的文件下载速度"))
                FeatureCount += 15
                BugCount += 16
            End If
            If LastVersion < 380 Then 'Snapshot 2.12.2
                FeatureCount += 3
                BugCount += 13
            End If
            If LastVersion < 378 Then 'Snapshot 2.12.1
                If LastVersion >= 377 Then
                    FeatureList.Add(New KeyValuePair(Of Integer, String)(1, "修复：在版本列表中存在 OptiFine 时可能无法加载版本列表"))
                    BugCount += 2
                End If
            End If
            If LastVersion < 377 Then 'Snapshot 2.12.0
                If LastVersion >= 373 Then
                    FeatureList.Add(New KeyValuePair(Of Integer, String)(5, "删除：暂时隐藏联机入口……不过只是暂时关闭，它还会回来的！"))
                End If
                FeatureList.Add(New KeyValuePair(Of Integer, String)(3, "新增：适配新的 Minecraft 版本号系统与 Unobfuscated 版本"))
                FeatureList.Add(New KeyValuePair(Of Integer, String)(2, "优化：巨幅优化各个下载页面和 Mod 管理页面的性能"))
                FeatureList.Add(New KeyValuePair(Of Integer, String)(1, "优化：更换披风时会显示当前使用的披风"))
                FeatureCount += 33
                BugCount += 29
            End If
            If LastVersion < 375 Then 'Snapshot 2.11.2
                'If LastVersion >= 373 Then
                '    FeatureList.Add(New KeyValuePair(Of Integer, String)(3, "优化：对联机进行了各种各样的优化，以改善稳定性"))
                '    FeatureList.Add(New KeyValuePair(Of Integer, String)(2, "优化：若有加入者的网络环境比房主更好，会提示可以让那位加入者担任房主"))
                'End If
                FeatureCount += 16
                BugCount += 4
            End If
            If LastVersion < 374 Then 'Snapshot 2.11.1
                'If LastVersion >= 373 Then
                '    FeatureList.Add(New KeyValuePair(Of Integer, String)(3, "优化：使用离线登录也可以直接加入联机房间了"))
                '    FeatureList.Add(New KeyValuePair(Of Integer, String)(3, "优化：会从所有共享节点中自动选择负载最低的进行中继连接"))
                '    FeatureList.Add(New KeyValuePair(Of Integer, String)(2, "优化：若复制了邀请码，则可以直接快速加入房间"))
                '    FeatureList.Add(New KeyValuePair(Of Integer, String)(2, "优化：关闭 BRCL 时总是会提示是否退出联机，防止在关闭 BRCL 时无意地关闭或退出了房间"))
                '    FeatureList.Add(New KeyValuePair(Of Integer, String)(2, "新增：允许自定义要连接的节点"))
                'End If
                FeatureCount += 9
                BugCount += 7
            End If
            If LastVersion < 373 Then 'Snapshot 2.11.0
                'FeatureList.Add(New KeyValuePair(Of Integer, String)(5, "新增：联机功能！"))
                FeatureCount += 7
                BugCount += 10
            End If
        End If
        '整理更新日志文本
        Dim ContentList As New List(Of String)
        Dim SortedFeatures = FeatureList.OrderByDescending(Function(f) f.Key).ToList
        If Not SortedFeatures.Any() AndAlso FeatureCount = 0 AndAlso BugCount = 0 Then ContentList.Add("没有更新日志……")
        For i = 0 To Math.Min(9, SortedFeatures.Count - 1) '最多取 10 项
            ContentList.Add(SortedFeatures(i).Value)
        Next
        If SortedFeatures.Count > 10 Then FeatureCount += SortedFeatures.Count - 10
        If FeatureCount > 0 OrElse BugCount > 0 Then
            ContentList.Add(If(FeatureCount > 0, FeatureCount & " 项小调整与修改", "") &
                If(FeatureCount > 0 AndAlso BugCount > 0, "，", "") &
                If(BugCount > 0, "修复了 " & BugCount & " 个 Bug", "") &
                "，详见完整更新日志")
        End If
        Dim Content As String = "· " & ContentList.Join(vbCrLf & "· ")
        '输出更新日志
        RunInNewThread(
        Sub()
            If MyMsgBox(Content, "BRCL 已更新至" & VersionDisplay, "确定", "完整更新日志") = 2 Then
                OpenWebsite("https://meloong.com/afd/a/LTCat?tab=feed")
            End If
        End Sub, "UpdateLog Output")
    End Sub

    '窗口加载
    Private IsWindowLoadFinished As Boolean = False
    Private ReadOnly StartupClock As Diagnostics.Stopwatch = Diagnostics.Stopwatch.StartNew()
    Private ReadOnly StartupUiReady As New Global.System.Threading.ManualResetEventSlim(False)
    Private NeedsLayoutMarker As Boolean
    Private DeferredStartupStarted As Boolean
    Private PageWarmupTimer As Threading.DispatcherTimer
    Private WarmupTarget As MyRadioButton
    Private Shared ReadOnly TraceBoot As Boolean = Environment.GetEnvironmentVariable("BRCL_STARTUP_TRACE") = "1"
    Private Shared Sub MarkBoot(stage As String)
        If TraceBoot Then Logger.Info("BootStage " & stage)
    End Sub
    Private ReadOnly EmptyPageLeft As MyPageLeft
    Private FrmNews As PageNews
    Private FrmBank As PageBank
    Public Sub New()
        ApplicationStartTick = GetTimeMs()
        MarkBoot("ctor")
        '窗体参数初始化
        FrmMain = Me
        '版本号改变
        Dim LastVersion As Integer = Settings.Get(Of Integer)("SystemLastVersionReg")
        If LastVersion < VersionCode Then
            '触发升级
            UpgradeSub(LastVersion)
        ElseIf LastVersion > VersionCode Then
            '触发降级
            DowngradeSub(LastVersion)
        End If
        '版本隔离设置迁移
        If Not Settings.HasSaved("LaunchArgumentIndieV2") Then
            If Settings.HasSaved("LaunchArgumentIndie") Then
                Logger.Info("从老 BRCL 迁移版本隔离")
                Settings.Set("LaunchArgumentIndieV2", Settings.Get(Of Integer)("LaunchArgumentIndie"))
            ElseIf HasIniKey("Setup", "LaunchVersionSelect") Then
                Logger.Info("从老 BRCL 升级，但此前未调整版本隔离，使用老的版本隔离默认值")
                Settings.Set("LaunchArgumentIndieV2", Settings.GetDefault("LaunchArgumentIndie"))
            Else
                Logger.Info("全新的 BRCL，使用新的版本隔离默认值")
                Settings.Set("LaunchArgumentIndieV2", Settings.GetDefault("LaunchArgumentIndieV2"))
            End If
        End If
        '刷新主题
        ThemeCheckAll(False)
        ThemeRefresh(Settings.Get(Of Integer)("UiLauncherTheme"))
        'Create controls after the initial palette, avoiding a second resource refresh.
        MarkBoot("launch-left")
        FrmLaunchLeft = New PageLaunchLeft
        MarkBoot("launch-right")
        FrmLaunchRight = New PageLaunchRight
        '注册拖拽事件（不能直接加 Handles，否则没用；#6340）
        [AddHandler](DragDrop.DragEnterEvent, New DragEventHandler(AddressOf HandleDrag), handledEventsToo:=True)
        [AddHandler](DragDrop.DragOverEvent, New DragEventHandler(AddressOf HandleDrag), handledEventsToo:=True)
        '加载 UI
        MarkBoot("window-xaml")
        InitializeComponent()
        MarkBoot("initial-size")
        RestoreStartupSize()
        Opacity = 0
        MarkBoot("attach-home")
        EmptyPageLeft = New MyPageLeft
        '开启管理员权限下的文件拖拽
        If WindowsUtils.HasAdminRole() Then
            Static Helper As New DragHelper
            AddHandler SourceInitialized,
            Sub()
                Dim WpfHelper As New WindowInteropHelper(Me)
                Helper.HwndIntPtrSource = HwndSource.FromHwnd(WpfHelper.Handle)
                Helper.AddHook()
            End Sub
            AddHandler Closing, Sub() Helper.RemoveDragHook()
            AddHandler Helper.DragDrop, Sub() FileDrag(Helper.DropFilePaths)
        End If
        '切换到首页
        If Not IsNothing(FrmLaunchLeft.Parent) Then FrmLaunchLeft.SetValue(ContentPresenter.ContentProperty, Nothing)
        If Not IsNothing(FrmLaunchRight.Parent) Then FrmLaunchRight.SetValue(ContentPresenter.ContentProperty, Nothing)
        PanMainLeft.Child = FrmLaunchLeft
        PageLeft = FrmLaunchLeft
        PanMainRight.Child = FrmLaunchRight
        PageRight = FrmLaunchRight
        ConfigurePageLayout(True)
        FrmLaunchRight.PageState = MyPageRight.PageStates.ContentStay
        '模式提醒
        If BuildType = BuildTypes.Debug Then Hint("[开发者模式] BRCL 正以开发者模式运行，这可能会造成严重的性能下降，请务必立即向开发者反馈此问题！", HintType.Red)
        If ModeDebug Then Hint("[调试模式] BRCL 正以调试模式运行，这可能会导致性能下降，若无必要请不要开启！")
        '尽早执行的加载池
        McFolderListLoader.Start(0) '为了让下载已存在文件检测可以正常运行，必须跑一次；为了让启动按钮尽快可用，需要尽早执行；为了与 PageLaunchLeft 联动，需要为 0 而不是 GetUuid

        Logger.Info($"第二阶段加载用时：{GetTimeMs() - ApplicationStartTick} ms")
    End Sub
    Private Sub RestoreStartupSize()
        Try
            Height = Settings.Get(Of Integer)("WindowHeight")
            Width = Settings.Get(Of Integer)("WindowWidth")
            If Not System.IO.File.Exists(Paths.Base & "BRCL\IndustrialLayout.v1") Then
                Width = Math.Min(1180, SystemParameters.WorkArea.Width - 40)
                Height = Math.Min(740, SystemParameters.WorkArea.Height - 40)
                NeedsLayoutMarker = True
            End If
        Catch ex As Exception '修复 #2019
            Logger.Error(ex, "读取窗口默认大小失败", LogBehavior.Toast)
            Height = MinHeight + 100
            Width = MinWidth + 100
        End Try
    End Sub

    Private Sub FormMain_Loaded(sender As Object, e As RoutedEventArgs) Handles Me.Loaded
        MarkBoot("loaded")
        InitRail()
        ApplicationStartTick = GetTimeMs()
        Handle = New WindowInteropHelper(Me).Handle
        '读取设置
        UpdateBackgroundAndTitleBar()
        PageSetupUI.HiddenRefresh()
        MarkBoot("audio")
        InstallUiSoundHandlers()
        WarmUiSounds()
        MarkBoot("buttons")
        '扩展按钮
        BtnExtraDownload.ShowCheck = AddressOf BtnExtraDownload_ShowCheck
        BtnExtraBack.ShowCheck = AddressOf BtnExtraBack_ShowCheck
        BtnExtraApril.ShowCheck = AddressOf BtnExtraApril_ShowCheck
        BtnExtraShutdown.ShowCheck = AddressOf BtnExtraShutdown_ShowCheck
        BtnExtraApril.ShowRefresh()
        '初始化尺寸改变
        Dim Resizer As New MyResizer(Me)
        Resizer.addResizerDown(ResizerB)
        Resizer.addResizerLeft(ResizerL)
        Resizer.addResizerLeftDown(ResizerLB)
        Resizer.addResizerLeftUp(ResizerLT)
        Resizer.addResizerRight(ResizerR)
        Resizer.addResizerRightDown(ResizerRB)
        Resizer.addResizerRightUp(ResizerRT)
        Resizer.addResizerUp(ResizerT)
        'PLC 彩蛋
        If RandomInteger(1, 1000) = 233 Then
            ShapeTitleLogo.Data = New GeometryConverter().ConvertFromString("M26,29 v-25 h6 a7,7 180 0 1 0,14 h-6 M83,6.5 a10,11.5 180 1 0 0,18 M48,2.5 v24.5 h13.5")
        End If
        '加载窗口
        MarkBoot("position")
        ThemeRefreshMain()
        'MinHeight = 50
        'MinWidth = 50
        Topmost = False
        If FrmStart IsNot Nothing Then FrmStart.Close(TimeSpan.FromMilliseconds(MotionDuration(240) / AniSpeed))
        '更改窗口
        Top = (GetWPFSize(My.Computer.Screen.WorkingArea.Height) - Height) / 2
        Left = (GetWPFSize(My.Computer.Screen.WorkingArea.Width) - Width) / 2
        IsSizeSaveable = True
        ShowWindowToTop()
        Dim HwndSource As Interop.HwndSource = PresentationSource.FromVisual(Me)
        HwndSource.AddHook(New Interop.HwndSourceHook(AddressOf WndProc))
        MarkBoot("enter-motion")
        Dim OpenDuration As Integer = MotionDuration(260)
        Dim BootDelay As Integer = If(UiMotionEnabled, 180, 0)
        AniStart({
            AaCode(Sub() AniControlEnabled -= 1, 20),
            AaOpacity(Me, Settings.Get(Of Integer)("UiLauncherTransparent") / 1000 + 0.4 - Opacity, OpenDuration),
            AaDouble(Sub(i) TransformPos.Y += i, -TransformPos.Y, OpenDuration,, New AniEaseOutFluent),
            AaDouble(Sub(i)
                         TransformScale.ScaleX += i
                         TransformScale.ScaleY += i
                     End Sub, 1 - TransformScale.ScaleX, OpenDuration,, New AniEaseOutFluent),
            AaDouble(Sub(i) BootPosition.Y += i, -8, OpenDuration,, New AniEaseOutFluent),
            AaDouble(Sub(i) BootProgressScale.ScaleX += i, 1, MotionDuration(280),, New AniEaseOutFluent),
            AaOpacity(BootLayer, -1, MotionDuration(180), BootDelay),
            AaCode(
            Sub()
                PanBack.RenderTransform = Nothing
                BootLayer.Visibility = Visibility.Collapsed
                IsWindowLoadFinished = True
                StartupUiReady.Set()
                Logger.Info($"启动首屏可交互：{StartupClock.ElapsedMilliseconds} ms（含窗口进入过渡）")
                PlayUiSound(UiSoundCue.Startup)
                Logger.Info($"DPI：{DPI}，系统版本：{Environment.OSVersion.VersionString}，BRCL 位置：{PathExe}")
                StartStartupUpdateCheck()
                StartDeferredStartup()
            End Sub, , True)
        }, "Form Show")
        'Timer 启动
        AniStart()
        TimerMainStart()
        '加载池
        RunInNewThread(
        Sub()
            'Filesystem scans and Java probing must not contend with the first render.
            StartupUiReady.Wait(TimeSpan.FromSeconds(5))
            If WindowClosing Then Return
            Dim AcceptedAgreementVersion = Settings.Get(Of Integer)("SystemEulaVersion")
            If AcceptedAgreementVersion < UserAgreementVersion Then
                Dim AgreementPrompt = If(AcceptedAgreementVersion = 0,
                                         "请在使用 BRCL 前阅读用户协议与免责声明。",
                                         "BRCL 用户协议与免责声明已更新，请阅读后确认。")
                Do
                    Select Case MyMsgBox(AgreementPrompt, UserAgreementTitle, "同意并继续", "拒绝并退出", "查看协议")
                        Case 1
                            Settings.Set("SystemEulaVersion", UserAgreementVersion)
                            If Settings.Get(Of Integer)("SystemEulaVersion") < UserAgreementVersion Then
                                MyMsgBox("无法保存协议确认。请检查启动器设置的写入权限后重试。", UserAgreementTitle, "退出", ForceWait:=True)
                                EndProgram(False)
                                Return
                            End If
                            Exit Do
                        Case 3
                            MyMsgBox(UserAgreementText, UserAgreementTitle, "返回", ForceWait:=True)
                        Case Else
                            EndProgram(False)
                            Return
                    End Select
                Loop
            End If
            If WindowClosing Then Return
            If StartupUiReady.IsSet Then StartStartupUpdateCheck()
            '启动加载器池
            Try
                JavaInit() '延后到同意协议后再执行，避免在初次启动时进行进程操作
                Thread.Sleep(100)
                DlClientListMojangLoader.Start(1) 'BRCL 会同时根据这里的加载结果决定是否使用官方源进行下载
                RunCountSub()
                RunInNewThread(AddressOf TryClearTaskTemp, "TryClearTaskTemp", ThreadPriority.BelowNormal)
            Catch ex As Exception
                Logger.Error(ex, "初始化加载池运行失败")
            End Try
            '清理自动更新文件
            Try
                FileUtils.Delete(Paths.Base & "BRCL\Plain Craft Launcher 2.exe")
            Catch ex As Exception
                Logger.Warn(ex, "清理自动更新文件失败")
            End Try
            '上报
            Telemetry("启动")
            If Environment.GetEnvironmentVariable("BRCL_UI_PREVIEW") <> "1" Then
                If UsageChoiceNeeded() Then
                    RunInUiWait(
                        Sub()
                            If WindowClosing OrElse Not UsageChoiceNeeded() Then Return
                            Dim Choice = BrclUsageConsentWindow.Ask(Me)
                            If Choice.HasValue Then SaveUsageChoice(Choice.Value)
                        End Sub)
                End If
                If Not WindowClosing Then
                    RecordUsage()
                End If
            End If
        End Sub, "初始化", ThreadPriority.Lowest)

        Logger.Info($"第三阶段加载用时：{GetTimeMs() - ApplicationStartTick} ms")

        '调试钩子：设置环境变量 BRCL_START_PAGE 时直接进入指定页面，便于界面自检
        If Environment.GetEnvironmentVariable("BRCL_UI_PREVIEW") = "1" AndAlso Not String.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BRCL_DIALOG_PREVIEW")) Then
            Dim PreviewTimer As New Threading.DispatcherTimer With {.Interval = TimeSpan.FromMilliseconds(1600)}
            AddHandler PreviewTimer.Tick,
                Sub()
                    PreviewTimer.Stop()
                    Select Case Environment.GetEnvironmentVariable("BRCL_DIALOG_PREVIEW")
                        Case "permission"
                            ShowBrclError("客户端目录访问失败", New UnauthorizedAccessException("示例：客户端目录暂时无法写入。"))
                        Case "fatal"
                            ShowBrclError("BRCL 初始化错误", New InvalidOperationException("示例：无法初始化客户端运行环境。"), Fatal:=True)
                        Case "confirm"
                            MyMsgBox("是否继续此示例操作？", "确认操作", "继续", "取消")
                        Case "usage"
                            BrclUsageConsentWindow.Ask(Me)
                        Case "long"
                            ShowBrclError("启动检查失败", New InvalidOperationException(String.Join(vbCrLf, Enumerable.Repeat("示例诊断行：请检查客户端文件和运行环境。", 100))))
                        Case Else
                            ShowBrclError("服务器状态获取失败", New Net.WebException("示例：远程服务关闭了连接。"))
                    End Select
                End Sub
            PreviewTimer.Start()
        End If
        Try
            Select Case Environment.GetEnvironmentVariable("BRCL_START_PAGE")
                Case "setup"
                    PageChange(PageType.Setup, PageSubType.SetupLaunch)
                Case "about"
                    PageChange(PageType.Setup, PageSubType.SetupAbout)
                Case "sound"
                    PageChange(PageType.Setup, PageSubType.SetupSound)
                Case "sponsors"
                    PageChange(PageType.Setup, PageSubType.SetupSponsors)
                Case "changelog"
                    PageChange(PageType.Setup, PageSubType.SetupChangelog)
                Case "brs"
                    PageChange(PageType.Brs)
                Case "stock"
                    PageChange(PageType.Stock)
                Case "map"
                    PageChange(PageType.SatelliteMap)
                Case "news"
                    PageChange(PageType.News)
                Case "bank"
                    PageChange(PageType.Bank)
                Case "ui"
                    PageChange(PageType.Setup, PageSubType.SetupUI)
                Case "tasks"
                    If Environment.GetEnvironmentVariable("BRCL_UI_PREVIEW") = "1" Then
                        '仅用于界面自检的本地计时任务，不发起下载、不修改游戏文件。
                        Dim PreviewTask As New LoaderTask(Of String, String)("预览下载进度（不下载文件）",
                            Sub(Task)
                                Dim Token = Task.CreateCancellationToken()
                                For StepIndex = 1 To 1800
                                    If Token.WaitHandle.WaitOne(100) Then Token.ThrowIfCancellationRequested()
                                    Task.Progress = StepIndex / 1800.0
                                Next
                            End Sub)
                        Dim PreviewCombo As New LoaderCombo(Of String)("下载界面预览", New List(Of LoaderBase) From {PreviewTask})
                        PreviewCombo.Start("")
                        LoaderTaskbarAdd(PreviewCombo)
                        PageChange(PageType.TaskManager)
                    End If
                Case "instance"
                    If Environment.GetEnvironmentVariable("BRCL_UI_PREVIEW") = "1" Then
                        Dim PreviewTimer As New Threading.DispatcherTimer With {.Interval = TimeSpan.FromSeconds(3)}
                        AddHandler PreviewTimer.Tick,
                            Sub()
                                If McInstanceListLoader.State = LoadState.Loading Then Return
                                PreviewTimer.Stop()
                                PageInstanceLeft.OpenSettings(If(FindBrsInstance(), McInstanceSelected))
                            End Sub
                        PreviewTimer.Start()
                    End If
            End Select
            '调试钩子：BRCL_DEBUG_TOGGLE=1 时启动 6 秒后自动切一次主题，便于自检联动
            If Environment.GetEnvironmentVariable("BRCL_DEBUG_TOGGLE") = "1" Then
                Dim ToggleTimer As New Threading.DispatcherTimer With {.Interval = TimeSpan.FromSeconds(6)}
                AddHandler ToggleTimer.Tick,
                    Sub()
                        Try
                            ToggleTimer.Stop()
                            Logger.Info("BRCL 自检：自动切换主题")
                            ToggleTheme()
                        Catch ex As Exception
                        End Try
                    End Sub
                ToggleTimer.Start()
            End If
        Catch ex As Exception
            Logger.Warn(ex, "调试钩子执行失败")
        End Try
    End Sub

    Private Sub LogFirstContentRendered() Handles Me.ContentRendered
        Static Logged As Boolean
        If Logged Then Return
        Logged = True
        Logger.Info($"启动首帧呈现：{StartupClock.ElapsedMilliseconds} ms")
    End Sub

    ''' <summary>
    ''' 解码背景图片：只解码窗口实际需要的像素宽度，避免保留大尺寸壁纸的完整分辨率。
    ''' WPF 无法解码的格式（WebP 等）回退到 MyBitmap，并立即释放其 GDI 位图。
    ''' </summary>
    Friend Shared Function LoadBackgroundImage(Address As String, DecodeWidth As Integer) As ImageSource
        Try
            Dim Bitmap As New Global.System.Windows.Media.Imaging.BitmapImage
            Using Input = IO.File.OpenRead(Address)
                Dim Decoder = Global.System.Windows.Media.Imaging.BitmapDecoder.Create(Input, Global.System.Windows.Media.Imaging.BitmapCreateOptions.DelayCreation, Global.System.Windows.Media.Imaging.BitmapCacheOption.None)
                Dim PixelWidth = Decoder.Frames(0).PixelWidth
                Input.Position = 0
                Bitmap.BeginInit()
                Bitmap.CacheOption = Global.System.Windows.Media.Imaging.BitmapCacheOption.OnLoad
                Bitmap.DecodePixelWidth = Math.Min(DecodeWidth, PixelWidth)
                Bitmap.StreamSource = Input
                Bitmap.EndInit()
            End Using
            Bitmap.Freeze()
            Return Bitmap
        Catch
            'Keep support for WebP and the legacy image formats.
            Dim Fallback As New MyBitmap(Address)
            Using NativeBitmap As System.Drawing.Bitmap = Fallback.Pic
                Return CType(Fallback, ImageSource)
            End Using
        End Try
    End Function

    ''' <summary>背景图片的目标解码宽度：窗口物理像素宽度，限制在 1280 至 3840 之间。</summary>
    Friend Function BackgroundDecodeWidth() As Integer
        Return CInt(Math.Min(3840, Math.Max(1280, ActualWidth * VisualTreeHelper.GetDpi(Me).DpiScaleX)))
    End Function

    Private Sub StartDeferredStartup()
        If DeferredStartupStarted OrElse WindowClosing Then Return
        DeferredStartupStarted = True
        Dim OriginalBackground = ImgBack.Background
        Dim DecodeWidth = BackgroundDecodeWidth()
        RunInNewThread(
            Sub()
                Try
                    If NeedsLayoutMarker Then
                        IO.Directory.CreateDirectory(Paths.Base & "BRCL")
                        IO.File.WriteAllText(Paths.Base & "BRCL\IndustrialLayout.v1", "BRS launcher layout")
                    End If
                    Dim Pictures = DirectoryUtils.EnumerateFiles(Paths.Base & "BRCL\Pictures\", True).
                        Where(Function(File) Not {"ini", "db"}.Contains(PathUtils.GetExtension(File))).ToList()
                    If Pictures.Count > 0 Then
                        Dim Address = RandomOne(Pictures)
                        'Only the frozen bitmap crosses threads. The brush is created on the UI thread and
                        'stays modifiable, because UpdateBackgroundAndTitleBar sets its stretch and tiling.
                        Dim Picture = LoadBackgroundImage(Address, DecodeWidth)
                        Dispatcher.BeginInvoke(Threading.DispatcherPriority.Background, New Action(
                            Sub()
                                If WindowClosing OrElse ImgBack.Background IsNot OriginalBackground Then Return
                                ImgBack.Background = New ImageBrush(Picture)
                                ImgBack.Visibility = Visibility.Visible
                                UpdateBackgroundAndTitleBar()
                                Logger.Info($"已应用背景图片：{Picture.Width:0} x {Picture.Height:0}")
                                If FrmSetupUI IsNot Nothing Then FrmSetupUI.BackgroundRefreshUI(True, Pictures.Count)
                            End Sub))
                    End If
                Catch ex As Exception
                    Logger.Warn(ex, "初始化背景图片失败")
                End Try
                If Not WindowClosing Then MusicRefreshPlay(False, True)
            End Sub, "延后初始化外观", ThreadPriority.Lowest)
    End Sub

    Private Function NeedsPageWarmup(button As MyRadioButton) As Boolean
        If button Is BtnTitleSelectBrs Then Return FrmBrs Is Nothing
        If button Is BtnTitleSelect3 Then Return FrmSetupLeft Is Nothing
        If button Is BtnTitleSelectStock Then Return FrmStock Is Nothing
        Return button Is BtnTitleSelectMap AndAlso FrmMap Is Nothing
    End Function

    Private Sub QueuePageWarmup(sender As Object, e As RoutedEventArgs) Handles BtnTitleSelectBrs.MouseEnter, BtnTitleSelect3.MouseEnter, BtnTitleSelectStock.MouseEnter, BtnTitleSelectMap.MouseEnter,
        BtnTitleSelectBrs.GotKeyboardFocus, BtnTitleSelect3.GotKeyboardFocus, BtnTitleSelectStock.GotKeyboardFocus, BtnTitleSelectMap.GotKeyboardFocus
        Dim target = DirectCast(sender, MyRadioButton)
        If Not IsWindowLoadFinished OrElse WindowClosing OrElse Not NeedsPageWarmup(target) OrElse Environment.GetEnvironmentVariable("BRCL_UI_PREVIEW") = "1" Then Return
        If PageWarmupTimer Is Nothing Then
            PageWarmupTimer = New Threading.DispatcherTimer(Threading.DispatcherPriority.ApplicationIdle) With {.Interval = TimeSpan.FromMilliseconds(240)}
            AddHandler PageWarmupTimer.Tick, AddressOf WarmupOnePage
        End If
        PageWarmupTimer.Stop()
        WarmupTarget = target
        PageWarmupTimer.Start()
    End Sub

    Private Sub WarmupOnePage(sender As Object, e As EventArgs)
        PageWarmupTimer.Stop()
        Dim target = WarmupTarget
        WarmupTarget = Nothing
        If WindowClosing OrElse Not IsActive OrElse Not IsVisible OrElse WindowState = WindowState.Minimized OrElse target Is Nothing Then Return
        If Not (target.IsMouseOver OrElse target.IsKeyboardFocusWithin) OrElse Not NeedsPageWarmup(target) Then Return
        If AniIsRun("FrmMain PageChangeRight") OrElse AniIsRun("PageRight PageChange " & PageRight.PageUuid) Then Return
        Try
            If My.Computer.Info.AvailablePhysicalMemory < 1073741824UL Then Return
            If target Is BtnTitleSelectBrs Then
                FrmBrs = New PageBRS
            ElseIf target Is BtnTitleSelect3 Then
                FrmSetupLeft = New PageSetupLeft
            ElseIf target Is BtnTitleSelectStock Then
                FrmStock = New PageStock
            ElseIf target Is BtnTitleSelectMap Then
                FrmMap = New PageMap
            End If
        Catch ex As Exception
            Logger.Warn(ex, "预热页面失败，将在打开时重试")
        End Try
    End Sub
    '根据打开次数触发的事件
    Private Sub RunCountSub()
        Settings.Set("SystemCount", Settings.Get(Of Integer)("SystemCount") + 1)
        If BuildType = BuildTypes.Snapshot Then
            Select Case Settings.Get(Of Integer)("SystemCount")
                Case 1
                    MyMsgBox("欢迎使用 BRCL 快照版！" & vbCrLf &
                             "快照版包含尚未在正式版发布的测试性功能，仅用于赞助者本人尝鲜。所以请不要发给其他人或者用于制作整合包哦！" & vbCrLf &
                             "如果你并非通过赞助或赞助者本人邀请进群获得的本程序，那么可能是有人在违规传播，记得提醒他一下啦。", "快照版使用说明")
            End Select
            If Settings.Get(Of Integer)("SystemCount") >= 99 Then
                If ThemeUnlock(6, False) Then
                    MyMsgBox("你已经使用了 99 次 BRCL 啦，感谢你长期以来的支持！" & vbCrLf &
                             "隐藏主题 铁杆粉 已解锁！", "提示")
                End If
            End If
        End If
    End Sub
    '升级与降级事件
    Private Sub UpgradeSub(LastVersionCode As Integer)
        Logger.Info($"版本号从 {LastVersionCode} 升高到 {VersionCode}")
        Settings.Set("SystemLastVersionReg", VersionCode)
        '检查有记录的最高版本号
        Dim HighestVersionCode As Integer
        Const SettingKey = If(BuildType = BuildTypes.Snapshot, "SystemHighestAlphaVersionReg", "SystemHighestBetaVersionReg")
        HighestVersionCode = Settings.Get(Of Integer)(SettingKey)
        If HighestVersionCode < VersionCode Then
            Settings.Set(SettingKey, VersionCode)
            Logger.Info($"最高版本号从 {HighestVersionCode} 升高到 {VersionCode}")
        End If
        '被移除的窗口设置选项
        If Settings.Get(Of Integer)("LaunchArgumentWindowType") = 5 Then Settings.Set("LaunchArgumentWindowType", 1)
        '修改主题设置项名称
        If HighestVersionCode <= 207 Then
            Dim UnlockedTheme As New List(Of String) From {"2"}
            UnlockedTheme.AddRange(New List(Of String)(Settings.Get(Of String)("UiLauncherThemeHide").ToString.Split("|")))
            UnlockedTheme.AddRange(New List(Of String)(Settings.Get(Of String)("UiLauncherThemeHide2").ToString.Split("|")))
            Settings.Set("UiLauncherThemeHide2", UnlockedTheme.Distinct.ToList.Join("|"c))
        End If
        '重置欧皇彩
        If LastVersionCode <= 115 AndAlso Settings.Get(Of String)("UiLauncherThemeHide2").ToString.Split("|").Contains("13") Then
            Dim UnlockedTheme As New List(Of String)(Settings.Get(Of String)("UiLauncherThemeHide2").ToString.Split("|"))
            UnlockedTheme.Remove("13")
            Settings.Set("UiLauncherThemeHide2", UnlockedTheme.Join("|"c))
            MyMsgBox("由于新版 BRCL 修改了欧皇彩的解锁方式，你需要重新解锁欧皇彩。" & vbCrLf &
                     "多谢各位的理解啦！", "重新解锁提醒")
        End If
        '重置滑稽彩
        If LastVersionCode <= 152 AndAlso Settings.Get(Of String)("UiLauncherThemeHide2").ToString.Split("|").Contains("12") Then
            Dim UnlockedTheme As New List(Of String)(Settings.Get(Of String)("UiLauncherThemeHide2").ToString.Split("|"))
            UnlockedTheme.Remove("12")
            Settings.Set("UiLauncherThemeHide2", UnlockedTheme.Join("|"c))
            MyMsgBox("由于新版 BRCL 修改了滑稽彩的解锁方式，你需要重新解锁滑稽彩。" & vbCrLf &
                     "多谢各位的理解啦！", "重新解锁提醒")
        End If
        '移动自定义皮肤
        If LastVersionCode <= 161 AndAlso FileUtils.Exists(Paths.Base & "BRCL\CustomSkin.png") AndAlso Not FileUtils.Exists(Paths.AppDataThenName & "CustomSkin.png") Then
            FileUtils.Copy(Paths.Base & "BRCL\CustomSkin.png", Paths.AppDataThenName & "CustomSkin.png")
            Logger.Info("已移动离线自定义皮肤 (162)")
        End If
        If LastVersionCode <= 263 AndAlso FileUtils.Exists(PathTemp & "CustomSkin.png") AndAlso Not FileUtils.Exists(Paths.AppDataThenName & "CustomSkin.png") Then
            FileUtils.Copy(PathTemp & "CustomSkin.png", Paths.AppDataThenName & "CustomSkin.png")
            Logger.Info("已移动离线自定义皮肤 (264)")
        End If
        '解除帮助页面的隐藏
        If LastVersionCode <= 205 Then
            Settings.Set("UiHiddenOtherHelp", False)
            Logger.Info("已解除帮助页面的隐藏")
        End If
        '单向迁移微软登录结果（#4836）
        If Not Settings.Get(Of Boolean)("CacheMsV2Migrated") Then
            Settings.Set("CacheMsV2Migrated", True)
            Settings.Set("CacheMsV2OAuthRefresh", Settings.Get(Of String)("CacheMsOAuthRefresh"))
            Settings.Set("CacheMsV2Access", Settings.Get(Of String)("CacheMsAccess"))
            Settings.Set("CacheMsV2ProfileJson", Settings.Get(Of String)("CacheMsProfileJson"))
            Settings.Set("CacheMsV2Uuid", Settings.Get(Of String)("CacheMsUuid"))
            Settings.Set("CacheMsV2Name", Settings.Get(Of String)("CacheMsName"))
            Logger.Info("已从老版本迁移微软登录结果")
        End If
        'Mod 命名设置迁移
        If Settings.HasSaved("ToolDownloadTranslate") AndAlso Not Settings.HasSaved("ToolDownloadTranslateV2") Then
            Settings.Set("ToolDownloadTranslateV2", Settings.Get(Of Integer)("ToolDownloadTranslate") + 1)
            Logger.Info("已从老版本迁移 Mod 命名设置")
        End If
        '重置 JVM 参数设置
        If LastVersionCode <= 381 AndAlso Settings.HasSaved("LaunchAdvanceJvm") AndAlso
           Settings.Get(Of String)("LaunchAdvanceJvm").ToString.Replace("-XX:+UseG1GC ", "").Replace("-XX:-UseAdaptiveSizePolicy ", "").Trim = Settings.GetDefault("LaunchAdvanceJvm") Then
            Settings.Reset("LaunchAdvanceJvm")
            Logger.Info("已重置 JVM 参数设置")
        End If
        '输出更新日志
        If LastVersionCode <= 0 Then Return
        If HighestVersionCode >= VersionCode Then Return
        ShowUpdateLog(HighestVersionCode)
    End Sub
    Private Sub DowngradeSub(LastVersionCode As Integer)
        Logger.Info($"版本号从 {LastVersionCode} 降低到 {VersionCode}")
        Settings.Set("SystemLastVersionReg", VersionCode)
    End Sub

#End Region

#Region "自定义窗口"

    '关闭
    Private Sub FormMain_Closing(sender As Object, e As CancelEventArgs) Handles Me.Closing
        EndProgram(True)
        e.Cancel = True
    End Sub
    ''' <summary>
    ''' 正常关闭程序。程序将在执行此方法后约 0.3s 退出。
    ''' </summary>
    ''' <param name="SendWarning">是否在还有下载任务未完成时发出警告。</param>
    Public Sub EndProgram(SendWarning As Boolean)
        If WindowClosing Then Return
        '强行结束下载任务？
        If HasDownloadingTask() Then
            If SendWarning AndAlso MyMsgBox("还有下载任务尚未完成，是否确定退出？", "提示", "确定", "取消") = 2 Then Return
            RunInNewThread(
            Sub()
                Logger.Info("正在强行停止任务")
                For Each Task As LoaderBase In LoaderTaskbar.ToList()
                    Task.Cancel()
                Next
            End Sub, "强行停止下载任务")
        End If
        '关闭联机？
        If FrmLinkMain?.TryExit(Not SendWarning, True) Then Return
        '关闭
        RunInUiWait(
        Sub()
            If WindowClosing Then Return
            WindowClosing = True
            AniStop("Form Show")
            AniStop("Form WindowMotion")
            IsHitTestVisible = False
            If Not UiMotionEnabled OrElse WindowState = WindowState.Minimized Then
                EndProgramForce()
                Return
            End If
            PanBack.RenderTransformOrigin = New Point(0.5, 0.5)
            Dim TransformPos As TranslateTransform = Nothing
            Dim TransformScale As ScaleTransform = Nothing
            PrepareWindowMotion(TransformScale, TransformPos)
            AniStart({
                AaOpacity(Me, -Opacity, 170,, New AniEaseInFluent(AniEasePower.Weak)),
                AaDouble(
                Sub(i)
                    TransformScale.ScaleX += i
                    TransformScale.ScaleY += i
                End Sub, 0.985 - TransformScale.ScaleX, 170,, New AniEaseInFluent),
                AaDouble(Sub(i) TransformPos.Y += i, 6 - TransformPos.Y, 170,, New AniEaseInFluent),
                AaCode(
                Sub()
                    IsHitTestVisible = False
                    Top = -10000
                    ShowInTaskbar = False
                End Sub, 175),
                AaCode(AddressOf EndProgramForce, 190)
            }, "Form Close")
            Logger.Info("收到关闭指令")
        End Sub)
    End Sub
    Private Shared IsLogShown As Boolean = False
    Public Shared Sub EndProgramForce(Optional ReturnCode As ProcessReturnValues = ProcessReturnValues.Success)
        On Error Resume Next
        If ReturnCode = ProcessReturnValues.Exception Then
            QueueLauncherCrash("启动器因异常退出，详情见 launcher.log。")
        Else
            CompleteCrashSession()
        End If
        IsProgramEnding = True
        StopUiSounds()
        AniControlEnabled += 1
        If IsUpdateWaitingRestart Then UpdateRestart(False)
        If ReturnCode = ProcessReturnValues.Exception Then
            If Not IsLogShown Then
                FeedbackInfo()
                Logger.Info("请在 https://github.com/Meloong-Git/PCL/issues 提交错误报告，以便于作者解决此问题！")
                IsLogShown = True
                StartProcess(Paths.Base & "BRCL\Log1.txt")
            End If
            Thread.Sleep(500) '防止 BRCL 在记事本打开前就被掐掉
        End If
        Logger.Info($"程序已退出，返回值：{ReturnCode}")
        ConfigUtils.SaveAll()
        Logger.Instance.Flush()
        If ReturnCode <> ProcessReturnValues.Success Then Environment.Exit(ReturnCode)
        Process.GetCurrentProcess.Kill()
    End Sub
    Private Sub BtnTitleClose_Click(sender As Object, e As RoutedEventArgs) Handles BtnTitleClose.Click
        EndProgram(True)
    End Sub

    '移动
    Private Sub FormDragMove(sender As Object, e As MouseButtonEventArgs) Handles PanTitle.MouseLeftButtonDown, PanMsg.MouseLeftButtonDown
        If WindowClosing OrElse WindowMotionBusy OrElse Not IsWindowLoadFinished Then Return
        If Not CType(sender, UIElement).IsMouseDirectlyOver Then Return
        If WindowState = WindowState.Maximized AndAlso sender IsNot PanTitle Then Return
        If sender Is PanTitle AndAlso e.ClickCount = 2 Then
            BtnTitleMax_Click()
            e.Handled = True
            Return
        End If
        e.Handled = True
        Try
            If WindowState = WindowState.Maximized Then
                MyResizer.DragMaximizedWindow(Me, PanTitle)
            Else
                DragMove()
            End If
        Catch ex As InvalidOperationException When Mouse.LeftButton <> MouseButtonState.Pressed
            'The button can be released between the input event and the native move loop.
        Catch ex As Exception
            Logger.Warn(ex, "拖动启动器窗口失败")
        End Try
    End Sub

    '改变大小
    ''' <summary>
    ''' 是否可以向注册表储存尺寸改变信息。以此避免初始化时误储存。
    ''' </summary>
    Public IsSizeSaveable As Boolean = False
    Private Sub FormMain_SizeChanged() Handles Me.SizeChanged, Me.Loaded, BorderForm.SizeChanged
        If IsSizeSaveable AndAlso WindowState = WindowState.Normal Then
            Settings.Set("WindowHeight", Height)
            Settings.Set("WindowWidth", Width)
        End If
        RectForm.Rect = New Rect(0, 0, BorderForm.ActualWidth, BorderForm.ActualHeight)
        PanForm.Width = BorderForm.ActualWidth + 0.001
        PanForm.Height = BorderForm.ActualHeight + 0.001
        UpdateRailExpand(PanForm.Width)
        PanMain.Width = Math.Max(0, PanForm.Width - RailWidthNow) '减去左侧导航栏宽度
        PanMain.Height = Math.Max(0, PanForm.Height - PanTitle.ActualHeight)
    End Sub

#Region "左侧导航栏"

    Private Const RailCompact As Double = 76
    Private Const RailWide As Double = 88
    Private RailWidthNow As Double = RailCompact
    Private RailIsWide As Boolean = False
    Private RailWidthTimer As Threading.DispatcherTimer

    Private Sub InitRail()
        Try
            For Each Item As Grid In {Rail0, Rail13, Rail10, Rail11, Rail14, Rail12, Rail3}
                Item.Focusable = True
                Item.FocusVisualStyle = CType(FindResource("BrclRailFocusVisual"), Style)
                KeyboardNavigation.SetIsTabStop(Item, True)
                Automation.AutomationProperties.SetName(Item, RailNameOf(CInt(Item.Tag)))
                Dim ShortcutLabel = TryCast(FindName("RailNum" & CStr(Item.Tag)), TextBlock)
                If ShortcutLabel IsNot Nothing Then Automation.AutomationProperties.SetAcceleratorKey(Item, ShortcutLabel.Text)
                AddHandler Item.KeyDown, AddressOf Rail_KeyDown
                AddHandler Item.MouseUp, AddressOf Rail_Click
                '悬停用短促的透明度过渡（110ms），接近终末地那种“设备回弹”手感
                AddHandler Item.MouseEnter, Sub(sender As Object, e As MouseEventArgs)
                                                Dim G As Grid = CType(sender, Grid)
                                                AniStart(AaOpacity(G, 0.82 - G.Opacity, 110), "RailHover " & G.Name)
                                            End Sub
                AddHandler Item.MouseLeave, Sub(sender As Object, e As MouseEventArgs)
                                                Dim G As Grid = CType(sender, Grid)
                                                AniStart(AaOpacity(G, 1 - G.Opacity, 140), "RailHover " & G.Name)
                                            End Sub
            Next
            AddHandler RailThemeBtn.MouseUp, AddressOf RailTheme_Click
            RailThemeBtn.Focusable = True
            RailThemeBtn.FocusVisualStyle = CType(FindResource("BrclRailFocusVisual"), Style)
            KeyboardNavigation.SetIsTabStop(RailThemeBtn, True)
            Automation.AutomationProperties.SetName(RailThemeBtn, "切换浅色或深色主题")
            AddHandler RailThemeBtn.KeyDown, AddressOf Rail_KeyDown
            AddHandler RailThemeBtn.MouseEnter, Sub() AniStart(AaOpacity(RailThemeBtn, 0.82 - RailThemeBtn.Opacity, 110), "RailThemeHover")
            AddHandler RailThemeBtn.MouseLeave, Sub() AniStart(AaOpacity(RailThemeBtn, 1 - RailThemeBtn.Opacity, 140), "RailThemeHover")
            '旧的顶部胶囊导航隐藏（逻辑仍在，只是不再露出）
            PanTitleSelect.Visibility = Visibility.Collapsed
            UpdateThemeIcon()
            ApplyChromeTheme()
            UpdateRail()
        Catch ex As Exception
            Logger.Warn(ex, "初始化左侧导航栏失败")
        End Try
    End Sub

    ''' <summary>
    ''' 宽窗口时侧栏展开为图标 + 文字（Linear / Discord 的做法），窄窗口回收为纯图标。
    ''' 用 1000 / 1080 两个阈值做滞回，避免在边界上反复抖动。
    ''' </summary>
    Private Sub UpdateRailExpand(FormWidth As Double)
        Try
            Dim ShouldWide As Boolean = False
            If ShouldWide = RailIsWide Then Return
            RailIsWide = ShouldWide
            AnimateRailWidth(If(ShouldWide, RailWide, RailCompact))
            Dim Items = {(Rail0, RailInner0, RailIcon0, RailText0, RailNum0),
                         (Rail13, RailInner13, RailIcon13, RailText13, RailNum13),
                         (Rail10, RailInner10, RailIcon10, RailText10, RailNum10),
                         (Rail11, RailInner11, RailIcon11, RailText11, RailNum11),
                         (Rail14, RailInner14, RailIcon14, RailText14, RailNum14),
                         (Rail12, RailInner12, RailIcon12, RailText12, RailNum12),
                         (Rail3, RailInner3, RailIcon3, RailText3, RailNum3),
                         (RailThemeBtn, RailThemeInner, RailThemeIcon, RailThemeText, RailNumTheme)}
            For Each It In Items
                Dim Item As Grid = It.Item1
                Dim Inner As StackPanel = It.Item2
                Dim Icon As Shapes.Path = It.Item3
                Dim Label As TextBlock = It.Item4
                Dim Num As TextBlock = It.Item5
                If ShouldWide Then
                    Item.Height = 44
                    Item.Margin = New Thickness(8, 2, 8, 2)
                    Inner.Orientation = Orientation.Horizontal
                    Inner.HorizontalAlignment = HorizontalAlignment.Left
                    Inner.Margin = New Thickness(26, 0, 0, 0)
                    Icon.Margin = New Thickness(0, 0, 12, 0)
                    Label.FontSize = 12.5
                    Label.Margin = New Thickness(0)
                    Label.VerticalAlignment = VerticalAlignment.Center
                    Num.HorizontalAlignment = HorizontalAlignment.Left
                    Num.VerticalAlignment = VerticalAlignment.Center
                    Num.Margin = New Thickness(10, 0, 0, 0)
                    Num.Opacity = 0.45
                Else
                    Item.Height = 56
                    Item.Margin = New Thickness(6, 2, 6, 2)
                    Inner.Orientation = Orientation.Vertical
                    Inner.HorizontalAlignment = HorizontalAlignment.Center
                    Inner.Margin = New Thickness(0, 6, 0, 0)
                    Icon.Margin = New Thickness(0)
                    Label.FontSize = 10.5
                    Label.Margin = New Thickness(0, 4, 0, 0)
                    Label.VerticalAlignment = VerticalAlignment.Top
                    Num.HorizontalAlignment = HorizontalAlignment.Right
                    Num.VerticalAlignment = VerticalAlignment.Top
                    Num.Margin = New Thickness(0, 2, 5, 0)
                    Num.Opacity = 0.7
                End If
            Next
            '主题按钮与其它项保持同样的间距节奏
            RailThemeBtn.Margin = If(ShouldWide, New Thickness(8, 14, 8, 2), New Thickness(6, 12, 6, 2))
        Catch ex As Exception
            Logger.Warn(ex, "更新侧栏展开状态失败")
        End Try
    End Sub

    Private Sub Rail_Click(sender As Object, e As MouseButtonEventArgs)
        If e.ChangedButton <> MouseButton.Left Then Return
        Dim Taget As Integer
        If Integer.TryParse(CStr(CType(sender, Grid).Tag), Taget) Then
            Logger.Info($"BRCL 点击导航项 {Taget}")
            PlayUiSound(UiSoundCue.Navigate)
            PageChange(Taget)
            e.Handled = True
        End If
    End Sub

    Private Sub Rail_KeyDown(sender As Object, e As KeyEventArgs)
        If e.Key <> Key.Enter AndAlso e.Key <> Key.Space Then Return
        e.Handled = True
        If e.IsRepeat Then Return
        If sender Is RailThemeBtn Then
            ToggleTheme()
        Else
            PlayUiSound(UiSoundCue.Navigate)
            PageChange(CInt(CType(sender, Grid).Tag))
        End If
    End Sub

    Private Sub UpdateRail()
        Try
            Dim Cur As Integer = CInt(PageCurrent.Page)
            SetRailItem(Rail0, RailBar0, RailIcon0, RailText0, Cur = CInt(PageType.Launch))
            SetRailItem(Rail10, RailBar10, RailIcon10, RailText10, Cur = CInt(PageType.Brs))
            SetRailItem(Rail11, RailBar11, RailIcon11, RailText11, Cur = CInt(PageType.Stock))
            SetRailItem(Rail14, RailBar14, RailIcon14, RailText14, Cur = CInt(PageType.Bank))
            SetRailItem(Rail12, RailBar12, RailIcon12, RailText12, Cur = CInt(PageType.SatelliteMap))
            SetRailItem(Rail3, RailBar3, RailIcon3, RailText3, Cur = CInt(PageType.Setup))
            SetRailItem(Rail13, RailBar13, RailIcon13, RailText13, Cur = CInt(PageType.News))
            LabTitlePage.Text = If(PageNameGet(PageCurrent) = "", RailNameOf(Cur), PageNameGet(PageCurrent))
        Catch ex As Exception
        End Try
    End Sub

    Private Shared Function RailNameOf(Page As Integer) As String
        Select Case Page
            Case CInt(PageType.Launch) : Return "启动"
            Case CInt(PageType.Brs) : Return "BRS 服务器"
            Case CInt(PageType.Stock) : Return "交易所"
            Case CInt(PageType.Bank) : Return "银行"
            Case CInt(PageType.SatelliteMap) : Return "卫星图"
            Case CInt(PageType.Setup) : Return "设置"
            Case CInt(PageType.News) : Return "新闻"
            Case Else : Return "BRCL"
        End Select
    End Function

    ''' <summary>侧栏宽度过渡：Linear / Discord 式 160ms ease-out，替代瞬变。</summary>
    Private Sub AnimateRailWidth(Target As Double)
        Try
            RailWidthTimer?.Stop()
            Dim From As Double = RailWidthNow
            If Math.Abs(Target - From) < 0.5 Then
                RailWidthNow = Target
                ColRail.Width = New GridLength(Target)
                PanRail.Width = Target
                Return
            End If
            Dim Sw As New System.Diagnostics.Stopwatch()
            Sw.Start()
            Dim Timer As New Threading.DispatcherTimer With {.Interval = TimeSpan.FromMilliseconds(16)}
            RailWidthTimer = Timer
            AddHandler Timer.Tick,
                Sub()
                    Dim P As Double = Math.Min(1, Sw.Elapsed.TotalMilliseconds / 160)
                    Dim E As Double = 1 - Math.Pow(1 - P, 3)
                    RailWidthNow = From + (Target - From) * E
                    ColRail.Width = New GridLength(RailWidthNow)
                    PanRail.Width = RailWidthNow
                    PanMain.Width = Math.Max(0, PanForm.Width - RailWidthNow)
                    If P >= 1 Then
                        Timer.Stop()
                        RailWidthNow = Target
                        ColRail.Width = New GridLength(Target)
                        PanRail.Width = Target
                        PanMain.Width = Math.Max(0, PanForm.Width - Target)
                    End If
                End Sub
            Timer.Start()
        Catch ex As Exception
            RailWidthNow = Target
            ColRail.Width = New GridLength(Target)
            PanRail.Width = Target
        End Try
    End Sub

    ''' <summary>窗口按钮等原生控件跟随主题：浅色用深色图标，深色用白色图标。</summary>
    Private Sub ApplyChromeTheme()
        Try
            Dim IsLight As Boolean = (Settings.Get(Of Integer)("UiThemeMode") = 1)
            BtnTitleClose.Theme = If(IsLight, MyIconButton.Themes.Black, MyIconButton.Themes.White)
            BtnTitleMin.Theme = If(IsLight, MyIconButton.Themes.Black, MyIconButton.Themes.White)
            BtnTitleMax.Theme = If(IsLight, MyIconButton.Themes.Black, MyIconButton.Themes.White)
            BtnTitleInner.Theme = If(IsLight, MyIconButton.Themes.Black, MyIconButton.Themes.White)
        Catch ex As Exception
        End Try
    End Sub

    Private Sub SetRailItem(Item As Grid, Bar As Shapes.Rectangle, Icon As Shapes.Path, Label As TextBlock, Selected As Boolean)
        Automation.AutomationProperties.SetItemStatus(Item, If(Selected, "已选中", ""))
        Bar.Visibility = If(Selected, Visibility.Visible, Visibility.Collapsed)
        If Selected Then
            '选中指示条做一次“落位”动画
            Bar.Opacity = 0
            AniStart(AaOpacity(Bar, 1, 160,, New AniEaseOutFluent), "RailBar " & Item.Name)
        End If
        If Selected Then
            Item.SetResourceReference(Panel.BackgroundProperty, "ColorBrushRailActive")
        Else
            Item.Background = Brushes.Transparent
        End If
        Icon.SetResourceReference(Shapes.Path.FillProperty, If(Selected, "IndustrialAccent", "IndustrialRailText"))
        Label.SetResourceReference(TextBlock.ForegroundProperty, If(Selected, "IndustrialRailActiveText", "IndustrialRailText"))
    End Sub

#End Region

    '主题切换
    Private Sub RailTheme_Click(sender As Object, e As MouseButtonEventArgs)
        If e.ChangedButton <> MouseButton.Left Then Return
        Logger.Info("BRCL 点击主题按钮")
        ToggleTheme()
    End Sub

    ''' <summary>在浅色与深色之间切换（供按钮与自检钩子复用）。</summary>
    Private Sub ToggleTheme()
        PlayUiSound(UiSoundCue.Toggle)
        Try
            Dim Mode As Integer = Settings.Get(Of Integer)("UiThemeMode")
            Settings.Set("UiThemeMode", If(Mode = 1, 2, 1))
            ThemeRefresh()
            UpdateThemeIcon()
            ApplyChromeTheme()
            FrmBrs?.RefreshTheme()
            FrmMap?.ApplyWebTheme()
            '交易所页面已打开时，让网页同步跟随新主题
            Try
                FrmStock?.ApplyWebTheme()
            Catch ex As Exception
            End Try
            Hint(If(Settings.Get(Of Integer)("UiThemeMode") = 1, "已切换到浅色主题", "已切换到深色主题"), HintType.Green)
        Catch ex As Exception
            Logger.Warn(ex, "切换主题失败")
        End Try
    End Sub

    ''' <summary>浅色时显示月亮（点击切到深色），深色时显示太阳。</summary>
    Private Sub UpdateThemeIcon()
        Try
            If Settings.Get(Of Integer)("UiThemeMode") = 1 Then
                RailThemeIcon.Data = CType(System.Windows.Application.Current.FindResource("BrclGeometryMoon"), Geometry)
                RailThemeBtn.ToolTip = "切换到深色"
            Else
                RailThemeIcon.Data = CType(System.Windows.Application.Current.FindResource("BrclGeometrySun"), Geometry)
                RailThemeBtn.ToolTip = "切换到浅色"
            End If
        Catch ex As Exception
        End Try
    End Sub

    Private Sub BtnTitleMax_Click() Handles BtnTitleMax.Click
        If WindowClosing OrElse WindowMotionBusy OrElse Not IsWindowLoadFinished Then Return
        WindowState = If(WindowState = WindowState.Maximized, WindowState.Normal, WindowState.Maximized)
    End Sub

    Private Sub BtnTitleMax_KeyDown(sender As Object, e As KeyEventArgs) Handles BtnTitleMax.KeyDown
        If e.Key <> Key.Enter AndAlso e.Key <> Key.Space Then Return
        e.Handled = True
        If e.IsRepeat Then Return
        PlayUiSound()
        BtnTitleMax_Click()
    End Sub

    Private Sub BtnTitleChrome_KeyDown(sender As Object, e As KeyEventArgs) Handles BtnTitleMin.KeyDown, BtnTitleClose.KeyDown, BtnTitleInner.KeyDown
        If e.Key <> Key.Enter AndAlso e.Key <> Key.Space Then Return
        e.Handled = True
        If e.IsRepeat OrElse WindowClosing OrElse WindowMotionBusy OrElse Not IsWindowLoadFinished Then Return
        PlayUiSound()
        If sender Is BtnTitleMin Then
            BtnTitleMin_Click()
        ElseIf sender Is BtnTitleClose Then
            BtnTitleClose_Click(sender, Nothing)
        ElseIf sender Is BtnTitleInner Then
            PageBack()
        End If
    End Sub

    Private Shared Sub UpdateMaximizeButton(Button As MyIconButton, Maximized As Boolean)
        Dim Label = If(Maximized, "还原", "最大化")
        Button.ToolTip = Label
        Automation.AutomationProperties.SetName(Button, Label)
        Button.Logo = CStr(System.Windows.Application.Current.FindResource(If(Maximized, "BrclIconRestore", "BrclIconMaximize")))
    End Sub

    Private Sub UpdateWindowStateChrome() Handles Me.StateChanged
        If BtnTitleMax Is Nothing Then Return
        Dim Maximized = WindowState = WindowState.Maximized OrElse
            (WindowState = WindowState.Minimized AndAlso LastUnminimizedWindowState = WindowState.Maximized)
        UpdateMaximizeButton(BtnTitleMax, Maximized)
        PanBack.Margin = New Thickness(If(Maximized, 0, 10))
        BorderForm.Margin = New Thickness(If(Maximized, 0, 8))
        RectForm.RadiusX = If(Maximized, 0, 3)
        RectForm.RadiusY = If(Maximized, 0, 3)
        For Each Edge As FrameworkElement In {ResizerT, ResizerB, ResizerL, ResizerR, ResizerLT, ResizerLB, ResizerRT, ResizerRB}
            Edge.Visibility = If(Maximized, Visibility.Collapsed, Visibility.Visible)
        Next
    End Sub

    '最小化
    Private Sub BtnTitleMin_Click() Handles BtnTitleMin.Click
        If WindowClosing OrElse WindowMotionBusy OrElse Not IsWindowLoadFinished Then Return
        If Not UiMotionEnabled Then
            WindowState = WindowState.Minimized
            Return
        End If
        AniStop("Form WindowMotion")
        WindowMotionBusy = True
        PanBack.IsHitTestVisible = False
        Dim Scale As ScaleTransform = Nothing
        Dim Position As TranslateTransform = Nothing
        PrepareWindowMotion(Scale, Position)
        AniStart({
            AaDouble(Sub(i)
                         Scale.ScaleX += i
                         Scale.ScaleY += i
                     End Sub, 0.98 - Scale.ScaleX, 160,, New AniEaseInFluent),
            AaDouble(Sub(i) Position.Y += i, 10 - Position.Y, 160,, New AniEaseInFluent),
            AaOpacity(Me, -Opacity, 160),
            AaCode(Sub()
                       WindowState = WindowState.Minimized
                       PanBack.RenderTransform = Nothing
                       Opacity = Settings.Get(Of Integer)("UiLauncherTransparent") / 1000 + 0.4
                       PanBack.IsHitTestVisible = True
                       WindowMotionBusy = False
                   End Sub,, True)
        }, "Form WindowMotion")
    End Sub

    Private WindowClosing As Boolean
    Private WindowMotionBusy As Boolean
    Private WindowWasMinimized As Boolean
    Private LastUnminimizedWindowState As WindowState = WindowState.Normal

    Private Sub PrepareWindowMotion(ByRef Scale As ScaleTransform, ByRef Position As TranslateTransform)
        Dim Group = TryCast(PanBack.RenderTransform, TransformGroup)
        Scale = If(Group?.Children.OfType(Of ScaleTransform)().FirstOrDefault(), New ScaleTransform(1, 1))
        Position = If(Group?.Children.OfType(Of TranslateTransform)().FirstOrDefault(), New TranslateTransform())
        PanBack.RenderTransformOrigin = New Point(0.5, 0.5)
        PanBack.RenderTransform = New TransformGroup With {.Children = New TransformCollection({Scale, Position})}
    End Sub
    Private Sub WindowRestoreAnimation() Handles Me.StateChanged
        If WindowClosing Then Return
        If WindowState = WindowState.Minimized Then
            WindowWasMinimized = True
            Return
        End If
        LastUnminimizedWindowState = WindowState
        If Not WindowWasMinimized OrElse Not IsWindowLoadFinished Then Return
        WindowWasMinimized = False
        If Not UiMotionEnabled Then Return
        AniStop("Form WindowMotion")
        WindowMotionBusy = False
        PanBack.IsHitTestVisible = True
        PanBack.RenderTransformOrigin = New Point(0.5, 0.5)
        Dim Scale As New ScaleTransform(0.985, 0.985)
        Dim Position As New TranslateTransform(0, 6)
        PanBack.RenderTransform = New TransformGroup With {.Children = New TransformCollection({Scale, Position})}
        Opacity = 0
        AniStart({
            AaOpacity(Me, Settings.Get(Of Integer)("UiLauncherTransparent") / 1000 + 0.4, 180),
            AaDouble(Sub(i)
                         Scale.ScaleX += i
                         Scale.ScaleY += i
                     End Sub, 0.015, 180,, New AniEaseOutFluent),
            AaDouble(Sub(i) Position.Y += i, -6, 180,, New AniEaseOutFluent),
            AaCode(Sub() PanBack.RenderTransform = Nothing,, True)
        }, "Form WindowMotion")
    End Sub

    '背景图片与标题栏
    Public Shared Sub UpdateBackgroundAndTitleBar(Value) '从设置更新
        If FrmMain Is Nothing OrElse Not FrmMain.IsLoaded Then Return
        FrmMain.UpdateBackgroundAndTitleBar()
    End Sub
    Public Sub UpdateBackgroundAndTitleBar()
        Logger.Info("从设置更新背景图片与标题栏样式")
        '背景图片透明度
        ImgBack.Opacity = Settings.Get(Of Integer)("UiBackgroundOpacity") / 1000
        '背景图片模糊
        Dim BlurRadius As Double = Settings.Get(Of Integer)("UiBackgroundBlur") + 1
        If BlurRadius = 1 Then
            ImgBack.Effect = Nothing
        Else
            ImgBack.Effect = New Effects.BlurEffect With {.Radius = BlurRadius}
        End If
        ImgBack.Margin = New Thickness(-BlurRadius / 1.8)
        '背景图片适应方式
        Dim BackgroundType As Integer = Settings.Get(Of Integer)("UiBackgroundSuit")
        Dim Brush = TryCast(ImgBack.Background, ImageBrush)
        If Brush IsNot Nothing AndAlso Brush.IsFrozen Then
            'A frozen brush cannot take the stretch/tiling below; work on a modifiable copy.
            Brush = Brush.CloneCurrentValue()
            ImgBack.Background = Brush
        End If
        If Brush IsNot Nothing Then
            If BackgroundType = 0 Then
                '智能：当图片较小时平铺，较大时适应
                If Brush.ImageSource.Width < PanMain.ActualWidth / 2 AndAlso Brush.ImageSource.Height < PanMain.ActualHeight / 2 Then
                    BackgroundType = 4 '平铺
                Else
                    BackgroundType = 2 '适应
                End If
            End If
            Brush.Stretch = Stretch.UniformToFill
            Brush.TileMode = TileMode.None
            Brush.Viewport = New Rect(0, 0, 1, 1)
            Brush.ViewportUnits = BrushMappingMode.RelativeToBoundingBox
            Brush.AlignmentX = AlignmentX.Center
            Brush.AlignmentY = AlignmentY.Center
            Select Case BackgroundType
                Case 1 '中
                    Brush.Stretch = Stretch.None
                Case 3 '拉伸
                    Brush.Stretch = Stretch.Fill
                Case 4 '平铺
                    Brush.Stretch = Stretch.None
                    Brush.TileMode = TileMode.Tile
                    Brush.Viewport = New Rect(0, 0, Brush.ImageSource.Width, Brush.ImageSource.Height)
                    Brush.ViewportUnits = BrushMappingMode.Absolute

                Case 2 '适应
                Case 5 '左上
                    Brush.AlignmentX = AlignmentX.Left
                    Brush.AlignmentY = AlignmentY.Top
                Case 6 '右上
                    Brush.AlignmentX = AlignmentX.Right
                    Brush.AlignmentY = AlignmentY.Top
                Case 7 '左下
                    Brush.AlignmentX = AlignmentX.Left
                    Brush.AlignmentY = AlignmentY.Bottom
                Case 8 '右下
                    Brush.AlignmentX = AlignmentX.Right
                    Brush.AlignmentY = AlignmentY.Bottom
                Case 9 '左
                    Brush.AlignmentX = AlignmentX.Left
                    Brush.AlignmentY = AlignmentY.Center
                Case 10 '右
                    Brush.AlignmentX = AlignmentX.Right
                    Brush.AlignmentY = AlignmentY.Center
                Case 11 '上
                    Brush.AlignmentX = AlignmentX.Center
                    Brush.AlignmentY = AlignmentY.Top
                Case 12 '下
                    Brush.AlignmentX = AlignmentX.Center
                    Brush.AlignmentY = AlignmentY.Bottom
            End Select
        End If
        '标题栏显示类型
        Select Case Settings.Get(Of Integer)("UiLogoType")
            Case 0 '无
                ShapeTitleLogo.Visibility = Visibility.Collapsed
                LabTitleLogo.Visibility = Visibility.Collapsed
                ImageTitleLogo.Visibility = Visibility.Collapsed
                If Not IsNothing(FrmSetupUI) Then 'TODO: 和 FrmSetupUI 解耦
                    FrmSetupUI.CheckLogoLeft.Visibility = Visibility.Visible
                    FrmSetupUI.PanLogoText.Visibility = Visibility.Collapsed
                    FrmSetupUI.PanLogoChange.Visibility = Visibility.Collapsed
                End If
            Case 1 '默认
                ShapeTitleLogo.Visibility = Visibility.Collapsed
                LabTitleLogo.Visibility = Visibility.Collapsed
                ImageTitleLogo.Visibility = Visibility.Collapsed
                Try
                    ImageTitleLogo.Source = "pack://application:,,,/BRCL;component/Images/TitleLogo.png"
                Catch ex As Exception
                    ImageTitleLogo.Source = Nothing
                End Try
                If Not IsNothing(FrmSetupUI) Then
                    FrmSetupUI.CheckLogoLeft.Visibility = Visibility.Collapsed
                    FrmSetupUI.PanLogoText.Visibility = Visibility.Collapsed
                    FrmSetupUI.PanLogoChange.Visibility = Visibility.Collapsed
                End If
            Case 2 '文本
                ShapeTitleLogo.Visibility = Visibility.Collapsed
                LabTitleLogo.Visibility = Visibility.Collapsed
                ImageTitleLogo.Visibility = Visibility.Collapsed
                If Not IsNothing(FrmSetupUI) Then
                    FrmSetupUI.CheckLogoLeft.Visibility = Visibility.Collapsed
                    FrmSetupUI.PanLogoText.Visibility = Visibility.Visible
                    FrmSetupUI.PanLogoChange.Visibility = Visibility.Collapsed
                End If
            Case 3 '图片
                ShapeTitleLogo.Visibility = Visibility.Collapsed
                LabTitleLogo.Visibility = Visibility.Collapsed
                ImageTitleLogo.Visibility = Visibility.Collapsed
                If Not IsNothing(FrmSetupUI) Then
                    FrmSetupUI.CheckLogoLeft.Visibility = Visibility.Collapsed
                    FrmSetupUI.PanLogoText.Visibility = Visibility.Collapsed
                    FrmSetupUI.PanLogoChange.Visibility = Visibility.Visible
                End If
                Try
                    ImageTitleLogo.Source = ReadTitleLogoFile()
                Catch ex As Exception
                    ImageTitleLogo.Source = Nothing
                    Logger.Error(ex, "显示标题栏图片失败", LogBehavior.Alert)
                End Try
        End Select
        FrmSetupUI?.CardLogo.TriggerForceResize()
        '标题栏文本
        LabTitleLogo.Text = Settings.Get(Of String)("UiLogoText")
        '标题栏文本是否居左
        PanTitleMain.ColumnDefinitions(0).Width = New GridLength(If(Settings.Get(Of Boolean)("UiLogoLeft") AndAlso Settings.Get(Of Integer)("UiLogoType") = 0, 0, 1), GridUnitType.Star)
    End Sub

#End Region

#Region "窗体事件"

    '按键事件
    Private Sub FormMain_KeyDown(sender As Object, e As KeyEventArgs) Handles Me.KeyDown
        If e.Handled OrElse e.IsRepeat Then Return
        '修复按下 Alt 后误认为弹出系统菜单导致的冻结
        If e.SystemKey = Key.LeftAlt OrElse e.SystemKey = Key.RightAlt Then e.Handled = True
        '在有弹窗时：回车选择第一个，Esc 选择最后一个
        If PanMsg.Children.Any Then
            If e.Key = Key.Enter Then
                e.Handled = True
                CType(PanMsg.Children(0), Object).Btn1_Click()
            ElseIf e.Key = Key.Escape OrElse (e.Key = Key.W AndAlso Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) Then
                e.Handled = True
                Dim Msg As Object = PanMsg.Children(0)
                If TypeOf Msg IsNot MyMsgInput AndAlso TypeOf Msg IsNot MyMsgSelect AndAlso Msg.Btn3.Visibility = Visibility.Visible Then
                    Msg.Btn3_Click()
                ElseIf Msg.Btn2.Visibility = Visibility.Visible Then
                    Msg.Btn2_Click()
                Else
                    Msg.Btn1_Click()
                End If
            End If
            Return
        End If

        '==========================
        ' 在没有弹窗时：继续检查……
        '==========================

        'Ctrl + 1~7 快速切换主页面（保留既有快捷键映射；输入框内不抢键）
        If Keyboard.Modifiers.HasFlag(ModifierKeys.Control) AndAlso TypeOf Keyboard.FocusedElement IsNot TextBox Then
            Select Case e.Key
                Case Key.D1, Key.NumPad1
                    e.Handled = True
                    PageChange(PageType.Launch)
                    Return
                Case Key.D2, Key.NumPad2
                    e.Handled = True
                    PageChange(PageType.Brs)
                    Return
                Case Key.D3, Key.NumPad3
                    e.Handled = True
                    PageChange(PageType.Stock)
                    Return
                Case Key.D4, Key.NumPad4
                    e.Handled = True
                    PageChange(PageType.SatelliteMap)
                    Return
                Case Key.D5, Key.NumPad5
                    e.Handled = True
                    PageChange(PageType.Setup)
                    Return
                Case Key.D6, Key.NumPad6
                    e.Handled = True
                    PageChange(PageType.News)
                    Return
                Case Key.D7, Key.NumPad7
                    e.Handled = True
                    PageChange(PageType.Bank)
                    Return
            End Select
        End If
        '按 ESC 返回上一级
        If e.Key = Key.Escape Then TriggerPageBack()
        '更改隐藏版本可见性
        If e.Key = Key.F11 AndAlso PageCurrent = FormMain.PageType.InstanceSelect Then
            FrmSelectRight.ShowHidden = Not FrmSelectRight.ShowHidden
            LoaderFolderRun(McInstanceListLoader, McFolderSelected, LoaderFolderRunType.ForceRun, MaxDepth:=1, ExtraPath:="versions\")
            Return
        End If
        '更改功能隐藏可见性
        If e.Key = Key.F12 Then
            PageSetupUI.HiddenForceShow = Not PageSetupUI.HiddenForceShow
            If PageSetupUI.HiddenForceShow Then
                Hint("功能隐藏设置已暂时关闭！", HintType.Green)
            Else
                Hint("功能隐藏设置已重新开启！", HintType.Green)
            End If
            PageSetupUI.HiddenRefresh()
            Return
        End If
        '按 F5 刷新页面
        If e.Key = Key.F5 Then
            e.Handled = True
            If TypeOf PageLeft Is IRefreshable Then CType(PageLeft, IRefreshable).Refresh()
            If TypeOf PageRight Is IRefreshable Then CType(PageRight, IRefreshable).Refresh()
            Return
        End If
        'Ctrl + w 关闭程序
        If e.Key = Key.W AndAlso Keyboard.Modifiers.HasFlag(ModifierKeys.Control) Then
            EndProgram(True)
            Return
        End If
        '调用启动游戏
        If e.Key = Key.Enter AndAlso PageCurrent = FormMain.PageType.Launch Then
            If IsAprilEnabled AndAlso Not IsAprilGiveup Then
                Hint("木大！")
            Else
                FrmLaunchLeft.LaunchButtonClick()
            End If
        End If
    End Sub
    Private Sub FormMain_MouseDown(sender As Object, e As MouseButtonEventArgs) Handles Me.MouseDown
        '鼠标侧键返回上一级
        If FrmMain.PanMsg.Children.Count > 0 OrElse WaitingMyMsgBox.Any Then Return '弹窗中（#5513）
        If e.ChangedButton = MouseButton.XButton1 OrElse e.ChangedButton = MouseButton.XButton2 Then TriggerPageBack()
    End Sub
    Private Sub TriggerPageBack()
        If PageCurrent = PageType.Download AndAlso PageCurrentSub = PageSubType.DownloadInstall AndAlso FrmDownloadInstall.IsInSelectPage Then
            FrmDownloadInstall.ExitSelectPage()
        Else
            PageBack()
        End If
    End Sub

    '切回窗口
    Private Sub FormMain_Activated() Handles Me.Activated
        '切回窗口时自动刷新
        Try
            If PageCurrent = PageType.InstanceSetup AndAlso PageCurrentSub = PageSubType.InstanceMod Then
                'Mod 管理
                FrmInstanceMod.ReloadModList()
            ElseIf PageCurrent = PageType.InstanceSetup AndAlso (PageCurrentSub = PageSubType.InstanceSetup OrElse PageCurrentSub = PageSubType.InstanceExport) Then
                '更新当前选用的 Java
                PageInstanceLeft.ReloadCurrentJava()
            ElseIf PageCurrent = PageType.InstanceSelect Then
                '版本选择
                LoaderFolderRun(McInstanceListLoader, McFolderSelected, LoaderFolderRunType.RunOnUpdated, MaxDepth:=1, ExtraPath:="versions\")
            End If
        Catch ex As Exception
            Logger.Error(ex, "切回窗口时出错")
        End Try
        '读取剪贴板，自动加入联机房间
        Return 'TODO: 联机复活赛
        If PageLinkMain.LinkState <> PageLinkMain.LinkStates.Waiting Then Return '已启动联机
        If PageCurrent = PageType.Link Then Return '已在联机界面
        Dim Code = ClipboardGetText() : If Code Is Nothing Then Return '剪贴板无文本
        If Settings.Get(Of String)("LinkLastAutoJoinInviteCode") = Code Then Return
        If PageLinkMain.ValidateCodeFormat(Code) IsNot Nothing Then Return '不是邀请码
        Settings.Set("LinkLastAutoJoinInviteCode", Code)
        RunInThread(
        Sub()
            If MyMsgBox("嘿，是否使用复制的邀请码加入房间？", "加入联机房间", "加入", "取消") = 2 Then Return '防止弹窗阻碍主线程，所以必须放在工作线程
            RunInUi(
            Sub()
                PageLinkMain.Join(Code)
                ClipboardSet(Nothing, False)
            End Sub)
        End Sub)
    End Sub

    '文件拖放
    Private Sub HandleDrag(sender As Object, e As DragEventArgs)
        Try
            If e.Handled AndAlso (e.Effects <> DragDropEffects.None) Then Return
            e.Handled = True
            '缓存
            Static PrevData As IDataObject, PrevEffects As DragDropEffects
            If e.Data Is PrevData Then
                e.Effects = PrevEffects
                Return
            End If
            '确定拖放效果
            e.Effects = DragDropEffects.None
            If e.Data.GetDataPresent(DataFormats.Text) Then
                Dim Str As String = e.Data.GetData(DataFormats.Text)
                If Str.StartsWithF("authlib-injector:yggdrasil-server:") Then
                    e.Effects = DragDropEffects.Copy
                ElseIf Str.StartsWithF("file:///") Then
                    e.Effects = DragDropEffects.Copy
                End If
            ElseIf e.Data.GetDataPresent(DataFormats.FileDrop) Then
                Dim Files As String() = e.Data.GetData(DataFormats.FileDrop)
                If Files IsNot Nothing AndAlso Files.Length > 0 Then
                    e.Effects = DragDropEffects.Link
                End If
            End If
            PrevData = e.Data
            PrevEffects = e.Effects
            Logger.Info($"设置拖放类型：{e.Effects}")
        Catch ex As Exception
            Logger.Error(ex, "处理拖放时出错")
        End Try
    End Sub
    Private Sub FrmMain_Drop(sender As Object, e As DragEventArgs) Handles Me.Drop
        Try
            ShowWindowToTop()
            If e.Data.GetDataPresent(DataFormats.Text) Then
                '获取文本
                Try
                    Dim Str As String = e.Data.GetData(DataFormats.Text)
                    Logger.Info($"接受文本拖拽：{Str}")
                    If Str.StartsWithF("authlib-injector:yggdrasil-server:") Then
                        'Authlib 拖拽
                        e.Handled = True
                        e.Effects = DragDropEffects.Copy
                        Dim AuthlibServer As String = StringUtils.FormUrlUnescape(Str.Substring("authlib-injector:yggdrasil-server:".Length))
                        Logger.Info($"Authlib 拖拽：{AuthlibServer}")
                        If Not String.IsNullOrEmpty(New ValidateHttp().Validate(AuthlibServer)) Then
                            Hint($"输入的 Authlib 验证服务器不符合网址格式（{AuthlibServer}）！", HintType.Red)
                            Return
                        End If
                        Dim Target = If(PageCurrent = PageType.InstanceSetup, PageInstanceLeft.Instance, McInstanceSelected)
                        If Target Is Nothing Then
                            Hint("请先下载游戏，再设置第三方登录！", HintType.Red)
                            Return
                        End If
                        If AuthlibServer = "https://littleskin.cn/api/yggdrasil" Then
                            'LittleSkin
                            If MyMsgBox($"是否要在版本 {Target.Name} 中开启 LittleSkin 登录？" & vbCrLf &
                                        "你可以在 版本设置 → 设置 → 服务器选项 中修改登录方式。", "第三方登录开启确认", "确定", "取消") = 2 Then
                                Return
                            End If
                            Settings.Set("VersionServerLogin", 4, Instance:=Target)
                            Settings.Set("VersionServerAuthServer", "https://littleskin.cn/api/yggdrasil", Instance:=Target)
                            Settings.Set("VersionServerAuthRegister", "https://littleskin.cn/auth/register", Instance:=Target)
                            Settings.Set("VersionServerAuthName", "LittleSkin 登录", Instance:=Target)
                        Else
                            '第三方 Authlib 服务器
                            If MyMsgBox($"是否要在版本 {Target.Name} 中开启第三方登录？" & vbCrLf &
                                        $"登录服务器：{AuthlibServer}" & vbCrLf & vbCrLf &
                                        "你可以在 版本设置 → 设置 → 服务器选项 中修改登录方式。", "第三方登录开启确认", "确定", "取消") = 2 Then
                                Return
                            End If
                            Settings.Set("VersionServerLogin", 4, Instance:=Target)
                            Settings.Set("VersionServerAuthServer", AuthlibServer, Instance:=Target)
                            Settings.Set("VersionServerAuthRegister", AuthlibServer.Replace("api/yggdrasil", "auth/register"), Instance:=Target)
                            Settings.Set("VersionServerAuthName", "", Instance:=Target)
                        End If
                        If PageCurrent = PageType.InstanceSetup AndAlso PageCurrentSub = PageSubType.InstanceSetup Then
                            '正在服务器选项页，需要刷新设置项显示
                            FrmInstanceSetup.Reload()
                        ElseIf PageCurrent = PageType.Launch Then
                            '正在主页，需要刷新左边栏
                            FrmLaunchLeft.RefreshPage(True, False)
                        End If
                    ElseIf Str.StartsWithF("file:///") Then
                        '文件拖拽（例如从浏览器下载窗口拖入）
                        Dim FilePath = Net.WebUtility.UrlDecode(Str).Substring("file:///".Length).Replace("/", "\")
                        e.Handled = True
                        e.Effects = DragDropEffects.Copy
                        FileDrag(New List(Of String) From {FilePath})
                    End If
                Catch ex As Exception
                    Logger.Warn(ex, "无法接取文本拖拽事件")
                    Return
                End Try
            ElseIf e.Data.GetDataPresent(DataFormats.FileDrop) Then
                '获取文件并检查
                Dim FilePathRaw = e.Data.GetData(DataFormats.FileDrop)
                If FilePathRaw Is Nothing Then '#2690
                    Hint("请将文件解压后再拖入！", HintType.Red)
                    Return
                End If
                e.Handled = True
                e.Effects = DragDropEffects.Link
                FileDrag(CType(FilePathRaw, IEnumerable(Of String)))
            End If
        Catch ex As Exception
            Logger.Error(ex, "接取拖拽事件失败")
        End Try
    End Sub
    Private Sub FileDrag(FilePathList As IEnumerable(Of String))
        RunInNewThread(
        Sub()
            Dim FilePath As String = FilePathList.First
            Logger.Warn($"接受文件拖拽：{FilePath}{If(FilePathList.Any, $" 等 {FilePathList.Count} 个文件", "")}")
            '基础检查
            If DirectoryUtils.Exists(FilePathList.First) AndAlso Not FileUtils.Exists(FilePathList.First) Then
                Hint("请拖入一个文件，而非文件夹！", HintType.Red)
                Return
            ElseIf Not FileUtils.Exists(FilePathList.First) Then
                Hint("拖入的文件不存在：" & FilePathList.First, HintType.Red)
                Return
            End If
            '多文件拖拽
            Dim PathList As List(Of String) = FilePathList.ToList()
            If FilePathList.Count > 1 Then
                '必须要求全部为 jar 文件
                For Each File In PathList
                    If Not {"jar", "litemod", "disabled", "old"}.Contains(File.AfterLast(".").Lower) Then
                        Hint("一次请只拖入一个文件！", HintType.Red)
                        Return
                    End If
                Next
            End If
            '主页
            Dim Extension As String = FilePath.AfterLast(".").Lower
            If Extension = "xaml" Then
                Logger.Info("文件后缀为 XAML，作为主页加载")
                If FileUtils.Exists(Paths.Base & "BRCL\Custom.xaml") Then
                    If MyMsgBox("已存在一个主页文件，是否要将它覆盖？", "覆盖确认", "覆盖", "取消") = 2 Then
                        Return
                    End If
                End If
                FileUtils.Copy(FilePath, Paths.Base & "BRCL\Custom.xaml")
                RunInUi(
                Sub()
                    Settings.Set("UiCustomType", 1)
                    FrmLaunchRight.ForceRefresh()
                    Hint("已加载主页自定义文件！", HintType.Green)
                End Sub)
                Return
            End If
            '安装 Mod
            If PageInstanceMod.InstallMods(PathList) Then Return
            '安装整合包
            If {"zip", "rar", "mrpack"}.Any(Function(t) t = Extension) Then '部分压缩包是 zip 格式但后缀为 rar，总之试一试
                Logger.Info("文件为压缩包，尝试作为整合包安装")
                Try
                    ModpackInstall(FilePath)
                    RunInUi(Sub() FrmMain.PageChange(FormMain.PageType.TaskManager))
                    Return
                Catch ex As Exception
                    If ex.IsCanceled Then Return
                    '安装失败，继续往后尝试
                End Try
            End If
            'RAR 处理
            If Extension = "rar" Then
                Hint("BRCL 无法处理 rar 格式的压缩包，请在解压后重新压缩为 zip 格式再试！")
                Return
            End If
            '错误报告分析
            Try
                Logger.Info("尝试进行错误报告分析")
                Dim Analyzer As New CrashAnalyzer
                Analyzer.Import(FilePath)
                If Not Analyzer.Prepare() Then Exit Try
                Analyzer.Analyze()
                Analyzer.Output(True, New List(Of String))
                Return
            Catch ex As Exception
                Logger.Error(ex, "自主错误报告分析失败")
            End Try
            '未知操作
            Hint("BRCL 无法确定应当执行的文件拖拽操作……")
        End Sub, "文件拖拽")
    End Sub

    '接受到 Windows 窗体事件
    Public IsSystemTimeChanged As Boolean = False
    Private Function WndProc(hwnd As IntPtr, msg As Integer, wParam As IntPtr, lParam As IntPtr, ByRef handled As Boolean) As IntPtr
        If msg = 30 Then
            Dim NowDate = Date.Now
            If NowDate.Date = ApplicationOpenTime.Date Then
                Logger.Info($"系统时间微调为：{NowDate.ToLongDateString} {NowDate.ToLongTimeString}")
                IsSystemTimeChanged = False
            Else
                Logger.Info($"系统时间修改为：{NowDate.ToLongDateString} {NowDate.ToLongTimeString}")
                IsSystemTimeChanged = True
            End If
        ElseIf msg = 400 * 16 + 2 Then
            Logger.Info($"收到置顶信息：{hwnd.ToInt64}")
            If Not IsWindowLoadFinished Then
                Logger.Info("窗口尚未加载完成，忽略置顶请求")
                Return IntPtr.Zero
            End If
            ShowWindowToTop()
            handled = True
        End If
        Return IntPtr.Zero
    End Function

    '窗口隐藏与置顶
    Private _Hidden As Boolean = False
    Public Property Hidden As Boolean
        Get
            Return _Hidden
        End Get
        Set(value As Boolean)
            If _Hidden = value Then Return
            _Hidden = value
            If value Then
                '隐藏
                Left -= 10000
                ShowInTaskbar = False
                Visibility = Visibility.Hidden
                Logger.Info($"窗口已隐藏，位置：({Left},{Top})")
            Else
                '取消隐藏
                If Left < -2000 Then Left += 10000
                ShowWindowToTop()
            End If
        End Set
    End Property
    ''' <summary>
    ''' 把当前窗口拖到最前面。
    ''' </summary>
    Public Sub ShowWindowToTop()
        RunInUi(
        Sub()
            '这一坨乱七八糟的，别改，改了指不定就炸了，自己电脑还复现不出来
            Visibility = Visibility.Visible
            ShowInTaskbar = True
            If WindowState = WindowState.Minimized Then WindowState = LastUnminimizedWindowState
            Hidden = False
            Topmost = True '偶尔 SetForegroundWindow 失效
            Topmost = False
            SetForegroundWindow(Handle)
            Focus()
            Logger.Info($"窗口已置顶，位置：({Left}, {Top}), {Width} x {Height}")
        End Sub)
    End Sub

#End Region

#Region "切换页面"

    '页面种类与属性
    ''' <summary>
    ''' 页面种类。
    ''' 该枚举在自定义事件中使用，是公开 API 的一部分。
    ''' </summary>
    Public Enum PageType
        ''' <summary>
        ''' 启动。
        ''' </summary>
        Launch = 0
        ''' <summary>
        ''' 下载。
        ''' </summary>
        Download = 1
        ''' <summary>
        ''' 联机。
        ''' </summary>
        Link = 2
        ''' <summary>
        ''' 设置。
        ''' </summary>
        Setup = 3
        ''' <summary>
        ''' 更多。
        ''' </summary>
        Other = 4
        ''' <summary>
        ''' 版本选择。这是一个副页面。
        ''' </summary>
        InstanceSelect = 5
        ''' <summary>
        ''' 任务管理。这是一个副页面。
        ''' </summary>
        TaskManager = 6
        ''' <summary>
        ''' 版本设置。这是一个副页面。
        ''' </summary>
        InstanceSetup = 7
        ''' <summary>
        ''' 社区资源详情。这是一个副页面。
        ''' </summary>
        ResourceDetail = 8
        ''' <summary>
        ''' 帮助详情。这是一个副页面。
        ''' </summary>
        HelpDetail = 9
        ''' <summary>
        ''' BRS 社区页。
        ''' </summary>
        Brs = 10
        ''' <summary>
        ''' 交易所页。
        ''' </summary>
        Stock = 11
        ''' <summary>服务器卫星地图。</summary>
        SatelliteMap = 12
        ''' <summary>服务器公告与动态。</summary>
        News = 13
        ''' <summary>团队银行。</summary>
        Bank = 14
    End Enum
    ''' <summary>
    ''' 次要页面种类。其数值必须与 StackPanel 中的下标一致。
    ''' 该枚举在自定义事件中使用，是公开 API 的一部分。
    ''' </summary>
    Public Enum PageSubType
        [Default] = 0
        DownloadInstall = 0
        DownloadMod = 2
        DownloadPack = 3
        DownloadDataPack = 4
        DownloadResourcePack = 5
        DownloadShader = 6
        SetupLaunch = 0
        SetupLink = 1
        SetupUI = 2
        SetupAbout = 3
        SetupSound = 4
        SetupSponsors = 5
        SetupChangelog = 6
        SetupPrivacy = 7
        LinkMain = 0
        OtherHelp = 0
        OtherAbout = 1
        OtherTest = 2
        InstanceOverall = 0
        InstanceSetup = 1
        InstanceMod = 2
        InstanceModDisabled = 3
        InstanceExport = 4
        InstanceSaves = 5
        InstanceResourcePacks = 6
        InstanceShaders = 7
        InstanceScreenshots = 8
        InstanceSchematics = 9
    End Enum
    ''' <summary>
    ''' 获取次级页面的名称。若并非次级页面则返回空字符串，故可以以此判断是否为次级页面。
    ''' </summary>
    Private Function PageNameGet(Stack As PageStackData) As String
        Select Case Stack.Page
            Case PageType.InstanceSelect
                Return "版本选择"
            Case PageType.TaskManager
                Return "任务管理"
            Case PageType.InstanceSetup
                Return "版本设置 - " & If(PageInstanceLeft.Instance Is Nothing, "未知版本", PageInstanceLeft.Instance.Name)
            Case PageType.ResourceDetail
                Return CType(Stack.Additional(0), ResourceProject).TranslatedName
            Case PageType.HelpDetail
                Return CType(Stack.Additional(0), HelpEntry).Title
            Case Else
                Return ""
        End Select
    End Function
    ''' <summary>
    ''' 刷新次级页面的名称。
    ''' </summary>
    Public Sub PageNameRefresh(Type As PageStackData)
        LabTitleInner.Text = PageNameGet(Type)
    End Sub
    ''' <summary>
    ''' 刷新次级页面的名称。
    ''' </summary>
    Public Sub PageNameRefresh()
        PageNameRefresh(PageCurrent)
    End Sub

    '页面状态存储
    ''' <summary>
    ''' 当前的主页面。
    ''' </summary>
    Public PageCurrent As PageStackData = PageType.Launch
    ''' <summary>
    ''' 上一个主页面。
    ''' </summary>
    Public PageLast As PageStackData = PageType.Launch
    ''' <summary>
    ''' 当前的子页面。
    ''' </summary>
    Public ReadOnly Property PageCurrentSub As PageSubType
        Get
            Select Case PageCurrent
                Case PageType.Download
                    If FrmDownloadLeft Is Nothing Then FrmDownloadLeft = New PageDownloadLeft
                    Return FrmDownloadLeft.PageID
                Case PageType.Setup
                    If FrmSetupLeft Is Nothing Then FrmSetupLeft = New PageSetupLeft
                    Return FrmSetupLeft.PageID
                Case PageType.Other
                    If FrmOtherLeft Is Nothing Then FrmOtherLeft = New PageOtherLeft
                    Return FrmOtherLeft.PageID
                Case PageType.InstanceSetup
                    If FrmInstanceLeft Is Nothing Then FrmInstanceLeft = New PageInstanceLeft
                    Return FrmInstanceLeft.PageID
                Case Else
                    Return 0 '没有子页面
            End Select
        End Get
    End Property
    ''' <summary>
    ''' 上层页面的编号堆栈，用于返回。
    ''' </summary>
    Public PageStack As New List(Of PageStackData)
    Public Class PageStackData

        Public Page As PageType
        Public Additional As Object

        Public Overrides Function Equals(other As Object) As Boolean
            If other Is Nothing Then Return False
            If TypeOf other Is PageStackData Then
                Dim PageOther As PageStackData = other
                If Page <> PageOther.Page Then Return False
                If Additional Is Nothing Then
                    Return PageOther.Additional Is Nothing
                Else
                    Return PageOther.Additional IsNot Nothing AndAlso Additional.Equals(PageOther.Additional)
                End If
            ElseIf TypeOf other Is Integer Then
                If Page <> other Then Return False
                Return Additional Is Nothing
            Else
                Return False
            End If
        End Function
        Public Shared Operator =(left As PageStackData, right As PageStackData) As Boolean
            Return EqualityComparer(Of PageStackData).Default.Equals(left, right)
        End Operator
        Public Shared Operator <>(left As PageStackData, right As PageStackData) As Boolean
            Return Not left = right
        End Operator
        Public Shared Widening Operator CType(Value As PageType) As PageStackData
            Return New PageStackData With {.Page = Value}
        End Operator
        Public Shared Widening Operator CType(Value As PageStackData) As PageType
            Return Value.Page
        End Operator
    End Class
    Public PageLeft As MyPageLeft
    Private _PageRight As MyPageRight
    Public Property PageRight As MyPageRight
        Get
            Return _PageRight
        End Get
        Set(value As MyPageRight)
            If value Is _PageRight Then Return
            If _PageRight IsNot Nothing Then RemoveHandler _PageRight.Loaded, AddressOf WorkspacePageLoaded
            _PageRight = value
            If value Is Nothing Then Return
            AddHandler value.Loaded, AddressOf WorkspacePageLoaded
            If value.IsLoaded Then RefreshWorkspaceScene(value)
        End Set
    End Property

    'A single canvas spans the workspace. Change its motif only when the new page
    'is mounted, so an outgoing page never receives the next page's background.
    Private Sub WorkspacePageLoaded(sender As Object, e As RoutedEventArgs)
        If sender Is _PageRight Then RefreshWorkspaceScene(_PageRight)
    End Sub
    Private Sub RefreshWorkspaceScene(page As MyPageRight)
        If WorkspaceBackdrop Is Nothing OrElse page Is Nothing Then Return
        WorkspaceBackdrop.SceneKey = page.GetType().Name
    End Sub

    '引发实际页面切换的入口
    Private IsChangingPage As Boolean = False
    ''' <summary>
    ''' 切换页面，并引起对应选择 UI 的改变。
    ''' </summary>
    Public Sub PageChange(Stack As PageStackData, Optional SubType As PageSubType = PageSubType.Default)
        'BRCL：下载页与更多页已被移除，任何指向它们的跳转都直接忽略
        If Stack.Page = PageType.Download OrElse Stack.Page = PageType.Other Then Return
        If PageNameGet(Stack) = "" Then
            '切换到主页面
            PageChangeExit()
            IsChangingPage = True '防止下面的勾选直接触发了 PageChangeActual
            'BRCL：导航按钮已不连续，按 Tag 查找对应按钮，不能再按索引取
            For Each Child As Object In PanTitleSelect.Children
                Dim Target As Integer
                If TypeOf Child Is MyRadioButton AndAlso Integer.TryParse(CStr(CType(Child, MyRadioButton).Tag), Target) AndAlso Target = CInt(Stack.Page) Then
                    CType(Child, MyRadioButton).SetChecked(True, True, PageNameGet(PageCurrent) = "")
                    Exit For
                End If
            Next
            IsChangingPage = False
            Select Case Stack.Page
                Case PageType.Download
                    If FrmDownloadLeft Is Nothing Then FrmDownloadLeft = New PageDownloadLeft
                    CType(FrmDownloadLeft.PanItem.Children(SubType), MyListItem).SetChecked(True, True, Stack = PageCurrent)
                Case PageType.Setup
                    If FrmSetupLeft Is Nothing Then FrmSetupLeft = New PageSetupLeft
                    'BRCL：左栏项目已不连续，按 Tag 查找，不能再按索引取
                    For Each Child As Object In FrmSetupLeft.PanItem.Children
                        Dim Target As Integer
                        If TypeOf Child Is MyListItem AndAlso Integer.TryParse(CStr(CType(Child, MyListItem).Tag), Target) AndAlso Target = CInt(SubType) Then
                            CType(Child, MyListItem).SetChecked(True, True, Stack = PageCurrent)
                            Exit For
                        End If
                    Next
                Case PageType.Other
                    If FrmOtherLeft Is Nothing Then FrmOtherLeft = New PageOtherLeft
                    CType(FrmOtherLeft.PanItem.Children(SubType), MyListItem).SetChecked(True, True, Stack = PageCurrent)
            End Select
            PageChangeActual(Stack, SubType)
        Else
            '切换到次页面
            Select Case Stack.Page
                Case PageType.InstanceSetup
                    If FrmInstanceLeft Is Nothing Then FrmInstanceLeft = New PageInstanceLeft
                    CType(FrmInstanceLeft.PanItem.Children(SubType), MyListItem).SetChecked(True, True, Stack = PageCurrent)
            End Select
            PageChangeActual(Stack, SubType)
        End If
    End Sub
    ''' <summary>
    ''' 通过点击导航栏改变页面。
    ''' </summary>
    Private Sub BtnTitleSelect_Click(sender As MyRadioButton, raiseByMouse As Boolean) Handles BtnTitleSelect0.Check, BtnTitleSelect2.Check, BtnTitleSelect3.Check, BtnTitleSelectBrs.Check, BtnTitleSelectStock.Check, BtnTitleSelectMap.Check, BtnTitleSelectNews.Check, BtnTitleSelectBank.Check
        If IsChangingPage Then Return
        PageChangeActual(Val(sender.Tag))
    End Sub
    ''' <summary>
    ''' 通过点击返回按钮或手动触发返回来改变页面。
    ''' </summary>
    Public Sub PageBack() Handles BtnTitleInner.Click
        If PageStack.Any() Then
            PageChangeActual(PageStack(0))
        Else
            PageChange(PageType.Launch)
        End If
    End Sub

    '实际处理页面切换
    ''' <summary>
    ''' 切换现有页面的实际方法。
    ''' </summary>
    Private Sub PageChangeActual(Stack As PageStackData, Optional SubType As PageSubType = -1)
        If PageCurrent = Stack AndAlso (PageCurrentSub = SubType OrElse SubType = -1) Then Return
        AniControlEnabled += 1
        Try
            '移除浏览器页前把键盘焦点还给主窗体，避免后续快捷键落入已隐藏的原生浏览器。
            If TypeOf PageRight Is PageStock OrElse TypeOf PageRight Is PageMap Then Keyboard.Focus(Me)

#Region "子页面处理"
            Dim PageName As String = PageNameGet(Stack)
            If PageName = "" Then
                '即将切换到一个顶级页面
                PageChangeExit()
            Else
                '即将切换到一个子页面
                If PageStack.Any Then
                    '子页面 → 另一个子页面，更新
                    AniStart({
                        AaOpacity(LabTitleInner, -LabTitleInner.Opacity, 130),
                        AaCode(Sub() LabTitleInner.Text = PageName,, True),
                        AaOpacity(LabTitleInner, 1, 150, 30)
                    }, "FrmMain Titlebar SubLayer")
                    If PageStack.Contains(Stack) Then
                        '返回到更上层的子页面
                        Do While PageStack.Contains(Stack)
                            PageStack.RemoveAt(0)
                        Loop
                    Else
                        '进入更深层的子页面
                        PageStack.Insert(0, PageCurrent)
                    End If
                Else
                    '主页面 → 子页面，进入
                    PanTitleInner.Visibility = Visibility.Visible
                    PanTitleMain.IsHitTestVisible = False
                    PanTitleInner.IsHitTestVisible = True
                    PageNameRefresh(Stack)
                    AniStart({
                        AaOpacity(PanTitleMain, -PanTitleMain.Opacity, 150),
                        AaX(PanTitleMain, 12 - PanTitleMain.Margin.Left, 150,, New AniEaseInFluent(AniEasePower.Weak)),
                        AaOpacity(PanTitleInner, 1 - PanTitleInner.Opacity, 150, 200),
                        AaX(PanTitleInner, -PanTitleInner.Margin.Left, 350, 200, New AniEaseOutBack),
                        AaCode(Sub() PanTitleMain.Visibility = Visibility.Collapsed,, True)
                    }, "FrmMain Titlebar FirstLayer")
                    PageStack.Insert(0, PageCurrent)
                End If
            End If
#End Region

#Region "实际更改页面框架 UI"
            PageLast = PageCurrent
            PageCurrent = Stack
            UpdateRail()
            Select Case Stack.Page
                Case PageType.Launch '启动
                    PageChangeAnim(FrmLaunchLeft, FrmLaunchRight)
                Case PageType.Link '联机
                    If FrmLinkMain Is Nothing Then FrmLinkMain = New PageLinkMain
                    PageChangeAnim(EmptyPageLeft, FrmLinkMain)
                Case PageType.Brs 'BRS 社区
                    If FrmBrs Is Nothing Then FrmBrs = New PageBRS
                    PageChangeAnim(EmptyPageLeft, FrmBrs)
                Case PageType.Stock '交易所
                    If FrmStock Is Nothing Then FrmStock = New PageStock
                    PageChangeAnim(EmptyPageLeft, FrmStock)
                Case PageType.SatelliteMap '卫星图
                    If FrmMap Is Nothing Then FrmMap = New PageMap
                    PageChangeAnim(EmptyPageLeft, FrmMap)
                Case PageType.News '新闻
                    If FrmNews Is Nothing Then FrmNews = New PageNews
                    PageChangeAnim(EmptyPageLeft, FrmNews)
                Case PageType.Bank '银行
                    If FrmBank Is Nothing Then FrmBank = New PageBank
                    PageChangeAnim(EmptyPageLeft, FrmBank)
                Case PageType.Setup '设置
                    If FrmSetupLeft Is Nothing Then FrmSetupLeft = New PageSetupLeft
                    PageChangeAnim(FrmSetupLeft, FrmSetupLeft.PageGet(SubType))
                Case PageType.InstanceSelect '版本选择
                    If FrmSelectLeft Is Nothing Then FrmSelectLeft = New PageSelectLeft
                    If FrmSelectRight Is Nothing Then FrmSelectRight = New PageSelectRight
                    PageChangeAnim(FrmSelectLeft, FrmSelectRight)
                Case PageType.TaskManager '任务管理
                    If FrmSpeedLeft Is Nothing Then FrmSpeedLeft = New PageSpeedLeft
                    If FrmSpeedRight Is Nothing Then FrmSpeedRight = New PageSpeedRight
                    PageChangeAnim(FrmSpeedLeft, FrmSpeedRight)
                Case PageType.InstanceSetup '版本设置
                    If FrmInstanceLeft Is Nothing Then FrmInstanceLeft = New PageInstanceLeft
                    PageChangeAnim(FrmInstanceLeft, FrmInstanceLeft.PageGet(SubType))
                Case PageType.ResourceDetail '社区资源详情
                    If FrmDownloadResourceDetail Is Nothing Then FrmDownloadResourceDetail = New PageDownloadResourceDetail
                    PageChangeAnim(EmptyPageLeft, FrmDownloadResourceDetail)
                Case PageType.HelpDetail '帮助详情
                    PageChangeAnim(EmptyPageLeft, Stack.Additional(1))
            End Select
#End Region

#Region "设置为最新状态"
            BtnExtraDownload.ShowRefresh()
            BtnExtraApril.ShowRefresh()
#End Region

            Logger.Info($"切换主要页面：{Stack.Page}, {SubType}")
        Catch ex As Exception
            Logger.Error(ex, $"切换主要页面失败（ID {PageCurrent.Page}）")
        Finally
            AniControlEnabled -= 1
        End Try
    End Sub
    Private Sub PageChangeAnim(TargetLeft As FrameworkElement, TargetRight As FrameworkElement)
        AniStop("FrmMain LeftChange")
        AniStop("PageLeft PageChange") '停止左边栏变更导致的右页面切换动画，防止它与本动画一起触发多次 PageOnEnter
        AniControlEnabled += 1
        '清除新页面关联性
        If TargetLeft.Parent IsNot Nothing Then TargetLeft.SetValue(ContentPresenter.ContentProperty, Nothing)
        If TargetRight IsNot Nothing AndAlso TargetRight.Parent IsNot Nothing Then TargetRight.SetValue(ContentPresenter.ContentProperty, Nothing)
        PageLeft = TargetLeft
        PageRight = TargetRight
        '触发页面通用动画
        If PanMainLeft.Child IsNot EmptyPageLeft Then CType(PanMainLeft.Child, MyPageLeft).TriggerHideAnimation()
        CType(PanMainRight.Child, MyPageRight).PageOnExit()
        AniControlEnabled -= 1
        '执行动画
        AniStart({
            AaCode(
            Sub()
                AniControlEnabled += 1
                '把新页面添加进容器
                ConfigurePageLayout(PageCurrent.Page = PageType.Launch)
                PanMainLeft.Child = PageLeft
                PageLeft.Opacity = 0
                PanMainLeft.Background = Nothing
                AniControlEnabled -= 1
                RunInUi(Sub() PanMainLeft_Resize(PanMainLeft.ActualWidth), True)
            End Sub, 110),
            AaCode(
            Sub()
                '延迟触发页面通用动画，以使得在 Loaded 事件中加载的控件得以处理
                PageLeft.Opacity = 1
                If PageLeft IsNot EmptyPageLeft Then PageLeft.TriggerShowAnimation()
            End Sub, 30, True)
        }, "FrmMain PageChangeLeft")
        AniStart({
            AaCode(
            Sub()
                AniControlEnabled += 1
                CType(PanMainRight.Child, MyPageRight).PageOnForceExit()
                '把新页面添加进容器
                PanMainRight.Child = PageRight
                PageRight.Opacity = 0
                PanMainRight.Background = Nothing
                AniControlEnabled -= 1
                RunInUi(Sub() BtnExtraBack.ShowRefresh(), True)
            End Sub, 110),
            AaCode(
            Sub()
                '延迟触发页面通用动画，以使得在 Loaded 事件中加载的控件得以处理
                PageRight.Opacity = 1
                PageRight.PageOnEnter()
            End Sub, 30, True)
        }, "FrmMain PageChangeRight")
    End Sub

    '启动页以场景为中心，玩家操作停靠右侧；其它页面保留二级导航结构。
    Private Sub ConfigurePageLayout(IsLaunch As Boolean)
        Dim PlayerHost = CType(PanMainLeft.Parent, Border)
        Dim ContentHost = CType(PanMainRight.Parent, Border)
        Grid.SetColumn(PlayerHost, If(IsLaunch, 1, 0))
        Grid.SetColumn(ContentHost, If(IsLaunch, 0, 1))
        PanMain.ColumnDefinitions(0).Width = If(IsLaunch, New GridLength(1, GridUnitType.Star), GridLength.Auto)
        PanMain.ColumnDefinitions(1).Width = If(IsLaunch, GridLength.Auto, New GridLength(1, GridUnitType.Star))
        PanLeft.HorizontalAlignment = If(IsLaunch, HorizontalAlignment.Right, HorizontalAlignment.Left)
        PlayerHost.BorderThickness = If(IsLaunch, New Thickness(1, 0, 0, 0), New Thickness(0))
        PlayerHost.SetResourceReference(Border.BorderBrushProperty, "ColorBrushCardLine")
        If IsLaunch Then
            PlayerHost.SetResourceReference(Border.BackgroundProperty, "ColorBrush7")
        Else
            PlayerHost.Background = Nothing
        End If
    End Sub
    ''' <summary>
    ''' 退出子界面。
    ''' </summary>
    Private Sub PageChangeExit()
        If PageStack.Any Then
            '子页面 → 主页面，退出
            PanTitleMain.Visibility = Visibility.Visible
            PanTitleMain.IsHitTestVisible = True
            PanTitleInner.IsHitTestVisible = False
            AniStart({
                AaOpacity(PanTitleInner, -PanTitleInner.Opacity, 150),
                AaX(PanTitleInner, -18 - PanTitleInner.Margin.Left, 150,, New AniEaseInFluent),
                AaOpacity(PanTitleMain, 1 - PanTitleMain.Opacity, 150, 200),
                AaX(PanTitleMain, -PanTitleMain.Margin.Left, 350, 200, New AniEaseOutBack(AniEasePower.Weak)),
                AaCode(Sub() PanTitleInner.Visibility = Visibility.Collapsed,, True)
            }, "FrmMain Titlebar FirstLayer")
            PageStack.Clear()
        Else
            '主页面 → 主页面，无事发生
        End If
    End Sub

    '左边栏改变
    Private Sub PanMainLeft_SizeChanged(sender As Object, e As SizeChangedEventArgs) Handles PanMainLeft.SizeChanged
        If Not e.WidthChanged Then Return
        PanMainLeft_Resize(e.NewSize.Width)
    End Sub
    Private Sub PanMainLeft_Resize(NewWidth As Double)
        Dim Delta As Double = NewWidth - RectLeftBackground.Width
        If Math.Abs(Delta) > 0.1 AndAlso AniControlEnabled = 0 Then
            If PanMain.Opacity < 0.1 Then PanMainLeft.IsHitTestVisible = False '避免左边栏指向背景未能完美覆盖左边栏
            If NewWidth > 0 Then
                '宽度足够，显示
                AniStart({
                    AaWidth(RectLeftBackground, NewWidth - RectLeftBackground.Width, 180,, New AniEaseOutFluent(AniEasePower.ExtraStrong)),
                    AaOpacity(RectLeftShadow, 1 - RectLeftShadow.Opacity, 180),
                    AaCode(Sub() PanMainLeft.IsHitTestVisible = True, 150)
                }, "FrmMain LeftChange", True)
            Else
                '宽度不足，隐藏
                AniStart({
                    AaWidth(RectLeftBackground, -RectLeftBackground.Width, 180,, New AniEaseOutFluent),
                    AaOpacity(RectLeftShadow, -RectLeftShadow.Opacity, 180),
                    AaCode(Sub() PanMainLeft.IsHitTestVisible = True, 150)
                }, "FrmMain LeftChange", True)
            End If
        Else
            RectLeftBackground.Width = NewWidth
            PanMainLeft.IsHitTestVisible = True
            AniStop("FrmMain LeftChange")
        End If
    End Sub

#End Region

#Region "控件拖动"

    '在时钟中调用，使得即使鼠标在窗口外松开，也可以释放控件
    Public Sub DragTick()
        If DragControl Is Nothing Then Return
        If Not Mouse.LeftButton = MouseButtonState.Pressed Then
            DragStop()
        End If
    End Sub
    '在鼠标移动时调用，以改变 Slider 位置
    Public Sub DragDoing() Handles PanBack.MouseMove
        If DragControl Is Nothing Then Return
        If Mouse.LeftButton = MouseButtonState.Pressed Then
            DragControl.DragDoing()
        Else
            DragStop()
        End If
    End Sub
    Public Sub DragStop()
        '存在其他线程调用的可能性，因此需要确保在 UI 线程运行
        RunInUi(Sub()
                    If DragControl Is Nothing Then Return
                    Dim Control = DragControl
                    DragControl = Nothing
                    Control.DragStop() '控件会在该事件中判断 DragControl，所以得放在后面
                End Sub)
    End Sub

#End Region

#Region "附加按钮"

    '音乐
    Private Sub BtnExtraMusic_Click(sender As Object, e As EventArgs) Handles BtnExtraMusic.Click
        MusicControlPause()
    End Sub
    Private Sub BtnExtraMusic_RightClick(sender As Object, e As EventArgs) Handles BtnExtraMusic.RightClick
        MusicControlNext()
    End Sub

    '任务管理
    Private Sub BtnExtraDownload_Click(sender As Object, e As EventArgs) Handles BtnExtraDownload.Click
        PageChange(PageType.TaskManager)
    End Sub
    Private Function BtnExtraDownload_ShowCheck() As Boolean
        Return HasDownloadingTask() AndAlso Not PageCurrent = PageType.TaskManager
    End Function

    '投降
    Public Sub AprilGiveup() Handles BtnExtraApril.Click
        If IsAprilEnabled AndAlso Not IsAprilGiveup Then
            Hint("=D", HintType.Green)
            IsAprilGiveup = True
            FrmLaunchLeft.AprilScaleTrans.ScaleX = 1
            FrmLaunchLeft.AprilScaleTrans.ScaleY = 1
            BtnExtraApril.ShowRefresh()
        End If
    End Sub
    Public Function BtnExtraApril_ShowCheck() As Boolean
        Return IsAprilEnabled AndAlso Not IsAprilGiveup AndAlso PageCurrent = PageType.Launch
    End Function

    '关闭 Minecraft
    Public Sub BtnExtraShutdown_Click() Handles BtnExtraShutdown.Click
        Try
            If McLaunchLoaderReal IsNot Nothing Then McLaunchLoaderReal.Cancel()
            For Each Watcher In McWatcherList
                Watcher.Kill()
            Next
            Hint("已关闭运行中的 Minecraft！", HintType.Green)
        Catch ex As Exception
            Logger.Error(ex, "强制关闭所有 Minecraft 失败")
        End Try
    End Sub
    Public Function BtnExtraShutdown_ShowCheck() As Boolean
        Return HasRunningMinecraft
    End Function

    ''' <summary>
    ''' 返回顶部。
    ''' </summary>
    Public Sub BackToTop() Handles BtnExtraBack.Click
        Dim RealScroll As MyScrollViewer = BtnExtraBack_GetRealChild()
        If RealScroll IsNot Nothing Then
            RealScroll.PerformVerticalOffsetDelta(-RealScroll.VerticalOffset)
        Else
            Logger.Error("无法返回顶部，未找到合适的 RealScroll", LogBehavior.Toast)
        End If
    End Sub
    Private Function BtnExtraBack_ShowCheck() As Boolean
        Dim RealScroll As MyScrollViewer = BtnExtraBack_GetRealChild()
        Return RealScroll IsNot Nothing AndAlso RealScroll.Visibility = Visibility.Visible AndAlso RealScroll.VerticalOffset > Height + If(BtnExtraBack.Show, 0, 700)
    End Function
    Private Function BtnExtraBack_GetRealChild() As MyScrollViewer
        If PanMainRight.Child Is Nothing OrElse TypeOf PanMainRight.Child IsNot MyPageRight Then Return Nothing
        Dim Page As MyPageRight = PanMainRight.Child
        Return Page.FindName(Page.PanScroll)
    End Function

#End Region

    '愚人节鼠标位置
    Public lastMouseArg As MouseEventArgs = Nothing
    Private Sub FormMain_MouseMove(sender As Object, e As MouseEventArgs) Handles Me.MouseMove
        lastMouseArg = e
    End Sub

End Class
