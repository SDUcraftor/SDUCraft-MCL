# SDUcraft 安装任务回归

在 Windows、.NET Framework 4.x 运行时和 .NET 8 SDK 环境执行：

```powershell
.\tests\Run-SduLoaderBridgeTests.ps1
```

脚本编译生产代码 `SduLoaderBridge.vb`，并从当前 `ModLoader.vb` 原样提取 `LoaderBase`、`LoaderTask`、`LoaderCombo`，使用实际的任务线程、父子调度、状态和取消机制运行测试。UI 调度、日志、未使用的下载器等宿主能力使用替代实现；不连接官网，不下载整合包，不操作游戏目录。

覆盖：父任务等待子安装、进度传递、原始失败传播、父取消子任务、子取消父任务、已完成/失败任务不被重启、等待线程中断时取消子任务，以及拒绝等待未启动任务。

测试输出保存在被 Git 忽略的 `tests/bin`。这些测试不代替真实整合包安装与 WPF 界面回归。

## 裸客户端的文件布局与 mods 更新

```powershell
.\tests\Run-SduBareClientTests.ps1
```

直接编译生产 `SduBareClientInstaller.vb`，创建真实的临时 ZIP、文件及实例目录，验证隔离布局、网站实例名、同名数据保护、损坏 JSON、取消、线程中断、公共缓存冲突、路径校验、重复文件、配置优先级、隔离设置校验、项目 ID 定位，以及“仅同步 mods + 完整旧目录备份”。更新测试逐字节比较配置、存档、截图、资源包、游戏 JSON/JAR 和 PCL 设置，确保它们不变。故障注入测试在实际目录移动中模拟发布失败与恢复失败，检查旧模组恢复或保留在可找回的备份中。

PCL 的 INI 写入由宿主回调提供，测试回调使用相同的冒号格式；生产安装器会重新读取磁盘，确认隔离设置已写入才发布实例。测试不覆盖真实整合包下载、Minecraft 启动、WPF UI 和断电恢复。

GitHub Actions 的 Debug、Release 任务均运行上述两套测试。
