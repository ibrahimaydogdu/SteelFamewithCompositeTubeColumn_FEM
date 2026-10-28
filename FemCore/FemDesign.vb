Imports System.Collections.Generic

'Steel design preferences (AISC 360-22, LRFD)
Public Class DesignPrefs
    Public PhiBend As Double = 0.9, PhiComp As Double = 0.9, PhiTY As Double = 0.9, PhiTF As Double = 0.75, PhiShear As Double = 0.9
    Public DCLimit As Double = 0.95
    Public DirectAnalysis As Boolean = True
End Class

'Capacities of an I section member (AISC 360-22, LRFD)
Public Class IShapeCapacity
    Public Pnc As Double, Pnt As Double                 'compression, tension (nominal)
    Public Mn3 As Double, Mn2 As Double                 'major and minor axis flexure (nominal, Lb as given)
    Public Vn2 As Double, Vn3 As Double
    Public PhiV2 As Double = 0.9, PhiV3 As Double = 0.9
    Public FlangeClass As String, WebClass As String
End Class

Public Module SteelDesign

    '_______________________________________________________________________________________________
    'Section ratios
    Public Function WebHeight(ByVal S As FemSection) As Double
        Return S.Depth - 2 * (S.FlangeThickness + S.Fillet)
    End Function

    Public Function Rts(ByVal S As FemSection) As Double
        Dim h0 As Double = S.Depth - S.FlangeThickness
        Dim Cw As Double = S.I22 * h0 * h0 / 4
        Return Math.Sqrt(Math.Sqrt(S.I22 * Cw) / S.S33)
    End Function

    '_______________________________________________________________________________________________
    'Compression (E3, E7) for the given effective lengths about both axes (K = 1 in the Direct Analysis Method)
    Public Function CompressionStrength(ByVal S As FemSection, ByVal LMajor As Double, ByVal LMinor As Double) As Double
        If S.IsBox Then Return BoxCompression(S, LMajor, LMinor)
        Dim E As Double = S.E, Fy As Double = S.Fy
        Dim Root As Double = Math.Sqrt(E / Fy)
        Dim lamF As Double = S.Width / (2 * S.FlangeThickness)
        Dim lamW As Double = WebHeight(S) / S.WebThickness
        'slender elements (E7): flange Qs (unstiffened), web Qa (stiffened)
        Dim Qs As Double = 1.0
        If lamF > 0.56 * Root Then
            If lamF <= 1.03 * Root Then
                Qs = 1.415 - 0.74 * lamF / Root
            Else
                Qs = 0.69 * E / (Fy * lamF * lamF)
            End If
        End If
        Dim Qa As Double = 1.0
        Dim KLr As Double = Math.Max(LMajor / S.R33, LMinor / S.R22)
        Dim Fe As Double = Math.PI * Math.PI * E / (KLr * KLr)
        If lamW > 1.49 * Root Then
            Dim Q As Double = Qs
            Dim Fcr0 As Double = If(KLr <= 4.71 * Root / Math.Sqrt(Q), Q * Math.Pow(0.658, Q * Fy / Fe) * Fy, 0.877 * Fe)
            Dim f As Double = Fcr0
            Dim be As Double = 1.92 * S.WebThickness * Math.Sqrt(E / f) * (1 - 0.34 / lamW * Math.Sqrt(E / f))
            be = Math.Min(be, WebHeight(S))
            Dim Ae As Double = S.A - (WebHeight(S) - be) * S.WebThickness
            Qa = Ae / S.A
        End If
        Dim Qt As Double = Qs * Qa
        Dim Fcr As Double
        If Qt = 1.0 Then
            Fcr = If(KLr <= 4.71 * Root, Math.Pow(0.658, Fy / Fe) * Fy, 0.877 * Fe)
        Else
            Fcr = If(KLr <= 4.71 * Root / Math.Sqrt(Qt), Qt * Math.Pow(0.658, Qt * Fy / Fe) * Fy, 0.877 * Fe)
        End If
        Return Fcr * S.A
    End Function

    '_______________________________________________________________________________________________
    'Major axis flexure (F2, F3): lateral-torsional buckling for the unbraced length Lb and the moment gradient factor Cb
    Public Function MajorFlexure(ByVal S As FemSection, ByVal Lb As Double, ByVal Cb As Double) As Double
        If S.IsBox Then Return BoxFlexure(S, True)
        Dim E As Double = S.E, Fy As Double = S.Fy
        Dim Root As Double = Math.Sqrt(E / Fy)
        Dim Mp As Double = Fy * S.Z33
        Dim h0 As Double = S.Depth - S.FlangeThickness
        Dim lamF As Double = S.Width / (2 * S.FlangeThickness)
        Dim lamW As Double = WebHeight(S) / S.WebThickness
        Dim rts1 As Double = Rts(S)
        Dim Lp As Double = 1.76 * S.R22 * Root
        Dim Jc As Double = S.J
        Dim t As Double = Jc / (S.S33 * h0)
        Dim Lr As Double = 1.95 * rts1 * E / (0.7 * Fy) * Math.Sqrt(t + Math.Sqrt(t * t + 6.76 * (0.7 * Fy / E) ^ 2))
        Dim Mn As Double
        If Lb <= Lp Then
            Mn = Mp
        ElseIf Lb <= Lr Then
            Mn = Cb * (Mp - (Mp - 0.7 * Fy * S.S33) * (Lb - Lp) / (Lr - Lp))
            Mn = Math.Min(Mn, Mp)
        Else
            Dim r As Double = Lb / rts1
            Dim Fcr As Double = Cb * Math.PI * Math.PI * E / (r * r) * Math.Sqrt(1 + 0.078 * t * r * r)
            Mn = Math.Min(Fcr * S.S33, Mp)
        End If
        'compression flange local buckling (F3): noncompact and slender flanges
        Dim lamPF As Double = 0.38 * Root, lamRF As Double = 1.0 * Root
        If lamF > lamPF Then
            Dim Mflb As Double
            If lamF <= lamRF Then
                Mflb = Mp - (Mp - 0.7 * Fy * S.S33) * (lamF - lamPF) / (lamRF - lamPF)
            Else
                Dim kc As Double = Math.Min(0.76, Math.Max(0.35, 4 / Math.Sqrt(lamW)))
                Mflb = 0.9 * E * kc * S.S33 / (lamF * lamF)
            End If
            Mn = Math.Min(Mn, Mflb)
        End If
        Return Mn
    End Function

    'Minor axis flexure (F6)
    Public Function MinorFlexure(ByVal S As FemSection) As Double
        If S.IsBox Then Return BoxFlexure(S, False)
        Dim E As Double = S.E, Fy As Double = S.Fy
        Dim Root As Double = Math.Sqrt(E / Fy)
        Dim lamF As Double = S.Width / (2 * S.FlangeThickness)
        Dim Mp As Double = Math.Min(Fy * S.Z22, 1.6 * Fy * S.S22)
        If lamF <= 0.38 * Root Then Return Mp
        If lamF <= 1.0 * Root Then Return Mp - (Mp - 0.7 * Fy * S.S22) * (lamF - 0.38 * Root) / (0.62 * Root)
        Return 0.69 * E / (lamF * lamF) * S.S22
    End Function

    'Shear: major axis (web, G2.1) and minor axis (flanges, G6); returns nominal strength and phi
    Public Sub Shear(ByVal S As FemSection, ByRef Vn2 As Double, ByRef Phi2 As Double, ByRef Vn3 As Double, ByRef Phi3 As Double)
        If S.IsBox Then
            Vn2 = BoxShear(S, True) : Vn3 = BoxShear(S, False) : Phi2 = 0.9 : Phi3 = 0.9
            Return
        End If
        Dim E As Double = S.E, Fy As Double = S.Fy
        Dim h As Double = WebHeight(S), tw As Double = S.WebThickness
        Dim lam As Double = h / tw
        Dim Aw As Double = S.Depth * tw
        Phi2 = 0.9
        Dim Cv1 As Double = 1.0
        If lam <= 2.24 * Math.Sqrt(E / Fy) Then
            Phi2 = 1.0
        Else
            Dim kv As Double = 5.34
            Dim lim1 As Double = 1.1 * Math.Sqrt(kv * E / Fy), lim2 As Double = 1.37 * Math.Sqrt(kv * E / Fy)
            If lam <= lim1 Then
                Cv1 = 1.0
            ElseIf lam <= lim2 Then
                Cv1 = lim1 / lam
            Else
                Cv1 = 1.51 * kv * E / (lam * lam * Fy)
            End If
        End If
        Vn2 = 0.6 * Fy * Aw * Cv1
        'minor axis: two flanges, kv = 1.2
        Dim bt As Double = S.Width / (2 * S.FlangeThickness)
        Dim Cv2 As Double = 1.0
        Dim kv2 As Double = 1.2
        Dim l1 As Double = 1.1 * Math.Sqrt(kv2 * E / Fy), l2 As Double = 1.37 * Math.Sqrt(kv2 * E / Fy)
        If bt <= l1 Then
            Cv2 = 1.0
        ElseIf bt <= l2 Then
            Cv2 = l1 / bt
        Else
            Cv2 = 1.51 * kv2 * E / (bt * bt * Fy)
        End If
        Vn3 = 0.6 * Fy * (2 * S.Width * S.FlangeThickness) * Cv2
        Phi3 = 0.9
    End Sub

    'Moment gradient factor of AISC 360-22 F1-1 from the absolute moments at the maximum and at the quarter points of the unbraced segment
    Public Function CbFactor(ByVal Mmax As Double, ByVal Ma As Double, ByVal Mb As Double, ByVal Mc As Double) As Double
        Dim d As Double = 2.5 * Mmax + 3 * Ma + 4 * Mb + 3 * Mc
        If d <= 0 OrElse Mmax <= 0 Then Return 1.0
        Return Math.Min(3.0, 12.5 * Mmax / d)
    End Function


    '_______________________________________________________________________________________________
    'PCR-10: unfilled box members (AISC 360-22 E3 / E7, F7, G4). Walls of width B (flanges for the major axis) and depth H (webs);
    'b / t with the flat width of IsBox sections. The box catalog of the host only holds walls that are compact for flexure and nonslender
    'for compression (the AISC 341 limits are stricter), so the slender branches of flexure are a conservative fallback that is not reached.
    Public Function BoxWallRatio(ByVal S As FemSection, ByVal WidthWalls As Boolean) As Double
        Dim Outer As Double = If(WidthWalls, S.Width, S.Depth)
        Return (Outer - S.BoxCornerT * S.FlangeThickness) / S.FlangeThickness
    End Function

    'E3 with Q = 1, E7 effective widths of the slender walls (Table E7.1 case (b), walls of square and rectangular HSS: c1 0.20, c2 1.38)
    Private Function BoxCompression(ByVal S As FemSection, ByVal LMajor As Double, ByVal LMinor As Double) As Double
        Dim E As Double = S.E, Fy As Double = S.Fy
        Dim Root As Double = Math.Sqrt(E / Fy)
        Dim KLr As Double = Math.Max(LMajor / S.R33, LMinor / S.R22)
        Dim Fe As Double = Math.PI * Math.PI * E / (KLr * KLr)
        Dim Fcr As Double = If(KLr <= 4.71 * Root, Math.Pow(0.658, Fy / Fe) * Fy, 0.877 * Fe)
        Dim LamR As Double = 1.4 * Root
        Dim Ae As Double = S.A
        For Each W In {True, False}
            Dim lam As Double = BoxWallRatio(S, W)
            If lam <= LamR * Math.Sqrt(Fy / Fcr) Then Continue For
            Dim b As Double = lam * S.FlangeThickness
            Dim Fel As Double = (1.38 * LamR / lam) ^ 2 * Fy
            Dim be As Double = b * (1 - 0.2 * Math.Sqrt(Fel / Fcr)) * Math.Sqrt(Fel / Fcr)
            Ae -= 2 * (b - Math.Max(be, 0)) * S.FlangeThickness
        Next
        Return Fcr * Ae
    End Function

    'F7.1 yielding, F7.2 flange local buckling (Eq. F7-2), F7.3 web local buckling (Eq. F7-5); no lateral-torsional buckling (square and
    'near-square boxes, F7.4 does not govern). Major: flanges = width walls, Z33 / S33; minor: the walls swap, Z22 / S22
    Private Function BoxFlexure(ByVal S As FemSection, ByVal Major As Boolean) As Double
        Dim E As Double = S.E, Fy As Double = S.Fy
        Dim Root As Double = Math.Sqrt(E / Fy)
        Dim Z As Double = If(Major, S.Z33, S.Z22), Se As Double = If(Major, S.S33, S.S22)
        Dim Mp As Double = Fy * Z
        Dim Mn As Double = Mp
        Dim lf As Double = BoxWallRatio(S, Major), lw As Double = BoxWallRatio(S, Not Major)
        If lf > 1.12 * Root Then
            If lf <= 1.4 * Root Then
                Mn = Math.Min(Mn, Mp - (Mp - Fy * Se) * (3.57 * lf / Root - 4.0))
            Else
                Mn = Math.Min(Mn, Fy * Se * 1.4 * Root / lf)
            End If
        End If
        If lw > 2.42 * Root Then
            If lw <= 5.7 * Root Then
                Mn = Math.Min(Mn, Mp - (Mp - Fy * Se) * (0.305 * lw / Root - 0.738))
            Else
                Mn = Math.Min(Mn, Fy * Se * 5.7 * Root / lw)
            End If
        End If
        Return Math.Min(Mn, Mp)
    End Function

    'G4: Vn = 0.6 Fy Aw Cv2, Aw = 2 h t (h: flat width of the walls parallel to the shear), kv = 5 (G2.2); major shear (V2) is carried by the
    'depth walls
    Private Function BoxShear(ByVal S As FemSection, ByVal Major As Boolean) As Double
        Dim E As Double = S.E, Fy As Double = S.Fy, t As Double = S.FlangeThickness
        Dim lam As Double = BoxWallRatio(S, Not Major)
        Dim kv As Double = 5.0
        Dim l1 As Double = 1.1 * Math.Sqrt(kv * E / Fy), l2 As Double = 1.37 * Math.Sqrt(kv * E / Fy)
        Dim Cv2 As Double = If(lam <= l1, 1.0, If(lam <= l2, l1 / lam, 1.51 * kv * E / (lam * lam * Fy)))
        Return 0.6 * Fy * 2 * lam * t * t * Cv2
    End Function

    'PCR-10: AISC 341-22 Table D1.1b for box and rectangular HSS members (both editions of the program: the box option is new). Every wall is a
    'flange for one of the bending axes of a column: case 12 b / t <= 0.55 (highly ductile) / 1.00 (moderately ductile) sqrt(E / Ry Fy); webs
    'case 13 h / t <= 2.45 (1 - 1.04 Ca) / 3.76 (1 - 3.05 Ca) sqrt(E / Ry Fy) for Ca <= 0.113, else 2.26 (1 - 0.38 Ca) / 2.61 (1 - 0.49 Ca),
    'at least 1.56 sqrt(E / Ry Fy); Ca = Pr / (Ry Fy Ag) (LRFD), 0 for tension. Flat widths of BoxWallRatio, design wall thickness.
    Public Function BoxSeismicLimits(ByVal High As Boolean, ByVal Ca As Double, ByRef Flange As Double, ByRef Web As Double) As Boolean
        Dim c As Double = Math.Max(Ca, 0.0)
        Flange = If(High, 0.55, 1.0)
        If c <= 0.113 Then
            Web = If(High, 2.45 * (1 - 1.04 * c), 3.76 * (1 - 3.05 * c))
        Else
            Web = Math.Max(If(High, 2.26 * (1 - 0.38 * c), 2.61 * (1 - 0.49 * c)), 1.56)
        End If
        Return True
    End Function

    Public Function BoxSeismicCompact(ByVal S As FemSection, ByVal High As Boolean, ByVal Ca As Double) As Boolean
        Dim Root As Double = Math.Sqrt(S.E / (RyAisc * S.Fy))
        Dim Fl, Wb As Double
        BoxSeismicLimits(High, Ca, Fl, Wb)
        Dim Lim As Double = Math.Min(Fl, Wb) * Root
        Return BoxWallRatio(S, True) <= Lim AndAlso BoxWallRatio(S, False) <= Lim
    End Function

    '_______________________________________________________________________________________________
    'AISC 341 (Table D1.1) seismic compactness of a rolled I section: flanges bf / 2tf <= 0.32 (High) or 0.40 (Moderate) sqrt(E / Ry Fy);
    'web h / tw limit with Ca = Pu / (phi Py)
    Public Const RyAisc As Double = 1.1
    'Calibration against ETABS (borderline columns are flagged by ETABS at about 1.2-1.25 times the Ca computed from the envelope axial force)
    Public Const CaCalibration As Double = 1.1

    Public Function SeismicCompact(ByVal S As FemSection, ByVal High As Boolean, ByVal CaIn As Double) As Boolean
        Dim Ca As Double = CaIn * CaCalibration
        Dim Root As Double = Math.Sqrt(S.E / (RyAisc * S.Fy))
        If S.Width / (2 * S.FlangeThickness) > If(High, 0.32, 0.4) * Root Then Return False
        Dim H As Double = WebHeight(S)
        Dim Lim As Double
        If Ca <= 0.114 Then
            Lim = If(High, 2.57 * (1 - 1.04 * Ca), 3.96 * (1 - 3.04 * Ca)) * Root
        Else
            Lim = Math.Max(If(High, 0.88 * (2.68 - Ca), 1.29 * (2.12 - Ca)), 1.57) * Root
        End If
        Return H / S.WebThickness <= Lim
    End Function

    'AISC 341 D1.2b: Lb / ry <= 0.095 E / (Ry Fy) (highly ductile) or 0.19 E / (Ry Fy) (moderately ductile) for the beams of the moment frame
    'AISC 341-22 (Aisc341_2022): D1.2b 0.086 (highly ductile), D1.2a(c) Eq. D1-2 0.17 (moderately ductile)
    Public Function LbRyOk(ByVal S As FemSection, ByVal Lb As Double, ByVal High As Boolean) As Boolean
        If Aisc341_2022 Then Return Lb / S.R22 <= If(High, 0.086, 0.17) * S.E / (RyAisc * S.Fy)
        Return Lb / S.R22 <= If(High, 0.095, 0.19) * S.E / (RyAisc * S.Fy)
    End Function

    'Paper 1 (PCR-5c, 5e): True = AISC 341-22 width-to-thickness and bracing limits; False = the limits above (earlier versions). Set by the host.
    Public Aisc341_2022 As Boolean = False

    'AISC 341-22 Table D1.1b: case 7 flanges bf / 2tf <= 0.30 (highly ductile) or 0.38 (moderately ductile) sqrt(E / Ry Fy); case 11 webs of
    'moment frame beams and columns h / tw <= 2.5 (1 - Ca)^2.3 or 5.4 (1 - Ca)^2.3 sqrt(E / Ry Fy), Ca = alpha_s Pr / (Ry Fy Ag), alpha_s = 1.0 (LRFD)
    Public Function Compact2022(ByVal FlangeRatio As Double, ByVal WebRatio As Double, ByVal E As Double, ByVal Fy As Double, ByVal High As Boolean, ByVal Ca As Double) As Boolean
        Dim Root As Double = Math.Sqrt(E / (RyAisc * Fy))
        If FlangeRatio > If(High, 0.3, 0.38) * Root Then Return False
        Dim c As Double = Math.Min(Math.Max(Ca, 0.0), 1.0)
        Return WebRatio <= If(High, 2.5, 5.4) * Math.Pow(1 - c, 2.3) * Root
    End Function

    'Seismic compactness of a member with the required axial strength Pr (compression, kN with the units of S): AISC 341-22 or the earlier limits
    Public Function SeismicCompactPr(ByVal S As FemSection, ByVal High As Boolean, ByVal Pr As Double) As Boolean
        If S.IsBox Then Return BoxSeismicCompact(S, High, Pr / (RyAisc * S.Fy * S.A))
        If Not Aisc341_2022 Then Return SeismicCompact(S, High, Pr / (0.9 * S.Fy * S.A))
        Return Compact2022(S.Width / (2 * S.FlangeThickness), WebHeight(S) / S.WebThickness, S.E, S.Fy, High, Pr / (RyAisc * S.Fy * S.A))
    End Function

End Module
