Imports System.IO
Imports System.IO.Compression
Imports System.Text
Imports System.Threading
Imports Newtonsoft.Json.Linq

Module SduBareClientTests
    Private Passed As Integer
    Private ReadOnly TestRoot As String = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "fixtures")

    Sub Main()
        Try
            Run("isolated layout, stable instance name and existing data", AddressOf IsolatedLayout)
            Run("root-level versions and backslash ZIP paths", AddressOf AlternateLayouts)
            Run("same-name directory and file are preserved", AddressOf ExistingTargets)
            Run("malformed JSON rolls back only staging", AddressOf MalformedJson)
            Run("configuration failure rolls back only staging", AddressOf ConfigurationFailure)
            Run("cancellation during extraction", AddressOf CancelExtraction)
            Run("cancellation before work and before publication", AddressOf CancelBeforePublication)
            Run("thread interruption cleans staging", AddressOf InterruptedInstall)
            Run("target appearing during installation is preserved", AddressOf TargetRace)
            Run("shared cache conflict preserves original bytes", AddressOf CacheConflict)
            Run("identical shared cache can be reused", AddressOf ReuseCache)
            Run("path traversal, invalid paths and symlinks are rejected", AddressOf InvalidPaths)
            Run("duplicate destinations are rejected", AddressOf Duplicates)
            Run("multiple versions are not selected by ZIP order", AddressOf AmbiguousVersions)
            Run("version-specific configuration wins in either ZIP order", AddressOf ConfigurationPriority)
            Run("missing isolation settings prevent publication", AddressOf IsolationRequired)
            Run("Windows instance names are sanitized", AddressOf SafeNames)
            Run("updates synchronize only mods and keep a full backup", AddressOf ModsOnlyUpdate)
            Run("failed or canceled updates keep original mods", AddressOf FailedUpdate)
            Run("updates cannot clear mods from a package without mods", AddressOf EmptyUpdate)
            Run("project identity survives website version changes", AddressOf StableIdentity)
            Run("publication failure restores backup", AddressOf RestoreAfterSwitchFailure)
            Run("restore failure preserves backup and reports its path", AddressOf PreserveBackupAfterRestoreFailure)
            Run("invalid source version names are rejected before writes", AddressOf InvalidSourceVersion)
            Run("archive markers retain actual paths and ignore separator/case differences", AddressOf ArchiveMarkers)
            Run("manual exports preserve extensions and never reuse stale packages", AddressOf ManualExports)
            Console.WriteLine("PASS: " & Passed & " bare-client filesystem scenarios (production installer, real temporary ZIP/files).")
        Catch ErrorValue As Exception
            Console.Error.WriteLine(ErrorValue.ToString())
            Environment.ExitCode = 1
        End Try
    End Sub

    Private Sub ArchiveMarkers()
        For Each Name In {"modpack.zip", "MODPACK.MRPACK", "wrapper/modpack.zip", "WRAPPER\MODPACK.MRPACK"}
            Using Data As New MemoryStream()
                Using Writer As New ZipArchive(Data, ZipArchiveMode.Create, True)
                    AddEntry(Writer, Name, "inner-package-bytes")
                    AddEntry(Writer, "deep/nested/modpack.zip", "must-not-match")
                End Using
                Data.Position = 0
                Using Archive As New ZipArchive(Data, ZipArchiveMode.Read)
                    Dim Entry = SduPackageFiles.FindMarker(Archive, "modpack.mrpack", "modpack.zip")
                    Check(Entry IsNot Nothing AndAlso Entry.FullName = Name, "Marker lost original ZIP entry: " & Name)
                    Using Reader As New StreamReader(Entry.Open())
                        Check(Reader.ReadToEnd() = "inner-package-bytes", "Could not extract matched inner entry")
                    End Using
                    Check(SduPackageFiles.FindEntry(Archive, Name.Replace("\"c, "/"c).ToLowerInvariant()) Is Entry, "Known-pack entry lookup disagrees with marker lookup")
                End Using
            End Using
        Next
        Using F As New Fixture({Pair("deep/nested/modpack.zip", "wrong")})
            Check(SduPackageFiles.FindMarker(F.Archive, "modpack.zip") Is Nothing, "Marker accepted arbitrary nesting")
        End Using
    End Sub

    Private Sub ManualExports()
        Using F As New Fixture()
            For Each Extension In {".zip", ".mrpack"}
                Dim Source = Path.Combine(F.Root, "download" & Extension)
                Dim Folder = Path.Combine(F.Root, "exports")
                File.WriteAllText(Source, "old-package")
                Dim First = SduPackageFiles.ExportForManualInstall(Source, Folder, "Test Pack")
                File.WriteAllText(Source, "new-package")
                Dim Second = SduPackageFiles.ExportForManualInstall(Source, Folder, "Test Pack")
                Check(First <> Second, "Export reused a stale filename")
                Check(Path.GetExtension(First) = Extension AndAlso Path.GetExtension(Second) = Extension, "Export extension lost its dot")
                Check(File.ReadAllText(First) = "old-package" AndAlso File.ReadAllText(Second) = "new-package", "Export overwrote old bytes or retained stale content")
            Next
        End Using
    End Sub

    Private Class Fixture
        Implements IDisposable
        Public ReadOnly Root As String = Path.Combine(TestRoot, Guid.NewGuid().ToString("N"))
        Public ReadOnly Mc As String = Path.Combine(Root, "mc")
        Public ReadOnly Target As String = Path.Combine(Mc, "versions", "Website Instance")
        Public ReadOnly Data As New MemoryStream()
        Public Archive As ZipArchive
        Public Result As SduBareClientInstaller.InstallResult

        Public Sub New(Optional Extra As KeyValuePair(Of String, String)() = Nothing, Optional Json As String = "{""id"":""pack-id"",""jar"":""pack-id""}", Optional Prefix As String = "Wrapper/.minecraft/")
            Directory.CreateDirectory(Root)
            Using Writer As New ZipArchive(Data, ZipArchiveMode.Create, True)
                If Json IsNot Nothing Then AddEntry(Writer, Prefix & "versions/pack-id/pack-id.json", Json)
                If Extra IsNot Nothing Then
                    For Each Item In Extra
                        AddEntry(Writer, Prefix & Item.Key, Item.Value)
                    Next
                End If
            End Using
            Data.Position = 0
            Archive = New ZipArchive(Data, ZipArchiveMode.Read, True)
        End Sub

        Public Function Install(Optional Token As CancellationToken = Nothing, Optional Configure As Action(Of String) = Nothing, Optional Progress As Action(Of Double) = Nothing) As String
            Dim ActualConfigure = If(Configure, AddressOf SetIsolation)
            Result = SduBareClientInstaller.Install(Archive, SduBareClientInstaller.Detect(Archive), Mc, "Website Instance", Token, ActualConfigure, Progress)
            Return Result.InstanceFolder
        End Function

        Public Sub NoPartialInstance()
            Check(Not Directory.Exists(Target), "Incomplete target instance survived")
            Check(Not Directory.Exists(Mc) OrElse Directory.GetDirectories(Mc, ".sdu-install-*").All(Function(Value) Value.EndsWith(".sdu-install-existing")), "Owned staging directory survived")
        End Sub

        Public Sub Dispose() Implements IDisposable.Dispose
            Archive.Dispose()
            Data.Dispose()
            '测试目录仅为当前 executable 下创建的 GUID 子目录。
            Check(Path.GetFullPath(Root).StartsWith(Path.GetFullPath(TestRoot) & Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), "Unsafe fixture cleanup")
            If Directory.Exists(Root) Then Directory.Delete(Root, True)
        End Sub
    End Class

    Private Sub IsolatedLayout()
        Using F As New Fixture({Pair("mods/root.jar", "mod"), Pair("config/test.cfg", "root config"), Pair("saves/world/level.dat", "pack save"),
                Pair("options.txt", "pack options"), Pair("resourcepacks/test.zip", "resource"), Pair("shaderpacks/test.zip", "shader"),
                Pair("libraries/test/lib.jar", "library"), Pair("assets/objects/ab/hash", "asset"),
                Pair("versions/pack-id/pack-id.jar", "client jar"), Pair("versions/pack-id/pack-id-natives/native.dll", "native"),
                Pair("versions/pack-id/config/test.cfg", "version config"), Pair("PCL/Setup.ini", "CustomSetting:retained" & vbCrLf)})
            Put(Path.Combine(F.Mc, "mods", "existing.jar"), "user mod")
            Dim ExistingSave = Path.Combine(F.Mc, "versions", "pack-id", "saves", "user", "level.dat")
            Put(ExistingSave, "user save")
            Dim Configured As Boolean
            Dim Result = F.Install(Configure:=Sub(Stage)
                Check(Not Directory.Exists(F.Target), "Instance was visible before configuration")
                Check(Stage.StartsWith(F.Mc & Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), "Staging not on destination volume")
                SetIsolation(Stage)
                Configured = True
            End Sub)
            Check(Configured AndAlso Result.TrimEnd("\"c) = F.Target, "Published path differs from task instance")
            EqualFile(ExistingSave, "user save")
            EqualFile(Path.Combine(F.Mc, "mods", "existing.jar"), "user mod")
            For Each Relative In {"mods/root.jar", "saves/world/level.dat", "options.txt", "resourcepacks/test.zip", "shaderpacks/test.zip"}
                Check(File.Exists(Path.Combine(F.Target, Relative)), "Game content missing from isolated instance: " & Relative)
                Check(Not File.Exists(Path.Combine(F.Mc, Relative)), "Game content polluted shared root: " & Relative)
            Next
            EqualFile(Path.Combine(F.Target, "config", "test.cfg"), "version config")
            EqualFile(Path.Combine(F.Target, "Website Instance.jar"), "client jar")
            EqualFile(Path.Combine(F.Target, "Website Instance-natives", "native.dll"), "native")
            EqualFile(Path.Combine(F.Mc, "libraries", "test", "lib.jar"), "library")
            EqualFile(Path.Combine(F.Mc, "assets", "objects", "ab", "hash"), "asset")
            Dim Json = JObject.Parse(File.ReadAllText(Path.Combine(F.Target, "Website Instance.json")))
            Check(CStr(Json("id")) = "Website Instance" AndAlso CStr(Json("jar")) = "Website Instance", "Self references still use package name")
            Check(Not File.Exists(Path.Combine(F.Target, "pack-id.json")), "Original main JSON was not renamed")
            Check(File.ReadAllText(Path.Combine(F.Target, "PCL", "Setup.ini")).Contains("CustomSetting:retained"), "Other PCL settings were discarded")
            Check(Directory.GetDirectories(F.Mc, ".sdu-install-*").Length = 0, "Successful staging was not removed")
        End Using
    End Sub

    Private Sub AlternateLayouts()
        For Each Prefix In {"", "Wrapper\.minecraft\"}
            Using F As New Fixture({Pair("mods/test.jar", "mod")}, Prefix:=Prefix)
                F.Install()
                EqualFile(Path.Combine(F.Target, "mods", "test.jar"), "mod")
            End Using
        Next
    End Sub

    Private Sub ExistingTargets()
        Using F As New Fixture()
            Dim Saved = Path.Combine(F.Target, "saves", "world", "level.dat")
            Put(Saved, "never delete")
            Expect(Of IOException)(Sub() F.Install())
            EqualFile(Saved, "never delete")
            Check(Directory.GetDirectories(F.Mc, ".sdu-install-*").Length = 0, "Existing target check happened after staging")
        End Using
        Using F As New Fixture()
            Put(F.Target, "existing file")
            Expect(Of IOException)(Sub() F.Install())
            EqualFile(F.Target, "existing file")
        End Using
    End Sub

    Private Sub MalformedJson()
        Using F As New Fixture({Pair("libraries/test.jar", "cache")}, Json:="broken-json")
            Dim Existing = Path.Combine(F.Mc, ".sdu-install-existing", "keep.txt")
            Put(Existing, "other task")
            Expect(Of Newtonsoft.Json.JsonReaderException)(Sub() F.Install())
            F.NoPartialInstance()
            EqualFile(Existing, "other task")
            Check(Not Directory.Exists(Path.Combine(F.Mc, "libraries")), "Cache copied before JSON validation")
        End Using
    End Sub

    Private Sub ConfigurationFailure()
        Using F As New Fixture()
            Expect(Of IOException)(Sub() F.Install(Configure:=Sub(Stage) Throw New IOException("configuration sentinel")))
            F.NoPartialInstance()
        End Using
    End Sub

    Private Sub CancelExtraction()
        Using F As New Fixture({Pair("mods/large.jar", New String("x"c, 200000)), Pair("config/second.cfg", "second")}), Source As New CancellationTokenSource()
            Expect(Of OperationCanceledException)(Sub() F.Install(Source.Token, Progress:=Sub(Value) Source.Cancel()))
            F.NoPartialInstance()
        End Using
    End Sub

    Private Sub CancelBeforePublication()
        Using F As New Fixture(), Source As New CancellationTokenSource()
            Source.Cancel()
            Expect(Of OperationCanceledException)(Sub() F.Install(Source.Token))
            Check(Not Directory.Exists(F.Mc), "Canceled task created game directory")
        End Using
        Using F As New Fixture({Pair("libraries/test.jar", "cache")}), Source As New CancellationTokenSource()
            Expect(Of OperationCanceledException)(Sub() F.Install(Source.Token, Sub(Stage)
                SetIsolation(Stage)
                Source.Cancel()
            End Sub))
            F.NoPartialInstance()
            Check(Not Directory.Exists(Path.Combine(F.Mc, "libraries")), "Canceled task published cache before configuration completed")
        End Using
    End Sub

    Private Sub InterruptedInstall()
        Using F As New Fixture(), Ready As New ManualResetEvent(False)
            Dim Failure As Exception = Nothing
            Dim Worker As New Thread(Sub()
                Try
                    F.Install(Configure:=Sub(Stage)
                        Ready.Set()
                        Thread.Sleep(Timeout.Infinite)
                    End Sub)
                Catch ex As Exception
                    Failure = ex
                End Try
            End Sub)
            Worker.IsBackground = True
            Worker.Start()
            Check(Ready.WaitOne(5000), "Install did not reach staging configuration")
            Worker.Interrupt()
            Check(Worker.Join(5000), "Interrupted installer survived")
            Check(TypeOf Failure Is ThreadInterruptedException, "Thread interruption was not preserved")
            F.NoPartialInstance()
        End Using
    End Sub

    Private Sub TargetRace()
        Using F As New Fixture({Pair("libraries/new.jar", "cache")})
            Dim Saved = Path.Combine(F.Target, "saves", "new-user-data.dat")
            Expect(Of IOException)(Sub() F.Install(Configure:=Sub(Stage)
                SetIsolation(Stage)
                Put(Saved, "created by another task")
            End Sub))
            EqualFile(Saved, "created by another task")
            Check(Directory.GetDirectories(F.Mc, ".sdu-install-*").Length = 0, "Failed publication left staging")
            '共享缓存可能已被其他任务使用，失败清理不能删除它。
            EqualFile(Path.Combine(F.Mc, "libraries", "new.jar"), "cache")
        End Using
    End Sub

    Private Sub CacheConflict()
        Using F As New Fixture({Pair("libraries/new.jar", "new cache"), Pair("assets/conflict", "package cache")})
            Dim Conflict = Path.Combine(F.Mc, "assets", "conflict")
            Put(Conflict, "user cache")
            Expect(Of IOException)(Sub() F.Install())
            F.NoPartialInstance()
            EqualFile(Conflict, "user cache")
            Check(Not File.Exists(Path.Combine(F.Mc, "libraries", "new.jar")), "Cache conflict preflight wrote other caches")
        End Using
    End Sub

    Private Sub ReuseCache()
        Using F As New Fixture({Pair("libraries/same.jar", "same cache")})
            Dim Cached = Path.Combine(F.Mc, "libraries", "same.jar")
            Put(Cached, "same cache")
            File.SetLastWriteTimeUtc(Cached, New DateTime(2020, 1, 1))
            Dim OriginalTime = File.GetLastWriteTimeUtc(Cached)
            F.Install()
            EqualFile(Cached, "same cache")
            Check(File.GetLastWriteTimeUtc(Cached) = OriginalTime, "Identical cache was overwritten")
        End Using
    End Sub

    Private Sub InvalidPaths()
        For Each Invalid In {"versions/pack-id/../../escape.txt", "libraries/../escape.jar", "/absolute.txt", "mods/C:evil.jar", "config/CON.txt", "config/file. ", "mods//file.jar"}
            Using F As New Fixture({Pair(Invalid, "bad")})
                Expect(Of InvalidDataException)(Sub() F.Install())
                F.NoPartialInstance()
                Check(Not Directory.Exists(F.Mc), "Invalid archive touched game root")
            End Using
        Next
        Using Data As New MemoryStream()
            Using Writer As New ZipArchive(Data, ZipArchiveMode.Create, True)
                AddEntry(Writer, "versions/pack-id/pack-id.json", "{}")
                AddEntry(Writer, "mods/link.jar", "target").ExternalAttributes = &HA0000000
            End Using
            Data.Position = 0
            Using Archive As New ZipArchive(Data, ZipArchiveMode.Read)
                Expect(Of InvalidDataException)(Sub() SduBareClientInstaller.Install(Archive, SduBareClientInstaller.Detect(Archive), Path.Combine(TestRoot, "invalid-link"), "test", CancellationToken.None, AddressOf SetIsolation))
            End Using
        End Using
    End Sub

    Private Sub Duplicates()
        Using F As New Fixture({Pair("mods/A.jar", "one"), Pair("mods/a.jar", "two")})
            Expect(Of InvalidDataException)(Sub() F.Install())
            F.NoPartialInstance()
        End Using
    End Sub

    Private Sub AmbiguousVersions()
        Using F As New Fixture({Pair("versions/other/other.json", "{}")})
            Expect(Of InvalidDataException)(Sub() F.Install())
            Check(Not Directory.Exists(F.Mc), "Ambiguous package touched game root")
        End Using
    End Sub

    Private Sub ConfigurationPriority()
        For Each Entries In {New KeyValuePair(Of String, String)() {Pair("config/a.cfg", "root"), Pair("versions/pack-id/config/a.cfg", "version")},
                             New KeyValuePair(Of String, String)() {Pair("versions/pack-id/config/a.cfg", "version"), Pair("config/a.cfg", "root")}}
            Using F As New Fixture(Entries)
                F.Install()
                EqualFile(Path.Combine(F.Target, "config", "a.cfg"), "version")
            End Using
        Next
    End Sub

    Private Sub IsolationRequired()
        Using F As New Fixture()
            Expect(Of IOException)(Sub() F.Install(Configure:=Sub(Stage)
                '模拟 WriteIni 写入失败且只记录日志的宿主行为。
            End Sub))
            F.NoPartialInstance()
        End Using
        Using F As New Fixture({Pair("PCL/Setup.ini", "VersionArgumentIndie:0" & vbCrLf & "VersionArgumentIndieV2:False" & vbCrLf)})
            Expect(Of IOException)(Sub() F.Install(Configure:=Sub(Stage)
            End Sub))
            F.NoPartialInstance()
        End Using
    End Sub

    Private Sub SafeNames()
        For Each Value In {"..", "CON", "NUL.txt", " hello. ", "a/b:c", New String("a"c, 100), "COM¹"}
            Dim Name = SduBareClientInstaller.SafeInstanceName(Value)
            Check(Name.Length > 0 AndAlso Name.Length <= 64 AndAlso Not Name.EndsWith(".") AndAlso Name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0, "Unsafe instance name: " & Name)
            Check(Name <> ".." AndAlso Not Name.StartsWith("CON") AndAlso Not Name.StartsWith("NUL") AndAlso Not Name.StartsWith("COM¹"), "Reserved instance name was accepted")
        Next
        Check(SduBareClientInstaller.SafeInstanceName("..", "SDUcraft-project-id") = "SDUcraft-project-id", "Empty title lost its project ID fallback")
    End Sub

    Private Sub InvalidSourceVersion()
        For Each Name In {".", "..", "CON", "trail."}
            Using F As New Fixture({Pair("versions/" & Name & "/" & Name & ".json", "{}")}, Json:=Nothing)
                Expect(Of InvalidDataException)(Sub() F.Install())
                Check(Not Directory.Exists(F.Mc), "Invalid version name touched game root")
            End Using
        Next
    End Sub

    Private Sub ModsOnlyUpdate()
        Using F As New Fixture({Pair("mods/current.jar", "new version"), Pair("mods/new.jar", "new mod"), Pair("config/user.cfg", "package config"),
                Pair("saves/world/level.dat", "package save"), Pair("options.txt", "package options"), Pair("libraries/new.jar", "package cache")})
            Dim Snapshot As New Dictionary(Of String, String) From {
                {"Website Instance.json", "{original version json}"}, {"Website Instance.jar", "original client"},
                {"config/user.cfg", "user config"}, {"saves/world/level.dat", "user save"}, {"screenshots/test.png", "user screenshot"},
                {"options.txt", "user options"}, {"PCL/Setup.ini", "user settings"}, {"PCL/SduPack.json", "user pack identity"},
                {"resourcepacks/user.zip", "user resource"}}
            For Each Item In Snapshot
                Put(Path.Combine(F.Target, Item.Key), Item.Value)
            Next
            Put(Path.Combine(F.Target, "mods", "current.jar"), "old version")
            Put(Path.Combine(F.Target, "mods", "removed.jar"), "removed mod")
            Dim Before = Directory.GetFiles(F.Target, "*", SearchOption.AllDirectories).Where(Function(Value) Not Value.Contains("\mods\")).ToDictionary(Function(Value) Value, Function(Value) File.ReadAllBytes(Value))
            F.Install(Configure:=Sub(Stage) Throw New Exception("Update must not configure or replace version files"))
            Check(F.Result.UpdatedMods AndAlso Directory.Exists(F.Result.ModsBackup), "Update backup missing")
            EqualFile(Path.Combine(F.Target, "mods", "current.jar"), "new version")
            EqualFile(Path.Combine(F.Target, "mods", "new.jar"), "new mod")
            Check(Not File.Exists(Path.Combine(F.Target, "mods", "removed.jar")), "Removed package mod survived synchronization")
            EqualFile(Path.Combine(F.Result.ModsBackup, "current.jar"), "old version")
            EqualFile(Path.Combine(F.Result.ModsBackup, "removed.jar"), "removed mod")
            Check(Not Directory.Exists(Path.Combine(F.Mc, "libraries")), "Mod update wrote shared dependencies")
            For Each Item In Before
                Check(File.ReadAllBytes(Item.Key).SequenceEqual(Item.Value), "Update changed a non-mod file: " & Item.Key)
            Next
            Check(Directory.GetFiles(F.Target, "*", SearchOption.AllDirectories).Where(Function(Value) Not Value.Contains("\mods\")).Count() = Before.Count, "Update added non-mod files")
        End Using
    End Sub

    Private Sub FailedUpdate()
        Using F As New Fixture({Pair("mods/new.jar", "new mod")}), Source As New CancellationTokenSource()
            Put(Path.Combine(F.Target, "Website Instance.json"), "original json")
            Put(Path.Combine(F.Target, "mods", "old.jar"), "original mod")
            Expect(Of OperationCanceledException)(Sub() F.Install(Source.Token, Progress:=Sub(Value) Source.Cancel()))
            EqualFile(Path.Combine(F.Target, "mods", "old.jar"), "original mod")
            Check(Not File.Exists(Path.Combine(F.Target, "mods", "new.jar")), "Canceled update modified current mods")
            Check(Directory.GetDirectories(F.Mc, ".sdu-install-*").Length = 0, "Canceled update left staging")
        End Using
        Using F As New Fixture({Pair("mods/new.jar", "new mod")})
            Put(Path.Combine(F.Target, "Website Instance.json"), "original json")
            Put(Path.Combine(F.Target, "mods", "old.jar"), "original mod")
            Expect(Of IOException)(Sub() F.Install(Progress:=Sub(Value) Throw New IOException("staging failure sentinel")))
            EqualFile(Path.Combine(F.Target, "mods", "old.jar"), "original mod")
            Check(Directory.GetDirectories(F.Mc, ".sdu-install-*").Length = 0, "Failed update left staging")
        End Using
    End Sub

    Private Sub EmptyUpdate()
        Using F As New Fixture()
            Put(Path.Combine(F.Target, "Website Instance.json"), "original json")
            Put(Path.Combine(F.Target, "mods", "old.jar"), "original mod")
            Expect(Of InvalidDataException)(Sub() F.Install())
            EqualFile(Path.Combine(F.Target, "mods", "old.jar"), "original mod")
        End Using
    End Sub

    Private Sub StableIdentity()
        Using F As New Fixture()
            Dim Marker = Path.Combine(F.Mc, "versions", "Pack (1.0)", "PCL", "SduPack.json")
            Put(Marker, "{""sduProjectId"":""project-1""}")
            Check(SduBareClientInstaller.ResolveInstanceName(F.Mc, "project-1", "Pack (2.0)") = "Pack (1.0)", "Website version change created a second instance")
            Check(SduBareClientInstaller.ResolveInstanceName(F.Mc, "another", "Another Pack") = "Another Pack", "Unrelated pack was matched")
            Expect(Of IOException)(Sub() SduBareClientInstaller.ResolveInstanceName(F.Mc, "another", "Pack (1.0)"))
            Put(Path.Combine(F.Mc, "versions", "Duplicate", "PCL", "SduPack.json"), "{""sduProjectId"":""project-1""}")
            Expect(Of IOException)(Sub() SduBareClientInstaller.ResolveInstanceName(F.Mc, "project-1", "Pack (3.0)"))
        End Using
    End Sub

    Private Sub RestoreAfterSwitchFailure()
        Using F As New Fixture()
            Dim Current = Path.Combine(F.Target, "mods")
            Dim Prepared = Path.Combine(F.Root, "prepared")
            Dim Backup = Path.Combine(F.Root, "backup")
            Put(Path.Combine(Current, "old.jar"), "old mod")
            Put(Path.Combine(Prepared, "new.jar"), "new mod")
            Dim Moves As Integer
            Expect(Of IOException)(Sub() SduBareClientInstaller.PublishMods(Prepared, Current, Backup, Sub(FromPath, ToPath)
                Moves += 1
                If Moves = 2 Then Throw New IOException("publish failure sentinel")
                Directory.Move(FromPath, ToPath)
            End Sub))
            EqualFile(Path.Combine(Current, "old.jar"), "old mod")
            Check(Not File.Exists(Path.Combine(Current, "new.jar")), "Failed publication partially replaced mods")
            Check(Not Directory.Exists(Backup), "Restored backup remained detached")
        End Using
    End Sub

    Private Sub PreserveBackupAfterRestoreFailure()
        Using F As New Fixture()
            Dim Current = Path.Combine(F.Target, "mods")
            Dim Prepared = Path.Combine(F.Root, "prepared")
            Dim Backup = Path.Combine(F.Root, "backup")
            Put(Path.Combine(Current, "old.jar"), "old mod")
            Put(Path.Combine(Prepared, "new.jar"), "new mod")
            Dim Moves As Integer
            Dim Failure As IOException = Nothing
            Try
                SduBareClientInstaller.PublishMods(Prepared, Current, Backup, Sub(FromPath, ToPath)
                    Moves += 1
                    If Moves > 1 Then Throw New IOException("blocked move sentinel")
                    Directory.Move(FromPath, ToPath)
                End Sub)
            Catch ex As IOException
                Failure = ex
            End Try
            Check(Failure IsNot Nothing AndAlso Failure.Message.Contains(Backup), "Missing backup recovery path")
            EqualFile(Path.Combine(Backup, "old.jar"), "old mod")
        End Using
    End Sub

    Private Sub SetIsolation(Stage As String)
        Dim IniPath = Path.Combine(Stage, "PCL", "Setup.ini")
        Dim Existing = If(File.Exists(IniPath), File.ReadAllLines(IniPath), New String() {})
        Dim Retained = Existing.Where(Function(Line) Not Line.StartsWith("VersionArgumentIndie:") AndAlso Not Line.StartsWith("VersionArgumentIndieV2:"))
        Put(IniPath, String.Join(vbCrLf, Retained.Concat({"VersionArgumentIndie:1", "VersionArgumentIndieV2:True", ""})))
    End Sub

    Private Function Pair(Name As String, Value As String) As KeyValuePair(Of String, String)
        Return New KeyValuePair(Of String, String)(Name, Value)
    End Function
    Private Function AddEntry(Archive As ZipArchive, Name As String, Value As String) As ZipArchiveEntry
        Dim Entry = Archive.CreateEntry(Name)
        Using Writer As New StreamWriter(Entry.Open(), New UTF8Encoding(False))
            Writer.Write(Value)
        End Using
        Return Entry
    End Function
    Private Sub Put(FilePath As String, Value As String)
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath))
        File.WriteAllText(FilePath, Value, New UTF8Encoding(False))
    End Sub
    Private Sub EqualFile(FilePath As String, Value As String)
        Check(File.Exists(FilePath) AndAlso File.ReadAllText(FilePath) = Value, "File content differs: " & FilePath)
    End Sub
    Private Sub Check(Condition As Boolean, Message As String)
        If Not Condition Then Throw New Exception(Message)
    End Sub
    Private Sub Expect(Of T As Exception)(Work As Action)
        Try
            Work()
        Catch ex As Exception
            If TypeOf ex Is T Then Return
            Throw
        End Try
        Throw New Exception("Expected exception: " & GetType(T).Name)
    End Sub
    Private Sub Run(Name As String, Work As Action)
        Work()
        Passed += 1
        Console.WriteLine("PASS: " & Name)
    End Sub
End Module
