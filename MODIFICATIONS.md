# PCL2 SDUcraft 修改副本：变更说明与开源代码对比

## 1. 这份副本在哪里

本目录是独立保存的新副本：

```text
D:\Code\PCL2-SDUcraft-Readable
```

可执行文件构建输出：

```text
D:\Code\PCL2-Patch\build-readable\Plain Craft Launcher 2.exe
```

当前构建信息：

| 项目 | 值 |
| --- | --- |
| 开源基线 | `Meloong-Git/PCL`，`main` 分支，`6b9a50d` |
| 基线版本 | `2.99.0.0 正式版` |
| 副本构建版本 | `2.99.0.0` |
| 副本 EXE SHA256 | `C179702D9424A3DBB522B69C6D788FCC6BD8F73ED52D85872BFCA2E809500C94` |

补丁快照：

```text
docs/patches/01-open-source-to-feature.patch
docs/patches/02-feature-to-readable.patch
docs/patches/03-direct-download-page.patch
```

其中：

- `01-open-source-to-feature.patch`：相对开源原版的功能差异快照。
- `02-feature-to-readable.patch`：把功能代码整理成可读结构的重构差异。
- `03-direct-download-page.patch`：新增内置 SDUcraft 整合包下载页，并补齐导航、页面容器和安装状态收尾。

---

## 2. 变更总览

### 2.1 功能性修改

| 修改点 | 开源原版行为 | 本副本行为 |
| --- | --- | --- |
| `RestrictMcDownload` | `True`，某些安装/下载路径会被限制。 | 改为 `False`。 |
| 启动页下载速览 | 无此面板。 | 新增 SDUcraft 下载速览面板。 |
| 自动更新 | 开源分支本身已经没有更新通知功能。 | 保持禁用；检查更新只提示该版本不包含更新功能。 |
| 启动器通知后的面板显示 | 不适用。 | 通知关闭后立即刷新/保留面板。 |
| 自定义主页刷新 | `PanCustom.Children.Clear()` 会清空所有子项。 | 只清空普通主页内容，保留 SDUcraft 面板。 |
| 内置整合包下载页 | 没有 SDUcraft 专用下载页。 | 下载侧栏新增“SDUcraft 整合包”，可搜索官网项目、下载文件并交给 PCL 现有安装器。 |

### 2.2 可读性重构

最初的原型把所有逻辑都放进 `PageLaunchRight.xaml.vb` 的一个大 `#Region` 中。现在拆成三层：

```text
Modules\Sdu\SduDownloadItem.vb
    下载项数据模型、JSON 解析、HTML 转纯文本

Modules\Sdu\SduDownloadReadStore.vb
    已读状态文件读写

Pages\PageLaunch\PageLaunchRight.SduDownloadPanel.vb
    面板 UI、分页渲染、滚动加载、未读标记、点击行为

Pages\PageLaunch\PageLaunchRight.xaml.vb
    原有启动页逻辑；只在刷新入口调用面板，并保证刷新主页时不误删面板
```

这样做的目的：

1. 启动页原文件只保留原有主页职责，减少无关代码混入。
2. JSON 字段访问集中到 `SduDownloadItem`，界面层不再直接读取 `Item("xxx")`。
3. 已读状态持久化集中到 `SduDownloadReadStore`。
4. 面板常量、字段、方法分段，方法名统一使用 `SduDownload` 前缀。
5. 关键数量、URL、阈值不再散落在方法中，而是集中为常量。

主要文件规模：

| 文件 | 行数 | 作用 |
| --- | ---: | --- |
| `Pages\PageLaunch\PageLaunchRight.xaml.vb` | 302 | 原启动页逻辑，已移除面板实现细节 |
| `Pages\PageLaunch\PageLaunchRight.SduDownloadPanel.vb` | 338 | 面板 UI 与交互 |
| `Modules\Sdu\SduDownloadItem.vb` | 81 | 下载项模型 |
| `Modules\Sdu\SduDownloadReadStore.vb` | 34 | 已读状态存储 |
| `Modules\Sdu\SduModpackService.vb` | 约 180 | 整合包与下载文件 API 的类型化访问、HTTPS 白名单过滤 |
| `Pages\PageDownload\PageDownloadSdu.vb` | 约 270 | 内置整合包下载页、搜索与一键安装入口 |

