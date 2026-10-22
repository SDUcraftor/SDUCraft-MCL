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
    Public Sub StartInstall(Item As SduGameDownload)
        Dim DownloadFile As SduGameDownloadFile = Item.PreferredFile
        If DownloadFile Is Nothing Then
            Throw New Exception("该整合包没有可用的 Windows 下载文件，无法安装。")
        End If

        Dim GameId As String = If(Item.Id = "", GetUuid().ToString("N"), Item.Id)
        Dim McFolder As String = PathUtils.AddSlashSuffix(McFolderSelected)
        Dim InstanceName As String = SduBareClientInstaller.ResolveInstanceName(McFolder, Item.Id, CreateSduInstanceName(Item))

        ' 下载文件（mrpack 或 zip，按 URL 后缀取名，适配器会按内容识别）
        Dim Extension As String = If(DownloadFile.FileUrl.EndsWithF(".mrpack", True), "mrpack", "zip")
        Dim TempFolder As String = RequestTaskTempFolder()
        Dim LocalFile As String = $"{TempFolder}SDUcraft-{GameId}.{Extension}"

        Dim LoaderName As String = $"SDUcraft 整合包安装：{InstanceName} "

        If LoaderTaskbar.Any(Function(l) l.Name = LoaderName) Then
            Hint("该整合包正在安装中！", HintType.Red)
            Return
        End If

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
            SduModpackAdapter.AdaptAndInstall(LocalFile, Item, Task, InstanceName, McFolder)
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
        RunInUi(Sub() FrmMain.PageChange(FormMain.PageType.TaskManager))

        '不能在任务刚加入列表后返回，否则两个入口会过早释放“安装中”状态。
        Try
            SduLoaderBridge.WaitForCompletion(Loader)
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
