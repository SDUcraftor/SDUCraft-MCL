# PCL2 SDUcraft 可读源码包

本源码包基于 PCL 开源源码 `2.99.0.0`（`Meloong-Git/PCL@6b9a50d`），包含：

- 启动页 SDUcraft 下载速览
- 下载侧栏的 SDUcraft 整合包页面
- 搜索、下载与 PCL 现有整合包安装流程的接入
- 已读状态、响应大小、字段长度和下载地址白名单

## 本次简单审查与修复

1. 补齐 `ItemSdu.Check` 事件，侧栏点击后会实际切换页面。
2. 修复 `DownloadSdu = 7` 与侧栏控件下标不一致导致的程序化切页错选。
3. 为代码构造的下载页注册独立的 `PanBack` 名称作用域，修复入场动画类型转换失败。
4. 调整状态行和列表间距，避免首条项目与卡片标题重叠。
5. 只选择 Windows 下载文件。
6. 安装状态保持到实际安装加载器结束，避免过早允许第二个安装任务。
7. 下载 URL 仅接受 SDUcraft 官网 HTTPS 的 `.zip` / `.mrpack` 文件；移除未使用的远端图片字段。

编译和界面回归均通过。静态界面核对了入口切换、页面重新进入、28 条数据加载以及 `bingo` 搜索筛选结果。

## 构建

完整构建参数见 `MODIFICATIONS.md` 第 5 节。项目依赖 `.NET Framework 4.8`、Visual Studio/MSBuild，以及 `PCLCS` 和 `MeloongCore`。

`NuGet.local.config` 和文档中的部分路径来自原构建机器，其他机器构建时可能需要按实际 SDK、引用程序集和本地 NuGet 缓存位置调整。

当前已验证产物 SHA256：

```text
C179702D9424A3DBB522B69C6D788FCC6BD8F73ED52D85872BFCA2E809500C94
```

## 已知限制

- 本次没有实际下载大型整合包或修改使用者 `.minecraft`，因此没有执行完整安装回归。
- 下载链路依赖 PCL 基础网络层处理 HTTP 重定向；未再次校验重定向后的最终主机。
- 开源基线仍不包含官方 Microsoft OAuth Client ID、CurseForge API Key、联网通知和赞助主题能力。
- 官网内容可能变化，接口字段变化时需要重新核对 `SduModpackService.vb`。

## 压缩包内容

源码包排除了 `.git`、`bin`、`obj`、`.vs` 和原版二进制 ZIP，只保留源码、项目配置、资源、许可与文档。
