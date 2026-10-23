Imports System.IO
Imports System.IO.Compression

''' <summary>
''' SDUcraft 整合包适配器：先检测官网包结构，再按结构分派安装。
''' 已知结构（自动安装）：
'''  - mrpack/Modrinth（modrinth.index.json，根或一级目录）：交给 ModpackInstall。
'''  - 带启动器压缩包（modpack.mrpack / modpack.zip）：提取后再次识别内层包的实际格式。
'''  - CurseForge/MCBBS/HMCL/MMC（manifest.json、mcbbs.packmeta、modpack.json、mmc-pack.json）：交给 ModpackInstall 自动识别。
'''  - 所有支持格式：首次完整安装；已有实例按模组 ID 合并 mods，保留独有内容并备份旧 mods。
'''  - 纯资源包（pack.mcmeta + pack.png）：拒绝安装。
''' 未知结构：把下载包复制到 PCL 目录下的 SduDownload 文件夹并打开资源管理器，让用户手动安装。
''' 此函数必须在工作线程中调用。
''' </summary>
Public Module SduModpackAdapter

    ''' <summary>
    ''' 检测整合包结构并执行安装。Task 用于反馈安装进度（0~1）。
    ''' </summary>
    Public Sub AdaptAndInstall(DownloadedFile As String, Item As SduGameDownload, Task As LoaderTask(Of Integer, Integer), InstanceName As String, McFolder As String,
                               Optional AllowDuringLaunch As Boolean = False)
        SduBareClientInstaller.ValidateSegment(InstanceName)
        Using Reservation = SduPackUpdate.Reserve(Path.Combine(McFolder, "versions", InstanceName), AllowDuringLaunch)
            AdaptPackage(DownloadedFile, Item, Task, InstanceName, McFolder, 0)
        End Using
    End Sub

    Private Sub AdaptPackage(DownloadedFile As String, Item As SduGameDownload, Task As LoaderTask(Of Integer, Integer), InstanceName As String, McFolder As String, Depth As Integer)
        If Depth > 4 Then Throw New InvalidDataException("整合包嵌套层数过多。")
        Logger.Info($"SDUcraft 整合包适配：{Item.Name} → 实例 {InstanceName}（{DownloadedFile}）")

        Dim Archive As ZipArchive = Nothing
        Try
            Archive = FileUtils.OpenZip(DownloadedFile)

            '------------------------------------------------------------------
            ' 0. mrpack/Modrinth 格式（根或一级目录）
            '------------------------------------------------------------------
            If SduPackageFiles.FindMarker(Archive, "modrinth.index.json") IsNot Nothing Then
                Logger.Info("检测到 Modrinth/mrpack 整合包，直接安装")
                Archive.Dispose()
                Archive = Nothing
                InstallKnownPack(DownloadedFile, InstanceName, McFolder, Task, Item)
                Return
            End If

            '------------------------------------------------------------------
            ' 1. 纯资源包
            '------------------------------------------------------------------
            If SduPackageFiles.FindEntry(Archive, "pack.mcmeta") IsNot Nothing AndAlso SduPackageFiles.FindEntry(Archive, "pack.png") IsNot Nothing Then
                Throw New Exception("该文件是一个 Minecraft 资源包，不是整合包，无法安装。请选择其他文件。")
            End If

            '------------------------------------------------------------------
            ' 2. 带启动器的压缩包（内含 modpack.mrpack 或 modpack.zip）
            '------------------------------------------------------------------
            Dim InnerEntry = SduPackageFiles.FindMarker(Archive, "modpack.mrpack", "modpack.zip")
            If InnerEntry IsNot Nothing Then
                Logger.Info($"检测到带启动器的整合包，提取内层 {InnerEntry.FullName}")
                Dim TempFolder As String = RequestTaskTempFolder()
                Dim MrpackPath As String = TempFolder & "inner-pack" & Path.GetExtension(InnerEntry.FullName)
                Using EntryStream As Stream = InnerEntry.Open()
                    FileUtils.Write(MrpackPath, EntryStream)
                End Using
                Archive.Dispose()
                Archive = Nothing
                Logger.Info($"内层整合包已提取至：{MrpackPath}")
                AdaptPackage(MrpackPath, Item, Task, InstanceName, McFolder, Depth + 1)
                Return
            End If

            '------------------------------------------------------------------
            ' 3. 其他已知整合包格式，交给 ModpackInstall 自动识别
            '------------------------------------------------------------------
            If SduPackageFiles.FindMarker(Archive, "mcbbs.packmeta", "manifest.json", "mmc-pack.json", "modpack.json") IsNot Nothing Then
                Logger.Info("检测到 CurseForge/MCBBS/HMCL/MMC 类整合包，交由 ModpackInstall 识别安装")
                Archive.Dispose()
                Archive = Nothing
                InstallKnownPack(DownloadedFile, InstanceName, McFolder, Task, Item)
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
                        Dim IniPath = SduFileSystem.ToIoPath(VersionStage & "PCL\Setup.ini")
                        WriteIni(IniPath, "VersionArgumentIndie", 1)
                        WriteIni(IniPath, "VersionArgumentIndieV2", True)
                        IniClearCache(IniPath)
                        SduPackRecord.Save(VersionStage, Item)
                    End Sub, Sub(Value) Task.Progress = Value)
                If Result.UpdatedMods Then SduPackRecord.Save(Result.InstanceFolder, Item)
                If Result.UpdatedMods Then
                    Logger.Info($"已仅更新 {InstanceName} 的 mods，旧模组备份：{If(Result.ModsBackup, "模组未变化或原实例没有 mods 目录")}")
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
            Throw New SduManualInstallRequiredException($"未能自动识别 {Item.Name} 的包结构，需要手动安装。")
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

    Private Sub InstallKnownPack(FilePath As String, InstanceName As String, McFolder As String, Task As LoaderTask(Of Integer, Integer), Item As SduGameDownload)
        Dim Target = Path.Combine(McFolder, "versions", InstanceName)
        If Not PathUtils.IsSame(McFolder, McFolderSelected) Then Throw New IOException("安装期间切换了 Minecraft 文件夹，请重新发起安装。")
        If SduFileSystem.File.Exists(Target) Then Throw New IOException("实例路径被文件占用。")
        If SduFileSystem.Directory.Exists(Target) Then
            '各格式安装器只在独立目录准备模组，不安装游戏本体或写入原实例。
            Dim Staging = Path.Combine(RequestTaskTempFolder(), "sdu-update-" & Guid.NewGuid().ToString("N"))
            SduFileSystem.Directory.CreateDirectory(Staging)
            Try
                SduLoaderBridge.WaitForCompletion(ModpackInstall(FilePath, InstanceName, ShowNotifications:=False, PreparationFolder:=Staging), Task)
                Dim Result = SduModMerge.Apply(McFolder, Target, Path.Combine(Staging, "mods"), Task.CreateCancellationToken())
                Logger.Info("整合包模组已合并，旧模组备份：" & If(Result.ModsBackup, "无旧模组"))
            Finally
                Try
                    If SduFileSystem.Directory.Exists(Staging) Then SduFileSystem.Directory.Delete(Staging, True)
                Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException
                    Logger.Warn(ex, "清理整合包更新暂存目录失败")
                End Try
            End Try
        Else
            SduLoaderBridge.WaitForCompletion(ModpackInstall(FilePath, InstanceName, ShowNotifications:=False), Task)
            If Not SduFileSystem.File.Exists(Path.Combine(Target, InstanceName & ".json")) Then Throw New IOException("安装器未生成预期实例。")
            Dim Ini = SduFileSystem.ToIoPath(Path.Combine(Target, "PCL", "Setup.ini"))
            WriteIni(Ini, "VersionArgumentIndie", 1)
            WriteIni(Ini, "VersionArgumentIndieV2", True)
            IniClearCache(Ini)
        End If
        SduPackRecord.Save(Target, Item)
        LoaderFolderRun(McInstanceListLoader, McFolder, LoaderFolderRunType.ForceRun, MaxDepth:=1, ExtraPath:="versions\", WaitForExit:=True)
    End Sub

End Module
