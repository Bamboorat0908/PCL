Imports System.Windows.Shapes
Imports System.Windows.Automation
Imports System.Windows.Automation.Peers
Imports System.Windows.Automation.Provider

Public Class MyIconButton

    Shared Sub New()
        FocusableProperty.OverrideMetadata(GetType(MyIconButton), New FrameworkPropertyMetadata(True))
        KeyboardNavigation.IsTabStopProperty.OverrideMetadata(GetType(MyIconButton), New FrameworkPropertyMetadata(True))
    End Sub

    Protected Overrides Sub OnInitialized(e As EventArgs)
        MyBase.OnInitialized(e)
        If ReadLocalValue(FocusVisualStyleProperty) Is DependencyProperty.UnsetValue Then
            SetResourceReference(FocusVisualStyleProperty, "BrclFocusVisual")
        End If
    End Sub

    Protected Overrides Function OnCreateAutomationPeer() As AutomationPeer
        Return New MyIconButtonAutomationPeer(Me)
    End Function

    '自定义事件
    Public Event Click(sender As Object, e As EventArgs)

    '自定义属性

    Public Uuid As Integer = GetUuid()
    Public Property Logo As String
        Get
            Return Path.Data.ToString
        End Get
        Set(value As String)
            Path.Data = (New GeometryConverter).ConvertFromString(value)
        End Set
    End Property

    Private _LogoScale As Double = 1
    Public Property LogoScale() As Double
        Get
            Return _LogoScale
        End Get
        Set(value As Double)
            _LogoScale = value
            If Not IsNothing(Path) Then Path.RenderTransform = New ScaleTransform With {.ScaleX = LogoScale, .ScaleY = LogoScale}
        End Set
    End Property

    Public Enum Themes
        Color
        White
        Black
        Red
        Custom
    End Enum
    Private _Theme As Themes = Themes.Color
    Public Property Theme As Themes
        Get
            Return _Theme
        End Get
        Set(value As Themes)
            If _Theme = value Then Return
            _Theme = value
            If Path IsNot Nothing Then RefreshAnim()
        End Set
    End Property

    Private _Foreground As New SolidColorBrush(Color.FromRgb(128, 128, 128))
    Public Property Foreground As SolidColorBrush
        Get
            Return _Foreground
        End Get
        Set(value As SolidColorBrush)
            _Foreground = value
            AniControlEnabled += 1
            RefreshAnim()
            AniControlEnabled -= 1
        End Set
    End Property

    '鼠标点击判定（务必放在点击事件之后，以使得 Button_MouseUp 先于 Button_MouseLeave 执行）
    Private IsMouseDown As Boolean = False
    Private _CanClick As Boolean = True
    Public Property CanClick As Boolean
        Get
            Return _CanClick
        End Get
        Set(value As Boolean)
            If _CanClick = value Then Return
            Dim OldEnabled = IsEnabled AndAlso _CanClick
            _CanClick = value
            If Not value Then Button_CancelKeyboardPress()
            Dim Peer = TryCast(UIElementAutomationPeer.FromElement(Me), MyIconButtonAutomationPeer)
            If Peer IsNot Nothing Then Peer.RaisePropertyChangedEvent(AutomationElementIdentifiers.IsEnabledProperty, OldEnabled, IsEnabled AndAlso value)
        End Set
    End Property

    '窗口顶栏已拥有专门的 KeyDown 处理，显式选择沿用，避免同一按键激活两次。
    Public Property KeyboardActivationHandledExternally As Boolean = False
    Private KeyboardPress As Key = Key.None

    Private Sub Button_KeyDown(sender As Object, e As KeyEventArgs) Handles Me.KeyDown
        If KeyboardActivationHandledExternally OrElse e.Handled Then Return
        If e.Key <> Key.Enter AndAlso e.Key <> Key.Space Then Return
        e.Handled = True
        If Not IsEnabled OrElse Not CanClick OrElse e.IsRepeat OrElse KeyboardPress <> Key.None Then Return
        KeyboardPress = e.Key
    End Sub

    Private Sub Button_KeyUp(sender As Object, e As KeyEventArgs) Handles Me.KeyUp
        If KeyboardActivationHandledExternally OrElse e.Handled Then Return
        If e.Key <> Key.Enter AndAlso e.Key <> Key.Space Then Return
        e.Handled = True
        Dim Activate = KeyboardPress = e.Key AndAlso IsEnabled AndAlso CanClick
        KeyboardPress = Key.None
        If Activate Then InvokeClick()
    End Sub

    Private Sub Button_CancelKeyboardPress() Handles Me.LostKeyboardFocus, Me.IsEnabledChanged, Me.Unloaded
        KeyboardPress = Key.None
        IsMouseDown = False
        AniStop("MyIconButton Scale " & Uuid)
        If PanBack IsNot Nothing Then
            CType(PanBack.RenderTransform, ScaleTransform).ScaleX = 1
            CType(PanBack.RenderTransform, ScaleTransform).ScaleY = 1
        End If
    End Sub

    Friend Sub InvokeClick()
        If Not IsEnabled OrElse Not CanClick Then Return
        PlayUiSound()
        Dim ClickArgs As New MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) With {.RoutedEvent = Mouse.MouseUpEvent}
        RaiseEvent Click(Me, ClickArgs)
        RaiseCustomEvent()
        Dim Peer = UIElementAutomationPeer.FromElement(Me)
        If Peer IsNot Nothing Then Peer.RaiseAutomationEvent(AutomationEvents.InvokePatternOnInvoked)
    End Sub
    Private Sub Button_MouseUp(sender As Object, e As MouseButtonEventArgs) Handles Me.MouseLeftButtonUp
        If Not IsEnabled OrElse Not CanClick Then Return
        If Not IsMouseDown Then Return
        Logger.Info($"按下图标按钮{If(String.IsNullOrEmpty(Name), "", "：" & Name)}")
        PlayUiSound()
        RaiseEvent Click(sender, e)
        e.Handled = True
        Button_MouseUp()
        RaiseCustomEvent() '自定义事件
    End Sub
    Private Sub Button_MouseDown(sender As Object, e As MouseButtonEventArgs) Handles Me.MouseLeftButtonDown
        If Not IsEnabled OrElse Not CanClick Then Return
        e.Handled = True '避免外层滚动容器重新取焦并打断这次点击。
        IsMouseDown = True
        Focus()
        '指向
        AniStart(AaScaleTransform(PanBack, 0.92 - CType(PanBack.RenderTransform, ScaleTransform).ScaleX, 85,, New AniEaseOutFluent), "MyIconButton Scale " & Uuid)
    End Sub
    Private Sub Button_MouseUp() Handles Me.MouseLeftButtonUp
        If Not CanClick Then Return
        If IsMouseDown Then
            IsMouseDown = False
            AniStart(AaScaleTransform(PanBack, 1 - CType(PanBack.RenderTransform, ScaleTransform).ScaleX, 160,, New AniEaseOutFluent), "MyIconButton Scale " & Uuid)
        End If
        RefreshAnim() '直接刷新颜色以判断是否已触发 MouseLeave
    End Sub
    Private Sub Button_MouseLeave() Handles Me.MouseLeave
        If Not CanClick Then Return
        IsMouseDown = False
        AniStart({
                     AaScaleTransform(PanBack, 1 - CType(PanBack.RenderTransform, ScaleTransform).ScaleX, 250,, New AniEaseOutFluent)
                 }, "MyIconButton Scale " & Uuid)
        RefreshAnim() '直接刷新颜色以判断是否已触发 MouseLeave
    End Sub

    '务必放在 IsMouseDown 更新之后
    Private Const AnimationColorIn As Integer = 120
    Private Const AnimationColorOut As Integer = 150
    Public Sub RefreshAnim() Handles Me.MouseEnter, Me.MouseLeave, Me.Loaded, Me.IsEnabledChanged
        Try
            If IsLoaded AndAlso AniControlEnabled = 0 Then '防止默认属性变更触发动画
                If PanBack.Background Is Nothing Then PanBack.Background = New MyColor(0, 255, 255, 255)
                If Path.Fill Is Nothing Then
                    Select Case Theme
                        Case Themes.White
                            Path.Fill = New SolidColorBrush(Colors.White)
                        Case Themes.Red
                            Path.Fill = New MyColor(160, 255, 76, 76)
                        Case Themes.Black
                            Path.Fill = New MyColor(160, 0, 0, 0)
                        Case Themes.Custom
                            Path.Fill = New MyColor(160, Foreground)
                    End Select
                End If
                Dim AnimList As New List(Of AniData)
                If Not IsEnabled Then
                    AnimList.Add(AaColor(Path, Shape.FillProperty, "ColorBrushGray5", AnimationColorIn))
                ElseIf IsMouseOver Then '指向
                    Select Case Theme
                        Case Themes.Color
                            AnimList.Add(AaColor(Path, Shape.FillProperty, "ColorBrush2", AnimationColorIn))
                        Case Themes.White
                            AnimList.Add(AaColor(Path, Shape.FillProperty, New MyColor(255, 255, 255) - Path.Fill, AnimationColorIn))
                            AnimList.Add(AaColor(PanBack, BackgroundProperty, New MyColor(50, 255, 255, 255) - PanBack.Background, AnimationColorIn))
                        Case Themes.Red
                            AnimList.Add(AaColor(Path, Shape.FillProperty, New MyColor(255, 76, 76) - Path.Fill, AnimationColorIn))
                        Case Themes.Black
                            AnimList.Add(AaColor(Path, Shape.FillProperty, New MyColor(230, 0, 0, 0) - Path.Fill, AnimationColorIn))
                        Case Themes.Custom
                            AnimList.Add(AaColor(Path, Shape.FillProperty, New MyColor(255, Foreground) - Path.Fill, AnimationColorIn))
                    End Select
                Else '普通
                    Select Case Theme
                        Case Themes.Color
                            AnimList.Add(AaColor(Path, Shape.FillProperty, "ColorBrush4", AnimationColorOut))
                            PanBack.Background = New MyColor(0, 255, 255, 255)
                        Case Themes.White
                            AnimList.Add(AaColor(Path, Shape.FillProperty, New MyColor(255, 255, 255) - Path.Fill, AnimationColorOut))
                            AnimList.Add(AaColor(PanBack, BackgroundProperty, New MyColor(0, 255, 255, 255) - PanBack.Background, AnimationColorOut))
                        Case Themes.Red
                            AnimList.Add(AaColor(Path, Shape.FillProperty, New MyColor(160, 255, 76, 76) - Path.Fill, AnimationColorOut))
                            PanBack.Background = New MyColor(0, 255, 255, 255)
                        Case Themes.Black
                            AnimList.Add(AaColor(Path, Shape.FillProperty, New MyColor(160, 0, 0, 0) - Path.Fill, AnimationColorOut))
                            PanBack.Background = New MyColor(0, 255, 255, 255)
                        Case Themes.Custom
                            AnimList.Add(AaColor(Path, Shape.FillProperty, New MyColor(160, Foreground) - Path.Fill, AnimationColorOut))
                            PanBack.Background = New MyColor(0, 255, 255, 255)
                    End Select
                End If
                AniStart(AnimList, "MyIconButton Color " & Uuid)
            Else
                AniStop("MyIconButton Color " & Uuid)
                If IsEnabled Then
                    Select Case Theme
                        Case Themes.Color
                            Path.SetResourceReference(Shape.FillProperty, "ColorBrush5")
                        Case Themes.White
                            Path.Fill = New SolidColorBrush(Colors.White)
                        Case Themes.Red
                            Path.Fill = New MyColor(160, 255, 76, 76)
                        Case Themes.Black
                            Path.Fill = New MyColor(160, 0, 0, 0)
                        Case Themes.Custom
                            Path.Fill = New MyColor(160, Foreground)
                    End Select
                Else
                    Path.SetResourceReference(Shape.FillProperty, "ColorBrushGray5")
                End If
                PanBack.Background = New MyColor(0, 255, 255, 255)
            End If
        Catch ex As Exception
            Logger.Warn(ex, "刷新图标按钮动画状态出错")
        End Try
    End Sub

