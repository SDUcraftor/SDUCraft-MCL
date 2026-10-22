Imports System.IO
Imports System.IO.Compression

''' <summary>包结构查找与手动安装导出；查找始终返回真实 ZIP 条目。</summary>
Public Module SduPackageFiles
    Public Function FindEntry(Archive As ZipArchive, Name As String) As ZipArchiveEntry
        Dim Normalized = Name.Replace("\"c, "/"c)
        Return Archive.Entries.FirstOrDefault(Function(Entry) Entry.FullName.Replace("\"c, "/"c).
            Equals(Normalized, StringComparison.OrdinalIgnoreCase))
    End Function

    Public Function FindMarker(Archive As ZipArchive, ParamArray Names As String()) As ZipArchiveEntry
        For Each Name In Names
            Dim Root = FindEntry(Archive, Name)
            If Root IsNot Nothing Then Return Root
            For Each Entry In Archive.Entries
                Dim Parts = Entry.FullName.Replace("\"c, "/"c).Split("/"c)
                If Parts.Length = 2 AndAlso Parts(1).Equals(Name, StringComparison.OrdinalIgnoreCase) Then Return Entry
            Next
        Next
        Return Nothing
    End Function

    ''' <summary>每次导出使用独立文件名，保留原后缀，避免误用或覆盖同名旧包。</summary>
    Public Function ExportForManualInstall(Source As String, Folder As String, InstanceName As String) As String
        Directory.CreateDirectory(Folder)
        Dim SafeName = SduBareClientInstaller.SafeInstanceName(InstanceName, "SDUcraft-Pack")
        Dim Extension = Path.GetExtension(Source)
        If String.IsNullOrEmpty(Extension) Then Extension = ".zip"
        Dim Target = Path.Combine(Folder, SafeName & "-" & Guid.NewGuid().ToString("N") & Extension)
        File.Copy(Source, Target, False)
        Return Target
    End Function
End Module
