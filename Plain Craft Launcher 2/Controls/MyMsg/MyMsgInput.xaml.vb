Public Class MyMsgInput

    Private ReadOnly MyConverter As MyMsgBoxConverter
    Private ReadOnly Uuid As Integer = GetUuid()

    Public Sub New(Converter As MyMsgBoxConverter)
        Try

            InitializeComponent()
            Btn1.Name = Btn1.Name & GetUuid()
            Btn2.Name = Btn2.Name & GetUuid()
            MyConverter = Converter
            LabTitle.Text = Converter.Title
            LabText.Text = Converter.Text
            PanText.Visibility = If(Converter.Text = "", Visibility.Collapsed, Visibility.Visible)
            TextArea.Text = Converter.Content
            TextArea.HintText = Converter.HintText
            If Converter.ValidateRules IsNot Nothing Then TextArea.ValidateRules = Converter.ValidateRules
            Btn1.Text = Converter.Button1
            If Converter.IsWarn Then
                Btn1.ColorType = MyButton.ColorState.Red
                LabTitle.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrushRedLight")
            End If
            Btn2.Text = Converter.Button2
            Btn2.Visibility = If(Converter.Button2 = "", Visibility.Collapsed, Visibility.Visible)
            ShapeLine.StrokeThickness = GetWPFSize(1)

        Catch ex As Exception
            Logger.Error(ex, "输入弹窗初始化失败", LogBehavior.Toast)
        End Try
    End Sub

    Private Sub Load(sender As Object, e As EventArgs) Handles MyBase.Loaded
        Try

            UpdateBounds()
            AddHandler FrmMain.PanMsg.SizeChanged, AddressOf OwnerSizeChanged
            'UI 初始化
            If Btn2.IsVisible AndAlso Not Btn1.ColorType = MyButton.ColorState.Red Then Btn1.ColorType = MyButton.ColorState.Highlight
            TextArea.Focus()
            TextArea.SelectionStart = TextArea.Text.Length
            '动画
            Opacity = 0
            AniStart(AaColor(FrmMain.PanMsg, Grid.BackgroundProperty, New MyColor(160, 0, 0, 0) - FrmMain.PanMsg.Background, 200), "PanMsg Background")
            AniStart({
                AaOpacity(Me, 1, If(SystemParameters.ClientAreaAnimation, 150, 1)),
                AaDouble(Sub(i) TransformPos.Y += i, -TransformPos.Y, If(SystemParameters.ClientAreaAnimation, 180, 1),, New AniEaseOutFluent),
                AaDouble(Sub(i) TransformRotate.Angle += i, -TransformRotate.Angle, 1)
            }, "MyMsgBox " & Uuid)
            '记录日志
            Logger.Info($"输入弹窗：{LabTitle.Text}")

        Catch ex As Exception
            Logger.Error(ex, "输入弹窗加载失败", LogBehavior.Toast)
        End Try
    End Sub
    Private Sub OwnerSizeChanged(Sender As Object, E As SizeChangedEventArgs)
        UpdateBounds()
    End Sub

    Private Sub DetachOwner() Handles Me.Unloaded
        If FrmMain?.PanMsg IsNot Nothing Then RemoveHandler FrmMain.PanMsg.SizeChanged, AddressOf OwnerSizeChanged
    End Sub

    Private Sub UpdateBounds()
        If FrmMain?.PanMsg Is Nothing Then Return
        Width = Math.Max(280, Math.Min(620, FrmMain.PanMsg.ActualWidth - 40))
        MinWidth = Math.Min(340, Width)
        MaxHeight = Math.Max(210, FrmMain.PanMsg.ActualHeight - 40)
        PanText.MaxHeight = Math.Max(50, MaxHeight - 210)
    End Sub

    Private Sub Close()
        DetachOwner()
        '结束线程阻塞
        MyConverter.WaitFrame.Continue = False
        Interop.ComponentDispatcher.PopModal()
        '动画
        AniStart({
            AaCode(
            Sub()
                If Not WaitingMyMsgBox.Any() Then
                    AniStart(AaColor(FrmMain.PanMsg, Grid.BackgroundProperty, New MyColor(0, 0, 0, 0) - FrmMain.PanMsg.Background, 200, Ease:=New AniEaseOutFluent(AniEasePower.Weak)))
                End If
            End Sub, 30),
            AaOpacity(Me, -Opacity, If(SystemParameters.ClientAreaAnimation, 100, 1)),
            AaDouble(Sub(i) TransformPos.Y += i, 6 - TransformPos.Y, If(SystemParameters.ClientAreaAnimation, 100, 1),, New AniEaseOutFluent),
            AaDouble(Sub(i) TransformRotate.Angle += i, -TransformRotate.Angle, 1),
            AaCode(Sub() CType(Parent, Grid).Children.Remove(Me), , True)
        }, "MyMsgBox " & Uuid)
    End Sub

    Public Sub Btn1_Click() Handles Btn1.Click
        TextArea.Validate() '#5773
        If MyConverter.IsExited OrElse Not TextArea.IsValidated Then Return
        MyConverter.IsExited = True
        MyConverter.Result = TextArea.Text
        Close()
    End Sub
    Public Sub Btn2_Click() Handles Btn2.Click
        If MyConverter.IsExited Then Return
        MyConverter.IsExited = True
        MyConverter.Result = Nothing
        Close()
    End Sub

    Private Sub TextCaption_ValidateChanged(sender As Object, e As EventArgs) Handles TextArea.ValidateChanged
        Btn1.IsEnabled = TextArea.IsValidated
    End Sub

    Private Sub Drag(sender As Object, e As MouseButtonEventArgs) Handles PanBorder.MouseLeftButtonDown, LabTitle.MouseLeftButtonDown
        On Error Resume Next
        If e.GetPosition(ShapeLine).Y <= 2 Then FrmMain.DragMove()
    End Sub

End Class
