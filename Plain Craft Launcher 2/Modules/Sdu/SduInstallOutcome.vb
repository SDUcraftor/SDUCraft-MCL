''' <summary>结构无法自动安装：保留失败状态，让外层统一导出并提示。</summary>
Public Class SduManualInstallRequiredException
    Inherits InvalidOperationException
    Public Sub New(Message As String)
        MyBase.New(Message)
    End Sub
End Class

''' <summary>安装器已经展示过结果，页面只记录日志，避免重复提示。</summary>
Public Class SduInstallReportedException
    Inherits Exception
    Public Sub New(Inner As Exception)
        MyBase.New(Inner.Message, Inner)
    End Sub
End Class