End Class

'导航保留原有 Grid 布局和 MouseUp/KeyDown 路由，仅补齐可访问的操作对象。
Public Class MyNavItem
    Inherits Grid

    Protected Overrides Function OnCreateAutomationPeer() As AutomationPeer
        Return New MyNavItemAutomationPeer(Me)
    End Function

    Friend Sub InvokeNavigation()
        If Not IsEnabled Then Return
        Dim Args As New MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) With {.RoutedEvent = Mouse.MouseUpEvent}
        Me.RaiseEvent(Args)
    End Sub
End Class

Public Class MyNavItemAutomationPeer
    Inherits FrameworkElementAutomationPeer
    Implements IInvokeProvider

    Public Sub New(owner As MyNavItem)
        MyBase.New(owner)
    End Sub

    Protected Overrides Function GetClassNameCore() As String
        Return NameOf(MyNavItem)
    End Function

    Protected Overrides Function GetAutomationControlTypeCore() As AutomationControlType
        Return AutomationControlType.Button
    End Function

    Protected Overrides Function IsControlElementCore() As Boolean
        Return True
    End Function

    Protected Overrides Function HasKeyboardFocusCore() As Boolean
        Return DirectCast(Owner, MyNavItem).IsKeyboardFocused
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
        Dim Item = DirectCast(Owner, MyNavItem)
        Item.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, New Action(AddressOf Item.InvokeNavigation))
    End Sub
