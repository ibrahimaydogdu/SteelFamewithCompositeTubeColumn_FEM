Imports System.Globalization

'Geometry and grouping of one example, computed without ETABS (so it can be tested alone).
'Levels: level k (1 .. Stories) is the top of story k; level 0 is the base. A column of story k runs from level k-1 to level k;
'the beams and the (null) floor areas of story k are at level k.
Public Class PlanColumn_
    Public X, Y, Z1, Z2 As Double
    Public Story As Integer
    Public Kind As String       'Corner | Edge | Interior
    Public Group As String
    Public Angle As Double      'rotation of the local axes about the column axis [deg]
End Class

Public Class PlanBeam_
    Public X1, Y1, X2, Y2, Z As Double
    Public Story As Integer
    Public Direction As String  'X | Y
    Public Kind As String       'Edge | Interior
    Public Moment As Boolean    'moment connection at both ends (False: pinned ends, gravity beam)
    Public Length As Double
    Public Group As String
End Class

Public Class PlanCell_
    Public X1, Y1, X2, Y2, Z As Double
    Public Story As Integer
End Class

Public Class PlanGroup_
    Public Name As String
    Public IsColumn As Boolean
    Public Kind As String
    Public Length As Double     'beams: span [m]
    Public FirstStory, LastStory As Integer
    Public Count As Integer
End Class

