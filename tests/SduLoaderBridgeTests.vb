Imports System.Diagnostics

Module SduLoaderBridgeTests
    Private Passed As Integer

    Sub Main()
        Try
            WaitingAndProgress()
            FailurePropagation()
            ManualInstallIsNotSuccess()
            ParentCancellation()
            ChildCancellation()
            AlreadyFinishedDoesNotRestart()
            AlreadyFailedDoesNotRestart()
            InterruptedWaitCancelsChild()
            RejectUnstartedChild()
            InstallStateNotifications()
            Console.WriteLine("PASS: " & Passed & " loader lifecycle scenarios (actual LoaderBase/LoaderTask/LoaderCombo).")
        Catch ErrorValue As Exception
            Console.Error.WriteLine(ErrorValue.ToString())
            Environment.ExitCode = 1
        End Try
    End Sub

    Private Class InstallFixture
        Public Started As New ManualResetEvent(False)
        Public Release As New ManualResetEvent(False)
        Public Exited As New ManualResetEvent(False)
        Public Runs As Integer
        Public Work As LoaderTask(Of String, Integer)
        Public Child As LoaderCombo(Of String)
        Public Owner As LoaderTask(Of Integer, Integer)
        Public Outer As LoaderCombo(Of String)

        Sub New(Optional Failure As Exception = Nothing)
            Work = New LoaderTask(Of String, Integer)("real install", Sub(Task)
                Interlocked.Increment(Runs)
                Try
                    Task.Progress = 0.4
                    Started.Set()
                    Release.WaitOne()
                    If Failure IsNot Nothing Then Throw Failure
                Finally
                    Exited.Set()
                End Try
            End Sub)
            Child = New LoaderCombo(Of String)("child installation", {Work})
            Child.Start("original-instance-input")
            Check(Started.WaitOne(5000), "Child did not start")
        End Sub

        Public Sub StartOuter()
            Owner = New LoaderTask(Of Integer, Integer)("adapter", Sub(Task) SduLoaderBridge.WaitForCompletion(Child, Task))
            Outer = New LoaderCombo(Of String)("outer installation", {Owner})
            Outer.Start("outer-instance-input")
        End Sub
    End Class

    Private Sub WaitingAndProgress()
        Dim Fixture As New InstallFixture()
        Fixture.StartOuter()
        Try
            UntilTrue(Function() Fixture.Owner.Progress >= 0.4, "Child progress was not forwarded")
            Check(Fixture.Outer.State = LoadState.Loading, "Outer finished while child was blocked")
            Check(Fixture.Child.Input = "original-instance-input", "Child input changed")
            Fixture.Release.Set()
            UntilTrue(Function() Fixture.Outer.State = LoadState.Finished, "Outer did not finish after child")
            Check(Fixture.Runs = 1, "Child restarted")
            Check(Fixture.Owner.Progress = 1, "Successful progress did not reach one")
            Pass("outer waits and forwards progress")
        Finally
            Fixture.Release.Set()
            Fixture.Outer.Cancel()
        End Try
    End Sub

    Private Sub FailurePropagation()
        Dim Failure As New InvalidOperationException("installation-failure-sentinel")
        Dim Fixture As New InstallFixture(Failure)
        Fixture.StartOuter()
        Fixture.Release.Set()
        UntilTrue(Function() Fixture.Outer.State = LoadState.Failed, "Child failure did not fail outer")
        Dim Current As Exception = Fixture.Outer.Error
        While Current IsNot Nothing AndAlso Current IsNot Failure
            Current = Current.InnerException
        End While
        Check(Current Is Failure, "Original failure was not preserved")
        Pass("child error reaches outer with original exception")
    End Sub

    Private Sub ManualInstallIsNotSuccess()
        Dim Failure As New SduManualInstallRequiredException("manual-install-required")
        Dim Fixture As New InstallFixture(Failure)
        Fixture.StartOuter()
        Fixture.Release.Set()
        UntilTrue(Function() Fixture.Outer.State = LoadState.Failed, "Manual fallback was marked as installed or canceled")
        Try
            SduLoaderBridge.WaitForCompletion(Fixture.Outer)
            Throw New Exception("Manual fallback returned normally")
        Catch ErrorValue As Exception
            Check(ErrorValue.GetBaseException() Is Failure, "Manual fallback lost its explicit reason")
            Dim Reported As New SduInstallReportedException(ErrorValue)
            Check(Reported.InnerException Is ErrorValue AndAlso Reported.GetBaseException() Is Failure, "Reported result lost original error")
        End Try
        Pass("manual fallback fails outer task with an explicit reason")
    End Sub

    Private Sub ParentCancellation()
        Dim Fixture As New InstallFixture()
        Fixture.StartOuter()
        UntilTrue(Function() Fixture.Owner.Progress >= 0.4, "Owner did not begin waiting")
        Fixture.Outer.Cancel()
        UntilTrue(Function() Fixture.Child.State = LoadState.Canceled, "Parent cancel did not cancel child")
        Check(Fixture.Exited.WaitOne(5000), "Child worker survived parent cancellation")
        UntilTrue(Function() Fixture.Owner.State = LoadState.Canceled, "Owner survived cancellation")
        Pass("parent cancel stops actual child worker")
    End Sub

    Private Sub ChildCancellation()
        Dim Fixture As New InstallFixture()
        Fixture.StartOuter()
        Fixture.Child.Cancel()
        UntilTrue(Function() Fixture.Outer.State = LoadState.Canceled, "Child cancel did not cancel outer")
        Check(Fixture.Exited.WaitOne(5000), "Canceled child worker survived")
        Pass("child cancel reaches outer")
    End Sub

    Private Sub AlreadyFinishedDoesNotRestart()
        Dim Fixture As New InstallFixture()
        Fixture.Release.Set()
        UntilTrue(Function() Fixture.Child.State = LoadState.Finished, "Child did not finish")
        SduLoaderBridge.WaitForCompletion(Fixture.Child)
        Check(Fixture.Child.State = LoadState.Finished, "Waiting restarted a finished combo")
        Check(Fixture.Runs = 1, "Finished child ran twice")
        Check(Fixture.Child.Input = "original-instance-input", "Finished child input was overwritten")
        Pass("already finished child is never restarted")
    End Sub

    Private Sub AlreadyFailedDoesNotRestart()
        Dim Fixture As New InstallFixture(New Exception("already-failed-sentinel"))
        Fixture.Release.Set()
        UntilTrue(Function() Fixture.Child.State = LoadState.Failed, "Child did not fail")
        Dim Observed As Boolean
        Try
            SduLoaderBridge.WaitForCompletion(Fixture.Child)
        Catch ErrorValue As Exception
            Observed = ErrorValue.ToString().Contains("already-failed-sentinel")
        End Try
        Check(Observed AndAlso Fixture.Runs = 1, "Already failed child restarted or lost error")
        Pass("already failed child reports error without restart")
    End Sub

    Private Sub InterruptedWaitCancelsChild()
        Dim Fixture As New InstallFixture()
        Dim Done As New ManualResetEvent(False)
        Dim Observed As Exception = Nothing
        Dim Waiter As New Thread(Sub()
            Try
                SduLoaderBridge.WaitForCompletion(Fixture.Child)
            Catch ErrorValue As Exception
                Observed = ErrorValue
            Finally
                Done.Set()
            End Try
        End Sub) With {.IsBackground = True}
        Waiter.Start()
        UntilTrue(Function() (Waiter.ThreadState And System.Threading.ThreadState.WaitSleepJoin) <> 0, "Waiter did not block")
        Waiter.Interrupt()
        Check(Done.WaitOne(5000), "Interrupted waiter did not exit")
        Check(TypeOf Observed Is ThreadInterruptedException, "Thread interruption was swallowed")
        UntilTrue(Function() Fixture.Child.State = LoadState.Canceled, "Interrupted waiter left child running")
        Check(Fixture.Exited.WaitOne(5000), "Child worker survived interrupted waiter")
        Pass("interrupted wait cancels child")
    End Sub

    Private Sub RejectUnstartedChild()
        Dim Child As New LoaderCombo(Of String)("not started", New List(Of LoaderBase)())
        Dim Observed As Boolean
        Try
            SduLoaderBridge.WaitForCompletion(Child)
        Catch ErrorValue As InvalidOperationException
            Observed = True
        End Try
        Check(Observed, "Unstarted child was silently started or waited forever")
        Pass("unstarted child rejected")
    End Sub

    Private Sub InstallStateNotifications()
        For Each State In {LoadState.Loading, LoadState.Finished, LoadState.Failed, LoadState.Canceled}
            Dim Loader As New LoaderCombo(Of String)("installation", New List(Of LoaderBase)())
            Loader.Error = New Exception("failure")
            Loader.State = State
            ResetNotifications()
            McInstallState(Loader, ShowNotifications:=False)
            Check(HintCount = 0 AndAlso AlertCount = 0, "SDU child emitted a duplicate notification")
            Check(CleanupCount = If(State = LoadState.Loading, 0, 1) AndAlso RefreshCount = CleanupCount, "Silencing notifications skipped cleanup or instance refresh")
            ResetNotifications()
            McInstallState(Loader)
            Check(AlertCount = If(State = LoadState.Failed, 1, 0), "Default failure alert changed")
            Check(HintCount = If(State = LoadState.Finished OrElse State = LoadState.Canceled, 1, 0), "Default completion/cancel hint changed")
            Pass("silent child and normal installation notifications: " & State.ToString())
        Next
    End Sub

    Private Sub UntilTrue(Condition As Func(Of Boolean), Message As String)
        Dim Timer = Stopwatch.StartNew()
        While Not Condition()
            If Timer.ElapsedMilliseconds > 5000 Then Throw New Exception(Message)
            Thread.Sleep(10)
        End While
    End Sub
    Private Sub Check(Condition As Boolean, Message As String)
        If Not Condition Then Throw New Exception(Message)
    End Sub
    Private Sub Pass(Name As String)
        Passed += 1
        Console.WriteLine("PASS: " & Name)
    End Sub
End Module
