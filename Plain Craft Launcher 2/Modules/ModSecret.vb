'由于包含加解密等安全信息，本文件中的部分代码已被删除

Friend Module ModSecret

    '标注 BRCL 的不同分支，仅用于替换标记
    Public Const VersionBranchMain As String = "OpenSource"
    '在开源版的注册表与常规版的注册表隔离，以防数据冲突
    Public Const RegFolder As String = "PCLDebug"
    '用于微软登录的 ClientId
    Public OAuthClientId As String = If(Environment.GetEnvironmentVariable("BRCL_MS_CLIENT_ID"), If(Environment.GetEnvironmentVariable("PCL_MS_CLIENT_ID"), ""))
    'Public application identifier from Prism Launcher; no client secret is included.
    'https://github.com/PrismLauncher/PrismLauncher/blob/develop/CMakeLists.txt
    Private Const BundledMicrosoftClientId As String = "c36a9fb6-4f2a-41ff-90bd-ae7cc92031eb"

    Public Sub EnsureMicrosoftClientId()
        Dim ConfigPath = IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "BRCL", "MicrosoftClientId.txt")
        If String.IsNullOrWhiteSpace(OAuthClientId) AndAlso IO.File.Exists(ConfigPath) Then OAuthClientId = IO.File.ReadAllText(ConfigPath).Trim()
        If String.IsNullOrWhiteSpace(OAuthClientId) Then OAuthClientId = BundledMicrosoftClientId
        If String.IsNullOrWhiteSpace(OAuthClientId) Then
            Dim Value = MyMsgBoxInput("配置微软正版登录", "请输入已获授权、支持 Minecraft 登录的 Microsoft 应用 Client ID。" & vbCrLf &
                "BRCL 开源源码未附带此值。请向构建维护者获取或配置自己的应用，无需填写密码或 Client Secret。", HintText:="Microsoft 应用 Client ID")
            If String.IsNullOrWhiteSpace(Value) Then Throw New OperationCanceledException
            Dim ParsedId As Guid
            If Not Guid.TryParse(Value.Trim(), ParsedId) Then Throw New Exception("$Client ID 应为有效的应用 GUID，请检查后重试。")
            IO.Directory.CreateDirectory(IO.Path.GetDirectoryName(ConfigPath))
            IO.File.WriteAllText(ConfigPath, ParsedId.ToString())
            OAuthClientId = ParsedId.ToString()
        End If
        Dim Id As Guid
        If Not Guid.TryParse(OAuthClientId.Trim(), Id) Then Throw New Exception("$微软 Client ID 格式无效，请检查环境变量或 BRCL\MicrosoftClientId.txt。")
        OAuthClientId = Id.ToString()
    End Sub
    'CurseForge API Key
    Public CurseForgeAPIKey As String = If(Environment.GetEnvironmentVariable("PCL_CURSEFORGE_API_KEY"), "")
    '用于匿名数据收集的腾讯云日志服务上报 URL，形如 https://{region}.cls.tencentcs.com/track?topic_id={topic_id}
    Public Const ClsBaseUrl As String = ""

