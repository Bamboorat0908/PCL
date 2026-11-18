''' <summary>离线恐龙跑酷的物理与碰撞逻辑，独立于下载和界面。</summary>
Public Class DinoRunnerModel
    Public Class Obstacle
        Public Property X As Double
        Public Property Y As Double
        Public Property Width As Double
        Public Property Height As Double
        Public Property IsBird As Boolean
    End Class

    Public Const Ground As Double = 176
    Public ReadOnly Obstacles As New List(Of Obstacle)
    Private ReadOnly Rng As Random
    Private SpawnIn As Double = 1.5
    Private Velocity As Double
    Public Property Elapsed As Double
    Public Property JumpHeight As Double
    Public Property Ducking As Boolean
    Public Property IsOver As Boolean
    Public ReadOnly Property Score As Integer
        Get
            Return CInt(Math.Floor(Elapsed * 10))
        End Get
    End Property
    Public ReadOnly Property Speed As Double
        Get
            Return Math.Min(400, 200 + Elapsed * 1.8)
        End Get
    End Property
    Public ReadOnly Property IsGrounded As Boolean
        Get
            Return JumpHeight <= 0
        End Get
    End Property

    Public Sub New(Optional Seed As Integer = -1)
        Rng = If(Seed = -1, New Random(), New Random(Seed))
    End Sub

    Public Sub Reset()
        Obstacles.Clear()
        Elapsed = 0
        JumpHeight = 0
        Velocity = 0
        Ducking = False
        IsOver = False
        SpawnIn = 1.5
    End Sub

    Public Sub Jump()
        If IsOver OrElse Not IsGrounded Then Return
        Ducking = False
        Velocity = 440
        JumpHeight = 0.01
    End Sub

    Public Sub Advance(Seconds As Double)
        If IsOver OrElse Seconds <= 0 OrElse Double.IsNaN(Seconds) Then Return
        Dim Remaining = Math.Min(Seconds, 0.1)
        While Remaining > 0 AndAlso Not IsOver
            Dim Delta = Math.Min(Remaining, 0.01)
            Remaining -= Delta
            Elapsed += Delta
            If Not IsGrounded Then
                JumpHeight += Velocity * Delta
                Velocity -= 1200 * Delta
                If JumpHeight <= 0 Then JumpHeight = 0 : Velocity = 0
            End If
            SpawnIn -= Delta
            If SpawnIn <= 0 Then
                Dim Bird = Score > 200 AndAlso Rng.Next(4) = 0
                Dim Width = If(Bird, 32, Rng.Next(16, 30))
                Dim Height = If(Bird, 20, Rng.Next(30, 48))
                Obstacles.Add(New Obstacle With {.X = 500, .Y = If(Bird, Ground - 47, Ground - Height), .Width = Width, .Height = Height, .IsBird = Bird})
                SpawnIn = (250 + Rng.Next(120)) / Speed
            End If
            Dim Duck = Ducking AndAlso IsGrounded
            Dim PlayerWidth = If(Duck, 40, 24)
            Dim PlayerHeight = If(Duck, 22, 44)
            For Index = Obstacles.Count - 1 To 0 Step -1
                Dim Item = Obstacles(Index)
                Item.X -= Speed * Delta
                If Item.X + Item.Width < 0 Then
                    Obstacles.RemoveAt(Index)
                ElseIf 56 + PlayerWidth - 3 > Item.X + 3 AndAlso 59 < Item.X + Item.Width - 3 AndAlso
                    Ground - JumpHeight - 3 > Item.Y + 3 AndAlso Ground - JumpHeight - PlayerHeight + 3 < Item.Y + Item.Height - 3 Then
                    IsOver = True
                End If
            Next
        End While
    End Sub
End Class
