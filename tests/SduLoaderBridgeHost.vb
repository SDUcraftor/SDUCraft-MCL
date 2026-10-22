Imports System.Runtime.CompilerServices

<Assembly: System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.8")>

' Host substitutes only. Actual state transitions, cancellation, worker threads
' and parent/child scheduling come from ModLoader.vb, extracted by the runner.
Public Interface ILoadingTrigger
    ReadOnly Property IsLoader As Boolean
    Property LoadingState As MyLoading.MyLoadingState
    Event LoadingStateChanged(NewState As MyLoading.MyLoadingState, OldState As MyLoading.MyLoadingState)
    Event ProgressChanged(NewProgress As Double, OldProgress As Double)
End Interface

Public Class MyLoading
    Public Enum MyLoadingState
        Run
        [Error]
        [Stop]
    End Enum
End Class

Public Class ProgressProvider
    Public Event ProgressChanged(Value As Double)
    Public Class RawProgress
        Public actual As Double
        Public skiped As Double
    End Class
    Public Function GetRaw() As RawProgress
        Return New RawProgress()
    End Function
    Public Sub [Set](Value As Double)
        RaiseEvent ProgressChanged(Value)
    End Sub
End Class

Public Class Settings
    Public Shared Function [Get](Of T)(Key As String) As T
        Return CType(CObj(False), T)
    End Function
End Class

Public Enum LogLevel
    Info
    [Error]
End Enum
Public Enum LogBehavior
    Toast
End Enum
Public Class Logger
    Public Shared Sub Info(Message As String)
    End Sub
    Public Shared Sub Log(Message As String, Level As LogLevel)
    End Sub
    Public Shared Sub Warn(Message As String)
    End Sub
    Public Shared Sub Warn(ErrorValue As Exception, Message As String)
    End Sub
    Public Shared Sub [Error](ErrorValue As Exception, Message As String, Optional Behavior As LogBehavior = LogBehavior.Toast)
    End Sub
    Public Shared Sub Trace(Message As String)
    End Sub
    Public Shared Sub Trace(Message As Func(Of String))
    End Sub
    Public Shared Sub Trace(ErrorValue As Exception, Message As String)
    End Sub
End Class

Public Class GeneralUtils
    Public Shared Function IsGenericInstanceOf(Value As Object, Definition As Type) As Boolean
        Return Value.GetType().IsGenericType AndAlso Value.GetType().GetGenericTypeDefinition() Is Definition
    End Function
End Class

Public Class TestWindow
    Public BtnExtraDownload As New TestDownloadButton()
End Class
Public Class TestDownloadButton
    Public Sub ShowRefresh()
    End Sub
End Class
Public Module TestHost
    Private NextId As Integer
    Public FrmMain As New TestWindow()
    Public Function GetUuid() As Integer
        Return Interlocked.Increment(NextId)
    End Function
    Public Function GetTimeMs() As Long
        Return Environment.TickCount
    End Function
    Public Function RandomInteger(Lower As Integer, Upper As Integer) As Integer
        Return Lower
    End Function
    Public Sub RunInUi(Work As Action)
        Work()
    End Sub
    Public Sub RunInUiWait(Work As Action)
        Work()
    End Sub
    Public Sub RunInThread(Work As Action)
        Dim Worker As New Thread(Sub() Work()) With {.IsBackground = True}
        Worker.Start()
    End Sub
    <Extension>
    Public Function IsCanceled(ErrorValue As Exception) As Boolean
        While ErrorValue IsNot Nothing
            If TypeOf ErrorValue Is OperationCanceledException OrElse TypeOf ErrorValue Is ThreadInterruptedException Then Return True
            ErrorValue = ErrorValue.InnerException
        End While
        Return False
    End Function
End Module

' These branches are referenced by LoaderCombo.Update but never used by tests.
Public Class NetFile
End Class
Public Class LoaderDownload
    Inherits LoaderBase
    Public Input As List(Of NetFile)
    Public Overrides Sub Start(Optional Input As Object = Nothing, Optional IsForceRestart As Boolean = False)
        Throw New NotSupportedException()
    End Sub
    Public Overrides Sub Cancel()
    End Sub
    Public Overrides Sub Failed(ErrorValue As Exception)
    End Sub
End Class
Public Class LoaderWorker
    Inherits LoaderBase
    Public Overrides Sub Start(Optional Input As Object = Nothing, Optional IsForceRestart As Boolean = False)
        Throw New NotSupportedException()
    End Sub
    Public Overrides Sub Cancel()
    End Sub
    Public Overrides Sub Failed(ErrorValue As Exception)
    End Sub
End Class
