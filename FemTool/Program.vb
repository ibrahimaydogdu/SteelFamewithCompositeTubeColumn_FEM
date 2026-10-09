Imports FemCore
Imports System.Collections.Generic
Imports System.Diagnostics
Imports System.Globalization
Imports System.IO
Imports System.Linq
Imports ETABSv1

'Development tool of the FEM version:
'  FemTool.exe selftest                       analytic checks of the element (no ETABS)
'  FemTool.exe static <model.EDB> [cases]     reads the model with ETABS, solves the static linear cases with FemCore and compares the
'                                             results with the ETABS analysis (displacements, reactions, member end forces)
Public Module Program
    Private Sap As cSapModel
    Private Etabs As cOAPI
    Private Pid As Integer

    Sub Main(ByVal args() As String)
        Try
            If args.Length = 0 Then
                Console.WriteLine("usage: FemTool selftest | static <model.EDB> [case1,case2,...]")
                Return
            End If
            If args(0) = "selftest" Then
                SelfTest()
            ElseIf args(0) = "tables" Then
                DumpTables(args(1), args(2))
            ElseIf args(0) = "info" Then
                InfoPoints(args(1))
            ElseIf args(0) = "static" Then
                StaticCompare(args(1), If(args.Length > 2, args(2), ""))
            End If
        Catch ex As Exception
            Console.WriteLine("EX " & ex.ToString())
        End Try
    End Sub

    '_______________________________________________________________________________________________
    Private Function Check(ByVal Name As String, ByVal Value As Double, ByVal Expected As Double, ByVal Tol As Double) As Boolean
        Dim e As Double = Math.Abs(Value - Expected) / Math.Max(Math.Abs(Expected), 1.0E-12)
        Console.WriteLine("  " & Name.PadRight(48) & Value.ToString("0.000000E+00") & "  expected " & Expected.ToString("0.000000E+00") & "  " & If(e <= Tol, "ok", "FAIL (" & e.ToString("0.0E+00") & ")"))
        Return e <= Tol
    End Function

    'Cantilever and simply supported beam against the closed-form Timoshenko solutions
    Private Sub SelfTest()
        Dim Sec As New FemSection With {.Name = "T", .E = 2.0E+8, .G = 7.7E+7, .A = 0.01, .As2 = 0.004, .As3 = 0.006, .J = 1.0E-5, .I22 = 2.0E-5, .I33 = 8.0E-5, .UnitWeight = 77}
        Dim L As Double = 4.0, P As Double = 10.0, w As Double = 5.0
        'Model 1: cantilever along X, tip loads in Y (bends about axis 3) and Z (about axis 2)
        Dim Mo As New FemModel
        Mo.Nodes.Add(New FemNode With {.Name = "1", .X = 0, .Y = 0, .Z = 0}) : Mo.Nodes.Add(New FemNode With {.Name = "2", .X = L, .Y = 0, .Z = 0})
        For d = 0 To 5 : Mo.Nodes(0).Restraint(d) = True : Next
        Dim E As New FemElement With {.Name = "E", .NI = 0, .NJ = 1, .Section = Sec, .Length = L}
        E.Axes(0, 0) = 1 : E.Axes(1, 1) = 1 : E.Axes(2, 2) = 1
        Mo.Elements.Add(E)
        Dim Pt As New FemPattern With {.Name = "TIP"}
        Dim nl As New FemNodalLoad With {.Node = 1} : nl.F(1) = P : nl.F(2) = P : nl.F(0) = P
        Pt.NodalLoads.Add(nl)
        Dim Ud As New FemPattern With {.Name = "UDL"}
        Ud.DistLoads.Add(New FemDistLoad With {.Element = 0, .Dir = 2, .IsGlobal = False, .D1 = 0, .D2 = L, .V1 = w, .V2 = w})
        Ud.DistLoads.Add(New FemDistLoad With {.Element = 0, .Dir = 3, .IsGlobal = False, .D1 = 0, .D2 = L, .V1 = w, .V2 = w})
        Mo.Patterns.Add(Pt) : Mo.Patterns.Add(Ud)
        Dim R = New FemStatic(Mo).Solve()
        Dim ok As Boolean = True
        Dim EI3 As Double = Sec.E * Sec.I33, EI2 As Double = Sec.E * Sec.I22
        ok = ok And Check("cantilever tip uy = PL3/3EI + PL/GAs2", R.U(0)(7), P * L ^ 3 / (3 * EI3) + P * L / (Sec.G * Sec.As2), 1.0E-9)
        ok = ok And Check("cantilever tip uz = PL3/3EI + PL/GAs3", R.U(0)(8), P * L ^ 3 / (3 * EI2) + P * L / (Sec.G * Sec.As3), 1.0E-9)
        ok = ok And Check("cantilever tip ux = PL/EA", R.U(0)(6), P * L / (Sec.E * Sec.A), 1.0E-9)
        ok = ok And Check("cantilever tip rz = PL2/2EI", R.U(0)(11), P * L ^ 2 / (2 * EI3), 1.0E-9)
        ok = ok And Check("cantilever tip ry = -PL2/2EI (about axis 2)", R.U(0)(10), -P * L ^ 2 / (2 * EI2), 1.0E-9)
        ok = ok And Check("reaction Fy", R.Reaction(0)(1), -P, 1.0E-9)
        ok = ok And Check("reaction Mz = -PL", R.Reaction(0)(5), -P * L, 1.0E-9)
        ok = ok And Check("udl tip uy = wL4/8EI + wL2/2GAs", R.U(1)(7), w * L ^ 4 / (8 * EI3) + w * L * L / (2 * Sec.G * Sec.As2), 1.0E-9)
        ok = ok And Check("udl reaction Fy = -wL", R.Reaction(1)(1), -w * L, 1.0E-9)
        ok = ok And Check("udl reaction Mz = -wL2/2", R.Reaction(1)(5), -w * L * L / 2, 1.0E-9)
        'Model 2: beam with both ends pinned in bending (releases M2, M3) between two fixed nodes: UDL w in Y: no moment, tip deflection by the free rotation
        Dim M2 As New FemModel
        M2.Nodes.Add(New FemNode With {.Name = "1", .X = 0, .Y = 0, .Z = 0}) : M2.Nodes.Add(New FemNode With {.Name = "2", .X = L, .Y = 0, .Z = 0})
        For d = 0 To 2 : M2.Nodes(0).Restraint(d) = True : M2.Nodes(1).Restraint(1) = True : M2.Nodes(1).Restraint(2) = True : Next
        M2.Nodes(0).Restraint(3) = True : M2.Nodes(0).Restraint(4) = True : M2.Nodes(0).Restraint(5) = True : M2.Nodes(1).Restraint(3) = True
        Dim E2 As New FemElement With {.Name = "E", .NI = 0, .NJ = 1, .Section = Sec, .Length = L}
        E2.Axes(0, 0) = 1 : E2.Axes(1, 1) = 1 : E2.Axes(2, 2) = 1
        M2.Elements.Add(E2)
        Dim U2 As New FemPattern With {.Name = "UDL"}
        U2.DistLoads.Add(New FemDistLoad With {.Element = 0, .Dir = 2, .IsGlobal = False, .D1 = 0, .D2 = L, .V1 = w, .V2 = w})
        M2.Patterns.Add(U2)
        'simply supported by restraints (rotations about Z and Y free at node 2, fixed at node 1 only for torsion): node 1 rotations free too
        M2.Nodes(0).Restraint(4) = False : M2.Nodes(0).Restraint(5) = False
        Dim R2 = New FemStatic(M2).Solve()
        ok = ok And Check("simply supported UDL: end moment 0", Math.Abs(R2.EndForce(0)(0)(5)) + 1, 1.0, 1.0E-9)
        ok = ok And Check("simply supported UDL: rotation rz at node 1 = wL3/24EI", R2.U(0)(5), w * L ^ 3 / (24 * EI3), 1.0E-9)
        ok = ok And Check("simply supported UDL: reaction Fy = wL/2", R2.Reaction(0)(1), -w * L / 2, 1.0E-9)
        'Model 3: released beam with a point load at mid-span: reaction wL/2 and the fixed-end forces
        Console.WriteLine(If(ok, "SELFTEST OK", "SELFTEST FAILED"))
    End Sub

    '_______________________________________________________________________________________________
    Private Sub DumpTables(ByVal File As String, ByVal Keys As String)
        StartEtabs()
        Try
            Sap.File.OpenFile(File)
            If Keys.StartsWith("?") Then
                Dim nT As Integer, tk() As String = Nothing, tn() As String = Nothing, im() As Integer = Nothing, ie() As Boolean = Nothing
                Sap.DatabaseTables.GetAllTables(nT, tk, tn, im, ie)
                For i = 0 To nT - 1
                    If tn(i).IndexOf(Keys.Substring(1), StringComparison.OrdinalIgnoreCase) >= 0 Then Console.WriteLine("  table: " & tn(i) & " (" & tk(i) & ")")
                Next
                Return
            End If
            For Each key In Keys.Split("|"c)
                Dim V, N As Integer, Fs() As String = Nothing, D() As String = Nothing
                Dim r = Sap.DatabaseTables.GetTableForEditingArray(key, "", V, Fs, N, D)
                Console.WriteLine("-- " & key & " ret " & r & " rows " & N)
                If Fs Is Nothing Then Continue For
                For i = 0 To Math.Min(N, 3) - 1
                    Console.WriteLine("   " & String.Join(" | ", Enumerable.Range(0, Fs.Length).Select(Function(j) Fs(j) & "=" & D(i * Fs.Length + j))))
                Next
            Next
        Finally
            StopEtabs()
        End Try
    End Sub

    Private Sub InfoPoints(ByVal File As String)
        StartEtabs()
        Try
            Sap.File.OpenFile(File)
            Sap.SetPresentUnits(eUnits.kN_m_C)
            Dim N As Integer, Names() As String = Nothing
            Sap.PointObj.GetNameList(N, Names)
            For Each nm In Names.Skip(17).Take(40)
                Dim x, y, z As Double, cs As String = "Global"
                Sap.PointObj.GetCoordCartesian(nm, x, y, z, cs)
                Dim Opt As eDiaphragmOption = eDiaphragmOption.Disconnect, DN As String = ""
                Dim r = Sap.PointObj.GetDiaphragm(nm, Opt, DN)
                Console.WriteLine(nm.PadRight(5) & " x=" & x.ToString("0.00") & " y=" & y.ToString("0.00") & " z=" & z.ToString("0.00").PadLeft(7) & " opt=" & Opt.ToString() & " (" & CInt(Opt) & ") name='" & DN & "' ret " & r)
            Next
            Dim na As Integer, an() As String = Nothing
            Sap.AreaObj.GetNameList(na, an)
            Console.WriteLine("areas " & na)
        Finally
            StopEtabs()
        End Try
    End Sub

    Private Sub StartEtabs()
        Dim H As cHelper = New Helper
        Dim Before = Process.GetProcessesByName("ETABS").Select(Function(x) x.Id).ToList()
        Etabs = H.CreateObject("C:\Program Files\Computers and Structures\ETABS 22\ETABS.exe")
        Etabs.ApplicationStart() : Etabs.Hide()
        Pid = Process.GetProcessesByName("ETABS").Select(Function(x) x.Id).FirstOrDefault(Function(i) Not Before.Contains(i))
        Sap = Etabs.SapModel
    End Sub

    Private Sub StopEtabs()
        Try : Etabs.ApplicationExit(False) : Catch : End Try
    End Sub

    Private Function Rel(ByVal v As Double, ByVal r As Double, ByVal scale As Double) As Double
        Return Math.Abs(v - r) / Math.Max(scale, 1.0E-9)
    End Function

    Private Sub StaticCompare(ByVal File As String, ByVal CaseList As String)
        StartEtabs()
        Try
            Console.WriteLine("open " & Sap.File.OpenFile(File))
            Sap.SetPresentUnits(eUnits.kN_m_C)
            'no P-Delta in the ETABS reference
            Dim PVer, PNR As Integer, PFs() As String = Nothing, PDt() As String = Nothing
            If Sap.DatabaseTables.GetTableForEditingArray("P-Delta Option Definition", "", PVer, PFs, PNR, PDt) = 0 AndAlso PNR > 0 Then
                Dim iM As Integer = Array.IndexOf(PFs, "AutoMethod")
                Console.WriteLine("preset P-Delta: " & If(iM >= 0, PDt(iM), "?"))
                If iM >= 0 AndAlso PDt(iM) <> "None" Then
                    PDt(iM) = "None"
                    Sap.DatabaseTables.SetTableForEditingArray("P-Delta Option Definition", PVer, PFs, PNR, PDt)
                    Dim pNF, pNE, pNW, pNI As Integer, Lg As String = Nothing
                    Console.WriteLine("preset P-Delta -> None: " & Sap.DatabaseTables.ApplyEditedTables(True, pNF, pNE, pNW, pNI, Lg) & " errors " & (pNF + pNE))
                End If
            End If
            'experiment switches for isolating ETABS behaviour: FEM_OFFSET = "none" (zero end offsets) or a rigid zone factor
            Dim off As String = Environment.GetEnvironmentVariable("FEM_OFFSET")
            If Not String.IsNullOrEmpty(off) Then
                Dim nFr0 As Integer, fr0() As String = Nothing
                Sap.FrameObj.GetNameList(nFr0, fr0)
                For Each f0 In fr0
                    Dim onlyEl As String = Environment.GetEnvironmentVariable("FEM_ONLY")
                    If Not String.IsNullOrEmpty(onlyEl) AndAlso Not onlyEl.Split(","c).Contains(f0) Then
                        Sap.FrameObj.SetEndLengthOffset(f0, False, 0, 0, 0)
                        Continue For
                    End If
                    If off = "none" Then
                        Sap.FrameObj.SetEndLengthOffset(f0, False, 0, 0, 0)
                    Else
                        'explicit offsets (the automatic lengths as numbers) with the given rigid zone factor
                        Dim au As Boolean, l1, l2, rz0 As Double
                        Sap.FrameObj.GetEndLengthOffset(f0, au, l1, l2, rz0)
                        Dim setRet As Integer = Sap.FrameObj.SetEndLengthOffset(f0, False, l1, l2, Double.Parse(off, Globalization.CultureInfo.InvariantCulture))
                        If f0 = fr0(0) Then
                            Dim au2 As Boolean, m1, m2, rz2 As Double
                            Sap.FrameObj.GetEndLengthOffset(f0, au2, m1, m2, rz2)
                            Console.WriteLine("  set ret " & setRet & ": element " & f0 & " before auto=" & au & " " & l1.ToString("0.0000") & " " & l2.ToString("0.0000") & " rz " & rz0 & "; after auto=" & au2 & " " & m1.ToString("0.0000") & " " & m2.ToString("0.0000") & " rz " & rz2)
                        End If
                    End If
                Next
                Console.WriteLine("end offsets experiment: " & off)
            End If
            'one linear static case per load pattern (FEM_<pattern>) so that the single patterns can be compared
            Dim nPat As Integer, patNames() As String = Nothing
            Sap.LoadPatterns.GetNameList(nPat, patNames)
            For Each pn In patNames
                If pn.StartsWith("~") Then Continue For
                Dim cn As String = "FEM_" & pn
                Sap.LoadCases.StaticLinear.SetCase(cn)
                Sap.LoadCases.StaticLinear.SetLoads(cn, 1, New String() {"Load"}, New String() {pn}, New Double() {1.0})
            Next
            Dim rd As New EtabsReader(Sap)
            Dim sw = Stopwatch.StartNew()
            Dim M = rd.Read()
            If Environment.GetEnvironmentVariable("FEM_DAM") = "1" Then M.AxialFactor = 0.8 : M.FlexuralFactor = 0.8 : Console.WriteLine("DAM stiffness reduction 0.8 (EA, EI) applied in the FEM")
            Console.WriteLine("model read in " & sw.Elapsed.TotalSeconds.ToString("0.0") & " s: nodes " & M.Nodes.Count & ", elements " & M.Elements.Count & ", sections " & M.Sections.Count & ", patterns " & M.Patterns.Count & ", static cases/combos " & M.Combos.Count & ", diaphragms " & M.DiaphragmCount)
            For Each n In M.Notes : Console.WriteLine("  NOTE " & n) : Next
            For Each sc In M.Sections
                Console.WriteLine("  section " & sc.Name & " mat " & sc.MaterialName & " E " & sc.E.ToString("0.0E+00") & " G " & sc.G.ToString("0.0E+00") & " A " & sc.A.ToString("0.0000E+00") & " As2 " & sc.As2.ToString("0.0000E+00") & " As3 " & sc.As3.ToString("0.0000E+00") & " J " & sc.J.ToString("0.0000E+00") & " I22 " & sc.I22.ToString("0.0000E+00") & " I33 " & sc.I33.ToString("0.0000E+00") & " w " & sc.UnitWeight.ToString("0.00"))
            Next
            Dim bad As Integer = 0
            For Each el In M.Elements
                Dim dev As Double = 0
                For i = 0 To 2 : For j = 0 To 2
                        Dim dotp As Double = 0
                        For c = 0 To 2 : dotp += el.Axes(i, c) * el.Axes(j, c) : Next
                        dev = Math.Max(dev, Math.Abs(dotp - If(i = j, 1, 0)))
                    Next : Next
                Dim nA = M.Nodes(el.NI), nB = M.Nodes(el.NJ)
                Dim d1(2) As Double : d1(0) = (nB.X - nA.X) / el.Length : d1(1) = (nB.Y - nA.Y) / el.Length : d1(2) = (nB.Z - nA.Z) / el.Length
                Dim dev1 As Double = Math.Abs(d1(0) - el.Axes(0, 0)) + Math.Abs(d1(1) - el.Axes(0, 1)) + Math.Abs(d1(2) - el.Axes(0, 2))
                If dev > 1.0E-6 OrElse dev1 > 1.0E-6 Then bad += 1 : If bad <= 3 Then Console.WriteLine("  axes problem element " & el.Name & " orthonormality " & dev.ToString("0.0E+00") & " axis1 vs member " & dev1.ToString("0.0E+00"))
            Next
            Console.WriteLine("  elements with axes problems: " & bad)
            Dim Wanted As List(Of String) = If(CaseList = "", M.Combos.Where(Function(c) c.Name.StartsWith("FEM_")).Select(Function(c) c.Name).ToList(), CaseList.Split(","c).ToList())
            Wanted = Wanted.Where(Function(c) M.Combos.Any(Function(x) x.Name = c)).ToList()
            Console.WriteLine("cases compared: " & String.Join(", ", Wanted))

            If Environment.GetEnvironmentVariable("FEM_DESIGNFIRST") = "1" Then
                Console.WriteLine("design first: analysis " & Sap.Analyze.RunAnalysis() & ", design " & Sap.DesignSteel.StartDesign() & ", unlock " & Sap.SetModelIsLocked(False))
            End If
            Dim ds As String = Environment.GetEnvironmentVariable("FEM_ANALMETHOD")
            If Not String.IsNullOrEmpty(ds) Then
                Dim dv, dn As Integer, dfs() As String = Nothing, dd() As String = Nothing
                Dim keyP As String = "Steel Frame Design Preferences - AISC 360-22"
                If Sap.DatabaseTables.GetTableForEditingArray(keyP, "", dv, dfs, dn, dd) = 0 AndAlso dn > 0 Then
                    dd(Array.IndexOf(dfs, "AnalMethod")) = ds
                    Sap.DatabaseTables.SetTableForEditingArray(keyP, dv, dfs, dn, dd)
                    Dim dNF, dNE, dNW, dNI As Integer, dLg As String = Nothing
                    Console.WriteLine("AnalMethod -> " & ds & ": " & Sap.DatabaseTables.ApplyEditedTables(True, dNF, dNE, dNW, dNI, dLg) & " errors " & (dNF + dNE))
                End If
            End If
            Console.WriteLine("ETABS analysis: " & Sap.Analyze.RunAnalysis())
            sw.Restart()
            Dim Res = New FemStatic(M).Solve()
            Console.WriteLine("FEM solution " & sw.Elapsed.TotalMilliseconds.ToString("0") & " ms (including assembly), free DOF " & New FemStatic(M).NFree & If(Res.Failed, " FAILED: " & Res.Message, ""))
            If Res.Failed Then Return

            Sap.Results.Setup.DeselectAllCasesAndCombosForOutput()
            For Each c In Wanted : Sap.Results.Setup.SetCaseSelectedForOutput(c, True) : Next
            'ETABS displacements
            Dim nr As Integer, Obj() As String = Nothing, Elm() As String = Nothing, LC() As String = Nothing, StepT() As String = Nothing, SN() As Double = Nothing
            Dim U1() As Double = Nothing, U2() As Double = Nothing, U3() As Double = Nothing, R1() As Double = Nothing, R2() As Double = Nothing, R3() As Double = Nothing
            Console.WriteLine("JointDispl ret " & Sap.Results.JointDispl("All", eItemTypeElm.GroupElm, nr, Obj, Elm, LC, StepT, SN, U1, U2, U3, R1, R2, R3))
            Dim Idx As New Dictionary(Of String, Integer)
            For i = 0 To M.Patterns.Count - 1 : Idx(M.Patterns(i).Name) = i : Next
            Dim Dsp As New Dictionary(Of String, Double())
            For k = 0 To nr - 1
                Dim ni As Integer = M.NodeIndex(Obj(k))
                If ni < 0 Then Continue For
                Dim key As String = LC(k)
                If Not Dsp.ContainsKey(key) Then Dsp(key) = New Double(M.Nodes.Count * 6 - 1) {}
                Dim a() As Double = {U1(k), U2(k), U3(k), R1(k), R2(k), R3(k)}
                For d = 0 To 5 : Dsp(key)(ni * 6 + d) = a(d) : Next
            Next
            'ETABS reactions
            Dim Rea As New Dictionary(Of String, Double())
            Dim F1() As Double = Nothing, F2() As Double = Nothing, F3() As Double = Nothing, MM1() As Double = Nothing, MM2() As Double = Nothing, MM3() As Double = Nothing
            Console.WriteLine("JointReact ret " & Sap.Results.JointReact("All", eItemTypeElm.GroupElm, nr, Obj, Elm, LC, StepT, SN, F1, F2, F3, MM1, MM2, MM3))
            For k = 0 To nr - 1
                Dim ni As Integer = M.NodeIndex(Obj(k))
                If ni < 0 Then Continue For
                If Not Rea.ContainsKey(LC(k)) Then Rea(LC(k)) = New Double(M.Nodes.Count * 6 - 1) {}
                Dim a() As Double = {F1(k), F2(k), F3(k), MM1(k), MM2(k), MM3(k)}
                For d = 0 To 5 : Rea(LC(k))(ni * 6 + d) = a(d) : Next
            Next
            'ETABS member forces at the ends
            Dim nFr As Integer, FO() As String = Nothing, FSta() As Double = Nothing, FE() As String = Nothing, FES() As Double = Nothing, FLC() As String = Nothing, FST() As String = Nothing, FSN() As Double = Nothing
            Dim P() As Double = Nothing, V2() As Double = Nothing, V3() As Double = Nothing, Tor() As Double = Nothing, Mo2() As Double = Nothing, Mo3() As Double = Nothing
            Console.WriteLine("FrameForce ret " & Sap.Results.FrameForce("All", eItemTypeElm.GroupElm, nFr, FO, FSta, FE, FES, FLC, FST, FSN, P, V2, V3, Tor, Mo2, Mo3))
            Dim EIdx As New Dictionary(Of String, Integer)
            For i = 0 To M.Elements.Count - 1 : EIdx(M.Elements(i).Name) = i : Next

            Dim shown As New HashSet(Of String)
            For Each el In M.Elements
                Dim nA = M.Nodes(el.NI), nB = M.Nodes(el.NJ)
                Dim kind As String = If(Math.Abs(el.Axes(0, 2)) > 0.9, "col", If(Math.Abs(el.Axes(0, 0)) > 0.9, "beamX", "beamY")) & " " & el.Section.Name & " rel " & String.Join("", Enumerable.Range(0, 6).Select(Function(d) If(el.ReleaseI(d), "1", "0"))) & "/" & String.Join("", Enumerable.Range(0, 6).Select(Function(d) If(el.ReleaseJ(d), "1", "0")))
                If shown.Add(kind & Math.Round(el.Axes(1, 0), 2) & Math.Round(el.Axes(1, 1), 2) & Math.Round(el.Axes(1, 2), 2)) AndAlso shown.Count < 12 Then
                    Console.WriteLine("  element " & el.Name & " " & kind & " ax1 (" & el.Axes(0, 0).ToString("0.00") & "," & el.Axes(0, 1).ToString("0.00") & "," & el.Axes(0, 2).ToString("0.00") & ") ax2 (" & el.Axes(1, 0).ToString("0.00") & "," & el.Axes(1, 1).ToString("0.00") & "," & el.Axes(1, 2).ToString("0.00") & ") ax3 (" & el.Axes(2, 0).ToString("0.00") & "," & el.Axes(2, 1).ToString("0.00") & "," & el.Axes(2, 2).ToString("0.00") & ")")
                End If
            Next
            Dim modPrinted As Integer = 0
            For Each el In M.Elements
                If el.Modifier.Any(Function(x) x <> 1.0) OrElse el.Section.Modifier.Any(Function(x) x <> 1.0) Then
                    modPrinted += 1
                    If modPrinted <= 3 Then Console.WriteLine("  modifiers element " & el.Name & " obj [" & String.Join(",", el.Modifier) & "] section [" & String.Join(",", el.Section.Modifier) & "]")
                End If
            Next
            Console.WriteLine("  elements with modifiers: " & modPrinted)
            Dim Att(M.Nodes.Count - 1) As Boolean
            For Each el In M.Elements : Att(el.NI) = True : Att(el.NJ) = True : Next
            Console.WriteLine()
            Console.WriteLine("case".PadRight(34) & "disp max err(trans)  disp max err(rot)  react max err  force max err (sign +/-)")
            Dim WorstAll As Double = 0
            For Each c In Wanted
                Dim Combo = M.Combos.First(Function(x) x.Name = c)
                Dim nd As Integer = M.Nodes.Count * 6
                Dim U(nd - 1) As Double, Rx(nd - 1) As Double
                Dim EF(M.Elements.Count - 1)() As Double
                For e = 0 To M.Elements.Count - 1 : EF(e) = New Double(11) {} : Next
                For Each t In Combo.Terms
                    Dim p0 As Integer = Idx(t.Name)
                    For i = 0 To nd - 1 : U(i) += t.Factor * Res.U(p0)(i) : Rx(i) += t.Factor * Res.Reaction(p0)(i) : Next
                    For e = 0 To M.Elements.Count - 1 : For i = 0 To 11 : EF(e)(i) += t.Factor * Res.FaceForce(p0)(e)(i) : Next : Next
                Next
                If Not Dsp.ContainsKey(c) Then Console.WriteLine(c.PadRight(34) & "no ETABS result") : Continue For
                Dim scT As Double = 0, scR As Double = 0
                For i = 0 To nd - 1
                    If Not Att(i \ 6) Then Continue For
                    If (i Mod 6) < 3 Then scT = Math.Max(scT, Math.Abs(Dsp(c)(i))) Else scR = Math.Max(scR, Math.Abs(Dsp(c)(i)))
                Next
                Dim eT As Double = 0, eR As Double = 0
                For i = 0 To nd - 1
                    If Not Att(i \ 6) Then Continue For
                    If (i Mod 6) < 3 Then eT = Math.Max(eT, Rel(U(i), Dsp(c)(i), scT)) Else eR = Math.Max(eR, Rel(U(i), Dsp(c)(i), scR))
                Next
                Dim sRe As Double = 0, eRe As Double = 0
                For i = 0 To nd - 1 : sRe = Math.Max(sRe, Math.Abs(Rea(c)(i))) : Next
                For i = 0 To nd - 1 : eRe = Math.Max(eRe, Rel(Rx(i), Rea(c)(i), If((i Mod 6) < 3, sRe, sRe))) : Next
                'member forces: ETABS station 0 and the last station of each element
                Dim ePos As Double = 0, eNeg As Double = 0, sF As Double = 0
                Dim CompMax(5) As Double, CompErr(5) As Double, CompWorst(5) As String, SumPlus(5) As Double, SumMinus(5) As Double
                Dim Last As New Dictionary(Of String, Integer), First As New Dictionary(Of String, Integer)
                For k = 0 To nFr - 1
                    If FLC(k) <> c Then Continue For
                    If Not First.ContainsKey(FO(k)) OrElse FSta(k) < FSta(First(FO(k))) Then First(FO(k)) = k
                    If Not Last.ContainsKey(FO(k)) OrElse FSta(k) > FSta(Last(FO(k))) Then Last(FO(k)) = k
                Next
                For Each kv In First : sF = Math.Max(sF, Math.Abs(P(kv.Value))) : sF = Math.Max(sF, Math.Abs(V2(kv.Value))) : sF = Math.Max(sF, Math.Abs(Mo3(kv.Value))) : sF = Math.Max(sF, Math.Abs(Mo2(kv.Value))) : Next
                For Each kv In First
                    Dim e As Integer = EIdx(kv.Key), kI As Integer = kv.Value, kJ As Integer = Last(kv.Key)
                    Dim f = EF(e)
                    'candidate internal-force convention: I end = -f, J end = +f
                    Dim sI() As Double = {-f(0), -f(1), -f(2), -f(3), -f(4), -f(5)}
                    Dim sJ() As Double = {f(6), f(7), f(8), f(9), f(10), f(11)}
                    Dim eI() As Double = {P(kI), V2(kI), V3(kI), Tor(kI), Mo2(kI), Mo3(kI)}
                    Dim eJ() As Double = {P(kJ), V2(kJ), V3(kJ), Tor(kJ), Mo2(kJ), Mo3(kJ)}
                    For d = 0 To 5
                        CompMax(d) = Math.Max(CompMax(d), Math.Max(Math.Abs(eI(d)), Math.Abs(eJ(d))))
                        Dim de As Double = Math.Max(Math.Abs(sI(d) - eI(d)), Math.Abs(sJ(d) - eJ(d)))
                        Dim deN As Double = Math.Max(Math.Abs(sI(d) + eI(d)), Math.Abs(sJ(d) + eJ(d)))
                        SumPlus(d) = Math.Max(SumPlus(d), de) : SumMinus(d) = Math.Max(SumMinus(d), deN)
                        If de > CompErr(d) Then CompErr(d) = de : CompWorst(d) = kv.Key
                    Next
                    For d = 0 To 5
                        ePos = Math.Max(ePos, Rel(sI(d), eI(d), sF)) : ePos = Math.Max(ePos, Rel(sJ(d), eJ(d), sF))
                        eNeg = Math.Max(eNeg, Rel(-sI(d), eI(d), sF)) : eNeg = Math.Max(eNeg, Rel(-sJ(d), eJ(d), sF))
                    Next
                Next
                Console.WriteLine(c.PadRight(34) & (eT * 100).ToString("0.000").PadLeft(10) & "%" & (eR * 100).ToString("0.000").PadLeft(15) & "%" & (eRe * 100).ToString("0.000").PadLeft(12) & "%" & (ePos * 100).ToString("0.000").PadLeft(11) & "% /" & (eNeg * 100).ToString("0.000") & "%")
                Dim wantEl As String = Environment.GetEnvironmentVariable("FEM_ELEMS")
                If Not String.IsNullOrEmpty(wantEl) Then
                    For Each en In wantEl.Split(","c)
                        If Not First.ContainsKey(en) Then Continue For
                        Dim e As Integer = EIdx(en), kI As Integer = First(en), kJ As Integer = Last(en)
                        Dim f = EF(e)
                        Console.WriteLine("    element " & en & " (" & M.Elements(e).Group & ")")
                        Dim nm6() As String = {"P", "V2", "V3", "T", "M2", "M3"}
                        Dim eI() As Double = {P(kI), V2(kI), V3(kI), Tor(kI), Mo2(kI), Mo3(kI)}
                        Dim eJ() As Double = {P(kJ), V2(kJ), V3(kJ), Tor(kJ), Mo2(kJ), Mo3(kJ)}
                        For d = 0 To 5
                            Console.WriteLine("      " & nm6(d).PadRight(3) & " I: ETABS " & eI(d).ToString("0.0000").PadLeft(11) & " FEM(-f) " & (-f(d)).ToString("0.0000").PadLeft(11) & "    J: ETABS " & eJ(d).ToString("0.0000").PadLeft(11) & " FEM(f) " & f(6 + d).ToString("0.0000").PadLeft(11))
                        Next
                    Next
                End If
                Console.WriteLine("    max error with sign + / - per component: " & String.Join(" | ", Enumerable.Range(0, 6).Select(Function(d) {"P", "V2", "V3", "T", "M2", "M3"}(d) & " " & SumPlus(d).ToString("0.000") & " / " & SumMinus(d).ToString("0.000"))))
                Console.WriteLine("    member force components (max abs, max error, worst element): " & String.Join(" | ", Enumerable.Range(0, 6).Select(Function(d) {"P", "V2", "V3", "T", "M2", "M3"}(d) & " " & CompMax(d).ToString("0.00") & " " & CompErr(d).ToString("0.000") & " el " & CompWorst(d))))
                WorstAll = Math.Max(WorstAll, Math.Max(eT, eR))
            Next
            If Rea.ContainsKey("Dead") Then Console.WriteLine("self weight check (case Dead): ETABS total Fz " & Rea("Dead").Where(Function(v, i) (i Mod 6) = 2).Sum().ToString("0.00") & " kN")
            Dim wFull As Double = 0, wClear As Double = 0
            For Each el In M.Elements
                wFull += el.Section.WeightPerLength * el.Length
                wClear += el.Section.WeightPerLength * Math.Max(0, el.Length - el.OffsetI - el.OffsetJ)
            Next
            Dim wc As Double = 0, wcc As Double = 0, wb As Double = 0, wbc As Double = 0, nOff As Integer = 0
            For Each el In M.Elements
                Dim isCol As Boolean = Math.Abs(el.Axes(0, 2)) > 0.9
                Dim wf As Double = el.Section.WeightPerLength * el.Length, wcl As Double = el.Section.WeightPerLength * Math.Max(0, el.Length - el.OffsetI - el.OffsetJ)
                If isCol Then wc += wf : wcc += wcl Else wb += wf : wbc += wcl
                If el.Name = "31" OrElse el.Name = "81" OrElse el.Name = "73" OrElse el.Name = "74" Then Console.WriteLine("  offsets of element " & el.Name & ": I " & el.OffsetI.ToString("0.0000") & " J " & el.OffsetJ.ToString("0.0000") & " L " & el.Length.ToString("0.0000"))
                If nOff < 4 Then Console.WriteLine("  element " & el.Name & If(isCol, " column", " beam") & " L " & el.Length.ToString("0.0000") & " offsetI " & el.OffsetI.ToString("0.0000") & " offsetJ " & el.OffsetJ.ToString("0.0000") & " " & el.Section.Name) : nOff += 1
            Next
            Console.WriteLine("  columns: joint-to-joint " & wc.ToString("0.00") & ", clear " & wcc.ToString("0.00") & "; beams: joint-to-joint " & wb.ToString("0.00") & ", clear " & wbc.ToString("0.00"))
            Console.WriteLine("  weight joint-to-joint " & wFull.ToString("0.00") & " kN, clear length " & wClear.ToString("0.00") & " kN")
            Console.WriteLine("worst displacement error " & (WorstAll * 100).ToString("0.000") & "%")
            'a detailed look at the first case
            DetailForFirst(M, Res, Wanted, Dsp, Rea)
        Finally
            StopEtabs()
        End Try
    End Sub

    Private Sub DetailForFirst(ByVal M As FemModel, ByVal Res As StaticResults, ByVal Wanted As List(Of String), ByVal Dsp As Dictionary(Of String, Double()), ByVal Rea As Dictionary(Of String, Double()))
        If Wanted.Count = 0 Then Return
        Dim c As String = Wanted(0)
        Dim Combo = M.Combos.First(Function(x) x.Name = c)
        Dim nd As Integer = M.Nodes.Count * 6
        Dim U(nd - 1) As Double
        For Each t In Combo.Terms
            Dim p0 As Integer = M.PatternIndex(t.Name)
            For i = 0 To nd - 1 : U(i) += t.Factor * Res.U(p0)(i) : Next
        Next
        Console.WriteLine("translations (top 6 by ETABS magnitude), case " & c & ":")
        For Each d In Enumerable.Range(0, nd).Where(Function(i) (i Mod 6) < 3).OrderByDescending(Function(i) Math.Abs(Dsp(c)(i))).Take(6)
            Console.WriteLine("   node " & M.Nodes(d \ 6).Name & " dof " & (d Mod 6) & ": FEM " & U(d).ToString("0.000000E+00") & " ETABS " & Dsp(c)(d).ToString("0.000000E+00") & " ratio " & (Dsp(c)(d) / If(U(d) = 0, 1.0E-30, U(d))).ToString("0.0000"))
        Next
        Console.WriteLine("largest node differences, case " & c & ":")
        Dim Diffs = Enumerable.Range(0, nd).Where(Function(i) (i Mod 6) >= 3).Select(Function(i) New With {.I = i, .D = Math.Abs(U(i) - Dsp(c)(i))}).OrderByDescending(Function(x) x.D).Take(8)
        For Each d In Diffs
            Console.WriteLine("   node " & M.Nodes(d.I \ 6).Name & " dof " & (d.I Mod 6) & ": FEM " & U(d.I).ToString("0.000000E+00") & " ETABS " & Dsp(c)(d.I).ToString("0.000000E+00"))
            Dim nn As FemNode = M.Nodes(d.I \ 6)
            Dim att = M.Elements.Where(Function(el) M.Nodes(el.NI) Is nn OrElse M.Nodes(el.NJ) Is nn).Select(Function(el) el.Name & If(Math.Abs(el.Axes(0, 2)) > 0.9, "c", If(Math.Abs(el.Axes(0, 0)) > 0.9, "x", "y")) & If(el.ReleaseI.Any(Function(r) r) OrElse el.ReleaseJ.Any(Function(r) r), "r", ""))
            Console.WriteLine("        at (" & nn.X.ToString("0.0") & "," & nn.Y.ToString("0.0") & "," & nn.Z.ToString("0.0") & ") diaphragm " & nn.Diaphragm & " elements " & String.Join(" ", att))
        Next
    End Sub
End Module
