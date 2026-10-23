Imports System.IO
Imports System.Linq
Imports System.Text

''' <summary>仅在文件系统边界使用扩展路径；校验、返回值与界面继续使用普通路径。</summary>
Friend NotInheritable Class SduFileSystem
    Private Sub New()
    End Sub

    Friend Shared Function ToIoPath(Value As String) As String
        Dim Full = Path.GetFullPath(Value)
        If Full.StartsWith("\\?\", StringComparison.Ordinal) Then Return Full
        If Full.StartsWith("\\", StringComparison.Ordinal) Then Return "\\?\UNC\" & Full.Substring(2)
        Return "\\?\" & Full
    End Function

    Friend Shared Function ToDisplayPath(Value As String) As String
        If Value.StartsWith("\\?\UNC\", StringComparison.OrdinalIgnoreCase) Then Return "\\" & Value.Substring(8)
        If Value.StartsWith("\\?\", StringComparison.Ordinal) Then Return Value.Substring(4)
        Return Value
    End Function

    Friend NotInheritable Class File
        Friend Shared Function Exists(Name As String) As Boolean
            Return IO.File.Exists(ToIoPath(Name))
        End Function
        Friend Shared Function ReadAllText(Name As String, Encoding As Encoding) As String
            Return IO.File.ReadAllText(ToIoPath(Name), Encoding)
        End Function
        Friend Shared Sub WriteAllText(Name As String, Text As String, Encoding As Encoding)
            IO.File.WriteAllText(ToIoPath(Name), Text, Encoding)
        End Sub
        Friend Shared Function GetAttributes(Name As String) As FileAttributes
            Return IO.File.GetAttributes(ToIoPath(Name))
        End Function
        Friend Shared Sub Move(Source As String, Destination As String)
            IO.File.Move(ToIoPath(Source), ToIoPath(Destination))
        End Sub
        Friend Shared Sub Delete(Name As String)
            IO.File.Delete(ToIoPath(Name))
        End Sub
        Friend Shared Function OpenRead(Name As String) As FileStream
            Return IO.File.OpenRead(ToIoPath(Name))
        End Function
        Friend Shared Function Length(Name As String) As Long
            Return New FileInfo(ToIoPath(Name)).Length
        End Function
    End Class

    Friend NotInheritable Class Directory
        Friend Shared Function Exists(Name As String) As Boolean
            Return IO.Directory.Exists(ToIoPath(Name))
        End Function
        Friend Shared Sub CreateDirectory(Name As String)
            IO.Directory.CreateDirectory(ToIoPath(Name))
        End Sub
        Friend Shared Function GetDirectories(Name As String) As String()
            Return IO.Directory.GetDirectories(ToIoPath(Name)).Select(Function(Child) ToDisplayPath(Child)).ToArray()
        End Function
        Friend Shared Function GetFiles(Name As String) As String()
            Return IO.Directory.GetFiles(ToIoPath(Name)).Select(Function(Child) ToDisplayPath(Child)).ToArray()
        End Function
        Friend Shared Sub Move(Source As String, Destination As String)
            IO.Directory.Move(ToIoPath(Source), ToIoPath(Destination))
        End Sub
        Friend Shared Sub Delete(Name As String, Recursive As Boolean)
            IO.Directory.Delete(ToIoPath(Name), Recursive)
        End Sub
    End Class
End Class
