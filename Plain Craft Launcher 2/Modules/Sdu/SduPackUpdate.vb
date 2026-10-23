Imports System.IO
Imports System.Collections.Concurrent
Imports System.Threading

''' <summary>更新与启动互斥，启动前按官网项目/文件修改时间检查。</summary>
Public Module SduPackUpdate
    Private ReadOnly Active As New ConcurrentDictionary(Of String, Byte)(StringComparer.OrdinalIgnoreCase)

    Public Function IsUpdating(InstanceFolder As String) As Boolean
        Return Active.ContainsKey(Path.GetFullPath(InstanceFolder).TrimEnd("\"c))
    End Function

    Friend Function Reserve(InstanceFolder As String, AllowDuringLaunch As Boolean) As IDisposable
        Dim Target = Path.GetFullPath(InstanceFolder).TrimEnd("\"c)
        Dim ErrorMessage As String = Nothing
        RunInUiWait(Sub()
                        If McInstanceLock.IsLocked(New McInstance(Target)) OrElse
                           McWatcherList.Any(Function(Watch) PathUtils.IsSame(Watch.Instance.PathVersion, Target) AndAlso Not Watch.GameProcess.HasExited) OrElse
                           (McLaunchLoader.State = LoadState.Loading AndAlso McInstanceSelected IsNot Nothing AndAlso
                            PathUtils.IsSame(McInstanceSelected.PathVersion, Target) AndAlso Not AllowDuringLaunch) Then
                            ErrorMessage = "该实例正在启动、运行或被修改，请结束后再更新。"
                        ElseIf Not Active.TryAdd(Target, 0) Then
                            ErrorMessage = "该实例正在安装或更新，请稍后再试。"
                        End If
                    End Sub)
        If ErrorMessage IsNot Nothing Then Throw New IOException(ErrorMessage)
        Return New Reservation(Target)
    End Function

    Private Class Reservation
        Implements IDisposable
        Private ReadOnly Target As String
        Public Sub New(Value As String)
            Target = Value
        End Sub
        Public Sub Dispose() Implements IDisposable.Dispose
            Active.TryRemove(Target, Nothing)
        End Sub
    End Class

    Public Sub CheckBeforeLaunch(Instance As McInstance, Token As CancellationToken)
        Dim Latest As SduGameDownload = Nothing
        Try
            Dim Record = SduPackRecord.Read(Instance.PathVersion)
            Dim Id = CStr(Record?("sduProjectId"))
            If String.IsNullOrWhiteSpace(Id) Then Return
            McLaunchLog("正在检查 SDUcraft 整合包更新……")
            Token.ThrowIfCancellationRequested()
            Latest = SduModpackService.FetchModpacks(Token, FastCheck:=True).FirstOrDefault(Function(Item) Item.Id = Id)
            Token.ThrowIfCancellationRequested()
            If Latest Is Nothing OrElse Not SduPackRecord.HasUpdate(Record, Latest) Then Return
        Catch ex As OperationCanceledException
            If Token.IsCancellationRequested Then Throw
            Hint("检查整合包更新超时，将使用本地整合包启动。", HintType.Blue)
            Return
        Catch ex As ThreadInterruptedException
            Throw
        Catch ex As Exception
            Logger.Warn(ex, "检查整合包更新失败，继续使用已安装内容")
            Hint("暂时无法检查整合包更新，将使用本地整合包启动。", HintType.Blue)
            Return
        End Try
        Dim Choice = MyMsgBox($"{Latest.DisplayName} 有新的整合包内容。" & vbCrLf &
                              $"官网时间：{If(Latest.PreferredFile?.ModifyDate, Latest.ModifyDate)}" & vbCrLf & vbCrLf &
                              "更新仅替换已有模组、加入新模组，保留本地独有模组、模组配置、快捷键和存档。",
                              "整合包更新", "更新并启动", "暂不更新", "取消启动")
        Token.ThrowIfCancellationRequested()
        If Choice = 3 OrElse Choice = 0 Then Throw New OperationCanceledException()
        If Choice = 1 Then
            Dim McRoot = Path.GetDirectoryName(Path.GetDirectoryName(Instance.PathVersion.TrimEnd("\"c)))
            SduModpackInstaller.StartInstall(Latest, McRoot, Instance.Name, Token, FromLaunch:=True)
        End If
    End Sub
End Module
