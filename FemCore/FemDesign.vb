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
    Public Function LbRyOk(ByVal S As FemSection, ByVal Lb As Double, ByVal High As Boolean) As Boolean
        Return Lb / S.R22 <= If(High, 0.095, 0.19) * S.E / (RyAisc * S.Fy)
    End Function

End Module
