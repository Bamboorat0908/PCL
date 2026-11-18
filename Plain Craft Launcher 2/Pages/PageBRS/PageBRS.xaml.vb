Imports System.Diagnostics
Imports System.IO
Imports System.Net.Http
Imports System.Threading.Tasks
Imports System.Windows.Documents
Imports System.Windows.Media
Imports Newtonsoft.Json.Linq

''' <summary>
''' BRS 页面：读取 zhushu.homes/BRS 的状态接口，用启动器自带 UI 单页展示服务器信息。
''' </summary>
Public Class PageBRS
    Implements IRefreshable

    Private Const WebsiteUrl As String = "https://zhushu.homes/BRS/"
    Private Const ApiUrl As String = WebsiteUrl & "api/status"
    Private Const MinecraftServerAddress As String = "trm.friedcoder.online"
    Private Const RefreshIntervalSeconds As Integer = 15
    Private Const UserAgent As String = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0 Safari/537.36"

    Private Shared ReadOnly CachePath As String = Paths.Base & "BRCL\BrsCache.json"

    '全进程复用同一个 HttpClient，避免每次刷新重建连接池（借鉴 HttpClient 使用规范）
    Private Shared ReadOnly Http As New HttpClient()

    Private IsInited As Boolean = False
    Private IsLoading As Boolean = False
    Private TimerRefresh As Threading.DispatcherTimer
    Private _LastSizeH As Double = 0
    Private ReadOnly FetchClock As Stopwatch = Stopwatch.StartNew()
    Private _LastFetchTick As Long = -100000
    Private HasData As Boolean = False
    Private LastData As JObject
    Private ReadOnly NumberTargets As New Dictionary(Of TextBlock, String)
    Private ReadOnly NumberAnimationPrefix As String = "BrsNumber " & GetUuid() & " "
    Private PendingData As PreparedStatus
    Private PendingIsCache As Boolean
    Private ReadOnly ApplyTimer As New Threading.DispatcherTimer(Threading.DispatcherPriority.Background) With {.Interval = TimeSpan.FromMilliseconds(40)}
    Private HostWindow As Window
    Private TrendDays As Integer = 7
    Private ShowAllRanks As Boolean = False
    '切页不重复请求；定时刷新由 IsLoading 防止并发，不受请求完成时间影响。
    Private Const MinFetchIntervalMs As Integer = RefreshIntervalSeconds * 1000

    Public Sub New()
        InitializeComponent()
        LabAddress.Text = MinecraftServerAddress
        LabAddress.Tag = MinecraftServerAddress
        LabAddress.ToolTip = "点击复制：" & MinecraftServerAddress
    End Sub

    Private Class PreparedStatus
        Public Json As JObject
        Public Dashboard As Dictionary(Of String, String)
        Public Summary As Dictionary(Of String, String)
        Public Playtime As JArray
        Public Achievements As JArray
    End Class

    Private Shared Function PrepareStatus(Json As JObject) As PreparedStatus
        ValidateData(Json)
        Dim Achievements = TryCast(Json("achievements"), JObject)
        Return New PreparedStatus With {
            .Json = Json, .Dashboard = BrsDashboardData.Build(Json), .Summary = BuildActivitySummary(Json),
            .Playtime = BuildLeaderboard(TryCast(Json("playtime"), JArray), False),
            .Achievements = BuildLeaderboard(TryCast(Achievements?("top"), JArray), True)}
    End Function

    Shared Sub New()
        Try
            Http.Timeout = TimeSpan.FromSeconds(12)
            Http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", UserAgent)
            Http.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json, text/plain, */*")
            Http.DefaultRequestHeaders.CacheControl = New Headers.CacheControlHeaderValue With {.NoCache = True}
        Catch ex As Exception
        End Try
    End Sub

    'MC 颜色码 → 浅色背景可读的近似色（白/浅灰在浅色卡片上不可读，统一压深）
    Private Shared ReadOnly MotdColors As New Dictionary(Of Char, String) From {
        {"0"c, "#000000"}, {"1"c, "#1B3FA0"}, {"2"c, "#1F7A33"}, {"3"c, "#12787D"},
        {"4"c, "#A32A2A"}, {"5"c, "#8B2FA0"}, {"6"c, "#9A5B06"}, {"7"c, "#6B6280"},
        {"8"c, "#8B82A0"}, {"9"c, "#3D5BD9"}, {"a"c, "#1F8A3B"}, {"b"c, "#12787D"},
        {"c"c, "#C9203F"}, {"d"c, "#B034C4"}, {"e"c, "#8A6A00"}, {"f"c, "#2A2338"},
        {"g"c, "#8A6A00"}, {"h"c, "#6B6280"}, {"i"c, "#6B6280"}, {"j"c, "#6B6280"}
    }

    Private Sub PageBRS_Loaded(sender As Object, e As RoutedEventArgs) Handles Me.Loaded
        HostWindow = Window.GetWindow(Me)
        If HostWindow IsNot Nothing Then
            RemoveHandler HostWindow.StateChanged, AddressOf ActivityChanged
            AddHandler HostWindow.StateChanged, AddressOf ActivityChanged
        End If
        If Not IsInited Then
            IsInited = True
            '首次进入先放占位，避免几秒的“空卡片”像是坏了
            ShowPlaceholders()
            AddHandler ApplyTimer.Tick, AddressOf ApplyPendingStatus
            'The base page owns entry animations. Start I/O after initial layout
            'instead of blocking Loaded or running a second animation per card.
            Dim Initialization = Dispatcher.BeginInvoke(Threading.DispatcherPriority.Background, New Action(
                Sub()
                    TryRenderCache()
                    LoadData()
                End Sub))
        Else
            LoadData()
        End If
        If PendingData IsNot Nothing Then
            ApplyTimer.Start()
        End If
        StartTimer()
    End Sub

    Private Sub ActivityChanged(sender As Object, e As EventArgs)
        If PageActive Then
            If PendingData IsNot Nothing Then ApplyTimer.Start()
            StartTimer()
        Else
            ApplyTimer.Stop()
            StopTimer()
        End If
    End Sub

    Private Sub VisibilityChanged(sender As Object, e As DependencyPropertyChangedEventArgs) Handles Me.IsVisibleChanged
        ActivityChanged(sender, EventArgs.Empty)
    End Sub

    Private ReadOnly Property PageActive As Boolean
        Get
            Return IsLoaded AndAlso IsVisible AndAlso (FrmMain Is Nothing OrElse FrmMain.WindowState <> WindowState.Minimized)
        End Get
    End Property

    Private Sub QueueStatus(Data As PreparedStatus, FromCache As Boolean)
        'A live response always supersedes a pending disk snapshot.
        If FromCache AndAlso (HasData OrElse PendingData IsNot Nothing) Then Return
        PendingData = Data
        PendingIsCache = FromCache
        If Not FromCache Then _LastFetchTick = FetchClock.ElapsedMilliseconds
        If PageActive Then ApplyTimer.Start()
    End Sub

    Private Sub ApplyPendingStatus(sender As Object, e As EventArgs)
        If Not PageActive OrElse PendingData Is Nothing Then
            ApplyTimer.Stop()
            Return
        End If
        'Do not rebuild visual rows while the page transition is rendering.
        If PageState = PageStates.ContentEnter OrElse PageState = PageStates.LoaderEnter OrElse
            PageState = PageStates.PageExit OrElse PageState = PageStates.ContentExit Then Return
        ApplyTimer.Stop()
        Dim Current = PendingData, Cached = PendingIsCache
        PendingData = Nothing
        ApplyPreparedData(Current, Cached)
    End Sub

    Private Sub PageBRS_Unloaded(sender As Object, e As RoutedEventArgs) Handles Me.Unloaded
        StopTimer()
        ApplyTimer.Stop()
        If HostWindow IsNot Nothing Then RemoveHandler HostWindow.StateChanged, AddressOf ActivityChanged
        HostWindow = Nothing
        '切页时结束到真实数值，避免回来后显示停在中途的在线人数。
        For Each Target In NumberTargets
            AniStop(NumberAnimationPrefix & Target.Key.Name)
            Target.Key.Text = Target.Value
        Next
        NumberTargets.Clear()
        Dim Cards = {CardHead, CardPlayers, CardTps, CardSeason, CardTrend, CardActivity, CardPlaytime, CardAchievements, CardWebsite}
        For i As Integer = 0 To Cards.Length - 1
            Cards(i).Opacity = 1
            Cards(i).RenderTransform = Transform.Identity
        Next
    End Sub

    Private Sub StartTimer()
        StopTimer()
        '与原站一致，每 15 秒获取一次；离开页面即停。
        TimerRefresh = New Threading.DispatcherTimer(Threading.DispatcherPriority.Background) With {.Interval = TimeSpan.FromSeconds(RefreshIntervalSeconds)}
        AddHandler TimerRefresh.Tick, AddressOf OnRefreshTick
        TimerRefresh.Start()
    End Sub

    Private Sub StopTimer()
        Try
            If TimerRefresh IsNot Nothing Then
                TimerRefresh.Stop()
                RemoveHandler TimerRefresh.Tick, AddressOf OnRefreshTick
                TimerRefresh = Nothing
            End If
        Catch ex As Exception
        End Try
    End Sub

    Private Sub OnRefreshTick(sender As Object, e As EventArgs)
        LoadData(True)
    End Sub

    Private Sub PageBRS_PreviewMouseWheel(sender As Object, e As MouseWheelEventArgs) Handles Me.PreviewMouseWheel
        '玩家列表优先滚动；到达列表边缘后，再由外层滚动整页。
        If Not ScrollPlayers.IsMouseOver OrElse ScrollPlayers.ScrollableHeight <= 0 Then Return
        If (e.Delta > 0 AndAlso ScrollPlayers.VerticalOffset <= 0) OrElse
           (e.Delta < 0 AndAlso ScrollPlayers.VerticalOffset >= ScrollPlayers.ScrollableHeight) Then Return
        ScrollPlayers.PerformVerticalOffsetDelta(-e.Delta)
        e.Handled = True
    End Sub

#Region "数据加载"

    Public Sub BtnRefresh_Click() Handles BtnRefresh.Click
        LoadData(True)
    End Sub

    ''' <summary>
    ''' F5 刷新（实现 IRefreshable）。
    ''' </summary>
    Public Sub RefreshPage() Implements IRefreshable.Refresh
        LoadData(True)
    End Sub

    Private Sub LoadData(Optional Force As Boolean = False)
        If Not PageActive Then Return
        If PendingData IsNot Nothing Then ApplyTimer.Start()
        If IsLoading Then Return
        If Not Force AndAlso FetchClock.ElapsedMilliseconds - _LastFetchTick < MinFetchIntervalMs AndAlso
            (HasData OrElse (PendingData IsNot Nothing AndAlso Not PendingIsCache)) Then Return
        IsLoading = True
        Try
            BtnRefresh.IsEnabled = False
            If LabUpdated.Text = "尚未刷新" Then LabUpdated.Text = "正在刷新…"
        Catch ex As Exception
        End Try
        RunInNewThread(
        Sub()
            Dim Result As PreparedStatus = Nothing
            Dim [Error] As String = Nothing
            Try
                Dim Body As String = Http.GetStringAsync(ApiUrl).GetAwaiter().GetResult()
                Result = PrepareStatus(JObject.Parse(Body))
                '写缓存，供下次启动或断网时兜底
                Try
                    DirectoryUtils.Create(Path.GetDirectoryName(CachePath))
                    Dim PendingPath As String = CachePath & ".tmp"
                    File.WriteAllText(PendingPath, Body)
                    If File.Exists(CachePath) Then
                        File.Replace(PendingPath, CachePath, Nothing)
                    Else
                        File.Move(PendingPath, CachePath)
                    End If
                Catch ex As Exception
                End Try
            Catch ex As Exception
                Result = Nothing
                Logger.Warn(ex, "获取 BRS 状态失败")
                [Error] = ex.Message
            End Try
            RunInUi(
            Sub()
                IsLoading = False
                Try
                    BtnRefresh.IsEnabled = True
                Catch ex As Exception
                End Try
                If Result Is Nothing Then
                    If PageActive Then ApplyError([Error])
                Else
                    QueueStatus(Result, False)
                End If
            End Sub)
        End Sub, "BRS 状态加载")
    End Sub

    ''' <summary>
    ''' 首次加载时的占位文案（拿到数据后会被 Clear 掉）。
    ''' </summary>
    Private Sub ShowPlaceholders()
        PanPlayers.Children.Clear()
        PanPlayers.Children.Add(New TextBlock With {
            .Text = "正在获取在线玩家…", .FontSize = 12, .Opacity = 0.55, .Margin = New Thickness(0, 2, 0, 0)})
        PanTrend.Children.Clear()
        PanTrend.Children.Add(New TextBlock With {
            .Text = "正在获取活跃数据…", .FontSize = 12, .Opacity = 0.55,
            .VerticalAlignment = VerticalAlignment.Center, .HorizontalAlignment = HorizontalAlignment.Center})
    End Sub

    ''' <summary>
    ''' 用本地缓存先渲染一次，保证首屏不空白、断网也有内容（参考 Prism 的新闻缓存兜底）。
    ''' </summary>
    Private Async Sub TryRenderCache()
        Try
            Dim Cached = Await Task.Run(
                Function()
                    If Not File.Exists(CachePath) Then Return Nothing
                    Dim Body As String = File.ReadAllText(CachePath)
                    If String.IsNullOrWhiteSpace(Body) Then Return Nothing
                    Return PrepareStatus(JObject.Parse(Body))
                End Function)
            If Cached Is Nothing Then Return
            QueueStatus(Cached, True)
            Logger.Info("BRS 状态：已使用本地缓存渲染首屏")
        Catch ex As Exception
            Logger.Warn(ex, "读取 BRS 缓存失败")
        End Try
    End Sub

    Private Shared Sub ValidateData(Json As JObject)
        Dim Status As JObject = TryCast(Json("status"), JObject)
        If Status Is Nothing OrElse Status("alive") Is Nothing OrElse Status("alive").Type <> JTokenType.Boolean Then
            Throw New FormatException("服务器状态数据缺少有效的 status.alive")
        End If
        '可选指标交给对应摘要逐项校验，单个坏值不阻止其他状态更新。
    End Sub

    Private Sub ApplyError(Message As String)
        LabAutoRefresh.Text = "15 秒自动更新 · 刷新失败"
        '已有数据（内存或缓存）时不清屏，只降级提示，避免一次网络抖动把整页打成空白
        If HasData Then
            LabUpdated.Text = "刷新失败，展示的是上次数据"
            LabInfoStrip.Text = "zhushu.homes · 刷新失败"
            LabDataHint.Text = "网络暂时不可用，以下为上次记录；在线情况与运行指标可能已变化。"
            LabDataHint.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrush3")
            LabOnlineCaption.Text = "上次在线"
            LabState.Text = "状态待确认"
            Dim Previous = TryCast(LastData?("status"), JObject)
            If Previous IsNot Nothing Then RenderOnlinePlayers(TryCast(Previous("players"), JArray), Previous.Value(Of Boolean)("alive"), True)
            Return
        End If
        LabState.Text = "连接失败"
        BadgeState.Background = StateBrush(False)
        LabUpdated.Text = "刷新失败，将自动重试"
        LastRenderedMotd = Nothing
        LastMotdTheme = -1
        LabMotd.Text = "无法获取服务器状态：" & Message
        LabDataHint.Text = "无法获取最新状态，可稍后刷新或访问 BRS 原站。"
    End Sub

    Private Function ApplyData(Json As JObject, Optional FromCache As Boolean = False) As Boolean
        Try
            Return ApplyPreparedData(PrepareStatus(Json), FromCache)
        Catch ex As Exception
            Logger.Warn(ex, "解析 BRS 状态数据失败")
            ApplyError(ex.Message)
            Return False
        End Try
    End Function

    Private Function ApplyPreparedData(Data As PreparedStatus, FromCache As Boolean) As Boolean
        Try
            Dim Json = Data.Json
            Dim Status As JObject = TryCast(Json("status"), JObject)
            Dim Alive As Boolean = Status IsNot Nothing AndAlso CBool(Status("alive"))
            Dim Dashboard = Data.Dashboard
            Dim IsSnapshot As Boolean = FromCache OrElse IsStale(Json)
            LabOnlineCaption.Text = If(IsSnapshot, "上次在线", "当前在线")
            '状态徽章
            If Alive Then
                LabState.Text = If(IsSnapshot, "上次在线", "运行中")
                BadgeState.Background = StateBrush(True)
            Else
                LabState.Text = If(IsSnapshot, "上次离线", "离线")
                BadgeState.Background = StateBrush(False)
            End If

            Dim MaxPlayers = ReadActivityNumber(Status?("max_players"), True)
            Dim Count = ReadActivityNumber(Status?("player_count"), True)
            Dim OldPlayers As Double
            Double.TryParse(LabPlayers.Text, OldPlayers)
            If Alive AndAlso Count.HasValue Then
                AnimateNumber(LabPlayers, OldPlayers, Count.Value, "0")
            Else
                AniStop(NumberAnimationPrefix & LabPlayers.Name)
                NumberTargets.Remove(LabPlayers)
                LabPlayers.Text = "—"
            End If
            LabPlayersMax.Text = "/" & ActivityNumberText(MaxPlayers)

            'MOTD
            Dim Motd As String = If(Status Is Nothing, "", If(Status("motd") Is Nothing, "", Status("motd").ToString()))
            RenderMotd(Motd.Replace("%onlineplayers%", If(Alive, ActivityNumberText(Count), "—")))

            LabSeason.Text = Dashboard("SeasonLabel")
            LabSeasonProgress.Text = Dashboard("SeasonDay")
            LabSeasonDay.Text = Dashboard("SeasonLabel")
            LabSeasonStart.Text = If(Dashboard("SeasonStart") = "—", "开服日期暂未提供", "开服于 " & Dashboard("SeasonStart"))
            LabSeasonPlayers.Text = Dashboard("SeasonPlayers")
            LabSeasonHours.Text = Dashboard("SeasonHours").Replace(" 小时", "")
            LabWorldSize.Text = Dashboard("WorldSize")
            LabEntities.Text = Dashboard("Entities")
            BarAchievement.Value = Double.Parse(Dashboard("AchievementPercent"), Global.System.Globalization.CultureInfo.InvariantCulture)

            'TPS / MSPT
            Dim Tps As Double
            If Alive AndAlso Double.TryParse(Dashboard("EstimatedTps"), Global.System.Globalization.NumberStyles.Float, Global.System.Globalization.CultureInfo.InvariantCulture, Tps) Then
                LabTps.Text = If(Tps >= 19, "流畅", If(Tps >= 15, "轻微卡顿", "较为卡顿"))
            Else
                LabTps.Text = If(Alive, "暂无性能数据", "离线")
            End If
            LabDashboardMspt.Text = Dashboard("Mspt")
            LabDashboardTps.Text = Dashboard("EstimatedTps")
            LabMspt.Text = "主世界 tick " & Dashboard("OverworldMspt")
            LabUptime.Text = Dashboard("Uptime")
            LabMemory.Text = Dashboard("Memory")
            LabLoadHint.Text = Dashboard("LoadHint")
            LabWeekAchievements.Text = Dashboard("WeekAchievements")
            LabWeeklyTopName.Text = If(Dashboard("WeeklyTopName") = "—", "暂无记录", Dashboard("WeeklyTopName"))
            LabWeeklyTopName.ToolTip = LabWeeklyTopName.Text
            LabWeeklyTopHours.Text = If(Dashboard("WeeklyTopName") = "—", "", Dashboard("WeeklyTopHours"))
            If CStr(PanWeeklyTopAvatar.Tag) <> Dashboard("WeeklyTopName") Then
                PanWeeklyTopAvatar.Tag = Dashboard("WeeklyTopName")
                PanWeeklyTopAvatar.Children.Clear()
                If Dashboard("WeeklyTopName") <> "—" Then PanWeeklyTopAvatar.Children.Add(New BrsPlayerAvatar(Dashboard("WeeklyTopName"), 24))
            End If

            '玩家连接与交流信息
            Dim ServerInfo As JObject = TryCast(Json("server"), JObject)
            Dim Community As JObject = TryCast(Json("qq"), JObject)
            LabServerName.Text = WebsiteText(ServerInfo?("name"), "BRS")
            LabServerName.ToolTip = LabServerName.Text
            LabCommunity.Text = WebsiteText(Community?("group"), "—")
            LabCommunity.Tag = WebsiteText(Community?("group"))
            LabCommunityHint.Text = If(String.IsNullOrWhiteSpace(CStr(LabCommunity.Tag)), "暂无交流群信息", "QQ 群 · 点击群号复制")

            Dim Summary = Data.Summary
            LabPack.Text = Summary("Pack")
            LabPack.ToolTip = LabPack.Text
            LabWeekRange.Text = Summary("WeekRange")
            LabWeekRange.ToolTip = LabWeekRange.Text
            LabWeekPlayers.Text = Summary("WeekPlayers")
            LabWeekHours.Text = Summary("WeekHours")
            LabWeekNewcomers.Text = Summary("WeekNewcomers")
            LabAchievementProgress.Text = Summary("AchievementProgress")
            LabAchievementCount.Text = Summary("AchievementCount")
            LabAchievementCount.ToolTip = LabAchievementCount.Text
            LabActivityUpdated.Text = Summary("ActivityUpdated")
            LabActivityUpdated.ToolTip = LabActivityUpdated.Text
            LabPlayerFoot.Text = "今日活跃 " & Dashboard("TodayActive") & " 人 · 最高同时在线 " & Dashboard("TodayPeak") & " 人"

            '在线玩家
            Dim Players As JArray = If(Status Is Nothing, Nothing, TryCast(Status("players"), JArray))
            RenderOnlinePlayers(Players, Alive, IsSnapshot)
            RenderWebsiteContent(Json, Data.Playtime, Data.Achievements)

            '更新时间
            Dim TimeText As String = "更新于 " & FormatUpdatedAt(Json("updated_at"))
            If FromCache Then
                TimeText &= "（本地缓存）"
            ElseIf IsStale(Json) Then
                TimeText &= "（源站数据滞后）"
            End If
            LabUpdated.Text = TimeText
            LabAutoRefresh.Text = "15 秒自动更新 · F5 刷新"
            LabInfoStrip.Text = "15 秒自动刷新 · " & TimeText.Replace("更新于 ", "数据 ")
            LabDataHint.Text = If(FromCache, "正在展示本地记录，联网后自动同步最新状态。", If(IsSnapshot, Dashboard("DataHint"), "游玩统计更新于 " & FormatUpdatedAt(Json("activity_updated_at"))))
            LabDataHint.SetResourceReference(TextBlock.ForegroundProperty, If(IsSnapshot, "ColorBrush3", "ColorBrush2"))
            LastData = Json
            HasData = True
            Return True
        Catch ex As Exception
            Logger.Warn(ex, "解析 BRS 状态数据失败")
            ApplyError(ex.Message)
            Return False
        End Try
    End Function

    Private Shared Function WebsiteText(Token As JToken, Optional Fallback As String = "") As String
        If Token Is Nothing OrElse Token.Type <> JTokenType.String OrElse String.IsNullOrWhiteSpace(Token.ToString()) Then Return Fallback
        Return Token.ToString().Trim()
    End Function

    Private Sub RenderOnlinePlayers(Players As JArray, Alive As Boolean, Optional IsSnapshot As Boolean = False)
        Dim Snapshot As New JObject From {{"alive", Alive}, {"players", If(Players, New JArray)}, {"snapshot", IsSnapshot}}
        Dim Previous = TryCast(PanPlayers.Tag, JObject)
        '15 秒刷新时保持正在查看的名单与滚动位置，只有名单变化才重建。
        If JToken.DeepEquals(Previous, Snapshot) Then Return
        'A cached roster often matches the first live response. Update its state
        'labels in place so the same rows, avatars and scroll position survive.
        If Alive AndAlso Players IsNot Nothing AndAlso Players.Count > 0 AndAlso
            Previous IsNot Nothing AndAlso Previous.Value(Of Boolean)("alive") AndAlso
            JToken.DeepEquals(Previous("players"), Snapshot("players")) Then
            For Each Row As Grid In PanPlayers.Children
                CType(Row.Children(2), TextBlock).Text = If(IsSnapshot, "上次在线", "在线")
            Next
            PanPlayers.Tag = Snapshot
            Return
        End If
        PanPlayers.Tag = Snapshot
        PanPlayers.Children.Clear()
        If Alive AndAlso Players IsNot Nothing AndAlso Players.Count > 0 Then
            LabPlayerCount.Text = "共 " & Players.Count.ToString() & " 人"
            For Each Player As JToken In Players
                If Player.Type = JTokenType.String Then PanPlayers.Children.Add(CreatePlayerChip(Player.ToString(), IsSnapshot))
            Next
        Else
            LabPlayerCount.Text = If(IsSnapshot, If(Alive, "上次无人在线", "上次离线"), If(Alive, "无人在线", "服务器离线"))
            PanPlayers.Children.Add(New TextBlock With {
                .Text = If(IsSnapshot, If(Alive, "上次记录中没有在线玩家，联网后同步最新状态。", "上次记录为离线，联网后同步最新状态。"), If(Alive, "当前没有玩家在线，晚点再来看看～", "服务器当前离线，恢复后会自动更新。")),
                .FontSize = 12, .Opacity = 0.6, .TextWrapping = TextWrapping.Wrap, .Margin = New Thickness(0, 2, 0, 6)})
        End If
    End Sub

    Private Sub RenderWebsiteContent(Json As JObject, Optional Playtime As JArray = Nothing, Optional AchievementRanks As JArray = Nothing)
        LabPlaytimeSummary.Text = "当前周目 " & ActivityNumberText(ReadActivityNumber(Json("playtime_players"), True)) &
            " 位玩家 · 累计 " & ActivityNumberText(ReadActivityNumber(Json("playtime_total_hours")), "#,0.0") & " 小时"
        Dim Achievements = TryCast(Json("achievements"), JObject)
        Dim Average = ReadActivityNumber(Achievements?("avg_pct"))
        LabAchievementSummary.Text = "已统计 " & ActivityNumberText(ReadActivityNumber(Achievements?("count"), True)) & " 位玩家"
        If Average.HasValue AndAlso Average.Value <= 100 Then LabAchievementSummary.Text &= " · 平均完成 " & ActivityNumberText(Average, "0.0") & "%"
        Dim Ranks = If(Playtime, BuildLeaderboard(TryCast(Json("playtime"), JArray), False))
        BtnRankMore.Visibility = If(Ranks.Count > 5, Visibility.Visible, Visibility.Collapsed)
        BtnRankMore.Text = If(ShowAllRanks, "收起", "展开 " & Ranks.Count.ToString())
        RenderLeaderboard(PanPlaytime, Ranks, If(ShowAllRanks, 15, 5))
        RenderLeaderboard(PanAchievements, If(AchievementRanks, BuildLeaderboard(TryCast(Achievements?("top"), JArray), True)), 5)
        Dim Sponsors = TryCast(Json("sponsors"), JObject)
        Dim ShowSponsors As Boolean = True
        If Sponsors IsNot Nothing AndAlso Sponsors("card_enabled") IsNot Nothing AndAlso Sponsors("card_enabled").Type = JTokenType.Boolean Then ShowSponsors = Sponsors.Value(Of Boolean)("card_enabled")
        BtnSponsors.Visibility = If(ShowSponsors, Visibility.Visible, Visibility.Collapsed)
        Dim History As JObject = SelectTrendHistory(Json, TrendDays)
        If Not JToken.DeepEquals(History, _LastHistory) Then RenderTrend(History)
    End Sub

    Private Sub BtnRankMore_Click() Handles BtnRankMore.Click
        ShowAllRanks = Not ShowAllRanks
        If LastData IsNot Nothing Then RenderWebsiteContent(LastData)
    End Sub

    Private Sub TrendRange_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnTrendWeek.Click, BtnTrendMonth.Click
        TrendDays = If(sender Is BtnTrendWeek, 7, 30)
        LabTrendTitle.Text = "近 " & TrendDays.ToString() & " 天活跃玩家"
        BtnTrendWeek.Text = If(TrendDays = 7, "7 天 · 当前", "7 天")
        BtnTrendMonth.Text = If(TrendDays = 30, "30 天 · 当前", "30 天")
        If LastData IsNot Nothing Then RenderTrend(SelectTrendHistory(LastData, TrendDays))
    End Sub

    Private Sub WebsiteLink_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnWebsite.Click, BtnSeasons.Click, BtnWeekly.Click, BtnMembers.Click, BtnPlayerArchive.Click
        OpenWebsite(WebsiteUrl & CStr(CType(sender, FrameworkElement).Tag))
    End Sub

    Private Sub Map_Click() Handles BtnMap.Click
        FrmMain.PageChange(FormMain.PageType.SatelliteMap)
    End Sub

    Private Sub ClientSettings_Click() Handles BtnClientSettings.Click
        PageInstanceLeft.OpenSettings(FindBrsInstance())
    End Sub

    Private Sub Sponsors_Click() Handles BtnSponsors.Click
        FrmMain.PageChange(FormMain.PageType.Setup, FormMain.PageSubType.SetupSponsors)
    End Sub

    Private Shared Function SelectTrendHistory(Json As JObject, Days As Integer) As JObject
        Dim Activity = TryCast(Json("activity"), JObject)
        Dim Source As JObject = TryCast(Activity?(If(Days = 7, "week", "month")), JObject)
        If Source Is Nothing OrElse Source.Count = 0 Then Source = TryCast(Activity?("month"), JObject)
        If Source Is Nothing OrElse Source.Count = 0 Then Source = TryCast(Json("history"), JObject)
        Dim Result As New JObject
        If Source Is Nothing Then Return Result
        Dim EndDay = ReadActivityDate(Json("today"))
        Dim Valid As New SortedDictionary(Of DateTime, Integer)
        For Each Entry As JProperty In Source.Properties()
            Dim Day = ReadActivityDate(New JValue(Entry.Name)), Count = ReadActivityNumber(Entry.Value, True)
            If Day.HasValue AndAlso Count.HasValue Then Valid(Day.Value) = CInt(Count.Value)
        Next
        If Valid.Count = 0 Then Return Result
        If Not EndDay.HasValue Then EndDay = Valid.Keys.Last()
        For Each Entry In Valid
            If Entry.Key > EndDay.Value OrElse Entry.Key < EndDay.Value.AddDays(1 - Days) Then Continue For
            Result(Entry.Key.ToString("yyyy-MM-dd", Global.System.Globalization.CultureInfo.InvariantCulture)) = Entry.Value
        Next
        Return Result
    End Function

    Private Shared Function BuildLeaderboard(Source As JArray, IsAchievements As Boolean) As JArray
        Dim Entries As New List(Of JObject)
        If Source Is Nothing Then Return New JArray
        For Each Token In Source
            Dim Item = TryCast(Token, JObject)
            If Item Is Nothing Then Continue For
            Dim Name As String = WebsiteText(Item("name"))
            Dim Value = ReadActivityNumber(Item(If(IsAchievements, "real", "seconds")), IsAchievements)
            If Name.Length = 0 OrElse Not Value.HasValue Then Continue For
            Dim Meta As String = ""
            If Not IsAchievements Then
                Dim Sessions = ReadActivityNumber(Item("sessions"), True)
                If Sessions.HasValue Then Meta = "上线 " & ActivityNumberText(Sessions) & " 次"
            End If
            Entries.Add(New JObject From {
                {"name", Name}, {"value", Value.Value}, {"meta", Meta},
                {"display", If(IsAchievements, ActivityNumberText(Value) & " 项", ActivityNumberText(Value.Value / 3600, "#,0.0") & " 小时")}
            })
        Next
        Entries.Sort(Function(A, B) B.Value(Of Double)("value").CompareTo(A.Value(Of Double)("value")))
        Dim Result As New JArray, Names As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        For Each Entry In Entries
            If Not Names.Add(Entry.Value(Of String)("name")) Then Continue For
            Result.Add(Entry)
            If Result.Count >= 15 Then Exit For
        Next
        Return Result
    End Function

    Private Sub RenderLeaderboard(Panel As StackPanel, Entries As JArray, Limit As Integer)
        Dim Shown As New JArray(Entries.Take(Limit))
        If JToken.DeepEquals(TryCast(Panel.Tag, JArray), Shown) Then Return
        Panel.Tag = Shown
        Panel.Children.Clear()
        If Shown.Count = 0 Then
            Panel.Children.Add(New TextBlock With {.Text = "原站暂未提供榜单数据", .FontSize = 11, .Opacity = 0.6, .Margin = New Thickness(0, 12, 0, 12)})
            Return
        End If
        For i As Integer = 0 To Shown.Count - 1
            Dim Item = CType(Shown(i), JObject)
            Dim Row As New Grid With {.Margin = New Thickness(0, 0, 0, 8), .MinHeight = 27}
            Row.ColumnDefinitions.Add(New ColumnDefinition With {.Width = New GridLength(23)})
            Row.ColumnDefinitions.Add(New ColumnDefinition With {.Width = New GridLength(1, GridUnitType.Star)})
            Row.ColumnDefinitions.Add(New ColumnDefinition With {.Width = GridLength.Auto})
            Dim Rank As New TextBlock With {.Text = (i + 1).ToString("00"), .FontSize = 11, .VerticalAlignment = VerticalAlignment.Center}
            Rank.SetResourceReference(TextBlock.ForegroundProperty, If(i < 3, "ColorBrush3", "ColorBrush2"))
            Row.Children.Add(Rank)
            Dim Name As String = Item.Value(Of String)("name")
            '头像固定在名字前，长 ID 可省略，右侧数值仍完整显示。
            Dim Identity As New Grid
            Identity.ColumnDefinitions.Add(New ColumnDefinition With {.Width = GridLength.Auto})
            Identity.ColumnDefinitions.Add(New ColumnDefinition With {.Width = New GridLength(1, GridUnitType.Star)})
            Identity.Children.Add(New BrsPlayerAvatar(Name, 24) With {.Margin = New Thickness(0, 0, 8, 0)})
            Dim Link As New Hyperlink(New Run(Name)) With {.NavigateUri = New Uri(WebsiteUrl & "player?name=" & Uri.EscapeDataString(Name)), .ToolTip = "查看 " & Name & " 的玩家档案"}
            Link.SetResourceReference(Hyperlink.ForegroundProperty, "ColorBrush1")
            AddHandler Link.RequestNavigate, Sub(sender2, e2)
                                                OpenWebsite(e2.Uri.AbsoluteUri)
                                                e2.Handled = True
                                            End Sub
            Dim Player As New TextBlock With {.FontSize = 12, .TextTrimming = TextTrimming.CharacterEllipsis, .VerticalAlignment = VerticalAlignment.Center, .Margin = New Thickness(0, 0, 8, 0)}
            Player.Inlines.Add(Link)
            Player.ToolTip = Name & If(Item.Value(Of String)("meta").Length > 0, " · " & Item.Value(Of String)("meta"), "")
            Grid.SetColumn(Player, 1)
            Identity.Children.Add(Player)
            Grid.SetColumn(Identity, 1)
            Row.Children.Add(Identity)
            Dim Value As New TextBlock With {.Text = Item.Value(Of String)("display"), .FontSize = 11, .VerticalAlignment = VerticalAlignment.Center}
            Value.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrush2")
            Grid.SetColumn(Value, 2)
            Row.Children.Add(Value)
            Panel.Children.Add(Row)
        Next
    End Sub

    '活动字段可选；缺失或异常时只降级对应信息，不影响服务器状态和其他卡片。
    Private Shared Function ReadActivityNumber(Token As JToken, Optional Whole As Boolean = False) As Double?
        If Token Is Nothing OrElse (Token.Type <> JTokenType.Integer AndAlso Token.Type <> JTokenType.Float) Then Return Nothing
        Dim Value As Double = Token.Value(Of Double)()
        If Double.IsNaN(Value) OrElse Double.IsInfinity(Value) OrElse Value < 0 Then Return Nothing
        If Whole AndAlso (Value <> Math.Truncate(Value) OrElse Value > Integer.MaxValue) Then Return Nothing
        Return Value
    End Function

    Private Shared Function ActivityNumberText(Value As Double?, Optional Format As String = "#,0") As String
        Return If(Value.HasValue, Value.GetValueOrDefault().ToString(Format, Global.System.Globalization.CultureInfo.InvariantCulture), "—")
    End Function

    Private Shared Function ReadActivityDate(Token As JToken) As DateTime?
        If Token Is Nothing OrElse Token.Type <> JTokenType.String Then Return Nothing
        Dim Value As DateTime
        If DateTime.TryParseExact(Token.ToString(), "yyyy-MM-dd", Global.System.Globalization.CultureInfo.InvariantCulture,
                                 Global.System.Globalization.DateTimeStyles.None, Value) Then Return Value
        Return Nothing
    End Function

    Private Shared Function BuildActivitySummary(Json As JObject) As Dictionary(Of String, String)
        Dim Week = TryCast(Json("weekly"), JObject)
        Dim Achievements = TryCast(Json("achievements"), JObject)
        Dim Server = TryCast(Json("server"), JObject)
        Dim PackToken As JToken = Server?("pack")
        Dim Pack As String = If(PackToken?.Type = JTokenType.String, PackToken.ToString().Trim(), "")
        Dim Summary As New Dictionary(Of String, String) From {
            {"Pack", If(Pack.Length = 0, "整合包信息暂不可用", "整合包 · " & Pack)},
            {"WeekPlayers", ActivityNumberText(ReadActivityNumber(Week?("players"), True))},
            {"WeekHours", ActivityNumberText(ReadActivityNumber(Week?("hours")), "#,0.0")},
            {"WeekNewcomers", ActivityNumberText(ReadActivityNumber(Week?("newcomers"), True))},
            {"WeekRange", "本周日期暂不可用"},
            {"AchievementProgress", "—"}, {"AchievementCount", "暂无成就统计"},
            {"TodayActivity", "今日活跃 — 人 · 最高同时在线 — 人"}
        }

        Dim StartDay = ReadActivityDate(Week?("from")), EndDay = ReadActivityDate(Week?("to"))
        If StartDay.HasValue AndAlso EndDay.HasValue AndAlso EndDay.Value >= StartDay.Value Then
            Summary("WeekRange") = StartDay.Value.ToString("MM-dd") & " 至 " & EndDay.Value.ToString("MM-dd")
        End If
        Dim Days = ReadActivityNumber(Week?("days"), True), DataDays = ReadActivityNumber(Week?("data_days"), True)
        If Days.HasValue AndAlso Days.Value > 0 AndAlso DataDays.HasValue AndAlso DataDays.Value <= Days.Value Then
            Summary("WeekRange") &= " · 已统计 " & DataDays.Value.ToString("0") & "/" & Days.Value.ToString("0") & " 天"
        End If

        Dim Unlocked = ReadActivityNumber(Achievements?("unlocked"), True)
        Dim Total = ReadActivityNumber(Achievements?("total_achievements"), True)
        If Unlocked.HasValue AndAlso Total.HasValue AndAlso Total.Value > 0 AndAlso Unlocked.Value <= Total.Value Then
            Summary("AchievementProgress") = (Unlocked.Value / Total.Value * 100).ToString("0.0", Global.System.Globalization.CultureInfo.InvariantCulture) & "%"
            Summary("AchievementCount") = ActivityNumberText(Unlocked) & " / " & ActivityNumberText(Total) & " 项 · 全服"
        End If

        Dim Updated As String = FormatUpdatedAt(Json("activity_updated_at"))
        Summary("ActivityUpdated") = If(Updated = "—", "统计更新时间未知", "统计更新 " & Updated)
        Dim WeekAchievements = ReadActivityNumber(Week?("ach"), True)
        If WeekAchievements.HasValue Then Summary("ActivityUpdated") &= " · 本周新增成就 " & ActivityNumberText(WeekAchievements) & " 项"

        '使用源站的日期，避免本机日期或时区不同导致查错当天数据。
        Dim Today = ReadActivityDate(Json("today"))
        If Today.HasValue Then
            Dim Key As String = Today.Value.ToString("yyyy-MM-dd", Global.System.Globalization.CultureInfo.InvariantCulture)
            Dim History = TryCast(Json("history"), JObject), Daily = TryCast(Json("daily"), JObject)
            Dim Activity = TryCast(Json("activity"), JObject)
            Dim Peaks As JObject = If(Activity Is Nothing, Nothing, TryCast(Activity("peak_month"), JObject))
            Dim Active = ReadActivityNumber(History?(Key), True)
            If Not Active.HasValue Then Active = ReadActivityNumber(Daily?(Key), True)
            Summary("TodayActivity") = "今日活跃 " & ActivityNumberText(Active) & " 人 · 最高同时在线 " & ActivityNumberText(ReadActivityNumber(Peaks?(Key), True)) & " 人"
        End If
        Return Summary
    End Function

    Private Sub CopyPlayerInfo(sender As Object, e As Input.MouseButtonEventArgs) Handles LabAddress.MouseLeftButtonUp, LabCommunity.MouseLeftButtonUp
        Dim Target = TryCast(sender, TextBlock)
        Dim Value As String = TryCast(Target?.Tag, String)
        If String.IsNullOrWhiteSpace(Value) Then Return
        ClipboardSet(Value)
        e.Handled = True
    End Sub

    Private Shared Function FormatUpdatedAt(Token As JToken) As String
        If Token Is Nothing OrElse Token.Type = JTokenType.Null Then Return "—"
        Dim Updated As DateTime
        If Token.Type = JTokenType.Date Then
            Updated = Token.Value(Of DateTime)()
        ElseIf Not DateTime.TryParse(Token.ToString(), Global.System.Globalization.CultureInfo.InvariantCulture, Global.System.Globalization.DateTimeStyles.RoundtripKind, Updated) Then
            Return "—"
        End If
        Return Updated.ToString("MM-dd HH:mm", Global.System.Globalization.CultureInfo.InvariantCulture)
    End Function

    Public Sub RefreshTheme()
        If LastData Is Nothing Then Return
        Dim Status As JObject = TryCast(LastData("status"), JObject)
        If Status Is Nothing Then Return
        BadgeState.Background = StateBrush(Status.Value(Of Boolean)("alive"))
        Dim Motd As String = If(Status.Value(Of String)("motd"), "")
        RenderMotd(Motd.Replace("%onlineplayers%", If(Status.Value(Of Boolean)("alive"), ActivityNumberText(ReadActivityNumber(Status("player_count"), True)), "—")))
    End Sub

    ''' <summary>状态徽章配色：浅色主题用更深的青/红，保证白字对比度 ≥4.5:1。</summary>
    Private Shared Function StateBrush(Alive As Boolean) As Brush
        Dim IsLight As Boolean = (Settings.Get(Of Integer)("UiThemeMode") = 1)
        If Alive Then
            Return New SolidColorBrush(If(IsLight, Color.FromRgb(&H12, &H80, &H6F), Color.FromRgb(&H1F, &HA8, &H96)))
        End If
        Return New SolidColorBrush(If(IsLight, Color.FromRgb(&HBE, &H2A, &H45), Color.FromRgb(&HD9, &H53, &H6A)))
    End Function

    Private Shared Function IsStale(Json As JObject) As Boolean
        Try
            If Json("ok")?.Type = JTokenType.Boolean AndAlso Not Json.Value(Of Boolean)("ok") Then Return True
            Return Json("stale") IsNot Nothing AndAlso CBool(Json("stale"))
        Catch
            Return False
        End Try
    End Function

#End Region

#Region "数值动画"

    Private Const NumAnimMs As Integer = 420

    ''' <summary>
    ''' 数值滚动：参考 Grafana 数字面板，刷新时给一次短暂的 count-up，避免数字瞬间跳变。
    ''' </summary>
    Private Sub AnimateNumber(Target As TextBlock, FromValue As Double, ToValue As Double, Format As String)
        Try
            Dim AnimationName = NumberAnimationPrefix & Target.Name
            AniStop(AnimationName)
            NumberTargets.Remove(Target)
            If Not IsLoaded OrElse Not UiMotionEnabled Then
                Target.Text = ToValue.ToString(Format)
                Return
            End If
            If Math.Abs(ToValue - FromValue) < 0.001 Then
                Target.Text = ToValue.ToString(Format)
                Return
            End If
            Dim Value = FromValue
            NumberTargets(Target) = ToValue.ToString(Format)
            AniStart({
                AaDouble(Sub(Delta)
                             Value += CDbl(Delta)
                             Dim Text = Value.ToString(Format)
                             If Target.Text <> Text Then Target.Text = Text
                         End Sub, ToValue - FromValue, NumAnimMs,, New AniEaseOutFluent),
                AaCode(Sub()
                           Target.Text = ToValue.ToString(Format)
                           NumberTargets.Remove(Target)
                       End Sub,, True)
            }, AnimationName)
        Catch ex As Exception
            Target.Text = ToValue.ToString(Format)
        End Try
    End Sub

