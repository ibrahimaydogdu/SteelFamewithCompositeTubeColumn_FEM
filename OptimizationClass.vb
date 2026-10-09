Imports System.IO
Imports System.Xml.Serialization

Partial Public Class OptimizationClass
    Public Memory As List(Of OptimizationStructure_.Member_)
    Public Ub() As Integer
    Public Lb() As Integer
    Public GlobalBest As OptimizationStructure_.Member_
    Public GlobalBestPrint As List(Of String)
    Public BestValue As Double
    Public Histories As List(Of OptimizationStructure_.History_)
    Public FormInfo As MiscellaneousStructures.FormInfo_
    Public FileList As MiscellaneousStructures.FileList_
    Public iter As Integer
    Public ILoop As Integer
    Public Update As Boolean
    Public ETABSModel As ETABS_Class
    Public ETABSCompositeCheck As List(Of String)      'ETABS composite column design of the final design
    Public FinalCheck As OptimizationStructure_.Member_    'final analysis of the best design (all cases, no repair)
    Public FinalConstraints As List(Of String)             'governing constraint values of that analysis
    Private LastUpdatedID As Integer = -1                  'memory position replaced by the last Eval (-1: none)
    Public Model As ModelIdentity_                         'model of the run (backup check)
    Public CostBreakdown As List(Of CostItem_)             'final design
    Public FinalDesignPrint As List(Of String)             'final design (after the ETABS guard): "group: section"
    Public FinalFails As Boolean                           'final analysis or ETABS composite check not satisfied
    Public ExcelFile As String                             'workbook written by Yazdir_Final
    Private Const PITCH_BANDWIDTH As Double = 0.01         'HS pitch adjustment: up to 1 % of the variable range (at least 1 section)

    Public Sub Init_HarmonySearch()
        ReDim FormInfo.OptInfo.HarmonySearch.ParVec(Memory.Count - 1)
        ReDim FormInfo.OptInfo.HarmonySearch.HMCRVec(Memory.Count - 1)
        For i = 0 To Memory.Count - 1
            FormInfo.OptInfo.HarmonySearch.ParVec(i) = FormInfo.OptInfo.HarmonySearch.PAR
            FormInfo.OptInfo.HarmonySearch.HMCRVec(i) = FormInfo.OptInfo.HarmonySearch.HMCR
        Next i
    End Sub
    Public Sub Init_BioGeographyBased()
        ReDim FormInfo.OptInfo.BioGeography.Mu(Memory.Count - 1)
        ReDim FormInfo.OptInfo.BioGeography.Lamda(Memory.Count - 1)
        For i = 0 To Memory.Count - 1
            FormInfo.OptInfo.BioGeography.Mu(i) = (Memory.Count - i) / (Memory.Count)
            FormInfo.OptInfo.BioGeography.Lamda(i) = 1.0 - FormInfo.OptInfo.BioGeography.Mu(i)
        Next
    End Sub
    Public Sub Main(ByRef Imem As Integer, ByRef ret As Integer)
        If FormInfo.OptInfo.OptimizationMethod = OptimizationStructure_.OptMethod_.HarmornySearch Then Main_HarmonySearch(Imem, ret)
        If FormInfo.OptInfo.OptimizationMethod = OptimizationStructure_.OptMethod_.BioGBasedO Then Main_BioGeographyBased(Imem, ret)
        If FormInfo.OptInfo.OptimizationMethod = OptimizationStructure_.OptMethod_.WhaleOpt Then Main_Whale(Imem, ret)
        If FormInfo.OptInfo.OptimizationMethod = OptimizationStructure_.OptMethod_.DandelionOpt Then Main_Dandelion(Imem, ret)
        Select Case FormInfo.OptInfo.OptimizationMethod
            Case OptimizationStructure_.OptMethod_.ArtificialBeeColony : Main_ArtificialBeeColony(Imem, ret)
            Case OptimizationStructure_.OptMethod_.AntColony : Main_AntColony(Imem, ret)
            Case OptimizationStructure_.OptMethod_.BrainStorm : Main_BrainStorm(Imem, ret)
            Case OptimizationStructure_.OptMethod_.CrowSearch : Main_CrowSearch(Imem, ret)
            Case OptimizationStructure_.OptMethod_.Firefly : Main_Firefly(Imem, ret)
            Case OptimizationStructure_.OptMethod_.Grasshopper : Main_Grasshopper(Imem, ret)
            Case OptimizationStructure_.OptMethod_.TeachingLearning : Main_TeachingLearning(Imem, ret)
            Case OptimizationStructure_.OptMethod_.TreeSeed : Main_TreeSeed(Imem, ret)
            Case OptimizationStructure_.OptMethod_.GreyWolf : Main_GreyWolf(Imem, ret)
            Case OptimizationStructure_.OptMethod_.HoneyBadger : Main_HoneyBadger(Imem, ret)
            Case OptimizationStructure_.OptMethod_.Aquila : Main_Aquila(Imem, ret)
            Case OptimizationStructure_.OptMethod_.SocialSpider : Main_SocialSpider(Imem, ret)
        End Select
    End Sub
    Private Sub Main_HarmonySearch(ByRef Imem As Integer, ByRef ret As Integer)
        Dim PAR As Double
        If FormInfo.OptInfo.HarmonySearch.PARChangeType = OptimizationStructure_.PAR_HMCR_ChangeType_.IStatic Then
            PAR = FormInfo.OptInfo.HarmonySearch.PAR
        ElseIf FormInfo.OptInfo.HarmonySearch.PARChangeType = OptimizationStructure_.PAR_HMCR_ChangeType_.Dynamic Then
            Dim ParMax As Double = 0.99
            Dim ParMin As Double = 0.01
            PAR = ParMin + (ParMax - ParMin) * CostSpread(FormInfo.OptInfo.HarmonySearch.PAR)
        ElseIf FormInfo.OptInfo.HarmonySearch.PARChangeType = OptimizationStructure_.PAR_HMCR_ChangeType_.Adaptive Then
            PAR = 1 / (1 + (1 - FormInfo.OptInfo.HarmonySearch.ParVec.Average) / FormInfo.OptInfo.HarmonySearch.ParVec.Average * Math.E ^ (-0.5 * (3 - Rnd() * 6)))
        End If
        Dim HMCR As Double
        If FormInfo.OptInfo.HarmonySearch.HMCRChangeType = OptimizationStructure_.PAR_HMCR_ChangeType_.IStatic Then
            HMCR = FormInfo.OptInfo.HarmonySearch.HMCR
        ElseIf FormInfo.OptInfo.HarmonySearch.HMCRChangeType = OptimizationStructure_.PAR_HMCR_ChangeType_.Dynamic Then
            Dim HMCRMax As Double = 0.99
            Dim HMCRMin As Double = 0.01
            HMCR = HMCRMin + (HMCRMax - HMCRMin) * CostSpread(FormInfo.OptInfo.HarmonySearch.HMCR)
        ElseIf FormInfo.OptInfo.HarmonySearch.HMCRChangeType = OptimizationStructure_.PAR_HMCR_ChangeType_.Adaptive Then
            HMCR = 1 / (1 + (1 - FormInfo.OptInfo.HarmonySearch.HMCRVec.Average) / FormInfo.OptInfo.HarmonySearch.HMCRVec.Average * Math.E ^ (-0.5 * (3 - Rnd() * 6)))
        End If
        Dim Member = New OptimizationStructure_.Member_
        ReDim Member.DesignVariables(Ub.Count - 1)
        For i = 0 To Ub.Count - 1
            If Rnd() > HMCR Then
                Member.DesignVariables(i) = RandomVariable(i)
            Else
                Dim ID As Integer = Math.Floor(Rnd() * Memory.Count)
                If Rnd() > PAR Then
                    Member.DesignVariables(i) = Memory(ID).DesignVariables(i)
                Else
                    'pitch adjustment: 1 .. bandwidth sections up or down (round(0.01 range * (Rnd-0.5)) was 0 in ~40 % of the cases)
                    Dim Bandwidth As Integer = Math.Max(1, CInt(Math.Round(PITCH_BANDWIDTH * (Ub(i) - Lb(i)))))
                    Dim StepSize As Integer = 1 + CInt(Int(Rnd() * Bandwidth))
                    Member.DesignVariables(i) = Memory(ID).DesignVariables(i) + If(Rnd() < 0.5, -StepSize, StepSize)
                End If
            End If
        Next
        Eval(Member, Imem, ret)
        If ret <> 0 Then : LogError("Problem occurred in :Eval") : Exit Sub : End If
        'the parameters belong to the member that entered the memory (Random / Worst update: not Imem)
        If Update AndAlso LastUpdatedID >= 0 Then
            FormInfo.OptInfo.HarmonySearch.ParVec(LastUpdatedID) = PAR
            FormInfo.OptInfo.HarmonySearch.HMCRVec(LastUpdatedID) = HMCR
        End If
    End Sub
    Private Sub Main_BioGeographyBased(ByRef Imem As Integer, ByRef ret As Integer)
        Dim Member As New OptimizationStructure_.Member_
        ReDim Member.DesignVariables(Ub.Count - 1)
        For idv = 0 To Ub.Count - 1
            If Rnd() < FormInfo.OptInfo.BioGeography.Lamda(Imem) Then
                Dim SelectMem As Integer = Roulette_wheel(FormInfo.OptInfo.BioGeography.Mu)
                Member.DesignVariables(idv) = Memory(SelectMem).DesignVariables(idv)
            Else
                Member.DesignVariables(idv) = Memory(Imem).DesignVariables(idv)
            End If
            If Rnd() < FormInfo.OptInfo.BioGeography.MutationRate Then
                If FormInfo.OptInfo.LevyFlight = True Then
                    Member.DesignVariables(idv) = LevyFlight(Memory(Imem), idv)
                Else
                    Member.DesignVariables(idv) = RandomVariable(idv)
                End If
            End If
        Next idv
        Eval(Member, Imem, ret)
        If ret <> 0 Then : LogError("Problem occurred in :Eval") : Exit Sub : End If
    End Sub
    'Best known design: the global best once a feasible design exists, otherwise the best member of the memory
    '(GlobalBest holds only feasible designs; before that its variables are all 0)
    Private Function Leader() As OptimizationStructure_.Member_
        If GlobalBest.DesignVariables IsNot Nothing AndAlso Not Double.IsInfinity(GlobalBest.PenalizedCost) Then Return GlobalBest
        Dim Best As OptimizationStructure_.Member_ = Memory(0)
        For Each M In Memory
            If M.PenalizedCost < Best.PenalizedCost Then Best = M
        Next
        Return Best
    End Function

    Private Sub Main_Whale(ByRef Imem As Integer, ByRef ret As Integer)
        Dim a1 As Double = 2.0 - CDbl(iter) * ((2.0) / CDbl(FormInfo.OptInfo.MaxFuncEvaluation)) ' % a is a parameter that drops linearly from  2 to 0
        Dim a2 As Double = -1.0 + CDbl(iter) * ((-1.0) / CDbl(FormInfo.OptInfo.MaxFuncEvaluation)) 'a2 is a parameter that drops linearly from  -1 to -2
        Dim Member As New OptimizationStructure_.Member_
        ReDim Member.DesignVariables(Ub.Count - 1)
        Dim Lead() As Integer = Leader().DesignVariables
        For idv = 0 To Ub.Count - 1
            'Compute parameters C and A
            Dim A As Double = 2.0 * a1 * CDbl(Rnd()) - a1
            Dim C As Double = 2 * Rnd()
            'Spiral position update
            Dim b As Double = 1.0
            Dim l1 As Double = (a2 - 1.0) * Rnd() + 1.0
            If Rnd() < 0.5 Then '!50% chance to choose between shrinking containment mechanism and spiral model
                If Math.Abs(A) >= 1 Then
                    Dim X_rand As OptimizationStructure_.Member_ = Memory(Math.Floor(Memory.Count * Rnd()))
                    Dim D_X_rand As Double = Math.Abs(C * X_rand.DesignVariables(idv) - Memory(Imem).DesignVariables(idv))
                    Member.DesignVariables(idv) = Math.Floor(X_rand.DesignVariables(idv) - A * D_X_rand)
                Else
                    Dim D_Leader As Double = Math.Abs(C * Lead(idv) - Memory(Imem).DesignVariables(idv))
                    Member.DesignVariables(idv) = Math.Floor(Lead(idv) - A * D_Leader)
                End If
            Else
                Dim Distance2Leader As Double = Math.Abs(Lead(idv) - Memory(Imem).DesignVariables(idv))
                Member.DesignVariables(idv) = Math.Floor(Distance2Leader * Math.Exp(b * l1) * Math.Cos(l1 * 2 * Math.PI) + Lead(idv))
            End If
        Next idv
        Eval(Member, Imem, ret)
        If ret <> 0 Then : LogError("Problem occurred in :Eval") : Exit Sub : End If
    End Sub

    'Dandelion optimizer (Zhao et al. 2022). The stages work on a continuous position X; the design variables are
    'rounded once at the end (rounding after every stage lost the small moves).
    Private Sub Main_Dandelion(ByRef Imem As Integer, ByRef ret As Integer)
        Dim Member As New OptimizationStructure_.Member_
        ReDim Member.DesignVariables(Ub.Count - 1)
        Dim T As Double = Math.Max(FormInfo.OptInfo.MaxFuncEvaluation, 2)          'T = 1 divided by zero in a
        Dim X(Ub.Count - 1) As Double
        Dim Clip = Sub()
                       For j = 0 To X.Length - 1
                           X(j) = Math.Min(Math.Max(X(j), Lb(j)), Ub(j))
                       Next
                   End Sub
        'Rising stage
        Dim alpha As Double = Rnd() * ((1 / T ^ 2) * iter ^ 2 - 2 / T * iter + 1) ' eq.(8) in this paper
        Dim a As Double = -1 / (T ^ 2 - 2 * T + 1)
        Dim b As Double = -2 * a
        Dim c As Double = 1 - a - b
        Dim k As Double = 1 - Rnd() * (c + a * iter ^ 2 + b * iter) ' eq.(11) In this paper
        For idv = 0 To Ub.Count - 1
            If NormalRnd() < 1.5 Then
                Dim lamb As Double = Math.Max(Math.Abs(NormalRnd()), 0.000001)
                Dim theta As Double = (2 * Rnd() - 1) * Math.PI
                Dim row As Double = 1 / Math.Exp(theta)
                Dim vx As Double = row * Math.Cos(theta)
                Dim vy As Double = row * Math.Sin(theta)
                Dim newv As Double = Rnd() * (Ub(idv) - Lb(idv)) + Lb(idv)
                X(idv) = Memory(Imem).DesignVariables(idv) + alpha * vx * vy * Lognpdf(lamb, 0, 1) * (newv - Memory(Imem).DesignVariables(idv)) ' eq.(5) in this paper
            Else
                X(idv) = Memory(Imem).DesignVariables(idv) * k
            End If
        Next idv
        Clip()

        'Decline stage
        For idv = 0 To Ub.Count - 1
            Dim dandelions_mean As Double = 0
            For i = 0 To Memory.Count - 1
                dandelions_mean += Memory(i).DesignVariables(idv)
            Next i
            dandelions_mean /= Memory.Count
            Dim beta As Double = NormalRnd()
            X(idv) = X(idv) - beta * alpha * (dandelions_mean - beta * alpha * X(idv)) ' eq.(13) In this paper
        Next idv
        Clip()

        'Landing stage
        Dim Elite() As Integer = Leader().DesignVariables
        For idv = 0 To Ub.Count - 1
            X(idv) = Elite(idv) + Steplength(1.5) * alpha * (Elite(idv) - X(idv) * (2 * iter / T)) ' eq.(15) In this paper
        Next idv
        Clip()
        For idv = 0 To Ub.Count - 1
            Member.DesignVariables(idv) = CInt(Math.Round(X(idv)))
        Next idv

        ' Calculated all dandelion seeds' fitness values
        Eval(Member, Imem, ret)
        If ret <> 0 Then : LogError("Problem occurred in :Eval") : Exit Sub : End If


    End Sub
    Public Sub RandomGenerate(ByRef Member As OptimizationStructure_.Member_, ByVal i As Integer, ByRef ret As Integer)
        Member = New OptimizationStructure_.Member_
        ReDim Member.DesignVariables(Ub.Count - 1)
        For j = 0 To Ub.Count - 1
            Member.DesignVariables(j) = RandomVariable(j)
        Next

        If FormInfo.OptInfo.TestWithMath = True Then
            Call Math_Evaluate(Member)
        Else
            ETABSModel.Evaluate(Member, iter, ret)
            If ret <> 0 Then : LogError("Problem occurred in :Evaluate") : Exit Sub : End If
        End If
        GlobalBestCheck(Member, i)
    End Sub
    Private Function Roulette_wheel(ByVal x() As Double) As Integer
        Dim RandomNum As Double = x.Sum * Rnd()
        Dim Select_N As Double = x(0)
        Dim SelectIndex As Integer = 0
        Do While RandomNum > Select_N And SelectIndex < x.Length - 1
            SelectIndex += 1
            Select_N += x(SelectIndex)
        Loop
        Return SelectIndex
    End Function
    Private Function LevyFlight(ByVal Member As OptimizationStructure_.Member_, ByVal idv As Integer) As Integer

        'Levy flights
        'Levy exponent And coefficient
        'For details, see equation (2.21), Page 16 (chapter 2) of the book
        'X.S.Yang, Nature - Inspired Metaheuristic Algorithms, 2nd Edition, Luniver Press, (2010).
        Dim id As Integer
        Dim Beta As Double = 1.5
        Dim Gamma1 As Double = 1.329340388179137
        Dim Gamma2 = 0.906402477055477
        Dim Sigma As Double = ((Gamma1 * Math.Sin(Math.PI * Beta / 2)) / (Gamma2 * Beta * 2 ^ ((Beta - 1) / 2))) ^ (1 / Beta)
        'This Is a simple way of implementing Levy flights
        'For standard random walks, use step=1;

        Dim URN As Double = NormalRnd() * Sigma
        Dim RZD As Double = NormalRnd()
        Do While Math.Abs(RZD) < 0.01          'was RZD < 0.01: rejected every negative value
            RZD = NormalRnd()
        Loop
        'Levy flights by Mantegna's algorithm	
        Dim STEPLevy As Double = URN / (Math.Abs(RZD)) ^ (1 + Beta)
        'In the Next equation, the difference factor (s-best) means that 
        'when the solution Is the best solution, it remains unchanged.     
        Dim Best() As Integer = Leader().DesignVariables
        Dim STSZ As Double = 0.01 * STEPLevy * (Member.DesignVariables(idv) - Best(idv))
        Dim RKD As Double = NormalRnd()
        'Here the factor 0.01 comes from the fact that L/100 should the typical
        'step Size of walks/flights where L Is the typical lenghtscale; 
        'otherwise, Levy flights may become too aggresive/efficient, 
        'which makes New solutions (even) jump out side of the design domain (And thus wasting evaluations).
        'Now the actual random walks Or flights
        'small random walk of -2 .. +2 sections in 40 % of the cases (Floor(-2 + 4 RVD) with RVD <= 0.4 gave only -2 / -1)
        Dim int1 As Integer = 0
        If Rnd() <= 0.4 Then int1 = CInt(Int(Rnd() * 5)) - 2
        id = Best(idv) + Math.Floor(STSZ * RKD) + int1
        Return id
    End Function

    Private Function Steplength(ByVal beta As Double) As Double
        ' beta Is set to 1.5 in this paper
        Dim num As Double = (1 + beta) * Math.Sin(Math.PI * beta / 2)
        Dim den As Double = 1.6168504121556959 'Gamma((1 + beta) / 2) * beta * 2 ^ ((beta - 1) / 2)
        Dim sigma_u As Double = (num / den) ^ (1 / beta)
        Dim u As Double = NormalRnd() * sigma_u 'Random('Normal',0,sigma_u,n,m);
        Dim v As Double = NormalRnd() ' Random('Normal',0,1,n,m);
        Return u / (Math.Max(Math.Abs(v), 0.000001) ^ (1 / beta)) * 0.1
    End Function

    'Standard normal random number (Box-Muller) from the seeded VB generator, so a seed repeats the run
    Private Shared Function NormalRnd() As Double
        Dim u1 As Double = 1.0 - Rnd()           '(0, 1]
        Dim u2 As Double = Rnd()
        Return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2)
    End Function
    Private Function Lognpdf(ByVal x As Double, ByVal mu As Double, ByVal sigma As Double) As Double
        Return 1 / (x * sigma * Math.Sqrt(2 * Math.PI)) * Math.Exp(-(Math.Log(x) - mu) ^ 2 / (2 * sigma ^ 2))
    End Function

    Private Sub GlobalBestCheck(ByVal MemberG As OptimizationStructure_.Member_, ByVal Iloop As Integer)
        If MemberG.PenalizedCost < BestValue Then
            Dim History As OptimizationStructure_.History_
            History.Iter = iter
            History.Penalty = MemberG.Penalty
            History.ILoop = Iloop
            History.Cost = MemberG.PenalizedCost
            Histories.Add(History)
            BestValue = MemberG.PenalizedCost
        End If
        If MemberG.PenalizedCost < GlobalBest.PenalizedCost And MemberG.Penalty = 0 Then
            GlobalBest = MemberG
            GlobalBest.DesignVariables = CType(MemberG.DesignVariables.Clone(), Integer())
            GlobalBestPrint = New List(Of String) From {
                "Global Best: " & CStr(GlobalBest.CostValue),
                "Group Name   Variable Name"
            }
            If FormInfo.OptInfo.TestWithMath = True Then
                For i = 0 To Ub.Count - 1
                    GlobalBestPrint.Add("Variable " & CStr(i) & ": " & CStr(MemberG.DesignVariables(i)))
                Next
            Else


                GlobalBestPrint.AddRange(ETABSModel.DescribeDesign(CType(GlobalBest.DesignVariables.Clone(), Integer())))
            End If
        End If
    End Sub
    Public Sub ClearDuplicates(ByRef ret As Integer)
        'Members are equal if their design variables are equal (Distinct() compared array references)
        Dim Before As Integer = Memory.Count
        Memory = Memory.GroupBy(Function(c) String.Join(",", c.DesignVariables)).Select(Function(g) g.First()).ToList()
        If Memory.Count <> Before Then MemberStateChanged()
        For i = Memory.Count To FormInfo.OptInfo.MemorySize - 1
            Dim Member As New OptimizationStructure_.Member_
            RandomGenerate(Member, ILoop, ret)
            If ret <> 0 Then : LogError("Problem occurred in :RandomGenerate") : Exit Sub : End If
            Memory.Add(Member)
        Next i
    End Sub
    'Uniform random integer in [Lb, Ub] (upper bound included)
    Private Function RandomVariable(ByVal i As Integer) As Integer
        Return Math.Min(Lb(i) + CInt(Int((Ub(i) - Lb(i) + 1) * Rnd())), Ub(i))
    End Function
    '(Max - Average) / (Max - Min) of the penalized costs, fallback if all costs are equal
    Private Function CostSpread(ByVal Fallback As Double) As Double
        Dim Costs As List(Of Double) = Memory.Select(Function(c) c.PenalizedCost).Where(Function(c) Not Double.IsInfinity(c)).ToList()
        If Costs.Count = 0 OrElse Costs.Max() = Costs.Min() Then Return Fallback
        Return (Costs.Max() - Costs.Average()) / (Costs.Max() - Costs.Min())
    End Function
    Public Sub LogError(ByVal msg As String)
        If ETABSModel IsNot Nothing Then ETABSModel.Errorlogprint(msg)
    End Sub
    Private Sub UBLBCheck(ByRef Member As OptimizationStructure_.Member_)
        For i = 0 To Ub.Count - 1
            If Member.DesignVariables(i) > Ub(i) Then Member.DesignVariables(i) = Ub(i)
            If Member.DesignVariables(i) < Lb(i) Then Member.DesignVariables(i) = Lb(i)
        Next i
    End Sub
    Private Sub Eval(ByRef Member As OptimizationStructure_.Member_, ByRef Imem As Integer, ByRef ret As Integer)
        Call UBLBCheck(Member)
        If FormInfo.OptInfo.TestWithMath = True Then
            Call Math_Evaluate(Member)
        Else
            ETABSModel.Evaluate(Member, iter, ret)
            If ret <> 0 Then : LogError("Problem occurred in :Evaluate") : Exit Sub : End If
        End If
        GlobalBestCheck(Member, ILoop)
        Dim CId = Imem
        Update = True
        MemoryUpdate(CId, Member, Update)
        LastUpdatedID = If(Update, CId, -1)
    End Sub
    Private Sub Math_Evaluate(ByRef Member As OptimizationStructure_.Member_)
        Dim Sect_Ind() As Integer = Member.DesignVariables
        Member.CostValue = (1 / 6.931 - (Sect_Ind(2) * Sect_Ind(1)) / (Sect_Ind(3) * Sect_Ind(0))) ^ 2
        Member.Penalty = 0
        Member.PenalizedCost = Member.CostValue
        iter += 1
    End Sub
    Public Sub Math_Init()
        Ub = {60, 60, 60, 60}
        Lb = {12, 12, 12, 12}
    End Sub
    Private Sub MemoryUpdate(ByRef ID As Integer, ByRef Member As OptimizationStructure_.Member_, ByRef Update As Boolean)
        If FormInfo.OptInfo.MemoryUpdateType = OptimizationStructure_.MemoryUpdateType_.NoGreedyRandom Or FormInfo.OptInfo.MemoryUpdateType = OptimizationStructure_.MemoryUpdateType_.GreedyRandom Then
            ID = Math.Floor(Rnd() * Memory.Count)
        ElseIf FormInfo.OptInfo.MemoryUpdateType = OptimizationStructure_.MemoryUpdateType_.NoGreedyWorst Or FormInfo.OptInfo.MemoryUpdateType = OptimizationStructure_.MemoryUpdateType_.GreedyWorst Then
            Dim Worst As Double = Memory.Max(Function(c) c.PenalizedCost)
            ID = Memory.FindIndex(Function(c) c.PenalizedCost = Worst)
        End If
        If FormInfo.OptInfo.MemoryUpdateType = OptimizationStructure_.MemoryUpdateType_.NoGreedyCurrent Or FormInfo.OptInfo.MemoryUpdateType = OptimizationStructure_.MemoryUpdateType_.NoGreedyRandom _
            Or FormInfo.OptInfo.MemoryUpdateType = OptimizationStructure_.MemoryUpdateType_.NoGreedyWorst Then Memory(ID) = Member
        If FormInfo.OptInfo.MemoryUpdateType = OptimizationStructure_.MemoryUpdateType_.GreedyCurrent Or FormInfo.OptInfo.MemoryUpdateType = OptimizationStructure_.MemoryUpdateType_.GreedyRandom _
            Or FormInfo.OptInfo.MemoryUpdateType = OptimizationStructure_.MemoryUpdateType_.GreedyWorst Then
            If Memory(ID).PenalizedCost > Member.PenalizedCost Then
                Memory(ID) = Member
            Else
                Update = False
            End If
        End If
    End Sub
    Private Const ETABS_GUARD_STEPS As Integer = 3

    Public Sub Opt_Finalize()
        Dim ret As Integer = 0
        FinalFails = False
        If FormInfo.OptInfo.OptimizationMethod = OptimizationStructure_.OptMethod_.ArtificialBeeColony Then
            LogError("Info: ABC scout bees (abandoned food sources): " & ScoutBees & ", " & If(FormInfo.OptInfo.LevyFlight, "Levy step around the best design", "random sources"))
        End If
        If GlobalBest.PenalizedCost = Double.PositiveInfinity Then
            LogError("Warning: no feasible design found")
            If ETABSModel Is Nothing OrElse Not ETABSModel.Quiet Then ETABS_Class.ShowMessage("No feasible design was found.")
        ElseIf FormInfo.OptInfo.TestWithMath = True Then
            Math_Evaluate(GlobalBest)
        Else
            'Re-analyse a copy of the best design as it is (no repair, all analysis cases), save as <file>_best.EDB.
            'GlobalBest itself is kept: the final values go to FinalCheck.
            ret = ETABSModel.RestoreRunCases()
            If ret <> 0 Then LogError("Problem occurred in :RestoreRunCases")
            Dim Final As OptimizationStructure_.Member_ = GlobalBest
            Final.DesignVariables = CType(GlobalBest.DesignVariables.Clone(), Integer())
            If ret = 0 Then ETABSModel.Evaluate(Final, iter, ret, applyRepair:=False)
            If ret <> 0 Then
                LogError("Problem occurred in :Evaluate (final analysis)")
            Else
                FinalCheck = Final
                FinalConstraints = ETABSModel.ConstraintSummary()
                For Each c In FinalConstraints
                    LogError("Info: final design, " & c)
                Next
                FinalFails = Final.Penalty > 0
                If Final.Penalty > 0 Then LogError("Warning: final analysis of the best design (all analysis cases, no repair) gives penalty " & Final.Penalty.ToString("G4"))
                'composite columns of the best design: ETABS composite column design (ETABS 20+)
                Dim VerifyRet As Integer = ETABSModel.VerifyCompositeWithETABS()
                If VerifyRet <> 0 Then LogError("Problem occurred in :VerifyCompositeWithETABS")
                'ETABS guard: a composite group failing the ETABS design gets the next section, the design is checked again
                For Guard = 1 To ETABS_GUARD_STEPS
                    If VerifyRet <> 0 Then Exit For
                    Dim Vars() As Integer = CType(Final.DesignVariables.Clone(), Integer())
                    If ETABSModel.StepUpETABSFailures(Vars) = 0 Then Exit For
                    ETABS_Class.Report("ETABS guard step " & Guard & ": larger sections for the columns failing the ETABS composite design")
                    Dim Guarded As OptimizationStructure_.Member_ = Final
                    Guarded.DesignVariables = Vars
                    ETABSModel.Evaluate(Guarded, iter, ret, applyRepair:=False)
                    If ret <> 0 Then : LogError("Problem occurred in :Evaluate (ETABS guard)") : Exit For : End If
                    Final = Guarded
                    FinalCheck = Final
                    FinalConstraints = ETABSModel.ConstraintSummary()
                    LogError("Info: ETABS guard step " & Guard & ", cost " & Final.CostValue.ToString("F2") & ", penalty " & Final.Penalty.ToString("G4"))
                    VerifyRet = ETABSModel.VerifyCompositeWithETABS()
                    If VerifyRet <> 0 Then LogError("Problem occurred in :VerifyCompositeWithETABS")
                Next
                FinalFails = Final.Penalty > 0
                If ETABSModel.ETABSRatioByVar.Values.Any(Function(r) r > 1) Then
                    FinalFails = True
                    LogError("Warning: final design still fails the ETABS composite design, see the composite check")
                End If
                ETABSCompositeCheck = ETABSModel.ETABS_print.ETABSCompositeCheck
                'cost breakdown and sections of the final design (= the best design unless the ETABS guard changed it)
                CostBreakdown = ETABSModel.CostBreakdown(Final.DesignVariables)
                FinalDesignPrint = ETABSModel.DescribeDesign(CType(Final.DesignVariables.Clone(), Integer()))
                Dim f As String = FormInfo.FileList.ETABSFile
                Dim SaveRet As Integer = ETABSModel.SapModel.File.Save(Path.Combine(Path.GetDirectoryName(f), Path.GetFileNameWithoutExtension(f) & "_best.EDB"))
                If SaveRet <> 0 Then LogError("Problem occurred in :File.Save (_best.EDB)")
                ret = If(VerifyRet <> 0, VerifyRet, SaveRet)
            End If
        End If
        If GlobalBest.PenalizedCost <> Double.PositiveInfinity Then
            Yazdir_Final()
            If ret = 0 Then LogError(If(FinalFails, "Warning: optimization completed, but the final design does not satisfy all checks (see the warnings above)", "Info: optimization completed successfully"))
        End If
        If ETABSModel IsNot Nothing Then ETABSModel.Close(ret, If(FinalFails, "Optimization completed, but the final design does not satisfy all checks. See the warnings in ErrorLog.txt.", Nothing))
    End Sub

    'Backup of the running search: <output>.backup.xml (next to the output file, independent of the current directory)
    Public Shared Function BackupPath(ByVal OutputFile As String) As String
        Return Path.ChangeExtension(OutputFile, ".backup.xml")
    End Function

    'midLoop: written during a loop (time based); the loop is repeated on restart, so the previous loop number is saved.
    'The file is written to <file>.tmp and then replaces the backup (the previous one stays as <file>.bak): a power
    'failure while writing never leaves a broken backup behind.
    Public Sub Backup_Write(Optional ByVal midLoop As Boolean = False)
        Dim Results = New Class_Backup() With {
            .Memory = Memory,
            .FormInfo = FormInfo,
            .GlobalBest = GlobalBest,
            .GlobalBestPrint = GlobalBestPrint,
            .BestValue = BestValue,
            .Histories = Histories,
            .iter = iter,
            .ILoop = If(midLoop, Math.Max(ILoop - 1, 0), ILoop),
            .SavedAt = Date.Now,
            .Model = Model
        }
        Dim Target As String = BackupPath(FileList.OutputFile)
        Dim Tmp As String = Target & ".tmp"
        Try
            Dim serializer As New XmlSerializer(GetType(Class_Backup))
            Using fs As New FileStream(Tmp, FileMode.Create, FileAccess.Write, FileShare.None, 65536, FileOptions.WriteThrough)
                Using writer As New StreamWriter(fs)
                    serializer.Serialize(writer, Results)
                End Using
            End Using
            If File.Exists(Target) Then
                File.Copy(Target, Target & ".bak", True)
                File.Delete(Target)
            End If
            File.Move(Tmp, Target)
        Catch ex As Exception
            'a failed backup must not stop the search
            LogError("Warning: backup not written (" & Target & "): " & ex.Message)
        End Try
    End Sub

    'Backup of an interrupted run; the previous backup (.bak) if the last one is not readable
    Public Shared Function Backup_Read(ByVal OutputFile As String, ByRef Message As String) As Class_Backup
        Dim Target As String = BackupPath(OutputFile)
        Dim serializer As New XmlSerializer(GetType(Class_Backup))
        Dim Problems As New List(Of String)
        For Each f In {Target, Target & ".bak"}
            If Not File.Exists(f) Then Continue For
            Try
                Using reader As New StreamReader(f)
                    Dim Results = CType(serializer.Deserialize(reader), Class_Backup)
                    Dim Problem As String = CheckContent(Results)
                    If Problem IsNot Nothing Then Throw New InvalidDataException(Problem)
                    Message = "Info: run restarted from " & f & " (saved " & Results.SavedAt.ToString("yyyy-MM-dd HH:mm:ss") & ", " & Results.iter & " analyses, loop " & Results.ILoop & ")"
                    If Problems.Count > 0 Then Message = "Warning: " & Problems(0) & "; " & Message.Substring("Info: ".Length)
                    Return Results
                End Using
            Catch ex As Exception
                Problems.Add(f & ": " & If(TypeOf ex Is InvalidDataException, ex.Message, ex.Message & If(ex.InnerException IsNot Nothing, " " & ex.InnerException.Message, "")))
            End Try
        Next
        Message = If(Problems.Count > 0, "The backup cannot be used:" & Environment.NewLine & String.Join(Environment.NewLine, Problems),
                     "No backup of the output file found: " & Target)
        Return Nothing
    End Function

    'Consistency of a backup read from the file (Nothing: valid)
    Public Shared Function CheckContent(ByVal B As Class_Backup) As String
        If B.FormInfo.OptInfo.MemorySize < 2 Then Return "no optimization settings"
        If B.Memory Is Nothing OrElse B.Memory.Count = 0 Then Return "empty memory"
        Dim N As Integer = If(B.Model IsNot Nothing AndAlso B.Model.GroupNames IsNot Nothing, B.Model.GroupNames.Count, -1)
        If N < 0 AndAlso B.Memory(0).DesignVariables IsNot Nothing Then N = B.Memory(0).DesignVariables.Length
        For k = 0 To B.Memory.Count - 1
            Dim dv() As Integer = B.Memory(k).DesignVariables
            If dv Is Nothing OrElse dv.Length = 0 Then Return "memory member " & (k + 1) & " has no design variables"
            If dv.Length <> N Then Return "memory member " & (k + 1) & " has " & dv.Length & " design variables instead of " & N
            If dv.Any(Function(x) x < 0) Then Return "memory member " & (k + 1) & " has a negative section index"
            If B.Model IsNot Nothing AndAlso B.Model.SectionCount > 0 AndAlso dv.Any(Function(x) x >= B.Model.SectionCount) Then Return "memory member " & (k + 1) & " has a section index outside the library"
        Next
        If B.iter < 0 OrElse B.ILoop < 0 Then Return "negative analysis / loop count"
        Return Nothing
    End Function
    Private Sub Yazdir_Final()
        Dim OptResults = New ClassFinal() With {
            .Analyses = iter,
            .FinalFails = FinalFails,
            .FormInfo = FormInfo,
            .CostBreakdown = CostBreakdown,
            .FinalDesignPrint = FinalDesignPrint,
            .GlobalBest = GlobalBest,
            .Histories = Histories,
            .BestValue = BestValue,
            .GlobalBestPrint = GlobalBestPrint,
            .Seed = FormInfo.Seed,
            .ETABSCompositeCheck = ETABSCompositeCheck,
            .FinalCheck = FinalCheck,
            .FinalConstraints = FinalConstraints
        }
        Dim serializer As New XmlSerializer(GetType(ClassFinal))
        Using writer As New StreamWriter(FileList.OutputFile)
            serializer.Serialize(writer, OptResults)
        End Using
        'the same result as an Excel workbook (<output>.xlsx)
        Try
            ExcelFile = ExcelExport.WriteResult(Path.ChangeExtension(FileList.OutputFile, ".xlsx"), OptResults, FormInfo)
            LogError("Info: result workbook " & ExcelFile)
        Catch ex As Exception
            LogError("Warning: Excel workbook not written: " & ex.Message)
        End Try
    End Sub
