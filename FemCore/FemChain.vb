Imports MathNet.Numerics.LinearAlgebra

'A load on one element in local axes, positions measured from the I joint along the joint-to-joint length
Public Class LocalLoad
    Public IsPoint As Boolean
    Public X1, X2 As Double             'distributed: start and end; point: X1 = position
    Public Q1(2), Q2(2) As Double       'distributed: intensity components (local 1, 2, 3) at X1 and X2
    Public P(2), M(2) As Double         'point: force and moment components
End Class

'Element with end offsets as ETABS models them (verified on P4): the member is a chain
'  joint I - rigid link (rz a) - flexible stub ((1 - rz) a) - [release] clear span [release] - flexible stub ((1 - rz) b) - rigid link (rz b) - joint J.
'End releases act at the faces (the ends of the clear span), so a pinned beam end transfers its shear to the joint with the lever arm of
'the offset. The chain is condensed to a 12 x 12 matrix between the two joints (local axes). The results keep what is needed to
'recover the forces at the faces of the clear span.
Public Class ChainResult
    Public K(11, 11) As Double                  'joint stiffness, local axes
    Public Load As Double()()                   'per pattern: joint load vector (12)
    Public ClearK(,) As Double                  'clear span stiffness (releases condensed), 12 x 12
    Public ClearLoad As Double()()              'per pattern: condensed equivalent load of the clear span
    Public ClearLength As Double
    'recovery of the face displacements: u(face DOF k) = (InternalValue) or (joint chain DOF); internal = H - G uE
    Public G(,) As Double
    Public H As Double()()
    Public FaceIsInternal(11) As Boolean
    Public FaceIndex(11) As Integer
    Public NInternal As Integer
    Public TrI(5, 5) As Double, TrJ(5, 5) As Double     'joint DOF -> chain end DOF (rigid links)
End Class

