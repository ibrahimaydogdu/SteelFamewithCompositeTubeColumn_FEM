Imports System.Globalization

'Paper 1 change request PCR-16a: optimality criteria predesign (fully stressed design + drift scaling). App.config Predesign = none
'(default, earlier versions: no predesign, no extra random numbers) | oc: before the search a resizing loop runs with the evaluator of
'the run (FEM); its analyses count in the analysis budget of the run. The result is the first member of the initial memory.
' start     : section of the ETABS initial design (W); tube / box groups: lightest section with I >= I of that W section
' strength  : capacity required = capacity * (r * gamma)^e, r = design ratio of the group (PMMRatio), capacity = area (W, box) or Pno (tube)
' drift     : stiffness required = I * (rho * gamma)^e, rho = largest drift / limit of the stories of the group (and the top drift);
'             columns and beams (OcDriftBeams), not the DriftRepairExclude groups
' exponent e: OcStrengthExponent / OcDriftExponent above the limit, OcReduceExponent below it (the section gets smaller)
' section   : lightest section of the bounds that is ductile and meets both requirements; then the geometric repair (column stacks)
' stop      : the vector repeats (converged), a cycle halves the exponents; at most OcIterations analyses
' result    : cheapest feasible design of the loop, else the design with the smallest penalty
'Hybrid columns: every Optimize stack starts composite (the all-composite layout is a design of the hybrid space); the loop does not
'choose the transition.
'PCR-16c, initial memory (OcMemory): seed (default) = the predesign, the other members random | neighborhood = the other members are the
'predesign moved by up to OcNeighborFraction of the bound width per variable | oc = the other members are OC designs too: short loops
'(OcVariantSteps analyses) from the predesign with the safety factor and exponents drawn by the seeded generator of the run (gamma 0.95-1.15,
'exponents 0.6-1.2, reduce exponent 0.3-0.7); a design that repeats is replaced by a neighborhood member.
'Search bounds (OcBounds): none (default) | narrow = predesign section +- OcBoundWidth * (catalog size - 1) | span = smallest to largest
'section of the feasible OC designs of the memory, widened by OcBoundWidth * (catalog size - 1) on both sides (no feasible OC design: the
'bounds stay); always within the earlier bounds.
'Steel option of a composite hybrid group: around the ETABS initial design; the transition variables stay open.
'PCR-16d, hybrid starts (OcHybridStarts): composite (default, earlier versions) = one loop from the all-composite layout | both = a second
'loop from the all-steel layout (every Optimize stack steel: the start of the steel run); both designs are members of the initial memory
'(already evaluated, counted in the budget), the better one is the predesign (first member). The loops still do not choose the transition.
Partial Public Class ETABS_Class
    Public ReadOnly PredesignMode As String = SettingChoice("Predesign", "none", {"none", "oc"})
    Private ReadOnly OcIterations As Integer = CInt(ReadNumber("OcIterations", 15))
    Private ReadOnly OcStrengthExponent As Double = ReadNumber("OcStrengthExponent", 1.0)
    Private ReadOnly OcDriftExponent As Double = ReadNumber("OcDriftExponent", 1.0)
    Private ReadOnly OcReduceExponent As Double = ReadNumber("OcReduceExponent", 0.5)
    Private ReadOnly OcSafety As Double = ReadNumber("OcSafety", 1.05)
    Private ReadOnly OcDriftBeams As Boolean = (SettingChoice("OcDriftBeams", "true", {"true", "false"}) = "true")
    Public ReadOnly OcBoundsMode As String = SettingChoice("OcBounds", "none", {"none", "narrow", "span"})
    Private ReadOnly OcBoundWidth As Double = ReadNumber("OcBoundWidth", 0.1)
    Public ReadOnly OcMemoryMode As String = SettingChoice("OcMemory", "seed", {"seed", "neighborhood", "oc"})
    Public ReadOnly OcNeighborFraction As Double = ReadNumber("OcNeighborFraction", 0.25)
    Private ReadOnly OcVariantSteps As Integer = CInt(ReadNumber("OcVariantSteps", 4))
    Public ReadOnly OcHybridStarts As String = SettingChoice("OcHybridStarts", "composite", {"composite", "both"})
    Private Const OC_RATIO_CAP As Double = 4.0            'design ratios with error terms (1 + errors / members) are capped
    Private Const OC_MIN_EXPONENT As Double = 0.1

    Private OcStories()() As Integer                      'stories of the members of each group variable
    'OcMemory = oc: evaluated OC designs for the initial memory (first = predesign), Nothing otherwise
    Public OcMembers As List(Of OptimizationStructure_.Member_)

    'Properties of section k of variable v (current type of the group): stiffness I (major axis), capacity, cost per length
    Private Function OcStiffness(ByVal v As Integer, ByVal k As Integer) As Double
        If IsTubeVar(v) Then Return CompositeSec(k).Transformed().I33
        Dim T As TubeSection_ = TubeOf(v, k)
        If T IsNot Nothing Then Return T.I33
        Return WSections(k).Imajor
    End Function

    Private Function OcCapacity(ByVal v As Integer, ByVal k As Integer) As Double
        If IsTubeVar(v) Then Return CompositeSec(k).Pno()
        Return SecArea(v, k)
    End Function

    Private Function OcCost(ByVal v As Integer, ByVal k As Integer) As Double
        Dim T As TubeSection_ = TubeOf(v, k)
        If T Is Nothing Then Return WSections(k).Area
        If IsTubeVar(v) Then Return TubeSettings.SteelUnitCost * T.SteelArea * A992Fy50Weight * TubeCostFactor(T) + TubeSettings.ConcreteUnitCost * T.ConcreteArea * 0.000000001
        Return T.SteelArea * TubeCostFactor(T)
    End Function

    'Lightest ductile section of the bounds with capacity >= Creq and stiffness >= Ireq; none: the section closest to both
    Private Function OcSelect(ByVal v As Integer, ByVal Creq As Double, ByVal Ireq As Double) As Integer
        Dim Best As Integer = -1, BestCost As Double = Double.MaxValue
        Dim Near As Integer = -1, NearFit As Double = -1, NearCost As Double = Double.MaxValue
        For k = GLb(v) To GUb(v)
            If Not IsDuctile(v, k) Then Continue For
            Dim C As Double = OcCapacity(v, k), I As Double = OcStiffness(v, k), Cost As Double = OcCost(v, k)
            If C >= Creq AndAlso I >= Ireq Then
                If Cost < BestCost Then Best = k : BestCost = Cost
            ElseIf Best < 0 Then
                Dim Fit As Double = Math.Min(If(Creq > 0, C / Creq, 1), If(Ireq > 0, I / Ireq, 1))
                If Fit > NearFit OrElse (Fit = NearFit AndAlso Cost < NearCost) Then Near = k : NearFit = Fit : NearCost = Cost
            End If
        Next
        Return If(Best >= 0, Best, Near)
    End Function

    'Ratio^exponent with the safety factor: above 1 OcStrengthExponent / OcDriftExponent (Up), below 1 OcReduceExponent (Down)
    Private Shared Function OcScale(ByVal Ratio As Double, ByVal Safety As Double, ByVal Up As Double, ByVal Down As Double) As Double
        Dim R As Double = Math.Min(Math.Max(Ratio, 0.000001), OC_RATIO_CAP) * Safety
        Return R ^ If(R > 1, Up, Down)
    End Function

    Private Sub OcInitializeStories()
        Dim L(SteelFrameDesignGroupIDs.Count - 1) As List(Of Integer)
        For v = 0 To L.Length - 1 : L(v) = New List(Of Integer) : Next
        For i = 0 To Stories.Length - 1
            For Each F In Stories(i).StoryFrames
                Dim v As Integer
                If F.GroupName Is Nothing OrElse Not VarIndex.TryGetValue(F.GroupName, v) Then Continue For
                If Not L(v).Contains(i) Then L(v).Add(i)
            Next
        Next
        OcStories = L.Select(Function(x) x.ToArray()).ToArray()
    End Sub

    'Start vector: ETABS initial design (W); tube and box groups: lightest section of the bounds with I >= I of the W section.
    'Hybrid: every Optimize group composite (Composite = True) or steel (PCR-16d)
    Private Function OcStart(Optional ByVal Composite As Boolean = True) As Integer()
        Dim Full(Ub.Length - 1) As Integer
        If Hybrid AndAlso AltVar IsNot Nothing Then
            For s = 0 To Stacks.Count - 1
                If StackVar(s) >= 0 Then Full(StackVar(s)) = If(Composite, Ub(StackVar(s)), Lb(StackVar(s)))
            Next
            For v = 0 To NG - 1
                If TypeVar(v) >= 0 Then Full(TypeVar(v)) = If(Composite, 1, 0)
            Next
        End If
        Dim G() As Integer = GroupVector(Full)
        If G Is Full Then G = CType(Full.Clone(), Integer())
        For v = 0 To G.Length - 1
            Dim W As Integer = Groups(SteelFrameDesignGroupIDs(v)).DesignSecID
            If W < 0 Then W = WSections.Count \ 2
            If IsTubeVar(v) OrElse IsBoxVar(v) Then
                G(v) = Math.Max(OcSelect(v, 0, WSections(W).Imajor), GLb(v))
            Else
                G(v) = Math.Min(Math.Max(W, GLb(v)), GUb(v))
            End If
        Next
        Dim Result() As Integer = CType(Full.Clone(), Integer())
        If Hybrid AndAlso AltVar IsNot Nothing Then EncodeHybrid(G, Result) Else Result = G
        Return Result
    End Function

    'Constraint values of one analysis (the resizing rules read them): story drifts, top drifts, design ratios of the groups
    Private Class OcState_
        Public Dx(), Dy(), Ratio() As Double
        Public TopX, TopY As Double
    End Class

    Private Function OcSaveState() As OcState_
        Return New OcState_ With {.Dx = Stories.Select(Function(s) s.InterStoryDriftX).ToArray(), .Dy = Stories.Select(Function(s) s.InterStoryDriftY).ToArray(),
                                  .Ratio = SteelFrameDesignGroupIDs.Select(Function(id) Groups(id).PMMRatio).ToArray(), .TopX = TopDriftX, .TopY = TopDriftY}
    End Function

    Private Sub OcRestoreState(ByVal S As OcState_)
        For i = 0 To Stories.Length - 1 : Stories(i).InterStoryDriftX = S.Dx(i) : Stories(i).InterStoryDriftY = S.Dy(i) : Next
        For v = 0 To SteelFrameDesignGroupIDs.Count - 1 : Groups(SteelFrameDesignGroupIDs(v)).PMMRatio = S.Ratio(v) : Next
        TopDriftX = S.TopX : TopDriftY = S.TopY
    End Sub

    'One OC loop from Start (at most MaxSteps analyses). Best = cheapest feasible design analysed by the loop, else the smallest penalty.
    'Initial: constraint values of Start (its analysis is not repeated, Start is not a result of the loop). Log: every step (Detail) or none.
    Private Sub OcLoop(ByVal Start() As Integer, ByVal MaxSteps As Integer, ByVal EUp As Double, ByVal EDrift As Double, ByVal EDown As Double,
                       ByVal Safety As Double, ByVal Detail As Boolean, ByRef iter_ As Integer, ByRef ret As Integer, ByRef Best As OptimizationStructure_.Member_,
                       ByRef Steps As Integer, Optional ByVal Initial As OcState_ = Nothing)
        Dim F3 = Function(x As Double) x.ToString("0.###", CultureInfo.InvariantCulture)
        Dim Full() As Integer = CType(Start.Clone(), Integer())
        Dim Seen As New HashSet(Of String)
        Dim HasBest As Boolean = False
        Steps = 0
        For k = 1 To MaxSteps + If(Initial IsNot Nothing, 1, 0)
            Dim G() As Integer
            Dim Key As String
            Dim M As New OptimizationStructure_.Member_ With {.DesignVariables = CType(Full.Clone(), Integer())}
            If k = 1 AndAlso Initial IsNot Nothing Then
                Key = String.Join(",", Full)
                Seen.Add(Key)
                GroupVector(Full)               'types of the start design
                OcRestoreState(Initial)
            Else
                'geometric repair of the group vector (column stacks, ductility)
                G = GroupVector(Full)
                E1_Modifier_Geometric(G)
                If Hybrid AndAlso G IsNot Full Then EncodeHybrid(G, Full)
                Key = String.Join(",", Full)
                Seen.Add(Key)
                M.DesignVariables = CType(Full.Clone(), Integer())
                Evaluate(M, iter_, ret, False)
                If ret <> 0 Then : Errorlogprint("Problem occurred on :Evaluate (OC predesign)") : Exit Sub : End If
                Steps += 1
                Dim Better As Boolean = Not HasBest OrElse (M.Penalty = 0 AndAlso (Best.Penalty > 0 OrElse M.CostValue < Best.CostValue)) OrElse
                                        (Best.Penalty > 0 AndAlso (M.Penalty < Best.Penalty OrElse (M.Penalty = Best.Penalty AndAlso M.CostValue < Best.CostValue)))
                If Better Then
                    Best = M : Best.DesignVariables = CType(M.DesignVariables.Clone(), Integer()) : HasBest = True
                End If
                If AnalysisFailed Then
                    Errorlogprint("Info: OC predesign step " & k & ": analysis failed, the loop stops")
                    Exit For
                End If
            End If
            G = GroupVector(M.DesignVariables)
            Dim Top As Double = If(TopDriftLimit > 0, Math.Max(TopDriftX, TopDriftY) / TopDriftLimit, 0)
            Dim MaxDrift As Double = Math.Max(Stories.Select(Function(st) Math.Max(st.InterStoryDriftX, st.InterStoryDriftY) / st.InterStoryDriftLimit).DefaultIfEmpty(0).Max(), Top)
            Dim MaxRatio As Double = SteelFrameDesignGroupIDs.Select(Function(id) Groups(id).PMMRatio).DefaultIfEmpty(0).Max()
            Dim MaxVar As Integer = Array.IndexOf(SteelFrameDesignGroupIDs.Select(Function(id) Groups(id).PMMRatio).ToArray(), MaxRatio)
            'new sections; a vector that was analysed before (cycle) halves the exponents
            Dim NewFull() As Integer = Nothing
            Dim Changed As Integer = 0
            Do
                Dim N() As Integer = CType(G.Clone(), Integer())
                For v = 0 To N.Length - 1
                    Dim Cur As Integer = G(v)
                    Dim Creq As Double = OcCapacity(v, Cur) * OcScale(Groups(SteelFrameDesignGroupIDs(v)).PMMRatio, Safety, EUp, EDown)
                    Dim Ireq As Double = 0
                    Dim Gname As String = Groups(SteelFrameDesignGroupIDs(v)).GroupName
                    If (IsColumnVar(v) OrElse OcDriftBeams) AndAlso Not DriftRepairExcluded(Gname) AndAlso OcStories(v).Length > 0 Then
                        Dim Rho As Double = Math.Max(OcStories(v).Max(Function(i) Math.Max(Stories(i).InterStoryDriftX, Stories(i).InterStoryDriftY) / Stories(i).InterStoryDriftLimit), Top)
                        Ireq = OcStiffness(v, Cur) * OcScale(Rho, Safety, EDrift, EDown)
                    End If
                    Dim S As Integer = OcSelect(v, Creq, Ireq)
                    If S >= 0 Then N(v) = S
                Next
                E1_Modifier_Geometric(N)
                Changed = Enumerable.Range(0, N.Length).Count(Function(v) N(v) <> G(v))
                NewFull = CType(M.DesignVariables.Clone(), Integer())
                If Hybrid AndAlso AltVar IsNot Nothing Then EncodeHybrid(N, NewFull) Else NewFull = N
                Dim NewKey As String = String.Join(",", NewFull)
                If NewKey = Key OrElse Not Seen.Contains(NewKey) Then Exit Do
                EUp *= 0.5 : EDrift *= 0.5 : EDown *= 0.5
                If Detail Then Errorlogprint("Info: OC predesign step " & k & ": cycle, exponents halved (" & F3(EUp) & ", " & F3(EDrift) & ", " & F3(EDown) & ")")
                If EUp < OC_MIN_EXPONENT Then Exit Do
            Loop
            If Detail Then
                Errorlogprint("Info: OC predesign step " & k & ": cost " & F3(M.CostValue) & ", penalty " & F3(M.Penalty) & ", drift / limit " & F3(MaxDrift) &
                              ", design ratio (max) " & F3(MaxRatio) & If(MaxVar >= 0, " (" & Groups(SteelFrameDesignGroupIDs(MaxVar)).GroupName &
                              If(G(MaxVar) >= GUb(MaxVar), ", upper bound", "") & ")", "") & ", " & Changed & " groups changed")
            End If
            If Changed = 0 OrElse String.Join(",", NewFull) = Key OrElse EUp < OC_MIN_EXPONENT Then Exit For
            Full = NewFull
        Next
    End Sub

    'PCR-16a: runs the predesign loop; iter_ = analysis counter of the run (budget). Returns the design vector of the first member.
    Public Function OcPredesign(ByRef iter_ As Integer, ByRef ret As Integer) As Integer()
        Dim F3 = Function(x As Double) x.ToString("0.###", CultureInfo.InvariantCulture)
        OcInitializeStories()
        Errorlogprint("Info: OC predesign (PCR-16a): at most " & OcIterations & " analyses, strength exponent " & F3(OcStrengthExponent) & ", drift exponent " &
                      F3(OcDriftExponent) & ", reduce exponent " & F3(OcReduceExponent) & ", safety " & F3(OcSafety) & ", beams in the drift scaling " &
                      OcDriftBeams.ToString().ToLowerInvariant() & "; memory " & OcMemoryMode & ", bounds " & OcBoundsMode)
        Dim IterAtStart As Integer = iter_, Steps As Integer = 0
        Dim Main As OptimizationStructure_.Member_ = Nothing
        OcLoop(OcStart(), OcIterations, OcStrengthExponent, OcDriftExponent, OcReduceExponent, OcSafety, True, iter_, ret, Main, Steps)
        If ret <> 0 OrElse Main.DesignVariables Is Nothing Then Return Nothing
        Errorlogprint("Info: OC predesign: " & Steps & " steps, " & (iter_ - IterAtStart) & " analyses, " &
                      If(Main.Penalty = 0, "feasible design, cost " & F3(Main.CostValue), "no feasible design, smallest penalty " & F3(Main.Penalty) & " (cost " & F3(Main.CostValue) & ")"))
        'PCR-16d: second loop from the all-steel layout; the better design is the predesign, the other one the second member
        Dim Other As OptimizationStructure_.Member_ = Nothing, HasOther As Boolean = False
        If Hybrid AndAlso AltVar IsNot Nothing AndAlso OcHybridStarts = "both" Then
            Dim IterSteel As Integer = iter_
            Dim SteelOc As OptimizationStructure_.Member_ = Nothing
            Errorlogprint("Info: OC predesign (PCR-16d): second loop from the all-steel layout")
            OcLoop(OcStart(False), OcIterations, OcStrengthExponent, OcDriftExponent, OcReduceExponent, OcSafety, True, iter_, ret, SteelOc, Steps)
            If ret <> 0 OrElse SteelOc.DesignVariables Is Nothing Then Return Nothing
            Errorlogprint("Info: OC predesign (PCR-16d, all-steel start): " & Steps & " steps, " & (iter_ - IterSteel) & " analyses, " &
                          If(SteelOc.Penalty = 0, "feasible design, cost " & F3(SteelOc.CostValue), "no feasible design, smallest penalty " & F3(SteelOc.Penalty) & " (cost " & F3(SteelOc.CostValue) & ")"))
            Dim SteelBetter As Boolean = (SteelOc.Penalty = 0 AndAlso (Main.Penalty > 0 OrElse SteelOc.CostValue < Main.CostValue)) OrElse
                                         (Main.Penalty > 0 AndAlso (SteelOc.Penalty < Main.Penalty OrElse (SteelOc.Penalty = Main.Penalty AndAlso SteelOc.CostValue < Main.CostValue)))
            If SteelBetter Then Other = Main : Main = SteelOc Else Other = SteelOc
            HasOther = String.Join(",", Other.DesignVariables) <> String.Join(",", Main.DesignVariables)
            Errorlogprint("Info: OC predesign (PCR-16d): predesign from the " & If(SteelBetter, "all-steel", "all-composite") & " start" &
                          If(HasOther, ", the other design is the second member of the initial memory", " (the two designs are the same)"))
        End If
        OcMembers = Nothing
        If HasOther AndAlso OcMemoryMode <> "oc" Then OcMembers = New List(Of OptimizationStructure_.Member_) From {Main, Other}
        If OcMemoryMode = "oc" Then
            'PCR-16c: further OC designs from the predesign with drawn parameters (seeded generator of the run)
            OcMembers = New List(Of OptimizationStructure_.Member_) From {Main}
            If HasOther Then OcMembers.Add(Other)
            Dim Keys As New HashSet(Of String) From {String.Join(",", Main.DesignVariables)}
            Dim IterVariants As Integer = iter_, Repeats As Integer = 0
            'constraint values of the predesign (one analysis; the variants start from them)
            Dim Again As New OptimizationStructure_.Member_ With {.DesignVariables = CType(Main.DesignVariables.Clone(), Integer())}
            Evaluate(Again, iter_, ret, False)
            If ret <> 0 Then Return Nothing
            Dim MainState As OcState_ = OcSaveState()
            For m = 1 To FormInfo.OptInfo.MemorySize - 1
                Dim Safety As Double = 0.95 + 0.2 * Rnd()
                Dim EUp As Double = 0.6 + 0.6 * Rnd(), EDrift As Double = 0.6 + 0.6 * Rnd(), EDown As Double = 0.3 + 0.4 * Rnd()
                Dim V As OptimizationStructure_.Member_ = Nothing
                OcLoop(Main.DesignVariables, OcVariantSteps, EUp, EDrift, EDown, Safety, False, iter_, ret, V, Steps, MainState)
                If ret <> 0 Then Return Nothing
                If V.DesignVariables Is Nothing OrElse Not Keys.Add(String.Join(",", V.DesignVariables)) Then Repeats += 1 : Continue For
                OcMembers.Add(V)
            Next
            Errorlogprint("Info: OC memory (PCR-16c): " & OcMembers.Count & " OC designs (" & OcMembers.Where(Function(x) x.Penalty = 0).Count() & " feasible, costs " &
                          F3(OcMembers.Min(Function(x) x.CostValue)) & " - " & F3(OcMembers.Max(Function(x) x.CostValue)) & "), " & Repeats & " repeated, " &
                          (iter_ - IterVariants) & " analyses")
        End If
        If OcBoundsMode = "narrow" Then
            OcNarrowBounds({Main.DesignVariables}, Main.DesignVariables, "around the predesign")
        ElseIf OcBoundsMode = "span" Then
            'only feasible OC designs set the span; without one the bounds stay (narrowing around infeasible designs can exclude the
            'feasible region, CONV15S 2026-10-31: CFT / hybrid 1 of 10 runs feasible)
            Dim Pool As IEnumerable(Of OptimizationStructure_.Member_) = If(OcMembers IsNot Nothing, OcMembers, {Main})
            Dim Designs As List(Of Integer()) = Pool.Where(Function(x) x.Penalty = 0).Select(Function(x) x.DesignVariables).ToList()
            If Designs.Count > 0 Then
                OcNarrowBounds(Designs, Main.DesignVariables, "around the sections of the " & Designs.Count & " feasible OC designs")
            Else
                Errorlogprint("Info: OC bounds (PCR-16c): no feasible OC design, the bounds are not narrowed")
            End If
        End If
        If OcMembers IsNot Nothing Then
            For i = 0 To OcMembers.Count - 1
                Dim x = OcMembers(i)
                For d = 0 To x.DesignVariables.Length - 1
                    x.DesignVariables(d) = Math.Min(Math.Max(x.DesignVariables(d), Lb(d)), Ub(d))     'inactive steel options (hybrid)
                Next
            Next
        End If
        Return Main.DesignVariables
    End Function

    'Bounds [L, U] of one catalog narrowed to [Lo - H, Hi + H], H = OcBoundWidth * (N - 1), within the earlier bounds
    Private Sub OcNarrow(ByVal Lo As Integer, ByVal Hi As Integer, ByVal N As Integer, ByRef L As Integer, ByRef U As Integer)
        Dim H As Integer = Math.Max(1, CInt(Math.Round(OcBoundWidth * (N - 1))))
        Dim A As Integer = Math.Min(Math.Max(Lo, L), U), B As Integer = Math.Min(Math.Max(Hi, L), U)
        L = Math.Max(L, A - H) : U = Math.Min(U, B + H)
    End Sub

    'PCR-16c: bounds from the smallest to the largest section of Designs per group (+- OcBoundWidth of the catalog); Ub / Lb are rebuilt
    '(hybrid: new arrays). Seed: design vector whose types set the group bounds afterwards.
    Private Sub OcNarrowBounds(ByVal Designs As IList(Of Integer()), ByVal Seed() As Integer, ByVal Text As String)
        Dim F3 = Function(x As Double) x.ToString("0.###", CultureInfo.InvariantCulture)
        Dim NV As Integer = SteelFrameDesignGroupIDs.Count
        Dim Lo(NV - 1) As Integer, Hi(NV - 1) As Integer
        For v = 0 To NV - 1 : Lo(v) = Integer.MaxValue : Hi(v) = Integer.MinValue : Next
        For Each D In Designs
            Dim G() As Integer = GroupVector(D)
            For v = 0 To NV - 1
                Lo(v) = Math.Min(Lo(v), G(v)) : Hi(v) = Math.Max(Hi(v), G(v))
            Next
        Next
        GroupVector(Seed)               'types of the predesign
        Dim Before As Double = 0, After As Double = 0
        For v = 0 To NV - 1
            Dim id As Integer = SteelFrameDesignGroupIDs(v)
            Before += Math.Log10(Math.Max(1, Ub(v) - Lb(v) + 1))
            If Groups(id).IsComposite AndAlso CompUb IsNot Nothing Then
                OcNarrow(Lo(v), Hi(v), CompCount(), CompLb(v), CompUb(v))
                'steel option of an Optimize group (hybrid): around the ETABS initial design
                If Hybrid AndAlso GMode IsNot Nothing AndAlso GMode(v) = GroupColumnType_.Optimize Then
                    Dim W As Integer = Math.Max(Groups(id).DesignSecID, 0)
                    Dim S As Integer = W, NS As Integer = WSections.Count
                    If IsBoxGroup(id) Then
                        NS = Boxes.Count : S = NS - 1
                        For k = 0 To NS - 1
                            If Boxes(k).I33 >= WSections(W).Imajor Then S = k : Exit For
                        Next
                    End If
                    OcNarrow(S, S, NS, SteelLb(v), SteelUb(v))
                    Seed(v) = Math.Min(Math.Max(Seed(v), SteelLb(v)), SteelUb(v))      'inactive steel option of the composite group
                End If
            Else
                OcNarrow(Lo(v), Hi(v), CatalogCount(v), SteelLb(v), SteelUb(v))
            End If
        Next
        If Hybrid AndAlso AltVar IsNot Nothing Then
            BuildHybridLayout()
            GroupVector(Seed)           'group bounds of the predesign types
        Else
            For i = 0 To Ub.Length - 1
                Dim C As Boolean = Groups(SteelFrameDesignGroupIDs(i)).IsComposite
                Ub(i) = If(C, CompUb(i), SteelUb(i)) : Lb(i) = If(C, CompLb(i), SteelLb(i))
            Next
            GUb = Ub : GLb = Lb
        End If
        For v = 0 To NV - 1
            After += Math.Log10(Math.Max(1, Ub(v) - Lb(v) + 1))
        Next
        Errorlogprint("Info: OC bounds (PCR-16c): " & Text & " +- " & F3(OcBoundWidth) & " of the catalog, search space of the group variables 10^" &
                      F3(Before) & " -> 10^" & F3(After))
    End Sub
End Class
