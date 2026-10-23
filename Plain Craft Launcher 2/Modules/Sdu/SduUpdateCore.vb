Imports System.IO
Imports System.Net
Imports System.Net.Http
Imports System.Reflection
Imports System.Security.Cryptography
Imports System.Text
Imports System.Text.RegularExpressions
Imports System.Threading
Imports Newtonsoft.Json.Linq

''' <summary>启动器更新协议、下载校验与文件发布；不依赖界面或用户设置。</summary>
Public NotInheritable Class SduUpdateCore
    Private Sub New()
    End Sub
    Public Const Repository As String = "SDUcraftor/SDUCraft-PCL"
    Public Const ReleasesUrl As String = "https://github.com/" & Repository & "/releases"
    Public Const ManifestName As String = "sducraft-update.json"
    Public Const ExeName As String = "SDUCraft-Launcher.exe"
    Public Const ConfigName As String = ExeName & ".config"
    Public Const LatestManifestUrl As String = ReleasesUrl & "/latest/download/" & ManifestName
    Public Const MaxExeSize As Long = 256L * 1024 * 1024

    Public Class ReleaseInfo
        Public Version As Version
        Public VersionText As String
        Public Notes As String
        Public Critical As Boolean
        Public ExeHash As String
        Public ExeSize As Long
        Public ConfigHash As String
        Public ConfigSize As Long
        Public ReadOnly Property AssetBase As String
            Get
                Return ReleasesUrl & "/download/v" & VersionText & "/"
            End Get
        End Property
    End Class

    Public Class InstallPlan
        Public Target As String
        Public ParentId As Integer
        Public ParentStarted As Long
        Public Restart As Boolean
        Public OriginalHash As String
        Public OriginalConfigHash As String
        Public Release As ReleaseInfo
        Public Stage As String
    End Class

    Public Shared Function ParseVersion(Text As String) As Version
        If Not Regex.IsMatch(If(Text, ""), "^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$") Then Throw New InvalidDataException("更新版本号无效。")
        Dim Parsed = New Version(Text)
        If Parsed.Major > 65534 OrElse Parsed.Minor > 65534 OrElse Parsed.Build > 65534 Then Throw New InvalidDataException("更新版本号超出范围。")
        Return New Version(Parsed.Major, Parsed.Minor, Parsed.Build, 0)
    End Function

    Public Shared Function ParseManifest(Text As String) As ReleaseInfo
        If Text Is Nothing OrElse Text.Length > 256 * 1024 Then Throw New InvalidDataException("更新信息过大。")
        Dim Json = JObject.Parse(Text)
        If CInt(Json("schemaVersion")) <> 1 Then Throw New InvalidDataException("不支持此更新信息格式。")
        Dim Result As New ReleaseInfo With {
            .VersionText = CStr(Json("version")), .Notes = If(CStr(Json("notes")), ""),
            .Critical = If(Json("critical") Is Nothing, False, CBool(Json("critical"))),
            .ExeHash = CStr(Json("exe")("sha256")), .ExeSize = CLng(Json("exe")("size")),
            .ConfigHash = CStr(Json("config")("sha256")), .ConfigSize = CLng(Json("config")("size"))}
        Result.Version = ParseVersion(Result.VersionText)
        If Result.Notes.Length > 4000 Then Result.Notes = Result.Notes.Substring(0, 4000)
        ValidateHash(Result.ExeHash)
        ValidateHash(Result.ConfigHash)
        If Result.ExeSize < 1024 OrElse Result.ExeSize > MaxExeSize OrElse Result.ConfigSize <= 0 OrElse Result.ConfigSize > 1024 * 1024 Then
            Throw New InvalidDataException("更新文件大小无效。")
        End If
        Return Result
    End Function

    Private Shared Sub ValidateHash(Value As String)
        If Not Regex.IsMatch(If(Value, ""), "^[a-fA-F0-9]{64}$") Then Throw New InvalidDataException("缺少有效的 SHA-256 校验信息。")
    End Sub

    Public Shared Function HashFile(FileName As String) As String
        Using Input = File.OpenRead(FileName), Hasher = SHA256.Create()
            Return BitConverter.ToString(Hasher.ComputeHash(Input)).Replace("-", "").ToLowerInvariant()
        End Using
    End Function

    Public Shared Sub VerifyFile(FileName As String, ExpectedHash As String, ExpectedSize As Long)
        ValidateHash(ExpectedHash)
        If Not File.Exists(FileName) OrElse New FileInfo(FileName).Length <> ExpectedSize OrElse
           Not String.Equals(HashFile(FileName), ExpectedHash, StringComparison.OrdinalIgnoreCase) Then
            Throw New InvalidDataException("更新文件校验失败：" & Path.GetFileName(FileName))
        End If
    End Sub

    Private Shared Function IsAllowedLocation(Location As Uri) As Boolean
        Return Location IsNot Nothing AndAlso Location.IsAbsoluteUri AndAlso Location.Scheme = Uri.UriSchemeHttps AndAlso
            Location.IsDefaultPort AndAlso Location.UserInfo = "" AndAlso Location.Fragment = "" AndAlso
            (Location.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) OrElse
             Location.Host.EndsWith(".githubusercontent.com", StringComparison.OrdinalIgnoreCase))
    End Function

    ''' <summary>独立 HTTP 客户端；不继承游戏下载器的凭据、API Key 或 Cookie。</summary>
    Public Shared Function Download(Url As String, Destination As String, Limit As Long, Token As CancellationToken,
                             Optional Progress As Action(Of Double) = Nothing, Optional Handler As HttpMessageHandler = Nothing) As Boolean
        Dim Location As New Uri(Url)
        Using Client As New HttpClient(If(Handler, New HttpClientHandler With {.AllowAutoRedirect = False, .UseCookies = False}))
            Client.Timeout = TimeSpan.FromSeconds(30)
            For Redirect = 0 To 5
                If Not IsAllowedLocation(Location) Then Throw New InvalidDataException("更新下载被重定向到非允许的地址。")
                Using Request As New HttpRequestMessage(HttpMethod.Get, Location)
                    Request.Headers.UserAgent.ParseAdd("SDUCraft-Launcher/Updater")
                    Request.Headers.CacheControl = New Headers.CacheControlHeaderValue With {.NoCache = True}
                    Using Response = Client.SendAsync(Request, HttpCompletionOption.ResponseHeadersRead, Token).GetAwaiter().GetResult()
                        If Response.StatusCode = HttpStatusCode.NotFound Then Return False
                        If CInt(Response.StatusCode) >= 300 AndAlso CInt(Response.StatusCode) < 400 Then
                            If Response.Headers.Location Is Nothing Then Throw New IOException("更新下载缺少重定向地址。")
                            Location = New Uri(Location, Response.Headers.Location)
                            Continue For
                        End If
                        Response.EnsureSuccessStatusCode()
                        If Response.Content.Headers.ContentLength.HasValue AndAlso Response.Content.Headers.ContentLength.Value > Limit Then Throw New InvalidDataException("更新文件超过大小限制。")
                        Using Timeout = CancellationTokenSource.CreateLinkedTokenSource(Token)
                            Timeout.CancelAfter(TimeSpan.FromMinutes(10))
                            Using Input = Response.Content.ReadAsStreamAsync().GetAwaiter().GetResult(), Output As New FileStream(Destination, FileMode.CreateNew, FileAccess.Write)
                                Dim Buffer(81919) As Byte
                                Dim Total As Long = 0
                                While True
                                    Dim Count = Input.ReadAsync(Buffer, 0, Buffer.Length, Timeout.Token).GetAwaiter().GetResult()
                                    If Count = 0 Then Exit While
                                    Total += Count
                                    If Total > Limit Then Throw New InvalidDataException("更新文件超过大小限制。")
                                    Output.Write(Buffer, 0, Count)
                                    If Progress IsNot Nothing Then Progress(Math.Min(1, CDbl(Total) / Limit))
                                End While
                            End Using
                        End Using
                        Return True
                    End Using
                End Using
            Next
        End Using
        Throw New IOException("更新下载重定向次数过多。")
    End Function

    Public Shared Function CreatePlan(Stage As String, Target As String, Release As ReleaseInfo) As InstallPlan
        Dim Process = Diagnostics.Process.GetCurrentProcess()
        Return New InstallPlan With {
            .Stage = Path.GetFullPath(Stage), .Target = Path.GetFullPath(Target), .Release = Release,
            .ParentId = Process.Id, .ParentStarted = Process.StartTime.ToUniversalTime().Ticks,
            .OriginalHash = HashFile(Target), .OriginalConfigHash = If(File.Exists(Target & ".config"), HashFile(Target & ".config"), Nothing)}
    End Function

    Public Shared Sub WritePlan(Plan As InstallPlan)
        Dim Json As New JObject From {
            {"target", Plan.Target}, {"parentId", Plan.ParentId}, {"parentStarted", Plan.ParentStarted}, {"restart", Plan.Restart},
            {"originalHash", Plan.OriginalHash}, {"originalConfigHash", Plan.OriginalConfigHash},
            {"release", New JObject From {
                {"schemaVersion", 1}, {"version", Plan.Release.VersionText},
                {"exe", New JObject From {{"sha256", Plan.Release.ExeHash}, {"size", Plan.Release.ExeSize}}},
                {"config", New JObject From {{"sha256", Plan.Release.ConfigHash}, {"size", Plan.Release.ConfigSize}}}}}}
        File.WriteAllText(Path.Combine(Plan.Stage, "update-plan.json"), Json.ToString(), New UTF8Encoding(False))
    End Sub

    Public Shared Function ReadPlan(PlanFile As String, RunningExe As String) As InstallPlan
        If New FileInfo(PlanFile).Length > 256 * 1024 Then Throw New InvalidDataException("更新计划过大。")
        Dim Json = JObject.Parse(File.ReadAllText(PlanFile, Encoding.UTF8))
        Dim Stage = Path.GetDirectoryName(Path.GetFullPath(RunningExe))
        Dim Target = Path.GetFullPath(CStr(Json("target")))
        If Not Regex.IsMatch(Path.GetFileName(Stage), "^\.sdu-update-[a-f0-9]{32}$") OrElse
           Not SamePath(Path.GetDirectoryName(Stage), Path.GetDirectoryName(Target)) OrElse
           Not SamePath(RunningExe, Path.Combine(Stage, ExeName)) OrElse
           Not SamePath(PlanFile, Path.Combine(Stage, "update-plan.json")) OrElse
           Not Path.GetExtension(Target).Equals(".exe", StringComparison.OrdinalIgnoreCase) Then Throw New InvalidDataException("更新目标或暂存位置无效。")
        RejectReparse(Stage)
        RejectReparse(Target)
        RejectReparse(Target & ".config")
        Dim Plan As New InstallPlan With {
            .Target = Target, .Stage = Stage, .Release = ParseManifest(Json("release").ToString()),
            .ParentId = CInt(Json("parentId")), .ParentStarted = CLng(Json("parentStarted")), .Restart = CBool(Json("restart")),
            .OriginalHash = CStr(Json("originalHash")), .OriginalConfigHash = CStr(Json("originalConfigHash"))}
        ValidateHash(Plan.OriginalHash)
        If Plan.OriginalConfigHash IsNot Nothing Then ValidateHash(Plan.OriginalConfigHash)
        Return Plan
    End Function

    Private Shared Function SamePath(Left As String, Right As String) As Boolean
        Return String.Equals(Path.GetFullPath(Left).TrimEnd(Path.DirectorySeparatorChar), Path.GetFullPath(Right).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)
    End Function

    Private Shared Sub RejectReparse(Location As String)
        Dim Current = Path.GetFullPath(Location)
        While Current IsNot Nothing
            If (File.Exists(Current) OrElse Directory.Exists(Current)) AndAlso (File.GetAttributes(Current) And FileAttributes.ReparsePoint) <> 0 Then Throw New IOException("更新路径包含链接或重解析目录。")
            Current = Path.GetDirectoryName(Current)
        End While
    End Sub

    Public Shared Sub VerifyPrepared(Plan As InstallPlan)
        VerifyFile(Path.Combine(Plan.Stage, ExeName), Plan.Release.ExeHash, Plan.Release.ExeSize)
        VerifyFile(Path.Combine(Plan.Stage, ConfigName), Plan.Release.ConfigHash, Plan.Release.ConfigSize)
        If AssemblyName.GetAssemblyName(Path.Combine(Plan.Stage, ExeName)).Version <> Plan.Release.Version Then Throw New InvalidDataException("更新程序的内部版本号与更新信息不一致。")
        Dim Config As New System.Xml.XmlDocument With {.XmlResolver = Nothing}
        Using Reader = System.Xml.XmlReader.Create(Path.Combine(Plan.Stage, ConfigName), New System.Xml.XmlReaderSettings With {.DtdProcessing = System.Xml.DtdProcessing.Prohibit, .XmlResolver = Nothing})
            Config.Load(Reader)
        End Using
        If Config.DocumentElement Is Nothing OrElse Config.DocumentElement.Name <> "configuration" Then Throw New InvalidDataException("更新配置文件无效。")
    End Sub

    Public Shared Sub WaitForParent(Plan As InstallPlan)
        Try
            Using Parent = Diagnostics.Process.GetProcessById(Plan.ParentId)
                If Parent.StartTime.ToUniversalTime().Ticks <> Plan.ParentStarted Then Return
                If Not Parent.WaitForExit(120000) Then Throw New IOException("旧启动器尚未退出，更新已取消。")
            End Using
        Catch ex As ArgumentException
            '旧进程已经退出。
        End Try
    End Sub

    ''' <summary>只替换当前 EXE 和相邻配置；保留备份，任一步失败均尝试恢复。</summary>
    Public Shared Sub Publish(Plan As InstallPlan, Optional Move As Action(Of String, String) = Nothing)
        VerifyPrepared(Plan)
        RejectReparse(Plan.Target)
        RejectReparse(Plan.Target & ".config")
        If Not File.Exists(Plan.Target) OrElse HashFile(Plan.Target) <> Plan.OriginalHash Then Throw New IOException("旧启动器已改变，已停止替换。")
        Dim ConfigExists = File.Exists(Plan.Target & ".config")
        If ConfigExists <> (Plan.OriginalConfigHash IsNot Nothing) OrElse
           (ConfigExists AndAlso HashFile(Plan.Target & ".config") <> Plan.OriginalConfigHash) Then Throw New IOException("旧配置已改变，已停止替换。")
        Dim Mover = If(Move, AddressOf File.Move)
        Dim BackupExe = Path.Combine(Plan.Stage, "backup.exe")
        Dim BackupConfig = Path.Combine(Plan.Stage, "backup.exe.config")
        Dim NewExe = Path.Combine(Plan.Stage, "publish.exe")
        Dim NewConfig = Path.Combine(Plan.Stage, "publish.exe.config")
        If File.Exists(BackupExe) OrElse File.Exists(BackupConfig) OrElse File.Exists(NewExe) OrElse File.Exists(NewConfig) Then Throw New IOException("更新发布目录已被使用。")
        File.Copy(Path.Combine(Plan.Stage, ExeName), NewExe)
        File.Copy(Path.Combine(Plan.Stage, ConfigName), NewConfig)
        Dim SavedExe As Boolean, SavedConfig As Boolean, PublishedExe As Boolean, PublishedConfig As Boolean
        Try
            Mover(Plan.Target, BackupExe) : SavedExe = True
            If ConfigExists Then
                Mover(Plan.Target & ".config", BackupConfig) : SavedConfig = True
            End If
            Mover(NewExe, Plan.Target) : PublishedExe = True
            Mover(NewConfig, Plan.Target & ".config") : PublishedConfig = True
        Catch Failure As Exception
            Try
                If PublishedConfig Then File.Delete(Plan.Target & ".config")
                If PublishedExe Then File.Delete(Plan.Target)
                If SavedExe Then File.Move(BackupExe, Plan.Target)
                If SavedConfig Then File.Move(BackupConfig, Plan.Target & ".config")
            Catch RestoreError As Exception
                Throw New IOException("更新失败，旧文件保留在备份目录，请手动恢复：" & Plan.Stage, New AggregateException(Failure, RestoreError))
            End Try
            Throw New IOException("更新失败，已恢复旧启动器。", Failure)
        End Try
    End Sub
End Class
