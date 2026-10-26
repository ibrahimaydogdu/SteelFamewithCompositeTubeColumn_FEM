Imports System.Globalization
Imports FemCore

'AISC 341-22 options (Paper 1 change requests PCR-5a to 5i, PCR-7). Every switch is an App.config key whose default is the behaviour of
'version 0f2f0be ("legacy"): old results are reproduced unless a key is set. The seismic data of the model (framing type, AISC 341 on/off,
'Omega0, SDS) come from the steel frame design preferences of the model (ModelBuilder writes them).
Partial Public Class ETABS_Class
    'legacy: AISC 341-16 width-to-thickness limits, Lb 0.095 / 0.19, CFT walls without Ry, no composite or corner SCWB (0f2f0be) | 2022: AISC 341-22
    Friend ReadOnly Aisc341Edition As String = SettingChoice("Aisc341Edition", "legacy", {"legacy", "2022"})
    'legacy: SCWB active when ETABS reports beam/column ratios | auto: SMF and C-SMF with AISC 341 on (E3.4a, G3.4a; none in IMF, E2.4 / G2.4) | on | off
    Friend ReadOnly SeismicScwbMode As String = SettingChoice("SeismicSCWB", "legacy", {"legacy", "auto", "on", "off"})
    'false: filled composite columns keep their stiffness in the analysis (0f2f0be) | true: AISC 360-22 I1.5 (0.8 EA, 0.64 EIeff) with the Direct Analysis Method
    Friend ReadOnly CompositeDamReduction As Boolean = (SettingChoice("CompositeDamReduction", "false", {"false", "true"}) = "true")
    'legacy: final design = first passing candidate, else guard steps (0f2f0be) | unmodified: a design changed by guard steps is a failed verification
    Friend ReadOnly FinalProtocol As String = SettingChoice("FinalProtocol", "legacy", {"legacy", "unmodified"})
    'xml: ductility level of the tube walls from TubeSections.xml (0f2f0be) | auto: from the framing type (SMF High, IMF Moderate, AISC 341 off: none)
    Friend ReadOnly TubeDuctilityMode As String = SettingChoice("TubeSeismicDuctility", "xml", {"xml", "auto"})
    'false: in the composite mode every column group is composite (0f2f0be) | true: the interior (gravity) column groups stay W sections
    Friend ReadOnly InteriorColumnsSteel As Boolean = (SettingChoice("InteriorColumnsSteel", "false", {"false", "true"}) = "true")
    'calibrated: beam side of the SCWB ratio from the closed form fitted to ETABS (0f2f0be) | code: AISC 341-22 Eq. E3-3 / G3.4a
    Friend ReadOnly ScwbBeamSide As String = SettingChoice("ScwbBeamSide", "calibrated", {"calibrated", "code"})

    Friend ReadOnly Property Is2022 As Boolean
        Get
            Return Aisc341Edition = "2022"
        End Get
    End Property

    'Steel frame design preferences of the model (ReadDesignPreferences)
    Friend PrefFrameType As String = ""         'SMF | IMF | OMF | ...
    Friend PrefSeismicActive As Boolean          'AISC 341 provisions checked by ETABS (preference "SeismicCode" = No, i.e. not ignored)
    Friend PrefOmega0 As Double, PrefSds As Double, PrefR As Double, PrefCd As Double
    Friend PrefRead As Boolean                   'False: the table could not be read

    'Choice setting: missing = default; a value outside the list stops the run (no silent assumption)
    Private Shared Function SettingChoice(ByVal Key As String, ByVal [Default] As String, ByVal Allowed() As String) As String
        Dim V As String = If(ReadSetting(Key), "").Trim().ToLowerInvariant()
        If V.Length = 0 Then Return [Default]
        If Not Allowed.Contains(V) Then Throw New ArgumentException("App.config " & Key & " = '" & V & "' is not valid (" & String.Join(" | ", Allowed) & ")")
        Return V
    End Function

    'AISC 341 provisions active in the optimizer: the model checks them (SeismicCode = No) and the framing type is a moment frame of AISC 341
    Friend ReadOnly Property Aisc341Active As Boolean
        Get
            Return PrefRead AndAlso PrefSeismicActive AndAlso (PrefFrameType = "SMF" OrElse PrefFrameType = "IMF")
        End Get
    End Property

    Private Sub ReadDesignPreferences()
        PrefRead = False
        Try
            Dim Version, N As Integer, Fields() As String = Nothing, Data() As String = Nothing
            If SapModel.DatabaseTables.GetTableForEditingArray("Steel Frame Design Preferences - " & FormInfo.FrameInfo.SteelDesignCode, "", Version, Fields, N, Data) <> 0 OrElse N < 1 Then
                Errorlogprint("Warning: steel frame design preferences of " & FormInfo.FrameInfo.SteelDesignCode & " not readable; AISC 341 options that need the framing type use the legacy rules")
                Return
            End If
            Dim Txt = Function(Name As String) As String
                          Dim i As Integer = Array.IndexOf(Fields, Name)
                          Return If(i >= 0, If(Data(i), "").Trim(), "")
                      End Function
            Dim Num = Function(Name As String) As Double
                          Dim x As Double
                          Return If(Double.TryParse(Txt(Name), NumberStyles.Float, CultureInfo.InvariantCulture, x), x, 0)
                      End Function
            PrefFrameType = Txt("FrameType")
            PrefSeismicActive = Not Txt("SeismicCode").Equals("Yes", StringComparison.OrdinalIgnoreCase)
            PrefOmega0 = Num("Omega0") : PrefSds = Num("Sds") : PrefR = Num("R") : PrefCd = Num("Cd")
            PrefDirectAnalysis = Txt("AnalMethod").StartsWith("Direct", StringComparison.OrdinalIgnoreCase)
            PrefRead = True
            Errorlogprint("Info: design preferences: framing " & PrefFrameType & ", AISC 341 " & If(PrefSeismicActive, "on", "off") & ", R " & PrefR.ToString("0.##", CultureInfo.InvariantCulture) &
                          ", Omega0 " & PrefOmega0.ToString("0.##", CultureInfo.InvariantCulture) & ", Cd " & PrefCd.ToString("0.##", CultureInfo.InvariantCulture) & ", SDS " & PrefSds.ToString("0.###", CultureInfo.InvariantCulture))
        Catch ex As Exception
            Errorlogprint("Warning: steel frame design preferences: " & ex.Message)
        End Try
    End Sub

    'PCR-5a: SCWB (beam/column moment ratio) as a constraint. legacy: ETABS gives beam/column ratios in the initial design (0f2f0be).
    'auto: AISC 341-22 E3.4a / G3.4a require it for SMF and C-SMF only (E2.4 / G2.4: none for IMF and C-IMF)
    Private Function ScwbActiveFor(ByVal BC0 As Dictionary(Of String, Double)) As Boolean
        Dim Legacy As Boolean = BC0 IsNot Nothing AndAlso BC0.Values.Any(Function(v) v > 0)
        Select Case SeismicScwbMode
            Case "on" : Return True
            Case "off" : Return False
            Case "auto"
                If Not PrefRead Then
                    Errorlogprint("Warning: SeismicSCWB = auto but the design preferences are not readable: legacy rule (beam/column ratios of ETABS)")
                    Return Legacy
                End If
                Return PrefSeismicActive AndAlso PrefFrameType = "SMF"
            Case Else
                Return Legacy
        End Select
    End Function

    'PCR-5a: member ductility (Table D1.1) and beam bracing (D1.2) checks. legacy: only together with SCWB (0f2f0be); otherwise whenever the
    'AISC 341 provisions are on for an SMF or IMF (C-SMF / C-IMF), independently of the moment ratio
    Friend ReadOnly Property SeismicChecksActive As Boolean
        Get
            If SeismicScwbMode = "legacy" OrElse Not PrefRead Then Return SCWBActive
            Return Aisc341Active
        End Get
    End Property

    'ETABS steel design: a beam/column capacity message is not a design error when the SCWB constraint is off (IMF: ETABS may still report it)
    Private ReadOnly Property IgnoreEtabsBeamColumnMessages As Boolean
        Get
            Return SeismicScwbMode <> "legacy" AndAlso Not SCWBActive
        End Get
    End Property

    '_______________________________________________________________________________________________
    'PCR-8: drift limits by load type (drift mode "lateral cases only"). legacy: the inter-story limit (h / InterStoryDriftR) and the top
    'drift limit (H / TopStoryDriftR) apply to every lateral case (0f2f0be) | split: seismic cases (amplified by SeismicDriftAmplification)
    'are checked against the inter-story limit only (ASCE 7-22 Table 12.12-1), the other lateral (wind) cases against the top limit only,
    'with their displacements multiplied by WindDriftFactor (service wind level)
    Friend ReadOnly DriftLimitMode As String = SettingChoice("DriftLimits", "legacy", {"legacy", "split"})
    Private ReadOnly WindDriftFactor As Double = ReadNumber("WindDriftFactor", 1.0)
    Private ReadOnly SeismicDriftCases As New HashSet(Of String)

    Friend ReadOnly Property DriftSplit As Boolean
        Get
            Return DriftLimitMode = "split" AndAlso FormInfo.DriftComboMode = MiscellaneousStructures.DriftComboMode_.LateralCasesOnly
        End Get
    End Property

    Private Sub LogDriftSplit()
        If DriftLimitMode <> "split" Then Return
        If Not DriftSplit Then
            Errorlogprint("Warning: DriftLimits = split needs the drift mode 'lateral cases only'; the legacy drift limits are used")
            Return
        End If
        Dim Wind = DriftCaseNames.Where(Function(c) Not SeismicDriftCases.Contains(c)).ToList()
        Errorlogprint("Info: drift limits split: seismic cases [" & String.Join(", ", SeismicDriftCases) & "] inter-story drift h / " &
                      FormInfo.FrameInfo.InterStoryDriftR.ToString("0.###", CultureInfo.InvariantCulture) & "; wind cases [" & String.Join(", ", Wind) & "] top drift H / " &
                      FormInfo.FrameInfo.TopStoryDriftR.ToString("0.###", CultureInfo.InvariantCulture) & " with displacement factor " & WindDriftFactor.ToString("0.####", CultureInfo.InvariantCulture))
        If Wind.Count = 0 Then Errorlogprint("Warning: drift limits split: no wind case, the top drift is not checked")
        If SeismicDriftCases.Count = 0 Then Errorlogprint("Warning: drift limits split: no seismic case, the inter-story drift is not checked")
    End Sub

    'PCR-5d: AISC 341-22 wall limits of the filled tubes with Ry = expected / specified yield stress of the model steel (no silent default).
    'PCR-5i: TubeSeismicDuctility = auto -> ductility level of the walls from the framing type (SMF High, IMF Moderate, AISC 341 off: none)
    Private Function SetTubeSeismicLimits(ByVal Fy As Double, ByVal EFy As Double) As Integer
        If Is2022 Then
            If Fy <= 0 OrElse EFy <= 0 Then
                Errorlogprint("Expected yield stress of " & STEEL_MATERIAL & " not available (Fy " & Fy & ", expected " & EFy & "): Ry of the AISC 341-22 tube wall limits cannot be set")
                Return -1
            End If
            TubeSettings.Edition2022 = True
            TubeSettings.Ry = EFy / Fy
        End If
        If TubeDuctilityMode = "auto" Then
            If Not PrefRead Then
                Errorlogprint("Warning: TubeSeismicDuctility = auto but the design preferences are not readable: ductility level of TubeSections.xml kept")
            Else
                TubeSettings.SeismicDuctility = If(Not Aisc341Active, SeismicDuctility_.None, If(PrefFrameType = "SMF", SeismicDuctility_.High, SeismicDuctility_.Moderate))
            End If
        End If
        Errorlogprint("Info: tube wall limits: " & TubeSettings.SeismicDuctility.ToString().ToLowerInvariant() & " ductility (" & TubeDuctilityMode & "), " &
                      If(Is2022, "AISC 341-22 with Ry " & TubeSettings.Ry.ToString("0.###", CultureInfo.InvariantCulture), "legacy (no Ry)"))
        Return 0
    End Function

    'PCR-7: InteriorColumnsSteel = true -> the interior (gravity) column groups stay W sections in the composite and hybrid modes.
    'Interior groups are recognised by the ModelBuilder group names COL-INTERIOR-<band> (App.config InteriorColumnGroups: name prefix)
    Private ReadOnly InteriorColumnPrefix As String = If(ReadSetting("InteriorColumnGroups"), "COL-INTERIOR").Trim()

    'PCR-5g: corner column groups (ModelBuilder COL-CORNER-<band>; App.config CornerColumnGroups: name prefix) when the corner rule is active
    Private ReadOnly CornerColumnPrefix As String = If(ReadSetting("CornerColumnGroups"), "COL-CORNER").Trim()

    Friend Function CornerScwbGroup(ByVal GroupName As String) As Boolean
        Return Is2022 AndAlso SCWBActive AndAlso GroupName IsNot Nothing AndAlso CornerColumnPrefix.Length > 0 AndAlso
               GroupName.StartsWith(CornerColumnPrefix, StringComparison.OrdinalIgnoreCase)
    End Function

    Friend Function SteelInteriorGroup(ByVal GroupName As String) As Boolean
        Return InteriorColumnsSteel AndAlso GroupName IsNot Nothing AndAlso InteriorColumnPrefix.Length > 0 AndAlso
               GroupName.StartsWith(InteriorColumnPrefix, StringComparison.OrdinalIgnoreCase)
    End Function

    'PCR-5f: Direct Analysis Method stiffness of the filled composite columns (AISC 360-22 I1.5: 0.8 x the axial stiffness, 0.64 EIeff).
    'ETABS 22.6 reduces only the members of the steel frame design, never the composite columns (V4-a(f)), so the factors are written here.
    Friend PrefDirectAnalysis As Boolean                 'steel design preference AnalMethod = Direct Analysis (ReadDesignPreferences)

    Friend ReadOnly Property CompositeDamActive As Boolean
        Get
            Return CompositeDamReduction AndAlso TubeMode AndAlso PrefDirectAnalysis
        End Get
    End Property

    'Modifiers (AMod, A2Mod, A3Mod, JMod, I2Mod, I3Mod, MMod, WMod) of the ETABS filled tube section of the final check. ETABS takes the gross
    'transformed section, I = Is + Ic Ec / Es (V4-a(f)); the flexural factor makes 0.64 EIeff with EIeff / Es of the internal check (C3 of I2-13)
    Private Function TubeModifiers(ByVal id As Integer, ByVal Plain() As String) As String()
        If Not CompositeDamActive Then Return Plain
        Dim T As TubeSection_ = Tubes(id)
        Dim n As Double = CompositeMat.Ec / CompositeMat.Es
        Dim Gross = Function(H As Double, B As Double) As Double
                        If T.Shape = TubeShape_.Pipe Then
                            Dim d As Double = T.H - 2 * T.t
                            Return Math.PI / 64 * (T.H ^ 4 - d ^ 4) + n * Math.PI / 64 * d ^ 4
                        End If
                        Dim hc As Double = H - 2 * T.t, bc As Double = B - 2 * T.t
                        Return (B * H ^ 3 - bc * hc ^ 3) / 12 + n * bc * hc ^ 3 / 12
                    End Function
        Dim Tr As TransformedSection_ = CompositeSec(id).Transformed()
        Dim F3 As Double = 0.64 * Tr.I33 / Gross(T.H, T.B)
        Dim F2 As Double = 0.64 * Tr.I22 / Gross(T.B, T.H)
        If F3 < 0.4 OrElse F3 > 0.7 OrElse F2 < 0.4 OrElse F2 > 0.7 Then
            Throw New InvalidOperationException("DAM stiffness modifier of " & T.Name & " out of range (" & F2 & ", " & F3 & "): check the units of the transformed section")
        End If
        Dim Fmt = Function(x As Double) x.ToString("R", CultureInfo.InvariantCulture)
        Return {"0.8", Plain(1), Plain(2), Plain(3), Fmt(F2), Fmt(F3), Plain(6), Plain(7)}
    End Function

    'PCR-5g: gravity part of the AISC 341-22 D1.4a overstrength combination 1.2 D + 0.5 L + 0.2 SDS D (ASCE 7-22 2.3.6, load combination 6):
    'dead / superimposed dead patterns 1.2 + 0.2 SDS, live / reducible live patterns 0.5 (roof live and snow: not part of it, no snow in the models)
    Friend Function CornerGravityCombo(ByVal C As FemCore.FemCombo, ByRef Why As String) As Boolean
        Dim N As Integer, Names() As String = Nothing
        If SapModel.LoadPatterns.GetNameList(N, Names) <> 0 OrElse N = 0 Then Why = "no load patterns" : Return False
        C.Name = "AISC341_D1.4a_gravity"
        C.Terms.Clear() : C.RSTerms.Clear()
        Dim Dead As Double = 1.2 + 0.2 * PrefSds
        Dim Txt As New List(Of String)
        For Each p In Names
            Dim T As ETABSv1.eLoadPatternType
            If SapModel.LoadPatterns.GetLoadType(p, T) <> 0 Then Continue For
            Dim f As Double = 0
            Select Case T
                Case ETABSv1.eLoadPatternType.Dead, ETABSv1.eLoadPatternType.SuperDead : f = Dead
                Case ETABSv1.eLoadPatternType.Live, ETABSv1.eLoadPatternType.ReduceLive : f = 0.5
            End Select
            If f = 0 Then Continue For
            C.Terms.Add(New FemCore.FemTerm With {.Name = p, .Factor = f})
            Txt.Add(f.ToString("0.###", CultureInfo.InvariantCulture) & " " & p)
        Next
        If C.Terms.Count = 0 Then Why = "no dead or live pattern" : Return False
        If PrefSds <= 0 Then Why = "SDS of the design preferences missing" : Return False
        Errorlogprint("Info: AISC 341-22 corner check (E3.3 / G3.3): Pr = " & String.Join(" + ", Txt) & " + Omega0 " & PrefOmega0.ToString("0.##", CultureInfo.InvariantCulture) & " (|RSx| + |RSy|)")
        Return True
    End Function

    'PCR-5g: composite corner column, AISC 360-22 I5 interaction of the internal composite check (same routine as G1_2_ConsComposite) for the
    'axial compression P [kN] and the moments Mx (major, local 3) and My [kN m] of the yielding beams, member length L [m]; shear not included
    Friend Function CompositeInteraction(ByVal SecID As Integer, ByVal P As Double, ByVal Mx As Double, ByVal My As Double, ByVal L As Double) As Double
        Dim Check As CompositeMemberCheck = MemberCheck(SecID, L * 1000)
        Dim PMM, Shear As Double
        Check.Ratio(-Math.Abs(P), {Mx * 1000}, {My * 1000}, 0, 0, PMM, Shear)
        Return PMM * CompositeStrengthFactor
    End Function

    Private Sub LogAisc341Options()
        SteelDesign.Aisc341_2022 = Is2022           'FEM checks (FemCore) and the ductility filter (PCR-5c, 5e)
        Errorlogprint("Info: AISC 341 options: edition " & Aisc341Edition & ", SCWB " & SeismicScwbMode & ", composite DAM reduction " & CompositeDamReduction.ToString().ToLowerInvariant() &
                      ", final protocol " & FinalProtocol & ", tube ductility " & TubeDuctilityMode & ", interior columns steel " & InteriorColumnsSteel.ToString().ToLowerInvariant() &
                      ", SCWB beam side " & ScwbBeamSide)
    End Sub
End Class
