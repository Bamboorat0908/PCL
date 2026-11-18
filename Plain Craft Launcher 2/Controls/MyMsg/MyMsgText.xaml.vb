Public Class MyMsgText

    Private ReadOnly MyConverter As MyMsgBoxConverter
    Private ReadOnly Uuid As Integer = GetUuid()

    Public Sub New(Converter As MyMsgBoxConverter)
        Try

            InitializeComponent()
            Btn1.Name = Btn1.Name & GetUuid()
            Btn2.Name = Btn2.Name & GetUuid()
            Btn3.Name = Btn3.Name & GetUuid()
            MyConverter = Converter
            LabTitle.Text = Converter.Title
            LabCaption.Text = Converter.Text
            Dim IsError As Boolean = Converter.ErrorDetails IsNot Nothing
            LabKind.Text = If(IsError, "错误", If(Converter.IsWarn, "注意", "提示"))
            If IsError Then
                LabAdvice.Text = Converter.RecoveryAdvice
                PanAdvice.Visibility = If(String.IsNullOrWhiteSpace(Converter.RecoveryAdvice), Visibility.Collapsed, Visibility.Visible)
                LabDetails.Text = Converter.ErrorDetails
                PanDetails.Visibility = Visibility.Visible
                PanDiagnostics.Visibility = Visibility.Visible
                LabCode.Text = Converter.ErrorCode
            End If
            Btn1.Text = Converter.Button1
            If Converter.IsWarn AndAlso Not IsError Then
                Btn1.ColorType = MyButton.ColorState.Red
            End If
            Btn2.Text = Converter.Button2
            Btn3.Text = Converter.Button3
            Btn2.Visibility = If(Converter.Button2 = "", Visibility.Collapsed, Visibility.Visible)
            Btn3.Visibility = If(Converter.Button3 = "", Visibility.Collapsed, Visibility.Visible)
            ShapeLine.StrokeThickness = GetWPFSize(1)

        Catch ex As Exception
            Logger.Error(ex, "普通弹窗初始化失败", LogBehavior.Toast)
        End Try
    End Sub

    Private Sub Load(sender As Object, e As EventArgs) Handles MyBase.Loaded
        Try

            'UI 初始化
            If Not Btn1.ColorType = MyButton.ColorState.Red Then Btn1.ColorType = MyButton.ColorState.Highlight
            UpdateBounds()
            AddHandler FrmMain.PanMsg.SizeChanged, AddressOf OwnerSizeChanged
            Btn1.Focus()
            '动画
            Opacity = 0
            AniStart(AaColor(FrmMain.PanMsg, Grid.BackgroundProperty, New MyColor(160, 0, 0, 0) - FrmMain.PanMsg.Background, 160), "PanMsg Background")
            AniStart({
                AaOpacity(Me, 1, If(SystemParameters.ClientAreaAnimation, 150, 1)),
                AaDouble(Sub(i) TransformPos.Y += i, -TransformPos.Y, If(SystemParameters.ClientAreaAnimation, 180, 1),, New AniEaseOutFluent)
            }, "MyMsgBox " & Uuid)
            '记录日志
            Logger.Info($"BRCL 弹窗：{LabTitle.Text}{vbCrLf}{LabCaption.Text}")

        Catch ex As Exception
            Logger.Error(ex, "普通弹窗加载失败", LogBehavior.Toast)
        End Try
    End Sub
    Private Sub Close()
        If FrmMain?.PanMsg IsNot Nothing Then RemoveHandler FrmMain.PanMsg.SizeChanged, AddressOf OwnerSizeChanged
        '结束线程阻塞
        If MyConverter.ForceWait OrElse Not MyConverter.Button2 = "" Then MyConverter.WaitFrame.Continue = False
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
            AaCode(Sub() CType(Parent, Grid).Children.Remove(Me), , True)
        }, "MyMsgBox " & Uuid)
    End Sub

    Private Sub OwnerSizeChanged(Sender As Object, E As SizeChangedEventArgs)
        UpdateBounds()
    End Sub

    Private Sub UpdateBounds()
        Width = Math.Max(280, Math.Min(620, FrmMain.PanMsg.ActualWidth - 40))
        MinWidth = Math.Min(340, Width)
        MaxHeight = Math.Max(210, FrmMain.PanMsg.ActualHeight - 40)
        PanCaption.MaxHeight = Math.Max(70, MaxHeight - If(MyConverter.ErrorDetails Is Nothing, 155, 210))
    End Sub

    Private Sub CopyDetails(Sender As Object, E As RoutedEventArgs) Handles BtnCopyDetails.Click
        If MyConverter.ErrorDetails Is Nothing Then Return
        Try
            Clipboard.SetText(MyConverter.ErrorCode & vbCrLf & MyConverter.Title & vbCrLf & MyConverter.Text & vbCrLf & MyConverter.RecoveryAdvice & vbCrLf & MyConverter.ErrorDetails)
            LabCode.Text = MyConverter.ErrorCode & " · 已复制"
        Catch
            LabCode.Text = "复制失败，请稍后再试"
        End Try
    End Sub

    Private Sub OpenLogs(Sender As Object, E As RoutedEventArgs) Handles BtnOpenLogs.Click
        If IO.Directory.Exists(Paths.BaseThenName) Then
            OpenExplorer(Paths.BaseThenName)
        Else
            LabCode.Text = "日志目录尚未创建"
        End If
    End Sub

    Public Sub Btn1_Click() Handles Btn1.Click
        If MyConverter.IsExited Then Return
        If MyConverter.Button1Action IsNot Nothing Then
            MyConverter.Button1Action()
        Else
            MyConverter.IsExited = True
            MyConverter.Result = 1
            Close()
        End If
    End Sub
    Public Sub Btn2_Click() Handles Btn2.Click
        If MyConverter.IsExited Then Return
        If MyConverter.Button2Action IsNot Nothing Then
            MyConverter.Button2Action()
        Else
            MyConverter.IsExited = True
            MyConverter.Result = 2
            Close()
        End If
    End Sub
    Public Sub Btn3_Click() Handles Btn3.Click
        If MyConverter.IsExited Then Return
        If MyConverter.Button3Action IsNot Nothing Then
            MyConverter.Button3Action()
        Else
            MyConverter.IsExited = True
            MyConverter.Result = 3
            Close()
        End If
    End Sub

    Private Sub Drag(sender As Object, e As MouseButtonEventArgs) Handles PanBorder.MouseLeftButtonDown, LabTitle.MouseLeftButtonDown
        On Error Resume Next
        If e.GetPosition(ShapeLine).Y <= 2 Then FrmMain.DragMove()
    End Sub

End Class
