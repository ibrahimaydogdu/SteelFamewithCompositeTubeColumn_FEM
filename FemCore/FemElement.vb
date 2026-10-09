Imports MathNet.Numerics.LinearAlgebra

'3D frame element (Timoshenko beam: shear deformation through As2 / As3, as ETABS), 12 DOF in local axes:
'node I: u1 u2 u3 r1 r2 r3 (0..5), node J (6..11). Plane 1-2 bends about local axis 3 (I33, As2), plane 1-3 about axis 2 (I22, As3).
Public Module FemElementMath

    Public Function LocalStiffness(ByVal S As FemSection, ByVal L As Double) As Double(,)
        Dim k(11, 11) As Double
        Dim EA As Double = S.E * S.A / L, GJ As Double = S.G * S.J / L
        k(0, 0) = EA : k(0, 6) = -EA : k(6, 0) = -EA : k(6, 6) = EA
        k(3, 3) = GJ : k(3, 9) = -GJ : k(9, 3) = -GJ : k(9, 9) = GJ
        'plane 1-2: DOF u2 (1), r3 (5), u2 (7), r3 (11)
        Dim EI As Double = S.E * S.I33
        Dim Phi As Double = If(S.As2 > 0, 12 * EI / (S.G * S.As2 * L * L), 0)
        Dim c As Double = EI / (L * L * L * (1 + Phi))
        Dim d() As Integer = {1, 5, 7, 11}
        Dim m(,) As Double = {{12 * c, 6 * L * c, -12 * c, 6 * L * c},
                              {6 * L * c, (4 + Phi) * L * L * c, -6 * L * c, (2 - Phi) * L * L * c},
                              {-12 * c, -6 * L * c, 12 * c, -6 * L * c},
                              {6 * L * c, (2 - Phi) * L * L * c, -6 * L * c, (4 + Phi) * L * L * c}}
        For a = 0 To 3 : For b = 0 To 3 : k(d(a), d(b)) += m(a, b) : Next : Next
        'plane 1-3: DOF u3 (2), r2 (4), u3 (8), r2 (10); rotation about axis 2 is the negative slope
        EI = S.E * S.I22
        Phi = If(S.As3 > 0, 12 * EI / (S.G * S.As3 * L * L), 0)
        c = EI / (L * L * L * (1 + Phi))
        d = {2, 4, 8, 10}
        Dim n(,) As Double = {{12 * c, -6 * L * c, -12 * c, -6 * L * c},
                              {-6 * L * c, (4 + Phi) * L * L * c, 6 * L * c, (2 - Phi) * L * L * c},
                              {-12 * c, 6 * L * c, 12 * c, 6 * L * c},
                              {-6 * L * c, (2 - Phi) * L * L * c, 6 * L * c, (4 + Phi) * L * L * c}}
        For a = 0 To 3 : For b = 0 To 3 : k(d(a), d(b)) += n(a, b) : Next : Next
        Return k
    End Function

    'Indices of the released local DOF of an element (0..11)
    Public Function ReleasedDofs(ByVal E As FemElement) As List(Of Integer)
        Dim R As New List(Of Integer)
        For i = 0 To 5
            If E.ReleaseI(i) Then R.Add(i)
            If E.ReleaseJ(i) Then R.Add(6 + i)
        Next
        Return R
    End Function

    'Static condensation of the released DOF: K' = Kaa - Kar Krr^-1 Kra (released rows and columns become zero); the same for the load vector
    Public Sub Condense(ByVal K As Double(,), ByVal F As Double(), ByVal Released As List(Of Integer))
        If Released.Count = 0 Then Return
        Dim Kept As New List(Of Integer)
        For i = 0 To 11 : If Not Released.Contains(i) Then Kept.Add(i)
        Next
        Dim nr As Integer = Released.Count, na As Integer = Kept.Count
        Dim Krr = Matrix(Of Double).Build.Dense(nr, nr), Kar = Matrix(Of Double).Build.Dense(na, nr)
        For i = 0 To nr - 1 : For j = 0 To nr - 1 : Krr(i, j) = K(Released(i), Released(j)) : Next : Next
        For i = 0 To na - 1 : For j = 0 To nr - 1 : Kar(i, j) = K(Kept(i), Released(j)) : Next : Next
        Dim X = Krr.Solve(Kar.Transpose())              'Krr^-1 Kra
        Dim Corr = Kar * X
        Dim fr = Vector(Of Double).Build.Dense(nr)
        For i = 0 To nr - 1 : fr(i) = F(Released(i)) : Next
        Dim fc = Kar * Krr.Solve(fr)
        For i = 0 To na - 1
            For j = 0 To na - 1 : K(Kept(i), Kept(j)) -= Corr(i, j) : Next
            F(Kept(i)) -= fc(i)
        Next
        For Each r In Released
            For j = 0 To 11 : K(r, j) = 0 : K(j, r) = 0 : Next
            F(r) = 0
        Next
    End Sub

    'Global-to-local transformation of the 12 element DOF (4 blocks of the 3x3 direction cosine matrix)
    Public Function TransformationMatrix(ByVal E As FemElement) As Double(,)
        Dim T(11, 11) As Double
        For blk = 0 To 3
            For i = 0 To 2 : For j = 0 To 2 : T(blk * 3 + i, blk * 3 + j) = E.Axes(i, j) : Next : Next
        Next
        Return T
    End Function

    '3-point Gauss-Legendre rule is exact for the cubic shape functions times a linear load; 4 points are used
    Private ReadOnly GaussX() As Double = {-0.8611363115940526, -0.3399810435848563, 0.3399810435848563, 0.8611363115940526}
    Private ReadOnly GaussW() As Double = {0.3478548451374538, 0.6521451548625462, 0.6521451548625462, 0.3478548451374538}

    'Equivalent nodal forces (local axes) of a load that varies linearly from q1 at x1 to q2 at x2 (distances from the I end);
    'q(c) = intensity in local direction c (0 = axial, 1 = local 2, 2 = local 3), per unit length
    Public Sub AddDistributed(ByVal F As Double(), ByVal L As Double, ByVal x1 As Double, ByVal x2 As Double, ByVal q1() As Double, ByVal q2() As Double)
        If x2 - x1 <= 0 Then Return
        For g = 0 To 3
            Dim x As Double = 0.5 * (x1 + x2) + 0.5 * (x2 - x1) * GaussX(g)
            Dim w As Double = 0.5 * (x2 - x1) * GaussW(g)
            Dim t As Double = (x - x1) / (x2 - x1)
            Dim q(2) As Double
            For c = 0 To 2 : q(c) = q1(c) + (q2(c) - q1(c)) * t : Next
            AddAtPoint(F, L, x, q(0) * w, q(1) * w, q(2) * w, 0, 0, 0)
        Next
    End Sub

    'Equivalent nodal forces of a point force (p1, p2, p3) and moment (m1, m2, m3) at distance x from the I end (local axes)
    Public Sub AddAtPoint(ByVal F As Double(), ByVal L As Double, ByVal x As Double, ByVal p1 As Double, ByVal p2 As Double, ByVal p3 As Double,
                          ByVal m1 As Double, ByVal m2 As Double, ByVal m3 As Double)
        Dim s As Double = x / L
        Dim s2 As Double = s * s, s3 As Double = s2 * s
        Dim N1 As Double = 1 - 3 * s2 + 2 * s3, N2 As Double = L * (s - 2 * s2 + s3), N3 As Double = 3 * s2 - 2 * s3, N4 As Double = L * (-s2 + s3)
        'axial and torsion
        F(0) += p1 * (1 - s) : F(6) += p1 * s
        F(3) += m1 * (1 - s) : F(9) += m1 * s
        'plane 1-2: v and r3
        F(1) += p2 * N1 : F(5) += p2 * N2 : F(7) += p2 * N3 : F(11) += p2 * N4
        'plane 1-3: w and r2 (rotation = -slope)
        F(2) += p3 * N1 : F(4) -= p3 * N2 : F(8) += p3 * N3 : F(10) -= p3 * N4
        'moment about axis 3 / 2 at x: derivative of the shape functions
        Dim d1 As Double = (-6 * s + 6 * s2) / L, d2 As Double = 1 - 4 * s + 3 * s2, d3 As Double = (6 * s - 6 * s2) / L, d4 As Double = -2 * s + 3 * s2
        F(1) += m3 * d1 : F(5) += m3 * d2 : F(7) += m3 * d3 : F(11) += m3 * d4
        F(2) -= m2 * d1 : F(4) += m2 * d2 : F(8) -= m2 * d3 : F(10) += m2 * d4
    End Sub

End Module
