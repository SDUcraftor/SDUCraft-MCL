Public Class PageOtherHelp
    Implements IRefreshable

    Private Class HelpTopic
        Public Title As String
        Public Category As String
        Public Body As String
        Public Keywords As String
        Public LinkText As String
        Public LinkUrl As String
    End Class

    Private ReadOnly Topics As New List(Of HelpTopic) From {
        New HelpTopic With {
            .Title = "第一次使用，如何进入 SDUcraft？", .Category = "开始游玩",
            .Body = "1. 连接山东大学校园网，确认能够打开 SDUcraft 官网。" & vbCrLf &
                    "2. 在皮肤站注册账号，并添加角色；在启动器首页填写该站的邮箱和密码。" & vbCrLf &
                    "3. 打开「下载 → SDUcraft 整合包」，选择对应项目，等待下载和安装完成。选择安装好的实例并启动游戏，按官网服务器列表和公告加入服务器。",
            .Keywords = "新手 入门 首次 开始 下载 游戏 实例 安装", .LinkText = "下载 SDUcraft 整合包", .LinkUrl = "download"},
        New HelpTopic With {
            .Title = "账号、密码和皮肤在哪里管理？", .Category = "皮肤站登录",
            .Body = "启动器仅使用 skin.sducraft.cn 的账号登录。请填写皮肤站注册邮箱与密码，并确认账号下已添加角色。" & vbCrLf &
                    "注册、找回密码、管理角色及上传皮肤均在皮肤站操作；首页也提供注册或找回密码入口。修改密码或皮肤后，可退出账号再重新登录，或刷新皮肤。",
            .Keywords = "登录 登陆 邮箱 密码 注册 角色 皮肤 验证 认证 auth yggdrasil", .LinkText = "打开 SDUcraft 皮肤站 ↗", .LinkUrl = SduSkinLogin.Website},
        New HelpTopic With {
            .Title = "提示连接校园网，或官网与下载无法访问？", .Category = "网络排查",
            .Body = "先连接山东大学校园网，并在浏览器中打开官网。校园网需要认证时，请先完成认证，再尝试刷新整合包列表。" & vbCrLf &
                    "启动器会在打开后检查官网是否能够连接。若已经连接校园网仍然提示失败，也可能是网络暂时异常或官网维护；这条提示不代表账号有问题。查看公告或稍后重试。",
            .Keywords = "校园网 网络 连接 超时 离线 失败 ping 网站 下载 空白", .LinkText = "打开官网检查连接 ↗", .LinkUrl = SduNetworkCheck.WebsiteUrl},
        New HelpTopic With {
            .Title = "已安装的整合包如何更新？", .Category = "安装与更新",
            .Body = "先关闭游戏，再次点击同一官网项目。启动器会重新下载整包；识别为裸客户端且找到已有实例时，只同步整个 mods 目录，并将旧 mods 留作备份。" & vbCrLf &
                    "新版不再包含的模组，以及自己添加的模组，会从当前 mods 中移除，但仍保留在旧备份中。存档、配置、截图和资源包不会被覆盖。Minecraft 本体及 Forge/Fabric 等加载器也不会升级；若新版要求更换这些版本，请另建实例安装。" & vbCrLf &
                    "mrpack、CurseForge 等标准格式目前仅支持全新安装；遇到同名实例会停止，不会覆盖旧实例。",
            .Keywords = "更新 升级 同步 mods 模组 mod forge fabric 配置 存档 mrpack curseforge", .LinkText = "查看官网整合包说明 ↗", .LinkUrl = "https://www.sducraft.top/downloads/game"},
        New HelpTopic With {
            .Title = "旧模组在哪里备份，如何恢复？", .Category = "备份与恢复",
            .Body = "启动器固定开启版本隔离，各实例的存档、mods 和配置分别保存在 versions/<实例名>/ 中。若此前使用公共目录，请将需要的旧文件手动复制到对应实例；旧文件不会自动移动或删除。" & vbCrLf &
                    "每次同步 mods 前，旧目录会保存到游戏根目录下的：" & vbCrLf &
                    "PCL/SduModBackups/<实例名>/<时间戳与随机标识>/mods/" & vbCrLf &
                    "更新成功提示和日志会给出本次备份的完整路径。备份不会自动删除。恢复前先关闭游戏，将当前 mods 另行保存，再把所需备份中的 mods 复制回该实例目录。" & vbCrLf &
                    "解压失败或在替换前取消会保留原模组；切换失败会尝试恢复，无法恢复时会提示备份位置。不要删除整个实例目录，以免丢失存档。",
            .Keywords = "备份 回退 恢复 还原 删除 存档 丢失 自己的模组 版本隔离 公共目录 迁移"},
        New HelpTopic With {
            .Title = "服务器地址、玩法和活动去哪里看？", .Category = "官网与社团",
            .Body = "官网「介绍 → 服务器列表」提供服务器与子服介绍，「社区 → 公告」发布最新消息，「更多 → 文档&指南」提供玩法文档和社团资料。请按对应项目确认游戏版本、连接地址及开放情况。" & vbCrLf &
                    "社团介绍页列有交流方式；加入或联系社团时，以官网公布的信息为准。",
            .Keywords = "地址 IP 端口 服务器 生存 创造 校区 空岛 指令 活动 公告 文档 加群 QQ 社团 联系", .LinkText = "关于我们与交流方式 ↗", .LinkUrl = "https://www.sducraft.top/intro"},
        New HelpTopic With {
            .Title = "安装失败或游戏启动后崩溃怎么办？", .Category = "常见问题",
            .Body = "先查看任务页或错误提示，确认是下载失败、安装失败，还是游戏崩溃。下载失败先检查校园网；游戏崩溃先核对整合包要求的游戏版本、加载器和 Java，并检查是否额外添加了模组。" & vbCrLf &
                    "无法自动识别的压缩包会导出到启动器旁的 SduDownload 文件夹，并打开目录供手动处理。标准整合包可尝试拖入启动器；含多个版本的裸客户端请解压后选择所需版本，不要反复覆盖原实例。" & vbCrLf &
                    "仍无法解决时，保留错误提示和相关日志，按照官网的交流方式联系维护人员，说明整合包名称与复现步骤。",
            .Keywords = "报错 崩溃 闪退 启动失败 安装失败 Java 日志 手动 拖入 多版本", .LinkText = "查看最新公告 ↗", .LinkUrl = "https://www.sducraft.top/community/notice"}
    }

    Public Sub New()
        InitializeComponent()
        RenderHelp()
    End Sub

    Private Sub PageOther_Loaded(sender As Object, e As RoutedEventArgs) Handles Me.Loaded
        PanBack.ScrollToHome()
    End Sub

    Public Sub SearchRun() Handles SearchBox.TextChanged
        If PanList Is Nothing Then Return
        RenderHelp()
    End Sub

    Private Sub RenderHelp()
        Dim Query = If(SearchBox.Text, "").Trim()
        PanList.Children.Clear()
        Dim Matches = Topics.Where(Function(Topic) Query = "" OrElse
            (Topic.Title & " " & Topic.Category & " " & Topic.Body & " " & Topic.Keywords).
                IndexOf(Query, StringComparison.OrdinalIgnoreCase) >= 0).ToList()
        LabResults.Text = If(Query = "", "常用指南 · 点击标题展开说明", $"找到 {Matches.Count} 条相关说明")
        PanEmpty.Visibility = If(Matches.Count = 0, Visibility.Visible, Visibility.Collapsed)
        For Each Topic In Matches
            Dim Card As New MyCard With {
                .Title = Topic.Title, .Margin = New Thickness(0, 0, 0, 14),
                .CanSwap = True, .IsSwapped = Query = "" AndAlso Topic IsNot Topics(0)}
            Dim Content As New StackPanel With {.Margin = New Thickness(20, 42, 20, 18)}
            Dim Category As New TextBlock With {.Text = Topic.Category, .FontSize = 11, .Margin = New Thickness(0, 0, 0, 8)}
            Category.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrushGray2")
            Content.Children.Add(Category)
            Dim Body As New TextBlock With {
                .Text = Topic.Body, .FontSize = 13, .TextWrapping = TextWrapping.Wrap, .LineHeight = 23}
            Body.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrushGray1")
            Content.Children.Add(Body)
            If Topic.LinkUrl IsNot Nothing Then
                Dim Link As New MyTextButton With {
                    .Content = Topic.LinkText, .Tag = Topic.LinkUrl,
                    .HorizontalAlignment = HorizontalAlignment.Left, .Margin = New Thickness(0, 12, 0, 0)}
                AddHandler Link.Click, AddressOf OpenHelpWebsite
                Content.Children.Add(Link)
            End If
            Card.Children.Add(Content)
            Card.SwapControl = Content
            PanList.Children.Add(Card)
        Next
    End Sub

    Private Sub OpenHelpButton(sender As Object, e As MouseButtonEventArgs)
        OpenHelpWebsite(sender, e)
    End Sub

    Private Sub OpenHelpWebsite(sender As Object, e As EventArgs)
        Dim Target = CStr(CType(sender, FrameworkElement).Tag)
        If Target = "download" Then
            FrmMain.PageChange(FormMain.PageType.Download, FormMain.PageSubType.DownloadSdu)
        Else
            OpenWebsite(Target)
        End If
    End Sub

    Private Sub ClearSearch(sender As Object, e As EventArgs)
        SearchBox.Text = ""
    End Sub

    Public Sub Refresh() Implements IRefreshable.Refresh
        SearchBox.Text = ""
        RenderHelp()
        PanBack.ScrollToHome()
    End Sub

    '保留内置错误说明等上下文帮助的调用入口。
    Public Shared Sub OnItemClick(Entry As HelpEntry)
        Try
            If Entry.IsEvent Then
                CustomEvent.Raise(Entry.EventType, Entry.EventData)
            Else
                EnterHelpPage(Entry)
            End If
        Catch ex As Exception
            Logger.Error(ex, "处理帮助项目点击时发生意外错误")
        End Try
    End Sub
    Public Shared Sub EnterHelpPage(Location As String)
        RunInThread(
        Sub()
            If Not HelpLoader.State = LoadState.Finished Then HelpLoader.WaitForExit(GetUuid)
            Dim Entry As New HelpEntry(Location)
            RunInUi(
            Sub()
                Dim FrmHelpDetail As New PageOtherHelpDetail
                If FrmHelpDetail.Init(Entry) Then
                    FrmMain.PageChange(New FormMain.PageStackData With {.Page = FormMain.PageType.HelpDetail, .Additional = {Entry, FrmHelpDetail}})
                Else
                    Logger.Warn("已取消进入帮助项目，这一般是由于 xaml 初始化失败，且用户在弹窗中手动放弃")
                End If
            End Sub)
        End Sub)
    End Sub
    Public Shared Sub EnterHelpPage(Entry As HelpEntry)
        RunInThread(
        Sub()
            If Not HelpLoader.State = LoadState.Finished Then HelpLoader.WaitForExit(GetUuid)
            RunInUi(
            Sub()
                Dim FrmHelpDetail As New PageOtherHelpDetail
                If FrmHelpDetail.Init(Entry) Then
                    FrmMain.PageChange(New FormMain.PageStackData With {.Page = FormMain.PageType.HelpDetail, .Additional = {Entry, FrmHelpDetail}})
                Else
                    Logger.Warn("已取消进入帮助项目，这一般是由于 xaml 初始化失败，且用户在弹窗中手动放弃")
                End If
            End Sub)
        End Sub)
    End Sub


End Class
