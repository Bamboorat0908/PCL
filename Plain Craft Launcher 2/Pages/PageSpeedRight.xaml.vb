Public Class PageSpeedRight

    Private Sub Init() Handles Me.Loaded
        PanBack.ScrollToHome()
        RefreshGameStatus()
    End Sub

    Private Game As DownloadMiniGame
    Public ReadOnly Property KeepOpenForGame As Boolean
        Get
            Return PanGame.Visibility = Visibility.Visible
        End Get
    End Property

    Private Sub ToggleGame() Handles BtnGameToggle.Click
        If KeepOpenForGame Then
            Game?.PauseGame()
            PanGame.Visibility = Visibility.Collapsed
            BtnGameToggle.Text = "玩一会儿"
            FrmSpeedLeft.TryReturnToHome()
        Else
            If Game Is Nothing Then
                Game = New DownloadMiniGame()
                PanGame.Children.Add(Game)
            End If
            PanGame.Visibility = Visibility.Visible
            BtnGameToggle.Text = "收起游戏"
            RefreshGameStatus()
        End If
    End Sub

    Public Sub RefreshGameStatus()
        If Game Is Nothing Then Return
        Dim Tasks = LoaderTaskbar.ToList().Where(Function(Task) Task.Show).ToList()
        Game.UpdateDownloadState(Tasks.Any(Function(Task) Task.State = LoadState.Loading OrElse Task.State = LoadState.Waiting),
                                 Tasks.Any(Function(Task) Task.State = LoadState.Failed))
    End Sub

    Private Sub PauseGame() Handles Me.Unloaded
        Game?.PauseGame()
    End Sub

End Class
