# SDUcraft 新增模块安全审查

## 1. 审查范围

本次审查只覆盖新增代码和直接改动的启动页逻辑：

```text
Plain Craft Launcher 2\Modules\Sdu\SduDownloadItem.vb
Plain Craft Launcher 2\Modules\Sdu\SduDownloadReadStore.vb
Plain Craft Launcher 2\Modules\Sdu\SduModpackService.vb
Plain Craft Launcher 2\Pages\PageLaunch\PageLaunchRight.SduDownloadPanel.vb
Plain Craft Launcher 2\Pages\PageLaunch\PageLaunchRight.xaml.vb
Plain Craft Launcher 2\Pages\PageDownload\PageDownloadSdu.vb
Plain Craft Launcher 2\Pages\PageDownload\PageDownloadLeft.xaml(.vb)
Plain Craft Launcher 2\FormMain.xaml.vb
```

审查基准：

```text
D:\Code\PCL2-SDUcraft-Readable
```

当前已构建副本：

```text
D:\Code\PCL2-Patch\build-readable\Plain Craft Launcher 2.exe
```

当前 SHA256：

```text
C179702D9424A3DBB522B69C6D788FCC6BD8F73ED52D85872BFCA2E809500C94
```

## 2. 结论

新增模块没有发现命令执行、路径穿越、任意 URL 打开、XAML 注入或敏感信息上传问题。

审查中发现并已修复 7 类问题：

| 级别 | 问题 | 状态 |
| --- | --- | --- |
| 中 | 自定义主页控件可伪造 `Tag="SduDownloadPanel"`，导致清空主页时残留任意控件。 | 已改为只按 `_SduDownloadCard` 对象引用保留面板。 |
| 中 | 远端响应无大小 / 条目 / 字段限制，可放大内存和 UI 构建成本。 | 已加响应、条目和字段上限。 |
| 低 | 远端异常消息可能直接显示到界面，存在误导性文案风险。 | 界面改为固定错误文案，完整异常只写日志。 |
| 低 | 启动页加载和通知关闭后的主动刷新可能竞态，导致重复请求。 | 增加启动锁。 |
| 低 | 远端 `id` 可包含逗号、换行等字符，破坏本地已读状态格式。 | 远端 Id 和本地文件读取都只允许安全字符集。 |
| 低 | 本地已读状态文件没有读取大小上限。 | 超过 1 MB 时忽略读取。 |
| 中 | 官网文件 API 返回的下载 URL 不可直接信任。 | 只接受固定 HTTPS 主机、固定路径和 `.zip` / `.mrpack` 文件。 |

## 3. 数据流审查

### 3.1 远端数据

新增面板请求：

```text
GET https://www.sducraft.top/api/download/
```

内置下载页还会请求：

```text
GET https://www.sducraft.top/api/filesetfordownload/
```

请求不携带账号、密码、Token、Cookie 或本机识别码。

远端字段进入程序后的用途：

| 字段 | 处理 | 安全结论 |
| --- | --- | --- |
| `id` | 限制长度和字符集，用于本地已读状态。 | 不参与路径、命令或 URL。 |
| `name` | 限制长度后写入 `TextBlock.Text`。 | 不会作为 HTML / XAML 解析。 |
| `version` | 限制长度后写入 `TextBlock.Text`。 | 同上。 |
| `publish_date` / `modify_date` | 限制长度并用于排序。 | 恶意值只影响排序。 |
| `intro` | 去除 HTML 标签和常见实体，截断后写入 `TextBlock.Text`。 | 不执行 HTML。 |
| `file` | 只接受固定官网 HTTPS、固定路径前缀和压缩包扩展名。 | 不打开任意主机或本地路径。 |
| `download` | 限制字符集和长度，仅用于关联下载项与文件。 | 不参与命令或路径。 |

### 3.2 本地状态

已读状态保存在：

```text
PCL\SduDownloadRead.txt
```

安全限制：

```vbnet
Const MaxStoreFileLength As Integer = 1024 * 1024
```

读取时忽略超过 1 MB 的文件；每个 Id 最大 128 字符，且只接受：

```text
A-Z a-z 0-9 _ . : -
```

