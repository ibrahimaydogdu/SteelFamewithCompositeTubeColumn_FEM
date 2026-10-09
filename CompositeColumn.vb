Imports System.Xml.Serialization

'=====================================================================================================
' AISC 360-16 / 360-22 Chapter I composite column design (LRFD)
' References: AISC360_16_Composite_Column_Rules.md, AISC360_22_Composite_Column_Rules.md
' Units: consistent model units (kN, mm -> stresses kN/mm²). All formulas are unit independent
' except the concrete shear term of 360-22 (sqrt(f'c) in ksi, converted with KSI).
' Local axes follow ETABS: axis 2 is along the depth (web of an I shape), M3 = major axis moment,
' V2 = major axis shear.
' Edition differences (filled sections only; encased sections are identical in both editions):
'   360-22: shear with concrete contribution (I4-1), I5-1a/b interaction for noncompact/slender
'           filled sections, H3-6 torsion interaction when Tr > 0.2 Tc.
'=====================================================================================================

'Default 0 = 360-16: backups written before the edition option existed keep their behavior
Public Enum CompositeCode_
    AISC360_16 = 0
    AISC360_22 = 1
End Enum

'Nominal flexural strength of encased sections (AISC I3.3(c)): plastic stress distribution or strain compatibility.
'Strain compatibility agrees with the ETABS 22 composite column design (PMM within about 2 %, 525M model);
'the plastic stress distribution gave a minor-axis Mn about 19 % above ETABS.
Public Enum FlexureMethod_
    PlasticStress = 0
    StrainCompatibility = 1
End Enum

Public Enum CompositeClass_
    Compact = 0
    Noncompact = 1
    Slender = 2
    TooSlender = 3
End Enum

Public Enum BendingAxis_
    Major = 0   'bending about local 3 (M3), fibers vary along local 2 (depth H)
    Minor = 1   'bending about local 2 (M2), fibers vary along local 3 (width B)
End Enum

Public Enum FiberKind_
    Steel = 0
    Concrete = 1
    Rebar = 2
End Enum

Public Structure Fiber_
    Public Pos As Double        'distance from the centroid, perpendicular to the bending axis
    Public Area As Double
    Public Kind As FiberKind_
End Structure

Public Class CompositeMaterial_
    Public Fy As Double         'steel yield stress
    Public Es As Double         'steel modulus
    Public fc As Double         'concrete compressive strength f'c
    Public Ec As Double         'concrete modulus
    Public Fysr As Double       'rebar yield stress
    Public Esr As Double        'rebar modulus
    Public SteelWeight As Double    'weight per unit volume
    Public ConcreteWeight As Double
    Public RebarWeight As Double
End Class

'Section properties for an ETABS "General" section (steel material, concrete transformed to steel)
Public Structure TransformedSection_
    Public T3, T2, Area, As2, As3, J, I22, I33, S22, S33, Z22, Z33, R22, R33 As Double
    Public WeightModifier As Double     'actual weight / weight of the transformed steel section
End Structure

