Imports System.Windows.Controls

''' <summary>
''' SDUcraft 官网整合包下载页。
''' 页面只负责展示、筛选和触发，安装交给 SduModpackInstaller。
''' </summary>
Public Class PageDownloadSdu
    Inherits MyPageRight
    Implements IRefreshable

    Private ReadOnly PanBack As New MyScrollViewer With {
        .VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        .HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
    }
    Private ReadOnly PanMain As New StackPanel With {.Margin = New Thickness(25, 25, 25, 20)}

    Private ReadOnly TextFilter As New MyTextBox With {
        .HintText = "搜索名称、版本或作者",
        .Margin = New Thickness(0, 0, 12, 0),
        .MinHeight = 32,
        .VerticalContentAlignment = VerticalAlignment.Center
    }
    Private ReadOnly BtnRefresh As New MyButton With {
        .Text = "刷新",
        .Padding = New Thickness(14, 5, 14, 5),
        .MinWidth = 72,
        .ColorType = MyButton.ColorState.Highlight
    }
    Private ReadOnly LabTarget As New TextBlock With {
        .FontSize = 10.5,
        .TextTrimming = TextTrimming.CharacterEllipsis
    }
    Private ReadOnly LabStatus As New TextBlock With {
        .FontSize = 11,
        .TextWrapping = TextWrapping.Wrap,
        .Margin = New Thickness(0, 0, 0, 10)
    }
    Private ReadOnly PanList As New StackPanel

    Private SduDownloadItems As List(Of SduGameDownload)
    Private SduDownloadLoaded As Boolean
    Private SduDownloadLoading As Boolean
    Private SduDownloadInstalling As Boolean

    Public Sub New()
        PanScroll = "PanBack"
        PanBack.Content = PanMain
        Child = PanBack
        '页面完全由代码构造，需要显式注册滚动容器；否则标题栏的返回顶部逻辑会查到主窗口的同名控件。
        NameScope.SetNameScope(Me, New NameScope())
        RegisterName("PanBack", PanBack)
        BuildUi()
    End Sub

    Private Sub PageLoaded() Handles Me.Loaded
        Refresh()
    End Sub

    Public Sub Refresh() Implements IRefreshable.Refresh
        If SduDownloadLoading Then Return

        SduDownloadLoading = True
        LabTarget.Text = $"安装位置：{McFolderSelected}"
        LabTarget.ToolTip = McFolderSelected
        LabStatus.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrushGray2")
        LabStatus.Text = "正在读取 SDUcraft 整合包列表……"
        PanList.Children.Clear()
        RunInNewThread(
            Sub()
                Try
                    SduDownloadItems = SduModpackService.FetchModpacks()
                    SduDownloadLoaded = True
                    RunInUi(
                        Sub()
                            SduDownloadLoading = False
                            RenderSduDownloadItems()
                        End Sub, True)
                Catch ex As Exception
                    SduDownloadLoading = False
                    Logger.Warn(ex, "加载 SDUcraft 整合包列表失败")
                    RunInUi(
                        Sub()
                            LabStatus.Text = "读取失败：无法连接到 SDUcraft 下载服务，请稍后再试。"
                            LabStatus.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrushRedDark")
                        End Sub, True)
                End Try
            End Sub, "加载 SDUcraft 整合包列表", ThreadPriority.BelowNormal)
    End Sub

    Private Sub BuildUi()
        Dim Header As New MyCard With {.Title = "SDUcraft 整合包", .Margin = New Thickness(0, 0, 0, 15)}
        Dim HeaderPanel As New StackPanel With {.Margin = New Thickness(16, 36, 16, 14)}

        Dim HeaderText As New TextBlock With {
            .TextWrapping = TextWrapping.Wrap,
            .FontSize = 12,
            .Text = "选择整合包即可下载并安装，进度可在任务管理中查看。"
        }
        HeaderText.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrushGray2")
        Dim SearchPanel As New Grid With {.Margin = New Thickness(0, 12, 0, 0)}
        SearchPanel.ColumnDefinitions.Add(New ColumnDefinition With {.Width = New GridLength(1, GridUnitType.Star)})
        SearchPanel.ColumnDefinitions.Add(New ColumnDefinition With {.Width = GridLength.Auto})
        Grid.SetColumn(TextFilter, 0)
        Grid.SetColumn(BtnRefresh, 1)
        SearchPanel.Children.Add(TextFilter)
        SearchPanel.Children.Add(BtnRefresh)
        AddHandler TextFilter.TextChanged, AddressOf TextFilter_Changed
        AddHandler BtnRefresh.Click, AddressOf BtnRefresh_Click
        LabTarget.Margin = New Thickness(0, 10, 0, 0)
        LabTarget.Text = $"安装位置：{McFolderSelected}"
        LabTarget.ToolTip = McFolderSelected
        LabTarget.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrushGray2")
        HeaderPanel.Children.Add(HeaderText)
        HeaderPanel.Children.Add(SearchPanel)
        HeaderPanel.Children.Add(LabTarget)
        Header.Children.Add(HeaderPanel)

        Dim ListCard As New MyCard With {.Title = "可安装整合包", .Margin = New Thickness(0, 0, 0, 15)}
        Dim ListContent As New StackPanel With {.Margin = New Thickness(16, 36, 16, 14)}
        LabStatus.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrushGray2")
        ListContent.Children.Add(LabStatus)
        ListContent.Children.Add(PanList)
        ListCard.Children.Add(ListContent)

        PanMain.Children.Add(Header)
        PanMain.Children.Add(ListCard)
    End Sub

    Private Sub TextFilter_Changed(Sender As Object, EventArgs As TextChangedEventArgs)
        RenderSduDownloadItems()
    End Sub

    Private Sub BtnRefresh_Click(Sender As Object, EventArgs As MouseButtonEventArgs)
        Refresh()
    End Sub

    Private Sub RenderSduDownloadItems()
        If SduDownloadLoading OrElse SduDownloadItems Is Nothing Then Return

        Dim Keyword = TextFilter.Text.Trim()
        Dim Available = SduDownloadItems.Where(Function(Item) Item.PreferredFile IsNot Nothing).ToList()
        Dim Matched = Available.Where(
            Function(Item)
                If Keyword = "" Then Return True
                Return Item.Name.IndexOf(Keyword, StringComparison.OrdinalIgnoreCase) >= 0 OrElse
                       Item.Version.IndexOf(Keyword, StringComparison.OrdinalIgnoreCase) >= 0 OrElse
                       Item.Author.IndexOf(Keyword, StringComparison.OrdinalIgnoreCase) >= 0
            End Function).ToList()

        PanList.Children.Clear()
        LabStatus.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrushGray2")
        LabStatus.Text = If(Keyword = "", $"共 {Matched.Count} 个整合包", $"找到 {Matched.Count} / {Available.Count} 个整合包")

        For Each Item In Matched
            Dim DownloadFile = Item.PreferredFile
            If DownloadFile Is Nothing Then Continue For
            PanList.Children.Add(CreateSduItem(Item, DownloadFile))
        Next

        If PanList.Children.Count = 0 Then
            LabStatus.Text = If(Available.Count = 0,
                                "SDUcraft 官网当前没有可安装整合包。",
                                "没有符合筛选条件的可安装整合包。")
            PanList.Children.Add(CreateSduEmptyState(If(Available.Count = 0, "暂无整合包", "没有找到相关整合包"),
                If(Available.Count = 0, "稍后刷新，看看有没有新内容。", "换个名称、版本或作者试试。")))
        End If
    End Sub

    Private Function CreateSduItem(Item As SduGameDownload, DownloadFile As SduGameDownloadFile) As Button
        Dim Intro = RemoveSduHtml(Item.IntroHtml)
        Dim Content As New StackPanel()
        Dim Heading As New Grid()
        Heading.ColumnDefinitions.Add(New ColumnDefinition With {.Width = New GridLength(1, GridUnitType.Star)})
        Heading.ColumnDefinitions.Add(New ColumnDefinition With {.Width = GridLength.Auto})
        Dim Title As New TextBlock With {
            .Text = Item.Name, .FontSize = 13.5, .FontWeight = FontWeights.SemiBold,
            .TextTrimming = TextTrimming.CharacterEllipsis, .Margin = New Thickness(0, 0, 12, 0),
            .VerticalAlignment = VerticalAlignment.Center
        }
        Title.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrush1")
        Dim Download As New TextBlock With {.Text = "下载 ↓", .FontSize = 11, .VerticalAlignment = VerticalAlignment.Center}
        Download.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrush3")
        Grid.SetColumn(Download, 1)
        Heading.Children.Add(Title)
        Heading.Children.Add(Download)
        Content.Children.Add(Heading)

        '每项元数据独立排版，窄窗口自动换行，不再挤入 MyListItem 的单行 Info。
        Dim Meta As New WrapPanel With {.Margin = New Thickness(0, 6, 0, 0)}
        Dim Version = CreateSduMetaText(If(Item.Version = "", "版本未注明", "MC " & Item.Version))
        Version.Margin = New Thickness(0)
        Version.MaxWidth = 150
        Version.ToolTip = If(Item.Version = "", "官网尚未注明游戏版本", "Minecraft " & Item.Version)
        Dim Chip As New Border With {.CornerRadius = New CornerRadius(3), .Padding = New Thickness(5, 1, 5, 1), .Margin = New Thickness(0, 0, 10, 3), .Child = Version}
        Chip.SetResourceReference(Border.BackgroundProperty, "ColorBrushGray8")
        Meta.Children.Add(Chip)
        Meta.Children.Add(CreateSduMetaText(If(DownloadFile.SizeInMegabytes > 0, $"{DownloadFile.SizeInMegabytes:0.##} MB", "大小未知")))
        Meta.Children.Add(CreateSduMetaText(DownloadFile.OperatingSystem))
        If Item.PublishDate <> "" Then Meta.Children.Add(CreateSduMetaText("发布 " & Item.PublishDate))
        If Item.Author <> "" Then
            Dim Author = CreateSduMetaText("作者 " & Item.Author)
            Author.MaxWidth = 160
            Author.ToolTip = Item.Author
            Meta.Children.Add(Author)
        End If
        Content.Children.Add(Meta)

        If Intro <> "" Then
            Dim Summary = System.Text.RegularExpressions.Regex.Match(Intro, "^【(?<title>[^】]+)】\s*")
            If Summary.Success AndAlso Summary.Length < Intro.Length Then
                Dim Prefix = Summary.Groups("title").Value
                If Prefix.IndexOf("Minecraft", StringComparison.OrdinalIgnoreCase) >= 0 OrElse
                   Prefix.IndexOf("SDUcraft", StringComparison.OrdinalIgnoreCase) >= 0 OrElse
                   (Item.Name <> "" AndAlso Prefix.Contains(Item.Name)) Then Intro = Intro.Substring(Summary.Length)
            End If
            Dim Description As New TextBlock With {.Text = Intro, .FontSize = 11.5, .Margin = New Thickness(0, 3, 0, 0), .TextTrimming = TextTrimming.CharacterEllipsis}
            Description.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrushGray2")
            Content.Children.Add(Description)
        End If

        Dim Card As New Button With {
            .Tag = Item, .Content = Content, .Padding = New Thickness(12, 10, 12, 10),
            .BorderThickness = New Thickness(1), .Margin = New Thickness(0, 0, 0, 8),
            .HorizontalContentAlignment = HorizontalAlignment.Stretch, .Cursor = Cursors.Hand
        }
        Automation.AutomationProperties.SetName(Card, $"下载并安装 {Item.Name}")
        Card.ToolTip = New ToolTip With {.MaxWidth = 380, .Content = New TextBlock With {
            .Text = Item.Name & If(Item.Author = "", "", vbCrLf & "作者：" & Item.Author) & If(Item.IntroHtml = "", "", vbCrLf & RemoveSduHtml(Item.IntroHtml)),
            .MaxHeight = 200, .TextWrapping = TextWrapping.Wrap}}
        Card.Style = CreateSduItemStyle()
        AddHandler Card.Click, AddressOf SduItemClick
        Return Card
    End Function

    Private Function CreateSduMetaText(Text As String) As TextBlock
        Dim Label As New TextBlock With {
            .Text = Text, .FontSize = 10.5, .Margin = New Thickness(0, 0, 10, 3),
            .VerticalAlignment = VerticalAlignment.Center, .TextTrimming = TextTrimming.CharacterEllipsis
        }
        Label.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrushGray2")
        Return Label
    End Function

    Private Function CreateSduItemStyle() As Style
        Dim Style As New Style(GetType(Button))
        Style.Setters.Add(New Setter(Control.BackgroundProperty, New DynamicResourceExtension("ColorBrushWhite")))
        Style.Setters.Add(New Setter(Control.BorderBrushProperty, New DynamicResourceExtension("ColorBrushGray6")))
        Dim Chrome As New FrameworkElementFactory(GetType(Border))
        Chrome.SetValue(Border.CornerRadiusProperty, New CornerRadius(6))
        For Each Pair In {Tuple.Create(Border.BackgroundProperty, "Background"), Tuple.Create(Border.BorderBrushProperty, "BorderBrush"),
                          Tuple.Create(Border.BorderThicknessProperty, "BorderThickness"), Tuple.Create(Border.PaddingProperty, "Padding")}
            Chrome.SetBinding(Pair.Item1, New Binding(Pair.Item2) With {.RelativeSource = RelativeSource.TemplatedParent})
        Next
        Dim Presenter As New FrameworkElementFactory(GetType(ContentPresenter))
        Presenter.SetBinding(ContentPresenter.ContentProperty, New Binding("Content") With {.RelativeSource = RelativeSource.TemplatedParent})
        Chrome.AppendChild(Presenter)
        Style.Setters.Add(New Setter(Control.TemplateProperty, New ControlTemplate(GetType(Button)) With {.VisualTree = Chrome}))
        For Each PropertyValue In {UIElement.IsMouseOverProperty, UIElement.IsKeyboardFocusWithinProperty}
            Dim Highlight As New Trigger With {.Property = PropertyValue, .Value = True}
            Highlight.Setters.Add(New Setter(Control.BackgroundProperty, New DynamicResourceExtension("ColorBrush8")))
            Highlight.Setters.Add(New Setter(Control.BorderBrushProperty, New DynamicResourceExtension("ColorBrush5")))
            Style.Triggers.Add(Highlight)
        Next
        Return Style
    End Function

    Private Function CreateSduEmptyState(Title As String, Message As String) As Border
        Dim Text As New StackPanel()
        Dim Heading As New TextBlock With {.Text = Title, .FontSize = 13, .FontWeight = FontWeights.SemiBold, .TextAlignment = TextAlignment.Center, .TextWrapping = TextWrapping.Wrap}
        Heading.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrush1")
        Dim Description As New TextBlock With {.Text = Message, .FontSize = 11.5, .Margin = New Thickness(0, 6, 0, 0), .TextAlignment = TextAlignment.Center, .TextWrapping = TextWrapping.Wrap}
        Description.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrushGray2")
        Text.Children.Add(Heading)
        Text.Children.Add(Description)
        Dim Panel As New Border With {.CornerRadius = New CornerRadius(6), .Padding = New Thickness(16, 24, 16, 24), .Child = Text}
        Panel.SetResourceReference(Border.BackgroundProperty, "ColorBrushGray8")
        Return Panel
    End Function

    Private Sub SduItemClick(Sender As Object, EventArgs As RoutedEventArgs)
        Dim Item = TryCast(TryCast(Sender, Button)?.Tag, SduGameDownload)
        If Item Is Nothing Then Return

        Dim DownloadFile = Item.PreferredFile
        If DownloadFile Is Nothing Then
            Hint("该条目没有可用的 Windows 整合包文件。", HintType.Red)
            Return
        End If
        If SduDownloadInstalling Then
            Hint("已有 SDUcraft 整合包正在下载安装。", HintType.Blue)
            Return
        End If
        If RestrictMcDownload Then
            Hint("当前构建已关闭 MC 下载安装功能。", HintType.Red)
            Return
        End If

        SduDownloadInstalling = True
        RunInNewThread(
            Sub()
                Try
                    SduModpackInstaller.StartInstall(Item)
                Catch ex As Exception
                    ex.ThrowIfCanceled()
                    If TypeOf ex Is SduInstallReportedException Then
                        Logger.Warn(ex, "SDUcraft 整合包安装结果已提示")
                    Else
                        Logger.Error(ex, "SDUcraft 整合包一键安装失败", LogBehavior.Alert)
                    End If
                Finally
                    RunInUi(Sub() SduDownloadInstalling = False)
                End Try
            End Sub, "安装 SDUcraft 整合包")
    End Sub

    Private Function RemoveSduHtml(Text As String) As String
        If String.IsNullOrWhiteSpace(Text) Then Return String.Empty

        Dim PlainText = System.Text.RegularExpressions.Regex.Replace(Text, "<[^>]+>", " ")
        PlainText = PlainText.
            Replace("&nbsp;", " ").
            Replace("&amp;", "&").
            Replace("&lt;", "<").
            Replace("&gt;", ">").
            Replace("&quot;", """")
        Return System.Text.RegularExpressions.Regex.Replace(PlainText, "\s+", " ").Trim()
    End Function

End Class
