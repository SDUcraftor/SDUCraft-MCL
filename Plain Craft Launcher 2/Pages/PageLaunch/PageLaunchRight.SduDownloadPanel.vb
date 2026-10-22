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
    Private Const SduDownloadMaxResponseLength As Integer = 2 * 1024 * 1024

#End Region

#Region "界面状态"

    Private _SduDownloadItems As List(Of SduDownloadItem)
    Private _SduDownloadReadIds As HashSet(Of String)
    Private _SduDownloadCard As MyCard
    Private _SduDownloadJumpRow As Button
    Private _SduDownloadMoreButton As Button
    Private _SduDownloadStatus As TextBlock
    Private WithEvents _SduDownloadScroll As ScrollViewer
    Private _SduDownloadStack As StackPanel

    Private _SduDownloadVisibleCount As Integer
    Private _SduDownloadIsLoading As Boolean
    Private _SduDownloadRefreshPending As Boolean
    Private _SduDownloadInstalling As Boolean

    Private ReadOnly SduDownloadStartLock As New Object

#End Region

    ''' <summary>
    ''' 请求刷新面板。启动器通知关闭后会调用这里，避免用户必须切换页面才看到面板。
    ''' </summary>
    Public Sub RefreshSduDownloadPanel(Optional Force As Boolean = False)
        SyncLock SduDownloadStartLock
            If _SduDownloadIsLoading Then
                If Force Then _SduDownloadRefreshPending = True
                Return
            End If
            If Not Force AndAlso IsSduDownloadPanelVisible() Then Return
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

            Dim Items = SduDownloadItem.ParseList(Response)

            RunInUi(
                Sub()
                    _SduDownloadItems = Items
                    _SduDownloadVisibleCount = 0
                    RenderSduDownloadPanel()
                End Sub, True)
        Catch ex As Exception
            Logger.Warn(ex, "加载 SDUcraft 下载面板失败")
            RunInUi(Sub() RenderSduDownloadError("无法连接到 SDUcraft 下载服务，请稍后再试。"), True)
        Finally
            Dim RefreshPending As Boolean
            SyncLock SduDownloadStartLock
                _SduDownloadIsLoading = False
                RefreshPending = _SduDownloadRefreshPending
                _SduDownloadRefreshPending = False
            End SyncLock
            If RefreshPending Then RefreshSduDownloadPanel(Force:=True)
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

        'MyCard 只预留标题高度；内容按顺序排列，避免多个顶部边距互相覆盖。
        Dim Content As New StackPanel With {.Margin = New Thickness(16, 34, 16, 12)}
        Dim Toolbar As New Grid With {.Margin = New Thickness(0, 0, 0, 8)}
        Toolbar.ColumnDefinitions.Add(New ColumnDefinition With {.Width = New GridLength(1, GridUnitType.Star)})
        Toolbar.ColumnDefinitions.Add(New ColumnDefinition With {.Width = GridLength.Auto})
        Dim Caption As New TextBlock With {
            .Text = "点击整合包，下载并安装",
            .FontSize = 11,
            .VerticalAlignment = VerticalAlignment.Center,
            .TextTrimming = TextTrimming.CharacterEllipsis,
            .Margin = New Thickness(0, 0, 8, 0)
        }
        Caption.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrushGray2")
        Toolbar.Children.Add(Caption)
        _SduDownloadJumpRow = CreateSduDownloadJumpRow()
        Grid.SetColumn(_SduDownloadJumpRow, 1)
        Toolbar.Children.Add(_SduDownloadJumpRow)

        _SduDownloadStack = New StackPanel With {.Margin = New Thickness(0, 0, 6, 0)}
        _SduDownloadScroll = New ScrollViewer With {
            .MaxHeight = SduDownloadListMaxHeight,
            .HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            .VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            .Focusable = False,
            .Content = _SduDownloadStack
        }
        '复用启动器的细滚动条，保持主题色；普通 ScrollViewer 仍支持键盘和鼠标滚轮。
        _SduDownloadScroll.SetResourceReference(FrameworkElement.StyleProperty, "ScrollViewerNoMargin")

        Dim Footer As New Grid With {.Margin = New Thickness(0, 8, 0, 0)}
        Footer.ColumnDefinitions.Add(New ColumnDefinition With {.Width = New GridLength(1, GridUnitType.Star)})
        Footer.ColumnDefinitions.Add(New ColumnDefinition With {.Width = GridLength.Auto})
        _SduDownloadStatus = New TextBlock With {.FontSize = 11, .VerticalAlignment = VerticalAlignment.Center}
        _SduDownloadStatus.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrushGray2")
        _SduDownloadMoreButton = CreateSduTextButton("加载更多", "加载下一页整合包")
        AddHandler _SduDownloadMoreButton.Click, Sub() AppendSduDownloadItems(SduDownloadPageSize)
        Grid.SetColumn(_SduDownloadMoreButton, 1)
        Footer.Children.Add(_SduDownloadStatus)
        Footer.Children.Add(_SduDownloadMoreButton)

        Content.Children.Add(Toolbar)
        Content.Children.Add(_SduDownloadScroll)
        Content.Children.Add(Footer)
        _SduDownloadCard.Children.Add(Content)
    End Sub

    Private Function CreateSduDownloadJumpRow() As Button
        Dim Button = CreateSduTextButton("浏览官网 ↗", "在浏览器中打开 SDUcraft 官网下载页")
        AddHandler Button.Click, AddressOf SduDownloadJumpClick
        Return Button
    End Function

    Private Function CreateSduTextButton(Text As String, Description As String) As Button
        Dim Button As New Button With {
            .FontSize = 11, .Cursor = Cursors.Hand,
            .Padding = New Thickness(7, 3, 7, 3), .MinHeight = 24,
            .Background = Brushes.Transparent, .BorderThickness = New Thickness(0), .ToolTip = Description
        }
        '显式文字控件，避免应用的隐式 TextBlock 样式覆盖按钮字号和强调色。
        Dim Label As New TextBlock With {.Text = Text, .FontSize = 11}
        Label.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrush3")
        Button.Content = Label
        Button.SetResourceReference(Control.ForegroundProperty, "ColorBrush3")
        Automation.AutomationProperties.SetName(Button, Description)
        Dim Chrome As New FrameworkElementFactory(GetType(Border))
        Chrome.SetValue(Border.CornerRadiusProperty, New CornerRadius(4))
        Chrome.SetBinding(Border.BackgroundProperty, New Binding("Background") With {.RelativeSource = RelativeSource.TemplatedParent})
        Chrome.SetBinding(Border.PaddingProperty, New Binding("Padding") With {.RelativeSource = RelativeSource.TemplatedParent})
        Dim Presenter As New FrameworkElementFactory(GetType(ContentPresenter))
        Presenter.SetBinding(ContentPresenter.ContentProperty, New Binding("Content") With {.RelativeSource = RelativeSource.TemplatedParent})
        Presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center)
        Presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center)
        Chrome.AppendChild(Presenter)
        Dim Template As New ControlTemplate(GetType(Button)) With {.VisualTree = Chrome}
        Dim Highlight As New Trigger With {.Property = UIElement.IsMouseOverProperty, .Value = True}
        Highlight.Setters.Add(New Setter(Border.BackgroundProperty, New DynamicResourceExtension("ColorBrush8")))
        Template.Triggers.Add(Highlight)
        Dim Focus As New Trigger With {.Property = UIElement.IsKeyboardFocusedProperty, .Value = True}
        Focus.Setters.Add(New Setter(Border.BackgroundProperty, New DynamicResourceExtension("ColorBrush8")))
        Template.Triggers.Add(Focus)
        Button.Template = Template
        Return Button
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
        '撤销 ScrollToEnd 的“保持在末尾”请求。追加后保持当前位置，下一次向下滚动即可继续加载。
        _SduDownloadScroll.ScrollToVerticalOffset(_SduDownloadScroll.VerticalOffset)
        UpdateSduDownloadStatus()
    End Sub

    Private Sub UpdateSduDownloadStatus()
        If _SduDownloadStatus Is Nothing OrElse _SduDownloadItems Is Nothing Then Return

        Dim TotalCount = _SduDownloadItems.Count
        If _SduDownloadVisibleCount < TotalCount Then
            _SduDownloadStatus.Text = $"已显示 {_SduDownloadVisibleCount} / {TotalCount} 个整合包"
            CType(_SduDownloadMoreButton.Content, TextBlock).Text = "加载更多 ↓"
            _SduDownloadMoreButton.Visibility = Visibility.Visible
        Else
            _SduDownloadStatus.Text = If(TotalCount = 0, "暂无可下载的整合包", $"已显示全部 {TotalCount} 个整合包")
            _SduDownloadMoreButton.Visibility = Visibility.Collapsed
        End If
        _SduDownloadScroll.Visibility = If(TotalCount = 0, Visibility.Collapsed, Visibility.Visible)
    End Sub

    Private Sub RenderSduDownloadError(ErrorMessage As String)
        RemoveSduDownloadPanel()

        Dim Card As New MyCard With {
            .Title = SduDownloadTitle,
            .Margin = New Thickness(0, 0, 0, 15),
            .Tag = SduDownloadPanelTag
        }
        _SduDownloadCard = Card
        Dim Message As New TextBlock With {
            .Text = $"读取失败：{ErrorMessage}{vbCrLf}请检查网络后刷新主页。",
            .Margin = New Thickness(16, 38, 16, 16),
            .FontSize = 12,
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
        _SduDownloadMoreButton = Nothing
        _SduDownloadStatus = Nothing
        _SduDownloadScroll = Nothing
        _SduDownloadStack = Nothing
    End Sub

#End Region

#Region "滚动加载"

    Private Sub SduDownloadScrollChanged(Sender As Object, EventArgs As ScrollChangedEventArgs) Handles _SduDownloadScroll.ScrollChanged
        If Not CanLoadMoreSduDownloadItems() Then Return

        Dim DistanceToBottom = _SduDownloadScroll.ScrollableHeight - _SduDownloadScroll.VerticalOffset
        If DistanceToBottom > SduDownloadScrollBottomThreshold Then Return

        '布局/缩放带来的 ScrollChanged 不应触发加载；保留首次只显示五项。
        If EventArgs.VerticalChange <= 0 OrElse EventArgs.ExtentHeightChange <> 0 Then Return
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
            .BorderThickness = New Thickness(1),
            .CornerRadius = New CornerRadius(6),
            .Padding = New Thickness(12, 7, 12, 7),
            .Margin = New Thickness(0, 0, 0, 6),
            .Focusable = True,
            .Cursor = Cursors.Hand,
            .Tag = Item.Id
        }
        Dim ItemStyle As New Style(GetType(Border))
        ItemStyle.Setters.Add(New Setter(Border.BackgroundProperty, New DynamicResourceExtension("ColorBrushWhite")))
        ItemStyle.Setters.Add(New Setter(Border.BorderBrushProperty, New DynamicResourceExtension("ColorBrushGray6")))
        For Each PropertyValue In {UIElement.IsMouseOverProperty, UIElement.IsKeyboardFocusWithinProperty}
            Dim Highlight As New Trigger With {.Property = PropertyValue, .Value = True}
            Highlight.Setters.Add(New Setter(Border.BackgroundProperty, New DynamicResourceExtension("ColorBrush8")))
            Highlight.Setters.Add(New Setter(Border.BorderBrushProperty, New DynamicResourceExtension("ColorBrush5")))
            ItemStyle.Triggers.Add(Highlight)
        Next
        Border.Style = ItemStyle
        Automation.AutomationProperties.SetName(Border, $"下载并安装 {Item.Name}")
        Border.ToolTip = New ToolTip With {
            .MaxWidth = 360,
            .Content = New TextBlock With {.Text = Item.Name & If(String.IsNullOrWhiteSpace(Item.Intro), "", vbCrLf & Item.Intro), .MaxHeight = 180, .TextWrapping = TextWrapping.Wrap}
        }

        Dim ItemStack As New StackPanel
        ItemStack.Children.Add(CreateItemTitle(Item, IsUnread))

        If Not String.IsNullOrWhiteSpace(Item.Version) OrElse Not String.IsNullOrWhiteSpace(Item.PublishDate) Then
            ItemStack.Children.Add(CreateItemMeta(Item))
        End If

        If Not String.IsNullOrWhiteSpace(Item.Intro) Then
            ItemStack.Children.Add(CreateItemIntro(Item))
        End If

        Border.Child = ItemStack
        AddHandler Border.MouseLeftButtonUp, AddressOf SduDownloadItemClick
        AddHandler Border.KeyDown,
            Sub(Sender, Args)
                If Args.Key <> Key.Enter AndAlso Args.Key <> Key.Space Then Return
                MarkSduDownloadItemRead(Border, Item.Id)
                StartSduDownloadInstall(Item.Id)
                Args.Handled = True
            End Sub
        Return Border
    End Function

    Private Function CreateItemTitle(Item As SduDownloadItem, IsUnread As Boolean) As Grid
        Dim TitleGrid As New Grid
        TitleGrid.ColumnDefinitions.Add(New ColumnDefinition With {.Width = New GridLength(1, GridUnitType.Star)})
        TitleGrid.ColumnDefinitions.Add(New ColumnDefinition With {.Width = GridLength.Auto})
        TitleGrid.ColumnDefinitions.Add(New ColumnDefinition With {.Width = GridLength.Auto})
        Dim Title As New TextBlock With {
            .Text = Item.Name,
            .FontSize = 13,
            .FontWeight = FontWeights.SemiBold,
            .TextTrimming = TextTrimming.CharacterEllipsis,
            .VerticalAlignment = VerticalAlignment.Center,
            .Margin = New Thickness(0, 0, 8, 0)
        }
        Title.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrush1")
        TitleGrid.Children.Add(Title)

        If IsUnread Then
            Dim Badge = CreateUnreadBadge()
            Grid.SetColumn(Badge, 1)
            TitleGrid.Children.Add(Badge)
        End If
        Dim Action As New TextBlock With {
            .Text = "↓", .FontSize = 14, .Margin = New Thickness(12, 0, 0, 0),
            .VerticalAlignment = VerticalAlignment.Center
        }
        Action.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrush3")
        Grid.SetColumn(Action, 2)
        TitleGrid.Children.Add(Action)

        Return TitleGrid
    End Function

    Private Function CreateUnreadBadge() As Border
        Dim Badge As New Border With {
            .Tag = "SduUnreadBadge",
            .CornerRadius = New CornerRadius(4),
            .Padding = New Thickness(5, 1, 5, 1),
            .HorizontalAlignment = HorizontalAlignment.Right,
            .VerticalAlignment = VerticalAlignment.Center,
            .Child = New TextBlock With {
                .Text = "新",
                .FontSize = 10,
                .FontWeight = FontWeights.SemiBold
            }
        }
        Badge.SetResourceReference(Border.BackgroundProperty, "ColorBrush8")
        CType(Badge.Child, TextBlock).SetResourceReference(TextBlock.ForegroundProperty, "ColorBrush2")
        Return Badge
    End Function

    Private Function CreateItemMeta(Item As SduDownloadItem) As StackPanel
        Dim Meta As New StackPanel With {.Orientation = Orientation.Horizontal, .Margin = New Thickness(0, 4, 0, 0)}
        If Not String.IsNullOrWhiteSpace(Item.Version) Then
            Dim Version As New TextBlock With {
                .Text = "MC " & Item.Version, .FontSize = 10,
                .MaxWidth = 130, .TextTrimming = TextTrimming.CharacterEllipsis,
                .ToolTip = "Minecraft " & Item.Version
            }
            Version.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrushGray2")
            Dim Chip As New Border With {.CornerRadius = New CornerRadius(3), .Padding = New Thickness(5, 1, 5, 1), .Child = Version, .Margin = New Thickness(0, 0, 8, 0)}
            Chip.SetResourceReference(Border.BackgroundProperty, "ColorBrushGray8")
            Meta.Children.Add(Chip)
        End If
        If Not String.IsNullOrWhiteSpace(Item.PublishDate) Then
            Dim Published As New TextBlock With {.Text = Item.PublishDate, .FontSize = 10.5, .VerticalAlignment = VerticalAlignment.Center}
            Published.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrushGray2")
            Meta.Children.Add(Published)
        End If
        Return Meta
    End Function

    Private Function CreateItemIntro(Item As SduDownloadItem) As TextBlock
        Dim Summary = System.Text.RegularExpressions.Regex.Replace(Item.Intro, "\s+", " ").Trim()
        Dim Heading = System.Text.RegularExpressions.Regex.Match(Summary, "^【(?<title>[^】]+)】\s*")
        If Heading.Success AndAlso Heading.Length < Summary.Length Then
            Dim HeadingText = Heading.Groups("title").Value
            If HeadingText.IndexOf("Minecraft", StringComparison.OrdinalIgnoreCase) >= 0 OrElse
               HeadingText.IndexOf("SDUcraft", StringComparison.OrdinalIgnoreCase) >= 0 OrElse
               (Item.Name <> "" AndAlso HeadingText.Contains(Item.Name)) Then
                Summary = Summary.Substring(Heading.Length)
            End If
        End If
        Dim Intro As New TextBlock With {
            .Text = Summary,
            .FontSize = 11.5,
            .TextTrimming = TextTrimming.CharacterEllipsis,
            .Margin = New Thickness(0, 4, 0, 0)
        }
        Intro.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrushGray2")
        Return Intro
    End Function

    Private Sub SduDownloadJumpClick(Sender As Object, EventArgs As RoutedEventArgs)
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
                If TypeOf ex Is SduInstallReportedException Then
                    Logger.Warn(ex, "SDUcraft 整合包安装结果已提示")
                Else
                    Logger.Error(ex, "SDUcraft 整合包一键安装失败", LogBehavior.Alert)
                End If
            Finally
                RunInUi(Sub() _SduDownloadInstalling = False)
            End Try
        End Sub)
    End Sub

    Private Sub MarkSduDownloadItemRead(ItemBorder As Border, ItemId As String)
        If String.IsNullOrEmpty(ItemId) Then Return
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

        Dim Badge = TitleGrid.Children.OfType(Of Border)().FirstOrDefault(Function(Child) Equals(Child.Tag, "SduUnreadBadge"))
        If Badge IsNot Nothing Then Badge.Visibility = Visibility.Collapsed
    End Sub

#End Region

End Class
