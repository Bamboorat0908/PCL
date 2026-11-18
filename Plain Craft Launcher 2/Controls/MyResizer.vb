Imports System.Runtime.InteropServices
Imports System.Windows.Interop
Imports System.Windows.Threading

Public Class MyResizer

    Private Delegate Sub RefreshDelegate()

    Private Structure POINT
        Public x As Integer
        Public y As Integer
        Public Sub New(x As Integer, y As Integer)
            Me.x = x
            Me.y = y
        End Sub
    End Structure
    Private Structure MINMAXINFO
        Public ptReserved As POINT
        Public ptMaxSize As POINT
        Public ptMaxPosition As POINT
        Public ptMinTrackSize As POINT
        Public ptMaxTrackSize As POINT
    End Structure
    Private Structure RECT
        Public left As Integer
        Public top As Integer
        Public right As Integer
        Public bottom As Integer
        Public Shared Empty As RECT = Nothing

        Public ReadOnly Property Width() As Integer
            Get
                Return Math.Abs(Me.right - Me.left)
            End Get
        End Property
        Public ReadOnly Property Height() As Integer
            Get
                Return Me.bottom - Me.top
            End Get
        End Property

        Public Sub New(left As Integer, top As Integer, right As Integer, bottom As Integer)
            Me.left = left
            Me.top = top
            Me.right = right
            Me.bottom = bottom
        End Sub
        Public Sub New(rcSrc As RECT)
            Me.left = rcSrc.left
            Me.top = rcSrc.top
            Me.right = rcSrc.right
            Me.bottom = rcSrc.bottom
        End Sub
    End Structure

    <StructLayout(LayoutKind.Sequential, CharSet:=CharSet.Auto)>
    Private Class MONITORINFO
        Public cbSize As Integer = Marshal.SizeOf(GetType(MONITORINFO))
        Public rcMonitor As RECT = Nothing
        Public rcWork As RECT = Nothing
        Public dwFlags As Integer = 0
    End Class

    Private Structure PointAPI
        Public X As Integer
        Public Y As Integer
    End Structure

    Private ReadOnly target As Window = Nothing

    ''' <summary>Restore a maximized window only after a drag, retaining the caption grab point.</summary>
    Public Shared Sub DragMaximizedWindow(window As Window, caption As FrameworkElement)
        If window Is Nothing OrElse caption Is Nothing OrElse window.WindowState <> WindowState.Maximized OrElse
            System.Windows.Input.Mouse.LeftButton <> System.Windows.Input.MouseButtonState.Pressed Then Return
        Dim handle = New WindowInteropHelper(window).Handle
        If handle = IntPtr.Zero Then Return
        Dim bounds As RECT
        If Not GetWindowRect(handle, bounds) OrElse bounds.Width <= 0 Then Return
        Dim press = System.Windows.Input.Mouse.GetPosition(caption)
        Dim screen = caption.PointToScreen(press)
        Dim ratio = Math.Max(0, Math.Min(1, (screen.X - bounds.left) / bounds.Width))
        Dim move As System.Windows.Input.MouseEventHandler = Nothing
        Dim up As System.Windows.Input.MouseButtonEventHandler = Nothing
        Dim lost As System.Windows.Input.MouseEventHandler = Nothing
        Dim unloaded As RoutedEventHandler = Nothing
        Dim cancelState As EventHandler = Nothing
        Dim cancelKey As System.Windows.Input.KeyEventHandler = Nothing
        Dim cleanup As Action = Nothing
        cleanup = Sub()
                      RemoveHandler caption.PreviewMouseMove, move
                      RemoveHandler caption.PreviewMouseLeftButtonUp, up
                      RemoveHandler caption.LostMouseCapture, lost
                      RemoveHandler caption.Unloaded, unloaded
                      RemoveHandler window.StateChanged, cancelState
                      RemoveHandler window.Deactivated, cancelState
                      RemoveHandler window.Closed, cancelState
                      RemoveHandler window.PreviewKeyDown, cancelKey
                      If caption.IsMouseCaptured Then caption.ReleaseMouseCapture()
                  End Sub
        move = Sub(sender, e)
                   If e.LeftButton <> System.Windows.Input.MouseButtonState.Pressed Then
                       cleanup()
                       Return
                   End If
                   Dim position = e.GetPosition(caption)
                   If Math.Abs(position.X - press.X) < SystemParameters.MinimumHorizontalDragDistance AndAlso
                       Math.Abs(position.Y - press.Y) < SystemParameters.MinimumVerticalDragDistance Then Return
                   cleanup()
                   e.Handled = True
                   If window.WindowState <> WindowState.Maximized Then Return
                   Try
                       RestoreCaptionAndDrag(window, caption, ratio, press.Y)
                   Catch ex As InvalidOperationException When System.Windows.Input.Mouse.LeftButton <> System.Windows.Input.MouseButtonState.Pressed
                       'The user can finish a quick gesture before the system move loop starts.
                   Catch ex As Exception
                       Logger.Warn(ex, "最大化窗口拖离失败")
                   End Try
               End Sub
        up = Sub(sender, e) cleanup()
        lost = Sub(sender, e) cleanup()
        unloaded = Sub(sender, e) cleanup()
        cancelState = Sub(sender, e) cleanup()
        cancelKey = Sub(sender, e)
                        If e.Key = System.Windows.Input.Key.Escape Then
                            cleanup()
                            e.Handled = True
                        End If
                    End Sub
        AddHandler caption.PreviewMouseMove, move
        AddHandler caption.PreviewMouseLeftButtonUp, up
        AddHandler caption.LostMouseCapture, lost
        AddHandler caption.Unloaded, unloaded
        AddHandler window.StateChanged, cancelState
        AddHandler window.Deactivated, cancelState
        AddHandler window.Closed, cancelState
        AddHandler window.PreviewKeyDown, cancelKey
        If Not caption.CaptureMouse() Then cleanup()
    End Sub

    Private Shared Sub RestoreCaptionAndDrag(window As Window, caption As FrameworkElement, ratio As Double, grabY As Double)
        Dim handle = New WindowInteropHelper(window).Handle
        window.WindowState = WindowState.Normal
        window.UpdateLayout()
        For attempt = 0 To 1
            Dim cursor As PointAPI
            Dim bounds As RECT
            If Not GetCursorPos(cursor) OrElse Not GetWindowRect(handle, bounds) Then Exit For
            Dim dpi = VisualTreeHelper.GetDpi(window)
            'Only native pixels are used for absolute desktop positions. The caption's actual
            'screen offset includes restored borders and the current monitor's DPI.
            Dim offsetY = caption.PointToScreen(New System.Windows.Point(0, grabY)).Y - bounds.top
            SetWindowPosition(handle, IntPtr.Zero, CInt(Math.Round(cursor.X - bounds.Width * ratio)),
                              CInt(Math.Round(cursor.Y - offsetY)), 0, 0, &H15UI) 'NOSIZE | NOZORDER | NOACTIVATE
            window.UpdateLayout()
            Dim currentDpi = VisualTreeHelper.GetDpi(window)
            If Math.Abs(dpi.DpiScaleX - currentDpi.DpiScaleX) < 0.001 AndAlso
                Math.Abs(dpi.DpiScaleY - currentDpi.DpiScaleY) < 0.001 Then Exit For
        Next
        window.DragMove()
    End Sub

    <DllImport("user32.dll", ExactSpelling:=True)>
    Private Shared Function GetWindowRect(handle As IntPtr, ByRef bounds As RECT) As Boolean
    End Function

    <DllImport("user32.dll", EntryPoint:="SetWindowPos", ExactSpelling:=True)>
    Private Shared Function SetWindowPosition(handle As IntPtr, insertAfter As IntPtr, x As Integer, y As Integer,
                                             width As Integer, height As Integer, flags As UInteger) As Boolean
    End Function

    Private resizeRight As Boolean = False
    Private resizeLeft As Boolean = False
    Private resizeUp As Boolean = False
    Private resizeDown As Boolean = False

    Private ReadOnly leftElements As New Dictionary(Of UIElement, Short)()
    Private ReadOnly rightElements As New Dictionary(Of UIElement, Short)()
    Private ReadOnly upElements As New Dictionary(Of UIElement, Short)()
    Private ReadOnly downElements As New Dictionary(Of UIElement, Short)()

    Private startMousePoint As PointAPI = Nothing
    Private startWindowSize As Size = Nothing
    Private startWindowLeftUpPoint As POINT = Nothing
    Private resizeDpiScaleX As Double = 1
    Private resizeDpiScaleY As Double = 1

    Private hs As HwndSource

    Public Sub New(target As Window)
        Me.target = target
        If IsNothing(target) Then Throw New Exception("Invalid Window handle")
        AddHandler target.SourceInitialized, AddressOf Me.MyMacClass_SourceInitialized
        AddHandler target.Closed, AddressOf TargetClosed
        'FormMain creates the resizer in Loaded, after SourceInitialized has fired.
        AttachSourceHook()
    End Sub

    Private Sub MyMacClass_SourceInitialized(sender As Object, e As EventArgs)
        AttachSourceHook()
    End Sub

    Private Sub AttachSourceHook()
        Dim Source = TryCast(PresentationSource.FromVisual(target), HwndSource)
        If Source Is Nothing OrElse Source Is hs Then Return
        If hs IsNot Nothing AndAlso Not hs.IsDisposed Then hs.RemoveHook(AddressOf WndProc)
        hs = Source
        hs.AddHook(AddressOf WndProc)
    End Sub

    Private Sub TargetClosed(sender As Object, e As EventArgs)
        RemoveHandler target.SourceInitialized, AddressOf MyMacClass_SourceInitialized
        RemoveHandler target.Closed, AddressOf TargetClosed
        If hs IsNot Nothing Then
            If Not hs.IsDisposed Then hs.RemoveHook(AddressOf WndProc)
            hs = Nothing
        End If
    End Sub

    Private Function WndProc(hwnd As IntPtr, msg As Integer, wParam As IntPtr, lParam As IntPtr, ByRef handled As Boolean) As IntPtr
        If msg = 36 Then
            WmGetMinMaxInfo(hwnd, lParam)
            handled = True
        End If
        Return CType(0, IntPtr)
    End Function

    Private Shared Sub WmGetMinMaxInfo(hwnd As IntPtr, lParam As IntPtr)
        Dim mINMAXINFO As MINMAXINFO = CType(Marshal.PtrToStructure(lParam, GetType(MINMAXINFO)), MINMAXINFO)
        Dim flags As Integer = 2
        Dim intPtr As IntPtr = MonitorFromWindow(hwnd, flags)
        Dim flag As Boolean = intPtr <> IntPtr.Zero
        If flag Then
            Dim mONITORINFO As New MONITORINFO()
            If Not GetMonitorInfo(intPtr, mONITORINFO) Then Return
            Dim rcWork As RECT = mONITORINFO.rcWork
            Dim rcMonitor As RECT = mONITORINFO.rcMonitor
            mINMAXINFO.ptMaxPosition.x = Math.Abs(rcWork.left - rcMonitor.left)
            mINMAXINFO.ptMaxPosition.y = Math.Abs(rcWork.top - rcMonitor.top)
            mINMAXINFO.ptMaxSize.x = Math.Abs(rcWork.right - rcWork.left)
            mINMAXINFO.ptMaxSize.y = Math.Abs(rcWork.bottom - rcWork.top)
            If rcWork.Height = rcMonitor.Height Then
                mINMAXINFO.ptMaxSize.y -= 2
            End If
        End If
        Marshal.StructureToPtr(mINMAXINFO, lParam, True)
    End Sub

    <DllImport("user32.dll", EntryPoint:="GetMonitorInfoW", CharSet:=CharSet.Unicode, ExactSpelling:=True, SetLastError:=True)>
    Private Shared Function GetMonitorInfo(hMonitor As IntPtr, <[In], Out> lpmi As MONITORINFO) As Boolean
    End Function

    Private Declare Function MonitorFromWindow Lib "User32" (handle As IntPtr, flags As Integer) As IntPtr

    Private Sub connectMouseHandlers(element As UIElement)
        AddHandler element.MouseLeftButtonDown, AddressOf Me.element_MouseLeftButtonDown
    End Sub

    Public Sub addResizerRight(element As UIElement)
        Me.connectMouseHandlers(element)
        Me.rightElements.Add(element, 0)
    End Sub

    Public Sub addResizerLeft(element As UIElement)
        Me.connectMouseHandlers(element)
        Me.leftElements.Add(element, 0)
    End Sub

    Public Sub addResizerUp(element As UIElement)
        Me.connectMouseHandlers(element)
        Me.upElements.Add(element, 0)
    End Sub

    Public Sub addResizerDown(element As UIElement)
        Me.connectMouseHandlers(element)
        Me.downElements.Add(element, 0)
    End Sub

    Public Sub addResizerRightDown(element As UIElement)
        Me.connectMouseHandlers(element)
        Me.rightElements.Add(element, 0)
        Me.downElements.Add(element, 0)
    End Sub

    Public Sub addResizerLeftDown(element As UIElement)
        Me.connectMouseHandlers(element)
        Me.leftElements.Add(element, 0)
        Me.downElements.Add(element, 0)
    End Sub

    Public Sub addResizerRightUp(element As UIElement)
        Me.connectMouseHandlers(element)
        Me.rightElements.Add(element, 0)
        Me.upElements.Add(element, 0)
    End Sub

    Public Sub addResizerLeftUp(element As UIElement)
        Me.connectMouseHandlers(element)
        Me.leftElements.Add(element, 0)
        Me.upElements.Add(element, 0)
    End Sub

    Private Sub element_MouseLeftButtonDown(sender As Object, e As MouseButtonEventArgs)
        If target.WindowState <> WindowState.Normal Then Return
        GetCursorPos(startMousePoint)
        Dim Dpi = VisualTreeHelper.GetDpi(target)
        resizeDpiScaleX = Dpi.DpiScaleX
        resizeDpiScaleY = Dpi.DpiScaleY
        startMousePoint.X = CInt(startMousePoint.X / Dpi.DpiScaleX)
        startMousePoint.Y = CInt(startMousePoint.Y / Dpi.DpiScaleY)
        startWindowSize = New Size(target.Width, target.Height)
        startWindowLeftUpPoint = New POINT(target.Left, target.Top)
        Dim key As UIElement = CType(sender, UIElement)
        If leftElements.ContainsKey(key) Then resizeLeft = True
        If rightElements.ContainsKey(key) Then resizeRight = True
        If upElements.ContainsKey(key) Then resizeUp = True
        If downElements.ContainsKey(key) Then resizeDown = True
        RunInNewThread(AddressOf updateSizeLoop, "窗口大小调整检测")
    End Sub

    Private Sub updateSizeLoop()
        Try
            While resizeDown OrElse resizeLeft OrElse resizeRight OrElse resizeUp
                target.Dispatcher.Invoke(AddressOf updateSize, DispatcherPriority.Render)
                target.Dispatcher.Invoke(AddressOf updateMouseDown, DispatcherPriority.Render)
                Thread.Sleep(0)
            End While
        Catch
        End Try
    End Sub

    Private Sub updateSize()
        Dim pointAPI As PointAPI = Nothing
        GetCursorPos(pointAPI)
        Dim Dpi = VisualTreeHelper.GetDpi(target)
        If Math.Abs(Dpi.DpiScaleX - resizeDpiScaleX) > 0.0001 OrElse Math.Abs(Dpi.DpiScaleY - resizeDpiScaleY) > 0.0001 Then
            'A resize gesture can cross monitors with different scale factors.
            'Rebase at the boundary instead of subtracting absolute coordinates
            'that were converted using different DPI scales.
            resizeDpiScaleX = Dpi.DpiScaleX
            resizeDpiScaleY = Dpi.DpiScaleY
            startMousePoint = New PointAPI With {.X = CInt(pointAPI.X / resizeDpiScaleX), .Y = CInt(pointAPI.Y / resizeDpiScaleY)}
            startWindowSize = New Size(target.Width, target.Height)
            startWindowLeftUpPoint = New POINT(CInt(target.Left), CInt(target.Top))
            Return
        End If
        pointAPI.X = CInt(pointAPI.X / Dpi.DpiScaleX)
        pointAPI.Y = CInt(pointAPI.Y / Dpi.DpiScaleY)
        Try

            Dim NewWidth As Double = -1
            Dim NewHeight As Double = -1
            Dim NewLeft As Double = -10000
            Dim NewTop As Double = -10000

            If resizeRight Then
                If target.Width = target.MinWidth Then
                    If startMousePoint.X < pointAPI.X Then
                        NewWidth = startWindowSize.Width - (startMousePoint.X - pointAPI.X)
                    End If
                Else
                    If startWindowSize.Width - ((startMousePoint.X - pointAPI.X)) >= target.MinWidth Then
                        NewWidth = startWindowSize.Width - (startMousePoint.X - pointAPI.X)
                    Else
                        NewWidth = target.MinWidth
                    End If
                End If
            End If

            If resizeDown Then
                If target.Height = target.MinHeight Then
                    If startMousePoint.Y < pointAPI.Y Then
                        NewHeight = startWindowSize.Height - (startMousePoint.Y - pointAPI.Y)
                    End If
                Else
                    If startWindowSize.Height - (startMousePoint.Y - pointAPI.Y) >= target.MinHeight Then
                        NewHeight = startWindowSize.Height - (startMousePoint.Y - pointAPI.Y)
                    Else
                        NewHeight = target.MinHeight
                    End If
                End If
            End If

            If resizeLeft Then
                If target.Width = target.MinWidth Then
                    If startMousePoint.X > pointAPI.X Then
                        NewWidth = startWindowSize.Width + startMousePoint.X - pointAPI.X
                        NewLeft = startWindowLeftUpPoint.x - (startMousePoint.X - pointAPI.X)
                    Else
                        NewWidth = target.MinWidth
                        NewLeft = startWindowLeftUpPoint.x + startWindowSize.Width - target.Width
                    End If
                Else
                    If startWindowSize.Width + (startMousePoint.X - pointAPI.X) >= target.MinWidth Then
                        NewWidth = startWindowSize.Width + (startMousePoint.X - pointAPI.X)
                        NewLeft = startWindowLeftUpPoint.x - (startMousePoint.X - pointAPI.X)
                    Else
                        NewWidth = target.MinWidth
                        NewLeft = startWindowLeftUpPoint.x + startWindowSize.Width - target.Width
                    End If
                End If
            End If

            If resizeUp Then
                If target.Height = target.MinHeight Then
                    If startMousePoint.Y > pointAPI.Y Then
                        NewHeight = startWindowSize.Height + startMousePoint.Y - pointAPI.Y
                        NewTop = startWindowLeftUpPoint.y - (startMousePoint.Y - pointAPI.Y)
                    Else
                        NewHeight = target.MinHeight
                        NewTop = startWindowLeftUpPoint.y + startWindowSize.Height - target.Height
                    End If
                Else
                    If startWindowSize.Height + (startMousePoint.Y - pointAPI.Y) >= target.MinHeight Then
                        NewHeight = startWindowSize.Height + startMousePoint.Y - pointAPI.Y
                        NewTop = startWindowLeftUpPoint.y - (startMousePoint.Y - pointAPI.Y)
                    Else
                        NewHeight = target.MinHeight
                        NewTop = startWindowLeftUpPoint.y + startWindowSize.Height - target.Height
                    End If
                End If
            End If

            If NewWidth > 10 AndAlso Math.Abs(NewWidth - target.Width) > 0.7 Then target.Width = NewWidth
            If NewHeight > 10 AndAlso Math.Abs(NewHeight - target.Height) > 0.7 Then target.Height = NewHeight
            If NewLeft > -9999 AndAlso Math.Abs(NewLeft - target.Left) > 0.7 Then target.Left = NewLeft
            If NewTop > -9999 AndAlso Math.Abs(NewTop - target.Top) > 0.7 Then target.Top = NewTop

        Catch
        End Try
    End Sub

    Private Sub updateMouseDown()
        Dim flag = (GetAsyncKeyState(&H1) And &H8000) = 0 '调用原生API判断鼠标是否抬起，如果使用WPF的API的话鼠标不在窗口上时不会更新状态 (#5655)
        If flag Then
            resizeRight = False
            resizeLeft = False
            resizeUp = False
            resizeDown = False
        End If
    End Sub

    Private Declare Function GetCursorPos Lib "user32.dll" (<Out()> ByRef lpPoint As PointAPI) As Boolean
    Private Declare Function GetAsyncKeyState Lib "user32.dll" (vKey As Integer) As Short
End Class
