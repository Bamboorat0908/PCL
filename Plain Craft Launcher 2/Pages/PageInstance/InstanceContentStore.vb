Imports System.IO
Imports System.IO.Compression

Public Enum InstanceContentKind
    Saves = 5
    ResourcePacks = 6
    Shaders = 7
    Screenshots = 8
    Schematics = 9
End Enum

Public Class InstanceContentEntry
    Public Property Name As String
    Public Property FullPath As String
    Public Property IsFolder As Boolean
    Public Property Summary As String
End Class

''' <summary>管理当前游戏目录内的内容；导入从不覆盖现有文件。</summary>
Public Class InstanceContentStore
    Public Shared Function FolderName(Kind As InstanceContentKind) As String
        Select Case Kind
            Case InstanceContentKind.Saves : Return "saves"
            Case InstanceContentKind.ResourcePacks : Return "resourcepacks"
            Case InstanceContentKind.Shaders : Return "shaderpacks"
            Case InstanceContentKind.Screenshots : Return "screenshots"
            Case InstanceContentKind.Schematics : Return "schematics"
            Case Else : Throw New ArgumentOutOfRangeException(NameOf(Kind))
        End Select
    End Function

    Public Shared Function Title(Kind As InstanceContentKind) As String
        Select Case Kind
            Case InstanceContentKind.Saves : Return "存档管理"
            Case InstanceContentKind.ResourcePacks : Return "资源包"
            Case InstanceContentKind.Shaders : Return "光影"
            Case InstanceContentKind.Screenshots : Return "截图"
            Case InstanceContentKind.Schematics : Return "机械动力蓝图"
            Case Else : Throw New ArgumentOutOfRangeException(NameOf(Kind))
        End Select
    End Function

    Public Shared Function ListEntries(Root As String, Query As String) As List(Of InstanceContentEntry)
        If Not Directory.Exists(Root) Then Return New List(Of InstanceContentEntry)
        Return New DirectoryInfo(Root).EnumerateFileSystemInfos().
            Where(Function(Item) Not Item.Name.StartsWith(".brcl-import-", StringComparison.OrdinalIgnoreCase) AndAlso
                  (Item.Attributes And FileAttributes.ReparsePoint) = 0 AndAlso
                  (String.IsNullOrWhiteSpace(Query) OrElse Item.Name.IndexOf(Query, StringComparison.OrdinalIgnoreCase) >= 0)).
            OrderByDescending(Function(Item) Item.LastWriteTime).
            Select(Function(Item) New InstanceContentEntry With {
                .Name = Item.Name, .FullPath = Item.FullName, .IsFolder = TypeOf Item Is DirectoryInfo,
                .Summary = If(TypeOf Item Is DirectoryInfo, "文件夹", StringUtils.FormatByteSize(DirectCast(Item, FileInfo).Length)) & " · " & Item.LastWriteTime.ToString("yyyy-MM-dd HH:mm")}).ToList()
    End Function

    Public Shared Sub ValidateChild(Root As String, ItemPath As String)
        Dim Parent = Path.GetDirectoryName(Path.GetFullPath(ItemPath).TrimEnd(Path.DirectorySeparatorChar))
        If Not String.Equals(Parent, Path.GetFullPath(Root).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase) Then Throw New IOException("所选项目不在当前内容目录中。")
        If (File.GetAttributes(ItemPath) And FileAttributes.ReparsePoint) <> 0 Then Throw New IOException("不支持对链接目录执行此操作。")
    End Sub

    Public Shared Function UniquePath(Root As String, Name As String) As String
        Dim Candidate = Path.Combine(Root, Name)
        Dim Number = 2
        While File.Exists(Candidate) OrElse Directory.Exists(Candidate)
            Candidate = Path.Combine(Root, Path.GetFileNameWithoutExtension(Name) & " (" & Number & ")" & Path.GetExtension(Name))
            Number += 1
        End While
        Return Candidate
    End Function

    Public Shared Sub CopyFolder(Source As String, Destination As String)
        Dim SourceFull = Path.GetFullPath(Source).TrimEnd(Path.DirectorySeparatorChar) & Path.DirectorySeparatorChar
        Dim DestinationFull = Path.GetFullPath(Destination).TrimEnd(Path.DirectorySeparatorChar) & Path.DirectorySeparatorChar
        If DestinationFull.StartsWith(SourceFull, StringComparison.OrdinalIgnoreCase) Then Throw New IOException("不能将文件夹复制到它自己或它的子目录中。")
        If (File.GetAttributes(Source) And FileAttributes.ReparsePoint) <> 0 Then Throw New IOException("导入内容包含链接目录。")
        Directory.CreateDirectory(Destination)
        For Each Item In New DirectoryInfo(Source).EnumerateFileSystemInfos()
            If (Item.Attributes And FileAttributes.ReparsePoint) <> 0 Then Throw New IOException("导入内容包含链接文件或目录。")
            Dim Target = Path.Combine(Destination, Item.Name)
            If TypeOf Item Is DirectoryInfo Then
                CopyFolder(Item.FullName, Target)
            Else
                File.Copy(Item.FullName, Target, False)
            End If
        Next
    End Sub

    Public Shared Function ImportItem(Root As String, Source As String, Kind As InstanceContentKind) As String
        Directory.CreateDirectory(Root)
        If Directory.Exists(Source) Then
            If Kind = InstanceContentKind.Saves AndAlso Not File.Exists(Path.Combine(Source, "level.dat")) Then Throw New IOException("请选择包含 level.dat 的存档文件夹。")
            Dim Target = UniquePath(Root, New DirectoryInfo(Source).Name)
            Dim SourceFull = Path.GetFullPath(Source).TrimEnd("\"c) & "\"
            If Path.GetFullPath(Target).StartsWith(SourceFull, StringComparison.OrdinalIgnoreCase) Then Throw New IOException("不能将文件夹导入到它自己的子目录中。")
            Dim Staging = Path.Combine(Path.GetFullPath(Root), ".brcl-import-" & Guid.NewGuid().ToString("N"))
            Try
                CopyFolder(Source, Staging)
                Directory.Move(Staging, Target)
            Finally
                If Directory.Exists(Staging) Then Directory.Delete(Staging, True)
            End Try
            Return Target
        End If
        Dim Extension = Path.GetExtension(Source).ToLowerInvariant()
        If Kind = InstanceContentKind.Saves Then Return ImportSaveZip(Root, Source)
        Dim Allowed = If(Kind = InstanceContentKind.Schematics, {".nbt"}, If(Kind = InstanceContentKind.Screenshots, {".png", ".jpg", ".jpeg", ".webp"}, {".zip"}))
        If Not Allowed.Contains(Extension) Then Throw New IOException("所选文件类型不适用于此分类。")
        Dim Destination = UniquePath(Root, Path.GetFileName(Source))
        File.Copy(Source, Destination, False)
        Return Destination
    End Function

    Private Shared Function ImportSaveZip(Root As String, Source As String) As String
        If Not Path.GetExtension(Source).Equals(".zip", StringComparison.OrdinalIgnoreCase) Then Throw New IOException("存档导入支持 ZIP 压缩包或存档文件夹。")
        Dim Staging = Path.Combine(Path.GetFullPath(Root), ".brcl-import-" & Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(Staging)
        Try
            Using Archive = ZipFile.OpenRead(Source)
                If Archive.Entries.Count > 100000 OrElse Archive.Entries.Sum(Function(Entry) Entry.Length) > 17179869184L Then Throw New IOException("压缩包过大，请解压后导入存档文件夹。")
                For Each Entry In Archive.Entries
                    Dim Relative = Entry.FullName.Replace("/"c, "\"c)
                    Dim Target = Path.GetFullPath(Path.Combine(Staging, Relative))
                    If Relative.Contains(":"c) OrElse Not Target.StartsWith(Staging & "\", StringComparison.OrdinalIgnoreCase) Then Throw New IOException("压缩包包含不安全的路径。")
                    If String.IsNullOrEmpty(Entry.Name) Then
                        Directory.CreateDirectory(Target)
                    Else
                        Directory.CreateDirectory(Path.GetDirectoryName(Target))
                        Entry.ExtractToFile(Target, False)
                    End If
                Next
            End Using
            Dim SaveFolder = Staging
            If Not File.Exists(Path.Combine(SaveFolder, "level.dat")) Then
                Dim Candidates = Directory.GetDirectories(Staging).Where(Function(Folder) File.Exists(Path.Combine(Folder, "level.dat"))).ToList()
                If Candidates.Count <> 1 Then Throw New IOException("压缩包内未找到唯一的存档，请解压后选择包含 level.dat 的文件夹。")
                SaveFolder = Candidates(0)
            End If
            Dim Destination = UniquePath(Root, Path.GetFileNameWithoutExtension(Source))
            Directory.Move(SaveFolder, Destination)
            Return Destination
        Finally
            'Staging 始终是本方法在 Root 内创建的随机临时目录。
            If Directory.Exists(Staging) Then Directory.Delete(Staging, True)
        End Try
    End Function

    Public Shared Function RenameItem(Root As String, ItemPath As String, NewName As String) As String
        ValidateChild(Root, ItemPath)
        If String.IsNullOrWhiteSpace(NewName) OrElse NewName <> NewName.Trim() OrElse NewName.EndsWith(".") OrElse NewName = "." OrElse NewName = ".." OrElse NewName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 Then Throw New IOException("名称不能为空，也不能包含路径符号或以空格、句点结尾。")
        Dim Target = Path.Combine(Root, NewName)
        If String.Equals(Path.GetFullPath(Target), Path.GetFullPath(ItemPath), StringComparison.OrdinalIgnoreCase) Then Return ItemPath
        If Directory.Exists(ItemPath) Then
            Directory.Move(ItemPath, Target)
        Else
            File.Move(ItemPath, Target)
        End If
        Return Target
    End Function
End Class
