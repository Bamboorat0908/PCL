Imports System.Diagnostics
Imports System.Windows.Automation
Imports System.Windows.Threading

''' <summary>下载页内的离线恐龙跑酷，离开页面或失焦时暂停。</summary>
Public Class DownloadMiniGame
    Inherits StackPanel

    Private ReadOnly Model As New DinoRunnerModel
    Private ReadOnly Board As DinoGameBoard
    Private ReadOnly Counter As New TextBlock With {.FontFamily = New FontFamily("Consolas, Microsoft YaHei UI"), .FontSize = 12, .Margin = New Thickness(0, 0, 0, 9)}
    Private ReadOnly StateText As New TextBlock With {.TextWrapping = TextWrapping.Wrap, .Margin = New Thickness(0, 9, 0, 0), .FontSize = 12}
    Private ReadOnly PlayButton As New MyButton With {.Text = "开始跑酷", .Height = 30, .MinWidth = 90, .Focusable = False, .Margin = New Thickness(0, 0, 8, 0)}
    Private ReadOnly ResetButton As New MyButton With {.Text = "重新开始", .Height = 30, .Focusable = False, .MinWidth = 90}
    Private ReadOnly Ticker As New DispatcherTimer(DispatcherPriority.Background) With {.Interval = TimeSpan.FromMilliseconds(20)}
    Private ReadOnly Clock As New Stopwatch
    Private Running As Boolean
    Private HasStarted As Boolean
    Private DownloadState As String = ""
    Private HostWindow As Window
    Private Best As Integer

    Public Sub New()
        SetResourceReference(TextElement.ForegroundProperty, "ColorBrush1")
        Board = New DinoGameBoard(Model) With {.Width = 480, .Height = 220, .Focusable = True}
        AutomationProperties.SetName(Board, "小恐龙跑酷；空格、上方向键或点击跳跃；下方向键下蹲；P 暂停")
        Children.Add(Counter)
        Children.Add(New Viewbox With {.Stretch = Stretch.Uniform, .StretchDirection = StretchDirection.DownOnly, .Child = Board})
        Dim Buttons As New StackPanel With {.Orientation = Orientation.Horizontal, .Margin = New Thickness(0, 10, 0, 0)}
        Buttons.Children.Add(PlayButton)
        Buttons.Children.Add(ResetButton)
        Children.Add(Buttons)
        Children.Add(New TextBlock With {.Text = "空格 / ↑ / 点击跳跃 · ↓ 下蹲 · P 暂停。躲开仙人掌和飞鸟，速度会逐渐增加。", .TextWrapping = TextWrapping.Wrap, .FontSize = 11, .Margin = New Thickness(0, 9, 0, 0)})
        Children.Add(StateText)
        AddHandler PlayButton.Click, Sub() TogglePlay()
        AddHandler ResetButton.Click, Sub() StartRound()
        AddHandler Ticker.Tick, AddressOf Tick
        AddHandler Board.MouseLeftButtonDown, Sub(sender, e)
                                                 Board.Focus()
                                                 If Not Running Then
                                                     If Not HasStarted OrElse Model.IsOver Then StartRound() Else ResumeGame()
                                                 End If
                                                 Model.Jump()
                                                 e.Handled = True
                                             End Sub
        AddHandler Board.KeyDown, AddressOf OnGameKeyDown
        AddHandler Board.KeyUp, Sub(sender, e)
                                    If e.Key = Key.Down Then Model.Ducking = False : e.Handled = True
                                End Sub
        AddHandler Board.LostKeyboardFocus, Sub() PauseGame()
        AddHandler Loaded, AddressOf OnLoaded
        AddHandler Unloaded, AddressOf OnUnloaded
        Draw()
    End Sub

    Private Sub OnLoaded(sender As Object, e As RoutedEventArgs)
        HostWindow = Window.GetWindow(Me)
        If HostWindow IsNot Nothing Then AddHandler HostWindow.Deactivated, AddressOf OnWindowDeactivated
        Draw()
    End Sub

    Private Sub OnUnloaded(sender As Object, e As RoutedEventArgs)
        PauseGame()
        If HostWindow IsNot Nothing Then RemoveHandler HostWindow.Deactivated, AddressOf OnWindowDeactivated
        HostWindow = Nothing
    End Sub

    Private Sub OnWindowDeactivated(sender As Object, e As EventArgs)
        PauseGame()
    End Sub

    Public Sub PauseGame()
        Ticker.Stop()
        Clock.Stop()
        Running = False
        Model.Ducking = False
        PlayButton.Text = If(Model.IsOver, "再来一局", If(HasStarted, "继续跑酷", "开始跑酷"))
        Draw()
    End Sub

    Private Sub TogglePlay()
        If Running Then
            PauseGame()
        ElseIf Not HasStarted OrElse Model.IsOver Then
            StartRound()
        Else
            ResumeGame()
        End If
    End Sub

    Private Sub StartRound()
        Model.Reset()
        HasStarted = True
        ResumeGame()
    End Sub

    Private Sub ResumeGame()
        If Not IsLoaded OrElse Not IsVisible Then Return
        Board.Focus()
        Running = True
        PlayButton.Text = "暂停跑酷"
        Clock.Restart()
        Ticker.Start()
        Draw()
    End Sub

    Private Sub OnGameKeyDown(sender As Object, e As KeyEventArgs)
        If Keyboard.Modifiers <> ModifierKeys.None Then Return
        Select Case e.Key
            Case Key.Space, Key.Up
                If Not e.IsRepeat Then
                    If Not Running Then
                        If Not HasStarted OrElse Model.IsOver Then StartRound() Else ResumeGame()
                    End If
                    Model.Jump()
                End If
            Case Key.Down
                Model.Ducking = True
            Case Key.P
                If Not e.IsRepeat Then TogglePlay()
            Case Else
                Return
        End Select
        e.Handled = True
    End Sub

    Private Sub Tick(sender As Object, e As EventArgs)
        If Not IsVisible OrElse HostWindow Is Nothing OrElse Not HostWindow.IsActive OrElse
            HostWindow.WindowState = WindowState.Minimized OrElse Not Board.IsKeyboardFocusWithin Then
            PauseGame()
            Return
        End If
        Model.Advance(Clock.Elapsed.TotalSeconds)
        Clock.Restart()
        Best = Math.Max(Best, Model.Score)
        If Model.IsOver Then PauseGame()
        Draw()
    End Sub

    Public Sub UpdateDownloadState(Active As Boolean, Failed As Boolean)
        Dim NextState = If(Failed, "failed", If(Active, "active", "done"))
        If NextState = DownloadState Then Return
        Dim Previous = DownloadState
        DownloadState = NextState
        StateText.Text = If(Failed, "有下载任务失败，请查看上方错误详情。", If(Active, "下载在后台继续，可随时收起小游戏查看进度。", "当前下载任务已结束，可收起小游戏返回。"))
        If NextState <> "active" AndAlso Previous = "active" Then PauseGame()
    End Sub

    Private Sub Draw()
        Counter.Text = "得分 " & Model.Score.ToString("00000") & "    本次最佳 " & Best.ToString("00000")
        Board.Message = If(Running, "", If(Model.IsOver, "本局结束 · 点击或空格重来", If(HasStarted, "已暂停 · 点击或空格继续", "小恐龙跑酷 / DINO RUN")))
        Board.InvalidateVisual()
    End Sub
End Class

''' <summary>用 DrawingContext 绘制像素恐龙，不在每帧创建控件。</summary>
Public Class DinoGameBoard
    Inherits FrameworkElement
    Private ReadOnly Model As DinoRunnerModel
    Public Property Message As String = ""
    Private Shared ReadOnly Ink As Brush = New SolidColorBrush(Color.FromRgb(225, 229, 217))
    Private Shared ReadOnly Accent As Brush = New SolidColorBrush(Color.FromRgb(242, 214, 73))
    Private Shared ReadOnly Back As Brush = New SolidColorBrush(Color.FromRgb(25, 33, 32))
    Private Shared ReadOnly Cactus As Brush = New SolidColorBrush(Color.FromRgb(126, 181, 143))

    Public Sub New(GameModel As DinoRunnerModel)
        Model = GameModel
        SnapsToDevicePixels = True
    End Sub

    Protected Overrides Sub OnRender(Dc As DrawingContext)
        MyBase.OnRender(Dc)
        Dc.DrawRectangle(Back, Nothing, New Rect(0, 0, 480, 220))
        Dc.DrawLine(New Pen(Ink, 1), New Point(0, DinoRunnerModel.Ground + 1), New Point(480, DinoRunnerModel.Ground + 1))
        Dim Offset = (Model.Elapsed * Model.Speed) Mod 48
        For Index = 0 To 10
            Dc.DrawRectangle(Ink, Nothing, New Rect(Index * 48 - Offset, 187 + (Index Mod 3) * 5, 10, 2))
        Next
        '云朵随距离慢速滚动。
        For Index = 0 To 2
            Dim X = ((Index * 180 + 140 - Model.Elapsed * 14) Mod 570 + 570) Mod 570 - 50
            Dc.DrawRectangle(New SolidColorBrush(Color.FromRgb(66, 79, 76)), Nothing, New Rect(X, 44 + Index * 14, 44, 3))
            Dc.DrawRectangle(New SolidColorBrush(Color.FromRgb(66, 79, 76)), Nothing, New Rect(X + 12, 38 + Index * 14, 20, 6))
        Next
        Dc.PushTransform(New TranslateTransform(52, DinoRunnerModel.Ground - Model.JumpHeight))
        Dim Duck = Model.Ducking AndAlso Model.IsGrounded
        If Duck Then
            Dc.DrawRectangle(Ink, Nothing, New Rect(3, -22, 35, 15))
            Dc.DrawRectangle(Ink, Nothing, New Rect(28, -25, 20, 10))
            Dc.DrawRectangle(Back, Nothing, New Rect(42, -23, 3, 3))
        Else
            Dc.DrawRectangle(Ink, Nothing, New Rect(15, -44, 22, 19))
            Dc.DrawRectangle(Ink, Nothing, New Rect(8, -29, 20, 21))
            Dc.DrawRectangle(Ink, Nothing, New Rect(2, -25, 10, 12))
            Dc.DrawRectangle(Ink, Nothing, New Rect(0, -31, 5, 11))
            Dc.DrawRectangle(Ink, Nothing, New Rect(25, -22, 9, 4))
            Dc.DrawRectangle(Back, Nothing, New Rect(30, -40, 3, 3))
            Dc.DrawRectangle(Back, Nothing, New Rect(27, -29, 10, 2))
        End If
        Dim StepFrame = CInt(Model.Elapsed * 12) Mod 2
        Dc.DrawRectangle(Ink, Nothing, New Rect(10, -8, 5, If(Model.IsGrounded AndAlso StepFrame = 0, 8, 4)))
        Dc.DrawRectangle(Ink, Nothing, New Rect(23, -8, 5, If(Model.IsGrounded AndAlso StepFrame = 1, 8, 4)))
        Dc.Pop()
        For Each Item In Model.Obstacles
            If Item.IsBird Then
                Dc.DrawRectangle(Accent, Nothing, New Rect(Item.X, Item.Y + 7, Item.Width, 7))
                Dim WingY = If(CInt(Model.Elapsed * 8) Mod 2 = 0, Item.Y, Item.Y + 12)
                Dc.DrawRectangle(Accent, Nothing, New Rect(Item.X + 12, WingY, 9, 10))
            Else
                Dc.DrawRectangle(Cactus, Nothing, New Rect(Item.X + Item.Width * 0.35, Item.Y, Item.Width * 0.3, Item.Height))
                Dc.DrawRectangle(Cactus, Nothing, New Rect(Item.X, Item.Y + 8, 5, Item.Height * 0.5))
                Dc.DrawRectangle(Cactus, Nothing, New Rect(Item.X, Item.Y + Item.Height * 0.5, Item.Width * 0.5, 5))
                Dc.DrawRectangle(Cactus, Nothing, New Rect(Item.X + Item.Width - 5, Item.Y + 4, 5, Item.Height * 0.45))
                Dc.DrawRectangle(Cactus, Nothing, New Rect(Item.X + Item.Width * 0.5, Item.Y + Item.Height * 0.4, Item.Width * 0.5, 5))
            End If
        Next
        If Not String.IsNullOrEmpty(Message) Then
            Dc.DrawRectangle(New SolidColorBrush(Color.FromArgb(190, 25, 33, 32)), Nothing, New Rect(0, 65, 480, 42))
            Dim Label As New FormattedText(Message, Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, New Typeface("Microsoft YaHei UI"), 17, Ink, VisualTreeHelper.GetDpi(Me).PixelsPerDip)
            Dc.DrawText(Label, New Point((480 - Label.Width) / 2, 75))
        End If
    End Sub
End Class
