# PCL SDUcraft 修改版

[![Build](https://github.com/SDUcraftor/SDUCraft-PCL/actions/workflows/build.yml/badge.svg)](https://github.com/SDUcraftor/SDUCraft-PCL/actions/workflows/build.yml)
[![License](https://img.shields.io/badge/license-PCL%20%E5%88%86%E5%8F%91%E6%9C%89%E9%99%90%E8%AE%B8%E5%8F%AF-946ce6)](LICENCE)

> **本仓库是第三方基于 [Plain Craft Launcher (PCL)](https://github.com/Meloong-Git/PCL) 开源源码的独立二次创作，不是官方版本。**
> 与 PCL 开发者龙腾猫跃、成都瓜皮龙科技有限公司均无隶属或授权关系，不代表官方立场。

面向 SDUcraft 服务器的 PCL 修改版：在官方开源源码的基础上，新增服务器整合包下载与一键安装能力，并把原型中堆在启动页里的逻辑重构为可读的分层结构。

## 项目信息

| 项目 | 值 |
| --- | --- |
| 开源基线 | `Meloong-Git/PCL`，`main` 分支，提交 `6b9a50d` |
| 基线版本 | `2.99.0.0` 正式版 |
| 本分支版本 | `1.2.2`（SDUcraft 修改版） |
| 技术栈 | WPF / VB.NET / .NET Framework 4.8 |
| 构建产物 | `Plain Craft Launcher 2.exe` |

## 与官方开源版的差异

### 功能修改

| 修改点 | 官方开源版 | 本分支 |
| --- | --- | --- |
| `RestrictMcDownload` | `True`，部分安装 / 下载路径受限 | 改为 `False` |
| 启动页下载速览 | 无 | 新增 SDUcraft 下载速览面板（含未读标记、分页加载） |
| 内置整合包下载页 | 无 | 下载页默认打开顶部「SDUcraft 整合包」，使用整合包方块图标；移除原版游戏和公共整合包入口，可搜索服务器官网项目并下载 |
| 整合包安装衔接 | 无 | 下载结果交给 PCL 现有安装器（含启动器包、`mrpack` 重打包适配） |
| 自动更新 | 开源分支已移除更新通知 | 启动器通过 GitHub Release 自动更新，优先镜像；启动游戏前按官网时间检查整合包更新 |
| 启动网络检查 | 无 SDUcraft 专项检查 | 后台请求官网，连接失败重试后提示连接校园网 |
| 登录方式 | 微软正版、离线及第三方登录 | 仅保留 SDUcraft 皮肤站账号登录，旧实例认证设置自动统一 |
| 帮助与功能入口 | 通用帮助、百宝箱、反馈、投票及版本修改 | 帮助页仅保留官网入口；移除内置指南、百宝箱、反馈、投票和版本修改页面入口 |
| 自定义主页刷新 | 清空全部子项 | 仅清空普通主页内容，保留 SDUcraft 面板 |

### 可读性重构

原型把全部逻辑写进 `PageLaunchRight.xaml.vb` 的一个大 `#Region`，现已拆分为：

```text
Modules\Sdu\SduDownloadItem.vb          下载项数据模型、JSON 解析、HTML 转纯文本
Modules\Sdu\SduPackRecord.vb            官网项目身份与已安装包的远端时间
Modules\Sdu\SduPackUpdate.vb            启动前检查更新、安装与启动互斥
Modules\Sdu\SduModMerge.vb              各格式通用的模组 ID 合并与完整备份
Modules\Sdu\SduModpackService.vb        整合包 / 下载文件 API 的类型化访问、HTTPS 白名单过滤
Modules\Sdu\SduModpackAdapter.vb        下载后识别包格式、首次安装及更新分发
Modules\Sdu\SduBareClientInstaller.vb  裸客户端隔离安装、mods 更新与失败恢复
Pages\PageLaunch\PageLaunchRight.SduDownloadPanel.vb   面板 UI 与交互
Pages\PageDownload\PageDownloadSdu.vb   内置整合包下载页
```

启动页原文件只保留原有主页职责，面板常量、字段、方法统一使用 `SduDownload` 前缀。

更多细节见 [`MODIFICATIONS.md`](MODIFICATIONS.md) 与 [`docs/patches/`](docs/patches)。

### 整合包安装与更新

- 裸客户端首次安装先在游戏目录内暂存、校验，再移动为 `versions/<网站实例名>`。主 JSON、主 JAR、原生库目录及 JSON 的自身 `jar` 引用随实例名统一。
- 裸客户端根级 `mods/config/saves/resourcepacks/shaderpacks/options.txt` 等游戏内容写入实例目录并开启版本隔离；只有根级 `libraries/assets` 使用公共缓存。已有公共缓存内容不同时停止安装，不覆盖原文件。
- 首次安装使用整合包内的 `config`、`options.txt`（模组设置和快捷键）。目标实例已存在时只合并 `mods`：按 Fabric、Forge、NeoForge、Quilt 或旧版模组描述中的 ID 替换旧 JAR，不依赖文件名；加入新模组，保留包内未提供的本地模组和已禁用状态。无法识别 ID 的文件按相对路径覆盖，不猜测并删除其他文件。配置、快捷键、存档、截图、资源包、游戏 JSON/JAR、PCL 设置和公共依赖均保持原样。
- 更新前旧 `mods` 整体备份至 `<Minecraft目录>/PCL/SduModBackups/<实例名>/<UTC时间>-<唯一编号>/mods`。切换失败时尝试恢复；恢复被权限或占用阻止时，保留备份并在异常中给出路径。取消或解包失败只清理本次暂存目录。
- 首次安装记录官网项目 ID，官网标题或版本号改变后仍定位原实例。旧版未带 `PCL/SduPack.json` 标记的实例只按完整实例名匹配，不猜测或迁移其他目录。
- 多版本裸客户端包无法唯一确定安装目标时转为手动处理。`mrpack/Modrinth/CurseForge/MCBBS/HMCL/MultiMC` 和内嵌整合包的 ZIP 均支持相同的更新规则；更新时只在临时目录准备包内容，不重新安装 Minecraft 或加载器。手动导入也能更新同名实例。
- 从官网安装的所有格式都会记录 `PCL/SduPack.json`。启动游戏前比较官网项目与 Windows 文件的最新时间；有更新时可选择“更新并启动”“暂不更新”或“取消启动”。网络失败不会阻止使用本地实例。纯本地导入且没有官网项目标记的实例不能自动查询官网版本，仍可手动导入新版更新。
- 首页与下载页的“新”标记只按发布时间判断：最近 7 天发布的内容置顶，再按发布时间倒序排列。修改旧条目或更新下载文件不会让旧发布变成“新”；发布日期缺失、无效或在未来时不显示“新”。整合包更新检查继续使用项目和文件修改时间。名称显示自动隐藏 `.zip/.mrpack` 等压缩包后缀。
- 本地已有实例时，从首页、下载页或手动导入更新前先确认；取消不会创建下载任务。启动前已选择“更新并启动”时不重复询问。
- 清单下载与包内 `overrides` 同时提供同 ID 模组时，使用 `overrides`；`client-overrides` 优先级更高。禁用副本和备份子目录不参与根级模组冲突判断。首次安装会把被覆盖的多余下载副本保留在 `PCL/SduSupersededMods`，更新时仍完整备份原有 `mods`。

回归测试使用临时 ZIP 和临时游戏目录，不操作真实游戏数据：

```powershell
.\tests\Run-SduBareClientTests.ps1
.\tests\Run-SduLoaderBridgeTests.ps1
.\tests\Run-SduPackFormatTests.ps1 -Executable 'Plain Craft Launcher 2\bin\Plain Craft Launcher 2.exe'
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
- 登录仅使用 `https://skin.sducraft.cn/api/yggdrasil`，已移除微软登录 Client ID 配置入口。CurseForge API Key 仍在「设置 → 启动 → SDUcraft 接入配置」中填写，或通过环境变量 `PCL_CURSEFORGE_API_KEY` 提供。

## 启动器自动更新

裸客户端安装对长路径使用 Windows 扩展路径访问，避免深目录下的依赖库、暂存文件、mods 备份超过传统长度限制时安装失败。无需修改系统的长路径开关，实例路径和界面仍显示普通路径。

正式版通过本仓库 GitHub Releases 检查启动器更新，可自动下载或下载前提示，校验后在退出时替换程序，并保留旧文件用于恢复。设置 → 启动器中的「检查更新」可手动触发。此流程保留游戏与用户设置，整合包的 mods 更新单独操作。

更新信息和文件均优先通过 `gh-proxy.com` 获取；镜像请求、内容解析或文件校验失败后，再尝试一次 GitHub 原地址。用户取消不会触发直连重试。

首次使用需要先手动安装一次包含此功能的版本。维护者推送 `v1.2.0` 这样的新标签，或手动运行 `Release launcher` 工作流，即可测试、构建并发布更新资产。完整步骤、协议和恢复方法见 [启动器发版与自动更新](docs/launcher-updates.md)。

## 启动网络与皮肤站登录

- 主窗口加载后在后台请求 `https://www.sducraft.top/`，只读取响应头；默认每次请求超时 4 秒、最多尝试两次。无法建立连接时弹窗提示连接山东大学校园网，每个进程只检查一次，不阻塞窗口启动。HTTP 错误状态也表示已连通，避免把网站自身故障误判为不在校园网。
- 首页仅显示「SDUcraft 皮肤站」登录和官网入口。注册、找回密码、皮肤管理均指向 `skin.sducraft.cn`，不再提供正版、离线、统一通行证或其他认证服务器入口。
- 选择实例和启动游戏时，旧认证设置统一为该皮肤站，登录与 Authlib-Injector 使用同一个地址。其他认证站点保存的账号、密码及令牌不会自动带入此站；原已属于该皮肤站的账号缓存可继续使用。
- 已移除对应的旧登录页面、微软 OAuth/Xbox 登录与离线身份构造流程，以及微软 Client ID、离线皮肤设置。
- 「SDUcraft 改版说明」仅在首次查看时显示；确认后按 Windows 用户保存状态，后续重启和更新不重复弹出。说明介绍改版来源、皮肤站登录、整合包与校园网要求。购买正版、赞助、旧快照版欢迎、累计使用次数和主题重新解锁提示移除；校园网检查、实际错误、首次使用协议及数据操作确认保留。
- 回归测试：`tests/Run-SduNetworkCheckTests.ps1`、`tests/Run-SduSkinLoginTests.ps1`。测试不使用真实账号或密码。

## SDUcraft 帮助中心

- 此分支固定开启所有实例的版本隔离，全局与实例设置均不再提供开关；旧的关闭设置会在读取实例游戏目录时同步为开启。存档、mods 和配置使用各自的 `versions/<实例名>/` 目录。原有公共目录中的文件保留，需要继续使用时请手动复制到对应实例目录。
- 已移除原版 Minecraft 正式版／测试版更新提示的选项与通知处理，历史开关不再触发弹窗；整合包安装所需的版本列表获取仍保留。

- 「更多」仅保留「帮助」与「关于与鸣谢」。帮助页已移除全部内置指南及搜索，仅提供官网文档、皮肤站、服务器列表和公告入口。错误弹窗调用的上下文帮助入口保留。
- 官网入口采用实际导航地址：[文档与指南](https://www.sducraft.top/more/doc)、[服务器列表](https://www.sducraft.top/intro/serverList)、[公告](https://www.sducraft.top/community/notice)、[社团介绍与交流方式](https://www.sducraft.top/intro)。服务器地址、版本和活动以官网为准。
- 版本设置保留「概览」「设置」「Mod 管理」「导出」，移除「修改」页面及对应功能隐藏选项。旧修改页跳转返回概览，旧百宝箱、反馈、投票跳转返回帮助。

## 许可、署名与合规

本分支属于基于 PCL 源码修改的第三方版本，上游采用 [《PCL 分发有限许可》](LICENCE)（[原文](https://shimo.im/docs/rGrd8pY8xWkt6ryW#anchor-X0bo)）。本分支当前的署名与功能处理如下：

1. 已明确声明本作品是第三方基于 PCL 独立进行的二次创作，**非官方版本**。
2. 软件显示名称以 `Plain Craft Launcher (PCL)` 开头，并附加表示第三方修改的后缀。
3. 在「关于」页面首位保留龙腾猫跃的署名及赞助链接，公开源代码，并继续使用 `LICENCE` 文件作为指引。
4. 本修改版按维护者需求移除了自动购买正版提醒；不提供正版账号或游戏所有权验证功能。
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
