''' <summary>
''' 启动页右侧的 SDUcraft 下载速览面板。
''' 这个文件只负责面板的数据加载和界面行为；下载项模型与已读状态存储已拆分到 Modules\Sdu。
''' </summary>
Partial Public Class PageLaunchRight

#Region "常量"

    Private Const SduDownloadPanelTag As String = "SduDownloadPanel"
    Private Const SduDownloadApiUrl As String = "https://www.sducraft.top/api/download/"
    Private Const SduDownloadPageUrl As String = "https://www.sducraft.top/downloads/game"

    ''' <summary>首次渲染的数量；第 6 条开始需要滚动后再加载。</summary>
    Private Const SduDownloadInitialCount As Integer = 5

    ''' <summary>每次滚动到底部后追加的数量。</summary>
    Private Const SduDownloadPageSize As Integer = 3

    Private Const SduDownloadTitle As String = "SDUcraft 游戏下载速览"
    Private Const SduDownloadListMaxHeight As Double = 318.0
    Private Const SduDownloadScrollBottomThreshold As Double = 28.0
    Private Const SduDownloadIntroMaxLength As Integer = 75
    Private Const SduDownloadMaxResponseLength As Integer = 2 * 1024 * 1024

#End Region

#Region "界面状态"

    Private _SduDownloadItems As List(Of SduDownloadItem)
    Private _SduDownloadReadIds As HashSet(Of String)
    Private _SduDownloadCard As MyCard
    Private _SduDownloadJumpRow As Border
    Private _SduDownloadStatus As TextBlock
    Private WithEvents _SduDownloadScroll As ScrollViewer
    Private _SduDownloadStack As StackPanel

    Private _SduDownloadVisibleCount As Integer
    Private _SduDownloadIsLoading As Boolean
    Private _SduDownloadIsWaitingNextScroll As Boolean
    Private _SduDownloadInstalling As Boolean

    Private ReadOnly SduDownloadStartLock As New Object

