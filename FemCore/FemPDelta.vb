'Second-order (P-Delta) solutions: the geometric stiffness of every member uses its axial force
Partial Public Class FemStatic

    'ETABS preset "Non-iterative Based on Mass": the axial forces of a gravity state (weight of the lumped masses, mass x g on every joint)
    'give the geometric stiffness of all following linear cases
    Public Sub UseMassPDelta()
        ComputeMasses()
        Dim Tmp As New FemModel With {.DiaphragmCount = M.DiaphragmCount, .AxialFactor = M.AxialFactor, .FlexuralFactor = M.FlexuralFactor}
        Tmp.Nodes = M.Nodes : Tmp.Elements = M.Elements : Tmp.Sections = M.Sections
        Dim P As New FemPattern With {.Name = "MASSG"}
        For i = 0 To M.Nodes.Count - 1
            Dim m1 As Double = MassFull(i * 6)
            If m1 = 0 Then Continue For
            Dim nl As New FemNodalLoad With {.Node = i}
            nl.F(2) = -m1 * GravityAccel
            P.NodalLoads.Add(nl)
        Next
        Tmp.Patterns.Add(P)
        Dim Res = New FemStatic(Tmp).Solve()
        AxialForces = Res.Axial(0)
        Dim sc As String = Environment.GetEnvironmentVariable("FEM_PSCALE")
        If Not String.IsNullOrEmpty(sc) Then
            Dim f As Double = Double.Parse(sc, Globalization.CultureInfo.InvariantCulture)
            For i = 0 To AxialForces.Length - 1 : AxialForces(i) *= f : Next
        End If
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
