'Second-order (P-Delta) solutions: the geometric stiffness of every member uses its axial force
Partial Public Class FemStatic

    'ETABS preset "Non-iterative Based on Mass" (measured on P4: the reactions ETABS reports at the diaphragm centre points are exactly
    'W_k u_k / z_k): every joint with lateral mass gets the geometric term  -m g / z  on its two lateral translations (z: elevation
    'above the base); the member stiffness itself carries no geometric part. Applies to all following linear cases and the modal case.
    Public MassGeometric As Boolean
    Public LastIterSolver As FemStatic
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

    Friend Sub AddMassGeometric(ByVal K As SkylineMatrix)
        If Not MassGeometric Then Return
        For dof = 0 To M.Nodes.Count * 6 - 1
            Dim w As Double = GeomW(dof)
            If w = 0 OrElse MapIdx(dof).Length = 0 Then Continue For
            For a = 0 To MapIdx(dof).Length - 1
                For b = 0 To MapIdx(dof).Length - 1
                    If MapIdx(dof)(a) >= MapIdx(dof)(b) Then K.Add(MapIdx(dof)(a), MapIdx(dof)(b), -MapCoef(dof)(a) * MapCoef(dof)(b) * w)
                Next
            Next
        Next
    End Sub

    'Nonlinear static case with geometric nonlinearity "P-Delta": iterates until the axial forces of the equilibrium state no longer change
    'Base: the linear solution of the patterns (optional): its axial forces start the iteration (one solution less)
    Public Function SolveIterative(ByVal Terms As List(Of FemTerm), Optional ByVal Base As StaticResults = Nothing, Optional ByVal MaxIter As Integer = 40, Optional ByVal Tol As Double = 0.000001, Optional ByVal Lumped As Boolean = False) As StaticResults
        Dim Tmp As New FemModel With {.DiaphragmCount = M.DiaphragmCount, .AxialFactor = M.AxialFactor, .FlexuralFactor = M.FlexuralFactor}
        Tmp.Nodes = M.Nodes : Tmp.Elements = M.Elements : Tmp.Sections = M.Sections
        Tmp.Patterns.Add(M.CombinePatterns(Terms, "COMBO"))
        Dim N() As Double = Nothing
        If Base IsNot Nothing AndAlso Not Base.Failed Then
            N = New Double(M.Elements.Count - 1) {}
            For Each t In Terms
                Dim p As Integer = M.PatternIndex(t.Name)
                If p < 0 Then Continue For
                For ei = 0 To N.Length - 1 : N(ei) += t.Factor * Base.Axial(p)(ei) : Next
            Next
        End If
        Dim Last As StaticResults = Nothing
        Dim Preset = New FemStatic(Tmp).LoadsOfAllElements()
        Dim oldElastic As Boolean = ElasticForces
        Dim oldLumped As Boolean = GeometricLumped
        GeometricLumped = Lumped          'lumped P/L geometric stiffness: P-Delta without the member buckling (P-delta) of the consistent matrix
        ElasticForces = False           'nonlinear static P-Delta (ETABS): member forces and reactions are the forces of the deformed equilibrium state (K + Kg)
        Try
        For it = 1 To MaxIter
            FemProfile.Hit("pdelta.iteration")
            Dim S As New FemStatic(Tmp) With {.AxialForces = N, .PresetLoads = Preset}
            LastIterSolver = S
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
        Finally
            ElasticForces = oldElastic
            GeometricLumped = oldLumped
        End Try
        Return Last
    End Function


    'Steel design of the combinations that refer to nonlinear (P-Delta) static cases: each has its own iterative solution
    Public Function DesignNonlinear(ByVal Combos As List(Of FemCombo), ByVal Prefs As DesignPrefs, ByVal Cur() As MemberRatios) As MemberRatios()
        For Each cmb In Combos.Where(Function(c) c.Nonlinear)
            Dim R = SolveIterative(cmb.Terms)
            If R.Failed Then Continue For
            Dim solver = LastIterSolver
            Dim sglCombo As New FemCombo With {.Name = cmb.Name}
            sglCombo.Terms.Add(New FemTerm With {.Name = "COMBO", .Factor = 1.0})
            For ei = 0 To M.Elements.Count - 1
                Dim r1 = solver.DesignElement(R, ei, New List(Of FemCombo) From {sglCombo}, Prefs)
                Dim c0 = Cur(ei)
                If r1.PMM > c0.PMM Then c0.PMM = r1.PMM : c0.P = r1.P : c0.MMaj = r1.MMaj : c0.MMin = r1.MMin : c0.PMMCombo = cmb.Name : c0.PMMStation = r1.PMMStation
                If r1.VMaj > c0.VMaj Then c0.VMaj = r1.VMaj : c0.VMajCombo = cmb.Name
                c0.VMin = Math.Max(c0.VMin, r1.VMin)
            Next
        Next
        Return Cur
    End Function

End Class