Public MustInherit Class CompositeSection
    Public Const PHI_C As Double = 0.75
    Public Const PHI_B As Double = 0.9
    Public Const PHI_T As Double = 0.9
    Public Const PHI_V As Double = 0.9
    Public Const PHI_TOR As Double = 0.9
    Protected Const STRIPS As Integer = 1000
    Protected Const KSI As Double = 145.0377        'ksi per kN/mm²
    Protected Const ECU As Double = 0.003           'concrete crushing strain (strain compatibility)

    Public Name As String
    Public Mat As CompositeMaterial_
    Public Code As CompositeCode_ = CompositeCode_.AISC360_16
    Public H As Double      'overall dimension along local 2 (depth)
    Public B As Double      'overall dimension along local 3 (width)

    Public MustOverride ReadOnly Property SteelArea As Double
    Public MustOverride ReadOnly Property RebarArea As Double
    Public MustOverride ReadOnly Property ConcreteArea As Double
    Public MustOverride Function SteelI(ByVal axis As BendingAxis_) As Double
    Public MustOverride Function RebarI(ByVal axis As BendingAxis_) As Double
    Public MustOverride Function ConcreteI(ByVal axis As BendingAxis_) As Double
    Public MustOverride Function Fibers(ByVal axis As BendingAxis_) As List(Of Fiber_)
    Public MustOverride Function Classify(ByVal isFlexure As Boolean) As CompositeClass_
    Public MustOverride Function Pno() As Double
    Public MustOverride Function NominalFlexure(ByVal axis As BendingAxis_) As Double
    'phi*Vn; Mr, Vr: required moment and shear of the combination (360-22 filled sections, Kc)
    Public MustOverride Function DesignShear(ByVal axis As BendingAxis_, Optional ByVal Mr As Double = 0, Optional ByVal Vr As Double = 0) As Double
    Protected MustOverride Function StiffnessCoefficient() As Double                    'C1 (encased) / C3 (filled)
    Protected MustOverride Function SteelShearArea(ByVal axis As BendingAxis_) As Double
    Protected MustOverride Function SteelTorsion() As Double
    Protected MustOverride Function ConcreteTorsion() As Double
    'Concrete stress block factor for the plastic stress distribution (0.85 or 0.95 for round filled)
    Protected Overridable ReadOnly Property ConcreteStressFactor As Double
        Get
            Return 0.85
        End Get
    End Property
    'True: moments about both axes are combined by SRSS (round sections)
    Public Overridable ReadOnly Property IsRound As Boolean
        Get
            Return False
        End Get
    End Property

    Public ReadOnly Property GrossArea As Double
        Get
            Return SteelArea + RebarArea + ConcreteArea
        End Get
    End Property

    'I2-12 / I2-14 (AISC 360-16)
    Public Function EIeff(ByVal axis As BendingAxis_) As Double
        Return Mat.Es * SteelI(axis) + Mat.Esr * RebarI(axis) + StiffnessCoefficient() * Mat.Ec * ConcreteI(axis)
    End Function

    'Nominal compressive strength, Lc2 / Lc3: effective lengths for buckling about local 2 / local 3
    Public Function NominalCompression(ByVal Lc2 As Double, ByVal Lc3 As Double) As Double
        Dim P0 As Double = Pno()
        Dim Pe As Double = Math.Min(Math.PI ^ 2 * EIeff(BendingAxis_.Major) / Lc3 ^ 2, Math.PI ^ 2 * EIeff(BendingAxis_.Minor) / Lc2 ^ 2)
        If P0 / Pe <= 2.25 Then Return P0 * 0.658 ^ (P0 / Pe)
        Return 0.877 * Pe
    End Function

    'I2-8 / I2-14: tension (concrete neglected)
    Public Function NominalTension() As Double
        Return Mat.Fy * SteelArea + Mat.Fysr * RebarArea
    End Function

    Public Function DesignFlexure(ByVal axis As BendingAxis_) As Double
        Return PHI_B * NominalFlexure(axis)
    End Function

    '_____________________________________________________________________________________________
    'Plastic stress distribution method (AISC I1.2a), pure bending (P = 0).
    'Compression side: steel Fy, rebar Fysr, concrete factor*f'c; tension side: steel/rebar only.
    Public Function PlasticMoment(ByVal axis As BendingAxis_) As Double
        Dim fb As List(Of Fiber_) = Fibers(axis)
        Dim fcc As Double = ConcreteStressFactor * Mat.fc
        Dim Stress = Function(f As Fiber_, comp As Boolean) As Double
                         Select Case f.Kind
                             Case FiberKind_.Steel : Return If(comp, Mat.Fy, -Mat.Fy)
                             Case FiberKind_.Rebar : Return If(comp, Mat.Fysr, -Mat.Fysr)
                             Case Else : Return If(comp, fcc, 0)
                         End Select
                     End Function
        Dim Force = Function(y0 As Double) As Double
                        Dim Sum As Double = 0
                        For Each f In fb
                            Sum += Stress(f, f.Pos > y0) * f.Area
                        Next
                        Return Sum
                    End Function
        Dim y As Double = Bisect(Force, -0.5 * Extent(axis), 0.5 * Extent(axis))
        Dim M As Double = 0
        For Each f In fb
            M += Stress(f, f.Pos > y) * f.Area * f.Pos
        Next
        Return M
    End Function

    'Strain compatibility method (AISC I1.2b), pure bending: linear strain, eps_cu = 0.003 at the extreme compression
    'fiber, concrete Whitney block 0.85 f'c over beta1 c (ACI 318), steel and bars elastic - perfectly plastic.
    Public Function StrainCompatibilityMoment(ByVal axis As BendingAxis_) As Double
        Dim fb As List(Of Fiber_) = Fibers(axis)
        Dim yt As Double = 0.5 * Extent(axis)
        Dim fcMPa As Double = Mat.fc * 1000
        Dim beta1 As Double = Math.Min(Math.Max(0.85 - 0.05 * (fcMPa - 28) / 7, 0.65), 0.85)
        Dim Stress = Function(f As Fiber_, yNA As Double) As Double
                         Dim c As Double = yt - yNA
                         If f.Kind = FiberKind_.Concrete Then Return If(f.Pos >= yt - beta1 * c, 0.85 * Mat.fc, 0)
                         Dim eps As Double = ECU * (f.Pos - yNA) / c
                         If f.Kind = FiberKind_.Rebar Then Return Math.Max(-Mat.Fysr, Math.Min(Mat.Fysr, Mat.Esr * eps))
                         Return Math.Max(-Mat.Fy, Math.Min(Mat.Fy, Mat.Es * eps))
                     End Function
        'axial force decreases when the neutral axis moves up
        Dim y As Double = Bisect(Function(yNA) fb.Sum(Function(f) Stress(f, yNA) * f.Area), -yt + 0.000001, yt - 0.000001)
        Dim M As Double = 0
        For Each f In fb
            M += Stress(f, y) * f.Area * f.Pos
        Next
        Return M
    End Function

    'Linear elastic moment at first yield (AISC I3.4b(b) M_y / (c) M_cr):
    'steel limited to Fy in tension and SteelCompLimit in compression, concrete to 0.7 f'c.
    Public Function ElasticLimitMoment(ByVal axis As BendingAxis_, ByVal SteelCompLimit As Double) As Double
        Dim fb As List(Of Fiber_) = Fibers(axis)
        Dim Modulus = Function(f As Fiber_) As Double
                          Select Case f.Kind
                              Case FiberKind_.Steel : Return Mat.Es
                              Case FiberKind_.Rebar : Return Mat.Esr
                              Case Else : Return Mat.Ec
                          End Select
                      End Function
        'neutral axis: sum(E*A*(y - y0)) = 0, concrete in tension ignored
        Dim Force = Function(y0 As Double) As Double
                        Dim Sum As Double = 0
                        For Each f In fb
                            If f.Kind = FiberKind_.Concrete AndAlso f.Pos <= y0 Then Continue For
                            Sum += Modulus(f) * f.Area * (f.Pos - y0)
                        Next
                        Return Sum
                    End Function
        Dim y As Double = Bisect(Force, -0.5 * Extent(axis), 0.5 * Extent(axis))
        'curvature at which the first limit is reached
        Dim kappa As Double = Double.MaxValue
        For Each f In fb
            Dim e As Double = f.Pos - y
            If f.Kind = FiberKind_.Steel Then
                If e > 0 Then kappa = Math.Min(kappa, SteelCompLimit / (Mat.Es * e))
                If e < 0 Then kappa = Math.Min(kappa, Mat.Fy / (Mat.Es * -e))
            ElseIf f.Kind = FiberKind_.Concrete AndAlso e > 0 Then
                kappa = Math.Min(kappa, 0.7 * Mat.fc / (Mat.Ec * e))
            End If
        Next
        Dim M As Double = 0
        For Each f In fb
            Dim e As Double = f.Pos - y
            If f.Kind = FiberKind_.Concrete AndAlso e <= 0 Then Continue For
            M += kappa * Modulus(f) * e * f.Area * f.Pos
        Next
        Return M
    End Function

    Protected Function Extent(ByVal axis As BendingAxis_) As Double
        Return If(axis = BendingAxis_.Major, H, B)
    End Function

    'Root of a decreasing function on [a, b]
    Protected Shared Function Bisect(ByVal Fn As Func(Of Double, Double), ByVal a As Double, ByVal b As Double) As Double
        For i = 1 To 100
            Dim m As Double = 0.5 * (a + b)
            If Fn(m) > 0 Then a = m Else b = m
            If b - a < 0.000001 Then Exit For
        Next
        Return 0.5 * (a + b)
    End Function

    'Rectangular torsion constant beta*a*b^3 (a >= b)
    Protected Shared Function RectTorsion(ByVal x As Double, ByVal y As Double) As Double
        Dim a As Double = Math.Max(x, y), b As Double = Math.Min(x, y)
        Return (1.0 / 3.0 - 0.21 * (b / a) * (1 - (b / a) ^ 4 / 12)) * a * b ^ 3
    End Function

    '_____________________________________________________________________________________________
    'Properties of the ETABS General section (material = steel). Flexural stiffness = EIeff (I2-12/14),
    'axial stiffness = sum of elastic axial stiffnesses (I1.5).
    Public Function Transformed() As TransformedSection_
        Dim n As Double = Mat.Ec / Mat.Es
        Dim g As Double = n * (1 + 0.3) / (1 + 0.2)          'Gc/Gs (poisson 0.2 / 0.3)
        Dim T As New TransformedSection_ With {
            .T3 = H, .T2 = B,
            .Area = SteelArea + RebarArea * Mat.Esr / Mat.Es + ConcreteArea * n,
            .As2 = SteelShearArea(BendingAxis_.Major) + g * 5.0 / 6.0 * ConcreteArea,
            .As3 = SteelShearArea(BendingAxis_.Minor) + g * 5.0 / 6.0 * ConcreteArea,
            .J = SteelTorsion() + g * ConcreteTorsion(),
            .I33 = EIeff(BendingAxis_.Major) / Mat.Es,
            .I22 = EIeff(BendingAxis_.Minor) / Mat.Es}
        T.S33 = T.I33 / (0.5 * H)
        T.S22 = T.I22 / (0.5 * B)
        T.Z33 = PlasticMoment(BendingAxis_.Major) / Mat.Fy
        T.Z22 = PlasticMoment(BendingAxis_.Minor) / Mat.Fy
        T.R33 = Math.Sqrt(T.I33 / T.Area)
        T.R22 = Math.Sqrt(T.I22 / T.Area)
        Dim ActualWeight As Double = Mat.SteelWeight * SteelArea + Mat.RebarWeight * RebarArea + Mat.ConcreteWeight * ConcreteArea
        T.WeightModifier = ActualWeight / (Mat.SteelWeight * T.Area)
        Return T
    End Function

    '_____________________________________________________________________________________________
    'Flexure term Mr/Mc of the interaction equations. Round sections: SRSS of the moments.
    Public Function MomentTerm(ByVal Mr33 As Double, ByVal Mc33 As Double, ByVal Mr22 As Double, ByVal Mc22 As Double) As Double
        If IsRound Then Return Math.Sqrt(Mr33 ^ 2 + Mr22 ^ 2) / Math.Min(Mc33, Mc22)
        Return Mr33 / Mc33 + Mr22 / Mc22
    End Function

    'AISC H1-1a / H1-1b (md section 7); 360-22 noncompact/slender filled sections in compression: I5-1a / I5-1b
    Public Function InteractionRatio(ByVal Pr As Double, ByVal Pc As Double, ByVal Mr33 As Double, ByVal Mc33 As Double, ByVal Mr22 As Double, ByVal Mc22 As Double,
                                     Optional ByVal Compression As Boolean = True) As Double
        Dim Mterm As Double = MomentTerm(Mr33, Mc33, Mr22, Mc22)
        Dim axial As Double = Pr / Pc
        Dim cp, cm As Double
        If Compression AndAlso Code = CompositeCode_.AISC360_22 AndAlso BalancePoint(cp, cm) Then
            If axial >= cp Then Return axial + (1 - cp) / cm * Mterm
            Return (1 - cm) / cp * axial + Mterm
        End If
        If axial >= 0.2 Then Return axial + 8.0 / 9.0 * Mterm
        Return axial / 2.0 + Mterm
    End Function

    'Balance point cp, cm of I5-1a/b (Table I5.1); False: the section uses H1.1
    Protected Overridable Function BalancePoint(ByRef cp As Double, ByRef cm As Double) As Boolean
        Return False
    End Function

    'I5-2: csr = (As Fy + Asr Fysr) / (Ac f'c)
    Protected Function StrengthRatio() As Double
        Return (SteelArea * Mat.Fy + RebarArea * Mat.Fysr) / (ConcreteArea * Mat.fc)
    End Function

    'Design torsional strength phi*Tn of the steel tube (H3.1), 0 = torsion not checked. L: member length
    Public Overridable Function DesignTorsion(ByVal L As Double) As Double
        Return 0
    End Function

    '360-22 filled sections (I4-1): Vn = steel + 0.06 Kc Ac sqrt(f'c) [ksi], Kc from the shear span M/(V d)
    Protected Function ConcreteShear(ByVal d As Double, ByVal Mr As Double, ByVal Vr As Double, ByVal KcMax As Double, ByVal IsCompact As Boolean) As Double
        If Code <> CompositeCode_.AISC360_22 Then Return 0
        Dim Kc As Double = 1.0
        If IsCompact AndAlso Math.Abs(Vr) * d > 0 Then
            Dim s As Double = Math.Abs(Mr / (Vr * d))
            If s <= 0.5 Then
                Kc = KcMax
            ElseIf s < 0.7 Then
                Kc = KcMax - (KcMax - 1) * (s - 0.5) / 0.2     'linear, continuous between 0.5 and 0.7
            End If
        End If
        Return 0.06 * Kc * ConcreteArea * Math.Sqrt(Mat.fc * KSI) / KSI
    End Function

    'Detailing limits (I2.1a / I2.2a); returns a ratio (> 1: not satisfied)
    Public Overridable Function DetailingRatio() As Double
        Return 0.01 / (SteelArea / GrossArea)
    End Function

    'Classification of an element: lambda against lambda_p, lambda_r, lambda_max
    Protected Shared Function ClassOf(ByVal lambda As Double, ByVal lp As Double, ByVal lr As Double, ByVal lmax As Double) As CompositeClass_
        If lambda <= lp Then Return CompositeClass_.Compact
        If lambda <= lr Then Return CompositeClass_.Noncompact
        If lambda <= lmax Then Return CompositeClass_.Slender
        Return CompositeClass_.TooSlender
    End Function

    'Web shear strength coefficient Cv1 (G2.1(b), rolled / built-up I webs without tension field), h_t = h/tw
    Protected Function ShearCv1(ByVal h_t As Double, ByVal kv As Double) As Double
        Dim lim As Double = 1.1 * Math.Sqrt(kv * Mat.Es / Mat.Fy)
        Return If(h_t <= lim, 1.0, lim / h_t)
    End Function

    'Web shear buckling coefficient Cv2 (G2.2 with kv: G4 boxes, G6 weak axis), h_t = h/t
    Protected Function ShearCv(ByVal h_t As Double, ByVal kv As Double) As Double
        Dim lim1 As Double = 1.1 * Math.Sqrt(kv * Mat.Es / Mat.Fy)
        Dim lim2 As Double = 1.37 * Math.Sqrt(kv * Mat.Es / Mat.Fy)
        If h_t <= lim1 Then Return 1.0
        If h_t <= lim2 Then Return lim1 / h_t
        Return 1.51 * kv * Mat.Es / (h_t ^ 2 * Mat.Fy)
    End Function
