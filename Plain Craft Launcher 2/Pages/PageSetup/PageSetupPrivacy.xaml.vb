Public Class PageSetupPrivacy
    Private Sub PageLoaded() Handles Me.Loaded
        PanBack.ScrollToHome()
        RefreshUsagePreference()
    End Sub

    Friend Sub RefreshUsagePreference()
        AniControlEnabled += 1
        Try
            CheckUsageStatistics.Checked = UsageEnabled()
            CheckAutomaticCrashUpload.Checked = (CrashMode() = 2)
            CheckAutomaticCrashUpload.IsEnabled = UsageEnabled()
            BtnReviewReports.Visibility = If(UsageEnabled() AndAlso ReviewReports().Length > 0, Visibility.Visible, Visibility.Collapsed)
        Finally
            AniControlEnabled -= 1
        End Try
    End Sub

    Private Sub ChangeUsagePreference(Sender As Object, User As Boolean) Handles CheckUsageStatistics.Change
        If Not User OrElse AniControlEnabled <> 0 Then Return
        SaveUsageChoice(CheckUsageStatistics.Checked)
        RefreshUsagePreference()
    End Sub

    Private Sub ChangeAutomaticUpload(Sender As Object, User As Boolean) Handles CheckAutomaticCrashUpload.Change
        If Not User OrElse AniControlEnabled <> 0 Then Return
        Settings.Set("BrclCrashMode", If(UsageEnabled() AndAlso CheckAutomaticCrashUpload.Checked, 2, 0))
        RefreshUsagePreference()
    End Sub

    Private Sub ReviewDiagnostics() Handles BtnReviewReports.Click
        RunInNewThread(Sub() OfferPendingReports(True), "预览诊断报告")
    End Sub
End Class
