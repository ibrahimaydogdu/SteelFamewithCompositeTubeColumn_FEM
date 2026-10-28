Imports System.IO
Imports System.Globalization

'Paper 1 change request PCR-10: corner columns as unfilled square steel boxes. App.config CornerColumns = mode (default, version
'c9b6321 / tag paper1-pre-box: the corner column groups follow the mode of the run, W sections in the steel mode) | box: a corner column
'group that is not composite takes an unfilled square box of the tube sources (library HSS and built-up boxes of TubeSections.xml) instead
'of a W section. Steel mode: corners unfilled boxes; composite mode: corners filled tubes (unchanged); hybrid: the corner stacks change
'from filled to unfilled boxes at the transition. The boxes are steel members: ETABS Tube sections with the ETABS steel frame design,
'in the FEM evaluator the box checks of FemCore (AISC 360-22 E3 / E7, F7, G4, H1-1; AISC 341 wall limits).
Partial Public Class ETABS_Class
    Friend ReadOnly CornerColumnsMode As String = SettingChoice("CornerColumns", "mode", {"mode", "box"})
    Public Boxes As List(Of TubeSection_)               'unfilled box catalog (CornerColumns = box), sorted by steel area
    'connection limits of the box catalog (AISC 358-22 5.3.2: RBS prequalified for built-up box columns up to 600 mm, user decision
    '2026-10-26): CornerBoxBuiltUpOnly true | false (library HSS too), CornerBoxMaxWidth [mm] 600 | 0 = no limit
    Private ReadOnly CornerBoxBuiltUpOnly As Boolean = (SettingChoice("CornerBoxBuiltUpOnly", "true", {"true", "false"}) = "true")
    Private ReadOnly CornerBoxMaxWidth As Double = If(If(ReadSetting("CornerBoxMaxWidth"), "").Trim() = "0", 0.0, ReadNumber("CornerBoxMaxWidth", 600))
    Private Const BOX_PREFIX As String = "BOX_"

    'Corner column group that takes unfilled boxes (independent of the AISC 341 settings)
    Friend Function CornerBoxGroup(ByVal GroupName As String) As Boolean
        Return CornerColumnsMode = "box" AndAlso GroupName IsNot Nothing AndAlso CornerColumnPrefix.Length > 0 AndAlso
               GroupName.StartsWith(CornerColumnPrefix, StringComparison.OrdinalIgnoreCase)
    End Function

    'Variable v holds an unfilled box (corner group in box mode, currently not composite)
    Public Function IsBoxVar(ByVal v As Integer) As Boolean
        If Boxes Is Nothing Then Return False
        Dim G = Groups(SteelFrameDesignGroupIDs(v))
        Return Not G.IsComposite AndAlso CornerBoxGroup(G.GroupName)
    End Function

    'Group (by id) whose steel option is the box catalog
    Private Function IsBoxGroup(ByVal ID As Integer) As Boolean
        Return Boxes IsNot Nothing AndAlso Groups(ID).IsColumn AndAlso CornerBoxGroup(Groups(ID).GroupName)
    End Function

    Private Function BoxSectionName(ByVal k As Integer) As String
        Return BOX_PREFIX & Boxes(k).Name
    End Function

    'Section library of the tubes: TubeSections.xml Library or the section library of the program
    Private Function TubeLibraryFile() As String
        Dim LibFile As String = If(String.IsNullOrWhiteSpace(TubeSettings.Library), SectionPropertyData, TubeSettings.Library)
        If Not File.Exists(LibFile) AndAlso Not String.IsNullOrWhiteSpace(LibFile) Then
            Dim Alt As String = Path.Combine(Path.GetDirectoryName(SectionPropertyData), Path.GetFileName(LibFile))
            If File.Exists(Alt) Then LibFile = Alt
        End If
        Return LibFile
    End Function

    'Box catalog (after InitializeGroups and the composite materials, before Initialize_UBLB). The steel mode reads TubeSections.xml for the
    'sources of the boxes only (the unit costs of the steel mode stay those of the W sections; TubeSteelCostFactor* apply to the boxes).
    Private Function InitializeBoxCatalog() As Integer
        Boxes = Nothing
        If CornerColumnsMode <> "box" Then Return 0
        Dim Corner = SteelFrameDesignGroupIDs.Where(Function(id) Groups(id).IsColumn AndAlso CornerBoxGroup(Groups(id).GroupName)).ToList()
        If Corner.Count = 0 Then
            Errorlogprint("Warning: CornerColumns = box but no column group starts with '" & CornerColumnPrefix & "' (App.config CornerColumnGroups): no box columns")
            Return 0
        End If
        Try
            If TubeSettings Is Nothing Then
                Dim TubePath As String = TubeSettings_.DefaultPath()
                TubeSettings = If(File.Exists(TubePath), TubeSettings_.Load(TubePath), New TubeSettings_())
                If Not File.Exists(TubePath) Then Errorlogprint("Warning: " & TubePath & " not found; default tube settings are used for the box catalog")
            End If
            Dim Fy, Fu, EFy, EFu, s1, s2, s3, Es, U, A, G As Double, SS, SH As Integer
            Dim ret As Integer = SapModel.PropMaterial.GetOSteel(STEEL_MATERIAL, Fy, Fu, EFy, EFu, SS, SH, s1, s2, s3)
            If ret <> 0 Then Errorlogprint("Problem occurred on :PropMaterial.GetOSteel " & STEEL_MATERIAL) : Return ret
            ret = SapModel.PropMaterial.GetMPIsotropic(STEEL_MATERIAL, Es, U, A, G)
            If ret <> 0 Then Errorlogprint("Problem occurred on :PropMaterial.GetMPIsotropic " & STEEL_MATERIAL) : Return ret
            Dim LimitText As String = Nothing
            Dim Limit As Double = BoxWallLimit(Fy, EFy, Es, LimitText)
            Dim LibFile As String = TubeLibraryFile()
            If Not File.Exists(LibFile) Then Errorlogprint("Warning: tube section library not found (" & LibFile & "): built-up boxes only")
            Dim Log As String = Nothing
            Boxes = TubeSettings.BoxCatalog(LibFile, Fy, Es, Limit, LimitText, CornerBoxBuiltUpOnly, CornerBoxMaxWidth, Log)
            Errorlogprint("Info: corner columns as unfilled boxes (CornerColumns = box) in groups [" & String.Join(", ", Corner.Select(Function(id) Groups(id).GroupName)) & "]; " & Log)
            If Boxes.Count = 0 Then Errorlogprint("No unfilled box sections: check TubeSections.xml") : Return -1
            Return 0
        Catch ex As Exception
            Errorlogprint("Problem occurred while building the box catalog: " & ex.Message)
            Return -1
        End Try
    End Function

    'AISC 341-22 wall limit of the catalog (b / t, Ca = 0; the FEM check applies Case 13 with the real Pr): SMF highly ductile, IMF moderately
    'ductile, Ry = expected / specified yield stress of the model steel; no limit when the AISC 341 provisions are off
    Private Function BoxWallLimit(ByVal Fy As Double, ByVal EFy As Double, ByVal Es As Double, ByRef Text As String) As Double
        If Not Aisc341Active Then Text = "no AISC 341 limit (provisions off or not a SMF / IMF)" : Return Double.MaxValue
        Dim High As Boolean = PrefFrameType = "SMF"
        Dim Ry As Double = If(Fy > 0 AndAlso EFy > 0, EFy / Fy, 0)
        If Ry <= 0 Then Throw New InvalidOperationException("expected yield stress of " & STEEL_MATERIAL & " missing: Ry of the AISC 341-22 box wall limit cannot be set")
        Dim Fl, Wb As Double
        FemCore.SteelDesign.BoxSeismicLimits(High, 0, Fl, Wb)
        Dim Lim As Double = Math.Min(Fl, Wb) * Math.Sqrt(Es / (Ry * Fy))
        Text = "the AISC 341-22 Table D1.1b case 12 limit (" & If(High, "highly", "moderately") & " ductile, b/t <= " & Lim.ToString("0.##", CultureInfo.InvariantCulture) &
               ", Ry " & Ry.ToString("0.###", CultureInfo.InvariantCulture) & ")"
        Return Lim
    End Function

    'Bounds of a box group: the whole box catalog. The ETABS strength design of the initial design (W auto select) does not contain the
    'AISC 341-22 E3.3 corner strength check that governs the corner boxes, so bounds derived from it cut off the boxes the corners need
    '(T2022d diagnostic 2026-10-26); the catalog is small (tens of sections)
    Private Sub BoxBounds(ByVal isec As Integer, ByVal SecID As Integer, ByRef U As Integer, ByRef L As Integer)
        U = Boxes.Count - 1
        L = 0
    End Sub

    'ETABS Tube sections (steel material, wall thickness t, corner radius 0: the properties of the FEM box section). PropFrame.SetTube and
    'SetTube_1 return an error in ETABS 22.6 (probe 2026-10-26, also for 300 x 300 x 10), so the sections come from the database table
    '"Steel Tube"; when one box is missing, every missing box of the catalog is created in the same import (one table import per run)
    Private Const STEEL_TUBE_TABLE As String = "Frame Section Property Definitions - Steel Tube"

    Private Function EnsureBoxSections(ByVal Ks As IEnumerable(Of Integer)) As Integer
        If Ks.All(Function(k) CreatedSections.Contains(BoxSectionName(k))) Then Return 0
        Dim Missing As List(Of Integer) = Enumerable.Range(0, Boxes.Count).Where(Function(k) Not CreatedSections.Contains(BoxSectionName(k))).ToList()
        Dim Fmt = Function(x As Double) x.ToString("R", CultureInfo.InvariantCulture)
        Dim Fields() As String = {"Name", "Material", "FromFile", "t3", "t2", "tf", "tw", "CornerRad", "AMod", "A2Mod", "A3Mod", "JMod", "I2Mod", "I3Mod", "MMod", "WMod", "Notes"}
        Dim Rows As New List(Of String)
        For Each k In Missing
            Dim B As TubeSection_ = Boxes(k)
            Rows.AddRange({BoxSectionName(k), STEEL_MATERIAL, "No", Fmt(B.H), Fmt(B.B), Fmt(B.t), Fmt(B.t), "0", "1", "1", "1", "1", "1", "1", "1", "1",
                           "Unfilled box " & B.Name & If(B.BuiltUp, " (built-up)", " (HSS)")})
        Next
        Dim ret As Integer = SetTable(STEEL_TUBE_TABLE, Fields, Missing.Count, Rows.ToArray())
        If ret <> 0 Then Return ret
        Dim NFatal, NErr, NWarn, NInfo As Integer, ImportLog As String = Nothing
        ret = SapModel.DatabaseTables.ApplyEditedTables(True, NFatal, NErr, NWarn, NInfo, ImportLog)
        If ret <> 0 OrElse NFatal + NErr > 0 Then
            Errorlogprint("Problem occurred on :DatabaseTables.ApplyEditedTables (box sections), errors " & NFatal + NErr & Environment.NewLine & ImportLog)
            Return If(ret <> 0, ret, -1)
        End If
        For Each k In Missing
            Dim PropType As ETABSv1.eFramePropType
            If SapModel.PropFrame.GetTypeOAPI(BoxSectionName(k), PropType) <> 0 OrElse PropType <> ETABSv1.eFramePropType.Box Then
                Errorlogprint("Box section " & BoxSectionName(k) & " was not created (warnings " & NWarn & ")" & Environment.NewLine & ImportLog)
                Return -1
            End If
            CreatedSections.Add(BoxSectionName(k))
        Next
        Errorlogprint("Info: " & Missing.Count & " unfilled box sections created (" & BoxSectionName(Missing.First()) & " ...)")
        Return 0
    End Function

    'Printable box: "HSS16X16X5/8 [BOX 406.4x406.4x14.8]"
    Private Function DescribeBox(ByVal k As Integer) As String
        Dim B As TubeSection_ = Boxes(k)
        Dim F = Function(x As Double) x.ToString("0.#", CultureInfo.InvariantCulture)
        Return B.Name & " [BOX " & F(B.H) & "x" & F(B.B) & "x" & F(B.t) & "]"
    End Function
End Class
