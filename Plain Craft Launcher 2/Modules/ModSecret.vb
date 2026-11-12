'由于包含加解密等安全信息，本文件中的部分代码已被删除

Friend Module ModSecret

    '标注 PCL 的不同分支，仅用于替换标记
    Public Const VersionBranchMain As String = "OpenSource"
    '在开源版的注册表与常规版的注册表隔离，以防数据冲突
    Public Const RegFolder As String = "PCLDebug"
    '用于微软登录的 ClientId
    Public OAuthClientId As String = If(Environment.GetEnvironmentVariable("PCL_MS_CLIENT_ID"), "")
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

    'BRCL：BakaXL 式紫罗兰主色 + 终末地式深色骨架（深紫黑表面，冷调文字）
    Public Color1 As New MyColor(236, 235, 245)   '#ECEBF5 主文字
    Public Color2 As New MyColor(169, 164, 196)   '#A9A4C4 次级文字
    Public Color3 As New MyColor(139, 108, 255)   '#8B6CFF BakaXL 紫罗兰主色
    Public Color4 As New MyColor(179, 157, 255)   '#B39DFF 紫浅
    Public Color5 As New MyColor(211, 199, 255)   '#D3C7FF 紫更浅
    Public Color6 As New MyColor(34, 30, 51)      '#221E33 面板浅层
    Public Color7 As New MyColor(26, 23, 39)      '#1A1727 卡片底
    Public Color8 As New MyColor(19, 17, 32)      '#131120 页面底
    Public ColorBg0 As New MyColor(255, 210, 95)  '#FFD25F 状态黄
    Public ColorAccent As New MyColor(255, 210, 95)
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
            If NewTheme >= 0 Then ThemeNow = NewTheme

            'BRCL：浅色/深色双主题（1=浅色，2=深色）
            Dim Mode As Integer = 2
            Try
                Mode = Settings.Get(Of Integer)("UiThemeMode")
            Catch ex As Exception
            End Try
            Dim IsLight As Boolean = (Mode = 1)
            Logger.Info($"BRCL 主题模式：{Mode}（{If(IsLight, "浅色", "深色")}）")
            If IsLight Then
                Color1 = New MyColor(29, 27, 38)
                Color2 = New MyColor(107, 101, 127)
                Color3 = New MyColor(124, 92, 255)
                Color4 = New MyColor(167, 139, 250)
                Color5 = New MyColor(216, 203, 255)
                Color6 = New MyColor(255, 255, 255)
                Color7 = New MyColor(255, 255, 255)
                Color8 = New MyColor(244, 243, 249)
            Else
                Color1 = New MyColor(236, 235, 245)
                Color2 = New MyColor(169, 164, 196)
                Color3 = New MyColor(139, 108, 255)
                Color4 = New MyColor(179, 157, 255)
                Color5 = New MyColor(211, 199, 255)
                Color6 = New MyColor(34, 30, 51)
                Color7 = New MyColor(26, 23, 39)
                Color8 = New MyColor(19, 17, 32)
            End If
            '主题附属资源：侧栏、卡片描边、铭牌线
            '左侧面板底（启动页账号区）：浅色下不能沿用原来的深色半透明，否则输入框会变成黑底黑字
            Application.Current.Resources("ColorBrushBackgroundTransparentSidebar") = New SolidColorBrush(If(IsLight, Color.FromRgb(&HF7, &HF5, &HFC), Color.FromRgb(&H17, &H15, &H23)))
            Application.Current.Resources("ColorBrushRail") = New SolidColorBrush(If(IsLight, Color.FromRgb(&HF7, &HF5, &HFC), Color.FromRgb(&H15, &H12, &H1F)))
            Application.Current.Resources("ColorBrushRailLine") = New SolidColorBrush(If(IsLight, Color.FromRgb(&HE0, &HD8, &HEE), Color.FromRgb(&H2A, &H25, &H40)))
            Application.Current.Resources("ColorBrushRailActive") = New SolidColorBrush(If(IsLight, Color.FromRgb(&HE9, &HE3, &HFB), Color.FromRgb(&H23, &H1E, &H38)))
            Application.Current.Resources("ColorBrushCardLine") = New SolidColorBrush(Color.FromArgb(If(IsLight, CByte(20), CByte(26)), If(IsLight, CByte(0), CByte(255)), If(IsLight, CByte(0), CByte(255)), If(IsLight, CByte(0), CByte(255))))
            ColorBg0 = Color4 * 0.4 + Color5 * 0.4 + ColorGray4 * 0.2
            ColorBg1 = New MyColor(190, Color7)

            ColorSemiTransparent = New MyColor(1, Color8)
            Application.Current.Resources("ColorBrush1") = New SolidColorBrush(Color1)
            Application.Current.Resources("ColorBrush2") = New SolidColorBrush(Color2)
            Application.Current.Resources("ColorBrush3") = New SolidColorBrush(Color3)
            Application.Current.Resources("ColorBrush4") = New SolidColorBrush(Color4)
            Application.Current.Resources("ColorBrush5") = New SolidColorBrush(Color5)
            Application.Current.Resources("ColorBrush6") = New SolidColorBrush(Color6)
            Application.Current.Resources("ColorBrush7") = New SolidColorBrush(Color7)
            Application.Current.Resources("ColorBrush8") = New SolidColorBrush(Color8)
            Application.Current.Resources("ColorBrushBg0") = New SolidColorBrush(ColorBg0)
            Application.Current.Resources("ColorBrushBg1") = New SolidColorBrush(ColorBg1)
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
    Public Sub ThemeRefreshMain()
        RunInUi(
        Sub()
            If Not FrmMain.IsLoaded Then Return
            '顶部条背景
            'BRCL：顶栏改为近乎纯黑的机能条，只用一条极细的品牌色下边线
            Dim Brush = New LinearGradientBrush With {.EndPoint = New Point(1, 0), .StartPoint = New Point(0, 0)}
            Dim LightBar As Boolean = (Color1.R = 29 AndAlso Color1.G = 27)
            If LightBar Then
                Brush.GradientStops.Add(New GradientStop With {.Offset = 0, .Color = New MyColor(250, 249, 254)})
                Brush.GradientStops.Add(New GradientStop With {.Offset = 0.6, .Color = New MyColor(255, 255, 255)})
                Brush.GradientStops.Add(New GradientStop With {.Offset = 1, .Color = New MyColor(250, 249, 254)})
            Else
                Brush.GradientStops.Add(New GradientStop With {.Offset = 0, .Color = New MyColor(22, 19, 36)})
                Brush.GradientStops.Add(New GradientStop With {.Offset = 0.6, .Color = New MyColor(28, 24, 45)})
                Brush.GradientStops.Add(New GradientStop With {.Offset = 1, .Color = New MyColor(22, 19, 36)})
            End If
            FrmMain.PanTitle.Background = Brush
            FrmMain.PanTitle.Background.Freeze()
            '主页面背景
            'BRCL：页面底色为深炭黑，带极轻的冷光过渡
            Brush = New LinearGradientBrush With {.EndPoint = New Point(0.15, 1), .StartPoint = New Point(0.85, 0)}
            If Color1.R = 29 AndAlso Color1.G = 27 Then
                Brush.GradientStops.Add(New GradientStop With {.Offset = -0.1, .Color = New MyColor(244, 243, 251)})
                Brush.GradientStops.Add(New GradientStop With {.Offset = 0.45, .Color = New MyColor(238, 236, 246)})
                Brush.GradientStops.Add(New GradientStop With {.Offset = 1.1, .Color = New MyColor(243, 241, 251)})
            Else
                Brush.GradientStops.Add(New GradientStop With {.Offset = -0.1, .Color = New MyColor(24, 21, 39)})
                Brush.GradientStops.Add(New GradientStop With {.Offset = 0.45, .Color = New MyColor(19, 17, 32)})
                Brush.GradientStops.Add(New GradientStop With {.Offset = 1.1, .Color = New MyColor(27, 23, 45)})
            End If
            FrmMain.PanForm.Background = Brush
            FrmMain.PanForm.Background.Freeze()
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
        Hint("该版本中不包含更新功能……")
    End Sub

    Friend IsUpdateWaitingRestart As Boolean = False
    Public Sub UpdateRestart(TriggerRestartAndByEnd As Boolean)
    End Sub
    Public Sub UpdateReplace(ProcessId As Integer, OldFileName As String, NewFileName As String, TriggerRestart As Boolean)
    End Sub

    ''' <summary>
    ''' 确保 PathTemp/Latest.exe 是最新正式版的 PCL，它会被用于整合包打包。
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

    Public ServerLoader As New LoaderTask(Of Integer, Integer)("PCL 配置更新", Sub() Logger.Info("该版本中不包含更新通知功能……"), Priority:=ThreadPriority.BelowNormal) With
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
