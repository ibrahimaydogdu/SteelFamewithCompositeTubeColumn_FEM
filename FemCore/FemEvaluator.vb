Imports System.Diagnostics

'Own analysis and design pipeline used by the optimizer instead of ETABS: sections of the design groups -> static, modal and response
'spectrum solutions -> joint displacements (drift) and steel design ratios (AISC 360-22 + strong column - weak beam).
Public Class FemEvaluation
    Public Solver As FemStatic
    Public StaticRes As StaticResults
    Public RS As New Dictionary(Of String, RSResults)
    Public Members() As MemberRatios
    Public BCMajor() As Double
    Public BCMinor() As Double
    Public Above() As Integer               'column above every column (-1: none)
    Public Failed As Boolean
    Public Message As String
    Friend Nonlinear As New Dictionary(Of String, Tuple(Of StaticResults, FemStatic))
End Class

Public Class FemEvaluator
    Public ReadOnly Model As FemModel
    Public MassPDelta As Boolean = True             'ETABS preset "Non-iterative Based on Mass" for the linear cases
    Public ReadOnly Timing As New Dictionary(Of String, Stopwatch)
    Private ReadOnly GroupElements As New Dictionary(Of String, List(Of Integer))

    Public Sub New(ByVal M As FemModel)
        Model = M
        For i = 0 To M.Elements.Count - 1
            Dim g As String = M.Elements(i).Group
            If g Is Nothing Then Continue For
            If Not GroupElements.ContainsKey(g) Then GroupElements(g) = New List(Of Integer)
            GroupElements(g).Add(i)
        Next
    End Sub

    Private Function Clock(ByVal Name As String) As Stopwatch
        Dim sw As Stopwatch = Nothing
        If Not Timing.TryGetValue(Name, sw) Then sw = New Stopwatch : Timing(Name) = sw
        Return sw
    End Function

    Public Function TimingReport() As String
        Return String.Join("; ", Timing.Select(Function(kv) kv.Key & " " & kv.Value.ElapsedMilliseconds & " ms"))
    End Function

    Private ElementByName As Dictionary(Of String, Integer)

    Public Function ElementIndex(ByVal Name As String) As Integer
        If ElementByName Is Nothing Then
            ElementByName = New Dictionary(Of String, Integer)
            For i = 0 To Model.Elements.Count - 1 : ElementByName(Model.Elements(i).Name) = i : Next
        End If
        Dim k As Integer
        If ElementByName.TryGetValue(Name, k) Then Return k
        Return -1
    End Function

    Public Function ElementsOfGroup(ByVal Group As String) As List(Of Integer)
        Dim L As List(Of Integer) = Nothing
        If GroupElements.TryGetValue(Group, L) Then Return L
        Return New List(Of Integer)
    End Function

    'The members get the section
    Public Sub AssignSection(ByVal Members As List(Of Integer), ByVal S As FemSection)
        For Each ei In Members
            Model.Elements(ei).Section = S
        Next
    End Sub

    'AISC 341 design errors of the members of the moment frame: sections that are not seismically compact (Ca from the largest axial
    'force of the design combinations) and beams with Lb / ry above the limit
    Public Function SeismicErrors(ByVal Ev As FemEvaluation, ByVal High As Boolean) As Boolean()
        Dim Res(Model.Elements.Count - 1) As Boolean
        For ei = 0 To Model.Elements.Count - 1
            Dim el = Model.Elements(ei)
            If el.Section Is Nothing OrElse el.Section.A <= 0 Then Continue For
            Dim isCol As Boolean = Math.Abs(el.Axes(0, 2)) > 0.9
            Dim moment As Boolean = isCol OrElse Not (el.ReleaseI(4) OrElse el.ReleaseI(5) OrElse el.ReleaseJ(4) OrElse el.ReleaseJ(5))
            If Not moment Then Continue For
            Dim Ca As Double = Ev.Members(ei).PuMax / (0.9 * el.Section.Fy * el.Section.A)
            Dim Bad As Boolean = Not SteelDesign.SeismicCompact(el.Section, High, Ca)
            If Not isCol Then
                Dim Lb As Double = If(el.UnbracedLTB > 0, el.UnbracedLTB, el.Length - el.OffsetI - el.OffsetJ)
                Bad = Bad OrElse Not SteelDesign.LbRyOk(el.Section, Lb, High)
            End If
            Res(ei) = Bad
        Next
        Return Res
    End Function

    'All members of the group get the section
    Public Sub AssignSection(ByVal Group As String, ByVal S As FemSection)
        For Each ei In ElementsOfGroup(Group)
            Model.Elements(ei).Section = S
        Next
    End Sub

    'Static (all patterns), modal and response spectrum solutions
    Public Function Analyze() As FemEvaluation
        Dim R As New FemEvaluation
        Dim c = Clock("Analysis") : c.Start()
        Try
            Dim S As New FemStatic(Model)
            If MassPDelta Then S.UseMassPDelta()
            R.Solver = S
            R.StaticRes = S.Solve()
            If R.StaticRes.Failed Then R.Failed = True : R.Message = R.StaticRes.Message : Return R
            If Model.RSCases.Count > 0 Then
                Dim Md = S.ModalSolve(Model.ModalCount)
                For Each rc In Model.RSCases : R.RS(rc.Name) = S.ResponseSpectrum(Md, rc) : Next
            End If
        Catch ex As Exception
            R.Failed = True : R.Message = ex.Message
        Finally
            c.Stop()
        End Try
        Return R
    End Function

    'Nonlinear (P-Delta) static case: iterative solution, kept for the displacements and the design
    Private Function NonlinearSolution(ByVal Ev As FemEvaluation, ByVal Cmb As FemCombo) As Tuple(Of StaticResults, FemStatic)
        Dim T As Tuple(Of StaticResults, FemStatic) = Nothing
        If Ev.Nonlinear.TryGetValue(Cmb.Name, T) Then Return T
        Dim R As StaticResults = TrySolveIterative(Ev, Cmb, False)
        If R.Failed Then
            'a member whose axial force exceeds its elastic buckling load makes the consistent geometric stiffness indefinite; ETABS P-Delta has
            'no member buckling (P-delta): the case is solved again with the lumped P/L geometric stiffness (the design check shows the overstress)
            FemProfile.Hit("pdelta.lumped fallback")
            R = TrySolveIterative(Ev, Cmb, True)
        End If
        If R.Failed Then Ev.Failed = True : Ev.Message = R.Message
        T = Tuple.Create(R, Ev.Solver.LastIterSolver)
        Ev.Nonlinear(Cmb.Name) = T
        Return T
    End Function

    Private Function TrySolveIterative(ByVal Ev As FemEvaluation, ByVal Cmb As FemCombo, ByVal Lumped As Boolean) As StaticResults
        Try
            Return Ev.Solver.SolveIterative(Cmb.Terms, Ev.StaticRes, Lumped:=Lumped)
        Catch ex As Exception
            Return New StaticResults With {.Failed = True, .Message = "case " & Cmb.Name & ": " & ex.Message}
        End Try
    End Function

    Public Function FindCombo(ByVal Name As String) As FemCombo
        Return Model.Combos.FirstOrDefault(Function(x) x.Name = Name)
    End Function

    'Displacement vectors (6 per node, global axes) of a case or combination: static part, and for response spectrum terms the
    'combinations static + RS and static - RS. Nothing: unknown name or failed solution.
    Public Function Displacements(ByVal Ev As FemEvaluation, ByVal Name As String) As List(Of Double())
        Dim Cmb = FindCombo(Name)
        If Cmb Is Nothing Then Return Nothing
        Dim c = Clock("Displacements") : c.Start()
        Try
            Dim nd As Integer = Model.Nodes.Count * 6
            Dim Base(nd - 1) As Double
            If Cmb.Nonlinear Then
                Dim T = NonlinearSolution(Ev, Cmb)
                If T.Item1.Failed Then Return Nothing
                Base = T.Item1.U(0)
            Else
                For Each t In Cmb.Terms
                    Dim p As Integer = Model.PatternIndex(t.Name)
                    If p < 0 Then Continue For
                    Dim U = Ev.StaticRes.U(p)
                    For i = 0 To nd - 1 : Base(i) += t.Factor * U(i) : Next
                Next
            End If
            Dim Res As New List(Of Double())
            If Cmb.RSTerms.Count = 0 Then Res.Add(Base) : Return Res
            Dim Mag(nd - 1) As Double
            For Each rt In Cmb.RSTerms
                Dim rr As RSResults = Nothing
                If Not Ev.RS.TryGetValue(rt.Name, rr) Then Continue For
                For i = 0 To nd - 1 : Mag(i) += Math.Abs(rt.Factor) * rr.U(i) : Next
            Next
            Dim Up(nd - 1) As Double, Dn(nd - 1) As Double
            For i = 0 To nd - 1 : Up(i) = Base(i) + Mag(i) : Dn(i) = Base(i) - Mag(i) : Next
            Res.Add(Up) : Res.Add(Dn)
            Return Res
        Finally
            c.Stop()
        End Try
    End Function

    'Steel design ratios of the design combinations and the AISC 341 beam / column capacity ratios
    Public Sub Design(ByVal Ev As FemEvaluation)
        Dim c = Clock("Design") : c.Start()
        Try
            Dim Combos = Model.Combos.Where(Function(x) Model.DesignCombos.Contains(x.Name)).ToList()
            Dim Fem = Ev.Solver.DesignMembers(Ev.StaticRes, Combos, Model.Prefs, Ev.RS)
            For Each cmb In Combos.Where(Function(x) x.Nonlinear)
                Dim T = NonlinearSolution(Ev, cmb)
                If T.Item1.Failed Then Continue For
                Dim sg As New FemCombo With {.Name = cmb.Name}
                sg.Terms.Add(New FemTerm With {.Name = "COMBO", .Factor = 1.0})
                Dim Single1 As New List(Of FemCombo) From {sg}
                System.Threading.Tasks.Parallel.For(0, Model.Elements.Count, FemProfile.Options, Sub(ei)
                                                                                 Dim r1 = T.Item2.DesignElement(T.Item1, ei, Single1, Model.Prefs)
                                                                                 Dim c0 = Fem(ei)
                                                                                 If r1.PMM > c0.PMM Then c0.PMM = r1.PMM : c0.P = r1.P : c0.MMaj = r1.MMaj : c0.MMin = r1.MMin : c0.PMMCombo = cmb.Name : c0.PMMStation = r1.PMMStation
                                                                                 If r1.VMaj > c0.VMaj Then c0.VMaj = r1.VMaj : c0.VMajCombo = cmb.Name
                                                                                 c0.VMin = Math.Max(c0.VMin, r1.VMin)
                                                                                 c0.PuMax = Math.Max(c0.PuMax, r1.PuMax)
                                                                             End Sub)
            Next
            Ev.Members = Fem
            Ev.Solver.StrongColumnWeakBeam(Fem, Ev.BCMajor, Ev.BCMinor)
            Ev.Above = Ev.Solver.Above
        Finally
            c.Stop()
        End Try
    End Sub

End Class
