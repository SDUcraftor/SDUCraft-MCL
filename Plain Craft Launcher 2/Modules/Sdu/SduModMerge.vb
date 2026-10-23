Imports System.IO
Imports System.IO.Compression
Imports System.Text
Imports System.Text.RegularExpressions
Imports System.Threading
Imports System.Collections.Concurrent
Imports Newtonsoft.Json.Linq

''' <summary>所有整合包格式共用的 mods 合并事务；目标配置不参与复制。</summary>
Friend Module SduModMerge
    Private ReadOnly Locks As New ConcurrentDictionary(Of String, Object)(StringComparer.OrdinalIgnoreCase)

    Friend Function Apply(McRoot As String, InstanceFolder As String, IncomingMods As String, Token As CancellationToken) As SduBareClientInstaller.InstallResult
        Dim Root = Path.GetFullPath(McRoot)
        Dim Target = Path.GetFullPath(InstanceFolder).TrimEnd(Path.DirectorySeparatorChar)
        Dim Name = Path.GetFileName(Target)
        SduBareClientInstaller.ValidateSegment(Name)
        If Not String.Equals(Target, SduBareClientInstaller.ChildPath(Root, "versions/" & Name), StringComparison.OrdinalIgnoreCase) Then Throw New IOException("无效的更新实例路径。")
        SyncLock Locks.GetOrAdd(Target, Function(Key) New Object())
            SduBareClientInstaller.RejectReparseParents(Root, Target)
            If Not SduFileSystem.File.Exists(Path.Combine(Target, Name & ".json")) Then Throw New IOException("更新目标不是有效实例：" & Name)
            Dim Current = Path.Combine(Target, "mods")
            SduBareClientInstaller.RejectReparseParents(Root, Current)
            If SduFileSystem.File.Exists(Current) Then Throw New IOException("mods 路径被文件占用。")
            Dim Incoming = EnumerateSafe(IncomingMods, Token)
            Dim Existing = EnumerateSafe(Current, Token)
            Token.ThrowIfCancellationRequested()
            If Incoming.Count = 0 Then Return New SduBareClientInstaller.InstallResult With {.InstanceFolder = Target & Path.DirectorySeparatorChar, .UpdatedMods = True}
            If Incoming.Sum(Function(File) SduFileSystem.File.Length(File)) > 5L * 1024 * 1024 * 1024 Then Throw New InvalidDataException("新版 mods 体积超过 5GB 上限。")
            Dim NewIds As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
            For Each File In Incoming
                For Each Id In ReadModIds(File)
                    If NewIds.ContainsKey(Id) Then Throw New InvalidDataException("新版包包含重复模组 ID：" & Id)
                    NewIds.Add(Id, File)
                Next
            Next
            Dim Replaced As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
            Dim Disabled As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
            For Each File In Existing
                Dim OldIds = ReadModIds(File)
                Dim Matches = OldIds.Where(Function(Id) NewIds.ContainsKey(Id)).Select(Function(Id) NewIds(Id)).Distinct().ToList()
                If Matches.Count = 0 Then Continue For
                If OldIds.Any(Function(Id) Not NewIds.ContainsKey(Id)) Then Throw New InvalidDataException("旧 JAR 同时包含新版未提供的其他模组，已停止更新以保留这些内容：" & Path.GetFileName(File))
                Replaced.Add(File)
                If File.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase) OrElse File.EndsWith(".disable", StringComparison.OrdinalIgnoreCase) Then
                    For Each Match In Matches
                        Disabled(Match) = If(File.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase), ".disabled", ".disable")
                    Next
                End If
            Next
            Dim Work = SduBareClientInstaller.ChildPath(Root, ".sdu-merge-" & Guid.NewGuid().ToString("N"))
            Dim Prepared = Path.Combine(Work, "mods")
            SduFileSystem.Directory.CreateDirectory(Prepared)
            Try
                CopyDirectorySkeleton(Current, Current, Prepared, Token)
                For Each File In Existing
                    If Not Replaced.Contains(File) Then CopyFile(File, Current, Prepared, Token)
                Next
                For Each File In Incoming
                    Dim Relative = File.Substring(IncomingMods.TrimEnd(Path.DirectorySeparatorChar).Length + 1)
                    Dim Suffix = If(Disabled.ContainsKey(File) AndAlso File.EndsWith(".jar", StringComparison.OrdinalIgnoreCase), Disabled(File), "")
                    Dim Destination = SduBareClientInstaller.ChildPath(Prepared, Relative & Suffix)
                    CopyTo(File, Destination, Token)
                Next
                Token.ThrowIfCancellationRequested()
                SduBareClientInstaller.RejectReparseParents(Root, Current)
                Dim Backup As String = Nothing
                If SduFileSystem.Directory.Exists(Current) Then
                    Backup = SduBareClientInstaller.ChildPath(Root, "PCL/SduModBackups/" & Name & "/" & Date.UtcNow.ToString("yyyyMMdd-HHmmss") & "-" & Guid.NewGuid().ToString("N") & "/mods")
                    SduBareClientInstaller.RejectReparseParents(Root, Backup)
                    SduFileSystem.Directory.CreateDirectory(Path.GetDirectoryName(Backup))
                End If
                SduBareClientInstaller.PublishMods(Prepared, Current, Backup, AddressOf SduFileSystem.Directory.Move)
                Return New SduBareClientInstaller.InstallResult With {.InstanceFolder = Target & Path.DirectorySeparatorChar, .UpdatedMods = True, .ModsBackup = Backup}
            Finally
                Try
                    If SduFileSystem.Directory.Exists(Work) Then SduFileSystem.Directory.Delete(Work, True)
                Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException
                    Diagnostics.Trace.TraceWarning("无法清理模组合并暂存目录 {0}：{1}", Work, ex.Message)
                End Try
            End Try
        End SyncLock
    End Function

    Private Sub CopyDirectorySkeleton(Root As String, Current As String, Prepared As String, Token As CancellationToken)
        If Not SduFileSystem.Directory.Exists(Current) Then Return
        For Each Folder In SduFileSystem.Directory.GetDirectories(Current)
            Token.ThrowIfCancellationRequested()
            SduBareClientInstaller.RejectReparseParents(Root, Folder)
            SduFileSystem.Directory.CreateDirectory(SduBareClientInstaller.ChildPath(Prepared, Folder.Substring(Root.Length + 1)))
            CopyDirectorySkeleton(Root, Folder, Prepared, Token)
        Next
    End Sub

    Private Sub CopyFile(Source As String, SourceRoot As String, DestinationRoot As String, Token As CancellationToken)
        CopyTo(Source, SduBareClientInstaller.ChildPath(DestinationRoot, Source.Substring(SourceRoot.TrimEnd(Path.DirectorySeparatorChar).Length + 1)), Token)
    End Sub

    Private Sub CopyTo(Source As String, Destination As String, Token As CancellationToken)
        Token.ThrowIfCancellationRequested()
        SduFileSystem.Directory.CreateDirectory(Path.GetDirectoryName(Destination))
        Using Input = SduFileSystem.File.OpenRead(Source), Output As New FileStream(SduFileSystem.ToIoPath(Destination), FileMode.Create, FileAccess.Write)
            Dim Buffer(65535) As Byte
            Do
                Token.ThrowIfCancellationRequested()
                Dim Count = Input.Read(Buffer, 0, Buffer.Length)
                If Count = 0 Then Exit Do
                Output.Write(Buffer, 0, Count)
            Loop
        End Using
    End Sub

    Private Function EnumerateSafe(Root As String, Token As CancellationToken) As List(Of String)
        Dim Result As New List(Of String)
        If Not SduFileSystem.Directory.Exists(Root) Then Return Result
        SduBareClientInstaller.RejectReparseParents(Root, Root)
        For Each File In SduFileSystem.Directory.GetFiles(Root)
            Token.ThrowIfCancellationRequested()
            SduBareClientInstaller.RejectReparseParents(Root, File)
            Result.Add(File)
        Next
        For Each Folder In SduFileSystem.Directory.GetDirectories(Root)
            SduBareClientInstaller.RejectReparseParents(Root, Folder)
            Result.AddRange(EnumerateSafe(Folder, Token))
        Next
        Return Result
    End Function

    ''' <summary>只读根级模组描述，不执行 JAR；无法识别的文件仍按相对路径合并。</summary>
    Friend Function ReadModIds(File As String) As HashSet(Of String)
        Dim Result As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        Dim Lower = File.ToLowerInvariant()
        If Not (Lower.EndsWith(".jar") OrElse Lower.EndsWith(".jar.disabled") OrElse Lower.EndsWith(".jar.disable")) Then Return Result
        Try
            Using Input = SduFileSystem.File.OpenRead(File), Archive As New ZipArchive(Input, ZipArchiveMode.Read)
                For Each Entry In Archive.Entries
                    If Entry.Length > 1024 * 1024 Then Continue For
                    Select Case Entry.FullName.ToLowerInvariant()
                        Case "fabric.mod.json", "quilt.mod.json"
                            Using Reader As New StreamReader(Entry.Open(), Encoding.UTF8)
                                Dim Json = JObject.Parse(Reader.ReadToEnd())
                                AddId(Result, CStr(If(Json("quilt_loader") IsNot Nothing, Json("quilt_loader")("id"), Json("id"))))
                            End Using
                        Case "meta-inf/mods.toml", "meta-inf/neoforge.mods.toml"
                            Using Reader As New StreamReader(Entry.Open(), Encoding.UTF8)
                                For Each Block As Match In Regex.Matches(Reader.ReadToEnd(), "(?ms)^\s*\[\[mods\]\]\s*(?<body>.*?)(?=^\s*\[|\z)")
                                    Dim Id = Regex.Match(Block.Groups("body").Value, "(?m)^\s*modId\s*=\s*[""'](?<id>[^""']+)[""']")
                                    If Id.Success Then AddId(Result, Id.Groups("id").Value)
                                Next
                            End Using
                        Case "mcmod.info"
                            Using Reader As New StreamReader(Entry.Open(), Encoding.UTF8)
                                Dim Json = JToken.Parse(Reader.ReadToEnd())
                                Dim Mods = If(TypeOf Json Is JArray, Json, Json("modList"))
                                If Mods IsNot Nothing Then
                                    For Each ModInfo In Mods
                                        AddId(Result, CStr(ModInfo("modid")))
                                    Next
                                End If
                            End Using
                    End Select
                Next
            End Using
        Catch ex As Exception When TypeOf ex Is InvalidDataException OrElse TypeOf ex Is Newtonsoft.Json.JsonException
            '非标准或损坏描述不作为按 ID 移除本地模组的依据。
            Result.Clear()
        End Try
        Return Result
    End Function

    Private Sub AddId(Ids As HashSet(Of String), Id As String)
        If Id IsNot Nothing AndAlso Regex.IsMatch(Id, "^[A-Za-z0-9_.-]{1,128}$") Then Ids.Add(Id)
    End Sub
End Module