本机用户可以修改该文件，但这不是特权边界；影响仅限“新 / 已读”标记。

## 4. 关键修复

### 4.1 防止自定义主页伪造面板

原逻辑按字符串保留面板：

```vbnet
If Child Is Nothing OrElse Not Equals(Child.Tag, "SduDownloadPanel") Then
    PanCustom.Children.RemoveAt(i)
End If
```

自定义主页是外部内容，控件可以声明：

```xml
<Border Tag="SduDownloadPanel" />
```

这可能让任意主页控件绕过清空逻辑。

现改为对象引用判断：

```vbnet
If Child IsNot _SduDownloadCard Then PanCustom.Children.RemoveAt(i)
```

只有当前面板自身持有的 `_SduDownloadCard` 会被保留。

### 4.2 远端响应限制

```vbnet
Private Const SduDownloadMaxResponseLength As Integer = 2 * 1024 * 1024
```

请求时要求响应必须是 JSON：

```vbnet
NetRequestByClientRetry(SduDownloadApiUrl, RequireJson:=True)
```

响应超过 2 MB 时拒绝解析。

模型层还有：

```vbnet
MaxItemCount = 1000
MaxIdLength = 128
MaxNameLength = 200
MaxVersionLength = 80
MaxDateLength = 32
MaxIntroLength = 4096
```

### 4.3 防止重复启动请求

```vbnet
SyncLock SduDownloadStartLock
    If _SduDownloadIsLoading Then Return
    If IsSduDownloadPanelVisible() Then Return
    _SduDownloadIsLoading = True
End SyncLock
```

### 4.4 错误文案固定

完整异常只写日志：

```vbnet
Logger.Warn(ex, "加载 SDUcraft 下载面板失败")
```

界面只显示：

```vbnet
"无法连接到 SDUcraft 下载服务，请稍后再试。"
```

### 4.5 整合包下载地址白名单

`SduModpackService` 只接受满足以下条件的下载地址：

```text
HTTPS
主机为 www.sducraft.top
路径位于 /FileSetForDownload/
扩展名为 .zip 或 .mrpack
```

未使用的远端图片 URL 字段已经移除；拒绝下载项时日志只写入固定说明。

## 5. 明确确认安全的点

| 检查项 | 结果 |
| --- | --- |
| 是否执行远端代码？ | 否。 |
| 是否把远端 JSON 拼入 XAML？ | 否。面板全部用 WPF 对象构造。 |
| 是否渲染远端 HTML？ | 否。HTML 被转成纯文本后放入 `TextBlock.Text`。 |
| 是否打开任意远端 URL？ | 否。网页入口硬编码；文件下载只接受固定官网白名单。 |
| 是否发生路径拼接？ | 临时文件只使用经过字符集限制的远端 Id；已读文件路径固定。 |
| 是否上传用户数据？ | 否。GET 请求无账号、Token、Cookie 或识别码。 |
| 是否保存敏感数据？ | 否。本地只保存已读 Id。 |

## 6. 残留风险与建议

1. 站点返回的标题、版本、简介仍属于不可信文案；恶意站点只能让面板显示误导内容或异常排序，不能执行代码。
2. HTML 清理当前服务于“纯文本显示”，不是通用 HTML sanitizer。将来如果启用 HTML 渲染或远端图片，必须重新设计安全边界。
3. API 没有分页参数，因此基础网络层仍会先读取完整响应。当前 2 MB 上限可阻止超大内容进入 JSON/UI 层；如需更强防护，应给 PCL 网络基础层增加最大响应字节数。
4. 本地已读文件可被本机用户篡改，但这不跨权限边界；最坏影响是已读标记丢失或异常。
5. 下载链路依赖 PCL 基础网络层跟随 HTTP 重定向；当前未对最终重定向地址再做主机校验。若站点未来改用外部 CDN，需要给基础下载层增加最终地址白名单。

## 7. 构建验证

安全加固后已重新构建成功：

```text
Release / AnyCPU
Plain Craft Launcher 2.exe
```

SHA256：

```text
C179702D9424A3DBB522B69C6D788FCC6BD8F73ED52D85872BFCA2E809500C94
```
