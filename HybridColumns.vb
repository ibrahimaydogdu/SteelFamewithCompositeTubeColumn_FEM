Imports System.Globalization
Imports System.IO

'=====================================================================================================
' Hybrid columns (stage 6): every column group is steel (W) or composite (filled tube / encased), fixed by
' the user (form table) or chosen by the optimization.
' Design vector (optimizer):  [group variables NG] [alternative section of the Optimize groups] [type variables]
'   group variable g : section of group g (Optimize groups: W section)
'   alternative      : composite section of an Optimize group
'   type (PerStack)  : t = number of Optimize groups of a column stack that are composite, from the bottom
'   type (PerGroup)  : 0 = steel, 1 = composite for each Optimize group
' Evaluation: the vector is decoded into one section per group and Groups().IsComposite (DecodeHybrid); the
' evaluation chain (geometry, repair, design, cost) works on that group vector; the repaired sections are
' written back (EncodeHybrid). Column stack: column groups connected vertically, ordered from the bottom.
'=====================================================================================================

Public Enum TransitionMode_
    PerStack = 0
    PerGroup = 1
End Enum

Public Enum GroupColumnType_
    Optimize = 0
    Steel = 1
    Composite = 2
End Enum

Public Class GroupTypeSetting_
    Public GroupName As String
    Public Type As GroupColumnType_
End Class

'Column group of a model (form table)
Public Class ColumnGroupInfo_
    Public Name As String
    Public Columns As Integer
    Public Stories As String
    Public Zmin As Double
End Class

