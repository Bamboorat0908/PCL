Imports System.IO
Imports System.IO.Compression
Imports System.Threading.Tasks
Imports Microsoft.VisualBasic.FileIO

Public Class PageInstanceContent
    Private ReadOnly Kind As InstanceContentKind
    Private ContentRoot As String
    Private Busy As Boolean

    Public Sub New(ContentKind As InstanceContentKind)
        InitializeComponent()
        Kind = ContentKind
    End Sub

    Private Sub OnLoad() Handles Me.Loaded
        ContentRoot = Path.Combine(PageInstanceLeft.Instance.PathIndie, InstanceContentStore.FolderName(Kind))
        LabTitle.Text = InstanceContentStore.Title(Kind)
        LabPath.Text = ContentRoot
        LabPath.ToolTip = ContentRoot
        LabDescription.Text = PageInstanceLeft.Instance.Name & " · " & If(Kind = InstanceContentKind.Schematics, "Create 蓝图使用 .nbt 文件。", If(Kind = InstanceContentKind.Screenshots, "选择截图可预览，双击打开原图。", If(Kind = InstanceContentKind.Saves, "支持存档文件夹或 ZIP；修改存档前请先退出游戏。", "导入后请在游戏内启用；与其他版本共用目录时，内容也会共用。")))
        BtnImportFolder.Visibility = If(Kind = InstanceContentKind.Screenshots OrElse Kind = InstanceContentKind.Schematics, Visibility.Collapsed, Visibility.Visible)
        RefreshEntries()
    End Sub

    Public Sub RefreshEntries() Handles BtnRefresh.Click, TextSearch.TextChanged
        If ContentRoot Is Nothing OrElse Busy Then Return
        Try
            Dim Items = InstanceContentStore.ListEntries(ContentRoot, TextSearch.Text)
            EntryList.ItemsSource = Items
            LabEmpty.Visibility = If(Items.Count = 0, Visibility.Visible, Visibility.Collapsed)
            LabEmpty.Text = If(String.IsNullOrWhiteSpace(TextSearch.Text), "这里还没有内容，可以从上方导入。", "没有匹配的内容。")
            LabStatus.Text = Items.Count & " 项 · 导入不会覆盖同名内容；删除后可从 Windows 回收站恢复。"
            UpdateSelection()
        Catch ex As Exception
            Logger.Error(ex, "读取版本内容失败", LogBehavior.Toast)
        End Try
    End Sub

    Private Sub UpdateSelection() Handles EntryList.SelectionChanged
        Dim Item = TryCast(EntryList.SelectedItem, InstanceContentEntry)
        PanSelection.IsEnabled = Item IsNot Nothing AndAlso Not Busy
        BtnExport.IsEnabled = Item IsNot Nothing AndAlso Not Busy
        ImgPreview.Source = Nothing
        PanPreview.Visibility = Visibility.Collapsed
        If Item Is Nothing OrElse Item.IsFolder OrElse Kind <> InstanceContentKind.Screenshots Then Return
        Try
            Dim Bitmap As New BitmapImage()
            Bitmap.BeginInit()
            Bitmap.CacheOption = BitmapCacheOption.OnLoad
            Bitmap.DecodePixelWidth = 380
            Bitmap.UriSource = New Uri(Item.FullPath)
            Bitmap.EndInit()
            Bitmap.Freeze()
            ImgPreview.Source = Bitmap
            PanPreview.Visibility = Visibility.Visible
        Catch
            LabStatus.Text = "此图片暂不能预览，可使用「打开所选」查看。"
        End Try
    End Sub

    Private Sub OpenFolder() Handles BtnFolder.Click
        Try
            Directory.CreateDirectory(ContentRoot)
            OpenExplorer(ContentRoot)
        Catch ex As Exception
            Logger.Error(ex, "打开内容目录失败", LogBehavior.Toast)
        End Try
    End Sub

    Private Sub OpenSelected() Handles BtnOpen.Click, EntryList.MouseDoubleClick
        Dim Item = TryCast(EntryList.SelectedItem, InstanceContentEntry)
        If Item Is Nothing Then Return
        Try
            InstanceContentStore.ValidateChild(ContentRoot, Item.FullPath)
            '目录和图片直接打开；压缩包与蓝图只定位文件，避免误执行其他文件。
            If Item.IsFolder OrElse Kind = InstanceContentKind.Screenshots Then
                Process.Start(New ProcessStartInfo(Item.FullPath) With {.UseShellExecute = True})
            Else
                OpenExplorer("/select,""" & Item.FullPath & """")
            End If
        Catch ex As Exception
            Logger.Error(ex, "打开所选内容失败", LogBehavior.Toast)
        End Try
    End Sub

    Private Async Sub ImportFile() Handles BtnImport.Click
        Try
            Dim Extensions = If(Kind = InstanceContentKind.Schematics, {"nbt"}, If(Kind = InstanceContentKind.Screenshots, {"png", "jpg", "jpeg", "webp"}, {"zip"}))
            Dim Source = Dialogs.SelectFile("导入" & InstanceContentStore.Title(Kind), False, filter:={(Extensions, "支持的文件")}).FirstOrDefault()
            If String.IsNullOrEmpty(Source) Then Return
            Await ImportSource(Source)
        Catch ex As Exception
            Logger.Error(ex, "选择要导入的文件失败", LogBehavior.Alert)
        End Try
    End Sub

    Private Async Sub ImportFolder() Handles BtnImportFolder.Click
        Try
            Dim Source = Dialogs.SelectFolder("选择要导入的文件夹", False).FirstOrDefault()
            If String.IsNullOrEmpty(Source) Then Return
            Await ImportSource(Source)
        Catch ex As Exception
            Logger.Error(ex, "选择要导入的文件夹失败", LogBehavior.Alert)
        End Try
    End Sub

    Private Async Function ImportSource(Source As String) As Task
        Dim Root = ContentRoot
        SetBusy(True)
        Try
            Await Task.Run(Sub() InstanceContentStore.ImportItem(Root, Source, Kind))
            Hint("导入完成。", HintType.Green)
        Catch ex As Exception
            Logger.Error(ex, "导入版本内容失败", LogBehavior.Alert)
        Finally
            SetBusy(False)
            RefreshEntries()
        End Try
    End Function

    Private Async Sub ExportSelected() Handles BtnExport.Click
        Dim Item = TryCast(EntryList.SelectedItem, InstanceContentEntry)
        If Item Is Nothing Then Return
        Try
            Dim Destination = Dialogs.SelectFolder("选择导出 / 备份目录", False).FirstOrDefault()
            If String.IsNullOrEmpty(Destination) Then Return
            Dim Root = ContentRoot
            SetBusy(True)
            Await Task.Run(Sub()
                               InstanceContentStore.ValidateChild(Root, Item.FullPath)
                               If Item.IsFolder Then
                                   Dim Target = InstanceContentStore.UniquePath(Destination, Item.Name & ".zip")
                                   CreateFolderBackup(Item.FullPath, Target)
                               Else
                                   File.Copy(Item.FullPath, InstanceContentStore.UniquePath(Destination, Item.Name), False)
                               End If
                           End Sub)
            Hint("导出 / 备份完成。", HintType.Green)
        Catch ex As Exception
            Logger.Error(ex, "导出版本内容失败", LogBehavior.Alert)
        Finally
            SetBusy(False)
        End Try
    End Sub

    Private Shared Sub CreateFolderBackup(Source As String, Target As String)
        If Path.GetFullPath(Target).StartsWith(Path.GetFullPath(Source).TrimEnd(Path.DirectorySeparatorChar) & Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) Then Throw New IOException("请选择所选内容以外的备份目录。")
        Dim Created As Boolean = False
        Try
            Using Output As New FileStream(Target, FileMode.CreateNew, FileAccess.Write, FileShare.None)
                Created = True
                Using Archive As New ZipArchive(Output, ZipArchiveMode.Create)
                    AddBackupFolder(Archive, New DirectoryInfo(Source), "")
                End Using
            End Using
        Catch
            'Only remove an archive successfully created by this operation.
            If Created Then
                Try
                    File.Delete(Target)
                Catch ex As Exception
                    Logger.Warn(ex, "清理未完成的内容备份失败")
                End Try
            End If
            Throw
        End Try
    End Sub

    Private Shared Sub AddBackupFolder(Archive As ZipArchive, Folder As DirectoryInfo, Relative As String)
        If (Folder.Attributes And FileAttributes.ReparsePoint) <> 0 Then Throw New IOException("备份内容包含链接文件或目录，请先移除链接。")
        Dim Items = Folder.EnumerateFileSystemInfos().ToList()
        If Items.Count = 0 AndAlso Relative.Length > 0 Then Archive.CreateEntry(Relative & "/")
        For Each Item In Items
            If (Item.Attributes And FileAttributes.ReparsePoint) <> 0 Then Throw New IOException("备份内容包含链接文件或目录，请先移除链接。")
            Dim EntryName = Relative & If(Relative.Length = 0, "", "/") & Item.Name
            If TypeOf Item Is DirectoryInfo Then
                AddBackupFolder(Archive, DirectCast(Item, DirectoryInfo), EntryName)
            Else
                Archive.CreateEntryFromFile(Item.FullName, EntryName, CompressionLevel.Fastest)
            End If
        Next
    End Sub

    Private Sub RenameSelected() Handles BtnRename.Click
        Dim Item = TryCast(EntryList.SelectedItem, InstanceContentEntry)
        If Item Is Nothing Then Return
        Dim NewName = MyMsgBoxInput("重命名", "请输入完整名称，文件请保留扩展名。", Item.Name)
        If String.IsNullOrWhiteSpace(NewName) Then Return
        Try
            InstanceContentStore.RenameItem(ContentRoot, Item.FullPath, NewName)
            RefreshEntries()
        Catch ex As Exception
            Logger.Error(ex, "重命名失败", LogBehavior.Alert)
        End Try
    End Sub

    Private Sub RecycleSelected() Handles BtnRecycle.Click
        Dim Item = TryCast(EntryList.SelectedItem, InstanceContentEntry)
        If Item Is Nothing OrElse MyMsgBox("将「" & Item.Name & "」移入 Windows 回收站？", "移入回收站", "移入回收站", "取消", IsWarn:=True) <> 1 Then Return
        Try
            InstanceContentStore.ValidateChild(ContentRoot, Item.FullPath)
            If Item.IsFolder Then
                FileSystem.DeleteDirectory(Item.FullPath, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin)
            Else
                FileSystem.DeleteFile(Item.FullPath, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin)
            End If
            RefreshEntries()
        Catch ex As Exception
            Logger.Error(ex, "移入回收站失败", LogBehavior.Alert)
        End Try
    End Sub

    Private Sub SetBusy(Value As Boolean)
        Busy = Value
        PanActions.IsEnabled = Not Busy
        TextSearch.IsEnabled = Not Busy
        PanSelection.IsEnabled = Not Busy AndAlso EntryList.SelectedItem IsNot Nothing
        If Busy Then LabStatus.Text = "正在处理文件，请稍候…"
    End Sub
End Class
