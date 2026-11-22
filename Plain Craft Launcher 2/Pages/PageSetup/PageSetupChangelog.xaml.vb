Imports System.IO
Imports System.Text

Public Class PageSetupChangelog
    Private ReadOnly LogText As String

    Public Sub New()
        InitializeComponent()
        LabVersion.Text = "当前版本 · BRCL " & BrclReleaseVersion
        Dim Entries As JArray
        Using Stream = GetType(PageSetupChangelog).Assembly.GetManifestResourceStream("BRCL.Changelog.json")
            If Stream Is Nothing Then Throw New InvalidDataException("缺少内置更新日志。")
            Using Reader As New StreamReader(Stream, Encoding.UTF8)
                Entries = JArray.Parse(Reader.ReadToEnd())
            End Using
        End Using
        Dim Text As New StringBuilder("# BRCL 更新日志" & vbCrLf & LabVersion.Text & vbCrLf)
        For Each Entry As JObject In Entries
            Dim Title = CStr(Entry("title"))
            Dim ReleaseDate = CStr(Entry("date"))
            Dim Content As New StackPanel With {.Margin = New Thickness(20, 18, 20, 18)}
            Content.Children.Add(New TextBlock With {
                .Text = Title, .FontSize = 20, .FontWeight = FontWeights.SemiBold,
                .TextWrapping = TextWrapping.Wrap})
            Dim Caption As New TextBlock With {.Text = ReleaseDate, .FontSize = 12, .Margin = New Thickness(0, 4, 0, 0)}
            Caption.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrush2")
            Content.Children.Add(Caption)
            Text.AppendLine().AppendLine("## " & Title).AppendLine(ReleaseDate)
            For Each Section As JObject In CType(Entry("sections"), JArray)
                Dim Heading = CStr(Section("title"))
                Dim Category As New TextBlock With {
                    .Text = Heading, .FontSize = 14, .FontWeight = FontWeights.SemiBold,
                    .TextWrapping = TextWrapping.Wrap, .Margin = New Thickness(0, 15, 0, 0)}
                Category.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrush3")
                Content.Children.Add(Category)
                Text.AppendLine().AppendLine("### " & Heading)
                For Each Item As JObject In CType(Section("items"), JArray)
                    Dim Summary = CStr(Item("summary"))
                    Dim Detail = CStr(Item("detail"))
                    Dim Row As New Grid With {.Margin = New Thickness(0, 8, 0, 0)}
                    Row.ColumnDefinitions.Add(New ColumnDefinition With {.Width = New GridLength(16)})
                    Row.ColumnDefinitions.Add(New ColumnDefinition With {.Width = New GridLength(1, GridUnitType.Star)})
                    Dim Bullet As New TextBlock With {.Text = "•", .FontSize = 13, .LineHeight = 22}
                    Bullet.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrush3")
                    Row.Children.Add(Bullet)
                    Dim Body As New TextBlock With {.FontSize = 13, .LineHeight = 22, .TextWrapping = TextWrapping.Wrap}
                    Body.Inlines.Add(New System.Windows.Documents.Run(Summary) With {.FontWeight = FontWeights.SemiBold})
                    Body.Inlines.Add(New System.Windows.Documents.Run("。" & Detail))
                    Grid.SetColumn(Body, 1)
                    Row.Children.Add(Body)
                    Content.Children.Add(Row)
                    Text.AppendLine("- " & Summary & "。" & Detail)
                Next
            Next
            Dim Card As New MyCard With {.UseAnimation = False, .Margin = New Thickness(0, 14, 0, 0)}
            Card.Children.Add(Content)
            PanEntries.Children.Add(Card)
        Next
        LogText = Text.ToString().TrimEnd()
    End Sub

    Private Sub PageLoaded() Handles Me.Loaded
        PanBack.ScrollToHome()
    End Sub

    Private Sub CopyLog() Handles BtnCopy.Click
        ClipboardSet(LogText)
    End Sub

    Private Sub CheckUpdate() Handles BtnCheckUpdate.Click
        UpdateCheckByButton()
    End Sub
End Class
