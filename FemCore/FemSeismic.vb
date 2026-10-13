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
        For ci = 0 To ne - 1
            Dim col = M.Elements(ci)
            If Not IsCol(col) OrElse col.Section.NoDesign Then Continue For
            Dim top As Integer = If(M.Nodes(col.NI).Z > M.Nodes(col.NJ).Z, col.NI, col.NJ)
            Dim above As Integer = -1
            For Each ei In Attached(top)
                Dim e = M.Elements(ei)
                If ei <> ci AndAlso IsCol(e) AndAlso M.Nodes(If(e.NI = top, e.NJ, e.NI)).Z > M.Nodes(top).Z Then above = ei
            Next
            Me.Above(ci) = above
            If above < 0 OrElse M.Elements(above).Section.NoDesign Then Continue For
            Dim ab = M.Elements(above)
            For plane = 0 To 1                                   '0 = major (axis 2 of the column in the plane), 1 = minor
                Dim sumB As Double = 0, nB As Integer = 0
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
                    Dim off As Double = If(atI, b.OffsetI, b.OffsetJ)
                    sumB += ScwbC0 * b.Section.Fy * b.Section.Z33 * (1 + ScwbC1 * off / b.Length)
                    nB += 1
                Next
                If nB = 0 Then Continue For
                Dim sumC As Double = 0
                For Each cc In {ci, above}
                    Dim c = M.Elements(cc)
                    Dim z As Double = If(plane = 0, c.Section.Z33, c.Section.Z22)
                    sumC += z * (c.Section.Fy - PuAisc341(Fem(cc)) / c.Section.A)
                Next
                If sumC <= 0 Then Continue For
                If plane = 0 Then Major(ci) = sumB / sumC Else Minor(ci) = sumB / sumC
            Next
        Next
    End Sub

End Class
