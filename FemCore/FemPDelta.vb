'Second-order (P-Delta) solutions: the geometric stiffness of every member uses its axial force
Partial Public Class FemStatic

    'ETABS preset "Non-iterative Based on Mass" (measured on P4: the reactions ETABS reports at the diaphragm centre points are exactly
    'W_k u_k / z_k): every joint with lateral mass gets the geometric term  -m g / z  on its two lateral translations (z: elevation
    'above the base); the member stiffness itself carries no geometric part. Applies to all following linear cases and the modal case.
    Public MassGeometric As Boolean
    Private GeomW() As Double

    Public Sub UseMassPDelta()
        ComputeMasses()
        Dim zBase As Double = 0
        Dim restrained = M.Nodes.Where(Function(n) n.Restraint(0) AndAlso n.Restraint(1) AndAlso n.Restraint(2)).ToList()
        If restrained.Count > 0 Then zBase = restrained.Min(Function(n) n.Z)
        ReDim GeomW(M.Nodes.Count * 6 - 1)
        For i = 0 To M.Nodes.Count - 1
            Dim z As Double = M.Nodes(i).Z - zBase
            If z <= 0.000001 Then Continue For
            For d = 0 To 1 : GeomW(i * 6 + d) = MassFull(i * 6 + d) * GravityAccel / z : Next
        Next
        MassGeometric = True
        AxialForces = Nothing
    End Sub

    Friend Sub AddMassGeometric(ByVal K As Double(,))
        If Not MassGeometric Then Return
        For dof = 0 To M.Nodes.Count * 6 - 1
            Dim w As Double = GeomW(dof)
            If w = 0 OrElse MapIdx(dof).Length = 0 Then Continue For
            For a = 0 To MapIdx(dof).Length - 1
                For b = 0 To MapIdx(dof).Length - 1
                    K(MapIdx(dof)(a), MapIdx(dof)(b)) -= MapCoef(dof)(a) * MapCoef(dof)(b) * w
                Next
            Next
        Next
    End Sub

    'Nonlinear static case with geometric nonlinearity "P-Delta": iterates until the axial forces of the equilibrium state no longer change
    Public Function SolveIterative(ByVal Terms As List(Of FemTerm), Optional ByVal MaxIter As Integer = 40, Optional ByVal Tol As Double = 0.000001) As StaticResults
        Dim Tmp As New FemModel With {.DiaphragmCount = M.DiaphragmCount, .AxialFactor = M.AxialFactor, .FlexuralFactor = M.FlexuralFactor}
        Tmp.Nodes = M.Nodes : Tmp.Elements = M.Elements : Tmp.Sections = M.Sections
        Tmp.Patterns.Add(M.CombinePatterns(Terms, "COMBO"))
        Dim N() As Double = Nothing
        Dim Last As StaticResults = Nothing
        For it = 1 To MaxIter
            Dim S As New FemStatic(Tmp) With {.AxialForces = N}
            Dim R = S.Solve()
            If R.Failed Then Return R
            Dim change As Double = 0, scale As Double = 1.0E-30
            If Last IsNot Nothing Then
                For i = 0 To R.U(0).Length - 1
                    change = Math.Max(change, Math.Abs(R.U(0)(i) - Last.U(0)(i)))
                    scale = Math.Max(scale, Math.Abs(R.U(0)(i)))
                Next
            End If
            Last = R
            N = R.Axial(0)
            If it > 1 AndAlso change / scale < Tol Then Exit For
        Next
        Return Last
    End Function

End Class
