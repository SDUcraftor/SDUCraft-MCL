# 启动器自动更新与 GitHub Actions 发布

更新源固定为公开仓库 `SDUcraftor/SDUCraft-PCL` 的正式 Releases，不需要为用户配置 GitHub Token。普通提交仍运行 Debug / Release 构建和回归；正式发版由 `Release launcher` 工作流完成。

## 发布一个版本

选择一个新的三段版本号，例如 `1.2.0`。将改动提交、推送后，可以使用其中一种方式：

1. GitHub 仓库 → Actions → **Release launcher** → Run workflow，选择分支，填写 `v1.2.0` 和简短更新说明。确有重要安全或兼容性修复时，勾选 `critical`。
2. 在待发布的提交上创建并推送标签：

   ```powershell
   git tag v1.2.0
   git push origin v1.2.0
   ```

标签方式以最近一次提交的标题作为更新说明，默认不是重要更新。版本号必须是 `vMAJOR.MINOR.PATCH`，不接受测试版后缀或前导零，各段不超过 65534。使用比已发布正式版更大的版本号。

工作流会检查新版本高于已有的三段版本正式发布，设置显示版本及 EXE 内部版本，运行全部回归、构建 Release，并实际执行生成的更新辅助进程测试。之后生成以下三个资产，在草稿 Release 中上传完整后才正式发布：

| 文件 | 用途 |
| --- | --- |
| `SDUCraft-Launcher.exe` | 启动器程序与内嵌依赖 |
| `SDUCraft-Launcher.exe.config` | .NET Framework 运行配置 |
| `sducraft-update.json` | 版本、更新说明、重要更新标记、两文件的大小与 SHA-256 |

工作流的 `GITHUB_TOKEN` 仅在发布任务授予当前仓库的 `contents: write`，无需额外 PAT。已发布的同名版本不会被覆盖；失败后留下的草稿可以重新运行上传。若工作流因仓库 Actions 策略无法发布，需要维护者允许工作流取得所声明的权限。

## 用户侧行为

- 首次启用需要手动安装一次支持此功能的版本。更早的构建没有检查更新实现，不能自行升级到此版本。手动安装时将发布的 EXE 和配置放在同一目录。
- 正式版主窗口打开后在后台检查；Debug / Snapshot 不自动检查。设置中可选自动下载、下载前提示、仅提示维护者标记的重要更新、关闭自动检查。手动「检查更新」始终可用。
- 元数据从 `releases/latest/download/sducraft-update.json` 获取；只接受比当前 EXE 内部版本更高的版本。下载程序和配置时固定到该版本的 `v<版本号>` 标签，不混用不同版本的文件。
- 使用独立 HTTP 客户端，不带游戏账号、CurseForge Key 或 Cookie；只允许 HTTPS GitHub / GitHub 资产下载域名，并限制重定向次数、体积和等待时间。校验文件大小、SHA-256、EXE 内部版本和配置 XML 后，才标记为可安装。
- 下载任务可在任务页取消。下载失败、校验失败或取消均不会替换当前程序。GitHub 不可达不影响启动游戏；后台检查失败仅记日志，手动检查会给出错误。
- 下载完成后可立即重启，或本次退出时安装。辅助进程先等待旧进程真正退出，再替换当前 EXE 和 `.exe.config`；不会强行杀死旧进程。自定义 EXE 文件名会保留。
- 游戏目录、整合包、存档、模组及启动器用户设置不会被这一流程覆盖。它与整合包「同步 mods」是两套独立操作。

## 备份与恢复

暂存目录位于当前 EXE 旁边，名称为 `.sdu-update-<32位随机标识>`，需对该目录有写权限。辅助进程只接受同一父目录的目标，不接受重解析目录或链接。

替换前检查旧 EXE 和配置是否仍与下载时一致，并将它们移入暂存目录内的 `backup.exe`、`backup.exe.config`。任一步替换失败都会尝试恢复；如果恢复也失败，会显示保存备份的完整路径。更新成功仍保留备份，不自动删除。确认新版本正常后，可手动删除对应更新目录。

手动恢复时先关闭启动器，将备份 EXE 复制回原文件名；若原先有配置，同时将配置备份恢复为 `<原文件名>.config`。不要移动或删除游戏实例目录。

本流程验证的是 HTTPS 发布源、文件完整性和版本一致性，未提供独立的代码签名机制。发布账号和仓库写权限决定更新源的可信度。单次替换错误有回滚，但不保证断电时 EXE 与配置能作为一个原子事务恢复。

## 本地验证

```powershell
.\tests\Run-SduUpdateTests.ps1
msbuild "Plain Craft Launcher 2\Plain Craft Launcher 2.vbproj" -p:Configuration=Release
.\tests\Run-SduUpdateIntegrationTests.ps1 -Executable 'Plain Craft Launcher 2\bin\Plain Craft Launcher 2.exe'
```

前者编译实际更新核心，用模拟 HTTP 和临时文件测试校验、路径限制及回滚，并在工程副本上验证发版脚本。后者运行实际构建出的启动器的 `--sdu-update` 分支，等待一个临时父进程退出，并检查替换、备份与用户文件。测试不发布 Release、不连接更新服务器、不操作真实游戏目录。
