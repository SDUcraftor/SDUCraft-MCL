Imports System.Windows.Controls

''' <summary>
''' SDUcraft 官网整合包下载页。
''' 页面只负责筛选和触发；下载、解压、版本创建复用 PCL 的 ModpackInstall 流程。
''' </summary>
Public Class PageDownloadSdu
    Inherits MyPageRight
    Implements IRefreshable

    Private ReadOnly PanBack As New MyScrollViewer With {
        .VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        .HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
    }
    Private ReadOnly PanMain As New StackPanel With {.Margin = New Thickness(25, 25, 25, 20)}

    Private ReadOnly TextFilter As New MyTextBox With {
        .HintText = "搜索名称、版本或作者",
        .Margin = New Thickness(0, 0, 12, 0),
        .VerticalContentAlignment = VerticalAlignment.Center
    }
    Private ReadOnly BtnRefresh As New MyButton With {
        .Text = "刷新",
        .Padding = New Thickness(18, 6, 18, 6),
        .MinWidth = 88,
        .ColorType = MyButton.ColorState.Highlight
    }
    Private ReadOnly LabTarget As New TextBlock With {
        .Opacity = 0.65,
        .FontSize = 12,
        .TextWrapping = TextWrapping.Wrap
    }
    Private ReadOnly LabStatus As New TextBlock With {
        .Opacity = 0.65,
        .FontSize = 12,
        .Margin = New Thickness(20, 38, 20, 10)
    }
    Private ReadOnly PanList As New StackPanel

    Private SduDownloadItems As List(Of SduGameDownload)
    Private SduDownloadLoaded As Boolean
    Private SduDownloadLoading As Boolean
    Private SduDownloadInstalling As Boolean

    Public Sub New()
        PanScroll = "PanBack"
        PanBack.Content = PanMain
        Child = PanBack
        '页面完全由代码构造，需要显式注册滚动容器；否则标题栏的返回顶部逻辑会查到主窗口的同名控件。
        NameScope.SetNameScope(Me, New NameScope())
        RegisterName("PanBack", PanBack)
        BuildUi()
    End Sub

    Private Sub PageLoaded() Handles Me.Loaded
        Refresh()
    End Sub

    Public Sub Refresh() Implements IRefreshable.Refresh
        If SduDownloadLoading Then Return

        SduDownloadLoading = True
        LabStatus.Text = "正在读取 SDUcraft 整合包列表……"
        PanList.Children.Clear()
        RunInNewThread(
            Sub()
                Try
                    SduDownloadItems = SduModpackService.FetchModpacks()
                    SduDownloadLoaded = True
                    RunInUi(
                        Sub()
                            SduDownloadLoading = False
                            RenderSduDownloadItems()
                        End Sub, True)
                Catch ex As Exception
                    SduDownloadLoading = False
                    Logger.Warn(ex, "加载 SDUcraft 整合包列表失败")
                    RunInUi(
                        Sub()
                            LabStatus.Text = "读取失败：无法连接到 SDUcraft 下载服务，请稍后再试。"
                        End Sub, True)
                End Try
            End Sub, "加载 SDUcraft 整合包列表", ThreadPriority.BelowNormal)
    End Sub

    Private Sub BuildUi()
        Dim Header As New MyCard With {.Title = "SDUcraft 官网整合包", .Margin = New Thickness(0, 0, 0, 15)}
        Dim HeaderPanel As New StackPanel With {.Margin = New Thickness(20, 36, 20, 14)}

        Dim HeaderText As New TextBlock With {
            .TextWrapping = TextWrapping.Wrap,
            .Text = "从 SDUcraft 官网获取整合包直链，下载后自动适配格式并以版本隔离方式安装到 .minecraft/versions 目录。" & vbCrLf &
                    "下载进度可在任务管理器中查看；安装使用 PCL 现有整合包安装器完成。"
        }
        LabTarget.Margin = New Thickness(0, 8, 0, 0)
        LabTarget.Text = $"默认安装目录：{McFolderSelected}"

        HeaderPanel.Children.Add(HeaderText)
        HeaderPanel.Children.Add(LabTarget)
        Header.Children.Add(HeaderPanel)

        Dim SearchCard As New MyCard With {.Title = "筛选", .Margin = New Thickness(0, 0, 0, 15)}
        Dim SearchPanel As New Grid With {.Margin = New Thickness(20, 36, 20, 14)}
        SearchPanel.ColumnDefinitions.Add(New ColumnDefinition With {.Width = New GridLength(1, GridUnitType.Star)})
        SearchPanel.ColumnDefinitions.Add(New ColumnDefinition With {.Width = GridLength.Auto})
        Grid.SetColumn(TextFilter, 0)
        Grid.SetColumn(BtnRefresh, 1)
        SearchPanel.Children.Add(TextFilter)
        SearchPanel.Children.Add(BtnRefresh)
        AddHandler TextFilter.TextChanged, AddressOf TextFilter_Changed
        AddHandler BtnRefresh.Click, AddressOf BtnRefresh_Click
        SearchCard.Children.Add(SearchPanel)

        Dim ListCard As New MyCard With {.Title = "可安装整合包", .Margin = New Thickness(0, 0, 0, 15)}
        ListCard.Children.Add(LabStatus)
        ListCard.Children.Add(PanList)
        PanList.Margin = New Thickness(20, 66, 20, 14)

        PanMain.Children.Add(Header)
        PanMain.Children.Add(SearchCard)
        PanMain.Children.Add(ListCard)
    End Sub

    Private Sub TextFilter_Changed(Sender As Object, EventArgs As TextChangedEventArgs)
        RenderSduDownloadItems()
    End Sub

    Private Sub BtnRefresh_Click(Sender As Object, EventArgs As MouseButtonEventArgs)
        Refresh()
    End Sub

    Private Sub RenderSduDownloadItems()
        If SduDownloadItems Is Nothing Then Return

        Dim Keyword = TextFilter.Text.Trim()
        Dim Matched = SduDownloadItems.Where(
            Function(Item)
                If Keyword = "" Then Return True
                Return Item.Name.IndexOf(Keyword, StringComparison.OrdinalIgnoreCase) >= 0 OrElse
                       Item.Version.IndexOf(Keyword, StringComparison.OrdinalIgnoreCase) >= 0 OrElse
                       Item.Author.IndexOf(Keyword, StringComparison.OrdinalIgnoreCase) >= 0
            End Function).ToList()

        PanList.Children.Clear()
        LabStatus.Text = $"共 {Matched.Count} 个可安装项目 · 点击条目即可下载并安装"

        For Each Item In Matched
            Dim DownloadFile = Item.PreferredFile
            If DownloadFile Is Nothing Then Continue For
            PanList.Children.Add(CreateSduItem(Item, DownloadFile))
        Next

        If PanList.Children.Count = 0 Then
            LabStatus.Text = If(SduDownloadItems.Count = 0,
                                "SDUcraft 官网当前没有可安装整合包。",
                                "没有符合筛选条件的可安装整合包。")
        End If
    End Sub

    Private Function CreateSduItem(Item As SduGameDownload, DownloadFile As SduGameDownloadFile) As MyListItem
        Dim Intro = RemoveSduHtml(Item.IntroHtml)
        If Intro.Length > 80 Then Intro = Intro.Substring(0, 80) & "…"

        Dim Info = $"版本：{If(Item.Version = "", "未填写", Item.Version)}    系统：{DownloadFile.OperatingSystem}    " &
                   $"大小：{DownloadFile.SizeInMegabytes:0.##} MB    发布：{Item.PublishDate}"
        If Item.Author <> "" Then Info &= $"    作者:{Item.Author}"
        If Intro <> "" Then Info &= vbCrLf & Intro

        Dim List As New MyListItem With {
            .Title = Item.Name,
            .Info = Info,
            .Height = 62,
            .Type = MyListItem.CheckType.Clickable,
            .Tag = Item,
            .Margin = New Thickness(0, 0, 0, 8),
            .FontSize = 14
        }
        AddHandler List.Click, AddressOf SduItemClick
        Return List
    End Function

    Private Sub SduItemClick(Sender As Object, EventArgs As System.Windows.Input.MouseButtonEventArgs)
        Dim Item = TryCast(TryCast(Sender, MyListItem)?.Tag, SduGameDownload)
        If Item Is Nothing Then Return

        Dim DownloadFile = Item.PreferredFile
        If DownloadFile Is Nothing Then
            Hint("该条目没有可用的 Windows 整合包文件。", HintType.Red)
            Return
        End If
        If SduDownloadInstalling Then
            Hint("已有 SDUcraft 整合包正在下载安装。", HintType.Blue)
            Return
        End If
        If RestrictMcDownload Then
            Hint("当前构建已关闭 MC 下载安装功能。", HintType.Red)
            Return
        End If

        SduDownloadInstalling = True
        RunInNewThread(
            Sub()
                Try
                    InstallSduDownload(Item, DownloadFile)
                Catch ex As Exception
                    ex.ThrowIfCanceled()
                    Logger.Error(ex, "SDUcraft 整合包一键安装失败", LogBehavior.Alert)
                Finally
                    SduDownloadInstalling = False
                End Try
            End Sub, "安装 SDUcraft 整合包")
    End Sub

    Private Sub InstallSduDownload(Item As SduGameDownload, DownloadFile As SduGameDownloadFile)
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

    Private Function CreateSduInstanceName(Item As SduGameDownload) As String
        Dim Name = Item.Name.Trim()
        If Item.Version <> "" AndAlso Not Name.Contains(Item.Version) Then Name &= " (" & Item.Version & ")"

        Name = System.Text.RegularExpressions.Regex.Replace(Name, "[\\/:*?""<>|!;\r\n\t]", "_").Trim("_"c, " "c)
        If Name.Length > 64 Then Name = Name.Substring(0, 64).Trim("_"c, " "c)
        If Name = "" Then Name = $"SDUcraft-{If(Item.Id = "", "Pack", Item.Id)}"
        Return Name
    End Function

    Private Function RemoveSduHtml(Text As String) As String
        If String.IsNullOrWhiteSpace(Text) Then Return String.Empty

        Dim PlainText = System.Text.RegularExpressions.Regex.Replace(Text, "<[^>]+>", " ")
        PlainText = PlainText.
            Replace("&nbsp;", " ").
            Replace("&amp;", "&").
            Replace("&lt;", "<").
            Replace("&gt;", ">").
            Replace("&quot;", """")
        Return System.Text.RegularExpressions.Regex.Replace(PlainText, "\s+", " ").Trim()
    End Function

End Class
