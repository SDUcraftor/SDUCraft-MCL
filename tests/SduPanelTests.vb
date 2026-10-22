Imports System.Windows.Threading
Imports System.Runtime.CompilerServices

'Compile the entire production panel. Only network, install and host services are substituted.
Partial Public Class PageLaunchRight
    Public PanCustom As New StackPanel()

    Public Shared Sub RunPanelTests()
        Dim Page As New PageLaunchRight()
        Page._SduDownloadItems = Enumerable.Range(1, 29).Select(Function(Index) New SduDownloadItem With {
            .Id = Index.ToString(), .Name = "Pack " & Index, .Version = "1.21.1", .IntroHtml = "Offline regression fixture"}).ToList()
        Page.RenderSduDownloadPanel()
        Dim Root As New Border With {.Width = 510, .Child = Page.PanCustom}
        Pump(Root)
        For Index = 1 To 3
            Pump(Root)
            Check(Page._SduDownloadVisibleCount = 5, "Layout auto-loaded more than five items")
        Next
        Dim Counts As New List(Of Integer) From {5}
        While Page._SduDownloadVisibleCount < 29
            Dim Expected = Math.Min(29, Page._SduDownloadVisibleCount + 3)
            Page._SduDownloadScroll.ScrollToEnd()
            Pump(Root)
            Check(Page._SduDownloadVisibleCount = Expected, "Continuous end scrolling stalled or loaded multiple pages")
            Counts.Add(Page._SduDownloadVisibleCount)
        End While
        Check(Page._SduDownloadMoreButton.Visibility = Visibility.Collapsed, "Last page still offers load more")
        Console.WriteLine("PASS: production panel continuous paging " & String.Join(" -> ", Counts))

        Page._SduDownloadVisibleCount = 0
        Page.RenderSduDownloadPanel()
        Dim Outer As New MyScrollViewer With {.Width = 510, .Height = 520}
        Root.Child = Nothing
        Dim Content As New StackPanel()
        Content.Children.Add(Page.PanCustom)
        Content.Children.Add(New Border With {.Height = 600})
        Outer.Content = Content
        Root.Child = Outer
        Pump(Root)
        For Index = 1 To 150
            Dim Source = CType(Page._SduDownloadStack.Children(0), UIElement)
            Dim Args As New MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, -120) With {.RoutedEvent = Mouse.PreviewMouseWheelEvent}
            Source.RaiseEvent(Args)
            If Not Args.Handled Then
                Args.RoutedEvent = Mouse.MouseWheelEvent
                Source.RaiseEvent(Args)
            End If
            Pump(Root)
            If Page._SduDownloadVisibleCount = 29 Then Exit For
        Next
        Check(Page._SduDownloadVisibleCount = 29, "Continuous wheel scrolling stalled")
        Console.WriteLine("PASS: production panel wheel loads all pages inside production MyScrollViewer")

        Page = New PageLaunchRight()
        PendingWork.Clear()
        RequestCount = 0
        Response = "[{""id"":""1"",""name"":""first""}]"
        Page.RefreshSduDownloadPanel()
        Check(PendingWork.Count = 1, "Initial load was not queued")
        PendingWork.Dequeue().Invoke()
        Check(RequestCount = 1 AndAlso Page._SduDownloadItems(0).Name = "first", "Initial response not rendered")
        Page.RefreshSduDownloadPanel()
        Check(PendingWork.Count = 0, "Initialization refetched a visible panel")
        Response = "[{""id"":""2"",""name"":""updated""}]"
        Page.RefreshSduDownloadPanel(Force:=True)
        Page.RefreshSduDownloadPanel(Force:=True)
        Page.RefreshSduDownloadPanel(Force:=True)
        Check(PendingWork.Count = 1, "Force refresh started concurrent requests")
        PendingWork.Dequeue().Invoke()
        Check(PendingWork.Count = 1, "Refresh requested during load was lost")
        PendingWork.Dequeue().Invoke()
        Check(RequestCount = 3 AndAlso Page._SduDownloadItems(0).Name = "updated", "Forced refresh did not replace stale data")
        Check(Page.PanCustom.Children.Count = 1, "Refresh duplicated the panel")
        Check(Not Page._SduDownloadIsLoading, "Refresh left loading state locked")
        Console.WriteLine("PASS: forced refresh replaces data and coalesces requests while loading")

        Response = "invalid JSON"
        Page.RefreshSduDownloadPanel(Force:=True)
        PendingWork.Dequeue().Invoke()
        Check(Page.PanCustom.Children.Count = 1 AndAlso Page.PanCustom.Children(0) Is Page._SduDownloadCard, "Error card ownership lost")
        Response = "[]"
        Page.RefreshSduDownloadPanel(Force:=True)
        PendingWork.Dequeue().Invoke()
        Check(Page._SduDownloadItems.Count = 0 AndAlso Page.PanCustom.Children.Count = 1, "Refresh failed to recover from network error")
        Console.WriteLine("PASS: failed refresh recovers without duplicate or orphaned error cards")
    End Sub

    Private Shared Sub Pump(Root As FrameworkElement)
        Root.Measure(New Size(510, Double.PositiveInfinity))
        Root.Arrange(New Rect(0, 0, 510, Root.DesiredSize.Height))
        Root.UpdateLayout()
        Dim Frame As New DispatcherFrame()
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, New Action(Sub() Frame.Continue = False))
        Dispatcher.PushFrame(Frame)
        Root.UpdateLayout()
    End Sub
    Private Shared Sub Check(Condition As Boolean, Message As String)
        If Not Condition Then Throw New Exception(Message)
    End Sub
