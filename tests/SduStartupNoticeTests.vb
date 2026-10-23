Imports System.IO
Imports System.Reflection
Imports System.Threading
Imports System.Threading.Tasks

Module SduStartupNoticeTests
    Private Passed As Integer
    Public VersionBranchMain As String = "OpenSource"
    Public Dialogs As Integer
    Public FailDialog As Boolean

    Public Function MyMsgBox(Content As String, Title As String, Button As String) As Integer
        Interlocked.Increment(Dialogs)
        If FailDialog Then Throw New IOException("dialog fixture failure")
        Return 1
    End Function

    Private Sub Check(Name As String, Test As Action)
        Test()
        Passed += 1
        Console.WriteLine("PASS " & Name)
    End Sub

    Private Sub Assert(Condition As Boolean)
        If Not Condition Then Throw New Exception("Notice assertion failed")
    End Sub

    Private Sub NewSession()
        GetType(SduStartupNotice).GetField("ShownThisSession", BindingFlags.NonPublic Or BindingFlags.Static).SetValue(Nothing, 0)
        Dialogs = 0 : Logger.Warnings = 0
    End Sub

    Private Function ChildRun(FileName As String) As String
        Dim Info As New Diagnostics.ProcessStartInfo(Assembly.GetExecutingAssembly().Location, "child " & ChrW(34) & FileName & ChrW(34)) With {
            .UseShellExecute = False, .CreateNoWindow = True, .RedirectStandardOutput = True}
        Using Child = Diagnostics.Process.Start(Info)
            If Not Child.WaitForExit(5000) Then
                Child.Kill()
                Throw New Exception("Notice fixture process timed out")
            End If
            Assert(Child.ExitCode = 0)
            Return Child.StandardOutput.ReadToEnd().Trim()
        End Using
    End Function

    Sub Main(Arguments As String())
        If Arguments.Length = 2 AndAlso Arguments(0) = "child" Then
            Settings.StateFile = Arguments(1)
            SduStartupNotice.ShowOnce()
            SduStartupNotice.ShowOnce()
            Console.WriteLine(Dialogs)
            Return
        End If
        Dim Root = Path.Combine(Path.GetTempPath(), "sdu-notice-test-" & Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(Root)
        Settings.StateFile = Path.Combine(Root, "seen.txt")
        Try
            Check("first launch shows and persists once", Sub()
                SduStartupNotice.ShowOnce() : SduStartupNotice.ShowOnce()
                Assert(Dialogs = 1 AndAlso File.Exists(Settings.StateFile))
            End Sub)
            Check("new session and application update keep acknowledgement", Sub()
                NewSession()
                SduStartupNotice.ShowOnce()
                Assert(Dialogs = 0)
            End Sub)
            Check("concurrent startup callbacks show one dialog", Sub()
                File.Delete(Settings.StateFile) : NewSession()
                Parallel.For(0, 20, Sub(Index) SduStartupNotice.ShowOnce())
                Assert(Dialogs = 1)
            End Sub)
            Check("other branches do not show or acknowledge", Sub()
                File.Delete(Settings.StateFile) : NewSession()
                VersionBranchMain = "Other"
                SduStartupNotice.ShowOnce()
                Assert(Dialogs = 0 AndAlso Not File.Exists(Settings.StateFile))
                VersionBranchMain = "OpenSource"
            End Sub)
            Check("failed dialog is not acknowledged", Sub()
                NewSession() : FailDialog = True
                SduStartupNotice.ShowOnce()
                Assert(Dialogs = 1 AndAlso Logger.Warnings = 1 AndAlso Not File.Exists(Settings.StateFile))
                FailDialog = False : NewSession()
                SduStartupNotice.ShowOnce()
                Assert(Dialogs = 1 AndAlso File.Exists(Settings.StateFile))
            End Sub)
            Check("storage failure does not repeat during same session", Sub()
                File.Delete(Settings.StateFile) : NewSession() : Settings.FailSaving = True
                SduStartupNotice.ShowOnce() : SduStartupNotice.ShowOnce()
                Assert(Dialogs = 1 AndAlso Logger.Warnings = 1 AndAlso Not File.Exists(Settings.StateFile))
                Settings.FailSaving = False
            End Sub)
            Check("first real process persists acknowledgement", Sub() Assert(ChildRun(Settings.StateFile) = "1"))
            Check("second real process does not display notice", Sub() Assert(ChildRun(Settings.StateFile) = "0"))
            Console.WriteLine("Startup notice: " & Passed & " passed")
        Catch ex As Exception
            Console.Error.WriteLine(ex)
            Environment.ExitCode = 1
        Finally
            Directory.Delete(Root, True)
        End Try
    End Sub
End Module

Public Class Settings
    Public Shared StateFile As String
    Public Shared FailSaving As Boolean
    Public Shared Function [Get](Of T)(Key As String) As T
        Return CType(CObj(File.Exists(StateFile)), T)
    End Function
    Public Shared Sub [Set](Key As String, Value As Boolean)
        If FailSaving Then Throw New IOException("storage fixture failure")
        File.WriteAllText(StateFile, Value.ToString())
    End Sub
End Class

Public Class Logger
    Public Shared Warnings As Integer
    Public Shared Sub Warn(Failure As Exception, Message As String)
        Interlocked.Increment(Warnings)
    End Sub
End Class
