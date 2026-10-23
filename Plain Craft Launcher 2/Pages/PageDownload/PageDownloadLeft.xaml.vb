Public Class PageDownloadLeft
    Implements IRefreshable

#Region "页面切换"

    ''' <summary>
    ''' 当前页面的编号。
    ''' </summary>
    Public PageID As FormMain.PageSubType = FormMain.PageSubType.DownloadSdu

    ''' <summary>旧下载入口统一转到 SDUcraft；保留其余社区资源的页面编号。</summary>
    Public Shared Function NormalizePageType(ID As FormMain.PageSubType) As FormMain.PageSubType
        Select Case ID
            Case FormMain.PageSubType.DownloadInstall, FormMain.PageSubType.DownloadPack
                Return FormMain.PageSubType.DownloadSdu
            Case Else
                Return ID
        End Select
    End Function

    ''' <summary>
    ''' 勾选事件改变页面。
    ''' </summary>
    Private Sub PageCheck(sender As FrameworkElement, e As RouteEventArgs) Handles ItemMod.Check, ItemSdu.Check, ItemResourcePack.Check, ItemShader.Check, ItemDataPack.Check
        '尚未初始化控件属性时，sender.Tag 为 Nothing，会导致切换到页面 0
        '若使用 IsLoaded，则会导致模拟点击不被执行（模拟点击切换页面时，控件的 IsLoaded 为 False）
        If sender.Tag IsNot Nothing Then PageChange(Val(sender.Tag))
    End Sub

    Public Function PageGet(Optional ID As FormMain.PageSubType = -1)
        If ID = -1 Then ID = PageID
        ID = NormalizePageType(ID)
        Select Case ID
            Case FormMain.PageSubType.DownloadMod
                If FrmDownloadMod Is Nothing Then FrmDownloadMod = New PageDownloadMod
                Return FrmDownloadMod
            Case FormMain.PageSubType.DownloadSdu
                If FrmDownloadSdu Is Nothing Then FrmDownloadSdu = New PageDownloadSdu
                Return FrmDownloadSdu
            Case FormMain.PageSubType.DownloadResourcePack
                If FrmDownloadResourcePack Is Nothing Then FrmDownloadResourcePack = New PageDownloadResourcePack
                Return FrmDownloadResourcePack
            Case FormMain.PageSubType.DownloadShader
                If FrmDownloadShader Is Nothing Then FrmDownloadShader = New PageDownloadShader
                Return FrmDownloadShader
            Case FormMain.PageSubType.DownloadDataPack
                If FrmDownloadDataPack Is Nothing Then FrmDownloadDataPack = New PageDownloadDataPack
                Return FrmDownloadDataPack
            Case Else
                Throw New Exception("未知的下载子页面种类：" & ID)
        End Select
    End Function

    ''' <summary>
    ''' 切换现有页面。
    ''' </summary>
    Public Sub PageChange(ID As FormMain.PageSubType)
        ID = NormalizePageType(ID)
        If PageID = ID Then Return
        AniControlEnabled += 1
        Try
            PageChangeRun(PageGet(ID))
            PageID = ID
        Catch ex As Exception
            Logger.Error(ex, $"切换分页面失败（ID {ID}）")
        Finally
            AniControlEnabled -= 1
        End Try
    End Sub
    Private Shared Sub PageChangeRun(Target As MyPageRight)
        AniStop("FrmMain PageChangeRight") '停止主页面的右页面切换动画，防止它与本动画一起触发多次 PageOnEnter
        If Target.Parent IsNot Nothing Then Target.SetValue(ContentPresenter.ContentProperty, Nothing)
        FrmMain.PageRight = Target
        CType(FrmMain.PanMainRight.Child, MyPageRight).PageOnExit()
        AniStart({
            AaCode(
            Sub()
                CType(FrmMain.PanMainRight.Child, MyPageRight).PageOnForceExit()
                FrmMain.PanMainRight.Child = FrmMain.PageRight
                FrmMain.PageRight.Opacity = 0
            End Sub, 130),
            AaCode(
            Sub()
                '延迟触发页面通用动画，以使得在 Loaded 事件中加载的控件得以处理
                FrmMain.PageRight.Opacity = 1
                FrmMain.PageRight.PageOnEnter()
            End Sub, 30, True)
        }, "PageLeft PageChange")
    End Sub

#End Region

    '强制刷新
    Public Sub Refresh_Click(sender As Object, e As EventArgs) '由边栏按钮匿名调用
        Refresh(Val(sender.Tag))
    End Sub
    Public Sub Refresh() Implements IRefreshable.Refresh
        Refresh(FrmMain.PageCurrentSub)
    End Sub
    Public Sub Refresh(SubType As FormMain.PageSubType)
        SubType = NormalizePageType(SubType)
        ResourceProject.Cache.Clear()
        ResourceVersion.ProjectFilesCache.Clear()
        Select Case SubType
            Case FormMain.PageSubType.DownloadMod
                If FrmDownloadMod IsNot Nothing Then
                    FrmDownloadMod.Content.Storage = New ResourceSearcher.SearchResult
                    FrmDownloadMod.Content.Page = 0
                    FrmDownloadMod.PageLoaderRestart()
                End If
                ItemMod.Checked = True
            Case FormMain.PageSubType.DownloadSdu
                If FrmDownloadSdu IsNot Nothing Then FrmDownloadSdu.Refresh()
                ItemSdu.Checked = True
            Case FormMain.PageSubType.DownloadResourcePack
                If FrmDownloadResourcePack IsNot Nothing Then
                    FrmDownloadResourcePack.Content.Storage = New ResourceSearcher.SearchResult
                    FrmDownloadResourcePack.Content.Page = 0
                    FrmDownloadResourcePack.PageLoaderRestart()
                End If
                ItemResourcePack.Checked = True
            Case FormMain.PageSubType.DownloadShader
                If FrmDownloadShader IsNot Nothing Then
                    FrmDownloadShader.Content.Storage = New ResourceSearcher.SearchResult
                    FrmDownloadShader.Content.Page = 0
                    FrmDownloadShader.PageLoaderRestart()
                End If
                ItemShader.Checked = True
            Case FormMain.PageSubType.DownloadDataPack
                If FrmDownloadDataPack IsNot Nothing Then
                    FrmDownloadDataPack.Content.Storage = New ResourceSearcher.SearchResult
                    FrmDownloadDataPack.Content.Page = 0
                    FrmDownloadDataPack.PageLoaderRestart()
                End If
                ItemDataPack.Checked = True
        End Select
        Hint("正在刷新……", Log:=False)
    End Sub


End Class
