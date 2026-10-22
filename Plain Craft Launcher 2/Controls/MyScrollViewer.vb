Public Class MyScrollViewer
    Inherits ScrollViewer

    Public Property DeltaMult As Double = 1

    Private RealOffset As Double
    Private Sub MyScrollViewer_PreviewMouseWheel(sender As Object, e As MouseWheelEventArgs) Handles Me.PreviewMouseWheel
        If e.Delta = 0 OrElse ActualHeight = 0 Then Return
        Dim Source = TryCast(e.OriginalSource, DependencyObject)
        If TypeOf Source Is Visual AndAlso
           PresentationSource.FromVisual(CType(Source, Visual))?.RootVisual?.GetType().FullName = "System.Windows.Controls.Primitives.PopupRoot" Then Return

        'Preview 是从外到内的隧道路由。外层必须先让仍可滚动的内层收到事件，
        '不能先标记 Handled，否则主页中的下载列表永远收不到鼠标滚轮。
        While Source IsNot Nothing AndAlso Source IsNot Me
            If TypeOf Source Is ComboBoxItem OrElse
               (TypeOf Source Is ComboBox AndAlso CType(Source, ComboBox).IsDropDownOpen) OrElse
               (TypeOf Source Is TextBox AndAlso CType(Source, TextBox).AcceptsReturn) Then Return
            Dim Nested = TryCast(Source, ScrollViewer)
            If Nested IsNot Nothing AndAlso CanScrollVertically(Nested, e.Delta) Then Return
            Source = GetWheelParent(Source)
        End While
        '内层到达边界后才由当前页面承接；当前页面也到达边界时继续传递。
        If Not CanScrollVertically(Me, e.Delta) Then Return
        e.Handled = True
        PerformVerticalOffsetDelta(-e.Delta)
    End Sub

    Private Shared Function CanScrollVertically(Viewer As ScrollViewer, Delta As Integer) As Boolean
        If Viewer.ScrollableHeight <= 0 Then Return False
        Return If(Delta > 0, Viewer.VerticalOffset > 0.5, Viewer.VerticalOffset < Viewer.ScrollableHeight - 0.5)
    End Function

    Private Shared Function GetWheelParent(Source As DependencyObject) As DependencyObject
        If TypeOf Source Is Visual OrElse TypeOf Source Is System.Windows.Media.Media3D.Visual3D Then
            Dim Parent = VisualTreeHelper.GetParent(Source)
            If Parent IsNot Nothing Then Return Parent
        End If
        If TypeOf Source Is ContentElement Then
            Dim Parent = ContentOperations.GetParent(CType(Source, ContentElement))
            If Parent IsNot Nothing Then Return Parent
            Dim Content = TryCast(Source, FrameworkContentElement)
            If Content IsNot Nothing Then Return Content.Parent
        End If
        Return LogicalTreeHelper.GetParent(Source)
    End Function
    Public Sub PerformVerticalOffsetDelta(Delta As Double)
        AniStart(
            AaDouble(
            Sub(AnimDelta As Double)
                RealOffset = (RealOffset + AnimDelta).Clamp(0, ScrollableHeight)
                ScrollToVerticalOffset(RealOffset)
            End Sub, Delta * DeltaMult, 300,, New AniEaseOutFluent(6)))
    End Sub
    Private Sub MyScrollViewer_ScrollChanged(sender As Object, e As ScrollChangedEventArgs) Handles Me.ScrollChanged
        RealOffset = VerticalOffset
        If FrmMain IsNot Nothing AndAlso (e.VerticalChange OrElse e.ViewportHeightChange) Then FrmMain.BtnExtraBack.ShowRefresh()
    End Sub
    Private Sub MyScrollViewer_IsVisibleChanged(sender As Object, e As DependencyPropertyChangedEventArgs) Handles Me.IsVisibleChanged
        If FrmMain IsNot Nothing Then FrmMain.BtnExtraBack.ShowRefresh()
    End Sub

    Public ScrollBar As MyScrollBar
    Private Sub Load() Handles Me.Loaded
        ScrollBar = GetTemplateChild("PART_VerticalScrollBar")
    End Sub

    Private Sub MyScrollViewer_PreviewGotKeyboardFocus(sender As Object, e As KeyboardFocusChangedEventArgs) Handles Me.PreviewGotKeyboardFocus
        If e.NewFocus IsNot Nothing AndAlso TypeOf e.NewFocus Is MySlider Then e.Handled = True '#3854，阻止获得焦点时自动滚动
    End Sub
End Class
