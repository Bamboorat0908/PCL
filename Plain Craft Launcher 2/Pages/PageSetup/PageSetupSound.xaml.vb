Public Class PageSetupSound
    Private Refreshing As Boolean

    Private Sub PageLoaded() Handles Me.Loaded
        Refreshing = True
        AniControlEnabled += 1
        Try
            SliderSoundVolume.GetHintText = Function(value) value & "%"
            SettingService.RefreshSettings(Me)
            PanBack.ScrollToHome()
        Finally
            AniControlEnabled -= 1
            Refreshing = False
        End Try
        RefreshOptions()
    End Sub

    Private Sub PreferenceChanged(sender As Object, user As Boolean) Handles CheckSoundEnabled.Change, CheckSoundInteraction.Change, CheckSoundStartup.Change, SliderSoundVolume.Change
        If Refreshing OrElse Not IsLoaded Then Return
        RefreshOptions()
    End Sub

    Private Sub RefreshOptions()
        PanSoundOptions.IsEnabled = CheckSoundEnabled.Checked
        PanSoundOptions.Opacity = If(CheckSoundEnabled.Checked, 1, 0.45)
        LabSoundVolume.Text = SliderSoundVolume.Value & "%"
        BtnPreviewClick.IsEnabled = CheckSoundInteraction.Checked AndAlso SliderSoundVolume.Value > 0
        BtnPreviewStartup.IsEnabled = CheckSoundStartup.Checked AndAlso SliderSoundVolume.Value > 0
    End Sub

    Private Sub PreviewStartup() Handles BtnPreviewStartup.Click
        PlayUiSound(UiSoundCue.Startup)
    End Sub
End Class
