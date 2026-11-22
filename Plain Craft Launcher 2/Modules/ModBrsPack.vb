Imports System.IO
Imports System.Text.RegularExpressions
Imports Newtonsoft.Json

Friend Module ModBrsPack
    Public Const BrsPackName As String = "TRM 1.11（高预设 / 低预设）"
    Private Const LaunchSelectionFile As String = "BRCL-TRM-selection.txt"
    Private Const CurrentPackVersion As String = "1.11"
    Private Const MarkerFile As String = "BRS-TRM.json"

    Friend Class BrsLaunchTarget
        Public ReadOnly Instance As McInstance
        Public ReadOnly PackVersion As Version
        Public ReadOnly PresetId As String

        Public Sub New(Instance As McInstance, PackVersion As Version, PresetId As String)
            Me.Instance = Instance
            Me.PackVersion = PackVersion
            Me.PresetId = PresetId
        End Sub

        Public ReadOnly Property Description As String
            Get
                Dim PresetName = If(PresetId = "high", "高预设", If(PresetId = "low", "低预设", "预设未知"))
                Return $"TRM {If(PackVersion?.ToString(), "版本未知")} · {PresetName}"
            End Get
        End Property

        Public ReadOnly Property HasUpgrade As Boolean
            Get
                Return PackVersion IsNot Nothing AndAlso PackVersion < Version.Parse(CurrentPackVersion)
            End Get
        End Property
    End Class

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
                Return PrimaryDownloadBase & "modpacks/" & FileName
            End Get
        End Property
        Public ReadOnly Property BackupUrl As String
            Get
                Return BackupDownloadBase & "modpacks/" & FileName
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

    Friend Function IsInstanceInFolder(Instance As McInstance, Folder As String) As Boolean
        Return Instance IsNot Nothing AndAlso Not String.IsNullOrEmpty(Folder) AndAlso
            String.Equals(Path.GetDirectoryName(Instance.PathVersion.TrimEnd("\"c)), Path.Combine(Folder, "versions"), StringComparison.OrdinalIgnoreCase) AndAlso
            Directory.Exists(Instance.PathVersion)
    End Function

    Friend Function ReadBrsLaunchTarget(Instance As McInstance) As BrsLaunchTarget
        If Instance Is Nothing OrElse Instance.State = McInstanceState.Error Then Return Nothing
        Dim RecognizedName = IsTrmInstanceName(Instance.Name)
        Dim MarkerPath = InstanceDataFile(Instance.PathVersion, MarkerFile)
        Dim HasMarker = File.Exists(MarkerPath)
        If Not (HasMarker OrElse RecognizedName) Then Return Nothing

        Dim Metadata As JObject = Nothing
        If HasMarker Then
            Try
                Metadata = FileUtils.ReadAsJson(MarkerPath)
            Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException OrElse TypeOf ex Is JsonException
                Logger.Warn(ex, $"读取 TRM 实例信息失败（{Instance.PathVersion}）")
            End Try
        End If

        Dim PackVersion As Version = Nothing
        Dim VersionText = TryCast(TryCast(Metadata?("version"), JValue)?.Value, String)
        If Not Version.TryParse(VersionText, PackVersion) AndAlso RecognizedName Then
            Version.TryParse(Regex.Match(Instance.Name, "(?<![0-9.])[0-9]+(?:\.[0-9]+){1,3}(?![0-9.])").Value, PackVersion)
        End If

        Dim Preset = TryCast(TryCast(Metadata?("preset"), JValue)?.Value, String)?.ToLowerInvariant()
        If Preset <> "high" AndAlso Preset <> "low" Then
            If Regex.IsMatch(Instance.Name, "高预设|(?<![a-z])high(?![a-z])", RegexOptions.IgnoreCase) Then Preset = "high"
            If Regex.IsMatch(Instance.Name, "低预设|(?<![a-z])low(?![a-z])", RegexOptions.IgnoreCase) Then Preset = "low"
        End If
        Return New BrsLaunchTarget(Instance, PackVersion, Preset)
    End Function

    Friend Function ReadBrsSelection(Folder As String) As String
        Dim SelectionPath = Path.Combine(Folder, LaunchSelectionFile)
        Try
            Return If(File.Exists(SelectionPath), FileUtils.ReadAsString(SelectionPath), "")
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException
            Logger.Warn(ex, $"读取已选 TRM 实例失败（{Folder}）")
            Return ""
        End Try
    End Function

    Friend Sub RememberBrsSelection(Folder As String, Instance As McInstance)
        If IsInstanceInFolder(Instance, Folder) AndAlso ReadBrsLaunchTarget(Instance) IsNot Nothing Then
            FileUtils.WriteAtomic(Path.Combine(Folder, LaunchSelectionFile), Instance.Name)
        End If
    End Sub

    Friend Sub RenameBrsSelection(Folder As String, OldName As String, NewName As String)
        If String.Equals(ReadBrsSelection(Folder), OldName, StringComparison.OrdinalIgnoreCase) Then
            FileUtils.WriteAtomic(Path.Combine(Folder, LaunchSelectionFile), NewName)
        End If
    End Sub

    Friend Sub PreserveBrsMetadataBeforeRename(Instance As McInstance)
        Dim Target = ReadBrsLaunchTarget(Instance)
        If Target IsNot Nothing AndAlso Not File.Exists(InstanceDataFile(Instance.PathVersion, MarkerFile)) Then
            WriteBrsMarker(Instance.PathVersion, Target.PackVersion?.ToString(), Target.PresetId)
        End If
    End Sub

    Private Sub WriteBrsMarker(Folder As String, PackVersion As String, PresetId As String)
        Dim Metadata As New JObject From {{"name", "TRM"}, {"version", PackVersion}, {"preset", PresetId}}
        FileUtils.WriteAtomic(InstanceDataFile(Folder, MarkerFile, ForWrite:=True), Metadata.ToString(Formatting.None))
    End Sub

    Friend Function SelectBrsLaunchTarget(Folder As String, Instances As IEnumerable(Of McInstance), SavedName As String) As BrsLaunchTarget
        Dim Candidates = Instances.Where(Function(Instance) IsInstanceInFolder(Instance, Folder))
        If SavedName <> "" Then
            Dim Selected = Candidates.FirstOrDefault(Function(Instance) String.Equals(Instance.Name, SavedName, StringComparison.OrdinalIgnoreCase))
            Return ReadBrsLaunchTarget(Selected)
        End If
        Return Candidates.Select(Function(Instance) ReadBrsLaunchTarget(Instance)).
            Where(Function(Target) Target IsNot Nothing).
            OrderByDescending(Function(Target) Target.PackVersion).
            ThenBy(Function(Target) Target.Instance.Name, StringComparer.OrdinalIgnoreCase).
            FirstOrDefault()
    End Function

    Public Function FindBrsInstance() As McInstance
        Dim Folder = McFolderSelected
        If Folder = "" OrElse McInstanceListLoader.State = LoadState.Loading Then Return Nothing
        Return SelectBrsLaunchTarget(Folder, McInstanceList.Values.SelectMany(Function(List) List), ReadBrsSelection(Folder))?.Instance
    End Function

    Friend Function CreateBrsInstallDirectory(InstallRoot As String, PresetName As String) As String
        Dim VersionsFolder = Path.Combine(InstallRoot, "versions")
        Dim Reservation = Path.Combine(VersionsFolder, ".brcl-trm-" & Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(Reservation)
        Try
            Dim Target = Path.Combine(VersionsFolder, PresetName)
            Dim Suffix = 2
            Do
                Try
                    '重命名空目录来占用目标名称。现有文件或目录不会被覆盖。
                    Directory.Move(Reservation, Target)
                    Return Target
                Catch ex As IOException When Directory.Exists(Target) OrElse File.Exists(Target)
                    Target = Path.Combine(VersionsFolder, $"{PresetName} ({Suffix})")
                    Suffix += 1
                End Try
            Loop
        Finally
            If Directory.Exists(Reservation) Then Directory.Delete(Reservation)
        End Try
    End Function

    Public Sub InstallBrsPack()
        If IsBrsPackInstalling Then Return
        Dim InstallRoot = McFolderSelected
        Dim PresetDialog As New BrsPackPresetWindow(InstallRoot)
        If PresetDialog.ShowDialog() <> True Then Return
        Dim InitialSelection = ReadBrsSelection(InstallRoot)
        Dim PresetId = If(PresetDialog.LowPreset, "low", "high")
        Dim Preset = GetBrsPackPreset(PresetDialog.LowPreset)
        Dim ArchivePath = Path.Combine(Paths.Base, "BRCL", "Downloads", Preset.FileName)
        Directory.CreateDirectory(Path.GetDirectoryName(ArchivePath))
        Dim Download As New LoaderDownload("下载服务器整合包", New List(Of NetFile) From {
            New NetFile({Preset.Url, Preset.BackupUrl}, ArchivePath, New FileChecker With {.Hash = Preset.Hash, .ActualSize = Preset.Size})}) With {.ProgressWeight = 20}
        Dim Install As New LoaderTask(Of String, String)("安装 TRM 整合包",
            Sub(Task As LoaderTask(Of String, String))
                Dim Token = Task.CreateCancellationToken()
                Token.ThrowIfCancellationRequested()
                Dim Problem = New FileChecker With {.Hash = Preset.Hash, .ActualSize = Preset.Size}
                Dim ErrorText = Problem.Check(ArchivePath)
                If ErrorText IsNot Nothing Then Throw New IOException(ErrorText)
                If McFolderSelected <> InstallRoot Then Throw New Exception("游戏目录已切换，请在目标目录重新安装。")
                '下载期间可能新增同名实例，在实际安装前分配未使用的目录。
                Dim VersionFolder = CreateBrsInstallDirectory(InstallRoot, Preset.Name)
                Dim InstanceName = Path.GetFileName(VersionFolder)
                Dim Child As LoaderCombo(Of String) = Nothing
                Try
                    Child = ModpackInstall(ArchivePath, InstanceName)
                    Child.WaitForExit(Input:=VersionFolder & "\", LoaderToSyncProgress:=Task, c:=Token)
                    Token.ThrowIfCancellationRequested()
                    Dim Installed As New McInstance(VersionFolder)
                    Installed.Load()
                    If Installed.State = McInstanceState.Error Then Throw New IOException(Installed.Info)
                    WriteBrsMarker(VersionFolder, CurrentPackVersion, PresetId)
                    RunInUiWait(Sub()
                                    Token.ThrowIfCancellationRequested()
                                    If ReadBrsSelection(InstallRoot) = InitialSelection Then
                                        RememberBrsSelection(InstallRoot, Installed)
                                        If McFolderSelected = InstallRoot Then McInstanceSelected = Installed
                                    End If
                                    If McFolderSelected = InstallRoot Then
                                        McInstanceListForceRefresh = True
                                        LoaderFolderRun(McInstanceListLoader, InstallRoot, LoaderFolderRunType.ForceRun, MaxDepth:=1, ExtraPath:="versions\")
                                    End If
                                End Sub)
                Catch
                    If Child IsNot Nothing Then
                        If Child.State = LoadState.Loading Then Child.Cancel()
                    Else
                        Try
                            '只有安装任务还未创建时，才尝试移除自己占用的空目录。
                            Directory.Delete(VersionFolder)
                        Catch ex As IOException
                            Logger.Warn(ex, $"无法清理 TRM 安装占位目录（{VersionFolder}）")
                        End Try
                    End If
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
