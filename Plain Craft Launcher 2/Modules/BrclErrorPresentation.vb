Imports System.IO
Imports System.Net
Imports System.Net.Http
Imports System.Text.RegularExpressions

'Keep recovery copy separate from technical details and from the caller's actions.
Public Class BrclErrorContent
    Public Property Title As String
    Public Property Summary As String
    Public Property Advice As String
    Public Property Details As String
    Public Property Code As String
End Class

Public Module BrclErrorPresentation
    Public Function IsErrorTitle(Title As String) As Boolean
        Return Regex.IsMatch(If(Title, ""), "失败|错误|异常|不可用")
    End Function

    Public Function BuildBrclError(Operation As String, Optional Ex As Exception = Nothing,
                                   Optional RawDetails As String = Nothing, Optional Fatal As Boolean = False) As BrclErrorContent
        Dim Context = If(Operation, "")
        Dim Detail = If(RawDetails, Context & If(Ex Is Nothing, "", vbCrLf & Ex.GetDisplay(True)))
        Dim Evidence = Context & vbCrLf & Detail
        Dim Errors As New List(Of Exception)
        Dim Current = Ex
        While Current IsNot Nothing AndAlso Errors.Count < 16
            Errors.Add(Current)
            Current = Current.InnerException
        End While
        Dim Result As New BrclErrorContent With {
            .Title = If(IsErrorTitle(Context) AndAlso Context <> "错误" AndAlso Context.Length <= 40 AndAlso Not Context.Contains(vbLf), SanitizeBrclDetails(Context.Replace("BRCL ", "").Trim()), "操作未完成"),
            .Summary = "BRCL 未能完成本次操作。",
            .Advice = "请先重试。若问题仍然出现，请复制技术详情，并联系 BRS 维护者协助排查。",
            .Code = "BRCL-APP-001",
            .Details = SanitizeBrclDetails(Detail)
        }
        If Errors.Any(Function(E) TypeOf E Is UnauthorizedAccessException) OrElse Regex.IsMatch(Evidence, "UnauthorizedAccessException|拒绝访问|没有.*权限") Then
            Result.Title = "无法访问所需文件"
            Result.Summary = "BRCL 没有完成文件读写操作。"
            Result.Advice = "请确认游戏目录可写、文件未被其他程序占用，再重试。必要时将启动器移到有写入权限的文件夹。"
            Result.Code = "BRCL-FILE-002"
        ElseIf Errors.Any(Function(E) TypeOf E Is IOException AndAlso (E.HResult And &HFFFF) = 112) OrElse Evidence.Contains("磁盘空间不足") Then
            Result.Title = "磁盘空间不足"
            Result.Summary = "BRCL 无法写入本次操作需要的文件。"
            Result.Advice = "请检查客户端与临时目录所在磁盘的可用空间，释放空间后再试。"
            Result.Code = "BRCL-FILE-004"
        ElseIf Regex.IsMatch(Evidence, "正由另一进程使用|正被另一个进程使用|sharing violation", RegexOptions.IgnoreCase) Then
            Result.Title = "文件正在使用"
            Result.Summary = "BRCL 需要的文件暂时被其他程序占用。"
            Result.Advice = "请关闭正在使用该文件的游戏或工具，等待片刻后重试。"
            Result.Code = "BRCL-FILE-003"
        ElseIf Errors.Any(Function(E) TypeOf E Is TimeoutException OrElse (TypeOf E Is WebException AndAlso CType(E, WebException).Status = WebExceptionStatus.Timeout)) OrElse Regex.IsMatch(Evidence, "TimeoutException|超时|timed out", RegexOptions.IgnoreCase) Then
            Result.Title = "连接超时"
            Result.Summary = "BRCL 未能在规定时间内收到响应。"
            Result.Advice = "请检查网络连接，稍后重试。服务端繁忙时，可以等待片刻后再次操作。"
            Result.Code = "BRCL-NET-002"
        ElseIf Errors.Any(Function(E) TypeOf E Is OperationCanceledException) Then
            Result.Title = "请求未完成"
            Result.Summary = "本次请求的等待已结束，或操作已被取消。"
            Result.Advice = "如果是主动取消，无需处理。需要继续时，请重新发起操作。"
            Result.Code = "BRCL-REQ-001"
        ElseIf Errors.Any(Function(E) TypeOf E Is WebException OrElse TypeOf E Is HttpRequestException) OrElse Regex.IsMatch(Evidence, "WebException|HttpRequestException|ConnectionClosed|连接.*关闭|无法连接|网络.*失败") Then
            Result.Title = "暂时无法连接服务"
            Result.Summary = "BRCL 未能与所需服务建立正常连接。"
            Result.Advice = "请检查网络后重试。若其他网页可以正常打开，请稍后再试，或查看 BRS 服务公告。"
            Result.Code = "BRCL-NET-001"
        ElseIf Regex.IsMatch(Evidence, "SHA-?256|哈希|校验失败|校验不一致", RegexOptions.IgnoreCase) Then
            Result.Title = "文件校验未通过"
            Result.Summary = "下载的文件与预期内容不一致，本次操作未继续。"
            Result.Advice = "请重新下载后再试。若仍然无法通过校验，请联系 BRS 维护者检查文件。"
            Result.Code = "BRCL-DATA-001"
        ElseIf Regex.IsMatch(Context, "登录|授权|认证") Then
            Result.Title = "登录未完成"
            Result.Summary = "BRCL 尚未取得有效的账号授权。"
            Result.Advice = "请重新发起登录，并在账号页面完成授权。若已取消授权，可以关闭此提示后重新选择账号。"
            Result.Code = "BRCL-AUTH-001"
        ElseIf Errors.Any(Function(E) TypeOf E Is FileNotFoundException OrElse TypeOf E Is DirectoryNotFoundException) OrElse Regex.IsMatch(Evidence, "FileNotFoundException|DirectoryNotFoundException|找不到.*文件|未能找到文件") Then
            Result.Title = "缺少所需文件"
            Result.Summary = "BRCL 未找到本次操作需要的文件或文件夹。"
            Result.Advice = "请检查客户端目录与文件是否完整，然后重新选择目录或重新安装对应组件。"
            Result.Code = "BRCL-FILE-001"
        ElseIf Regex.IsMatch(Context, "Java|运行环境|Framework", RegexOptions.IgnoreCase) Then
            Result.Title = "运行环境需要检查"
            Result.Summary = "BRCL 无法使用当前选择的运行环境。"
            Result.Advice = "请在启动设置中检查 Java 路径与版本。启动器本身无法运行时，请检查系统所需的 .NET Framework 组件。"
            Result.Code = "BRCL-ENV-001"
        End If
        If Context.Contains("更新") Then
            Result.Title = "更新未完成"
            Result.Summary = "BRCL 未能完成此次更新操作。"
            Result.Advice &= vbCrLf & "你可以稍后再次检查更新。"
        ElseIf Context.Contains("整合包") AndAlso Result.Code = "BRCL-APP-001" Then
            Result.Title = "整合包安装未完成"
            Result.Summary = "BRS 客户端尚未安装完成。"
            Result.Advice = "请检查网络连接与磁盘可用空间后重试。再次失败时，请复制技术详情联系 BRS 维护者。"
        End If
        If Fatal Then
            Result.Title = "BRCL 无法继续运行"
            Result.Summary = "启动器遇到了无法恢复的问题，需要关闭。"
            Result.Advice = "请先复制技术详情，关闭后重新启动 BRCL。若问题再次出现，请将详情与日志提供给 BRS 维护者。"
            Result.Code = "BRCL-APP-999"
        End If
        Return Result
    End Function

    Public Function SanitizeBrclDetails(Value As String) As String
        Dim Clean = If(Value, "")
        'This is also used before the main window and account settings are ready.
        Try
            Clean = FilterUserName(FilterAccessToken(Clean, "*"c), "*"c)
        Catch
        End Try
        Return RedactBrclCredentials(Clean, "[已隐藏]")
    End Function

    Friend Function RedactBrclCredentials(Value As String, Replacement As String) As String
        'Consume the entire quoted value, including spaces and escaped quotes.
        'Stopping at whitespace can otherwise expose the rest of a password.
        Dim QuotedValue = "(?:""(?:\\.|[^""\\\r\n])*""|'(?:\\.|[^'\\\r\n])*')"
        Dim BareValue = "[^\s""'&,;}]+"
        Dim SecretValue = "(?:" & QuotedValue & "|" & BareValue & ")"
        Dim Clean = If(Value, "")
        Clean = Regex.Replace(Clean, "(?i)([""']?authorization[""']?\s*[:=]\s*)(?:" & QuotedValue & "|(?:Bearer|Basic)\s+" & BareValue & "|" & BareValue & ")", "$1" & Replacement)
        Clean = Regex.Replace(Clean, "(?i)((?<![\w-])[""']?(?:access[_-]?token|refresh[_-]?token|client[_-]?token|id[_-]?token|client[_-]?secret|api[_-]?key|password|passwd)[""']?\s*[:=]\s*)" & SecretValue, "$1" & Replacement)
        Clean = Regex.Replace(Clean, "(?i)(--(?:access[_-]?token|refresh[_-]?token|client[_-]?token|id[_-]?token|client[_-]?secret|api[_-]?key|password|passwd)(?:\s+|=))" & SecretValue, "$1" & Replacement)
        Return Regex.Replace(Clean, "(?i)(Bearer\s+)[A-Za-z0-9._~+/=-]+", "$1" & Replacement)
    End Function

    Public Sub ShowBrclError(Operation As String, Optional Ex As Exception = Nothing,
                             Optional RawDetails As String = Nothing, Optional Fatal As Boolean = False)
        If Fatal Then QueueLauncherCrash(Operation & vbCrLf & If(RawDetails, "") & vbCrLf & If(Ex?.ToString(), ""))
        Dim Content = BuildBrclError(Operation, Ex, RawDetails, Fatal)
        If Not Fatal AndAlso FrmMain?.PanMsg IsNot Nothing AndAlso FrmMain.IsLoaded Then
            MyMsgBox(Content.Summary, Content.Title, "知道了", IsWarn:=False,
                     ErrorDetails:=Content.Details, ErrorCode:=Content.Code, RecoveryAdvice:=Content.Advice)
        Else
            Try
                RunInUiWait(Sub() BrclErrorWindow.ShowError(Content, Fatal))
            Catch
                'If WPF itself is damaged, retain a last-resort readable error message.
                MessageBox.Show(Content.Summary & vbCrLf & vbCrLf & Content.Advice & vbCrLf & vbCrLf & Content.Code & vbCrLf & Content.Details,
                                "BRCL · " & Content.Title, MessageBoxButton.OK, MessageBoxImage.Error)
            End Try
        End If
    End Sub
End Module