End Class

'=====================================================================================================
' Encased I shape (md section 6): rectangular concrete H x B around a W section, bars at RebarPos.
' Encased sections are always compact.
'=====================================================================================================
Public Class EncasedIShape
    Inherits CompositeSection

    Public Steel As SectionStructures_.STEEL_I_SECTION
    Public BarDiameter As Double
    Public RebarPos As List(Of Double())        '{x (along B), y (along H)} of each bar center

    Public Sub New(ByVal W As SectionStructures_.STEEL_I_SECTION, ByVal Depth As Double, ByVal Width As Double,
                   ByVal BarDia As Double, ByVal Bars As List(Of Double()), ByVal Material As CompositeMaterial_)
        Steel = W : H = Depth : B = Width : BarDiameter = BarDia : RebarPos = Bars : Mat = Material
        Name = W.SectionName
    End Sub

    Private ReadOnly Property BarArea As Double
        Get
            Return Math.PI * BarDiameter ^ 2 / 4
        End Get
    End Property
    Public Overrides ReadOnly Property SteelArea As Double
        Get
            Return Steel.Area
        End Get
    End Property
    Public Overrides ReadOnly Property RebarArea As Double
        Get
            Return RebarPos.Count * BarArea
        End Get
    End Property
    Public Overrides ReadOnly Property ConcreteArea As Double
        Get
            Return B * H - SteelArea - RebarArea
        End Get
    End Property
    Public Overrides Function SteelI(axis As BendingAxis_) As Double
        Return If(axis = BendingAxis_.Major, Steel.Imajor, Steel.Iminor)
    End Function
    Public Overrides Function RebarI(axis As BendingAxis_) As Double
        Dim I As Double = 0
        For Each p In RebarPos
            Dim d As Double = If(axis = BendingAxis_.Major, p(1), p(0))
            I += Math.PI * BarDiameter ^ 4 / 64 + BarArea * d ^ 2
        Next
        Return I
    End Function
    Public Overrides Function ConcreteI(axis As BendingAxis_) As Double
        If axis = BendingAxis_.Major Then Return B * H ^ 3 / 12 - SteelI(axis) - RebarI(axis)
        Return H * B ^ 3 / 12 - SteelI(axis) - RebarI(axis)
    End Function
    Public Overrides Function Classify(isFlexure As Boolean) As CompositeClass_
        Return CompositeClass_.Compact
    End Function
    'I2-4
    Public Overrides Function Pno() As Double
        Return Mat.Fy * SteelArea + Mat.Fysr * RebarArea + 0.85 * Mat.fc * ConcreteArea
    End Function
    'I2-13: C1 = 0.25 + 3 (As + Asr)/Ag <= 0.7
    Protected Overrides Function StiffnessCoefficient() As Double
        Return Math.Min(0.25 + 3 * (SteelArea + RebarArea) / GrossArea, 0.7)
    End Function
    'I3.3(c): plastic stress distribution or strain compatibility on the composite section
    Public FlexureMethod As FlexureMethod_ = FlexureMethod_.PlasticStress
    Private ReadOnly MnCache As New Dictionary(Of BendingAxis_, Double)
    Public Overrides Function NominalFlexure(axis As BendingAxis_) As Double
        Dim Mn As Double
        If Not MnCache.TryGetValue(axis, Mn) Then
            Mn = If(FlexureMethod = FlexureMethod_.StrainCompatibility, StrainCompatibilityMoment(axis), PlasticMoment(axis))
            MnCache(axis) = Mn
        End If
        Return Mn
    End Function
    'I4.1(b)(a): steel section alone, Chapter G (same in 360-16 and 360-22)
    Public Overrides Function DesignShear(axis As BendingAxis_, Optional Mr As Double = 0, Optional Vr As Double = 0) As Double
        If axis = BendingAxis_.Major Then
            Dim h As Double = Steel.Depth - 2 * If(Steel.KDES > 0, Steel.KDES, Steel.FlangeThickness)
            Dim Aw As Double = Steel.Depth * Steel.WebThickness
            If h / Steel.WebThickness <= 2.24 * Math.Sqrt(Mat.Es / Mat.Fy) Then Return 1.0 * 0.6 * Mat.Fy * Aw     'G2.1(a)
            Return PHI_V * 0.6 * Mat.Fy * Aw * ShearCv1(h / Steel.WebThickness, 5.34)        'G2.1(b)
        End If
        'G6: two flanges, kv = 1.2, h/t = bf / (2 tf)
        Dim Af As Double = 2 * Steel.FlangeLength * Steel.FlangeThickness
        Return PHI_V * 0.6 * Mat.Fy * Af * ShearCv(Steel.FlangeLength / (2 * Steel.FlangeThickness), 1.2)
    End Function
    'I2.1a: As >= 1% Ag, rho_sr >= 0.4%, at least 4 bars
    Public Overrides Function DetailingRatio() As Double
        Dim r As Double = Math.Max(0.01 / (SteelArea / GrossArea), 0.004 / (RebarArea / GrossArea))
        If RebarPos.Count < 4 Then r = Math.Max(r, 2)
        Return r
    End Function
    Protected Overrides Function SteelShearArea(axis As BendingAxis_) As Double
        Return If(axis = BendingAxis_.Major, Steel.ShearAreaMajor, Steel.ShearAreaMinor)
    End Function
    Protected Overrides Function SteelTorsion() As Double
        Return Steel.TorsionalConstant
    End Function
    Protected Overrides Function ConcreteTorsion() As Double
        Return RectTorsion(B, H)
    End Function

    'Strips along the bending direction; steel as plates (scaled to the library area), concrete net of steel and bars
    Private ReadOnly FiberCache As New Dictionary(Of BendingAxis_, List(Of Fiber_))
    Public Overrides Function Fibers(axis As BendingAxis_) As List(Of Fiber_)
        If FiberCache.ContainsKey(axis) Then Return FiberCache(axis)
        Dim d As Double = Steel.Depth, bf As Double = Steel.FlangeLength, tf As Double = Steel.FlangeThickness, tw As Double = Steel.WebThickness
        Dim L As Double = Extent(axis)                      'length along the bending direction
        Dim Wd As Double = If(axis = BendingAxis_.Major, B, H)  'width of the strips
        Dim dy As Double = L / STRIPS
        Dim PlateArea As Double = 2 * bf * tf + (d - 2 * tf) * tw
        Dim scale As Double = SteelArea / PlateArea
        Dim SteelWidth = Function(y As Double) As Double
                             Dim a As Double = Math.Abs(y)
                             If axis = BendingAxis_.Major Then
                                 If a > d / 2 Then Return 0
                                 If a > d / 2 - tf Then Return bf
                                 Return tw
                             End If
                             If a > bf / 2 Then Return 0
                             If a > tw / 2 Then Return 2 * tf
                             Return d
                         End Function
        Dim conc(STRIPS - 1) As Double
        Dim list As New List(Of Fiber_)
        For i = 0 To STRIPS - 1
            Dim y As Double = -L / 2 + (i + 0.5) * dy
            'average steel width in the strip (sub-sampled so thin plates are captured exactly enough)
            Dim sw As Double = 0
            For k = 0 To 9
                sw += SteelWidth(-L / 2 + i * dy + (k + 0.5) * dy / 10)
            Next
            sw /= 10
            If sw > 0 Then list.Add(New Fiber_ With {.Pos = y, .Area = sw * dy * scale, .Kind = FiberKind_.Steel})
            conc(i) = (Wd - sw) * dy
        Next
        For Each p In RebarPos
            Dim y As Double = If(axis = BendingAxis_.Major, p(1), p(0))
            list.Add(New Fiber_ With {.Pos = y, .Area = BarArea, .Kind = FiberKind_.Rebar})
            'concrete displaced by the bar: from the strips the bar diameter covers (one strip can be smaller than the bar)
            Dim i0 As Integer = Math.Min(Math.Max(CInt(Math.Floor((y - BarDiameter / 2 + L / 2) / dy)), 0), STRIPS - 1)
            Dim i1 As Integer = Math.Min(Math.Max(CInt(Math.Floor((y + BarDiameter / 2 + L / 2) / dy)), 0), STRIPS - 1)
            For i = i0 To i1
                conc(i) -= BarArea / (i1 - i0 + 1)
            Next
        Next
        For i = 0 To STRIPS - 1
            If conc(i) > 0 Then list.Add(New Fiber_ With {.Pos = -L / 2 + (i + 0.5) * dy, .Area = conc(i), .Kind = FiberKind_.Concrete})
        Next
        FiberCache(axis) = list
        Return list
    End Function
