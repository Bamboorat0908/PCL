Imports System.IO
Imports System.Net.Http
Imports System.Windows.Media
Imports Newtonsoft.Json.Linq

Public Class PageSetupSponsors
    Implements IRefreshable

    Private Const SponsorUrl As String = "https://afdian.com/a/brszz"
    Private Const WebsiteUrl As String = "https://zhushu.homes/BRS/sponsors"
    Private Shared ReadOnly Http As New HttpClient With {.Timeout = TimeSpan.FromSeconds(12)}
    Private Shared ReadOnly CachePath As String = Paths.Base & "BRCL\SponsorsCache.json"
    Private IsLoading As Boolean
    Private IsInited As Boolean
    Private LastFetch As DateTime = DateTime.MinValue
    Private LastData As JObject

    Shared Sub New()
        Http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0 Safari/537.36")
        Http.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json, text/plain, */*")
    End Sub

    Private Sub Page_Loaded(sender As Object, e As RoutedEventArgs) Handles Me.Loaded
        If Not IsInited Then
            IsInited = True
            Try
                If File.Exists(CachePath) Then ApplyData(JObject.Parse(File.ReadAllText(CachePath)), True)
            Catch ex As Exception
                Logger.Warn(ex, "读取赞助鸣谢缓存失败")
            End Try
        End If
        If DateTime.UtcNow - LastFetch > TimeSpan.FromMinutes(5) Then LoadData()
    End Sub

    Private Sub Sponsor_Click() Handles BtnSponsor.Click
        OpenWebsite(SponsorUrl)
    End Sub

    Private Sub Website_Click() Handles BtnWebsite.Click
        OpenWebsite(WebsiteUrl)
    End Sub

    Public Sub RefreshPage() Implements IRefreshable.Refresh
        LoadData()
    End Sub

    Private Sub Refresh_Click() Handles BtnRefresh.Click
        LoadData()
    End Sub

    Private Sub LoadData()
        If IsLoading Then Return
        IsLoading = True
        BtnRefresh.IsEnabled = False
        LabUpdated.Text = "正在获取赞助名单…"
        RunInNewThread(
        Sub()
            Dim Result As JObject = Nothing
            Try
                Dim Body = FetchSponsors()
                Result = JObject.Parse(Body)
                BuildSponsors(Result)
                Try
                    Directory.CreateDirectory(Path.GetDirectoryName(CachePath))
                    Dim PendingPath = CachePath & ".tmp"
                    File.WriteAllText(PendingPath, Body)
                    If File.Exists(CachePath) Then
                        File.Replace(PendingPath, CachePath, Nothing)
                    Else
                        File.Move(PendingPath, CachePath)
                    End If
                Catch ex As Exception
                    Logger.Warn(ex, "保存赞助鸣谢缓存失败")
                End Try
            Catch ex As Exception
                Result = Nothing
                Logger.Warn(ex, "获取赞助鸣谢失败")
            End Try
            RunInUi(
            Sub()
                IsLoading = False
                BtnRefresh.IsEnabled = True
                If Result IsNot Nothing Then
                    ApplyData(Result)
                    LastFetch = DateTime.UtcNow
                Else
                    LabUpdated.Text = If(LastData Is Nothing, "暂时无法获取名单，请稍后重试；也可查看原站鸣谢页。", "刷新失败，正在展示上次保存的名单。")
                End If
            End Sub)
        End Sub, "赞助鸣谢加载")
    End Sub

    Private Shared Function FetchSponsors() As String
        For Attempt As Integer = 0 To 1
            Try
                Return Http.GetStringAsync("https://zhushu.homes/BRS/api/sponsors").GetAwaiter().GetResult()
            Catch ex As Exception When Attempt = 0 AndAlso (TypeOf ex Is HttpRequestException OrElse TypeOf ex Is Threading.Tasks.TaskCanceledException)
                Threading.Thread.Sleep(400)
            End Try
        Next
        Throw New InvalidOperationException("无法获取赞助名单")
    End Function

    Private Shared Function ReadText(Token As JToken) As String
        Return If(Token IsNot Nothing AndAlso Token.Type = JTokenType.String, Token.ToString().Trim(), "")
    End Function

    Private Shared Function BuildSponsors(Data As JObject) As JArray
        Dim Source = TryCast(Data("sponsors"), JArray)
        If Source Is Nothing Then Throw New FormatException("赞助名单缺少 sponsors 数组")
        Dim Result As New JArray
        For Each Token In Source
            Dim Item = TryCast(Token, JObject)
            If Item Is Nothing Then Continue For
            Dim Name = ReadText(Item("name"))
            If Name.Length = 0 Then Continue For
            Result.Add(New JObject From {
                {"name", Name},
                {"note", ReadText(Item("note"))}, {"date", ReadText(Item("date"))},
                {"has_avatar", Not (Item("has_avatar") IsNot Nothing AndAlso Item("has_avatar").Type = JTokenType.Boolean AndAlso Not Item.Value(Of Boolean)("has_avatar"))}
            })
        Next
        Return Result
    End Function

    Private Sub ApplyData(Data As JObject, Optional FromCache As Boolean = False)
        Dim Entries = BuildSponsors(Data)
        LabSummary.Text = Entries.Count.ToString() & " 位支持者"
        LabUpdated.Text = If(FromCache, "已载入本地保存的名单，正在同步最新记录…", "已同步 BRS 原站 · " & DateTime.Now.ToString("MM-dd HH:mm"))
        Dim Page = TryCast(Data("page"), JObject)
        Dim Footer = ReadText(Page?("footer"))
        LabFooter.Text = If(Footer.Length > 0, Footer, "名单与 BRS 原站同步，感谢大家的支持。")
        LastData = Data
        If JToken.DeepEquals(TryCast(PanSponsors.Tag, JArray), Entries) Then Return
        PanSponsors.Tag = Entries
        PanSponsors.Children.Clear()
        If Entries.Count = 0 Then
            PanSponsors.Children.Add(New TextBlock With {.Text = "暂无赞助记录，感谢每一位支持 BRS 的伙伴。", .Margin = New Thickness(0, 12, 0, 12), .TextWrapping = TextWrapping.Wrap})
        End If
        For Each Item As JObject In Entries
            Dim Row As New Grid With {.Margin = New Thickness(0, 9, 0, 9), .MinHeight = 40}
            Row.ColumnDefinitions.Add(New ColumnDefinition With {.Width = GridLength.Auto})
            Row.ColumnDefinitions.Add(New ColumnDefinition With {.Width = New GridLength(1, GridUnitType.Star)})
            Dim Name = Item.Value(Of String)("name")
            Dim Avatar As New BrsPlayerAvatar(Name, 34, Item.Value(Of Boolean)("has_avatar")) With {.Margin = New Thickness(0, 0, 12, 0)}
            Row.Children.Add(Avatar)
            Dim Details As New StackPanel With {.VerticalAlignment = VerticalAlignment.Center}
            Details.Children.Add(New TextBlock With {.Text = Name, .FontSize = 13, .FontWeight = FontWeights.SemiBold, .TextTrimming = TextTrimming.CharacterEllipsis, .ToolTip = Name})
            Dim Meta = String.Join(" · ", {Item.Value(Of String)("note"), Item.Value(Of String)("date")}.Where(Function(Value) Not String.IsNullOrWhiteSpace(Value)))
            If Meta.Length > 0 Then
                Dim Note As New TextBlock With {.Text = Meta, .FontSize = 11, .Margin = New Thickness(0, 4, 0, 0), .TextWrapping = TextWrapping.Wrap}
                Note.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrush2")
                Details.Children.Add(Note)
            End If
            Grid.SetColumn(Details, 1)
            Row.Children.Add(Details)
            PanSponsors.Children.Add(Row)
            Dim Line As New Shapes.Rectangle With {.Height = 1, .Opacity = 0.6}
            Line.SetResourceReference(Shapes.Rectangle.FillProperty, "ColorBrushCardLine")
            PanSponsors.Children.Add(Line)
        Next
    End Sub
End Class