#End Region

    ''' <summary>
    ''' 请求刷新面板。启动器通知关闭后会调用这里，避免用户必须切换页面才看到面板。
    ''' </summary>
    Public Sub RefreshSduDownloadPanel()
        SyncLock SduDownloadStartLock
            If _SduDownloadIsLoading Then Return
            If IsSduDownloadPanelVisible() Then Return
            _SduDownloadIsLoading = True
        End SyncLock

        RunInThread(AddressOf LoadSduDownloadPanel)
    End Sub

    Private Sub LoadSduDownloadPanel()
        Try
            Dim Response = NetRequestByClientRetry(SduDownloadApiUrl, RequireJson:=True)
            If Response.Length > SduDownloadMaxResponseLength Then
                Throw New InvalidOperationException($"下载讯息响应超过安全上限：{Response.Length} 字符")
            End If

            _SduDownloadItems = SduDownloadItem.ParseList(Response)

            RunInUi(
                Sub()
                    _SduDownloadIsLoading = False
                    _SduDownloadVisibleCount = 0
                    _SduDownloadIsWaitingNextScroll = False
                    RenderSduDownloadPanel()
                End Sub, True)
        Catch ex As Exception
            _SduDownloadIsLoading = False
            Logger.Warn(ex, "加载 SDUcraft 下载面板失败")
            RunInUi(Sub() RenderSduDownloadError("无法连接到 SDUcraft 下载服务，请稍后再试。"), True)
        End Try
    End Sub

    Private Function IsSduDownloadPanelVisible() As Boolean
        Return _SduDownloadCard IsNot Nothing AndAlso _SduDownloadItems IsNot Nothing
    End Function

#Region "渲染"

    Private Sub RenderSduDownloadPanel()
        If _SduDownloadItems Is Nothing Then Return

        RemoveSduDownloadPanel()
        _SduDownloadReadIds = SduDownloadReadStore.ReadIds()
        CreateSduDownloadControls()

        PanCustom.Children.Insert(0, _SduDownloadCard)
        AppendSduDownloadItems(SduDownloadInitialCount)
        UpdateSduDownloadStatus()
    End Sub

    Private Sub CreateSduDownloadControls()
        _SduDownloadCard = New MyCard With {
            .Title = SduDownloadTitle,
            .Margin = New Thickness(0, 0, 0, 15),
            .Tag = SduDownloadPanelTag
        }

        '顶部浮动行：跳转官网下载界面
        _SduDownloadJumpRow = CreateSduDownloadJumpRow()

        _SduDownloadStatus = New TextBlock With {
            .FontSize = 11.5,
            .Opacity = 0.65,
            .Margin = New Thickness(20, 68, 20, 6)
        }

        _SduDownloadStack = New StackPanel
        _SduDownloadScroll = New ScrollViewer With {
            .MaxHeight = SduDownloadListMaxHeight,
            .Margin = New Thickness(20, 96, 20, 14),
            .HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            .VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            .Content = _SduDownloadStack
        }

        _SduDownloadCard.Children.Add(_SduDownloadJumpRow)
        _SduDownloadCard.Children.Add(_SduDownloadStatus)
        _SduDownloadCard.Children.Add(_SduDownloadScroll)
    End Sub

    Private Function CreateSduDownloadJumpRow() As Border
        Dim Text As New TextBlock With {
            .Text = "在官网下载页浏览全部整合包",
            .FontSize = 12,
            .Foreground = New SolidColorBrush(Color.FromRgb(19, 112, 243)),
            .VerticalAlignment = VerticalAlignment.Center
        }
        Dim Arrow As New TextBlock With {
            .Text = "前往官网 →",
            .FontSize = 12,
            .Foreground = New SolidColorBrush(Color.FromRgb(19, 112, 243)),
            .FontWeight = FontWeights.Bold,
            .VerticalAlignment = VerticalAlignment.Center
        }
        Dim Row As New Grid
        Row.ColumnDefinitions.Add(New ColumnDefinition With {.Width = New GridLength(1, GridUnitType.Star)})
        Row.ColumnDefinitions.Add(New ColumnDefinition With {.Width = GridLength.Auto})
        Grid.SetColumn(Text, 0)
        Grid.SetColumn(Arrow, 1)
        Row.Children.Add(Text)
        Row.Children.Add(Arrow)

        Dim Border As New Border With {
            .Background = New SolidColorBrush(Color.FromRgb(234, 242, 254)),
            .CornerRadius = New CornerRadius(4),
            .Margin = New Thickness(20, 34, 20, 0),
            .Padding = New Thickness(10, 7, 10, 7),
            .Cursor = Cursors.Hand,
            .Child = Row
        }
        AddHandler Border.MouseLeftButtonUp, AddressOf SduDownloadJumpClick
        Return Border
    End Function

    Private Sub AppendSduDownloadItems(ItemCount As Integer)
        If _SduDownloadItems Is Nothing OrElse _SduDownloadStack Is Nothing Then Return

        Dim RemainingCount = _SduDownloadItems.Count - _SduDownloadVisibleCount
        If RemainingCount <= 0 Then
            UpdateSduDownloadStatus()
            Return
        End If

        Dim RenderCount = Math.Min(ItemCount, RemainingCount)
        Dim LastIndex = _SduDownloadVisibleCount + RenderCount - 1

        For Index = _SduDownloadVisibleCount To LastIndex
            _SduDownloadStack.Children.Add(CreateSduDownloadCardItem(_SduDownloadItems(Index)))
        Next

        _SduDownloadVisibleCount += RenderCount
        UpdateSduDownloadStatus()
    End Sub

    Private Sub UpdateSduDownloadStatus()
        If _SduDownloadStatus Is Nothing OrElse _SduDownloadItems Is Nothing Then Return

        Dim TotalCount = _SduDownloadItems.Count
        If _SduDownloadVisibleCount < TotalCount Then
            Dim NextCount = Math.Min(SduDownloadPageSize, TotalCount - _SduDownloadVisibleCount)
            _SduDownloadStatus.Text = $"已加载 {_SduDownloadVisibleCount} / {TotalCount} 条 · 向下滚动加载后 {NextCount} 条 · 点击条目直接下载安装"
        Else
            _SduDownloadStatus.Text = $"已加载全部 {TotalCount} 条 · 点击条目直接下载安装 · 未读标为【新】"
        End If
    End Sub

    Private Sub RenderSduDownloadError(ErrorMessage As String)
        RemoveSduDownloadPanel()

        Dim Card As New MyCard With {
            .Title = SduDownloadTitle,
            .Margin = New Thickness(0, 0, 0, 15),
            .Tag = SduDownloadPanelTag
        }
        Dim Message As New TextBlock With {
            .Text = $"读取失败：{ErrorMessage}{vbCrLf}请检查网络后刷新主页。",
            .Margin = New Thickness(20, 36, 20, 14),
            .TextWrapping = TextWrapping.Wrap
        }

        Card.Children.Add(Message)
        PanCustom.Children.Insert(0, Card)
    End Sub

    Private Sub RemoveSduDownloadPanel()
        For Index = PanCustom.Children.Count - 1 To 0 Step -1
            Dim Child = TryCast(PanCustom.Children(Index), FrameworkElement)
            If Child Is _SduDownloadCard Then
                PanCustom.Children.RemoveAt(Index)
            End If
        Next

        _SduDownloadCard = Nothing
        _SduDownloadJumpRow = Nothing
        _SduDownloadStatus = Nothing
        _SduDownloadScroll = Nothing
        _SduDownloadStack = Nothing
    End Sub

#End Region

#Region "滚动加载"

    Private Sub SduDownloadScrollChanged(Sender As Object, EventArgs As ScrollChangedEventArgs) Handles _SduDownloadScroll.ScrollChanged
        If Not CanLoadMoreSduDownloadItems() Then Return

        Dim DistanceToBottom = _SduDownloadScroll.ScrollableHeight - _SduDownloadScroll.VerticalOffset
        If DistanceToBottom > SduDownloadScrollBottomThreshold Then
            _SduDownloadIsWaitingNextScroll = False
            Return
        End If

        '避免一次连续滚动事件把剩余数据全部展开；只有再次接近底部时才追加下一页。
        If _SduDownloadIsWaitingNextScroll Then Return

        _SduDownloadIsWaitingNextScroll = True
        AppendSduDownloadItems(SduDownloadPageSize)
    End Sub

    Private Function CanLoadMoreSduDownloadItems() As Boolean
        Return _SduDownloadItems IsNot Nothing AndAlso
               _SduDownloadScroll IsNot Nothing AndAlso
               _SduDownloadVisibleCount < _SduDownloadItems.Count
    End Function

#End Region

#Region "条目交互"

    Private Function CreateSduDownloadCardItem(Item As SduDownloadItem) As Border
        Dim IsUnread = Not String.IsNullOrEmpty(Item.Id) AndAlso
                       (_SduDownloadReadIds Is Nothing OrElse Not _SduDownloadReadIds.Contains(Item.Id))

        Dim Border As New Border With {
            .Background = New SolidColorBrush(Color.FromRgb(255, 255, 255)),
            .BorderBrush = New SolidColorBrush(Color.FromRgb(217, 225, 236)),
            .BorderThickness = New Thickness(1),
            .CornerRadius = New CornerRadius(5),
            .Padding = New Thickness(10, 8, 10, 8),
            .Margin = New Thickness(0, 0, 0, 8),
            .Cursor = Cursors.Hand,
            .Tag = Item.Id
        }

        Dim ItemStack As New StackPanel
        ItemStack.Children.Add(CreateItemTitle(Item, IsUnread))

        If Not String.IsNullOrWhiteSpace(Item.Version) Then
            ItemStack.Children.Add(CreateItemMeta(Item))
        End If

        If Not String.IsNullOrWhiteSpace(Item.Intro) Then
            ItemStack.Children.Add(CreateItemIntro(Item))
        End If

        Border.Child = ItemStack
        AddHandler Border.MouseLeftButtonUp, AddressOf SduDownloadItemClick
        Return Border
    End Function

    Private Function CreateItemTitle(Item As SduDownloadItem, IsUnread As Boolean) As Grid
        Dim TitleGrid As New Grid
        Dim Title As New TextBlock With {
            .Text = Item.Name,
            .FontSize = 13,
            .FontWeight = FontWeights.Bold,
            .Foreground = New SolidColorBrush(Color.FromRgb(23, 48, 94)),
            .TextWrapping = TextWrapping.Wrap,
            .Margin = New Thickness(0, 0, If(IsUnread, 28, 0), 0)
        }

        TitleGrid.Children.Add(Title)

        If IsUnread Then
            TitleGrid.Children.Add(CreateUnreadBadge())
        End If

        Return TitleGrid
    End Function

    Private Function CreateUnreadBadge() As Border
        Return New Border With {
            .Background = New SolidColorBrush(Color.FromRgb(229, 72, 77)),
            .CornerRadius = New CornerRadius(3),
            .Padding = New Thickness(5, 0, 5, 0),
            .HorizontalAlignment = HorizontalAlignment.Right,
            .VerticalAlignment = VerticalAlignment.Top,
            .Child = New TextBlock With {
                .Text = "新",
                .Foreground = Brushes.White,
                .FontSize = 10.5,
                .FontWeight = FontWeights.Bold
            }
        }
    End Function

    Private Function CreateItemMeta(Item As SduDownloadItem) As TextBlock
        Return New TextBlock With {
            .Text = $"{Item.PublishDate}    {Item.Version}",
            .FontSize = 11.5,
            .Opacity = 0.68,
            .Margin = New Thickness(0, 2, 0, 0)
        }
    End Function

    Private Function CreateItemIntro(Item As SduDownloadItem) As TextBlock
        Dim Intro = Item.Intro
        If Intro.Length > SduDownloadIntroMaxLength Then
            Intro = Intro.Substring(0, SduDownloadIntroMaxLength) & "…"
        End If

        Return New TextBlock With {
            .Text = Intro,
            .FontSize = 11.5,
            .Opacity = 0.82,
            .TextWrapping = TextWrapping.Wrap,
            .MaxHeight = 34,
            .TextTrimming = TextTrimming.CharacterEllipsis,
            .Margin = New Thickness(0, 3, 0, 0)
        }
    End Function

    Private Sub SduDownloadJumpClick(Sender As Object, EventArgs As System.Windows.Input.MouseButtonEventArgs)
        OpenWebsite(SduDownloadPageUrl)
    End Sub

    Private Sub SduDownloadItemClick(Sender As Object, EventArgs As System.Windows.Input.MouseButtonEventArgs)
        Dim ItemBorder = TryCast(Sender, Border)
        If ItemBorder Is Nothing Then Return

        Dim ItemId = If(ItemBorder.Tag, String.Empty).ToString()
        If Not String.IsNullOrEmpty(ItemId) Then
            MarkSduDownloadItemRead(ItemBorder, ItemId)
        End If

        StartSduDownloadInstall(ItemId)
    End Sub

    Private Sub StartSduDownloadInstall(ItemId As String)
        If ItemId = "" Then Return
        If RestrictMcDownload Then
            Hint("当前构建已关闭 MC 下载安装功能。", HintType.Red)
            Return
        End If
        If _SduDownloadInstalling Then
            Hint("已有 SDUcraft 整合包正在下载安装。", HintType.Blue)
            Return
        End If

        _SduDownloadInstalling = True
        RunInThread(
        Sub()
            Try
                '下载页数据与速览列表共用同一 ID，这里拉取完整数据以拿到下载直链
                Dim Packs = SduModpackService.FetchModpacks()
                Dim Pack = Packs.FirstOrDefault(Function(P) P.Id = ItemId)
                If Pack Is Nothing Then
                    Hint("未找到该整合包的下载文件。", HintType.Red)
                    Return
                End If
                SduModpackInstaller.StartInstall(Pack)
            Catch ex As Exception
                ex.ThrowIfCanceled()
                Logger.Error(ex, "SDUcraft 整合包一键安装失败", LogBehavior.Alert)
            Finally
                RunInUi(Sub() _SduDownloadInstalling = False)
            End Try
        End Sub)
    End Sub

    Private Sub MarkSduDownloadItemRead(ItemBorder As Border, ItemId As String)
        If _SduDownloadReadIds Is Nothing Then
            _SduDownloadReadIds = New HashSet(Of String)
        End If
        If _SduDownloadReadIds.Contains(ItemId) Then Return

        _SduDownloadReadIds.Add(ItemId)
        SduDownloadReadStore.SaveIds(_SduDownloadReadIds)

        Dim ItemStack = TryCast(ItemBorder.Child, StackPanel)
        If ItemStack Is Nothing OrElse ItemStack.Children.Count = 0 Then Return

        Dim TitleGrid = TryCast(ItemStack.Children(0), Grid)
        If TitleGrid Is Nothing Then Return

        If TitleGrid.Children.Count > 1 Then TitleGrid.Children.RemoveAt(1)

        Dim Title = TryCast(TitleGrid.Children(0), TextBlock)
        If Title IsNot Nothing Then Title.Margin = New Thickness(0)
    End Sub

#End Region

End Class
