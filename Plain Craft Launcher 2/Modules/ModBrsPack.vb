Imports System.IO
Imports System.Text.RegularExpressions

Friend Module ModBrsPack
    Public Const BrsPackName As String = "TRM 1.11（高预设 / 低预设）"
    Friend Class BrsPackPreset
        Public ReadOnly Name As String
        Public ReadOnly FileName As String
        Public ReadOnly Hash As String
        Public ReadOnly Size As Long
        Public Sub New(PresetName As String, ArchiveName As String, Sha256 As String, ArchiveSize As Long)
            Name = PresetName
            FileName = ArchiveName
            Hash = Sha256
            Size = ArchiveSize
        End Sub
        Public ReadOnly Property Url As String
            Get
                Return "http://t42.sjcmc.cn:25261/modpacks/" & FileName
            End Get
        End Property
    End Class

    Public Function GetBrsPackPreset(LowPreset As Boolean) As BrsPackPreset
        If LowPreset Then Return New BrsPackPreset("TRM 1.11 低预设", "TRM-1.11-low.zip", "77ae44973a087a767bf64ce927dcae66595a330d7d8e77e2b48ad3c900d369fd", 164806392)
        Return New BrsPackPreset("TRM 1.11 高预设", "TRM-1.11-high.zip", "408a5201984a03dea09cc9a475186af56a5b350fa6860707774e2e823cf403fe", 164408085)
    End Function
    Private PackTask As LoaderCombo(Of String)

    Public ReadOnly Property IsBrsPackInstalling As Boolean
        Get
            Return PackTask IsNot Nothing AndAlso PackTask.State = LoadState.Loading
        End Get
    End Property

    Public Function IsTrmInstanceName(Name As String) As Boolean
        Return Not String.IsNullOrWhiteSpace(Name) AndAlso
            (Regex.IsMatch(Name, "(^|[^a-z0-9])TRM($|[^a-z0-9])", RegexOptions.IgnoreCase) OrElse
             Name.IndexOf("The Retired Machinist", StringComparison.OrdinalIgnoreCase) >= 0 OrElse Name.Contains("退休机械工"))
    End Function

    Private Function IsTrmInstance(Instance As McInstance) As Boolean
        Return Instance IsNot Nothing AndAlso Instance.State <> McInstanceState.Error AndAlso
            (IsTrmInstanceName(Instance.Name) OrElse File.Exists(Path.Combine(Instance.PathVersion, "PCL", "BRS-TRM.json")))
    End Function

    Public Function FindBrsInstance() As McInstance
        If IsTrmInstance(McInstanceSelected) Then Return McInstanceSelected
        Return McInstanceList.Values.SelectMany(Function(List) List).FirstOrDefault(Function(Instance) IsTrmInstance(Instance))
    End Function

    Public Sub InstallBrsPack()
        If IsBrsPackInstalling Then Return
        Dim InstallRoot = McFolderSelected
        Dim PresetDialog As New BrsPackPresetWindow(InstallRoot)
        If PresetDialog.ShowDialog() <> True Then Return
        Dim Choice = If(PresetDialog.LowPreset, 2, 1)
        Dim Preset = GetBrsPackPreset(PresetDialog.LowPreset)
        Dim InstanceName = Preset.Name
        Dim Suffix = 2
        While Directory.Exists(Path.Combine(InstallRoot, "versions", InstanceName))
            InstanceName = Preset.Name & " (" & Suffix & ")"
            Suffix += 1
        End While
        Dim ArchivePath = Path.Combine(Paths.Base, "BRCL", "Downloads", Preset.FileName)
        Directory.CreateDirectory(Path.GetDirectoryName(ArchivePath))
        Dim Download As New LoaderDownload("下载服务器整合包", New List(Of NetFile) From {
            New NetFile({Preset.Url}, ArchivePath, New FileChecker With {.Hash = Preset.Hash, .ActualSize = Preset.Size})}) With {.ProgressWeight = 20}
        Dim Install As New LoaderTask(Of String, String)("安装 TRM 整合包",
            Sub(Task As LoaderTask(Of String, String))
                Dim Token = Task.CreateCancellationToken()
                Token.ThrowIfCancellationRequested()
                Dim Problem = New FileChecker With {.Hash = Preset.Hash, .ActualSize = Preset.Size}
                Dim ErrorText = Problem.Check(ArchivePath)
                If ErrorText IsNot Nothing Then Throw New IOException(ErrorText)
                If McFolderSelected <> InstallRoot Then Throw New Exception("游戏目录已切换，请在目标目录重新安装。")
                Dim Child As LoaderCombo(Of String) = Nothing
                Try
                    Child = ModpackInstall(ArchivePath, InstanceName)
                    Child.WaitForExit(Input:=Path.Combine(InstallRoot, "versions", InstanceName) & "\", LoaderToSyncProgress:=Task, c:=Token)
                    Token.ThrowIfCancellationRequested()
                    Dim VersionFolder = Path.Combine(InstallRoot, "versions", InstanceName)
                    Directory.CreateDirectory(Path.Combine(VersionFolder, "PCL"))
                    File.WriteAllText(Path.Combine(VersionFolder, "PCL", "BRS-TRM.json"), "{""name"":""TRM"",""version"":""1.11"",""preset"":""" & If(Choice = 2, "low", "high") & """}")
                    WriteIni(InstallRoot & "PCL.ini", "Version", InstanceName)
                    RunInUiWait(Sub()
                                    If McFolderSelected = InstallRoot Then
                                        McInstanceSelected = New McInstance(VersionFolder).Load()
                                        McInstanceListForceRefresh = True
                                        LoaderFolderRun(McInstanceListLoader, InstallRoot, LoaderFolderRunType.ForceRun, MaxDepth:=1, ExtraPath:="versions\")
                                    End If
                                End Sub)
                Catch
                    If Child IsNot Nothing AndAlso Child.State = LoadState.Loading Then Child.Cancel()
                    Throw
                End Try
            End Sub) With {.ProgressWeight = 80}
        PackTask = New LoaderCombo(Of String)(Preset.Name, New List(Of LoaderBase) From {Download, Install}) With {
            .OnStateChanged = Sub(Loader)
                                  If Loader.State = LoadState.Failed Then MyMsgBox(Loader.Error.GetDisplay(True), "服务器整合包安装失败", IsWarn:=True)
                                  If Loader.State = LoadState.Finished Then Hint("TRM 整合包安装完成，可以启动 BRS 了！", HintType.Green)
                                  FrmLaunchLeft?.RefreshButtonsUI()
                              End Sub}
        PackTask.Start(InstallRoot)
        LoaderTaskbarAdd(PackTask)
        FrmMain.BtnExtraDownload.ShowRefresh()
        FrmLaunchLeft.RefreshButtonsUI()
    End Sub
End Module