End Class
Public Class ClassFinal
    Public Seed As Integer
    Public GlobalBest As OptimizationStructure_.Member_
    Public BestValue As Double
    Public Histories As List(Of OptimizationStructure_.History_)
    Public GlobalBestPrint As List(Of String)
    'per composite group: "Group: ETABS PMM .., shear .. | internal .." (ETABS 20+)
    Public ETABSCompositeCheck As List(Of String)
    'cost / penalty of the best design in the final analysis (all analysis cases, no repair)
    Public FinalCheck As OptimizationStructure_.Member_
    'governing constraints of the final analysis: "<constraint>: <value / limit>"
    Public FinalConstraints As List(Of String)
    Public Analyses As Integer
    Public FinalFails As Boolean
    'cost per group of the final design (+ total row) and its sections ("group: section")
    Public CostBreakdown As List(Of CostItem_)
    Public FinalDesignPrint As List(Of String)
    Public FormInfo As MiscellaneousStructures.FormInfo_      'settings of the run (Excel export of the file)
End Class
Public Class Class_Backup
    Public Memory As List(Of OptimizationStructure_.Member_)
    Public FormInfo As MiscellaneousStructures.FormInfo_
    Public GlobalBest As OptimizationStructure_.Member_
    Public BestValue As Double
    Public Histories As List(Of OptimizationStructure_.History_)
    Public iter As Integer
    Public ILoop As Integer
    Public GlobalBestPrint As List(Of String)
    Public SavedAt As Date
    Public Model As ModelIdentity_          'model of the run (old backups: Nothing)
End Class


