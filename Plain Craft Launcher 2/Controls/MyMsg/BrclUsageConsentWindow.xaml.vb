Public Class BrclUsageConsentWindow

    Private Choice As Boolean?
    Private OpenPrivacySettings As Boolean

    Friend Shared Function Ask(ParentWindow As FormMain) As Boolean?
        If Not ParentWindow.IsVisible Then Return Nothing
        Dim Dialog As New BrclUsageConsentWindow With {.Owner = ParentWindow}
        Dialog.Width = Math.Min(Dialog.Width, SystemParameters.WorkArea.Width - 24)
        Dialog.MaxHeight = SystemParameters.WorkArea.Height - 24
        Dialog.ShowDialog()
        If Dialog.OpenPrivacySettings AndAlso ParentWindow.IsVisible Then
            ParentWindow.PageChange(FormMain.PageType.Setup, FormMain.PageSubType.SetupPrivacy)
        End If
        Return Dialog.Choice
    End Function

    Public Sub New()
        InitializeComponent()
        Select Case CrashMode()
            Case 1
                LabCrashMode.Text = "当前状态 · 每次询问"
            Case 2
                LabCrashMode.Text = "当前状态 · 自动发送脱敏报告"
            Case Else
                LabCrashMode.Text = "当前状态 · 不收集、不发送"
        End Select
    End Sub

    Private Sub FocusChoice() Handles Me.ContentRendered
        DeclineButton.Focus()
    End Sub

    Private Sub Decline_Click(Sender As Object, E As RoutedEventArgs)
        Choice = False
        Close()
    End Sub

    Private Sub Accept_Click(Sender As Object, E As RoutedEventArgs)
        Choice = True
        Close()
    End Sub

    Private Sub Dismiss_Click(Sender As Object, E As RoutedEventArgs)
        Close()
    End Sub

    Private Sub Privacy_Click(Sender As Object, E As RoutedEventArgs)
        OpenPrivacySettings = True
        Close()
    End Sub

    Private Sub HandleDismissKey(Sender As Object, E As Input.KeyEventArgs) Handles Me.PreviewKeyDown
        If E.Key <> Input.Key.Escape Then Return
        E.Handled = True
        Close()
    End Sub
End Class
