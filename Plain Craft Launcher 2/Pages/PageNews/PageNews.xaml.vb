Imports System.Globalization
Imports System.Net.Http
Imports System.Windows.Media
Imports Newtonsoft.Json.Linq

''' <summary>读取 BRS 已发布新闻，以启动器原生控件展示纯文本公告与动态。</summary>
Public Class PageNews
    Implements IRefreshable

    Private Const WebsiteUrl As String = "https://zhushu.homes/BRS/news"
    Private Const PageSize As Integer = 30
    Private Shared ReadOnly Http As New HttpClient With {.Timeout = TimeSpan.FromSeconds(15)}
    Private ReadOnly Entries As New List(Of NewsItem)
    Private ReadOnly ReadButtons As New Dictionary(Of String, MyButton)
    Private SelectedCategory As String = ""
    Private SelectedId As String
    Private LastReadId As String
    Private ListScrollOffset As Double
    Private NextOffset As Integer
    Private Total As Integer
    Private HasMore As Boolean
    Private IsLoading As Boolean
    Private HasData As Boolean
    Private LastFetch As DateTime = DateTime.MinValue
    Private LastFailedAppend As Boolean

    Shared Sub New()
        Http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0 Safari/537.36")
        Http.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json")
        Http.DefaultRequestHeaders.CacheControl = New Headers.CacheControlHeaderValue With {.NoCache = True}
    End Sub

    Private Class NewsItem
        Public Id As String
        Public Title As String
        Public Category As String
        Public Summary As String
        Public Content As String
        Public PublishedAt As DateTimeOffset
        Public UpdatedAt As DateTimeOffset
        Public Pinned As Boolean
    End Class

    Private Class NewsPage
        Public Items As New List(Of NewsItem)
        Public Total As Integer
        Public HasMore As Boolean
        Public NextOffset As Integer
    End Class

    Private Sub Page_Loaded() Handles Me.Loaded
        If Not HasData OrElse DateTime.UtcNow - LastFetch > TimeSpan.FromMinutes(1) Then LoadData()
    End Sub

    Private Sub Page_SizeChanged(sender As Object, e As SizeChangedEventArgs) Handles Me.SizeChanged
        UpdateHeaderLayout()
    End Sub

    Private Sub UpdateHeaderLayout()
        Dim Reading = CardDetail.Visibility = Visibility.Visible
        Dim ShortWindow = ActualHeight > 0 AndAlso ActualHeight < 500
        Dim HideArtwork = ActualWidth < 500 OrElse Reading OrElse ShortWindow
        ColumnArtwork.Width = New GridLength(If(HideArtwork, 0, 64))
        NewsArtwork.Visibility = If(HideArtwork, Visibility.Collapsed, Visibility.Visible)
        HeaderEyebrow.Visibility = If(Reading OrElse ShortWindow, Visibility.Collapsed, Visibility.Visible)
        HeaderDescription.Visibility = HeaderEyebrow.Visibility
        HeaderTitle.FontSize = If(Reading OrElse ShortWindow, 18, 22)
    End Sub

    Public Sub RefreshPage() Implements IRefreshable.Refresh
        LoadData()
    End Sub

    Private Sub Refresh_Click() Handles BtnRefresh.Click
        LoadData()
    End Sub

    Private Sub Retry_Click() Handles BtnRetry.Click
        LoadData(LastFailedAppend)
    End Sub

    Private Sub LoadMore_Click() Handles BtnLoadMore.Click
        LoadData(True)
    End Sub

    Private Sub Website_Click() Handles BtnWebsite.Click
        OpenWebsite(WebsiteUrl)
    End Sub

    Private Sub Back_Click() Handles BtnBack.Click
        ShowList()
        ScrollMain.ScrollToVerticalOffset(ListScrollOffset)
        Dim ReadButton As MyButton = Nothing
        If LastReadId IsNot Nothing AndAlso ReadButtons.TryGetValue(LastReadId, ReadButton) Then ReadButton.Focus()
    End Sub

    Private Sub Filter_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnAll.Click, BtnAnnouncements.Click, BtnUpdates.Click
        If IsLoading Then Return
        Dim Category = CStr(CType(sender, MyButton).Tag)
        If Category = SelectedCategory Then
            ShowList()
            Return
        End If
        SelectedCategory = Category
        Entries.Clear()
        PanItems.Children.Clear()
        HasData = False
        HasMore = False
        NextOffset = 0
        Total = 0
        BtnLoadMore.Visibility = Visibility.Collapsed
        LabChannel.Text = If(Category = "", "全部发布", If(Category = "announcement", "服务器公告", "社区动态"))
        LabCount.Text = "WAITING"
        LastFetch = DateTime.MinValue
        BtnAll.ColorType = If(Category = "", MyButton.ColorState.Highlight, MyButton.ColorState.Normal)
        BtnAnnouncements.ColorType = If(Category = "announcement", MyButton.ColorState.Highlight, MyButton.ColorState.Normal)
        BtnUpdates.ColorType = If(Category = "update", MyButton.ColorState.Highlight, MyButton.ColorState.Normal)
        ShowList()
        LoadData()
    End Sub

    Private Sub SetLoading(Value As Boolean)
        IsLoading = Value
        For Each Button In {BtnRefresh, BtnRetry, BtnLoadMore, BtnAll, BtnAnnouncements, BtnUpdates}
            Button.IsEnabled = Not Value
        Next
        BtnLoadMore.Text = If(Value, "正在加载…", "加载更多")
    End Sub

    Private Sub LoadData(Optional Append As Boolean = False)
        If IsLoading OrElse (Append AndAlso Not HasMore) Then Return
        Dim Offset = If(Append, NextOffset, 0)
        Dim Category = SelectedCategory
        SetLoading(True)
        CardError.Visibility = Visibility.Collapsed
        CardEmpty.Visibility = Visibility.Collapsed
        LabSync.Text = "正在同步"
        DotSync.SetResourceReference(Shapes.Rectangle.FillProperty, "IndustrialAccent")
        LabStatus.Text = If(Append, "正在获取更多新闻…", "正在同步最新新闻…")
        RunInNewThread(
        Sub()
            Dim Result As NewsPage = Nothing
            Try
                Dim Url = WebsiteUrl & "/api/news?limit=" & PageSize & "&offset=" & Offset
                If Category.Length > 0 Then Url &= "&category=" & Uri.EscapeDataString(Category)
                Dim Json = JObject.Parse(Http.GetStringAsync(Url).GetAwaiter().GetResult())
                Result = ParseData(Json)
                If Result.HasMore AndAlso Result.NextOffset <= Offset Then Throw New FormatException("新闻分页未前进")
            Catch ex As Exception
                Result = Nothing
                Logger.Warn(ex, "获取 BRS 新闻失败")
            End Try
            RunInUi(
            Sub()
                SetLoading(False)
                If Result IsNot Nothing Then
                    ApplyPage(Result, Append)
                    LastFetch = DateTime.UtcNow
                Else
                    ApplyError(Append)
                End If
            End Sub)
        End Sub, "BRS 新闻加载")
    End Sub

    Private Shared Function ReadText(Token As JToken) As String
        Return If(Token IsNot Nothing AndAlso Token.Type = JTokenType.String, Token.ToString().Trim(), "")
    End Function

    Private Shared Function ReadDate(Token As JToken) As DateTimeOffset
        If Token Is Nothing Then Return DateTimeOffset.MinValue
        If Token.Type = JTokenType.Date Then
            Dim Value = Token.Value(Of DateTime)()
            If Value.Kind = DateTimeKind.Unspecified Then Value = DateTime.SpecifyKind(Value, DateTimeKind.Utc)
            Return New DateTimeOffset(Value).ToUniversalTime()
        End If
        Dim Parsed As DateTimeOffset
        If DateTimeOffset.TryParse(ReadText(Token), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, Parsed) Then Return Parsed.ToUniversalTime()
        Return DateTimeOffset.MinValue
    End Function

    Private Shared Function ReadCount(Token As JToken, Fallback As Integer) As Integer
        Dim Value As Integer
        If Token IsNot Nothing AndAlso Token.Type = JTokenType.Integer AndAlso Integer.TryParse(Token.ToString(), Value) AndAlso Value >= 0 Then Return Value
        Return Fallback
    End Function

    Private Shared Function ParseData(Data As JObject) As NewsPage
        Dim Source = TryCast(Data("items"), JArray)
        If Source Is Nothing Then Throw New FormatException("新闻响应缺少 items 数组")
        Dim Result As New NewsPage
        For Each Token In Source
            Dim Item = TryCast(Token, JObject)
            If Item Is Nothing Then Continue For
            Dim Id = If(Item("id") IsNot Nothing AndAlso Item("id").Type = JTokenType.Integer, Item("id").ToString(), ReadText(Item("id")))
            Dim Title = ReadText(Item("title")), Category = ReadText(Item("category"))
            If Id.Length = 0 OrElse Title.Length = 0 OrElse (Category <> "announcement" AndAlso Category <> "update") Then Continue For
            'Only text is rendered. Remote content is never interpreted as XAML, HTML or an event.
            Result.Items.Add(New NewsItem With {
                .Id = Id, .Title = Title, .Category = Category, .Summary = ReadText(Item("summary")),
                .Content = ReadText(Item("content")), .PublishedAt = ReadDate(Item("published_at")),
                .UpdatedAt = ReadDate(Item("updated_at")),
                .Pinned = Item("pinned") IsNot Nothing AndAlso Item("pinned").Type = JTokenType.Boolean AndAlso Item.Value(Of Boolean)("pinned")})
        Next
        Result.Total = ReadCount(Data("total"), Result.Items.Count)
        Result.HasMore = Data("has_more") IsNot Nothing AndAlso Data("has_more").Type = JTokenType.Boolean AndAlso Data.Value(Of Boolean)("has_more")
        Result.NextOffset = ReadCount(Data("next_offset"), 0)
        If Result.HasMore AndAlso (Source.Count = 0 OrElse Result.NextOffset = 0) Then Throw New FormatException("新闻分页数据无效")
        Return Result
    End Function

    'Also permits native integration checks without invoking network I/O or Loaded events.
    Private Sub ApplyData(Data As JObject, Optional Append As Boolean = False)
        ApplyPage(ParseData(Data), Append)
    End Sub

    Private Sub ApplyPage(Data As NewsPage, Append As Boolean)
        If Not Append Then Entries.Clear()
        For Each Item In Data.Items
            Dim Existing = Entries.FindIndex(Function(Value) Value.Id = Item.Id)
            If Existing >= 0 Then
                Entries(Existing) = Item
            Else
                Entries.Add(Item)
            End If
        Next
        Total = Data.Total
        HasMore = Data.HasMore
        NextOffset = Data.NextOffset
        HasData = True
        CardError.Visibility = Visibility.Collapsed
        LabSync.Text = "新闻频道已连接"
        DotSync.Fill = New SolidColorBrush(Color.FromRgb(184, 235, 103))
        LabChannel.Text = If(SelectedCategory = "", "全部发布", If(SelectedCategory = "announcement", "服务器公告", "社区动态"))
        LabCount.Text = Math.Max(Total, Entries.Count).ToString("D2") & " POSTS"
        LabStatus.Text = "已显示 " & Entries.Count & " / " & Math.Max(Total, Entries.Count) & " 篇 · 同步于 " & DateTime.Now.ToString("MM-dd HH:mm")
        RenderItems()
        Dim Selected = Entries.Find(Function(Item) Item.Id = SelectedId)
        If Selected Is Nothing Then
            ShowList()
        Else
            ShowDetail(Selected, False)
        End If
    End Sub

    Private Sub ApplyError(Optional Append As Boolean = False)
        LastFailedAppend = Append
        CardError.Visibility = Visibility.Visible
        CardEmpty.Visibility = Visibility.Collapsed
        LabSync.Text = "等待重新连接"
        DotSync.SetResourceReference(Shapes.Rectangle.FillProperty, "ColorBrush2")
        LabStatus.Text = If(HasData, "同步失败，正在显示上次读取的新闻。", "新闻暂时无法连接，请重试。")
    End Sub

    Private Shared Function CategoryName(Category As String) As String
        Return If(Category = "announcement", "公告", "动态")
    End Function

    Private Shared Function MetaText(Item As NewsItem) As String
        Dim DateLabel = If(Item.PublishedAt = DateTimeOffset.MinValue, "发布时间待确认", Item.PublishedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm"))
        Return If(Item.Pinned, "置顶 · ", "") & CategoryName(Item.Category) & " · " & DateLabel
    End Function

    Private Sub RenderItems()
        PanItems.Children.Clear()
        ReadButtons.Clear()
        For Index As Integer = 0 To Entries.Count - 1
            Dim Current = Entries(Index)
            Dim Featured = Index = 0
            Dim Card As New MyCard With {.UseAnimation = False, .Margin = New Thickness(0, 14, 0, 0)}
            If Featured Then
                Dim Tint As New Border With {.Opacity = 0.055, .IsHitTestVisible = False}
                Tint.SetResourceReference(Border.BackgroundProperty, "IndustrialAccent")
                Card.Children.Add(Tint)
                Dim Edge As New Shapes.Rectangle With {.Height = 3, .VerticalAlignment = VerticalAlignment.Top, .IsHitTestVisible = False}
                Edge.SetResourceReference(Shapes.Rectangle.FillProperty, "IndustrialAccent")
                Card.Children.Add(Edge)
                Dim Corner As New Shapes.Path With {.Data = Geometry.Parse("M0 0 H18 V18 Z"), .Width = 18, .Height = 18, .HorizontalAlignment = HorizontalAlignment.Right, .VerticalAlignment = VerticalAlignment.Top, .IsHitTestVisible = False}
                Corner.SetResourceReference(Shapes.Path.FillProperty, "IndustrialAccent")
                Card.Children.Add(Corner)
            End If
            Dim Row As New Grid With {.Margin = New Thickness(18, If(Featured, 22, 18), 20, 18)}
            Row.ColumnDefinitions.Add(New ColumnDefinition With {.Width = New GridLength(64)})
            Row.ColumnDefinitions.Add(New ColumnDefinition With {.Width = New GridLength(1, GridUnitType.Star)})
            Dim DateRail As New StackPanel With {.Margin = New Thickness(0, 0, 14, 0)}
            Dim DayText = If(Current.PublishedAt = DateTimeOffset.MinValue, "—", Current.PublishedAt.ToLocalTime().ToString("dd"))
            DateRail.Children.Add(New TextBlock With {.Text = DayText, .FontSize = If(Featured, 34, 29), .FontFamily = New FontFamily("Bahnschrift,Segoe UI"), .FontWeight = FontWeights.Bold})
            Dim Month As New TextBlock With {.Text = If(Current.PublishedAt = DateTimeOffset.MinValue, "DATE", Current.PublishedAt.ToLocalTime().ToString("yyyy.MM")), .FontFamily = New FontFamily("Consolas"), .FontSize = 10, .Margin = New Thickness(0, 1, 0, 0)}
            Month.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrush2")
            DateRail.Children.Add(Month)
            Dim Line As New Shapes.Rectangle With {.Height = 2, .Margin = New Thickness(0, 12, 8, 0)}
            Line.SetResourceReference(Shapes.Rectangle.FillProperty, If(Featured, "IndustrialAccent", "ColorBrushCardLine"))
            DateRail.Children.Add(Line)
            Dim Order As New TextBlock With {.Text = (Index + 1).ToString("D2"), .FontFamily = New FontFamily("Consolas"), .FontSize = 10, .Opacity = 0.5, .Margin = New Thickness(0, 9, 0, 0)}
            Order.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrush2")
            DateRail.Children.Add(Order)
            Row.Children.Add(DateRail)
            Dim Panel As New StackPanel
            Grid.SetColumn(Panel, 1)
            Dim Meta As New WrapPanel
            Dim Badge As New Border With {.Padding = New Thickness(6, 2, 6, 2), .Margin = New Thickness(0, 0, 8, 3)}
            If Current.Category = "announcement" Then
                Badge.SetResourceReference(Border.BackgroundProperty, "IndustrialAccent")
            Else
                Badge.Background = New SolidColorBrush(Color.FromRgb(184, 235, 103))
            End If
            Badge.Child = New TextBlock With {.Text = CategoryName(Current.Category), .Foreground = New SolidColorBrush(Color.FromRgb(34, 37, 29)), .FontSize = 10, .FontWeight = FontWeights.SemiBold}
            Meta.Children.Add(Badge)
            Dim Label As New TextBlock With {.Text = If(Current.Pinned, "PINNED / 置顶", If(Featured, "LATEST / 最新发布", "BRS / 新闻")), .FontFamily = New FontFamily("Consolas"), .FontSize = 10, .VerticalAlignment = VerticalAlignment.Center, .Margin = New Thickness(0, 0, 0, 3)}
            Label.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrush2")
            Meta.Children.Add(Label)
            Panel.Children.Add(Meta)
            Panel.Children.Add(New TextBlock With {.Text = Current.Title, .FontSize = If(Featured, 22, 17), .FontWeight = FontWeights.SemiBold, .TextWrapping = TextWrapping.Wrap, .Margin = New Thickness(0, 8, 0, 0)})
            Dim Summary = Current.Summary
            If Summary.Length = 0 Then Summary = Current.Content
            If Summary.Length > 140 Then Summary = Summary.Substring(0, 140) & "…"
            If Summary.Length > 0 Then
                Dim Preview As New TextBlock With {.Text = Summary, .FontSize = If(Featured, 13, 12), .LineHeight = 22, .TextWrapping = TextWrapping.Wrap, .Margin = New Thickness(0, 10, 0, 0)}
                Preview.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrush2")
                Panel.Children.Add(Preview)
            End If
            Dim ReadButton As New MyButton With {.Text = If(Current.Id = LastReadId, "再次阅读", "阅读全文"), .ColorType = If(Featured, MyButton.ColorState.Highlight, MyButton.ColorState.Normal), .Height = 30, .Padding = New Thickness(12, 0, 12, 0), .HorizontalAlignment = HorizontalAlignment.Left, .Margin = New Thickness(0, 14, 0, 0)}
            ReadButton.SetResourceReference(MyButton.IconProperty, "BrclGeometryArrowRight")
            Automation.AutomationProperties.SetName(ReadButton, "阅读新闻：" & Current.Title)
            AddHandler ReadButton.Click, Sub() ShowDetail(Current)
            ReadButtons(Current.Id) = ReadButton
            Panel.Children.Add(ReadButton)
            Row.Children.Add(Panel)
            Card.Children.Add(Row)
            PanItems.Children.Add(Card)
        Next
        BtnLoadMore.Visibility = If(HasMore, Visibility.Visible, Visibility.Collapsed)
    End Sub

    Private Sub ShowList()
        SelectedId = Nothing
        CardDetail.Visibility = Visibility.Collapsed
        UpdateHeaderLayout()
        PanChannel.Visibility = Visibility.Visible
        PanList.Visibility = Visibility.Visible
        CardEmpty.Visibility = If(HasData AndAlso Entries.Count = 0 AndAlso CardError.Visibility <> Visibility.Visible, Visibility.Visible, Visibility.Collapsed)
        LabEmptyTitle.Text = If(SelectedCategory = "", "还没有新闻", "还没有" & CategoryName(SelectedCategory))
        ScrollMain.ScrollToHome()
    End Sub

    Private Sub ShowDetail(Item As NewsItem, Optional ScrollToTop As Boolean = True)
        If ScrollToTop Then ListScrollOffset = ScrollMain.VerticalOffset
        LastReadId = Item.Id
        Dim ReadButton As MyButton = Nothing
        If ReadButtons.TryGetValue(Item.Id, ReadButton) Then ReadButton.Text = "再次阅读"
        SelectedId = Item.Id
        LabDetailTitle.Text = Item.Title
        LabDetailMeta.Text = MetaText(Item)
        If Item.PublishedAt <> DateTimeOffset.MinValue AndAlso Item.UpdatedAt - Item.PublishedAt > TimeSpan.FromMinutes(1) Then LabDetailMeta.Text &= " · 更新于 " & Item.UpdatedAt.ToLocalTime().ToString("MM-dd HH:mm")
        LabDetailContent.Text = If(Item.Content.Length > 0, Item.Content, Item.Summary)
        CardEmpty.Visibility = Visibility.Collapsed
        PanList.Visibility = Visibility.Collapsed
        PanChannel.Visibility = Visibility.Collapsed
        CardDetail.Visibility = Visibility.Visible
        UpdateHeaderLayout()
        If ScrollToTop Then
            ScrollMain.ScrollToHome()
            If IsLoaded Then Dispatcher.BeginInvoke(Threading.DispatcherPriority.Input, New Action(Sub()
                                                                                                      If SelectedId = Item.Id AndAlso CardDetail.Visibility = Visibility.Visible Then BtnBack.Focus()
                                                                                                  End Sub))
        End If
    End Sub
End Class