End Class

Public Class MyCard
    Inherits Grid
    Public Property Title As String
End Class
Public Class SduGameDownload
    Public Property Id As String
End Class
Public Class SduModpackService
    Public Shared Function FetchModpacks() As List(Of SduGameDownload)
        Throw New InvalidOperationException("Tests must not fetch install URLs")
    End Function
End Class
Public Class SduModpackInstaller
    Public Shared Sub StartInstall(Item As SduGameDownload)
        Throw New InvalidOperationException("Tests must not install packages")
    End Sub
End Class
Public Class SduDownloadReadStore
    Public Shared Function ReadIds() As HashSet(Of String)
        Return New HashSet(Of String)()
    End Function
    Public Shared Sub SaveIds(Ids As HashSet(Of String))
    End Sub
End Class
Public Enum LogBehavior
    Alert
End Enum
Public Enum HintType
    Red
    Blue
End Enum
Public Class Logger
    Public Shared Sub Warn(ErrorValue As Exception, Message As String)
    End Sub
    Public Shared Sub [Error](ErrorValue As Exception, Message As String, Behavior As LogBehavior)
        Throw New InvalidOperationException("Unexpected alert: " & Message, ErrorValue)
    End Sub
End Class
Public Module PanelTestHost
    Public PendingWork As New Queue(Of Action)()
    Public RequestCount As Integer
    Public Response As String
    Public RestrictMcDownload As Boolean = True
    Public Sub RunInThread(Work As Action)
        PendingWork.Enqueue(Work)
    End Sub
    Public Sub RunInUi(Work As Action, Optional Wait As Boolean = False)
        Work()
    End Sub
    Public Function NetRequestByClientRetry(Url As String, Optional RequireJson As Boolean = False) As String
        RequestCount += 1
        Return Response
    End Function
    Public Sub Hint(Message As String, Kind As HintType)
    End Sub
    Public Sub OpenWebsite(Url As String)
        Throw New InvalidOperationException("Tests must not open a browser")
    End Sub
    <Extension>
    Public Sub ThrowIfCanceled(ErrorValue As Exception)
        If TypeOf ErrorValue Is OperationCanceledException Then Throw ErrorValue
    End Sub
End Module
