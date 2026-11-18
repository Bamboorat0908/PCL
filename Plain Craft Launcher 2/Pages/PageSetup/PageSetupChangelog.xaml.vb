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
        Dim Text As New StringBuilder("BRCL 更新日志" & vbCrLf & LabVersion.Text & vbCrLf)
        For Each Entry As JObject In Entries
            Dim Title = CStr(Entry("title"))
            Dim Metadata = CStr(Entry("date")) & " · " & CStr(Entry("status"))
            Dim Content As New StackPanel With {.Margin = New Thickness(20, 18, 20, 18)}
            Dim Caption As New TextBlock With {.Text = Metadata, .FontSize = 11, .TextWrapping = TextWrapping.Wrap}
            Caption.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrush2")
            Content.Children.Add(Caption)
            Content.Children.Add(New TextBlock With {
                .Text = Title, .FontSize = 16, .FontWeight = FontWeights.SemiBold,
                .TextWrapping = TextWrapping.Wrap, .Margin = New Thickness(0, 7, 0, 5)})
            Text.AppendLine().AppendLine(Title & " · " & Metadata)
            For Each Item As JValue In CType(Entry("items"), JArray)
                Dim Description = CStr(Item)
                Dim Row As New Grid With {.Margin = New Thickness(0, 8, 0, 0)}
                Row.ColumnDefinitions.Add(New ColumnDefinition With {.Width = New GridLength(16)})
                Row.ColumnDefinitions.Add(New ColumnDefinition With {.Width = New GridLength(1, GridUnitType.Star)})
                Dim Bullet As New TextBlock With {.Text = "•", .FontSize = 13, .LineHeight = 22}
                Bullet.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrush3")
                Row.Children.Add(Bullet)
                Dim Body As New TextBlock With {.Text = Description, .FontSize = 13, .LineHeight = 22, .TextWrapping = TextWrapping.Wrap}
                Grid.SetColumn(Body, 1)
                Row.Children.Add(Body)
                Content.Children.Add(Row)
                Text.AppendLine("- " & Description)
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