---

## 3. 与开源代码的核心对比

### 3.1 开源原版的启动页刷新入口

开源原版 `PageLaunchRight.RefreshReal()` 只处理自定义主页：

```vbnet
Private Sub RefreshReal()
    Dim Content As String = Nothing, Url As String = Nothing
    Select Case Settings.Get(Of Integer)("UiCustomType")
        Case 1
            Content = FileUtils.TryReadAsString(Paths.Base & "PCL\Custom.xaml")
        Case 2
            Url = Settings.Get(Of String)("UiCustomNet")
        '......
    End Select

    '......

    '同步到 UI
    RunInUi(Sub() LoadContent(Content))
End Sub
```

本副本在进入主页逻辑前先刷新 SDUcraft 面板：

```vbnet
Private Sub RefreshReal()
    RefreshSduDownloadPanel()

    Dim Content As String = Nothing, Url As String = Nothing
    '......

    RunInUi(Sub() LoadContent(Content))
End Sub
```

`RefreshSduDownloadPanel()` 会避免重复加载：

```vbnet
Public Sub RefreshSduDownloadPanel()
    If _SduDownloadIsLoading Then Return
    If IsSduDownloadPanelVisible() Then Return

    RunInThread(AddressOf LoadSduDownloadPanel)
End Sub
```

---

### 3.2 `RestrictMcDownload`

开源原版：

```vbnet
Public Const RestrictMcDownload As Boolean = True
```

本副本：

```vbnet
Public Const RestrictMcDownload As Boolean = False
```

这个开关位于：

```text
Plain Craft Launcher 2\Modules\ModSecret.vb
```

---

### 3.3 下载项模型

功能原型直接在 UI 层访问 JSON：

```vbnet
Dim Id = If(Item("id") Is Nothing,
            If(Item("name"), "").ToString(),
            Item("id").ToString())

Dim Name = If(Item("name"), "未命名下载").ToString()
Dim Version = If(Item("version"), "").ToString()
Dim Intro = StripSduHtml(If(Item("intro"), "").ToString())
```

这样有几个问题：

1. 界面代码需要了解 JSON 字段名。
2. `Nothing` 和空字符串判断散落在多处。
3. 排序字段、HTML 清理、默认值都混在渲染代码里。

可读性副本把 JSON 结构转成明确模型：

```vbnet
Public Class SduDownloadItem
    Public Property Id As String = String.Empty
    Public Property Name As String = "未命名下载"
    Public Property Version As String = String.Empty
    Public Property PublishDate As String = String.Empty
    Public Property ModifyDate As String = String.Empty
    Public Property IntroHtml As String = String.Empty

    Public ReadOnly Property SortDate As String
        Get
            Return If(String.IsNullOrWhiteSpace(ModifyDate), PublishDate, ModifyDate)
        End Get
    End Property

    Public ReadOnly Property Intro As String
        Get
            Return RemoveHtml(IntroHtml)
        End Get
    End Property
End Class
```

解析入口集中在这里：

```vbnet
Public Shared Function ParseList(Json As String) As List(Of SduDownloadItem)
    Return JArray.Parse(Json).
        OfType(Of JObject)().
        Select(AddressOf FromJson).
        OrderByDescending(Function(Item) Item.SortDate).
        ToList()
End Function
```

界面层之后只接触类型化属性：

```vbnet
Item.Id
Item.Name
Item.Version
Item.PublishDate
Item.Intro
```

---

### 3.4 已读状态存储

功能原型把文件路径和读写逻辑写在页面里：

```vbnet
Dim File As String = Paths.Base & "PCL\SduDownloadRead.txt"
If System.IO.File.Exists(File) Then
    For Each Id In System.IO.File.ReadAllText(File).Split(","c)
        If Id.Trim() <> "" Then Result.Add(Id.Trim())
    Next
End If
```

