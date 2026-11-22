Imports Microsoft.Win32

Friend Module ModDataMigration
    Private ReadOnly RegistryRoot As String = GetRegistryRoot()
    Private ReadOnly LegacyRegistry As String = RegistryRoot & "PCLDebug"
    Private ReadOnly CurrentRegistry As String = RegistryRoot & "BRCL"
    Private Const RegistryMigrationMarker As String = "__BRCL_LegacySettingsMigrated"
    Private ReadOnly RegistryMigrationLock As New Object()
    Private RegistryMigrationComplete As Boolean

    Private Function GetRegistryRoot() As String
        Dim TestId = Environment.GetEnvironmentVariable("BRCL_TEST_REGISTRY_ID")
        If String.IsNullOrEmpty(TestId) Then Return "Software\"
        Return $"Software\BRCLRegression\{Guid.ParseExact(TestId, "N"):N}\Software\"
    End Function

    Friend Function ResolveDataFile(Current As String, Legacy As String, Optional ForWrite As Boolean = False) As String
        If FileUtils.Exists(Current) OrElse Not FileUtils.Exists(Legacy) Then Return Current

        Dim Temporary = Current & "." & Guid.NewGuid().ToString("N") & ".tmp"
        Try
            DirectoryUtils.Create(IO.Path.GetDirectoryName(Current))
            Using Input = New FileStream(PathUtils.ForApi(Legacy), FileMode.Open, FileAccess.Read, FileShare.Read),
                  Output = New FileStream(PathUtils.ForApi(Temporary), FileMode.CreateNew, FileAccess.Write, FileShare.None)
                Input.CopyTo(Output)
                Output.Flush(True)
            End Using
            Try
                IO.File.Move(PathUtils.ForApi(Temporary), PathUtils.ForApi(Current))
            Catch ex As IOException When FileUtils.Exists(Current)
                '另一实例已发布新文件，保留先成功的文件。
            End Try
            Return Current
        Catch ex As Exception When Not ForWrite AndAlso (TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException)
            Logger.Warn(ex, $"迁移数据文件失败，继续读取旧文件：{Legacy}")
            Return Legacy
        Finally
            If FileUtils.Exists(Temporary) Then
                Try
                    FileUtils.Delete(Temporary)
                Catch ex As Exception
                    Logger.Warn(ex, $"无法清理迁移暂存文件：{Temporary}")
                End Try
            End If
        End Try
    End Function

    Friend Function InstanceDataFile(Folder As String, FileName As String, Optional ForWrite As Boolean = False) As String
        Return ResolveDataFile(IO.Path.Combine(Folder, "BRCL", FileName), IO.Path.Combine(Folder, "PCL", FileName), ForWrite)
    End Function

    Friend Function IsInstanceLogoReference(Path As String) As Boolean
        If String.IsNullOrEmpty(Path) Then Return False
        Dim StandardPath = Path.Replace("/"c, "\"c)
        Return StandardPath.EndsWith("BRCL\Logo.png", StringComparison.OrdinalIgnoreCase) OrElse
               StandardPath.EndsWith("PCL\Logo.png", StringComparison.OrdinalIgnoreCase)
    End Function

    Friend Function ReadTitleLogoFile() As String
        Dim Current = IO.Path.Combine(Paths.Base, "BRCL", "Logo.png")
        If FileUtils.Exists(Current & ".cleared") Then Return Current
        Return ResolveDataFile(Current, IO.Path.Combine(Paths.Base, "PCL", "Logo.png"))
    End Function

    Friend Sub ClearTitleLogoFile()
        Dim Current = IO.Path.Combine(Paths.Base, "BRCL", "Logo.png")
        '旧图标供回退使用，清空后不再将它重新导入。
        FileUtils.WriteAtomic(Current & ".cleared", "")
        FileUtils.Delete(Current)
    End Sub

    Private Sub EnsureRegistryMigration()
        SyncLock RegistryMigrationLock
            If RegistryMigrationComplete Then Return
            Using Gate As New Mutex(False, "Local\BRCL_SettingsMigration")
                Try
                    If Not Gate.WaitOne(5000) Then Throw New IOException("等待设置迁移锁超时。")
                Catch ex As AbandonedMutexException
                    '上一进程未完成时，从已经复制的值继续。
                End Try
                Try
                    Using Current = Registry.CurrentUser.CreateSubKey(CurrentRegistry)
                        If Not Object.Equals(Current.GetValue(RegistryMigrationMarker), 1) Then
                            Using Legacy = Registry.CurrentUser.OpenSubKey(LegacyRegistry)
                                If Legacy IsNot Nothing Then
                                    Dim Existing = New HashSet(Of String)(Current.GetValueNames(), StringComparer.OrdinalIgnoreCase)
                                    For Each Name In Legacy.GetValueNames()
                                        If Name.Equals(RegistryMigrationMarker, StringComparison.OrdinalIgnoreCase) OrElse Existing.Contains(Name) Then Continue For
                                        Dim Value = Legacy.GetValue(Name, Nothing, RegistryValueOptions.DoNotExpandEnvironmentNames)
                                        If Value IsNot Nothing Then Current.SetValue(Name, Value, Legacy.GetValueKind(Name))
                                    Next
                                End If
                            End Using
                            Current.Flush()
                            Current.SetValue(RegistryMigrationMarker, 1, RegistryValueKind.DWord)
                            Current.Flush()
                        End If
                    End Using
                    RegistryMigrationComplete = True
                Finally
                    Gate.ReleaseMutex()
                End Try
            End Using
        End SyncLock
    End Sub

    Friend Function ReadDataRegistry(Key As String, DefaultValue As Object) As Object
        Try
            EnsureRegistryMigration()
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException OrElse TypeOf ex Is Security.SecurityException
            Logger.Warn(ex, "设置注册表迁移未完成，读取时会尝试旧配置。")
        End Try

        Dim CurrentValue As Object = Nothing
        Try
            Using Current = Registry.CurrentUser.OpenSubKey(CurrentRegistry)
                CurrentValue = Current?.GetValue(Key)
            End Using
        Catch ex As Exception When Not RegistryMigrationComplete AndAlso (TypeOf ex Is UnauthorizedAccessException OrElse TypeOf ex Is Security.SecurityException)
            Logger.Warn(ex, "无法读取新的设置注册表。")
        End Try
        If CurrentValue IsNot Nothing Then Return CurrentValue
        If RegistryMigrationComplete Then Return DefaultValue
        Using Legacy = Registry.CurrentUser.OpenSubKey(LegacyRegistry)
            Return If(Legacy?.GetValue(Key), DefaultValue)
        End Using
    End Function

    Friend Sub WriteDataRegistry(Key As String, Value As Object)
        If Key.Equals(RegistryMigrationMarker, StringComparison.OrdinalIgnoreCase) Then Throw New ArgumentException("注册表迁移标记为保留值。")
        EnsureRegistryMigration()
        Using Current = Registry.CurrentUser.OpenSubKey(CurrentRegistry, True)
            If Value Is Nothing Then
                Current.DeleteValue(Key, False)
            Else
                Current.SetValue(Key, Value)
            End If
        End Using
    End Sub
End Module

Friend Class MigratingInstanceConfig
    Implements IConfigProvider

    Private ReadOnly Folder As String
    Private CurrentPath As String
    Private Current As JsonConfigProvider

    Public Sub New(Folder As String)
        Me.Folder = Folder
    End Sub

    Private Function Provider(Optional ForWrite As Boolean = False) As JsonConfigProvider
        Dim Path = InstanceDataFile(Folder, "config.json", ForWrite)
        If Current Is Nothing OrElse Not String.Equals(CurrentPath, Path, StringComparison.OrdinalIgnoreCase) Then
            CurrentPath = Path
            Current = New JsonConfigProvider(Path)
        End If
        Return Current
    End Function

    Public Function Read(Of T)(Key As String, DefaultValue As T, Encrypted As Boolean) As T Implements IConfigProvider.Read
        SyncLock Me
            Return Provider().Read(Key, DefaultValue, Encrypted)
        End SyncLock
    End Function

    Public Function HasValue(Key As String) As Boolean Implements IConfigProvider.HasValue
        SyncLock Me
            Return Provider().HasValue(Key)
        End SyncLock
    End Function

    Public Sub [Set](Of T)(Key As String, Value As T, Encrypted As Boolean) Implements IConfigProvider.Set
        SyncLock Me
            Provider(True).Set(Key, Value, Encrypted)
        End SyncLock
    End Sub

    Public Sub Remove(Key As String) Implements IConfigProvider.Remove
        SyncLock Me
            Provider(True).Remove(Key)
        End SyncLock
    End Sub

    Public Sub DiscardCache() Implements IConfigProvider.DiscardCache
        SyncLock Me
            Current?.DiscardCache()
        End SyncLock
    End Sub
End Class
