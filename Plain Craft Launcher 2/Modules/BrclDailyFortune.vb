Imports System.IO
Imports System.Globalization
Imports System.Security.Cryptography
Imports System.Text
Imports Newtonsoft.Json.Linq

Public NotInheritable Class BrclFortuneResult
    Public ReadOnly Property Day As DateTime
    Public ReadOnly Property Grade As String
    Public ReadOnly Property Blessing As String

    Public Sub New(Day As DateTime, Grade As String, Blessing As String)
        Me.Day = Day.Date
        Me.Grade = Grade
        Me.Blessing = Blessing
    End Sub
End Class

'One locally saved draw per calendar day. Both the grade and blessing are saved
'before revealing them, so reopening a page or launcher cannot reroll the result.
Public NotInheritable Class BrclDailyFortune
    Private Shared ReadOnly Grades As String() = {"大吉", "吉", "小吉", "平", "凶", "大凶"}
    Private Shared ReadOnly Blessings As String() = {
        "愿今天的每一次出发，都有温柔的回应。",
        "愿你的背包装满收获，心里装满快乐。",
        "愿你挖到心仪的矿石，也遇见合拍的朋友。",
        "愿齿轮顺畅转动，心里的小计划慢慢实现。",
        "愿你走过的每一条路，都有灯火等你回家。",
        "愿今天的你，比昨天多一点轻松与自在。",
        "愿你的认真被看见，你的付出有收获。",
        "愿平凡的一天，也藏着意想不到的小惊喜。",
        "愿你不慌不忙，把日子过成喜欢的模样。",
        "愿仓库常满，朋友常在，快乐常来。",
        "愿今天的难题，都能找到一位好队友。",
        "愿你的灵感不断，下一项发明顺利落地。",
        "愿小小的不顺，都被大大的好心情化解。",
        "愿你总有重新出发的勇气，也有歇一歇的底气。",
        "愿每一次回家，都有熟悉的风景和安心的灯光。",
        "愿你在忙碌之间，也能遇见一场漂亮的日落。",
        "愿好事像成熟的麦田，一点一点来到你身边。",
        "愿今天有人与你分享收获，也分担烦恼。",
        "愿你想做的事，今天能向前迈出一小步。",
        "愿你带着好奇出发，带着满足归来。"}

    Public Shared Function ReadForDate(FilePath As String, Today As DateTime) As BrclFortuneResult
        If Not File.Exists(FilePath) Then Return Nothing
        Dim Json As JObject
        Try
            Json = JObject.Parse(File.ReadAllText(FilePath, Encoding.UTF8))
        Catch ex As Newtonsoft.Json.JsonException
            Throw New FormatException("今日运势记录无法读取", ex)
        End Try
        Dim SavedDay As DateTime
        If Json("version")?.Type <> JTokenType.Integer OrElse Json.Value(Of Integer)("version") <> 1 OrElse
            Json("date")?.Type <> JTokenType.String OrElse
            Not DateTime.TryParseExact(Json.Value(Of String)("date"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, SavedDay) OrElse
            Json("grade")?.Type <> JTokenType.String OrElse Array.IndexOf(Grades, Json.Value(Of String)("grade")) < 0 OrElse
            Json("blessing")?.Type <> JTokenType.String OrElse String.IsNullOrWhiteSpace(Json.Value(Of String)("blessing")) OrElse
            Json.Value(Of String)("blessing").Length > 160 Then Throw New FormatException("今日运势记录无效")
        If SavedDay.Date <> Today.Date Then Return Nothing
        Return New BrclFortuneResult(SavedDay, Json.Value(Of String)("grade"), Json.Value(Of String)("blessing"))
    End Function

    Public Shared Function DrawForDate(FilePath As String, Today As DateTime) As BrclFortuneResult
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath))
        'The separate lock also serializes competing launcher/page instances.
        Using DrawLock As New FileStream(FilePath & ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None)
            Dim Existing = ReadForDate(FilePath, Today)
            If Existing IsNot Nothing Then Return Existing
            Dim Result As New BrclFortuneResult(Today, ChooseGrade(NextRandom(100)), Blessings(NextRandom(Blessings.Length)))
            Dim Json As New JObject From {
                {"version", 1}, {"date", Result.Day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)},
                {"grade", Result.Grade}, {"blessing", Result.Blessing}}
            Dim Pending = FilePath & "." & Guid.NewGuid().ToString("N") & ".tmp"
            Try
                Dim Bytes = New UTF8Encoding(False).GetBytes(Json.ToString(Newtonsoft.Json.Formatting.None))
                Using Output As New FileStream(Pending, FileMode.CreateNew, FileAccess.Write, FileShare.None)
                    Output.Write(Bytes, 0, Bytes.Length)
                    Output.Flush(True)
                End Using
                If File.Exists(FilePath) Then
                    File.Replace(Pending, FilePath, Nothing)
                Else
                    File.Move(Pending, FilePath)
                End If
            Finally
                If File.Exists(Pending) Then File.Delete(Pending)
            End Try
            Return Result
        End Using
    End Function

    Private Shared Function ChooseGrade(Roll As Integer) As String
        If Roll < 0 OrElse Roll >= 100 Then Throw New ArgumentOutOfRangeException(NameOf(Roll))
        If Roll < 20 Then Return Grades(0)
        If Roll < 50 Then Return Grades(1)
        If Roll < 75 Then Return Grades(2)
        If Roll < 90 Then Return Grades(3)
        If Roll < 98 Then Return Grades(4)
        Return Grades(5)
    End Function

    Private Shared Function NextRandom(Count As Integer) As Integer
        Dim Bytes(3) As Byte
        Dim Value As UInteger
        Dim Limit = UInteger.MaxValue - (UInteger.MaxValue Mod CUInt(Count))
        Using Random = RandomNumberGenerator.Create()
            Do
                Random.GetBytes(Bytes)
                Value = BitConverter.ToUInt32(Bytes, 0)
            Loop While Value >= Limit
        End Using
        Return CInt(Value Mod CUInt(Count))
    End Function
End Class
