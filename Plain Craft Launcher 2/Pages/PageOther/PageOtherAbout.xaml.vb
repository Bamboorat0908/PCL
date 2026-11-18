Public Class PageOtherAbout

    Private Sub PageOtherAbout_Loaded(sender As Object, e As RoutedEventArgs) Handles Me.Loaded
        PanBack.ScrollToHome()
        LabVersion.Text = "BRCL " & BrclReleaseVersion
        LabBaseVersion.Text = "基础版本 PCL " & VersionBaseName & " · 构建 " & GetType(PageOtherAbout).Assembly.GetName().Version.ToString()
        LabRuntime.Text = "Windows · .NET Framework 4.8 · " & If(Environment.Is64BitProcess, "64 位", "32 位") & "进程"
        LabCrashPreference.Text = "自动诊断报告 · " & If(CrashMode() = 0, "已关闭", If(CrashMode() = 1, "每次询问", "自动发送"))
    End Sub

    Private Sub CopyVersion() Handles BtnCopyVersion.Click
        ClipboardSet(String.Join(vbCrLf, {LabVersion.Text, LabBaseVersion.Text, LabRuntime.Text}))
    End Sub

    Private Sub CheckUpdate() Handles BtnCheckUpdate.Click
        UpdateCheckByButton()
    End Sub

    Private Sub DownloadLauncher() Handles BtnDownloadLauncher.Click
        Try
            OpenWebsite("http://t42.sjcmc.cn:25261/releases/BRCL-" & BrclUpdateRevision & ".exe")
        Catch ex As Exception
            ShowBrclError("打开启动器下载地址失败", ex)
        End Try
    End Sub

    Private Sub OpenLogs() Handles BtnOpenLogs.Click
        Try
            OpenExplorer(Paths.Base & "BRCL\")
        Catch ex As Exception
            ShowBrclError("打开日志目录失败", ex)
        End Try
    End Sub

    Private Sub OpenLink(sender As Object, e As EventArgs) Handles BtnBrsWebsite.Click, BtnPclSource.Click, BtnPclLicense.Click, BtnCoreSource.Click, BtnWebViewInfo.Click, BtnSupportAuthor.Click
        Dim Url As String
        If sender Is BtnBrsWebsite Then
            Url = "https://zhushu.homes/BRS/"
        ElseIf sender Is BtnSupportAuthor Then
            Url = "https://meloong.com/afd/a/LTCat"
        ElseIf sender Is BtnPclSource Then
            Url = "https://github.com/Meloong-Git/PCL"
        ElseIf sender Is BtnPclLicense Then
            ShowBundledLicense()
            Return
        ElseIf sender Is BtnCoreSource Then
            Url = "https://github.com/Meloong-Git/MeloongCore"
        Else
            Url = "https://learn.microsoft.com/microsoft-edge/webview2/"
        End If
        Try
            OpenWebsite(Url)
        Catch ex As Exception
            ShowBrclError("打开项目链接失败", ex)
        End Try
    End Sub

    Private Sub ShowBundledLicense()
        Try
            Dim Text As String
            Using Stream = GetType(PageOtherAbout).Assembly.GetManifestResourceStream("BRCL.LICENCE")
                If Stream Is Nothing Then Throw New IO.FileNotFoundException("缺少内嵌许可内容。")
                Using Reader As New IO.StreamReader(Stream, Global.System.Text.Encoding.UTF8)
                    Text = Reader.ReadToEnd()
                End Using
            End Using
            If MyMsgBox(Text, "随附许可", "关闭", "复制许可文本") = 2 Then ClipboardSet(Text)
        Catch ex As Exception
            ShowBrclError("查看随附许可失败", ex)
        End Try
    End Sub
End Class
