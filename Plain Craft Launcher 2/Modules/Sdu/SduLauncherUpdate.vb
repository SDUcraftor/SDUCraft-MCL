Imports System.IO
Imports System.Reflection
Imports System.Text
Imports System.Threading

''' <summary>GitHub Releases 自动更新的界面与任务调度。</summary>
Public Module SduLauncherUpdate
    Private Started As Integer
    Private Busy As Integer
    Private ReadOnly UpdateLock As New Object()
    Private Pending As SduUpdateCore.InstallPlan
    Private RestartRequested As Boolean
    Public LatestIsCurrent As Boolean?

    Public Sub CheckAtStartup()
        If BuildType <> BuildTypes.Release OrElse Settings.Get(Of Integer)("SystemSystemUpdate") = 3 Then Return
        If Interlocked.CompareExchange(Started, 1, 0) <> 0 Then Return
        Check(False)
    End Sub

    Public Sub CheckManually()
        Dim IsReady As Boolean
        SyncLock UpdateLock
            IsReady = Pending IsNot Nothing
        End SyncLock
        If IsReady Then
            ShowReady()
            Return
        End If
        Check(True)
    End Sub

    Private Sub Check(Manual As Boolean)
        If Interlocked.CompareExchange(Busy, 1, 0) <> 0 Then
            If Manual Then Hint("正在检查或下载启动器更新，请稍候。")
            Return
        End If
        If Manual Then Hint("正在检查 SDUcraft 启动器更新……")
        RunInNewThread(
        Sub()
            Dim ManifestFile = Path.Combine(Path.GetTempPath(), "sdu-update-" & Guid.NewGuid().ToString("N") & ".json")
            Try
                Dim ManifestUrl = SduUpdateCore.LatestManifestUrl & "?sdu_check=" & Date.UtcNow.Ticks
                If Not SduUpdateCore.Download(ManifestUrl, ManifestFile, 256 * 1024, CancellationToken.None,
                    Validate:=Sub(FileName) SduUpdateCore.ParseManifest(File.ReadAllText(FileName, Encoding.UTF8))) Then
                    If Manual Then Hint("当前尚未发布可自动更新的启动器版本。", HintType.Blue)
                    Return
                End If
                Dim Release = SduUpdateCore.ParseManifest(File.ReadAllText(ManifestFile, Encoding.UTF8))
                Dim CurrentVersion = Assembly.GetExecutingAssembly().GetName().Version
                LatestIsCurrent = Release.Version <= CurrentVersion
                If LatestIsCurrent.Value Then
                    If Manual Then Hint("当前已是最新版 SDUcraft 启动器。", HintType.Green)
                    Return
                End If
                Dim Mode = Settings.Get(Of Integer)("SystemSystemUpdate")
                If Not Manual AndAlso Mode = 2 AndAlso Not Release.Critical Then Return
                If Manual OrElse Mode <> 0 Then
                    Dim Description = "发现 SDUcraft 启动器 " & Release.VersionText & "，当前版本 " & CurrentVersion.ToString(3) & "。" & vbCrLf & vbCrLf &
                                      If(Release.Notes = "", "此版本未填写更新说明。", Release.Notes) & vbCrLf & vbCrLf & "下载后可在退出时安装，游戏和启动器设置会保留。"
                    If MyMsgBox(Description, "启动器更新", "下载更新", "稍后") <> 1 Then Return
                End If
                DownloadRelease(Release)
            Catch ex As Exception
                If ex.IsCanceled Then
                    Hint("启动器更新下载已取消。", HintType.Blue)
                Else
                    Logger.Warn(ex, "检查或下载 SDUcraft 启动器更新失败")
                    If Manual Then MyMsgBox("未能完成启动器更新，请稍后重试。" & vbCrLf & ex.Message, "更新失败", "我知道了", IsWarn:=True)
                End If
            Finally
                Try
                    If File.Exists(ManifestFile) Then File.Delete(ManifestFile)
                Catch
                End Try
                Interlocked.Exchange(Busy, 0)
            End Try
        End Sub, "SDUcraft 启动器更新", ThreadPriority.BelowNormal)
    End Sub

    Private Sub DownloadRelease(Release As SduUpdateCore.ReleaseInfo)
        Dim Worker As New LoaderTask(Of SduUpdateCore.ReleaseInfo, SduUpdateCore.InstallPlan)("下载 SDUcraft 启动器更新",
        Sub(Task)
            Dim Target = Assembly.GetExecutingAssembly().Location
            Dim Stage = Path.Combine(Path.GetDirectoryName(Target), ".sdu-update-" & Guid.NewGuid().ToString("N"))
            Dim Completed As Boolean = False
            Directory.CreateDirectory(Stage)
            Try
                Dim Token = Task.CreateCancellationToken()
                If Not SduUpdateCore.Download(Release.AssetBase & SduUpdateCore.ExeName, Path.Combine(Stage, SduUpdateCore.ExeName), Release.ExeSize, Token,
                    Sub(Value) Task.Progress = Value * 0.9,
                    Validate:=Sub(FileName) SduUpdateCore.VerifyExecutable(FileName, Release)) Then Throw New IOException("发布版本缺少启动器文件。")
                If Not SduUpdateCore.Download(Release.AssetBase & SduUpdateCore.ConfigName, Path.Combine(Stage, SduUpdateCore.ConfigName), Release.ConfigSize, Token,
                    Validate:=Sub(FileName) SduUpdateCore.VerifyConfiguration(FileName, Release)) Then Throw New IOException("发布版本缺少配置文件。")
                Token.ThrowIfCancellationRequested()
                Dim Plan = SduUpdateCore.CreatePlan(Stage, Target, Release)
                SduUpdateCore.VerifyPrepared(Plan)
                SduUpdateCore.WritePlan(Plan)
                SduUpdateCore.ReadPlan(Path.Combine(Stage, "update-plan.json"), Path.Combine(Stage, SduUpdateCore.ExeName))
                Token.ThrowIfCancellationRequested()
                Task.Output = Plan
                Task.Progress = 1
                Completed = True
            Finally
                If Not Completed Then
                    Try
                        Directory.Delete(Stage, True)
                    Catch ex As Exception
                        Logger.Warn(ex, "清理启动器更新暂存目录失败")
                    End Try
                End If
            End Try
        End Sub)
        Dim DownloadTask As New LoaderCombo(Of SduUpdateCore.ReleaseInfo)(Worker.Name, New List(Of LoaderBase) From {Worker})
        RunInUiWait(
        Sub()
            LoaderTaskbarAdd(DownloadTask)
            DownloadTask.Start(Release)
            FrmMain.BtnExtraDownload.ShowRefresh()
        End Sub)
        SduLoaderBridge.WaitForCompletion(DownloadTask)
        SyncLock UpdateLock
            Pending = Worker.Output
            IsUpdateWaitingRestart = True
        End SyncLock
        If Not IsProgramEnding Then RunInUi(AddressOf ShowReady, True)
    End Sub

    Private Sub ShowReady()
        If IsProgramEnding Then Return
        If MyMsgBox("新版启动器已下载并校验完成。" & vbCrLf & "可以立即重启安装，也可以在本次退出时安装。", "更新已就绪", "立即重启", "退出时安装") = 1 Then
            If HasDownloadingTask() Then
                Hint("还有任务正在运行，请完成或取消任务后，再点击检查更新以重启。", HintType.Blue)
                Return
            End If
            RestartRequested = True
            FrmMain.EndProgram(True)
        End If
    End Sub

    Public Sub LaunchPendingOnExit()
        SyncLock UpdateLock
            If Pending Is Nothing Then Return
            Try
                SduUpdateCore.VerifyPrepared(Pending)
                Pending.Restart = RestartRequested
                SduUpdateCore.WritePlan(Pending)
                Dim Helper = Path.Combine(Pending.Stage, SduUpdateCore.ExeName)
                Dim PlanFile = Path.Combine(Pending.Stage, "update-plan.json")
                Diagnostics.Process.Start(New Diagnostics.ProcessStartInfo With {
                    .FileName = Helper, .Arguments = "--sdu-update " & ChrW(34) & PlanFile & ChrW(34),
                    .WorkingDirectory = Pending.Stage, .UseShellExecute = False, .CreateNoWindow = True, .WindowStyle = Diagnostics.ProcessWindowStyle.Hidden})
                Pending = Nothing
                IsUpdateWaitingRestart = False
            Catch ex As Exception
                Logger.Warn(ex, "启动更新替换进程失败，保留旧启动器")
            End Try
        End SyncLock
    End Sub

    ''' <summary>在通常的启动初始化之前处理；不获取启动器单例、不运行主窗口。</summary>
    Public Function RunHelper(Arguments As String()) As Integer
        If Arguments.Length <> 2 Then Return 1
        Dim Plan As SduUpdateCore.InstallPlan = Nothing
        Try
            Plan = SduUpdateCore.ReadPlan(Arguments(1), Assembly.GetExecutingAssembly().Location)
            SduUpdateCore.WaitForParent(Plan)
            SduUpdateCore.Publish(Plan)
            If Plan.Restart Then
                Diagnostics.Process.Start(New Diagnostics.ProcessStartInfo With {
                    .FileName = Plan.Target, .Arguments = "--wait", .WorkingDirectory = Path.GetDirectoryName(Plan.Target), .UseShellExecute = True})
            End If
            Return 0
        Catch ex As Exception
            System.Windows.MessageBox.Show(ex.Message & vbCrLf & "请保留更新目录中的 backup.exe 和配置备份，必要时用于恢复。", "SDUcraft 启动器更新失败", MessageBoxButton.OK, MessageBoxImage.Error)
            Return 1
        End Try
    End Function
End Module
