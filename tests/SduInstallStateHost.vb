Imports System.Runtime.CompilerServices

'Capture only host side effects. The notification decisions are production McInstallState.
Public Enum HintType
    Green
    Red
    Blue
End Enum
Public Enum LoaderFolderRunType
    ForceRun
End Enum
Public Module InstallStateTestHost
    Public McFolderSelected As String = "unused-test-folder\"
    Public McInstanceListLoader As Object
    Public HintCount, AlertCount, CleanupCount, RefreshCount As Integer
    Public Sub ResetNotifications()
        HintCount = 0
        AlertCount = 0
        CleanupCount = 0
        RefreshCount = 0
    End Sub
    Public Sub Hint(Message As String, Kind As HintType)
        HintCount += 1
    End Sub
    Public Sub MyMsgBox(Message As String, Title As String, Optional IsWarn As Boolean = False)
        AlertCount += 1
    End Sub
    Public Sub WriteIni(File As String, Key As String, Value As String)
    End Sub
    Public Sub McInstallFailedClearFolder(Loader As LoaderBase)
        CleanupCount += 1
    End Sub
    Public Sub LoaderFolderRun(Loader As Object, Folder As String, Mode As LoaderFolderRunType, MaxDepth As Integer, ExtraPath As String)
        RefreshCount += 1
    End Sub
    <Extension>
    Public Function GetDisplay(ErrorValue As Exception, Detailed As Boolean) As String
        Return ErrorValue.Message
    End Function
End Module
