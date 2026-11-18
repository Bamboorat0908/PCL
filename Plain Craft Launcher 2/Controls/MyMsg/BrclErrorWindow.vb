Imports System.Diagnostics
Imports System.IO
Imports System.Windows.Markup

'A self-contained fallback for startup/fatal errors. No application resources required.
Public Class BrclErrorWindow
    Inherits Window

    Public Shared Sub ShowError(Content As BrclErrorContent, Fatal As Boolean)
        Dim Dialog As New BrclErrorWindow(Content, Fatal)
        Dialog.ShowDialog()
    End Sub

    Private Sub New(Content As BrclErrorContent, Fatal As Boolean)
        Title = "BRCL · " & Content.Title
        Width = Math.Min(620, SystemParameters.WorkArea.Width - 40)
        MaxHeight = Math.Max(260, SystemParameters.WorkArea.Height - 60)
        SizeToContent = SizeToContent.Height
        ResizeMode = ResizeMode.NoResize
        WindowStyle = WindowStyle.None
        WindowStartupLocation = WindowStartupLocation.CenterScreen
        FontFamily = New FontFamily("Microsoft YaHei UI")
        Dim Light As Boolean
        Try
            Light = Settings.Get(Of Integer)("UiThemeMode") = 1
        Catch
        End Try
        Dim Surface As Brush = Paint(If(Light, "#F5F5EF", "#1C2022"))
        Dim Ink As Brush = Paint(If(Light, "#191D1F", "#F1F2E9"))
        Dim Muted As Brush = Paint(If(Light, "#555F60", "#B3BAB9"))
        Background = Surface
        Foreground = Ink
        Dim Frame As New Border With {.BorderBrush = Paint("#68706A"), .BorderThickness = New Thickness(1), .Background = Surface}
        Dim Stack As New StackPanel With {.Margin = New Thickness(26, 22, 26, 24)}
        Frame.Child = Stack
        Me.Content = Frame
        Dim Brand As New TextBlock With {.Text = "BRCL / SYSTEM MESSAGE", .Foreground = Muted, .FontFamily = New FontFamily("Consolas"), .FontSize = 11}
        Stack.Children.Add(Brand)
        Stack.Children.Add(New TextBlock With {.Text = Content.Title, .Foreground = Ink, .FontSize = 23, .FontWeight = FontWeights.SemiBold, .TextWrapping = TextWrapping.Wrap, .Margin = New Thickness(0, 10, 0, 16)})
        Stack.Children.Add(New Border With {.Height = 3, .Background = Paint("#F2D649"), .Margin = New Thickness(0, 0, 0, 18)})
        Dim Body As New StackPanel
        Body.Children.Add(New TextBlock With {.Text = Content.Summary, .Foreground = Ink, .FontSize = 14, .LineHeight = 22, .TextWrapping = TextWrapping.Wrap})
        Body.Children.Add(New TextBlock With {.Text = "建议处理", .Foreground = Ink, .FontSize = 12, .FontWeight = FontWeights.SemiBold, .Margin = New Thickness(0, 18, 0, 6)})
        Body.Children.Add(New TextBlock With {.Text = Content.Advice, .FontSize = 13, .Foreground = Muted, .LineHeight = 21, .TextWrapping = TextWrapping.Wrap})
        Dim Details As New Expander With {.Header = New TextBlock With {.Text = "技术详情", .Foreground = Ink}, .Margin = New Thickness(0, 16, 0, 0), .Foreground = Ink}
        Details.Content = New TextBox With {.Text = Content.Details, .IsReadOnly = True, .TextWrapping = TextWrapping.Wrap, .VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            .Background = Surface, .Foreground = Ink, .BorderThickness = New Thickness(0), .FontFamily = New FontFamily("Consolas, Microsoft YaHei UI"), .FontSize = 11, .MaxHeight = 150, .Margin = New Thickness(0, 8, 0, 0)}
        Body.Children.Add(Details)
        Stack.Children.Add(New ScrollViewer With {.Content = Body, .VerticalScrollBarVisibility = ScrollBarVisibility.Auto, .MaxHeight = Math.Max(120, MaxHeight - 230)})
        Dim Feedback As New TextBlock With {.Text = Content.Code, .FontFamily = New FontFamily("Consolas, Microsoft YaHei UI"), .FontSize = 11, .Foreground = Muted, .Margin = New Thickness(0, 16, 0, 10)}
        Stack.Children.Add(Feedback)
        Dim Actions As New WrapPanel With {.HorizontalAlignment = HorizontalAlignment.Right}
        Dim Copy = MakeButton("复制详情", False, Surface, Ink)
        AddHandler Copy.Click, Sub()
                                   Try
                                       Clipboard.SetText(Content.Code & vbCrLf & Content.Title & vbCrLf & Content.Summary & vbCrLf & Content.Advice & vbCrLf & Content.Details)
                                       Feedback.Text = Content.Code & " · 已复制"
                                   Catch
                                       Feedback.Text = "复制失败，请稍后再试"
                                   End Try
                               End Sub
        Actions.Children.Add(Copy)
        Dim Logs = MakeButton("打开日志目录", False, Surface, Ink)
        AddHandler Logs.Click, Sub()
                                   Try
                                       Dim Folder = Paths.BaseThenName
                                       If Not Directory.Exists(Folder) Then Throw New DirectoryNotFoundException()
                                       Process.Start(New ProcessStartInfo(Folder) With {.UseShellExecute = True})
                                   Catch
                                       Feedback.Text = "暂时无法打开日志目录"
                                   End Try
                               End Sub
        Actions.Children.Add(Logs)
        Dim Dismiss = MakeButton(If(Fatal, "关闭启动器", "知道了"), True, Surface, Ink)
        Dismiss.IsDefault = True
        Dismiss.IsCancel = True
        AddHandler Dismiss.Click, Sub() Close()
        Actions.Children.Add(Dismiss)
        Stack.Children.Add(Actions)
        AddHandler Frame.MouseLeftButtonDown, Sub(Sender, E)
                                                  If E.GetPosition(Stack).Y < 70 Then DragMove()
                                              End Sub
        AddHandler PreviewKeyDown, Sub(Sender, E)
                                       If E.Key = Input.Key.Escape Then
                                           E.Handled = True
                                           Close()
                                       End If
                                   End Sub
        Try
            Icon = New Imaging.BitmapImage(New Uri("pack://application:,,,/BRCL;component/Images/LauncherLogo.png"))
            If FrmMain IsNot Nothing AndAlso FrmMain.IsLoaded AndAlso FrmMain.IsVisible Then
                Owner = FrmMain
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            End If
        Catch
        End Try
    End Sub

    Private Shared Function Paint(Value As String) As Brush
        Return CType(New BrushConverter().ConvertFromString(Value), Brush)
    End Function

    Private Shared Function MakeButton(Label As String, Primary As Boolean, Surface As Brush, Ink As Brush) As Button
        Dim Result As New Button With {.Content = New TextBlock With {.Text = Label, .Foreground = If(Primary, Paint("#191D1F"), Ink)}, .MinHeight = 36, .Padding = New Thickness(14, 8, 14, 8), .Margin = New Thickness(8, 4, 0, 0),
            .Foreground = If(Primary, Paint("#191D1F"), Ink), .Background = If(Primary, Paint("#F2D649"), Surface), .BorderBrush = Paint("#68706A"), .BorderThickness = New Thickness(1)}
        Result.Template = CType(XamlReader.Parse("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Button'><Border Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}' Padding='{TemplateBinding Padding}'><ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/></Border></ControlTemplate>"), ControlTemplate)
        Return Result
    End Function
End Class
