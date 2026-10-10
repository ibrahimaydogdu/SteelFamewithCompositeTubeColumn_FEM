Imports MathNet.Numerics.LinearAlgebra

Public Class FemMassSource
    Public SelfMass As Boolean = True
    Public IncludeVertical As Boolean = False                   'vertical (Z) mass; ETABS default: lateral mass only
    Public Patterns As New List(Of FemTerm)                     'load patterns converted to mass (pattern, multiplier)
End Class

Public Class FemSpectrum
    Public Name As String
    Public T() As Double, Sa() As Double                        'period [s], ordinate (multiplied by the case scale factor)
    Public Function Value(ByVal Period As Double) As Double
        If Period <= T(0) Then Return Sa(0)
        For i = 1 To T.Length - 1
            If Period <= T(i) Then
                Dim fr As Double = (Period - T(i - 1)) / (T(i) - T(i - 1))
                Return Sa(i - 1) + fr * (Sa(i) - Sa(i - 1))
            End If
        Next
        Return Sa(Sa.Length - 1)
    End Function
End Class

Public Class FemRSCase
    Public Name As String
    Public Direction As Integer                                 '0 = U1 (global X), 1 = U2, 2 = U3
    Public Spectrum As String
    Public ScaleFactor As Double                                'acceleration scale [m/s2] (includes g)
    Public Damping As Double = 0.05
    Public Eccentricity As Double
End Class

Public Class ModalResults
    Public Omega() As Double
    Public Period() As Double
    Public Phi As Matrix(Of Double)                             'reduced DOF x modes, normalised to unit modal mass
    Public Gamma(,) As Double                                   'modes x 3 (global X, Y, Z): participation factors
    Public MassRatio(,) As Double                               'modes x 3: modal mass / total mass
    Public TotalMass() As Double                                'X, Y, Z
    Public Iterations As Integer
End Class

