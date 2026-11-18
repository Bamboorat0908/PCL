Imports System.Globalization
Imports System.Numerics
Imports System.Threading
Imports System.Windows.Media

Public Class PageBank
    Implements IRefreshable

    Private ReadOnly IdentityTimer As New Threading.DispatcherTimer With {.Interval = TimeSpan.FromSeconds(1)}
    Private Client As BrclBank.BankClient
    Private AccountCancel As CancellationTokenSource
    Private HistoryCancel As CancellationTokenSource
    Private Identity As String = ""
    Private Generation As Integer
    Private HistoryVersion As Integer
    Private Accounts As New BrclBank.BankAccounts
    Private Points As New List(Of BrclBank.BankPoint)
    Private SelectedTab As String = "account"
    Private SelectedDays As Integer = 7
    Private IsPrivate As Boolean
    Private SelectedId As String
    Private IsBusy As Boolean
    Private HistoryFailed As Boolean

    Public Sub New()
        InitializeComponent()
        AddHandler IdentityTimer.Tick, AddressOf CheckIdentity
    End Sub

    Private Sub Page_Loaded() Handles Me.Loaded
        IdentityTimer.Start()
        CheckIdentity()
        UpdateLayoutMode()
    End Sub

    Private Sub Page_Unloaded() Handles Me.Unloaded
        IdentityTimer.Stop()
        ResetConnection()
        Identity = ""
        ClearData()
    End Sub

    Private Function CurrentIdentity() As String
        If Settings.Get(Of McLoginType)("LoginType") <> McLoginType.Ms Then Return ""
        Dim Token = Settings.Get(Of String)("CacheMsV2Access")
        Dim Uuid = Settings.Get(Of String)("CacheMsV2Uuid")
        If Token.Length = 0 OrElse Uuid.Length = 0 Then Return ""
        If Settings.Get(Of Long)("CacheMsV2Expires") <= GetUnixTimestampUtc() + 30 Then Return ""
        Return Uuid & "|" & Token
    End Function

    Private Sub CheckIdentity()
        Dim Current = CurrentIdentity()
        If Current <> Identity Then
            ResetConnection()
            ClearData()
            Identity = Current
            RefreshPage()
        ElseIf Current.Length = 0 Then
            ShowNotice("登录后，自动关联你的团队银行", "使用启动器的微软正版账号登录，即可查看你获得直接授权的团队账户。", True)
        End If
    End Sub

    Private Sub ResetConnection()
        Generation += 1
        HistoryVersion += 1
        AccountCancel?.Cancel()
        HistoryCancel?.Cancel()
        Client?.Dispose()
        Client = Nothing
        SetBusy(False)
    End Sub

    Private Sub ClearData()
        HistoryVersion += 1
        HistoryCancel?.Cancel()
        Accounts = New BrclBank.BankAccounts
        Points.Clear()
        SelectedId = Nothing
        PanAccountChoices.Children.Clear()
        PanDetails.Visibility = Visibility.Collapsed
        BtnDetails.Text = "查看当前账户详情"
        LabAccountName.Text = "尚未关联团队账户"
        LabAccountTail.Text = "登录后自动关联，无需另设银行密码。"
        LabConnection.Text = "待关联"
        LabAccountCount.Text = "尚未关联"
        LabAccountId.Text = ""
        LabSync.Text = "等待登录"
        LabWelcome.Text = "查看团队资金，掌握账户变化。"
        RenderMoney()
        UpdateTabs()
    End Sub

    Private Sub SetBusy(Value As Boolean)
        IsBusy = Value
        BtnRefresh.IsEnabled = Not Value
        BtnRefresh.Text = If(Value, "同步中", "刷新")
        PanAccountChoices.IsEnabled = Not Value
    End Sub

    Private Sub ShowNotice(Title As String, Description As String, Login As Boolean)
        CardNotice.Visibility = Visibility.Visible
        LabNoticeTitle.Text = Title
        LabNotice.Text = Description
        BtnLogin.Visibility = If(Login, Visibility.Visible, Visibility.Collapsed)
    End Sub

    Public Async Sub RefreshPage() Implements IRefreshable.Refresh
        If IsBusy Then Return
        Dim Current = CurrentIdentity()
        If Current <> Identity Then
            ResetConnection()
            ClearData()
            Identity = Current
        End If
        If Current.Length = 0 Then
            ShowNotice("登录后，自动关联你的团队银行", "使用启动器的微软正版账号登录，即可查看你获得直接授权的团队账户。", True)
            Return
        End If
        Dim Address As Uri
        Try
            Address = BrclBank.BankServiceAddress()
        Catch ex As Exception
            ResetConnection()
            ClearData()
            ShowNotice("银行连接配置需要更新", "银行服务地址不可用，请联系服务器管理员更新启动器配置。", False)
            Return
        End Try
        LabWelcome.Text = Settings.Get(Of String)("CacheMsV2Name") & "，欢迎回来。"
        If Address Is Nothing Then
            ShowNotice("银行服务准备中", "微软账号已识别。服务器开放银行服务后，这里将自动关联你有权限访问的团队账户。", False)
            LabSync.Text = "账号已识别 · 等待银行服务开放"
            Return
        End If
        If Client Is Nothing Then Client = New BrclBank.BankClient(Address)
        Dim Version = Generation
        Dim Player = Settings.Get(Of String)("CacheMsV2Uuid")
        Dim Token = Settings.Get(Of String)("CacheMsV2Access")
        AccountCancel?.Cancel()
        AccountCancel = New CancellationTokenSource
        Dim Cancel = AccountCancel.Token
        SetBusy(True)
        LabSync.Text = "正在同步账户"
        Try
            Dim Data = Await Client.GetAccounts(Token, Player, Cancel)
            If Cancel.IsCancellationRequested OrElse Version <> Generation OrElse CurrentIdentity() <> Current Then Return
            ApplyAccounts(Data)
        Catch ex As OperationCanceledException
            If Not Cancel.IsCancellationRequested AndAlso Version = Generation Then ApplyFailure("service_unavailable")
        Catch ex As Exception
            If Version = Generation AndAlso CurrentIdentity() = Current Then
                Dim Failure = TryCast(ex, BrclBank.BankServiceException)
                ApplyFailure(If(Failure Is Nothing, "service_unavailable", Failure.Code))
            End If
        Finally
            If Version = Generation Then SetBusy(False)
        End Try
    End Sub

    Private Sub ApplyFailure(Code As String)
        'A failed authorization check must not leave a previous account visible.
        ResetConnection()
        ClearData()
        LabSync.Text = "同步未完成"
        Select Case Code
            Case "unauthorized", "identity_mismatch"
                Client?.Dispose() : Client = Nothing
                ShowNotice("银行登录需要重新验证", "请先刷新启动器的微软登录状态，再返回银行重试。", True)
            Case "rate_limited"
                ShowNotice("查询较为频繁", "请稍等片刻后点击刷新。", False)
            Case "account_unavailable"
                ShowNotice("账户暂不可访问", "账户可能已移除，或你的访问授权已变化。请刷新后重试。", False)
            Case Else
                ShowNotice("暂时无法连接银行", "请检查网络连接后刷新；服务器维护期间，银行数据可能暂时不可用。", False)
        End Select
    End Sub

    Private Sub ApplyAccounts(Data As BrclBank.BankAccounts)
        Dim Previous = SelectedAccount()?.Id
        Accounts = Data
        SelectedId = If(Data.Accounts.Any(Function(Account) Account.Id = Previous), Previous, Data.Accounts.FirstOrDefault()?.Id)
        CardNotice.Visibility = Visibility.Collapsed
        LabAccountCount.Text = Data.Accounts.Count & " 个账户"
        LabConnection.Text = If(Data.Accounts.Count > 0, "已关联", "待关联")
        If Data.Accounts.Count = 0 Then
            ShowNotice("还没有关联的团队银行", "当前账号尚未获得任何团队银行的直接授权。可在游戏内创建团队银行，或请银行管理者将你加入信任列表。", False)
            LabAccountName.Text = "暂无可访问账户"
            LabAccountTail.Text = "团队银行授权后，再次刷新即可关联。"
        End If
        RenderAccount()
        LabSync.Text = "更新于 " & DisplayTime(Data.CapturedAt, "MM-dd HH:mm")
        LoadHistory()
    End Sub

    Private Function SelectedAccount() As BrclBank.BankAccount
        Return Accounts.Accounts.FirstOrDefault(Function(Account) Account.Id = SelectedId)
    End Function

    Private Sub SelectAccount(Id As String)
        If Not Accounts.Accounts.Any(Function(Account) Account.Id = Id) Then Return
        SelectedId = Id
        PanDetails.Visibility = Visibility.Collapsed
        BtnDetails.Text = "查看当前账户详情"
        RenderAccount()
        LoadHistory()
    End Sub

    Private Sub RenderAccount()
        Dim Account = SelectedAccount()
        If Account IsNot Nothing Then
            LabAccountName.Text = If(String.IsNullOrWhiteSpace(Account.Label), "未命名团队银行", Account.Label)
            LabAccountTail.Text = "团队共享账户 · " & Account.Grants.Distinct().Count() & " 位授权成员"
            LabAccountId.Text = Account.Id
        End If
        RenderMoney()
        UpdateTabs()
    End Sub

    Private Function Money(Value As BigInteger) As String
        Return If(IsPrivate, "••••••", Value.ToString("N0", CultureInfo.InvariantCulture))
    End Function

    Private Sub RenderMoney()
        Dim Account = SelectedAccount()
        CardBalance.Visibility = If(Account Is Nothing, Visibility.Collapsed, Visibility.Visible)
        BtnPrivacy.IsEnabled = Account IsNot Nothing
        LabBalance.Text = If(Account Is Nothing, "— —", Money(Account.Balance))
        LabVisible.Text = If(Account Is Nothing, "—", Money(Account.VisibleBalance))
        LabReserve.Text = If(Account Is Nothing, "—", Money(Account.ReserveBalance))
        LabMembers.Text = If(Account Is Nothing, "—", Account.Grants.Distinct().Count().ToString(CultureInfo.InvariantCulture))
        RenderAccountChoices()
        RenderHistory()
    End Sub

    Private Sub RenderAccountChoices()
        PanAccountChoices.Children.Clear()
        For Each Account In Accounts.Accounts
            Dim Row As New Button With {.Style = CType(FindResource("BankAccountRow"), Style), .Tag = Account.Id, .Margin = New Thickness(0, 0, 0, 9)}
            If Account.Id = SelectedId Then Row.SetResourceReference(Button.BorderBrushProperty, "IndustrialAccent")
            Automation.AutomationProperties.SetName(Row, "查看" & If(String.IsNullOrWhiteSpace(Account.Label), "未命名团队银行", Account.Label))
            Dim Layout As New Grid
            Layout.ColumnDefinitions.Add(New ColumnDefinition With {.Width = New GridLength(1, GridUnitType.Star)})
            Layout.ColumnDefinitions.Add(New ColumnDefinition With {.Width = GridLength.Auto})
            Dim Description As New StackPanel With {.Margin = New Thickness(0, 0, 12, 0), .VerticalAlignment = VerticalAlignment.Center}
            Description.Children.Add(New TextBlock With {.Text = If(String.IsNullOrWhiteSpace(Account.Label), "未命名团队银行", Account.Label), .FontSize = 14, .FontWeight = FontWeights.SemiBold, .TextTrimming = TextTrimming.CharacterEllipsis})
            Dim Detail As New TextBlock With {.Text = "团队共享 · " & Account.Grants.Distinct().Count() & " 位成员", .FontSize = 11, .Margin = New Thickness(0, 6, 0, 0)}
            Detail.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrush2")
            Description.Children.Add(Detail)
            Layout.Children.Add(Description)
            Dim Amount As New StackPanel With {.HorizontalAlignment = HorizontalAlignment.Right, .VerticalAlignment = VerticalAlignment.Center}
            Amount.Children.Add(New TextBlock With {.Text = Money(Account.Balance), .FontFamily = New FontFamily("Consolas"), .FontSize = If(ActualWidth > 0 AndAlso ActualWidth < 540, 16, 20), .FontWeight = FontWeights.SemiBold, .HorizontalAlignment = HorizontalAlignment.Right})
            Dim State As New TextBlock With {.Text = If(Account.Id = SelectedId, "当前查看 · 铜币", "铜币  ›"), .FontSize = 10, .HorizontalAlignment = HorizontalAlignment.Right, .Margin = New Thickness(0, 5, 0, 0)}
            State.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrush2")
            Amount.Children.Add(State)
            Grid.SetColumn(Amount, 1)
            Layout.Children.Add(Amount)
            Row.Content = Layout
            AddHandler Row.Click, Sub(Sender As Object, Args As RoutedEventArgs) SelectAccount(CStr(CType(Sender, Button).Tag))
            PanAccountChoices.Children.Add(Row)
        Next
    End Sub

    Private Sub Details_Click() Handles BtnDetails.Click
        PanDetails.Visibility = If(PanDetails.Visibility = Visibility.Visible, Visibility.Collapsed, Visibility.Visible)
        BtnDetails.Text = If(PanDetails.Visibility = Visibility.Visible, "收起账户详情", "查看当前账户详情")
    End Sub

    Private Sub Privacy_Click() Handles BtnPrivacy.Click
        IsPrivate = Not IsPrivate
        BtnPrivacy.Text = If(IsPrivate, "显示金额", "隐藏金额")
        RenderMoney()
    End Sub

    Private Sub Refresh_Click() Handles BtnRefresh.Click
        RefreshPage()
    End Sub

    Private Sub Login_Click() Handles BtnLogin.Click
        FrmMain.PageChange(FormMain.PageType.Launch)
        If FrmLaunchLeft IsNot Nothing AndAlso FrmLaunchLeft.RadioLoginType5.Visibility = Visibility.Visible Then
            FrmLaunchLeft.RadioLoginType5.SetChecked(True, True, True)
        End If
    End Sub

    Private Sub Copy_Click() Handles BtnCopy.Click
        If SelectedAccount() Is Nothing Then Return
        Try
            Clipboard.SetText(SelectedAccount().Id)
            Hint("已复制账户编号", HintType.Green)
        Catch ex As Exception
            Hint("暂时无法访问剪贴板，请稍后重试", HintType.Blue)
        End Try
    End Sub

    Private Sub Tab_Click(sender As Object, e As MouseButtonEventArgs) Handles TabAccount.Click, TabTransactions.Click, TabAnalysis.Click
        SelectedTab = CStr(CType(sender, MyButton).Tag)
        UpdateTabs()
        DrawChart()
    End Sub

    Private Sub UpdateTabs()
        Dim HasAccount = SelectedAccount() IsNot Nothing
        TabTransactions.IsEnabled = HasAccount
        TabAnalysis.IsEnabled = HasAccount
        If Not HasAccount Then SelectedTab = "account"
        TabAccount.ColorType = If(SelectedTab = "account", MyButton.ColorState.Highlight, MyButton.ColorState.Normal)
        TabTransactions.ColorType = If(SelectedTab = "transactions", MyButton.ColorState.Highlight, MyButton.ColorState.Normal)
        TabAnalysis.ColorType = If(SelectedTab = "analysis", MyButton.ColorState.Highlight, MyButton.ColorState.Normal)
        CardAccount.Visibility = If(SelectedTab = "account" AndAlso SelectedAccount() IsNot Nothing, Visibility.Visible, Visibility.Collapsed)
        CardTransactions.Visibility = If(SelectedTab = "transactions", Visibility.Visible, Visibility.Collapsed)
        CardAnalysis.Visibility = If(SelectedTab = "analysis", Visibility.Visible, Visibility.Collapsed)
    End Sub

    Private Sub Days_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnDays7.Click, BtnDays30.Click, BtnDays90.Click
        SelectedDays = Integer.Parse(CStr(CType(sender, MyButton).Tag), CultureInfo.InvariantCulture)
        For Each Button In {BtnDays7, BtnDays30, BtnDays90}
            Button.ColorType = If(CStr(Button.Tag) = SelectedDays.ToString(), MyButton.ColorState.Highlight, MyButton.ColorState.Normal)
        Next
        LoadHistory()
    End Sub

    Private Async Sub LoadHistory()
        HistoryVersion += 1
        Dim Version = HistoryVersion
        HistoryCancel?.Cancel()
        HistoryCancel = New CancellationTokenSource
        Dim Cancel = HistoryCancel.Token
        Points.Clear()
        HistoryFailed = False
        RenderHistory()
        Dim Account = SelectedAccount()
        If Account Is Nothing OrElse Client Is Nothing Then Return
        LabHistoryStatus.Text = "正在同步已记录的余额…"
        Try
            Dim Data = Await Client.GetHistory(Account.Id, SelectedDays, Cancel)
            If Version <> HistoryVersion OrElse CurrentIdentity() <> Identity Then Return
            Points = Data
        Catch ex As OperationCanceledException
            If Cancel.IsCancellationRequested Then Return
            HistoryFailed = True
        Catch ex As BrclBank.BankServiceException
            If Version <> HistoryVersion Then Return
            If ex.Code = "unauthorized" OrElse ex.Code = "account_unavailable" Then
                ApplyFailure(ex.Code)
                Return
            End If
            HistoryFailed = True
        Catch ex As Exception
            If Version <> HistoryVersion Then Return
            HistoryFailed = True
        End Try
        If Version = HistoryVersion Then RenderHistory()
    End Sub

    Private Sub RenderHistory()
        LabChange.Text = "—" : LabHigh.Text = "—" : LabLow.Text = "—"
        LabChartStart.Text = "" : LabChartEnd.Text = ""
        LabChartEmpty.Text = If(HistoryFailed, "暂时无法获取余额记录", "暂无余额记录")
        LabHistoryStatus.Text = If(HistoryFailed, "余额记录同步失败，可点击刷新重试。", If(SelectedAccount() Is Nothing, "关联账户后，查看已记录的余额走势。", "记录将从银行服务开始采集时积累。"))
        If Points.Count > 0 Then
            LabHistoryStatus.Text = "近 " & SelectedDays & " 天 · " & Points.Count & " 个访问采样点"
            LabChartStart.Text = DisplayTime(Points(0).CapturedAt, "MM-dd HH:mm")
            LabChartEnd.Text = DisplayTime(Points(Points.Count - 1).CapturedAt, "MM-dd HH:mm")
            LabHigh.Text = Money(Points.Max(Function(Point) Point.Balance))
            LabLow.Text = Money(Points.Min(Function(Point) Point.Balance))
            If Points.Count > 1 Then
                Dim Change = Points(Points.Count - 1).Balance - Points(0).Balance
                LabChange.Text = If(Not IsPrivate AndAlso Change.Sign > 0, "+", "") & Money(Change)
            Else
                LabHistoryStatus.Text &= " · 尚不足以形成趋势"
            End If
        End If
        DrawChart()
    End Sub

    Private Sub DrawChart() Handles ChartCanvas.SizeChanged
        ChartCanvas.Children.Clear()
        LabChartEmpty.Visibility = If(IsPrivate OrElse Points.Count < 2, Visibility.Visible, Visibility.Collapsed)
        If IsPrivate Then LabChartEmpty.Text = "金额已隐藏"
        If IsPrivate OrElse Points.Count < 2 OrElse ChartCanvas.ActualWidth <= 0 Then Return
        Dim Width = ChartCanvas.ActualWidth - 28, Height = ChartCanvas.ActualHeight - 28
        For Index = 0 To 3
            Dim Line As New Shapes.Line With {.X1 = 14, .X2 = Width + 14, .Y1 = 14 + Height * Index / 3, .Y2 = 14 + Height * Index / 3, .StrokeThickness = 1, .StrokeDashArray = New DoubleCollection({3.0, 4.0})}
            Line.SetResourceReference(Shapes.Line.StrokeProperty, "ColorBrushCardLine")
            ChartCanvas.Children.Add(Line)
        Next
        Dim Low = Points.Min(Function(Point) Point.Balance), High = Points.Max(Function(Point) Point.Balance)
        Dim Range = High - Low
        Dim TimeRange = Points(Points.Count - 1).CapturedAt - Points(0).CapturedAt
        Dim Curve As New Shapes.Polyline With {.StrokeThickness = 2, .StrokeLineJoin = PenLineJoin.Round}
        Curve.SetResourceReference(Shapes.Polyline.StrokeProperty, "ColorBrush3")
        For Each Point In Points
            'All accounting stays in BigInteger. Only normalized chart coordinates use Double.
            Dim Fraction = If(Range.IsZero, 0.5, CDbl((Point.Balance - Low) * 1000000 / BigInteger.Max(BigInteger.One, Range)) / 1000000)
            Curve.Points.Add(New Point(14 + Width * (Point.CapturedAt - Points(0).CapturedAt) / TimeRange, 14 + Height * (1 - Fraction)))
        Next
        ChartCanvas.Children.Add(Curve)
    End Sub

    Private Shared Function DisplayTime(Value As Long, Format As String) As String
        Return DateTimeOffset.FromUnixTimeMilliseconds(Value).ToLocalTime().ToString(Format, CultureInfo.InvariantCulture)
    End Function

    Private Sub UpdateLayoutMode() Handles Me.SizeChanged
        Dim Compact = ActualWidth < 540
        ColumnArtwork.Width = New GridLength(If(Compact, 0, 92))
        BankArtwork.Visibility = If(Compact, Visibility.Collapsed, Visibility.Visible)
        LabBalance.FontSize = If(Compact, 28, 36)
    End Sub
End Class