End Class

'=====================================================================================================
' Filled rectangular box (md 3.1, 5): outer H x B, wall thickness t (corner radii neglected).
'=====================================================================================================
Public Class FilledBox
    Inherits CompositeSection
    Public t As Double

    Public Sub New(ByVal Depth As Double, ByVal Width As Double, ByVal Thickness As Double, ByVal Material As CompositeMaterial_)
        H = Depth : B = Width : t = Thickness : Mat = Material
        Name = "BOX" & H & "x" & B & "x" & t
    End Sub

    Public Overrides ReadOnly Property SteelArea As Double
        Get
            Return B * H - (B - 2 * t) * (H - 2 * t)
        End Get
    End Property
    Public Overrides ReadOnly Property RebarArea As Double
        Get
            Return 0
        End Get
    End Property
    Public Overrides ReadOnly Property ConcreteArea As Double
        Get
            Return (B - 2 * t) * (H - 2 * t)
        End Get
    End Property
    Public Overrides Function SteelI(axis As BendingAxis_) As Double
        If axis = BendingAxis_.Major Then Return (B * H ^ 3 - (B - 2 * t) * (H - 2 * t) ^ 3) / 12
        Return (H * B ^ 3 - (H - 2 * t) * (B - 2 * t) ^ 3) / 12
    End Function
    Public Overrides Function RebarI(axis As BendingAxis_) As Double
        Return 0
    End Function
    Public Overrides Function ConcreteI(axis As BendingAxis_) As Double
        If axis = BendingAxis_.Major Then Return (B - 2 * t) * (H - 2 * t) ^ 3 / 12
        Return (H - 2 * t) * (B - 2 * t) ^ 3 / 12
    End Function

    'Width-to-thickness of the walls, b = B - 3t (B4.1b(d))
    Private Function LambdaAlong(ByVal Dimension As Double) As Double
        Return (Dimension - 3 * t) / t
    End Function
    Private ReadOnly Property Root As Double
        Get
            Return Math.Sqrt(Mat.Es / Mat.Fy)
        End Get
    End Property

    'Table I1.1a (compression) / I1.1b (flexure)
    Public Overrides Function Classify(isFlexure As Boolean) As CompositeClass_
        If Not isFlexure Then Return ClassOf(Math.Max(LambdaAlong(B), LambdaAlong(H)), 2.26 * Root, 3.0 * Root, 5.0 * Root)
        Return FlexureClass(BendingAxis_.Major)
    End Function
    Private Function FlexureClass(ByVal axis As BendingAxis_) As CompositeClass_
        Dim lf As Double = LambdaAlong(If(axis = BendingAxis_.Major, B, H))  'flange: wall parallel to the bending axis
        Dim lw As Double = LambdaAlong(If(axis = BendingAxis_.Major, H, B))
        Dim cf = ClassOf(lf, 2.26 * Root, 3.0 * Root, 5.0 * Root)
        Dim cw = ClassOf(lw, 3.0 * Root, 5.7 * Root, 5.7 * Root)
        Return CType(Math.Max(CInt(cf), CInt(cw)), CompositeClass_)
    End Function

    Private ReadOnly Property ConcreteTerm As Double
        Get
            Return Mat.fc * (ConcreteArea + RebarArea * Mat.Es / Mat.Ec)
        End Get
    End Property
    'I2.2b, C2 = 0.85
    Public Overrides Function Pno() As Double
        Dim Pp As Double = Mat.Fy * SteelArea + 0.85 * ConcreteTerm
        Dim Py As Double = Mat.Fy * SteelArea + 0.7 * ConcreteTerm
        Dim lambda As Double = Math.Max(LambdaAlong(B), LambdaAlong(H))
        Dim lp As Double = 2.26 * Root, lr As Double = 3.0 * Root
        Select Case Classify(False)
            Case CompositeClass_.Compact : Return Pp
            Case CompositeClass_.Noncompact : Return Pp - (Pp - Py) / (lr - lp) ^ 2 * (lambda - lp) ^ 2
            Case Else : Return Fcr() * SteelArea + 0.7 * ConcreteTerm
        End Select
    End Function
    'I2-10
    Private Function Fcr() As Double
        Return 9 * Mat.Es / Math.Max(LambdaAlong(B), LambdaAlong(H)) ^ 2
    End Function
    'I2-15: C3 = 0.45 + 3 (As + Asr)/Ag <= 0.9
    Protected Overrides Function StiffnessCoefficient() As Double
        Return Math.Min(0.45 + 3 * (SteelArea + RebarArea) / GrossArea, 0.9)
    End Function
    'I3.4b
    Public Overrides Function NominalFlexure(axis As BendingAxis_) As Double
        Dim Mp As Double = PlasticMoment(axis)
        Select Case FlexureClass(axis)
            Case CompositeClass_.Compact : Return Mp
            Case CompositeClass_.Noncompact
                Dim lf As Double = LambdaAlong(If(axis = BendingAxis_.Major, B, H))
                Dim lw As Double = LambdaAlong(If(axis = BendingAxis_.Major, H, B))
                Dim ratio As Double = Math.Max((lf - 2.26 * Root) / (0.74 * Root), (lw - 3.0 * Root) / (2.7 * Root))
                Dim My As Double = ElasticLimitMoment(axis, Mat.Fy)
                Return Mp - (Mp - My) * Math.Min(Math.Max(ratio, 0), 1)
            Case CompositeClass_.Slender
                Return ElasticLimitMoment(axis, Math.Min(Fcr(), Mat.Fy))
            Case Else
                Return 0
        End Select
    End Function
    'Steel: G4, Aw = 2 h t, h = H - 3t, kv = 5 (360-16 I4.1(a)). 360-22: + concrete term (I4-1), Kc <= 10
    Public Overrides Function DesignShear(axis As BendingAxis_, Optional Mr As Double = 0, Optional Vr As Double = 0) As Double
        Dim d As Double = If(axis = BendingAxis_.Major, H, B)
        Dim hw As Double = d - 3 * t
        Dim Vs As Double = 0.6 * Mat.Fy * (2 * hw * t) * ShearCv(hw / t, 5.0)
        Return PHI_V * (Vs + ConcreteShear(d, Mr, Vr, 10.0, FlexureClass(axis) = CompositeClass_.Compact))
    End Function
    'Table I5.1, rectangular: cp = 0.17 csr^-0.4, cm = 1.06 csr^-0.11 >= 1 (csr >= 0.5) / 0.90 csr^-0.36 <= 1.67
    Protected Overrides Function BalancePoint(ByRef cp As Double, ByRef cm As Double) As Boolean
        If Classify(False) = CompositeClass_.Compact AndAlso FlexureClass(BendingAxis_.Major) = CompositeClass_.Compact AndAlso
           FlexureClass(BendingAxis_.Minor) = CompositeClass_.Compact Then Return False
        Dim csr As Double = StrengthRatio()
        cp = 0.17 * csr ^ -0.4
        cm = If(csr >= 0.5, Math.Max(1.06 * csr ^ -0.11, 1.0), Math.Min(0.9 * csr ^ -0.36, 1.67))
        Return True
    End Function
    'H3.1(b) rectangular HSS: C = 2(B-t)(H-t)t - 4.5(4-pi)t³, h = longer flat width
    Public Overrides Function DesignTorsion(L As Double) As Double
        If Code <> CompositeCode_.AISC360_22 Then Return 0
        Dim C As Double = 2 * (B - t) * (H - t) * t - 4.5 * (4 - Math.PI) * t ^ 3
        Dim h_t As Double = (Math.Max(H, B) - 3 * t) / t
        Dim Fcr As Double
        If h_t <= 2.45 * Root Then
            Fcr = 0.6 * Mat.Fy
        ElseIf h_t <= 3.07 * Root Then
            Fcr = 0.6 * Mat.Fy * 2.45 * Root / h_t
        Else
            Fcr = 0.458 * Math.PI ^ 2 * Mat.Es / h_t ^ 2
        End If
        Return PHI_TOR * Fcr * C
    End Function
    Protected Overrides Function SteelShearArea(axis As BendingAxis_) As Double
        Return 2 * t * If(axis = BendingAxis_.Major, H, B)
    End Function
    Protected Overrides Function SteelTorsion() As Double
        Dim Ap As Double = (B - t) * (H - t)
        Return 4 * Ap ^ 2 * t / (2 * (B - t + H - t))
    End Function
    Protected Overrides Function ConcreteTorsion() As Double
        Return RectTorsion(B - 2 * t, H - 2 * t)
    End Function
    Public Overrides Function Fibers(axis As BendingAxis_) As List(Of Fiber_)
        Dim L As Double = Extent(axis)
        Dim Wd As Double = If(axis = BendingAxis_.Major, B, H)
        Dim dy As Double = L / STRIPS
        Dim list As New List(Of Fiber_)
        For i = 0 To STRIPS - 1
            Dim y As Double = -L / 2 + (i + 0.5) * dy
            Dim inner As Double = If(Math.Abs(y) < L / 2 - t, Wd - 2 * t, 0)
            list.Add(New Fiber_ With {.Pos = y, .Area = (Wd - inner) * dy, .Kind = FiberKind_.Steel})
            If inner > 0 Then list.Add(New Fiber_ With {.Pos = y, .Area = inner * dy, .Kind = FiberKind_.Concrete})
        Next
        Return list
    End Function
