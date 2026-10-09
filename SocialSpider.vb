'_______________________________________________________________________________________________________________
'Social Spider Optimization (SSO), Cuevas et al. 2013, translated from the Fortran code of I. Aydogdu
'(fortran\SocialSpider: SSO_Column\SSO.f90 2015, SSO_Frame\SSO.f90 2018) and SSO.m.
'The memory is the colony. Every spider is a female or a male (Member_.IsMale); the number of females
'Nf = floor((0.9 - rand 0.25) N) is drawn once (AlgorithmState_.SpiderFemales).
'  weight     w = (worst - J) / (worst - best), J = penalized cost
'  vibration  Vib = w_k exp(-d^2), d = distance / distmax (distmax = |Ub - Lb|)
'  female     rand < PF: f + a Vibc (sc - f) + b Vibb (sb - f) + c (rand - 1/2)
'             else      f - a Vibc (sc - f) - b Vibb (sb - f) + c (rand - 1/2)
'             sc = nearest spider with a larger weight, sb = best spider
'  male       w > median of the male weights (dominant): m + a Vibf (sf - m) + c (rand - 1/2), sf = nearest female
'             otherwise: m + a (weighted mean of the males - m)
'  mating     after the last member of a loop: every dominant male mates with the females within the radius
'             (per variable, as in the Fortran code); every variable of the offspring is taken by roulette on the
'             weights of the male and of these females; the offspring replaces the worst spider if it is better
'             and takes its gender
'  jump       (optional, SSO_Column) every spider keeps a variable with probability 0.7 + 0.25 w, otherwise the
'             variable is random; the new spider replaces the worst one if it is better
'Positions are rounded and clipped to the section bounds. A move that gives a design already in the colony
'changes one random variable by one section (no analysis is spent on a known design).
'Not taken from the Fortran code (errors): clipping with a stale index k, rounding written into the old position
'(m(i,j) instead of Sect(j)), direction to the best female while the vibration uses the best spider (and
'f(ibestS,j) outside of the female array), roulette probabilities with a sum above 1.
Partial Public Class OptimizationClass

    Private Sub Main_SocialSpider(ByVal Imem As Integer, ByRef ret As Integer)
        InitMethodState(True)
        If Imem = 0 Then SpiderBalanceGenders()
        Dim Greedy As Boolean = Par("Greedy") >= 0.5
        Dim W() As Double = SpiderWeights()
        Dim X() As Double = Pos(Imem)
        Dim XNew(NVar - 1) As Double
        If Not Memory(Imem).IsMale Then
            Dim Pf As Double = Par("PF")
            Dim c As Integer = SpiderNearest(Imem, Function(k) W(k) > W(Imem))
            Dim b As Integer = SpiderBest()
            Dim VibC As Double = If(c < 0, 0, W(c) * Math.Exp(-SpiderDist2(Imem, c)))
            Dim VibB As Double = W(b) * Math.Exp(-SpiderDist2(Imem, b))
            Dim Sc() As Double = If(c < 0, X, Pos(c))
            Dim Sb() As Double = Pos(b)
            Dim Attract As Boolean = Rnd() < Pf
            For d = 0 To NVar - 1
                Dim Stp As Double = Rnd() * VibC * (Sc(d) - X(d)) + Rnd() * VibB * (Sb(d) - X(d))
                XNew(d) = If(Attract, X(d) + Stp, X(d) - Stp) + Rnd() * (Rnd() - 0.5)
            Next
        Else
            Dim Males As List(Of Integer) = SpiderIndices(True)
            Dim Median As Double = SpiderMedian(Males.Select(Function(k) W(k)))
            If W(Imem) > Median Then
                Dim f As Integer = SpiderNearest(Imem, Function(k) Not Memory(k).IsMale)
                Dim VibF As Double = If(f < 0, 0, W(f) * Math.Exp(-SpiderDist2(Imem, f)))
                Dim Sf() As Double = If(f < 0, X, Pos(f))
                For d = 0 To NVar - 1
                    XNew(d) = X(d) + Rnd() * VibF * (Sf(d) - X(d)) + Rnd() * (Rnd() - 0.5)
                Next
            Else
                Dim SumW As Double = Males.Sum(Function(k) W(k))
                For d = 0 To NVar - 1
                    Dim dd As Integer = d
                    Dim Mean As Double = If(SumW > 0, Males.Sum(Function(k) W(k) * Memory(k).DesignVariables(dd)) / SumW,
                                            Males.Average(Function(k) CDbl(Memory(k).DesignVariables(dd))))
                    XNew(d) = X(d) + Rnd() * (Mean - X(d))
                Next
            End If
        End If
        Dim Cand As OptimizationStructure_.Member_ = SpiderNew(XNew)
        Cand.IsMale = Memory(Imem).IsMale
        EvalAt(Cand, Imem, Greedy, ret)
        If ret <> 0 Then Return
        If Imem = Memory.Count - 1 Then
            SpiderMating(ret)
            If ret <> 0 Then Return
            If Par("Jump") >= 0.5 Then SpiderJump(ret)
        End If
    End Sub

    'mating of the dominant males (one offspring per male)
    Private Sub SpiderMating(ByRef ret As Integer)
        Dim W() As Double = SpiderWeights()
        Dim Males As List(Of Integer) = SpiderIndices(True)
        Dim Females As List(Of Integer) = SpiderIndices(False)
        If Males.Count = 0 OrElse Females.Count = 0 Then Return
        Dim Median As Double = SpiderMedian(Males.Select(Function(k) W(k)))
        Dim RFrac As Double = Par("Radius")
        For Each i In Males.Where(Function(k) W(k) > Median).ToList()
            Dim Child As New OptimizationStructure_.Member_
            ReDim Child.DesignVariables(NVar - 1)
            Dim Mated As Boolean = False
            For d = 0 To NVar - 1
                Dim xm As Integer = Memory(i).DesignVariables(d)
                Dim r As Double = RFrac * (Ub(d) - Lb(d))
                Dim dd As Integer = d
                Dim Group As List(Of Integer) = Females.Where(Function(k) Math.Abs(Memory(k).DesignVariables(dd) - xm) <= r).ToList()
                If Group.Count = 0 Then
                    Child.DesignVariables(d) = xm
                Else
                    Mated = True
                    Group.Add(i)
                    Dim Pw() As Double = Group.Select(Function(k) W(k)).ToArray()
                    Dim s As Integer = If(Pw.Sum() > 0, Roulette_wheel(Pw), CInt(Int(Rnd() * Group.Count)))
                    Child.DesignVariables(d) = Memory(Group(s)).DesignVariables(d)
                End If
            Next
            If Not Mated OrElse SpiderKnown(Child.DesignVariables) Then Continue For
            Dim Wst As Integer = WorstIndex()
            Child.IsMale = Memory(Wst).IsMale
            EvalAt(Child, Wst, True, ret)
            If ret <> 0 Then Return
            W = SpiderWeights()
        Next
    End Sub

    'spider jump of the SSO_Column code (spiderjumpEq = 1)
    Private Sub SpiderJump(ByRef ret As Integer)
        For i = 0 To Memory.Count - 1
            Dim W() As Double = SpiderWeights()
            Dim Pr As Double = 0.7 + 0.25 * W(i)
            Dim C As OptimizationStructure_.Member_ = CopyMember(Memory(i))
            For d = 0 To NVar - 1
                If Pr < Rnd() Then C.DesignVariables(d) = RandomVariable(d)
            Next
            If SpiderKnown(C.DesignVariables) Then Continue For
            Dim Wst As Integer = WorstIndex()
            C.IsMale = Memory(Wst).IsMale
            EvalAt(C, Wst, True, ret)
            If ret <> 0 Then Return
        Next
    End Sub

    'rounded position; a design already in the colony is moved by one section in one random variable
    Private Function SpiderNew(ByVal X() As Double) As OptimizationStructure_.Member_
        Dim M As OptimizationStructure_.Member_ = ToMember(X)
        Dim Tries As Integer = 0
        While SpiderKnown(M.DesignVariables) AndAlso Tries < 10
            Dim d As Integer = CInt(Int(Rnd() * NVar))
            Dim v As Integer = M.DesignVariables(d) + If(Rnd() < 0.5, -1, 1)
            If v > Ub(d) Then v = Ub(d) - 1
            If v < Lb(d) Then v = Lb(d) + 1
            M.DesignVariables(d) = Math.Min(Math.Max(v, Lb(d)), Ub(d))
            Tries += 1
        End While
        Return M
    End Function

    Private Function SpiderKnown(ByVal V() As Integer) As Boolean
        For Each M In Memory
            If M.DesignVariables.SequenceEqual(V) Then Return True
        Next
        Return False
    End Function

    'w = (worst - J) / (worst - best); 1 for all if the costs are equal, 0 for an infinite cost
    Private Function SpiderWeights() As Double()
        Dim J() As Double = Memory.Select(Function(m) m.PenalizedCost).ToArray()
        Dim Fin = J.Where(Function(x) Not Double.IsInfinity(x) AndAlso Not Double.IsNaN(x)).ToList()
        Dim W(J.Length - 1) As Double
        If Fin.Count = 0 Then Return W
        Dim Best As Double = Fin.Min(), Worst As Double = Fin.Max()
        For k = 0 To J.Length - 1
            If Double.IsInfinity(J(k)) OrElse Double.IsNaN(J(k)) Then
                W(k) = 0
            ElseIf Worst = Best Then
                W(k) = 1
            Else
                W(k) = (Worst - J(k)) / (Worst - Best)
            End If
        Next
        Return W
    End Function

    'squared distance normalized by the diagonal of the search space
    Private Function SpiderDist2(ByVal i As Integer, ByVal k As Integer) As Double
        Dim S As Double = 0, Dm As Double = 0
        For d = 0 To NVar - 1
            S += CDbl(Memory(i).DesignVariables(d) - Memory(k).DesignVariables(d)) ^ 2
            Dm += CDbl(Ub(d) - Lb(d)) ^ 2
        Next
        Return If(Dm > 0, S / Dm, 0)
    End Function

    Private Function SpiderNearest(ByVal i As Integer, ByVal Cond As Func(Of Integer, Boolean)) As Integer
        Dim Best As Integer = -1, BestD As Double = Double.MaxValue
        For k = 0 To Memory.Count - 1
            If k = i OrElse Not Cond(k) Then Continue For
            Dim D As Double = SpiderDist2(i, k)
            If D < BestD Then BestD = D : Best = k
        Next
        Return Best
    End Function

    Private Function SpiderBest() As Integer
        Dim b As Integer = 0
        For k = 1 To Memory.Count - 1
            If Memory(k).PenalizedCost < Memory(b).PenalizedCost Then b = k
        Next
        Return b
    End Function

    Private Function SpiderIndices(ByVal Male As Boolean) As List(Of Integer)
        Return Enumerable.Range(0, Memory.Count).Where(Function(k) Memory(k).IsMale = Male).ToList()
    End Function

    Private Shared Function SpiderMedian(ByVal V As IEnumerable(Of Double)) As Double
        Dim S = V.OrderBy(Function(x) x).ToList()
        If S.Count = 0 Then Return 0
        If S.Count Mod 2 = 1 Then Return S(S.Count \ 2)
        Return (S(S.Count \ 2 - 1) + S(S.Count \ 2)) / 2
    End Function

    'number of females = State.SpiderFemales (members added by ClearDuplicates are female by default)
    Private Sub SpiderBalanceGenders()
        Dim Nf As Integer = Math.Min(Math.Max(State.SpiderFemales, 1), Math.Max(Memory.Count - 1, 1))
        Dim F As Integer = Memory.Where(Function(m) Not m.IsMale).Count()
        For k = Memory.Count - 1 To 0 Step -1
            If F = Nf Then Exit For
            Dim M As OptimizationStructure_.Member_ = Memory(k)
            If F > Nf AndAlso Not M.IsMale Then
                M.IsMale = True : Memory(k) = M : F -= 1
            ElseIf F < Nf AndAlso M.IsMale Then
                M.IsMale = False : Memory(k) = M : F += 1
            End If
        Next
    End Sub

    'Nf = floor((0.9 - rand 0.25) N), 1 .. N-1; the first Nf members (random initial order) are the females
    Private Sub SpiderInit()
        Dim N As Integer = Memory.Count
        Dim Nf As Integer = CInt(Math.Floor((0.9 - Rnd() * 0.25) * N))
        State.SpiderFemales = Math.Min(Math.Max(Nf, 1), Math.Max(N - 1, 1))
        For k = 0 To N - 1
            Dim M As OptimizationStructure_.Member_ = Memory(k)
            M.IsMale = k >= State.SpiderFemales
            Memory(k) = M
        Next
    End Sub
End Class
