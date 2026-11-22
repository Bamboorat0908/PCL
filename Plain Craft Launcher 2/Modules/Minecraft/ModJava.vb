Public Module ModJava

    ''' <summary>
    ''' 初始化 Java 列表。
    ''' 若没有任何可用的 Java，则触发搜索。
    ''' </summary>
    Public Sub JavaInit()
        Try
            '从老版本迁移
            If Configs.JavaConfigVersion.Get() = 0 Then
                Try
                    'Java 列表
                    Logger.Warn("正在从老版本迁移 Java 列表")
                    Dim OldJavaList = Settings.Get(Of String)("LaunchArgumentJavaAll").DeserializeJson(Of JArray).
                                      Select(Function(JavaObject) New Java(JavaObject("Path").ToString)).ToList()
                    OldJavaList = JavaUtils.SortAsync(OldJavaList).Run()
                    Logger.Info($"已从老版本设置中发现 {OldJavaList.Count} 个 Java")
                    '强制选择的 Java
                    Dim SelectedJavaSetting = Settings.Get(Of String)("LaunchArgumentJavaSelect")
                    Dim SelectedJava = If(SelectedJavaSetting.StartsWithF("{"), SelectedJavaSetting.DeserializeJson(Of JObject)()("Path").ToString, "")
                    Dim Match = OldJavaList.FirstOrDefault(Function(j) j.Folder.Equals(SelectedJava, StringComparison.OrdinalIgnoreCase))
                    If Match IsNot Nothing Then
                        OldJavaList.Remove(Match)
                        OldJavaList.Insert(0, Match)
                        Logger.Info($"已将老版本设置中强制选择的 Java 置顶：{Match}")
                    End If
                    Configs.JavaList.Set(New ConcurrentList(Of Java)(OldJavaList))
                    '完成迁移
                    Configs.JavaConfigVersion.Set(Versions.JavaConfigVersion)
                Catch ex As Exception
                    Logger.Error(ex, "从老版本 BRCL 迁移 Java 列表失败")
                End Try
            End If
            '读取缓存
            Dim List = Configs.JavaList.Get()
            If List.Any Then
                Logger.Info($"共有 {List.Count} 个可用的 Java：{vbCrLf}- {List.Select(Function(j) j.ToString).Join(vbCrLf + "- ")}")
                Return
            End If
            '如果没有 Java，则触发搜索
            Logger.Warn("初始化未找到可用的 Java，将自动触发搜索")
            JavaListRefreshWorker.Start()
        Catch ex As Exception
            Logger.Error(ex, "初始化 Java 列表失败")
            Configs.JavaList.Reset()
        End Try
    End Sub

    ''' <summary>
    ''' 搜索并获取所有可用的 Java，检查所有 Java 的可用性，写入设置，并在结束后更新设置页面显示。
    ''' </summary>
    Public WithEvents JavaListRefreshWorker As New RedoableWorker(Sub(c, p) JavaUtils.RefreshListAsync(c, p).Run())

#Region "UI 事件"

    ''' <summary>
    ''' 更新设置页面的 Java 列表显示。
    ''' </summary>
    Public Sub UpdateJavaLists() Handles JavaListRefreshWorker.Started, JavaListRefreshWorker.Stopped
        RunInUi(
        Sub()
            FrmSetupLaunch?.UpdateJavaList()
            FrmInstanceSetup?.UpdateJavaList()
        End Sub)
    End Sub

    ''' <summary>
    ''' 手动触发导入 Java。
    ''' </summary>
    Public Sub ManuallyImportJava()
        '选择
        Dim NewJavaPath As String = Dialogs.SelectFile("选择 java.exe（可能在 bin 文件夹中）", False, filter:="java.exe|java.exe").FirstOrDefault()
        If String.IsNullOrEmpty(NewJavaPath) Then Return
        '验证 Java 可用
        Dim NewJava As New Java(PathUtils.RemoveLastPart(NewJavaPath))
        If Not NewJava.CheckAsync().Run() Then
            Hint("该 Java 存在异常，无法使用", HintType.Red)
            Return
        End If
        '不再移除它
        Configs.JavaRemovedList.Edit(Sub(ByRef List) List.RemoveIf(Function(f) f.Equals(NewJava.Folder, StringComparison.OrdinalIgnoreCase)))
        '加入列表顶部
        Configs.JavaList.Edit(
        Sub(ByRef List)
            List.Remove(NewJava)
            List.Insert(0, NewJava)
        End Sub)
        Hint($"已将 Java 加入列表：{NewJavaPath}", HintType.Green)
        UpdateJavaLists()
    End Sub

    ''' <summary>
    ''' 手动从 Java 列表中移除指定的 Java，并记录到已移除列表中。
    ''' </summary>
    Public Sub ManuallyRemoveJava(JavaEntry As Java)
        Configs.JavaList.Edit(Sub(ByRef List) List.Remove(JavaEntry)) '从 Java 列表中移除
        Configs.JavaRemovedList.Edit(Function(List) List.Append(JavaEntry.Folder).DistinctBy(Function(s) s.Lower()).ToList()) '记录已移除
        Hint($"已将 Java 移除出列表：{JavaEntry}", HintType.Green)
        UpdateJavaLists()
        PageInstanceLeft.ReloadCurrentJava() '如果强制指定了该 Java，这会把它自动重置掉
    End Sub

    ''' <summary>
    ''' 手动触发搜索 Java，强制检查所有 Java。
    ''' </summary>
    Public Sub ManuallySearchJava(Optional CancellationToken As CancellationToken = Nothing)
        RunInThread(
        Sub()
            Configs.JavaList.Get().ForAll(Sub(java) java.InvalidateChecked()) '重置已检查的状态
            JavaListRefreshWorker.Start(cancellationToken:=CancellationToken)
            JavaListRefreshWorker.WaitIfRunningAsync(cancellationToken:=CancellationToken).Run() 'worker 开始/结束时会自动重载 UI
            If Configs.JavaList.Get().Any Then
                Hint("已找到 " & Configs.JavaList.Get().Count & " 个 Java！", HintType.Green)
            Else
                Hint("未找到可用的 Java！", HintType.Red)
            End If
        End Sub)
    End Sub

#End Region

#Region "选取"

    ''' <summary>
    ''' 分析指定 Minecraft 版本的基础 Java 版本要求。不受设置影响。
    ''' 如果由于 Java 版本要求冲突而导致没有可用的 Java，则返回 null。
    ''' </summary>
    Public Function GetJavaRequirement(Instance As McInstance) As (Range As ValueRange(Of Version), RecommendedComponent As String)

        '版本设置已经强制指定了 Java 版本要求
        If Settings.Get(Of Integer)("VersionArgumentJavaV2", Instance) = 1 Then
            Try
                Dim Range = ValueRange(Of Version).FromString(Settings.Get(Of String)("VersionArgumentJavaRange", Instance), Function(s) StringUtils.ParseVersionWithDefaults(s))
                Return (Range, Nothing)
            Catch ex As Exception
                Logger.Warn($"版本设置中指定的 Java 版本范围有误（{Settings.Get(Of String)("VersionArgumentJavaRange", Instance)}），这可能是因为首次修改此设置，尚未初始化")
            End Try
        End If

        '----------------------------------- 从 Minecraft 版本信息获取需求 -----------------------------------

        Dim JavaRange = ValueRange(Of Version).All()
        Dim AddConstraint =
        Sub(Constraint As ValueRange(Of Version))
            JavaRange = JavaRange?.Intersect(Constraint)
            If JavaRange.IsEmpty Then
                Logger.Warn($"Java 版本要求冲突，若启动失败请在版本设置手动指定 Java：当前追加的要求为 {Constraint}，游戏版本为 {Instance.VersionDisplayName}", LogBehavior.Toast)
                JavaRange = Constraint
            End If
        End Sub

        '原版 26-：通过版本号判断
        If (Not Instance.Version.Vaild AndAlso Instance.ReleaseTime >= New Date(2024, 4, 2)) OrElse
           (Instance.Version.Vaild AndAlso Instance.Version.Vanilla >= New Version(20, 0, 5)) Then
            '1.20.5+（24w14a+）：至少 Java 21
            AddConstraint(ValueRange(Of Version).AtLeast(New Version(21, 0)))
        ElseIf (Not Instance.Version.Vaild AndAlso Instance.ReleaseTime >= New Date(2021, 11, 16)) OrElse
               (Instance.Version.Vaild AndAlso Instance.Version.Vanilla.Major >= 18) Then
            '1.18 pre2+：至少 Java 17
            AddConstraint(ValueRange(Of Version).AtLeast(New Version(17, 0)))
        ElseIf (Not Instance.Version.Vaild AndAlso Instance.ReleaseTime >= New Date(2021, 5, 11)) OrElse
               (Instance.Version.Vaild AndAlso Instance.Version.Vanilla.Major >= 17) Then
            '1.17+ (21w19a+)：至少 Java 16
            AddConstraint(ValueRange(Of Version).AtLeast(New Version(16, 0)))
        ElseIf Instance.ReleaseTime.Year >= 2017 Then 'Minecraft 1.12 与 1.11 的分界线正好是 2017 年，太棒了
            '1.12+：至少 Java 8
            AddConstraint(ValueRange(Of Version).AtLeast(New Version(8, 0)))
        ElseIf Instance.ReleaseTime <= New Date(2013, 5, 1) AndAlso Instance.ReleaseTime.Year >= 2001 Then '避免某些版本写个 1960 年
            '1.5.2-：最高 Java 8
            AddConstraint(ValueRange(Of Version).LessThan(New Version(9, 0)))
        End If

        '版本元数据声明的 Java 最低要求（包含快照和整合包）
        Dim RecommendedComponent As String = Nothing
        Dim RecommendedCode As Integer =
            If(Instance.JsonObject?("javaVersion")?("majorVersion")?.ToObject(Of Integer),
            If(Instance.JsonVersion?("java_version")?.ToObject(Of Integer), 0))
        RecommendedComponent = If(Instance.JsonObject?("javaVersion")?("component")?.ToString,
                                  Instance.JsonVersion?("java_component")?.ToString)
        If RecommendedComponent = "" Then RecommendedComponent = Nothing
        If RecommendedCode >= 8 AndAlso RecommendedCode < 100 Then
            Logger.Info("Mojang 要求至少使用 Java " & RecommendedCode)
            AddConstraint(ValueRange(Of Version).AtLeast(New Version(RecommendedCode, 0)))
        End If

        'OptiFine 检测
        If Instance.Version.HasOptiFine AndAlso Instance.Version.Vaild Then '不管非标准版本
            If Instance.Version.Vanilla.Major < 7 Then
                '<1.7：至多 Java 8
                AddConstraint(ValueRange(Of Version).LessThan(New Version(9, 0)))
            ElseIf Instance.Version.Vanilla.Major >= 8 AndAlso Instance.Version.Vanilla.Major < 12 Then
                '1.8 - 1.11：必须恰好 Java 8
                AddConstraint(ValueRange(Of Version).ClosedOpen(New Version(8, 0), New Version(9, 0)))
            ElseIf Instance.Version.Vanilla.Major = 12 Then
                '1.12：最高 Java 8
                AddConstraint(ValueRange(Of Version).LessThan(New Version(9, 0)))
            End If
        End If

        'Forge 检测（部分测试结果见 #8432）
        If Instance.Version.HasForge Then
            If Instance.Version.Vanilla >= New Version(6, 0, 1) AndAlso Instance.Version.Vanilla <= New Version(7, 0, 2) Then
                '1.6.1 - 1.7.2：必须 Java 7
                AddConstraint(ValueRange(Of Version).ClosedOpen(New Version(7, 0), New Version(8, 0)))
            ElseIf Instance.Version.Vanilla.Major <= 12 OrElse Not Instance.Version.Vaild Then '非标准版本
                '<=1.12：Java 8
                AddConstraint(ValueRange(Of Version).LessThan(New Version(9, 0)))
            ElseIf Instance.Version.Vanilla.Major <= 14 Then
                '1.13 - 1.14：Java 8 - 10
                AddConstraint(ValueRange(Of Version).ClosedOpen(New Version(8, 0), New Version(11, 0)))
            ElseIf Instance.Version.Vanilla.Major = 15 Then
                '1.15：Java 8 - 15
                AddConstraint(ValueRange(Of Version).ClosedOpen(New Version(8, 0), New Version(16, 0)))
            ElseIf CompareVersionGE(Instance.Version.Forge, "34.0.0") AndAlso CompareVersionGE("36.2.25", Instance.Version.Forge) Then
                '1.16.3~5，Forge 34.0.0 ~ 36.2.25：最高 Java 8u320
                AddConstraint(ValueRange(Of Version).AtMost(New Version(8, 0, 320)))
            ElseIf CompareVersionGE(Instance.Version.Forge, "36.2.26") AndAlso CompareVersionGE("36.999999.999999", Instance.Version.Forge) Then
                '1.16.5，Forge 36.2.26+，<37.0.0：最高 Java 23
                AddConstraint(ValueRange(Of Version).LessThan(New Version(24, 0)))
            ElseIf CompareVersionGE(Instance.Version.Forge, "37.0.0") AndAlso CompareVersionGE("37.0.79", Instance.Version.Forge) Then
                '1.17.1，Forge 37.0.0 ~ 37.0.79：最高 Java 16
                AddConstraint(ValueRange(Of Version).LessThan(New Version(17, 0)))
            ElseIf Instance.Version.Vanilla.Major = 18 AndAlso Instance.Version.HasOptiFine Then '#305
                '1.18：若安装了 OptiFine，最高 Java 18
                AddConstraint(ValueRange(Of Version).LessThan(New Version(19, 0)))
            ElseIf CompareVersionGE(Instance.Version.Forge, "45.0.21") AndAlso CompareVersionGE("45.0.65", Instance.Version.Forge) Then
                '1.19.4，Forge 45.0.21 ~ 45.0.65：最高 Java 19
                AddConstraint(ValueRange(Of Version).LessThan(New Version(20, 0)))
            ElseIf CompareVersionGE(Instance.Version.Forge, "45.0.66") AndAlso CompareVersionGE("47.4.8", Instance.Version.Forge) Then
                '1.19.4~1.20.1，Forge 45.0.66 ~ 47.4.8：最高 Java 21
                AddConstraint(ValueRange(Of Version).LessThan(New Version(22, 0)))
            End If
        End If

        'NeoForge 检测
        If Instance.Version.HasNeoForge Then
            If Instance.Version.Vanilla = New Version(20, 0, 1) OrElse
               (CompareVersionGE("20.2.62-beta", Instance.Version.NeoForge) AndAlso Not Instance.Version.NeoForge.Contains("25w14craftmine")) Then
                '1.20.1，以及 1.20.2 的 20.2.62-beta 之前：最高 Java 21
                AddConstraint(ValueRange(Of Version).LessThan(New Version(22, 0)))
            End If
        End If

        'Fabric 检测
        If Instance.Version.HasFabric AndAlso Instance.Version.Vaild Then '不管非标准版本
            If Instance.Version.Vanilla.Major >= 15 AndAlso Instance.Version.Vanilla.Major <= 16 Then
                '1.15 - 1.16：Java 8+
                AddConstraint(ValueRange(Of Version).AtLeast(New Version(8, 0)))
            ElseIf Instance.Version.Vanilla.Major >= 18 Then
                '1.18+：Java 17+
                AddConstraint(ValueRange(Of Version).AtLeast(New Version(17, 0)))
            End If
        End If

        'LiteLoader 检测
        If Instance.Version.HasLiteLoader AndAlso Instance.Version.Vaild Then '不管非标准版本
            '最高 Java 8
            AddConstraint(ValueRange(Of Version).LessThan(New Version(9, 0)))
        End If

        '统一通行证检测
        If Settings.Get(Of McLoginType)("LoginType") = McLoginType.Nide Then
            '至少 Java 8u101
            AddConstraint(ValueRange(Of Version).AtLeast(New Version(8, 0, 141)))
        End If

        Return (JavaRange, RecommendedComponent)
    End Function

    ''' <summary>推荐游戏对应的 Java 主版本，并保持在模组或用户指定的兼容范围内。</summary>
    Public Function GetRecommendedJavaMajor(Vanilla As Version, ReleaseTime As Date, MetadataMajor As Integer, Range As ValueRange(Of Version)) As Integer
        If Range Is Nothing OrElse Range.IsEmpty Then Return 0
        Dim Preferred As Integer = MetadataMajor
        If Preferred < 5 OrElse Preferred >= 100 Then
            If (Vanilla IsNot Nothing AndAlso Vanilla >= New Version(20, 0, 5)) OrElse (Vanilla Is Nothing AndAlso ReleaseTime >= New Date(2024, 4, 2)) Then
                Preferred = 21
            ElseIf (Vanilla IsNot Nothing AndAlso Vanilla.Major >= 18) OrElse (Vanilla Is Nothing AndAlso ReleaseTime >= New Date(2021, 11, 16)) Then
                Preferred = 17
            ElseIf (Vanilla IsNot Nothing AndAlso Vanilla.Major >= 17) OrElse (Vanilla Is Nothing AndAlso ReleaseTime >= New Date(2021, 5, 11)) Then
                Preferred = 16
            Else
                Preferred = 8
            End If
        End If
        Return Enumerable.Range(5, 95).
            Where(Function(Major) Not Range.Intersect(ValueRange(Of Version).ClosedOpen(New Version(Major, 0), New Version(Major + 1, 0))).IsEmpty).
            OrderBy(Function(Major) Math.Abs(Major - Preferred)).ThenBy(Function(Major) Major).FirstOrDefault()
    End Function

    Private Function SelectAutomaticJava(Instance As McInstance, Range As ValueRange(Of Version), RecommendedComponent As String,
                                         TryFixOrHint As Boolean, c As CancellationToken, p As ProgressProvider) As Java
        Dim AutoManage = Instance IsNot Nothing AndAlso Settings.Get(Of Boolean)("LaunchJavaAutoSelect")
        Dim Preferred = If(Instance Is Nothing, GetRecommendedJavaMajor(Nothing, Date.MinValue, 21, Range),
            GetRecommendedJavaMajor(If(Instance.Version.Vaild, Instance.Version.Vanilla, Nothing), Instance.ReleaseTime,
                If(Instance.JsonObject?("javaVersion")?("majorVersion")?.ToObject(Of Integer), If(Instance.JsonVersion?("java_version")?.ToObject(Of Integer), 0)), Range))
        If Range Is Nothing OrElse Range.IsEmpty OrElse Preferred = 0 Then Throw New InvalidOperationException("Java 版本要求存在冲突，请在版本设置中检查 Java 版本范围。")
        Logger.Info($"Java 兼容范围：{Range}，推荐主版本：{Preferred}，自动管理：{AutoManage}")
        Dim Checked As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        Dim FindLocal As Func(Of Java) =
            Function()
                JavaListRefreshWorker.WaitIfRunning(cancellationToken:=c)
                Dim Available As New List(Of Java)
                For Each Candidate In Configs.JavaList.Get()
                    c.ThrowIfCancellationRequested()
                    '每次启动重新执行检查，避免已被移除或损坏的 Java 命中旧缓存。
                    If TryFixOrHint AndAlso Checked.Add(Candidate.Folder) Then Candidate.InvalidateChecked()
                    If Not Candidate.CheckAsync(c).Run() OrElse Candidate.Version Is Nothing OrElse Not Range.Contains(Candidate.Version) Then Continue For
                    If Not AutoManage Then Return Candidate
                    Available.Add(Candidate)
                Next
                Return RankJavaCandidates(Available, Preferred).FirstOrDefault()
            End Function
        Dim Result = FindLocal()
        If Result IsNot Nothing AndAlso (Not AutoManage OrElse Result.Version.Major = Preferred OrElse Not TryFixOrHint) Then Return Result
        SyncLock JavaLock
            c.ThrowIfCancellationRequested()
            JavaListRefreshWorker.Start(c, p?.SplitTo(0.2))
            Result = FindLocal()
            If Result IsNot Nothing AndAlso (Not AutoManage OrElse Result.Version.Major = Preferred) Then Return Result
            If Not TryFixOrHint Then Return Result
            Try
                Hint($"正在准备适合此版本的 Java {Preferred}，完成后将自动继续启动")
                Dim Installed = DownloadManagedJava(Range, Preferred, RecommendedComponent, c, p)
                p?.Finish()
                Hint($"已自动选择 Java {Installed.Version.Major}", HintType.Green)
                Return Installed
            Catch ex As Exception
                ex.ThrowIfCanceled()
                If Result IsNot Nothing Then
                    Logger.Warn(ex, $"推荐 Java 下载失败，改用兼容的 Java {Result.Version}")
                    Hint($"推荐 Java 下载失败，已使用兼容的 Java {Result.Version.Major}；可在启动设置中更换下载源", HintType.Red)
                    Return Result
                End If
                Throw New InvalidOperationException($"未能准备 Java {Preferred}。请检查网络，或在【设置 → 启动 → Java 运行环境】中更换下载源、导入 Java。", ex)
            End Try
        End SyncLock
    End Function

    ''' <summary>优先推荐主版本，其次兼容主版本；同主版本选择本机较新的补丁。</summary>
    Public Function RankJavaCandidates(Candidates As IEnumerable(Of Java), Preferred As Integer) As IEnumerable(Of Java)
        Return Candidates.Where(Function(Candidate) Candidate.Version IsNot Nothing).
            OrderBy(Function(Candidate) Math.Abs(Candidate.Version.Major - Preferred)).
            ThenByDescending(Function(Candidate) Candidate.Version)
    End Function

    ''' <summary>
    ''' 根据版本或 Java 范围要求寻找适合的 Java。
    ''' 若指定了 <paramref name="TryFixOrHint"/>，则会在找不到时会尝试下载 Java 或提示用户下载，否则只会静默运行。
    ''' </summary>
    ''' <returns>最适合的 Java。若找不到或失败则返回 null，除非取消否则不会抛出异常。</returns>
    Public Function SelectOrDownloadJava(Target As OneOf(Of ValueRange(Of Version), McInstance), TryFixOrHint As Boolean, Optional c As CancellationToken = Nothing, Optional p As ProgressProvider = Nothing) As Java
        Try
            If Target.Is(Of McInstance) Then MigrateInstanceJavaSettings(Target.As(Of McInstance), c, p?.SplitTo(0.2)) '迁移
            Dim Instance As McInstance = If(Target.Is(Of McInstance), Target.As(Of McInstance), Nothing)
            Select Case If(Instance Is Nothing, 0, Settings.Get(Of Integer)("VersionArgumentJavaV2", Instance))

                Case 2 '============================= 使用版本文件夹中的 Java =============================

                    '查找版本文件夹下的 Java
                    Logger.Info($"版本设置要求使用版本文件夹中的 Java：{Instance.PathVersion}")
                    Dim JavaFound = JavaUtils.SearchFoldersAsync(True, {Instance.PathVersion}, c, p?.SplitTo(1)).Run().FirstOrDefault()
                    If JavaFound IsNot Nothing Then
                        Logger.Info($"已发现版本文件夹中的 Java：{JavaFound}")
                        Return JavaFound
                    End If
                    '未能找到
                    If TryFixOrHint Then
                        SwitchToInstanceSetup(Instance)
                        MyMsgBox($"该版本的版本设置选择了 {vbLQ}使用版本文件夹中的 Java{vbRQ}，但版本文件夹里没能找到 Java。{vbCrLf}请先修改启动选项中的 Java 设置，或在版本文件夹中放一个 Java，然后再试。",
                            "未找到 Java", IsWarn:=True, ForceWait:=True)
                    End If
                    Return Nothing

                Case 3 '=============================== 强制指定特定 Java ===============================

                    Dim ChosenJava = Configs.JavaForced.Get(Instance.Config)
                    Logger.Info($"版本设置中强制指定的 Java：{Instance.PathVersion} → {ChosenJava}")
                    If ChosenJava Is Nothing Then
                        If TryFixOrHint Then
                            SwitchToInstanceSetup(Instance)
                            MyMsgBox($"该版本的版本设置选择了 {vbLQ}使用指定的 Java{vbRQ}，但还没有指定任何 Java。{vbCrLf}请先选择你想使用的 Java，然后再试。",
                                "未选择 Java", IsWarn:=True, ForceWait:=True)
                        End If
                        p?.Skip() : Return Nothing '<==== 设置中未指定 Java
                    End If
                    If Not Configs.JavaList.Get().Contains(ChosenJava) Then
                        Configs.JavaForced.Reset(Instance.Config)
                        UpdateJavaLists()
                        If TryFixOrHint Then
                            SwitchToInstanceSetup(Instance)
                            Logger.Error("版本设置中指定的 Java 不在 Java 列表中，请重新选择一个 Java！", LogBehavior.Alert)
                        End If
                        p?.Skip() : Return Nothing '<==== 设置中指定的 Java 不在列表中
                    End If
                    If Not ChosenJava.CheckAsync(c).Run() Then
                        Configs.JavaForced.Reset(Instance.Config)
                        UpdateJavaLists()
                        If TryFixOrHint Then
                            SwitchToInstanceSetup(Instance)
                            Logger.Error("版本设置中指定的 Java 已无法使用，请重新选择一个 Java，然后再试。", LogBehavior.Alert)
                        Else
                            Logger.Error("版本设置中指定的 Java 已无法使用，请重新选择一个 Java！", LogBehavior.Toast)
                        End If
                        p?.Finish() : Return Nothing '<==== 指定的 Java 存在异常
                    End If
                    p?.Finish() : Return ChosenJava '<==== 成功找到并检查通过

                Case Else '======================== 根据版本范围自动选择 JavaList 中的 Java ========================

                    Dim Requirement = If(Instance Is Nothing, (Target.As(Of ValueRange(Of Version)), CType(Nothing, String)), GetJavaRequirement(Instance))
                    Return SelectAutomaticJava(Instance, Requirement.Item1, Requirement.Item2, TryFixOrHint, c, p)
            End Select
        Catch ex As Exception
            ex.ThrowIfCanceled()
            Logger.Error(ex, "选择 Java 时出现异常", If(TryFixOrHint, LogBehavior.Alert, LogBehavior.None))
            p?.Skip() : Return Nothing
        End Try
    End Function
    Private Sub SwitchToInstanceSetup(Instance As McInstance)
        RunInUi(
        Sub()
            PageInstanceLeft.Instance = Instance
            FrmMain.PageChange(FormMain.PageType.InstanceSetup, FormMain.PageSubType.InstanceSetup)
        End Sub)
    End Sub
    Private ReadOnly JavaLock As New Object

    ''' <summary>
    ''' 从老版本 BRCL 迁移版本独立的 Java 设置。
    ''' </summary>
    Private Sub MigrateInstanceJavaSettings(Instance As McInstance, Optional c As CancellationToken = Nothing, Optional p As ProgressProvider = Nothing)
        If Configs.JavaMigrated.Get(Instance.Config) Then p?.Skip() : Return
        If FrmInstanceSetup IsNot Nothing Then RunInUiWait(Sub() FrmInstanceSetup.ComboArgumentJava.IsEnabled = False)
        Try
            Logger.Info($"正在尝试迁移版本独立的 Java 设置（{Instance.Name}）")
            '强制使用指定 Java
            Dim ForcedJavaSetting = Settings.Get(Of String)("VersionArgumentJavaSelect", Instance)
            If Not ForcedJavaSetting.StartsWithF("{") Then
                p?.Set(0.4, True) : GoTo NoForcedJava
            End If
            Dim ForcedJava = New Java(ForcedJavaSetting.DeserializeJson(Of JObject)()("Path").ToString)
            If Not ForcedJava.CheckAsync(c).Run() Then
                Hint("该版本的设置中强制指定的 Java 存在异常，该设置已重置", HintType.Red)
                p?.Set(0.4) : GoTo NoForcedJava
            End If
            Logger.Info($"将版本独立设置改为老版本中强制指定的 Java：{ForcedJava}")
            If Not Configs.JavaList.Get().Contains(ForcedJava) Then
                Configs.JavaList.Edit(Sub(ByRef List) List.Add(ForcedJava)) '加入列表底部
                Logger.Info($"已将强制指定的 Java 加入 Java 列表底部：{ForcedJava}")
            End If
            Configs.JavaForced.Set(ForcedJava, Instance.Config)
            Settings.Set("VersionArgumentJavaV2", 3, Instance)
            UpdateJavaLists()
            Configs.JavaMigrated.Set(True, Instance.Config) '完成
            p?.Finish() : Return
NoForcedJava:
            Dim JavaFound = JavaUtils.SearchFoldersAsync(True, {Instance.PathVersion}, c, p?.SplitTo(1)).Run()
            If JavaFound.Any() Then
                '从版本文件夹中发现了 Java
                Logger.Info($"在版本文件夹中发现了 Java，将版本独立设置改为优先使用版本文件夹的 Java：{JavaFound}")
                Settings.Set("VersionArgumentJavaV2", 2, Instance)
            Else
                '自动选择
                Logger.Info("没有需要迁移的内容，自动选择 Java")
                Settings.Set("VersionArgumentJavaV2", 0, Instance)
            End If
            Configs.JavaMigrated.Set(True, Instance.Config) '完成
        Finally
            If FrmInstanceSetup IsNot Nothing Then RunInUiWait(
            Sub()
                SettingService.RefreshSettings(FrmInstanceSetup.ComboArgumentJava)
                SettingService.RefreshSettings(FrmInstanceSetup.ComboArgumentJavaSelect)
                FrmInstanceSetup.ComboArgumentJava.IsEnabled = True
            End Sub)
        End Try
    End Sub

#End Region

#Region "受管理 Java 下载"

    Private Const JavaRuntimeIndex As String = "https://piston-meta.mojang.com/v1/products/java-runtime/2ec0cc96c44e5a76b9c8b7c39df7210883d12871/all.json"

    Private Function DownloadManagedJava(Range As ValueRange(Of Version), Preferred As Integer, Component As String,
                                         c As CancellationToken, p As ProgressProvider) As Java
        Dim Source = Settings.Get(Of Integer)("LaunchJavaDownloadSource")
        Logger.Info($"准备 Java {Preferred}，下载源：{JavaDownloadSourceName(Source)}")
        'Temurin 的同一份安装包在官方、清华、南京间测速；特殊旧版使用 Mojang 运行环境。
        If Source <> 2 Then
            Try
                Dim Archive = ResolveTemurinJavaArchive(Preferred, Range, Source, c)
                If Archive IsNot Nothing Then Return InstallJavaArchive(Archive, Range, c, p)
                If Source = 3 OrElse Source = 4 Then Throw New InvalidOperationException("所选镜像没有满足兼容范围的 Java，请切换为自动测速或导入 Java。")
            Catch ex As Exception
                ex.ThrowIfCanceled()
                If Source = 3 OrElse Source = 4 Then Throw
                Logger.Warn(ex, "Temurin Java 获取失败，尝试同一下载策略下的 Mojang 运行环境")
            End Try
        End If
        Dim IndexUrls = OrderJavaDownloadUrls(GetMojangJavaDownloadUrls(JavaRuntimeIndex, Source), c)
        Dim Index = JObject.Parse(ReadJavaMetadata(IndexUrls, Nothing, c))
        Dim Platform = TryCast(Index("windows-x64"), JObject)
        If Platform Is Nothing Then Throw New InvalidDataException("Java 下载列表中没有 Windows x64 运行环境。")
        Dim Candidates As New List(Of (Component As String, Entry As JObject, Version As Version))
        For Each Item In Platform.Properties()
            If Item.Name = "minecraft-java-exe" Then Continue For
            For Each Entry As JObject In Item.Value
                Dim Name = Entry("version")?("name")?.ToString()
                Dim Match = System.Text.RegularExpressions.Regex.Match(If(Name, ""), "^(?<major>\d+)(?:u(?<update>\d+)|(?:\.(?<minor>\d+))(?:\.(?<patch>\d+))?(?:\.(?<build>\d+))?)?")
                If Not Match.Success Then Continue For
                Dim Version As New Version(CInt(Val(Match.Groups("major").Value)), CInt(Val(Match.Groups("minor").Value)),
                    CInt(Val(If(Match.Groups("update").Success, Match.Groups("update").Value, Match.Groups("patch").Value))), CInt(Val(Match.Groups("build").Value)))
                If Version.Major = Preferred AndAlso Range.Contains(Version) Then Candidates.Add((Item.Name, Entry, Version))
            Next
        Next
        If Candidates.Count = 0 Then Throw New InvalidOperationException($"下载源没有符合范围 {Range} 的 Java {Preferred}。请在启动设置中更换下载源或导入 Java。")
        Dim Target = Candidates.OrderByDescending(Function(Item) Item.Component = Component).ThenByDescending(Function(Item) Item.Version).First()
        Dim Manifest = Target.Entry("manifest")
        Dim Checker As New FileChecker With {.Hash = Manifest("sha1")?.ToString(), .IsJson = True}
        If Manifest("size") IsNot Nothing Then Checker.ActualSize = CLng(Manifest("size"))
        If Checker.Hash Is Nothing OrElse Not System.Text.RegularExpressions.Regex.IsMatch(Checker.Hash, "\A[0-9a-fA-F]{40}\z") Then Throw New InvalidDataException("Java 清单缺少有效校验值。")
        Dim Files = JObject.Parse(ReadJavaMetadata(GetMojangJavaDownloadUrls(Manifest("url").ToString(), Source), Checker, c))("files")
        Dim Stage = CreateJavaStageDirectory()
        Try
            Dim RawFiles = CType(Files, JObject).Properties().Where(Function(Item) Item.Value("downloads")?("raw") IsNot Nothing).ToList()
            If RawFiles.Count = 0 Then Throw New InvalidDataException("Java 清单中没有可下载文件。")
            '仅选一个代表性大文件测速，其他运行时文件复用源排序。
            Dim Sample = RawFiles.OrderByDescending(Function(Item) CLng(Item.Value("downloads")("raw")("size"))).First().Value("downloads")("raw")("url").ToString()
            Dim SourceOrder = OrderJavaDownloadUrls(GetMojangJavaDownloadUrls(Sample, Source), c).
                Select(Function(Url) If(New Uri(Url).Host = "bmclapi2.bangbang93.com", 2, 1)).ToArray()
            Dim Downloads As New List(Of NetFile)
            For Each Item In RawFiles
                c.ThrowIfCancellationRequested()
                Dim Info = Item.Value("downloads")("raw")
                Dim FileHash = Info("sha1")?.ToString()
                If Not System.Text.RegularExpressions.Regex.IsMatch(If(FileHash, ""), "\A[0-9a-fA-F]{40}\z") Then Throw New InvalidDataException("Java 文件缺少有效校验值。")
                Dim Destination = GetJavaRuntimeFilePath(Stage, Item.Name)
                Downloads.Add(New NetFile(SourceOrder.Select(Function(Id) MapMojangJavaUrl(Info("url").ToString(), Id)), Destination,
                    New FileChecker With {.Hash = FileHash, .ActualSize = CLng(Info("size"))}))
            Next
            RunJavaFileDownload(Downloads, c, p)
            Return PublishJavaRuntime(Stage, Stage, Range, Preferred, c)
        Finally
            CleanupJavaStage(Stage)
        End Try
    End Function

    Private Function ReadJavaMetadata(Urls As IEnumerable(Of String), Checker As FileChecker, c As CancellationToken) As String
        Dim Stage = CreateJavaStageDirectory()
        Dim Target = IO.Path.Combine(Stage, "metadata.json")
        Try
            Using Deadline As New CancellationTokenSource(30000), Linked = CancellationTokenSource.CreateLinkedTokenSource(c, Deadline.Token)
                Try
                    RunJavaFileDownload(New List(Of NetFile) From {New NetFile(Urls, Target, If(Checker, New FileChecker With {.IsJson = True}))}, Linked.Token, Nothing)
                Catch ex As Exception
                    c.ThrowIfCancellationRequested()
                    If Deadline.IsCancellationRequested Then Throw New TimeoutException("获取 Java 元数据超时，请更换下载源。", ex)
                    Throw
                End Try
            End Using
            Return IO.File.ReadAllText(Target)
        Finally
            CleanupJavaStage(Stage)
        End Try
    End Function

    Private Sub RunJavaFileDownload(Files As List(Of NetFile), c As CancellationToken, p As ProgressProvider)
        Dim Loader As New LoaderDownload("下载 Java 运行环境", Files)
        Try
            Loader.WaitForExit(c:=c, p:=p?.SplitTo(0.85))
        Finally
            Loader.Cancel()
        End Try
    End Sub

    Private Function InstallJavaArchive(Archive As JavaArchiveDownloadInfo, Range As ValueRange(Of Version), c As CancellationToken, p As ProgressProvider) As Java
        Dim Stage = CreateJavaStageDirectory()
        Try
            Dim ZipPath = IO.Path.Combine(Stage, "runtime.zip")
            Dim ExtractPath = IO.Path.Combine(Stage, "runtime")
            Dim Checker As New FileChecker With {.Hash = Archive.Sha256, .ActualSize = Archive.Size}
            RunJavaFileDownload(New List(Of NetFile) From {New NetFile(Archive.Urls, ZipPath, Checker)}, c, p)
            c.ThrowIfCancellationRequested()
            Dim CheckError = Checker.Check(ZipPath)
            If CheckError IsNot Nothing Then Throw New InvalidDataException("Java 安装包校验失败：" & CheckError)
            ExtractJavaRuntimeArchive(ZipPath, ExtractPath, c)
            Dim JavaFiles = IO.Directory.GetFiles(ExtractPath, "java.exe", SearchOption.AllDirectories).
                Where(Function(File) IO.Path.GetFileName(IO.Path.GetDirectoryName(File)).Equals("bin", StringComparison.OrdinalIgnoreCase)).ToArray()
            If JavaFiles.Length <> 1 Then Throw New InvalidDataException("Java 安装包中没有唯一的 bin/java.exe。")
            Dim RuntimeRoot = IO.Directory.GetParent(IO.Path.GetDirectoryName(JavaFiles(0))).FullName
            Return PublishJavaRuntime(Stage, RuntimeRoot, Range, Archive.JavaVersion.Major, c)
        Finally
            CleanupJavaStage(Stage)
        End Try
    End Function

    ''' <summary>拒绝压缩包或清单中的绝对路径、设备名、ADS 与目录穿越。</summary>
    Public Function GetJavaRuntimeFilePath(BaseDir As String, RelativePath As String) As String
        If String.IsNullOrWhiteSpace(RelativePath) OrElse IO.Path.IsPathRooted(RelativePath) OrElse RelativePath.Contains(":") Then Throw New InvalidDataException("Java 文件路径无效。")
        Dim Parts = RelativePath.Replace("/", "\").Split("\"c)
        For Each Part In Parts
            If Part = "" OrElse Part = "." OrElse Part = ".." OrElse Part.EndsWith(".") OrElse Part.EndsWith(" ") OrElse
                Part.IndexOfAny(IO.Path.GetInvalidFileNameChars()) >= 0 OrElse
                System.Text.RegularExpressions.Regex.IsMatch(Part, "\A(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|\z)", System.Text.RegularExpressions.RegexOptions.IgnoreCase) Then
                Throw New InvalidDataException("Java 文件路径包含不安全的目录名。")
            End If
        Next
        Dim Root = IO.Path.GetFullPath(BaseDir).TrimEnd(IO.Path.DirectorySeparatorChar) & IO.Path.DirectorySeparatorChar
        Dim Result = IO.Path.GetFullPath(IO.Path.Combine(Root, String.Join("\", Parts)))
        If Not Result.StartsWith(Root, StringComparison.OrdinalIgnoreCase) Then Throw New InvalidDataException("Java 文件路径超出安装目录。")
        Return Result
    End Function

    Public Sub ExtractJavaRuntimeArchive(ZipPath As String, Destination As String, Optional c As CancellationToken = Nothing)
        Dim TotalSize As Long = 0
        Using Archive = ZipFile.OpenRead(ZipPath)
            If Archive.Entries.Count > 20000 Then Throw New InvalidDataException("Java 安装包文件过多。")
            For Each Entry In Archive.Entries
                c.ThrowIfCancellationRequested()
                Dim Relative = Entry.FullName.TrimEnd("/"c, "\"c)
                Dim Target = GetJavaRuntimeFilePath(Destination, Relative)
                If ((Entry.ExternalAttributes >> 16) And &HF000) = &HA000 Then Throw New InvalidDataException("Java 安装包不能包含符号链接。")
                TotalSize += Entry.Length
                If TotalSize > 2L * 1024 * 1024 * 1024 Then Throw New InvalidDataException("Java 安装包展开后超过大小限制。")
                If Entry.FullName.EndsWith("/") OrElse Entry.FullName.EndsWith("\") Then
                    IO.Directory.CreateDirectory(Target)
                    Continue For
                End If
                IO.Directory.CreateDirectory(IO.Path.GetDirectoryName(Target))
                Using Input = Entry.Open(), Output = New FileStream(Target, FileMode.CreateNew, FileAccess.Write)
                    Dim Buffer(65535) As Byte
                    While True
                        c.ThrowIfCancellationRequested()
                        Dim Count = Input.Read(Buffer, 0, Buffer.Length)
                        If Count = 0 Then Exit While
                        Output.Write(Buffer, 0, Count)
                    End While
                End Using
            Next
        End Using
    End Sub

    Private Function CreateJavaStageDirectory() As String
        Dim Stage = IO.Path.Combine(Paths.AppData, ".minecraft", "brcl-java-staging", Guid.NewGuid().ToString("N"))
        IO.Directory.CreateDirectory(Stage)
        Return Stage
    End Function

    Private Sub CleanupJavaStage(Stage As String)
        Try
            If IO.Directory.Exists(Stage) Then IO.Directory.Delete(Stage, True)
        Catch ex As Exception
            Logger.Warn(ex, "清理 Java 下载临时目录失败，将保留临时文件")
        End Try
    End Sub

    Private Function PublishJavaRuntime(Stage As String, RuntimeRoot As String, Range As ValueRange(Of Version), Preferred As Integer, c As CancellationToken) As Java
        c.ThrowIfCancellationRequested()
        Dim Candidate As New Java(IO.Path.Combine(RuntimeRoot, "bin"))
        If Not Candidate.CheckAsync(c).Run() OrElse Candidate.Version.Major <> Preferred OrElse Not Range.Contains(Candidate.Version) Then Throw New InvalidDataException("下载的 Java 未通过运行或版本兼容性检查。")
        Dim FinalRoot = IO.Path.Combine(Paths.AppData, ".minecraft", "runtime", $"brcl-java-{Preferred}-{Guid.NewGuid():N}")
        IO.Directory.CreateDirectory(IO.Path.GetDirectoryName(FinalRoot))
        c.ThrowIfCancellationRequested()
        '只发布已验证的全新目录，不覆盖、清理其他 Java 或已在运行的游戏文件。
        IO.Directory.Move(RuntimeRoot, FinalRoot)
        Dim Installed As New Java(IO.Path.Combine(FinalRoot, "bin"))
        If Not Installed.CheckAsync(c).Run() Then Throw New InvalidDataException("Java 安装后验证失败。")
        Configs.JavaList.Edit(Sub(ByRef List) List.Add(Installed))
        UpdateJavaLists()
        Logger.Info($"已安装并自动选择 Java：{Installed}")
        Return Installed
    End Function

#End Region

End Module
