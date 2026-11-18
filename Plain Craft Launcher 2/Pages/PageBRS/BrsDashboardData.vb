Imports System.Globalization
Imports Newtonsoft.Json.Linq

''' <summary>信息页补充数据。每个可选字段独立校验，异常值只影响对应指标。</summary>
Public Class BrsDashboardData
    Private Const Missing As String = "—"
    Private Shared ReadOnly Culture As CultureInfo = CultureInfo.InvariantCulture

    Public Shared Function Build(Json As JObject) As Dictionary(Of String, String)
        Dim Result As New Dictionary(Of String, String) From {
            {"Uptime", Missing}, {"Memory", Missing}, {"Entities", Missing}, {"WorldSize", Missing},
            {"OverworldMspt", Missing}, {"Mspt", Missing}, {"EstimatedTps", Missing},
            {"TodayActive", Missing}, {"TodayPeak", Missing}, {"SeasonStart", Missing},
            {"SeasonDay", Missing}, {"SeasonLabel", "当前周目"}, {"AchievementPercent", "0"}, {"Alive", "false"},
            {"SeasonPlayers", Missing}, {"SeasonHours", Missing}, {"WeekAchievements", Missing},
            {"WeeklyTopName", Missing}, {"WeeklyTopHours", Missing},
            {"LoadHint", "等待服务器性能数据"}, {"DataHint", "服务器状态与游玩统计分别刷新"}
        }
        If Json Is Nothing Then Return Result
        Dim Status = ObjectField(Json, "status")
        Dim Alive = BooleanField(Status, "alive")
        Result("Alive") = If(Alive.HasValue AndAlso Alive.Value, "true", "false")
        If Alive.HasValue AndAlso Alive.Value Then
            Dim Uptime = NumberField(Status, "uptime_sec", True)
            If Uptime.HasValue Then Result("Uptime") = FormatUptime(Uptime.Value)
            Result("Memory") = FormatNumber(NumberField(Status, "rss_kb"), "0.0", " GiB", 1024 * 1024)
            Result("Entities") = FormatNumber(NumberField(Status, "entities", True), "#,0")
            Dim Mspt = NumberField(Status, "tps_overall_ms")
            Result("Mspt") = FormatNumber(Mspt, "0.0", " ms")
            Result("OverworldMspt") = FormatNumber(NumberField(Status, "tps_overworld_ms"), "0.0", " ms")
            If Mspt.HasValue Then
                Result("EstimatedTps") = If(Mspt.Value = 0, 20, Math.Min(20, 1000 / Mspt.Value)).ToString("0.0", Culture)
                Result("LoadHint") = If(Mspt.Value <= 50,
                    "每 tick 耗时未超过 50 ms，处于 20 TPS 的参考范围。",
                    "每 tick 耗时超过 50 ms，服务端负载较高，可能影响方块交互和生物响应。")
            End If
        ElseIf Alive.HasValue Then
            Result("LoadHint") = "服务器离线，性能数据暂不可用。"
        End If
        '世界存档大小是持久数据；离线时仍可展示源站最后一次统计。
        Result("WorldSize") = FormatNumber(NumberField(Status, "world_size_kb"), "0.0", " GiB", 1024 * 1024)

        Dim Today = DateField(Json, "today")
        If Today.HasValue Then
            Dim Key = Today.Value.ToString("yyyy-MM-dd", Culture)
            Dim Active = NumberField(ObjectField(Json, "history"), Key, True)
            If Not Active.HasValue Then Active = NumberField(ObjectField(Json, "daily"), Key, True)
            Result("TodayActive") = FormatNumber(Active, "#,0")
            Dim Activity = ObjectField(Json, "activity")
            Dim Peak = NumberField(ObjectField(Activity, "peak_month"), Key, True)
            If Not Peak.HasValue Then Peak = NumberField(ObjectField(Activity, "peak_week"), Key, True)
            Result("TodayPeak") = FormatNumber(Peak, "#,0")
        End If

        Dim Start = DateField(ObjectField(Json, "season"), "start")
        If Start.HasValue Then Result("SeasonStart") = Start.Value.ToString("yyyy-MM-dd", Culture)
        Dim Day = NumberField(ObjectField(Json, "season"), "day", True)
        If Day.HasValue Then Result("SeasonDay") = "第 " & Day.Value.ToString("0", Culture) & " 天"
        Dim SeasonLabel = TextField(ObjectField(Json, "server"), "season_label")
        If SeasonLabel <> Missing Then Result("SeasonLabel") = SeasonLabel
        Dim Achievements = ObjectField(Json, "achievements")
        Dim Unlocked = NumberField(Achievements, "unlocked", True), Total = NumberField(Achievements, "total_achievements", True)
        If Unlocked.HasValue AndAlso Total.HasValue AndAlso Total.Value > 0 AndAlso Unlocked.Value <= Total.Value Then
            Result("AchievementPercent") = (Unlocked.Value / Total.Value * 100).ToString("0.0", Culture)
        End If
        Result("SeasonPlayers") = FormatNumber(NumberField(Json, "playtime_players", True), "#,0")
        Result("SeasonHours") = FormatNumber(NumberField(Json, "playtime_total_hours"), "#,0.0", " 小时")
        Dim Week = ObjectField(Json, "weekly")
        Result("WeekAchievements") = FormatNumber(NumberField(Week, "ach", True), "#,0", " 项")
        Dim Top = ObjectField(Week, "top")
        Result("WeeklyTopName") = TextField(Top, "name")
        Result("WeeklyTopHours") = FormatNumber(NumberField(Top, "seconds", True), "#,0.0", " 小时", 3600)

        Dim Stale = BooleanField(Json, "stale"), Ok = BooleanField(Json, "ok")
        If (Stale.HasValue AndAlso Stale.Value) OrElse (Ok.HasValue AndAlso Not Ok.Value) Then
            Result("DataHint") = "源站数据可能滞后，请结合更新时间查看。"
        Else
            Result("DataHint") = "服务器 " & UpdatedText(Json("updated_at")) & " · 游玩统计 " & UpdatedText(Json("activity_updated_at"))
        End If
        Return Result
    End Function

    Private Shared Function ObjectField(Source As JObject, Key As String) As JObject
        Return If(Source Is Nothing, Nothing, TryCast(Source(Key), JObject))
    End Function

    Private Shared Function BooleanField(Source As JObject, Key As String) As Boolean?
        Dim Token = If(Source Is Nothing, Nothing, Source(Key))
        If Token Is Nothing OrElse Token.Type <> JTokenType.Boolean Then Return Nothing
        Return Token.Value(Of Boolean)()
    End Function

    Private Shared Function NumberField(Source As JObject, Key As String, Optional Whole As Boolean = False) As Double?
        Dim Token = If(Source Is Nothing, Nothing, Source(Key))
        If Token Is Nothing OrElse (Token.Type <> JTokenType.Integer AndAlso Token.Type <> JTokenType.Float) Then Return Nothing
        Dim Value As Double
        If Not Double.TryParse(Token.ToString(), NumberStyles.Float, Culture, Value) OrElse Double.IsNaN(Value) OrElse Double.IsInfinity(Value) OrElse Value < 0 Then Return Nothing
        If Whole AndAlso (Value <> Math.Truncate(Value) OrElse Value > Integer.MaxValue) Then Return Nothing
        Return Value
    End Function

    Private Shared Function DateField(Source As JObject, Key As String) As DateTime?
        Dim Token = If(Source Is Nothing, Nothing, Source(Key))
        If Token Is Nothing OrElse Token.Type <> JTokenType.String Then Return Nothing
        Dim Value As DateTime
        If DateTime.TryParseExact(Token.ToString(), "yyyy-MM-dd", Culture, DateTimeStyles.None, Value) Then Return Value
        Return Nothing
    End Function

    Private Shared Function TextField(Source As JObject, Key As String) As String
        Dim Token = If(Source Is Nothing, Nothing, Source(Key))
        If Token Is Nothing OrElse Token.Type <> JTokenType.String OrElse String.IsNullOrWhiteSpace(Token.ToString()) Then Return Missing
        Return Token.ToString().Trim()
    End Function

    Private Shared Function FormatNumber(Value As Double?, Format As String, Optional Unit As String = "", Optional Divisor As Double = 1) As String
        Return If(Value.HasValue, (Value.GetValueOrDefault() / Divisor).ToString(Format, Culture) & Unit, Missing)
    End Function

    Private Shared Function FormatUptime(Seconds As Double) As String
        Dim Span = TimeSpan.FromSeconds(Seconds)
        If Span.TotalDays >= 1 Then Return Math.Floor(Span.TotalDays).ToString("0", Culture) & "天" & Span.Hours.ToString(Culture) & "小时"
        If Span.TotalHours >= 1 Then Return Span.Hours.ToString(Culture) & "小时" & Span.Minutes.ToString(Culture) & "分"
        If Span.TotalMinutes >= 1 Then Return Span.Minutes.ToString(Culture) & "分"
        Return Span.Seconds.ToString(Culture) & "秒"
    End Function

    Private Shared Function UpdatedText(Token As JToken) As String
        If Token Is Nothing Then Return Missing
        Dim Value As DateTime
        If Token.Type = JTokenType.Date Then
            Value = Token.Value(Of DateTime)()
        ElseIf Token.Type <> JTokenType.String OrElse Not DateTime.TryParse(Token.ToString(), Culture, DateTimeStyles.RoundtripKind, Value) Then
            Return Missing
        End If
        Return Value.ToString("MM-dd HH:mm", Culture)
    End Function
End Class
