Imports System.Windows
Imports System.Windows.Controls
Imports System.Windows.Input
Imports System.Windows.Threading
Imports System.Runtime.CompilerServices

Module SduScrollTests
    Private Passed As Integer

    <STAThread>
    Sub Main(Arguments As String())
        Try
            Dim App As New Application()
            Using ResourceStream = IO.File.OpenRead(Arguments(0))
                App.Resources = CType(Markup.XamlReader.Load(ResourceStream), ResourceDictionary)
            End Using
            For Each CustomInner In {False, True}
                Dim Kind = If(CustomInner, "custom", "standard")
                Run(Kind & " inner viewer receives wheel before outer page", Sub()
                    Dim F As New Fixture(CustomInner)
                    F.Wheel(-120)
                    Check(F.Inner.VerticalOffset > 0, "Inner list did not scroll")
                    Check(F.Outer.VerticalOffset = 0, "Outer page consumed inner list wheel")
                End Sub)
                Run(Kind & " bottom boundary hands wheel to page", Sub()
                    Dim F As New Fixture(CustomInner)
                    F.Inner.ScrollToEnd()
                    F.Pump()
                    F.Wheel(-120)
                    Check(F.Outer.VerticalOffset > 0, "Bottom boundary swallowed page wheel")
                End Sub)
                Run(Kind & " top boundary hands reverse wheel to page", Sub()
                    Dim F As New Fixture(CustomInner)
                    F.Outer.ScrollToVerticalOffset(150)
                    F.Pump()
                    Dim Before = F.Outer.VerticalOffset
                    F.Wheel(120)
                    Check(F.Outer.VerticalOffset < Before, "Top boundary swallowed reverse wheel")
                    Check(F.Inner.VerticalOffset = 0, "Top boundary moved the inner viewer")
                End Sub)
            Next
            Run("wheel over page content scrolls page", Sub()
                Dim F As New Fixture(False)
                SendWheel(F.Outside, -120)
                F.Pump()
                Check(F.Outer.VerticalOffset > 0, "Page content wheel did not move page")
            End Sub)
            Run("inner viewer scrolls when page has no overflow", Sub()
                Dim F As New Fixture(False, False)
                F.Wheel(-120)
                Check(F.Inner.VerticalOffset > 0 AndAlso F.Outer.VerticalOffset = 0, "Inner viewer depends on page overflow")
            End Sub)
            Run("multiline editor retains wheel", Sub()
                Dim F As New Fixture(False)
                Dim Editor As New TextBox With {.Height = 100, .AcceptsReturn = True, .Text = String.Join(vbCrLf, Enumerable.Repeat("editor line", 50)), .VerticalScrollBarVisibility = ScrollBarVisibility.Auto}
                F.Inner.Content = Editor
                F.Pump()
                SendWheel(Editor, -120)
                F.Pump()
                Check(F.Outer.VerticalOffset = 0, "Page captured multiline editor wheel")
            End Sub)
            Run("inline text inside inner viewer retains routing", Sub()
                Dim F As New Fixture(False)
                Dim RunText As New Documents.Run("inline content")
                F.Source.Inlines.Clear()
                F.Source.Inlines.Add(RunText)
                SendWheel(RunText, -120)
                F.Pump()
                Check(F.Inner.VerticalOffset > 0 AndAlso F.Outer.VerticalOffset = 0, "ContentElement routing failed")
            End Sub)
            Run("zero wheel delta changes neither viewer", Sub()
                Dim F As New Fixture(False)
                F.Wheel(0)
                Check(F.Inner.VerticalOffset = 0 AndAlso F.Outer.VerticalOffset = 0, "Zero delta moved content")
            End Sub)
            Console.WriteLine("PASS: " & Passed & " real WPF wheel routing scenarios (production MyScrollViewer).")
            PageLaunchRight.RunPanelTests()
        Catch ex As Exception
            Console.Error.WriteLine(ex)
            Environment.ExitCode = 1
        End Try
    End Sub

    Private Class Fixture
        Public ReadOnly Outer As New MyScrollViewer With {.Width = 340, .Height = 220, .VerticalScrollBarVisibility = ScrollBarVisibility.Auto}
        Public ReadOnly Inner As ScrollViewer
        Public ReadOnly Source As New TextBlock With {.Text = "download item", .Height = 600}
        Public ReadOnly Outside As New TextBlock With {.Text = "page content", .Height = 40}

        Sub New(CustomInner As Boolean, Optional PageOverflow As Boolean = True)
            Inner = If(CustomInner, CType(New MyScrollViewer(), ScrollViewer), New ScrollViewer())
            Inner.Height = 120
            If Not CustomInner Then Inner.SetResourceReference(FrameworkElement.StyleProperty, "ScrollViewerNoMargin")
            Inner.VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            Inner.Content = Source
            Dim Content As New StackPanel()
            Content.Children.Add(Outside)
            Content.Children.Add(Inner)
            If PageOverflow Then Content.Children.Add(New Border With {.Height = 500})
            Outer.Content = Content
            Pump()
            Check(Inner.ScrollableHeight > 0, "Invalid fixture: inner content fits")
            If PageOverflow Then Check(Outer.ScrollableHeight > 0, "Invalid fixture: page content fits")
        End Sub

        Public Sub Wheel(Delta As Integer)
            SendWheel(Source, Delta)
            Pump()
        End Sub

        Public Sub Pump()
            Outer.Measure(New Size(340, 220))
            Outer.Arrange(New Rect(0, 0, 340, 220))
            Outer.UpdateLayout()
            Dim Frame As New DispatcherFrame()
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, New Action(Sub() Frame.Continue = False))
            Dispatcher.PushFrame(Frame)
            Outer.UpdateLayout()
        End Sub
    End Class

    'Use WPF's real tunnel/bubble event routes, including the default ScrollViewer wheel handler.
    Private Sub SendWheel(Source As DependencyObject, Delta As Integer)
        Dim Args As New MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, Delta) With {.RoutedEvent = Mouse.PreviewMouseWheelEvent}
        RaiseWheel(Source, Args)
        If Args.Handled Then Return
        Args.RoutedEvent = Mouse.MouseWheelEvent
        RaiseWheel(Source, Args)
    End Sub
    Private Sub RaiseWheel(Source As DependencyObject, Args As MouseWheelEventArgs)
        If TypeOf Source Is UIElement Then
            CType(Source, UIElement).RaiseEvent(Args)
        Else
            CType(Source, ContentElement).RaiseEvent(Args)
        End If
    End Sub
    Private Sub Check(Condition As Boolean, Message As String)
        If Not Condition Then Throw New Exception(Message)
    End Sub
    Private Sub Run(Name As String, Work As Action)
        Work()
        Passed += 1
        Console.WriteLine("PASS: " & Name)
    End Sub
End Module

'Only host animation and unrelated UI services are substituted. Scrolling, viewport
'measurement, visual/logical trees and input routing use the actual WPF runtime.
Public Class MyScrollBar
    Inherits Primitives.ScrollBar
End Class
Public Class MySlider
    Inherits Slider
End Class
Public Class AniEaseOutFluent
    Sub New(Power As Integer)
    End Sub
End Class
Public Class TestWindow
    Public BtnExtraBack As New TestButton()
End Class
Public Class TestButton
    Public Sub ShowRefresh()
    End Sub
End Class
Public Module ScrollTestHost
    Public FrmMain As New TestWindow()
    Public Function AaDouble(Apply As Action(Of Double), Delta As Double, Duration As Integer, Optional Delay As Integer = 0, Optional Ease As Object = Nothing) As Action
        Return Sub() Apply(Delta)
    End Function
    Public Sub AniStart(Work As Action)
        Work()
    End Sub
    <Extension>
    Public Function Clamp(Value As Double, Minimum As Double, Maximum As Double) As Double
        Return Math.Max(Minimum, Math.Min(Maximum, Value))
    End Function
End Module
