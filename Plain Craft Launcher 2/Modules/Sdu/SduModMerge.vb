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
            Dim Plan = PlanIncoming(IncomingMods, Incoming, Token)
            Incoming = Plan.Files
            Dim NewIds = Plan.Ids
            Dim Replaced As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
            Dim Disabled As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
            Dim ActiveIds As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
            For Each File In Existing.Where(Function(Value) IsRootMod(Current, Value, IncludeDisabled:=False))
                ActiveIds.UnionWith(ReadModIds(File))
            Next
            For Each File In Existing
                If Not IsRootMod(Current, File, IncludeDisabled:=True) Then Continue For
                Dim OldIds = ReadModIds(File)
                Dim Matches = OldIds.Where(Function(Id) NewIds.ContainsKey(Id)).Select(Function(Id) NewIds(Id)).Distinct().ToList()
                If Matches.Count = 0 Then Continue For
                Dim IsDisabled = Not File.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)
                '已有启用版本时，禁用副本只是备份，不得因此禁用新版本或移走该副本。
                If IsDisabled AndAlso OldIds.Any(Function(Id) ActiveIds.Contains(Id)) Then Continue For
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

    Private Const OverridesRecord As String = ".sdu-mod-overrides.json"

    ''' <summary>记录包内 overrides 的实际来源；不能用下载完成时间或文件名猜测优先级。</summary>
    Friend Sub RecordOverrides(SourceFolder As String, InstanceFolder As String, Token As CancellationToken)
        Dim SourceMods = Path.Combine(SourceFolder, "mods")
        Dim Names As New HashSet(Of String)(ReadPreferredNames(InstanceFolder), StringComparer.OrdinalIgnoreCase)
        Dim SourceFiles = EnumerateSafe(SourceMods, Token)
        Dim SourceIds As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        For Each File In SourceFiles.Where(Function(Value) IsRootMod(SourceMods, Value, IncludeDisabled:=False))
            SourceIds.UnionWith(ReadModIds(File))
        Next
        '后复制的 client-overrides 比普通 overrides 优先，文件名变化也按 ID 覆盖来源记录。
        For Each Name In Names.ToList()
            Dim Destination = SduBareClientInstaller.ChildPath(Path.Combine(InstanceFolder, "mods"), Name)
            If SduFileSystem.File.Exists(Destination) AndAlso SourceIds.Overlaps(ReadModIds(Destination)) Then Names.Remove(Name)
        Next
        For Each File In SourceFiles
            Names.Add(File.Substring(SourceMods.Length + 1).Replace("\"c, "/"c))
        Next
        SduFileSystem.Directory.CreateDirectory(InstanceFolder)
        SduFileSystem.File.WriteAllText(Path.Combine(InstanceFolder, OverridesRecord), New JArray(Names).ToString(), New UTF8Encoding(False))
    End Sub

    Private Function ReadPreferredNames(Folder As String) As List(Of String)
        Dim Record = Path.Combine(Folder, OverridesRecord)
        If Not SduFileSystem.File.Exists(Record) Then Return New List(Of String)
        If SduFileSystem.File.Length(Record) > 2 * 1024 * 1024 Then Throw New InvalidDataException("模组来源记录超过大小上限。")
        Return JArray.Parse(SduFileSystem.File.ReadAllText(Record, Encoding.UTF8)).Select(Function(Value) CStr(Value)).ToList()
    End Function

    Private Class IncomingPlan
        Friend Files As List(Of String)
        Friend Ids As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
        Friend Superseded As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
    End Class

    Private Function PlanIncoming(ModsFolder As String, Files As List(Of String), Token As CancellationToken) As IncomingPlan
        Dim Preferred As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        For Each Name In ReadPreferredNames(Path.GetDirectoryName(ModsFolder.TrimEnd("\"c)))
            Preferred.Add(SduBareClientInstaller.ChildPath(ModsFolder, Name))
        Next
        Dim Candidates As New Dictionary(Of String, List(Of String))(StringComparer.OrdinalIgnoreCase)
        For Each File In Files.Where(Function(Value) IsRootMod(ModsFolder, Value, IncludeDisabled:=False))
            Token.ThrowIfCancellationRequested()
            For Each Id In ReadModIds(File)
                If Not Candidates.ContainsKey(Id) Then Candidates.Add(Id, New List(Of String))
                Candidates(Id).Add(File)
            Next
        Next
        Dim Plan As New IncomingPlan
        For Each Entry In Candidates
            Dim Choices = Entry.Value
            Dim Winner = Choices(0)
            If Choices.Count > 1 Then
                Dim PreferredChoices = Choices.Where(Function(File) Preferred.Contains(File)).ToList()
                Winner = If(PreferredChoices.Count = 1, PreferredChoices(0),
                    SelectNewest(Entry.Key, If(PreferredChoices.Count > 0, PreferredChoices, Choices)))
                Plan.Superseded.UnionWith(Choices.Where(Function(File) Not String.Equals(File, Winner, StringComparison.OrdinalIgnoreCase)))
            End If
            Plan.Ids.Add(Entry.Key, Winner)
        Next
        If Plan.Ids.Values.Any(Function(File) Plan.Superseded.Contains(File)) Then Throw New InvalidDataException("重复 JAR 同时包含其他不可替代模组，已停止更新以保留内容。")
        Plan.Files = Files.Where(Function(File) Not Plan.Superseded.Contains(File)).ToList()
        Return Plan
    End Function

    '同一来源重复打包时只比较 JAR 元数据，不从文件名、复制时间推断版本。
    Private Function SelectNewest(Id As String, Choices As List(Of String)) As String
        Dim Versions As New Dictionary(Of String, Match)(StringComparer.OrdinalIgnoreCase)
        For Each File In Choices
            Dim Metadata = ReadModMetadata(File)
            Dim Value As String = Nothing
            Metadata.TryGetValue(Id, Value)
            Dim Parsed = Regex.Match(If(Value, ""), "^v?(?<core>\d+(?:\.\d+)*)(?:-(?<pre>[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?(?:\+[0-9A-Za-z-]+(?:[.+][0-9A-Za-z-]+)*)?$")
            If Not Parsed.Success Then Throw DuplicateError(Id, Choices)
            Versions.Add(File, Parsed)
        Next
        Dim Ordered = Choices.OrderBy(Function(File) File, StringComparer.OrdinalIgnoreCase).ToList()
        Dim Winner = Ordered(0)
        For Each File In Ordered.Skip(1)
            If CompareModVersions(Versions(File), Versions(Winner)) > 0 Then Winner = File
        Next
        '版本相同且内容不同的构建不能随意选一个；完全相同的副本可以去重。
        For Each File In Ordered.Where(Function(Value) Not String.Equals(Value, Winner, StringComparison.OrdinalIgnoreCase))
            If CompareModVersions(Versions(File), Versions(Winner)) = 0 AndAlso Not SameModBytes(File, Winner) Then Throw DuplicateError(Id, Choices)
        Next
        Return Winner
    End Function

    Private Function DuplicateError(Id As String, Choices As List(Of String)) As InvalidDataException
        Return New InvalidDataException("无法确定重复模组的有效版本：" & Id & "（" & String.Join("、", Choices.Select(Function(File) Path.GetFileName(File))) & "）。缺少可比较的内部版本号，或同版本文件内容不同。请检查整合包内容。")
    End Function

    Private Function CompareNumber(Left As String, Right As String) As Integer
        Left = Left.TrimStart("0"c) : Right = Right.TrimStart("0"c)
        If Left.Length <> Right.Length Then Return Left.Length.CompareTo(Right.Length)
        Return String.Compare(Left, Right, StringComparison.Ordinal)
    End Function

    Private Function CompareModVersions(Left As Match, Right As Match) As Integer
        Dim A = Left.Groups("core").Value.Split("."c), B = Right.Groups("core").Value.Split("."c)
        For Index = 0 To Math.Max(A.Length, B.Length) - 1
            Dim Comparison = CompareNumber(If(Index < A.Length, A(Index), "0"), If(Index < B.Length, B(Index), "0"))
            If Comparison <> 0 Then Return Comparison
        Next
        Dim AP = Left.Groups("pre").Value, BP = Right.Groups("pre").Value
        If AP = "" OrElse BP = "" Then Return If(AP = BP, 0, If(AP = "", 1, -1))
        A = AP.Split("."c) : B = BP.Split("."c)
        For Index = 0 To Math.Min(A.Length, B.Length) - 1
            Dim AN = Regex.IsMatch(A(Index), "^\d+$"), BN = Regex.IsMatch(B(Index), "^\d+$")
            Dim Comparison = If(AN AndAlso BN, CompareNumber(A(Index), B(Index)),
                If(AN <> BN, If(AN, -1, 1), String.Compare(A(Index), B(Index), StringComparison.Ordinal)))
            If Comparison <> 0 Then Return Comparison
        Next
        Return A.Length.CompareTo(B.Length)
    End Function

    Private Function SameModBytes(Left As String, Right As String) As Boolean
        If SduFileSystem.File.Length(Left) <> SduFileSystem.File.Length(Right) Then Return False
        Using Hash = System.Security.Cryptography.SHA256.Create(), A = SduFileSystem.File.OpenRead(Left), B = SduFileSystem.File.OpenRead(Right)
            Return Hash.ComputeHash(A).SequenceEqual(Hash.ComputeHash(B))
        End Using
    End Function

    Private Function IsRootMod(Root As String, File As String, IncludeDisabled As Boolean) As Boolean
        If Not String.Equals(Path.GetDirectoryName(File), Root.TrimEnd("\"c), StringComparison.OrdinalIgnoreCase) Then Return False
        Return File.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) OrElse
            (IncludeDisabled AndAlso (File.EndsWith(".jar.disabled", StringComparison.OrdinalIgnoreCase) OrElse File.EndsWith(".jar.disable", StringComparison.OrdinalIgnoreCase)))
    End Function

    ''' <summary>首次安装也使用 overrides 优先规则；多余的下载副本移到实例元数据中保留。</summary>
    Friend Sub FinalizeFirstInstall(InstanceFolder As String, Token As CancellationToken)
        Dim Mods = Path.Combine(InstanceFolder.TrimEnd("\"c), "mods")
        Dim Plan = PlanIncoming(Mods, EnumerateSafe(Mods, Token), Token)
        If Plan.Superseded.Count = 0 Then Return
        Dim Backup = Path.Combine(InstanceFolder, "PCL", "SduSupersededMods", Guid.NewGuid().ToString("N"))
        SduBareClientInstaller.RejectReparseParents(InstanceFolder, Backup)
        SduFileSystem.Directory.CreateDirectory(Backup)
        For Each File In Plan.Superseded
            Token.ThrowIfCancellationRequested()
            SduFileSystem.File.Move(File, Path.Combine(Backup, Path.GetFileName(File)))
        Next
    End Sub

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
        Return New HashSet(Of String)(ReadModMetadata(File).Keys, StringComparer.OrdinalIgnoreCase)
    End Function

    Private Function ReadModMetadata(File As String) As Dictionary(Of String, String)
        Dim Result As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
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
                                Dim Info = If(Json("quilt_loader"), Json)
                                AddMod(Result, CStr(Info("id")), CStr(Info("version")))
                            End Using
                        Case "meta-inf/mods.toml", "meta-inf/neoforge.mods.toml"
                            Using Reader As New StreamReader(Entry.Open(), Encoding.UTF8)
                                For Each Block As Match In Regex.Matches(Reader.ReadToEnd(), "(?ms)^\s*\[\[mods\]\]\s*(?<body>.*?)(?=^\s*\[|\z)")
                                    Dim Id = Regex.Match(Block.Groups("body").Value, "(?m)^\s*modId\s*=\s*[""'](?<id>[^""']+)[""']")
                                    Dim Version = Regex.Match(Block.Groups("body").Value, "(?m)^\s*version\s*=\s*[""'](?<version>[^""']+)[""']")
                                    Dim Value = Version.Groups("version").Value
                                    If Value = "${file.jarVersion}" Then
                                        Dim Manifest = Archive.GetEntry("META-INF/MANIFEST.MF")
                                        If Manifest IsNot Nothing AndAlso Manifest.Length <= 1024 * 1024 Then
                                            Using ManifestReader As New StreamReader(Manifest.Open(), Encoding.UTF8)
                                                Value = Regex.Match(ManifestReader.ReadToEnd(), "(?m)^Implementation-Version:\s*([^\r\n]+)").Groups(1).Value.Trim()
                                            End Using
                                        End If
                                    End If
                                    If Id.Success Then AddMod(Result, Id.Groups("id").Value, Value)
                                Next
                            End Using
                        Case "mcmod.info"
                            Using Reader As New StreamReader(Entry.Open(), Encoding.UTF8)
                                Dim Json = JToken.Parse(Reader.ReadToEnd())
                                Dim Mods = If(TypeOf Json Is JArray, Json, Json("modList"))
                                If Mods IsNot Nothing Then
                                    For Each ModInfo In Mods
                                        AddMod(Result, CStr(ModInfo("modid")), CStr(ModInfo("version")))
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

    Private Sub AddMod(Mods As Dictionary(Of String, String), Id As String, Version As String)
        If Id IsNot Nothing AndAlso Regex.IsMatch(Id, "^[A-Za-z0-9_.-]{1,128}$") Then Mods(Id) = Version
    End Sub
End Module
