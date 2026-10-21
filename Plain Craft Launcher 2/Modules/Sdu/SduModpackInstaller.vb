''' <summary>
''' SDUcraft 整合包一键下载安装器。
''' 首页速览面板与内置下载页共用：先下载官网直链（进度显示），再交给适配器检测结构并安装。
''' 必须在工作线程中调用。
''' </summary>
Public Module SduModpackInstaller

    ''' <summary>
    ''' 启动整合包下载并安装。下载与安装进度显示在任务栏加载器中。
    ''' </summary>
    Public Sub StartInstall(Item As SduGameDownload)
        Dim DownloadFile As SduGameDownloadFile = Item.PreferredFile
        If DownloadFile Is Nothing Then
            Throw New Exception("该整合包没有可用的 Windows 下载文件，无法安装。")
        End If

        Dim GameId As String = If(Item.Id = "", GetUuid().ToString("N"), Item.Id)
        Dim InstanceName As String = CreateSduInstanceName(Item)

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
            SduModpackAdapter.AdaptAndInstall(LocalFile, Item, Task)
        End Sub) With {.ProgressWeight = 0.1})

        ' 启动加载器，显示进度到任务栏
        Dim Loader As New LoaderCombo(Of String)(LoaderName, Loaders) With {
            .OnStateChanged = Sub(MyLoader)
                Select Case MyLoader.State
                    Case LoadState.Failed
                        Hint(MyLoader.Name & "失败：" & MyLoader.Error.GetDisplay(False), HintType.Red)
                    Case LoadState.Canceled
                        Hint(MyLoader.Name & "已取消！", HintType.Blue)
                End Select
                McInstallFailedClearFolder(MyLoader)
            End Sub
        }
        ' 安装到默认 .minecraft/versions/<实例名> 目录，由 PCL 自动版本隔离
        Loader.Start(McFolderSelected & "versions\" & InstanceName & "\")

        ' UI 操作必须在 UI 线程执行
        RunInUi(Sub()
            LoaderTaskbarAdd(Loader)
            FrmMain.BtnExtraDownload.ShowRefresh()
        End Sub)
        RunInUi(Sub() FrmMain.PageChange(FormMain.PageType.TaskManager))
    End Sub

    ''' <summary>
    ''' 生成 SDUcraft 实例名（去除非法字符并限制长度）。
    ''' </summary>
    Public Function CreateSduInstanceName(Item As SduGameDownload) As String
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
