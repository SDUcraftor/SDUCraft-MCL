Imports System.IO
Imports System.IO.Compression

''' <summary>
''' SDUcraft 整合包适配器：先检测官网包结构，再按结构分派安装。
''' 已知结构（自动安装）：
'''  - mrpack/Modrinth（modrinth.index.json，根或一级目录）：交给 ModpackInstall。
'''  - 带启动器压缩包（modpack.mrpack / modpack.zip）：提取内层 mrpack 后交给 ModpackInstall。
'''  - CurseForge/MCBBS/HMCL/MMC（manifest.json、mcbbs.packmeta、modpack.json、mmc-pack.json）：交给 ModpackInstall 自动识别。
'''  - 裸客户端整包：首次安装暂存后发布隔离实例；已有实例仅同步 mods，并备份旧 mods。
'''  - 纯资源包（pack.mcmeta + pack.png）：拒绝安装。
''' 未知结构：把下载包复制到 PCL 目录下的 SduDownload 文件夹并打开资源管理器，让用户手动安装。
''' 此函数必须在工作线程中调用。
''' </summary>
Public Module SduModpackAdapter

    ''' <summary>
    ''' 检测整合包结构并执行安装。Task 用于反馈安装进度（0~1）。
    ''' </summary>
    Public Sub AdaptAndInstall(DownloadedFile As String, Item As SduGameDownload, Task As LoaderTask(Of Integer, Integer), InstanceName As String, McFolder As String)
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
                InstallKnownPack(DownloadedFile, InstanceName, McFolder, Task)
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
                InstallKnownPack(MrpackPath, InstanceName, McFolder, Task)
                Return
            End If

            '------------------------------------------------------------------
            ' 3. 其他已知整合包格式，交给 ModpackInstall 自动识别
            '------------------------------------------------------------------
            If HasMarker(Entries, "mcbbs.packmeta", "manifest.json", "mmc-pack.json", "modpack.json") Then
                Logger.Info("检测到 CurseForge/MCBBS/HMCL/MMC 类整合包，交由 ModpackInstall 识别安装")
                Archive.Dispose()
                Archive = Nothing
                InstallKnownPack(DownloadedFile, InstanceName, McFolder, Task)
                Return
            End If

            '------------------------------------------------------------------
            ' 4. 裸客户端整包（.minecraft/versions/<名>/<名>.json）
            '------------------------------------------------------------------
            Dim Layout = SduBareClientInstaller.Detect(Archive)
            If Layout IsNot Nothing Then
                If McInstanceLock.IsLocked(New McInstance(Path.Combine(McFolder, "versions", InstanceName))) Then
                    Throw New IOException("该实例正在启动或运行，请关闭游戏后再更新模组。")
                End If
                Logger.Info($"检测到裸客户端整包，原版本：{Layout.VersionName}，安装实例：{InstanceName}")
                Dim Result = SduBareClientInstaller.Install(Archive, Layout, McFolder, InstanceName, Task.CreateCancellationToken(),
                    Sub(VersionStage)
                        Dim IniPath = VersionStage & "PCL\Setup.ini"
                        WriteIni(IniPath, "VersionArgumentIndie", 1)
                        WriteIni(IniPath, "VersionArgumentIndieV2", True)
                        IniClearCache(IniPath)
                        Dim Marker As New JObject From {{"sduProjectId", Item.Id}}
                        File.WriteAllText(VersionStage & "PCL\SduPack.json", Marker.ToString(), New System.Text.UTF8Encoding(False))
                    End Sub, Sub(Value) Task.Progress = Value)
                If Result.UpdatedMods Then
                    Logger.Info($"已仅更新 {InstanceName} 的 mods，旧模组备份：{If(Result.ModsBackup, "原实例没有 mods 目录")}")
                    Hint($"{InstanceName} 的模组已更新。" & If(Result.ModsBackup Is Nothing, "", vbCrLf & "旧模组备份：" & Result.ModsBackup), HintType.Blue)
                Else
                    Logger.Info($"裸客户端已安装至 {Result.InstanceFolder}（已开启版本隔离）")
                End If
                LoaderFolderRun(McInstanceListLoader, McFolder, LoaderFolderRunType.ForceRun, MaxDepth:=1, ExtraPath:="versions\", WaitForExit:=True)
                Logger.Info($"版本列表已刷新，{InstanceName} 已可用")
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
            ElseIf TypeOf ex Is InvalidDataException Then
                Throw New Exception("整合包无法自动安装：" & ex.Message, ex)
            ElseIf ex.Message.Contains("Error.WinIOError") Then
                Throw New Exception("整合包文件已损坏或不是 zip/mrpack 格式：" & PathUtils.GetLastPart(DownloadedFile), ex)
            End If
            Throw
        Finally
            If Archive IsNot Nothing Then
                Try : Archive.Dispose() : Catch : End Try
            End If
        End Try
    End Sub

    Private Sub InstallKnownPack(FilePath As String, InstanceName As String, McFolder As String, Task As LoaderTask(Of Integer, Integer))
        Dim Target = Path.Combine(McFolder, "versions", InstanceName)
        '标准格式的官方安装器会复制 overrides/配置及重建游戏文件，不能当成“仅更新 mods”。
        If Directory.Exists(Target) OrElse File.Exists(Target) Then
            Throw New IOException("此格式暂不支持仅更新 mods，已停止安装以保留现有实例。请手动更新 mods，或先重命名旧实例后全新安装：" & InstanceName)
        End If
        If Not PathUtils.IsSame(McFolder, McFolderSelected) Then Throw New IOException("安装期间切换了 Minecraft 文件夹，请重新发起安装。")
        SduLoaderBridge.WaitForCompletion(ModpackInstall(FilePath, InstanceName), Task)
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

#End Region

#Region "未知结构手动安装"

    ''' <summary>
    ''' 未识别结构：把下载包复制到 PCL 目录下 SduDownload 文件夹，打开资源管理器提示用户手动安装。
    ''' </summary>
    Private Sub OpenManualInstallFolder(DownloadedFile As String, Item As SduGameDownload)
        SduModpackInstaller.OpenManualInstallFolder(DownloadedFile,
            SduModpackInstaller.CreateSduInstanceName(Item),
            $"未能自动识别 {Item.Name} 的包结构。")
    End Sub

#End Region

End Module
