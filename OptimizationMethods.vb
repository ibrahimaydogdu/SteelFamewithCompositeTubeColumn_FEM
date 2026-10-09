Imports System.Globalization

'_______________________________________________________________________________________________________________
'Method catalog: name, description, parameters (key, label, default, bounds) of every optimization method. The form
'builds the "Method parameters" box from it; the values are kept in FormInfo.OptInfo (HS / BBO in their own
'structures, all other methods in OptInfo.Params) and therefore in the backup.
Public Class ParamDef_
    Public Key As String
    Public Label As String
    Public DefaultValue As Double
    Public Min As Double
    Public Max As Double
    Public IsInteger As Boolean
    Public Choices() As String          'combo box (value = index), Nothing: number
    Public Tip As String
End Class

Public Class MethodDef_
    Public Method As OptimizationStructure_.OptMethod_
    Public Name As String
    Public Description As String
    Public Reference As String
    Public Params As New List(Of ParamDef_)
    Public UsesMemoryUpdate As Boolean      'the form "Memory update" applies (otherwise the method's own acceptance rule)
    Public LevyNote As String               'what the Levy flight option changes, Nothing: not used
End Class

Public Module MethodCatalog
    Public ReadOnly Methods As List(Of MethodDef_) = Build()

    Public Function Find(ByVal m As OptimizationStructure_.OptMethod_) As MethodDef_
        Return Methods.FirstOrDefault(Function(d) d.Method = m)
    End Function

    Private Function P(ByVal Key As String, ByVal Label As String, ByVal Def As Double, ByVal Min As Double, ByVal Max As Double, Optional ByVal IsInt As Boolean = False,
                       Optional ByVal Tip As String = Nothing, Optional ByVal Choices() As String = Nothing) As ParamDef_
        Return New ParamDef_ With {.Key = Key, .Label = Label, .DefaultValue = Def, .Min = Min, .Max = Max, .IsInteger = IsInt OrElse Choices IsNot Nothing, .Tip = Tip, .Choices = Choices}
    End Function

    Private Function Build() As List(Of MethodDef_)
        Dim ChangeTypes() As String = {"Static", "Dynamic", "Adaptive"}
        Dim L As New List(Of MethodDef_)
        Dim Add = Sub(Meth As OptimizationStructure_.OptMethod_, Name As String, Desc As String, Ref As String, UsesMU As Boolean, Levy As String, Ps() As ParamDef_)
                    L.Add(New MethodDef_ With {.Method = Meth, .Name = Name, .Description = Desc, .Reference = Ref, .UsesMemoryUpdate = UsesMU, .LevyNote = Levy, .Params = Ps.ToList()})
                End Sub
        Add(OptimizationStructure_.OptMethod_.HarmornySearch, "Harmony Search (HS)", "Memory consideration (HMCR), pitch adjustment (PAR), random selection.", "Geem et al. 2001", True, Nothing,
          {P("PAR", "PAR", 0.6, 0, 1), P("HMCR", "HMCR", 0.9, 0, 1), P("PARType", "PAR type", 1, 0, 2, Choices:=ChangeTypes), P("HMCRType", "HMCR type", 2, 0, 2, Choices:=ChangeTypes)})
        Add(OptimizationStructure_.OptMethod_.BioGBasedO, "Biogeography-Based (BBO)", "Migration between habitats by immigration / emigration rates, mutation.", "Simon 2008", True, "mutation: Levy step around the best design instead of a random section",
          {P("MutationRate", "Mutation rate", 0.1, 0, 1)})
        Add(OptimizationStructure_.OptMethod_.WhaleOpt, "Whale Optimization (WOA)", "Encircling, bubble-net spiral and random search of humpback whales.", "Mirjalili & Lewis 2016", True, Nothing, {})
        Add(OptimizationStructure_.OptMethod_.DandelionOpt, "Dandelion Optimizer (DO)", "Rising, descending and landing stages of dandelion seeds.", "Zhao et al. 2022", True, Nothing, {})
        Add(OptimizationStructure_.OptMethod_.ArtificialBeeColony, "Artificial Bee Colony (ABC)", "Employed bee, onlooker bee (roulette) and scout bee (source abandoned after 'limit' trials). 2 analyses per member and loop (+ scouts).", "Karaboga 2005", False, "scout bee: Levy step around the best design instead of a random source",
          {P("Limit", "Abandonment limit (0 = auto)", 0, 0, 100000, True, "trials without improvement before a food source is abandoned; 0: colony size x number of variables"),
           P("MR", "Modification rate (0 = one variable)", 0, 0, 1, Tip:="probability that a variable is changed in a neighbour; 0: one random variable (original ABC)")})
        Add(OptimizationStructure_.OptMethod_.AntColony, "Ant Colony (ACO)", "Ants select sections by pheromone and a lighter-section heuristic; rank-based pheromone deposit of the best designs, evaporation.", "Camp & Bichon 2004; Dorigo & Stützle 2004", False, Nothing,
          {P("Alpha", "Pheromone weight (alpha)", 1, 0, 10), P("Beta", "Heuristic weight (beta)", 0.5, 0, 10, Tip:="preference for lighter sections"),
           P("Rho", "Evaporation rate (rho)", 0.2, 0, 1), P("Elite", "Depositing fraction", 0.2, 0.01, 1, Tip:="fraction of the archive (best designs) that deposits pheromone")})
        Add(OptimizationStructure_.OptMethod_.BrainStorm, "Brain Storm (BSO)", "Clusters of ideas (k-means), new ideas from one or two clusters with a logsig step size.", "Shi 2011", False, "a replaced cluster center: Levy step around the best design instead of a random idea",
          {P("Clusters", "Number of clusters", 5, 1, 100, True), P("PReplace", "P(replace a center)", 0.2, 0, 1), P("POne", "P(one cluster)", 0.8, 0, 1),
           P("POneCenter", "P(center | one cluster)", 0.4, 0, 1), P("PTwoCenter", "P(centers | two clusters)", 0.5, 0, 1), P("Slope", "logsig slope k", 20, 0.1, 1000),
           P("StepScale", "Step size (x range)", 0.1, 0, 1, Tip:="noise = logsig x rand x N(0,1) x step size x (Ub - Lb)")})
        Add(OptimizationStructure_.OptMethod_.CrowSearch, "Crow Search (CSA)", "A crow follows the memory of another crow (flight length) or moves randomly (awareness probability).", "Askarzadeh 2016", False, "random move: Levy step around the best design",
          {P("AP", "Awareness probability (AP)", 0.1, 0, 1), P("FL", "Flight length (fl)", 2, 0, 10)})
        Add(OptimizationStructure_.OptMethod_.Firefly, "Firefly (FA)", "Moves toward all brighter fireflies, attractiveness beta0 exp(-gamma r^2), random step alpha (damped).", "Yang 2008", False, Nothing,
          {P("Alpha", "Randomness alpha (x range)", 0.2, 0, 1), P("Beta0", "Attractiveness beta0", 1, 0, 5), P("BetaMin", "Minimum attractiveness", 0.2, 0, 5),
           P("Gamma", "Absorption gamma", 15, 0, 1000, Tip:="distance normalized by the size of the search space"), P("Damping", "Alpha damping per loop", 0.97, 0, 1)})
        Add(OptimizationStructure_.OptMethod_.Grasshopper, "Grasshopper (GOA)", "Social interaction s(r) = f exp(-r/l) - exp(-r) of all grasshoppers, comfort zone c decreasing from cMax to cMin, target = best design.", "Saremi et al. 2017", False, Nothing,
          {P("CMax", "cMax", 1, 0, 10), P("CMin", "cMin", 0.00004, 0, 10), P("F", "Intensity of attraction f", 0.5, 0, 10), P("L", "Attractive length scale l", 1.5, 0.01, 100)})
        Add(OptimizationStructure_.OptMethod_.TeachingLearning, "Teaching-Learning (TLBO)", "Teacher phase (move toward the teacher, away from the mean) and learner phase (learn from a random learner). 2 analyses per member and loop (3 with the HS phase).", "Rao et al. 2011", False, Nothing,
          {P("TF", "Teaching factor (0 = random 1 / 2)", 0, 0, 2, Tip:="0: TF = round(1 + rand) (original TLBO)"), P("HSPhase", "Harmony Search phase", 0, 0, 1, Choices:={"No", "Yes (TLBO-HS)"}),
           P("HMCR", "HMCR (HS phase)", 0.85, 0, 1), P("PAR", "PAR (HS phase)", 0.45, 0, 1)})
        Add(OptimizationStructure_.OptMethod_.TreeSeed, "Tree-Seed (TSA)", "Every tree produces seeds toward the best tree or a random tree (search tendency ST); the best seed replaces the tree.", "Kiran 2015", False, Nothing,
          {P("ST", "Search tendency (ST)", 0.1, 0, 1), P("SeedsMin", "Seeds per tree, min", 2, 1, 100, True), P("SeedsMax", "Seeds per tree, max", 5, 1, 100, True)})
        Add(OptimizationStructure_.OptMethod_.GreyWolf, "Grey Wolf (GWO)", "Wolves move to the mean of the positions given by the alpha, beta and delta wolves; a decreases from 2 to 0.", "Mirjalili et al. 2014", False, Nothing,
          {P("AStart", "a at the start", 2, 0, 10)})
        Add(OptimizationStructure_.OptMethod_.HoneyBadger, "Honey Badger (HBA)", "Digging phase (cardioid motion with smell intensity) and honey phase (following the honeyguide); greedy selection.", "Hashim et al. 2022", False, Nothing,
          {P("Beta", "Ability to get food (beta)", 6, 1, 20), P("C", "Density constant C", 2, 1, 10)})
        Add(OptimizationStructure_.OptMethod_.Aquila, "Aquila Optimizer (AO)", "Expanded and narrowed exploration (high soar, contour flight with Levy), expanded and narrowed exploitation (low flight, walk and grab); greedy selection.", "Abualigah et al. 2021", False, Nothing,
          {P("Alpha", "Exploitation alpha", 0.1, 0, 1), P("Delta", "Exploitation delta", 0.1, 0, 1)})
        Add(OptimizationStructure_.OptMethod_.SocialSpider, "Social Spider (SSO)", "Female and male spiders move by the vibrations of the nearest better spider, the best spider and the nearest female; dominant males mate with the females within the mating radius (roulette on the weights). About 1.1 analyses per member and loop (up to 1.5 with the jump).", "Cuevas et al. 2013; Fortran code of I. Aydogdu", False, Nothing,
          {P("PF", "Probability of attraction (PF)", 0.7, 0, 1, Tip:="female: rand < PF moves toward the vibrations, otherwise away"),
           P("Radius", "Mating radius (x range)", 0.5, 0, 1, Tip:="a female mates for a variable if |female - male| <= radius x (Ub - Lb); 0.5 as in the Fortran code"),
           P("Greedy", "Accept a move", 1, 0, 1, Choices:={"Always (SSO)", "If better"}, Tip:="Always: original SSO; If better: greedy, as the Fortran option greedyselection = 1"),
           P("Jump", "Spider jump", 0, 0, 1, Choices:={"No", "Yes"}, Tip:="after mating every spider keeps a variable with probability 0.7 + 0.25 w (SSO_Column); +1 analysis per member")})
        Return L
    End Function

    'Value of a method parameter (HS / BBO in their structures, the others in OptInfo.Params, default if missing)
    Public Function GetParam(ByRef O As OptimizationStructure_.OptInfo_, ByVal m As OptimizationStructure_.OptMethod_, ByVal Key As String) As Double
        Select Case m
            Case OptimizationStructure_.OptMethod_.HarmornySearch
                Select Case Key
                    Case "PAR" : Return O.HarmonySearch.PAR
                    Case "HMCR" : Return O.HarmonySearch.HMCR
                    Case "PARType" : Return O.HarmonySearch.PARChangeType
                    Case "HMCRType" : Return O.HarmonySearch.HMCRChangeType
                End Select
            Case OptimizationStructure_.OptMethod_.BioGBasedO
                If Key = "MutationRate" Then Return O.BioGeography.MutationRate
        End Select
        Dim Name As String = m.ToString()
        If O.Params IsNot Nothing Then
            Dim v = O.Params.FirstOrDefault(Function(x) x.Method = Name AndAlso x.Key = Key)
            If v IsNot Nothing Then Return v.Value
        End If
        Dim d = Find(m).Params.FirstOrDefault(Function(x) x.Key = Key)
        Return If(d Is Nothing, 0, d.DefaultValue)
    End Function

    Public Sub SetParam(ByRef O As OptimizationStructure_.OptInfo_, ByVal m As OptimizationStructure_.OptMethod_, ByVal Key As String, ByVal Value As Double)
        Select Case m
            Case OptimizationStructure_.OptMethod_.HarmornySearch
                Select Case Key
                    Case "PAR" : O.HarmonySearch.PAR = Value : Return
                    Case "HMCR" : O.HarmonySearch.HMCR = Value : Return
                    Case "PARType" : O.HarmonySearch.PARChangeType = CType(CInt(Value), OptimizationStructure_.PAR_HMCR_ChangeType_) : Return
                    Case "HMCRType" : O.HarmonySearch.HMCRChangeType = CType(CInt(Value), OptimizationStructure_.PAR_HMCR_ChangeType_) : Return
                End Select
            Case OptimizationStructure_.OptMethod_.BioGBasedO
                If Key = "MutationRate" Then O.BioGeography.MutationRate = Value : Return
        End Select
        If O.Params Is Nothing Then O.Params = New List(Of MethodParam_)
        Dim Name As String = m.ToString()
        Dim v = O.Params.FirstOrDefault(Function(x) x.Method = Name AndAlso x.Key = Key)
        If v Is Nothing Then
            O.Params.Add(New MethodParam_ With {.Method = Name, .Key = Key, .Value = Value})
        Else
            v.Value = Value
        End If
    End Sub
