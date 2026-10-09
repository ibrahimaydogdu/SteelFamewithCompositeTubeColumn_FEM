Imports System.Globalization

'Step 9.3a: ASCE 7-22 load combinations (strength design, Section 2.3.1 and 2.3.6) and the steel / composite column design settings.
Partial Public Class EtabsBuilder
    Public Const DESIGN_CODE As String = "AISC 360-22"
    Public ComboNames As New List(Of String)

    Private Sub AddCombo(ByVal Name As String, ByVal ParamArray Terms() As Tuple(Of String, Double))
        Chk(SapModel.RespCombo.Add(Name, 0), "RespCombo.Add " & Name)
        For Each t In Terms
            Dim Kind As ETABSv1.eCNameType = ETABSv1.eCNameType.LoadCase
            Chk(SapModel.RespCombo.SetCaseList(Name, Kind, t.Item1, t.Item2), "RespCombo.SetCaseList " & Name)
        Next
        ComboNames.Add(Name)
    End Sub

    Private Shared Function Dd(ByVal Fac As Double) As Tuple(Of String, Double)()
        Return {Term(PAT_DEAD, Fac), Term(PAT_SDL, Fac)}
    End Function

    Private Shared Function Join(ByVal ParamArray Parts()() As Tuple(Of String, Double)) As Tuple(Of String, Double)()
        Return Parts.SelectMany(Function(x) x).ToArray()
    End Function

    Private Shared Function Scaled(ByVal Terms() As Tuple(Of String, Double), ByVal Fac As Double) As Tuple(Of String, Double)()
        Return Terms.Select(Function(x) Term(x.Item1, x.Item2 * Fac)).ToArray()
    End Function

    Private Shared Function Term(ByVal Case1 As String, ByVal Factor As Double) As Tuple(Of String, Double)
        Return Tuple.Create(Case1, Factor)
    End Function

    '_______________________________________________________________________________________________
    '  ASCE 7-22 2.3.1:  (1) 1.4D  (2) 1.2D + 1.6L + 0.5(Lr or S)  (3) 1.2D + 1.6(Lr or S) + (f1 L or 0.5W)
    '                    (4) 1.2D + 1.0W + f1 L + 0.5(Lr or S)  (5) 0.9D + 1.0W
    '  ASCE 7-22 2.3.6:  (6) (1.2 + 0.2 SDS) D + rho E + f1 L + 0.2S  (7) (0.9 - 0.2 SDS) D + rho E
    '  D = Dead + SDL, E = response spectrum cases (100 % + 30 % orthogonal combination, ETABS takes both signs of the spectrum results),
    '  W = WX, WY (Case 1) and 0.75 WX +- 0.75 WY (Case 3 of Figure 27.3-8), both signs.
    Private Sub DefineCombinations(ByVal Plan As BuildPlan_)
        Dim P As BuildParams_ = Plan.P
        Dim Snow As Boolean = P.SnowGround > 0
        Dim F1 As Double = P.LiveFactor
        ComboNames.Clear()

        Dim Roof As New List(Of Tuple(Of String, String)) From {Tuple.Create("Lr", PAT_ROOFLIVE)}       'roof live, snow
        If Snow Then Roof.Add(Tuple.Create("S", PAT_SNOW))
        Dim Winds As New List(Of Tuple(Of String, Tuple(Of String, Double)())) From {
            Tuple.Create("+WX", {Term(PAT_WX, 1)}), Tuple.Create("-WX", {Term(PAT_WX, -1)}), Tuple.Create("+WY", {Term(PAT_WY, 1)}), Tuple.Create("-WY", {Term(PAT_WY, -1)}),
            Tuple.Create("+WX+WY", {Term(PAT_WX, 0.75), Term(PAT_WY, 0.75)}), Tuple.Create("+WX-WY", {Term(PAT_WX, 0.75), Term(PAT_WY, -0.75)}),
            Tuple.Create("-WX+WY", {Term(PAT_WX, -0.75), Term(PAT_WY, 0.75)}), Tuple.Create("-WX-WY", {Term(PAT_WX, -0.75), Term(PAT_WY, -0.75)})}
        AddCombo("C1", Dd(1.4))
        For Each r In Roof
            AddCombo("C2_" & r.Item1, Join(Dd(1.2), {Term(PAT_LIVE, 1.6), Term(r.Item2, 0.5)}))
            AddCombo("C3L_" & r.Item1, Join(Dd(1.2), {Term(r.Item2, 1.6), Term(PAT_LIVE, F1)}))
        Next
        For Each w In Winds
            For Each r In Roof
                AddCombo("C3W_" & r.Item1 & w.Item1, Join(Dd(1.2), {Term(r.Item2, 1.6)}, Scaled(w.Item2, 0.5)))
                AddCombo("C4_" & r.Item1 & w.Item1, Join(Dd(1.2), w.Item2, {Term(PAT_LIVE, F1), Term(r.Item2, 0.5)}))
            Next
            AddCombo("C5" & w.Item1, Join(Dd(0.9), w.Item2))
        Next
        'seismic: E in the four orthogonal combinations
        Dim Eq As New List(Of Tuple(Of String, Tuple(Of String, Double)())) From {
            Tuple.Create("RSX", {Term(CASE_RSX, P.Rho)}), Tuple.Create("RSY", {Term(CASE_RSY, P.Rho)}),
            Tuple.Create("RSX+0.3RSY", {Term(CASE_RSX, P.Rho), Term(CASE_RSY, 0.3 * P.Rho)}), Tuple.Create("0.3RSX+RSY", {Term(CASE_RSX, 0.3 * P.Rho), Term(CASE_RSY, P.Rho)})}
        For Each e In Eq
            Dim Lat As Tuple(Of String, Double)() = {Term(PAT_LIVE, F1)}
            If Snow Then Lat = {Term(PAT_LIVE, F1), Term(PAT_SNOW, 0.2)}
            AddCombo("C6_" & e.Item1, Join(Dd(1.2 + 0.2 * P.SDS), e.Item2, Lat))
            AddCombo("C7_" & e.Item1, Join(Dd(0.9 - 0.2 * P.SDS), e.Item2))
        Next
        'the design of the optimization program uses the strength combinations selected in the model
        Chk(SapModel.DesignSteel.SetCode(DESIGN_CODE), "DesignSteel.SetCode")
        Chk(SapModel.DesignCompositeColumn.SetCode(DESIGN_CODE), "DesignCompositeColumn.SetCode")
        For Each n In ComboNames
            Chk(SapModel.DesignSteel.SetComboStrength(n, True), "DesignSteel.SetComboStrength " & n)
            Chk(SapModel.DesignCompositeColumn.SetComboStrength(n, True), "DesignCompositeColumn.SetComboStrength " & n)
        Next
        Rep("Load combinations (ASCE 7-22 2.3.1 and 2.3.6, f1 = " & F1.ToString("0.##", CultureInfo.InvariantCulture) & ", rho " & P.Rho.ToString("0.##", CultureInfo.InvariantCulture) & ", 0.2 SDS = " & (0.2 * P.SDS).ToString("0.###", CultureInfo.InvariantCulture) & "): " & ComboNames.Count & " combinations, all selected for the " & DESIGN_CODE & " steel and composite column designs")
    End Sub

    '_______________________________________________________________________________________________
    'Lateral bracing of the beams (secondary beams / deck): unbraced length ratio of every beam = 1 / ceil(span / BeamBracingSpacing),
    'written to the design overwrites (minor axis and lateral-torsional buckling). Without it ETABS takes the whole span as unbraced
    'and AISC 341 D1.2b (Lb / ry limit of the SMF beams) rejects most sections.
    Private Sub DefineBeamBracing(ByVal Plan As BuildPlan_)
        Dim P As BuildParams_ = Plan.P
        If P.BeamBracingSpacing <= 0 Then Rep("Beam lateral bracing: none (beams unbraced over the whole span)") : Return
        Dim Changed As Integer
        For Each B In Plan.Beams
            Dim R As Double = 1.0 / Math.Max(1, CInt(Math.Ceiling(B.Length / P.BeamBracingSpacing - 0.000001)))
            If R >= 0.9999 Then Continue For
            'The overwrite table ignores LMinor / LTB on import (silently), so the API is used: items 27 = LMinor, 28 = LTB of AISC 360-22
            Dim N As String = FrameNames(B)
            If SapModel.DesignSteel.AISC360_22.SetOverwrite(N, 27, R, ETABSv1.eItemType.Objects) = 0 AndAlso SapModel.DesignSteel.AISC360_22.SetOverwrite(N, 28, R, ETABSv1.eItemType.Objects) = 0 Then Changed += 1
        Next
        Rep("Beam lateral bracing: spacing " & F(P.BeamBracingSpacing) & " m, " & Changed & " of " & Plan.Beams.Count & " beams divided into equal unbraced lengths (design overwrites LMinor and LTB)")
    End Sub

    '_______________________________________________________________________________________________
    'Seismic design category (ASCE 7-22 Tables 11.6-1 and 11.6-2, risk categories I to IV; S1 >= 0.75 g: E or F)
    Public Shared Function SeismicDesignCategory(ByVal P As BuildParams_) As String
        Dim IV As Boolean = (P.RiskCategory = "IV")
        Dim ByShort As Integer = If(P.SDS < 1 / 6.0, 0, If(P.SDS < 1 / 3.0, If(IV, 2, 1), If(P.SDS < 0.5, 2, 3)))
        Dim ByOne As Integer = If(P.SD1 < 1 / 15.0, 0, If(P.SD1 < 2 / 15.0, If(IV, 2, 1), If(P.SD1 < 0.2, 2, 3)))
        Dim Cat As Integer = Math.Max(ByShort, ByOne)
        If P.S1 >= 0.75 Then Return If(IV, "F", "E")
        Return "ABCD"(Cat).ToString()
    End Function

    'Steel frame design preferences: framing type, seismic parameters, Direct Analysis Method, LRFD
    Private Sub DefineDesignSettings(ByVal Plan As BuildPlan_)
        Dim P As BuildParams_ = Plan.P
        Dim Sdc As String = SeismicDesignCategory(P)
        UpdatePreferences("Steel Frame Design Preferences - AISC 360-22", New Dictionary(Of String, String) From {
            {"FrameType", P.FrameClass}, {"SDC", Sdc}, {"ImpFactor", P.Ie.ToString("0.###", CultureInfo.InvariantCulture)}, {"Rho", P.Rho.ToString("0.###", CultureInfo.InvariantCulture)},
            {"Sds", P.SDS.ToString("0.###", CultureInfo.InvariantCulture)}, {"R", P.EffR().ToString("0.###", CultureInfo.InvariantCulture)},
            {"Omega0", P.EffOmega0().ToString("0.###", CultureInfo.InvariantCulture)}, {"Cd", P.EffCd().ToString("0.###", CultureInfo.InvariantCulture)},
            {"DesProv", "LRFD"}, {"AnalMethod", "Direct Analysis"}, {"SeismicCode", If(P.SeismicProvisions, "No", "Yes")}})
        Rep("Design settings (" & DESIGN_CODE & "): " & P.FrameClass & ", seismic design category " & Sdc & " (risk category " & P.RiskCategory & "), R " & P.EffR().ToString("0.##", CultureInfo.InvariantCulture) &
            ", Omega0 " & P.EffOmega0().ToString("0.##", CultureInfo.InvariantCulture) & ", Cd " & P.EffCd().ToString("0.##", CultureInfo.InvariantCulture) & ", LRFD, Direct Analysis Method, AISC 341 seismic provisions " & If(P.SeismicProvisions, "ON (strong column - weak beam etc. are checked)", "OFF (AISC 360 strength and drift only)"))
    End Sub
End Class
