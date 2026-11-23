Imports System.Globalization
Imports System.Net.Http
Imports System.Numerics
Imports System.Text
Imports System.Text.RegularExpressions
Imports System.Threading
Imports System.Threading.Tasks
Imports Newtonsoft.Json.Linq

''' <summary>Player-scoped bank data. The TRM administrator credential never belongs in this process.</summary>
Public Module BrclBank
    Public Class BankAccount
        Public Id As String
        Public Label As String
        Public Balance As BigInteger
        Public VisibleBalance As BigInteger
        Public ReserveBalance As BigInteger
        Public Grants As New List(Of String)
        Public Overrides Function ToString() As String
            Return If(String.IsNullOrWhiteSpace(Label), "未命名团队银行", Label) & " · " & Id.Substring(Id.Length - 8)
        End Function
    End Class

    Public Class BankPoint
        Public CapturedAt As Long
        Public Balance As BigInteger
    End Class

    Public Class BankAccounts
        Public CapturedAt As Long
        Public Accounts As New List(Of BankAccount)
    End Class

    Public Class BankServiceException
        Inherits Exception
        Public ReadOnly Code As String
        Public Sub New(Value As String, Optional Detail As String = Nothing)
            MyBase.New("Bank service: " & Value & If(String.IsNullOrEmpty(Detail), "", " (" & Detail & ")"))
            Code = Value
        End Sub
    End Class

    Public Const OfficialBankHost As String = "zhushu.homes"

    ''' <summary>
    ''' The bank service receives the player's Minecraft access token. An override written next to the
    ''' launcher (for example inside a shared modpack folder) must therefore never redirect it to a
    ''' third-party host: only the official HTTPS host or a service on this computer is accepted.
    ''' </summary>
    Public Function IsTrustedBankAddress(Target As Uri) As Boolean
        If Target Is Nothing Then Return False
        If Target.IsLoopback Then Return Target.Scheme = Uri.UriSchemeHttps OrElse Target.Scheme = Uri.UriSchemeHttp
        Return Target.Scheme = Uri.UriSchemeHttps AndAlso Target.IsDefaultPort AndAlso
            String.Equals(Target.IdnHost, OfficialBankHost, StringComparison.OrdinalIgnoreCase)
    End Function

    Public Function BankServiceAddress() As Uri
        Dim Value = Environment.GetEnvironmentVariable("BRCL_BANK_SERVICE_URL")
        Dim Source = "BRCL_BANK_SERVICE_URL"
        If String.IsNullOrWhiteSpace(Value) Then
            Dim Config = IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "BRCL", "BankServiceUrl.txt")
            If IO.File.Exists(Config) Then
                If New IO.FileInfo(Config).Length > 2048 Then Throw New BankServiceException("invalid_service_address")
                Value = IO.File.ReadAllText(Config, Encoding.UTF8).Trim()
                Source = "BRCL\BankServiceUrl.txt"
            Else
                Value = "https://zhushu.homes/bank/v1"
            End If
        End If
        If String.IsNullOrWhiteSpace(Value) Then Return Nothing
        Dim Target = ValidateBankAddress(Value)
        If Not IsTrustedBankAddress(Target) Then
            'The caller logs the code; the host is enough to diagnose a misconfiguration.
            Throw New BankServiceException("untrusted_service_address", $"{Source} → {Target.Host}")
        End If
        Return Target
    End Function

    Public Function ValidateBankAddress(Value As String) As Uri
        Dim Target As Uri = Nothing
        If Not Uri.TryCreate(Value.Trim().TrimEnd("/"c) & "/", UriKind.Absolute, Target) OrElse
            (Target.Scheme <> Uri.UriSchemeHttps AndAlso Not (Target.Scheme = Uri.UriSchemeHttp AndAlso Target.IsLoopback)) OrElse
            Target.UserInfo.Length > 0 OrElse Target.Query.Length > 0 OrElse Target.Fragment.Length > 0 Then
            Throw New BankServiceException("invalid_service_address")
        End If
        Return Target
    End Function

    Public Function BankUuid(Value As String) As String
        Dim Parsed As Guid
        If Not Guid.TryParseExact(Value, "D", Parsed) AndAlso Not Guid.TryParseExact(Value, "N", Parsed) Then Throw New FormatException("Invalid bank UUID")
        Return Parsed.ToString("D")
    End Function

    Public Function BankMoney(Token As JToken) As BigInteger
        If Token Is Nothing OrElse Token.Type <> JTokenType.String OrElse
            Not Regex.IsMatch(Token.ToString(), "\A(?:0|[1-9][0-9]{0,199})\z") Then Throw New FormatException("Invalid bank amount")
        Return BigInteger.Parse(Token.ToString(), CultureInfo.InvariantCulture)
    End Function

    Public Function BankTime(Token As JToken) As Long
        Dim Value As Long
        If Token Is Nothing OrElse Token.Type <> JTokenType.Integer OrElse Not Long.TryParse(Token.ToString(), Value) OrElse
            Value < 0 OrElse Value > 253402214400000L Then Throw New FormatException("Invalid bank timestamp")
        Return Value
    End Function

    Public Function ParseBankAccounts(Json As JObject, PlayerId As String) As BankAccounts
        If CStr(Json("currency")) <> "sdmcoin" OrElse CStr(Json("unit")) <> "copper" Then Throw New FormatException("Unexpected bank currency")
        Dim Items = TryCast(Json("accounts"), JArray)
        If Items Is Nothing Then Throw New FormatException("Missing bank accounts")
        Dim Result As New BankAccounts With {.CapturedAt = BankTime(Json("capturedAt"))}
        Dim Seen As New HashSet(Of String)(StringComparer.Ordinal)
        For Each Item As JObject In Items
            If CStr(Item("type")) <> "team" OrElse CStr(Item("nativeType")) <> "BLAZE_BANKER" Then Throw New FormatException("Unsupported bank account")
            Dim Account As New BankAccount With {
                .Id = BankUuid(CStr(Item("id"))), .Label = If(CStr(Item("label")), ""),
                .Balance = BankMoney(Item("balance")), .VisibleBalance = BankMoney(Item("visibleBalance")), .ReserveBalance = BankMoney(Item("reserveBalance"))}
            If Account.Label.Length > 200 OrElse Account.Balance <> Account.VisibleBalance + Account.ReserveBalance OrElse Not Seen.Add(Account.Id) Then Throw New FormatException("Inconsistent bank account")
            Dim Grants = TryCast(Item("grants"), JArray)
            If Grants Is Nothing Then Throw New FormatException("Missing direct grants")
            For Each Grant As JObject In Grants
                If CStr(Grant("role")) <> "trusted" Then Throw New FormatException("Unknown bank role")
                Account.Grants.Add(BankUuid(CStr(Grant("principalId"))))
            Next
            If Not Account.Grants.Contains(BankUuid(PlayerId)) Then Throw New FormatException("Account belongs to another player")
            Result.Accounts.Add(Account)
        Next
        Return Result
    End Function

    Public Function ParseBankHistory(Json As JObject, AccountId As String) As List(Of BankPoint)
        If BankUuid(CStr(Json("accountId"))) <> BankUuid(AccountId) OrElse CStr(Json("currency")) <> "sdmcoin" OrElse CStr(Json("unit")) <> "copper" OrElse CStr(Json("source")) <> "observed_snapshots" Then Throw New FormatException("Unexpected bank history")
        Dim Items = TryCast(Json("points"), JArray)
        If Items Is Nothing OrElse Items.Count > 1000 Then Throw New FormatException("Invalid bank history")
        Dim Result As New List(Of BankPoint)
        Dim Previous As Long = -1
        For Each Item As JObject In Items
            Dim Point As New BankPoint With {.CapturedAt = BankTime(Item("capturedAt")), .Balance = BankMoney(Item("balance"))}
            If Point.CapturedAt <= Previous Then Throw New FormatException("Unordered bank history")
            Previous = Point.CapturedAt
            Result.Add(Point)
        Next
        Return Result
    End Function

    Public Class BankClient
        Implements IDisposable
        Private ReadOnly Http As HttpClient
        Private ReadOnly BaseAddress As Uri
        Private Session As String = ""
        Private ExpiresAt As DateTimeOffset = DateTimeOffset.MinValue
        Public Sub New(Address As Uri)
            BaseAddress = ValidateBankAddress(Address.AbsoluteUri)
            Http = New HttpClient(New HttpClientHandler With {.AllowAutoRedirect = False}) With {.Timeout = TimeSpan.FromSeconds(15), .MaxResponseContentBufferSize = 2097152}
            Http.DefaultRequestHeaders.UserAgent.ParseAdd("BRCL/1.0")
            Http.DefaultRequestHeaders.Accept.ParseAdd("application/json")
        End Sub

        Private Async Function Request(Method As HttpMethod, Route As String, Credential As String, Cancel As CancellationToken) As Task(Of JObject)
            Using Message As New HttpRequestMessage(Method, New Uri(BaseAddress, Route))
                Message.Headers.Authorization = New Headers.AuthenticationHeaderValue("Bearer", Credential)
                Message.Headers.CacheControl = New Headers.CacheControlHeaderValue With {.NoCache = True, .NoStore = True}
                If Method = HttpMethod.Post Then Message.Content = New StringContent("{}", Encoding.UTF8, "application/json")
                Using Response = Await Http.SendAsync(Message, Cancel).ConfigureAwait(False)
                    If Not Response.IsSuccessStatusCode Then
                        'Never log bodies, credentials, or redirect locations from an authentication response.
                        Select Case CInt(Response.StatusCode)
                            Case 401 : Session = "" : Throw New BankServiceException("unauthorized")
                            Case 403, 404 : Throw New BankServiceException("account_unavailable")
                            Case 429 : Throw New BankServiceException("rate_limited")
                            Case Else : Throw New BankServiceException("service_unavailable")
                        End Select
                    End If
                    Dim Body = Await Response.Content.ReadAsStringAsync().ConfigureAwait(False)
                    Cancel.ThrowIfCancellationRequested()
                    Return JObject.Parse(Body)
                End Using
            End Using
        End Function

        Public Async Function GetAccounts(MinecraftToken As String, PlayerId As String, Cancel As CancellationToken) As Task(Of BankAccounts)
            If Session.Length = 0 OrElse ExpiresAt <= DateTimeOffset.UtcNow.AddSeconds(30) Then
                Dim Auth = Await Request(HttpMethod.Post, "session", MinecraftToken, Cancel).ConfigureAwait(False)
                If BankUuid(CStr(Auth("player")("uuid"))) <> BankUuid(PlayerId) Then Throw New BankServiceException("identity_mismatch")
                Dim Credential = CStr(Auth("token"))
                If Credential Is Nothing OrElse Not Regex.IsMatch(Credential, "\A[A-Za-z0-9_-]{32,256}\z") Then Throw New FormatException("Invalid bank session")
                Dim Expiry = DateTimeOffset.FromUnixTimeMilliseconds(BankTime(Auth("expiresAt")))
                If Expiry <= DateTimeOffset.UtcNow Then Throw New BankServiceException("unauthorized")
                Session = Credential
                ExpiresAt = Expiry
            End If
            Return ParseBankAccounts(Await Request(HttpMethod.Get, "accounts", Session, Cancel).ConfigureAwait(False), PlayerId)
        End Function

        Public Async Function GetHistory(AccountId As String, Days As Integer, Cancel As CancellationToken) As Task(Of List(Of BankPoint))
            Return ParseBankHistory(Await Request(HttpMethod.Get, "accounts/" & BankUuid(AccountId) & "/history?days=" & Days.ToString(CultureInfo.InvariantCulture), Session, Cancel).ConfigureAwait(False), AccountId)
        End Function

        Public Sub Dispose() Implements IDisposable.Dispose
            Session = ""
            Http.Dispose()
        End Sub
    End Class
End Module