现在独立成 `SduDownloadReadStore`：

```vbnet
Public Function ReadIds() As HashSet(Of String)
    Dim Result As New HashSet(Of String)

    Try
        If Not System.IO.File.Exists(StoreFilePath) Then Return Result

        For Each Id In System.IO.File.ReadAllText(StoreFilePath).Split(","c)
            If Not String.IsNullOrWhiteSpace(Id) Then Result.Add(Id.Trim())
        Next
    Catch ex As Exception
        Logger.Warn(ex, "读取 SDUcraft 下载面板已读记录失败")
    End Try

    Return Result
End Function
```

页面层只调用：

```vbnet
_SduDownloadReadIds = SduDownloadReadStore.ReadIds()
SduDownloadReadStore.SaveIds(_SduDownloadReadIds)
```

---

### 3.5 首屏渲染与滚动分页

面板的目标行为：

```text
启动后：只渲染前 5 条
向下滚动到接近底部：追加 3 条
继续滚动：继续追加 3 条
直到显示全部下载讯息
```

关键常量：

```vbnet
Private Const SduDownloadInitialCount As Integer = 5
Private Const SduDownloadPageSize As Integer = 3
Private Const SduDownloadListMaxHeight As Double = 318.0
Private Const SduDownloadScrollBottomThreshold As Double = 28.0
```

滚动事件核心：

```vbnet
Private Sub SduDownloadScrollChanged(Sender As Object, EventArgs As ScrollChangedEventArgs) _
    Handles _SduDownloadScroll.ScrollChanged

    If Not CanLoadMoreSduDownloadItems() Then Return

    Dim DistanceToBottom = _SduDownloadScroll.ScrollableHeight - _SduDownloadScroll.VerticalOffset
    If DistanceToBottom > SduDownloadScrollBottomThreshold Then
        _SduDownloadIsWaitingNextScroll = False
        Return
    End If

    If _SduDownloadIsWaitingNextScroll Then Return

    _SduDownloadIsWaitingNextScroll = True
    AppendSduDownloadItems(SduDownloadPageSize)
End Sub
```

追加逻辑：

```vbnet
Private Sub AppendSduDownloadItems(ItemCount As Integer)
    If _SduDownloadItems Is Nothing OrElse _SduDownloadStack Is Nothing Then Return

    Dim RemainingCount = _SduDownloadItems.Count - _SduDownloadVisibleCount
    If RemainingCount <= 0 Then
        UpdateSduDownloadStatus()
        Return
    End If

    Dim RenderCount = Math.Min(ItemCount, RemainingCount)
    Dim LastIndex = _SduDownloadVisibleCount + RenderCount - 1

    For Index = _SduDownloadVisibleCount To LastIndex
        _SduDownloadStack.Children.Add(CreateSduDownloadCardItem(_SduDownloadItems(Index)))
    Next

    _SduDownloadVisibleCount += RenderCount
    UpdateSduDownloadStatus()
End Sub
```

说明：

- SDUcraft API 没有分页参数，所以网络请求仍是一次获取完整 JSON 数组。
- 这里减少的是 UI 构建和首次渲染压力：首屏只构建 5 个条目。
- `_SduDownloadIsWaitingNextScroll` 用于避免同一段滚动事件连续把所有剩余数据展开。

---

### 3.6 未读标记

条目进入界面时判断是否未读：

```vbnet
Private Function CreateSduDownloadCardItem(Item As SduDownloadItem) As Border
    Dim IsUnread = Not String.IsNullOrEmpty(Item.Id) AndAlso
                   (_SduDownloadReadIds Is Nothing OrElse Not _SduDownloadReadIds.Contains(Item.Id))

    '......

    If IsUnread Then
        TitleGrid.Children.Add(CreateUnreadBadge())
    End If

    '......
End Function
```

未读徽标：

