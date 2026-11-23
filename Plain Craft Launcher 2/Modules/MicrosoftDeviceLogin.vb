'设备代码协议的状态与校验不依赖窗口、账号存储或网络，便于离线验证。
Public NotInheritable Class MicrosoftDeviceLogin
    Public ReadOnly Property UserCode As String
    Public ReadOnly Property DeviceCode As String
    Public ReadOnly Property Website As String
    Public ReadOnly Property ExpiresSeconds As Integer
    Private CurrentPollSeconds As Double
    Public ReadOnly Property PollSeconds As Double
        Get
            Return CurrentPollSeconds
        End Get
    End Property
    Private ConsecutiveFailures As Integer
    Public ReadOnly Property FailureCount As Integer
        Get
            Return ConsecutiveFailures
        End Get
    End Property
    Private ProtocolInterval As Double

    Public Sub New(Data As JObject)
        UserCode = RequiredString(Data, "user_code")
        DeviceCode = RequiredString(Data, "device_code")
        Dim Target As Uri = Nothing
        If Not Uri.TryCreate(RequiredString(Data, "verification_uri"), UriKind.Absolute, Target) OrElse
           Target.Scheme <> Uri.UriSchemeHttps OrElse Not String.IsNullOrEmpty(Target.UserInfo) Then
            Throw New FormatException("微软返回的登录网址无效，请重新登录。")
        End If
        Website = Target.AbsoluteUri
        ExpiresSeconds = ReadSeconds(Data, "expires_in", 900)
        ProtocolInterval = ReadSeconds(Data, "interval", 5)
        CurrentPollSeconds = ProtocolInterval
    End Sub

    Private Shared Function RequiredString(Data As JObject, Name As String) As String
        Dim Value = Data?(Name)
        If Value Is Nothing OrElse Value.Type <> JTokenType.String OrElse String.IsNullOrWhiteSpace(Value.Value(Of String)()) Then
            '只记录字段名称，绝不回显可能含有令牌的响应正文。
            Throw New FormatException("微软登录响应缺少有效字段：" & Name)
        End If
        Return Value.Value(Of String)()
    End Function

    Private Shared Function ReadSeconds(Data As JObject, Name As String, DefaultValue As Integer) As Integer
        Dim Value = Data(Name)
        If Value Is Nothing Then Return DefaultValue
        Dim Seconds As Long
        If Value.Type <> JTokenType.Integer OrElse Not Long.TryParse(Value.ToString(), Seconds) OrElse Seconds <= 0 OrElse Seconds > Integer.MaxValue Then
            Throw New FormatException("微软登录响应中的时间无效：" & Name)
        End If
        Return CInt(Seconds)
    End Function

    Public Function RemainingSeconds(ElapsedSeconds As Double) As Integer
        Return CInt(Math.Ceiling(Math.Max(0, ExpiresSeconds - ElapsedSeconds)))
    End Function

    Public Sub AuthorizationPending()
        ConsecutiveFailures = 0
        CurrentPollSeconds = ProtocolInterval
    End Sub

    Public Sub SlowDown()
        'RFC 8628：slow_down 对本次及后续轮询永久增加 5 秒。
        ProtocolInterval += 5
        AuthorizationPending()
    End Sub

    Public Function TransientFailure() As Boolean
        ConsecutiveFailures += 1
        If FailureCount > 3 Then Return False
        CurrentPollSeconds = Math.Max(ProtocolInterval, Math.Min(60, PollSeconds * 2))
        Return True
    End Function

    Public Shared Function ErrorCode(Response As String) As String
        Try
            Dim Value = JObject.Parse(Response)("error")
            If Value?.Type = JTokenType.String Then Return Value.Value(Of String)()
        Catch
            '网关可能返回 HTML 或空正文，由调用方按 HTTP 状态分类。
        End Try
        Return ""
    End Function

    Friend Shared Function RequiresNewAuthorization(Response As String) As Boolean
        Dim Code = ErrorCode(Response)
        If Code <> "" Then Return Code = "invalid_grant" OrElse Code = "interaction_required"
        Dim Text = If(Response, "")
        Return Text.ContainsIgnoreCase("must sign in again") OrElse Text.ContainsIgnoreCase("password expired") OrElse
            (Text.Contains("refresh_token") AndAlso Text.ContainsIgnoreCase("is not valid"))
    End Function

    Public Shared Function ReadTokens(Data As JObject, Optional PreviousRefreshToken As String = Nothing) As String()
        Dim Access = RequiredString(Data, "access_token")
        Dim Refresh = Data?("refresh_token")
        If Refresh Is Nothing AndAlso Not String.IsNullOrWhiteSpace(PreviousRefreshToken) Then Return {Access, PreviousRefreshToken}
        Return {Access, RequiredString(Data, "refresh_token")}
    End Function
End Class
