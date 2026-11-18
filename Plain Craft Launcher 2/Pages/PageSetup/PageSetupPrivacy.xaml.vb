Public Class PageSetupPrivacy
    Private Sub PageLoaded() Handles Me.Loaded
        PanBack.ScrollToHome()
        AniControlEnabled += 1
        Try
            SettingService.RefreshSettings(Me)
        Finally
            AniControlEnabled -= 1
        End Try
    End Sub

    Friend Sub RefreshUsagePreference()
        AniControlEnabled += 1
        Try
            SettingService.RefreshSettings(CheckUsageStatistics)
        Finally
            AniControlEnabled -= 1
        End Try
    End Sub

    Private Sub ReviewDiagnostics() Handles BtnReviewReports.Click
        RunInNewThread(Sub() OfferPendingReports(True), "预览诊断报告")
    End Sub
End Class
