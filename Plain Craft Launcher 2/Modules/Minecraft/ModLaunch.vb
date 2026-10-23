Public Module ModLaunch

#Region "开始"

    Public CurrentLaunchOptions As McLaunchOptions = Nothing
    Public Class McLaunchOptions
        ''' <summary>
        ''' 强制指定在启动后进入的服务器 IP。
        ''' 默认值：Nothing。使用版本设置的值。
        ''' </summary>
        Public ServerIp As String = Nothing
        ''' <summary>
        ''' 将启动脚本保存到该地址，然后取消启动。这同时会改变启动时的提示等。
        ''' 默认值：Nothing。不保存。
        ''' </summary>
        Public SaveBatch As String = Nothing
        ''' <summary>
        ''' 强行指定启动的 MC 版本。
        ''' 默认值：Nothing。使用 McInstanceCurrent。
        ''' </summary>
        Public Instance As McInstance = Nothing
        ''' <summary>
        ''' 额外的启动参数。
        ''' </summary>
        Public ExtraGameArgs As New List(Of String)
    End Class
    ''' <summary>
    ''' 尝试启动 Minecraft。必须在 UI 线程调用。
    ''' 返回是否实际开始了启动（如果没有，则一定弹出了错误提示）。
    ''' </summary>
    Public Function McLaunchStart(Optional Options As McLaunchOptions = Nothing) As Boolean
        CurrentLaunchOptions = If(Options, New McLaunchOptions)
        '预检查
        If Not RunInUi() Then Throw New Exception("McLaunchStart 必须在 UI 线程调用！")
        If McLaunchLoader.State = LoadState.Loading Then
            Hint("已有游戏正在启动中！", HintType.Red)
            Return False
        End If
        '强制切换需要启动的版本
        If CurrentLaunchOptions.Instance IsNot Nothing AndAlso McInstanceSelected <> CurrentLaunchOptions.Instance Then
            McLaunchLog("在启动前切换到版本 " & CurrentLaunchOptions.Instance.Name)
            '检查版本是否存在
            Try
                CurrentLaunchOptions.Instance.GetJsonPath()
            Catch
                Hint($"无法启动 {CurrentLaunchOptions.Instance.Name}：当前 Minecraft 文件夹中尚未安装该版本！", HintType.Red)
                Return False
            End Try
            '检查版本
            CurrentLaunchOptions.Instance.Load()
            If CurrentLaunchOptions.Instance.State = McInstance.McInstanceState.Error Then
                Hint($"无法启动 {CurrentLaunchOptions.Instance.Name}：{CurrentLaunchOptions.Instance.Info}", HintType.Red)
                Return False
            End If
            '切换版本
            McInstanceSelected = CurrentLaunchOptions.Instance
            FrmLaunchLeft.RefreshButtonsUI()
            FrmLaunchLeft.RefreshPage(False, False)
        End If
        If McInstanceSelected IsNot Nothing AndAlso McInstanceLock.IsLocked(McInstanceSelected) Then
            Hint($"版本 {McInstanceSelected.Name} 正在被修改，请稍后再启动！", HintType.Red)
            Return False
        End If
        FrmMain.AprilGiveup()
        '禁止进入版本选择页面（否则就可以在启动中切换 McInstanceSelected 了）
        FrmMain.PageStack = FrmMain.PageStack.Where(Function(p) p.Page <> FormMain.PageType.InstanceSelect).ToList
        '实际启动加载器
        McLaunchLoader.Start(Options, IsForceRestart:=True)
        Return True
    End Function

    ''' <summary>
    ''' 记录启动日志。
    ''' </summary>
    Public Sub McLaunchLog(Text As String, Optional Level As LogLevel = LogLevel.Info)
        Text = FilterUserName(FilterAccessToken(Text, "*"), "*")
        If ModeDebug Then
            RunInUi(
            Sub()
                FrmLaunchRight.LabLog.AppendText($"[{Date.Now:HH':'mm':'ss'.'fff}] {Text}{vbCrLf}")
            End Sub)
        End If
        Logger.Log(Text, Level)
    End Sub

    '启动状态切换
    Public McLaunchLoader As New LoaderTask(Of McLaunchOptions, Object)("Loader Launch", AddressOf McLaunchStart) With {.OnStateChanged = AddressOf McLaunchState}
    Public McLaunchLoaderReal As LoaderCombo(Of Object)
    Public McLaunchProcess As Process
    Public McLaunchWatcher As Watcher
    Private Sub McLaunchState(Loader As LoaderTask(Of McLaunchOptions, Object))
        Select Case McLaunchLoader.State
            Case LoadState.Finished, LoadState.Failed, LoadState.Waiting, LoadState.Canceled
                FrmLaunchLeft.PageChangeToLogin()
            Case LoadState.Loading
                '在预检测结束后再触发动画
                FrmLaunchRight.LabLog.Text = ""
        End Select
    End Sub
    ''' <summary>
    ''' 指定启动中断时的提示文本。若不为 Nothing 则会显示为绿色。
    ''' </summary>
    Private CanceledHint As String = Nothing

    '实际的启动方法
    Private Sub McLaunchStart(Loader As LoaderTask(Of McLaunchOptions, Object))
        '开始动画
        RunInUiWait(AddressOf FrmLaunchLeft.PageChangeToLaunching)
        '预检测（预检测的错误将直接抛出）
        Try
            McLaunchPrecheck()
            McLaunchLog("预检测已通过")
        Catch ex As Exception
            If Not ex.IsCanceled Then Hint(ex.Message, HintType.Red)
            Throw
        End Try
        Dim IsSavingBatch As Boolean = CurrentLaunchOptions?.SaveBatch IsNot Nothing

        '正式加载
        Try
            '构造主加载器
            Dim Loaders As New List(Of LoaderBase) From {
                New LoaderTask(Of Integer, Integer)("获取 Java", AddressOf McLaunchJava) With {.ProgressWeight = 4, .Block = False},
                McLoginLoader, '.ProgressWeight = 15, .Block = False
                New LoaderCombo(Of String)("补全文件", DlClientFix(McInstanceSelected, False, True)) With {.ProgressWeight = 15, .Show = False},
                New LoaderTask(Of String, Integer)("获取启动参数", AddressOf McLaunchArgumentMain) With {.ProgressWeight = 2},
                New LoaderTask(Of Integer, Integer)("解压文件", AddressOf McLaunchNatives) With {.ProgressWeight = 2},
                New LoaderTask(Of Integer, Integer)("预启动处理", AddressOf McLaunchPrerun) With {.ProgressWeight = 1},
                New LoaderTask(Of Integer, Integer)("执行自定义命令", AddressOf McLaunchCustom) With {.ProgressWeight = 1},
                New LoaderTask(Of Integer, Process)("启动进程", AddressOf McLaunchRun) With {.ProgressWeight = 2},
                New LoaderTask(Of Process, Integer)("等待游戏窗口出现", AddressOf McLaunchWait) With {.ProgressWeight = 1},
                New LoaderTask(Of Integer, Integer)("结束处理", AddressOf McLaunchEnd) With {.ProgressWeight = 1}
            }
            '内存优化
            Select Case Settings.Get(Of Integer)("VersionRamOptimize", Instance:=McInstanceSelected)
                Case 0 '全局
                    If Settings.Get(Of Boolean)("LaunchArgumentRam") Then '使用全局设置
                        CType(Loaders(2), LoaderCombo(Of String)).Block = False
                        Loaders.Insert(3, New LoaderTask(Of Integer, Integer)("内存优化", AddressOf McLaunchMemoryOptimize) With {.ProgressWeight = 30})
                    End If
                Case 1 '开启
                    CType(Loaders(2), LoaderCombo(Of String)).Block = False
                    Loaders.Insert(3, New LoaderTask(Of Integer, Integer)("内存优化", AddressOf McLaunchMemoryOptimize) With {.ProgressWeight = 30})
                Case 2 '关闭
            End Select
            Dim LaunchLoader As New LoaderCombo(Of Object)("Minecraft 启动", Loaders) With {.Show = False}
            If McLoginLoader.State = LoadState.Finished Then McLoginLoader.State = LoadState.Waiting '要求重启登录主加载器，它会自行决定是否启动副加载器
            '等待加载器执行并更新 UI
            McLaunchLoaderReal = LaunchLoader
            CanceledHint = Nothing
            LaunchLoader.Start()
            '任务栏进度条
            LoaderTaskbarAdd(LaunchLoader)
            Do While LaunchLoader.State = LoadState.Loading
                FrmLaunchLeft.Dispatcher.Invoke(AddressOf FrmLaunchLeft.LaunchingRefresh)
                Thread.Sleep(100)
            Loop
            FrmLaunchLeft.Dispatcher.Invoke(AddressOf FrmLaunchLeft.LaunchingRefresh)
            '成功与失败处理
            Select Case LaunchLoader.State
                Case LoadState.Finished
                    Hint(McInstanceSelected.Name & " 启动成功！", HintType.Green)
                    '上报
                    If Not IsSavingBatch Then
                        Telemetry("Minecraft 启动成功",
                              "Version", McInstanceSelected.VersionDisplayName,
                              "LoginType", Settings.Get(Of McLoginType)("LoginType").ToString)
                    End If
                Case LoadState.Canceled
                    If CanceledHint Is Nothing Then
                        Hint(If(IsSavingBatch, "已取消导出启动脚本！", "已取消启动！"), HintType.Blue)
                    Else
                        Hint(CanceledHint, HintType.Green)
                    End If
                Case LoadState.Failed
                    Throw LaunchLoader.Error
                Case Else
                    Throw New Exception("错误的状态改变：" & LaunchLoader.State.ToString())
            End Select
        Catch ex As Exception
            ex.ThrowIfCanceled()
            Dim CurrentEx = ex
NextInner:
            If CurrentEx.Message.StartsWithF("$") Then
                '若有以 $ 开头的错误信息，则以此为准显示提示
                MyMsgBox(CurrentEx.Message.TrimStart("$"), If(IsSavingBatch, "导出启动脚本失败", "启动失败"))
                Throw
            ElseIf CurrentEx.InnerException IsNot Nothing Then
                '检查下一级错误
                CurrentEx = CurrentEx.InnerException
                GoTo NextInner
            Else
                '没有特殊处理过的错误信息
                McLaunchLog("错误：" & ex.GetDisplay(True))
                Logger.Error(ex, If(IsSavingBatch, "导出启动脚本失败", "Minecraft 启动失败"), LogBehavior.Alert)
                '上报
                If Not IsSavingBatch Then
                    Telemetry("Minecraft 启动失败",
                              "Version", McInstanceSelected.VersionDisplayName,
                              "LoginType", Settings.Get(Of McLoginType)("LoginType").ToString,
                              "Exception", FilterUserName(FilterAccessToken(ex.GetDisplay(True), "*"), "*"))
                End If
                Throw
            End If
        End Try
    End Sub

#End Region

#Region "内存优化"

    Private Sub McLaunchMemoryOptimize(Loader As LoaderTask(Of Integer, Integer))
        McLaunchLog("内存优化开始")
        Dim Finished As Boolean = False
        RunInNewThread(
        Sub()
            PageOtherTest.MemoryOptimize(False)
            Finished = True
        End Sub, "Launch Memory Optimize")
        Do While Not Finished AndAlso Not Loader.IsCanceled
            If Loader.Progress < 0.7 Then
                Loader.Progress += 0.007 '10s
            Else
                Loader.Progress += (0.95 - Loader.Progress) * 0.02 '最快 += 0.005
            End If
            Thread.Sleep(100)
        Loop
    End Sub

#End Region

#Region "预检测"

    Private Sub McLaunchPrecheck()
        SduSkinLogin.EnsureConfiguration(McInstanceSelected)
        If Settings.Get(Of Boolean)("SystemDebugDelay") Then Thread.Sleep(RandomInteger(100, 2000))
        '检查路径
        If McInstanceSelected.PathIndie.Contains("!") OrElse McInstanceSelected.PathIndie.Contains(";") Then Throw New Exception("游戏路径中不可包含 ! 或 ;（" & McInstanceSelected.PathIndie & "）")
        If McInstanceSelected.PathVersion.Contains("!") OrElse McInstanceSelected.PathVersion.Contains(";") Then Throw New Exception("游戏路径中不可包含 ! 或 ;（" & McInstanceSelected.PathVersion & "）")
        '检查版本
        If McInstanceSelected Is Nothing Then Throw New Exception("未选择 Minecraft 版本！")
        McInstanceSelected.Load()
        If McInstanceSelected.State = McInstance.McInstanceState.Error Then Throw New Exception("Minecraft 存在问题：" & McInstanceSelected.Info)
        '检查输入信息
        Dim CheckResult As String = ""
        RunInUiWait(Sub() CheckResult = McLoginAble(McLoginInput()))
        If CheckResult <> "" Then Throw New ArgumentException(CheckResult)
    End Sub

#End Region

#Region "主登录模块"

    '旧枚举数值仅用于识别历史配置；本分支只保留 Auth 登录流程。
    Public Enum McLoginType
        Legacy = 0
        Nide = 2
        Auth = 3
        Ms = 5
    End Enum

    '各个登录方式的对应数据
    Public MustInherit Class McLoginData
        ''' <summary>
        ''' 登录方式。
        ''' </summary>
        Public Type As McLoginType
        Public Overrides Function Equals(obj As Object) As Boolean
            Return obj IsNot Nothing AndAlso obj.GetHashCode() = GetHashCode()
        End Function
    End Class
    Public Class McLoginServer
        Inherits McLoginData

        ''' <summary>
        ''' 登录用户名。
        ''' </summary>
        Public UserName As String
        ''' <summary>
        ''' 登录密码。
        ''' </summary>
        Public Password As String
        ''' <summary>
        ''' 登录服务器基础地址。
        ''' </summary>
        Public BaseUrl As String
        ''' <summary>
        ''' 登录所使用的标识符，目前只可能为 “Auth” 或 “Nide”，用于存储缓存等。
        ''' </summary>
        Public Token As String
        ''' <summary>
        ''' 登录方式的描述字符串。
        ''' </summary>
        Public Description As String
        ''' <summary>
        ''' 是否在本次登录中强制要求玩家重新选择角色，目前仅对 Authlib-Injector 生效。
        ''' </summary>
        Public ForceReselectProfile As Boolean = False

        Public Sub New(Type As McLoginType)
            Me.Type = Type
        End Sub
        Public Overrides Function GetHashCode() As Integer
            Return (UserName & Password & BaseUrl & Token & Type).GetStableHashCode() Mod Integer.MaxValue
        End Function

    End Class

    '登录返回结果
    Public Structure McLoginResult
        Public Name As String
        Public Uuid As String
        Public AccessToken As String
        Public Type As String
        Public ClientToken As String
        ''' <summary>
        ''' 进行微软登录时返回的 profile 信息。
        ''' </summary>
        Public ProfileJson As String
    End Structure

    ''' <summary>
    ''' 根据登录信息获取玩家的 MC 用户名。如果无法获取则返回 Nothing。
    ''' </summary>
    Public Function McLoginName() As String
        Dim Name = Settings.Get(Of String)("CacheAuthName")
        Return If(Name = "", Nothing, Name)
    End Function
    ''' <summary>
    ''' 当前是否可以进行登录。若不可以则会返回错误原因。
    ''' </summary>
    Public Function McLoginAble() As String
        SduSkinLogin.EnsureConfiguration(McInstanceSelected)
        If Settings.Get(Of String)("CacheAuthAccess") <> "" Then Return ""
        Return If(FrmLoginAuth Is Nothing, "请先登录 SDUcraft 皮肤站账号。", FrmLoginAuth.IsVaild())
    End Function
    ''' <summary>
    ''' 登录输入是否可以进行登录。若不可以则会返回错误原因。
    ''' </summary>
    Public Function McLoginAble(LoginData As McLoginData) As String
        Dim ServerData = TryCast(LoginData, McLoginServer)
        If ServerData Is Nothing Then Return "此启动器仅支持 SDUcraft 皮肤站登录。"
        Dim ErrorMessage = SduSkinLogin.ValidateLogin(CInt(ServerData.Type), ServerData.Token, ServerData.BaseUrl)
        If ErrorMessage <> "" Then Return ErrorMessage
        Return PageLoginAuth.IsVaild(ServerData)
    End Function

    '登录主模块加载器
    Public McLoginLoader As New LoaderTask(Of McLoginData, McLoginResult)("登录", AddressOf McLoginStart, AddressOf McLoginInput, ThreadPriority.BelowNormal) With
        {.ReloadTimeout = 1, .ProgressWeight = 15, .Block = False}
    Public Function McLoginInput() As McLoginData
        SduSkinLogin.EnsureConfiguration(McInstanceSelected)
        If Settings.Get(Of String)("CacheAuthAccess") = "" Then Return PageLoginAuth.GetLoginData()
        Return PageLoginAuthSkin.GetLoginData()
    End Function
    Private Sub McLoginStart(Data As LoaderTask(Of McLoginData, McLoginResult))
        McLaunchLog("登录加载已开始")
        '校验登录信息
        Dim CheckResult As String = McLoginAble(Data.Input)
        If Not CheckResult = "" Then Throw New ArgumentException(CheckResult)
        '获取对应加载器
        Dim Loader As LoaderBase = McLoginAuthLoader
        '尝试加载
        Loader.WaitForExit(Data.Input, McLoginLoader, Data.IsForceRestarting)
        Data.Output = CType(Loader, Object).Output
        RunInUi(Sub() FrmLaunchLeft.RefreshPage(True, False)) '刷新自动填充列表
        McLaunchLog("登录加载已结束")
    End Sub

