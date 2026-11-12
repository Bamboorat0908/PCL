Imports System.IO
Imports System.Net.Http
Imports System.Windows.Documents
Imports System.Windows.Media
Imports Newtonsoft.Json.Linq

''' <summary>
''' BRS 页面：读取 zhushu.homes/BRS 的状态接口，用启动器自带 UI 单页展示服务器信息。
''' </summary>
Public Class PageBRS
    Implements IRefreshable

    Private Const ApiUrl As String = "https://zhushu.homes/BRS/api/status"
    Private Const UserAgent As String = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0 Safari/537.36"

    Private Shared ReadOnly CachePath As String = Paths.Base & "BRCL\BrsCache.json"

    '全进程复用同一个 HttpClient，避免每次刷新重建连接池（借鉴 HttpClient 使用规范）
    Private Shared ReadOnly Http As New HttpClient()

    Private IsInited As Boolean = False
    Private IsLoading As Boolean = False
    Private TimerRefresh As Threading.DispatcherTimer
    Private _LastSizeH As Double = 0
    Private _LastFetchTick As Integer = -100000
    '同一份数据 30 秒内不重复请求，避免来回切页把接口打爆
    Private Const MinFetchIntervalMs As Integer = 30000

    Shared Sub New()
        Try
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 Or SecurityProtocolType.Tls11 Or SecurityProtocolType.Tls
            Http.Timeout = TimeSpan.FromSeconds(12)
            Http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", UserAgent)
            Http.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json, text/plain, */*")
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
        If Not IsInited Then
            IsInited = True
            '首次进入先放占位，避免几秒的“空卡片”像是坏了
            ShowPlaceholders()
            '先渲染上次缓存，避免首屏空白或断网时一片“—”
            TryRenderCache()
        End If
        LoadData()
        StartTimer()
    End Sub

    Private Sub PageBRS_Unloaded(sender As Object, e As RoutedEventArgs) Handles Me.Unloaded
        StopTimer()
    End Sub

    Private Sub StartTimer()
        StopTimer()
        '60 秒自动刷新（离开页面时停、回来时重建，修复返回后不再自动刷新的问题）
        TimerRefresh = New Threading.DispatcherTimer With {.Interval = TimeSpan.FromSeconds(60)}
        AddHandler TimerRefresh.Tick, Sub() LoadData()
        TimerRefresh.Start()
    End Sub

    Private Sub StopTimer()
        Try
            If TimerRefresh IsNot Nothing Then
                TimerRefresh.Stop()
                RemoveHandler TimerRefresh.Tick, Sub() LoadData()
                TimerRefresh = Nothing
            End If
        Catch ex As Exception
        End Try
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
        If IsLoading Then Return
        If Not Force AndAlso Environment.TickCount - _LastFetchTick < MinFetchIntervalMs AndAlso LabTps.Text <> "—" Then Return
        IsLoading = True
        Try
            BtnRefresh.IsEnabled = False
            If LabUpdated.Text = "尚未刷新" Then LabUpdated.Text = "正在刷新…"
        Catch ex As Exception
        End Try
        RunInNewThread(
        Sub()
            Dim Result As JObject = Nothing
            Dim [Error] As String = Nothing
            Try
                Dim Body As String = Http.GetStringAsync(ApiUrl).GetAwaiter().GetResult()
                Result = JObject.Parse(Body)
                '写缓存，供下次启动或断网时兜底
                Try
                    DirectoryUtils.Create(Path.GetDirectoryName(CachePath))
                    File.WriteAllText(CachePath, Body)
                Catch ex As Exception
                End Try
            Catch ex As Exception
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
                    ApplyError([Error])
                Else
                    _LastFetchTick = Environment.TickCount
                    ApplyData(Result)
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
            .Text = "正在获取近 14 天数据…", .FontSize = 12, .Opacity = 0.55,
            .VerticalAlignment = VerticalAlignment.Center, .HorizontalAlignment = HorizontalAlignment.Center})
    End Sub

    ''' <summary>
    ''' 用本地缓存先渲染一次，保证首屏不空白、断网也有内容（参考 Prism 的新闻缓存兜底）。
    ''' </summary>
    Private Sub TryRenderCache()
        Try
            If Not File.Exists(CachePath) Then Return
            Dim Body As String = File.ReadAllText(CachePath)
            If String.IsNullOrWhiteSpace(Body) Then Return
            ApplyData(JObject.Parse(Body), True)
            Logger.Info("BRS 状态：已使用本地缓存渲染首屏")
        Catch ex As Exception
            Logger.Warn(ex, "读取 BRS 缓存失败")
        End Try
    End Sub

    Private Sub ApplyError(Message As String)
        '已有数据（内存或缓存）时不清屏，只降级提示，避免一次网络抖动把整页打成空白
        If LabTps.Text <> "—" Then
            LabUpdated.Text = "刷新失败，展示的是上次数据"
            Return
        End If
        LabState.Text = "连接失败"
        BadgeState.Background = New SolidColorBrush(Color.FromRgb(&HC9, &H20, &H3F))
        LabUpdated.Text = "刷新失败，将自动重试"
        LabMotd.Text = "无法获取服务器状态：" & Message
    End Sub

    Private Sub ApplyData(Json As JObject, Optional FromCache As Boolean = False)
        Try
            Dim Status As JObject = TryCast(Json("status"), JObject)
            Dim Alive As Boolean = Status IsNot Nothing AndAlso CBool(Status("alive"))
            '状态徽章
            If Alive Then
                LabState.Text = "运行中"
                BadgeState.Background = New SolidColorBrush(Color.FromRgb(&H0B, &H73, &H50))
            Else
                LabState.Text = "离线"
                BadgeState.Background = New SolidColorBrush(Color.FromRgb(&HC9, &H20, &H3F))
            End If

            Dim MaxPlayers As Integer = If(Status Is Nothing, 0, CInt(Status("max_players")))
            Dim Count As Integer = If(Status Is Nothing, 0, CInt(Status("player_count")))
            LabPlayers.Text = Count.ToString()
            LabPlayersMax.Text = "/" & MaxPlayers.ToString()

            'MOTD
            Dim Motd As String = If(Status Is Nothing, "", If(Status("motd") Is Nothing, "", Status("motd").ToString()))
            RenderMotd(Motd.Replace("%onlineplayers%", Count.ToString()))

            '赛季
            Dim Season As JObject = TryCast(Json("season"), JObject)
            If Season IsNot Nothing AndAlso Season("day") IsNot Nothing Then
                Dim Day As Integer = CInt(Season("day"))
                Dim Qq As String = ""
                Try
                    If Json("qq") IsNot Nothing AndAlso Json("qq")("group") IsNot Nothing Then Qq = " · QQ群 " & Json("qq")("group").ToString()
                Catch ex As Exception
                End Try
                LabSeason.Text = "第九周目 · 第 " & Day.ToString() & " 天" & Qq
                LabSeasonDay.Text = "开服第 " & Day.ToString() & " 天"
            Else
                LabSeason.Text = ""
                LabSeasonDay.Text = "—"
            End If

            'TPS / MSPT
            If Status IsNot Nothing AndAlso Status("tps_overall_ms") IsNot Nothing Then
                Dim Mspt As Double = CDbl(Status("tps_overall_ms"))
                Dim Tps As Double = Math.Min(20.0, 1000.0 / Math.Max(Mspt, 0.1))
                LabTps.Text = Tps.ToString("0.0")
                LabMspt.Text = Mspt.ToString("0.0") & " ms/tick"
            Else
                LabTps.Text = "—"
                LabMspt.Text = "— ms/tick"
            End If

            '运行时长
            If Status IsNot Nothing AndAlso Status("uptime_sec") IsNot Nothing Then
                LabUptime.Text = FormatUptime(CInt(Status("uptime_sec")))
            Else
                LabUptime.Text = "—"
            End If

            '实体 / 世界 / 崩溃
            LabEntities.Text = If(Status IsNot Nothing AndAlso Status("entities") IsNot Nothing, CInt(Status("entities")).ToString(), "—")
            If Status IsNot Nothing AndAlso Status("world_size_kb") IsNot Nothing Then
                LabWorld.Text = FormatSize(CDbl(Status("world_size_kb")))
            Else
                LabWorld.Text = "—"
            End If
            LabCrash.Text = If(Status IsNot Nothing AndAlso Status("crash_reports") IsNot Nothing, "崩溃报告 " & CInt(Status("crash_reports")).ToString() & " 份", "—")

            '在线玩家
            PanPlayers.Children.Clear()
            Dim Players As JArray = If(Status Is Nothing, Nothing, TryCast(Status("players"), JArray))
            If Players IsNot Nothing AndAlso Players.Count > 0 Then
                LabPlayerCount.Text = "共 " & Players.Count.ToString() & " 人"
                For Each P As JToken In Players
                    PanPlayers.Children.Add(CreatePlayerChip(P.ToString()))
                Next
            Else
                LabPlayerCount.Text = "无人在线"
                Dim Empty As New TextBlock With {.Text = "当前没有玩家在线，晚点再来看看～", .FontSize = 12, .Opacity = 0.6, .Margin = New Thickness(0, 2, 0, 6)}
                PanPlayers.Children.Add(Empty)
                Dim BtnRetryPlayers As New MyButton With {.Text = "重新检测", .Height = 22, .MinWidth = 66, .Padding = New Thickness(8, 0, 8, 0), .HorizontalAlignment = HorizontalAlignment.Left}
                AddHandler BtnRetryPlayers.Click, Sub(sender2, e2) LoadData()
                PanPlayers.Children.Add(BtnRetryPlayers)
            End If
            If LabPlayerFoot IsNot Nothing Then
                LabPlayerFoot.Text = "上限 " & MaxPlayers.ToString() & " 人 · 数据来自 zhushu.homes/BRS"
            End If

            '趋势
            RenderTrend(TryCast(Json("history"), JObject))

            '更新时间
            Dim Updated As String = If(Json("updated_at") Is Nothing, "", Json("updated_at").ToString())
            Dim TimeText As String = "更新于 " & If(Updated.Length >= 16, Updated.Substring(11, 5), "—")
            If FromCache Then
                TimeText &= "（本地缓存）"
            ElseIf IsStale(Json) Then
                TimeText &= "（源站数据滞后）"
            End If
            LabUpdated.Text = TimeText
        Catch ex As Exception
            Logger.Warn(ex, "解析 BRS 状态数据失败")
            ApplyError(ex.Message)
        End Try
    End Sub

    Private Shared Function IsStale(Json As JObject) As Boolean
        Try
            Return Json("stale") IsNot Nothing AndAlso CBool(Json("stale"))
        Catch
            Return False
        End Try
    End Function