End Module

'_______________________________________________________________________________________________________________
'Algorithms added in stage 15. Every Main_<method>(Imem) produces new designs for member Imem of the memory and
'evaluates them with the framework (repair, cache, global best). The continuous positions are rounded and clipped
'to the section bounds; the acceptance rule is the one of the method (greedy unless noted).
Partial Public Class OptimizationClass

    Public ScoutBees As Integer         'ABC: abandoned food sources (this program session; reported at the end)

    Private Function Par(ByVal Key As String) As Double
        Return MethodCatalog.GetParam(FormInfo.OptInfo, FormInfo.OptInfo.OptimizationMethod, Key)
    End Function

    'search progress 0 .. 1 (analyses / max. analyses) and the loop based "iteration" t of T
    Private ReadOnly Property Progress As Double
        Get
            Return Math.Min(Math.Max(iter / Math.Max(FormInfo.OptInfo.MaxFuncEvaluation, 1), 0), 1)
        End Get
    End Property
    Private ReadOnly Property TotalLoops As Double
        Get
            Return Math.Max(2, FormInfo.OptInfo.MaxFuncEvaluation / Math.Max(Memory.Count, 1))
        End Get
    End Property

    Private ReadOnly Property NVar As Integer
        Get
            Return Ub.Length
        End Get
    End Property

    'continuous position -> design (rounded, within the bounds)
    Private Function ToMember(ByVal X() As Double) As OptimizationStructure_.Member_
        Dim M As New OptimizationStructure_.Member_
        ReDim M.DesignVariables(NVar - 1)
        For d = 0 To NVar - 1
            Dim v As Double = If(Double.IsNaN(X(d)), (Lb(d) + Ub(d)) / 2.0, X(d))
            M.DesignVariables(d) = CInt(Math.Round(Math.Min(Math.Max(v, Lb(d)), Ub(d))))
        Next
        Return M
    End Function

    Private Function Pos(ByVal k As Integer) As Double()
        Return Memory(k).DesignVariables.Select(Function(x) CDbl(x)).ToArray()
    End Function

    Private Function RandomOther(ByVal i As Integer) As Integer
        If Memory.Count < 2 Then Return i
        Dim k As Integer
        Do
            k = CInt(Int(Rnd() * Memory.Count))
        Loop While k = i
        Return k
    End Function

    'mean of variable d over the memory
    Private Function MeanOf(ByVal d As Integer) As Double
        Return Memory.Average(Function(m) CDbl(m.DesignVariables(d)))
    End Function

    Private Function WorstIndex() As Integer
        Dim w As Integer = 0
        For k = 1 To Memory.Count - 1
            If Memory(k).PenalizedCost > Memory(w).PenalizedCost Then w = k
        Next
        Return w
    End Function

    'analysis of a new design (repair, cache, global best); the memory is not changed
    Private Sub EvaluateOnly(ByRef Member As OptimizationStructure_.Member_, ByRef ret As Integer)
        Call UBLBCheck(Member)
        If FormInfo.OptInfo.TestWithMath = True Then
            Call Math_Evaluate(Member)
        Else
            ETABSModel.Evaluate(Member, iter, ret)
            If ret <> 0 Then : LogError("Problem occurred in :Evaluate") : Return : End If
        End If
        GlobalBestCheck(Member, ILoop)
    End Sub

    'analysis and acceptance: member Target is replaced (always, or if the new design is better)
    Private Function EvalAt(ByRef Member As OptimizationStructure_.Member_, ByVal Target As Integer, ByVal Greedy As Boolean, ByRef ret As Integer) As Boolean
        EvaluateOnly(Member, ret)
        If ret <> 0 Then Return False
        Dim Replace As Boolean = Not Greedy OrElse Member.PenalizedCost < Memory(Target).PenalizedCost
        If Replace Then Memory(Target) = Member
        Update = Replace
        LastUpdatedID = If(Replace, Target, -1)
        Return Replace
    End Function

    'Levy step around the best design for every variable (Mantegna, as LevyFlight)
    Private Function LevyAroundBest(ByVal Around As OptimizationStructure_.Member_) As OptimizationStructure_.Member_
        Dim M As New OptimizationStructure_.Member_
        ReDim M.DesignVariables(NVar - 1)
        For d = 0 To NVar - 1
            M.DesignVariables(d) = LevyFlight(Around, d)
        Next
        Return M
    End Function

    Private Function RandomMember() As OptimizationStructure_.Member_
        Dim M As New OptimizationStructure_.Member_
        ReDim M.DesignVariables(NVar - 1)
        For d = 0 To NVar - 1
            M.DesignVariables(d) = RandomVariable(d)
        Next
        Return M
    End Function

    'Levy step of AO (s = 0.01, beta = 1.5)
    Private Shared Function LevyAO() As Double
        Const B As Double = 1.5
        Const SigmaU As Double = 0.6966      '(Gamma(1+B) sin(pi B/2) / (Gamma((1+B)/2) B 2^((B-1)/2)))^(1/B)
        Dim u As Double = NormalRnd() * SigmaU
        Dim v As Double = Math.Max(Math.Abs(NormalRnd()), 0.000001)
        Return 0.01 * u / v ^ (1 / B)
    End Function

    '___________________________________________________________________________________________________________
    'State of the methods (kept in FormInfo.OptInfo.State, part of the backup)
    Private ReadOnly Property State As AlgorithmState_
        Get
            If FormInfo.OptInfo.State Is Nothing Then FormInfo.OptInfo.State = New AlgorithmState_()
            Return FormInfo.OptInfo.State
        End Get
    End Property

    'after the initial memory and after a restart from a backup (missing / wrong sized state)
    Public Sub InitMethodState(Optional ByVal OnlyIfMissing As Boolean = False)
        Dim S As AlgorithmState_ = State
        Select Case FormInfo.OptInfo.OptimizationMethod
            Case OptimizationStructure_.OptMethod_.ArtificialBeeColony
                If Not OnlyIfMissing OrElse S.Trials Is Nothing OrElse S.Trials.Length <> Memory.Count Then ReDim S.Trials(Memory.Count - 1)
            Case OptimizationStructure_.OptMethod_.AntColony
                If Not OnlyIfMissing OrElse S.Pheromone Is Nothing OrElse S.Pheromone.Length <> NVar Then
                    ReDim S.Pheromone(NVar - 1)
                    For d = 0 To NVar - 1
                        S.Pheromone(d) = Enumerable.Repeat(1.0, Ub(d) + 1).ToArray()
                    Next
                End If
            Case OptimizationStructure_.OptMethod_.GreyWolf
                If Not OnlyIfMissing OrElse S.Leaders Is Nothing OrElse S.Leaders.Count <> 3 Then
                    S.Leaders = Memory.OrderBy(Function(c) c.PenalizedCost).Take(3).Select(Function(c) CopyMember(c)).ToList()
                    While S.Leaders.Count < 3 : S.Leaders.Add(CopyMember(S.Leaders(0))) : End While
                End If
            Case OptimizationStructure_.OptMethod_.SocialSpider
                If Not OnlyIfMissing OrElse S.SpiderFemales <= 0 Then SpiderInit()
        End Select
    End Sub

    'per member state is lost when the memory is rearranged (duplicates cleared)
    Private Sub MemberStateChanged()
        If FormInfo.OptInfo.State IsNot Nothing AndAlso FormInfo.OptInfo.State.Trials IsNot Nothing Then ReDim FormInfo.OptInfo.State.Trials(Memory.Count - 1)
    End Sub

    Private Shared Function CopyMember(ByVal M As OptimizationStructure_.Member_) As OptimizationStructure_.Member_
        Dim C As OptimizationStructure_.Member_ = M
        C.DesignVariables = CType(M.DesignVariables.Clone(), Integer())
        Return C
    End Function

    '___________________________________________________________________________________________________________
    'Artificial Bee Colony: employed bee for source Imem, one onlooker bee (roulette on fitness), scout if the
    'source was not improved for 'limit' trials
    Private Sub Main_ArtificialBeeColony(ByVal Imem As Integer, ByRef ret As Integer)
        InitMethodState(True)
        Dim Trials() As Integer = State.Trials
        Dim Limit As Double = Par("Limit")
        If Limit <= 0 Then Limit = Memory.Count * NVar
        'employed bee
        Dim Cand As OptimizationStructure_.Member_ = AbcNeighbour(Imem)
        If EvalAt(Cand, Imem, True, ret) Then Trials(Imem) = 0 Else Trials(Imem) += 1
        If ret <> 0 Then Return
        'onlooker bee: source chosen with probability fit / sum(fit), fit = 1 / (1 + f)
        Dim Fit() As Double = Memory.Select(Function(c) If(Double.IsInfinity(c.PenalizedCost), 0, 1 / (1 + Math.Max(c.PenalizedCost, 0)))).ToArray()
        Dim k As Integer = If(Fit.Sum() > 0, Roulette_wheel(Fit), CInt(Int(Rnd() * Memory.Count)))
        Cand = AbcNeighbour(k)
        If EvalAt(Cand, k, True, ret) Then Trials(k) = 0 Else Trials(k) += 1
        If ret <> 0 Then Return
        'scout bee
        If Trials(Imem) > Limit Then
            ScoutBees += 1
            Cand = If(FormInfo.OptInfo.LevyFlight, LevyAroundBest(Memory(Imem)), RandomMember())
            EvalAt(Cand, Imem, False, ret)
            Trials(Imem) = 0
        End If
    End Sub

    'v = x + phi (x - x_k), phi in [-1, 1], for one variable (or each variable with probability MR); a change that
    'rounds to zero becomes one section up or down
    Private Function AbcNeighbour(ByVal i As Integer) As OptimizationStructure_.Member_
        Dim MR As Double = Par("MR")
        Dim k As Integer = RandomOther(i)
        Dim C As OptimizationStructure_.Member_ = CopyMember(Memory(i))
        Dim One As Integer = CInt(Int(Rnd() * NVar))
        For d = 0 To NVar - 1
            If (MR > 0 AndAlso Rnd() < MR) OrElse d = One Then
                Dim x As Integer = Memory(i).DesignVariables(d)
                Dim v As Integer = CInt(Math.Round(x + (2 * Rnd() - 1) * (x - Memory(k).DesignVariables(d))))
                If v = x Then v = x + If(Rnd() < 0.5, -1, 1)
                C.DesignVariables(d) = v
            End If
        Next
        Return C
    End Function

    '___________________________________________________________________________________________________________
    'Ant Colony: pheromone per variable and section; at the start of a loop evaporation and rank based deposit of
    'the best designs of the archive (= memory); an ant replaces the worst design of the archive if it is better
    Private Sub Main_AntColony(ByVal Imem As Integer, ByRef ret As Integer)
        InitMethodState(True)
        Dim Tau()() As Double = State.Pheromone
        Dim Alpha As Double = Par("Alpha"), Beta As Double = Par("Beta")
        If Imem = 0 Then
            Dim Rho As Double = Par("Rho")
            Dim W As Integer = Math.Max(1, CInt(Math.Round(Par("Elite") * Memory.Count)))
            Dim Ranked = Memory.Where(Function(c) Not Double.IsInfinity(c.PenalizedCost)).OrderBy(Function(c) c.PenalizedCost).Take(W).ToList()
            For d = 0 To NVar - 1
                For s = 0 To Tau(d).Length - 1
                    Tau(d)(s) = Math.Max((1 - Rho) * Tau(d)(s), TAU_MIN)
                Next
            Next
            If Ranked.Count > 0 Then
                Dim BestCost As Double = Math.Max(Ranked(0).PenalizedCost, 1.0E-300)
                For r = 0 To Ranked.Count - 1
                    Dim Amount As Double = (Ranked.Count - r) / CDbl(Ranked.Count) * BestCost / Math.Max(Ranked(r).PenalizedCost, 1.0E-300)
                    For d = 0 To NVar - 1
                        Dim s As Integer = Ranked(r).DesignVariables(d)
                        If s >= 0 AndAlso s < Tau(d).Length Then Tau(d)(s) += Amount
                    Next
                Next
            End If
        End If
        Dim Cand As New OptimizationStructure_.Member_
        ReDim Cand.DesignVariables(NVar - 1)
        For d = 0 To NVar - 1
            Dim w(Ub(d) - Lb(d)) As Double
            For s = Lb(d) To Ub(d)
                w(s - Lb(d)) = Tau(d)(s) ^ Alpha * AcoHeuristic(d, s) ^ Beta
            Next
            Cand.DesignVariables(d) = Lb(d) + If(w.Sum() > 0, Roulette_wheel(w), CInt(Int(Rnd() * w.Length)))
        Next
        EvalAt(Cand, WorstIndex(), True, ret)
    End Sub
    Private Const TAU_MIN As Double = 0.01

    'lighter sections preferred: lightest area / area (math test: 1)
    Private Function AcoHeuristic(ByVal d As Integer, ByVal s As Integer) As Double
        If ETABSModel Is Nothing OrElse ETABSModel.WSections Is Nothing OrElse s >= ETABSModel.FullVarCount(d) Then Return 1
        Return ETABSModel.FullVarArea(d, 0) / Math.Max(ETABSModel.FullVarArea(d, s), 1.0E-12)
    End Function

    '___________________________________________________________________________________________________________
    'Brain Storm: k-means clusters of the memory at the start of a loop (center = best idea of the cluster), new
    'idea from one cluster or the combination of two, Gaussian noise with logsig step size; greedy for member Imem
    Private BsoClusters As List(Of List(Of Integer))
    Private BsoCenters As List(Of Double())

    Private Sub Main_BrainStorm(ByVal Imem As Integer, ByRef ret As Integer)
        If Imem = 0 OrElse BsoClusters Is Nothing Then BsoCluster()
        Dim Base() As Double
        If Rnd() < Par("POne") OrElse BsoClusters.Count < 2 Then
            Dim c As Integer = Roulette_wheel(BsoClusters.Select(Function(x) CDbl(x.Count)).ToArray())
            Base = If(Rnd() < Par("POneCenter"), CType(BsoCenters(c).Clone(), Double()), Pos(BsoClusters(c)(CInt(Int(Rnd() * BsoClusters(c).Count)))))
        Else
            Dim c1 As Integer = CInt(Int(Rnd() * BsoClusters.Count))
            Dim c2 As Integer
            Do : c2 = CInt(Int(Rnd() * BsoClusters.Count)) : Loop While c2 = c1
            Dim A() As Double, B() As Double
            If Rnd() < Par("PTwoCenter") Then
                A = BsoCenters(c1) : B = BsoCenters(c2)
            Else
                A = Pos(BsoClusters(c1)(CInt(Int(Rnd() * BsoClusters(c1).Count)))) : B = Pos(BsoClusters(c2)(CInt(Int(Rnd() * BsoClusters(c2).Count))))
            End If
            Dim wgt As Double = Rnd()
            Base = Enumerable.Range(0, NVar).Select(Function(d) wgt * A(d) + (1 - wgt) * B(d)).ToArray()
        End If
        Dim Xi As Double = 1 / (1 + Math.Exp(-(0.5 * TotalLoops - ILoop) / Math.Max(Par("Slope"), 0.000001))) * Rnd()
        Dim Sc As Double = Par("StepScale")
        For d = 0 To NVar - 1
            Base(d) += Xi * NormalRnd() * Sc * (Ub(d) - Lb(d))
        Next
        EvalAt(ToMember(Base), Imem, True, ret)
    End Sub

    Private Sub BsoCluster()
        Dim N As Integer = Memory.Count
        Dim K As Integer = Math.Max(1, Math.Min(CInt(Par("Clusters")), N))
        'initial centers: K distinct random members
        Dim Order As List(Of Integer) = Enumerable.Range(0, N).OrderBy(Function(x) Rnd()).ToList()
        Dim Cent As List(Of Double()) = Order.Take(K).Select(Function(k1) Pos(k1)).ToList()
        Dim Assign(N - 1) As Integer
        For It = 1 To 20
            Dim Changed As Boolean = False
            For i = 0 To N - 1
                Dim x() As Double = Pos(i)
                Dim Best As Integer = 0, BestD As Double = Double.MaxValue
                For c = 0 To K - 1
                    Dim dd As Double = 0
                    For d = 0 To NVar - 1
                        dd += (x(d) - Cent(c)(d)) ^ 2
                    Next
                    If dd < BestD Then BestD = dd : Best = c
                Next
                If Assign(i) <> Best OrElse It = 1 Then Changed = Changed OrElse Assign(i) <> Best : Assign(i) = Best
            Next
            For c = 0 To K - 1
                Dim cc As Integer = c
                Dim Members = Enumerable.Range(0, N).Where(Function(i) Assign(i) = cc).ToList()
                If Members.Count = 0 Then Continue For
                Cent(c) = Enumerable.Range(0, NVar).Select(Function(d) Members.Average(Function(i) CDbl(Memory(i).DesignVariables(d)))).ToArray()
            Next
            If Not Changed AndAlso It > 1 Then Exit For
        Next
        BsoClusters = New List(Of List(Of Integer))
        BsoCenters = New List(Of Double())
        For c = 0 To K - 1
            Dim cc As Integer = c
            Dim Members = Enumerable.Range(0, N).Where(Function(i) Assign(i) = cc).ToList()
            If Members.Count = 0 Then Continue For
            BsoClusters.Add(Members)
            Dim BestM As Integer = Members.OrderBy(Function(i) Memory(i).PenalizedCost).First()
            BsoCenters.Add(Pos(BestM))         'center = best idea of the cluster
        Next
        'a center replaced by a random idea (Levy option: around the best design)
        If Rnd() < Par("PReplace") AndAlso BsoCenters.Count > 0 Then
            Dim c As Integer = CInt(Int(Rnd() * BsoCenters.Count))
            Dim R As OptimizationStructure_.Member_ = If(FormInfo.OptInfo.LevyFlight, LevyAroundBest(Memory(BsoClusters(c)(0))), RandomMember())
            BsoCenters(c) = New Double(NVar - 1) {}
            For d = 0 To NVar - 1
                BsoCenters(c)(d) = Math.Min(Math.Max(R.DesignVariables(d), Lb(d)), Ub(d))
            Next
        End If
    End Sub

    '___________________________________________________________________________________________________________
    'Crow Search: crow Imem follows the memory of a random crow (flight length) unless that crow is aware (AP):
    'then a random position (Levy option: Levy step around the best); the memory of Imem keeps the better design
    Private Sub Main_CrowSearch(ByVal Imem As Integer, ByRef ret As Integer)
        Dim AP As Double = Par("AP"), FL As Double = Par("FL")
        Dim j As Integer = RandomOther(Imem)
        Dim Cand As OptimizationStructure_.Member_
        If Rnd() >= AP Then
            Dim X() As Double = Pos(Imem), Mj() As Double = Pos(j)
            For d = 0 To NVar - 1
                X(d) += Rnd() * FL * (Mj(d) - X(d))
            Next
            Cand = ToMember(X)
        Else
            Cand = If(FormInfo.OptInfo.LevyFlight, LevyAroundBest(Memory(Imem)), RandomMember())
        End If
        EvalAt(Cand, Imem, True, ret)
    End Sub

    '___________________________________________________________________________________________________________
    'Firefly: firefly Imem moves toward every brighter firefly (lower penalized cost); attractiveness with the
    'normalized distance; random step alpha (x range), damped every loop; greedy
    Private Sub Main_Firefly(ByVal Imem As Integer, ByRef ret As Integer)
        Dim Beta0 As Double = Par("Beta0"), BetaMin As Double = Par("BetaMin"), Gamma As Double = Par("Gamma")
        Dim Alpha As Double = Par("Alpha") * Par("Damping") ^ Math.Max(ILoop - 1, 0)
        Dim RMax As Double = Math.Sqrt(Enumerable.Range(0, NVar).Sum(Function(d) CDbl(Ub(d) - Lb(d)) ^ 2))
        Dim X() As Double = Pos(Imem)
        Dim Own As Double = Memory(Imem).PenalizedCost
        For j = 0 To Memory.Count - 1
            If j = Imem OrElse Not Memory(j).PenalizedCost < Own Then Continue For
            Dim Xj() As Double = Pos(j)
            Dim r2 As Double = 0
            For d = 0 To NVar - 1
                r2 += (X(d) - Xj(d)) ^ 2
            Next
            If RMax > 0 Then r2 /= RMax * RMax
            Dim Beta As Double = (Beta0 - BetaMin) * Math.Exp(-Gamma * r2) + BetaMin
            For d = 0 To NVar - 1
                X(d) += Beta * (Xj(d) - X(d))
            Next
        Next
        For d = 0 To NVar - 1
            X(d) += Alpha * (Rnd() - 0.5) * (Ub(d) - Lb(d))
        Next
        EvalAt(ToMember(X), Imem, True, ret)
    End Sub

    '___________________________________________________________________________________________________________
    'Grasshopper: X_i = c * sum_j c (ub - lb) / 2 s(2 + rem(d_ij, 2)) (x_j - x_i) / d_ij + target, s(r) = f e^(-r/l) - e^(-r),
    'c from cMax to cMin; the new position replaces grasshopper Imem (original GOA, the target is the best design)
    Private Sub Main_Grasshopper(ByVal Imem As Integer, ByRef ret As Integer)
        Dim CMax As Double = Par("CMax"), CMin As Double = Par("CMin"), F As Double = Par("F"), Lscale As Double = Par("L")
        Dim c As Double = CMax - Progress * (CMax - CMin)
        Dim Xi() As Double = Pos(Imem)
        Dim Si(NVar - 1) As Double
        For j = 0 To Memory.Count - 1
            If j = Imem Then Continue For
            Dim Xj() As Double = Pos(j)
            Dim Dist As Double = Math.Sqrt(Enumerable.Range(0, NVar).Sum(Function(d) (Xj(d) - Xi(d)) ^ 2))
            Dim r As Double = 2 + (Dist Mod 2)
            Dim s As Double = F * Math.Exp(-r / Lscale) - Math.Exp(-r)
            For d = 0 To NVar - 1
                Si(d) += c * (Ub(d) - Lb(d)) / 2.0 * s * (Xj(d) - Xi(d)) / (Dist + 2.2204E-16)
            Next
        Next
        Dim Target() As Integer = Leader().DesignVariables
        Dim X(NVar - 1) As Double
        For d = 0 To NVar - 1
            X(d) = c * Si(d) + Target(d)
        Next
        EvalAt(ToMember(X), Imem, False, ret)
    End Sub

    '___________________________________________________________________________________________________________
    'Teaching-Learning: teacher phase X + r (teacher - TF mean), learner phase with a random partner, optional
    'Harmony Search phase (TLBO-HS: an improvised design replaces the worst one if better); greedy
    Private Sub Main_TeachingLearning(ByVal Imem As Integer, ByRef ret As Integer)
        Dim Teacher() As Integer = Leader().DesignVariables
        Dim MeanX(NVar - 1) As Double
        For d = 0 To NVar - 1
            MeanX(d) = MeanOf(d)
        Next
        Dim TF As Double = Par("TF")
        If TF <= 0 Then TF = Math.Round(1 + Rnd())
        Dim X() As Double = Pos(Imem)
        For d = 0 To NVar - 1
            X(d) += Rnd() * (Teacher(d) - TF * MeanX(d))
        Next
        EvalAt(ToMember(X), Imem, True, ret)
        If ret <> 0 Then Return
        'learner phase
        Dim j As Integer = RandomOther(Imem)
        X = Pos(Imem)
        Dim Xj() As Double = Pos(j)
        Dim Better As Boolean = Memory(Imem).PenalizedCost < Memory(j).PenalizedCost
        For d = 0 To NVar - 1
            X(d) += Rnd() * If(Better, X(d) - Xj(d), Xj(d) - X(d))
        Next
        EvalAt(ToMember(X), Imem, True, ret)
        If ret <> 0 OrElse Par("HSPhase") < 0.5 Then Return
        'Harmony Search phase
        Dim HMCR As Double = Par("HMCR"), PARv As Double = Par("PAR")
        Dim H As New OptimizationStructure_.Member_
        ReDim H.DesignVariables(NVar - 1)
        For d = 0 To NVar - 1
            If Rnd() >= HMCR Then
                H.DesignVariables(d) = RandomVariable(d)
            Else
                H.DesignVariables(d) = Memory(CInt(Int(Rnd() * Memory.Count))).DesignVariables(d)
                If Rnd() < PARv Then H.DesignVariables(d) += If(Rnd() < 0.5, -1, 1)
            End If
        Next
        EvalAt(H, WorstIndex(), True, ret)
    End Sub

    '___________________________________________________________________________________________________________
    'Tree-Seed: tree Imem produces SeedsMin .. SeedsMax seeds; per variable with probability ST toward the best
    'tree, otherwise relative to a random tree; the best seed replaces the tree if it is better
    Private Sub Main_TreeSeed(ByVal Imem As Integer, ByRef ret As Integer)
        Dim ST As Double = Par("ST")
        Dim Lo As Integer = CInt(Par("SeedsMin")), Hi As Integer = Math.Max(CInt(Par("SeedsMax")), Lo)
        Dim Ns As Integer = Lo + CInt(Int(Rnd() * (Hi - Lo + 1)))
        Dim Best() As Integer = Leader().DesignVariables
        Dim T() As Double = Pos(Imem)
        Dim BestSeed As OptimizationStructure_.Member_ = Nothing, Found As Boolean = False
        For sd = 1 To Ns
            Dim X(NVar - 1) As Double
            For d = 0 To NVar - 1
                Dim r As Integer = RandomOther(Imem)
                Dim a As Double = 2 * Rnd() - 1
                X(d) = If(Rnd() < ST, T(d) + a * (Best(d) - Memory(r).DesignVariables(d)), T(d) + a * (T(d) - Memory(r).DesignVariables(d)))
            Next
            Dim S As OptimizationStructure_.Member_ = ToMember(X)
            EvaluateOnly(S, ret)
            If ret <> 0 Then Return
            If Not Found OrElse S.PenalizedCost < BestSeed.PenalizedCost Then BestSeed = S : Found = True
        Next
        Dim Replace As Boolean = Found AndAlso BestSeed.PenalizedCost < Memory(Imem).PenalizedCost
        If Replace Then Memory(Imem) = BestSeed
        Update = Replace
        LastUpdatedID = If(Replace, Imem, -1)
    End Sub

    '___________________________________________________________________________________________________________
    'Grey Wolf: X = (X1 + X2 + X3) / 3 with X_k = leader_k - A |C leader_k - X|, A = 2 a r1 - a, C = 2 r2,
    'a from AStart to 0; alpha, beta, delta = the three best designs found (kept); the wolf moves (no greedy)
    Private Sub Main_GreyWolf(ByVal Imem As Integer, ByRef ret As Integer)
        InitMethodState(True)
        Dim Lead As List(Of OptimizationStructure_.Member_) = State.Leaders
        Dim a As Double = Par("AStart") * (1 - Progress)
        Dim X() As Double = Pos(Imem)
        Dim XNew(NVar - 1) As Double
        For d = 0 To NVar - 1
            Dim Sum As Double = 0
            For k = 0 To 2
                Dim A1 As Double = 2 * a * Rnd() - a
                Dim C1 As Double = 2 * Rnd()
                Dim L As Double = Lead(k).DesignVariables(d)
                Sum += L - A1 * Math.Abs(C1 * L - X(d))
            Next
            XNew(d) = Sum / 3
        Next
        Dim Cand As OptimizationStructure_.Member_ = ToMember(XNew)
        EvalAt(Cand, Imem, False, ret)
        If ret <> 0 Then Return
        'leaders: alpha, beta, delta (distinct designs)
        Dim Key As String = String.Join(",", Cand.DesignVariables)
        If Lead.Any(Function(m) String.Join(",", m.DesignVariables) = Key) Then Return
        For k = 0 To 2
            If Cand.PenalizedCost < Lead(k).PenalizedCost Then
                Lead.Insert(k, CopyMember(Cand))
                Lead.RemoveAt(3)
                Exit For
            End If
        Next
    End Sub

    '___________________________________________________________________________________________________________
    'Honey Badger: digging phase (cardioid) or honey phase around the prey (= best design); greedy
    Private Sub Main_HoneyBadger(ByVal Imem As Integer, ByRef ret As Integer)
        Dim Beta As Double = Par("Beta"), Cc As Double = Par("C")
        Dim Alpha As Double = Cc * Math.Exp(-Progress)
        Dim Prey() As Integer = Leader().DesignVariables
        Dim Xi() As Double = Pos(Imem)
        Dim Xn() As Double = Pos((Imem + 1) Mod Memory.Count)
        Dim F As Double = If(Rnd() < 0.5, 1, -1)
        Dim Digging As Boolean = Rnd() < 0.5
        Dim X(NVar - 1) As Double
        For d = 0 To NVar - 1
            Dim S As Double = (Xi(d) - Xn(d)) ^ 2
            Dim Di As Double = Prey(d) - Xi(d)
            'smell intensity, limited to 1: for d -> 0 the original expression grows without bound
            Dim I As Double = Math.Min(Rnd() * S / (4 * Math.PI * Di * Di + 0.0000000001), 1)
            If Digging Then
                Dim Card As Double = Math.Abs(Math.Cos(2 * Math.PI * Rnd()) * (1 - Math.Cos(2 * Math.PI * Rnd())))
                X(d) = Prey(d) + F * Beta * I * Prey(d) + F * Rnd() * Alpha * Di * Card
            Else
                X(d) = Prey(d) + F * Rnd() * Alpha * Di
            End If
        Next
        EvalAt(ToMember(X), Imem, True, ret)
    End Sub

    '___________________________________________________________________________________________________________
    'Aquila: t <= 2/3 T exploration (high soar with vertical stoop / contour flight with Levy), otherwise
    'exploitation (low flight / walk and grab); t, T in loops; greedy
    Private Sub Main_Aquila(ByVal Imem As Integer, ByRef ret As Integer)
        Dim T As Double = TotalLoops
        Dim tt As Double = Math.Min(Math.Max(ILoop, 1), T)
        Dim Best() As Integer = Leader().DesignVariables
        Dim XM(NVar - 1) As Double
        For d = 0 To NVar - 1
            XM(d) = MeanOf(d)
        Next
        Dim Xi() As Double = Pos(Imem)
        Dim X(NVar - 1) As Double
        If tt <= 2.0 / 3.0 * T Then
            If Rnd() <= 0.5 Then
                Dim r As Double = Rnd()
                For d = 0 To NVar - 1
                    X(d) = Best(d) * (1 - tt / T) + (XM(d) - Best(d) * r)
                Next
            Else
                Dim XR() As Double = Pos(CInt(Int(Rnd() * Memory.Count)))
                Const R1 As Double = 10, U As Double = 0.00565, W As Double = 0.005, Theta1 As Double = 3 * Math.PI / 2
                Dim r As Double = Rnd()
                For d = 0 To NVar - 1
                    Dim D1 As Double = d + 1
                    Dim rr As Double = R1 + U * D1
                    Dim th As Double = -W * D1 + Theta1
                    Dim xs As Double = rr * Math.Sin(th), ys As Double = rr * Math.Cos(th)
                    X(d) = Best(d) * LevyAO() + XR(d) + (ys - xs) * r
                Next
            End If
        Else
            If Rnd() <= 0.5 Then
                Dim Al As Double = Par("Alpha"), De As Double = Par("Delta")
                For d = 0 To NVar - 1
                    X(d) = (Best(d) - XM(d)) * Al - Rnd() + ((Ub(d) - Lb(d)) * Rnd() + Lb(d)) * De
                Next
            Else
                Dim QF As Double = tt ^ ((2 * Rnd() - 1) / ((1 - T) ^ 2))
                Dim G1 As Double = 2 * Rnd() - 1
                Dim G2 As Double = 2 * (1 - tt / T)
                For d = 0 To NVar - 1
                    X(d) = QF * Best(d) - (G1 * Xi(d) * Rnd()) - G2 * LevyAO() + Rnd() * G1
                Next
            End If
        End If
        EvalAt(ToMember(X), Imem, True, ret)
    End Sub
End Class