#End Region
#Region "分方式登录模块"

    '各个登录方式的主对象与输入构造
    Public McLoginAuthLoader As New LoaderTask(Of McLoginServer, McLoginResult)("Loader Login Auth", AddressOf McLoginServerStart) With {.ReloadTimeout = 1000 * 60 * 10}

    '主加载函数，返回所有需要的登录信息

    Private Sub McLoginServerStart(Data As LoaderTask(Of McLoginServer, McLoginResult))
        Dim Input As McLoginServer = Data.Input
        Dim ErrorMessage = McLoginAble(Input)
        If ErrorMessage <> "" Then Throw New ArgumentException(ErrorMessage)
        Dim NeedRefresh As Boolean = False, WasRefreshed As Boolean = False
        McLaunchLog("登录方式：" & Input.Description)
        Data.Progress = 0.05
        '尝试登录
        If (Not Data.Input.ForceReselectProfile) AndAlso
            Settings.Get(Of String)("Cache" & Input.Token & "Username") = Data.Input.UserName AndAlso
            Settings.Get(Of String)("Cache" & Input.Token & "Pass") = Data.Input.Password AndAlso
            Settings.Get(Of String)("Cache" & Input.Token & "Access") <> "" AndAlso
            Settings.Get(Of String)("Cache" & Input.Token & "Client") <> "" AndAlso
            Settings.Get(Of String)("Cache" & Input.Token & "Uuid") <> "" AndAlso
            Settings.Get(Of String)("Cache" & Input.Token & "Name") <> "" Then
            '尝试验证登录
            Try
                If Data.IsCanceled Then Throw New OperationCanceledException
                McLoginRequestValidate(Data)
                GoTo LoginFinish
            Catch ex As Exception
                Logger.Warn(ex, "验证登录失败")
                If ex.IsBadNetwork Then Throw New Exception("$登录失败：无法连接到 SDUcraft 皮肤站。" & vbCrLf & "请确认已连接校园网并且皮肤站可访问，然后重试。")
            End Try
            Data.Progress = 0.25
            '尝试刷新登录
Refresh:
            Try
                If Data.IsCanceled Then Throw New OperationCanceledException
                McLoginRequestRefresh(Data, NeedRefresh)
                GoTo LoginFinish
            Catch ex As Exception
                McLaunchLog("刷新登录失败：" & ex.GetDisplay(True))
                If WasRefreshed Then Throw New Exception("二轮刷新登录失败", ex)
            End Try
            Data.Progress = If(NeedRefresh, 0.85, 0.45)
        End If
        '尝试普通登录
        Try
            If Data.IsCanceled Then Throw New OperationCanceledException
            NeedRefresh = McLoginRequestLogin(Data)
        Catch ex As Exception
            McLaunchLog("登录失败：" & ex.GetDisplay(True))
            Throw
        End Try
        If NeedRefresh Then
            McLaunchLog("重新进行刷新登录")
            WasRefreshed = True
            Data.Progress = 0.65
            GoTo Refresh
        End If