#End Region

#Region "渲染"

    Private Sub RenderMotd(Motd As String)
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
        Return Brush
    End Function

    Private Function CreatePlayerChip(Name As String) As FrameworkElement
        Dim B As New Border With {
            .Background = CType(Application.Current.Resources("ColorBrush6"), Brush),
            .CornerRadius = New CornerRadius(9),
            .Padding = New Thickness(9, 3, 9, 4),
            .Margin = New Thickness(0, 0, 6, 6)
        }
        B.Child = New TextBlock With {.Text = Name, .FontSize = 12, .Foreground = CType(Application.Current.Resources("ColorBrush1"), Brush)}
        Return B
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
            Return
        End If
        Dim Items As New List(Of KeyValuePair(Of String, Integer))
        For Each P As JProperty In History.Properties()
            Dim V As Integer = 0
            Integer.TryParse(P.Value.ToString(), V)
            Items.Add(New KeyValuePair(Of String, Integer)(P.Name, V))
        Next
        Items.Sort(Function(A, B) String.CompareOrdinal(A.Key, B.Key))
        If Items.Count > 14 Then Items = Items.GetRange(Items.Count - 14, 14)

        Dim MaxV As Integer = 1
        For Each It In Items
            If It.Value > MaxV Then MaxV = It.Value
        Next
        LabTrendInfo.Text = "峰值 " & MaxV.ToString() & " 人"
        LabAxisStart.Text = Items(0).Key.Substring(5)
        LabAxisMid.Text = Items(Items.Count \ 2).Key.Substring(5)
        LabAxisEnd.Text = Items(Items.Count - 1).Key.Substring(5)
        If LabPlayerFoot IsNot Nothing Then
            LabPlayerFoot.Text = "上限 " & LabPlayersMax.Text.TrimStart("/"c) & " 人 · 近 14 天峰值 " & MaxV.ToString() & " 人"
        End If

        Dim TrackH As Double = If(PanTrend.ActualHeight > 40, PanTrend.ActualHeight - 2, 62)
        For i As Integer = 0 To Items.Count - 1
            Dim It = Items(i)
            PanTrend.ColumnDefinitions.Add(New ColumnDefinition())
            Dim Cell As New Grid With {.Margin = New Thickness(3, 0, 3, 0)}
            Cell.RowDefinitions.Add(New RowDefinition With {.Height = New GridLength(1, GridUnitType.Star)})
            Cell.RowDefinitions.Add(New RowDefinition With {.Height = New GridLength(1, GridUnitType.Star)})
            '底槽
            Dim Track As New Border With {
                .Background = CType(Application.Current.Resources("ColorBrush6"), Brush),
                .Opacity = 0.55,
                .CornerRadius = New CornerRadius(4),
                .VerticalAlignment = VerticalAlignment.Bottom,
                .Height = TrackH
            }
            Grid.SetRowSpan(Track, 2)
            Cell.Children.Add(Track)
            '柱
            Dim BarH As Double = Math.Max(4, TrackH * It.Value / MaxV)
            Dim Bar As New Border With {
                .Background = CType(Application.Current.Resources("ColorBrush3"), Brush),
                .CornerRadius = New CornerRadius(4),
                .VerticalAlignment = VerticalAlignment.Bottom,
                .Height = BarH,
                .ToolTip = It.Key.Substring(5) & " · " & It.Value.ToString() & " 人"
            }
            Grid.SetRowSpan(Bar, 2)
            Cell.Children.Add(Bar)
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