```vbnet
Private Function CreateUnreadBadge() As Border
    Return New Border With {
        .Background = New SolidColorBrush(Color.FromRgb(229, 72, 77)),
        .CornerRadius = New CornerRadius(3),
        .Padding = New Thickness(5, 0, 5, 0),
        .HorizontalAlignment = HorizontalAlignment.Right,
        .VerticalAlignment = VerticalAlignment.Top,
        .Child = New TextBlock With {
            .Text = "新",
            .Foreground = Brushes.White,
            .FontSize = 10.5,
            .FontWeight = FontWeights.Bold
        }
    }
End Function
```

点击条目后只更新当前 Border 的 UI，不重建整个列表，因此滚动位置不会丢失：

```vbnet
Private Sub MarkSduDownloadItemRead(ItemBorder As Border, ItemId As String)
    If _SduDownloadReadIds.Contains(ItemId) Then Return

    _SduDownloadReadIds.Add(ItemId)
    SduDownloadReadStore.SaveIds(_SduDownloadReadIds)

    '移除红色“新”徽标，并把标题右侧预留空间清零。
    If TitleGrid.Children.Count > 1 Then TitleGrid.Children.RemoveAt(1)
    Title.Margin = New Thickness(0)
End Sub
```

---

### 3.7 通知后立即显示

开源原版 `FormMain.xaml.vb` 的开源提示结束于：

```vbnet
If VersionBranchMain = "OpenSource" Then
    MyMsgBox($"该版本中无法使用以下特性：......", "开源版本说明")
End If
```

本副本在确认通知后主动刷新面板：

```vbnet
If VersionBranchMain = "OpenSource" Then
    MyMsgBox($"该版本中无法使用以下特性：......", "开源版本说明")
    RunInUi(Sub() FrmLaunchRight.RefreshSduDownloadPanel(), True)
End If
```

同时，自定义主页刷新不能误删面板。

开源原版：

```vbnet
PanCustom.Children.Clear()
```

本副本：

```vbnet
'只清空自定义主页内容，保留已加载的 SDUcraft 下载面板
For i = PanCustom.Children.Count - 1 To 0 Step -1
    Dim Child = TryCast(PanCustom.Children(i), FrameworkElement)
    If Child IsNot _SduDownloadCard Then
        PanCustom.Children.RemoveAt(i)
    End If
Next
```

---

### 3.8 内置整合包下载页

下载侧栏新增“SDUcraft 整合包”，对应：

```text
Pages\PageDownload\PageDownloadSdu.vb
```

页面从两个 API 获取数据并关联：

```text
https://www.sducraft.top/api/download/
https://www.sducraft.top/api/filesetfordownload/
```

主要行为：

1. 只显示官网提供 Windows 下载文件的条目，当前共有 28 项。
2. 支持按名称、版本和作者即时筛选。
3. 点击条目后把文件下载到 PCL 任务缓存目录。
4. 下载完成后调用现有 `ModpackInstall` 流程，避免重复实现整合包解析、下载前置和版本创建。
5. 安装状态会保持到实际安装加载器结束，防止重复启动第二个 SDUcraft 安装任务。

下载地址必须同时满足以下条件才会进入列表：

```text
HTTPS
主机为 www.sducraft.top
路径位于 /FileSetForDownload/
扩展名为 .zip 或 .mrpack
```

页面本身由代码构造，因此显式建立了 `PanBack` 名称作用域，并在卡片标题下预留状态行高度，避免与主窗口同名控件或首条列表项冲突。

---

## 4. 面板数据来源与状态文件

数据来源：

```text
https://www.sducraft.top/api/download/
```

面板页面：

```text
https://www.sducraft.top/downloads/game
```

内置下载页还会读取：

```text
https://www.sducraft.top/api/filesetfordownload/
```

当前使用的 JSON 字段：

| JSON 字段 | 模型属性 | 说明 |
| --- | --- | --- |
| `id` | `Id` | 已读状态标识 |
| `name` | `Name` | 条目标题 |
| `version` | `Version` | 游戏版本 |
| `publish_date` | `PublishDate` | 发布日期 |
| `modify_date` | `ModifyDate` | 修改日期；排序时优先 |
| `intro` | `IntroHtml` / `Intro` | 原始介绍和纯文本介绍 |

