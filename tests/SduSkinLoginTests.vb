Imports System.Runtime.CompilerServices

Module SduSkinLoginTests
    Private Passed As Integer
    Sub Main()
        Try
            Settings.Reset()
            SduSkinLogin.EnsureConfiguration()
            Check(Settings.Get(Of Integer)("LoginType") = 3 AndAlso Settings.Get(Of Integer)("LoginPageType") = 4, "Fresh launch did not select skin login")
            Check(Settings.Get(Of String)("CacheAuthServerServer") = SduSkinLogin.AuthServer, "Wrong global auth server")
            Check(SduSkinLogin.RegisterPage = "https://skin.sducraft.cn/auth/register", "Registration link incorrectly uses the API path")
            Pass("fresh launch uses the confirmed skin site and correct registration path")

            Settings.Reset()
            Settings.Set("CacheAuthServerServer", "https://another-site.invalid/api/yggdrasil")
            Dim CredentialKeys = {"CacheAuthAccess", "CacheAuthClient", "CacheAuthUuid", "CacheAuthName", "CacheAuthUsername", "CacheAuthPass", "LoginAuthEmail", "LoginAuthPass"}
            For Each Key In CredentialKeys
                Settings.Set(Key, "foreign-site-secret-fixture")
            Next
            SduSkinLogin.EnsureConfiguration()
            Check(CredentialKeys.All(Function(Key) Settings.Get(Of String)(Key) = ""), "Foreign-site credentials survived migration")
            Pass("foreign-site credentials and tokens are not reused for SDUcraft")

            For Each OldMode In {0, 1, 2, 3, 99}
                Settings.Reset()
                Settings.Set("CacheAuthServerServer", SduSkinLogin.AuthServer)
                Settings.Set("CacheAuthAccess", "sdu-token-fixture")
                Settings.Set("LoginAuthPass", "sdu-password-fixture")
                Settings.Set("LoginType", 5)
                Dim Instance As New McInstance()
                Settings.Set("VersionServerLogin", OldMode, Instance)
                Settings.Set("VersionServerAuthServer", "https://old.invalid/api/yggdrasil", Instance)
                SduSkinLogin.EnsureConfiguration(Instance)
                Check(Settings.Get(Of Integer)("LoginType") = 3 AndAlso Settings.Get(Of Integer)("VersionServerLogin", Instance) = 4, "Old mode was reactivated")
                Check(Settings.Get(Of String)("VersionServerAuthServer", Instance) = SduSkinLogin.AuthServer, "Instance endpoint differs from login endpoint")
                Check(Settings.Get(Of String)("VersionServerAuthRegister", Instance) = SduSkinLogin.RegisterPage, "Instance registration endpoint differs")
                Check(Settings.Get(Of String)("CacheAuthAccess") = "sdu-token-fixture" AndAlso Settings.Get(Of String)("LoginAuthPass") = "sdu-password-fixture", "Existing SDUcraft login was unnecessarily erased")
                Settings.Writes = 0
                SduSkinLogin.EnsureConfiguration(Instance)
                Check(Settings.Writes = 0, "Repeated refresh rewrites settings and retriggers instance reloads")
                Pass("old instance mode " & OldMode & " migrates idempotently without losing SDUcraft credentials")
            Next

            For Each OldType In {0, 2, 5}
                Check(SduSkinLogin.ValidateLogin(OldType, "Auth", SduSkinLogin.LoginServer) <> "", "Disabled login type was accepted")
                Pass("disabled login type " & OldType & " is rejected before authentication")
            Next
            For Each InvalidServer In {Nothing, "", "http://skin.sducraft.cn/api/yggdrasil/authserver", "https://another-site.invalid/authserver", SduSkinLogin.LoginServer & ".evil.invalid", SduSkinLogin.LoginServer & "?redirect=1"}
                Check(SduSkinLogin.ValidateLogin(3, "Auth", InvalidServer) <> "", "Unexpected credential destination was accepted")
            Next
            Check(SduSkinLogin.ValidateLogin(3, "Nide", SduSkinLogin.LoginServer) <> "", "Unsupported token cache namespace was accepted")
            Check(SduSkinLogin.ValidateLogin(3, "Auth", SduSkinLogin.LoginServer & "/") = "", "Confirmed endpoint was rejected")
            Pass("only the pinned HTTPS endpoint and Auth token namespace are accepted")

            Settings.Reset()
            FrmLoginAuth = New PageLoginAuth()
            FrmLoginAuth.ComboName.Text = "fixture@example.invalid"
            FrmLoginAuth.TextPass.Password = "fixture-password"
            Dim Data = CType(McLoginInput(), McLoginServer)
            Check(Data.Type = McLoginType.Auth AndAlso Data.BaseUrl = SduSkinLogin.LoginServer AndAlso Data.UserName = "fixture@example.invalid", "Production login input used an old login flow")
            Check(McLoginAble(Data) = "", "Valid production skin login was rejected")
            Check(McLoginAble(CType(Nothing, McLoginData)) <> "", "Null input was accepted")
            Data.BaseUrl = "https://another-site.invalid/authserver"
            Check(McLoginAble(Data) <> "", "Production validation allowed another server")
            Data.BaseUrl = SduSkinLogin.LoginServer
            Data.Type = CType(5, McLoginType)
            Check(McLoginAble(Data) <> "", "Production validation allowed a legacy Microsoft type")
            Pass("production login routing and validation cannot fall back to removed login methods")

            Settings.Set("CacheAuthAccess", "sdu-token")
            Settings.Set("CacheAuthUsername", "cached@example.invalid")
            Settings.Set("CacheAuthPass", "cached-password")
            Settings.Set("CacheAuthName", "SkinPlayer")
            Data = CType(McLoginInput(), McLoginServer)
            Check(Data.BaseUrl = SduSkinLogin.LoginServer AndAlso Data.UserName = "cached@example.invalid", "Cached login bypassed skin-site routing")
            Check(McLoginName() = "SkinPlayer", "Displayed player name came from a removed account type")
            Pass("cached skin login and player name use only SDUcraft account data")

            Dim Previous As New McInstance()
            Settings.Set("VersionServerLogin", 4, Previous)
            Settings.Set("VersionServerAuthServer", "https://another-site.invalid/api/yggdrasil", Previous)
            McInstanceSelected = Previous
            Check(Settings.Get(Of String)("CacheAuthAccess") = "sdu-token", "Instance selection discarded the existing SDUcraft token")
            Check(Settings.Get(Of String)("CacheAuthServerServer") = SduSkinLogin.AuthServer, "Instance selection restored another authentication destination")
            Check(Settings.Get(Of String)("VersionServerAuthServer", Previous) = SduSkinLogin.AuthServer, "Selected instance was not migrated")
            Pass("production instance selection cannot restore another site or discard the SDUcraft session")
            Console.WriteLine("PASS: " & Passed & " skin-login policy/routing scenarios (no real credentials or network requests).")
        Catch ex As Exception
            Console.Error.WriteLine(ex)
            Environment.ExitCode = 1
        End Try
    End Sub
    Private Sub Check(Condition As Boolean, Message As String)
        If Not Condition Then Throw New Exception(Message)
    End Sub
    Private Sub Pass(Name As String)
        Passed += 1
        Console.WriteLine("PASS: " & Name)
    End Sub