Public Module FemChain

    Private Const Eps As Double = 0.000000001

    'rigid link transformation: u_node = u_joint + theta x r, r = (rx, 0, 0)
    Private Sub RigidLink(ByVal Tr As Double(,), ByVal rx As Double, ByVal ry As Double, ByVal rz As Double)
        For i = 0 To 5 : For j = 0 To 5 : Tr(i, j) = If(i = j, 1.0, 0.0) : Next : Next
        'theta x r = (t2 rz - t3 ry, t3 rx - t1 rz, t1 ry - t2 rx)
        Tr(0, 4) = rz : Tr(0, 5) = -ry
        Tr(1, 3) = -rz : Tr(1, 5) = rx
        Tr(2, 3) = ry : Tr(2, 4) = -rx
    End Sub

    'Section properties with the section and object modifiers applied
    Public Function EffectiveSection(ByVal E As FemElement, ByVal AxialFactor As Double, ByVal FlexuralFactor As Double) As FemSection
        Dim S = E.Section
        Dim m(7) As Double
        For i = 0 To 7 : m(i) = S.Modifier(i) * E.Modifier(i) : Next
        Return New FemSection With {.Name = S.Name, .MaterialName = S.MaterialName, .E = S.E, .G = S.G, .A = S.A * m(0) * AxialFactor, .As2 = S.As2 * m(1), .As3 = S.As3 * m(2),
                                    .J = S.J * m(3), .I22 = S.I22 * m(4) * FlexuralFactor, .I33 = S.I33 * m(5) * FlexuralFactor, .UnitWeight = S.UnitWeight * m(7)}
    End Function

    'a: offset at I, b: offset at J (lengths), rz: rigid zone factor
    Public Function Build(ByVal E As FemElement, ByVal Loads As List(Of LocalLoad)(), ByVal NPat As Integer, ByVal AxialFactor As Double, ByVal FlexuralFactor As Double) As ChainResult
        Dim R As New ChainResult
        Dim L As Double = E.Length
        Dim a As Double = Math.Max(0, E.OffsetI), b As Double = Math.Max(0, E.OffsetJ)
        If a + b >= L Then a = 0 : b = 0
        Dim rz As Double = Math.Min(1, Math.Max(0, E.RigidZone))
        Dim xA As Double = rz * a, xB As Double = a, xC As Double = L - b, xD As Double = L - rz * b
        Dim HasS1 As Boolean = xB - xA > Eps, HasS2 As Boolean = xD - xC > Eps
        'unique DOF: 0..5 = chain node A, 6..11 = chain node D, then internal nodes B (if HasS1), C (if HasS2)
        Dim nInt As Integer = (If(HasS1, 6, 0)) + (If(HasS2, 6, 0))
        Dim nTot As Integer = 12 + nInt
        Dim bBase As Integer = 12, cBase As Integer = 12 + If(HasS1, 6, 0)
        Dim DofOfA(5) As Integer, DofOfB(5) As Integer, DofOfC(5) As Integer, DofOfD(5) As Integer
        For i = 0 To 5
            DofOfA(i) = i : DofOfD(i) = 6 + i
            DofOfB(i) = If(HasS1, bBase + i, i)
            DofOfC(i) = If(HasS2, cBase + i, 6 + i)
        Next
        Dim Kg(nTot - 1, nTot - 1) As Double
        Dim F(NPat - 1)() As Double
        For p = 0 To NPat - 1 : F(p) = New Double(nTot - 1) {} : Next
        Dim JointLoad(NPat - 1)() As Double
        For p = 0 To NPat - 1 : JointLoad(p) = New Double(11) {} : Next

        'segments: (x0, x1, dof maps, releases)
        Dim Sec As FemSection = EffectiveSection(E, AxialFactor, FlexuralFactor)
        Dim ClearRel = New List(Of Integer)
        For i = 0 To 5
            If E.ReleaseI(i) Then ClearRel.Add(i)
            If E.ReleaseJ(i) Then ClearRel.Add(6 + i)
        Next
        Dim AddSegment = Sub(x0 As Double, x1 As Double, dofA() As Integer, dofB() As Integer, rel As List(Of Integer), isClear As Boolean)
                             Dim Ls As Double = x1 - x0
                             Dim ks = LocalStiffness(Sec, Ls)
                             Dim map(11) As Integer
                             For i = 0 To 5 : map(i) = dofA(i) : map(6 + i) = dofB(i) : Next
                             Dim fs(NPat - 1)() As Double
                             For p = 0 To NPat - 1
                                 fs(p) = New Double(11) {}
                                 For Each ld In Loads(p)
                                     AddLoadToSegment(fs(p), ld, x0, x1)
                                 Next
                             Next
                             If rel.Count > 0 Then
                                 Dim kk = CType(ks.Clone(), Double(,))
                                 For p = 0 To NPat - 1 : Dim kc = CType(ks.Clone(), Double(,)) : Condense(kc, fs(p), rel) : Next
                                 Condense(kk, New Double(11) {}, rel)
                                 ks = kk
                             End If
                             If isClear Then
                                 R.ClearK = CType(ks.Clone(), Double(,))
                                 R.ClearLength = Ls
                                 R.ClearLoad = New Double(NPat - 1)() {}
                                 For p = 0 To NPat - 1 : R.ClearLoad(p) = CType(fs(p).Clone(), Double()) : Next
                             End If
                             For i = 0 To 11
                                 For j = 0 To 11 : Kg(map(i), map(j)) += ks(i, j) : Next
                             Next
                             For p = 0 To NPat - 1
                                 For i = 0 To 11 : F(p)(map(i)) += fs(p)(i) : Next
                             Next
                         End Sub
        If HasS1 Then AddSegment(xA, xB, DofOfA, DofOfB, New List(Of Integer), False)
        AddSegment(xB, xC, DofOfB, DofOfC, ClearRel, True)
        If HasS2 Then AddSegment(xC, xD, DofOfC, DofOfD, New List(Of Integer), False)

        'face displacement sources: clear span DOF 0..5 = node B, 6..11 = node C
        For i = 0 To 5
            R.FaceIsInternal(i) = HasS1 : R.FaceIndex(i) = If(HasS1, i, i)
            R.FaceIsInternal(6 + i) = HasS2 : R.FaceIndex(6 + i) = If(HasS2, If(HasS1, 6, 0) + i, 6 + i)
        Next
        R.NInternal = nInt

        'condense the internal DOF
        Dim Kee(11, 11) As Double
        For i = 0 To 11 : For j = 0 To 11 : Kee(i, j) = Kg(i, j) : Next : Next
        Dim Fe(NPat - 1)() As Double
        For p = 0 To NPat - 1
            Fe(p) = New Double(11) {}
            For i = 0 To 11 : Fe(p)(i) = F(p)(i) : Next
        Next
        If nInt > 0 Then
            Dim Kii = Matrix(Of Double).Build.Dense(nInt, nInt), Kie = Matrix(Of Double).Build.Dense(nInt, 12)
            For i = 0 To nInt - 1
                For j = 0 To nInt - 1 : Kii(i, j) = Kg(12 + i, 12 + j) : Next
                For j = 0 To 11 : Kie(i, j) = Kg(12 + i, j) : Next
            Next
            Dim Chol = Kii.Cholesky()
            Dim Gm = Chol.Solve(Kie)
            R.G = New Double(nInt - 1, 11) {}
            For i = 0 To nInt - 1 : For j = 0 To 11 : R.G(i, j) = Gm(i, j) : Next : Next
            Dim Corr = Kie.Transpose() * Gm
            For i = 0 To 11 : For j = 0 To 11 : Kee(i, j) -= Corr(i, j) : Next : Next
            R.H = New Double(NPat - 1)() {}
            For p = 0 To NPat - 1
                Dim fi = Vector(Of Double).Build.Dense(nInt)
                For i = 0 To nInt - 1 : fi(i) = F(p)(12 + i) : Next
                Dim h = Chol.Solve(fi)
                R.H(p) = h.ToArray()
                Dim c = Kie.Transpose() * h
                For i = 0 To 11 : Fe(p)(i) -= c(i) : Next
            Next
        Else
            R.H = New Double(NPat - 1)() {}
            For p = 0 To NPat - 1 : R.H(p) = New Double() {} : Next
        End If

        'rigid links to the joints
        RigidLink(R.TrI, xA, E.Ecc2, E.Ecc3) : RigidLink(R.TrJ, xD - L, E.Ecc2, E.Ecc3)
        Dim Tr(11, 11) As Double
        For i = 0 To 5 : For j = 0 To 5 : Tr(i, j) = R.TrI(i, j) : Tr(6 + i, 6 + j) = R.TrJ(i, j) : Next : Next
        Dim tmp(11, 11) As Double
        For i = 0 To 11 : For j = 0 To 11
                Dim s As Double = 0
                For m = 0 To 11 : s += Kee(i, m) * Tr(m, j) : Next
                tmp(i, j) = s
            Next : Next
        For i = 0 To 11 : For j = 0 To 11
                Dim s As Double = 0
                For m = 0 To 11 : s += Tr(m, i) * tmp(m, j) : Next
                R.K(i, j) = s
            Next : Next
        R.Load = New Double(NPat - 1)() {}
        For p = 0 To NPat - 1
            Dim jl(11) As Double
            For i = 0 To 11
                Dim s As Double = 0
                For m = 0 To 11 : s += Tr(m, i) * Fe(p)(m) : Next
                jl(i) = s
            Next
            'loads on the rigid zones act on the joints (force and its moment about the joint)
            For Each ld In Loads(p)
                AddRigidZoneLoad(jl, ld, 0, xA, 0)
                AddRigidZoneLoad(jl, ld, xD, L, L)
            Next
            R.Load(p) = jl
        Next
        Return R
    End Function

    'Equivalent nodal loads of the part of a load that lies in the flexible segment [x0, x1]
    Private Sub AddLoadToSegment(ByVal F As Double(), ByVal ld As LocalLoad, ByVal x0 As Double, ByVal x1 As Double)
        Dim Ls As Double = x1 - x0
        If ld.IsPoint Then
            If ld.X1 > x0 - Eps AndAlso ld.X1 <= x1 + Eps Then
                'a point exactly at the boundary of two segments goes to the first one
                If Math.Abs(ld.X1 - x0) < Eps AndAlso x0 > Eps Then Return
                AddAtPoint(F, Ls, Math.Min(Ls, Math.Max(0, ld.X1 - x0)), ld.P(0), ld.P(1), ld.P(2), ld.M(0), ld.M(1), ld.M(2))
            End If
            Return
        End If
        Dim s0 As Double = Math.Max(ld.X1, x0), s1 As Double = Math.Min(ld.X2, x1)
        If s1 - s0 <= Eps Then Return
        Dim q1(2) As Double, q2(2) As Double
        For c = 0 To 2
            q1(c) = Interp(ld, c, s0) : q2(c) = Interp(ld, c, s1)
        Next
        AddDistributed(F, Ls, s0 - x0, s1 - x0, q1, q2)
    End Sub

    Private Function Interp(ByVal ld As LocalLoad, ByVal c As Integer, ByVal x As Double) As Double
        If ld.X2 - ld.X1 <= 0 Then Return ld.Q1(c)
        Return ld.Q1(c) + (ld.Q2(c) - ld.Q1(c)) * (x - ld.X1) / (ld.X2 - ld.X1)
    End Function

    'Loads in a rigid zone [z0, z1] go to the joint located at xj: force and moment about the joint
    Private Sub AddRigidZoneLoad(ByVal JL As Double(), ByVal ld As LocalLoad, ByVal z0 As Double, ByVal z1 As Double, ByVal xj As Double)
        If z1 - z0 <= Eps Then Return
        Dim off As Integer = If(xj = 0, 0, 6)
        If ld.IsPoint Then
            If ld.X1 >= z0 - Eps AndAlso ld.X1 <= z1 + Eps Then
                If off = 6 AndAlso Math.Abs(ld.X1 - z0) < Eps Then Return      'boundary point belongs to the flexible side
                JL(off + 0) += ld.P(0) : JL(off + 1) += ld.P(1) : JL(off + 2) += ld.P(2)
                JL(off + 3) += ld.M(0) : JL(off + 4) += ld.M(1) + (-(ld.X1 - xj)) * ld.P(2) : JL(off + 5) += ld.M(2) + (ld.X1 - xj) * ld.P(1)
            End If
            Return
        End If
        Dim s0 As Double = Math.Max(ld.X1, z0), s1 As Double = Math.Min(ld.X2, z1)
        If s1 - s0 <= Eps Then Return
        'exact for linear intensity: split into 4 Gauss-like slices of the trapezoid (midpoint rule on 16 slices is exact for linear load)
        Dim n As Integer = 16
        For k = 0 To n - 1
            Dim xa As Double = s0 + (s1 - s0) * k / n, xb As Double = s0 + (s1 - s0) * (k + 1) / n
            Dim xm As Double = 0.5 * (xa + xb)
            Dim q(2) As Double
            For c = 0 To 2 : q(c) = Interp(ld, c, xm) : Next
            Dim dl As Double = xb - xa
            JL(off + 0) += q(0) * dl : JL(off + 1) += q(1) * dl : JL(off + 2) += q(2) * dl
            JL(off + 4) += -(xm - xj) * q(2) * dl : JL(off + 5) += (xm - xj) * q(1) * dl
        Next
    End Sub

    'Forces of the clear span ends (local axes) for given joint displacements (local, 12) and pattern p
    Public Function FaceForces(ByVal R As ChainResult, ByVal ujoint As Double(), ByVal p As Integer) As Double()
        'chain end DOF: u_A = TrI u_I, u_D = TrJ u_J
        Dim uE(11) As Double
        For i = 0 To 5
            Dim sa As Double = 0, sd As Double = 0
            For j = 0 To 5 : sa += R.TrI(i, j) * ujoint(j) : sd += R.TrJ(i, j) * ujoint(6 + j) : Next
            uE(i) = sa : uE(6 + i) = sd
        Next
        Dim uI(R.NInternal - 1) As Double
        For i = 0 To R.NInternal - 1
            Dim s As Double = R.H(p)(i)
            For j = 0 To 11 : s -= R.G(i, j) * uE(j) : Next
            uI(i) = s
        Next
        Dim uf(11) As Double
        For k = 0 To 11
            uf(k) = If(R.FaceIsInternal(k), uI(R.FaceIndex(k)), uE(R.FaceIndex(k)))
        Next
        Dim f(11) As Double
        For i = 0 To 11
            Dim s As Double = 0
            For j = 0 To 11 : s += R.ClearK(i, j) * uf(j) : Next
            f(i) = s - R.ClearLoad(p)(i)
        Next
        Return f
    End Function

End Module