已读状态文件：

```text
PCL\SduDownloadRead.txt
```

文件格式示例：

```text
34,33,32,27
```

---

## 5. 构建方式

本副本使用的本地构建参数如下：

```powershell
$env:NUGET_PACKAGES = 'D:\Code\Tools\NuGetPackages'

dotnet restore `
  '.\PCL2-SDUcraft-Readable\PCLCS\PCLCS.csproj' `
  --configfile '.\PCL2-SDUcraft-Readable\NuGet.local.config' `
  -p:FrameworkPathOverride='D:\Code\Tools\RefAssemblies\build\.NETFramework\v4.8' `
  -p:Configuration=Release

dotnet build --no-restore `
  '.\PCL2-SDUcraft-Readable\PCLCS\PCLCS.csproj' `
  -p:FrameworkPathOverride='D:\Code\Tools\RefAssemblies\build\.NETFramework\v4.8' `
  -c Release

& 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe' `
  'PCL2-SDUcraft-Readable\Plain Craft Launcher 2\Plain Craft Launcher 2.vbproj' `
  -p:Configuration=Release `
  -p:Platform=AnyCPU `
  -p:FrameworkPathOverride='D:\Code\Tools\RefAssemblies\build\.NETFramework\v4.8' `
  -p:OutputPath='D:\Code\PCL2-Patch\build-readable\' `
  -m -v:m
```

---

## 6. 与开源版本的主要差异位置

| 文件 | 修改类型 | 说明 |
| --- | --- | --- |
| `Plain Craft Launcher 2\Modules\ModSecret.vb` | 功能 | `RestrictMcDownload` 关闭。 |
| `Plain Craft Launcher 2\FormMain.xaml.vb` | 功能 | 通知确认后刷新面板。 |
| `Plain Craft Launcher 2\Pages\PageLaunch\PageLaunchRight.xaml.vb` | 功能 / 重构 | 接入面板；刷新主页时保留面板。 |
| `Plain Craft Launcher 2\Pages\PageLaunch\PageLaunchRight.SduDownloadPanel.vb` | 新增 | 面板 UI、滚动加载、交互。 |
| `Plain Craft Launcher 2\Modules\Sdu\SduDownloadItem.vb` | 新增 | 下载项模型与 JSON 解析。 |
| `Plain Craft Launcher 2\Modules\Sdu\SduDownloadReadStore.vb` | 新增 | 已读状态存储。 |
| `Plain Craft Launcher 2\Modules\Sdu\SduModpackService.vb` | 新增 | 官网整合包与文件 API 访问、Windows 文件选择和下载直链白名单。 |
| `Plain Craft Launcher 2\Pages\PageDownload\PageDownloadSdu.vb` | 新增 | 内置搜索、下载和安装入口。 |
| `Plain Craft Launcher 2\Pages\PageDownload\PageDownloadLeft.xaml` | 功能 | 下载侧栏新增 SDUcraft 条目。 |
| `Plain Craft Launcher 2\Pages\PageDownload\PageDownloadLeft.xaml.vb` | 功能 | 接入页面切换、刷新和页面实例。 |
| `Plain Craft Launcher 2\Plain Craft Launcher 2.vbproj` | 构建 | 注册新增源码文件；适配本机构建。 |
| `PCLCS\PCLCS.csproj` | 构建 | 适配本机 .NET SDK / 引用程序集环境。 |
| `NuGet.local.config` | 构建 | 使用 `D:\Code\Tools\NuGet` 本地包缓存。 |

---

## 7. 仍然保持的开源限制

本副本基于 PCL 开源分支，因此以下限制与开源基线一致：

1. 不包含官方构建中的微软正版登录 `ClientId`。
2. 不包含官方构建中的 CurseForge API Key。
3. 不包含更新与联网通知功能。
4. 部分赞助主题能力仍然不可用。

第三方登录 / Authlib-Injector 外置登录能力没有被关闭，因此仍可用于 SDUcraft 皮肤站这类 Yggdrasil 认证服务。

---

## 8. 新增模块安全审查

完整审查报告另见：

```text
SECURITY_REVIEW.md
```

审查范围：

```text
Modules\Sdu\SduDownloadItem.vb
Modules\Sdu\SduDownloadReadStore.vb
Modules\Sdu\SduModpackService.vb
Pages\PageLaunch\PageLaunchRight.SduDownloadPanel.vb
Pages\PageLaunch\PageLaunchRight.xaml.vb 中新增的保留面板逻辑
Pages\PageDownload\PageDownloadSdu.vb
Pages\PageDownload\PageDownloadLeft.xaml(.vb) 中新增的导航逻辑
FormMain.xaml.vb 中新增的通知后刷新调用
```

### 8.1 已修复的问题

#### A. 自定义主页可以伪造面板 Tag

原实现刷新自定义主页时按字符串保留面板：

```vbnet
'有风险
If Child Is Nothing OrElse Not Equals(Child.Tag, "SduDownloadPanel") Then
    PanCustom.Children.RemoveAt(i)
