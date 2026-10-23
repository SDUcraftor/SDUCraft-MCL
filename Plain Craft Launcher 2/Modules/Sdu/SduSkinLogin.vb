''' <summary>本分支只允许 SDUcraft 皮肤站认证，统一首页、实例与启动参数使用的地址。</summary>
Public Module SduSkinLogin
    Public Const Website As String = "https://skin.sducraft.cn"
    Public Const AuthServer As String = Website & "/api/yggdrasil"
    Public Const LoginServer As String = AuthServer & "/authserver"
    Public Const RegisterPage As String = Website & "/auth/register"
    Public Const ForgotPasswordPage As String = Website & "/auth/forgot"
    Public Const ClosetPage As String = Website & "/user/closet"
    Public Const DisplayName As String = "SDUcraft 皮肤站"
    Private ReadOnly ConfigurationLock As New Object()

    Public Sub EnsureConfiguration(Optional Instance As McInstance = Nothing)
        SyncLock ConfigurationLock
            '其他认证站点保存的账号、密码和令牌不能自动发送给 SDUcraft。
            If Not String.Equals(Settings.Get(Of String)("CacheAuthServerServer").TrimEnd("/"c), AuthServer, StringComparison.OrdinalIgnoreCase) Then
                For Each Key In {"CacheAuthAccess", "CacheAuthClient", "CacheAuthUuid", "CacheAuthName", "CacheAuthUsername", "CacheAuthPass", "LoginAuthEmail", "LoginAuthPass"}
                    SetIfChanged(Key, "")
                Next
            End If
            SetIfChanged("CacheAuthServerServer", AuthServer)
            SetIfChanged("CacheAuthServerName", DisplayName)
            SetIfChanged("CacheAuthServerRegister", RegisterPage)
            SetIfChanged("LoginType", 3)
            SetIfChanged("LoginPageType", 4)
            If Instance Is Nothing Then Return
            SetIfChanged("VersionServerAuthServer", AuthServer, Instance)
            SetIfChanged("VersionServerAuthName", DisplayName, Instance)
            SetIfChanged("VersionServerAuthRegister", RegisterPage, Instance)
            SetIfChanged("VersionServerLogin", 4, Instance)
        End SyncLock
    End Sub

    Private Sub SetIfChanged(Key As String, Value As Object, Optional Instance As McInstance = Nothing)
        If TypeOf Value Is Integer Then
            If Settings.Get(Of Integer)(Key, Instance:=Instance) <> CInt(Value) Then Settings.Set(Key, Value, Instance:=Instance)
        ElseIf Settings.Get(Of String)(Key, Instance:=Instance) <> CStr(Value) Then
            Settings.Set(Key, Value, Instance:=Instance)
        End If
    End Sub

    Friend Function ValidateLogin(Type As Integer, Token As String, Server As String) As String
        If Type <> 3 OrElse Token <> "Auth" OrElse
           Not String.Equals(If(Server, "").TrimEnd("/"c), LoginServer, StringComparison.OrdinalIgnoreCase) Then
            Return "此启动器仅支持 SDUcraft 皮肤站登录，请使用 skin.sducraft.cn 的账号。"
        End If
        Return ""
    End Function
End Module
