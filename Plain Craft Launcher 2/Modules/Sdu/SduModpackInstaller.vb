''' <summary>
''' SDUcraft 整合包一键下载安装器。
''' 首页速览面板与内置下载页共用：先下载官网直链（进度显示），再交给适配器检测结构并安装。
''' 必须在工作线程中调用。
''' </summary>
Public Module SduModpackInstaller

    ''' <summary>
    ''' 启动整合包下载并安装，并等待整个流程结束。必须在工作线程中调用。
    ''' 下载与安装进度显示在任务栏加载器中，失败和取消会抛回调用方。
    ''' </summary>
    Public Sub StartInstall(Item As SduGameDownload, Optional TargetMcFolder As String = Nothing, Optional TargetInstanceName As String = Nothing,
                            Optional Token As System.Threading.CancellationToken = Nothing, Optional FromLaunch As Boolean = False)
        Dim DownloadFile As SduGameDownloadFile = Item.PreferredFile
        If DownloadFile Is Nothing Then
            Throw New Exception("该整合包没有可用的 Windows 下载文件，无法安装。")
        End If

        'GameId 只保留文件名安全字符（API 返回的 id 含冒号时会导致写盘失败或异常路径）
        Dim GameId As String = System.Text.RegularExpressions.Regex.Replace(If(Item.Id, ""), "[^A-Za-z0-9_.-]", "_")
        If GameId = "" Then GameId = GetUuid().ToString("N")
        Dim McFolder As String = PathUtils.AddSlashSuffix(If(TargetMcFolder, McFolderSelected))
        Dim InstanceName As String = If(TargetInstanceName, SduBareClientInstaller.ResolveInstanceName(McFolder, Item.Id, CreateSduInstanceName(Item)))

        Dim LoaderName As String = $"SDUcraft 整合包安装：{InstanceName} "

        If LoaderTaskbar.Any(Function(l) l.Name = LoaderName) Then
            Hint("该整合包正在安装中！", HintType.Red)
            Return
        End If

        '启动前检查已经询问过“更新并启动”；下载页、首页及手动导入则先确认已有实例。
        If Not FromLaunch AndAlso Not ConfirmUpdate(McFolder, InstanceName) Then Return
        Token.ThrowIfCancellationRequested()

        '确认后才创建临时目录和下载任务。
        Dim Extension As String = If(DownloadFile.FileUrl.EndsWithF(".mrpack", True), "mrpack", "zip")
        Dim TempFolder As String = RequestTaskTempFolder()
        Dim LocalFile As String = $"{TempFolder}SDUcraft-{GameId}.{Extension}"

        ' 构建步骤加载器（显示进度）
        Dim Loaders As New List(Of LoaderBase)

        ' 步骤 1：下载整合包文件（有进度条）
        Loaders.Add(New LoaderDownload("下载整合包文件", {
            New NetFile({DownloadFile.FileUrl}, LocalFile,
                        New FileChecker With {.MinSize = 10 * 1024})
        }) With {.ProgressWeight = 10, .Block = True})

        ' 步骤 2：检测包结构并安装（适配器按结构分派，进度由 Task 反馈）
        Loaders.Add(New LoaderTask(Of Integer, Integer)("安装整合包",
        Sub(Task)
            SduModpackAdapter.AdaptAndInstall(LocalFile, Item, Task, InstanceName, McFolder, FromLaunch)
        End Sub) With {.ProgressWeight = 10})

        ' 启动加载器，显示进度到任务栏
        '目标目录由实际子安装器管理；外层统一等待、通知，不重复清理目录。
        Dim Loader As New LoaderCombo(Of String)(LoaderName, Loaders)
        ' 安装到默认 .minecraft/versions/<实例名> 目录，由 PCL 自动版本隔离
        Loader.Start(McFolder & "versions\" & InstanceName & "\")

        ' UI 操作必须在 UI 线程执行
        RunInUi(Sub()
            LoaderTaskbarAdd(Loader)
            FrmMain.BtnExtraDownload.ShowRefresh()
        End Sub)
        If Not FromLaunch Then RunInUi(Sub() FrmMain.PageChange(FormMain.PageType.TaskManager))

        '不能在任务刚加入列表后返回，否则两个入口会过早释放“安装中”状态。
        Try
            SduLoaderBridge.WaitForCompletion(Loader, ExternalToken:=Token)
            Hint(LoaderName & "成功！", HintType.Green)
        Catch ex As Exception
            If ex.IsCanceled Then
                Hint(LoaderName & "已取消！", HintType.Blue)
                Throw
            End If
            Logger.Warn(ex, "SDUcraft 自动安装未完成")
            Dim ManualRequired = TypeOf ex.GetBaseException() Is SduManualInstallRequiredException
            Dim Message = If(ManualRequired, ex.GetBaseException().Message,
                $"{InstanceName} 自动安装失败：{ex.GetDisplay(False)}")
            Try
                OpenManualInstallFolder(LocalFile, InstanceName, Message, IsFailure:=Not ManualRequired)
            Catch ExportError As Exception
                Logger.Warn(ExportError, "导出手动安装包失败")
                Hint(Message & vbCrLf & "导出失败，原始下载文件：" & LocalFile, HintType.Red)
            End Try
            Throw New SduInstallReportedException(ex)
        End Try
    End Sub

    Friend Function ConfirmUpdate(McFolder As String, InstanceName As String, Optional Ask As Func(Of String, Integer) = Nothing) As Boolean
        Dim Target = System.IO.Path.Combine(McFolder, "versions", InstanceName)
        If Not SduFileSystem.Directory.Exists(Target) Then Return True
        If Not SduFileSystem.File.Exists(System.IO.Path.Combine(Target, InstanceName & ".json")) Then
            Throw New System.IO.IOException("同名目录不是有效的 Minecraft 实例，已保留原目录：" & Target)
        End If
        Dim Message = $"本地已安装整合包：{InstanceName}" & vbCrLf & vbCrLf &
                      "是否更新这个实例的模组？" & vbCrLf &
                      "已有且包内也有的模组会替换，没有的会加入；本地独有模组不会移除。" & vbCrLf &
                      "模组配置、快捷键和存档保持原样，更新前会备份旧 mods。"
        Return If(Ask Is Nothing, MyMsgBox(Message, "发现已安装的整合包", "更新模组", "取消"), Ask(Message)) = 1
    End Function

    ''' <summary>
    ''' 把已下载的整合包复制到 PCL 目录下 SduDownload 文件夹并打开资源管理器，提示用户手动安装。
    ''' 下载文件不存在时只提示，不打开文件夹。
    ''' </summary>
    Public Sub OpenManualInstallFolder(DownloadedFile As String, InstanceName As String, Message As String, Optional IsFailure As Boolean = False)
        If Not FileUtils.Exists(DownloadedFile) Then
            Hint(Message, HintType.Red)
            Return
        End If

        Dim Folder As String = Paths.Base & "SduDownload\"
        Dim Target = SduPackageFiles.ExportForManualInstall(DownloadedFile, Folder, InstanceName)
        Logger.Info($"已复制整合包到手动安装目录：{Target}")
        OpenExplorer(Folder)
        Hint(Message & vbCrLf & "已打开下载文件夹，请手动拖入此文件安装：" & System.IO.Path.GetFileName(Target), If(IsFailure, HintType.Red, HintType.Blue))
    End Sub

    ''' <summary>
    ''' 生成 SDUcraft 实例名（去除非法字符并限制长度）。
    ''' </summary>
    Public Function CreateSduInstanceName(Item As SduGameDownload) As String
        Dim Name As String = Item.Name.Trim()
        If Item.Version <> "" AndAlso Not Name.Contains(Item.Version) Then
            Name &= " (" & Item.Version & ")"
        End If
        Return SduBareClientInstaller.SafeInstanceName(Name, $"SDUcraft-{If(Item.Id = "", "Pack", Item.Id)}")
    End Function

End Module
