# SDUcraft 安装任务回归

在 Windows、.NET Framework 4.x 运行时和 .NET 8 SDK 环境执行：

```powershell
.\tests\Run-SduLoaderBridgeTests.ps1
```

脚本编译生产代码 `SduLoaderBridge.vb`，并从当前 `ModLoader.vb` 原样提取 `LoaderBase`、`LoaderTask`、`LoaderCombo`，使用实际的任务线程、父子调度、状态和取消机制运行测试。UI 调度、日志、未使用的下载器等宿主能力使用替代实现；不连接官网，不下载整合包，不操作游戏目录。

覆盖：父任务等待子安装、进度传递、原始失败传播、父取消子任务、子取消父任务、已完成/失败任务不被重启、等待线程中断时取消子任务，以及拒绝等待未启动任务。

测试输出保存在被 Git 忽略的 `tests/bin`。这些测试不代替真实整合包安装与 WPF 界面回归。
