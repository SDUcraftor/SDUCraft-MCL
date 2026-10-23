Imports System.IO
Imports System.Net
Imports System.Net.Http
Imports System.Reflection
Imports System.Threading
Imports System.Threading.Tasks
Imports Newtonsoft.Json.Linq

<Assembly: AssemblyVersion("1.2.3.0")>
Module SduUpdateTests
    Private Root As String
    Private Passed As Integer

    Private Sub Check(Name As String, Test As Action)
        Test()
        Passed += 1
        Console.WriteLine("PASS " & Name)
    End Sub

    Private Sub Assert(Condition As Boolean, Optional Message As String = "Assertion failed")
        If Not Condition Then Throw New Exception(Message)
    End Sub

    Private Sub Reject(Action As Action)
        Try
            Action()
        Catch
            Return
        End Try
        Throw New Exception("Expected rejection")
    End Sub

    Private Function Manifest() As JObject
        Return New JObject From {{"schemaVersion", 1}, {"version", "1.2.3"}, {"notes", "test"},
            {"exe", New JObject From {{"size", 2048}, {"sha256", New String("a"c, 64)}}},
            {"config", New JObject From {{"size", 20}, {"sha256", New String("b"c, 64)}}}}
    End Function

    Private Function Fixture(Optional HasConfig As Boolean = True) As SduUpdateCore.InstallPlan
        Dim Folder = Path.Combine(Root, Guid.NewGuid().ToString("N"))
        Dim Stage = Path.Combine(Folder, ".sdu-update-" & Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(Stage)
        Dim Target = Path.Combine(Folder, "My custom launcher.exe")
        File.Copy(Assembly.GetExecutingAssembly().Location, Target)
        File.AppendAllText(Target, "old-version-marker")
        If HasConfig Then File.WriteAllText(Target & ".config", "<configuration><!--old--></configuration>")
        File.Copy(Assembly.GetExecutingAssembly().Location, Path.Combine(Stage, SduUpdateCore.ExeName))
        File.WriteAllText(Path.Combine(Stage, SduUpdateCore.ConfigName), "<configuration><!--new--></configuration>")
        File.WriteAllText(Path.Combine(Folder, "user-settings.ini"), "keep-settings")
        Directory.CreateDirectory(Path.Combine(Folder, "game"))
        File.WriteAllText(Path.Combine(Folder, "game", "save.dat"), "keep-save")
        Dim Release = SduUpdateCore.ParseManifest(Manifest().ToString())
        Release.ExeHash = SduUpdateCore.HashFile(Path.Combine(Stage, SduUpdateCore.ExeName))
        Release.ExeSize = New FileInfo(Path.Combine(Stage, SduUpdateCore.ExeName)).Length
        Release.ConfigHash = SduUpdateCore.HashFile(Path.Combine(Stage, SduUpdateCore.ConfigName))
        Release.ConfigSize = New FileInfo(Path.Combine(Stage, SduUpdateCore.ConfigName)).Length
        Return SduUpdateCore.CreatePlan(Stage, Target, Release)
    End Function

    Private Function Fetch(Handler As HttpMessageHandler, Optional Limit As Long = 100, Optional Token As CancellationToken = Nothing) As Boolean
        Return SduUpdateCore.Download(SduUpdateCore.LatestManifestUrl, Path.Combine(Root, Guid.NewGuid().ToString("N")), Limit, Token, Handler:=Handler)
    End Function

    Sub Main()
        Root = Path.Combine(Path.GetTempPath(), "sdu-update-tests-" & Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(Root)
        Try
            Check("stable manifest and pinned asset URL", Sub()
                Dim Info = SduUpdateCore.ParseManifest(Manifest().ToString())
                Assert(Info.Version = New Version(1, 2, 3, 0))
                Assert(Info.AssetBase = SduUpdateCore.ReleasesUrl & "/download/v1.2.3/")
            End Sub)
            For Each Invalid In New String() {"v1.2.3", "1.2", "1.2.3-beta", "01.2.3", "1.2.3.4", "65535.0.0", "9999999999999.0.0", ""}
                Dim Value = Invalid
                Check("invalid version " & Value, Sub() Reject(Sub() SduUpdateCore.ParseVersion(Value)))
            Next
            For Each Key In New String() {"schemaVersion", "hash", "size", "config"}
                Dim Item = Key
                Check("invalid manifest " & Item, Sub()
                    Dim Json = Manifest()
                    Select Case Item
                        Case "schemaVersion" : Json(Item) = 2
                        Case "hash" : Json("exe")("sha256") = "bad"
                        Case "size" : Json("exe")("size") = SduUpdateCore.MaxExeSize + 1
                        Case "config" : Json("config")("size") = 0
                    End Select
                    Reject(Sub() SduUpdateCore.ParseManifest(Json.ToString()))
                End Sub)
            Next
            Check("HTTPS redirect and credential isolation", Sub()
                Dim Calls As Integer
                Assert(Fetch(New FakeHandler(Function(Request, Token)
                    Calls += 1
                    Assert(Request.Headers.Authorization Is Nothing AndAlso Not Request.Headers.Contains("x-api-key") AndAlso Not Request.Headers.Contains("Cookie"))
                    Assert(Request.Headers.UserAgent.ToString().Contains("SDUCraft"))
                    If Calls = 1 Then
                        Dim Response As New HttpResponseMessage(HttpStatusCode.Redirect)
                        Response.Headers.Location = New Uri("https://release-assets.githubusercontent.com/test")
                        Return Response
                    End If
                    Return New HttpResponseMessage(HttpStatusCode.OK) With {.Content = New StringContent("ok")}
                End Function)))
                Assert(Calls = 2)
            End Sub)
            Check("mirror success never contacts direct GitHub", Sub()
                Dim Calls As Integer
                Assert(Fetch(New FakeHandler(Function(Request, Token)
                    Calls += 1
                    Assert(Request.RequestUri.AbsoluteUri = SduUpdateCore.MirrorPrefix & SduUpdateCore.LatestManifestUrl)
                    Return New HttpResponseMessage(HttpStatusCode.OK) With {.Content = New StringContent("ok")}
                End Function)))
                Assert(Calls = 1)
            End Sub)
            For Each Failure In New String() {"http", "network", "timeout", "404"}
                Dim Kind = Failure
                Check("mirror " & Kind & " failure falls back once", Sub()
                    Dim Calls As New List(Of String)
                    Assert(Fetch(New FakeHandler(Function(Request, Token)
                        Calls.Add(Request.RequestUri.Host)
                        If Calls.Count = 1 Then
                            Assert(Request.RequestUri.Host = "gh-proxy.com")
                            Select Case Kind
                                Case "network" : Throw New HttpRequestException("mirror unavailable")
                                Case "timeout" : Throw New TaskCanceledException("mirror timeout")
                                Case "404" : Return New HttpResponseMessage(HttpStatusCode.NotFound)
                                Case Else : Return New HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                            End Select
                        End If
                        Assert(Request.RequestUri.AbsoluteUri = SduUpdateCore.LatestManifestUrl)
                        Return New HttpResponseMessage(HttpStatusCode.OK) With {.Content = New StringContent("ok")}
                    End Function)))
                    Assert(Calls.SequenceEqual(New String() {"gh-proxy.com", "github.com"}))
                End Sub)
            Next
            Check("invalid mirror manifest falls back to verified direct metadata", Sub()
                Dim Calls As Integer
                Dim Destination = Path.Combine(Root, "manifest-fallback.json")
                Assert(SduUpdateCore.Download(SduUpdateCore.LatestManifestUrl, Destination, 4096, CancellationToken.None,
                    Handler:=New FakeHandler(Function(Request, Token)
                        Calls += 1
                        Return New HttpResponseMessage(HttpStatusCode.OK) With {.Content = New StringContent(If(Calls = 1, "<html>proxy error</html>", Manifest().ToString()))}
                    End Function), Validate:=Sub(Name) SduUpdateCore.ParseManifest(File.ReadAllText(Name))))
                Assert(Calls = 2 AndAlso SduUpdateCore.ParseManifest(File.ReadAllText(Destination)).VersionText = "1.2.3")
            End Sub)
            Check("bad mirror hash falls back and removes partial files", Sub()
                Dim Destination = Path.Combine(Root, "hash-fallback.exe")
                Dim Reference = Path.Combine(Root, "hash-reference")
                File.WriteAllText(Reference, "good")
                Dim Expected = SduUpdateCore.HashFile(Reference)
                Dim Calls As Integer
                Assert(SduUpdateCore.Download(SduUpdateCore.LatestManifestUrl, Destination, 100, CancellationToken.None,
                    Handler:=New FakeHandler(Function(Request, Token)
                        Calls += 1
                        Return New HttpResponseMessage(HttpStatusCode.OK) With {.Content = New StringContent(If(Calls = 1, "bad!", "good"))}
                    End Function), Validate:=Sub(Name) SduUpdateCore.VerifyFile(Name, Expected, 4)))
                Assert(Calls = 2 AndAlso File.ReadAllText(Destination) = "good")
                Assert(Directory.GetFiles(Root, "*.download-*").Length = 0)
            End Sub)
            Check("interrupted mirror body starts direct download from empty file", Sub()
                Dim Calls As Integer
                Dim Destination = Path.Combine(Root, "body-fallback")
                Assert(SduUpdateCore.Download(SduUpdateCore.LatestManifestUrl, Destination, 100, CancellationToken.None,
                    Handler:=New FakeHandler(Function(Request, Token)
                        Calls += 1
                        Dim Body As HttpContent = If(Calls = 1, CType(New StreamContent(New BrokenStream()), HttpContent), New StringContent("good"))
                        Return New HttpResponseMessage(HttpStatusCode.OK) With {.Content = Body}
                    End Function)))
                Assert(Calls = 2 AndAlso File.ReadAllText(Destination) = "good")
                Assert(Directory.GetFiles(Root, "*.download-*").Length = 0)
            End Sub)
            Check("user cancellation during mirror prevents GitHub retry", Sub()
                Using Source As New CancellationTokenSource()
                    Dim Calls As Integer
                    Reject(Sub() Fetch(New FakeHandler(Function(Request, Token)
                        Calls += 1
                        Source.Cancel()
                        Token.ThrowIfCancellationRequested()
                        Return New HttpResponseMessage(HttpStatusCode.OK)
                    End Function), Token:=Source.Token))
                    Assert(Calls = 1)
                End Using
            End Sub)
            Check("existing destination is never overwritten or downloaded", Sub()
                Dim Destination = Path.Combine(Root, "keep-existing")
                File.WriteAllText(Destination, "keep")
                Dim Calls As Integer
                Reject(Sub() SduUpdateCore.Download(SduUpdateCore.LatestManifestUrl, Destination, 100, CancellationToken.None,
                    Handler:=New FakeHandler(Function(Request, Token)
                        Calls += 1
                        Return New HttpResponseMessage(HttpStatusCode.OK)
                    End Function)))
                Assert(Calls = 0 AndAlso File.ReadAllText(Destination) = "keep")
            End Sub)
            For Each Location In New String() {"http://github.com/test", "https://evil.example/test", "https://github.com.evil.example/test", "https://name@github.com/test", "https://github.com:444/test",
                "https://gh-proxy.com.evil.example/test", "https://sub.gh-proxy.com/test", "https://gh-proxy.com/https://evil.example/test", "https://gh-proxy.com/https://github.com/other/repo/releases/download/v1/test"}
                Dim Url = Location
                Check("reject unsafe redirect " & Url, Sub() Reject(Sub() Fetch(New FakeHandler(Function(Request, Token)
                    Dim Response As New HttpResponseMessage(HttpStatusCode.Redirect)
                    Response.Headers.Location = New Uri(Url)
                    Return Response
                End Function))))
            Next
            Check("missing release returns false", Sub() Assert(Not Fetch(New FakeHandler(Function(Request, Token) New HttpResponseMessage(HttpStatusCode.NotFound)))))
            Check("HTTP failure rejected", Sub() Reject(Sub() Fetch(New FakeHandler(Function(Request, Token) New HttpResponseMessage(HttpStatusCode.Forbidden)))))
            Check("redirect loop rejected", Sub() Reject(Sub() Fetch(New FakeHandler(Function(Request, Token)
                Dim Response As New HttpResponseMessage(HttpStatusCode.Redirect)
                Response.Headers.Location = New Uri(SduUpdateCore.LatestManifestUrl)
                Return Response
            End Function))))
            Check("oversize response rejected", Sub() Reject(Sub() Fetch(New FakeHandler(Function(Request, Token) New HttpResponseMessage(HttpStatusCode.OK) With {.Content = New StringContent(New String("x"c, 101))}))))
            Check("oversize stream without Content-Length rejected", Sub() Reject(Sub() Fetch(New FakeHandler(Function(Request, Token) New HttpResponseMessage(HttpStatusCode.OK) With {.Content = New StreamContent(New HiddenLengthStream(New Byte(100) {}))}))))
            Check("cancellation rejected", Sub()
                Using Source As New CancellationTokenSource()
                    Source.Cancel()
                    Reject(Sub() Fetch(New FakeHandler(Function(Request, Token)
                        Token.ThrowIfCancellationRequested()
                        Return New HttpResponseMessage(HttpStatusCode.OK)
                    End Function), Token:=Source.Token))
                End Using
            End Sub)
            Check("plan round trip", Sub()
                Dim Plan = Fixture()
                Plan.Restart = True
                SduUpdateCore.WritePlan(Plan)
                Dim Read = SduUpdateCore.ReadPlan(Path.Combine(Plan.Stage, "update-plan.json"), Path.Combine(Plan.Stage, SduUpdateCore.ExeName))
                Assert(Read.Restart AndAlso Read.Target = Plan.Target AndAlso Read.OriginalHash = Plan.OriginalHash)
            End Sub)
            Check("target outside stage parent rejected", Sub()
                Dim Plan = Fixture()
                Plan.Target = Path.Combine(Root, "outside.exe")
                SduUpdateCore.WritePlan(Plan)
                Reject(Sub() SduUpdateCore.ReadPlan(Path.Combine(Plan.Stage, "update-plan.json"), Path.Combine(Plan.Stage, SduUpdateCore.ExeName)))
            End Sub)
            Check("wrong helper path rejected", Sub()
                Dim Plan = Fixture()
                SduUpdateCore.WritePlan(Plan)
                Reject(Sub() SduUpdateCore.ReadPlan(Path.Combine(Plan.Stage, "update-plan.json"), Plan.Target))
            End Sub)
            For Each HasConfig In New Boolean() {True, False}
                Dim WithConfig = HasConfig
                Check("successful replacement config=" & WithConfig, Sub()
                    Dim Plan = Fixture(WithConfig)
                    SduUpdateCore.Publish(Plan)
                    Assert(SduUpdateCore.HashFile(Plan.Target) = Plan.Release.ExeHash)
                    Assert(SduUpdateCore.HashFile(Plan.Target & ".config") = Plan.Release.ConfigHash)
                    Assert(SduUpdateCore.HashFile(Path.Combine(Plan.Stage, "backup.exe")) = Plan.OriginalHash)
                    Assert(File.Exists(Path.Combine(Plan.Stage, "backup.exe.config")) = WithConfig)
                    Dim Folder = Path.GetDirectoryName(Plan.Target)
                    Assert(File.ReadAllText(Path.Combine(Folder, "user-settings.ini")) = "keep-settings")
                    Assert(File.ReadAllText(Path.Combine(Folder, "game", "save.dat")) = "keep-save")
                End Sub)
            Next
            For FailureStep = 1 To 4
                Dim FailAt = FailureStep
                Check("rollback after move failure " & FailAt, Sub()
                    Dim Plan = Fixture()
                    Dim Count As Integer
                    Reject(Sub() SduUpdateCore.Publish(Plan, Sub(From, Destination)
                        Count += 1
                        If Count = FailAt Then Throw New IOException("injected failure")
                        File.Move(From, Destination)
                    End Sub))
                    Assert(SduUpdateCore.HashFile(Plan.Target) = Plan.OriginalHash)
                    Assert(SduUpdateCore.HashFile(Plan.Target & ".config") = Plan.OriginalConfigHash)
                End Sub)
            Next
            For Each Damage In New String() {"exe", "version", "config", "old-exe", "old-config"}
                Dim Kind = Damage
                Check("reject changed " & Kind, Sub()
                    Dim Plan = Fixture()
                    Select Case Kind
                        Case "exe" : File.AppendAllText(Path.Combine(Plan.Stage, SduUpdateCore.ExeName), "tampered")
                        Case "version" : Plan.Release.Version = New Version(9, 0, 0, 0)
                        Case "config"
                            Dim Config = Path.Combine(Plan.Stage, SduUpdateCore.ConfigName)
                            File.WriteAllText(Config, "<!DOCTYPE configuration [<!ENTITY a 'bad'>]><configuration>&a;</configuration>")
                            Plan.Release.ConfigHash = SduUpdateCore.HashFile(Config)
                            Plan.Release.ConfigSize = New FileInfo(Config).Length
                        Case "old-exe" : File.AppendAllText(Plan.Target, "changed")
                        Case "old-config" : File.AppendAllText(Plan.Target & ".config", "changed")
                    End Select
                    Dim Before = SduUpdateCore.HashFile(Plan.Target)
                    Dim ConfigBefore = SduUpdateCore.HashFile(Plan.Target & ".config")
                    Reject(Sub() SduUpdateCore.Publish(Plan))
                    Assert(SduUpdateCore.HashFile(Plan.Target) = Before AndAlso SduUpdateCore.HashFile(Plan.Target & ".config") = ConfigBefore)
                End Sub)
            Next
            Check("PID reuse does not wait or kill", Sub()
                Dim Plan = Fixture()
                Plan.ParentStarted = 0
                SduUpdateCore.WaitForParent(Plan)
            End Sub)
            Console.WriteLine("Updater lifecycle: " & Passed & " passed")
        Catch ex As Exception
            Console.Error.WriteLine(ex)
            Environment.ExitCode = 1
        Finally
            Directory.Delete(Root, True)
        End Try
    End Sub

    Private Class FakeHandler
        Inherits HttpMessageHandler
        Private ReadOnly Respond As Func(Of HttpRequestMessage, CancellationToken, HttpResponseMessage)
        Public Sub New(Respond As Func(Of HttpRequestMessage, CancellationToken, HttpResponseMessage))
            Me.Respond = Respond
        End Sub
        Protected Overrides Function SendAsync(Request As HttpRequestMessage, Token As CancellationToken) As Task(Of HttpResponseMessage)
            Return Task.FromResult(Respond(Request, Token))
        End Function
    End Class

    Private Class HiddenLengthStream
        Inherits MemoryStream
        Public Sub New(Bytes As Byte())
            MyBase.New(Bytes)
        End Sub
        Public Overrides ReadOnly Property CanSeek As Boolean
            Get
                Return False
            End Get
        End Property
    End Class

    Private Class BrokenStream
        Inherits HiddenLengthStream
        Private Reads As Integer
        Public Sub New()
            MyBase.New(New Byte() {})
        End Sub
        Public Overrides Function ReadAsync(Buffer As Byte(), Offset As Integer, Count As Integer, Token As CancellationToken) As Task(Of Integer)
            Reads += 1
            If Reads > 1 Then Throw New IOException("interrupted mirror stream")
            Array.Copy(System.Text.Encoding.UTF8.GetBytes("bad"), 0, Buffer, Offset, 3)
            Return Task.FromResult(3)
        End Function
    End Class
End Module
