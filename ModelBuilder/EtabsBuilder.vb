Imports System.Configuration
Imports System.Globalization
Imports System.IO

Public Class BuilderException
    Inherits Exception
    Public Sub New(ByVal Message As String)
        MyBase.New(Message)
    End Sub
End Class

'Builds the ETABS model of one example: geometry, materials, section pools, groups, supports, diaphragms, floor areas
'(step 9.1); loads (9.2); combinations, design preferences, final check (9.3).
Partial Public Class EtabsBuilder
    Public Log As Action(Of String)
    Public Hide As Boolean
    Private ETABSObject As ETABSv1.cOAPI
    Private SapModel As ETABSv1.cSapModel
    Private EtabsPid As Integer = -1
    Private TempDirs As New List(Of String)
    Public BeamPoolSize, ColumnPoolSize As Integer
    Private BeamPool, ColumnPool As List(Of LibSection_)

    Public Const STEEL_MATERIAL As String = "A992Fy50"
    Public Const BEAM_LIST As String = "BeamSectionList"           'names of the optimization program (App.config BeamAutoSelectList / ColumnAutoSelectList)
    Public Const COLUMN_LIST As String = "ColumnSectionList"
    Public Const DIAPHRAGM As String = "D1"

    Private Sub Msg(ByVal Text As String)
        If Log IsNot Nothing Then Log(Text)
    End Sub

    Private Sub Chk(ByVal Ret As Integer, ByVal What As String)
        If Ret <> 0 Then Throw New BuilderException("ETABS API error: " & What & " (code " & Ret & ")")
    End Sub

    Private Shared Function Setting(ByVal Key As String) As String
        Return ConfigurationManager.AppSettings(Key)
    End Function

    Public Shared Function FindETABS() As String
        Dim Configured As String = Setting("ETABSProgramPath")
        If Not String.IsNullOrWhiteSpace(Configured) AndAlso File.Exists(Configured) Then Return Configured
        Dim Root As String = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Computers and Structures")
        If Not Directory.Exists(Root) Then Return Nothing
        Return Directory.GetDirectories(Root, "ETABS *").OrderByDescending(Function(d) d).Select(Function(d) Path.Combine(d, "ETABS.exe")).FirstOrDefault(Function(f) File.Exists(f))
    End Function

    Public Shared Function FindLibrary(ByVal EtabsExe As String) As String
        Dim Configured As String = Setting("SectionPropertyDataPath")
        If Not String.IsNullOrWhiteSpace(Configured) AndAlso File.Exists(Configured) Then Return Configured
        Dim Alt As String = Path.Combine(Path.GetDirectoryName(EtabsExe), "Property Libraries", "AISC16M.xml")
        Return If(File.Exists(Alt), Alt, Nothing)
    End Function

    'A new ETABS instance (never the running one of the user)
    Public Sub Start()
        Dim Exe As String = FindETABS()
        If Exe Is Nothing Then Throw New BuilderException("ETABS program not found: check ETABSProgramPath in ModelBuilder.exe.config")
        Dim Before As New HashSet(Of Integer)(Process.GetProcessesByName("ETABS").Select(Function(x) x.Id))
        Dim H As ETABSv1.cHelper = New ETABSv1.Helper
        ETABSObject = H.CreateObject(Exe)
        If ETABSObject Is Nothing Then Throw New BuilderException("cannot create an ETABS object")
        Chk(ETABSObject.ApplicationStart(), "ApplicationStart")
        SapModel = ETABSObject.SapModel
        Dim Started = Process.GetProcessesByName("ETABS").Where(Function(x) Not Before.Contains(x.Id)).ToList()
        EtabsPid = If(Started.Count = 1, Started(0).Id, -1)
        If Hide Then Chk(ETABSObject.Hide(), "Hide")
        Dim Ver As String = Nothing, VerNum As Double
        If SapModel.GetVersion(Ver, VerNum) = 0 Then Msg("ETABS " & Ver & " (" & Exe & ")")
    End Sub

    'Closes the own ETABS instance only
    Public Sub Shutdown()
        Try
            If ETABSObject IsNot Nothing Then ETABSObject.ApplicationExit(False)
        Catch ex As Exception
            Msg("Warning: ApplicationExit: " & ex.Message)
        End Try
        If EtabsPid > 0 Then
            Try
                Dim P As Process = Process.GetProcessById(EtabsPid)
                If Not P.WaitForExit(30000) Then P.Kill() : Msg("Warning: ETABS process " & EtabsPid & " ended")
            Catch
            End Try
        End If
        ETABSObject = Nothing : SapModel = Nothing
        For Each d In TempDirs
            Try
                If Directory.Exists(d) Then Directory.Delete(d, True)
            Catch
                Msg("Warning: temporary folder not removed: " & d)
            End Try
        Next
        TempDirs.Clear()
    End Sub

    '_______________________________________________________________________________________________
    'The model of one example, saved as <OutDir>\<Name>.EDB. Returns the file name.
    Public Function Build(ByVal Plan As BuildPlan_, ByVal LibFile As String, ByVal OutDir As String) As String
        Dim P As BuildParams_ = Plan.P
        Dim EdbFile As String = Path.Combine(OutDir, P.Name & ".EDB")
        Report.Clear()
        Result = New AuditResult_

        Chk(SapModel.InitializeNewModel(ETABSv1.eUnits.kN_m_C), "InitializeNewModel")
        Chk(SapModel.File.NewBlank(), "File.NewBlank")
        Chk(SapModel.SetPresentUnits(ETABSv1.eUnits.kN_m_C), "SetPresentUnits")

        DefineStories(Plan)
        DefineMaterials(P)
        Dim Beams As List(Of LibSection_) = Nothing, Columns As List(Of LibSection_) = Nothing
        DefineSections(Plan, LibFile, Beams, Columns)
        DefineMembers(Plan, Beams, Columns)
        DefineFloors(Plan)
        DefineLoads(Plan)
        DefineCombinations(Plan)
        DefineDesignSettings(Plan)
        DefineBeamBracing(Plan)

        'check analysis in a temporary folder (the analysis files do not go to the output folder), then the clean model is saved
        Dim Temp As String = Path.Combine(Path.GetTempPath(), "ModelBuilder", P.Name & "_" & Date.Now.ToString("HHmmss"))
        Directory.CreateDirectory(Temp)
        TempDirs.Add(Temp)
        Chk(SapModel.File.Save(Path.Combine(Temp, P.Name & ".EDB")), "File.Save (check copy)")
        PreSizeMembers(Plan)
        AuditModel(Plan)
        RestoreMedianSections(Plan)
        If SapModel.GetModelIsLocked() Then Chk(SapModel.SetModelIsLocked(False), "SetModelIsLocked")
        DefineBeamBracing(Plan)             'again: changing the sections (RestoreMedianSections) clears the design overwrites

        Chk(SapModel.File.Save(EdbFile), "File.Save")
        Msg("model saved: " & EdbFile)
        File.WriteAllText(Path.Combine(OutDir, P.Name & "_report.txt"), Plan.Describe() & Environment.NewLine & Report.ToString(), New System.Text.UTF8Encoding(True))
        Return EdbFile
    End Function

    Private Sub DefineStories(ByVal Plan As BuildPlan_)
        Dim N As Integer = Plan.P.Stories
        Dim Names(N - 1) As String, Heights(N - 1) As Double, Master(N - 1) As Boolean, Similar(N - 1) As String, Splice(N - 1) As Boolean, SpliceH(N - 1) As Double, Colors(N - 1) As Integer
        For k = 1 To N
            Names(k - 1) = "Story" & k
            Heights(k - 1) = Plan.Levels(k) - Plan.Levels(k - 1)
            Master(k - 1) = True                'every story is its own master (no "similar to" stories)
            Similar(k - 1) = "None"
        Next
        Chk(SapModel.Story.SetStories_2(0, N, Names, Heights, Master, Similar, Splice, SpliceH, Colors), "Story.SetStories_2")
        Msg(N & " stories, height " & Plan.Height.ToString("0.###", CultureInfo.InvariantCulture) & " m")
    End Sub

    'A blank ETABS model already holds A992Fy50 (Fy 344.7 MPa, E 199948 MPa), 4000Psi, A615Gr60 and A416Gr270: those are used as they are.
    'A concrete grade that is not there yet (f'c other than the default) is defined here.
    Private Sub DefineMaterials(ByVal P As BuildParams_)
        Dim N As Integer, Names() As String = Nothing
        Chk(SapModel.PropMaterial.GetNameList(N, Names), "PropMaterial.GetNameList")
        If Not Names.Contains(STEEL_MATERIAL) Then Throw New BuilderException("the blank model has no material " & STEEL_MATERIAL)
        Dim Fy, Fu, EFy, EFu, StrainHard, StrainMax, StrainRup, FinalSlope As Double, SSType, SSHys As Integer
        Chk(SapModel.PropMaterial.GetOSteel_1(STEEL_MATERIAL, Fy, Fu, EFy, EFu, SSType, SSHys, StrainHard, StrainMax, StrainRup, FinalSlope), "PropMaterial.GetOSteel_1")
        If Math.Abs(Fy / 1000 - SectionPools.FY_STEEL) > 1 Then Throw New BuilderException("Fy of " & STEEL_MATERIAL & " is " & (Fy / 1000).ToString("0.#", CultureInfo.InvariantCulture) & " MPa, " & SectionPools.FY_STEEL & " MPa expected")
        Dim Fc As Double = P.ConcreteFc
        ConcreteName = (Math.Round(Fc * 145.0377 / 100) * 100).ToString("0", CultureInfo.InvariantCulture) & "Psi"
        If Names.Contains(ConcreteName) Then
            Msg("materials in the model: " & STEEL_MATERIAL & " (Fy " & (Fy / 1000).ToString("0.#", CultureInfo.InvariantCulture) & " MPa), " & ConcreteName & " (existing)")
            Return
        End If
        Chk(SapModel.PropMaterial.SetMaterial(ConcreteName, ETABSv1.eMatType.Concrete), "PropMaterial.SetMaterial " & ConcreteName)
        Chk(SapModel.PropMaterial.SetMPIsotropic(ConcreteName, 4700 * Math.Sqrt(Fc) * 1000, 0.2, 0.0000099), "PropMaterial.SetMPIsotropic " & ConcreteName)
        Chk(SapModel.PropMaterial.SetWeightAndMass(ConcreteName, 1, 23.5616), "PropMaterial.SetWeightAndMass " & ConcreteName)
        Chk(SapModel.PropMaterial.SetOConcrete_1(ConcreteName, Fc * 1000, False, 0, 1, 2, 0.002219, 0.005, -0.1, 0, 0), "PropMaterial.SetOConcrete_1 " & ConcreteName)
        Msg("materials: " & STEEL_MATERIAL & ", " & ConcreteName & " defined (fc " & Fc.ToString("0.##", CultureInfo.InvariantCulture) & " MPa)")
    End Sub

    Private ConcreteName As String

    'Imports the pool sections and defines the auto select lists
    Private Sub DefineSections(ByVal Plan As BuildPlan_, ByVal LibFile As String, ByRef Beams As List(Of LibSection_), ByRef Columns As List(Of LibSection_))
        Dim P As BuildParams_ = Plan.P
        Dim All = SectionPools.ReadW(LibFile)
        Beams = SectionPools.BeamPool(All, P)
        Columns = SectionPools.ColumnPool(All, P)
        BeamPoolSize = Beams.Count : ColumnPoolSize = Columns.Count
        BeamPool = Beams : ColumnPool = Columns
        If Beams.Count < 3 Then Throw New BuilderException("the beam pool has " & Beams.Count & " sections: check BeamMinDepth, BeamMaxDepth and SeismicFilter")
        If Columns.Count < 3 Then Throw New BuilderException("the column pool has " & Columns.Count & " sections: check ColumnSeries, ColumnCa and SeismicFilter")
        Dim Imported As New HashSet(Of String)
        For Each S In Beams.Concat(Columns)
            If Imported.Add(S.Label) Then Chk(SapModel.PropFrame.ImportProp(S.Label, STEEL_MATERIAL, LibFile, S.Label), "PropFrame.ImportProp " & S.Label)
        Next
        Chk(SapModel.PropFrame.SetAutoSelectSteel(BEAM_LIST, Beams.Count, Beams.Select(Function(s) s.Label).ToArray(), Beams(Beams.Count \ 2).Label), "PropFrame.SetAutoSelectSteel " & BEAM_LIST)
        Chk(SapModel.PropFrame.SetAutoSelectSteel(COLUMN_LIST, Columns.Count, Columns.Select(Function(s) s.Label).ToArray(), Columns(Columns.Count \ 2).Label), "PropFrame.SetAutoSelectSteel " & COLUMN_LIST)
        Msg("section pools (" & P.SeismicFilter & " ductility filter): " & BEAM_LIST & " " & Beams.Count & " sections, " & COLUMN_LIST & " " & Columns.Count & " sections")
    End Sub

    Private FrameNames As New Dictionary(Of Object, String)

    'Frames, groups, end releases of the pinned beams
    Private Sub DefineMembers(ByVal Plan As BuildPlan_, ByVal Beams As List(Of LibSection_), ByVal Columns As List(Of LibSection_))
        Dim BeamSection As String = Beams(Beams.Count \ 2).Label, ColumnSection As String = Columns(Columns.Count \ 2).Label
        For Each G In Plan.Groups
            Chk(SapModel.GroupDef.SetGroup(G.Name), "GroupDef.SetGroup " & G.Name)
        Next
        Dim Name As String = ""
        For Each C In Plan.Columns
            Chk(SapModel.FrameObj.AddByCoord(C.X, C.Y, C.Z1, C.X, C.Y, C.Z2, Name, ColumnSection, "", "Global"), "FrameObj.AddByCoord (column)")
            Chk(SapModel.FrameObj.SetGroupAssign(Name, C.Group), "FrameObj.SetGroupAssign " & C.Group)
            If C.Angle <> 0 Then Chk(SapModel.FrameObj.SetLocalAxes(Name, C.Angle), "FrameObj.SetLocalAxes")
            FrameNames(C) = Name
        Next
        Dim II() As Boolean = {False, False, False, False, True, True}, Start() As Double = {0, 0, 0, 0, 0, 0}
        For Each Bm In Plan.Beams
            Chk(SapModel.FrameObj.AddByCoord(Bm.X1, Bm.Y1, Bm.Z, Bm.X2, Bm.Y2, Bm.Z, Name, BeamSection, "", "Global"), "FrameObj.AddByCoord (beam)")
            Chk(SapModel.FrameObj.SetGroupAssign(Name, Bm.Group), "FrameObj.SetGroupAssign " & Bm.Group)
            If Not Bm.Moment Then Chk(SapModel.FrameObj.SetReleases(Name, II, II, Start, Start), "FrameObj.SetReleases")
            FrameNames(Bm) = Name
        Next
        Msg(Plan.Columns.Count & " columns (" & Plan.Columns.Where(Function(c) c.Angle <> 0).Count() & " rotated by 90 degrees), " & Plan.Beams.Count & " beams, " & Plan.Groups.Count & " groups")
    End Sub

    'Supports and rigid diaphragms (the slab is not modeled: the floor loads go to the beams, see Loads.vb)
    Private Sub DefineFloors(ByVal Plan As BuildPlan_)
        Dim NP As Integer, PN() As String = Nothing, PX() As Double = Nothing, PY() As Double = Nothing, PZ() As Double = Nothing
        Chk(SapModel.PointObj.GetAllPoints(NP, PN, PX, PY, PZ), "PointObj.GetAllPoints")
        Dim Interior As New HashSet(Of String)(Plan.Columns.Where(Function(c) c.Kind = "Interior" AndAlso c.Story = 1).Select(Function(c) Key(c.X, c.Y)))
        Dim Fixed() As Boolean = {True, True, True, True, True, True}, Pinned() As Boolean = {True, True, True, False, False, False}
        'the blank model has a rigid diaphragm D1 already
        Dim ND As Integer, DN() As String = Nothing, SemiRigid As Boolean
        Chk(SapModel.Diaphragm.GetNameList(ND, DN), "Diaphragm.GetNameList")
        If DN IsNot Nothing AndAlso DN.Contains(DIAPHRAGM) Then
            Chk(SapModel.Diaphragm.GetDiaphragm(DIAPHRAGM, SemiRigid), "Diaphragm.GetDiaphragm")
            If SemiRigid Then Throw New BuilderException("diaphragm " & DIAPHRAGM & " is semi-rigid")
        Else
            Chk(SapModel.Diaphragm.SetDiaphragm(DIAPHRAGM, False), "Diaphragm.SetDiaphragm")
        End If
        Dim NBase As Integer, NPinned As Integer
        For i = 0 To NP - 1
            If Math.Abs(PZ(i)) < 0.0001 Then
                Dim Pin As Boolean = Plan.P.FrameSystem = "Perimeter" AndAlso Plan.P.InteriorColumnBase = "Pinned" AndAlso Interior.Contains(Key(PX(i), PY(i)))
                Chk(SapModel.PointObj.SetRestraint(PN(i), If(Pin, Pinned, Fixed)), "PointObj.SetRestraint")
                NBase += 1 : If Pin Then NPinned += 1
            Else
                Chk(SapModel.PointObj.SetDiaphragm(PN(i), ETABSv1.eDiaphragmOption.DefinedDiaphragm, DIAPHRAGM), "PointObj.SetDiaphragm")
            End If
        Next
        Msg(NBase & " supports (" & NPinned & " pinned), rigid diaphragm " & DIAPHRAGM & " at " & (NP - NBase) & " joints")
    End Sub

    Private Shared Function Key(ByVal X As Double, ByVal Y As Double) As String
        Return Math.Round(X, 3).ToString(CultureInfo.InvariantCulture) & "|" & Math.Round(Y, 3).ToString(CultureInfo.InvariantCulture)
    End Function

End Class
