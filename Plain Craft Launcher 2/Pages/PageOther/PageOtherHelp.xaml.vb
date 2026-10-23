Public Class PageOtherHelp
    Implements IRefreshable

    Public Sub New()
        InitializeComponent()
    End Sub

    Private Sub PageOther_Loaded(sender As Object, e As RoutedEventArgs) Handles Me.Loaded
        PanBack.ScrollToHome()
    End Sub

    Private Sub OpenHelpButton(sender As Object, e As MouseButtonEventArgs)
        OpenWebsite(CStr(CType(sender, FrameworkElement).Tag))
    End Sub

    Public Sub Refresh() Implements IRefreshable.Refresh
        PanBack.ScrollToHome()
    End Sub

    '保留内置错误说明等上下文帮助的调用入口。
    Public Shared Sub OnItemClick(Entry As HelpEntry)
        Try
            If Entry.IsEvent Then
                CustomEvent.Raise(Entry.EventType, Entry.EventData)
            Else
                EnterHelpPage(Entry)
            End If
        Catch ex As Exception
            Logger.Error(ex, "处理帮助项目点击时发生意外错误")
        End Try
    End Sub
    Public Shared Sub EnterHelpPage(Location As String)
        RunInThread(
        Sub()
            If Not HelpLoader.State = LoadState.Finished Then HelpLoader.WaitForExit(GetUuid)
            Dim Entry As New HelpEntry(Location)
            RunInUi(
            Sub()
                Dim FrmHelpDetail As New PageOtherHelpDetail
                If FrmHelpDetail.Init(Entry) Then
                    FrmMain.PageChange(New FormMain.PageStackData With {.Page = FormMain.PageType.HelpDetail, .Additional = {Entry, FrmHelpDetail}})
                Else
                    Logger.Warn("已取消进入帮助项目，这一般是由于 xaml 初始化失败，且用户在弹窗中手动放弃")
                End If
            End Sub)
        End Sub)
    End Sub
    Public Shared Sub EnterHelpPage(Entry As HelpEntry)
        RunInThread(
        Sub()
            If Not HelpLoader.State = LoadState.Finished Then HelpLoader.WaitForExit(GetUuid)
            RunInUi(
            Sub()
                Dim FrmHelpDetail As New PageOtherHelpDetail
                If FrmHelpDetail.Init(Entry) Then
                    FrmMain.PageChange(New FormMain.PageStackData With {.Page = FormMain.PageType.HelpDetail, .Additional = {Entry, FrmHelpDetail}})
                Else
                    Logger.Warn("已取消进入帮助项目，这一般是由于 xaml 初始化失败，且用户在弹窗中手动放弃")
                End If
            End Sub)
        End Sub)
    End Sub


End Class
