Imports FemCore
Imports System.Collections.Generic
Imports System.Globalization
Imports System.Linq
Imports ETABSv1

'Reads an ETABS model (open in SapModel) into a FemModel, in kN and m. Unsupported features (area objects, springs, cardinal point
'offsets, non-I sections, rigid zone factor > 0, unknown load direction codes, ...) are listed in FemModel.Notes; the caller
'decides whether the model may be solved.
Public Class EtabsReader
    Private ReadOnly Sap As cSapModel
    Private ReadOnly M As New FemModel
    Private ReadOnly CpStats As New Dictionary(Of String, Integer)
    Public Log As Action(Of String) = Sub(s) Console.WriteLine(s)

    Public Sub New(ByVal SapModel As cSapModel)
        Sap = SapModel
    End Sub

    Private Sub Note(ByVal s As String)
        If Not M.Notes.Contains(s) Then M.Notes.Add(s)
    End Sub

    Public Function Read() As FemModel
        Sap.SetPresentUnits(eUnits.kN_m_C)
        ReadPoints()
        ReadFrames()
        ReadPatterns()
        ReadCases()
        ReadMassAndSpectra()
        Return M
    End Function

    'Mass source (self mass, load patterns), response spectrum functions and cases, modal case
    Private Sub ReadMassAndSpectra()
        Dim V, N As Integer, Fs() As String = Nothing, D() As String = Nothing
        If Sap.DatabaseTables.GetTableForEditingArray("Mass Source Definition", "", V, Fs, N, D) = 0 AndAlso N > 0 Then
            Dim Col = Function(row As Integer, f As String) As String
                          Dim j As Integer = Array.IndexOf(Fs, f)
                          Return If(j >= 0, D(row * Fs.Length + j), "")
                      End Function
            M.MassSource.SelfMass = (Col(0, "SourceSelf") = "Yes")
            M.MassSource.IncludeVertical = (Col(0, "IncVertical") = "Yes")
            If Col(0, "SourceAdded") = "Yes" Then
                Dim V2, N2 As Integer, F2() As String = Nothing, D2() As String = Nothing
                If Sap.DatabaseTables.GetTableForEditingArray("Joint Assignments - Additional Mass", "", V2, F2, N2, D2) = 0 AndAlso N2 > 0 Then Note("additional joint masses (" & N2 & " rows)")
                If Sap.DatabaseTables.GetTableForEditingArray("Frame Assignments - Additional Mass", "", V2, F2, N2, D2) = 0 AndAlso N2 > 0 Then Note("additional frame masses (" & N2 & " rows)")
            End If
            If Col(0, "SourceLoads") = "Yes" Then
                For r = 0 To N - 1
                    Dim lp As String = Col(r, "LoadPattern")
                    If lp <> "" Then M.MassSource.Patterns.Add(New FemTerm With {.Name = lp, .Factor = Double.Parse(Col(r, "Multiplier"), CultureInfo.InvariantCulture)})
                Next
            End If
            If Col(0, "LumpMass") <> "Yes" Then Note("consistent mass (only lumped mass is modelled)")
        End If
        'response spectrum functions
        Dim nf As Integer, fn() As String = Nothing, ft() As Integer = Nothing
        Sap.Func.GetNameList(nf, fn, 0)
        For Each name In If(fn, New String() {})
            Dim nv As Integer, tt() As Double = Nothing, vv() As Double = Nothing
            If Sap.Func.GetValues(name, nv, tt, vv) = 0 AndAlso nv > 1 Then M.Spectra.Add(New FemSpectrum With {.Name = name, .T = tt, .Sa = vv})
        Next
        'response spectrum cases
        Dim NC As Integer, Names() As String = Nothing
        Sap.LoadCases.GetNameList(NC, Names)
        For Each nm In Names
            Dim ct As eLoadCaseType, st As Integer
            If Sap.LoadCases.GetTypeOAPI(nm, ct, st) <> 0 Then Continue For
            If ct = eLoadCaseType.ResponseSpectrum Then
                Dim nl As Integer, ln() As String = Nothing, fu() As String = Nothing, sf() As Double = Nothing, cs() As String = Nothing, an() As Double = Nothing
                If Sap.LoadCases.ResponseSpectrum.GetLoads(nm, nl, ln, fu, sf, cs, an) <> 0 Then Continue For
                If nl <> 1 Then Note("response spectrum case " & nm & " with " & nl & " directions") : Continue For
                Dim R As New FemRSCase With {.Name = nm, .Spectrum = fu(0), .ScaleFactor = sf(0)}
                R.Direction = If(ln(0) = "U1", 0, If(ln(0) = "U2", 1, 2))
                If an(0) <> 0 Then Note("response spectrum angle " & an(0) & " (" & nm & ")")
                Dim dmp As Double
                If Sap.LoadCases.ResponseSpectrum.GetDampConstant(nm, dmp) = 0 Then R.Damping = dmp
                Dim ecc As Double
                If Sap.LoadCases.ResponseSpectrum.GetEccentricity(nm, ecc) = 0 Then R.Eccentricity = ecc
                M.RSCases.Add(R)
            ElseIf ct = eLoadCaseType.Modal Then
                Dim mx, mn As Integer
                If Sap.LoadCases.ModalEigen.GetNumberModes(nm, mx, mn) = 0 Then M.ModalName = nm : M.ModalCount = mx
            End If
        Next
    End Sub

    Private Sub ReadPoints()
        Dim N As Integer, Names() As String = Nothing
        Sap.PointObj.GetNameList(N, Names)
        Dim DiaKeys As New Dictionary(Of String, Integer)
        For Each nm In Names
            Dim nd As New FemNode With {.Name = nm}
            Dim cs As String = "Global"
            Sap.PointObj.GetCoordCartesian(nm, nd.X, nd.Y, nd.Z, cs)
            Dim rs() As Boolean = Nothing
            If Sap.PointObj.GetRestraint(nm, rs) = 0 Then
                For d = 0 To 5 : nd.Restraint(d) = rs(d) : Next
            End If
            Dim K() As Double = Nothing
            If Sap.PointObj.GetSpring(nm, K) = 0 AndAlso K IsNot Nothing AndAlso K.Any(Function(x) x <> 0) Then Note("point springs (" & nm & ")")
            Dim Opt As eDiaphragmOption = eDiaphragmOption.Disconnect, DName As String = ""
            If Sap.PointObj.GetDiaphragm(nm, Opt, DName) = 0 AndAlso Opt = eDiaphragmOption.DefinedDiaphragm Then
                Dim key As String = DName & "@" & Math.Round(nd.Z, 3).ToString(CultureInfo.InvariantCulture)
                Dim idx As Integer
                If Not DiaKeys.TryGetValue(key, idx) Then idx = DiaKeys.Count : DiaKeys.Add(key, idx)
                nd.Diaphragm = idx
            ElseIf Opt = eDiaphragmOption.FromShellObject Then
                Note("diaphragm from shell objects (" & nm & ")")
            End If
            M.Nodes.Add(nd)
        Next
        M.DiaphragmCount = DiaKeys.Count
    End Sub

    Private Sub ReadFrames()
        Dim N As Integer, Names() As String = Nothing
        Sap.FrameObj.GetNameList(N, Names)
        Dim SecIdx As New Dictionary(Of String, FemSection)
        Dim Cardinal As New HashSet(Of Integer)
        For Each nm In Names
            Dim p1 As String = "", p2 As String = ""
            Sap.FrameObj.GetPoints(nm, p1, p2)
            Dim E As New FemElement With {.Name = nm, .NI = M.NodeIndex(p1), .NJ = M.NodeIndex(p2)}
            Dim a = M.Nodes(E.NI), b = M.Nodes(E.NJ)
            E.Length = Math.Sqrt((b.X - a.X) ^ 2 + (b.Y - a.Y) ^ 2 + (b.Z - a.Z) ^ 2)
            Dim pn As String = "", sa As String = ""
            Sap.FrameObj.GetSection(nm, pn, sa)
            If Not SecIdx.ContainsKey(pn) Then SecIdx.Add(pn, ReadSection(pn))
            E.Section = SecIdx(pn)
            Dim fm() As Double = Nothing
            If Sap.FrameObj.GetModifiers(nm, fm) = 0 AndAlso fm IsNot Nothing AndAlso fm.Length >= 8 Then
                For i = 0 To 7 : E.Modifier(i) = fm(i) : Next
            End If
            Dim ri() As Boolean = Nothing, rj() As Boolean = Nothing, si() As Double = Nothing, sj() As Double = Nothing
            If Sap.FrameObj.GetReleases(nm, ri, rj, si, sj) = 0 Then
                For d = 0 To 5 : E.ReleaseI(d) = ri(d) : E.ReleaseJ(d) = rj(d) : Next
                If si.Any(Function(x) x <> 0) OrElse sj.Any(Function(x) x <> 0) Then Note("partial fixity springs at releases (" & nm & ")")
            End If
            Dim Auto As Boolean, L1, L2, RZ As Double
            If Sap.FrameObj.GetEndLengthOffset(nm, Auto, L1, L2, RZ) = 0 Then
                E.OffsetI = L1 : E.OffsetJ = L2 : E.RigidZone = RZ
            End If
            Dim T() As Double = Nothing
            If Sap.FrameObj.GetTransformationMatrix(nm, T, True) = 0 AndAlso T IsNot Nothing AndAlso T.Length = 9 Then
                'the 9 values are a 3x3 matrix whose COLUMNS are the local axes (verified: axis 1 equals the member direction for every element)
                Dim dx As Double = (b.X - a.X) / E.Length, dy As Double = (b.Y - a.Y) / E.Length, dz As Double = (b.Z - a.Z) / E.Length
                For k = 0 To 2 : For c = 0 To 2 : E.Axes(k, c) = T(c * 3 + k) : Next : Next
                Dim devCol As Double = Math.Abs(E.Axes(0, 0) - dx) + Math.Abs(E.Axes(0, 1) - dy) + Math.Abs(E.Axes(0, 2) - dz)
                Dim devRow As Double = devCol
                If Math.Min(devRow, devCol) > 1.0E-5 Then Note("local axis 1 differs from the member direction (" & nm & ")")
            Else
                Note("local axes not available (" & nm & ")")
            End If
            Dim cp As Integer, mir, st As Boolean, o1(2) As Double, o2(2) As Double, cs2 As String = "Local"
            If Sap.FrameObj.GetInsertionPoint(nm, cp, mir, st, o1, o2, cs2) = 0 Then
                Cardinal.Add(cp)
                CpStats(cp & " mirror " & mir & " stiffTransform " & st) = (If(CpStats.ContainsKey(cp & " mirror " & mir & " stiffTransform " & st), CpStats(cp & " mirror " & mir & " stiffTransform " & st), 0)) + 1
                If o1 IsNot Nothing AndAlso (o1.Any(Function(x) x <> 0) OrElse o2.Any(Function(x) x <> 0)) Then Note("joint offsets (" & nm & ")")
                'cardinal point: the section reference point is at the joint; the centroid is offset (rigidly when the stiffness is transformed)
                If st Then
                    Select Case cp
                        Case 10, 5, 11 : 'centroid, middle centre
                        Case 8 : E.Ecc2 = -E.Section.Depth / 2          'top centre: centroid below the joint along local 2
                        Case 2 : E.Ecc2 = E.Section.Depth / 2           'bottom centre
                        Case Else : Note("cardinal point " & cp & " (" & nm & ")")
                    End Select
                End If
            End If
            Dim ng As Integer, gn() As String = Nothing
            If Sap.FrameObj.GetGroupAssign(nm, ng, gn) = 0 Then E.Group = String.Join("/", gn.Where(Function(g) g <> "All"))
            M.Elements.Add(E)
        Next
        M.Sections.AddRange(SecIdx.Values)
        Log("  cardinal points in the model: " & String.Join(", ", CpStats.Select(Function(kv) kv.Key & " x" & kv.Value)))
    End Sub

    Private Function ReadSection(ByVal Name As String) As FemSection
        Dim S As New FemSection With {.Name = Name}
        Dim fn As String = "", mat As String = "", t3, t2, tf, tw, t2b, tfb, fr As Double, col As Integer, notes As String = "", guid As String = ""
        If Sap.PropFrame.GetISection_1(Name, fn, mat, t3, t2, tf, tw, t2b, tfb, fr, col, notes, guid) <> 0 Then
            Note("section " & Name & " is not an I section")
            Return S
        End If
        S.MaterialName = mat : S.Depth = t3 : S.Width = t2 : S.FlangeThickness = tf : S.WebThickness = tw
        Dim A, As2, As3, J, I22, I33, S22, S33, Z22, Z33, R22, R33 As Double
        Sap.PropFrame.GetSectProps(Name, A, As2, As3, J, I22, I33, S22, S33, Z22, Z33, R22, R33)
        S.A = A : S.As2 = As2 : S.As3 = As3 : S.J = J : S.I22 = I22 : S.I33 = I33
        Dim E, U, Al, G, Tmp As Double
        Sap.PropMaterial.GetMPIsotropic(mat, E, U, Al, G, Tmp)
        S.E = E : S.G = G
        Dim W, Ms As Double
        Sap.PropMaterial.GetWeightAndMass(mat, W, Ms, Tmp)
        S.UnitWeight = W : S.UnitMass = Ms
        Dim secMod() As Double = Nothing
        If Sap.PropFrame.GetModifiers(Name, secMod) = 0 AndAlso secMod IsNot Nothing AndAlso secMod.Length >= 8 Then
            For i = 0 To 7 : S.Modifier(i) = secMod(i) : Next
        End If
        Dim Fy, Fu, EFy, EFu, SH, SA, SM, SR, FS As Double, SST, SHT As Integer
        If Sap.PropMaterial.GetOSteel_1(mat, Fy, Fu, EFy, EFu, SST, SHT, SH, SM, SR, FS, Tmp) = 0 Then S.Fy = Fy : S.Fu = Fu
        Return S
    End Function

    Private Sub ReadPatterns()
        Dim N As Integer, Names() As String = Nothing
        Sap.LoadPatterns.GetNameList(N, Names)
        For Each nm In Names
            Dim P As New FemPattern With {.Name = nm}
            Dim sw As Double
            Sap.LoadPatterns.GetSelfWTMultiplier(nm, sw)
            P.SelfWeightMultiplier = sw
            M.Patterns.Add(P)
        Next
        'joint loads
        Dim n1 As Integer, pn() As String = Nothing, lp() As String = Nothing, st() As Integer = Nothing, cs() As String = Nothing
        Dim f1() As Double = Nothing, f2() As Double = Nothing, f3() As Double = Nothing, m1() As Double = Nothing, m2() As Double = Nothing, m3() As Double = Nothing
        If Sap.PointObj.GetLoadForce("All", n1, pn, lp, st, cs, f1, f2, f3, m1, m2, m3, eItemType.Group) = 0 Then
            For k = 0 To n1 - 1
                Dim pi As Integer = M.PatternIndex(lp(k)), ni As Integer = M.NodeIndex(pn(k))
                If pi < 0 OrElse ni < 0 Then Continue For
                If cs(k) <> "Global" Then Note("joint load in coordinate system " & cs(k)) : Continue For
                Dim L As New FemNodalLoad With {.Node = ni}
                L.F(0) = f1(k) : L.F(1) = f2(k) : L.F(2) = f3(k) : L.F(3) = m1(k) : L.F(4) = m2(k) : L.F(5) = m3(k)
                M.Patterns(pi).NodalLoads.Add(L)
            Next
        End If
        'frame distributed loads
        Dim n2 As Integer, fnm() As String = Nothing, lp2() As String = Nothing, ty() As Integer = Nothing, cs2() As String = Nothing, dr() As Integer = Nothing
        Dim rd1() As Double = Nothing, rd2() As Double = Nothing, d1() As Double = Nothing, d2() As Double = Nothing, v1() As Double = Nothing, v2() As Double = Nothing
        If Sap.FrameObj.GetLoadDistributed("All", n2, fnm, lp2, ty, cs2, dr, rd1, rd2, d1, d2, v1, v2, eItemType.Group) = 0 Then
            For k = 0 To n2 - 1
                Dim pi As Integer = M.PatternIndex(lp2(k)), ei As Integer = ElementIndex(fnm(k))
                If pi < 0 OrElse ei < 0 Then Continue For
                If ty(k) <> 1 Then Note("distributed moment loads on frames") : Continue For
                Dim code As Integer = dr(k)
                If Not (code >= 1 AndAlso code <= 6 OrElse code = 10) Then Note("frame load direction code " & code) : Continue For
                Dim D As New FemDistLoad With {.Element = ei, .Dir = code, .IsGlobal = (cs2(k) <> "Local"), .Relative = False}
                Dim L As Double = M.Elements(ei).Length
                D.D1 = rd1(k) * L : D.D2 = rd2(k) * L : D.V1 = v1(k) : D.V2 = v2(k)
                M.Patterns(pi).DistLoads.Add(D)
            Next
        End If
        'frame point loads
        Dim n3 As Integer, fn3() As String = Nothing, lp3() As String = Nothing, ty3() As Integer = Nothing, cs3() As String = Nothing, dr3() As Integer = Nothing
        Dim rel() As Double = Nothing, dist() As Double = Nothing, val() As Double = Nothing
        If Sap.FrameObj.GetLoadPoint("All", n3, fn3, lp3, ty3, cs3, dr3, rel, dist, val, eItemType.Group) = 0 Then
            For k = 0 To n3 - 1
                Dim pi As Integer = M.PatternIndex(lp3(k)), ei As Integer = ElementIndex(fn3(k))
                If pi < 0 OrElse ei < 0 Then Continue For
                Dim code As Integer = dr3(k)
                If Not (code >= 1 AndAlso code <= 6 OrElse code = 10) Then Note("frame point load direction code " & code) : Continue For
                Dim L As Double = M.Elements(ei).Length
                M.Patterns(pi).PointLoads.Add(New FemPointLoadOnElement With {.Element = ei, .Dir = code, .IsGlobal = (cs3(k) <> "Local"), .Relative = False,
                    .Dist = rel(k) * L, .Value = val(k), .IsMoment = (ty3(k) = 2)})
            Next
        End If
        'area / shell loads
        Dim na As Integer, an() As String = Nothing
        Sap.AreaObj.GetNameList(na, an)
        If na > 0 Then Note(na & " area objects")
    End Sub

    Private ElemIndexMap As Dictionary(Of String, Integer)
    Private Function ElementIndex(ByVal Name As String) As Integer
        If ElemIndexMap Is Nothing Then
            ElemIndexMap = New Dictionary(Of String, Integer)
            For i = 0 To M.Elements.Count - 1 : ElemIndexMap(M.Elements(i).Name) = i : Next
        End If
        Dim k As Integer
        Return If(ElemIndexMap.TryGetValue(Name, k), k, -1)
    End Function

    'Static linear cases and combinations flattened to load patterns; other case types are noted
    Private Sub ReadCases()
        Dim Flat As New Dictionary(Of String, List(Of FemTerm))
        Dim N As Integer, Names() As String = Nothing
        Sap.LoadCases.GetNameList(N, Names)
        For Each nm In Names
            Dim ct As eLoadCaseType, st As Integer
            If Sap.LoadCases.GetTypeOAPI(nm, ct, st) <> 0 Then Continue For
            Dim nl As Integer, lt() As String = Nothing, ln() As String = Nothing, sf() As Double = Nothing
            If ct = eLoadCaseType.NonlinearStatic Then
                If Sap.LoadCases.StaticNonlinear.GetLoads(nm, nl, lt, ln, sf) <> 0 Then Continue For
                M.NonlinearCases.Add(nm)
            ElseIf ct = eLoadCaseType.LinearStatic Then
                If Sap.LoadCases.StaticLinear.GetLoads(nm, nl, lt, ln, sf) <> 0 Then Continue For
            Else
                If nm.StartsWith("~") Then Continue For
                Note("case " & nm & " is " & ct.ToString() & " (not static)")
                Continue For
            End If
            Dim Terms As New List(Of FemTerm)
            For k = 0 To nl - 1
                If lt(k) <> "Load" Then Note("static case " & nm & " has load type " & lt(k)) : Continue For
                Terms.Add(New FemTerm With {.Name = ln(k), .Factor = sf(k)})
            Next
            Flat(nm) = Terms
        Next
        Dim NC As Integer, Cn() As String = Nothing
        Sap.RespCombo.GetNameList(NC, Cn)
        Dim Pending As New List(Of String)(Cn)
        Dim Guard As Integer = 0
        While Pending.Count > 0 AndAlso Guard < 10
            Guard += 1
            For Each cn1 In Pending.ToList()
                Dim ni As Integer, tp() As eCNameType = Nothing, cname() As String = Nothing, sf() As Double = Nothing
                If Sap.RespCombo.GetCaseList(cn1, ni, tp, cname, sf) <> 0 Then Pending.Remove(cn1) : Continue For
                Dim ok As Boolean = True
                For k = 0 To ni - 1 : If Not Flat.ContainsKey(cname(k)) Then ok = False
                Next
                If Not ok Then Continue For
                Dim Terms As New List(Of FemTerm)
                For k = 0 To ni - 1
                    For Each t In Flat(cname(k)) : Terms.Add(New FemTerm With {.Name = t.Name, .Factor = t.Factor * sf(k)}) : Next
                Next
                Flat(cn1) = Terms : Pending.Remove(cn1)
            Next
        End While
        For Each cn1 In Pending : Note("combination " & cn1 & " refers to non-static cases") : Next
        For Each kv In Flat
            Dim C As New FemCombo With {.Name = kv.Key}
            For Each t In kv.Value
                Dim ex = C.Terms.FirstOrDefault(Function(x) x.Name = t.Name)
                If ex Is Nothing Then C.Terms.Add(New FemTerm With {.Name = t.Name, .Factor = t.Factor}) Else ex.Factor += t.Factor
            Next
            M.Combos.Add(C)
        Next
    End Sub
End Class