End Class

'=====================================================================================================
' Filled round pipe (md 3.2, 5): outer diameter D, wall thickness t.
'=====================================================================================================
Public Class FilledPipe
    Inherits CompositeSection
    Public D As Double
    Public t As Double

    Public Sub New(ByVal Diameter As Double, ByVal Thickness As Double, ByVal Material As CompositeMaterial_)
        D = Diameter : t = Thickness : H = D : B = D : Mat = Material
        Name = "PIPE" & D & "x" & t
    End Sub

    Public Overrides ReadOnly Property IsRound As Boolean
        Get
            Return True
        End Get
    End Property
    Protected Overrides ReadOnly Property ConcreteStressFactor As Double
        Get
            Return 0.95         'C2 = 0.95 for round sections (I2-9, I3.4b)
        End Get
    End Property
    Public Overrides ReadOnly Property SteelArea As Double
        Get
            Return Math.PI / 4 * (D ^ 2 - (D - 2 * t) ^ 2)
        End Get
    End Property
    Public Overrides ReadOnly Property RebarArea As Double
        Get
            Return 0
        End Get
    End Property
    Public Overrides ReadOnly Property ConcreteArea As Double
        Get
            Return Math.PI / 4 * (D - 2 * t) ^ 2
        End Get
    End Property
    Public Overrides Function SteelI(axis As BendingAxis_) As Double
        Return Math.PI / 64 * (D ^ 4 - (D - 2 * t) ^ 4)
    End Function
    Public Overrides Function RebarI(axis As BendingAxis_) As Double
        Return 0
    End Function
    Public Overrides Function ConcreteI(axis As BendingAxis_) As Double
        Return Math.PI / 64 * (D - 2 * t) ^ 4
    End Function
    Private ReadOnly Property Lambda As Double
        Get
            Return D / t
        End Get
    End Property
    Public Overrides Function Classify(isFlexure As Boolean) As CompositeClass_
        Dim r As Double = Mat.Es / Mat.Fy
        If isFlexure Then Return ClassOf(Lambda, 0.09 * r, 0.31 * r, 0.31 * r)
        Return ClassOf(Lambda, 0.15 * r, 0.19 * r, 0.31 * r)
    End Function
    'I2.2b, C2 = 0.95
    Public Overrides Function Pno() As Double
        Dim r As Double = Mat.Es / Mat.Fy
        Dim Ct As Double = Mat.fc * ConcreteArea
        Dim Pp As Double = Mat.Fy * SteelArea + 0.95 * Ct
        Dim Py As Double = Mat.Fy * SteelArea + 0.7 * Ct
        Select Case Classify(False)
            Case CompositeClass_.Compact : Return Pp
            Case CompositeClass_.Noncompact : Return Pp - (Pp - Py) / (0.04 * r) ^ 2 * (Lambda - 0.15 * r) ^ 2
            Case Else : Return Fcr() * SteelArea + 0.7 * Ct
        End Select
    End Function
    'I2-11
    Private Function Fcr() As Double
        Return 0.72 * Mat.Fy / (Lambda * Mat.Fy / Mat.Es) ^ 0.2
    End Function
    Protected Overrides Function StiffnessCoefficient() As Double
        Return Math.Min(0.45 + 3 * SteelArea / GrossArea, 0.9)
    End Function
    Public Overrides Function NominalFlexure(axis As BendingAxis_) As Double
        Dim r As Double = Mat.Es / Mat.Fy
        Dim Mp As Double = PlasticMoment(axis)
        Select Case Classify(True)
            Case CompositeClass_.Compact : Return Mp
            Case CompositeClass_.Noncompact
                Return Mp - (Mp - ElasticLimitMoment(axis, Mat.Fy)) * (Lambda - 0.09 * r) / (0.22 * r)
            Case Else
                Return 0    'lambda_r = lambda_max: slender pipes are not permitted in flexure
        End Select
    End Function
    'G5 (Lv term neglected): Fcr = 0.78 E /(D/t)^1.5 <= 0.6 Fy, Vn = Fcr Ag / 2 (360-16 I4.1(a))
    '360-22 (md 5.3): Vn = Fcr Av + 0.06 Kc Ac sqrt(f'c), Av = 2 As / pi, Kc <= 9
    Public Overrides Function DesignShear(axis As BendingAxis_, Optional Mr As Double = 0, Optional Vr As Double = 0) As Double
        Dim Fcrv As Double = Math.Min(0.78 * Mat.Es / Lambda ^ 1.5, 0.6 * Mat.Fy)
        If Code <> CompositeCode_.AISC360_22 Then Return PHI_V * Fcrv * SteelArea / 2
        Return PHI_V * (Fcrv * 2 * SteelArea / Math.PI + ConcreteShear(D, Mr, Vr, 9.0, Classify(True) = CompositeClass_.Compact))
    End Function
    'Table I5.1, round: cp = 0.27 csr^-0.4, cm = 1.10 csr^-0.08 >= 1 (csr >= 0.5) / 0.95 csr^-0.32 <= 1.67
    Protected Overrides Function BalancePoint(ByRef cp As Double, ByRef cm As Double) As Boolean
        If Classify(False) = CompositeClass_.Compact AndAlso Classify(True) = CompositeClass_.Compact Then Return False
        Dim csr As Double = StrengthRatio()
        cp = 0.27 * csr ^ -0.4
        cm = If(csr >= 0.5, Math.Max(1.1 * csr ^ -0.08, 1.0), Math.Min(0.95 * csr ^ -0.32, 1.67))
        Return True
    End Function
    'H3.1(a) round HSS: C = pi (D-t)² t / 2, Fcr = max(H3-2a, H3-2b) <= 0.6 Fy
    Public Overrides Function DesignTorsion(L As Double) As Double
        If Code <> CompositeCode_.AISC360_22 Then Return 0
        Dim C As Double = Math.PI * (D - t) ^ 2 * t / 2
        Dim Fcr As Double = Math.Max(1.23 * Mat.Es / (Math.Sqrt(L / D) * Lambda ^ 1.25), 0.6 * Mat.Es / Lambda ^ 1.5)
        Return PHI_TOR * Math.Min(Fcr, 0.6 * Mat.Fy) * C
    End Function
    Protected Overrides Function SteelShearArea(axis As BendingAxis_) As Double
        Return SteelArea / 2
    End Function
    Protected Overrides Function SteelTorsion() As Double
        Return 2 * SteelI(BendingAxis_.Major)
    End Function
    Protected Overrides Function ConcreteTorsion() As Double
        Return 2 * ConcreteI(BendingAxis_.Major)
    End Function
    Public Overrides Function Fibers(axis As BendingAxis_) As List(Of Fiber_)
        Dim R As Double = D / 2, Ri As Double = D / 2 - t
        Dim dy As Double = D / STRIPS
        Dim Chord = Function(rad As Double, y As Double) If(Math.Abs(y) < rad, 2 * Math.Sqrt(rad ^ 2 - y ^ 2), 0)
        Dim list As New List(Of Fiber_)
        For i = 0 To STRIPS - 1
            Dim y As Double = -R + (i + 0.5) * dy
            Dim ci As Double = Chord(Ri, y)
            list.Add(New Fiber_ With {.Pos = y, .Area = (Chord(R, y) - ci) * dy, .Kind = FiberKind_.Steel})
            If ci > 0 Then list.Add(New Fiber_ With {.Pos = y, .Area = ci * dy, .Kind = FiberKind_.Concrete})
        Next
        Return list
    End Function
