Imports System.Globalization
Imports System.IO

'Step 9.3b: analysis of the finished model (in a temporary folder), modal check, equivalent lateral force base shear
'(ASCE 7-22 12.8, numerical) and scaling of the response spectrum cases (12.9.1.4), model check report.
Partial Public Class EtabsBuilder
    Public Result As New AuditResult_

    Public Class AuditResult_
        Public Weight As Double                 'seismic weight [kN]
        Public PeriodX, PeriodY As Double       's (modal, directions of the largest participation)
        Public ElfX, ElfY As Double             'ELF base shear [kN]
        Public SpectrumX, SpectrumY As Double   'response spectrum base shear before scaling [kN]
        Public ScaleX, ScaleY As Double         'applied scale factors (>= 1)
        Public Participation As Double          'cumulative modal mass participation, min of x and y
        Public Modes As Integer
        Public WindX, WindY As Double           'wind base shear [kN]
        Public Cd, Ie As Double
    End Class

    '_______________________________________________________________________________________________
    'Pre-sizing: all beams and all columns take the section of the same relative index f (0 .. 1) of their pools (sorted by area).
    'The smallest f whose modal periods are not above Cu Ta (the period limit of the ELF procedure) is found by bisection on modal
    'analyses. The response spectrum scale factor of the check then belongs to a building of realistic stiffness: with placeholder
    'sections the building is much more flexible, its spectrum shear is far below the ELF shear and the scale factor is too large for
    'the stiffer designs of the optimization.
    Private Sub AssignPoolIndex(ByVal Plan As BuildPlan_, ByVal F As Double)
        If SapModel.GetModelIsLocked() Then Chk(SapModel.SetModelIsLocked(False), "SetModelIsLocked")      'a section cannot be changed while the results are kept
        Dim Bs As String = BeamPool(CInt(Math.Round(F * (BeamPool.Count - 1)))).Label
        Dim Cs As String = ColumnPool(CInt(Math.Round(F * (ColumnPool.Count - 1)))).Label
        For Each C In Plan.Columns : Chk(SapModel.FrameObj.SetSection(FrameNames(C), Cs), "FrameObj.SetSection " & Cs) : Next
        For Each B In Plan.Beams : Chk(SapModel.FrameObj.SetSection(FrameNames(B), Bs), "FrameObj.SetSection " & Bs) : Next
    End Sub

    'Largest period of the two directions (the modes with the largest participation)
    Private Function ControlPeriod() As Double
        If SapModel.GetModelIsLocked() Then Chk(SapModel.SetModelIsLocked(False), "SetModelIsLocked")
        Chk(SapModel.Analyze.SetRunCaseFlag("", False, True), "Analyze.SetRunCaseFlag")
        Chk(SapModel.Analyze.SetRunCaseFlag(CASE_MODAL, True), "Analyze.SetRunCaseFlag " & CASE_MODAL)
        Analyze("pre-sizing")
        Chk(SapModel.Results.Setup.DeselectAllCasesAndCombosForOutput(), "Results.Setup.DeselectAllCasesAndCombosForOutput")
        Chk(SapModel.Results.Setup.SetCaseSelectedForOutput(CASE_MODAL), "Results.Setup.SetCaseSelectedForOutput")
        Dim NM As Integer, LC() As String = Nothing, ST() As String = Nothing, SN() As Double = Nothing, Per() As Double = Nothing
        Dim Ux() As Double = Nothing, Uy() As Double = Nothing, Uz() As Double = Nothing, SUx() As Double = Nothing, SUy() As Double = Nothing, SUz() As Double = Nothing
        Dim Rx() As Double = Nothing, Ry() As Double = Nothing, Rz() As Double = Nothing, SRx() As Double = Nothing, SRy() As Double = Nothing, SRz() As Double = Nothing
        Chk(SapModel.Results.ModalParticipatingMassRatios(NM, LC, ST, SN, Per, Ux, Uy, Uz, SUx, SUy, SUz, Rx, Ry, Rz, SRx, SRy, SRz), "Results.ModalParticipatingMassRatios")
        If NM < 3 Then Throw New BuilderException("modal analysis gave " & NM & " modes")
        Dim iX As Integer = Enumerable.Range(0, NM).OrderByDescending(Function(i) Ux(i)).First(), iY As Integer = Enumerable.Range(0, NM).OrderByDescending(Function(i) Uy(i)).First()
        Return Math.Max(Per(iX), Per(iY))
    End Function

    Private Sub PreSizeMembers(ByVal Plan As BuildPlan_)
        Dim P As BuildParams_ = Plan.P
        If Not P.PreSize Then
            Rep("Pre-sizing is off: the members keep the median sections of their pools")
            Return
        End If
        Dim Limit As Double = Cu(P.SD1) * 0.0724 * Math.Pow(Plan.Height, 0.8)
        Dim Lo As Double = 0, Hi As Double = 1, Best As Double = 1, TBest As Double
        Dim Steps As Integer = 0
        AssignPoolIndex(Plan, Hi) : TBest = ControlPeriod() : Steps += 1
        If TBest > Limit Then
            Rep("Pre-sizing: even the heaviest sections give T " & F(TBest, "0.000") & " s > Cu Ta " & F(Limit, "0.000") & " s; the heaviest sections are kept")
        ElseIf TBest >= 0.9 * Limit Then
            Rep("Pre-sizing: the heaviest sections give T " & F(TBest, "0.000") & " s, close to Cu Ta " & F(Limit, "0.000") & " s; they are used for the check")
        Else
            Dim Tmid As Double
            For i = 1 To 6
                Dim Mid As Double = (Lo + Hi) / 2
                AssignPoolIndex(Plan, Mid) : Tmid = ControlPeriod() : Steps += 1
                If Tmid <= Limit Then
                    Hi = Mid : Best = Mid : TBest = Tmid
                Else
                    Lo = Mid
                End If
            Next
            AssignPoolIndex(Plan, Best)
        End If
        Dim Bs As String = BeamPool(CInt(Math.Round(Best * (BeamPool.Count - 1)))).Label, Cs As String = ColumnPool(CInt(Math.Round(Best * (ColumnPool.Count - 1)))).Label
        Chk(SapModel.Analyze.SetRunCaseFlag("", True, True), "Analyze.SetRunCaseFlag")
        Rep("Pre-sizing (" & Steps & " modal analyses): relative pool index " & F(Best, "0.000") & ", beams " & Bs & ", columns " & Cs & ", T " & F(TBest, "0.000") & " s (limit Cu Ta = " & F(Limit, "0.000") & " s)")
    End Sub

    'After the check the members get the neutral median sections of the pools again (the optimization program starts from its own initial design)
    Private Sub RestoreMedianSections(ByVal Plan As BuildPlan_)
        If Not Plan.P.PreSize Then Return
        AssignPoolIndex(Plan, 0.5)
        Rep("Members set back to the median sections of the pools (beams " & BeamPool(CInt(Math.Round(0.5 * (BeamPool.Count - 1)))).Label & ", columns " & ColumnPool(CInt(Math.Round(0.5 * (ColumnPool.Count - 1)))).Label & "); they are placeholders, the optimization program sizes the members")
    End Sub

    'Cu of ASCE 7 Table 12.8-1 by SD1
    Private Shared Function Cu(ByVal Sd1 As Double) As Double
        Dim X() As Double = {0.1, 0.15, 0.2, 0.3, 0.4}, Y() As Double = {1.7, 1.6, 1.5, 1.4, 1.4}
        If Sd1 <= X(0) Then Return Y(0)
        If Sd1 >= X(4) Then Return Y(4)
        For i = 1 To 4
            If Sd1 <= X(i) Then Return Y(i - 1) + (Y(i) - Y(i - 1)) * (Sd1 - X(i - 1)) / (X(i) - X(i - 1))
        Next
        Return 1.4
    End Function

    'ELF base shear V = Cs W (ASCE 7-22 12.8.1 and 12.8.2); steel moment frame Ta = 0.0724 hn^0.8 (hn in m)
    Private Function ElfShear(ByVal P As BuildParams_, ByVal Height As Double, ByVal W As Double, ByVal Tmodal As Double, ByRef Cs As Double, ByRef T As Double) As Double
        Dim RI As Double = P.EffR() / P.Ie
        Dim Ta As Double = 0.0724 * Math.Pow(Height, 0.8)
        T = Math.Min(Tmodal, Cu(P.SD1) * Ta)
        Cs = P.SDS / RI
        Dim Upper As Double = If(T <= P.TL, P.SD1 / (T * RI), P.SD1 * P.TL / (T * T * RI))
        Cs = Math.Min(Cs, Upper)
        Cs = Math.Max(Cs, Math.Max(0.044 * P.SDS * P.Ie, 0.01))
        If P.S1 >= 0.6 Then Cs = Math.Max(Cs, 0.5 * P.S1 / RI)
        Return Cs * W
    End Function

    Private Class BaseShear_
        Public Fx, Fy, Fz As Double
    End Class

    Private Function ReadBase(ByVal Cases() As String) As Dictionary(Of String, BaseShear_)
        Chk(SapModel.Results.Setup.DeselectAllCasesAndCombosForOutput(), "Results.Setup.DeselectAllCasesAndCombosForOutput")
        For Each c In Cases : Chk(SapModel.Results.Setup.SetCaseSelectedForOutput(c), "Results.Setup.SetCaseSelectedForOutput " & c) : Next
        Dim NR As Integer, LC() As String = Nothing, ST() As String = Nothing, SN() As Double = Nothing, Fx() As Double = Nothing, Fy() As Double = Nothing, Fz() As Double = Nothing
        Dim Mx() As Double = Nothing, My() As Double = Nothing, Mz() As Double = Nothing, Gx, Gy, Gz As Double
        Chk(SapModel.Results.BaseReact(NR, LC, ST, SN, Fx, Fy, Fz, Mx, My, Mz, Gx, Gy, Gz), "Results.BaseReact")
        Dim D As New Dictionary(Of String, BaseShear_)
        For i = 0 To NR - 1
            D(LC(i)) = New BaseShear_ With {.Fx = Fx(i), .Fy = Fy(i), .Fz = Fz(i)}
        Next
        Return D
    End Function

    Private Sub Analyze(ByVal What As String)
        Chk(SapModel.Analyze.RunAnalysis(), "Analyze.RunAnalysis (" & What & ")")
    End Sub

    '_______________________________________________________________________________________________
    'Called with the finished model saved as TempFile (analysis files stay in that temporary folder)
    Private Sub AuditModel(ByVal Plan As BuildPlan_)
        Dim P As BuildParams_ = Plan.P
        Dim R As AuditResult_ = Result
        R.WindX = WindBaseShearX : R.WindY = WindBaseShearY : R.Cd = P.EffCd() : R.Ie = P.Ie
        Dim Started As Date = Date.Now
        Dim MaxModes As Integer = Math.Min(60, 3 * P.Stories)
        Dim Participation As Double
        Do
            Analyze("check")
            Chk(SapModel.Results.Setup.DeselectAllCasesAndCombosForOutput(), "Results.Setup.DeselectAllCasesAndCombosForOutput")
            Chk(SapModel.Results.Setup.SetCaseSelectedForOutput(CASE_MODAL), "Results.Setup.SetCaseSelectedForOutput")
            Dim NMod As Integer, LCm() As String = Nothing, STm() As String = Nothing, SNm() As Double = Nothing, Pm() As Double = Nothing
            Dim Ux1() As Double = Nothing, Uy1() As Double = Nothing, Uz1() As Double = Nothing, SUx1() As Double = Nothing, SUy1() As Double = Nothing, SUz1() As Double = Nothing
            Dim Rx1() As Double = Nothing, Ry1() As Double = Nothing, Rz1() As Double = Nothing, SRx1() As Double = Nothing, SRy1() As Double = Nothing, SRz1() As Double = Nothing
            Chk(SapModel.Results.ModalParticipatingMassRatios(NMod, LCm, STm, SNm, Pm, Ux1, Uy1, Uz1, SUx1, SUy1, SUz1, Rx1, Ry1, Rz1, SRx1, SRy1, SRz1), "Results.ModalParticipatingMassRatios")
            Participation = If(NMod > 0, Math.Min(SUx1(NMod - 1), SUy1(NMod - 1)), 0)
            If Participation >= 0.9 OrElse NMod < NumModes OrElse NumModes >= MaxModes Then Exit Do
            Dim Bigger As Integer = Math.Min(MaxModes, NumModes * 2)
            Rep("Mass participation " & F(Participation * 100, "0.0") & " % with " & NumModes & " modes: raised to " & Bigger & " modes")
            NumModes = Bigger
            If SapModel.GetModelIsLocked() Then Chk(SapModel.SetModelIsLocked(False), "SetModelIsLocked")
            Chk(SapModel.LoadCases.ModalEigen.SetNumberModes(CASE_MODAL, NumModes, 1), "LoadCases.ModalEigen.SetNumberModes")
        Loop
        Dim Cases As New List(Of String) From {PAT_DEAD, PAT_SDL, PAT_PART, CASE_RSX, CASE_RSY}
        If P.SnowGround > 0 Then Cases.Add(PAT_SNOW)
        Dim B = ReadBase(Cases.ToArray())
        R.Weight = B(PAT_DEAD).Fz + B(PAT_SDL).Fz + B(PAT_PART).Fz
        If P.SnowGround > 0 AndAlso 0.7 * P.SnowExposure * P.SnowThermal * P.SnowImportance * P.SnowGround > 1.44 Then R.Weight += 0.2 * B(PAT_SNOW).Fz

        'modal results: the periods of the largest participation in x and y, cumulative participation
        Dim NM As Integer, LC() As String = Nothing, ST() As String = Nothing, SN() As Double = Nothing, Per() As Double = Nothing
        Dim Ux() As Double = Nothing, Uy() As Double = Nothing, Uz() As Double = Nothing, SUx() As Double = Nothing, SUy() As Double = Nothing, SUz() As Double = Nothing
        Dim Rx() As Double = Nothing, Ry() As Double = Nothing, Rz() As Double = Nothing, SRx() As Double = Nothing, SRy() As Double = Nothing, SRz() As Double = Nothing
        Chk(SapModel.Results.ModalParticipatingMassRatios(NM, LC, ST, SN, Per, Ux, Uy, Uz, SUx, SUy, SUz, Rx, Ry, Rz, SRx, SRy, SRz), "Results.ModalParticipatingMassRatios")
        If NM < 3 Then Throw New BuilderException("modal analysis gave " & NM & " modes")
        Dim iX As Integer = Enumerable.Range(0, NM).OrderByDescending(Function(i) Ux(i)).First(), iY As Integer = Enumerable.Range(0, NM).OrderByDescending(Function(i) Uy(i)).First()
        R.PeriodX = Per(iX) : R.PeriodY = Per(iY) : R.Modes = NM
        R.Participation = Math.Min(SUx(NM - 1), SUy(NM - 1))
        Rep("Modal analysis: " & NM & " modes, T1 " & F(Per(0), "0.000") & " s, T2 " & F(Per(1), "0.000") & " s, T3 " & F(Per(2), "0.000") & " s; x mode " & (iX + 1) & " T " & F(R.PeriodX, "0.000") & " s (" & F(Ux(iX) * 100, "0") & " %), y mode " & (iY + 1) & " T " &
            F(R.PeriodY, "0.000") & " s (" & F(Uy(iY) * 100, "0") & " %); cumulative mass participation x " & F(SUx(NM - 1) * 100, "0.0") & " %, y " & F(SUy(NM - 1) * 100, "0.0") & " %")
        If R.Participation < 0.9 Then Rep("WARNING: cumulative mass participation is below 90 % (ASCE 7-22 12.9.1.1): increase Modes")

        'equivalent lateral force base shear and scaling of the spectrum cases (12.9.1.4)
        Dim CsX, CsY, TX, TY As Double
        R.ElfX = ElfShear(P, Plan.Height, R.Weight, R.PeriodX, CsX, TX)
        R.ElfY = ElfShear(P, Plan.Height, R.Weight, R.PeriodY, CsY, TY)
        R.SpectrumX = Math.Abs(B(CASE_RSX).Fx) : R.SpectrumY = Math.Abs(B(CASE_RSY).Fy)
        R.ScaleX = Math.Max(1.0, P.SpectrumScaleMin * R.ElfX / R.SpectrumX)
        R.ScaleY = Math.Max(1.0, P.SpectrumScaleMin * R.ElfY / R.SpectrumY)
        Rep("Seismic weight W " & F(R.Weight, "0") & " kN; equivalent lateral force: x T " & F(TX, "0.000") & " s (Ta " & F(0.0724 * Math.Pow(Plan.Height, 0.8), "0.000") & ", Cu " & F(Cu(P.SD1), "0.00") & ") Cs " & F(CsX, "0.0000") & " V " & F(R.ElfX, "0") & " kN; y T " & F(TY, "0.000") &
            " s Cs " & F(CsY, "0.0000") & " V " & F(R.ElfY, "0") & " kN")
        Rep("Response spectrum base shear before scaling: x " & F(R.SpectrumX, "0") & " kN (" & F(R.SpectrumX / R.ElfX * 100, "0.0") & " % of V), y " & F(R.SpectrumY, "0") & " kN (" & F(R.SpectrumY / R.ElfY * 100, "0.0") & " % of V); target " & F(P.SpectrumScaleMin * 100, "0") & " %")
        If R.ScaleX > 1.0000001 OrElse R.ScaleY > 1.0000001 Then
            Dim Sf As Double = P.Ie * GRAVITY / P.EffR()
            If SapModel.GetModelIsLocked() Then Chk(SapModel.SetModelIsLocked(False), "SetModelIsLocked")
            RescaleSpectrum(Sf * R.ScaleX, Sf * R.ScaleY)
            Analyze("scaled spectrum")
            Dim B2 = ReadBase({CASE_RSX, CASE_RSY})
            Dim NewX As Double = Math.Abs(B2(CASE_RSX).Fx), NewY As Double = Math.Abs(B2(CASE_RSY).Fy)
            Rep("Response spectrum scale factors x " & F(R.ScaleX, "0.0000") & ", y " & F(R.ScaleY, "0.0000") & " (scale factors of the cases " & F(Sf * R.ScaleX, "0.0000") & ", " & F(Sf * R.ScaleY, "0.0000") & " m/s2); base shear now x " & F(NewX, "0") & " kN, y " & F(NewY, "0") & " kN")
            If NewX < P.SpectrumScaleMin * R.ElfX * 0.999 OrElse NewY < P.SpectrumScaleMin * R.ElfY * 0.999 Then Rep("WARNING: the scaled base shear is below the target")
        Else
            Rep("Response spectrum base shear is not below the target: no scaling")
        End If
        Rep("Note: the response spectrum scale factors are fixed numbers computed for the building of the check above (" & If(P.PreSize, "sections of the pre-sizing", "placeholder sections") & "). In the optimization the stiffness changes with the sections, so the base shear ratio changes; a design-dependent scaling (ASCE 7-22 12.9.1.4 for every analysis) is not part of the optimization program yet.")
        Rep("Seismic drift amplification of the optimization program (App.config SeismicDriftAmplification) = Cd / Ie = " & F(P.EffCd(), "0.##") & " / " & F(P.Ie, "0.##") & " = " & F(P.EffCd() / P.Ie, "0.###") & "; story drift limit " & F(P.DriftLimit, "0.###") & " hsx, wind service drift limit H / " & F(P.ServiceDriftRatio, "0"))
        Rep("Wind base shear (ASCE 7-22 Chapter 27): x " & F(R.WindX, "0") & " kN, y " & F(R.WindY, "0") & " kN (strength level, load factor 1.0 in the combinations)")
        Rep("Model check done in " & F((Date.Now - Started).TotalSeconds, "0") & " s")
    End Sub

    'New scale factors of the spectrum cases; every other column of the table is kept
    Private Sub RescaleSpectrum(ByVal SfX As Double, ByVal SfY As Double)
        Dim Key As String = "Load Case Definitions - Response Spectrum"
        Dim V, N As Integer, Fs() As String = Nothing, D() As String = Nothing
        Chk(SapModel.DatabaseTables.GetTableForEditingArray(Key, "", V, Fs, N, D), "DatabaseTables.GetTableForEditingArray " & Key)
        Dim Rows As New List(Of Dictionary(Of String, String))
        For i = 0 To N - 1
            Dim Row1 As New Dictionary(Of String, String)
            For j = 0 To Fs.Length - 1 : Row1(Fs(j)) = D(i * Fs.Length + j) : Next
            If Row1("Name") = CASE_RSX Then Row1("TransAccSF") = F(SfX, "0.######")
            If Row1("Name") = CASE_RSY Then Row1("TransAccSF") = F(SfY, "0.######")
            Rows.Add(Row1)
        Next
        PutTable(Key, V, Fs.ToList(), Rows)
    End Sub
End Class