End If
```

自定义主页是外部内容，控件可以声明：

```xml
<Border Tag="SduDownloadPanel" />
```

这样可能让任意自定义主页控件绕过清空逻辑，在主页切换后残留。

现改为只按对象引用保留可信面板：

```vbnet
'只按对象引用保留 SDUcraft 面板；不能信任任意主页控件设置的 Tag。
For i = PanCustom.Children.Count - 1 To 0 Step -1
    Dim Child = TryCast(PanCustom.Children(i), FrameworkElement)
    If Child IsNot _SduDownloadCard Then PanCustom.Children.RemoveAt(i)
Next
```

移除面板时也同样只移除 `_SduDownloadCard` 引用。

#### B. 远端响应缺少基本资源限制

新增 API 返回的是站点 JSON，属于不可信输入。原实现会直接进入：

```vbnet
JArray.Parse(Json)
```

如果远端异常、被劫持或站点误返回超大内容，可能放大内存和 CPU 占用。

现在加入限制：

```vbnet
Private Const SduDownloadMaxResponseLength As Integer = 2 * 1024 * 1024

Dim Response = NetRequestByClientRetry(SduDownloadApiUrl, RequireJson:=True)
If Response.Length > SduDownloadMaxResponseLength Then
    Throw New InvalidOperationException($"下载讯息响应超过安全上限：{Response.Length} 字符")
End If
```

同时限制单条字段长度和最大条目数：

```vbnet
Private Const MaxItemCount As Integer = 1000
Private Const MaxIdLength As Integer = 128
Private Const MaxNameLength As Integer = 200
Private Const MaxVersionLength As Integer = 80
Private Const MaxDateLength As Integer = 32
Private Const MaxIntroLength As Integer = 4096
```

#### C. 已读状态文件缺少大小上限

`PCL\SduDownloadRead.txt` 属于本地状态文件。虽然它不包含敏感信息，但读取时不应无限信任文件大小。

现在加入：

```vbnet
Const MaxStoreFileLength As Integer = 1024 * 1024

If New System.IO.FileInfo(StoreFilePath).Length > MaxStoreFileLength Then
    Logger.Warn("SDUcraft 已读记录文件超过安全上限，已忽略本次读取")
    Return Result
End If
```

#### D. 启动请求可能重复触发

`_SduDownloadIsLoading` 原来的检查和赋值不是原子操作。启动页加载和通知关闭后的主动刷新如果时机接近，可能重复发起网络请求。

现在用启动锁收紧：

```vbnet
SyncLock SduDownloadStartLock
    If _SduDownloadIsLoading Then Return
    If IsSduDownloadPanelVisible() Then Return
    _SduDownloadIsLoading = True