Public Class BuildPlan_
    Public P As BuildParams_
    Public XLines As New List(Of Double)
    Public YLines As New List(Of Double)
    Public Levels As New List(Of Double)        '(0) = base, (k) = top of story k
    Public Columns As New List(Of PlanColumn_)
    Public Beams As New List(Of PlanBeam_)
    Public Cells As New List(Of PlanCell_)
    Public Groups As New List(Of PlanGroup_)

    Public Shared Function Create(ByVal P As BuildParams_) As BuildPlan_
        Dim B As New BuildPlan_ With {.P = P}
        Dim Msg As String = Nothing
        Dim Bx = BuildParams_.ParseBays(P.BaysX, Msg), By = BuildParams_.ParseBays(P.BaysY, Msg)
        B.XLines.Add(0) : For Each d In Bx : B.XLines.Add(Math.Round(B.XLines.Last() + d, 4)) : Next
        B.YLines.Add(0) : For Each d In By : B.YLines.Add(Math.Round(B.YLines.Last() + d, 4)) : Next
        Dim nx As Integer = Bx.Count, ny As Integer = By.Count, N As Integer = P.Stories
        B.Levels.Add(0)
        For k = 1 To N : B.Levels.Add(Math.Round(P.FirstStoryHeight + (k - 1) * P.StoryHeight, 4)) : Next

        Dim GroupMap As New Dictionary(Of String, PlanGroup_)
        Dim Band = Function(k As Integer) As String
                       Dim First As Integer = ((k - 1) \ P.StoryBand) * P.StoryBand + 1
                       Return "S" & First.ToString("00") & "-" & Math.Min(N, First + P.StoryBand - 1).ToString("00")
                   End Function
        Dim Reg = Sub(Name As String, IsCol As Boolean, Kind As String, Len As Double, k As Integer)
                      Dim G As PlanGroup_ = Nothing
                      If Not GroupMap.TryGetValue(Name, G) Then
                          Dim First As Integer = ((k - 1) \ P.StoryBand) * P.StoryBand + 1
                          G = New PlanGroup_ With {.Name = Name, .IsColumn = IsCol, .Kind = Kind, .Length = Len, .FirstStory = First, .LastStory = Math.Min(N, First + P.StoryBand - 1)}
                          GroupMap.Add(Name, G) : B.Groups.Add(G)
                      End If
                      G.Count += 1
                  End Sub

        For k = 1 To N
            For i = 0 To nx
                For j = 0 To ny
                    Dim ei As Boolean = (i = 0 OrElse i = nx), ej As Boolean = (j = 0 OrElse j = ny)
                    Dim Kind As String = If(ei AndAlso ej, "Corner", If(ei OrElse ej, "Edge", "Interior"))
                    Dim C As New PlanColumn_ With {.X = B.XLines(i), .Y = B.YLines(j), .Z1 = B.Levels(k - 1), .Z2 = B.Levels(k), .Story = k, .Kind = Kind}
                    If P.ColumnOrientation = "Auto" AndAlso Kind = "Edge" AndAlso ei Then C.Angle = 90      'edge column of an x = const line: the y frame bends it about the strong axis
                    C.Group = "COL-" & Kind.ToUpperInvariant() & "-" & Band(k)
                    B.Columns.Add(C) : Reg(C.Group, True, Kind, 0, k)
                Next
            Next
            For j = 0 To ny                         'beams in x
                For i = 0 To nx - 1
                    B.AddBeam(k, "X", B.XLines(i), B.YLines(j), B.XLines(i + 1), B.YLines(j), (j = 0 OrElse j = ny), Band, Reg)
                Next
            Next
            For i = 0 To nx                         'beams in y
                For j = 0 To ny - 1
                    B.AddBeam(k, "Y", B.XLines(i), B.YLines(j), B.XLines(i), B.YLines(j + 1), (i = 0 OrElse i = nx), Band, Reg)
                Next
            Next
            For i = 0 To nx - 1
                For j = 0 To ny - 1
                    B.Cells.Add(New PlanCell_ With {.X1 = B.XLines(i), .Y1 = B.YLines(j), .X2 = B.XLines(i + 1), .Y2 = B.YLines(j + 1), .Z = B.Levels(k), .Story = k})
                Next
            Next
        Next
        Return B
    End Function

    Private Sub AddBeam(ByVal k As Integer, ByVal Dir As String, ByVal x1 As Double, ByVal y1 As Double, ByVal x2 As Double, ByVal y2 As Double, ByVal Edge As Boolean,
                        ByVal Band As Func(Of Integer, String), ByVal Reg As Action(Of String, Boolean, String, Double, Integer))
        Dim Len As Double = Math.Round(Math.Abs(x2 - x1) + Math.Abs(y2 - y1), 4)
        Dim Kind As String = If(Edge, "Edge", "Interior")
        Dim Bm As New PlanBeam_ With {.X1 = x1, .Y1 = y1, .X2 = x2, .Y2 = y2, .Z = Levels(k), .Story = k, .Direction = Dir, .Kind = Kind, .Length = Len,
                                      .Moment = (P.FrameSystem = "Space" OrElse Edge)}
        Bm.Group = "BM-" & If(Edge, "EDGE", "INT") & "-L" & Math.Round(Len * 1000).ToString(CultureInfo.InvariantCulture) & "-" & Band(k)
        Beams.Add(Bm) : Reg(Bm.Group, False, Kind, Len, k)
    End Sub

    Public ReadOnly Property ColumnGroups As IEnumerable(Of PlanGroup_)
        Get
            Return Groups.Where(Function(g) g.IsColumn)
        End Get
    End Property

    Public ReadOnly Property BeamGroups As IEnumerable(Of PlanGroup_)
        Get
            Return Groups.Where(Function(g) Not g.IsColumn)
        End Get
    End Property

    'Overall dimensions [m]
    Public ReadOnly Property Height As Double
        Get
            Return Levels.Last()
        End Get
    End Property

    Public Function Describe() As String
        Dim F = Function(x As Double) x.ToString("0.###", CultureInfo.InvariantCulture)
        Dim Sb As New System.Text.StringBuilder
        Sb.AppendLine("Plan " & F(XLines.Last()) & " x " & F(YLines.Last()) & " m (" & (XLines.Count - 1) & " x " & (YLines.Count - 1) & " bays), " & P.Stories & " stories, height " & F(Height) & " m")
        Sb.AppendLine("System: " & P.FrameSystem & If(P.FrameSystem = "Perimeter", " (interior column base " & P.InteriorColumnBase & ")", "") & ", group band " & P.StoryBand & " stories")
        Sb.AppendLine("Columns " & Columns.Count & ", beams " & Beams.Count & " (moment " & Beams.Where(Function(b) b.Moment).Count() & ", pinned " & Beams.Where(Function(b) Not b.Moment).Count() & "), floor areas " & Cells.Count)
        Sb.AppendLine("Groups: " & ColumnGroups.Count() & " column, " & BeamGroups.Count() & " beam (" & Groups.Count & " design variables)")
        For Each G In Groups
            Sb.AppendLine("  " & G.Name.PadRight(26) & G.Count.ToString().PadLeft(5) & " members, stories " & G.FirstStory & "-" & G.LastStory)
        Next
        Return Sb.ToString()
    End Function
End Class
