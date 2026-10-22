# PCL SDUcraft 修改版

[![Build](https://github.com/SDUcraftor/SDUCraft-MCL/actions/workflows/build.yml/badge.svg)](https://github.com/SDUcraftor/SDUCraft-MCL/actions/workflows/build.yml)
[![License](https://img.shields.io/badge/license-PCL%20%E5%88%86%E5%8F%91%E6%9C%89%E9%99%90%E8%AE%B8%E5%8F%AF-946ce6)](LICENCE)

> **本仓库是第三方基于 [Plain Craft Launcher (PCL)](https://github.com/Meloong-Git/PCL) 开源源码的独立二次创作，不是官方版本。**
> 与 PCL 开发者龙腾猫跃、成都瓜皮龙科技有限公司均无隶属或授权关系，不代表官方立场。

面向 SDUcraft 服务器的 PCL 修改版：在官方开源源码的基础上，新增服务器整合包下载与一键安装能力，并把原型中堆在启动页里的逻辑重构为可读的分层结构。

## 项目信息

| 项目 | 值 |
| --- | --- |
| 开源基线 | `Meloong-Git/PCL`，`main` 分支，提交 `6b9a50d` |
| 基线版本 | `2.99.0.0` 正式版 |
| 本分支版本 | `2.99.0.0`（第三方修改） |
| 技术栈 | WPF / VB.NET / .NET Framework 4.8 |
| 构建产物 | `Plain Craft Launcher 2.exe` |

## 与官方开源版的差异

### 功能修改

| 修改点 | 官方开源版 | 本分支 |
| --- | --- | --- |
| `RestrictMcDownload` | `True`，部分安装 / 下载路径受限 | 改为 `False` |
| 启动页下载速览 | 无 | 新增 SDUcraft 下载速览面板（含未读标记、分页加载） |
| 内置整合包下载页 | 无 | 侧栏新增「SDUcraft 整合包」，可搜索服务器官网项目并下载 |
| 整合包安装衔接 | 无 | 下载结果交给 PCL 现有安装器（含启动器包、`mrpack` 重打包适配） |
| 自动更新 | 开源分支已移除更新通知 | 保持禁用，检查更新仅提示本版本不含更新功能 |
| 自定义主页刷新 | 清空全部子项 | 仅清空普通主页内容，保留 SDUcraft 面板 |

### 可读性重构

原型把全部逻辑写进 `PageLaunchRight.xaml.vb` 的一个大 `#Region`，现已拆分为：

```text
Modules\Sdu\SduDownloadItem.vb          下载项数据模型、JSON 解析、HTML 转纯文本
Modules\Sdu\SduDownloadReadStore.vb     已读状态持久化
Modules\Sdu\SduModpackService.vb        整合包 / 下载文件 API 的类型化访问、HTTPS 白名单过滤
Modules\Sdu\SduModpackAdapter.vb        下载后适配：提取内层 mrpack、裸客户端重打包
Modules\Sdu\SduBareClientInstaller.vb  裸客户端隔离安装、仅同步 mods、备份与失败恢复
Pages\PageLaunch\PageLaunchRight.SduDownloadPanel.vb   面板 UI 与交互
Pages\PageDownload\PageDownloadSdu.vb   内置整合包下载页
```

启动页原文件只保留原有主页职责，面板常量、字段、方法统一使用 `SduDownload` 前缀。

更多细节见 [`MODIFICATIONS.md`](MODIFICATIONS.md) 与 [`docs/patches/`](docs/patches)。

### 裸客户端安装与更新

- 首次安装先在游戏目录内暂存、校验，再移动为 `versions/<网站实例名>`。主 JSON、主 JAR、原生库目录及 JSON 的自身 `jar` 引用随实例名统一。
- 根级 `mods/config/saves/resourcepacks/shaderpacks/options.txt` 等游戏内容写入实例目录并开启版本隔离；只有根级 `libraries/assets` 使用公共缓存。已有公共缓存内容不同时停止安装，不覆盖原文件。
- 目标实例已存在时，仅将 `mods` 与新版包同步。新版包没有的旧模组从当前 `mods` 移出，配置、存档、截图、资源包、游戏 JSON/JAR、PCL 设置和公共依赖均保持原样。没有任何 `mods` 文件的包不会清空已有模组。
- 更新前旧 `mods` 整体备份至 `<Minecraft目录>/PCL/SduModBackups/<实例名>/<UTC时间>-<唯一编号>/mods`。切换失败时尝试恢复；恢复被权限或占用阻止时，保留备份并在异常中给出路径。取消或解包失败只清理本次暂存目录。
- 首次安装记录官网项目 ID，官网标题或版本号改变后仍定位原实例。旧版未带 `PCL/SduPack.json` 标记的实例只按完整实例名匹配，不猜测或迁移其他目录。
- 多版本裸客户端包无法唯一确定安装目标时转为手动处理。此处的“仅更新 mods”针对裸客户端包；标准 `mrpack/CurseForge/MCBBS/HMCL/MMC` 仍由官方安装器处理首次安装，已有同名实例时停止，避免官方安装流程覆盖其配置或游戏文件。

回归测试使用临时 ZIP 和临时游戏目录，不操作真实游戏数据：

```powershell
.\tests\Run-SduBareClientTests.ps1
.\tests\Run-SduLoaderBridgeTests.ps1
```

## 构建

前置条件：

- Windows 10 / 11
- .NET Framework 4.8 开发包
- .NET 8 SDK（用于构建 `MeloongCore` 与 `PCLCS`；根目录 `global.json` 允许使用已安装的最新 8.0 SDK）
- Visual Studio 2022，或 Build Tools 中的 MSBuild

```powershell
dotnet restore PCLCS\PCLCS.csproj -p:Configuration=Release
msbuild "Plain Craft Launcher 2\Plain Craft Launcher 2.vbproj" -p:Configuration=Release
```

说明：

- `MeloongCore` 是 `PCLCS.csproj` 的 `ProjectReference`，构建时必须同时存在（本仓库已作为普通目录包含）。
- VB 主工程通过 `ProjectReference` 构建 `PCLCS` 与 `MeloongCore`，再将生成的 DLL 嵌入资源；干净检出不需要预先准备 `PCLCS/bin`。
- 安装任务生命周期回归可运行 `./tests/Run-SduLoaderBridgeTests.ps1`，不下载整合包或修改游戏目录。
- `NuGet.local.config` 是本机构建配置（含本机 NuGet 源路径），已加入 `.gitignore`。在其他机器构建时请按实际环境替换为自己的 NuGet 源。
- 官方 CI 会通过 Secrets 注入 `PCL_MS_CLIENT_ID`、`PCL_CURSEFORGE_API_KEY`；未配置时对应值为空，不影响编译。

## 许可、署名与合规

本分支属于 PCL 的「重度使用（基于本仓库修改）」，遵循 [《PCL 分发有限许可》](LICENCE)（[原文](https://shimo.im/docs/rGrd8pY8xWkt6ryW#anchor-X0bo)）的要求：

1. 已明确声明本作品是第三方基于 PCL 独立进行的二次创作，**非官方版本**。
2. 软件显示名称以 `Plain Craft Launcher (PCL)` 开头，并附加表示第三方修改的后缀。
3. 在「关于」页面首位保留龙腾猫跃的署名及赞助链接，公开源代码，并继续使用 `LICENCE` 文件作为指引。
4. 保留与 `McLaunchPrecheck` 相一致（或更强）的 Minecraft 正版购买提示。
5. 不提供与 PCL 赞助解锁类似的付费解锁功能。

> 维护者请注意：`Plain Craft Launcher 2/Pages/PageOther/PageOtherAbout.xaml` 中的龙腾猫跃署名、赞助链接以及上游版权声明均为合规必需内容，**请勿删除**。
> 该页面底部「成都瓜皮龙科技有限公司」的版权信息属于上游原始声明，本仓库为第三方修改版，两者不应混淆。

第三方组件：

| 组件 | 许可 |
| --- | --- |
| [MeloongCore](https://github.com/Meloong-Git/MeloongCore) | Apache-2.0 |
| Newtonsoft.Json | MIT |

## 免责声明

本软件不是 Minecraft 官方产品。未经 Mojang 或 Microsoft 批准，也与 Mojang 或 Microsoft 无关联。

本仓库按现状提供，不对使用本修改版造成的任何数据丢失、账号风险或服务器问题负责；请自行备份游戏数据。