LoginFinish:
        Data.Progress = 0.95
        '保存启动记录
        Dim Dict As New Dictionary(Of String, String)
        Dim Emails As New List(Of String)
        Dim Passwords As New List(Of String)
        Try
            If Not Settings.Get(Of String)("Login" & Input.Token & "Email") = "" Then Emails.AddRange(Settings.Get(Of String)("Login" & Input.Token & "Email").Split("¨"))
            If Not Settings.Get(Of String)("Login" & Input.Token & "Pass") = "" Then Passwords.AddRange(Settings.Get(Of String)("Login" & Input.Token & "Pass").Split("¨"))
            For i = 0 To Emails.Count - 1
                Dict.Add(Emails(i), Passwords(i))
            Next
            Dict.Remove(Input.UserName)
            Emails = New List(Of String)(Dict.Keys)
            Emails.Insert(0, Input.UserName)
            Passwords = New List(Of String)(Dict.Values)
            Passwords.Insert(0, Input.Password)
            Settings.Set("Login" & Input.Token & "Email", Emails.Join("¨"c))
            Settings.Set("Login" & Input.Token & "Pass", Passwords.Join("¨"c))
        Catch ex As Exception
            Logger.Error(ex, "保存启动记录失败", LogBehavior.Toast)
            Settings.Set("Login" & Input.Token & "Email", "")
            Settings.Set("Login" & Input.Token & "Pass", "")
        End Try
    End Sub


    'Server 登录：三种验证方式的请求
    Private Sub McLoginRequestValidate(ByRef Data As LoaderTask(Of McLoginServer, McLoginResult))
        McLaunchLog("验证登录开始（Validate, " & Data.Input.Token & "）")
        '提前缓存信息，否则如果在登录请求过程中退出登录，设置项目会被清空，导致输出存在空值
        Dim AccessToken As String = Settings.Get(Of String)("Cache" & Data.Input.Token & "Access")
        Dim ClientToken As String = Settings.Get(Of String)("Cache" & Data.Input.Token & "Client")
        Dim Uuid As String = Settings.Get(Of String)("Cache" & Data.Input.Token & "Uuid")
        Dim Name As String = Settings.Get(Of String)("Cache" & Data.Input.Token & "Name")
        '发送登录请求
        Dim RequestData As New JObject(
            New JProperty("accessToken", AccessToken), New JProperty("clientToken", ClientToken), New JProperty("requestUser", True))
        NetRequestByClientRetry(
            Data.Input.BaseUrl & "/validate", HttpMethod.Post,
            Content:=RequestData.ToString(Newtonsoft.Json.Formatting.None),
            Headers:={{"Accept-Language", "zh-CN"}},
            ContentType:="application/json; charset=utf-8") '没有返回值的
        '将登录结果输出
        Data.Output.AccessToken = AccessToken
        Data.Output.ClientToken = ClientToken
        Data.Output.Uuid = Uuid
        Data.Output.Name = Name
        Data.Output.Type = Data.Input.Token
        '不更改缓存，直接结束
        McLaunchLog("验证登录成功（Validate, " & Data.Input.Token & "）")
    End Sub
    Private Sub McLoginRequestRefresh(ByRef Data As LoaderTask(Of McLoginServer, McLoginResult), RequestUser As Boolean)
        McLaunchLog("刷新登录开始（Refresh, " & Data.Input.Token & "）")
        Dim LoginJson As JObject = NetRequestByClientRetry(
               Data.Input.BaseUrl & "/refresh", HttpMethod.Post,
               Content:=New JObject(
                   New JProperty("selectedProfile", New JObject(
                       New JProperty("name", Settings.Get(Of String)($"Cache{Data.Input.Token}Name")),
                       New JProperty("id", Settings.Get(Of String)($"Cache{Data.Input.Token}Uuid"))
                   )),
                   New JProperty("accessToken", Settings.Get(Of String)($"Cache{Data.Input.Token}Access")),
                   New JProperty("requestUser", True)
               ).ToString(Newtonsoft.Json.Formatting.None),
               Headers:={{"Accept-Language", "zh-CN"}},
               ContentType:="application/json; charset=utf-8", RequireJson:=True).DeserializeJson()
        '将登录结果输出
        If LoginJson("selectedProfile") Is Nothing Then Throw New Exception("选择的角色 " & Settings.Get(Of String)("Cache" & Data.Input.Token & "Name") & " 无效！")
        Data.Output.AccessToken = LoginJson("accessToken").ToString
        Data.Output.ClientToken = LoginJson("clientToken").ToString
        Data.Output.Uuid = LoginJson("selectedProfile")("id").ToString
        Data.Output.Name = LoginJson("selectedProfile")("name").ToString
        Data.Output.Type = Data.Input.Token
        '保存缓存
        Settings.Set("Cache" & Data.Input.Token & "Access", Data.Output.AccessToken)
        Settings.Set("Cache" & Data.Input.Token & "Client", Data.Output.ClientToken)
        Settings.Set("Cache" & Data.Input.Token & "Uuid", Data.Output.Uuid)
        Settings.Set("Cache" & Data.Input.Token & "Name", Data.Output.Name)
        Settings.Set("Cache" & Data.Input.Token & "Username", Data.Input.UserName)
        Settings.Set("Cache" & Data.Input.Token & "Pass", Data.Input.Password)
        McLaunchLog("刷新登录成功（Refresh, " & Data.Input.Token & "）")
    End Sub
    Private Function McLoginRequestLogin(ByRef Data As LoaderTask(Of McLoginServer, McLoginResult)) As Boolean
        Try
            Dim NeedRefresh As Boolean = False
            McLaunchLog("登录开始（Login, " & Data.Input.Token & "）")
            Dim RequestData As New JObject(
                New JProperty("agent", New JObject(New JProperty("name", "Minecraft"), New JProperty("version", 1))),
                New JProperty("username", Data.Input.UserName),
                New JProperty("password", Data.Input.Password),
                New JProperty("requestUser", True))
            Dim LoginJson As JObject = NetRequestByClientRetry(
                Data.Input.BaseUrl & "/authenticate", HttpMethod.Post,
                Content:=RequestData.ToString(Newtonsoft.Json.Formatting.None),
                Headers:={{"Accept-Language", "zh-CN"}},
                ContentType:="application/json; charset=utf-8", RequireJson:=True).DeserializeJson()
            '检查登录结果
            If LoginJson("availableProfiles").Count = 0 Then
                If Data.Input.ForceReselectProfile Then Hint("你还没有创建角色，无法更换！", HintType.Red)
                Throw New Exception("$你还没有创建角色，请在创建角色后再试！")
            ElseIf Data.Input.ForceReselectProfile AndAlso LoginJson("availableProfiles").IsSingle Then
                Hint("你的账户中只有一个角色，无法更换！", HintType.Red)
            End If
            Dim SelectedName As String = Nothing
            Dim SelectedId As String = Nothing
            If (LoginJson("selectedProfile") Is Nothing OrElse Data.Input.ForceReselectProfile) AndAlso LoginJson("availableProfiles").Count > 1 Then
                '要求选择档案；优先从缓存读取
                NeedRefresh = True
                Dim CacheId As String = Settings.Get(Of String)("Cache" & Data.Input.Token & "Uuid")
                For Each Profile In LoginJson("availableProfiles")
                    If Profile("id").ToString = CacheId Then
                        SelectedName = Profile("name").ToString
                        SelectedId = Profile("id").ToString
                        McLaunchLog("根据缓存选择的角色：" & SelectedName)
                    End If
                Next
                '缓存无效，要求玩家选择
                If SelectedName Is Nothing Then
                    McLaunchLog("要求玩家选择角色")
                    RunInUiWait(
                    Sub()
                        Dim SelectionControl As New List(Of IMyRadio)
                        Dim SelectionJson As New List(Of JToken)
                        For Each Profile In LoginJson("availableProfiles")
                            SelectionControl.Add(New MyRadioBox With {.Text = Profile("name").ToString})
                            SelectionJson.Add(Profile)
                        Next
                        Dim SelectedIndex As Integer = MyMsgBoxSelect(SelectionControl, "选择使用的角色")
                        SelectedName = SelectionJson(SelectedIndex)("name").ToString
                        SelectedId = SelectionJson(SelectedIndex)("id").ToString
                    End Sub)
                    McLaunchLog("玩家选择的角色：" & SelectedName)
                End If
            Else
                SelectedName = LoginJson("selectedProfile")("name").ToString
                SelectedId = LoginJson("selectedProfile")("id").ToString
            End If
            '将登录结果输出
            Data.Output.AccessToken = LoginJson("accessToken").ToString
            Data.Output.ClientToken = LoginJson("clientToken").ToString
            Data.Output.Name = SelectedName
            Data.Output.Uuid = SelectedId
            Data.Output.Type = Data.Input.Token
            '保存缓存
            Settings.Set("Cache" & Data.Input.Token & "Access", Data.Output.AccessToken)
            Settings.Set("Cache" & Data.Input.Token & "Client", Data.Output.ClientToken)
            Settings.Set("Cache" & Data.Input.Token & "Uuid", Data.Output.Uuid)
            Settings.Set("Cache" & Data.Input.Token & "Name", Data.Output.Name)
            Settings.Set("Cache" & Data.Input.Token & "Username", Data.Input.UserName)
            Settings.Set("Cache" & Data.Input.Token & "Pass", Data.Input.Password)
            McLaunchLog("登录成功（Login, " & Data.Input.Token & "）")
            Return NeedRefresh
        Catch ex As Exception
            Dim AllMessage As String = ex.GetDisplay(False)
            Logger.Info(ex, "登录失败原始错误信息")
            '读取服务器返回的错误
            If TypeOf ex Is HttpRequestCodeException Then
                Dim ErrorMessage As String = Nothing
                Try
                    Dim Response = DirectCast(ex, HttpRequestCodeException).Response
                    If Response.ContainsIgnoreCase("errorMessage") Then ErrorMessage = Response.DeserializeJson()("errorMessage").ToString()
                Catch
                End Try
                If Not String.IsNullOrWhiteSpace(ErrorMessage) Then
                    If ErrorMessage.Contains("密码错误") OrElse ErrorMessage.ContainsIgnoreCase("Incorrect username or password") Then
                        '密码错误，退出登录 (#5090)
                        McLaunchLog("密码错误，退出登录")
                        Select Case Data.Input.Type
                            Case McLoginType.Auth
                                RunInUi(AddressOf PageLoginAuthSkin.ExitLogin)

                        End Select
                    End If
                    Throw New Exception("$登录失败：" & ErrorMessage)
                End If
            End If
            '通用关键字检测
            If AllMessage.Contains("(403)") Then
                Select Case Data.Input.Type
                    Case McLoginType.Auth
                        Throw New Exception("$登录失败，以下为可能的原因：" & vbCrLf &
                                            " - 输入的账号或密码错误。" & vbCrLf &
                                            " - 登录尝试过于频繁，导致被暂时屏蔽。请不要操作，等待 10 分钟后再试。" & vbCrLf &
                                            " - 只注册了账号，但没有在皮肤站新建角色。")
                End Select
            ElseIf ex.IsBadNetwork() Then
                Throw New Exception("$登录失败：无法连接到 SDUcraft 皮肤站。" & vbCrLf & "请确认已连接校园网并且皮肤站可访问，然后重试。")
            ElseIf ex.Message.StartsWithF("$") Then
                Throw
            Else
                Throw New Exception("登录失败：" & ex.Message, ex)
            End If
            Return False
        End Try
    End Function

