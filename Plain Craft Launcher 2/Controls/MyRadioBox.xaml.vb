Imports System.Windows.Markup
Imports System.Windows.Shapes

Imports System.Windows.Automation
Imports System.Windows.Automation.Peers
Imports System.Windows.Automation.Provider

<ContentProperty("Inlines")>
Public Class MyRadioBox
    Implements IMyRadio, ISettingControl

    Protected Overrides Function OnCreateAutomationPeer() As AutomationPeer
        Return New BrclRadioAutomationPeer(Me, Function() Checked, AddressOf SelectFromInput)
    End Function
    Friend Sub SelectFromInput()
        If Not IsEnabled OrElse Checked Then Return
        PlayUiSound(UiSoundCue.Toggle)
        SetChecked(True, True)
    End Sub
    Private PressedKey As Key = Key.None
    Private Sub Radiobox_KeyDown(sender As Object, e As KeyEventArgs) Handles Me.KeyDown
        If e.Handled OrElse (e.Key <> Key.Space AndAlso e.Key <> Key.Enter) Then Return
        e.Handled = True
        If Not IsEnabled OrElse e.IsRepeat OrElse PressedKey <> Key.None Then Return
        PressedKey = e.Key
    End Sub
    Private Sub Radiobox_KeyUp(sender As Object, e As KeyEventArgs) Handles Me.KeyUp
        If e.Handled OrElse (e.Key <> Key.Space AndAlso e.Key <> Key.Enter) Then Return
        e.Handled = True
        Dim Activate = PressedKey = e.Key AndAlso IsEnabled
        PressedKey = Key.None
        If Activate Then SelectFromInput()
    End Sub
    Private Sub Radiobox_CancelKey() Handles Me.LostKeyboardFocus, Me.IsEnabledChanged, Me.Unloaded
        PressedKey = Key.None
        Radiobox_MouseLeave()
    End Sub

    '基础

    Public Uuid As Integer = GetUuid()
    Public Event PreviewCheck(sender As Object, e As RouteEventArgs)
    Public Event PreviewChange(sender As Object, e As RouteEventArgs)
    Public Event Check(sender As Object, e As RouteEventArgs) Implements IMyRadio.Check
    Public Event Changed(sender As Object, e As RouteEventArgs) Implements IMyRadio.Changed

    '自定义属性
    Public Property Checked As Boolean
        Get
            Return GetValue(CheckedProperty)
        End Get
        Set(value As Boolean)
            SetChecked(value, False)
        End Set
    End Property
    Public Shared ReadOnly CheckedProperty As DependencyProperty =
        DependencyProperty.Register("Checked", GetType(Boolean), GetType(MyRadioBox), New PropertyMetadata(False,
        Sub(d As MyRadioBox, e As DependencyPropertyChangedEventArgs)
            '在使用 XAML 设置 Checked 属性时，不会触发 Checked_Set 方法，所以需要在这里手动触发 UI 改变
            If Not d.IsLoaded Then d.SyncUI()
            Dim Peer = TryCast(UIElementAutomationPeer.FromElement(d), BrclRadioAutomationPeer)
            If Peer IsNot Nothing Then Peer.NotifySelectionChanged(CBool(e.OldValue), CBool(e.NewValue))
        End Sub))

    ''' <summary>
    ''' 手动设置 Checked 属性。
    ''' </summary>
    ''' <param name="value">新的 Checked 属性。</param>
    ''' <param name="user">是否由用户引发。</param>
    Public Sub SetChecked(value As Boolean, user As Boolean)
        Try
            'Preview 事件
            If value AndAlso user Then
                Dim e = New RouteEventArgs(user)
                RaiseEvent PreviewCheck(Me, e)
                If e.Handled Then
                    Radiobox_MouseLeave()
                    Return
                End If
            End If

            '自定义属性基础
            Dim IsChanged As Boolean = False
            If IsLoaded AndAlso Not value = Checked Then RaiseEvent PreviewChange(Me, New RouteEventArgs(user))
            If value <> Checked Then
                SetValue(CheckedProperty, value)
                IsChanged = True
            End If

            '保证最多只有一个单选框选中
            If Parent Is Nothing Then Return
            Dim RadioboxList As New List(Of MyRadioBox)
            Dim CheckedCount As Integer = 0
            For Each Control In CType(Parent, Object).Children '收集控件列表与选中个数
                If TypeOf Control Is MyRadioBox Then
                    RadioboxList.Add(Control)
                    If Control.Checked Then CheckedCount += 1
                End If
            Next
            If CheckedCount > 1 Then
                If Checked Then
                    '如果本控件选中，则取消其他所有控件的选中
                    For Each Control As MyRadioBox In RadioboxList
                        If Control.Checked AndAlso Not Control.Equals(Me) Then Control.Checked = False
                    Next
                Else
                    '如果本控件未选中，则只保留第一个选中的控件
                    Dim FirstChecked = False
                    For Each Control As MyRadioBox In RadioboxList
                        If Control.Checked Then
                            If FirstChecked Then
                                Control.Checked = False '修改 Checked 会自动触发 Change 事件，所以不用额外触发
                            Else
                                FirstChecked = True
                            End If
                        End If
                    Next
                End If
            End If

            If IsChanged Then
                '触发事件
                If Checked Then RaiseEvent Check(Me, New RouteEventArgs(user))
                RaiseEvent Changed(Me, New RouteEventArgs(user))
                RaiseCustomEvent()
                '更改动画
                SyncUI()
            End If

        Catch ex As Exception
            Logger.Error(ex, "单选框勾选改变错误", LogBehavior.Toast)
        End Try
    End Sub
    Private Sub SyncUI()
        If AniControlEnabled = 0 AndAlso IsLoaded Then '防止默认属性变更触发动画
            If Checked Then
                '由无变有
                If ShapeDot.Opacity < 0.01 Then ShapeDot.Opacity = 1
                AniStart({
                          AaScale(ShapeBorder, 10 - ShapeBorder.Width, AnimationTimeOfCheck, , New AniEaseOutFluent(AniEasePower.Weak), , True),
                          AaScale(ShapeBorder, 8, AnimationTimeOfCheck * 2, AnimationTimeOfCheck * 0.6, New AniEaseOutBack, , True)
                     }, "MyRadioBox Border " & Uuid)
                AniStart({
                          AaScale(ShapeDot, 9 - ShapeDot.Width, AnimationTimeOfCheck * 2.6,, New AniEaseOutBack(AniEasePower.Weak), , True),
                          AaOpacity(ShapeDot, 1 - ShapeDot.Opacity, AnimationTimeOfCheck * 0.5, AnimationTimeOfCheck * 0.6)
                     }, "MyRadioBox Dot " & Uuid)
                AniStart({
                          AaColor(ShapeBorder, Ellipse.StrokeProperty, If(IsMouseOver, "ColorBrush3", If(IsEnabled, "ColorBrush2", "ColorBrushGray5")), AnimationTimeOfCheck)
                     }, "MyRadioBox BorderColor " & Uuid)
            Else
                '由有变无
                AniStart({
                          AaScale(ShapeBorder, 18 - ShapeBorder.Width, AnimationTimeOfCheck, , New AniEaseOutFluent, , True)
                     }, "MyRadioBox Border " & Uuid)
                AniStart({
                          AaScale(ShapeDot, -ShapeDot.Width, AnimationTimeOfCheck, , New AniEaseInFluent, , True),
                          AaOpacity(ShapeDot, -ShapeDot.Opacity, AnimationTimeOfCheck * 0.5, AnimationTimeOfCheck * 0.2)
                     }, "MyRadioBox Dot " & Uuid)
                AniStart({
                          AaColor(ShapeBorder, Ellipse.StrokeProperty, If(IsMouseOver, "ColorBrush3", If(IsEnabled, "ColorBrush1", "ColorBrushGray5")), AnimationTimeOfCheck)
                     }, "MyRadioBox BorderColor " & Uuid)
            End If
        Else
            '不使用动画
            AniStop("MyRadioBox Border " & Uuid)
            AniStop("MyRadioBox Dot " & Uuid)
            AniStop("MyRadioBox BorderColor " & Uuid)
            If Checked Then
                ShapeDot.Width = 9
                ShapeDot.Height = 9
                ShapeDot.Opacity = 1
                ShapeDot.Margin = New Thickness(5.5, 0, 0, 0)
                ShapeBorder.SetResourceReference(Ellipse.StrokeProperty, If(IsEnabled, "ColorBrush2", "ColorBrushGray5"))
            Else
                ShapeDot.Width = 0
                ShapeDot.Height = 0
                ShapeDot.Opacity = 0
                ShapeDot.Margin = New Thickness(10, 0, 0, 0)
                ShapeBorder.SetResourceReference(Ellipse.StrokeProperty, If(IsEnabled, "ColorBrush1", "ColorBrushGray5"))
            End If
        End If
    End Sub

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
    End Property '内容
    Public Shared ReadOnly TextProperty As DependencyProperty = DependencyProperty.Register("Text", GetType(String), GetType(MyRadioBox), New PropertyMetadata(New PropertyChangedCallback(
    Sub(sender, e) If sender IsNot Nothing Then CType(sender, MyRadioBox).LabText.Text = e.NewValue)))

    '点击事件

    Private MouseDowned As Boolean = False
    Private AllowMouseDown As Boolean = True
    Private Sub Radiobox_MouseUp() Handles Me.MouseLeftButtonUp
        If Not IsEnabled Then Return
        If Not MouseDowned Then Return
        Logger.Info($"按下单选框：{Text}")
        PlayUiSound(UiSoundCue.Toggle)
        SetChecked(True, True)
        MouseDowned = False
        AniStart(AaColor(ShapeBorder, Ellipse.FillProperty, "ColorBrushHalfWhite", 100), "MyRadioBox Background " & Uuid)
    End Sub
    Private Sub Radiobox_MouseDown(sender As Object, e As MouseButtonEventArgs) Handles Me.MouseLeftButtonDown
        If Not IsEnabled Then Return
        e.Handled = True
        MouseDowned = True
        Focus()
        AniStart(AaColor(ShapeBorder, Ellipse.FillProperty, "ColorBrushBg1", 100), "MyRadioBox Background " & Uuid)
        If Not Checked Then
            AniStart(AaScale(ShapeBorder, 16.5 - ShapeBorder.Width, 1000, , New AniEaseOutFluent(AniEasePower.Strong), Absolute:=True), "MyRadioBox Border " & Uuid)
        End If
    End Sub
    Private Sub Radiobox_MouseLeave() Handles Me.MouseLeave
        If Not MouseDowned Then Return
        MouseDowned = False
        AniStart(AaColor(ShapeBorder, Ellipse.FillProperty, "ColorBrushHalfWhite", 100), "MyRadioBox Background " & Uuid)
        If Not Checked Then
            AniStart(AaScale(ShapeBorder, 18 - ShapeBorder.Width, 400, , New AniEaseOutFluent(AniEasePower.Strong), Absolute:=True), "MyRadioBox Border " & Uuid)
        End If
    End Sub

    '指向动画

    Private Const AnimationTimeOfMouseIn As Integer = 100 '鼠标指向动画长度
    Private Const AnimationTimeOfMouseOut As Integer = 200 '鼠标指向动画长度
    Private Const AnimationTimeOfCheck As Integer = 150 '勾选状态变更动画长度
    Private Sub Radiobox_IsEnabledChanged() Handles Me.IsEnabledChanged
        If Me.IsLoaded AndAlso AniControlEnabled = 0 Then '防止默认属性变更触发动画
            '有动画
            If Me.IsEnabled Then
                '可用
                Radiobox_MouseLeaveAnimation()
            Else
                '不可用
                AniStart(AaColor(ShapeBorder, Ellipse.StrokeProperty, ColorGray5 - ShapeBorder.Stroke, AnimationTimeOfMouseOut), "MyRadioBox BorderColor " & Uuid)
                AniStart(AaColor(LabText, TextBlock.ForegroundProperty, ColorGray5 - LabText.Foreground, AnimationTimeOfMouseOut), "MyRadioBox TextColor " & Uuid)
            End If
        Else
            '无动画
            AniStop("MyRadioBox BorderColor " & Uuid)
            AniStop("MyRadioBox TextColor " & Uuid)
            LabText.SetResourceReference(TextBlock.ForegroundProperty, If(Me.IsEnabled, "ColorBrush1", "ColorBrushGray5"))
            ShapeBorder.SetResourceReference(Ellipse.StrokeProperty, If(Me.IsEnabled, If(Checked, "ColorBrush2", "ColorBrush1"), "ColorBrushGray5"))
        End If
    End Sub
    Private Sub Radiobox_MouseEnterAnimation() Handles Me.MouseEnter
        AniStart(AaColor(ShapeBorder, Ellipse.StrokeProperty, "ColorBrush3", AnimationTimeOfMouseIn), "MyRadioBox BorderColor " & Uuid)
        AniStart(AaColor(LabText, TextBlock.ForegroundProperty, "ColorBrush3", AnimationTimeOfMouseIn), "MyRadioBox TextColor " & Uuid)
    End Sub
    Private Sub Radiobox_MouseLeaveAnimation() Handles Me.MouseLeave
        If Not Me.IsEnabled Then Return 'MouseLeave 比 IsEnabledChanged 后执行，所以如果自定义事件修改了 IsEnabled，将导致显示错误
        If IsLoaded AndAlso AniControlEnabled = 0 Then
            AniStart(AaColor(ShapeBorder, Ellipse.StrokeProperty, If(IsEnabled, If(Checked, "ColorBrush2", "ColorBrush1"), "ColorBrushGray5"), AnimationTimeOfMouseOut), "MyRadioBox BorderColor " & Uuid)
            AniStart(AaColor(LabText, TextBlock.ForegroundProperty, If(IsEnabled, "ColorBrush1", "ColorBrushGray5"), AnimationTimeOfMouseOut), "MyRadioBox TextColor " & Uuid)
        Else
            AniStop("MyRadioBox BorderColor " & Uuid)
            AniStop("MyRadioBox TextColor " & Uuid)
            ShapeBorder.SetResourceReference(Ellipse.StrokeProperty, If(IsEnabled, If(Checked, "ColorBrush2", "ColorBrush1"), "ColorBrushGray5"))
            LabText.SetResourceReference(TextBlock.ForegroundProperty, If(IsEnabled, "ColorBrush1", "ColorBrushGray5"))
        End If
    End Sub

#Region "设置"

    Private Sub RefreshSetting(NewValue As String) Implements ISettingControl.RefreshSetting
        Checked = NewValue = SettingService.GetValue(Me)
    End Sub

    Private Function GetCurrentSetting() As String Implements ISettingControl.GetCurrentSetting
        Return If(Checked, SettingService.GetValue(Me), Nothing)
    End Function

    Private Sub SaveSetting() Handles Me.Check
        SettingService.SaveSetting(Me)
    End Sub

#End Region

End Class

'Both radio styles keep their existing same-parent grouping. As with WPF's
'RadioButtonAutomationPeer, Select replaces the selection and removal is invalid.
Public Class BrclRadioAutomationPeer
    Inherits FrameworkElementAutomationPeer
    Implements ISelectionItemProvider

    Private ReadOnly IsChecked As Func(Of Boolean)
    Private ReadOnly SelectAction As Action
    Public Sub New(owner As FrameworkElement, checked As Func(Of Boolean), selectAction As Action)
        MyBase.New(owner)
        IsChecked = checked
        Me.SelectAction = selectAction
    End Sub
    Protected Overrides Function GetClassNameCore() As String
        Return Owner.GetType().Name
    End Function
    Protected Overrides Function GetAutomationControlTypeCore() As AutomationControlType
        Return AutomationControlType.RadioButton
    End Function
    Protected Overrides Function GetNameCore() As String
        Dim ExplicitName = MyBase.GetNameCore()
        If Not String.IsNullOrWhiteSpace(ExplicitName) Then Return ExplicitName
        Dim Label = If(TypeOf Owner Is MyRadioBox, DirectCast(Owner, MyRadioBox).LabText, DirectCast(Owner, MyRadioButton).LabText)
        Return New TextRange(Label.ContentStart, Label.ContentEnd).Text
    End Function
    Protected Overrides Function GetChildrenCore() As List(Of AutomationPeer)
        Return Nothing
    End Function
    Private Function GroupItems() As List(Of FrameworkElement)
        Dim ParentPanel = TryCast(DirectCast(Owner, FrameworkElement).Parent, Panel)
        If ParentPanel Is Nothing Then Return New List(Of FrameworkElement) From {DirectCast(Owner, FrameworkElement)}
        Return ParentPanel.Children.OfType(Of FrameworkElement)().Where(Function(item) Owner.GetType().IsInstanceOfType(item)).ToList()
    End Function
    Protected Overrides Function GetPositionInSetCore() As Integer
        Return GroupItems().IndexOf(DirectCast(Owner, FrameworkElement)) + 1
    End Function
    Protected Overrides Function GetSizeOfSetCore() As Integer
        Return GroupItems().Count
    End Function
    Public Overrides Function GetPattern(patternInterface As PatternInterface) As Object
        If patternInterface = PatternInterface.SelectionItem Then Return Me
        Return MyBase.GetPattern(patternInterface)
    End Function
    Public ReadOnly Property IsSelected As Boolean Implements ISelectionItemProvider.IsSelected
        Get
            Return IsChecked()
        End Get
    End Property
    Public ReadOnly Property SelectionContainer As IRawElementProviderSimple Implements ISelectionItemProvider.SelectionContainer
        Get
            Return Nothing 'Same as native RadioButton: sibling group is exposed by position/size.
        End Get
    End Property
    Public Sub SelectItem() Implements ISelectionItemProvider.Select
        If Not IsEnabled() Then Throw New ElementNotEnabledException()
        SelectAction()
    End Sub
    Public Sub AddToSelection() Implements ISelectionItemProvider.AddToSelection
        If Not IsEnabled() Then Throw New ElementNotEnabledException()
        If Not IsSelected Then Throw New InvalidOperationException("单选项请使用 Select 进行切换。")
    End Sub
    Public Sub RemoveFromSelection() Implements ISelectionItemProvider.RemoveFromSelection
        If Not IsEnabled() Then Throw New ElementNotEnabledException()
        If IsSelected Then Throw New InvalidOperationException("单选组必须保留一个选中项。")
    End Sub
    Public Sub NotifySelectionChanged(oldValue As Boolean, newValue As Boolean)
        RaisePropertyChangedEvent(SelectionItemPatternIdentifiers.IsSelectedProperty, oldValue, newValue)
        If newValue Then RaiseAutomationEvent(AutomationEvents.SelectionItemPatternOnElementSelected)
    End Sub
End Class