'Modal analysis (lumped translational masses, subspace iteration) and response spectrum analysis (CQC) on the reduced DOF
Partial Public Class FemStatic
    Public Const GravityAccel As Double = 9.80665

    Private KRed As Double(,)
    Private ChainK() As ChainResult
    Private MassFull() As Double                                'lumped translational masses, 6 per node (only 0..2 used)

    'Assemble the reduced stiffness matrix (no loads)
    Private Sub AssembleStiffness()
        Dim ne As Integer = M.Elements.Count
        ReDim KRed(NFree - 1, NFree - 1)
        ReDim ChainK(ne - 1)
        Dim NoLoads(-1) As List(Of LocalLoad)
        For ei = 0 To ne - 1
            Dim E = M.Elements(ei)
            Dim Tm = TransformationMatrix(E)
            Dim dofs = ElementDofs(E)
            Dim ch = Build(E, NoLoads, 0, M.AxialFactor, M.FlexuralFactor, If(AxialForces Is Nothing, 0.0, AxialForces(ei)))
            ChainK(ei) = ch
            Dim tmp(11, 11) As Double, kg(11, 11) As Double
            For i = 0 To 11 : For j = 0 To 11
                    Dim s As Double = 0
                    For mm = 0 To 11 : s += ch.K(i, mm) * Tm(mm, j) : Next
                    tmp(i, j) = s
                Next : Next
            For i = 0 To 11 : For j = 0 To 11
                    Dim s As Double = 0
                    For mm = 0 To 11 : s += Tm(mm, i) * tmp(mm, j) : Next
                    kg(i, j) = s
                Next : Next
            AddToReduced(KRed, kg, dofs)
        Next
        AddMassGeometric(KRed)
    End Sub

    'Lumped nodal masses: half of the member mass to each end (vertical members over the full length, others over the clear length, as
    'the self weight), plus the vertical component of the loads of the mass source patterns (load / g)
    Private Sub ComputeMasses()
        Dim nn As Integer = M.Nodes.Count
        ReDim MassFull(nn * 6 - 1)
        If M.MassSource.SelfMass Then
            For Each E In M.Elements
                Dim S = E.Section
                Dim Lc As Double = E.Length
                If Math.Abs(E.Axes(0, 2)) <= 0.9 AndAlso E.OffsetI + E.OffsetJ < E.Length Then Lc = E.Length - E.OffsetI - E.OffsetJ
                Dim mass As Double = S.UnitMass * S.A * S.Modifier(0) * E.Modifier(0) * S.Modifier(6) * E.Modifier(6) * Lc
                If S.UnitMass = 0 Then mass = S.UnitWeight / GravityAccel * S.A * S.Modifier(0) * E.Modifier(0) * Lc
                For c = 0 To 2
                    MassFull(E.NI * 6 + c) += mass / 2 : MassFull(E.NJ * 6 + c) += mass / 2
                Next
            Next
        End If
        Dim np As Integer = M.Patterns.Count
        For Each term In M.MassSource.Patterns
            Dim p As Integer = M.PatternIndex(term.Name)
            If p < 0 Then Continue For
            For ei = 0 To M.Elements.Count - 1
                Dim E = M.Elements(ei)
                Dim loads = ElementLoads(ei, E)
                Dim Lst As New List(Of LocalLoad)(loads(p))
                Dim ch = Build(E, New List(Of LocalLoad)() {Lst}, 1, M.AxialFactor, M.FlexuralFactor)
                Dim Tm = TransformationMatrix(E)
                For side = 0 To 1
                    Dim fz As Double = 0
                    For j = 0 To 5
                        Dim s As Double = 0
                        For mm = 0 To 11 : s += Tm(mm, side * 6 + j) * ch.Load(0)(mm) : Next
                        If j = 2 Then fz = s
                    Next
                    Dim nd As Integer = If(side = 0, E.NI, E.NJ)
                    Dim m1 As Double = -fz * term.Factor / GravityAccel
                    For c = 0 To 2 : MassFull(nd * 6 + c) += m1 : Next
                Next
            Next
            For Each nl In M.Patterns(p).NodalLoads
                Dim m1 As Double = -nl.F(2) * term.Factor / GravityAccel
                For c = 0 To 2 : MassFull(nl.Node * 6 + c) += m1 : Next
            Next
        Next
        If Not M.MassSource.IncludeVertical Then
            For i = 0 To nn - 1 : MassFull(i * 6 + 2) = 0 : Next
        End If
        For Each nd In M.Nodes.Where(Function(x) x.AddedMass IsNot Nothing)
            Dim i As Integer = M.Nodes.IndexOf(nd)
            For c = 0 To 2 : MassFull(i * 6 + c) += nd.AddedMass(c) : Next
        Next
    End Sub

    Public Function NodalMass(ByVal Node As Integer, ByVal Dof As Integer) As Double
        If MassFull Is Nothing Then ComputeMasses()
        Return MassFull(Node * 6 + Dof)
    End Function

    'Reduced mass matrix  M_red = T' M T
    Private Function ReducedMass() As Double(,)
        Dim Mr(NFree - 1, NFree - 1) As Double
        For dof = 0 To M.Nodes.Count * 6 - 1
            Dim m1 As Double = MassFull(dof)
            If m1 = 0 OrElse MapIdx(dof).Length = 0 Then Continue For
            For a = 0 To MapIdx(dof).Length - 1
                For b = 0 To MapIdx(dof).Length - 1
                    Mr(MapIdx(dof)(a), MapIdx(dof)(b)) += MapCoef(dof)(a) * MapCoef(dof)(b) * m1
                Next
            Next
        Next
        Return Mr
    End Function

    'Reduced influence vector of a global translation direction (0 X, 1 Y, 2 Z): T q = unit translation of every node
    Private Function InfluenceVector(ByVal Direction As Integer) As Double()
        Dim r(NFree - 1) As Double
        For i = 0 To M.Nodes.Count - 1
            Dim dof As Integer = i * 6 + Direction
            If MapIdx(dof).Length = 1 AndAlso MapCoef(dof)(0) = 1.0 Then
                r(MapIdx(dof)(0)) = 1.0          'free DOF
            ElseIf MapIdx(dof).Length = 2 Then
                r(MapIdx(dof)(0)) = 1.0          'diaphragm master of the translation
            End If
        Next
        Return r
    End Function

    '_______________________________________________________________________________________________
    'Subspace iteration for the lowest nModes of  K phi = w2 M phi  (K reduced stiffness, factorised once)
    Public Function ModalSolve(ByVal nModes As Integer) As ModalResults
        AssembleStiffness()
        ComputeMasses()
        Dim Mr = ReducedMass()
        Dim n As Integer = NFree
        Dim Km = Matrix(Of Double).Build.DenseOfArray(KRed)
        Dim Mm = Matrix(Of Double).Build.DenseOfArray(Mr)
        Dim Chol = Km.Cholesky()
        Dim massDof As Integer = 0
        For i = 0 To n - 1 : If Mr(i, i) > 0 Then massDof += 1
        Next
        nModes = Math.Min(nModes, massDof)
        Dim p As Integer = Math.Min(massDof, Math.Min(2 * nModes, nModes + 8))
        'start vectors: M diagonal, then unit vectors at the DOF with the largest M/K ratio
        Dim X = Matrix(Of Double).Build.Dense(n, p)
        Dim Ratio As New List(Of KeyValuePair(Of Integer, Double))
        For i = 0 To n - 1
            If Mr(i, i) > 0 Then Ratio.Add(New KeyValuePair(Of Integer, Double)(i, Mr(i, i) / KRed(i, i)))
        Next
        Ratio.Sort(Function(a, b) b.Value.CompareTo(a.Value))
        For i = 0 To n - 1 : X(i, 0) = Mr(i, i) : Next
        For k = 1 To p - 1
            If k - 1 < Ratio.Count Then X(Ratio(k - 1).Key, k) = 1.0 Else X(Math.Min(n - 1, k), k) = 1.0
        Next
        Dim Lam(p - 1) As Double, OldLam(p - 1) As Double
        Dim R As New ModalResults
        For it = 1 To 200
            Dim Y = Chol.Solve(Mm * X)                      'K^-1 M X
            Dim Kr = Y.Transpose() * (Mm * X)               'Y' K Y = Y' M X
            Dim Mq = Y.Transpose() * (Mm * Y)
            'symmetric reduced eigenproblem  Kr q = lam Mq q
            Dim Lm As Matrix(Of Double)
            Try
                Lm = Mq.Cholesky().Factor
            Catch
                Throw New InvalidOperationException("the reduced mass matrix is singular (modal analysis needs mass at the free DOF)")
            End Try
            Dim Li = Lm.Inverse()
            Dim C = Li * Kr * Li.Transpose()
            C = (C + C.Transpose()) * 0.5
            Dim Evd = C.Evd(Symmetricity.Symmetric)
            Dim ev = Evd.EigenValues.Real()
            Dim order = Enumerable.Range(0, p).OrderBy(Function(i) ev(i)).ToArray()
            Dim Q = Li.Transpose() * Evd.EigenVectors
            Dim Xn = Matrix(Of Double).Build.Dense(n, p)
            For k = 0 To p - 1
                Lam(k) = ev(order(k))
                Dim col = Y * Q.Column(order(k))
                Xn.SetColumn(k, col)
            Next
            X = Xn
            R.Iterations = it
            Dim conv As Boolean = it > 1
            For k = 0 To nModes - 1
                If Math.Abs(Lam(k) - OldLam(k)) > 1.0E-10 * Math.Abs(Lam(k)) Then conv = False
            Next
            OldLam = CType(Lam.Clone(), Double())
            If conv Then Exit For
        Next
        ReDim R.Omega(nModes - 1) : ReDim R.Period(nModes - 1)
        R.Phi = Matrix(Of Double).Build.Dense(n, nModes)
        For k = 0 To nModes - 1
            R.Omega(k) = Math.Sqrt(Math.Max(Lam(k), 0))
            R.Period(k) = 2 * Math.PI / R.Omega(k)
            Dim v = X.Column(k)
            Dim mn As Double = v.DotProduct(Mm * v)
            R.Phi.SetColumn(k, v / Math.Sqrt(mn))
        Next
        ReDim R.Gamma(nModes - 1, 2) : ReDim R.MassRatio(nModes - 1, 2) : ReDim R.TotalMass(2)
        For d = 0 To 2
            Dim rv = Vector(Of Double).Build.DenseOfArray(InfluenceVector(d))
            R.TotalMass(d) = rv.DotProduct(Mm * rv)
            For k = 0 To nModes - 1
                Dim g As Double = R.Phi.Column(k).DotProduct(Mm * rv)
                R.Gamma(k, d) = g
                R.MassRatio(k, d) = If(R.TotalMass(d) > 0, g * g / R.TotalMass(d), 0)
            Next
        Next
        Return R
    End Function


    '_______________________________________________________________________________________________
    'Response spectrum analysis of one direction: modal amplitudes  A_i = Gamma_i a_i / w_i^2  (a_i = scale factor x spectrum(T_i)),
    'results combined with the CQC rule
    Public Function ResponseSpectrum(ByVal Md As ModalResults, ByVal Rs As FemRSCase) As RSResults
        Dim R As New RSResults
        Dim sp As FemSpectrum = M.Spectra.FirstOrDefault(Function(x) x.Name = Rs.Spectrum)
        If sp Is Nothing Then Throw New InvalidOperationException("response spectrum function " & Rs.Spectrum & " not found")
        Dim nm As Integer = Md.Period.Length, ne As Integer = M.Elements.Count, ndof As Integer = M.Nodes.Count * 6
        Dim Un(nm - 1)() As Double, Rn(nm - 1)() As Double, Fn(nm - 1)()() As Double, BaseF(nm - 1)() As Double, DiaF(nm - 1)() As Double
        For k = 0 To nm - 1
            Dim amp As Double = Md.Gamma(k, Rs.Direction) * Rs.ScaleFactor * sp.Value(Md.Period(k)) / (Md.Omega(k) * Md.Omega(k))
            Dim U(ndof - 1) As Double
            For dof = 0 To ndof - 1
                Dim sm As Double = 0
                For t = 0 To MapIdx(dof).Length - 1 : sm += MapCoef(dof)(t) * Md.Phi(MapIdx(dof)(t), k) : Next
                U(dof) = amp * sm
            Next
            Un(k) = U
            DiaF(k) = New Double(Math.Max(M.DiaphragmCount, 1) - 1) {}
            For i = 0 To M.Nodes.Count - 1
                Dim dk As Integer = M.Nodes(i).Diaphragm
                If dk >= 0 Then DiaF(k)(dk) += MassFull(i * 6 + Rs.Direction) * Md.Omega(k) * Md.Omega(k) * U(i * 6 + Rs.Direction)
            Next
            Dim Rx(ndof - 1) As Double
            Fn(k) = New Double(ne - 1)() {}
            For ei = 0 To ne - 1
                Dim E = M.Elements(ei)
                Dim dofs = ElementDofs(E)
                Dim Tm = TransformationMatrix(E)
                Dim ul(11) As Double
                For i = 0 To 11
                    Dim sm As Double = 0
                    For j = 0 To 11 : sm += Tm(i, j) * U(dofs(j)) : Next
                    ul(i) = sm
                Next
                Dim f(11) As Double
                For i = 0 To 11
                    Dim sm As Double = 0
                    For j = 0 To 11 : sm += ChainK(ei).KEl(i, j) * ul(j) : Next
                    f(i) = sm
                Next
                Fn(k)(ei) = FaceForces(ChainK(ei), ul, -1)
                For i = 0 To 11
                    Dim sm As Double = 0
                    For j = 0 To 11 : sm += Tm(j, i) * f(j) : Next
                    Rx(dofs(i)) += sm
                Next
            Next
            For i = 0 To M.Nodes.Count - 1
                For d = 0 To 5 : If Not M.Nodes(i).Restraint(d) Then Rx(i * 6 + d) = 0
                Next
            Next
            Rn(k) = Rx
            BaseF(k) = New Double(2) {}
            For i = 0 To M.Nodes.Count - 1 : For d = 0 To 2 : BaseF(k)(d) += Rx(i * 6 + d) : Next : Next
        Next
        'CQC
        Dim rho(nm - 1, nm - 1) As Double
        Dim xi As Double = Rs.Damping
        For i = 0 To nm - 1 : For j = 0 To nm - 1
                Dim r1 As Double = Md.Omega(j) / Md.Omega(i)
                rho(i, j) = 8 * xi * xi * (1 + r1) * Math.Pow(r1, 1.5) / ((1 - r1 * r1) ^ 2 + 4 * xi * xi * r1 * (1 + r1) ^ 2)
            Next : Next
        Dim Combine = Function(get1 As Func(Of Integer, Double)) As Double
                          Dim sm As Double = 0
                          Dim v(nm - 1) As Double
                          For k = 0 To nm - 1 : v(k) = get1(k) : Next
                          For i = 0 To nm - 1 : For j = 0 To nm - 1 : sm += rho(i, j) * v(i) * v(j) : Next : Next
                          Return Math.Sqrt(Math.Max(sm, 0))
                      End Function
        R.U = New Double(ndof - 1) {} : R.Reaction = New Double(ndof - 1) {}
        For d = 0 To 2
            Dim d2 As Integer = d
            R.BaseForce(d) = Combine(Function(k) BaseF(k)(d2))
        Next
        For dof = 0 To ndof - 1
            Dim d1 As Integer = dof
            R.U(dof) = Combine(Function(k) Un(k)(d1))
            R.Reaction(dof) = Combine(Function(k) Rn(k)(d1))
        Next
        R.DiaphragmForce = New Double(Math.Max(M.DiaphragmCount, 1) - 1) {}
        For dk = 0 To M.DiaphragmCount - 1
            Dim dk1 As Integer = dk
            R.DiaphragmForce(dk) = Combine(Function(k) DiaF(k)(dk1))
        Next
        R.FaceForce = New Double(ne - 1)() {}
        For ei = 0 To ne - 1
            R.FaceForce(ei) = New Double(11) {}
            For c = 0 To 11
                Dim e1 As Integer = ei, c1 As Integer = c
                R.FaceForce(ei)(c) = Combine(Function(k) Fn(k)(e1)(c1))
            Next
        Next
        If Rs.Eccentricity > 0 AndAlso M.DiaphragmCount > 0 Then AddAccidentalTorsion(R, Rs)
        Return R
    End Function

    'Accidental eccentricity (measured on P4): static torsion of every diaphragm, M_k = F_k x ecc x L_k (F_k: CQC inertial force of the
    'diaphragm in the case direction, L_k: plan dimension of the diaphragm perpendicular to the direction), whose responses are added to
    'the CQC results by absolute value
    Private Sub AddAccidentalTorsion(ByVal R As RSResults, ByVal Rs As FemRSCase)
        Dim nd As Integer = M.DiaphragmCount
        Dim Att(M.Nodes.Count - 1) As Boolean
        For Each el In M.Elements : Att(el.NI) = True : Att(el.NJ) = True : Next
        Dim Tmp As New FemModel With {.DiaphragmCount = M.DiaphragmCount, .AxialFactor = M.AxialFactor, .FlexuralFactor = M.FlexuralFactor}
        Tmp.Nodes = M.Nodes : Tmp.Elements = M.Elements : Tmp.Sections = M.Sections
        For k = 0 To nd - 1
            Dim P As New FemPattern With {.Name = "TQ" & k}
            Dim ni As Integer = Enumerable.Range(0, M.Nodes.Count).First(Function(i) Att(i) AndAlso M.Nodes(i).Diaphragm = k)
            Dim nl As New FemNodalLoad With {.Node = ni}
            nl.F(5) = 1.0
            P.NodalLoads.Add(nl)
            Tmp.Patterns.Add(P)
        Next
        Dim Res = New FemStatic(Tmp).Solve()
        Dim ndof As Integer = M.Nodes.Count * 6
        Dim dU(ndof - 1) As Double, dR(ndof - 1) As Double
        Dim dF(M.Elements.Count - 1)() As Double
        For e = 0 To M.Elements.Count - 1 : dF(e) = New Double(11) {} : Next
        For k = 0 To nd - 1
            Dim xs = M.Nodes.Where(Function(n1) n1.Diaphragm = k AndAlso Att(M.Nodes.IndexOf(n1))).Select(Function(n1) n1.X)
            Dim ys = M.Nodes.Where(Function(n1) n1.Diaphragm = k AndAlso Att(M.Nodes.IndexOf(n1))).Select(Function(n1) n1.Y)
            Dim perp As Double = If(Rs.Direction = 0, ys.Max() - ys.Min(), xs.Max() - xs.Min())
            Dim mk As Double = R.DiaphragmForce(k) * Rs.Eccentricity * perp
            For i = 0 To ndof - 1 : dU(i) += mk * Res.U(k)(i) : dR(i) += mk * Res.Reaction(k)(i) : Next
            For e = 0 To M.Elements.Count - 1
                For c = 0 To 11 : dF(e)(c) += mk * Res.FaceForce(k)(e)(c) : Next
            Next
        Next
        For i = 0 To ndof - 1 : R.U(i) += Math.Abs(dU(i)) : R.Reaction(i) += Math.Abs(dR(i)) : Next
        For e = 0 To M.Elements.Count - 1
            For c = 0 To 11 : R.FaceForce(e)(c) += Math.Abs(dF(e)(c)) : Next
        Next
    End Sub

End Class

Public Class RSResults
    Public U() As Double                    'combined displacement magnitudes (full DOF)
    Public Reaction() As Double
    Public BaseForce(2) As Double           'CQC of the total base forces FX, FY, FZ
    Public DiaphragmForce() As Double       'CQC of the inertial force of every diaphragm in the case direction
    Public FaceForce()() As Double          '[element](12): combined magnitudes at the faces, local axes
End Class
