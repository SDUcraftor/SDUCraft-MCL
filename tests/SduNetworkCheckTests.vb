Imports System.IO
Imports System.Net
Imports System.Net.Http
Imports System.Reflection
Imports System.Threading.Tasks

Module SduNetworkCheckTests
    Private Passed As Integer

    Sub Main()
        Try
            For Each Status In {HttpStatusCode.OK, HttpStatusCode.NoContent, HttpStatusCode.Redirect, HttpStatusCode.Forbidden, HttpStatusCode.ServiceUnavailable}
                Dim Handler As New FakeHandler(Function(Request, Token)
                    Check(Request.RequestUri.AbsoluteUri = SduNetworkCheck.WebsiteUrl, "Probe changed target")
                    Check(Request.Method = HttpMethod.Get AndAlso Request.RequestUri.Scheme = "https", "Probe did not use HTTPS GET")
                    Return Task.FromResult(New HttpResponseMessage(Status))
                End Function)
                Dim Result = SduNetworkCheck.CheckWebsiteAsync(Handler).GetAwaiter().GetResult()
                Check(Result.Reachable AndAlso Result.StatusCode.HasValue AndAlso Result.StatusCode.Value = Status AndAlso Handler.Calls = 1, "HTTP response was treated as campus network failure")
                Pass("HTTP " & CInt(Status) & " proves connectivity without retry")
            Next

            Dim RetryHandler As New FakeHandler(Function(Request, Token)
                Return Task.FromResult(New HttpResponseMessage(HttpStatusCode.OK))
            End Function)
            RetryHandler.FailuresRemaining = 1
            Check(SduNetworkCheck.CheckWebsiteAsync(RetryHandler).GetAwaiter().GetResult().Reachable, "Transient failure did not recover")
            Check(RetryHandler.Calls = 2, "Transient failure was not retried once")
            Pass("transient connection failure succeeds on retry")

            Dim BrokenHandler As New FakeHandler(Function(Request, Token)
                Throw New HttpRequestException("offline-fixture")
            End Function)
            Dim Failed = SduNetworkCheck.CheckWebsiteAsync(BrokenHandler).GetAwaiter().GetResult()
            Check(Not Failed.Reachable AndAlso TypeOf Failed.Failure Is HttpRequestException AndAlso BrokenHandler.Calls = 2, "Persistent failure not reported after two attempts")
            Pass("persistent connection failure stops after two attempts")

            Dim SlowHandler As New FakeHandler(Async Function(Request, Token)
                Await Task.Delay(Timeout.Infinite, Token)
                Return New HttpResponseMessage(HttpStatusCode.OK)
            End Function)
            Dim Timer = Diagnostics.Stopwatch.StartNew()
            Dim TimedOut = SduNetworkCheck.CheckWebsiteAsync(SlowHandler, TimeoutMilliseconds:=40).GetAwaiter().GetResult()
            Check(Not TimedOut.Reachable AndAlso TypeOf TimedOut.Failure Is OperationCanceledException AndAlso SlowHandler.Calls = 2, "Timeout was not canceled and retried")
            Check(Timer.ElapsedMilliseconds < 3000, "Timeout did not bound the check")
            Pass("timeouts cancel the request and stop after one retry")

            Dim Body As New UnreadableContent()
            Dim HeadersHandler As New FakeHandler(Function(Request, Token)
                Return Task.FromResult(New HttpResponseMessage(HttpStatusCode.OK) With {.Content = Body})
            End Function)
            Check(SduNetworkCheck.CheckWebsiteAsync(HeadersHandler).GetAwaiter().GetResult().Reachable, "Response body was downloaded")
            Check(Body.WasDisposed, "Response body was not disposed")
            Pass("probe reads headers only and disposes the response")

            ResetStartup()
            Dim Probes As Integer
            Dim FailureProbe As Func(Of Task(Of SduNetworkCheck.ProbeResult)) = Function()
                Probes += 1
                Return Task.FromResult(New SduNetworkCheck.ProbeResult(New HttpRequestException("offline")))
            End Function
            SduNetworkCheck.StartCheck(FailureProbe)
            SduNetworkCheck.StartCheck(FailureProbe)
            Check(Work.Count = 1 AndAlso Probes = 0 AndAlso Warnings = 0, "Startup blocked or scheduled duplicate checks")
            Work.Dequeue().Invoke()
            Check(Probes = 1 AndAlso Warnings = 1, "Offline startup did not show exactly one warning")
            Check(WarningTitle = "需要连接校园网" AndAlso WarningText.Contains("山东大学校园网"), "Warning omitted campus network instructions")
            Check(WarningText.Contains("官网暂时不可用"), "Warning claimed connectivity failure proves off-campus access")
            Pass("startup returns before probing and warns only once per process")

            ResetStartup()
            SduNetworkCheck.StartCheck(Function() Task.FromResult(New SduNetworkCheck.ProbeResult(HttpStatusCode.OK)))
            Work.Dequeue().Invoke()
            Check(Warnings = 0, "Online startup displayed campus warning")
            Pass("online startup has no dialog")

            ResetStartup()
            SduNetworkCheck.StartCheck(Function() Task.FromException(Of SduNetworkCheck.ProbeResult)(New InvalidOperationException("internal-fixture")))
            Work.Dequeue().Invoke()
            Check(Warnings = 0, "Unexpected code error was mislabeled as network failure")
            Pass("unexpected check errors do not interrupt startup or mislabel network")
            Console.WriteLine("PASS: " & Passed & " network-check scenarios (production source, no external requests).")
        Catch ex As Exception
            Console.Error.WriteLine(ex)
            Environment.ExitCode = 1
        End Try
    End Sub

    Private Class FakeHandler
        Inherits HttpMessageHandler
        Private ReadOnly Respond As Func(Of HttpRequestMessage, CancellationToken, Task(Of HttpResponseMessage))
        Public Calls As Integer
        Public FailuresRemaining As Integer
        Public Sub New(Reply As Func(Of HttpRequestMessage, CancellationToken, Task(Of HttpResponseMessage)))
            Respond = Reply
        End Sub
        Protected Overrides Function SendAsync(Request As HttpRequestMessage, Token As CancellationToken) As Task(Of HttpResponseMessage)
            Calls += 1
            If FailuresRemaining > 0 Then
                FailuresRemaining -= 1
                Throw New HttpRequestException("transient-fixture")
            End If
            Return Respond(Request, Token)
        End Function
    End Class

    Private Class UnreadableContent
        Inherits HttpContent
        Public WasDisposed As Boolean
        Protected Overrides Function SerializeToStreamAsync(Stream As Stream, Context As TransportContext) As Task
            Throw New Exception("Probe must not download HTML")
        End Function
        Protected Overrides Function TryComputeLength(ByRef Length As Long) As Boolean
            Return False
        End Function
        Protected Overrides Sub Dispose(Disposing As Boolean)
            WasDisposed = True
            MyBase.Dispose(Disposing)
        End Sub
    End Class

    Private Sub ResetStartup()
        'Simulate a fresh launcher process for each startup outcome.
        GetType(SduNetworkCheck).GetField("Started", BindingFlags.NonPublic Or BindingFlags.Static).SetValue(Nothing, 0)
        Work.Clear()
        Warnings = 0
    End Sub
    Private Sub Check(Condition As Boolean, Message As String)
        If Not Condition Then Throw New Exception(Message)
    End Sub
    Private Sub Pass(Name As String)
        Passed += 1
        Console.WriteLine("PASS: " & Name)
    End Sub
End Module

'Only thread dispatch, logging and the dialog host are substituted.
Public Module NetworkTestHost
    Public Work As New Queue(Of Action)()
    Public Warnings As Integer
    Public WarningTitle, WarningText As String
    Public Sub RunInNewThread(Action As Action, Name As String, Priority As ThreadPriority)
        Work.Enqueue(Action)
    End Sub
    Public Function MyMsgBox(Caption As String, Title As String, Button1 As String, IsWarn As Boolean) As Integer
        Warnings += 1
        WarningTitle = Title
        WarningText = Caption
        Return 1
    End Function
End Module
Public Class Logger
    Public Shared Sub Info(Message As String)
    End Sub
    Public Shared Sub Warn(ErrorValue As Exception, Message As String)
    End Sub
End Class
