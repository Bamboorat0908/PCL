Imports NAudio.Wave
Imports NAudio.Wave.SampleProviders

'Original, synthesized UI cues. No network requests or loose sound files are needed.
Public Module BrclAudio
    Public Enum UiSoundCue
        Click
        Navigate
        Toggle
        Startup
    End Enum

    Private Const SampleRate As Integer = 44100
    Private ReadOnly AudioLock As New Object
    Private ReadOnly RequestLock As New Object
    Private ReadOnly AudioClock As Diagnostics.Stopwatch = Diagnostics.Stopwatch.StartNew()
    Private ReadOnly SoundFormat As WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, 2)
    Private ReadOnly CachedCues As New Lazy(Of Dictionary(Of UiSoundCue, Single()))(Function() [Enum].GetValues(GetType(UiSoundCue)).Cast(Of UiSoundCue)().ToDictionary(Function(c) c, AddressOf CreateCueSamples))
    Private AudioOutput As WaveOutEvent
    Private AudioMixer As MixingSampleProvider
    Private IdleTimer As Threading.Timer
    Private LastInteraction As Double = -1000
    Private LastStartup As Double = -1000
    Private LastPlayback As Double
    Private PreferenceRevision As Integer
    Private AudioUnavailable As Boolean
    Private AudioStopped As Integer
    Private StandardButtonsRegistered As Integer

    Public Sub InstallUiSoundHandlers()
        If Threading.Interlocked.Exchange(StandardButtonsRegistered, 1) <> 0 Then Return
        EventManager.RegisterClassHandler(GetType(Primitives.ButtonBase), Primitives.ButtonBase.ClickEvent, New RoutedEventHandler(AddressOf StandardButtonClick))
    End Sub

    Private Sub StandardButtonClick(sender As Object, e As RoutedEventArgs)
        If TypeOf sender Is Primitives.ToggleButton Then
            PlayUiSound(UiSoundCue.Toggle)
        Else
            PlayUiSound()
        End If
    End Sub

    Public Sub WarmUiSounds()
        If Not Settings.Get(Of Boolean)("UiSoundEnabled") Then Return
        If Settings.Get(Of Integer)("UiSoundVolume") <= 0 Then Return
        If Not Settings.Get(Of Boolean)("UiSoundInteraction") AndAlso Not Settings.Get(Of Boolean)("UiSoundStartup") Then Return
        Threading.ThreadPool.QueueUserWorkItem(
            Sub(state)
                SyncLock AudioLock
                    If Threading.Volatile.Read(AudioStopped) = 0 Then EnsureAudioOutput()
                End SyncLock
            End Sub)
    End Sub

    Public Sub PlayUiSound(Optional Cue As UiSoundCue = UiSoundCue.Click)
        If Threading.Volatile.Read(AudioStopped) <> 0 OrElse Not Settings.Get(Of Boolean)("UiSoundEnabled") Then Return
        If Cue = UiSoundCue.Startup Then
            If Not Settings.Get(Of Boolean)("UiSoundStartup") Then Return
        ElseIf Not Settings.Get(Of Boolean)("UiSoundInteraction") Then
            Return
        End If
        Dim Gain As Single = CSng(Math.Max(0, Math.Min(100, Settings.Get(Of Integer)("UiSoundVolume"))) / 100.0)
        If Gain = 0 Then Return
        Dim RequestedAt = AudioClock.Elapsed.TotalMilliseconds
        SyncLock RequestLock
            If Cue = UiSoundCue.Startup Then
                If RequestedAt - LastStartup < 750 Then Return
                LastStartup = RequestedAt
            Else
                If RequestedAt - LastInteraction < 65 Then Return
                LastInteraction = RequestedAt
            End If
        End SyncLock
        Dim Revision = Threading.Volatile.Read(PreferenceRevision)
        Threading.ThreadPool.QueueUserWorkItem(
            Sub(state)
                SyncLock AudioLock
                    If Threading.Volatile.Read(AudioStopped) <> 0 OrElse Revision <> Threading.Volatile.Read(PreferenceRevision) Then Return
                    'Do not replay a backlog after a slow device initialization.
                    If Cue <> UiSoundCue.Startup AndAlso AudioClock.Elapsed.TotalMilliseconds - RequestedAt > 250 Then Return
                    If Not EnsureAudioOutput() Then Return
                    Try
                        AudioMixer.AddMixerInput(New CueSampleProvider(CachedCues.Value(Cue), Gain))
                        LastPlayback = AudioClock.Elapsed.TotalMilliseconds
                        AudioOutput.Play()
                        IdleTimer.Change(1200, Threading.Timeout.Infinite)
                    Catch ex As Exception
                        DisableAudioOutput(ex)
                    End Try
                End SyncLock
            End Sub)
    End Sub

    'Invalidate queued cues immediately, including when volume reaches zero.
    Public Sub UiSoundPreferencesChanged()
        Dim Revision = Threading.Interlocked.Increment(PreferenceRevision)
        Threading.ThreadPool.QueueUserWorkItem(
            Sub(state)
                SyncLock AudioLock
                    If Revision <> Threading.Volatile.Read(PreferenceRevision) Then Return
                    AudioMixer?.RemoveAllMixerInputs()
                End SyncLock
            End Sub)
    End Sub

    Private Function EnsureAudioOutput() As Boolean
        If AudioUnavailable Then Return False
        If AudioOutput IsNot Nothing Then Return True
        Try
            Dim Cues = CachedCues.Value 'Precompute once, off the UI thread.
            AudioMixer = New MixingSampleProvider(SoundFormat) With {.ReadFully = True}
            AudioOutput = New WaveOutEvent With {.DesiredLatency = 60, .NumberOfBuffers = 3}
            AudioOutput.Init(New SampleToWaveProvider16(AudioMixer))
            IdleTimer = New Threading.Timer(AddressOf StopIdleAudio, Nothing, Threading.Timeout.Infinite, Threading.Timeout.Infinite)
            Logger.Info("界面音效已就绪")
            Return True
        Catch ex As Exception
            DisableAudioOutput(ex)
            Return False
        End Try
    End Function

    Private Sub StopIdleAudio(state As Object)
        SyncLock AudioLock
            If AudioOutput Is Nothing OrElse AudioClock.Elapsed.TotalMilliseconds - LastPlayback < 1000 Then Return
            Try
                AudioOutput.Stop()
                AudioMixer.RemoveAllMixerInputs()
            Catch ex As Exception
                DisableAudioOutput(ex)
            End Try
        End SyncLock
    End Sub

    Private Sub DisableAudioOutput(ex As Exception)
        AudioUnavailable = True
        Try
            IdleTimer?.Dispose()
            AudioOutput?.Dispose()
        Catch
        End Try
        IdleTimer = Nothing
        AudioOutput = Nothing
        AudioMixer = Nothing
        Logger.Warn(ex, "界面音效暂不可用，本次运行继续使用静音交互")
    End Sub

    Public Sub StopUiSounds()
        Threading.Interlocked.Exchange(AudioStopped, 1)
        Threading.Interlocked.Increment(PreferenceRevision)
        IdleTimer?.Dispose()
        'Device cleanup must not delay window closing.
        Threading.ThreadPool.QueueUserWorkItem(
            Sub(state)
                SyncLock AudioLock
                    Try
                        AudioOutput?.Dispose()
                    Catch
                    End Try
                    AudioOutput = Nothing
                End SyncLock
            End Sub)
    End Sub

    Friend Function CreateCueSamples(Cue As UiSoundCue) As Single()
        Dim Duration As Double = If(Cue = UiSoundCue.Startup, 0.68, If(Cue = UiSoundCue.Toggle, 0.15, If(Cue = UiSoundCue.Navigate, 0.12, 0.085)))
        Dim Frames As Integer = CInt(Duration * SampleRate)
        Dim Samples(Frames * 2 - 1) As Single
        For Frame = 0 To Frames - 1
            Dim Time = Frame / CDbl(SampleRate)
            Dim Value As Double
            Select Case Cue
                Case UiSoundCue.Startup
                    'A soft D-major signature, with a quiet upper octave tail.
                    Value = Bell(Time, 587.33, 0.28, 0.39) + Bell(Time - 0.095, 739.99, 0.25, 0.37) + Bell(Time - 0.19, 880, 0.23, 0.34) + Bell(Time - 0.285, 1174.66, 0.15, 0.30)
                Case UiSoundCue.Toggle
                    Value = Bell(Time, 659.25, 0.31, 0.10) + Bell(Time - 0.035, 880, 0.22, 0.10)
                Case UiSoundCue.Navigate
                    Value = Bell(Time, 739.99, 0.38, 0.12)
                Case Else
                    Value = Bell(Time, 880, 0.36, 0.085) + Bell(Time, 440, 0.09, 0.06)
            End Select
            'Both endpoints are silent; the short fade prevents clicks at truncation.
            Dim Fade = Math.Min(1, Math.Max(0, (Duration - Time - 1.0 / SampleRate) / 0.018))
            Value = Math.Max(-0.85, Math.Min(0.85, Value * Fade))
            Samples(Frame * 2) = CSng(Value)
            Samples(Frame * 2 + 1) = CSng(Value)
        Next
        Return Samples
    End Function

    Private Function Bell(Time As Double, Frequency As Double, Gain As Double, Length As Double) As Double
        If Time <= 0 OrElse Time >= Length Then Return 0
        Dim Envelope = Math.Min(1, Time / 0.006) * Math.Exp(-Time / (Length * 0.24)) * Math.Min(1, (Length - Time) / 0.02)
        Dim Phase = 2 * Math.PI * Frequency * Time
        Return Gain * Envelope * (Math.Sin(Phase) + 0.16 * Math.Sin(Phase * 2) + 0.04 * Math.Sin(Phase * 3))
    End Function

    Private Class CueSampleProvider
        Implements ISampleProvider
        Private ReadOnly Samples As Single()
        Private ReadOnly Gain As Single
        Private Position As Integer
        Public Sub New(Samples As Single(), Gain As Single)
            Me.Samples = Samples
            Me.Gain = Gain
        End Sub
        Public ReadOnly Property WaveFormat As WaveFormat Implements ISampleProvider.WaveFormat
            Get
                Return SoundFormat
            End Get
        End Property
        Public Function Read(Buffer As Single(), Offset As Integer, Count As Integer) As Integer Implements ISampleProvider.Read
            Dim Available = Math.Min(Count, Samples.Length - Position)
            For Index = 0 To Available - 1
                Buffer(Offset + Index) = Samples(Position + Index) * Gain
            Next
            Position += Available
            Return Available
        End Function
    End Class
End Module
