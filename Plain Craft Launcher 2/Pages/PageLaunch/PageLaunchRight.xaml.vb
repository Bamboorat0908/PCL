Imports System.IO
Imports System.Net.Http
Imports System.Threading
Imports System.Windows.Media.Animation
Imports System.Windows.Media.Imaging
Imports System.Windows.Threading
Imports Newtonsoft.Json.Linq

'BRS 专属首页。账号与启动仍由 PageLaunchLeft 的原有链路处理。
Public Class PageLaunchRight
    Implements IRefreshable

    Private Shared ReadOnly Http As HttpClient = CreateClient()
    Private ReadOnly Timer As New DispatcherTimer(DispatcherPriority.Background) With {.Interval = TimeSpan.FromSeconds(60)}
    Private Request As CancellationTokenSource
    Private Busy As Boolean
    Private HasData As Boolean
    Private CacheReadStarted As Boolean
    Private PendingStatus As JObject
    Private PendingIsCache As Boolean
    Private ReadOnly FetchClock As Diagnostics.Stopwatch = Diagnostics.Stopwatch.StartNew()
    Private LastFetchTick As Long = -60000
    Private HostWindow As Window
    Private ReadOnly CachePath As String = Paths.Base & "BRCL\HomeStatus.json"
    Private CalendarDate As DateTime = DateTime.MinValue
    Private FortunePath As String = Paths.Base & "BRCL\DailyFortune.json"
    Private CurrentFortune As BrclFortuneResult
    Private FortuneBusy As Boolean
    Private FortuneVersion As Integer
    Private RequestedHeroWidth As Integer
    Private HeroDecodeRunning As Boolean
    Private Const HeroImageWidth As Integer = 1672
    Private Const HeroImageHeight As Integer = 941
    Private Shared ReadOnly HeroImageUri As New Uri("pack://application:,,,/BRCL;component/Images/BrsMachinist.jpg")

    Private Sub UpdateHeroImage(sender As Object, e As SizeChangedEventArgs) Handles Hero.SizeChanged
        UpdateHeroImage(VisualTreeHelper.GetDpi(Hero))
    End Sub

    Protected Overrides Sub OnDpiChanged(OldDpi As DpiScale, NewDpi As DpiScale)
        MyBase.OnDpiChanged(OldDpi, NewDpi)
        If Hero IsNot Nothing Then UpdateHeroImage(NewDpi)
    End Sub

    Private Async Sub UpdateHeroImage(Dpi As DpiScale)
        If Hero.ActualWidth <= 0 OrElse Hero.ActualHeight <= 0 Then Return
        'UniformToFill 可能按高度放大后裁边。按实际物理像素选择解码尺寸，并合并相近的窗口尺寸。
        Dim Required = Math.Max(Hero.ActualWidth * Dpi.DpiScaleX,
                                Hero.ActualHeight * Dpi.DpiScaleY * HeroImageWidth / HeroImageHeight)
        RequestedHeroWidth = CInt(Math.Min(HeroImageWidth, Math.Ceiling(Required / 256) * 256))
        If HeroDecodeRunning Then Return

        HeroDecodeRunning = True
        Try
            Do
                Dim DecodeWidth = RequestedHeroWidth
                If TryCast(HeroImage.Source, BitmapImage)?.DecodePixelWidth = DecodeWidth Then Exit Do
                Dim Bitmap = Await Task.Run(Function() DecodeHeroImage(DecodeWidth))
                'If the window was resized during decoding, reuse the result only when it still fits.
                If DecodeWidth = RequestedHeroWidth Then
                    HeroImage.Source = Bitmap
                    Exit Do
                End If
            Loop
        Catch ex As Exception
            Logger.Warn(ex, "解码首页背景图失败")
        Finally
            HeroDecodeRunning = False
        End Try
    End Sub

    Private Shared Function DecodeHeroImage(DecodeWidth As Integer) As BitmapImage
        Dim Bitmap As New BitmapImage()
        Bitmap.BeginInit()
        Bitmap.UriSource = HeroImageUri
        Bitmap.CacheOption = BitmapCacheOption.OnLoad
        Bitmap.DecodePixelWidth = DecodeWidth
        Bitmap.EndInit()
        Bitmap.Freeze()
        Return Bitmap
    End Function

    Private Shared Function CreateClient() As HttpClient
        Dim Client As New HttpClient With {.Timeout = TimeSpan.FromSeconds(12), .MaxResponseContentBufferSize = 4 * 1024 * 1024}
        Client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 BRCL")
        Return Client
    End Function

    Private Sub Init() Handles Me.Loaded
        UpdateCalendar(DateTime.Today)
        HostWindow = Window.GetWindow(Me)
        If HostWindow IsNot Nothing Then
            RemoveHandler HostWindow.StateChanged, AddressOf ActivityChanged
            AddHandler HostWindow.StateChanged, AddressOf ActivityChanged
        End If
        PanLog.Visibility = ModeDebug.ToVisibility
        If PendingStatus IsNot Nothing Then
            ApplyData(PendingStatus, PendingIsCache)
            PendingStatus = Nothing
        End If
        Timer.Stop()
        RemoveHandler Timer.Tick, AddressOf OnTimer
        AddHandler Timer.Tick, AddressOf OnTimer
        Timer.Start()
        'Let WPF present the page before starting cache/network work.
        Dim Initialization = Dispatcher.BeginInvoke(DispatcherPriority.Background, New Action(
            Sub()
                If Not IsLoaded Then Return
                If Not CacheReadStarted Then
                    CacheReadStarted = True
                    ReadCachedStatus()
                End If
                LoadStatus()
            End Sub))
    End Sub

    Private Sub LeavePage() Handles Me.Unloaded
        Timer.Stop()
        Request?.Cancel()
        StopFortuneMotion()
        If HostWindow IsNot Nothing Then RemoveHandler HostWindow.StateChanged, AddressOf ActivityChanged
        HostWindow = Nothing
    End Sub

    Private Sub ActivityChanged(sender As Object, e As EventArgs)
        If Not PageActive Then
            Timer.Stop()
            Return
        End If
        UpdateCalendar(DateTime.Today)
        If PendingStatus IsNot Nothing Then
            ApplyData(PendingStatus, PendingIsCache)
            PendingStatus = Nothing
        End If
        Timer.Start()
        LoadStatus()
    End Sub

    Private Sub VisibilityChanged(sender As Object, e As DependencyPropertyChangedEventArgs) Handles Me.IsVisibleChanged
        ActivityChanged(sender, EventArgs.Empty)
    End Sub

    Private Sub OnTimer(sender As Object, e As EventArgs)
        UpdateCalendar(DateTime.Today)
        LoadStatus()
    End Sub

    Public Sub ForceRefresh() Implements IRefreshable.Refresh
        LoadStatus(True)
    End Sub

    Private Sub RefreshClick(sender As Object, e As RoutedEventArgs) Handles BtnRefresh.Click
        LoadStatus(True)
    End Sub

    Private ReadOnly Property PageActive As Boolean
        Get
            Return IsLoaded AndAlso IsVisible AndAlso (FrmMain Is Nothing OrElse FrmMain.WindowState <> WindowState.Minimized)
        End Get
    End Property

    Private Async Sub ReadCachedStatus()
        Try
            Dim Cached = Await Task.Run(
                Function()
                    If Not File.Exists(CachePath) Then Return Nothing
                    Return PrepareHomeData(JObject.Parse(File.ReadAllText(CachePath)))
                End Function)
            'The live request may win the race. Never replace it with older disk data.
            If Cached Is Nothing OrElse HasData OrElse PendingStatus IsNot Nothing Then Return
            If PageActive Then
                ApplyData(Cached, True)
            Else
                PendingStatus = Cached
                PendingIsCache = True
            End If
        Catch ex As Exception
            Logger.Warn(ex, "读取 BRS 首页缓存失败")
        End Try
    End Sub

    Private Async Function FetchStatus(Current As CancellationToken) As Task(Of JObject)
        Dim Response = Await Http.GetAsync("https://zhushu.homes/BRS/api/status", Current).ConfigureAwait(False)
        Dim Body As String
        Using Response
            Response.EnsureSuccessStatusCode()
            Body = Await Response.Content.ReadAsStringAsync().ConfigureAwait(False)
        End Using
        Current.ThrowIfCancellationRequested()
        Dim Result = PrepareHomeData(JObject.Parse(Body))
        'Parsing and cache I/O stay on the worker continuation. The home page
        'stores only the metadata it displays, not the full rankings/history.
        Try
            Directory.CreateDirectory(Path.GetDirectoryName(CachePath))
            Dim Pending = CachePath & ".tmp"
            File.WriteAllText(Pending, Result.ToString(Newtonsoft.Json.Formatting.None))
            If File.Exists(CachePath) Then
                File.Replace(Pending, CachePath, Nothing)
            Else
                File.Move(Pending, CachePath)
            End If
        Catch ex As Exception
            Logger.Warn(ex, "写入 BRS 首页缓存失败")
        End Try
        Return Result
    End Function

    Private Async Sub LoadStatus(Optional Force As Boolean = False)
        If Busy OrElse Not PageActive Then Return
        If Not Force AndAlso HasData AndAlso FetchClock.ElapsedMilliseconds - LastFetchTick < 60000 Then Return
        Busy = True
        BtnRefresh.IsEnabled = False
        Dim Current As New CancellationTokenSource()
        Request = Current
        Try
            Dim Result = Await FetchStatus(Current.Token)
            Current.Token.ThrowIfCancellationRequested()
            LastFetchTick = FetchClock.ElapsedMilliseconds
            If PageActive Then
                ApplyData(Result, False)
            Else
                PendingStatus = Result
                PendingIsCache = False
            End If
        Catch ex As OperationCanceledException
            If Not Current.IsCancellationRequested AndAlso PageActive Then ShowUnavailable()
        Catch ex As Exception
            Logger.Warn(ex, "刷新 BRS 首页失败")
            If PageActive Then ShowUnavailable()
        Finally
            Dim Restart = Current.IsCancellationRequested AndAlso IsLoaded
            Busy = False
            BtnRefresh.IsEnabled = True
            If Request Is Current Then Request = Nothing
            Current.Dispose()
            If Restart Then
                Dim RestartOperation = Dispatcher.BeginInvoke(DispatcherPriority.Background, New Action(Sub() LoadStatus()))
            End If
        End Try
    End Sub

    Private Shared Function PrepareHomeData(Json As JObject) As JObject
        ValidateHomeData(Json)
        Dim Result As New JObject From {
            {"server", SelectHomeFields(TryCast(Json("server"), JObject), "pack", "season_label")},
            {"status", SelectHomeFields(TryCast(Json("status"), JObject), "alive", "player_count", "max_players")},
            {"season", SelectHomeFields(TryCast(Json("season"), JObject), "day")}}
        For Each Name As String In {"updated_at", "stale"}
            If Json(Name) IsNot Nothing Then Result(Name) = Json(Name).DeepClone()
        Next
        Return Result
    End Function

    Private Shared Function SelectHomeFields(Source As JObject, ParamArray Names As String()) As JObject
        Dim Result As New JObject
        If Source Is Nothing Then Return Result
        'Copy only what is displayed, avoiding even a temporary clone of player
        'rosters, rankings, and other large parts of the dashboard response.
        For Each FieldName As String In Names
            If Source(FieldName) IsNot Nothing Then Result(FieldName) = Source(FieldName).DeepClone()
        Next
        Return Result
    End Function

    Private Sub ShowUnavailable()
        LabHomeState.Text = "● 服务器状态未知"
        LabHomeState.Foreground = New Media.SolidColorBrush(Media.Color.FromRgb(217, 223, 215))
        LabHomePlayers.Text = "当前人数 —"
        LabHomeUpdated.Text = "连接失败 · " & If(HasData, "已保留上次周目信息 · ", "") & "点击刷新重试"
        If Not HasData Then
            LabHomeSeason.Text = "周目信息待同步"
            LabHomePack.Text = "BRS 专属整合包"
            LabHomePackEnglish.Text = "YOUR NEXT CHAPTER STARTS HERE"
            LabHomeDay.Text = "请检查网络后刷新"
        End If
    End Sub

    Private Sub ApplyData(Json As JObject, Cached As Boolean)
        ValidateHomeData(Json)
        Dim Server = TryCast(Json("server"), JObject)
        Dim Status = TryCast(Json("status"), JObject)
        If Server Is Nothing OrElse Status Is Nothing OrElse Status("alive")?.Type <> JTokenType.Boolean Then Throw New FormatException("无效的 BRS 首页数据")
        Dim Pack As String = Server.Value(Of String)("pack")
        Dim Season As String = Server.Value(Of String)("season_label")
        If String.IsNullOrWhiteSpace(Pack) OrElse String.IsNullOrWhiteSpace(Season) Then Throw New FormatException("缺少 BRS 周目或整合包信息")
        Dim Day = Json("season")?.Value(Of Integer?)("day")
        Dim Alive = Status.Value(Of Boolean)("alive")
        Dim Count = Status.Value(Of Integer?)("player_count")
        Dim Stale As Boolean = Cached OrElse Json.Value(Of Boolean?)("stale").GetValueOrDefault()
        Dim Updated As DateTimeOffset
        If DateTimeOffset.TryParse(Json("updated_at")?.ToString(), Updated) Then
            If DateTimeOffset.Now - Updated > TimeSpan.FromMinutes(5) Then Stale = True
        Else
            Stale = True
        End If
        '先解析全部字段，再提交 UI。
        Dim Split = Pack.IndexOf("（", StringComparison.Ordinal)
        LabHomePack.Text = If(Split >= 0, Pack.Substring(Split + 1).TrimEnd("）"c), Pack)
        LabHomePackEnglish.Text = If(Split >= 0, Pack.Substring(0, Split).Trim().ToUpperInvariant(), "BRS / EXCLUSIVE MODPACK")
        LabHomeSeason.Text = Season
        LabHomeDay.Text = If(Day.HasValue, "本周目已开启 " & Day.Value & " 天", "周目天数暂不可用")
        LabHomeState.Text = If(Stale, "● 服务器状态未知", If(Alive, "● 服务器在线", "○ 服务器离线"))
        LabHomeState.Foreground = New Media.SolidColorBrush(If(Stale, Media.Color.FromRgb(217, 223, 215), If(Alive, Media.Color.FromRgb(172, 223, 150), Media.Color.FromRgb(240, 168, 147))))
        LabHomePlayers.Text = "当前人数 " & If(Stale, "—", If(Not Alive, "0", If(Count.HasValue, Count.GetValueOrDefault().ToString(), "—")))
        LabHomeUpdated.Text = If(Stale, "数据已过期 · ", "已同步 · ") & If(Updated = DateTimeOffset.MinValue, "更新时间未知", Updated.ToString("MM-dd HH:mm")) & If(Cached, " · 本地缓存", "") & " · 每 60 秒刷新"
        HasData = True
    End Sub

    'The calendar is local and independent of server availability. Refresh on return
    'and every minute so an open page also rolls over correctly at midnight.
    Private Sub UpdateCalendar(Today As DateTime)
        Today = Today.Date
        If CalendarDate = Today Then Return
        CalendarDate = Today
        Dim Weekdays As String() = {"星期日", "星期一", "星期二", "星期三", "星期四", "星期五", "星期六"}
        LabCalendarDay.Text = Today.Day.ToString("00")
        LabCalendarWeekday.Text = Weekdays(CInt(Today.DayOfWeek))
        LabCalendarMonth.Text = Today.ToString("yyyy 年 M 月")
        LabCalendarLunar.Text = GetLunarLabel(Today)
        PanCalendarWeek.Children.Clear()
        Dim Monday = Today.AddDays(-((CInt(Today.DayOfWeek) + 6) Mod 7))
        Dim ShortWeekdays As String() = {"一", "二", "三", "四", "五", "六", "日"}
        For Index As Integer = 0 To 6
            Dim Day = Monday.AddDays(Index)
            Dim IsToday = Day = Today
            Dim Tile As New Border With {.CornerRadius = New CornerRadius(4), .Padding = New Thickness(0, 4, 0, 4), .Margin = New Thickness(1, 0, 1, 0), .ToolTip = Day.ToString("yyyy-MM-dd") & If(IsToday, " · 今天", "")}
            If IsToday Then Tile.Background = New Media.SolidColorBrush(Media.Color.FromRgb(242, 214, 73))
            Dim Labels As New StackPanel
            For Each Label As TextBlock In {
                New TextBlock With {.Text = ShortWeekdays(Index), .FontSize = 10, .HorizontalAlignment = HorizontalAlignment.Center},
                New TextBlock With {.Text = Day.Day.ToString(), .FontSize = 13, .FontWeight = If(IsToday, FontWeights.Bold, FontWeights.Normal), .Margin = New Thickness(0, 3, 0, 0), .HorizontalAlignment = HorizontalAlignment.Center}}
                If IsToday Then
                    Label.Foreground = New Media.SolidColorBrush(Media.Color.FromRgb(36, 40, 29))
                Else
                    Label.SetResourceReference(TextBlock.ForegroundProperty, If(Day.Month <> Today.Month, "ColorBrush2", "ColorBrush1"))
                End If
                Labels.Children.Add(Label)
            Next
            Tile.Child = Labels
            Automation.AutomationProperties.SetName(Tile, Day.ToString("yyyy年M月d日") & " " & Weekdays(CInt(Day.DayOfWeek)) & If(IsToday, " 今天", ""))
            PanCalendarWeek.Children.Add(Tile)
        Next
        LoadFortune(Today)
    End Sub

    Private Shared Function GetLunarLabel(Today As DateTime) As String
        Dim Lunar As New Globalization.ChineseLunisolarCalendar
        If Today < Lunar.MinSupportedDateTime OrElse Today > Lunar.MaxSupportedDateTime Then Return "每天，都是新的存档。"
        Dim Year = Lunar.GetYear(Today)
        Dim Month = Lunar.GetMonth(Today)
        Dim LeapMonth = Lunar.GetLeapMonth(Year)
        Dim IsLeap = LeapMonth > 0 AndAlso Month = LeapMonth
        If LeapMonth > 0 AndAlso Month >= LeapMonth Then Month -= 1
        Dim Months As String() = {"正", "二", "三", "四", "五", "六", "七", "八", "九", "十", "冬", "腊"}
        Dim Numbers As String() = {"一", "二", "三", "四", "五", "六", "七", "八", "九", "十"}
        Dim Day = Lunar.GetDayOfMonth(Today)
        Dim DayText As String
        If Day <= 10 Then
            DayText = "初" & Numbers(Day - 1)
        ElseIf Day < 20 Then
            DayText = "十" & Numbers(Day - 11)
        ElseIf Day = 20 Then
            DayText = "二十"
        ElseIf Day < 30 Then
            DayText = "廿" & Numbers(Day - 21)
        Else
            DayText = "三十"
        End If
        Return "农历" & If(IsLeap, "闰", "") & Months(Month - 1) & "月" & DayText
    End Function

    Private Async Sub LoadFortune(Today As DateTime)
        FortuneVersion += 1
        Dim Version = FortuneVersion
        FortuneBusy = True
        CurrentFortune = Nothing
        ShowFortune(Nothing)
        BtnDrawFortune.IsEnabled = False
        BtnDrawFortune.Content = "读取中…"
        Try
            Dim Result = Await Task.Run(Function() BrclDailyFortune.ReadForDate(FortunePath, Today))
            If Version <> FortuneVersion OrElse CalendarDate <> Today.Date Then Return
            ShowFortune(Result)
        Catch ex As Exception
            Logger.Warn(ex, "读取今日运势失败")
            If Version = FortuneVersion Then ShowFortune(Nothing, "运势记录暂不可用，请稍后重试。")
        Finally
            If Version = FortuneVersion Then FortuneBusy = False
        End Try
    End Sub

    Private Sub ShowFortune(Result As BrclFortuneResult, Optional ErrorHint As String = Nothing)
        StopFortuneMotion()
        CurrentFortune = Result
        BtnDrawFortune.Visibility = If(Result Is Nothing, Visibility.Visible, Visibility.Collapsed)
        PanFortuneResult.Visibility = If(Result Is Nothing, Visibility.Collapsed, Visibility.Visible)
        BtnDrawFortune.IsEnabled = True
        BtnDrawFortune.Content = If(ErrorHint Is Nothing, "抽取今日运势", "重试抽签")
        LabCalendarIdea.Text = If(Result Is Nothing, "", Result.Grade)
        LabCalendarHint.Text = If(ErrorHint, If(Result Is Nothing, "每天一签，收下一句专属祝福。", Result.Blessing))
    End Sub

    Private Sub StopFortuneMotion()
        FortuneShakeTransform.BeginAnimation(Media.RotateTransform.AngleProperty, Nothing)
        FortuneStickRise.BeginAnimation(Media.TranslateTransform.YProperty, Nothing)
        FortuneSparkle.BeginAnimation(UIElement.OpacityProperty, Nothing)
        PanFortuneResult.BeginAnimation(UIElement.OpacityProperty, Nothing)
        FortuneRevealOffset.BeginAnimation(Media.TranslateTransform.YProperty, Nothing)
        FortuneRevealScale.BeginAnimation(Media.ScaleTransform.ScaleXProperty, Nothing)
        FortuneRevealScale.BeginAnimation(Media.ScaleTransform.ScaleYProperty, Nothing)
        LabCalendarHint.BeginAnimation(UIElement.OpacityProperty, Nothing)
        FortuneBlessingOffset.BeginAnimation(Media.TranslateTransform.YProperty, Nothing)
        PanFortuneDrawing.Visibility = Visibility.Collapsed
        LabCalendarHint.Visibility = Visibility.Visible
    End Sub

    Private Sub BeginFortuneShake()
        StopFortuneMotion()
        BtnDrawFortune.Visibility = Visibility.Collapsed
        PanFortuneResult.Visibility = Visibility.Collapsed
        PanFortuneDrawing.Visibility = Visibility.Visible
        LabCalendarHint.Visibility = Visibility.Hidden
        Dim Sway As New DoubleAnimationUsingKeyFrames With {.FillBehavior = FillBehavior.Stop}
        Dim Angles As Double() = {0, -8, 8, -10, 10, -8, 8, -5, 5, -2, 2, 0}
        For Index As Integer = 0 To Angles.Length - 1
            Sway.KeyFrames.Add(New EasingDoubleKeyFrame(Angles(Index), KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(Index * 100)), New CubicEase With {.EasingMode = EasingMode.EaseInOut}))
        Next
        FortuneShakeTransform.BeginAnimation(Media.RotateTransform.AngleProperty, Sway)
        FortuneStickRise.BeginAnimation(Media.TranslateTransform.YProperty, New DoubleAnimation(0, -3, TimeSpan.FromMilliseconds(160)) With {.AutoReverse = True, .RepeatBehavior = New RepeatBehavior(3), .FillBehavior = FillBehavior.Stop})
        FortuneSparkle.BeginAnimation(UIElement.OpacityProperty, New DoubleAnimation(0.25, 1, TimeSpan.FromMilliseconds(280)) With {.AutoReverse = True, .RepeatBehavior = New RepeatBehavior(2), .FillBehavior = FillBehavior.Stop})
    End Sub

    Private Sub RevealFortune(Result As BrclFortuneResult)
        ShowFortune(Result)
        LabCalendarHint.Visibility = Visibility.Hidden
        PanFortuneResult.BeginAnimation(UIElement.OpacityProperty, New DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(280)))
        FortuneRevealOffset.BeginAnimation(Media.TranslateTransform.YProperty, New DoubleAnimation(8, 0, TimeSpan.FromMilliseconds(350)) With {.EasingFunction = New CubicEase With {.EasingMode = EasingMode.EaseOut}})
        Dim Pop As New DoubleAnimation(0.88, 1, TimeSpan.FromMilliseconds(380)) With {.EasingFunction = New BackEase With {.EasingMode = EasingMode.EaseOut, .Amplitude = 0.3}}
        FortuneRevealScale.BeginAnimation(Media.ScaleTransform.ScaleXProperty, Pop)
        FortuneRevealScale.BeginAnimation(Media.ScaleTransform.ScaleYProperty, Pop)
    End Sub

    Private Sub RevealFortuneBlessing()
        LabCalendarHint.Visibility = Visibility.Visible
        LabCalendarHint.BeginAnimation(UIElement.OpacityProperty, New DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(320)))
        FortuneBlessingOffset.BeginAnimation(Media.TranslateTransform.YProperty, New DoubleAnimation(4, 0, TimeSpan.FromMilliseconds(320)) With {.EasingFunction = New CubicEase With {.EasingMode = EasingMode.EaseOut}})
    End Sub

    Private Async Sub DrawFortune(sender As Object, e As RoutedEventArgs) Handles BtnDrawFortune.Click
        If FortuneBusy Then Return
        Dim Today = DateTime.Today
        If CalendarDate <> Today Then
            UpdateCalendar(Today)
            Return
        End If
        If CurrentFortune IsNot Nothing AndAlso CurrentFortune.Day = Today Then Return
        FortuneBusy = True
        FortuneVersion += 1
        Dim Version = FortuneVersion
        BtnDrawFortune.IsEnabled = False
        BtnDrawFortune.Content = "抽签中…"
        Dim PlayMotion = UiMotionEnabled
        If PlayMotion Then BeginFortuneShake()
        Try
            'Persist immediately; the short ceremony only controls presentation.
            'Leaving the page or closing the launcher cannot reroll a saved draw.
            Dim Saved = Task.Run(Function() BrclDailyFortune.DrawForDate(FortunePath, Today))
            Await Task.WhenAll(Saved, Task.Delay(If(PlayMotion, 1200, 0)))
            Dim Result = Await Saved
            If Version <> FortuneVersion Then Return
            If DateTime.Today <> Today Then
                UpdateCalendar(DateTime.Today)
                Return
            End If
            If Not PlayMotion Then
                ShowFortune(Result)
                Return
            End If
            RevealFortune(Result)
            Await Task.Delay(450)
            If Version <> FortuneVersion Then Return
            If DateTime.Today <> Today Then
                UpdateCalendar(DateTime.Today)
                Return
            End If
            RevealFortuneBlessing()
            Await Task.Delay(320)
            If Version = FortuneVersion Then StopFortuneMotion()
        Catch ex As Exception
            Logger.Warn(ex, "保存今日运势失败")
            If Version = FortuneVersion Then ShowFortune(Nothing, "暂时无法读取或保存运势，请稍后重试。")
        Finally
            If Version = FortuneVersion Then FortuneBusy = False
        End Try
    End Sub

    Private Shared Sub ValidateHomeData(Json As JObject)
        Dim Server = TryCast(Json("server"), JObject)
        Dim Status = TryCast(Json("status"), JObject)
        If Server Is Nothing OrElse Status Is Nothing Then Throw New FormatException("缺少 BRS 服务器信息")
        If Status("alive") Is Nothing OrElse Status("alive").Type <> JTokenType.Boolean Then Throw New FormatException("无效的 BRS 状态")
        For Each FieldName As String In {"pack", "season_label"}
            If Server(FieldName)?.Type <> JTokenType.String OrElse String.IsNullOrWhiteSpace(Server.Value(Of String)(FieldName)) Then Throw New FormatException("缺少 BRS 周目或整合包信息")
        Next
        For Each Value In {Status("player_count"), Status("max_players"), Json("season")?("day")}
            If Value Is Nothing OrElse Value.Type = JTokenType.Null Then Continue For
            Dim Number As Integer
            If Value.Type <> JTokenType.Integer OrElse Not Integer.TryParse(Value.ToString(), Number) OrElse Number < 0 Then Throw New FormatException("无效的 BRS 数量字段")
        Next
    End Sub

    Private Sub AdaptiveHero(sender As Object, e As SizeChangedEventArgs) Handles Me.SizeChanged, CalendarCard.SizeChanged, HomeHeader.SizeChanged, LabHomeUpdated.SizeChanged
        If ActualWidth <= 0 OrElse ActualHeight <= 0 Then Return
        Dim WideCalendar = ActualWidth >= 620
        Dim WeekWidth = If(WideCalendar, Math.Min(260, Math.Max(210, ActualWidth * 0.3)), Math.Min(280, Math.Max(140, ActualWidth - 196)))
        CalendarWeekColumn.Width = If(WideCalendar, New GridLength(WeekWidth), New GridLength(1, GridUnitType.Star))
        CalendarIdeaColumn.Width = If(WideCalendar, New GridLength(1, GridUnitType.Star), New GridLength(0))
        PanCalendarWeek.Width = WeekWidth
        Grid.SetColumn(CalendarIdea, If(WideCalendar, 3, 0))
        Grid.SetRow(CalendarIdea, If(WideCalendar, 0, 2))
        Grid.SetColumnSpan(CalendarIdea, If(WideCalendar, 1, 4))
        Grid.SetRowSpan(CalendarIdea, If(WideCalendar, 2, 1))
        CalendarIdea.BorderThickness = If(WideCalendar, New Thickness(1, 0, 0, 0), New Thickness(0, 1, 0, 0))
        CalendarIdea.Margin = If(WideCalendar, New Thickness(20, 0, 0, 0), New Thickness(0, 12, 0, 0))
        CalendarIdea.Padding = If(WideCalendar, New Thickness(20, 0, 0, 0), New Thickness(0, 10, 0, 0))
        Grid.SetColumn(CalendarIdeaText, If(WideCalendar, 0, 1))
        Grid.SetRow(CalendarIdeaText, If(WideCalendar, 1, 0))
        Grid.SetColumnSpan(CalendarIdeaText, If(WideCalendar, 2, 1))
        CalendarIdeaText.Margin = If(WideCalendar, New Thickness(0, 8, 0, 0), New Thickness(0))
        CalendarIdeaLabel.Margin = If(WideCalendar, New Thickness(0), New Thickness(0, 1, 10, 0))
        LabHomePack.FontSize = If(ActualWidth < 530, 30, 38)
        'Reserve the measured card/footer heights instead of guessing a fixed
        'offset. The hero yields space as the calendar reflows or text wraps.
        Dim Reserved = PanMain.Margin.Top + PanMain.Margin.Bottom +
            HomeHeader.ActualHeight + HomeHeader.Margin.Bottom +
            CalendarCard.ActualHeight + CalendarCard.Margin.Top +
            LabHomeUpdated.ActualHeight + LabHomeUpdated.Margin.Top + 8
        Hero.Height = Math.Max(224, Math.Min(360, ActualHeight - Reserved))
    End Sub
End Class
