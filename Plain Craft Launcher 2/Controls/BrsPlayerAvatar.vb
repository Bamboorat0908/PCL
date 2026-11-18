Imports System.IO
Imports System.Net.Http
Imports System.Text.RegularExpressions
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows.Media
Imports System.Windows.Media.Imaging

''' <summary>BRS 玩家头像。共用下载、内存与本地缓存，无法读取时显示名字首字。</summary>
Public Class BrsPlayerAvatar
    Inherits Border

    Private Shared ReadOnly Http As New HttpClient With {.Timeout = TimeSpan.FromSeconds(12)}
    Private Shared ReadOnly DownloadSlots As New SemaphoreSlim(4)
    Private Shared ReadOnly Cache As New Dictionary(Of String, AvatarEntry)(StringComparer.OrdinalIgnoreCase)
    Private Const MaxCachedAvatars As Integer = 128
    Private Const MaxAvatarBytes As Integer = 1024 * 1024
    Private Shared ReadOnly PlayerNamePattern As New Regex("\A[A-Za-z0-9_]{1,16}\z", RegexOptions.CultureInvariant)
    Private ReadOnly PlayerName As String
    Private ReadOnly CanLoad As Boolean
    Private ReadOnly Picture As New Image With {.Stretch = Stretch.UniformToFill}
    Private IsLoading As Boolean

    Shared Sub New()
        Http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0 Safari/537.36")
    End Sub

    Private Class AvatarEntry
        Public Created As DateTime = DateTime.UtcNow
        Public Picture As Task(Of BitmapSource)
    End Class

    Public Sub New(Name As String, Optional Size As Double = 28, Optional HasAvatar As Boolean = True)
        PlayerName = If(Name, "").Trim()
        CanLoad = HasAvatar AndAlso PlayerNamePattern.IsMatch(PlayerName)
        Width = Size
        Height = Size
        CornerRadius = New CornerRadius(3)
        VerticalAlignment = VerticalAlignment.Center
        ToolTip = PlayerName
        SetResourceReference(BackgroundProperty, "ColorBrushBg1")
        Dim Content As New Grid
        Dim Initial As New TextBlock With {
            .Text = If(PlayerName.Length = 0, "?", PlayerName.Substring(0, 1).ToUpperInvariant()),
            .FontSize = Size * 0.44, .FontWeight = FontWeights.SemiBold,
            .HorizontalAlignment = HorizontalAlignment.Center, .VerticalAlignment = VerticalAlignment.Center}
        Initial.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrush2")
        Content.Children.Add(Initial)
        RenderOptions.SetBitmapScalingMode(Picture, BitmapScalingMode.NearestNeighbor)
        Content.Children.Add(Picture)
        Child = Content
        Automation.AutomationProperties.SetName(Me, PlayerName & " 的头像")
    End Sub

    Private Async Sub Avatar_Loaded(sender As Object, e As RoutedEventArgs) Handles Me.Loaded
        If Not CanLoad OrElse IsLoading OrElse Picture.Source IsNot Nothing Then Return
        IsLoading = True
        Try
            Dim Pending As Task(Of BitmapSource)
            SyncLock Cache
                Dim Entry As AvatarEntry = Nothing
                If Not Cache.TryGetValue(PlayerName, Entry) OrElse DateTime.UtcNow - Entry.Created > TimeSpan.FromMinutes(5) Then
                    TrimCacheForInsert()
                    Entry = New AvatarEntry With {.Picture = Task.Run(Function() LoadPictureAsync(PlayerName))}
                    Cache(PlayerName) = Entry
                End If
                Pending = Entry.Picture
            End SyncLock
            Picture.Source = Await Pending
        Catch ex As Exception
            '头像失败不打断名单，也不弹出错误提示。
        Finally
            IsLoading = False
        End Try
    End Sub

    Private Shared Sub TrimCacheForInsert()
        'Called under Cache's lock. Completed entries are cheapest to evict;
        'bound the lookup even when a burst of unique names is still downloading.
        While Cache.Count >= MaxCachedAvatars
            Dim OldestKey As String = Nothing
            Dim OldestEntry As AvatarEntry = Nothing
            For Each Pair In Cache
                If OldestEntry Is Nothing OrElse
                    (Pair.Value.Picture.IsCompleted AndAlso Not OldestEntry.Picture.IsCompleted) OrElse
                    (Pair.Value.Picture.IsCompleted = OldestEntry.Picture.IsCompleted AndAlso Pair.Value.Created < OldestEntry.Created) Then
                    OldestKey = Pair.Key
                    OldestEntry = Pair.Value
                End If
            Next
            Cache.Remove(OldestKey)
        End While
    End Sub

    Private Shared Async Function DownloadPictureAsync(Name As String) As Task(Of Byte())
        Using Current As New CancellationTokenSource(TimeSpan.FromSeconds(12)),
            Response = Await Http.GetAsync("https://zhushu.homes/BRS/avatar/" & Uri.EscapeDataString(Name), HttpCompletionOption.ResponseHeadersRead, Current.Token).ConfigureAwait(False)
            Response.EnsureSuccessStatusCode()
            If Response.Content.Headers.ContentLength.GetValueOrDefault() > MaxAvatarBytes Then Throw New InvalidDataException("头像文件过大")
            Using Source = Await Response.Content.ReadAsStreamAsync().ConfigureAwait(False), Output As New MemoryStream()
                Dim Buffer(8191) As Byte
                Do
                    Dim Count = Await Source.ReadAsync(Buffer, 0, Buffer.Length, Current.Token).ConfigureAwait(False)
                    If Count = 0 Then Exit Do
                    If Output.Length + Count > MaxAvatarBytes Then Throw New InvalidDataException("头像文件过大")
                    Output.Write(Buffer, 0, Count)
                Loop
                Return Output.ToArray()
            End Using
        End Using
    End Function

    Private Shared Async Function LoadPictureAsync(Name As String) As Task(Of BitmapSource)
        Await DownloadSlots.WaitAsync().ConfigureAwait(False)
        Try
            Dim FilePath = Path.Combine(PathTemp, "BrsAvatars", Name.ToLowerInvariant() & ".png")
            Dim Cached As BitmapSource = Nothing
            If File.Exists(FilePath) Then
                Try
                    If New FileInfo(FilePath).Length <= MaxAvatarBytes Then Cached = Decode(File.ReadAllBytes(FilePath))
                    If Cached IsNot Nothing AndAlso DateTime.UtcNow - File.GetLastWriteTimeUtc(FilePath) < TimeSpan.FromDays(1) Then Return Cached
                Catch ex As Exception
                    Cached = Nothing
                End Try
            End If
            Try
                Dim Bytes = Await DownloadPictureAsync(Name).ConfigureAwait(False)
                Dim Result = Decode(Bytes)
                Try
                    Directory.CreateDirectory(Path.GetDirectoryName(FilePath))
                    File.WriteAllBytes(FilePath, Bytes)
                Catch ex As Exception
                End Try
                Return Result
            Catch ex As Exception
                Return Cached
            End Try
        Finally
            DownloadSlots.Release()
        End Try
    End Function

    Private Shared Function Decode(Bytes As Byte()) As BitmapSource
        Using Stream As New MemoryStream(Bytes)
            Dim Result As New BitmapImage
            Result.BeginInit()
            Result.CacheOption = BitmapCacheOption.OnLoad
            Result.DecodePixelWidth = 64
            Result.StreamSource = Stream
            Result.EndInit()
            Result.Freeze()
            Return Result
        End Using
    End Function
End Class
