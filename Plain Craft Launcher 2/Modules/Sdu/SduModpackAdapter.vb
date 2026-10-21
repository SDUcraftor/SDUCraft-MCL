Imports System.IO
Imports System.IO.Compression
Imports System.Text.RegularExpressions

''' <summary>
''' SDUcraft 整合包适配器：先检测官网包结构，再按结构分派安装。
''' 已知结构（自动安装）：
'''  - mrpack/Modrinth（modrinth.index.json，根或一级目录）：交给 ModpackInstall。
'''  - 带启动器压缩包（modpack.mrpack / modpack.zip）：提取内层 mrpack 后交给 ModpackInstall。
'''  - CurseForge/MCBBS/HMCL/MMC（manifest.json、mcbbs.packmeta、modpack.json、mmc-pack.json）：交给 ModpackInstall 自动识别。
'''  - 裸客户端整包（含 .minecraft/versions/名/名.json）：直接解压进默认 .minecraft，写入 PCL 版本隔离配置。
'''  - 纯资源包（pack.mcmeta + pack.png）：拒绝安装。
''' 未知结构：把下载包复制到 PCL 目录下的 SduDownload 文件夹并打开资源管理器，让用户手动安装。
''' 此函数必须在工作线程中调用。
''' </summary>
Public Module SduModpackAdapter

    ''' <summary>
    ''' 检测整合包结构并执行安装。Task 用于反馈安装进度（0~1）。
    ''' </summary>
    Public Sub AdaptAndInstall(DownloadedFile As String, Item As SduGameDownload, Task As LoaderTask(Of Integer, Integer))
        Dim InstanceName As String = CreateSduInstanceName(Item)
        Logger.Info($"SDUcraft 整合包适配：{Item.Name} → 实例 {InstanceName}（{DownloadedFile}）")

        Dim Archive As ZipArchive = Nothing
        Try
            Archive = FileUtils.OpenZip(DownloadedFile)

            '收集条目（目录分隔符统一为 /）
            Dim Entries As New List(Of String)
            For Each Entry As ZipArchiveEntry In Archive.Entries
                Entries.Add(Entry.FullName.Replace("\"c, "/"c))
            Next

            '------------------------------------------------------------------
            ' 0. mrpack/Modrinth 格式（根或一级目录）
            '------------------------------------------------------------------
            If HasMarker(Entries, "modrinth.index.json") Then
                Logger.Info("检测到 Modrinth/mrpack 整合包，直接安装")
                Archive.Dispose()
                Archive = Nothing
                ModpackInstall(DownloadedFile, InstanceName)
                Return
            End If

            '------------------------------------------------------------------
            ' 1. 纯资源包
            '------------------------------------------------------------------
            If ContainsRoot(Entries, "pack.mcmeta") AndAlso ContainsRoot(Entries, "pack.png") Then
                Throw New Exception("该文件是一个 Minecraft 资源包，不是整合包，无法安装。请选择其他文件。")
            End If

            '------------------------------------------------------------------
            ' 2. 带启动器的压缩包（内含 modpack.mrpack 或 modpack.zip）
            '------------------------------------------------------------------
            Dim InnerEntryName As String = FindMarker(Entries, "modpack.mrpack", "modpack.zip")
            If InnerEntryName <> "" Then
                Logger.Info($"检测到带启动器的整合包，提取内层 {InnerEntryName}")
                Dim TempFolder As String = RequestTaskTempFolder()
                Dim MrpackPath As String = TempFolder & "modpack.mrpack"
                Using EntryStream As Stream = Archive.GetEntry(InnerEntryName).Open()
                    FileUtils.Write(MrpackPath, EntryStream)
                End Using
                Archive.Dispose()
                Archive = Nothing
                Logger.Info($"内层 modpack.mrpack 已提取至：{MrpackPath}")
                ModpackInstall(MrpackPath, InstanceName)
                Return
            End If

            '------------------------------------------------------------------
            ' 3. 其他已知整合包格式，交给 ModpackInstall 自动识别
            '------------------------------------------------------------------
            If HasMarker(Entries, "mcbbs.packmeta", "manifest.json", "mmc-pack.json", "modpack.json") Then
                Logger.Info("检测到 CurseForge/MCBBS/HMCL/MMC 类整合包，交由 ModpackInstall 识别安装")
                Archive.Dispose()
                Archive = Nothing
                ModpackInstall(DownloadedFile, InstanceName)
                Return
            End If

            '------------------------------------------------------------------
            ' 4. 裸客户端整包（.minecraft/versions/<名>/<名>.json）
            '------------------------------------------------------------------
            Dim McBase As String = FindMcBase(Entries)
            If McBase <> "" Then
                Dim VersionName As String = GetVersionNameFromMcBase(Entries)
                Logger.Info($"检测到裸客户端整包，.minecraft 前缀：{McBase}，版本名：{VersionName}")
                InstallBareClientToDefaultMc(Archive, McBase, VersionName, Task)
                Return
            End If

            '------------------------------------------------------------------
            ' 5. 未识别的结构：打开文件夹，让用户手动安装
            '------------------------------------------------------------------
            Logger.Warn("整合包结构未识别，打开下载文件所在文件夹供用户手动安装")
            Archive.Dispose()
            Archive = Nothing
            OpenManualInstallFolder(DownloadedFile, Item)
        Catch ex As Exception
            If ex.IsCanceled Then
                Throw
            ElseIf ex.Message.Contains("Error.WinIOError") OrElse TypeOf ex Is InvalidDataException Then
                Throw New Exception("整合包文件已损坏或不是 zip/mrpack 格式：" & PathUtils.GetLastPart(DownloadedFile), ex)
            End If
            Throw
        Finally
            If Archive IsNot Nothing Then
                Try : Archive.Dispose() : Catch : End Try
            End If
        End Try
    End Sub

#Region "结构检测"

    ''' <summary>在条目集合中查找关键文件（允许位于根或一级包裹目录），返回命中条目名。</summary>
    Private Function FindMarker(Entries As List(Of String), ParamArray Markers As String()) As String
        For Each Marker In Markers
            If Entries.Contains(Marker) Then Return Marker
            For Each Entry In Entries
                Dim Parts As String() = Entry.Split("/"c)
                If Parts.Length = 2 AndAlso Parts(1).Equals(Marker, StringComparison.OrdinalIgnoreCase) Then
                    Return Entry
                End If
            Next
        Next
        Return ""
    End Function

    ''' <summary>是否存在任一关键文件（根或一级目录）。</summary>
    Private Function HasMarker(Entries As List(Of String), ParamArray Markers As String()) As Boolean
        Return FindMarker(Entries, Markers) <> ""
    End Function

    ''' <summary>文件是否直接位于压缩包根目录。</summary>
    Private Function ContainsRoot(Entries As List(Of String), FileName As String) As Boolean
        Return Entries.Any(Function(Entry) Entry.Equals(FileName, StringComparison.OrdinalIgnoreCase))
    End Function

    ''' <summary>
    ''' 查找 .minecraft 根目录前缀：匹配形如 “包裹/.minecraft/versions/名/名.json” 的条目。
    ''' 返回 .minecraft 目录在压缩包内的前缀（如 “SDUcraft_4thclient/.minecraft/”），未找到返回空串。
    ''' </summary>
    Private Function FindMcBase(Entries As List(Of String)) As String
        Dim Regex As New Regex("^.*\/(?=versions\/(?<ver>[^\/]+)\/(\k<ver>)\.json$)", RegexOptions.IgnoreCase)
        For Each Entry In Entries
            Dim Match As Match = Regex.Match("/" & Entry)
            If Match.Success Then Return Match.Value.TrimStart("/"c)
        Next
        Return ""
    End Function

    ''' <summary>从 .minecraft 前缀命中的条目中提取版本文件夹名。</summary>
    Private Function GetVersionNameFromMcBase(Entries As List(Of String)) As String
        Dim Regex As New Regex("^.*\/(?=versions\/(?<ver>[^\/]+)\/(\k<ver>)\.json$)", RegexOptions.IgnoreCase)
        For Each Entry In Entries
            Dim Match As Match = Regex.Match("/" & Entry)
            If Match.Success Then Return Match.Groups("ver").Value
        Next
        Return ""
    End Function

#End Region

#Region "裸客户端整包自动安装"

    ''' <summary>
    ''' 把裸客户端整包解压进当前默认 Minecraft 目录（.minecraft）：
    '''  - versions/名/ 完整解压到 McFolder\versions\名\（如已存在同名版本则删除后重装）；
    '''  - 其余内容（libraries、assets、mods、config 等）合并复制，已存在的文件不覆盖；
    '''  - 写入 PCL\Setup.ini 开启版本隔离。
    ''' </summary>
    Private Sub InstallBareClientToDefaultMc(Archive As ZipArchive, McBase As String, VersionName As String, Task As LoaderTask(Of Integer, Integer))
        Dim McFolder As String = PathUtils.AddSlashSuffix(McFolderSelected)
        Dim TargetVersion As String = McFolder & "versions\" & VersionName & "\"
        Dim VersionPrefix As String = McBase & "versions/" & VersionName & "/"

        '删除已存在的同名版本，保证重装时不会残留旧文件
        If DirectoryUtils.Exists(TargetVersion) Then
            Logger.Info($"删除已存在的版本文件夹：{TargetVersion}")
            DirectoryUtils.Delete(TargetVersion)
        End If

        '计划复制：版本文件夹（覆盖式）+ 其余内容（合并式，跳过已存在文件）
        Dim Files As New List(Of ZipArchiveEntry)
        For Each Entry As ZipArchiveEntry In Archive.Entries
            Dim Name As String = Entry.FullName.Replace("\"c, "/"c)
            If Name.EndsWith("/") Then Continue For
            If Name.StartsWith(McBase & "versions/", StringComparison.OrdinalIgnoreCase) AndAlso
               Not Name.StartsWith(VersionPrefix, StringComparison.OrdinalIgnoreCase) Then
                '其他版本目录不复制
                Continue For
            End If
            If Name.StartsWith(McBase, StringComparison.OrdinalIgnoreCase) Then Files.Add(Entry)
        Next

        Dim TotalBytes As Long = 0
        For Each Entry In Files
            TotalBytes += Math.Max(0, Entry.Length)
        Next
        Logger.Info($"裸客户端安装：{Files.Count} 个文件，共 {TotalBytes / 1024 / 1024:0.##} MB")

        Dim Token As CancellationToken = Task.CreateCancellationToken()
        Dim DoneBytes As Long = 0
        For Each Entry In Files
            Token.ThrowIfCancellationRequested()

            Dim Name As String = Entry.FullName.Replace("\"c, "/"c)
            Dim Relative As String = Name.Substring(McBase.Length).Replace("/"c, "\"c)
            Dim IsVersion As Boolean = Name.StartsWith(VersionPrefix, StringComparison.OrdinalIgnoreCase)
            Dim Target As String
            If IsVersion Then
                Target = TargetVersion & Relative.Substring(("versions/" & VersionName & "/").Length)
            Else
                Target = McFolder & Relative
            End If

            '合并模式：目标文件已存在则跳过（不覆盖用户已有文件）
            If Not IsVersion AndAlso FileUtils.Exists(Target) Then Continue For

            DirectoryUtils.Create(PathUtils.RemoveLastPart(Target))
            Using Stream As Stream = Entry.Open()
                FileUtils.Write(Target, Stream)
            End Using

            DoneBytes += Entry.Length
            If TotalBytes > 0 Then Task.Progress = DoneBytes / TotalBytes
        Next

        '开启版本隔离
        DirectoryUtils.Create(TargetVersion & "PCL\")
        Dim IniPath As String = TargetVersion & "PCL\Setup.ini"
        WriteIni(IniPath, "VersionArgumentIndie", 1)
        WriteIni(IniPath, "VersionArgumentIndieV2", True)
        Logger.Info($"裸客户端已安装至 {TargetVersion}（已开启版本隔离）")

        '刷新版本列表
        LoaderFolderRun(McInstanceListLoader, McFolderSelected, LoaderFolderRunType.ForceRun, MaxDepth:=1, ExtraPath:="versions\", WaitForExit:=True)
        Logger.Info($"版本列表已刷新，{VersionName} 已可用")
    End Sub

#End Region

#Region "未知结构手动安装"

    ''' <summary>
    ''' 未识别结构：把下载包复制到 PCL 目录下 SduDownload 文件夹，打开资源管理器提示用户手动安装。
    ''' </summary>
    Private Sub OpenManualInstallFolder(DownloadedFile As String, Item As SduGameDownload)
        Dim Folder As String = Paths.Base & "SduDownload\"
        DirectoryUtils.Create(Folder)
        Dim FileName As String = CreateSduInstanceName(Item) & PathUtils.GetExtension(DownloadedFile)
        Dim Target As String = Folder & FileName
        If Not FileUtils.Exists(Target) Then FileUtils.Copy(DownloadedFile, Target)
        Logger.Info($"已复制整合包到手动安装目录：{Target}")
        OpenExplorer(Folder)
        Hint($"未能自动识别 {Item.Name} 的包结构，已打开下载文件夹，请手动将其拖入启动器安装。", HintType.Blue)
    End Sub

#End Region

    ''' <summary>
    ''' 生成 SDUcraft 实例名（与 PageDownloadSdu 中一致）。
    ''' </summary>
    Private Function CreateSduInstanceName(Item As SduGameDownload) As String
        Dim Name As String = Item.Name.Trim()
        If Item.Version <> "" AndAlso Not Name.Contains(Item.Version) Then
            Name &= " (" & Item.Version & ")"
        End If
        Name = System.Text.RegularExpressions.Regex.Replace(Name, "[\\/:*?""<>|!;\r\n\t]", "_").Trim("_"c, " "c)
        If Name.Length > 64 Then Name = Name.Substring(0, 64).Trim("_"c, " "c)
        If Name = "" Then Name = $"SDUcraft-{If(Item.Id = "", "Pack", Item.Id)}"
        Return Name
    End Function

End Module