End SyncLock
```

#### E. 异常消息直接显示到界面

网络异常消息可能包含远端返回内容。把它直接放进界面可能被用于误导性文案。

现在完整异常只写日志：

```vbnet
Logger.Warn(ex, "加载 SDUcraft 下载面板失败")
```

界面只显示固定文案：

```vbnet
RenderSduDownloadError("无法连接到 SDUcraft 下载服务，请稍后再试。")
```

#### F. 下载项 Id 和本地状态格式收紧

远端 JSON 中的 `id` 会进入本地已读状态文件。原实现只限制长度，没有限制字符；如果远端返回逗号、换行或超长字符串，可能破坏本地状态格式或放大比较成本。

现在远端 Id 只接受：

```text
A-Z a-z 0-9 _ . : -
```

并且最大 128 字符：

```vbnet
Private Shared Function ReadId(Item As JObject) As String
    Dim Value = ReadBoundedString(Item, "id", MaxIdLength)
    Dim IsValid = RegularExpressions.Regex.IsMatch(Value, "^[A-Za-z0-9_.:-]+$")
    Return If(IsValid, Value, String.Empty)
End Function
```

本地已读文件读取时也应用同样规则，并忽略超过 128 字符的 Id：

```vbnet
Dim IsValid = Id.Length <= 128 AndAlso
              Regex.IsMatch(Id, "^[A-Za-z0-9_.:-]+$")
If IsValid Then Result.Add(Id)
```

#### G. 整合包下载直链白名单

内置下载页不直接信任 API 返回的文件地址。只有同时满足 HTTPS、固定主机、固定路径前缀和 `.zip` / `.mrpack` 扩展名的链接才会被使用：

```vbnet
If Url.Scheme <> Uri.UriSchemeHttps Then Return False
If Not String.Equals(Url.Host, AllowedHost, StringComparison.OrdinalIgnoreCase) Then Return False
If Not Url.AbsolutePath.StartsWithF("/FileSetForDownload/", False) Then Return False

Dim Extension = PathUtils.GetExtension(Url.AbsolutePath).Lower()
Return Extension = "zip" OrElse Extension = "mrpack"
```

未使用的远端图片 URL 字段已经移除，拒绝下载项时也只记录固定说明，避免把不可信 URL 写入日志。

### 8.2 确认为安全的点

| 检查点 | 结论 |
| --- | --- |
| HTML 注入 | 站点返回的 `intro` 只经过 HTML 转纯文本，最终写入 `TextBlock.Text`，不会作为 HTML/XAML 执行。 |
| XAML 注入 | 新面板使用代码构造 WPF 控件，不把远端 JSON 拼进 XAML，也不通过 `GetObjectFromXML` 加载。 |
| 命令执行 | 没有把远端字段传给 `Process.Start`、shell、脚本或编译器。 |
| URL 注入 | 点击条目只打开硬编码的 `https://www.sducraft.top/downloads/game`，不打开远端返回的 URL。 |
| 路径穿越 | 已读文件路径固定为 `PCL\SduDownloadRead.txt`，远端字段不参与路径拼接。 |
| 敏感信息发送 | 请求是匿名 GET，不携带账号、密码、Token 或启动器识别码。 |
| 已读状态泄露 | 已读状态只保存在本地，不会上传。 |

### 8.3 需要继续注意的点

1. 站点返回的 `name`、`version`、`intro` 仍属于不可信内容；当前只能影响面板文案、顺序和长度。
2. 如果未来把 `intro` 改回 HTML 渲染、加入远端图片，或改为打开远端 URL，必须重新做 URL 白名单和 HTML 清理。
3. SDUcraft API 没有分页参数，所以网络层仍会一次性收到完整 JSON；当前已限制响应长度、条目数和字段长度。
4. 本地已读文件可以被本机用户修改，但影响仅限“新 / 已读”标记，不会造成代码执行。
5. `NetRequestByClientRetry` 仍会先把完整响应读入字符串后才返回；当前 `2 MB` 上限可以阻止超大响应进入 JSON/UI 层，但不能限制底层 HTTP 读取过程中的瞬时内存。若要做严格防线，需要后续给 PCL 基础网络层增加“最大响应字节数”参数。
