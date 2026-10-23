Imports System.Threading

''' <summary>
''' 等待已经启动的安装加载器，不再次调用 Start，避免重启刚完成的 LoaderCombo。
''' </summary>
Friend Module SduLoaderBridge

    ''' <summary>
    ''' 在工作线程等待真实安装结束，并向所属步骤传递进度、失败和取消。
    ''' Owner 为空时只等待，用于使页面的安装中状态覆盖整个下载安装过程。
    ''' </summary>
    Public Sub WaitForCompletion(Child As LoaderBase, Optional Owner As LoaderTask(Of Integer, Integer) = Nothing,
                                 Optional ExternalToken As CancellationToken = Nothing)
        If Child Is Nothing Then Throw New ArgumentNullException(NameOf(Child))

        Using Linked = CancellationTokenSource.CreateLinkedTokenSource(ExternalToken, If(Owner Is Nothing, CancellationToken.None, Owner.CreateCancellationToken()))
            Dim Token As CancellationToken = Linked.Token
            Using Registration As CancellationTokenRegistration = Token.Register(AddressOf Child.Cancel)
                Try
                    Do
                        Token.ThrowIfCancellationRequested()
                        Select Case Child.State
                            Case LoadState.Finished
                                If Owner IsNot Nothing Then Owner.Progress = 1
                                Return
                            Case LoadState.Failed
                                Throw New Exception(Child.Name & "失败", Child.Error)
                            Case LoadState.Canceled
                                Throw New OperationCanceledException(Child.Name & "已取消")
                            Case LoadState.Waiting
                                Throw New InvalidOperationException("不能等待尚未启动的安装任务：" & Child.Name)
                        End Select

                        If Owner IsNot Nothing Then Owner.Progress = Child.Progress
                        Thread.Sleep(20)
                    Loop
                Catch
                    '父步骤取消会中断等待线程；异常退出时也必须取消仍在运行的子安装。
                    Child.Cancel()
                    Throw
                End Try
            End Using
            End Using
    End Sub

End Module
