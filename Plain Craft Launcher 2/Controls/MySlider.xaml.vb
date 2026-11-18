Imports System.Windows.Shapes
Imports System.Windows.Automation
Imports System.Windows.Automation.Peers
Imports System.Windows.Automation.Provider

Public Class MySlider
    Implements ISettingControl

    Protected Overrides Function OnCreateAutomationPeer() As AutomationPeer
        Return New MySliderAutomationPeer(Me)
    End Function

    '基础

    Public Uuid As Integer = GetUuid()
    Public Event Change(sender As Object, user As Boolean)
    Public Event PreviewChange(sender As Object, e As RouteEventArgs)

    '自定义属性

    Private _MaxValue As Integer = 100
    Public Property MaxValue As Integer
        Get
            Return _MaxValue
        End Get
        Set(value As Integer)
            value = Math.Max(0, value)
            If value = _MaxValue Then Return
            Dim OldMaximum = _MaxValue
            Dim OldValue = _Value
            _MaxValue = value
            _Value = Math.Min(_Value, value)
            RefreshWidth(Nothing, Nothing)
            Dim Peer = TryCast(UIElementAutomationPeer.FromElement(Me), MySliderAutomationPeer)
            If Peer IsNot Nothing Then
                Peer.RaisePropertyChangedEvent(RangeValuePatternIdentifiers.MaximumProperty, CDbl(OldMaximum), CDbl(value))
                If OldValue <> _Value Then Peer.RaisePropertyChangedEvent(RangeValuePatternIdentifiers.ValueProperty, CDbl(OldValue), CDbl(_Value))
            End If
            If OldValue <> _Value AndAlso AniControlEnabled = 0 Then RaiseEvent Change(Me, False)
        End Set
    End Property
    Private ChangeByKey As Boolean = False
    Private _Value As Integer = 0
    Public Property Value As Integer
        Get
            Return _Value
        End Get
        Set(newValue As Integer)
            Try

                newValue = newValue.Clamp(0, MaxValue)
                If _Value = newValue Then Return

                '触发 Preview 事件，修改新值
                Dim OldValue = _Value
                _Value = newValue
                If AniControlEnabled = 0 Then
                    Dim e = New RouteEventArgs(False)
                    RaiseEvent PreviewChange(Me, e)
                    If e.Handled Then
                        _Value = OldValue
                        If ReferenceEquals(DragControl, Me) Then DragControl = Nothing
                        DragStop()
                        Return
                    End If
                End If

                If IsLoaded AndAlso AniControlEnabled = 0 AndAlso MaxValue > 0 AndAlso ActualWidth > ShapeDot.Width Then
                    Dim NewWidth As Double = _Value / MaxValue * (ActualWidth - ShapeDot.Width)
                    Dim DeltaProcess As Double = Math.Abs(LineFore.Width / (ActualWidth - ShapeDot.Width) - _Value / MaxValue)
                    Dim Time As Double = (1 - Math.Pow(1 - DeltaProcess, 3)) * 300 + If(ChangeByKey, 100, 0)
                    AniStart({
                            AaWidth(LineFore, Math.Max(0, NewWidth + If(NewWidth < 0.5, 0, 0.5)) - LineFore.Width, Time,, If(Time > 50, New AniEaseOutFluent, New AniEaseLinear)),
                            AaWidth(LineBack, Math.Max(0, ActualWidth - ShapeDot.Width - NewWidth + If(ActualWidth - ShapeDot.Width - NewWidth < 0.5, 0, 0.5)) - LineBack.Width, Time,, If(Time > 50, New AniEaseOutFluent, New AniEaseLinear)),
                            AaX(ShapeDot, NewWidth - ShapeDot.Margin.Left, Time,, If(Time > 50, New AniEaseOutFluent, New AniEaseLinear))
                         }, "MySlider Progress " & Uuid)
                Else
                    RefreshWidth(Nothing, Nothing)
                End If
                Dim Peer = TryCast(UIElementAutomationPeer.FromElement(Me), MySliderAutomationPeer)
                If Peer IsNot Nothing Then Peer.RaisePropertyChangedEvent(RangeValuePatternIdentifiers.ValueProperty, CDbl(OldValue), CDbl(_Value))
                If AniControlEnabled = 0 Then RaiseEvent Change(Me, False)

            Catch ex As Exception
                Logger.Error(ex, "滑动条进度改变出错", LogBehavior.Toast)
            End Try
        End Set
    End Property
    Private Sub RefreshWidth(sender As Object, e As SizeChangedEventArgs) Handles Me.SizeChanged
        If Not IsNothing(e) Then PanMain.Width = e.NewSize.Width
        AniStop("MySlider Progress " & Uuid)
        Dim AvailableWidth = Math.Max(0, ActualWidth - ShapeDot.Width)
        Dim NewWidth As Double = If(MaxValue = 0, 0, _Value / MaxValue * AvailableWidth)
        LineFore.Width = Math.Max(0, NewWidth + If(NewWidth < 0.5, 0, 0.5))
        LineBack.Width = Math.Max(0, AvailableWidth - NewWidth + If(AvailableWidth - NewWidth < 0.5, 0, 0.5))
        ShapeDot.Margin = New Thickness(NewWidth, ShapeDot.Margin.Top, ShapeDot.Margin.Right, ShapeDot.Margin.Bottom)
    End Sub

    '拖动

    Public GetHintText As [Delegate]
    Private Sub DragStart(sender As Object, e As MouseButtonEventArgs) Handles Me.MouseLeftButtonDown
        If Not IsEnabled Then Return
        Focus() '实际操作才取得焦点；鼠标经过滑条不改变键盘位置。
        e.Handled = True '防止 ScrollViewer 失焦问题
        DragControl = Me
        RequestMainTick()
        RefreshColor()
        FrmMain.DragDoing()
        AniStart({
                 AaScaleTransform(ShapeDot, 1.3 - CType(ShapeDot.RenderTransform, ScaleTransform).ScaleX, 40,, New AniEaseOutFluent)
            }, "MySlider Scale " & Uuid)
        RefreshPopup()
        AniStop("MySlider KeyPopup " & Uuid)
    End Sub
    Public Sub DragDoing()
        If Not IsEnabled OrElse Not IsLoaded OrElse MaxValue = 0 OrElse ActualWidth <= ShapeDot.Width Then Return
        Dim Percent As Double = ((Mouse.GetPosition(PanMain).X - ShapeDot.Width / 2) / (ActualWidth - ShapeDot.Width)).Clamp(0, 1)
        Dim NewValue As Integer = Percent * MaxValue
        If Not NewValue = Value Then
            Value = NewValue
        End If
        RefreshPopup()
    End Sub
    Public Sub DragStop()
        RefreshColor()
        AniStart({
                 AaScaleTransform(ShapeDot, 1 - CType(ShapeDot.RenderTransform, ScaleTransform).ScaleX, 200,, New AniEaseOutFluent)
            }, "MySlider Scale " & Uuid)
        Popup.IsOpen = False
    End Sub
    Private Sub CancelDrag() Handles Me.IsEnabledChanged, Me.Unloaded
        If ReferenceEquals(DragControl, Me) Then DragControl = Nothing
        AniStop("MySlider Scale " & Uuid)
        AniStop("MySlider KeyPopup " & Uuid)
        CType(ShapeDot.RenderTransform, ScaleTransform).ScaleX = 1
        CType(ShapeDot.RenderTransform, ScaleTransform).ScaleY = 1
        Popup.IsOpen = False
    End Sub
    Public Sub RefreshPopup()
        If GetHintText Is Nothing Then Return
        Popup.IsOpen = True
        TextHint.Text = GetHintText.DynamicInvoke(Value)
        Dim typeface As New Typeface(TextHint.FontFamily, TextHint.FontStyle, TextHint.FontWeight, TextHint.FontStretch)
        Dim formattedText As New FormattedText(TextHint.Text, Thread.CurrentThread.CurrentCulture, TextHint.FlowDirection, typeface, TextHint.FontSize, TextHint.Foreground, DPI)
        TextHint.Width = formattedText.Width '使用手动测量的宽度修复 #1057
    End Sub

    '指向动画

    Private Sub RefreshColor() Handles Me.IsEnabledChanged, Me.MouseEnter, Me.MouseLeave
        Try

            '判断当前颜色
            Dim ForegroundName As String, DotFillName As String
            Dim AnimationTime As Integer
            If IsEnabled Then
                If Not IsNothing(DragControl) AndAlso DragControl.Equals(Me) Then
                    ForegroundName = "ColorBrush3"
                    DotFillName = "ColorBrush3"
                    AnimationTime = 40
                ElseIf IsMouseOver Then
                    ForegroundName = "ColorBrush3"
                    DotFillName = "ColorBrush3"
                    AnimationTime = 40
                Else
                    ForegroundName = "ColorBrushBg0"
                    DotFillName = "ColorBrushBg0"
                    AnimationTime = 100
                End If
            Else
                ForegroundName = "ColorBrushGray5"
                DotFillName = "ColorBrushGray5"
                AnimationTime = 200
            End If
            '触发颜色动画
            If IsLoaded AndAlso AniControlEnabled = 0 Then '防止默认属性变更触发动画
                '有动画
                AniStart({
                            AaColor(Me, BorderBrushProperty, ForegroundName, AnimationTime),
                            AaColor(ShapeDot, Ellipse.FillProperty, DotFillName, AnimationTime)
                         }, "MySlider Color " & Uuid)
            Else
                '无动画
                AniStop("MySlider Color " & Uuid)
                SetResourceReference(BorderBrushProperty, ForegroundName)
                ShapeDot.SetResourceReference(Ellipse.FillProperty, DotFillName)
            End If

        Catch ex As Exception
            Logger.Warn(ex, "滑动条颜色改变出错")
        End Try
    End Sub

    '按键改变

    Public Property ValueByKey As UInteger = 1
    Private Sub AutomationEnabledChanged(sender As Object, e As DependencyPropertyChangedEventArgs) Handles Me.IsEnabledChanged
        Dim Peer = TryCast(UIElementAutomationPeer.FromElement(Me), MySliderAutomationPeer)
        If Peer IsNot Nothing Then Peer.RaisePropertyChangedEvent(RangeValuePatternIdentifiers.IsReadOnlyProperty, Not CBool(e.OldValue), Not CBool(e.NewValue))
    End Sub

    Private Sub MySlider_KeyDown(sender As Object, e As KeyEventArgs) Handles Me.KeyDown
        If e.Handled OrElse Not IsEnabled Then Return
        '拒绝一边拖动一边用按键改变
        If ReferenceEquals(Me, DragControl) Then Return
        '改变值
        Dim NewValue As Long
        Dim StepValue = CLng(Math.Max(1, Math.Min(CLng(ValueByKey), Integer.MaxValue)))
        Select Case e.Key
            Case Key.Left, Key.Down
                NewValue = CLng(Value) - StepValue
            Case Key.Right, Key.Up
                NewValue = CLng(Value) + StepValue
            Case Key.Home
                NewValue = 0
            Case Key.End
                NewValue = MaxValue
            Case Key.PageDown
                NewValue = CLng(Value) - Math.Min(CLng(Integer.MaxValue), StepValue * 10)
            Case Key.PageUp
                NewValue = CLng(Value) + Math.Min(CLng(Integer.MaxValue), StepValue * 10)
            Case Else
                Return
        End Select
        e.Handled = True
        ChangeByKey = True
        Try
            Value = CInt(Math.Max(0L, Math.Min(CLng(MaxValue), NewValue)))
        Finally
            ChangeByKey = False
        End Try
        '更新 Popup
        If GetHintText IsNot Nothing Then
            RefreshPopup()
            AniStop("MySlider KeyPopup " & Uuid)
            AniStart(AaCode(Sub() Popup.IsOpen = False, 700 * AniSpeed), "MySlider KeyPopup " & Uuid)
        End If
    End Sub

