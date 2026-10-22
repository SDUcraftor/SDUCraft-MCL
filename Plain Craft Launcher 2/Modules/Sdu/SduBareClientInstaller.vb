Imports System.IO
Imports System.IO.Compression
Imports System.Collections.Concurrent
Imports System.Text
Imports System.Text.RegularExpressions
Imports System.Threading
Imports Newtonsoft.Json.Linq

''' <summary>裸客户端的布局识别、暂存及发布。只清理本次创建的暂存目录，绝不删除目标实例。</summary>
Friend Module SduBareClientInstaller
    Friend Class Layout
        Public McBase As String
        Public VersionName As String
    End Class

    Friend Class InstallResult
        Public InstanceFolder As String
        Public UpdatedMods As Boolean
        Public ModsBackup As String
    End Class

    Private ReadOnly UpdateLocks As New ConcurrentDictionary(Of String, Object)(StringComparer.OrdinalIgnoreCase)

    ''' <summary>首次安装记录官网项目 ID，后续版本号变化仍更新同一实例。旧实例无标记时仅按完整实例名匹配。</summary>
    Friend Function ResolveInstanceName(McFolder As String, ProjectId As String, ProposedName As String) As String
        If String.IsNullOrWhiteSpace(ProjectId) Then Return ProposedName
        Dim Versions = Path.Combine(McFolder, "versions")
        Dim Matches As New List(Of String)
        If Directory.Exists(Versions) Then
            For Each Folder In Directory.GetDirectories(Versions)
                Dim Marker = Path.Combine(Folder, "PCL", "SduPack.json")
                If Not File.Exists(Marker) Then Continue For
                Dim SavedId As String
                Try
                    SavedId = CStr(JObject.Parse(File.ReadAllText(Marker, Encoding.UTF8))("sduProjectId"))
                Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is Newtonsoft.Json.JsonException OrElse TypeOf ex Is UnauthorizedAccessException
                    '损坏的标记不能作为覆盖任意目录的依据。
                    Continue For
                End Try
                If SavedId = ProjectId Then Matches.Add(Path.GetFileName(Folder))
                If SavedId <> ProjectId AndAlso Path.GetFileName(Folder).Equals(ProposedName, StringComparison.OrdinalIgnoreCase) Then
                    Throw New IOException("同名实例属于其他官网项目，已保留原目录，请先在版本管理中重命名：" & ProposedName)
                End If
            Next
        End If
        If Matches.Count > 1 Then Throw New IOException("找到多个属于该官网项目的实例，请先在版本管理中保留一个需要更新的实例，或手动安装。")
        Return If(Matches.Count = 1, Matches(0), ProposedName)
    End Function

    Private Class PlannedFile
        Public Entry As ZipArchiveEntry
        Public RelativePath As String
        Public SharedCache As Boolean
        Public FromVersion As Boolean
    End Class

    ''' <summary>无版本返回 Nothing；多个版本需要用户手动选择，不能按 ZIP 顺序猜测。</summary>
    Friend Function Detect(Archive As ZipArchive) As Layout
        Dim Candidates As New List(Of Layout)
        For Each Entry In Archive.Entries
            Dim Match = Regex.Match(Entry.FullName.Replace("\"c, "/"c),
                "^(?<base>(?:.*/)?)versions/(?<ver>[^/]+)/\k<ver>\.json$", RegexOptions.IgnoreCase)
            If Not Match.Success Then Continue For
            ValidateSegment(Match.Groups("ver").Value)
            Candidates.Add(New Layout With {.McBase = Match.Groups("base").Value, .VersionName = Match.Groups("ver").Value})
        Next
        If Candidates.Count > 1 Then Throw New InvalidDataException("裸客户端中包含多个版本，无法确定要安装的实例。请解压后选择需要的版本手动安装。")
        Return Candidates.SingleOrDefault()
    End Function

    ''' <summary>统一网站实例名，防止 Windows 保留名、尾部点号等映射到意外目录。</summary>
    Friend Function SafeInstanceName(Value As String, Optional Fallback As String = "SDUcraft-Pack") As String
        Dim Name = Regex.Replace(If(Value, ""), "[\\/:*?""<>|!;\x00-\x1f]", "_").Trim("_"c, " "c, "."c)
        If Name.Length > 64 Then Name = Name.Substring(0, 64).Trim("_"c, " "c, "."c)
        If Name = "" Then Name = Regex.Replace(Fallback, "[\\/:*?""<>|!;\x00-\x1f]", "_").Trim("_"c, " "c, "."c)
        If Name = "" Then Name = "SDUcraft-Pack"
        If IsReservedName(Name) Then Name = "SDUcraft-" & Name
        Return Name.Substring(0, Math.Min(64, Name.Length)).TrimEnd(" "c, "."c)
    End Function

    ''' <summary>
    ''' 先把完整实例解压到同盘暂存目录，校验 JSON、设置隔离后才发布。
    ''' 仅 libraries/assets 进入公共缓存，使用不覆盖的文件移动；取消/失败留下的缓存不做删除。
    ''' ConfigureInstance 在暂存目录内执行，调用方可使用 PCL 原有的 INI 写入接口。
    ''' </summary>
    Friend Function Install(Archive As ZipArchive, Source As Layout, McFolder As String, InstanceName As String,
                            Token As CancellationToken, ConfigureInstance As Action(Of String),
                            Optional Progress As Action(Of Double) = Nothing) As InstallResult
        ValidateSegment(InstanceName)
        ValidateSegment(Source.VersionName)
        Dim Root = Path.GetFullPath(McFolder).TrimEnd(Path.DirectorySeparatorChar) & Path.DirectorySeparatorChar
        Dim Target = ChildPath(Root, "versions/" & InstanceName)
        RejectReparseParents(Root, Target)
        Dim Plan = BuildPlan(Archive, Source, InstanceName)
        Token.ThrowIfCancellationRequested()
        If Directory.Exists(Target) Then Return UpdateMods(Plan, Root, Target, InstanceName, Token, Progress)
        EnsureMissingTarget(Target)

        '暂存目录位于游戏根目录下，发布实例时可用同盘 Directory.Move，目标已存在时必定失败。
        Directory.CreateDirectory(Root)
        Dim Staging = ChildPath(Root, ".sdu-install-" & Guid.NewGuid().ToString("N"))
        If Directory.Exists(Staging) OrElse File.Exists(Staging) Then Throw New IOException("暂存目录已存在。")
        Directory.CreateDirectory(Staging)
        Try
            Dim VersionStage = Path.Combine(Staging, "instance")
            Dim CacheStage = Path.Combine(Staging, "cache")
            Directory.CreateDirectory(VersionStage)
            Dim Total As Long = Plan.Sum(Function(File) File.Entry.Length)
            Dim Done As Long = 0
            For Each File In Plan
                Token.ThrowIfCancellationRequested()
                Dim Destination = ChildPath(If(File.SharedCache, CacheStage, VersionStage), File.RelativePath)
                Directory.CreateDirectory(Path.GetDirectoryName(Destination))
                Using Input = File.Entry.Open(), Output As New FileStream(Destination, FileMode.CreateNew, FileAccess.Write)
                    CopyWithCancellation(Input, Output, Token)
                End Using
                Done += File.Entry.Length
                If Progress IsNot Nothing Then Progress(If(Total = 0, 0, CDbl(Done) / Total) * 0.85)
            Next

            '目录名、主 JSON、主 JAR 和 JSON 内部的自身引用均使用同一网站实例名。
            Dim JsonPath = Path.Combine(VersionStage, InstanceName & ".json")
            Dim Json = JObject.Parse(File.ReadAllText(JsonPath, Encoding.UTF8))
            Json("id") = InstanceName
            If String.Equals(CStr(Json("jar")), Source.VersionName, StringComparison.OrdinalIgnoreCase) Then Json("jar") = InstanceName
            If String.Equals(CStr(Json("inheritsFrom")), Source.VersionName, StringComparison.OrdinalIgnoreCase) Then
                Throw New InvalidDataException("版本 JSON 的 inheritsFrom 不能指向自身。")
            End If
            File.WriteAllText(JsonPath, Json.ToString(), New UTF8Encoding(False))
            ConfigureInstance(VersionStage & Path.DirectorySeparatorChar)
            'WriteIni 会记录错误而不抛出；直接读取磁盘，避免隔离设置写入失败后仍发布实例。
            Dim Ini = File.ReadAllText(Path.Combine(VersionStage, "PCL", "Setup.ini"), Encoding.UTF8)
            If Not Regex.IsMatch(Ini, "(?im)^VersionArgumentIndie:1\r?$") OrElse
               Not Regex.IsMatch(Ini, "(?im)^VersionArgumentIndieV2:True\r?$") Then
                Throw New IOException("版本隔离设置未成功写入，已停止安装。")
            End If
            Token.ThrowIfCancellationRequested()

            '先检查所有公共缓存冲突。内容不同则终止，不能覆盖已有游戏依赖。
            Dim Caches = Plan.Where(Function(File) File.SharedCache).ToList()
            For Each File In Caches
                Token.ThrowIfCancellationRequested()
                Dim Destination = ChildPath(Root, File.RelativePath)
                RejectReparseParents(Root, Destination)
                If IO.File.Exists(Destination) AndAlso Not SameContents(ChildPath(CacheStage, File.RelativePath), Destination, Token) Then
                    Throw New IOException("公共缓存中已有不同内容的文件，已保留原文件：" & Destination)
                End If
            Next
            For Each File In Caches
                Token.ThrowIfCancellationRequested()
                Dim Destination = ChildPath(Root, File.RelativePath)
                RejectReparseParents(Root, Destination)
                Directory.CreateDirectory(Path.GetDirectoryName(Destination))
                Dim Cached = ChildPath(CacheStage, File.RelativePath)
                Try
                    IO.File.Move(Cached, Destination)
                Catch ex As IOException
                    '其他安装任务可能刚写入相同缓存，只接受内容完全一致的结果。
                    If Not IO.File.Exists(Destination) OrElse Not SameContents(Cached, Destination, Token) Then Throw
                End Try
            Next

            Token.ThrowIfCancellationRequested()
            RejectReparseParents(Root, Target)
            Directory.CreateDirectory(Path.GetDirectoryName(Target))
            EnsureMissingTarget(Target)
            Directory.Move(VersionStage, Target)
            '发布后不再响应取消或回滚目标，避免删除已经可用、可能被用户打开的实例。
            Return New InstallResult With {.InstanceFolder = Target & Path.DirectorySeparatorChar}
        Finally
            'Staging 是本次 GUID 目录，发布后的 Target 不在其中；绝不根据实例名清理目录。
            Try
                If Directory.Exists(Staging) Then Directory.Delete(Staging, True)
            Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException
                Diagnostics.Trace.TraceWarning("无法清理 SDUcraft 暂存目录 {0}：{1}", Staging, ex.Message)
            End Try
        End Try
    End Function

    ''' <summary>已存在的实例只同步 mods；旧 mods 永久保留为备份，切换失败则恢复。</summary>
    Private Function UpdateMods(Plan As List(Of PlannedFile), Root As String, Target As String, InstanceName As String,
                                Token As CancellationToken, Progress As Action(Of Double)) As InstallResult
        SyncLock UpdateLocks.GetOrAdd(Target, Function(Key) New Object())
            Token.ThrowIfCancellationRequested()
            If Not File.Exists(Path.Combine(Target, InstanceName & ".json")) Then
                Throw New IOException("目标目录不是可识别的 Minecraft 实例，已保留原目录：" & Target)
            End If
            Dim Mods = Plan.Where(Function(File) Not File.SharedCache AndAlso File.RelativePath.StartsWith("mods/", StringComparison.OrdinalIgnoreCase)).ToList()
            If Mods.Count = 0 Then Throw New InvalidDataException("包中没有 mods 文件，已保留现有模组；不能将空目录作为更新覆盖。")
            Dim CurrentMods = Path.Combine(Target, "mods")
            RejectReparseParents(Root, CurrentMods)
            If File.Exists(CurrentMods) Then Throw New IOException("实例的 mods 路径已被文件占用，已停止更新。")
            Dim Staging = ChildPath(Root, ".sdu-install-" & Guid.NewGuid().ToString("N"))
            If Directory.Exists(Staging) OrElse File.Exists(Staging) Then Throw New IOException("暂存目录已存在。")
            Directory.CreateDirectory(Staging)
            Dim ModsStage = Path.Combine(Staging, "mods")
            Try
                Directory.CreateDirectory(ModsStage)
                Dim Total As Long = Mods.Sum(Function(File) File.Entry.Length)
                Dim Done As Long = 0
                For Each File In Mods
                    Token.ThrowIfCancellationRequested()
                    Dim Destination = ChildPath(ModsStage, File.RelativePath.Substring("mods/".Length))
                    Directory.CreateDirectory(Path.GetDirectoryName(Destination))
                    Using Input = File.Entry.Open(), Output As New FileStream(Destination, FileMode.CreateNew, FileAccess.Write)
                        CopyWithCancellation(Input, Output, Token)
                    End Using
                    Done += File.Entry.Length
                    If Progress IsNot Nothing Then Progress(If(Total = 0, 0, CDbl(Done) / Total) * 0.95)
                Next
                Token.ThrowIfCancellationRequested()
                RejectReparseParents(Root, CurrentMods)
                Dim Backup As String = Nothing
                If Directory.Exists(CurrentMods) Then
                    Backup = ChildPath(Root, "PCL/SduModBackups/" & InstanceName & "/" & Date.UtcNow.ToString("yyyyMMdd-HHmmss") & "-" & Guid.NewGuid().ToString("N") & "/mods")
                    RejectReparseParents(Root, Backup)
                    Directory.CreateDirectory(Path.GetDirectoryName(Backup))
                End If
                PublishMods(ModsStage, CurrentMods, Backup, AddressOf Directory.Move)
                Return New InstallResult With {.InstanceFolder = Target & Path.DirectorySeparatorChar, .UpdatedMods = True, .ModsBackup = Backup}
            Finally
                Try
                    If Directory.Exists(Staging) Then Directory.Delete(Staging, True)
                Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException
                    Diagnostics.Trace.TraceWarning("无法清理 SDUcraft 模组暂存目录 {0}：{1}", Staging, ex.Message)
                End Try
            End Try
        End SyncLock
    End Function

    ''' <summary>两次同盘移动之间不检查取消；MoveDirectory 是可注入故障的文件系统操作。</summary>
    Friend Sub PublishMods(Prepared As String, Current As String, Backup As String, MoveDirectory As Action(Of String, String))
        If Backup IsNot Nothing Then MoveDirectory(Current, Backup)
        Try
            MoveDirectory(Prepared, Current)
        Catch ex As Exception
            If Backup IsNot Nothing Then
                Try
                    MoveDirectory(Backup, Current)
                Catch RestoreError As Exception
                    Throw New IOException("模组切换失败，旧 mods 已保留在以下备份目录，请手动恢复：" & Backup, New AggregateException(ex, RestoreError))
                End Try
            End If
            Throw
        End Try
    End Sub

    Private Function BuildPlan(Archive As ZipArchive, Source As Layout, InstanceName As String) As List(Of PlannedFile)
        Dim VersionPrefix = "versions/" & Source.VersionName & "/"
        Dim Plan As New Dictionary(Of String, PlannedFile)(StringComparer.OrdinalIgnoreCase)
        For Each Entry In Archive.Entries
            Dim Name = Entry.FullName.Replace("\"c, "/"c)
            If Not Name.StartsWith(Source.McBase, StringComparison.OrdinalIgnoreCase) Then Continue For
            Dim Relative = Name.Substring(Source.McBase.Length)
            If Relative = "" Then Continue For
            For Each Segment In Relative.TrimEnd("/"c).Split("/"c)
                ValidateSegment(Segment)
            Next
            'ZIP 中的符号链接不能作为目录或文件安装。
            If ((Entry.ExternalAttributes >> 16) And &HF000) = &HA000 Then Throw New InvalidDataException("不支持包内符号链接：" & Name)
            If Name.EndsWith("/") Then Continue For
            Dim FromVersion = Relative.StartsWith(VersionPrefix, StringComparison.OrdinalIgnoreCase)
            If Relative.StartsWith("versions/", StringComparison.OrdinalIgnoreCase) AndAlso Not FromVersion Then
                Throw New InvalidDataException("裸客户端含有其他版本的文件，请手动选择版本安装。")
            End If
            If FromVersion Then
                Relative = Relative.Substring(VersionPrefix.Length)
                If Relative.Equals(Source.VersionName & ".json", StringComparison.OrdinalIgnoreCase) Then Relative = InstanceName & ".json"
                If Relative.Equals(Source.VersionName & ".jar", StringComparison.OrdinalIgnoreCase) Then Relative = InstanceName & ".jar"
                If Relative.StartsWith(Source.VersionName & "-natives/", StringComparison.OrdinalIgnoreCase) Then
                    Relative = InstanceName & "-natives/" & Relative.Substring((Source.VersionName & "-natives/").Length)
                End If
            End If
            Dim SharedCache = Not FromVersion AndAlso (Relative.StartsWith("libraries/", StringComparison.OrdinalIgnoreCase) OrElse Relative.StartsWith("assets/", StringComparison.OrdinalIgnoreCase))
            Dim Key = If(SharedCache, "cache/", "instance/") & Relative
            Dim Previous As PlannedFile = Nothing
            If Plan.TryGetValue(Key, Previous) Then
                '已隔离客户端里的版本专属配置优先于根级配置，与 ZIP 条目顺序无关。
                If Previous.FromVersion = FromVersion Then Throw New InvalidDataException("压缩包包含重复目标文件：" & Relative)
                If Previous.FromVersion Then Continue For
            End If
            Plan(Key) = New PlannedFile With {.Entry = Entry, .RelativePath = Relative, .SharedCache = SharedCache, .FromVersion = FromVersion}
        Next
        Return Plan.Values.ToList()
    End Function

    Private Sub EnsureMissingTarget(Target As String)
        If Directory.Exists(Target) OrElse File.Exists(Target) Then
            Throw New IOException("实例目录已存在，已保留原版本及其存档、配置。请先在版本管理中重命名已有实例，再重新安装：" & Target)
        End If
    End Sub

    Private Sub ValidateSegment(Value As String)
        If String.IsNullOrWhiteSpace(Value) OrElse Value = "." OrElse Value = ".." OrElse
           Value.EndsWith(".") OrElse Value.EndsWith(" ") OrElse Value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 OrElse IsReservedName(Value) Then
            Throw New InvalidDataException("压缩包或实例名包含无效路径段：" & Value)
        End If
    End Sub

    Private Function IsReservedName(Value As String) As Boolean
        Return Regex.IsMatch(Value, "^(CON|PRN|AUX|NUL|CLOCK\$|COM[0-9¹²³]|LPT[0-9¹²³])(?:\.|$)", RegexOptions.IgnoreCase)
    End Function

    Private Function ChildPath(Root As String, Relative As String) As String
        Dim FullRoot = Path.GetFullPath(Root).TrimEnd(Path.DirectorySeparatorChar) & Path.DirectorySeparatorChar
        Dim Result = Path.GetFullPath(Path.Combine(FullRoot, Relative.Replace("/"c, Path.DirectorySeparatorChar)))
        If Not Result.StartsWith(FullRoot, StringComparison.OrdinalIgnoreCase) Then Throw New InvalidDataException("安装路径超出目标目录：" & Relative)
        Return Result
    End Function

    Private Sub RejectReparseParents(Root As String, Target As String)
        Dim Current = Target
        Do
            If (Directory.Exists(Current) OrElse File.Exists(Current)) AndAlso (File.GetAttributes(Current) And FileAttributes.ReparsePoint) <> 0 Then
                Throw New IOException("安装目标包含链接或重解析点，已停止写入：" & Current)
            End If
            If Current.TrimEnd(Path.DirectorySeparatorChar).Equals(Root.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase) Then Exit Do
            Current = Path.GetDirectoryName(Current)
        Loop While Current IsNot Nothing
    End Sub

    Private Sub CopyWithCancellation(Input As Stream, Output As Stream, Token As CancellationToken)
        Dim Buffer(65535) As Byte
        Do
            Token.ThrowIfCancellationRequested()
            Dim Count = Input.Read(Buffer, 0, Buffer.Length)
            If Count = 0 Then Exit Do
            Output.Write(Buffer, 0, Count)
        Loop
    End Sub

    Private Function SameContents(Left As String, Right As String, Token As CancellationToken) As Boolean
        If New FileInfo(Left).Length <> New FileInfo(Right).Length Then Return False
        Using LeftStream = File.OpenRead(Left), RightStream = File.OpenRead(Right)
            Dim LeftBuffer(65535), RightBuffer(65535) As Byte
            Do
                Token.ThrowIfCancellationRequested()
                Dim LeftCount = LeftStream.Read(LeftBuffer, 0, LeftBuffer.Length)
                Dim RightCount = RightStream.Read(RightBuffer, 0, RightBuffer.Length)
                If LeftCount <> RightCount Then Return False
                If LeftCount = 0 Then Return True
                For Index = 0 To LeftCount - 1
                    If LeftBuffer(Index) <> RightBuffer(Index) Then Return False
                Next
            Loop
        End Using
    End Function
End Module
