Imports System.IO
Imports System.Globalization
Imports FemCore

'The two evaluators of the optimizer (StructureEvaluator.vb): ETABS (the existing code of this class) and the own FEM solver.
Partial Public Class ETABS_Class

    Private Backend As IStructureEvaluator          'evaluator of the optimization
    Private EtabsBackend As EtabsEvaluator          'always available: initial design with the auto select lists, final check

    Public ReadOnly Property UsesFem As Boolean
        Get
            Return TypeOf Backend Is FemBackend
        End Get
    End Property

    'Final check of the best design found with the FEM evaluator: from now on ETABS evaluates (the analysis of the last FEM design is not valid)
    Public Sub SwitchToEtabs()
        If Not UsesFem Then Return
        Backend = EtabsBackend
        InvalidateAnalysis()
        Errorlogprint("Info: final check of the best design with ETABS (evaluator switched from FEM to ETABS)")
    End Sub

    'The cheapest designs that the FEM evaluator found feasible (distinct design vectors, repaired): the final stage checks them with ETABS
    'in cost order and takes the first that passes (the best FEM design can be slightly infeasible in ETABS: FEM D/C ratios are 0.4-2.4 % lower)
    Private ReadOnly Archive As New List(Of OptimizationStructure_.Member_)
    Private ReadOnly ArchiveKeys As New HashSet(Of String)
    Private ReadOnly ArchiveSize As Integer = CInt(ReadNumber("FemArchiveSize", 20))

    Friend Sub ArchiveFeasible(ByVal Member As OptimizationStructure_.Member_)
        If Not UsesFem OrElse Member.Penalty > 0 OrElse Member.DesignVariables Is Nothing Then Return
        Dim Key As String = String.Join(",", Member.DesignVariables)
        If ArchiveKeys.Contains(Key) Then Return
        If Archive.Count >= ArchiveSize AndAlso Member.CostValue >= Archive(Archive.Count - 1).CostValue Then Return
        Dim Copy As OptimizationStructure_.Member_ = Member
        Copy.DesignVariables = CType(Member.DesignVariables.Clone(), Integer())
        Dim At As Integer = Archive.FindIndex(Function(m) m.CostValue > Copy.CostValue)
        If At < 0 Then Archive.Add(Copy) Else Archive.Insert(At, Copy)
        ArchiveKeys.Add(Key)
        If Archive.Count > ArchiveSize Then
            ArchiveKeys.Remove(String.Join(",", Archive(Archive.Count - 1).DesignVariables))
            Archive.RemoveAt(Archive.Count - 1)
        End If
    End Sub

    'Candidates for the final ETABS check: the archive, cheapest first
    Public Function FeasibleArchive() As List(Of OptimizationStructure_.Member_)
        Return Archive.ToList()
    End Function

    'ETABS guard of a design found with the FEM evaluator: the variables whose group ratio, story drift or beam/column ratio violates the
    'limits in the ETABS analysis move to larger sections (the steps of the repair rules, at least one position), then the geometric
    'constraints are repaired. Returns the number of changed variables.
    Public Function StepUpViolations(ByRef Vec() As Integer) As Integer
        Dim Sect_Ind() As Integer = GroupVector(Vec)
        Dim Steps() As Integer = RepairSteps()
        Dim Before() As Integer = CType(Sect_Ind.Clone(), Integer())
        For v = 0 To Sect_Ind.Length - 1
            Dim Violates As Boolean = Groups(SteelFrameDesignGroupIDs(v)).PMMRatio > 1 OrElse Steps(v) > 0
            If Not Violates Then Continue For
            'next section(s) of the catalog that is at least as stiff and strong about the major axis as the current one (the next section by area
            'can be less deep)
            Dim Cur As Integer = Sect_Ind(v)
            Dim First As Integer = Math.Min(Cur + Math.Max(Steps(v), 1), GUb(v))
            For k = First To GUb(v)
                If WSections(k).Imajor >= WSections(Cur).Imajor AndAlso WSections(k).PlasticModulusMajor >= WSections(Cur).PlasticModulusMajor Then Sect_Ind(v) = k : Exit For
            Next
        Next
        E1_Modifier_Geometric(Sect_Ind)
        Dim Changed As Integer = 0
        For v = 0 To Sect_Ind.Length - 1
            If Sect_Ind(v) <> Before(v) Then
                Changed += 1
                Errorlogprint("Info: ETABS guard, group " & Groups(SteelFrameDesignGroupIDs(v)).GroupName & ": " & SecName(v, Before(v)) & " -> " & SecName(v, Sect_Ind(v)))
            End If
        Next
        If Sect_Ind IsNot Vec Then EncodeHybrid(Sect_Ind, Vec)
        Return Changed
    End Function

    Public ReadOnly Property EvaluatorName As String
        Get
            Return If(Backend Is Nothing, "ETABS", Backend.Name)
        End Get
    End Property

    'Form option "FEM solver" (FormInfo.UseFem; App.config Evaluator = Fem | Etabs sets its initial state). Fem: the optimization runs on the own solver; a model that it cannot handle keeps ETABS.
    Private Function SelectEvaluator() As Integer
        Backend = EtabsBackend
        If Not FormInfo.UseFem Then Return 0
        Dim Reason As String = Nothing
        Dim B As FemBackend = FemBackend.TryCreate(Me, Reason)
        If B Is Nothing Then
            Errorlogprint("Warning: FEM evaluator not used (" & Reason & "), the optimization runs with ETABS")
            Return 0
        End If
        Backend = B
        InvalidateAnalysis()        'the last ETABS analysis is not a FEM analysis
        Errorlogprint("Info: evaluator FEM (own solver): " & B.Description)
        Return 0
    End Function

    '_______________________________________________________________________________________________
    'ETABS: sections to the frame groups, RunAnalysis, JointDispl, steel design (existing code of the class)
    Friend Class EtabsEvaluator
        Implements IStructureEvaluator

        Private ReadOnly O As ETABS_Class
        Friend UpdateSections As Boolean        'ReadSteelDesign: also read the sections selected by ETABS (auto select lists)

        Public Sub New(ByVal Owner As ETABS_Class)
            O = Owner
        End Sub

        Public ReadOnly Property Name As String Implements IStructureEvaluator.Name
            Get
                Return "ETABS"
            End Get
        End Property

        Public Function AssignSections(ByVal Sect_Ind() As Integer) As Integer Implements IStructureEvaluator.AssignSections
            Return O.E2_SetSection(Sect_Ind)
        End Function

        Public Function Analyze() As Integer Implements IStructureEvaluator.Analyze
            Return O.E3_Analysis()
        End Function

        Public Function ReadDisplacements(ByVal Rows As List(Of DispRow)) As Integer Implements IStructureEvaluator.ReadDisplacements
            Return O.EtabsJointDisplacements(Rows)
        End Function

        Public Function ReadSteelDesign(ByVal Result As Dictionary(Of String, GroupDesign)) As Integer Implements IStructureEvaluator.ReadSteelDesign
            Return O.EtabsSteelDesign(Result, UpdateSections)
        End Function
    End Class

    Friend Function EtabsJointDisplacements(ByVal Rows As List(Of DispRow)) As Integer
        Dim ret As Integer = SelectOutputCases()
        If (ret <> 0) Then Return ret
        Dim NumberResults As Integer
        Dim Obj() As String = Nothing
        Dim Elm() As String = Nothing
        Dim LoadCase() As String = Nothing
        Dim StepType() As String = Nothing
        Dim StepNum() As Double = Nothing
        Dim U1() As Double = Nothing
        Dim U2() As Double = Nothing
        Dim U3() As Double = Nothing
        Dim R1() As Double = Nothing
        Dim R2() As Double = Nothing
        Dim R3() As Double = Nothing
        ret = SapModel.Results.JointDispl("All", ETABSv1.eItemTypeElm.GroupElm, NumberResults, Obj, Elm, LoadCase, StepType, StepNum, U1, U2, U3, R1, R2, R3)
        If (ret <> 0) Then : Errorlogprint("Problem occurred on :Results.JointDispl") : Return ret : End If
        For k = 0 To NumberResults - 1
            Rows.Add(New DispRow With {.Point = Obj(k), .CaseName = LoadCase(k), .U1 = U1(k), .U2 = U2(k)})
        Next k
        Return ret
    End Function

    'ETABS steel design of the model: governing ratios and design errors per group (auto select lists: also the design sections)
    Friend Function EtabsSteelDesign(ByVal Result As Dictionary(Of String, GroupDesign), ByVal updateDesignSections As Boolean) As Integer
        Dim ret As Integer = G1_1_Design()
        If (ret <> 0) Then : Errorlogprint("Problem occurred on :G1_1_Design") : Return ret : End If
        Try
            'one call for the whole model (instead of one per group), results grouped by the design groups
            Dim NumberItems As Integer
            Dim FrameName As String() = Nothing
            Dim Ratio As Double() = Nothing
            Dim RatioType As Integer() = Nothing
            Dim Location As Double() = Nothing
            Dim ComboName As String() = Nothing
            Dim ErrorSummary As String() = Nothing
            Dim WarningSummary As String() = Nothing
            Dim ByGroup As New Dictionary(Of String, List(Of Integer))
            If SteelFrameDesignGroupIDs.Any(Function(id) Not (CompositeActive AndAlso Groups(id).IsComposite)) Then
                If SapModel.DesignSteel.GetSummaryResults("All", NumberItems, FrameName, Ratio, RatioType, Location, ComboName, ErrorSummary, WarningSummary, ETABSv1.eItemType.Group) <> 0 Then NumberItems = 0
                For j = 0 To NumberItems - 1
                    Dim F As Integer
                    If Not FrameIndex.TryGetValue(FrameName(j), F) OrElse Frames(F).GroupName Is Nothing Then Continue For
                    If Not ByGroup.ContainsKey(Frames(F).GroupName) Then ByGroup(Frames(F).GroupName) = New List(Of Integer)
                    ByGroup(Frames(F).GroupName).Add(j)
                Next
            End If
            ret = 0
            Dim DumpFile As String = Environment.GetEnvironmentVariable("ETABS_DUMP")
            If Not String.IsNullOrEmpty(DumpFile) AndAlso NumberItems > 0 Then
                Dim L As New List(Of String) From {"#EVAL"}
                Dim BCText As New Dictionary(Of String, String)
                Try
                    Dim TV, TN As Integer, TF() As String = Nothing, TD() As String = Nothing
                    If SapModel.DatabaseTables.GetTableForDisplayArray("Steel Column Envelope - " & FormInfo.FrameInfo.SteelDesignCode, Nothing, "", TV, TF, TN, TD) = 0 AndAlso TF IsNot Nothing Then
                        Dim iU As Integer = Array.IndexOf(TF, "UniqueName"), iMa As Integer = Array.IndexOf(TF, "BCMajor"), iMi As Integer = Array.IndexOf(TF, "BCMinor")
                        For r = 0 To TN - 1 : BCText(TD(r * TF.Length + iU)) = "BC " & TD(r * TF.Length + iMa) & " " & TD(r * TF.Length + iMi) : Next
                    End If
                Catch
                End Try
                For j = 0 To NumberItems - 1
                    Dim Sect As String = Nothing
                    SapModel.DesignSteel.GetDesignSection(FrameName(j), Sect)
                    Dim bc As String = Nothing
                    BCText.TryGetValue(FrameName(j), bc)
                    L.Add(FrameName(j) & "|" & Sect & "|" & Ratio(j).ToString("0.0000", CultureInfo.InvariantCulture) & "|" & ComboName(j) & "|" & If(ErrorSummary(j), "").Replace(vbCr, " ").Replace(vbLf, " ").Trim() & "|" & If(bc, "BC - -"))
                Next
                File.AppendAllLines(DumpFile, L)
            End If
            Dim BCRatios As Dictionary(Of String, Double) = Nothing
            If NumberItems > 0 AndAlso ErrorSummary IsNot Nothing AndAlso ErrorSummary.Any(Function(m) m IsNot Nothing AndAlso m.Contains(BC_MESSAGE)) Then BCRatios = ReadBeamColumnRatios()
            For i = 0 To SteelFrameDesignGroupIDs.Count - 1
                Dim ID As Integer = SteelFrameDesignGroupIDs(i)
                If CompositeActive AndAlso Groups(ID).IsComposite Then Continue For
                Dim Items As List(Of Integer) = Nothing
                If Not ByGroup.TryGetValue(Groups(ID).GroupName, Items) Then Continue For
                Dim GD As New GroupDesign With {.MaxRatio = Items.Max(Function(j) Ratio(j)), .Items = Items.Count}
                'a beam/column capacity ratio above the limit (AISC 341 strong column - weak beam) is a continuous constraint: its ratio
                'goes into the ratio of the group instead of the error count
                If BCRatios IsNot Nothing Then BCRatios.TryGetValue(Groups(ID).GroupName, GD.BCRatio)
                GD.ErrorCount = Items.Where(Function(j) Not String.IsNullOrEmpty(ErrorSummary(j)) AndAlso Not (BCRatios IsNot Nothing AndAlso OnlyBeamColumnMessage(ErrorSummary(j)))).Count()
                If GD.ErrorCount > 0 Then LogDesignErrors(Groups(ID).GroupName, Items, FrameName, ErrorSummary, BCRatios IsNot Nothing)
                If updateDesignSections Then
                    'largest (by area) design section of the group
                    Dim BestName As String = Nothing
                    Dim BestArea As Double = Double.NegativeInfinity
                    For Each j In Items
                        Dim PropName As String = Nothing
                        ret = SapModel.DesignSteel.GetDesignSection(FrameName(j), PropName)
                        If (ret <> 0) Then : Errorlogprint("Problem occurred on :DesignSteel.GetDesignSection") : Return ret : End If
                        Dim SectID As Integer = WSections.FindIndex(Function(c) c.SectionName = PropName)
                        Dim Area As Double = If(SectID >= 0, WSections(SectID).Area, 0)
                        If Area > BestArea Then : BestArea = Area : BestName = PropName : End If
                    Next
                    Groups(ID).DesignSecName = BestName
                    Groups(ID).DesignSecID = WSections.FindIndex(Function(c) c.SectionName = BestName)
                End If
                Result(Groups(ID).GroupName) = GD
            Next i
        Catch ex As Exception
            Errorlogprint("Problem occurred on :EtabsSteelDesign " & ex.Message)
            ret = -1
        End Try
        Return ret
    End Function

    '_______________________________________________________________________________________________
    'FEM: own solver (FemCore) on the model read from ETABS once; sections of the catalog are assigned to the groups
    Friend Class FemBackend
        Implements IStructureEvaluator

        Private ReadOnly O As ETABS_Class
        Private ReadOnly Fem As FemEvaluator
        Private ReadOnly Model As FemModel
        Private ReadOnly Sections As New Dictionary(Of Integer, FemSection)     'catalog index -> section
        Private ReadOnly Template As FemSection                                  'material of the steel sections
        Private ReadOnly GroupElements As New Dictionary(Of String, List(Of Integer))
        Private Current As FemEvaluation
        Private Designed As Boolean
        Private MissingCases As New HashSet(Of String)
        Public Description As String
        'FEM D/C ratios are 0.4-2.4 % below the ETABS ratios on average (measured on P4): App.config FemRatioMargin multiplies them (default 1.0)
        Private ReadOnly RatioMargin As Double = ReadNumber("FemRatioMargin", 1.0)

        Public ReadOnly Property Name As String Implements IStructureEvaluator.Name
            Get
                Return "FEM"
            End Get
        End Property

        Private Sub New(ByVal Owner As ETABS_Class, ByVal M As FemModel, ByVal T As FemSection)
            O = Owner : Model = M : Template = T
            Fem = New FemEvaluator(M) With {.MassPDelta = Owner.FormInfo.PDelta}
            For Each Id In Owner.SteelFrameDesignGroupIDs
                Dim L As New List(Of Integer)
                For Each nm In Owner.Groups(Id).GroupObjectNames
                    Dim k As Integer = Fem.ElementIndex(nm)
                    If k >= 0 Then L.Add(k)
                Next
                GroupElements(Owner.Groups(Id).GroupName) = L
            Next
        End Sub

        'Nothing (Reason): the model or the settings cannot be handled by the own solver
        Public Shared Function TryCreate(ByVal Owner As ETABS_Class, ByRef Reason As String) As FemBackend
            If Owner.FormInfo.CompositeColumns Then Reason = "composite columns are not available in the FEM evaluator yet" : Return Nothing
            If Owner.FormInfo.CheckStructure Then Reason = "Check Structure always uses ETABS" : Return Nothing
            Try
                Dim Units As ETABSv1.eUnits = Owner.SapModel.GetPresentUnits()
                Dim Rd As New EtabsReader(Owner.SapModel)
                Rd.Log = Sub(s) Return
                Dim M As FemModel = Rd.Read()
                Owner.SapModel.SetPresentUnits(Units)
                If M.Prefs.DirectAnalysis Then M.AxialFactor = 0.8 : M.FlexuralFactor = 0.8
                'harmless notes: sections that are assigned from the catalog later, diaphragms defined by shell objects (the joints carry the diaphragm), modal cases
                Dim Notes = M.Notes.Where(Function(n) Not n.StartsWith("section ") AndAlso Not n.StartsWith("diaphragm from shell objects") AndAlso Not n.EndsWith(" is Modal (not static)")).ToList()
                If Notes.Count > 0 Then Reason = "unsupported model features: " & String.Join("; ", Notes) : Return Nothing
                Dim T As FemSection = M.Sections.FirstOrDefault(Function(s) s.MaterialName = STEEL_MATERIAL AndAlso s.A > 0)
                If T Is Nothing Then T = M.Sections.FirstOrDefault(Function(s) s.A > 0)
                If T Is Nothing Then Reason = "no I section with a material in the model" : Return Nothing
                Dim B As New FemBackend(Owner, M, T)
                B.Description = M.Nodes.Count & " joints, " & M.Elements.Count & " members, DAM " & If(M.Prefs.DirectAnalysis, "0.8", "off") & ", P-Delta " & If(Owner.FormInfo.PDelta, "mass", "off")
                Return B
            Catch ex As Exception
                Reason = ex.Message
                Return Nothing
            End Try
        End Function

        'FemSection of a catalog section (the library is in mm; the FEM model in m, kN)
        Public Function SectionOf(ByVal Index As Integer) As FemSection
            Dim S As FemSection = Nothing
            If Sections.TryGetValue(Index, S) Then Return S
            Dim C = O.WSections(Index)
            S = New FemSection With {.Name = C.SectionName, .MaterialName = Template.MaterialName, .E = Template.E, .G = Template.G,
                .UnitWeight = Template.UnitWeight, .UnitMass = Template.UnitMass, .Fy = Template.Fy, .Fu = Template.Fu,
                .Depth = C.Depth / 1000, .Width = C.FlangeLength / 1000, .FlangeThickness = C.FlangeThickness / 1000, .WebThickness = C.WebThickness / 1000,
                .A = C.Area / 1000000, .As2 = C.ShearAreaMajor / 1000000, .As3 = C.ShearAreaMinor / 1000000, .J = C.TorsionalConstant / 1.0E+12,
                .I22 = C.Iminor / 1.0E+12, .I33 = C.Imajor / 1.0E+12,
                .S22 = Math.Min(C.SectionModulusMinorPos, C.SectionModulusMinorNeg) / 1.0E+9, .S33 = Math.Min(C.SectionModulusMajorPos, C.SectionModulusMajorNeg) / 1.0E+9,
                .Z22 = C.PlasticModulusMinor / 1.0E+9, .Z33 = C.PlasticModulusMajor / 1.0E+9,
                .R22 = C.RadiusofGyrationMinor / 1000, .R33 = C.RadiusofGyrationMajor / 1000, .Fillet = Math.Max(C.KDES - C.FlangeThickness, 0) / 1000}
            Sections(Index) = S
            Return S
        End Function

        'Debug dump: moment beams at the top joint of a column with a design error / moment beams at the joint
        Private Function BeamErrors(ByVal ci As Integer, ByVal Errors() As Boolean) As String
            Dim col = Model.Elements(ci)
            If Math.Abs(col.Axes(0, 2)) <= 0.9 Then Return "-"
            Dim top As Integer = If(Model.Nodes(col.NI).Z > Model.Nodes(col.NJ).Z, col.NI, col.NJ)
            Dim n As Integer = 0, nb As Integer = 0
            For i = 0 To Model.Elements.Count - 1
                Dim b = Model.Elements(i)
                If Math.Abs(b.Axes(0, 2)) > 0.9 OrElse (b.NI <> top AndAlso b.NJ <> top) Then Continue For
                Dim rel = If(b.NI = top, b.ReleaseI, b.ReleaseJ)
                If rel(4) OrElse rel(5) Then Continue For
                nb += 1
                If Errors IsNot Nothing AndAlso Errors(i) Then n += 1
            Next
            Return n & "/" & nb
        End Function

        'Debug dump: Ca of the member, critical Ca of its web (High ductility), flange ratio / limit, Lb/ry ratio / limit, BC ratios
        Private Function SeismicDiag(ByVal el As FemElement, ByVal m As MemberRatios) As String
            Dim S = el.Section
            Dim Root As Double = Math.Sqrt(S.E / (SteelDesign.RyAisc * S.Fy))
            Dim Ca As Double = m.PuMax / (0.9 * S.Fy * S.A)
            Dim wr As Double = SteelDesign.WebHeight(S) / S.WebThickness / Root
            Dim crit As Double = If(wr >= 2.258, (1 - wr / 2.57) / 1.04, 2.68 - wr / 0.88)
            Dim fr As Double = S.Width / (2 * S.FlangeThickness) / Root / 0.32
            Dim Lb As Double = If(el.UnbracedLTB > 0, el.UnbracedLTB, el.Length - el.OffsetI - el.OffsetJ)
            Dim lr As Double = Lb / S.R22 / (0.095 * S.E / (SteelDesign.RyAisc * S.Fy))
            Dim isCol As Boolean = Math.Abs(el.Axes(0, 2)) > 0.9
            Dim rel = Function(r() As Boolean) String.Join("", r.Select(Function(x) If(x, "1", "0")))
            Dim top As Integer = If(Model.Nodes(el.NI).Z > Model.Nodes(el.NJ).Z, el.NI, el.NJ)
            Dim nb As Integer = 0, nbm As Integer = 0
            For Each ot In Model.Elements
                If ot Is el OrElse Math.Abs(ot.Axes(0, 2)) > 0.9 Then Continue For
                For Each nd In {el.NI, el.NJ}
                    If ot.NI = nd OrElse ot.NJ = nd Then
                        nb += 1
                        Dim rr = If(ot.NI = nd, ot.ReleaseI, ot.ReleaseJ)
                        If Not (rr(4) OrElse rr(5)) Then nbm += 1
                    End If
                Next
            Next
            Return String.Format(CultureInfo.InvariantCulture, "{0}|Ca0 {1:0.0000}|Cacrit {2:0.0000}|flange {3:0.000}|lb {4:0.000}|relI {5}|relJ {6}|beams {7}/{8}|{9}", If(isCol, "col", "beam"), Ca, crit, fr, lr, rel(el.ReleaseI), rel(el.ReleaseJ), nbm, nb, el.Group)
        End Function

        Public Function AssignSections(ByVal Sect_Ind() As Integer) As Integer Implements IStructureEvaluator.AssignSections
            Dim c = O.Clock("SetSection") : c.Start()
            Try
                For i = 0 To O.SteelFrameDesignGroupIDs.Count - 1
                    Dim G As String = O.Groups(O.SteelFrameDesignGroupIDs(i)).GroupName
                    Fem.AssignSection(GroupElements(G), SectionOf(Sect_Ind(i)))
                Next
                O.Iter += 1
            Finally
                c.Stop()
            End Try
            Return 0
        End Function

        Public Function Analyze() As Integer Implements IStructureEvaluator.Analyze
            Dim c = O.Clock("Analysis") : c.Start()
            Try
                Current = Fem.Analyze()
                Designed = False
                O.AnalysisFailed = Current.Failed
                If Current.Failed Then O.Errorlogprint("Warning: FEM analysis not finished (" & Current.Message & "), design penalized")
            Finally
                c.Stop()
            End Try
            Return 0
        End Function

        Public Function ReadDisplacements(ByVal Rows As List(Of DispRow)) As Integer Implements IStructureEvaluator.ReadDisplacements
            Dim c = O.Clock("JointDispl") : c.Start()
            Try
                Dim Nodes As New List(Of Integer)
                For i = 0 To Model.Nodes.Count - 1
                    If O.PointIndex.ContainsKey(Model.Nodes(i).Name) Then Nodes.Add(i)
                Next
                Dim Names As IEnumerable(Of String) = O.DriftCaseNames.Concat(O.DriftComboNames)
                Dim Any As Boolean = False
                If Not Current.Failed Then
                    For Each nm In Names
                        Dim Vs = Fem.Displacements(Current, nm)
                        If Vs Is Nothing Then
                            'unknown name (the case type is not supported) or a nonlinear case that could not be solved (Current.Failed is set then)
                            If Fem.FindCombo(nm) Is Nothing AndAlso MissingCases.Add(nm) Then O.Errorlogprint("Warning: FEM evaluator: drift case / combination '" & nm & "' is not in the FEM model, skipped")
                            Continue For
                        End If
                        For Each U In Vs
                            For Each n In Nodes
                                Rows.Add(New DispRow With {.Point = Model.Nodes(n).Name, .CaseName = nm, .U1 = U(n * 6) * 1000, .U2 = U(n * 6 + 1) * 1000})
                            Next
                            Any = True
                        Next
                    Next
                End If
                If Current.Failed Then O.AnalysisFailed = True       'a nonlinear case failed while the displacements were computed
                If Current.Failed OrElse Not Any Then
                    'failed (or no drift result): zero displacements, the design is penalized by AnalysisFailed
                    O.AnalysisFailed = True
                    For Each n In Nodes
                        Rows.Add(New DispRow With {.Point = Model.Nodes(n).Name, .CaseName = "FEM", .U1 = 0, .U2 = 0})
                    Next
                    If Not Current.Failed Then O.Errorlogprint("Warning: FEM evaluator: none of the drift cases is available")
                End If
            Finally
                c.Stop()
            End Try
            Return 0
        End Function

        Public Function ReadSteelDesign(ByVal Result As Dictionary(Of String, GroupDesign)) As Integer Implements IStructureEvaluator.ReadSteelDesign
            Dim c = O.Clock("SteelDesign") : c.Start()
            Try
                If Not Designed AndAlso Not Current.Failed Then Fem.Design(Current) : Designed = True
                If Current.Failed Then O.AnalysisFailed = True
                Dim Errors() As Boolean = Nothing
                If Not Current.Failed AndAlso O.SCWBActive Then Errors = Fem.SeismicErrors(Current, O.DuctilityLevel <> "Moderate")
                Dim DumpFile As String = Environment.GetEnvironmentVariable("FEM_DUMP")
                If Not String.IsNullOrEmpty(DumpFile) AndAlso Not Current.Failed Then
                    Dim L As New List(Of String) From {"#EVAL"}
                    For e = 0 To Model.Elements.Count - 1
                        Dim m = Current.Members(e)
                        L.Add(Model.Elements(e).Name & "|" & Model.Elements(e).Section.Name & "|" & Math.Max(m.PMM, Math.Max(m.VMaj, m.VMin)).ToString("0.0000", CultureInfo.InvariantCulture) & "|" & m.PMMCombo & "|" & If(Errors IsNot Nothing AndAlso Errors(e), "ERR", "") & "|" & SeismicDiag(Model.Elements(e), m) & "|bErr " & BeamErrors(e, Errors) & "|above " & If(Current.Above(e) >= 0, Model.Elements(Current.Above(e)).Name & If(Errors IsNot Nothing AndAlso Errors(Current.Above(e)), " ERR", " ok"), "- -") & "|BC " & Current.BCMajor(e).ToString("0.000", CultureInfo.InvariantCulture) & " " & Current.BCMinor(e).ToString("0.000", CultureInfo.InvariantCulture))
                    Next
                    IO.File.AppendAllLines(DumpFile, L)
                End If
                For Each Id In O.SteelFrameDesignGroupIDs
                    Dim G As String = O.Groups(Id).GroupName
                    Dim GD As New GroupDesign
                    For Each e In GroupElements(G)
                        GD.Items += 1
                        If Current.Failed Then Continue For
                        Dim m = Current.Members(e)
                        Dim Bad As Boolean = O.SCWBActive AndAlso Errors(e)
                        'ETABS leaves the beam/column capacity ratio blank for a member that has another design error
                        Dim BCr As Double = If(O.SCWBActive AndAlso Not Bad, Math.Max(Current.BCMajor(e), Current.BCMinor(e)), 0.0)
                        Dim BCBad As Boolean = BCr > 1.0       'beam/column capacity error
                        'ETABS gives no ratio to a member with a design error (not seismically compact, Lb/ry, beam/column capacity)
                        If Not Bad AndAlso Not BCBad Then GD.MaxRatio = Math.Max(GD.MaxRatio, RatioMargin * Math.Max(m.PMM, Math.Max(m.VMaj, m.VMin)))
                        If O.SCWBActive Then
                            GD.BCRatio = Math.Max(GD.BCRatio, BCr)
                            If Bad Then GD.ErrorCount += 1
                        End If
                    Next
                    If GD.Items > 0 Then Result(G) = GD
                Next
            Finally
                c.Stop()
            End Try
            Return 0
        End Function
    End Class

End Class
