Imports System.Net
Imports System.Net.Http
Imports System.Threading
Imports System.Threading.Tasks

''' <summary>启动时检查官网是否可连接；不在 UI 线程等待网络，也不依据 ICMP 判断。</summary>
Public Module SduNetworkCheck
    Public Const WebsiteUrl As String = "https://www.sducraft.top/"
    Private Started As Integer

    Public Sub CheckAtStartup()
        StartCheck(Function() CheckWebsiteAsync())
    End Sub

    Friend Sub StartCheck(Probe As Func(Of Task(Of ProbeResult)))
        If Interlocked.CompareExchange(Started, 1, 0) <> 0 Then Return
        RunInNewThread(
            Sub()
                Try
                    Dim Result = Probe().GetAwaiter().GetResult()
                    If Result.Reachable Then
                        Logger.Info($"SDUcraft 启动网络检查：官网已连通（HTTP {CInt(Result.StatusCode.Value)}）")
                        Return
                    End If
                    Logger.Warn(Result.Failure, "SDUcraft 启动网络检查：无法连接官网")
                    MyMsgBox("无法连接 SDUcraft 官网。" & vbCrLf &
                             "需要连接校园网才能访问相关服务，请连接山东大学校园网后重试。" & vbCrLf & vbCrLf &
                             "如果已经连接校园网，请检查网络连接；也可能是官网暂时不可用，请稍后重试。",
                             "需要连接校园网", "我知道了", IsWarn:=True)
                Catch ex As Exception
                    '检查自身异常不影响启动，不把程序异常误报为校园网故障。
                    Logger.Warn(ex, "执行 SDUcraft 启动网络检查失败")
                End Try
            End Sub, "SDUcraft 启动网络检查", ThreadPriority.BelowNormal)
    End Sub

    Friend Class ProbeResult
        Public ReadOnly Reachable As Boolean
        Public ReadOnly StatusCode As HttpStatusCode?
        Public ReadOnly Failure As Exception

        Friend Sub New(Code As HttpStatusCode)
            Reachable = True
            StatusCode = Code
        End Sub
        Friend Sub New(ErrorValue As Exception)
            Failure = ErrorValue
        End Sub
    End Class

    ''' <summary>
    ''' 仅请求官网响应头，单次超时 4 秒，默认最多尝试两次。
    ''' HTTP 错误也代表网络已连通；不跟随跳转到其他站点，不下载页面正文。
    ''' Handler 与时间参数供离线回归注入，不改变生产检查目标。
    ''' </summary>
    Friend Async Function CheckWebsiteAsync(Optional Handler As HttpMessageHandler = Nothing,
                                            Optional TimeoutMilliseconds As Integer = 4000,
                                            Optional Attempts As Integer = 2) As Task(Of ProbeResult)
        If TimeoutMilliseconds <= 0 Then Throw New ArgumentOutOfRangeException(NameOf(TimeoutMilliseconds))
        If Attempts <= 0 Then Throw New ArgumentOutOfRangeException(NameOf(Attempts))
        Dim Failure As Exception = Nothing
        Using Client As New HttpClient(If(Handler, New HttpClientHandler With {.AllowAutoRedirect = False}))
            Client.Timeout = TimeSpan.FromMilliseconds(TimeoutMilliseconds)
            For Attempt = 1 To Attempts
                Try
                    Using Request As New HttpRequestMessage(HttpMethod.Get, WebsiteUrl)
                        Request.Headers.UserAgent.ParseAdd("SDUCraft-MCL/NetworkCheck")
                        Request.Headers.CacheControl = New Headers.CacheControlHeaderValue With {.NoCache = True}
                        Using Response = Await Client.SendAsync(Request, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(False)
                            Return New ProbeResult(Response.StatusCode)
                        End Using
                    End Using
                Catch ex As HttpRequestException
                    Failure = ex
                Catch ex As OperationCanceledException
                    Failure = ex
                End Try
                If Attempt < Attempts Then Await Task.Delay(250).ConfigureAwait(False)
            Next
        End Using
        Return New ProbeResult(Failure)
    End Function
End Module