#End Region

#Region "渲染"

    Private LastRenderedMotd As String
    Private LastMotdTheme As Integer = -1

    Private Sub RenderMotd(Motd As String)
        Dim Theme = Settings.Get(Of Integer)("UiThemeMode")
        If LastRenderedMotd = Motd AndAlso LastMotdTheme = Theme Then Return
        LastRenderedMotd = Motd
        LastMotdTheme = Theme
        LabMotd.Inlines.Clear()
        Dim Lines As String() = Motd.Replace("\n", vbLf).Replace("\:", ":").Split({vbLf, vbCrLf}, StringSplitOptions.RemoveEmptyEntries)
        Dim Shown As Integer = 0
        For Each Line As String In Lines
            If Shown >= 2 Then Exit For
            If Line.Trim().Length = 0 Then Continue For
            If Shown > 0 Then LabMotd.Inlines.Add(New LineBreak())
            Dim Current As New Run("")
            Dim Bold As Boolean = False
            Dim I As Integer = 0
            Dim Sb As New Text.StringBuilder()
            While I < Line.Length
                If Line(I) = "§"c AndAlso I + 1 < Line.Length Then
                    Dim Code As Char = Char.ToLowerInvariant(Line(I + 1))
                    If Sb.Length > 0 Then
                        LabMotd.Inlines.Add(New Run(Sb.ToString()) With {.Foreground = MotdBrush(Current.Foreground, Current), .FontWeight = If(Bold, FontWeights.Bold, FontWeights.Normal)})
                        Sb.Clear()
                    End If
                    If Code = "l"c Then
                        Bold = True
                    ElseIf Code = "r"c Then
                        Bold = False
                        Current.Foreground = Nothing
                    ElseIf MotdColors.ContainsKey(Code) Then
                        Current.Foreground = New SolidColorBrush(CType(ColorConverter.ConvertFromString(MotdColors(Code)), Color))
                    End If
                    I += 2
                Else
                    Sb.Append(Line(I))
                    I += 1
                End If
            End While
            If Sb.Length > 0 Then
                LabMotd.Inlines.Add(New Run(Sb.ToString()) With {.Foreground = MotdBrush(Current.Foreground, Current), .FontWeight = If(Bold, FontWeights.Bold, FontWeights.Normal)})
            End If
            Shown += 1
        Next
        If LabMotd.Inlines.Count = 0 Then LabMotd.Text = "BRS 生存服务器"
    End Sub

    Private Shared Function MotdBrush(Brush As Brush, Current As Run) As Brush
        If Brush Is Nothing Then
            Return CType(Application.Current.Resources("ColorBrush1"), Brush)
        End If
        Dim Solid As SolidColorBrush = TryCast(Brush, SolidColorBrush)
        If Solid IsNot Nothing AndAlso Settings.Get(Of Integer)("UiThemeMode") <> 1 Then
            Dim C As Color = Solid.Color
            Return New SolidColorBrush(Color.FromRgb(CByte(C.R + (255 - C.R) * 0.45), CByte(C.G + (255 - C.G) * 0.45), CByte(C.B + (255 - C.B) * 0.45)))
        End If
        Return Brush
    End Function

    '玩家行：头像 + 名字 + 右侧“在线”。
    Private Function CreatePlayerChip(Name As String, Optional IsSnapshot As Boolean = False) As FrameworkElement
        Dim Row As New Grid With {.Height = 30, .Margin = New Thickness(0, 0, 0, 2)}
        Row.ColumnDefinitions.Add(New ColumnDefinition With {.Width = GridLength.Auto})
        Row.ColumnDefinitions.Add(New ColumnDefinition With {.Width = New GridLength(1, GridUnitType.Star)})
        Row.ColumnDefinitions.Add(New ColumnDefinition With {.Width = GridLength.Auto})
        Row.Children.Add(New BrsPlayerAvatar(Name, 24) With {.Margin = New Thickness(0, 0, 9, 0)})
        Dim T As New TextBlock With {.Text = Name, .FontSize = 12.5, .VerticalAlignment = VerticalAlignment.Center, .TextTrimming = TextTrimming.CharacterEllipsis, .Margin = New Thickness(0, 0, 8, 0), .ToolTip = Name}
        T.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrush1")
        Grid.SetColumn(T, 1)
        Row.Children.Add(T)
        Dim State As New TextBlock With {.Text = If(IsSnapshot, "上次在线", "在线"), .FontSize = 10, .VerticalAlignment = VerticalAlignment.Center, .Opacity = 0.55}
        State.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrush2")
        Grid.SetColumn(State, 2)
        Row.Children.Add(State)
        Return Row
    End Function

    Private _LastHistory As JObject

    Private Sub PanTrend_SizeChanged(sender As Object, e As SizeChangedEventArgs) Handles PanTrend.SizeChanged
        '只有高度变化超过 6px 才重绘，避免拖拽窗口时高频重建可视化树
        If _LastHistory Is Nothing Then Return
        If Math.Abs(PanTrend.ActualHeight - _LastSizeH) < 6 Then Return
        _LastSizeH = PanTrend.ActualHeight
        RenderTrend(_LastHistory)
    End Sub

    Private Sub RenderTrend(History As JObject)
        _LastHistory = History
        PanTrend.Children.Clear()
        PanTrend.ColumnDefinitions.Clear()
        If History Is Nothing OrElse History.Count = 0 Then
            LabTrendInfo.Text = "暂无数据"
            LabAxisStart.Text = ""
            LabAxisMid.Text = ""
            LabAxisEnd.Text = ""
            PanTrend.Children.Add(New TextBlock With {.Text = "原站暂无活跃数据", .FontSize = 11, .Opacity = 0.6, .VerticalAlignment = VerticalAlignment.Center, .HorizontalAlignment = HorizontalAlignment.Center})
            Return
        End If
        Dim Items As New List(Of KeyValuePair(Of String, Integer))
        For Each P As JProperty In History.Properties()
            Dim V As Integer = 0
            Dim Day As DateTime
            If Not DateTime.TryParseExact(P.Name, "yyyy-MM-dd", Global.System.Globalization.CultureInfo.InvariantCulture, Global.System.Globalization.DateTimeStyles.None, Day) Then Continue For
            If Not Integer.TryParse(P.Value.ToString(), V) OrElse V < 0 Then Continue For
            Items.Add(New KeyValuePair(Of String, Integer)(P.Name, V))
        Next
        Items.Sort(Function(A, B) String.CompareOrdinal(A.Key, B.Key))
        If Items.Count = 0 Then
            RenderTrend(Nothing)
            Return
        End If
        If Items.Count > TrendDays Then Items = Items.GetRange(Items.Count - TrendDays, TrendDays)

        Dim MaxV As Integer = 0
        For Each It In Items
            If It.Value > MaxV Then MaxV = It.Value
        Next
        LabTrendInfo.Text = "单日最多 " & MaxV.ToString() & " 人"
        LabAxisStart.Text = Items(0).Key.Substring(5)
        LabAxisMid.Text = Items(Items.Count \ 2).Key.Substring(5)
        LabAxisEnd.Text = Items(Items.Count - 1).Key.Substring(5)
        Dim TrackH As Double = If(PanTrend.ActualHeight > 40, PanTrend.ActualHeight - 2, 62)
        For i As Integer = 0 To Items.Count - 1
            Dim It = Items(i)
            PanTrend.ColumnDefinitions.Add(New ColumnDefinition())
            Dim Cell As New Grid With {.Margin = New Thickness(3, 0, 3, 0)}
            Cell.RowDefinitions.Add(New RowDefinition With {.Height = New GridLength(1, GridUnitType.Star)})
            Cell.RowDefinitions.Add(New RowDefinition With {.Height = New GridLength(1, GridUnitType.Star)})
            '底槽
            Dim Track As New Border With {
                .Opacity = 0.42,
                .CornerRadius = New CornerRadius(4),
                .VerticalAlignment = VerticalAlignment.Bottom,
                .Height = TrackH
            }
            Track.SetResourceReference(Border.BackgroundProperty, "ColorBrush6")
            Grid.SetRowSpan(Track, 2)
            Cell.Children.Add(Track)
            '柱
            Dim BarH As Double = If(It.Value = 0, 0, Math.Max(4, TrackH * It.Value / Math.Max(1, MaxV)))
            Dim Bar As New Border With {
                .CornerRadius = New CornerRadius(4),
                .VerticalAlignment = VerticalAlignment.Bottom,
                .Height = BarH,
                .ToolTip = It.Key.Substring(5) & " · " & It.Value.ToString() & " 人"
            }
            Bar.SetResourceReference(Border.BackgroundProperty, If(i = Items.Count - 1, "ColorBrush3", "ColorBrush2"))
            Grid.SetRowSpan(Bar, 2)
            Bar.Opacity = 0
            Cell.Children.Add(Bar)
            AniStart(AaOpacity(Bar, 1, 240, i * 32, New AniEaseOutFluent), "TrendBar " & i)
            Grid.SetColumn(Cell, i)
            PanTrend.Children.Add(Cell)
        Next
    End Sub

    Private Shared Function FormatUptime(Seconds As Integer) As String
        If Seconds < 60 Then Return Seconds.ToString() & " 秒"
        Dim Ts As TimeSpan = TimeSpan.FromSeconds(Seconds)
        If Ts.TotalDays >= 1 Then Return CInt(Math.Floor(Ts.TotalDays)).ToString() & " 天 " & Ts.Hours.ToString() & " 时"
        If Ts.TotalHours >= 1 Then Return CInt(Math.Floor(Ts.TotalHours)).ToString() & " 时 " & Ts.Minutes.ToString() & " 分"
        Return Ts.Minutes.ToString() & " 分"
    End Function

    Private Shared Function FormatSize(Kb As Double) As String
        Dim Mb As Double = Kb / 1024
        If Mb >= 1024 Then Return (Mb / 1024).ToString("0.0") & " GB"
        Return Mb.ToString("0") & " MB"
    End Function

#End Region

End Class