#End Region

#Region "Java 处理"

    Public McLaunchJavaSelected As Java = Nothing
    Private Sub McLaunchJava(Task As LoaderTask(Of Integer, Integer))
        McLaunchJavaSelected = SelectOrDownloadJava(McInstanceSelected, True, Task.CreateCancellationToken, Task.CreateSyncProgressProvider)
        If Task.IsCanceled Then Return
        If McLaunchJavaSelected Is Nothing Then Throw New OperationCanceledException 'SelectOrDownloadJava 已经会给予适当的提示
        If ModeDebug Then
            McLaunchLog($"预期 Java 版本范围：{GetJavaRequirement(McInstanceSelected).Range}")
            McLaunchLog($"Java 版本设置类别：{Settings.Get(Of Integer)("VersionArgumentJavaV2", McInstanceSelected)}")
        End If
        McLaunchLog("选择的 Java：" & McLaunchJavaSelected.ToString)
    End Sub

#End Region

#Region "启动参数"
    Private McLaunchArgument As String
    Private McLaunchEncoding As Encoding

    '主方法
    Private Sub McLaunchArgumentMain(Loader As LoaderTask(Of String, Integer))
        '获取基础参数
        Dim Arguments As New List(Of String)
        McLaunchLog("参数获取开始")
        Arguments.AddRange(McLaunchArgumentsJVM()) 'JVM 参数
        Arguments.Add(McInstanceSelected.JsonObject("mainClass")) 'mainClass
        Arguments.AddRange(McLaunchArgumentsGame()) '游戏参数
        '替换
        McLaunchLog("=================== 启动参数开始 ===================")
        Dim Replacements = McLaunchArgumentsReplace(Arguments).ToList
        Dim Argument As String = ""
        For Each Arg In Arguments
            '应用替换标记
            For Each Replacement In Replacements
                Arg = Arg.Replace(Replacement.Key, Replacement.Value)
            Next
            '为带特殊字符的参数添加双引号
            If Arg.IndexOfAny({"&"c, "|"c, "<"c, ">"c, "^"c, " "c}) >= 0 AndAlso Not Arg.StartsWithF("""") AndAlso Not Arg.EndsWithF("""") Then
                Arg = $"""{Arg.Replace("""", "\""")}""" '转义在原本参数中间的双引号
            End If
            '输出
            McLaunchLog(Arg)
            Argument &= Arg & " "
        Next
        McLaunchLog("=================== 启动参数结束 ===================")
        '输出
        McLaunchArgument = Argument.TrimEnd
    End Sub

    'JVM 部分
    Private Function McLaunchArgumentsJVM() As List(Of String)
        Dim Args As New List(Of String)

        'Minecraft 参数
        If McInstanceSelected.JsonObject("arguments") IsNot Nothing AndAlso McInstanceSelected.JsonObject("arguments")("jvm") IsNot Nothing Then
            '新版 MC：从版本 JSON 获取参数
            Dim CurrentInstance As McInstance = McInstanceSelected
NextInstance:
            McLaunchLog($"新版 JVM 参数：从版本 JSON 获取（{CurrentInstance.Name}）")
            If CurrentInstance.JsonObject("arguments") IsNot Nothing AndAlso CurrentInstance.JsonObject("arguments")("jvm") IsNot Nothing Then
                For Each SubJson As JToken In CurrentInstance.JsonObject("arguments")("jvm")
                    Select Case SubJson.Type
                        Case JTokenType.String
                            Args.Add(SubJson.ToString.Trim)
                        Case JTokenType.Object
                            Dim Argument As JObject = CType(SubJson, JObject)
                            If Argument.ContainsKey("rules") AndAlso Not McJsonRuleCheck(Argument("rules")) Then Continue For '不满足准则
                            If Not Argument.ContainsKey("value") Then Continue For '没有 value 字段
                            If Argument("value").Type = JTokenType.String Then
                                Args.Add(Argument("value").ToString.Trim)
                            Else
                                For Each value As JToken In Argument("value")
                                    Args.Add(value.ToString.Trim)
                                Next
                            End If
                    End Select
                Next
            End If
            If CurrentInstance.InheritName <> "" Then
                CurrentInstance = New McInstance(CurrentInstance.InheritName)
                GoTo NextInstance
            End If
            '1.17.1 中有个 "-Dos.name=Windows 10"，不立即加双引号会导致 10 被隔开
            Args = Args.Select(Function(a) If(a.Contains(" ") AndAlso Not a.StartsWithF("""") AndAlso Not a.EndsWithF(""""), $"""{a.Replace("""", "\""")}""", a)).ToList
        Else
            '旧版 MC：直接添加参数
            McLaunchLog($"旧版 JVM 参数")
            Args.Add("-XX:HeapDumpPath=MojangTricksIntelDriversForPerformance_javaw.exe_minecraft.exe.heapdump")
            Args.Add("-Djava.library.path=${natives_directory}")
            Args.Add("-cp") : Args.Add("${classpath}")
        End If

        '设置里自定义的参数
        Dim CustomArg As String = Settings.Get(Of String)("VersionAdvanceJvm", Instance:=McInstanceSelected)
        If CustomArg = "" Then CustomArg = Settings.Get(Of String)("LaunchAdvanceJvm")
        If CustomArg <> "" Then CustomArg = ArgumentReplace(CustomArg)

        '重新分割字符串并去重
        Args = DeduplicateJavaArguments(SplitJavaArguments(Args.Join(" "c).Replace("McEmu= ", "McEmu=") & " " & CustomArg).ToList, True)

        'Authlib-Injector
        If McLoginLoader.Output.Type = "Auth" Then
            If McLaunchJavaSelected.Version.Major >= 6 Then Args.Add("-Djavax.net.ssl.trustStoreType=WINDOWS-ROOT") '信任系统根证书（5252#）
            Dim Server As String = SduSkinLogin.AuthServer
            Try
                McLaunchLog($"开始 Authlib Injector Prefetch")
                Dim Response As String = NetRequestByClientRetry(Server, Encoding:=Encoding.UTF8)
                Args.Add("-javaagent:""${pure_directory}\authlib-injector.jar""=""" & Server & """")
                Args.Add("-Dauthlibinjector.side=client")
                Args.Add("-Dauthlibinjector.yggdrasil.prefetched=" & Convert.ToBase64String(Encoding.UTF8.GetBytes(Response)))
            Catch ex As Exception
                Throw New Exception("无法连接到第三方登录服务器（" & If(Server, Nothing) & "）", ex)
            End Try
        End If

        'JLW
        '非 GBK 编码下命令行参数乱码的问题（JDK-8272352）
        Dim UseJLW As Boolean =
            Not Settings.Get(Of Boolean)("LaunchAdvanceDisableJLW") AndAlso Not Settings.Get(Of Boolean)("VersionAdvanceDisableJLW", McInstanceSelected) AndAlso
            Not IsGBKEncoding '即使游戏文件夹名称不含非 ASCII 字符，但玩家名等也可能出现中文，因此依然需要 JLW（#8739）
        If UseJLW AndAlso CustomArg.Contains("-javaagent") Then
            UseJLW = False
            McLaunchLog("由于使用了自定义 javaagent，已禁用 JLW，这可能导致游戏崩溃！", LogLevel.Warn)
        End If
        If UseJLW Then
            If McLaunchJavaSelected.Version.Major >= 9 Then
                Args.Add("--add-exports") : Args.Add("cpw.mods.bootstraplauncher/cpw.mods.bootstraplauncher=ALL-UNNAMED")
            End If
            Args.Add("-Doolloo.jlw.tmpdir=${pure_directory}") '这里需要不以 \ 结尾
        End If
        McLaunchLog("使用 JLW：" & UseJLW)

        'LUA
        Dim UseLUA As Boolean =
            Not Settings.Get(Of Boolean)("LaunchAdvanceDisableLUA") AndAlso Not Settings.Get(Of Boolean)("VersionAdvanceDisableLUA", McInstanceSelected) AndAlso
            McLibListGet(McInstanceSelected, False).Select(Function(e) e.OriginalName).Contains("org.lwjgl:lwjgl:3.4.1")
        If UseLUA Then Args.Add($"-javaagent:""{LaunchUtils.ExtractPatch("Launch\LwjglUnsafeAgent.jar", PathPure.Value)}""")
        McLaunchLog("使用 LUA：" & UseLUA)

#Region "内存管理"

        McLaunchLog("当前剩余内存：" & Math.Round(My.Computer.Info.AvailablePhysicalMemory / 1024 / 1024 / 1024 * 10) / 10 & "G")
        Dim TargetRam As Integer = Math.Floor(PageInstanceSetup.GetRam(McInstanceSelected) * 1024)
        Args.Add("-Xmx" & TargetRam & "m")

        '获取设置的管理方式
        '0：在 Java 21+ 使用分代 ZGC，20~15 使用非分代的 ZGC，14- 使用 G1GC
        '1：在 Java 21+ 使用分代 ZGC，20- 使用 G1GC
        '2：仅 G1GC
        '3：不指定，玩家自定义
        Dim SetupType As Integer = Settings.Get(Of Integer)("LaunchAdvanceGC")
        If McInstanceSelected IsNot Nothing AndAlso Settings.Get(Of Integer)("VersionAdvanceGC", McInstanceSelected) > 0 Then SetupType = Settings.Get(Of Integer)("VersionAdvanceGC", McInstanceSelected) - 1 '去掉默认
        If SetupType <> 3 Then
            '确定是使用 G1GC 还是 ZGC
            Dim UseG1GC As Boolean = False
            If (SetupType = 0 AndAlso McLaunchJavaSelected.Version.Major < 15) OrElse
               (SetupType = 1 AndAlso McLaunchJavaSelected.Version.Major < 21) OrElse
               (SetupType = 2 OrElse SetupType = 4) Then UseG1GC = True '检查 Java 版本，ZGC 在 Java 15+ 可用，分代 ZGC 在 Java 21+ 可用
            If Environment.OSVersion.Version.Major < 10 OrElse Environment.OSVersion.Version.Build < 17763 Then UseG1GC = True '检查系统兼容性，ZGC 需要 Windows 10 1809+
            McLaunchLog($"GC 设置：{SetupType}，选取 {If(UseG1GC, "G1GC", "ZGC")}，Java 版本：{McLaunchJavaSelected.Version.Major}")
            '移除已有的 GC 参数
            Args = Args.Where(Function(a) Not a.RegexCheck(
                "( )*-XX:[+-]?(Use\w+GC|ZGenerational|UseCompactObjectHeaders|G1\w+Percent|G1\w+Size|(Max|Min)(GCPauseMillis|HeapFreeRatio))")).ToList
            '添加 GC 参数
            Args.Add("-XX:+UnlockExperimentalVMOptions")
            If McLaunchJavaSelected.Version.Major >= 24 Then Args.Add("-XX:+UseCompactObjectHeaders")
            If UseG1GC AndAlso SetupType = 4 Then
                '优化的 G1GC
                Args.Add("-XX:+UseG1GC")
                Args.Add("-XX:G1NewSizePercent=20")
                Args.Add("-XX:G1ReservePercent=20")
                Args.Add("-XX:G1HeapRegionSize=32M")
                Args.Add("-XX:MaxGCPauseMillis=50")
                If Not ModeDebug Then Args.Add("-XX:+PerfDisableSharedMem")
                If McLaunchJavaSelected.Version.Major <= 7 Then Args.Add("-XX:MaxPermSize=512m") '#8286
                If McLaunchJavaSelected.Version.Major = 8 Then Args.Add("-XX:+ParallelRefProcEnabled")
                If McLaunchJavaSelected.Version.Major >= 12 Then Args.Add("-XX:MinHeapFreeRatio=25") '需要比 G1NewSizePercent 大一点
                If McLaunchJavaSelected.Version.Major >= 12 Then Args.Add("-XX:MaxHeapFreeRatio=40") '需要比 MinHeapFreeRatio 大一些
            ElseIf UseG1GC Then
                'Mojang G1GC
                Args.Add("-XX:+UseG1GC")
                Args.Add("-XX:G1NewSizePercent=20")
                Args.Add("-XX:G1ReservePercent=20")
                Args.Add("-XX:G1HeapRegionSize=32M")
                Args.Add("-XX:MaxGCPauseMillis=50")
                If McLaunchJavaSelected.Version.Major <= 7 Then Args.Add("-XX:MaxPermSize=512m") '#8286
            Else
                'ZGC
                Args.Add("-XX:+UseZGC")
                If McLaunchJavaSelected.Version.Major = 21 OrElse McLaunchJavaSelected.Version.Major = 22 Then Args.Add("-XX:+ZGenerational") 'Java 23 起默认启用分代 ZGC，不需要再添加参数
            End If
        End If

        Args.Remove("-XX:MaxDirectMemorySize=256M") '#3511 的清理

#End Region

        'Log4j 漏洞防御参数
        Args.Add("-Dlog4j2.formatMsgNoLookups=true")

        '编码（#4700、#5892、#5909、#6934、#8950、#9241）
        Dim UseCompatEncoding = McLaunchJavaSelected.Version.Major >= 18
        Dim EncodingName = If(UseCompatEncoding,
                              McLaunchJavaSelected.NativeEncodingName, 'Java 18+ 会添加 COMPAT，因此 Java 会回退到 native.encoding
                              McLaunchJavaSelected.FileEncodingName) 'Java 17- 直接使用 file.encoding
        Try
            McLaunchEncoding = Encoding.GetEncoding(If(EncodingName.Equals("GBK", StringComparison.OrdinalIgnoreCase), "GB18030", EncodingName))
        Catch ex As Exception
            Dim FallbackEncoding = Encoding.GetEncoding(Globalization.CultureInfo.CurrentCulture.TextInfo.ANSICodePage) '老版本 Java 的编码选择行为
            McLaunchLog($"无法识别 Java 编码 {If(EncodingName, "Nothing")}，已回退为 {FallbackEncoding.WebName}", LogLevel.Warn)
            McLaunchEncoding = FallbackEncoding
        End Try
        McLaunchLog($"使用编码：{McLaunchEncoding.WebName}")
        Dim LaunchEncodingName = If(McLaunchEncoding.CodePage = 54936, "GBK", McLaunchEncoding.WebName)
        Dim StdoutArgName = If(McLaunchJavaSelected.Version.Major < 19, "sun.stdout.encoding", "stdout.encoding") '老版本 Java 应该设置 sun.std
        Dim StderrArgName = If(McLaunchJavaSelected.Version.Major < 19, "sun.stderr.encoding", "stderr.encoding")
        If Not Args.Any(Function(a) a.StartsWithF($"-D{StdoutArgName}=")) Then Args.Add($"-D{StdoutArgName}={LaunchEncodingName}")
        If Not Args.Any(Function(a) a.StartsWithF($"-D{StderrArgName}=")) Then Args.Add($"-D{StderrArgName}={LaunchEncodingName}")
        If UseCompatEncoding AndAlso Not Args.Any(Function(a) a.StartsWithF("-Dfile.encoding=")) Then Args.Add("-Dfile.encoding=COMPAT") '防止老版本 MC 与部分 Mod 不兼容 UTF-8

        '为 JLW 添加 -jar，它必须放在最后
        If UseJLW Then
            Args.Add("-jar") : Args.Add(LaunchUtils.ExtractPatch("Launch\JavaWrapper.jar", PathPure.Value))
        End If

        '再次去重并输出
        Return DeduplicateJavaArguments(Args, True)
    End Function
    '游戏参数部分
    Private Function McLaunchArgumentsGame() As List(Of String)
        Dim Arg As String = ""

        'Minecraft 参数
        '旧版 MC：直接添加参数
        If McInstanceSelected.JsonObject("minecraftArguments")?.ToString.Any Then
            Dim BasicString As String = McInstanceSelected.JsonObject("minecraftArguments").ToString
            McLaunchLog($"旧版游戏参数：" & BasicString)
            BasicString += " --height ${resolution_height} --width ${resolution_width}" '总是添加宽高选项，之后去重的时候会覆盖 MC 自带的
            Arg &= " " & BasicString
        End If
        '新版 MC：从版本 JSON 获取参数
        If McInstanceSelected.JsonObject("arguments") IsNot Nothing AndAlso McInstanceSelected.JsonObject("arguments")("game") IsNot Nothing Then
            Dim CurrentInstance As McInstance = McInstanceSelected
            Dim AppendArg = Sub(a As String) Arg &= " " & If(a.Contains(" ") AndAlso Not a.StartsWithF("""") AndAlso Not a.EndsWithF(""""), $"""{a.Replace("""", "\""")}""", a)
NextInstance:
            McLaunchLog($"新版游戏参数：从版本 JSON 获取（{CurrentInstance.Name}）")
            If CurrentInstance.JsonObject("arguments") IsNot Nothing AndAlso CurrentInstance.JsonObject("arguments")("game") IsNot Nothing Then
                For Each SubJson As JToken In CurrentInstance.JsonObject("arguments")("game")
                    Select Case SubJson.Type
                        Case JTokenType.String
                            AppendArg(SubJson.ToString)
                        Case JTokenType.Object
                            Dim Argument As JObject = CType(SubJson, JObject)
                            If Argument.ContainsKey("rules") AndAlso Not McJsonRuleCheck(Argument("rules")) Then Continue For '不满足准则
                            If Not Argument.ContainsKey("value") Then Continue For '没有 value 字段
                            If Argument("value").Type = JTokenType.String Then
                                AppendArg(Argument("value").ToString)
                            Else
                                For Each value As JToken In Argument("value")
                                    AppendArg(value.ToString)
                                Next
                            End If
                    End Select
                Next
            End If
            If CurrentInstance.InheritName <> "" Then
                CurrentInstance = New McInstance(CurrentInstance.InheritName)
                GoTo NextInstance
            End If
        End If

        '设置里自定义的参数
        Dim CustomArg As String = Settings.Get(Of String)("VersionAdvanceGame", Instance:=McInstanceSelected)
        If CustomArg = "" Then CustomArg = Settings.Get(Of String)("LaunchAdvanceGame")
        If CustomArg <> "" Then CustomArg = ArgumentReplace(CustomArg)
        Arg &= " " & CustomArg

        '把 OptiFineForgeTweaker 放在参数的末尾
        If (McInstanceSelected.Version.ModLoaders.HasFlagF(ModLoaders.Forge) OrElse McInstanceSelected.Version.ModLoaders.HasFlagF(ModLoaders.LiteLoader)) AndAlso
                McInstanceSelected.Version.HasOptiFine AndAlso Not McInstanceSelected.Version.OptiFineAsMod Then
            If Arg.Contains("--tweakClass optifine.OptiFineForgeTweaker") Then
                McLaunchLog("发现正确的 OptiFineForge TweakClass，目前的游戏参数：" & Arg)
                Arg = Arg.Replace(" --tweakClass optifine.OptiFineForgeTweaker", "") & " --tweakClass optifine.OptiFineForgeTweaker"
            End If
            If Arg.Contains("--tweakClass optifine.OptiFineTweaker") Then
                McLaunchLog("发现错误的 OptiFineForge TweakClass，目前的游戏参数：" & Arg)
                Arg = Arg.Replace(" --tweakClass optifine.OptiFineTweaker", "") & " --tweakClass optifine.OptiFineForgeTweaker"
                Try
                    FileUtils.Write(
                        McInstanceSelected.GetJsonPath(),
                        FileUtils.ReadAsString(McInstanceSelected.GetJsonPath()).Replace("optifine.OptiFineTweaker", "optifine.OptiFineForgeTweaker"))
                Catch ex As Exception
                    Logger.Warn(ex, "替换 OptiFineForge TweakClass 失败")
                End Try
            End If
        End If

        '重新分割字符串并去重
        Dim Args As List(Of String) = DeduplicateJavaArguments(SplitJavaArguments(Arg).ToList, False)

        '额外传入的参数
        Args.AddRange(CurrentLaunchOptions.ExtraGameArgs)

        '全屏
        If Settings.Get(Of Integer)("LaunchArgumentWindowType") = 0 Then Args.Add("--fullscreen")

        '自动进入服务器
        Dim Server As String = If(String.IsNullOrEmpty(CurrentLaunchOptions.ServerIp), Settings.Get(Of String)("VersionServerEnter", McInstanceSelected), CurrentLaunchOptions.ServerIp)
        If Server.Any Then
            If McInstanceSelected.ReleaseTime > New Date(2023, 4, 4) Then
                'QuickPlay
                Args.Add($"--quickPlayMultiplayer") : Args.Add(Server)
            Else
                '老版本
                If Server.Contains(":") Then
                    '包含端口号
                    Args.Add("--server") : Args.Add(Server.Split(":")(0))
                    Args.Add("--port") : Args.Add(Server.Split(":")(1))
                Else
                    '不包含端口号
                    Args.Add("--server") : Args.Add(Server)
                    Args.Add("--port") : Args.Add("25565")
                End If
                If McInstanceSelected.Version.HasOptiFine Then Hint("OptiFine 与自动进入服务器可能不兼容，有概率导致材质丢失甚至游戏崩溃！", HintType.Red)
            End If
        End If

        '再次去重并输出
        Return DeduplicateJavaArguments(Args, False)
    End Function
    '获取替换标记
    Private Iterator Function McLaunchArgumentsReplace(Arguments As List(Of String)) As IEnumerable(Of (Key As String, Value As String))
        '基础参数
        Yield ("${classpath_separator}", ";")
        Yield ("${natives_directory}", PathUtils.RemoveSlashSuffix(GetNativesFolder()))
        Yield ("${library_directory}", PathUtils.ToShortPath(McFolderSelected & "libraries"))
        Yield ("${libraries_directory}", PathUtils.ToShortPath(McFolderSelected & "libraries"))
        Yield ("${pure_directory}", PathUtils.RemoveSlashSuffix(PathPure.Value)) '由 PCL 添加，这会允许在分割参数并去重后再替换路径，防止路径中的特殊字符影响参数分割和去重
        Yield ("${launcher_name}", "PCL")
        Yield ("${launcher_version}", VersionCode)
        Yield ("${version_name}", McInstanceSelected.Name)
        Yield ("${game_directory}", PathUtils.ToShortPath(Left(McInstanceSelected.PathIndie, McInstanceSelected.PathIndie.Count - 1)))
        Yield ("${assets_root}", PathUtils.ToShortPath(McFolderSelected & "assets"))
        Yield ("${user_properties}", "{}")
        Yield ("${auth_player_name}", McLoginLoader.Output.Name)
        Yield ("${auth_uuid}", McLoginLoader.Output.Uuid)
        Yield ("${auth_access_token}", McLoginLoader.Output.AccessToken)
        Yield ("${access_token}", McLoginLoader.Output.AccessToken)
        Yield ("${auth_session}", McLoginLoader.Output.AccessToken)
        Yield ("${user_type}", "msa") '#1221
        Yield ("${primary_jar}", PathUtils.ToShortPath(McInstanceSelected.PathVersion & McInstanceSelected.Name & ".jar")) '#6942
        Yield ("${game_assets}", PathUtils.ToShortPath(McFolderSelected & "assets\virtual\legacy")) '1.5.2 的 pre-1.6 资源索引应与 legacy 合并
        Yield ("${assets_index_name}", McAssetsGetIndexName(McInstanceSelected))

        '自定义信息
        Dim ArgumentInfo As String
        ArgumentInfo = Settings.Get(Of String)("VersionArgumentInfo", Instance:=McInstanceSelected)
        If ArgumentInfo = "" Then ArgumentInfo = Settings.Get(Of String)("LaunchArgumentInfo")
        If ArgumentInfo = "" Then '若自定义信息为空，则必须从参数中去掉该部分，不然 MC 会显示 ${version_type} 或者一个空字符串
            If Arguments.Contains("--versionType") Then
                Dim Index As Integer = Arguments.IndexOf("--versionType")
                Arguments.RemoveAt(Index)
                If Index < Arguments.Count Then Arguments.RemoveAt(Index)
            End If
            Yield ("${version_type}", """""")
        Else
            Yield ("${version_type}", ArgumentReplace(ArgumentInfo))
        End If

        '窗口尺寸
        Dim TargetSize As Size
        Select Case Settings.Get(Of Integer)("LaunchArgumentWindowType")
            Case 2 '与启动器尺寸一致
                Dim Result As Size
                RunInUiWait(Sub() Result = New Size(GetPixelSize(FrmMain.PanForm.ActualWidth), GetPixelSize(FrmMain.PanForm.ActualHeight)))
                TargetSize = Result
                TargetSize.Height -= 29.5 * DPI / 96 '标题栏高度
            Case 3 '自定义
                TargetSize = New Size(Math.Max(100, Settings.Get(Of Integer)("LaunchArgumentWindowWidth")), Math.Max(100, Settings.Get(Of Integer)("LaunchArgumentWindowHeight")))
            Case Else
                TargetSize = New Size(854, 480)
        End Select
        If McInstanceSelected.Version.Vanilla.Major <= 12 AndAlso McLaunchJavaSelected.Version >= New Version(8, 0, 200) AndAlso McLaunchJavaSelected.Version <= New Version(8, 0, 321) AndAlso
                Not McInstanceSelected.Version.HasOptiFine AndAlso Not McInstanceSelected.Version.ModLoaders.HasFlagF(ModLoaders.Forge) Then '修复 1.12.2- 下 JRE 8u200~321 下窗口大小为设置大小的 DPI% 倍（#3463）
            McLaunchLog($"已应用窗口大小过大修复（Java 版本：{McLaunchJavaSelected.Version}）")
            TargetSize.Width /= DPI / 96
            TargetSize.Height /= DPI / 96
        End If
        Yield ("${resolution_width}", Math.Round(TargetSize.Width))
        Yield ("${resolution_height}", Math.Round(TargetSize.Height))

        '支持库
        Dim Cps As New List(Of String)
        Dim OptiFineCp As String = Nothing
        For Each Library As McLibEntry In McLibListGet(McInstanceSelected, True)
            If Library.IsNatives Then Continue For
            If Library.Name IsNot Nothing AndAlso Library.Name = "optifine:OptiFine" Then
                OptiFineCp = Library.LocalPath
            Else
                Cps.Add(Library.LocalPath)
            End If
        Next
        If OptiFineCp IsNot Nothing Then Cps.Insert(Cps.Count - 2, OptiFineCp) 'OptiFine 的总是需要放到倒数第二位
        Yield ("${classpath}", Cps.Select(Function(c) PathUtils.ToShortPath(c)).Join(";"c))
    End Function

    ''' <summary>
    ''' 分割 Java 参数字符串。
    ''' </summary>
    Private Iterator Function SplitJavaArguments(Arg As String) As IEnumerable(Of String)
        Arg = Arg.ReplaceLineEndings(" ", mergeMultiple:=True).Trim '预处理换行符
        If String.IsNullOrWhiteSpace(Arg) Then Return
        Dim Current As New StringBuilder
        Dim InQuotes As Boolean = False
        Dim i As Integer = 0
        While i < Arg.Length
            Dim c As Char = Arg(i)
            If c = "\"c AndAlso i + 1 < Arg.Length AndAlso Arg(i + 1) = """"c Then '检测 \"
                '转义的双引号：保留反斜杠和引号，不切换 InQuotes
                Current.Append("\""")
                i += 1 '额外跳过下一个字符（双引号）
            ElseIf c = """"c Then
                '未转义的双引号：切换 InQuotes
                InQuotes = Not InQuotes
                Current.Append(c)
            ElseIf c = " "c AndAlso Not InQuotes Then
                '不在双引号内的空格：一个参数结束
                If Current.Length > 0 Then
                    Yield Current.ToString()
                    Current.Clear()
                End If
            Else
                '普通字符，直接添加
                Current.Append(c)
            End If
            i += 1
        End While
        If Current.Length > 0 Then Yield Current.ToString '添加剩余的
    End Function
    ''' <summary>
    ''' 对 Java 参数进行去重。
    ''' </summary>
    Private Function DeduplicateJavaArguments(Args As List(Of String), IsJVMArgs As Boolean) As List(Of String)
        Dim Result As New List(Of String)
        Dim i As Integer = 0
        While i < Args.Count
            Dim Key As String = Args(i)
            If Not Key.StartsWithF("-") OrElse
               (i + 1 >= Args.Count OrElse (Args(i + 1).StartsWithF("-") AndAlso Val(Args(i + 1).AfterFirst("-")) = 0)) Then '避免 -xPos 23 -xPos -50 中后面的 -50 被识别为参数
                '单参数
                i += 1
                If Result.Contains(Key) Then
                    McLaunchLog($"已去重单个{If(IsJVMArgs, " JVM ", "游戏")}参数：{Key}")
                    Continue While
                End If
                Result.Add(Key)
            Else
                '以空格间隔的键值对（例如 --width 233）
                Dim Value As String = Args(i + 1)
                i += 2
                For j = 0 To Result.Count - 2 '向前寻找相同的键
                    If Result(j) <> Key Then Continue For
                    '仅当是游戏参数且不是 tweakClass 时，用新的值覆盖旧的值
                    '据测试，如果包含多个相同 --width 或 --uuid 等参数，两个都会直接失效，如果这发生在关键参数上会导致崩溃
                    If Not IsJVMArgs AndAlso Key <> "--tweakClass" Then
                        McLaunchLog($"已覆盖重复的{If(IsJVMArgs, " JVM ", "游戏")}参数：{Key} {Result(j + 1)} → {Key} {Value}")
                        Result(j + 1) = Value
                        Continue While
                    End If
                    '键和值完全相同，直接抛弃
                    If Result(j + 1) = Value Then
                        McLaunchLog($"已去重{If(IsJVMArgs, " JVM ", "游戏")}参数对：{Key} {Value}")
                        Continue While
                    End If
                Next
                Result.Add(Key)
                Result.Add(Value)
            End If
        End While
        Return Result
    End Function

#End Region

#Region "解压 Natives"

    Private Sub McLaunchNatives(Loader As LoaderTask(Of Integer, Integer))

        '创建文件夹
        Dim Target As String = GetNativesFolder()
        DirectoryUtils.Create(Target)

        '解压文件
        McLaunchLog("正在解压 Natives 文件")
        Dim ExistFiles As New List(Of String)
        For Each Native As McLibEntry In McLibListGet(McInstanceSelected, True)
            If Not Native.IsNatives Then Continue For
            Dim Zip As ZipArchive
            Try
                Zip = FileUtils.OpenZip(Native.LocalPath)
            Catch ex As InvalidDataException
                Logger.Warn(ex, $"打开 Natives 文件失败（{Native.LocalPath}）")
                FileUtils.Delete(Native.LocalPath)
                Throw New Exception("无法打开 Natives 文件（" & Native.LocalPath & "），该文件可能已损坏，请重新尝试启动游戏")
            End Try
            For Each Entry In Zip.Entries
                Dim FileName As String = Entry.FullName
                If PathUtils.GetExtension(FileName) = "dll" Then
                    '实际解压文件的步骤
                    Dim FilePath As String = Target & FileName
                    ExistFiles.Add(FilePath)
                    Dim OriginalFile = FileUtils.GetInfo(FilePath)
                    If OriginalFile.Exists Then
                        If OriginalFile.Length = Entry.Length Then
                            If ModeDebug Then McLaunchLog("无需解压：" & FilePath)
                            Continue For
                        End If
                        '删除原文件
                        Try
                            FileUtils.Delete(FilePath)
                        Catch ex As UnauthorizedAccessException
                            McLaunchLog("删除原 dll 访问被拒绝，这通常代表有一个 MC 正在运行，跳过解压：" & FilePath)
                            McLaunchLog("实际的错误信息：" & ex.GetDisplay(True))
                            Exit For
                        End Try
                    End If
                    '解压新文件
                    FileUtils.Write(FilePath, Entry.Open)
                    McLaunchLog("已解压：" & FilePath)
                End If
            Next
            If Zip IsNot Nothing Then Zip.Dispose()
        Next

        '删除多余文件
        For Each FileName As String In DirectoryUtils.EnumerateFiles(Target)
            If ExistFiles.Contains(FileName) Then Continue For
            Try
                McLaunchLog("删除：" & FileName)
                FileUtils.Delete(FileName)
            Catch ex As UnauthorizedAccessException
                McLaunchLog("删除多余文件访问被拒绝，跳过删除步骤")
                McLaunchLog("实际的错误信息：" & ex.GetDisplay(True))
                Return
            End Try
        Next

    End Sub
    ''' <summary>
    ''' 获取 Natives 文件夹路径，以 \ 结尾。
    ''' </summary>
    Public Function GetNativesFolder(Optional Instance As McInstance = Nothing) As String
        If Instance Is Nothing Then Instance = McInstanceSelected
        Dim Result As String = PathUtils.AddSlashSuffix(PathUtils.ToShortPath(Instance.PathVersion)) & Instance.Name & "-natives\"
        If Not (IsGBKEncoding OrElse Result.IsAsciiOnly()) Then
            Result = Paths.AppData & ".minecraft\bin\natives\"
            If Not Result.IsAsciiOnly() Then
                Result = OsDrive & "ProgramData\PCL\natives\"
            End If
        End If
        DirectoryUtils.Create(Result) '提前创建，这样 DirectoryUtils.Shorten 才有结果（否则在长路径下首次启动 Forge 会崩溃）
        Return PathUtils.AddSlashSuffix(PathUtils.ToShortPath(Result))
    End Function

#End Region

#Region "启动与前后处理"

    Private Sub McLaunchPrerun()

        '使用高性能显卡
        If Settings.Get(Of Boolean)("LaunchAdvanceGraphicCard") Then
            Try
                SetGPUPreference(McLaunchJavaSelected.JavaExePath)
                SetGPUPreference(PathExe)
            Catch ex As Exception
                If WindowsUtils.HasAdminRole() Then
                    Logger.Warn(ex, "直接调整显卡设置失败")
                Else
                    Logger.Warn(ex, "直接调整显卡设置失败，将以管理员权限重启 PCL 再次尝试")
                    Try
                        If RunAsAdmin($"--gpu ""{McLaunchJavaSelected.JavaExePath}""") = ProcessReturnValues.TaskDone Then
                            McLaunchLog("以管理员权限重启 PCL 并调整显卡设置成功")
                        Else
                            Throw New Exception("调整过程中出现异常")
                        End If
                    Catch exx As Exception
                        Logger.Error(exx, "调整显卡设置失败，Minecraft 可能会使用默认显卡运行", LogBehavior.Toast)
                    End Try
                End If
            End Try
        End If

        '更新 launcher_profiles.json
        Try
            '确保可用
            If Not McLoginLoader.Output.Type = "Microsoft" Then Exit Try
            McFolderLauncherProfilesJsonCreate(McFolderSelected)
            '构建需要替换的 Json 对象
            Dim ReplaceJsonString As String = "
            {
              ""authenticationDatabase"": {
                ""00000111112222233333444445555566"": {
                  ""username"": """ & McLoginLoader.Output.Name.Replace("""", "-") & """,
                  ""profiles"": {
                    ""66666555554444433333222221111100"": {
                        ""displayName"": """ & McLoginLoader.Output.Name & """
                    }
                  }
                }
              },
              ""clientToken"": """ & McLoginLoader.Output.ClientToken & """,
              ""selectedUser"": {
                ""account"": ""00000111112222233333444445555566"", 
                ""profile"": ""66666555554444433333222221111100""
              }
            }"
            Dim ReplaceJson As JObject = (ReplaceJsonString).DeserializeJson()
            '更新文件
            Dim Profiles As JObject = FileUtils.ReadAsJson(McFolderSelected & "launcher_profiles.json")
            Profiles.Merge(ReplaceJson)
            FileUtils.Write(McFolderSelected & "launcher_profiles.json", Profiles.ToString, encoding:=Encoding.GetEncoding("GB18030"))
            McLaunchLog("已更新 launcher_profiles.json")
        Catch ex As Exception
            Logger.Warn(ex, "更新 launcher_profiles.json 失败，将在删除文件后重试")
            Try
                FileUtils.Delete(McFolderSelected & "launcher_profiles.json")
                McFolderLauncherProfilesJsonCreate(McFolderSelected)
                '构建需要替换的 Json 对象
                Dim ReplaceJsonString As String = "
                    {
                      ""authenticationDatabase"": {
                        ""00000111112222233333444445555566"": {
                          ""username"": """ & McLoginLoader.Output.Name.Replace("""", "-") & """,
                          ""profiles"": {
                            ""66666555554444433333222221111100"": {
                                ""displayName"": """ & McLoginLoader.Output.Name & """
                            }
                          }
                        }
                      },
                      ""clientToken"": """ & McLoginLoader.Output.ClientToken & """,
                      ""selectedUser"": {
                        ""account"": ""00000111112222233333444445555566"", 
                        ""profile"": ""66666555554444433333222221111100""
                      }
                    }"
                Dim ReplaceJson As JObject = ReplaceJsonString.DeserializeJson()
                '更新文件
                Dim Profiles As JObject = FileUtils.ReadAsJson(McFolderSelected & "launcher_profiles.json")
                Profiles.Merge(ReplaceJson)
                FileUtils.Write(McFolderSelected & "launcher_profiles.json", Profiles.ToString, encoding:=Encoding.GetEncoding("GB18030"))
                McLaunchLog("已在删除后更新 launcher_profiles.json")
            Catch exx As Exception
                Logger.Error(exx, "更新 launcher_profiles.json 失败")
            End Try
        End Try

        '更新 options.txt
        Dim SetupFileAddress As String = McInstanceSelected.PathIndie & "options.txt"
        If Not FileUtils.Exists(SetupFileAddress) Then
            'Yosbr Mod 兼容（#2385）：https://www.curseforge.com/minecraft/mc-mods/yosbr
            Dim YosbrFileAddress As String = McInstanceSelected.PathIndie & "config\yosbr\options.txt"
            If FileUtils.Exists(YosbrFileAddress) Then
                McLaunchLog("将修改 Yosbr Mod 中的 options.txt")
                SetupFileAddress = YosbrFileAddress
                WriteIni(SetupFileAddress, "lang", "none") '忽略默认语言
            End If
        End If
        Try
            '语言
            '1.0-     ：没有语言选项
            '1.1 ~ 5  ：zh_CN 时正常，zh_cn 时崩溃（最后两位字母必须大写，否则将会 NPE 崩溃）
            '1.6 ~ 10 ：zh_CN 时正常，zh_cn 时自动切换为英文
            '1.11 ~ 12：zh_cn 时正常，zh_CN 时虽然显示了中文但语言设置会错误地显示选择英文
            '1.13+    ：zh_cn 时正常，zh_CN 时自动切换为英文
            Dim CurrentLang As String = ReadIni(SetupFileAddress, "lang", "none")
            Dim RequiredLang As String = If(CurrentLang = "none" OrElse Not DirectoryUtils.Exists(McInstanceSelected.PathIndie & "saves"), '#3844，整合包可能已经自带了 options.txt
                If(Settings.Get(Of Boolean)("ToolHelpChinese"), "zh_cn", "en_us"), CurrentLang.Lower)
            If Not McInstanceSelected.Version.Valid OrElse McInstanceSelected.Version.Vanilla.Major <= 10 Then
                '将最后两位改为大写，前面的部分保留
                RequiredLang = RequiredLang.Substring(0, RequiredLang.Length - 2) & RequiredLang.Substring(RequiredLang.Length - 2).Upper
            End If
            If CurrentLang = RequiredLang Then
                McLaunchLog($"需要的语言为 {RequiredLang}，当前语言为 {CurrentLang}，无需修改")
            Else
                WriteIni(SetupFileAddress, "lang", "-") '触发缓存更改，避免删除后重新下载残留缓存
                WriteIni(SetupFileAddress, "lang", RequiredLang)
                McLaunchLog($"已将语言从 {CurrentLang} 修改为 {RequiredLang}")
            End If
            ''如果是初次设置，一并修改 forceUnicodeFont
            'If Settings.Get(Of Boolean)("ToolHelpChinese") AndAlso (CurrentLang = "none" OrElse Not DirectoryUtils.Exists(McInstanceSelected.PathIndie & "saves")) Then
            '    WriteIni(SetupFileAddress, "forceUnicodeFont", "true")
            '    McLaunchLog("已开启 forceUnicodeFont")
            'End If
            '窗口
            Select Case Settings.Get(Of Integer)("LaunchArgumentWindowType")
                Case 0 '全屏
                    WriteIni(SetupFileAddress, "fullscreen", "true")
                Case 1 '默认
                Case Else '其他
                    WriteIni(SetupFileAddress, "fullscreen", "false")
            End Select
        Catch ex As Exception
            Logger.Error(ex, "更新 options.txt 失败", LogBehavior.Toast)
        End Try


    End Sub
    Private Sub McLaunchCustom(Loader As LoaderTask(Of Integer, Integer))

        '获取自定义命令
        Dim CustomCommandGlobal As String = Settings.Get(Of String)("LaunchAdvanceRun")
        If CustomCommandGlobal <> "" Then CustomCommandGlobal = ArgumentReplace(CustomCommandGlobal)
        Dim CustomCommandVersion As String = Settings.Get(Of String)("VersionAdvanceRun", Instance:=McInstanceSelected)
        If CustomCommandVersion <> "" Then CustomCommandVersion = ArgumentReplace(CustomCommandVersion)

        '输出 bat
        Try
            Dim CmdString As String =
                $"chcp {If(McLaunchEncoding.CodePage = 54936 AndAlso IsGBKEncoding, 936, McLaunchEncoding.CodePage)}>nul" & vbCrLf &
                "@echo off" & vbCrLf &
                $"title 启动 - {McInstanceSelected.Name}" & vbCrLf &
                "echo 游戏正在启动，请稍候。" & vbCrLf &
                $"cd /D ""{PathUtils.ToShortPath(McInstanceSelected.PathIndie)}""" & vbCrLf &
                CustomCommandGlobal & vbCrLf &
                CustomCommandVersion & vbCrLf &
                $"""{McLaunchJavaSelected.JavaExePath}"" {McLaunchArgument}" & vbCrLf &
                "echo 游戏已退出。" & vbCrLf &
                "pause"
            FileUtils.Write(
                filePath:=If(CurrentLaunchOptions.SaveBatch, Paths.Base & "PCL\LatestLaunch.bat"),
                text:=FilterAccessToken(CmdString, "F").Replace("%", "%%"),
                encoding:=McLaunchEncoding)
            If CurrentLaunchOptions.SaveBatch IsNot Nothing Then
                McLaunchLog("导出启动脚本完成，强制结束启动过程")
                CanceledHint = "导出启动脚本成功！"
                OpenExplorer(CurrentLaunchOptions.SaveBatch)
                Loader.Parent.Cancel()
                Return '导出脚本完成
            End If
        Catch ex As Exception
            Logger.Warn(ex, "输出启动脚本失败")
            If CurrentLaunchOptions.SaveBatch IsNot Nothing Then Throw ex '直接触发启动失败
        End Try

        '执行自定义命令
        If CustomCommandGlobal <> "" Then
            McLaunchLog("正在执行全局自定义命令：" & CustomCommandGlobal)
            Dim CustomProcess As Process = Nothing
            Try
                CustomProcess = StartProcess(New ProcessStartInfo With {
                    .FileName = "cmd.exe",
                    .Arguments = $"/c ""{CustomCommandGlobal}""",
                    .WorkingDirectory = McFolderSelected,
                    .UseShellExecute = False,
                    .CreateNoWindow = True
                })
                If Settings.Get(Of Boolean)("LaunchAdvanceRunWait") Then
                    Do Until CustomProcess.HasExited OrElse Loader.IsCanceled
                        Thread.Sleep(10)
                    Loop
                End If
            Catch ex As Exception
                Logger.Error(ex, "执行全局自定义命令失败", LogBehavior.Toast)
            Finally
                If CustomProcess IsNot Nothing AndAlso Not CustomProcess.HasExited AndAlso Loader.IsCanceled Then
                    McLaunchLog("由于取消启动，已强制结束自定义命令 CMD 进程") '#1183
                    CustomProcess.Kill()
                End If
            End Try
        End If
        If CustomCommandVersion <> "" Then
            McLaunchLog("正在执行版本自定义命令：" & CustomCommandVersion)
            Dim CustomProcess As Process = Nothing
            Try
                CustomProcess = StartProcess(New ProcessStartInfo With {
                    .FileName = "cmd.exe",
                    .Arguments = $"/c ""{CustomCommandVersion}""",
                    .WorkingDirectory = McFolderSelected,
                    .UseShellExecute = False,
                    .CreateNoWindow = True
                })
                If Settings.Get(Of Boolean)("VersionAdvanceRunWait", Instance:=McInstanceSelected) Then
                    Do Until CustomProcess.HasExited OrElse Loader.IsCanceled
                        Thread.Sleep(10)
                    Loop
                End If
            Catch ex As Exception
                Logger.Error(ex, "执行版本自定义命令失败", LogBehavior.Toast)
            Finally
                If CustomProcess IsNot Nothing AndAlso Not CustomProcess.HasExited AndAlso Loader.IsCanceled Then
                    McLaunchLog("由于取消启动，已强制结束自定义命令 CMD 进程") '#1183
                    CustomProcess.Kill()
                End If
            End Try
        End If

    End Sub
    Private Sub McLaunchRun(Loader As LoaderTask(Of Integer, Process))

        '启动信息
        Dim StartInfo As New ProcessStartInfo(McLaunchJavaSelected.JavaExePath) '使用 javaw.exe 会导致 #6263

        '设置环境变量
        Dim PathEnv As String = StartInfo.EnvironmentVariables("Path")
        Dim Paths As New List(Of String)
        If Not String.IsNullOrEmpty(PathEnv) Then Paths.AddRange(PathEnv.Split(";"))
        Paths.Add(PathUtils.ToShortPath(McLaunchJavaSelected.Folder))
        StartInfo.EnvironmentVariables("Path") = Paths.Distinct.Join(";"c)
        StartInfo.EnvironmentVariables("appdata") = PathUtils.ToShortPath(McFolderSelected)

        '设置其他参数
        StartInfo.WorkingDirectory = McInstanceSelected.PathIndie
        StartInfo.UseShellExecute = False
        StartInfo.StandardOutputEncoding = McLaunchEncoding
        StartInfo.StandardErrorEncoding = McLaunchEncoding
        StartInfo.RedirectStandardOutput = True
        StartInfo.RedirectStandardError = True
        StartInfo.CreateNoWindow = True
        StartInfo.Arguments = McLaunchArgument

        '开始进程
        Dim GameProcess = StartProcess(StartInfo)
        McLaunchLog("已启动游戏进程：" & McLaunchJavaSelected.JavaExePath)
        If Loader.IsCanceled Then
            McLaunchLog("由于取消启动，已强制结束游戏进程") '#1631
            GameProcess.Kill()
            Return
        End If
        Loader.Output = GameProcess
        McLaunchProcess = GameProcess

        '进程优先级处理
        Try
            GameProcess.PriorityBoostEnabled = True
            Select Case Settings.Get(Of Integer)("LaunchArgumentPriority")
                Case 0 '高
                    GameProcess.PriorityClass = ProcessPriorityClass.AboveNormal
                Case 2 '低
                    GameProcess.PriorityClass = ProcessPriorityClass.BelowNormal
                Case Else '中
            End Select
        Catch ex As Exception
            Logger.Error(ex, "设置进程优先级失败")
        End Try

    End Sub
    Private Sub McLaunchWait(Loader As LoaderTask(Of Process, Integer))

        '输出信息
        McLaunchLog("")
        McLaunchLog("~ 基础参数 ~")
        McLaunchLog("PCL 版本：" & VersionDisplay & " (" & VersionCode & ")")
        McLaunchLog($"游戏版本：{McInstanceSelected.VersionDisplayName}（{McInstanceSelected.Version.Vanilla}，Drop {McInstanceSelected.Version.Drop}{If(McInstanceSelected.Version.Reliable, "", "，无法完全确定")}）")
        McLaunchLog("资源版本：" & McAssetsGetIndexName(McInstanceSelected))
        McLaunchLog("版本继承：" & If(McInstanceSelected.InheritName = "", "无", McInstanceSelected.InheritName))
        McLaunchLog("分配的内存：" & PageInstanceSetup.GetRam(McInstanceSelected) & " GB（" & Math.Round(PageInstanceSetup.GetRam(McInstanceSelected) * 1024) & " MB）")
        McLaunchLog("MC 文件夹：" & McFolderSelected)
        McLaunchLog("版本文件夹：" & McInstanceSelected.PathVersion)
        McLaunchLog("版本隔离：" & (McInstanceSelected.PathIndie = McInstanceSelected.PathVersion))
        McLaunchLog("HMCL 格式：" & McInstanceSelected.IsHmclFormatJson)
        McLaunchLog("Java 信息：" & If(McLaunchJavaSelected IsNot Nothing, McLaunchJavaSelected.ToString, "无可用 Java"))
        McLaunchLog("Natives 文件夹：" & GetNativesFolder())
        McLaunchLog("")
        McLaunchLog("~ 登录参数 ~")
        McLaunchLog("玩家用户名：" & McLoginLoader.Output.Name)
        McLaunchLog("AccessToken：" & McLoginLoader.Output.AccessToken)
        McLaunchLog("ClientToken：" & McLoginLoader.Output.ClientToken)
        McLaunchLog("UUID：" & McLoginLoader.Output.Uuid)
        McLaunchLog("登录方式：" & McLoginLoader.Output.Type)
        McLaunchLog("")

        '获取窗口标题
        Dim WindowTitle As String = Settings.Get(Of String)("VersionArgumentTitle", Instance:=McInstanceSelected)
        If WindowTitle = "" Then WindowTitle = Settings.Get(Of String)("LaunchArgumentTitle")
        WindowTitle = ArgumentReplace(WindowTitle, ReplaceTime:=False)

        '初始化等待
        Dim Watcher As New Watcher(Loader, McInstanceSelected, WindowTitle)
        McLaunchWatcher = Watcher

        '等待
        Do While Watcher.State = Watcher.MinecraftState.Loading
            Thread.Sleep(100)
        Loop
        If Watcher.State = Watcher.MinecraftState.Crashed Then
            Throw New OperationCanceledException
        End If

    End Sub
    Private Sub McLaunchEnd()
        McLaunchLog("开始启动结束处理")

        '暂停或开始音乐播放
        If Settings.Get(Of Boolean)("UiMusicStop") Then
            RunInUi(Sub() If MusicPause() Then Logger.Info("已根据设置，在启动后暂停音乐播放"))
        ElseIf Settings.Get(Of Boolean)("UiMusicStart") Then
            RunInUi(Sub() If MusicResume() Then Logger.Info("已根据设置，在启动后开始音乐播放"))
        End If

        '启动器可见性
        McLaunchLog("启动器可见性：" & Settings.Get(Of Integer)("LaunchArgumentVisible"))
        Select Case Settings.Get(Of Integer)("LaunchArgumentVisible")
            Case 0
                '直接关闭
                McLaunchLog("已根据设置，在启动后关闭启动器")
                RunInUi(Sub() FrmMain.EndProgram(False))
            Case 2, 3
                '隐藏
                McLaunchLog("已根据设置，在启动后隐藏启动器")
                RunInUi(Sub() FrmMain.Hidden = True)
            Case 4
                '最小化
                McLaunchLog("已根据设置，在启动后最小化启动器")
                RunInUi(Sub() FrmMain.WindowState = WindowState.Minimized)
            Case 5
                '啥都不干
        End Select

        '启动计数
        Settings.Set("SystemLaunchCount", Settings.Get(Of Integer)("SystemLaunchCount") + 1)

    End Sub

#End Region

End Module
