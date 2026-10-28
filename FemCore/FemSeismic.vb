Imports System.Collections.Generic

'AISC 341 checks that depend on the model: strong column - weak beam ratios (empirical fit to ETABS, see FEM_SURUM_KAYITLARI.md), and the
'section based checks (compactness and the Lb/ry limit of the beams)
Partial Public Class FemStatic

    'ETABS fit (P4, random designs, 133 joints, rms 0.5 %): ratio = sum Mpb / sum Mpc with
    '  Mpb = C0 Fy Zb (1 + C1 off / L)    (beam in the plane of the column axis, moment connection at the joint, off: end offset, L: joint to joint length)
    '  Mpc = Zc (Fy - Pu / Ag)            (column below and column above the joint; Z about the axis of the plane; Pu: largest compression)
    Public Const ScwbC0 As Double = 1.5347
    Public Const ScwbC1 As Double = 2.2542

    'Axial force of the AISC 341 checks: ETABS uses the combinations with the response spectrum cases only (fit on 1113 column ratios: gravity
    'combinations get the weight -0.02); without a response spectrum case the largest compression of all combinations
    Public Function PuAisc341(ByVal R As MemberRatios) As Double
        Return If(M.RSCases.Count > 0, R.PuSeis, R.PuMax)
    End Function

    Public Above() As Integer                                   'column above the top joint of every column (-1: none), set by StrongColumnWeakBeam

    'Ratios of every column (per element index): Major and Minor (0 when the joint has no column above or no moment beam in that plane)
    Public Sub StrongColumnWeakBeam(ByVal Fem() As MemberRatios, ByRef Major() As Double, ByRef Minor() As Double)
        Dim ne As Integer = M.Elements.Count
        ReDim Major(ne - 1) : ReDim Minor(ne - 1)
        ReDim Above(ne - 1)
        For i = 0 To ne - 1 : Above(i) = -1 : Next
        Dim IsCol = Function(e As FemElement) Math.Abs(e.Axes(0, 2)) > 0.9
        Dim Attached As New Dictionary(Of Integer, List(Of Integer))
        For i = 0 To ne - 1
            For Each nd In {M.Elements(i).NI, M.Elements(i).NJ}
                If Not Attached.ContainsKey(nd) Then Attached(nd) = New List(Of Integer)
                Attached(nd).Add(i)
            Next
        Next
        'PCR-5b: composite columns (AISC 341-22 G3.4a) when the model asks for them; otherwise only steel columns (earlier versions)
        Dim Comp = Function(e As FemElement) M.CompositeScwb AndAlso e.Section.NoDesign AndAlso e.Section.Mpcc IsNot Nothing
        Dim Usable = Function(e As FemElement) Not e.Section.NoDesign OrElse Comp(e)
        Dim Joints As New List(Of ScwbJoint)
        For ci = 0 To ne - 1
            Dim col = M.Elements(ci)
            If Not IsCol(col) OrElse Not Usable(col) Then Continue For
            Dim top As Integer = If(M.Nodes(col.NI).Z > M.Nodes(col.NJ).Z, col.NI, col.NJ)
            Dim above As Integer = -1
            For Each ei In Attached(top)
                Dim e = M.Elements(ei)
                If ei <> ci AndAlso IsCol(e) AndAlso M.Nodes(If(e.NI = top, e.NJ, e.NI)).Z > M.Nodes(top).Z Then above = ei
            Next
            Me.Above(ci) = above
            Dim HasAbove As Boolean = above >= 0 AndAlso Usable(M.Elements(above))
            If Not HasAbove AndAlso Not M.CornerScwb Then Continue For          'roof joints: only the corner check below (PCR-5g)
            Dim ab As FemElement = If(HasAbove, M.Elements(above), Nothing)
            Dim AnyComp As Boolean = Comp(col) OrElse (HasAbove AndAlso Comp(ab))
            Dim Cols() As Integer = If(HasAbove, {ci, above}, {ci})
            'moment connected beams at the top joint, per plane
            Dim BeamSum(1) As Double, BeamCount(1) As Integer, BeamDir(1) As Integer, BeamSumRbs(1) As Double
            For plane = 0 To 1                                   '0 = major (axis 2 of the column in the plane), 1 = minor
                Dim sumB As Double = 0, nB As Integer = 0, sumR As Double = 0
                For Each bi In Attached(top)
                    Dim b = M.Elements(bi)
                    If IsCol(b) Then Continue For
                    Dim atI As Boolean = (b.NI = top)
                    Dim rel = If(atI, b.ReleaseI, b.ReleaseJ)
                    If rel(4) OrElse rel(5) Then Continue For            'pinned: not part of the moment frame
                    Dim dirx As Double = b.Axes(0, 0), diry As Double = b.Axes(0, 1)
                    Dim along2 As Double = Math.Abs(dirx * col.Axes(1, 0) + diry * col.Axes(1, 1))
                    Dim inPlane As Boolean = If(plane = 0, along2 > 0.7, along2 <= 0.7)
                    If Not inPlane Then Continue For
                    BeamDir(plane) = If(Math.Abs(dirx) >= Math.Abs(diry), 0, 1)      'global direction of the frame line (0 = x, 1 = y)
                    Dim off As Double = If(atI, b.OffsetI, b.OffsetJ)
                    If AnyComp OrElse M.ScwbBeamCode Then
                        'AISC 341-22, plastic hinge at the column face, moment projected to the column centreline over the end offset with the
                        'hinge shear 2 Mp / Lh (gravity shear neglected): composite joints G3.4a 1.1 Mpbe + Muv, Mpbe = Ry Fy Z; steel joints
                        'Eq. E3-3 Mpr + alpha_s Mv, Mpr = Cpr Ry Fy Z, Cpr = (Fy + Fu) / 2 Fy <= 1.2
                        Dim Fy As Double = b.Section.Fy
                        Dim Cpr As Double = If(Fy > 0 AndAlso b.Section.Fu > 0, Math.Min((Fy + b.Section.Fu) / (2 * Fy), 1.2), 1.2)
                        Dim Mp As Double = If(AnyComp, 1.1, Cpr) * SteelDesign.RyAisc * Fy * b.Section.Z33
                        Dim Lh As Double = Math.Max(b.Length - b.OffsetI - b.OffsetJ, 0.001)
                        sumB += Mp + 2 * Mp / Lh * off
                    Else
                        sumB += ScwbC0 * b.Section.Fy * b.Section.Z33 * (1 + ScwbC1 * off / b.Length)
                    End If
                    If M.CornerRbs Then sumR += RbsProjected(b, off, If(CornerVg IsNot Nothing, CornerVg(bi), 0.0))
                    nB += 1
                Next
                BeamSum(plane) = sumB : BeamCount(plane) = nB : BeamSumRbs(plane) = sumR
            Next
            'column side of the joint per plane (E3-2 / G3.4a Mpcc), Pr of the design combinations
            Dim SumC(1) As Double
            For plane = 0 To 1
                If BeamCount(plane) = 0 Then Continue For
                For Each cc In Cols
                    Dim c = M.Elements(cc)
                    If Comp(c) Then
                        SumC(plane) += c.Section.Mpcc(PuAisc341(Fem(cc)), plane)     'G3.4a: Mpcc at Prc (AISC 360-22 Chapter I plastic stress distribution)
                    Else
                        Dim z As Double = If(plane = 0, c.Section.Z33, c.Section.Z22)
                        SumC(plane) += z * (c.Section.Fy - PuAisc341(Fem(cc)) / c.Section.A)
                    End If
                Next
            Next
            'PCR-5g (AISC 341-22 E3.3, Commentary case (b): multi-bay planar frames intersecting at the corner columns only): a corner joint
            '(moment beams in both planes) has no joint moment ratio; the frame lines are checked per storey with the corner columns ignored
            'and the corner columns for strength with the beams of both directions yielding (StoreyAndCornerChecks)
            Dim Corner As Boolean = M.CornerScwb AndAlso CornerPr IsNot Nothing AndAlso BeamCount(0) > 0 AndAlso BeamCount(1) > 0
            If M.CornerScwb AndAlso CornerPr IsNot Nothing Then
                Joints.Add(New ScwbJoint With {.Col = ci, .Above = If(HasAbove, above, -1), .Node = top, .Corner = Corner,
                                               .BeamSum = BeamSum, .BeamCount = BeamCount, .Dir = BeamDir, .SumC = SumC,
                                               .CornerBeamSum = If(M.CornerRbs, BeamSumRbs, BeamSum)})
                If Corner Then Continue For
            End If
            If Not HasAbove Then Continue For                                     'roof joints: no joint ratio (storey check above)
            'G3.4a exception: a joint of composite columns only, each with Prc < 0.1 Pyc, is not checked
            If Comp(col) AndAlso Comp(ab) AndAlso PuAisc341(Fem(ci)) < 0.1 * col.Section.Pyc AndAlso PuAisc341(Fem(above)) < 0.1 * ab.Section.Pyc Then Continue For
            For plane = 0 To 1
                If BeamCount(plane) = 0 OrElse SumC(plane) <= 0 Then Continue For
                If plane = 0 Then Major(ci) = BeamSum(plane) / SumC(plane) Else Minor(ci) = BeamSum(plane) / SumC(plane)
            Next
        Next
        If M.CornerScwb AndAlso CornerPr IsNot Nothing Then StoreyAndCornerChecks(Joints, Fem, Major, Minor, Comp)
    End Sub

    Private Class ScwbJoint
        Public Col, Above, Node As Integer
        Public Corner As Boolean
        Public BeamSum(), SumC(), CornerBeamSum() As Double
        Public BeamCount(), Dir() As Integer
    End Class

    'PCR-11 (M.CornerRbs): beam strength of the corner check at the column centreline with the RBS of AISC 358-22 5.7: Eq. 5.7-1 to 5.7-3
    'a = 0.625 bbf, b = 0.75 d (middle of the ranges), c = 0.25 bbf; ZRBS = Zb - 2 c tbf (d - tbf) (Eq. 5.7-4); Mpr = Cpr Ry Fy ZRBS (2.4.3);
    'hinge at Sh = a + b / 2 (Eq. 5.7-5); Vh = 2 Mpr / Lh' + Vgravity (Eq. 2.4-3, same sign; Lh' = clear span - 2 Sh, Vgravity = Vg: end
    'shear of the beam under 1.2 D + 0.5 L); moment projected to the column centreline over Sh + end offset
    Public Shared Function RbsProjected(ByVal b As FemElement, ByVal off As Double, ByVal Vg As Double) As Double
        Dim S = b.Section
        Dim Fy As Double = S.Fy
        Dim Cpr As Double = If(Fy > 0 AndAlso S.Fu > 0, Math.Min((Fy + S.Fu) / (2 * Fy), 1.2), 1.2)
        Dim Ze As Double = S.Z33
        Dim Sh As Double = 0
        If S.Width > 0 AndAlso S.FlangeThickness > 0 AndAlso S.Depth > 0 Then
            Ze = S.Z33 - 2 * 0.25 * S.Width * S.FlangeThickness * (S.Depth - S.FlangeThickness)
            Sh = 0.625 * S.Width + 0.375 * S.Depth
        End If
        Dim Mpr As Double = Cpr * SteelDesign.RyAisc * Fy * Ze
        Dim Lh As Double = Math.Max(b.Length - b.OffsetI - b.OffsetJ - 2 * Sh, 0.001)
        Return Mpr + (2 * Mpr / Lh + Vg) * (off + Sh)
    End Function

    'PCR-5g, AISC 341-22 Commentary E3.3 case (b), conservative alternate. (1) Storey check of every frame line at every floor:
    'sum of the projected beam strengths of all moment connected beams of the line (corner joints included) <= sum of the column strengths
    'of the non-corner joints of the line (corner columns ignored); the ratio goes to the non-corner columns of the line. Roof: exempt when
    'every non-corner column meets the axial limit of the E3.4a / G3.4a exception. (2) Corner columns: AISC 360-22 H1-1 (steel) or the
    'composite interaction (CFT, host routine) with Pr of D1.4a(b) (CornerPr) and the projected beam strengths of both directions, split
    'between the column above and below the joint by the flexural stiffness I / L
    Private Sub StoreyAndCornerChecks(ByVal J As List(Of ScwbJoint), ByVal Fem() As MemberRatios, ByVal Major() As Double, ByVal Minor() As Double,
                                      ByVal Comp As Func(Of FemElement, Boolean))
        Dim Lines As New Dictionary(Of String, List(Of Tuple(Of ScwbJoint, Integer)))
        For Each jt In J
            Dim nd = M.Nodes(jt.Node)
            For plane = 0 To 1
                If jt.BeamCount(plane) = 0 Then Continue For
                Dim Coord As Double = If(jt.Dir(plane) = 0, nd.Y, nd.X)
                Dim Key As String = jt.Dir(plane) & "|" & Math.Round(Coord, 3).ToString(Globalization.CultureInfo.InvariantCulture) & "|" &
                                    Math.Round(nd.Z, 3).ToString(Globalization.CultureInfo.InvariantCulture)
                If Not Lines.ContainsKey(Key) Then Lines(Key) = New List(Of Tuple(Of ScwbJoint, Integer))
                Lines(Key).Add(Tuple.Create(jt, plane))
            Next
        Next
        Dim Exempt = Function(ci As Integer) As Boolean
                         Dim c = M.Elements(ci)
                         Return If(Comp(c), PuAisc341(Fem(ci)) < 0.1 * c.Section.Pyc, PuAisc341(Fem(ci)) < 0.3 * c.Section.Fy * c.Section.A)
                     End Function
        For Each kv In Lines
            Dim NonCorner = kv.Value.Where(Function(t) Not t.Item1.Corner).ToList()
            If NonCorner.Count = 0 OrElse NonCorner.Count = kv.Value.Count Then Continue For        'no corner on the line: joint checks only
            If NonCorner.All(Function(t) t.Item1.Above < 0) AndAlso NonCorner.All(Function(t) Exempt(t.Item1.Col)) Then Continue For
            Dim sB As Double = kv.Value.Sum(Function(t) t.Item1.BeamSum(t.Item2))
            Dim sC As Double = NonCorner.Sum(Function(t) t.Item1.SumC(t.Item2))
            Dim Rho As Double = If(sC > 0, sB / sC, 10.0)
            For Each t In NonCorner
                If t.Item2 = 0 Then Major(t.Item1.Col) = Math.Max(Major(t.Item1.Col), Rho) Else Minor(t.Item1.Col) = Math.Max(Minor(t.Item1.Col), Rho)
            Next
        Next
        For Each jt In J.Where(Function(x) x.Corner)
            If M.CornerRoofExempt AndAlso jt.Above < 0 AndAlso Exempt(jt.Col) Then Continue For            'PCR-11: E3.4a(a)(1) / G3.4a exception
            Dim Segs() As Integer = If(jt.Above >= 0, {jt.Col, jt.Above}, {jt.Col})
            Dim Len = Function(e As Integer) Math.Max(M.Elements(e).Length, 0.001)
            Dim Kx As Double = Segs.Sum(Function(e) M.Elements(e).Section.I33 / Len(e)), Ky As Double = Segs.Sum(Function(e) M.Elements(e).Section.I22 / Len(e))
            For Each s In Segs
                Dim E = M.Elements(s)
                Dim Mx As Double = jt.CornerBeamSum(0) * If(Kx > 0, E.Section.I33 / Len(s) / Kx, 1.0 / Segs.Length)
                Dim My As Double = jt.CornerBeamSum(1) * If(Ky > 0, E.Section.I22 / Len(s) / Ky, 1.0 / Segs.Length)
                Dim Pr As Double = CornerPr(s)
                Dim R As Double
                If Comp(E) Then
                    R = If(E.Section.CompositeCheck IsNot Nothing, E.Section.CompositeCheck(Pr, Mx, My, Len(s)), 0.0)
                Else
                    Dim Pc As Double = 0.9 * SteelDesign.CompressionStrength(E.Section, Len(s), Len(s))
                    Dim Mcx As Double = 0.9 * SteelDesign.MajorFlexure(E.Section, Len(s), 1.0), Mcy As Double = 0.9 * SteelDesign.MinorFlexure(E.Section)
                    Dim rp As Double = Pr / Math.Max(Pc, 0.000001)
                    R = If(rp >= 0.2, rp + 8.0 / 9.0 * (Mx / Mcx + My / Mcy), rp / 2 + Mx / Mcx + My / Mcy)
                End If
                Major(s) = Math.Max(Major(s), R) : Minor(s) = Math.Max(Minor(s), R)
            Next
        Next
    End Sub

    'PCR-5g: required axial compression of every column for the corner check, AISC 341-22 D1.4a with the overstrength seismic load of both
    'directions: P(CornerGravity) + Omega0 (|P_RSx| + |P_RSy|) (PCR-11 CornerOrtho 0.3: Omega0 max(|P_RSx| + 0.3 |P_RSy|, 0.3 |P_RSx| + |P_RSy|)),
    'largest over the stations (compression positive). Set before StrongColumnWeakBeam.
    Public CornerPr() As Double

    'PCR-11: seismic axial force magnitudes of the two directions combined: max(Qx + f Qy, f Qx + Qy) (f 1.0: 100 % + 100 %, 0.3: 100 % + 30 %)
    Public Shared Function OrthogonalSum(ByVal Qx As Double, ByVal Qy As Double, ByVal f As Double) As Double
        Return Math.Max(Qx + f * Qy, f * Qx + Qy)
    End Function

    'PCR-11 (M.CornerRbs): Vgravity of AISC 358-22 Eq. 2.4-3 of every beam, largest |V2| at the ends of the clear span under M.RbsGravity
    Public CornerVg() As Double

    Public Sub SetCornerAxial(ByVal R As StaticResults, ByVal RS As Dictionary(Of String, RSResults))
        Dim ne As Integer = M.Elements.Count
        Dim Out(ne - 1) As Double, Vg(ne - 1) As Double
        System.Threading.Tasks.Parallel.For(0, ne, FemProfile.Options, Sub(ei)
                                                                          If Math.Abs(M.Elements(ei).Axes(0, 2)) <= 0.9 Then
                                                                              If Not M.CornerRbs Then Return
                                                                              Dim xb() As Double = StationLocations(ei)
                                                                              Dim V0 As Double = 0, V1 As Double = 0
                                                                              For Each t In M.RbsGravity.Terms
                                                                                  Dim pat As Integer = M.PatternIndex(t.Name)
                                                                                  If pat < 0 Then Continue For
                                                                                  Dim sp = StationForces(R, pat, ei, xb)
                                                                                  V0 += t.Factor * sp(0)(1) : V1 += t.Factor * sp(xb.Length - 1)(1)
                                                                              Next
                                                                              Vg(ei) = Math.Max(Math.Abs(V0), Math.Abs(V1))
                                                                              Return
                                                                          End If
                                                                          Dim xs() As Double = StationLocations(ei)
                                                                          Dim N(xs.Length - 1) As Double, Q(xs.Length - 1) As Double, Qd(1)() As Double
                                                                          Qd(0) = New Double(xs.Length - 1) {} : Qd(1) = New Double(xs.Length - 1) {}
                                                                          For Each t In M.CornerGravity.Terms
                                                                              Dim pat As Integer = M.PatternIndex(t.Name)
                                                                              If pat < 0 Then Continue For
                                                                              Dim sp = StationForces(R, pat, ei, xs)
                                                                              For j = 0 To xs.Length - 1 : N(j) += t.Factor * sp(j)(0) : Next
                                                                          Next
                                                                          For Each rc In M.RSCases.Where(Function(c) c.Direction = 0 OrElse c.Direction = 1)
                                                                              Dim rr As RSResults = Nothing
                                                                              If RS Is Nothing OrElse Not RS.TryGetValue(rc.Name, rr) Then Continue For
                                                                              Dim sp = RSStationForces(rr, ei, xs)
                                                                              For j = 0 To xs.Length - 1 : Qd(rc.Direction)(j) += Math.Abs(sp(j)(0)) : Next
                                                                          Next
                                                                          For j = 0 To xs.Length - 1
                                                                              Q(j) = OrthogonalSum(Qd(0)(j), Qd(1)(j), M.CornerOrtho)
                                                                          Next
                                                                          Dim Pmax As Double = 0
                                                                          For j = 0 To xs.Length - 1 : Pmax = Math.Max(Pmax, -N(j) + M.Omega0 * Q(j)) : Next
                                                                          Out(ei) = Pmax
                                                                      End Sub)
        CornerPr = Out
        CornerVg = Vg
    End Sub

End Class
