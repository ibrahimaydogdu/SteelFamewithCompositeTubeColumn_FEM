Imports MathNet.Numerics.LinearAlgebra

'Results of a linear static solution (one entry per load pattern, in the order of FemModel.Patterns)
Public Class StaticResults
    Public U As Double()()              'full displacement vector (6 per node, global axes)
    Public Reaction As Double()()       'reactions at the restrained DOF (global axes; zero elsewhere)
    Public EndForce As Double()()()     '[pattern][element](12): forces of the joints on the element ends, local axes
    Public FaceForce As Double()()()    '[pattern][element](12): forces on the ends of the clear span (faces), local axes
    Public Axial As Double()()          '[pattern][element]: mean axial force of the clear span (tension positive)
    Public Failed As Boolean
    Public Message As String
End Class

'Linear static solver: 3D frame, rigid diaphragms (one per story, master DOF Ux Uy Rz at the centroid), restraints, releases.
'The DOF map keeps for every node DOF the reduced DOF it depends on (a free DOF: itself; a diaphragm slave: the three masters).
Partial Public Class FemStatic
    Private ReadOnly M As FemModel
    Private MapIdx()() As Integer
    Private MapCoef()() As Double
    Public NFree As Integer
    Private Owner As List(Of String)
    Public AxialForces() As Double                  'axial force per element for the geometric stiffness (Nothing: linear)

    Public Sub New(ByVal Model As FemModel)
        M = Model
        BuildMap()
    End Sub

    Private Sub BuildMap()
        Dim nn As Integer = M.Nodes.Count
        ReDim MapIdx(nn * 6 - 1) : ReDim MapCoef(nn * 6 - 1)
        NFree = 0
        Owner = New List(Of String)
        'joints without any element (e.g. the diaphragm centre points ETABS adds) take no part in the model
        Dim Attached(nn - 1) As Boolean
        For Each el In M.Elements : Attached(el.NI) = True : Attached(el.NJ) = True : Next
        'diaphragm masters
        Dim Master(M.DiaphragmCount - 1, 2) As Integer, Xc(M.DiaphragmCount - 1) As Double, Yc(M.DiaphragmCount - 1) As Double, Cnt(M.DiaphragmCount - 1) As Integer
        For Each nd In M.Nodes
            If nd.Diaphragm >= 0 AndAlso Attached(M.Nodes.IndexOf(nd)) Then
                Xc(nd.Diaphragm) += nd.X : Yc(nd.Diaphragm) += nd.Y : Cnt(nd.Diaphragm) += 1
            End If
        Next
        For k = 0 To M.DiaphragmCount - 1
            If Cnt(k) > 0 Then Xc(k) /= Cnt(k) : Yc(k) /= Cnt(k)
            For c = 0 To 2 : Master(k, c) = NFree : NFree += 1 : Owner.Add("diaphragm " & k & " master " & {"Ux", "Uy", "Rz"}(c)) : Next
        Next
        For i = 0 To nn - 1
            Dim nd = M.Nodes(i)
            For d = 0 To 5
                Dim dof As Integer = i * 6 + d
                If nd.Restraint(d) OrElse (Not Attached(i) AndAlso Not (nd.Diaphragm >= 0 AndAlso (d = 0 OrElse d = 1 OrElse d = 5))) Then
                    MapIdx(dof) = New Integer() {} : MapCoef(dof) = New Double() {}
                ElseIf nd.Diaphragm >= 0 AndAlso (d = 0 OrElse d = 1 OrElse d = 5) Then
                    Dim k As Integer = nd.Diaphragm
                    If d = 0 Then
                        MapIdx(dof) = {Master(k, 0), Master(k, 2)} : MapCoef(dof) = {1.0, -(nd.Y - Yc(k))}
                    ElseIf d = 1 Then
                        MapIdx(dof) = {Master(k, 1), Master(k, 2)} : MapCoef(dof) = {1.0, nd.X - Xc(k)}
                    Else
                        MapIdx(dof) = {Master(k, 2)} : MapCoef(dof) = {1.0}
                    End If
                Else
                    MapIdx(dof) = {NFree} : MapCoef(dof) = {1.0} : NFree += 1 : Owner.Add("node " & nd.Name & " " & {"U1", "U2", "U3", "R1", "R2", "R3"}(d))
                End If
            Next
        Next
    End Sub

    Private Function ElementDofs(ByVal E As FemElement) As Integer()
        Dim d(11) As Integer
        For i = 0 To 5 : d(i) = E.NI * 6 + i : d(6 + i) = E.NJ * 6 + i : Next
        Return d
    End Function

    'Loads of all patterns on one element in local axes (the part of the member weight that ETABS counts: vertical members over the full
    'joint-to-joint length, other members over the clear length between the end offsets - measured on P4)
    Private Function ElementLoads(ByVal ei As Integer, ByVal E As FemElement) As List(Of LocalLoad)()
        Dim np As Integer = M.Patterns.Count
        Dim Res(np - 1) As List(Of LocalLoad)
        Dim L As Double = E.Length
        For p = 0 To np - 1
            Dim Pt = M.Patterns(p)
            Res(p) = New List(Of LocalLoad)
            If Pt.SelfWeightMultiplier <> 0 Then
                Dim w As Double = Pt.SelfWeightMultiplier * EffectiveSection(E, 1.0, 1.0).WeightPerLength
                Dim ld As New LocalLoad With {.IsPoint = False, .X1 = 0, .X2 = L}
                For c = 0 To 2 : ld.Q1(c) = -w * E.Axes(c, 2) : ld.Q2(c) = ld.Q1(c) : Next
                If Math.Abs(E.Axes(0, 2)) <= 0.9 AndAlso E.OffsetI + E.OffsetJ < L Then ld.X1 = E.OffsetI : ld.X2 = L - E.OffsetJ
                Res(p).Add(ld)
            End If
            For Each dl In Pt.DistLoads
                If dl.Element <> ei Then Continue For
                Dim ld As New LocalLoad With {.IsPoint = False}
                ld.X1 = If(dl.Relative, dl.D1 * L, dl.D1) : ld.X2 = If(dl.Relative, dl.D2 * L, dl.D2)
                Dim q1() As Double = LoadVector(E, dl.Dir, dl.IsGlobal, dl.V1), q2() As Double = LoadVector(E, dl.Dir, dl.IsGlobal, dl.V2)
                For c = 0 To 2 : ld.Q1(c) = q1(c) : ld.Q2(c) = q2(c) : Next
                Res(p).Add(ld)
            Next
            For Each pl In Pt.PointLoads
                If pl.Element <> ei Then Continue For
                Dim ld As New LocalLoad With {.IsPoint = True, .X1 = If(pl.Relative, pl.Dist * L, pl.Dist)}
                Dim v() As Double = LoadVector(E, pl.Dir, pl.IsGlobal, pl.Value)
                For c = 0 To 2
                    If pl.IsMoment Then ld.M(c) = v(c) Else ld.P(c) = v(c)
                Next
                Res(p).Add(ld)
            Next
        Next
        Return Res
    End Function

    'Local components of a load of magnitude v in an ETABS direction code (1..3 local, 4..6 global X Y Z, 10 gravity)
    Private Shared Function LoadVector(ByVal E As FemElement, ByVal Dir As Integer, ByVal IsGlobal As Boolean, ByVal v As Double) As Double()
        Dim q(2) As Double
        If Dir >= 1 AndAlso Dir <= 3 Then
            q(Dir - 1) = v
        ElseIf Dir >= 4 AndAlso Dir <= 6 Then
            For c = 0 To 2 : q(c) = v * E.Axes(c, Dir - 4) : Next
        ElseIf Dir = 10 Then
            For c = 0 To 2 : q(c) = -v * E.Axes(c, 2) : Next
        Else
            Throw New NotSupportedException("load direction code " & Dir & " is not supported")
        End If
        Return q
    End Function

    Public Function Solve() As StaticResults
        Dim R As New StaticResults
        Dim np As Integer = M.Patterns.Count, ne As Integer = M.Elements.Count
        R.U = New Double(np - 1)() {} : R.Reaction = New Double(np - 1)() {} : R.EndForce = New Double(np - 1)()() {} : R.FaceForce = New Double(np - 1)()() {} : R.Axial = New Double(np - 1)() {}
        Dim K(NFree - 1, NFree - 1) As Double
        Dim Fred(np - 1)() As Double
        For p = 0 To np - 1 : Fred(p) = New Double(NFree - 1) {} : Next
        Dim Chains(ne - 1) As ChainResult, TT As Double()(,) = New Double(ne - 1)(,) {}

        For ei = 0 To ne - 1
            Dim E = M.Elements(ei)
            Dim Tm = TransformationMatrix(E)
            Dim dofs = ElementDofs(E)
            Dim ch = Build(E, ElementLoads(ei, E), np, M.AxialFactor, M.FlexuralFactor, If(AxialForces Is Nothing, 0.0, AxialForces(ei)))
            Chains(ei) = ch : TT(ei) = Tm
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
            AddToReduced(K, kg, dofs)
            For p = 0 To np - 1
                Dim fg(11) As Double
                Dim anyLoad As Boolean = False
                For i = 0 To 11
                    Dim s As Double = 0
                    For mm = 0 To 11 : s += Tm(mm, i) * ch.Load(p)(mm) : Next
                    fg(i) = s : If s <> 0 Then anyLoad = True
                Next
                If Not anyLoad Then Continue For
                For a = 0 To 11
                    For t = 0 To MapIdx(dofs(a)).Length - 1 : Fred(p)(MapIdx(dofs(a))(t)) += MapCoef(dofs(a))(t) * fg(a) : Next
                Next
            Next
        Next
        AddMassGeometric(K)
        'nodal loads
        For p = 0 To np - 1
            For Each nl In M.Patterns(p).NodalLoads
                For d = 0 To 5
                    Dim dof As Integer = nl.Node * 6 + d
                    For t = 0 To MapIdx(dof).Length - 1 : Fred(p)(MapIdx(dof)(t)) += MapCoef(dof)(t) * nl.F(d) : Next
                Next
            Next
        Next

        Dim q(np - 1)() As Double
        Try
            Dim Kd = Matrix(Of Double).Build.DenseOfArray(K)
            Dim Rhs = Matrix(Of Double).Build.Dense(NFree, np)
            For p = 0 To np - 1 : For i = 0 To NFree - 1 : Rhs(i, p) = Fred(p)(i) : Next : Next
            Dim Sol = Kd.Cholesky().Solve(Rhs)
            For p = 0 To np - 1
                q(p) = New Double(NFree - 1) {}
                For i = 0 To NFree - 1 : q(p)(i) = Sol(i, p) : Next
            Next
        Catch ex As Exception
            R.Failed = True : R.Message = "stiffness matrix is not positive definite (unstable model?): " & ex.Message & "; first non-positive pivot: " & FirstBadPivot(K)
            Return R
        End Try

        For p = 0 To np - 1
            Dim U(M.Nodes.Count * 6 - 1) As Double
            For dof = 0 To U.Length - 1
                Dim s As Double = 0
                For t = 0 To MapIdx(dof).Length - 1 : s += MapCoef(dof)(t) * q(p)(MapIdx(dof)(t)) : Next
                U(dof) = s
            Next
            R.U(p) = U
            Dim Rx(U.Length - 1) As Double
            R.EndForce(p) = New Double(ne - 1)() {} : R.FaceForce(p) = New Double(ne - 1)() {} : R.Axial(p) = New Double(ne - 1) {}
            For ei = 0 To ne - 1
                Dim E = M.Elements(ei)
                Dim dofs = ElementDofs(E)
                Dim ul(11) As Double
                For i = 0 To 11
                    Dim s As Double = 0
                    For j = 0 To 11 : s += TT(ei)(i, j) * U(dofs(j)) : Next
                    ul(i) = s
                Next
                Dim f(11) As Double
                For i = 0 To 11
                    Dim s As Double = 0
                    For j = 0 To 11 : s += Chains(ei).KEl(i, j) * ul(j) : Next
                    f(i) = s - Chains(ei).Load(p)(i)
                Next
                R.EndForce(p)(ei) = f
                R.FaceForce(p)(ei) = FaceForces(Chains(ei), ul, p)
                R.Axial(p)(ei) = 0.5 * (R.FaceForce(p)(ei)(6) - R.FaceForce(p)(ei)(0))
                For i = 0 To 11
                    Dim s As Double = 0
                    For j = 0 To 11 : s += TT(ei)(j, i) * f(j) : Next
                    Rx(dofs(i)) += s
                Next
            Next
            'reaction = sum of the end forces on the joint - applied nodal load
            For Each nl In M.Patterns(p).NodalLoads
                For d = 0 To 5 : Rx(nl.Node * 6 + d) -= nl.F(d) : Next
            Next
            For i = 0 To M.Nodes.Count - 1
                For d = 0 To 5 : If Not M.Nodes(i).Restraint(d) Then Rx(i * 6 + d) = 0
                Next
            Next
            R.Reaction(p) = Rx
        Next
        Return R
    End Function

    'Dense Cholesky pivots of the reduced matrix: the first non-positive one points to the unstable DOF
    Private Function FirstBadPivot(ByVal K As Double(,)) As String
        Dim n As Integer = NFree
        Dim L(n - 1, n - 1) As Double
        For i = 0 To n - 1
            For j = 0 To i
                Dim sm As Double = K(i, j)
                For k2 = 0 To j - 1 : sm -= L(i, k2) * L(j, k2) : Next
                If i = j Then
                    If sm <= 1.0E-9 * Math.Abs(K(i, i)) Then Return "reduced DOF " & i & " (" & Owner(i) & "), pivot " & sm.ToString("0.0E+00") & " diagonal " & K(i, i).ToString("0.0E+00")
                    L(i, i) = Math.Sqrt(sm)
                Else
                    L(i, j) = sm / L(j, j)
                End If
            Next
        Next
        Return "none found"
    End Function

    Private Sub AddToReduced(ByVal K As Double(,), ByVal kg As Double(,), ByVal dofs As Integer())
        For a = 0 To 11
            Dim ia = MapIdx(dofs(a)), ca = MapCoef(dofs(a))
            If ia.Length = 0 Then Continue For
            For b = 0 To 11
                Dim v As Double = kg(a, b)
                If v = 0 Then Continue For
                Dim ib = MapIdx(dofs(b)), cb = MapCoef(dofs(b))
                For s = 0 To ia.Length - 1
                    For t = 0 To ib.Length - 1
                        K(ia(s), ib(t)) += ca(s) * cb(t) * v
                    Next
                Next
            Next
        Next
    End Sub

End Class