End Class

'Border 本身不提供按钮语义；Peer 将名称、焦点和 Invoke 暴露给 Windows 辅助技术。
Public Class MyIconButtonAutomationPeer
    Inherits FrameworkElementAutomationPeer
    Implements IInvokeProvider

    Public Sub New(owner As MyIconButton)
        MyBase.New(owner)
    End Sub

    Private ReadOnly Property Button As MyIconButton
        Get
            Return DirectCast(Owner, MyIconButton)
        End Get
    End Property

    Protected Overrides Function GetClassNameCore() As String
        Return NameOf(MyIconButton)
    End Function

    Protected Overrides Function GetAutomationControlTypeCore() As AutomationControlType
        Return AutomationControlType.Button
    End Function

    Protected Overrides Function GetNameCore() As String
        Dim ExplicitName = MyBase.GetNameCore()
        If Not String.IsNullOrWhiteSpace(ExplicitName) Then Return ExplicitName
        Return If(TryCast(Button.ToolTip, String), "")
    End Function

    Protected Overrides Function IsEnabledCore() As Boolean
        Return Button.IsEnabled AndAlso Button.CanClick
    End Function

    Protected Overrides Function IsControlElementCore() As Boolean
        Return True
    End Function

    Protected Overrides Function HasKeyboardFocusCore() As Boolean
        Return Button.IsKeyboardFocused
    End Function

    Protected Overrides Function GetChildrenCore() As List(Of AutomationPeer)
        Return Nothing '图标路径属于装饰，不再单独进入朗读顺序。
    End Function

    Public Overrides Function GetPattern(patternInterface As PatternInterface) As Object
        If patternInterface = PatternInterface.Invoke Then Return Me
        Return MyBase.GetPattern(patternInterface)
    End Function

    Public Sub Invoke() Implements IInvokeProvider.Invoke
        If Not IsEnabled() Then Throw New ElementNotEnabledException()
        Button.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, New Action(AddressOf Button.InvokeClick))
    End Sub
End Class

Partial Public Module ModAnimation
    Public Sub AniDispose(Control As MyIconButton, RemoveFromChildren As Boolean, Optional CallBack As ParameterizedThreadStart = Nothing)
        If Not Control.IsHitTestVisible Then Return
        Control.IsHitTestVisible = False
        AniStart({
                 AaScaleTransform(Control, -1.5, 200,, New AniEaseInFluent),
                 AaCode(Sub()
                            If RemoveFromChildren Then
                                CType(Control.Parent, Object).Children.Remove(Control)
                            Else
                                Control.Visibility = Visibility.Collapsed
                            End If
                            If CallBack IsNot Nothing Then CallBack(Control)
                        End Sub,, True)
        }, "MyIconButton Dispose " & Control.Uuid)
    End Sub
End Module