End Class

'=====================================================================================================
' Member check: second-order amplification (md 4) and interaction (md 7)
'=====================================================================================================
Public Class CompositeMemberCheck
    Public Section As CompositeSection
    Public L As Double
    Public K22 As Double = 1.0      'effective length factor, buckling about local 2 (minor)
    Public K33 As Double = 1.0      'effective length factor, buckling about local 3 (major)
    Public B2 As Double = 1.0       'sway amplifier (1.0 if the analysis includes P-Delta)

    Private PcComp, PcTens, Mc33, Mc22, Tc, Pe1_33, Pe1_22 As Double

    Public Sub New(ByVal Sec As CompositeSection, ByVal Length As Double, ByVal K2 As Double, ByVal K3 As Double, ByVal B2factor As Double)
        Section = Sec : L = Length : K22 = K2 : K33 = K3 : B2 = B2factor
        PcComp = CompositeSection.PHI_C * Sec.NominalCompression(K22 * L, K33 * L)
        PcTens = CompositeSection.PHI_T * Sec.NominalTension()
        Mc33 = Sec.DesignFlexure(BendingAxis_.Major)
        Mc22 = Sec.DesignFlexure(BendingAxis_.Minor)
        Tc = Sec.DesignTorsion(L)
        'I1.5: flexural stiffness 0.64 EIeff for stability (Appendix 8, EI*)
        Pe1_33 = Math.PI ^ 2 * 0.64 * Sec.EIeff(BendingAxis_.Major) / L ^ 2
        Pe1_22 = Math.PI ^ 2 * 0.64 * Sec.EIeff(BendingAxis_.Minor) / L ^ 2
    End Sub

    'Cm with internal end moments Mi, Mj (same sign = single curvature): Cm = 0.6 - 0.4 M1/M2, M1/M2 > 0 reverse curvature
    Public Shared Function Cm(ByVal Mi As Double, ByVal Mj As Double) As Double
        Dim Ml As Double = If(Math.Abs(Mi) >= Math.Abs(Mj), Mi, Mj)
        Dim Ms As Double = If(Math.Abs(Mi) >= Math.Abs(Mj), Mj, Mi)
        If Math.Abs(Ml) < 0.000000001 Then Return 0.6
        Return 0.6 + 0.4 * (Ms / Ml)
    End Function

    Public Shared Function B1(ByVal Cm_ As Double, ByVal Pr As Double, ByVal Pe1 As Double) As Double
        Dim den As Double = 1 - Pr / Pe1
        If den <= 0.01 Then Return 100
        Return Math.Max(Cm_ / den, 1.0)
    End Function

    'P: axial (compression negative, ETABS), M3/M2 at the stations i..j, V2/V3/T max absolute shears / torsion
    'Returns D/C = max(PMM, shear) for one load combination
    Public Function Ratio(ByVal P As Double, ByVal M3() As Double, ByVal M2() As Double, ByVal V2 As Double, ByVal V3 As Double, ByRef PMM As Double, ByRef Shear As Double,
                          Optional ByVal T As Double = 0) As Double
        Dim Pr As Double = Math.Abs(P)
        Dim M33 As Double = M3.Max(Function(m) Math.Abs(m))
        Dim M22 As Double = M2.Max(Function(m) Math.Abs(m))
        Dim M33max As Double = M33, M22max As Double = M22
        Dim Pc As Double
        If P < 0 Then
            Pc = PcComp
            M33max *= B1(Cm(M3.First(), M3.Last()), Pr, Pe1_33) * B2
            M22max *= B1(Cm(M2.First(), M2.Last()), Pr, Pe1_22) * B2
        Else
            Pc = PcTens
        End If
        PMM = Section.InteractionRatio(Pr, Pc, M33max, Mc33, M22max, Mc22, P < 0)
        Dim v2r As Double = Math.Abs(V2) / Section.DesignShear(BendingAxis_.Major, M33, V2)
        Dim v3r As Double = Math.Abs(V3) / Section.DesignShear(BendingAxis_.Minor, M22, V3)
        Shear = Math.Max(v2r, v3r)
        'H3-6 (md 7.4): significant torsion, Tr > 0.2 Tc
        If Tc > 0 AndAlso Math.Abs(T) > 0.2 * Tc Then
            Dim Tor As Double = Pr / Pc + Section.MomentTerm(M33max, Mc33, M22max, Mc22) + (v2r + v3r + Math.Abs(T) / Tc) ^ 2
            PMM = Math.Max(PMM, Tor)
        End If
        Return Math.Max(PMM, Shear)
    End Function