#Region "设置"

    Private Sub RefreshSetting(NewValue As String) Implements ISettingControl.RefreshSetting
        Value = Val(NewValue)
    End Sub

    Private Function GetCurrentSetting() As String Implements ISettingControl.GetCurrentSetting
        Return Value
    End Function

    Private Sub SaveSetting() Handles Me.Change
        SettingService.SaveSetting(Me)
    End Sub

#End Region

End Class

Public Class MySliderAutomationPeer
    Inherits FrameworkElementAutomationPeer
    Implements IRangeValueProvider

    Public Sub New(owner As MySlider)
        MyBase.New(owner)
    End Sub

    Private ReadOnly Property Slider As MySlider
        Get
            Return DirectCast(Owner, MySlider)
        End Get
    End Property

    Protected Overrides Function GetClassNameCore() As String
        Return NameOf(MySlider)
    End Function

    Protected Overrides Function GetAutomationControlTypeCore() As AutomationControlType
        Return AutomationControlType.Slider
    End Function

    Protected Overrides Function GetNameCore() As String
        Dim ExplicitName = MyBase.GetNameCore()
        If Not String.IsNullOrWhiteSpace(ExplicitName) Then Return ExplicitName
        Return If(TryCast(Slider.ToolTip, String), "")
    End Function

    Protected Overrides Function GetChildrenCore() As List(Of AutomationPeer)
        Return Nothing
    End Function

    Protected Overrides Function GetOrientationCore() As AutomationOrientation
        Return AutomationOrientation.Horizontal
    End Function

    Protected Overrides Function IsControlElementCore() As Boolean
        Return True
    End Function

    Protected Overrides Function HasKeyboardFocusCore() As Boolean
        Return Slider.IsKeyboardFocused
    End Function

    Public Overrides Function GetPattern(patternInterface As PatternInterface) As Object
        If patternInterface = PatternInterface.RangeValue Then Return Me
        Return MyBase.GetPattern(patternInterface)
    End Function

    Public ReadOnly Property IsReadOnly As Boolean Implements IRangeValueProvider.IsReadOnly
        Get
            Return Not Slider.IsEnabled
        End Get
    End Property

    Public ReadOnly Property LargeChange As Double Implements IRangeValueProvider.LargeChange
        Get
            Return Math.Min(Integer.MaxValue, SmallChange * 10)
        End Get
    End Property

    Public ReadOnly Property Maximum As Double Implements IRangeValueProvider.Maximum
        Get
            Return Slider.MaxValue
        End Get
    End Property

    Public ReadOnly Property Minimum As Double Implements IRangeValueProvider.Minimum
        Get
            Return 0
        End Get
    End Property

    Public ReadOnly Property SmallChange As Double Implements IRangeValueProvider.SmallChange
        Get
            Return Math.Max(1, Math.Min(CDbl(Slider.ValueByKey), Integer.MaxValue))
        End Get
    End Property

    Public ReadOnly Property Value As Double Implements IRangeValueProvider.Value
        Get
            Return Slider.Value
        End Get
    End Property

    Public Sub SetValue(value As Double) Implements IRangeValueProvider.SetValue
        If Not Slider.IsEnabled Then Throw New ElementNotEnabledException()
        If Double.IsNaN(value) OrElse Double.IsInfinity(value) OrElse value < Minimum OrElse value > Maximum Then Throw New ArgumentOutOfRangeException(NameOf(value))
        Slider.Value = CInt(Math.Round(value, MidpointRounding.AwayFromZero))
    End Sub
End Class
