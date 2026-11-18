Imports System.Windows.Markup

Imports System.Windows.Automation
Imports System.Windows.Automation.Peers
Imports System.Windows.Automation.Provider

<ContentProperty("Inlines")>
Public Class MyButton

    '声明
    Public Event Click(sender As Object, e As MouseButtonEventArgs) '自定义事件

    Protected Overrides Function OnCreateAutomationPeer() As AutomationPeer
        Return New MyButtonAutomationPeer(Me)
    End Function

    Friend Sub InvokeClick()
        If Not IsEnabled Then Return
        Logger.Info($"按下按钮：{Text}")
        Dim ClickArgs As New MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) With {.RoutedEvent = Mouse.MouseUpEvent}
        If SoundFeedback Then PlayUiSound()
        RaiseEvent Click(Me, ClickArgs)
        RaiseCustomEvent()
    End Sub

    '自定义属性
    Public Uuid As Integer = GetUuid()
    Public Shared ReadOnly IconProperty As DependencyProperty = DependencyProperty.Register(NameOf(Icon), GetType(Geometry), GetType(MyButton), New FrameworkPropertyMetadata(Nothing, FrameworkPropertyMetadataOptions.AffectsMeasure))
    ''' <summary>可选的主题矢量图标；为空时保持纯文本按钮的原有尺寸。</summary>
    Public Property Icon As Geometry
        Get
            Return CType(GetValue(IconProperty), Geometry)
        End Get
        Set(value As Geometry)
            SetValue(IconProperty, value)
        End Set
    End Property
    Public ReadOnly Property Inlines As InlineCollection
        Get
            Return LabText.Inlines
        End Get
    End Property
    Public Property Text As String
        Get
            Return GetValue(TextProperty)
        End Get
        Set(value As String)
            SetValue(TextProperty, value)
        End Set
    End Property '显示文本
    Public Shared ReadOnly TextProperty As DependencyProperty = DependencyProperty.Register("Text", GetType(String), GetType(MyButton), New PropertyMetadata(New PropertyChangedCallback(
    Sub(sender As DependencyObject, e As DependencyPropertyChangedEventArgs)
        If sender IsNot Nothing Then CType(sender, MyButton).LabText.Text = e.NewValue
    End Sub)))
    Public Property TextPadding As Thickness
        Get
            Return LabText.Padding
        End Get
        Set(value As Thickness)
            LabText.Padding = value
        End Set
    End Property
    Private _ColorType As ColorState = ColorState.Normal  '配色方案
    Public Property ColorType As ColorState
        Get
            Return _ColorType
        End Get
        Set(value As ColorState)
            _ColorType = value
            RefreshColor()
            If PanFore IsNot Nothing Then IndustrialCutCorner()
        End Set
    End Property
    Public Enum ColorState
        Normal = 0
        Highlight = 1
        Red = 2
    End Enum
    '属性穿透
    Public Shared Shadows ReadOnly PaddingProperty As DependencyProperty = DependencyProperty.Register("Padding", GetType(Thickness), GetType(MyButton), New PropertyMetadata(New PropertyChangedCallback(
        Sub(sender As MyButton, e As DependencyPropertyChangedEventArgs) If sender IsNot Nothing Then sender.PanFore.Padding = e.NewValue)))
    Public Overloads Property Padding As Thickness
        Get
            Return PanFore.Padding
        End Get
        Set(value As Thickness)
            PanFore.Padding = value
        End Set
    End Property
    Public Property RealRenderTransform As Transform
        Get
            Return PanFore.RenderTransform
        End Get
        Set(value As Transform)
            PanFore.RenderTransform = value
        End Set
    End Property

    '自定义事件
    Private Sub IndustrialCutCorner() Handles Me.SizeChanged, Me.Loaded
        If ColorType <> ColorState.Highlight OrElse PanFore.ActualWidth < 24 OrElse PanFore.ActualHeight < 24 Then
            PanFore.Clip = Nothing
            Return
        End If
        Dim W = PanFore.ActualWidth
        Dim H = PanFore.ActualHeight
        Dim Clip As New StreamGeometry()
        Using Shape = Clip.Open()
            Shape.BeginFigure(New Point(0, 0), True, True)
            Shape.LineTo(New Point(W, 0), True, False)
            Shape.LineTo(New Point(W, H - 10), True, False)
            Shape.LineTo(New Point(W - 10, H), True, False)
            Shape.LineTo(New Point(0, H), True, False)
        End Using
        Clip.Freeze()
        PanFore.Clip = Clip
    End Sub

    Private Const AnimationColorIn As Integer = 100
    Private Const AnimationColorOut As Integer = 200
    Private Sub RefreshColor(Optional obj = Nothing, Optional e = Nothing) Handles Me.MouseEnter, Me.MouseLeave, Me.Loaded, Me.IsEnabledChanged
        Try
            '实心主动作使用独立前景；主题切换由动态资源更新。
            If ColorType = ColorState.Highlight Then
                AniStop("MyButton Color " & Uuid)
                PanFore.SetResourceReference(Border.BackgroundProperty, If(IsEnabled, "IndustrialAccent", "ColorBrush6"))
                PanFore.SetResourceReference(Border.BorderBrushProperty, If(IsEnabled, "IndustrialAccent", "ColorBrushCardLine"))
                LabText.SetResourceReference(TextBlock.ForegroundProperty, If(IsEnabled, "IndustrialOnAccent", "ColorBrush2"))
                PanFore.Opacity = If(IsMouseOver AndAlso IsEnabled, 0.9, 1)
                Return
            End If
            PanFore.Opacity = 1
            PanFore.SetResourceReference(Border.BackgroundProperty, "ColorBrush7")
            LabText.SetBinding(TextBlock.ForegroundProperty, New Binding("BorderBrush") With {.Source = PanFore})
            If IsLoaded AndAlso AniControlEnabled = 0 Then '防止默认属性变更触发动画

                If IsEnabled Then
                    Select Case ColorType
                        Case ColorState.Normal
                            If IsMouseOver Then
                                '指向（Main 3）
                                AniStart({AaColor(PanFore, Border.BorderBrushProperty, "ColorBrush3", AnimationColorIn)}, "MyButton Color " & Uuid)
                            Else
                                '普通（Main 1）
                                AniStart({AaColor(PanFore, Border.BorderBrushProperty, "ColorBrush1", AnimationColorOut)}, "MyButton Color " & Uuid)
                            End If
                        Case ColorState.Highlight
                            If IsMouseOver Then
                                '指向（Main 3）
                                AniStart({AaColor(PanFore, Border.BorderBrushProperty, "ColorBrush3", AnimationColorIn)}, "MyButton Color " & Uuid)
                            Else
                                '高亮（Main 2）
                                AniStart({AaColor(PanFore, Border.BorderBrushProperty, "ColorBrush2", AnimationColorOut)}, "MyButton Color " & Uuid)
                            End If
                        Case ColorState.Red
                            If IsMouseOver Then
                                '红色指向
                                AniStart({AaColor(PanFore, Border.BorderBrushProperty, "ColorBrushRedLight", AnimationColorIn)}, "MyButton Color " & Uuid)
                            Else
                                '红色
                                AniStart({AaColor(PanFore, Border.BorderBrushProperty, "ColorBrushRedDark", AnimationColorOut)}, "MyButton Color " & Uuid)
                            End If
                    End Select
                Else
                    '不可用（Gray 4）
                    AniStart({AaColor(PanFore, Border.BorderBrushProperty, ColorGray4 - PanFore.BorderBrush, AnimationColorOut)}, "MyButton Color " & Uuid)
                End If
            Else

                AniStop("MyButton Color " & Uuid)
                If IsEnabled Then
                    Select Case ColorType
                        Case ColorState.Normal
                            If IsMouseOver Then
                                PanFore.SetResourceReference(Border.BorderBrushProperty, "ColorBrush3")
                            Else
                                PanFore.SetResourceReference(Border.BorderBrushProperty, "ColorBrush1")
                            End If
                        Case ColorState.Highlight
                            If IsMouseOver Then
                                PanFore.SetResourceReference(Border.BorderBrushProperty, "ColorBrush3")
                            Else
                                PanFore.SetResourceReference(Border.BorderBrushProperty, "ColorBrush2")
                            End If
                        Case ColorState.Red
                            If IsMouseOver Then
                                PanFore.SetResourceReference(Border.BorderBrushProperty, "ColorBrushRedLight")
                            Else
                                PanFore.SetResourceReference(Border.BorderBrushProperty, "ColorBrushRedDark")
                            End If
                    End Select
                Else
                    PanFore.BorderBrush = ColorGray4
                End If

            End If
        Catch ex As Exception
            Logger.Warn(ex, "刷新按钮颜色出错")
        End Try
    End Sub

    Private PressedKey As Key = Key.None
    Public Property SoundFeedback As Boolean = True

    Private Sub Button_KeyDown(sender As Object, e As KeyEventArgs) Handles Me.KeyDown
        If e.Key <> Key.Space AndAlso e.Key <> Key.Enter Then Return
        e.Handled = True '阻止窗口把焦点按钮上的 Enter 再解释成默认按钮或启动游戏。
        If Not IsEnabled OrElse e.IsRepeat OrElse PressedKey <> Key.None Then Return
        PressedKey = e.Key
        If UiMotionEnabled Then
            AniStart(AaScaleTransform(PanFore, 0.975 - CType(PanFore.RenderTransform, ScaleTransform).ScaleX, 85,, New AniEaseOutFluent), "MyButton Scale " & Uuid)
        End If
    End Sub

    Private Sub Button_KeyUp(sender As Object, e As KeyEventArgs) Handles Me.KeyUp
        If e.Key <> Key.Space AndAlso e.Key <> Key.Enter Then Return
        e.Handled = True
        If PressedKey <> e.Key Then Return
        PressedKey = Key.None
        ReleasePressVisual()
        If Not IsEnabled Then Return
        Logger.Info($"按下按钮：{Text}")
        Dim ClickArgs As New MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) With {.RoutedEvent = Mouse.MouseUpEvent}
        If SoundFeedback Then PlayUiSound()
        RaiseEvent Click(Me, ClickArgs)
        RaiseCustomEvent()
    End Sub

    Private Sub Button_CancelPress() Handles Me.LostKeyboardFocus, Me.IsEnabledChanged, Me.Unloaded
        PressedKey = Key.None
        IsMouseDown = False
        ResetPressVisual()
    End Sub

    Private Sub ResetPressVisual()
        AniStop("MyButton Scale " & Uuid)
        CType(PanFore.RenderTransform, ScaleTransform).ScaleX = 1
        CType(PanFore.RenderTransform, ScaleTransform).ScaleY = 1
    End Sub

    Private Sub ReleasePressVisual()
        If UiMotionEnabled Then
            AniStart(AaScaleTransform(PanFore, 1 - CType(PanFore.RenderTransform, ScaleTransform).ScaleX, 160,, New AniEaseOutFluent), "MyButton Scale " & Uuid)
        Else
            ResetPressVisual()
        End If
    End Sub

    '鼠标点击判定（务必放在点击事件之后，以使得 Button_MouseUp 先于 Button_MouseLeave 执行）
    Private IsMouseDown As Boolean = False
    Private Sub Button_MouseUp(sender As Object, e As MouseButtonEventArgs) Handles Me.MouseLeftButtonUp
        If Not IsEnabled OrElse Not IsMouseDown Then Return
        Logger.Info($"按下按钮：{Text}")
        If SoundFeedback Then PlayUiSound()
        RaiseEvent Click(sender, e)
        RaiseCustomEvent() '自定义事件
    End Sub
    Private Sub Button_MouseDown(sender As Object, e As MouseButtonEventArgs) Handles Me.MouseLeftButtonDown
        If Not IsEnabled Then Return
        e.Handled = True '防止外层 ScrollViewer 抢走焦点并取消这次按下。
        IsMouseDown = True
        Focus()
        If Not UiMotionEnabled Then Return
        AniStart(AaScaleTransform(PanFore, 0.975 - CType(PanFore.RenderTransform, ScaleTransform).ScaleX, 85,, New AniEaseOutFluent), "MyButton Scale " & Uuid)
    End Sub
    Private Sub Button_MouseEnter() Handles Me.MouseEnter
        If _ColorType = ColorState.Highlight Then Return
        AniStart(AaColor(PanFore, BackgroundProperty, If(_ColorType = ColorState.Red, "ColorBrushRedBack", "ColorBrush7"), AnimationColorIn), "MyButton Background " & Uuid)
    End Sub
    Private Sub Button_MouseUp() Handles Me.MouseLeftButtonUp
        If Not IsMouseDown Then Return
        IsMouseDown = False
        ReleasePressVisual()
    End Sub
    Private Sub Button_MouseLeave() Handles Me.MouseLeave
        If _ColorType <> ColorState.Highlight Then AniStart(AaColor(PanFore, BackgroundProperty, "ColorBrush7", AnimationColorOut), "MyButton Background " & Uuid)
        If Not IsMouseDown Then Return
        IsMouseDown = False
        ReleasePressVisual()
    End Sub

End Class

Public Class MyButtonAutomationPeer
    Inherits FrameworkElementAutomationPeer
    Implements IInvokeProvider

    Public Sub New(owner As MyButton)
        MyBase.New(owner)
    End Sub

    Protected Overrides Function GetClassNameCore() As String
        Return NameOf(MyButton)
    End Function

    Protected Overrides Function GetAutomationControlTypeCore() As AutomationControlType
        Return AutomationControlType.Button
    End Function

    Protected Overrides Function GetNameCore() As String
        Dim ExplicitName = MyBase.GetNameCore()
        If Not String.IsNullOrWhiteSpace(ExplicitName) Then Return ExplicitName
        Dim Button = DirectCast(Owner, MyButton)
        Return If(String.IsNullOrWhiteSpace(Button.Text), New TextRange(Button.LabText.ContentStart, Button.LabText.ContentEnd).Text, Button.Text)
    End Function

    Protected Overrides Function GetChildrenCore() As List(Of AutomationPeer)
        Return Nothing
    End Function

    Public Overrides Function GetPattern(patternInterface As PatternInterface) As Object
        If patternInterface = PatternInterface.Invoke Then Return Me
        Return MyBase.GetPattern(patternInterface)
    End Function

    Public Sub Invoke() Implements IInvokeProvider.Invoke
        If Not IsEnabled() Then Throw New ElementNotEnabledException()
        Dim Button = DirectCast(Owner, MyButton)
        Button.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, New Action(AddressOf Button.InvokeClick))
    End Sub
End Class