End Class

'=====================================================================================================
' Settings of the encased columns (EncasedSections.xml). Each W section of the library produces one
' encased section: concrete H x B = W + 2 * ConcreteCover (rounded), bars per face as needed.
'=====================================================================================================
<XmlRoot("EncasedSettings")>
Public Class EncasedSettings_
    Public ConcreteMaterial As String = "4000Psi"
    Public RebarMaterial As String = "A615Gr60"
    Public ConcreteCover As Double = 75             'from steel flange tip / flange face to concrete face [mm]
    Public RebarCover As Double = 50                'from concrete face to bar center [mm]
    Public RebarDiameter As Double = 20             '[mm]
    Public TieDiameter As Double = 10               '[mm] ties of the ETABS encased section (clear cover = RebarCover - tie - bar/2)
    Public TieSpacing As Double = 150               '[mm]
    Public FlexureMethod As FlexureMethod_ = FlexureMethod_.StrainCompatibility    'Mn of the encased sections
    Public MinBarsPerFace As Integer = 2            '2 = corner bars only
    Public MaxBarsPerFace As Integer = 6
    Public DimensionRounding As Double = 50         '[mm]
    Public MinDimension As Double = 300             '[mm]
    Public K22 As Double = 1.0
    Public K33 As Double = 1.0
    Public B2 As Double = 1.0
    Public SectionPrefix As String = "EC_"
    'Relative unit costs of the objective function (composite mode)
    'Default relative unit costs from assumed unit prices (steel = 1): fabricated and erected structural steel 2.0 $/kg
    '(204 $/kN), reinforcement 1.0 $/kg (102 $/kN), placed concrete C30 120 $/m³, column formwork 30 $/m²
    Public SteelUnitCost As Double = 1.0            'per kN of structural steel
    Public RebarUnitCost As Double = 0.5            'per kN of reinforcement
    Public ConcreteUnitCost As Double = 0.6         'per m³ of concrete
    Public FormworkUnitCost As Double = 0.15        'per m² of formwork (column perimeter)

    Public Shared Function DefaultPath() As String
        Return IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "EncasedSections.xml")
    End Function

    'Settings of the file next to the program, defaults if it is missing or unreadable (form defaults)
    Public Shared Function LoadOrDefault() As EncasedSettings_
        Try
            If IO.File.Exists(DefaultPath()) Then Return Load(DefaultPath())
        Catch
        End Try
        Return New EncasedSettings_()
    End Function

    Public Shared Function Load(ByVal FilePath As String) As EncasedSettings_
        Dim serializer As New XmlSerializer(GetType(EncasedSettings_))
        Using reader As New IO.StreamReader(FilePath)
            Return CType(serializer.Deserialize(reader), EncasedSettings_)
        End Using
    End Function

    Private Function RoundUp(ByVal x As Double) As Double
        If DimensionRounding <= 0 Then Return x
        Return Math.Ceiling(x / DimensionRounding - 0.000001) * DimensionRounding
    End Function

    'Encased section for a W section: dimensions from the covers, bars increased until rho_sr >= 0.004
    Public Function Build(ByVal W As SectionStructures_.STEEL_I_SECTION, ByVal Mat As CompositeMaterial_) As EncasedIShape
        Dim H As Double = Math.Max(RoundUp(W.Depth + 2 * ConcreteCover), MinDimension)
        Dim B As Double = Math.Max(RoundUp(W.FlangeLength + 2 * ConcreteCover), MinDimension)
        Dim Sec As EncasedIShape = Nothing
        'at least 2 bars per face (corners); a MaxBarsPerFace below 2 would leave Sec = Nothing
        For n = Math.Max(MinBarsPerFace, 2) To Math.Max(Math.Max(MaxBarsPerFace, MinBarsPerFace), 2)
            Sec = New EncasedIShape(W, H, B, RebarDiameter, BarLayout(H, B, n), Mat)
            If Sec.RebarArea / Sec.GrossArea >= 0.004 Then Exit For
        Next
        Sec.FlexureMethod = FlexureMethod
        Return Sec
    End Function

    'n bars per face (corners included), perimeter layout
    Private Function BarLayout(ByVal H As Double, ByVal B As Double, ByVal n As Integer) As List(Of Double())
        Dim x0 As Double = B / 2 - RebarCover, y0 As Double = H / 2 - RebarCover
        Dim bars As New List(Of Double())
        For i = 0 To n - 1
            Dim x As Double = -x0 + 2 * x0 * i / (n - 1)
            bars.Add(New Double() {x, y0})
            bars.Add(New Double() {x, -y0})
        Next
        For i = 1 To n - 2
            Dim y As Double = -y0 + 2 * y0 * i / (n - 1)
            bars.Add(New Double() {x0, y})
            bars.Add(New Double() {-x0, y})
        Next
        Return bars
    End Function
End Class