Partial Public Class ETABS_Class

    Public ReadOnly Property Hybrid As Boolean
        Get
            Return FormInfo.CompositeColumns AndAlso FormInfo.HybridColumns
        End Get
    End Property

    Private NG As Integer                               'number of group variables
    Private GMode() As GroupColumnType_                 'type setting per group variable (beams: Steel)
    Private AltVar() As Integer                         'index of the alternative (composite) section variable, -1: none
    Private TypeVar() As Integer                        'PerGroup: index of the type variable, -1: none
    Public Stacks As List(Of List(Of Integer))          'column stacks: group variables from the bottom
    Private StackVar() As Integer                       'PerStack: index of the transition variable of each stack, -1: none
    Private StackOfVar() As Integer                     'stack of each group variable, -1: none
    Private SteelUb(), SteelLb(), CompUb(), CompLb() As Integer
    Public GUb(), GLb() As Integer                      'bounds of the group variables for the current types
    Private ColumnPairs As New List(Of String())        'column-column pairs (upper, lower group) before SkipCtoC
    Private AssignedComp() As Boolean                   'type of the section assigned in ETABS (design procedure)
    Private LastAnalysedComp() As Boolean

    'Column groups: all members vertical (variable groups)
    Private Function IsColumnVar(ByVal v As Integer) As Boolean
        Return Groups(SteelFrameDesignGroupIDs(v)).IsColumn
    End Function

    Private Function CurrentTypes() As Boolean()
        Return SteelFrameDesignGroupIDs.Select(Function(id) Groups(id).IsComposite).ToArray()
    End Function

    '_______________________________________________________________________________________________
    'Stacks and type settings (after InitializeGeometricCons, before Initialize_UBLB)
    Private Function InitializeHybrid() As Integer
        NG = SteelFrameDesignGroupIDs.Count
        'stacks: connected components of the column-column pairs, ordered by the lowest elevation
        Dim Parent(NG - 1) As Integer
        For v = 0 To NG - 1 : Parent(v) = v : Next
        Dim Find As Func(Of Integer, Integer) = Nothing
        Find = Function(x As Integer) If(Parent(x) = x, x, Find(Parent(x)))
        For Each P In ColumnPairs
            Dim a As Integer = Find(VarIndex(P(0))), b As Integer = Find(VarIndex(P(1)))
            If a <> b Then Parent(a) = b
        Next
        Dim Zmin(NG - 1) As Double
        For v = 0 To NG - 1
            Zmin(v) = Double.MaxValue
            For Each n In Groups(SteelFrameDesignGroupIDs(v)).GroupObjectNames
                Dim k As Integer
                If Not FrameIndex.TryGetValue(n, k) Then Continue For
                For Each pn In {Frames(k).FirstPointName, Frames(k).SecondPointName}
                    Zmin(v) = Math.Min(Zmin(v), Points(PointIndex(pn)).Zcoord)
                Next
            Next
        Next
        Stacks = Enumerable.Range(0, NG).Where(Function(v) IsColumnVar(v)).GroupBy(Function(v) Find(v)).
                 Select(Function(g) g.OrderBy(Function(v) Zmin(v)).ToList()).OrderBy(Function(s) Zmin(s(0))).ToList()
        ReDim StackOfVar(NG - 1)
        For v = 0 To NG - 1 : StackOfVar(v) = -1 : Next
        For s = 0 To Stacks.Count - 1
            For Each v In Stacks(s) : StackOfVar(v) = s : Next
        Next
        'type settings of the form (missing: Optimize); beams are steel
        ReDim GMode(NG - 1)
        Dim Settings As New Dictionary(Of String, GroupColumnType_)
        If FormInfo.GroupTypes IsNot Nothing Then
            For Each gt In FormInfo.GroupTypes
                If gt IsNot Nothing AndAlso gt.GroupName IsNot Nothing Then Settings(gt.GroupName) = gt.Type
            Next
        End If
        For v = 0 To NG - 1
            Dim id As Integer = SteelFrameDesignGroupIDs(v)
            If Not IsColumnVar(v) Then
                GMode(v) = GroupColumnType_.Steel
            ElseIf Not Settings.TryGetValue(Groups(id).GroupName, GMode(v)) Then
                GMode(v) = GroupColumnType_.Optimize
            End If
            Groups(id).IsComposite = GMode(v) = GroupColumnType_.Composite
        Next
        'composite fixed above a steel group of the same stack: allowed (user decision), warned
        For Each S In Stacks
            For i = 1 To S.Count - 1
                If GMode(S(i)) = GroupColumnType_.Composite AndAlso S.Take(i).Any(Function(v) GMode(v) = GroupColumnType_.Steel) Then
                    Errorlogprint("Warning: hybrid columns: group " & GName(S(i)) & " is fixed composite above a fixed steel group of its stack")
                End If
            Next
        Next
        For s = 0 To Stacks.Count - 1
            Errorlogprint("Info: hybrid columns, stack " & (s + 1) & " (bottom to top): " &
                          String.Join(", ", Stacks(s).Select(Function(v) GName(v) & "=" & GMode(v).ToString())))
        Next
        Return 0
    End Function

    Private Function GName(ByVal v As Integer) As String
        Return Groups(SteelFrameDesignGroupIDs(v)).GroupName
    End Function

    'Design vector layout and bounds of the optimizer (after the bounds of the group variables)
    Private Sub BuildHybridLayout()
        Dim Ubs As New List(Of Integer), Lbs As New List(Of Integer)
        For v = 0 To NG - 1
            Dim C As Boolean = GMode(v) = GroupColumnType_.Composite
            Ubs.Add(If(C, CompUb(v), SteelUb(v))) : Lbs.Add(If(C, CompLb(v), SteelLb(v)))
        Next
        ReDim AltVar(NG - 1)
        ReDim TypeVar(NG - 1)
        For v = 0 To NG - 1
            AltVar(v) = -1 : TypeVar(v) = -1
            If GMode(v) <> GroupColumnType_.Optimize Then Continue For
            AltVar(v) = Ubs.Count
            Ubs.Add(CompUb(v)) : Lbs.Add(CompLb(v))
        Next
        ReDim StackVar(Stacks.Count - 1)
        For s = 0 To Stacks.Count - 1 : StackVar(s) = -1 : Next
        If FormInfo.TransitionMode = TransitionMode_.PerGroup Then
            For v = 0 To NG - 1
                If GMode(v) <> GroupColumnType_.Optimize Then Continue For
                TypeVar(v) = Ubs.Count
                Ubs.Add(1) : Lbs.Add(0)
            Next
        Else
            For s = 0 To Stacks.Count - 1
                Dim n As Integer = Stacks(s).Where(Function(v) GMode(v) = GroupColumnType_.Optimize).Count()
                If n = 0 Then Continue For
                StackVar(s) = Ubs.Count
                Ubs.Add(n) : Lbs.Add(0)
            Next
        End If
        Ub = Ubs.ToArray() : Lb = Lbs.ToArray()
        GUb = CType(SteelUb.Clone(), Integer()) : GLb = CType(SteelLb.Clone(), Integer())
        Errorlogprint("Info: hybrid columns (" & FormInfo.TransitionMode.ToString() & "): " & Ub.Length & " design variables (" & NG & " groups, " &
                      AltVar.Where(Function(x) x >= 0).Count() & " alternative composite sections, " & (Ub.Length - NG - AltVar.Where(Function(x) x >= 0).Count()) & " type variables)")
    End Sub

    'Is group variable v composite for the design vector Full?
    Private Function CompositeOf(ByVal v As Integer, ByVal Full() As Integer) As Boolean
        Select Case GMode(v)
            Case GroupColumnType_.Steel : Return False
            Case GroupColumnType_.Composite : Return True
        End Select
        If FormInfo.TransitionMode = TransitionMode_.PerGroup Then Return Full(TypeVar(v)) >= 1
        Dim S As List(Of Integer) = Stacks(StackOfVar(v))
        Dim Rank As Integer = S.Where(Function(x) GMode(x) = GroupColumnType_.Optimize).ToList().IndexOf(v)      'from the bottom
        Return Rank < Full(StackVar(StackOfVar(v)))
    End Function

    'Design vector -> one section per group; sets Groups().IsComposite and the group bounds
    Private Function DecodeHybrid(ByVal Full() As Integer) As Integer()
        Dim G(NG - 1) As Integer
        For v = 0 To NG - 1
            Dim C As Boolean = CompositeOf(v, Full)
            Dim id As Integer = SteelFrameDesignGroupIDs(v)
            Groups(id).IsComposite = C
            G(v) = If(C AndAlso AltVar(v) >= 0, Full(AltVar(v)), Full(v))
            If GUb IsNot Nothing AndAlso CompUb IsNot Nothing Then
                GUb(v) = If(C, CompUb(v), SteelUb(v)) : GLb(v) = If(C, CompLb(v), SteelLb(v))
            End If
        Next
        Return G
    End Function

    'Repaired sections of the group vector -> design vector (the type variables are not changed)
    Private Sub EncodeHybrid(ByVal G() As Integer, ByVal Full() As Integer)
        For v = 0 To NG - 1
            If Groups(SteelFrameDesignGroupIDs(v)).IsComposite AndAlso AltVar(v) >= 0 Then Full(AltVar(v)) = G(v) Else Full(v) = G(v)
        Next
    End Sub

    'Group vector of a design vector (hybrid: decoded, types set); other modes: the vector itself
    Public Function GroupVector(ByVal Vec() As Integer) As Integer()
        If Not Hybrid OrElse Vec Is Nothing OrElse Vec.Length = NG OrElse AltVar Is Nothing Then Return Vec
        Return DecodeHybrid(Vec)
    End Function

    'Check Structure: types of the groups of an output file
    Public Sub SetGroupTypes(ByVal Composite() As Boolean)
        For v = 0 To Math.Min(Composite.Length, SteelFrameDesignGroupIDs.Count) - 1
            Groups(SteelFrameDesignGroupIDs(v)).IsComposite = Composite(v) AndAlso IsColumnVar(v)
        Next
    End Sub

    'Printable design: "<group>: <section>" per group; hybrid: the type of every stack
    Public Function DescribeDesign(ByVal Vec() As Integer) As List(Of String)
        Dim G() As Integer = GroupVector(Vec)
        Dim L As List(Of String) = Enumerable.Range(0, SteelFrameDesignGroupIDs.Count).Select(Function(v) GName(v) & ": " & DescribeVariable(v, G(v))).ToList()
        If Hybrid AndAlso Stacks IsNot Nothing Then
            For s = 0 To Stacks.Count - 1
                Dim Comp = Stacks(s).Where(Function(v) Groups(SteelFrameDesignGroupIDs(v)).IsComposite).Select(Function(v) GName(v)).ToList()
                Dim Stl = Stacks(s).Where(Function(v) Not Groups(SteelFrameDesignGroupIDs(v)).IsComposite).Select(Function(v) GName(v)).ToList()
                L.Add("Stack " & (s + 1) & " (bottom to top " & String.Join(", ", Stacks(s).Select(Function(v) GName(v))) & ") = composite [" & String.Join(", ", Comp) &
                      "], steel [" & String.Join(", ", Stl) & "]")
            Next
        End If
        Return L
    End Function

    'Catalog size and steel area of section s of design variable d (ACO heuristic), independent of the current types
    Public Function FullVarCount(ByVal d As Integer) As Integer
        If Not Hybrid OrElse AltVar Is Nothing Then Return CatalogCount(d)
        If d < NG Then Return If(GMode(d) = GroupColumnType_.Composite, CompCount(), WSections.Count)
        If AltVar.Contains(d) Then Return CompCount()
        Return Ub(d) + 1
    End Function

    Public Function FullVarArea(ByVal d As Integer, ByVal s As Integer) As Double
        If Not Hybrid OrElse AltVar Is Nothing Then Return SecArea(d, s)
        If d < NG Then Return If(GMode(d) = GroupColumnType_.Composite, CompArea(s), WSections(s).Area)
        If AltVar.Contains(d) Then Return CompArea(s)
        Return 1
    End Function

    Private Function CompCount() As Integer
        Return If(TubeMode, Tubes.Count, WSections.Count)
    End Function

    Private Function CompArea(ByVal s As Integer) As Double
        Return If(TubeMode, Tubes(s).SteelArea, WSections(s).Area)
    End Function

    'Column-column ratio (> 1: violated). Same catalog: max(area, depth ratio) (as before stage 6); W column on a tube:
    'W depth / tube H, W flange / tube B (the W column stands on the tube); tube on a W column: tube H / W depth, B / flange
    Private Function CtoCRatio(ByVal UpVar As Integer, ByVal UpK As Integer, ByVal DownVar As Integer, ByVal DownK As Integer) As Double
        Dim UT As Boolean = IsTubeVar(UpVar), DT As Boolean = IsTubeVar(DownVar)
        If UT = DT Then Return Math.Max(Math.Max(SecArea(UpVar, UpK) / SecArea(DownVar, DownK), SecDepth(UpVar, UpK) / SecDepth(DownVar, DownK)), 1)
        If DT Then Return Math.Max(Math.Max(WSections(UpK).Depth / Tubes(DownK).H, WSections(UpK).FlangeLength / Tubes(DownK).B), 1)
        Return Math.Max(Math.Max(Tubes(UpK).H / WSections(DownK).Depth, Tubes(UpK).B / WSections(DownK).FlangeLength), 1)
    End Function

    '_______________________________________________________________________________________________
    'Form: column groups of a model (all members vertical frames), read from a working copy with a hidden ETABS
    Public Shared Function ReadColumnGroups(ByVal ModelFile As String, ByRef Message As String) As List(Of ColumnGroupInfo_)
        Dim L As New List(Of ColumnGroupInfo_)
        Dim Dir As String = Path.Combine(Path.GetTempPath(), "SteelFrameOpt", "groups_" & Date.Now.ToString("yyyyMMdd_HHmmss"))
        Dim O As ETABSv1.cOAPI = Nothing
        Try
            Directory.CreateDirectory(Dir)
            Dim Work As String = Path.Combine(Dir, Path.GetFileName(ModelFile))
            File.Copy(ModelFile, Work, True)
            Dim Prog As String = ReadSetting("ETABSProgramPath")
            If String.IsNullOrWhiteSpace(Prog) OrElse Not File.Exists(Prog) Then Prog = FindInstalledETABS()
            Dim H As ETABSv1.cHelper = New ETABSv1.Helper()
            O = H.CreateObject(Prog)
            O.ApplicationStart()
            O.Hide()
            Dim S As ETABSv1.cSapModel = O.SapModel
            If S.File.OpenFile(Work) <> 0 Then Message = "The model could not be opened: " & ModelFile : Return L
            S.SetPresentUnits(ETABSv1.eUnits.kN_mm_C)
            Dim N As Integer, Names() As String = Nothing
            S.GroupDef.GetNameList(N, Names)
            For Each G In Names.Take(N).Where(Function(x) x <> "All")
                Dim NO As Integer, OT() As Integer = Nothing, ON_() As String = Nothing
                If S.GroupDef.GetAssignments(G, NO, OT, ON_) <> 0 OrElse NO = 0 Then Continue For
                If Enumerable.Range(0, NO).Any(Function(k) OT(k) <> 2) Then Continue For
                Dim Vertical As Boolean = True, Zmin As Double = Double.MaxValue
                Dim StoryNames As New List(Of String)
                For k = 0 To NO - 1
                    Dim P1 As String = Nothing, P2 As String = Nothing
                    S.FrameObj.GetPoints(ON_(k), P1, P2)
                    Dim x1, y1, z1, x2, y2, z2 As Double
                    S.PointObj.GetCoordCartesian(P1, x1, y1, z1)
                    S.PointObj.GetCoordCartesian(P2, x2, y2, z2)
                    If Math.Abs(x1 - x2) > 1 OrElse Math.Abs(y1 - y2) > 1 Then Vertical = False : Exit For
                    Zmin = Math.Min(Zmin, Math.Min(z1, z2))
                    Dim Lab As String = Nothing, St As String = Nothing
                    If S.FrameObj.GetLabelFromName(ON_(k), Lab, St) = 0 AndAlso Not StoryNames.Contains(St) Then StoryNames.Add(St)
                Next
                If Not Vertical Then Continue For
                L.Add(New ColumnGroupInfo_ With {.Name = G, .Columns = NO, .Zmin = Zmin, .Stories = String.Join(", ", StoryNames.Take(6)) & If(StoryNames.Count > 6, " ...", "")})
            Next
            L = L.OrderBy(Function(c) c.Zmin).ThenBy(Function(c) c.Name).ToList()
            Message = L.Count & " column groups read"
        Catch ex As Exception
            Message = "Column groups could not be read: " & ex.Message
        Finally
            Try
                If O IsNot Nothing Then O.ApplicationExit(False)
            Catch
            End Try
            Try
                Directory.Delete(Dir, True)
            Catch
            End Try
        End Try
        Return L
    End Function
End Class
