''' <summary>
''' 保存和读取“SDUcraft 下载讯息已读”状态。
''' 状态文件使用逗号分隔的下载项 Id，放在 PCL 数据目录中。
''' </summary>
Public Module SduDownloadReadStore

    Private ReadOnly StoreFilePath As String = Paths.Base & "PCL\SduDownloadRead.txt"

    Public Function ReadIds() As HashSet(Of String)
        Dim Result As New HashSet(Of String)
        Const MaxStoreFileLength As Integer = 1024 * 1024

        Try
            If Not System.IO.File.Exists(StoreFilePath) Then Return Result
            If New System.IO.FileInfo(StoreFilePath).Length > MaxStoreFileLength Then
                Logger.Warn("SDUcraft 已读记录文件超过安全上限，已忽略本次读取")
                Return Result
            End If

            For Each Id In System.IO.File.ReadAllText(StoreFilePath).Split(","c)
                Id = Id.Trim()
                Dim IsValid = Id.Length <= 128 AndAlso
                              System.Text.RegularExpressions.Regex.IsMatch(Id, "^[A-Za-z0-9_.:-]+$")
                If IsValid Then Result.Add(Id)
            Next
        Catch ex As Exception
            Logger.Warn(ex, "读取 SDUcraft 下载面板已读记录失败")
        End Try

        Return Result
    End Function

    Public Sub SaveIds(Ids As HashSet(Of String))
        Try
            Dim Content = String.Join(",", Ids)
            System.IO.File.WriteAllText(StoreFilePath, Content)
        Catch ex As Exception
            Logger.Warn(ex, "保存 SDUcraft 下载面板已读记录失败")
        End Try
    End Sub

End Module
