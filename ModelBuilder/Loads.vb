Imports System.Globalization
Imports System.Text.RegularExpressions

'Step 9.2: load patterns, floor and cladding loads, snow, wind (ASCE 7-22 Chapter 27 forces computed here), mass source,
'modal case, response spectrum function and cases (ASCE 7-22 Chapter 11 and 12).
'ETABS 22.6 cannot create automatic ASCE 7-22 wind / seismic patterns through the API (see ASAMA9_ONERI.md, Section 12).
Partial Public Class EtabsBuilder
    Public Const PAT_DEAD As String = "Dead"            'frame self weight (default pattern of the blank model)
    Public Const PAT_SDL As String = "SDL"              'slab self weight + superimposed dead + cladding
    Public Const PAT_LIVE As String = "Live"            'default pattern of the blank model
    Public Const PAT_ROOFLIVE As String = "RoofLive"
    Public Const PAT_SNOW As String = "Snow"
    Public Const PAT_PART As String = "PartMass"        'partition weight, used only in the mass source (ASCE 7 12.7.2: at least 10 psf)
    Public Const PAT_WX As String = "WX"
    Public Const PAT_WY As String = "WY"
    Public Const FUNC_RS As String = "ASCE722"
    Public Const CASE_MODAL As String = "Modal"
    Public Const CASE_RSX As String = "RSX"
    Public Const CASE_RSY As String = "RSY"
    Private Const GRAVITY As Double = 9.80665

    Private Report As New System.Text.StringBuilder

    Private Sub Rep(ByVal Text As String)
        Report.AppendLine(Text)
        Msg(Text)
    End Sub

    Private Shared Function F(ByVal x As Double, Optional ByVal Fmt As String = "0.###") As String
        Return x.ToString(Fmt, CultureInfo.InvariantCulture)
    End Function

    '_______________________________________________________________________________________________
    'Read-modify-write of a database table. An import deletes the items that are missing from the imported table, so the
    'existing rows are read first; rows with the same value in KeyField are replaced, the others are kept.
    Private Sub UpsertTable(ByVal Key As String, ByVal KeyField As String, ByVal NewRows As List(Of Dictionary(Of String, String)))
        Dim V, N As Integer, Fs() As String = Nothing, D() As String = Nothing
        Chk(SapModel.DatabaseTables.GetTableForEditingArray(Key, "", V, Fs, N, D), "DatabaseTables.GetTableForEditingArray " & Key)
        Dim Rows As New List(Of Dictionary(Of String, String))
        Dim Cols As New List(Of String)
        If Fs IsNot Nothing Then
            Cols.AddRange(Fs)
            For i = 0 To N - 1
                Dim Row As New Dictionary(Of String, String)
                For j = 0 To Fs.Length - 1 : Row(Fs(j)) = D(i * Fs.Length + j) : Next
                Rows.Add(Row)
            Next
        End If
        For Each nr In NewRows
            Rows.RemoveAll(Function(r) r.ContainsKey(KeyField) AndAlso nr.ContainsKey(KeyField) AndAlso r(KeyField) = nr(KeyField))
            Rows.Add(nr)
            For Each k In nr.Keys : If Not Cols.Contains(k) Then Cols.Add(k)
            Next
        Next
        PutTable(Key, V, Cols, Rows)
    End Sub

    'Writes the rows of a table and applies the import; a rejected record or an error stops the build with the import log
    Private Sub PutTable(ByVal Key As String, ByVal Version As Integer, ByVal Cols As List(Of String), ByVal Rows As List(Of Dictionary(Of String, String)))
        Dim Out(Rows.Count * Cols.Count - 1) As String
        For i = 0 To Rows.Count - 1
            For j = 0 To Cols.Count - 1 : Out(i * Cols.Count + j) = If(Rows(i).ContainsKey(Cols(j)), Rows(i)(Cols(j)), "") : Next
        Next
        Chk(SapModel.DatabaseTables.SetTableForEditingArray(Key, Version, Cols.ToArray(), Rows.Count, Out), "DatabaseTables.SetTableForEditingArray " & Key)
        Dim NF, NE, NW, NI As Integer, Log As String = Nothing
        Dim Ret As Integer = SapModel.DatabaseTables.ApplyEditedTables(True, NF, NE, NW, NI, Log)
        Dim Partial1 As Boolean = False
        For Each m As Match In Regex.Matches(If(Log, ""), "(\d+) of (\d+) records successfully read")
            If CInt(m.Groups(1).Value) < CInt(m.Groups(2).Value) Then Partial1 = True
        Next
        If Ret <> 0 OrElse NF + NE > 0 OrElse Partial1 Then
            Dim Detail = String.Join(Environment.NewLine, (If(Log, "")).Split({vbLf(0)}).SkipWhile(Function(l) Not l.Contains("DETAILED LOG")).Select(Function(l) l.TrimEnd()).Where(Function(l) l.Length > 0).Take(30))
            Throw New BuilderException("table import " & Key & " failed (code " & Ret & ", errors " & (NF + NE) & "):" & Environment.NewLine & Detail)
        End If
        If NW > 0 Then Msg("Warning: table import " & Key & ": " & NW & " warnings")
    End Sub

    'Sets fields of the (single) row of a preferences table
    Private Sub UpdatePreferences(ByVal Key As String, ByVal Values As Dictionary(Of String, String))
        Dim V, N As Integer, Fs() As String = Nothing, D() As String = Nothing
        Chk(SapModel.DatabaseTables.GetTableForEditingArray(Key, "", V, Fs, N, D), "DatabaseTables.GetTableForEditingArray " & Key)
        If N < 1 OrElse Fs Is Nothing Then Throw New BuilderException("table " & Key & " has no row")
        Dim R1 As New Dictionary(Of String, String)
        For j = 0 To Fs.Length - 1 : R1(Fs(j)) = D(j) : Next
        For Each kv In Values
            If Not R1.ContainsKey(kv.Key) Then Throw New BuilderException("table " & Key & " has no field " & kv.Key)
            R1(kv.Key) = kv.Value
        Next
        PutTable(Key, V, Fs.ToList(), New List(Of Dictionary(Of String, String)) From {R1})
    End Sub

    Private Shared Function RowOf(ByVal ParamArray KeyValues() As String) As Dictionary(Of String, String)
        Dim D As New Dictionary(Of String, String)
        For i = 0 To KeyValues.Length - 2 Step 2 : D(KeyValues(i)) = KeyValues(i + 1) : Next
        Return D
    End Function

    '_______________________________________________________________________________________________
    Private Sub DefineLoads(ByVal Plan As BuildPlan_)
        Dim P As BuildParams_ = Plan.P
        Dim N As Integer = P.Stories
        Dim Pats = New List(Of Tuple(Of String, ETABSv1.eLoadPatternType)) From {
            Tuple.Create(PAT_SDL, ETABSv1.eLoadPatternType.SuperDead), Tuple.Create(PAT_ROOFLIVE, ETABSv1.eLoadPatternType.RoofLive),
            Tuple.Create(PAT_PART, ETABSv1.eLoadPatternType.Other), Tuple.Create(PAT_WX, ETABSv1.eLoadPatternType.Wind), Tuple.Create(PAT_WY, ETABSv1.eLoadPatternType.Wind)}
        If P.SnowGround > 0 Then Pats.Add(Tuple.Create(PAT_SNOW, ETABSv1.eLoadPatternType.Snow))
        For Each t In Pats : Chk(SapModel.LoadPatterns.Add(t.Item1, t.Item2, 0, True), "LoadPatterns.Add " & t.Item1) : Next

        'floor loads: two-way distribution to the beams (45-degree rule, as ETABS "uniform to frame"), kPa
        Dim FloorSdl As Double = P.SlabWeight + P.SuperDead, RoofSdl As Double = P.SlabWeight + P.RoofSuperDead
        Dim LiveFloor As Double = P.LiveLoad + P.Partition
        Dim SnowPf As Double = 0.7 * P.SnowExposure * P.SnowThermal * P.SnowImportance * P.SnowGround
        Dim BeamAt As New Dictionary(Of String, PlanBeam_)
        For Each Bm In Plan.Beams
            BeamAt(BeamKey(Bm.Story, Bm.Direction, If(Bm.Direction = "X", Bm.Y1, Bm.X1), If(Bm.Direction = "X", Bm.X1, Bm.Y1))) = Bm
        Next
        For Each C In Plan.Cells
            Dim Roof As Boolean = (C.Story = N)
            CellLoad(C, BeamAt, PAT_SDL, If(Roof, RoofSdl, FloorSdl))
            If Roof Then
                CellLoad(C, BeamAt, PAT_ROOFLIVE, P.RoofLive)
                If P.SnowGround > 0 Then CellLoad(C, BeamAt, PAT_SNOW, SnowPf)
            Else
                CellLoad(C, BeamAt, PAT_LIVE, LiveFloor)
                CellLoad(C, BeamAt, PAT_PART, 0.48)
            End If
        Next
        'cladding on the perimeter beams, kN/m
        If P.Cladding > 0 Then
            For Each Bm In Plan.Beams.Where(Function(b) b.Kind = "Edge")
                Chk(SapModel.FrameObj.SetLoadDistributed(FrameNames(Bm), PAT_SDL, 1, 10, 0, 1, P.Cladding, P.Cladding, "Global", True, False), "FrameObj.SetLoadDistributed (cladding)")
            Next
        End If
        Dim Area As Double = Plan.XLines.Last() * Plan.YLines.Last()
        Rep("Gravity loads: slab " & F(P.SlabWeight) & " + superimposed dead " & F(P.SuperDead) & " kPa (roof " & F(P.RoofSuperDead) & "), cladding " & F(P.Cladding) & " kN/m, live " & F(P.LiveLoad) & " + partitions " & F(P.Partition) & " kPa, roof live " & F(P.RoofLive) & " kPa" &
            If(P.SnowGround > 0, ", snow pf " & F(SnowPf) & " kPa (pg " & F(P.SnowGround) & ")", ", no snow"))
        Rep("Floor area " & F(Area, "0.#") & " m2 per story (the slab is not modeled; floor loads act on the beams, rigid diaphragm D1)")

        DefineWind(Plan)
        DefineMass(Plan, SnowPf)
        DefineSpectrum(Plan)
    End Sub

    Private Shared Function BeamKey(ByVal Story As Integer, ByVal Dir As String, ByVal Fixed As Double, ByVal Start As Double) As String
        Return Story & Dir & Math.Round(Fixed, 3).ToString(CultureInfo.InvariantCulture) & "|" & Math.Round(Start, 3).ToString(CultureInfo.InvariantCulture)
    End Function

    'Uniform load w [kPa] of a rectangular cell on its four beams: triangular load on the short edges, trapezoidal on the long edges
    '(sum = w a b). Loads are added to the pattern (Replace = False).
    Private Sub CellLoad(ByVal C As PlanCell_, ByVal BeamAt As Dictionary(Of String, PlanBeam_), ByVal Pattern As String, ByVal W As Double)
        If W = 0 Then Return
        Dim A As Double = C.X2 - C.X1, B As Double = C.Y2 - C.Y1, S As Double = Math.Min(A, B), Peak As Double = W * S / 2
        'edges parallel to x (length A) at y1 and y2; edges parallel to y (length B) at x1 and x2
        For Each y In {C.Y1, C.Y2}
            EdgeLoad(BeamAt(BeamKey(C.Story, "X", y, C.X1)), Pattern, Peak, S / 2 / A)
        Next
        For Each x In {C.X1, C.X2}
            EdgeLoad(BeamAt(BeamKey(C.Story, "Y", x, C.Y1)), Pattern, Peak, S / 2 / B)
        Next
    End Sub

    'Load of peak value p [kN/m] over a beam, rising linearly over the relative length r from each end (r = 0.5: triangle)
    Private Sub EdgeLoad(ByVal Bm As PlanBeam_, ByVal Pattern As String, ByVal Peak As Double, ByVal R As Double)
        Dim Name As String = FrameNames(Bm)
        Dim Seg = Sub(D1 As Double, D2 As Double, V1 As Double, V2 As Double)
                      Chk(SapModel.FrameObj.SetLoadDistributed(Name, Pattern, 1, 10, D1, D2, V1, V2, "Global", True, False), "FrameObj.SetLoadDistributed " & Pattern)
                  End Sub
        If R >= 0.4999 Then
            Seg(0, 0.5, 0, Peak) : Seg(0.5, 1, Peak, 0)
        Else
            Seg(0, R, 0, Peak) : Seg(R, 1 - R, Peak, Peak) : Seg(1 - R, 1, Peak, 0)
        End If
    End Sub

    '_______________________________________________________________________________________________
    'Wind loads: ASCE 7-22 Chapter 27 Directional Procedure, enclosed building, forces on the face joints of every story.
    '  qz = 0.613 Kz Kzt Ke V^2 [N/m2] (V in m/s), Kz = 2.01 (z / zg)^(2 / alpha) (z >= 4.57 m, else z = 4.57 m; Table 26.10-1)
    '  windward face: qz G Cp (Cp = 0.8) at the story level, leeward face: qh G Cp (Cp = -0.5 for L/B <= 1, -0.3 for 2, -0.2 for >= 4; Table 27.3-1)
    '  the sum of both is at least 0.77 kPa (16 psf, 27.1.5)
    'The load of a story acts on its joints in proportion to the tributary width, so it has no torsion. Ke = 1.
    Public WindBaseShearX As Double, WindBaseShearY As Double

    Private Function Kz(ByVal Z As Double, ByVal Exposure As String) As Double
        Dim Alpha As Double = If(Exposure = "B", 7.5, If(Exposure = "C", 9.5, 11.5))
        Dim Zg As Double = If(Exposure = "B", 365.76, If(Exposure = "C", 274.32, 213.36))
        Return 2.01 * Math.Pow(Math.Max(Z, 4.572) / Zg, 2 / Alpha)
    End Function

    Private Sub DefineWind(ByVal Plan As BuildPlan_)
        Dim P As BuildParams_ = Plan.P
        Dim N As Integer = P.Stories, H As Double = Plan.Height
        Dim NP As Integer, PN() As String = Nothing, PX() As Double = Nothing, PY() As Double = Nothing, PZ() As Double = Nothing
        Chk(SapModel.PointObj.GetAllPoints(NP, PN, PX, PY, PZ), "PointObj.GetAllPoints")
        Dim Lx As Double = Plan.XLines.Last(), Ly As Double = Plan.YLines.Last()
        Dim QzOf = Function(Z As Double) 0.613 * Kz(Z, P.WindExposure) * P.WindKzt * P.WindSpeed * P.WindSpeed / 1000.0
        Dim Qh As Double = QzOf(H)
        Dim Lines As New List(Of String)
        For Each Axis In {"X", "Y"}
            Dim Par As Double = If(Axis = "X", Lx, Ly), Perp As Double = If(Axis = "X", Ly, Lx), Pattern As String = If(Axis = "X", PAT_WX, PAT_WY)
            Dim LB As Double = Par / Perp
            Dim CpL As Double = If(LB <= 1, -0.5, If(LB <= 2, -0.5 + (LB - 1) * 0.2, If(LB <= 4, -0.3 + (LB - 2) * 0.05, -0.2)))
            Dim Total As Double = 0     'VB does not reset a Dim inside a loop body
            Dim Lines2 As New List(Of Double)(If(Axis = "X", Plan.YLines, Plan.XLines))       'coordinates across the wind direction
            For k = 1 To N
                Dim Z As Double = Plan.Levels(k)
                Dim Trib As Double = (Z - Plan.Levels(k - 1)) / 2 + If(k < N, (Plan.Levels(k + 1) - Z) / 2, 0)
                Dim Pw As Double = QzOf(Z) * P.WindGust * 0.8, Pl As Double = Qh * P.WindGust * Math.Abs(CpL)
                Dim Scale As Double = Math.Max(1.0, 0.77 / (Pw + Pl))
                For i = 0 To NP - 1
                    If Math.Abs(PZ(i) - Z) > 0.0001 Then Continue For
                    Dim Along As Double = If(Axis = "X", PX(i), PY(i)), Across As Double = If(Axis = "X", PY(i), PX(i))
                    Dim OnWind As Boolean = Math.Abs(Along) < 0.0001, OnLee As Boolean = Math.Abs(Along - Par) < 0.0001
                    If Not OnWind AndAlso Not OnLee Then Continue For
                    Dim j As Integer = Lines2.FindIndex(Function(c) Math.Abs(c - Across) < 0.0001)
                    Dim Width As Double = (If(j < Lines2.Count - 1, Lines2(j + 1), Lines2(j)) - If(j > 0, Lines2(j - 1), Lines2(j))) / 2
                    Dim Force As Double = If(OnWind, Pw, Pl) * Scale * Width * Trib
                    Dim Val() As Double = {If(Axis = "X", Force, 0), If(Axis = "Y", Force, 0), 0, 0, 0, 0}
                    Chk(SapModel.PointObj.SetLoadForce(PN(i), Pattern, Val, False, "Global"), "PointObj.SetLoadForce " & Pattern)
                    Total += Force
                Next
                Lines.Add("  " & Pattern & " story " & k & " z " & F(Z, "0.0") & " m: windward " & F(Pw * Scale, "0.000") & " kPa, leeward " & F(Pl * Scale, "0.000") & " kPa")
            Next
            If Axis = "X" Then WindBaseShearX = Total Else WindBaseShearY = Total
            Rep("Wind " & Axis & ": V " & F(P.WindSpeed) & " m/s, exposure " & P.WindExposure & ", Kzt " & F(P.WindKzt) & ", G " & F(P.WindGust) & ", L/B " & F(LB, "0.00") & " (leeward Cp " & F(CpL, "0.00") & "), qh " & F(Qh, "0.000") & " kPa, base shear " & F(Total, "0") & " kN")
        Next
        Report.AppendLine("Wind pressures (without the 16 psf minimum scaling where it does not apply):")
        For Each l In Lines : Report.AppendLine(l) : Next
    End Sub

    '_______________________________________________________________________________________________
    'Mass source: element self mass + SDL + partitions (+ 20 % of the snow load when pf > 1.44 kPa, ASCE 7 12.7.2); modal case
    Private NumModes As Integer

    Private Sub DefineMass(ByVal Plan As BuildPlan_, ByVal SnowPf As Double)
        Dim Pats As New List(Of String) From {PAT_SDL, PAT_PART}, Fac As New List(Of Double) From {1.0, 1.0}
        If Plan.P.SnowGround > 0 AndAlso SnowPf > 1.44 Then Pats.Add(PAT_SNOW) : Fac.Add(0.2)
        Chk(SapModel.PropMaterial.SetMassSource_1(True, False, True, Pats.Count, Pats.ToArray(), Fac.ToArray()), "PropMaterial.SetMassSource_1")
        'start with Modes (at least one per story); the check analysis raises the number when the participation is below 90 %
        'at most three per story (the dynamic degrees of freedom of the diaphragms) and 60: every analysis of the optimization solves them
        NumModes = Math.Min(Math.Min(60, 3 * Plan.P.Stories), Math.Max(Plan.P.Modes, Plan.P.Stories))
        Chk(SapModel.LoadCases.ModalEigen.SetNumberModes(CASE_MODAL, NumModes, 1), "LoadCases.ModalEigen.SetNumberModes")
        Rep("Mass source: self mass + " & String.Join(" + ", Pats.Select(Function(p, i) If(Fac(i) = 1.0, p, F(Fac(i)) & " " & p))) & "; modal case " & CASE_MODAL & " with " & NumModes & " modes")
    End Sub

    '_______________________________________________________________________________________________
    'ASCE 7-22 design response spectrum and the response spectrum cases (scale factor Ie g / R; CQC, SRSS directions, accidental eccentricity)
    Private Sub DefineSpectrum(ByVal Plan As BuildPlan_)
        Dim P As BuildParams_ = Plan.P
        UpsertTable("Functions - Response Spectrum - ASCE7-22", "Name", New List(Of Dictionary(Of String, String)) From {
            RowOf("Name", FUNC_RS, "Direction", "Horizontal", "Type", "Design", "SDS", F(P.SDS, "0.####"), "SD1", F(P.SD1, "0.####"), "SMS", F(1.5 * P.SDS, "0.####"), "SM1", F(1.5 * P.SD1, "0.####"),
                "TL", F(P.TL), "SiteClass", P.SiteClass, "DampRatio", "0.05")})
        Dim SF As Double = P.Ie * GRAVITY / P.EffR()
        Dim Cases As New List(Of Dictionary(Of String, String))
        For Each t In {Tuple.Create(CASE_RSX, "U1"), Tuple.Create(CASE_RSY, "U2")}
            Cases.Add(RowOf("Name", t.Item1, "LoadName", t.Item2, "Function", FUNC_RS, "TransAccSF", F(SF, "0.#####"), "Angle", "0", "ModalCase", CASE_MODAL, "ModalCombo", "CQC",
                          "RigidResp", "No", "DirCombo", "SRSS", "EccenRatio", F(P.EccentricityRatio), "DesignType", "Program Determined"))
        Next
        UpsertTable("Load Case Definitions - Response Spectrum", "Name", Cases)
        Rep("Response spectrum: ASCE 7-22 design spectrum SDS " & F(P.SDS) & " g, SD1 " & F(P.SD1) & " g, TL " & F(P.TL) & " s, site class " & P.SiteClass & "; cases " & CASE_RSX & ", " & CASE_RSY &
            " (scale factor Ie g / R = " & F(SF, "0.0000") & " m/s2, R " & F(P.EffR()) & ", Ie " & F(P.Ie) & "; CQC, SRSS, eccentricity " & F(P.EccentricityRatio) & ")")
    End Sub
End Class
