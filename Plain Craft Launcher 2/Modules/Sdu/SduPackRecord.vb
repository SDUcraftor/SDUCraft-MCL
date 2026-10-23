Imports System.IO
Imports System.Text
Imports System.Globalization
Imports Newtonsoft.Json.Linq

''' <summary>官网整合包身份与已成功安装的远端时间；不以本机下载时间代替包版本。</summary>
Friend Module SduPackRecord
    Friend Function Read(InstanceFolder As String) As JObject
        Dim File = Path.Combine(InstanceFolder, "PCL", "SduPack.json")
        If Not SduFileSystem.File.Exists(File) Then Return Nothing
        If SduFileSystem.File.Length(File) > 64 * 1024 Then Throw New InvalidDataException("整合包更新记录超过大小上限。")
        Return JObject.Parse(SduFileSystem.File.ReadAllText(File, Encoding.UTF8))
    End Function

    Friend Sub Save(InstanceFolder As String, Item As SduGameDownload)
        If String.IsNullOrWhiteSpace(Item.Id) Then Return '手动导入的包不能伪造官网身份。
        Dim Folder = Path.Combine(InstanceFolder, "PCL")
        SduBareClientInstaller.RejectReparseParents(InstanceFolder, Folder)
        SduFileSystem.Directory.CreateDirectory(Folder)
        Dim Record As New JObject From {
            {"sduProjectId", Item.Id}, {"projectModified", Item.ModifyDate}, {"published", Item.PublishDate},
            {"fileModified", If(Item.PreferredFile?.ModifyDate, "")}, {"version", Item.Version},
            {"installedAt", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture)}}
        Dim File = Path.Combine(Folder, "SduPack.json")
        SduBareClientInstaller.RejectReparseParents(InstanceFolder, File)
        Dim Temp = File & "." & Guid.NewGuid().ToString("N") & ".tmp"
        Try
            SduFileSystem.File.WriteAllText(Temp, Record.ToString(), New UTF8Encoding(False))
            If SduFileSystem.File.Exists(File) Then
                IO.File.Replace(SduFileSystem.ToIoPath(Temp), SduFileSystem.ToIoPath(File), Nothing)
            Else
                SduFileSystem.File.Move(Temp, File)
            End If
        Finally
            If SduFileSystem.File.Exists(Temp) Then SduFileSystem.File.Delete(Temp)
        End Try
    End Sub

    Friend Function HasUpdate(Installed As JObject, Latest As SduGameDownload) As Boolean
        If Installed Is Nothing OrElse CStr(Installed("sduProjectId")) <> Latest.Id Then Return False
        Dim Remote = Latest.LatestTime
        If Not Remote.HasValue Then Return False
        Dim Local = SduDownloadItem.NewestTime(CStr(Installed("projectModified")), CStr(Installed("fileModified")), CStr(Installed("published")))
        '旧版只有 ID 的标记没有可靠基线，需要成功更新一次后才能确认已是最新。
        Return Not Local.HasValue OrElse Remote.Value > Local.Value
    End Function

End Module
