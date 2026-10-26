Imports System.IO
Imports System.Xml.Serialization
Imports System.Globalization

'Initial design cache (Paper 1, PCR-1). The ETABS initial design with the auto select lists and the widening of the bounds for the
'story drift take several minutes and give the same result for every run of the same model and settings. With App.config
'InitialDesignCache = <folder> the result (design sections, SCWB flag, search bounds) is written once to <folder>\<key>.initdesign.xml and
'read by the next runs. The key is the SHA-256 of the model file, App.config, the program, the section libraries and the form settings
'that change the bounds; the seed, the method and the budget are not part of it. Empty (default) = no cache (earlier versions).
Public Class InitialDesignCache_
    Public Key As String
    Public GroupNames As List(Of String)
    Public DesignSecName As List(Of String)
    Public DesignSecID As List(Of Integer)
    Public PMMRatio As List(Of Double)
    Public SCWBActive As Boolean
    Public SteelUb, SteelLb, CompUb, CompLb, Ub, Lb, GUb, GLb As Integer()
    Public LastEvaluated As Integer()       'design of the last ETABS analysis of the widening: analysed again on loading (same model state for the FEM reader)
End Class

Partial Public Class ETABS_Class
    Private ReadOnly InitialDesignFolder As String = If(ReadSetting("InitialDesignCache"), "").Trim()
    'The FEM evaluator reads the model (incl. the automatic end offsets, which ETABS computes from the assigned sections) after the initial
    'design; the cached run must leave the model in the same state: the last design analysed by WidenBoundsForDrift is analysed again
    Private WidenLastEvaluated As Integer()

    Private Function InitialDesignKey() As String
        Dim P As New List(Of String) From {"initdesign-2"}
        P.Add(If(FileHash(FormInfo.FileList.ETABSFile), ""))
        P.Add(If(FileHash(AppDomain.CurrentDomain.SetupInformation.ConfigurationFile), ""))
        P.Add(If(FileHash(Reflection.Assembly.GetExecutingAssembly().Location), ""))
        P.Add(If(FileHash(GetType(FemCore.FemModel).Assembly.Location), ""))
        P.Add(If(File.Exists(TubeSettings_.DefaultPath()), FileHash(TubeSettings_.DefaultPath()), ""))
        P.Add(If(File.Exists(SectionPropertyData), FileHash(SectionPropertyData), ""))
        Dim F As MiscellaneousStructures.FormInfo_ = FormInfo
        P.Add(String.Join("|", F.CompositeColumns, F.CompositeType, F.HybridColumns, F.TransitionMode, F.PDelta, F.DriftComboMode, F.SkipUnusedCases,
                          F.FrameInfo.InterStoryDriftR.ToString("R", CultureInfo.InvariantCulture), F.FrameInfo.TopStoryDriftR.ToString("R", CultureInfo.InvariantCulture),
                          F.FrameInfo.SteelDesignCode, F.CompositeCode))
        If F.GroupTypes IsNot Nothing Then P.Add(String.Join(";", F.GroupTypes.Where(Function(g) g IsNot Nothing).Select(Function(g) g.GroupName & "=" & g.Type.ToString())))
        Using Sha As Security.Cryptography.SHA256 = Security.Cryptography.SHA256.Create()
            Return BitConverter.ToString(Sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(String.Join(Environment.NewLine, P)))).Replace("-", "").Substring(0, 32)
        End Using
    End Function

    Private Function InitialDesignFile(ByVal Key As String) As String
        Return Path.Combine(InitialDesignFolder, Key & ".initdesign.xml")
    End Function

    'True: the initial design and the bounds come from the cache (the ETABS initial design and the widening are skipped)
    Private Function LoadInitialDesign() As Boolean
        If InitialDesignFolder.Length = 0 Then Return False
        Dim Key As String = InitialDesignKey()
        Dim FileName As String = InitialDesignFile(Key)
        If Not File.Exists(FileName) Then Errorlogprint("Info: initial design cache: no entry " & Path.GetFileName(FileName) & " (computed and written at the end)") : Return False
        Dim C As InitialDesignCache_
        Try
            Using R As New StreamReader(FileName)
                C = CType(New XmlSerializer(GetType(InitialDesignCache_)).Deserialize(R), InitialDesignCache_)
            End Using
        Catch ex As Exception
            Errorlogprint("Warning: initial design cache " & FileName & " not readable (" & ex.Message & "): computed again") : Return False
        End Try
        Dim NV As Integer = SteelFrameDesignGroupIDs.Count
        Dim Names = SteelFrameDesignGroupIDs.Select(Function(id) Groups(id).GroupName).ToList()
        If C.Key <> Key OrElse C.GroupNames Is Nothing OrElse Not C.GroupNames.SequenceEqual(Names) OrElse C.Ub Is Nothing OrElse C.Ub.Length <> NV Then
            Errorlogprint("Warning: initial design cache " & FileName & " does not match the model: computed again") : Return False
        End If
        For i = 0 To NV - 1
            Dim id As Integer = SteelFrameDesignGroupIDs(i)
            Groups(id).DesignSecName = C.DesignSecName(i) : Groups(id).DesignSecID = C.DesignSecID(i) : Groups(id).PMMRatio = C.PMMRatio(i)
        Next
        'the design procedure of the variable columns, as in Initialize_UBLB (the auto select sections are not assigned)
        For Each Col In Frames.Where(Function(fr) fr.FrameDesignProcedure = FramePointStoryGroupStructures_.DesignProcedure_.SteelFrameDesign AndAlso fr.GroupName IsNot Nothing AndAlso
                                                   VarIndex.ContainsKey(fr.GroupName) AndAlso fr.FrameDirc = FramePointStoryGroupStructures_.FrameDirc_.Z)
            SapModel.FrameObj.SetDesignProcedure(Col.FrameName, FramePointStoryGroupStructures_.DesignProcedure_.SteelFrameDesign, ETABSv1.eItemType.Objects)
        Next
        SCWBActive = C.SCWBActive
        If SCWBActive Then Errorlogprint("Info: AISC 341 strong column - weak beam is active: the lower bound of the beam groups is opened (lightest section)")
        If SeismicScwbMode <> "legacy" Then Errorlogprint("Info: AISC 341 member ductility and beam bracing checks " & If(SeismicChecksActive, "active", "off") & " (SeismicSCWB " & SeismicScwbMode & ")")
        InitializeDuctilityFilter()
        SteelUb = C.SteelUb : SteelLb = C.SteelLb : CompUb = C.CompUb : CompLb = C.CompLb : Ub = C.Ub : Lb = C.Lb
        If Hybrid Then
            BuildHybridLayout()
            If C.GUb IsNot Nothing AndAlso Not (GUb.SequenceEqual(C.GUb) AndAlso GLb.SequenceEqual(C.GLb)) Then
                Errorlogprint("Warning: initial design cache: hybrid bounds differ from the cached ones: computed again") : Return False
            End If
        Else
            GUb = Ub : GLb = Lb
        End If
        If C.LastEvaluated Is Nothing OrElse C.LastEvaluated.Length <> NV Then Errorlogprint("Warning: initial design cache without the last design of the widening: computed again") : Return False
        CompositeActive = FormInfo.CompositeColumns
        Dim M As New OptimizationStructure_.Member_ With {.DesignVariables = CType(C.LastEvaluated.Clone(), Integer())}
        Dim Dummy As Integer = 0, ret As Integer = 0
        Evaluate(M, Dummy, ret, False)
        If ret <> 0 Then Errorlogprint("Warning: initial design cache: analysis of the last design of the widening failed: computed again") : Return False
        Errorlogprint("Info: initial design and search bounds from the cache " & FileName)
        Return True
    End Function

    Private Sub SaveInitialDesign()
        If InitialDesignFolder.Length = 0 Then Return
        If WidenLastEvaluated Is Nothing Then Errorlogprint("Info: initial design not cached (WidenBoundsForDrift off: the model state cannot be restored)") : Return
        Try
            Directory.CreateDirectory(InitialDesignFolder)
            Dim C As New InitialDesignCache_ With {.Key = InitialDesignKey(), .SCWBActive = SCWBActive,
                .GroupNames = SteelFrameDesignGroupIDs.Select(Function(id) Groups(id).GroupName).ToList(),
                .DesignSecName = SteelFrameDesignGroupIDs.Select(Function(id) Groups(id).DesignSecName).ToList(),
                .DesignSecID = SteelFrameDesignGroupIDs.Select(Function(id) Groups(id).DesignSecID).ToList(),
                .PMMRatio = SteelFrameDesignGroupIDs.Select(Function(id) Groups(id).PMMRatio).ToList(),
                .SteelUb = SteelUb, .SteelLb = SteelLb, .CompUb = CompUb, .CompLb = CompLb, .Ub = Ub, .Lb = Lb, .GUb = GUb, .GLb = GLb,
                .LastEvaluated = WidenLastEvaluated}
            Dim FileName As String = InitialDesignFile(C.Key)
            Dim Tmp As String = FileName & "." & Process.GetCurrentProcess().Id & ".tmp"
            Using W As New StreamWriter(Tmp)
                Call New XmlSerializer(GetType(InitialDesignCache_)).Serialize(W, C)
            End Using
            If File.Exists(FileName) Then File.Delete(Tmp) Else File.Move(Tmp, FileName)        'parallel runs: the first file stays
            Errorlogprint("Info: initial design cached " & FileName)
        Catch ex As Exception
            Errorlogprint("Warning: initial design cache not written: " & ex.Message)
        End Try
    End Sub
End Class
