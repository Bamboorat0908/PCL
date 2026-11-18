Imports System
Imports System.Windows
Imports System.Windows.Media
Imports System.Windows.Media.Animation

''' <summary>
''' Shared, non-interactive workspace artwork. The scene is anchored to the workspace,
''' rather than individual cards, so its lines remain continuous between content areas.
''' Rendering only runs when size, scene or theme brushes change; there is no idle timer.
''' </summary>
Public Class IndustrialBackdrop
    Inherits FrameworkElement

    Public Shared ReadOnly SceneKeyProperty As DependencyProperty = DependencyProperty.Register(
        NameOf(SceneKey), GetType(String), GetType(IndustrialBackdrop),
        New FrameworkPropertyMetadata("PageLaunchRight", FrameworkPropertyMetadataOptions.AffectsRender, AddressOf SceneChanged))
    Public Shared ReadOnly LineBrushProperty As DependencyProperty = DependencyProperty.Register(
        NameOf(LineBrush), GetType(Brush), GetType(IndustrialBackdrop),
        New FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender, AddressOf SceneChanged))
    Public Shared ReadOnly AccentBrushProperty As DependencyProperty = DependencyProperty.Register(
        NameOf(AccentBrush), GetType(Brush), GetType(IndustrialBackdrop),
        New FrameworkPropertyMetadata(Brushes.Gold, FrameworkPropertyMetadataOptions.AffectsRender, AddressOf SceneChanged))
    Public Shared ReadOnly BaseBrushProperty As DependencyProperty = DependencyProperty.Register(
        NameOf(BaseBrush), GetType(Brush), GetType(IndustrialBackdrop),
        New FrameworkPropertyMetadata(Nothing, FrameworkPropertyMetadataOptions.AffectsRender, AddressOf SceneChanged))
    Public Shared ReadOnly IsLightThemeProperty As DependencyProperty = DependencyProperty.Register(
        NameOf(IsLightTheme), GetType(Boolean), GetType(IndustrialBackdrop),
        New FrameworkPropertyMetadata(False, FrameworkPropertyMetadataOptions.AffectsRender, AddressOf ThemeChanged))
    Public Shared ReadOnly TransitionProgressProperty As DependencyProperty = DependencyProperty.Register(
        NameOf(TransitionProgress), GetType(Double), GetType(IndustrialBackdrop),
        New FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender))

    Private PreviousFrame As DrawingGroup
    Private LastFrame As DrawingGroup
    Private CurrentScene As DrawingGroup
    Private TransitionGeneration As Integer

    Public Property IsLightTheme As Boolean
        Get
            Return CBool(GetValue(IsLightThemeProperty))
        End Get
        Set(value As Boolean)
            SetValue(IsLightThemeProperty, value)
        End Set
    End Property
    Public Property TransitionProgress As Double
        Get
            Return CDbl(GetValue(TransitionProgressProperty))
        End Get
        Set(value As Double)
            SetValue(TransitionProgressProperty, value)
        End Set
    End Property

    Public Property SceneKey As String
        Get
            Return CStr(GetValue(SceneKeyProperty))
        End Get
        Set(value As String)
            SetValue(SceneKeyProperty, value)
        End Set
    End Property
    Public Property LineBrush As Brush
        Get
            Return CType(GetValue(LineBrushProperty), Brush)
        End Get
        Set(value As Brush)
            SetValue(LineBrushProperty, value)
        End Set
    End Property
    Public Property AccentBrush As Brush
        Get
            Return CType(GetValue(AccentBrushProperty), Brush)
        End Get
        Set(value As Brush)
            SetValue(AccentBrushProperty, value)
        End Set
    End Property
    Public Property BaseBrush As Brush
        Get
            Return CType(GetValue(BaseBrushProperty), Brush)
        End Get
        Set(value As Brush)
            SetValue(BaseBrushProperty, value)
        End Set
    End Property

    Public Sub New()
        IsHitTestVisible = False
        Focusable = False
        ClipToBounds = True
        SnapsToDevicePixels = False
        AddHandler Unloaded, Sub()
                                 FinishTransition()
                                 LastFrame = Nothing
                                 CurrentScene = Nothing
                             End Sub
    End Sub

    Private Shared Sub SceneChanged(owner As DependencyObject, e As DependencyPropertyChangedEventArgs)
        DirectCast(owner, IndustrialBackdrop).CurrentScene = Nothing
    End Sub

    Private Shared Sub ThemeChanged(owner As DependencyObject, e As DependencyPropertyChangedEventArgs)
        Dim backdrop = DirectCast(owner, IndustrialBackdrop)
        backdrop.CurrentScene = Nothing
        Dim snapshot = backdrop.LastFrame
        backdrop.FinishTransition()
        If snapshot Is Nothing OrElse Not backdrop.IsLoaded OrElse Not UiMotionEnabled Then Return
        backdrop.PreviousFrame = DirectCast(snapshot.GetCurrentValueAsFrozen(), DrawingGroup)
        Dim generation = backdrop.TransitionGeneration
        Dim animation As New DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(340)) With {
            .EasingFunction = New CubicEase With {.EasingMode = EasingMode.EaseInOut},
            .FillBehavior = FillBehavior.Stop}
        AddHandler animation.Completed,
            Sub()
                If generation = backdrop.TransitionGeneration Then backdrop.FinishTransition()
            End Sub
        backdrop.BeginAnimation(TransitionProgressProperty, animation, HandoffBehavior.SnapshotAndReplace)
    End Sub

    Private Sub FinishTransition()
        TransitionGeneration += 1
        BeginAnimation(TransitionProgressProperty, Nothing)
        SetCurrentValue(TransitionProgressProperty, 1.0)
        PreviousFrame = Nothing
    End Sub

    Protected Overrides Sub OnRenderSizeChanged(sizeInfo As SizeChangedInfo)
        'A frozen drawing belongs to its original viewport. Never stretch it during a resize.
        FinishTransition()
        LastFrame = Nothing
        CurrentScene = Nothing
        MyBase.OnRenderSizeChanged(sizeInfo)
    End Sub

    ''' <summary>Every right-hand page resolves without loading or inspecting its content.</summary>
    Public Shared Function ResolveScene(key As String) As String
        Dim name = If(key, "").ToLowerInvariant()
        If name.Contains("news") Then Return "editorial"
        If name.Contains("stock") OrElse name.Contains("trade") OrElse name.Contains("bank") Then Return "market"
        If name.Contains("map") Then Return "cartography"
        If name.Contains("sound") OrElse name.Contains("music") Then Return "signal"
        If name.Contains("sponsor") Then Return "community"
        If name.Contains("brs") OrElse name.Contains("link") Then Return "network"
        If name.Contains("changelog") OrElse name.Contains("about") OrElse name.Contains("help") Then Return "archive"
        If name.Contains("download") OrElse name.Contains("instance") OrElse name.Contains("select") OrElse name.Contains("speed") Then Return "assembly"
        If name.Contains("setup") OrElse name.Contains("setting") Then Return "calibration"
        If name.Contains("launch") OrElse name.Contains("login") Then Return "orbital"
        Return "calibration"
    End Function

    ''' <summary>A deterministic subpage signature, independent of randomized String.GetHashCode.</summary>
    Public Shared Function SceneSignature(key As String) As Integer
        Dim signature As Long = 5381
        For Each character In If(key, "")
            signature = (signature * 33L + Convert.ToInt32(character)) Mod 2147483647L
        Next
        Return CInt(signature)
    End Function

    Protected Overrides Sub OnRender(context As DrawingContext)
        MyBase.OnRender(context)
        Dim w = RenderSize.Width, h = RenderSize.Height
        If w < 1 OrElse h < 1 OrElse Double.IsInfinity(w) OrElse Double.IsInfinity(h) Then Return
        If CurrentScene Is Nothing Then
            Dim scene As New DrawingGroup
            Using painter = scene.Open()
                DrawScene(painter, w, h)
            End Using
            CurrentScene = scene
        End If
        Dim progress = Math.Max(0, Math.Min(1, TransitionProgress))
        If PreviousFrame Is Nothing OrElse progress >= 1 Then
            LastFrame = CurrentScene
            context.DrawDrawing(CurrentScene)
            Return
        End If
        Dim frame As New DrawingGroup
        Using painter = frame.Open()
            painter.DrawDrawing(PreviousFrame)
            painter.PushOpacity(progress)
            painter.DrawDrawing(CurrentScene)
            painter.Pop()
        End Using
        LastFrame = frame
        context.DrawDrawing(frame)
    End Sub

    Private Sub DrawScene(context As DrawingContext, w As Double, h As Double)
        context.PushClip(New RectangleGeometry(New Rect(0, 0, w, h)))
        If BaseBrush IsNot Nothing Then context.DrawRectangle(BaseBrush, Nothing, New Rect(0, 0, w, h))
        If LineBrush Is Nothing Then
            context.Pop()
            Return
        End If

        Dim phase = SceneSignature(SceneKey) / 2147483647.0
        If IsLightTheme Then
            DrawLightScene(context, w, h, phase)
            context.Pop()
            Return
        End If
        Dim glow As New RadialGradientBrush(Color.FromArgb(12, 133, 150, 132), Colors.Transparent) With {
            .Center = New Point(0.76, 0.63), .GradientOrigin = New Point(0.76, 0.63),
            .RadiusX = 0.68, .RadiusY = 0.82}
        context.DrawRectangle(glow, Nothing, New Rect(0, 0, w, h))
        DrawSurvey(context, w, h, phase)

        'A contained diagram, with breathing room even at the narrowest supported width.
        'The mask tapers into the page instead of ending at a card-sized rectangle.
        Dim inset = Math.Min(28.0, Math.Min(w, h) * 0.08)
        Dim size = Math.Min(Math.Min(w - inset * 2, h - inset * 2), 610.0)
        If size > 24 Then
            Dim x = w - inset - size - Math.Min(36.0, w * 0.025) * phase
            Dim y = h - inset - size
            Dim mask As New RadialGradientBrush With {
                .Center = New Point(0.66, 0.62), .GradientOrigin = New Point(0.66, 0.62),
                .RadiusX = 0.82, .RadiusY = 0.88}
            mask.GradientStops.Add(New GradientStop(Color.FromArgb(230, 255, 255, 255), 0))
            mask.GradientStops.Add(New GradientStop(Color.FromArgb(200, 255, 255, 255), 0.48))
            mask.GradientStops.Add(New GradientStop(Color.FromArgb(0, 255, 255, 255), 1))
            mask.Freeze()
            context.PushOpacityMask(mask)
            context.PushTransform(New TranslateTransform(x, y))
            context.PushTransform(New ScaleTransform(size / 500.0, size / 500.0))
            'Separate graphite structure from yellow inlays, as in the light theme.
            'The dark theme retains its own instrument, contour and signal compositions.
            Dim ink = LineBrush.CloneCurrentValue()
            ink.Opacity *= 0.23
            Dim yellow = If(AccentBrush, LineBrush).CloneCurrentValue()
            yellow.Opacity *= 0.66
            Dim line As New Pen(ink, 0.9 * 500.0 / size)
            Dim accent As New Pen(yellow, 1.45 * 500.0 / size)
            DrawDarkFrame(context, line, accent, phase)
            Select Case ResolveScene(SceneKey)
                Case "orbital" : DrawOrbital(context, line, accent, phase)
                Case "editorial" : DrawEditorial(context, line, accent, phase)
                Case "network" : DrawNetwork(context, line, accent, phase)
                Case "market" : DrawMarket(context, line, accent, phase)
                Case "cartography" : DrawCartography(context, line, accent, phase)
                Case "signal" : DrawSignal(context, line, accent, phase)
                Case "community" : DrawCommunity(context, line, accent, phase)
                Case "archive" : DrawArchive(context, line, accent, phase)
                Case "assembly" : DrawAssembly(context, line, accent, phase)
                Case Else : DrawCalibration(context, line, accent, phase)
            End Select
            context.Pop()
            context.Pop()
            context.Pop()
        End If
        context.Pop()
    End Sub

    Private Sub DrawLightScene(dc As DrawingContext, w As Double, h As Double, phase As Double)
        'Paper-like light composition: sparse registration marks, airy axonometric plates,
        'and independently weighted yellow details instead of the dark instrument diagram.
        Dim wash As New RadialGradientBrush(Color.FromArgb(105, 255, 255, 249), Colors.Transparent) With {
            .Center = New Point(0.78, 0.56), .GradientOrigin = New Point(0.78, 0.56),
            .RadiusX = 0.74, .RadiusY = 0.92}
        dc.DrawRectangle(wash, Nothing, New Rect(0, 0, w, h))
        dc.PushOpacity(0.095)
        Dim gridPen As New Pen(LineBrush, 0.65)
        For x = 32.0 To w Step 96
            For y = 28.0 To h Step 96
                Cross(dc, gridPen, x, y, 2)
            Next
        Next
        dc.Pop()
        dc.PushOpacity(0.22)
        Dim guide As New Pen(LineBrush, 0.7)
        Segment(dc, guide, 14, 46, 14, 14, Math.Min(152, w * 0.28), 14)
        Segment(dc, guide, w - 82, h - 14, w - 14, h - 14, w - 14, h - 43)
        dc.Pop()
        If AccentBrush IsNot Nothing Then
            dc.PushOpacity(0.75)
            dc.DrawRectangle(AccentBrush, Nothing, New Rect(14, 12, Math.Min(32, w * 0.07), 3))
            dc.DrawRectangle(AccentBrush, Nothing, New Rect(Math.Max(14, w - 44), h - 16, 30, 3))
            dc.Pop()
        End If

        Dim inset = Math.Min(28.0, Math.Min(w, h) * 0.08)
        Dim size = Math.Min(Math.Min(w - inset * 2, h - inset * 2), 650.0)
        If size <= 24 Then Return
        Dim mask As New RadialGradientBrush(Colors.White, Colors.Transparent) With {
            .Center = New Point(0.6, 0.58), .GradientOrigin = New Point(0.6, 0.58),
            .RadiusX = 0.92, .RadiusY = 1.04}
        dc.PushOpacityMask(mask)
        dc.PushTransform(New TranslateTransform(w - inset - size, h - inset - size))
        dc.PushTransform(New ScaleTransform(size / 500.0, size / 500.0))
        Dim ink = LineBrush.CloneCurrentValue()
        ink.Opacity *= 0.22
        Dim yellow = If(AccentBrush, LineBrush).CloneCurrentValue()
        yellow.Opacity *= 0.83
        Dim line As New Pen(ink, 0.85 * 500.0 / size)
        Dim accent As New Pen(yellow, 1.6 * 500.0 / size)
        Dim origin As New Point(242 + phase * 14, 146 + phase * 9)

        'All three plates use the same projection and vertical direction.
        'This keeps depth and edge alignment coherent at any aspect ratio.
        DrawLightPlate(dc, line, New Point(origin.X + 8, origin.Y + 70), 230, 175, 12)
        DrawLightPlate(dc, line, origin, 210, 165, 20)
        DrawLightPlate(dc, line, New Point(origin.X + 3, origin.Y - 42), 156, 118, 10)
        dc.PushOpacity(0.6)
        Dim dimension As New Pen(ink, 0.7 * 500.0 / size)
        Segment(dc, dimension, 56, 304, 56, 353, 146, 405, 184, 405)
        Cross(dc, dimension, 56, 304, 4)
        Cross(dc, dimension, 146, 405, 4)
        Segment(dc, dimension, 344, 91, 386, 91, 441, 123, 441, 166)
        dc.Pop()

        'A small plan engraved on the main plane retains each page family's identity.
        dc.PushTransform(New MatrixTransform(0.866, 0.5, -0.866, 0.5, origin.X, origin.Y))
        Select Case ResolveScene(SceneKey)
            Case "orbital", "calibration", "community"
                dc.DrawEllipse(Nothing, line, New Point(104, 80), 56, 56)
                dc.DrawEllipse(Nothing, line, New Point(104, 80), 38, 38)
                Arc(dc, accent, New Point(104, 80), 56, 205, 250)
                Cross(dc, line, 104, 80, 9)
            Case "network", "cartography"
                Segment(dc, line, 43, 112, 73, 48, 146, 45, 174, 111, 108, 132, 43, 112)
                Segment(dc, line, 73, 48, 108, 92, 174, 111)
                Segment(dc, line, 43, 112, 108, 92, 146, 45)
                For Each point In {New Point(43, 112), New Point(73, 48), New Point(146, 45), New Point(174, 111)}
                    dc.DrawEllipse(Nothing, line, point, 4, 4)
                Next
                dc.DrawEllipse(yellow, Nothing, New Point(108, 92), 4, 4)
            Case "market", "signal"
                Segment(dc, line, 40, 36, 40, 132, 178, 132)
                For i = 0 To 5
                    Dim x = 58 + i * 21, height = 18 + i * 10 + Math.Sin(i + phase * 3) * 10
                    Segment(dc, If(i = 4, accent, line), x, 122, x, 122 - height)
                Next
            Case Else
                For i = 0 To 3
                    Dim y = 44 + i * 23
                    Segment(dc, line, 46, y, 155 - (i Mod 2) * 19, y)
                    dc.DrawEllipse(Nothing, If(i = 1, accent, line), New Point(171, y), 3.5, 3.5)
                Next
        End Select
        dc.Pop()

        'Yellow is a tiny material insert and registration strip, never a large wash.
        Dim marker = IsoPoint(origin, 24, 165)
        dc.DrawLine(accent, marker, IsoPoint(origin, 64, 165))
        DrawPolygon(dc, yellow, Nothing, IsoPoint(origin, 168, 16), IsoPoint(origin, 190, 16),
                    IsoPoint(origin, 190, 23), IsoPoint(origin, 168, 23))
        dc.DrawRectangle(yellow, Nothing, New Rect(382, 382, 25, 3))
        dc.DrawRectangle(yellow, Nothing, New Rect(412, 382, 5, 3))
        dc.Pop()
        dc.Pop()
        dc.Pop()
    End Sub

    Private Shared Function IsoPoint(origin As Point, x As Double, y As Double, Optional depth As Double = 0) As Point
        Return New Point(origin.X + (x - y) * 0.866, origin.Y + (x + y) * 0.5 + depth)
    End Function

    Private Shared Sub DrawLightPlate(dc As DrawingContext, line As Pen, origin As Point, width As Double, depth As Double, thickness As Double)
        Dim back = IsoPoint(origin, 0, 0), right = IsoPoint(origin, width, 0)
        Dim front = IsoPoint(origin, width, depth), left = IsoPoint(origin, 0, depth)
        Dim frontBottom = IsoPoint(origin, width, depth, thickness)
        DrawPolygon(dc, New SolidColorBrush(Color.FromArgb(12, 74, 84, 81)), line,
                    left, front, frontBottom, IsoPoint(origin, 0, depth, thickness))
        DrawPolygon(dc, New SolidColorBrush(Color.FromArgb(17, 180, 157, 69)), line,
                    right, front, frontBottom, IsoPoint(origin, width, 0, thickness))
        DrawPolygon(dc, New SolidColorBrush(Color.FromArgb(92, 255, 255, 251)), line, back, right, front, left)
    End Sub

    Private Shared Sub DrawPolygon(dc As DrawingContext, fill As Brush, pen As Pen, ParamArray points As Point())
        If points.Length < 3 Then Return
        Dim geometry As New StreamGeometry
        Using writer = geometry.Open()
            writer.BeginFigure(points(0), True, True)
            For i = 1 To points.Length - 1
                writer.LineTo(points(i), True, False)
            Next
        End Using
        geometry.Freeze()
        dc.DrawGeometry(fill, pen, geometry)
    End Sub

    Private Shared Sub DrawDarkFrame(dc As DrawingContext, line As Pen, accent As Pen, phase As Double)
        'Open corner brackets and small material inserts echo the light registration marks,
        'without importing its floating plates into the dark composition.
        dc.PushOpacity(0.65)
        Segment(dc, line, 48, 118, 48, 81, 65, 64, 118 + phase * 18, 64)
        Segment(dc, line, 385, 440, 430, 440, 452, 418, 452, 374)
        For i = 0 To 6
            Segment(dc, line, 57 + i * 10, 425, 57 + i * 10, If(i Mod 3 = 0, 417, 421))
        Next
        dc.Pop()
        dc.DrawRectangle(accent.Brush, Nothing, New Rect(66, 62.5, 25, 2.5))
        dc.DrawRectangle(accent.Brush, Nothing, New Rect(383, 438.5, 25, 2.5))
        dc.DrawRectangle(accent.Brush, Nothing, New Rect(414, 438.5, 4, 2.5))
        dc.DrawRectangle(accent.Brush, Nothing, New Rect(424, 438.5, 4, 2.5))
    End Sub

    Private Sub DrawSurvey(dc As DrawingContext, w As Double, h As Double, phase As Double)
        Dim hairline As New Pen(LineBrush, 0.55)
        Dim spacing = 48.0
        dc.PushOpacity(0.045)
        'Keep the grid quiet; fine crosses appear only at a few large grid intersections.
        For x = 24.0 To w Step spacing
            dc.DrawLine(hairline, New Point(x, 0), New Point(x, h))
        Next
        For y = 24.0 To h Step spacing
            dc.DrawLine(hairline, New Point(0, y), New Point(w, y))
        Next
        dc.Pop()
        dc.PushOpacity(0.12)
        Dim guide As New Pen(LineBrush, 0.8)
        Dim left = 14.0, top = 14.0
        Dim railEnd = Math.Min(w * (0.22 + phase * 0.06), 210.0)
        Segment(dc, guide, left, Math.Min(62, h * 0.15), left, top + 12, left + 12, top, Math.Max(left + 12, railEnd), top)
        Dim right = Math.Max(left, w - 14)
        Dim bottom = Math.Max(top, h - 14)
        Segment(dc, guide, Math.Max(left, right - 74), bottom, right - 12, bottom, right, bottom - 12, right, Math.Max(top, bottom - 38))
        dc.Pop()
        If AccentBrush IsNot Nothing AndAlso w > 90 AndAlso h > 90 Then
            dc.PushOpacity(0.62)
            dc.DrawRectangle(AccentBrush, Nothing, New Rect(30, 12.5, 28, 2.5))
            dc.DrawRectangle(AccentBrush, Nothing, New Rect(w - 62, h - 15.5, 26, 2.5))
            dc.DrawRectangle(AccentBrush, Nothing, New Rect(w - 30, h - 15.5, 4, 2.5))
            dc.Pop()
        End If
    End Sub

    Private Shared Sub DrawOrbital(dc As DrawingContext, line As Pen, accent As Pen, phase As Double)
        Dim center As New Point(275 + phase * 14, 265 - phase * 10)
        For Each radius In {70.0, 118.0, 176.0, 214.0}
            Arc(dc, line, center, radius, 25 + phase * 18, 278 + phase * 8)
        Next
        For i = 0 To 23
            Dim angle = 26 + i * 11
            dc.DrawLine(If(i = 5 OrElse i = 19, accent, line), Polar(center, 208, angle), Polar(center, If(i Mod 4 = 0, 221, 215), angle))
        Next
        Arc(dc, accent, center, 176, 218, 247)
        Arc(dc, accent, center, 118, 22, 35)
        Segment(dc, line, 58, 356, 126, 356, 198, 284)
        Segment(dc, line, 290, 80, 348, 80, 400, 132)
        Cross(dc, line, center.X, center.Y, 8)
        Dim marker = Polar(center, 118, 58 + phase * 30)
        dc.DrawEllipse(accent.Brush, Nothing, marker, 2.5, 2.5)
        dc.DrawEllipse(Nothing, line, marker, 6, 6)
        Segment(dc, line, marker.X + 7, marker.Y, marker.X + 37, marker.Y)
    End Sub

    Private Shared Sub DrawEditorial(dc As DrawingContext, line As Pen, accent As Pen, phase As Double)
        Dim offset = phase * 20
        For i = 0 To 4
            Dim y = 132 + i * 46
            Segment(dc, line, 65, y + offset, 118, y + offset, 155, y - 28 + offset, 415, y - 28 + offset)
        Next
        Segment(dc, line, 133, 74, 133, 405, 426, 405)
        Segment(dc, line, 171, 85, 171, 377)
        Segment(dc, line, 208, 85, 208, 377)
        Segment(dc, accent, 133, 74, 179, 74)
        Segment(dc, accent, 329, 312 + offset, 389, 312 + offset)
        Cross(dc, line, 426, 405, 7)
        For i = 0 To 4
            dc.DrawEllipse(Nothing, line, New Point(65, 132 + i * 46 + offset), 2.4, 2.4)
        Next
        dc.DrawEllipse(accent.Brush, Nothing, New Point(65, 224 + offset), 2.4, 2.4)
    End Sub

    Private Shared Sub DrawNetwork(dc As DrawingContext, line As Pen, accent As Pen, phase As Double)
        Dim a As New Point(99, 163 + phase * 30), b As New Point(276, 96), c As New Point(411, 225)
        Dim d As New Point(339, 401), e As New Point(122, 357), hub As New Point(251, 255 + phase * 16)
        Segment(dc, line, a.X, a.Y, b.X, b.Y, c.X, c.Y, d.X, d.Y, e.X, e.Y, a.X, a.Y)
        For Each node In {a, b, c, d, e}
            dc.DrawLine(line, node, hub)
            dc.DrawEllipse(Nothing, line, node, 13, 13)
            dc.DrawEllipse(Nothing, line, node, 4, 4)
        Next
        dc.DrawEllipse(Nothing, line, hub, 45, 45)
        dc.DrawEllipse(Nothing, accent, hub, 31, 31)
        dc.DrawEllipse(accent.Brush, Nothing, b, 3, 3)
        Arc(dc, line, hub, 84, 202, 299)
        Cross(dc, accent, hub.X, hub.Y, 6)
    End Sub

    Private Shared Sub DrawMarket(dc As DrawingContext, line As Pen, accent As Pen, phase As Double)
        Segment(dc, line, 62, 87, 62, 407, 438, 407)
        For i = 0 To 5
            Dim x = 101 + i * 58
            Segment(dc, line, x, 102, x, 407)
            Segment(dc, line, 62, 117 + i * 52, 438, 117 + i * 52)
        Next
        Dim shift = phase * 22
        Segment(dc, line, 63, 350, 108, 350, 108, 294 + shift, 162, 294 + shift, 162, 319, 216, 319, 216, 226, 270, 226, 270, 245 + shift, 328, 245 + shift, 328, 163, 384, 163, 384, 122, 437, 122)
        Segment(dc, accent, 328, 245 + shift, 328, 163, 384, 163)
        dc.DrawRectangle(accent.Brush, Nothing, New Rect(381, 119, 6, 6))
        For i = 0 To 5
            Dim x = 112 + i * 56, y = 344 - i * 34 + Math.Sin(i + phase * 4) * 24
            Segment(dc, line, x, y - 25, x, y + 25)
            dc.DrawRectangle(Nothing, line, New Rect(x - 7, y - 12, 14, 24))
        Next
    End Sub

    Private Shared Sub DrawCartography(dc As DrawingContext, line As Pen, accent As Pen, phase As Double)
        For i = 0 To 6
            Dim margin = i * 19.0, bend = phase * 18
            Curve(dc, line, New Point(66 + margin, 114 + margin * 0.2), New Point(154 + margin, 35 + margin), New Point(352 - margin, 91 + margin + bend), New Point(425 - margin * 0.45, 229))
            Curve(dc, line, New Point(425 - margin * 0.45, 229), New Point(471 - margin, 358 - margin + bend), New Point(246 + margin, 453 - margin), New Point(105 + margin, 374 - margin * 0.35))
        Next
        Segment(dc, line, 58, 269, 440, 269)
        Segment(dc, line, 279, 53, 279, 442)
        Cross(dc, accent, 279, 269, 9)
        dc.DrawEllipse(Nothing, accent, New Point(279, 269), 26, 26)
        Segment(dc, line, 307, 269, 357, 239, 417, 239)
    End Sub

    Private Shared Sub DrawCalibration(dc As DrawingContext, line As Pen, accent As Pen, phase As Double)
        Dim center As New Point(276, 259)
        Arc(dc, line, center, 172, 16 + phase * 10, 316)
        Arc(dc, line, center, 143, 50, 283 - phase * 14)
        For i = 0 To 28
            Dim angle = 18 + i * 10
            dc.DrawLine(If(i = 4 OrElse i = 20, accent, line), Polar(center, 166, angle), Polar(center, If(i Mod 4 = 0, 183, 177), angle))
        Next
        Segment(dc, line, 48, 358, 126, 358, 126, 104, 359, 104)
        Segment(dc, line, 171, 166, 381, 166)
        Segment(dc, line, 171, 219, 352, 219)
        Segment(dc, line, 171, 272, 381, 272)
        dc.DrawEllipse(Nothing, accent, New Point(229 + phase * 41, 219), 6, 6)
        Segment(dc, accent, 126, 104, 170, 104)
        Cross(dc, line, center.X, center.Y, 6)
    End Sub

    Private Shared Sub DrawSignal(dc As DrawingContext, line As Pen, accent As Pen, phase As Double)
        Segment(dc, line, 55, 257, 445, 257)
        For band = 0 To 4
            Dim points As New List(Of Point)
            For i = 0 To 48
                Dim x = 60 + i * 8, envelope = Math.Sin(Math.PI * i / 48)
                points.Add(New Point(x, 145 + band * 48 + Math.Sin(i * 0.25 + phase * 3 + band * 0.4) * envelope * (24 + band * 4)))
            Next
            Polyline(dc, line, points)
        Next
        For i = 0 To 18
            Dim height = 12 + Math.Abs(Math.Sin(i * 0.45 + phase * 4)) * 62
            Segment(dc, If(i = 12, accent, line), 78 + i * 19, 408, 78 + i * 19, 408 - height)
        Next
        Arc(dc, accent, New Point(392, 100), 18, 235, 310)
    End Sub

    Private Shared Sub DrawCommunity(dc As DrawingContext, line As Pen, accent As Pen, phase As Double)
        Dim left As New Point(193, 248), right As New Point(309, 248)
        For i = 0 To 2
            dc.DrawEllipse(Nothing, line, left, 72 + i * 32, 72 + i * 32)
            dc.DrawEllipse(Nothing, line, right, 72 + i * 32, 72 + i * 32)
        Next
        Dim angle = 30 + phase * 65
        Dim mark = Polar(right, 104, angle)
        dc.DrawEllipse(Nothing, accent, mark, 5, 5)
        Segment(dc, line, 70, 395, 205, 395, 239, 361, 428, 361)
        Segment(dc, accent, 239, 361, 287, 361)
        Cross(dc, line, 251, 248, 7)
    End Sub

    Private Shared Sub DrawArchive(dc As DrawingContext, line As Pen, accent As Pen, phase As Double)
        For i = 0 To 3
            Dim x = 104 + i * 33, y = 71 + i * 27
            Segment(dc, line, x, y + 252, x, y, x + 194, y, x + 222, y + 28, x + 222, y + 252)
            Segment(dc, line, x + 194, y, x + 194, y + 28, x + 222, y + 28)
        Next
        For i = 0 To 4
            Segment(dc, line, 218, 239 + i * 28, 354 + phase * 16, 239 + i * 28)
        Next
        Segment(dc, accent, 203, 152, 251, 152)
        Segment(dc, line, 66, 399, 154, 399, 176, 377)
        Cross(dc, line, 413, 435, 7)
    End Sub

    Private Shared Sub DrawAssembly(dc As DrawingContext, line As Pen, accent As Pen, phase As Double)
        Dim centerX = 266 + phase * 18
        For i = 0 To 3
            Dim y = 122 + i * 62
            Segment(dc, line, centerX - 154, y, centerX, y - 75, centerX + 154, y, centerX, y + 75, centerX - 154, y)
            If i < 3 Then
                Segment(dc, line, centerX - 154, y, centerX - 154, y + 34)
                Segment(dc, line, centerX + 154, y, centerX + 154, y + 34)
            End If
        Next
        Segment(dc, line, 66, 352, 66, 416, 197, 416)
        Segment(dc, line, 424, 85, 448, 109, 448, 175)
        Segment(dc, accent, centerX - 154, 184, centerX - 99, 157)
        Cross(dc, line, centerX, 307, 6)
    End Sub

    Private Shared Sub Segment(dc As DrawingContext, pen As Pen, ParamArray coordinates As Double())
        If coordinates.Length < 4 Then Return
        Dim geometry As New StreamGeometry
        Using writer = geometry.Open()
            writer.BeginFigure(New Point(coordinates(0), coordinates(1)), False, False)
            For i = 2 To coordinates.Length - 2 Step 2
                writer.LineTo(New Point(coordinates(i), coordinates(i + 1)), True, False)
            Next
        End Using
        geometry.Freeze()
        dc.DrawGeometry(Nothing, pen, geometry)
    End Sub

    Private Shared Sub Polyline(dc As DrawingContext, pen As Pen, points As IList(Of Point))
        If points.Count < 2 Then Return
        Dim geometry As New StreamGeometry
        Using writer = geometry.Open()
            writer.BeginFigure(points(0), False, False)
            For i = 1 To points.Count - 1
                writer.LineTo(points(i), True, False)
            Next
        End Using
        geometry.Freeze()
        dc.DrawGeometry(Nothing, pen, geometry)
    End Sub

    Private Shared Sub Curve(dc As DrawingContext, pen As Pen, start As Point, first As Point, second As Point, finish As Point)
        Dim geometry As New StreamGeometry
        Using writer = geometry.Open()
            writer.BeginFigure(start, False, False)
            writer.BezierTo(first, second, finish, True, False)
        End Using
        geometry.Freeze()
        dc.DrawGeometry(Nothing, pen, geometry)
    End Sub

    Private Shared Sub Arc(dc As DrawingContext, pen As Pen, center As Point, radius As Double, fromAngle As Double, toAngle As Double)
        Dim geometry As New StreamGeometry
        Using writer = geometry.Open()
            writer.BeginFigure(Polar(center, radius, fromAngle), False, False)
            writer.ArcTo(Polar(center, radius, toAngle), New Size(radius, radius), 0,
                         Math.Abs(toAngle - fromAngle) > 180, SweepDirection.Clockwise, True, False)
        End Using
        geometry.Freeze()
        dc.DrawGeometry(Nothing, pen, geometry)
    End Sub

    Private Shared Function Polar(center As Point, radius As Double, degrees As Double) As Point
        Dim radians = degrees * Math.PI / 180.0
        Return New Point(center.X + Math.Cos(radians) * radius, center.Y + Math.Sin(radians) * radius)
    End Function

    Private Shared Sub Cross(dc As DrawingContext, pen As Pen, x As Double, y As Double, radius As Double)
        dc.DrawLine(pen, New Point(x - radius, y), New Point(x + radius, y))
        dc.DrawLine(pen, New Point(x, y - radius), New Point(x, y + radius))
    End Sub
End Class