#Region "网络鉴权"

    Friend Function SecretCdnSign(UrlWithMark As String) As String
        If Not UrlWithMark.EndsWithF("{CDN}") Then Return UrlWithMark
        Return UrlWithMark.Replace("{CDN}", "").Replace(" ", "%20")
    End Function
    ''' <summary>
    ''' 设置 Headers 的 UA、Referer。
    ''' </summary>
    Friend Sub SecretHeadersSign(Url As String, ByRef Req As HttpRequestMessage, Optional SimulateBrowserHeaders As Boolean = False)
        If Not Req.Headers.UserAgent.Any Then
            If Url.Contains("baidupcs.com") OrElse Url.Contains("baidu.com") Then
                Req.Headers.Add("User-Agent", "LogStatistic")  '#4951
            ElseIf SimulateBrowserHeaders Then
                Req.Headers.Add("User-Agent", $"PCL2/{VersionBaseName}.{CInt(BuildType)} Mozilla/5.0 AppleWebKit/537.36 Chrome/63.0.3239.132 Safari/537.36")
            Else
                Req.Headers.Add("User-Agent", $"PCL2/{VersionBaseName}.{CInt(BuildType)}")
            End If
        End If
        If Not SimulateBrowserHeaders Then Req.Headers.Add("Referer", $"http://{VersionCode}.open.pcl2.server/")
        If Url.Contains("api.curseforge.com") OrElse Url.Contains("forgecdn.net") Then Req.Headers.Add("x-api-key", CurseForgeAPIKey)
    End Sub

#End Region

#Region "主题"

    'BRCL 工业终端：语义颜色兼容旧主题键。
    Public Color1 As New MyColor(241, 242, 233)
    Public Color2 As New MyColor(179, 186, 185)
    Public Color3 As New MyColor(242, 214, 73)
    Public Color4 As New MyColor(209, 184, 72)
    Public Color5 As New MyColor(238, 227, 163)
    Public Color6 As New MyColor(39, 44, 46)
    Public Color7 As New MyColor(28, 32, 34)
    Public Color8 As New MyColor(17, 20, 22)
    Public ColorBg0 As New MyColor(242, 214, 73)
    Public ColorAccent As New MyColor(242, 214, 73)
    Public ColorBg1 As New MyColor(190, Color7)
    Public ColorGray1 As New MyColor(64, 64, 64)
    Public ColorGray2 As New MyColor(115, 115, 115)
    Public ColorGray3 As New MyColor(140, 140, 140)
    Public ColorGray4 As New MyColor(146, 146, 146)
    Public ColorGray5 As New MyColor(204, 204, 204)
    Public ColorGray6 As New MyColor(235, 235, 235)
    Public ColorGray7 As New MyColor(240, 240, 240)
    Public ColorGray8 As New MyColor(245, 245, 245)
    Public ColorSemiTransparent As New MyColor(1, Color8)

    Public ThemeNow As Integer = -1
    Public ColorHue As Integer = 210, ColorSat As Integer = 85, ColorLightAdjust As Integer = 0, ColorHueTopbarDelta As OneOf(Of Integer, Integer()) = 0
    Public ThemeDontClick As Integer = 0

    Public Sub ThemeRefresh(Optional NewTheme As Integer = -1)
        Try
            If ThemeNow = NewTheme AndAlso NewTheme >= 0 Then Return
            If NewTheme >= 0 Then
                ThemeNow = NewTheme
                If ThemeNow = 12 Then RequestMainTick()
            End If

            'BRCL：浅色/深色双主题（1=浅色，2=深色）
            Dim Mode As Integer = 2
            Try
                Mode = Settings.Get(Of Integer)("UiThemeMode")
            Catch ex As Exception
            End Try
            Dim IsLight As Boolean = (Mode = 1)
            Logger.Info($"BRCL 主题模式：{Mode}（{If(IsLight, "浅色", "深色")}）")
            If IsLight Then
                Color1 = New MyColor(25, 29, 31)
                Color2 = New MyColor(85, 95, 96)
                Color3 = New MyColor(116, 89, 0) '浅色底的强调文字需足够深
                Color4 = New MyColor(151, 116, 28)
                Color5 = New MyColor(222, 207, 151)
                Color6 = New MyColor(232, 234, 225)
                Color7 = New MyColor(245, 245, 239)
                Color8 = New MyColor(231, 232, 227)
            Else
                Color1 = New MyColor(241, 242, 233)
                Color2 = New MyColor(179, 186, 185)
                Color3 = New MyColor(242, 214, 73)
                Color4 = New MyColor(209, 184, 72)
                Color5 = New MyColor(238, 227, 163)
                Color6 = New MyColor(39, 44, 46)
                Color7 = New MyColor(28, 32, 34)
                Color8 = New MyColor(17, 20, 22)
            End If
            Dim Resources = Application.Current.Resources
            Dim AnimatePalette = Application.Current.MainWindow IsNot Nothing AndAlso Application.Current.MainWindow.IsLoaded AndAlso UiMotionEnabled
            Resources("BrclIsLightTheme") = IsLight
            SetThemeBrush(Resources, "IndustrialAccent", Color.FromRgb(242, 214, 73), AnimatePalette)
            Resources("IndustrialOnAccent") = New SolidColorBrush(Color.FromRgb(25, 29, 31))
            Resources("IndustrialRailText") = New SolidColorBrush(Color.FromRgb(179, 186, 185))
            Resources("IndustrialRailActiveText") = New SolidColorBrush(Color.FromRgb(241, 242, 233))
            SetThemeBrush(Resources, "ColorBrushBackgroundTransparentSidebar", If(IsLight, Color.FromArgb(232, 239, 240, 233), Color.FromArgb(232, 24, 28, 30)), AnimatePalette)
            Resources("ColorBrushRail") = New SolidColorBrush(Color.FromRgb(20, 24, 26))
            Resources("ColorBrushRailLine") = New SolidColorBrush(Color.FromRgb(55, 63, 65))
            Resources("ColorBrushRailActive") = New SolidColorBrush(Color.FromRgb(46, 49, 40))
            SetThemeBrush(Resources, "ColorBrushCardLine", If(IsLight, Color.FromRgb(198, 203, 199), Color.FromRgb(55, 64, 65)), AnimatePalette)
            SetThemeBrush(Resources, "ColorBrushGray3", CType(Color2, Color), AnimatePalette)
            ColorBg0 = Color6
            ColorBg1 = New MyColor(190, Color7)

            ColorSemiTransparent = New MyColor(1, Color8)
            SetThemeBrush(Resources, "ColorBrush1", CType(Color1, Color), AnimatePalette)
            SetThemeBrush(Resources, "ColorBrush2", CType(Color2, Color), AnimatePalette)
            SetThemeBrush(Resources, "ColorBrush3", CType(Color3, Color), AnimatePalette)
            SetThemeBrush(Resources, "ColorBrush4", CType(Color4, Color), AnimatePalette)
            SetThemeBrush(Resources, "ColorBrush5", CType(Color5, Color), AnimatePalette)
            SetThemeBrush(Resources, "ColorBrush6", CType(Color6, Color), AnimatePalette)
            SetThemeBrush(Resources, "ColorBrush7", CType(Color7, Color), AnimatePalette)
            SetThemeBrush(Resources, "ColorBrush8", CType(Color8, Color), AnimatePalette)
            SetThemeBrush(Resources, "ColorBrushBg0", CType(ColorBg0, Color), AnimatePalette)
            SetThemeBrush(Resources, "ColorBrushBg1", CType(ColorBg1, Color), AnimatePalette)
            Application.Current.Resources("ColorObject1") = CType(Color1, Color)
            Application.Current.Resources("ColorObject2") = CType(Color2, Color)
            Application.Current.Resources("ColorObject3") = CType(Color3, Color)
            Application.Current.Resources("ColorObject4") = CType(Color4, Color)
            Application.Current.Resources("ColorObject5") = CType(Color5, Color)
            Application.Current.Resources("ColorObject6") = CType(Color6, Color)
            Application.Current.Resources("ColorObject7") = CType(Color7, Color)
            Application.Current.Resources("ColorObject8") = CType(Color8, Color)
            Application.Current.Resources("ColorObjectBg0") = CType(ColorBg0, Color)
            Application.Current.Resources("ColorObjectBg1") = CType(ColorBg1, Color)
            ThemeRefreshMain()
        Catch ex As Exception
            Logger.Error(ex, "刷新主题颜色失败", LogBehavior.Toast)
        End Try
    End Sub
    Private Sub SetThemeBrush(resources As ResourceDictionary, key As String, target As Color, animate As Boolean)
        Dim previous = TryCast(resources(key), SolidColorBrush)
        Dim replacement As New SolidColorBrush(target)
        If animate AndAlso previous IsNot Nothing AndAlso previous.Color <> target Then
            Dim transition As New Media.Animation.ColorAnimation(previous.Color, target, TimeSpan.FromMilliseconds(320)) With {
                .EasingFunction = New Media.Animation.CubicEase With {.EasingMode = Media.Animation.EasingMode.EaseInOut},
                .FillBehavior = Media.Animation.FillBehavior.Stop}
            AddHandler transition.Completed, Sub() replacement.BeginAnimation(SolidColorBrush.ColorProperty, Nothing)
            'Application resources may freeze a published brush. Attach its animation first.
            replacement.BeginAnimation(SolidColorBrush.ColorProperty, transition, Media.Animation.HandoffBehavior.SnapshotAndReplace)
        End If
        resources(key) = replacement
    End Sub

    Public Sub ThemeRefreshMain()
        RunInUi(
        Sub()
            If Not FrmMain.IsLoaded Then Return
            Dim IsLight As Boolean = (Settings.Get(Of Integer)("UiThemeMode") = 1)
            FrmMain.PanTitle.SetResourceReference(Panel.BackgroundProperty, "ColorBrush7")
            FrmMain.PanForm.SetResourceReference(Panel.BackgroundProperty, "ColorBrush8")
        End Sub)
    End Sub
    Friend Sub ThemeCheckAll(EffectSetup As Boolean)
    End Sub
    Friend Function ThemeCheckOne(Id As Integer) As Boolean
        Return True
    End Function
    Friend Function ThemeUnlock(Id As Integer, Optional ShowDoubleHint As Boolean = True, Optional UnlockHint As String = Nothing) As Boolean
        Return False
    End Function

#End Region

#Region "更新"

    Friend Sub UpdateCheckByButton()
        RunInNewThread(Sub() CheckBrclUpdate(True), "BRCL Update")
    End Sub

    Friend IsUpdateWaitingRestart As Boolean = False
    Public Sub UpdateRestart(TriggerRestartAndByEnd As Boolean)
    End Sub
    Public Sub UpdateReplace(ProcessId As Integer, OldFileName As String, NewFileName As String, TriggerRestart As Boolean)
    End Sub

    ''' <summary>
    ''' 确保 PathTemp/Latest.exe 是最新正式版的 BRCL，它会被用于整合包打包。
    ''' 如果不是，则下载一个。
    ''' </summary>
    Friend Sub DownloadLatestPCL(Optional LoaderToSyncProgress As LoaderBase = Nothing)
        '注意：如果要自行实现这个功能，请换用另一个文件路径，以免与官方版本冲突
    End Sub

#End Region

#Region "联网配置"

    ''' <summary>
    ''' 联网获取的配置信息。
    ''' 若获取失败或仍在获取中，可能为 Nothing。
    ''' </summary>
    Public ServerConfig As JObject

    Public ServerLoader As New LoaderTask(Of Integer, Integer)("BRCL 更新检查", Sub() CheckBrclUpdate(False), Priority:=ThreadPriority.BelowNormal) With
        {.ReloadTimeout = 1000 * 60 * 60} '超时 1 小时

#End Region

#Region "赞助等级"

    Public ReadOnly Property CurrentRank As DonationRank
        Get
            Return DonationRank.None
        End Get
    End Property

    Public Sub InputPotatoCode(IsUpdating As Boolean)
    End Sub
    Friend Sub GeneratePotatoCode()
    End Sub

    ''' <summary>
    ''' 获取设备识别码。
    ''' </summary>
    Friend Function GetIdentify() As String
        Return "0000-0000-0000-0000"
    End Function

#End Region

End Module