End Module

Public Class McInstance
    Public ReadOnly Id As String = Guid.NewGuid().ToString("N")
    Public ReadOnly Property PathVersion As String
        Get
            Return "fixture/" & Id
        End Get
    End Property
    Public ReadOnly Property Name As String
        Get
            Return Id
        End Get
    End Property
End Class
Public Class Settings
    Private Shared Values As New Dictionary(Of String, Object)()
    Public Shared Writes As Integer
    Private Shared Function StorageKey(Key As String, Instance As McInstance) As String
        Return If(Instance Is Nothing, "global", Instance.Id) & ":" & Key
    End Function
    Public Shared Function [Get](Of T)(Key As String, Optional Instance As McInstance = Nothing) As T
        Dim Value As Object = Nothing
        If Not Values.TryGetValue(StorageKey(Key, Instance), Value) Then Value = If(GetType(T) Is GetType(String), CObj(""), CObj(0))
        If GetType(T).IsEnum Then Return CType([Enum].ToObject(GetType(T), Convert.ToInt32(Value)), T)
        Return CType(Convert.ChangeType(Value, GetType(T)), T)
    End Function
    Public Shared Sub [Set](Key As String, Value As Object, Optional Instance As McInstance = Nothing)
        Values(StorageKey(Key, Instance)) = Value
        Writes += 1
    End Sub
    Public Shared Sub Reset()
        Values.Clear()
        Writes = 0
    End Sub
End Class
Public Class TestText
    Public Text As String = ""
    Public Password As String = ""
End Class
Partial Public Class PageLoginAuth
    Public ComboName As New TestText()
    Public TextPass As New TestText()
    Public Function IsVaild() As String
        Return IsVaild(GetLoginData())
    End Function
End Class
Partial Public Class PageLoginAuthSkin
End Class
Public Module LoginTestHost
    Public FrmLoginAuth As New PageLoginAuth()
    Public McFolderSelected As String = "fixture/"
    Public Sub WriteIni(Path As String, Key As String, Value As String)
    End Sub
    <Extension>
    Public Function GetStableHashCode(Value As String) As Integer
        Return Value.GetHashCode()
    End Function
End Module
Public Class Logger
    Public Shared Sub Info(Message As String)
    End Sub
End Class
Public Class PageDownloadResourceDetail
    Public Shared CachedFolder As New Dictionary(Of String, String)()
End Class
