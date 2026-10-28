Imports System.Collections.Generic

'Structural model of the own FEM solver (no ETABS dependency). Units: kN, m (the reader converts). Local axes follow ETABS:
'1 = member axis (I -> J), 2 and 3 = transverse axes (row-wise direction cosines in Axes).
'DOF order of a node: U1, U2, U3 (global X, Y, Z translations), R1, R2, R3 (rotations about X, Y, Z).
Public Class FemNode
    Public Name As String
    Public X, Y, Z As Double
    Public Restraint(5) As Boolean
    Public Diaphragm As Integer = -1            'index in FemModel.Diaphragms, -1 = none
    Public AddedMass() As Double                'additional translational masses (X, Y, Z) assigned to the joint
End Class

Public Class FemSection
    Public Name As String
    Public MaterialName As String
    Public E, G As Double                       'kN/m2
    Public A, As2, As3, J, I22, I33 As Double   'm2, m4 (As2 / As3 = 0: no shear deformation)
    Public UnitWeight As Double                 'kN/m3 (self weight)
    Public UnitMass As Double                   'kN s2/m4 (mass density)
    Public Depth, Width, FlangeThickness, WebThickness As Double    'm (I sections; design checks)
    Public Fy, Fu As Double                     'kN/m2
    Public S22, S33, Z22, Z33, R22, R33 As Double       'elastic and plastic moduli, radii of gyration
    Public NoDesign As Boolean                  'not designed by the steel design (composite column: own check from the station forces)
    Public DamAxial As Double = 1.0             'NoDesign sections: Direct Analysis Method factors of EA and EI (composite columns, AISC 360-22 I1.5: 0.8 / 0.64);
    Public DamFlexural As Double = 1.0          '1.0 = no reduction (the steel factors of the model do not apply to them)
    Public Mpcc As Func(Of Double, Integer, Double)  'composite column: plastic moment at the axial compression (kN, plane 0 major / 1 minor) [kN m] (AISC 341-22 G3.4a)
    Public Pyc As Double                        'composite column: axial yield strength [kN] (G3.4a exception Prc < 0.1 Pyc)
    Public CompositeCheck As Func(Of Double, Double, Double, Double, Double)  'composite column: interaction ratio for (Pr compression kN, Mx, My kN m, length m) (PCR-5g corner check)
    Public Fillet As Double                     'fillet radius (k = tf + r)
    'PCR-10: unfilled box (square / rectangular HSS or built-up box): Depth = H (walls parallel to axis 2), Width = B, FlangeThickness =
    'WebThickness = wall thickness; flat width of a wall = outer dimension - BoxCornerT x t (HSS 3, built-up 2; AISC 360-22 B4.1b(d))
    Public IsBox As Boolean
    Public BoxCornerT As Double = 3.0
    Public Modifier() As Double = {1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0}      'property modifiers: area, As2, As3, torsion, I22, I33, mass, weight
    Public ReadOnly Property WeightPerLength As Double
        Get
            Return UnitWeight * A
        End Get
    End Property
End Class

Public Class FemElement
    Public Name As String
    Public NI, NJ As Integer                    'node indices (FemModel.Nodes)
    Public Section As FemSection
    Public Axes(2, 2) As Double                 'Axes(k, c): component c (X, Y, Z) of local axis k+1
    Public ReleaseI(5), ReleaseJ(5) As Boolean  'local DOF released at the I / J end (U1, U2, U3, R1, R2, R3)
    Public Length As Double                     'joint to joint length
    Public OffsetI, OffsetJ As Double           'end length offsets
    Public RigidZone As Double                  'rigid zone factor (fraction of the offsets that is rigid)
    Public Modifier() As Double = {1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0}      'object property modifiers (same order as the section modifiers)
    Public UnbracedMajor, UnbracedMinor, UnbracedLTB As Double      'design unbraced lengths [m] (0: clear length)
    Public Ecc2, Ecc3 As Double                 'joint -> centroid offset in local axes 2 and 3 (cardinal point), rigid
    Public Group As String
    Public Story As Integer
End Class

Public Class FemDistLoad
    Public Element As Integer
    Public Dir As Integer                       'ETABS direction code: 1..3 local, 4..6 global X Y Z, 10 gravity
    Public IsGlobal As Boolean
    Public Relative As Boolean
    Public D1, D2, V1, V2 As Double
End Class

Public Class FemPointLoadOnElement
    Public Element As Integer
    Public Dir As Integer
    Public IsGlobal As Boolean
    Public Relative As Boolean
    Public Dist As Double
    Public Value As Double
    Public IsMoment As Boolean
End Class

Public Class FemNodalLoad
    Public Node As Integer
    Public F(5) As Double                       'global forces and moments
End Class

Public Class FemPattern
    Public Name As String
    Public SelfWeightMultiplier As Double
    Public NodalLoads As New List(Of FemNodalLoad)
    Public DistLoads As New List(Of FemDistLoad)
    Public PointLoads As New List(Of FemPointLoadOnElement)
End Class

Public Class FemTerm
    Public Name As String
    Public Factor As Double
End Class

'A linear static case or a combination, flattened to load patterns
Public Class FemCombo
    Public Name As String
    Public Terms As New List(Of FemTerm)
    Public RSTerms As New List(Of FemTerm)         'response spectrum cases (results are magnitudes)
    Public Nonlinear As Boolean                    'refers to a nonlinear (P-Delta) static case: needs its own iterative solution
End Class

Public Class FemModel
    Public Nodes As New List(Of FemNode)
    Public Elements As New List(Of FemElement)
    Public Sections As New List(Of FemSection)
    Public Patterns As New List(Of FemPattern)
    Public Combos As New List(Of FemCombo)
    Public DiaphragmCount As Integer
    Public MassSource As New FemMassSource
    Public Spectra As New List(Of FemSpectrum)
    Public RSCases As New List(Of FemRSCase)
    Public ModalName As String
    Public DesignCombos As New List(Of String)
    Public Prefs As New DesignPrefs
    Public NonlinearCases As New List(Of String)     'static cases that ETABS solves as nonlinear (P-Delta when set)
    Public ModalCount As Integer
    Public CompositeScwb As Boolean              'AISC 341-22 G3.4a (PCR-5b): composite columns (sections with Mpcc) take part in the SCWB ratios
    Public ScwbBeamCode As Boolean               'beam side of steel joints by AISC 341-22 Eq. E3-3 instead of the ETABS fit (composite joints: G3.4a always)
    Public CornerScwb As Boolean                 'AISC 341-22 E3.3 / G3.3 (PCR-5g): columns of two intersecting moment frames, both directions together
    Public CornerGravity As New FemCombo         'gravity part of the D1.4a overstrength combination (1.2 D + 0.5 L + 0.2 SDS D: dead and live patterns)
    Public Omega0 As Double                      'overstrength factor of the seismic force-resisting system
    'PCR-11 options of the corner check (defaults = PCR-5g)
    Public CornerRbs As Boolean                  'beam side: reduced beam section of AISC 358-22 5.7 (c = 0.25 bbf) with Mpr of Eq. 2.4-1 instead of the full section
    Public CornerOrtho As Double = 1.0           'orthogonal factor of the overstrength axial load: 1.0 = 100 % + 100 %, 0.3 = ASCE 7-22 12.5.1.2(a) 100 % + 30 %
    Public CornerRoofExempt As Boolean           'AISC 341-22 E3.4a exception (a)(1) at the roof corner joints (as in the storey check)
    Public RbsGravity As New FemCombo            'Vgravity of AISC 358-22 Eq. 2.4-3: 1.2 D + 0.5 L (dead and live patterns of CornerGravity)
    Public AxialFactor As Double = 1.0           'stiffness reduction of the Direct Analysis Method applied by ETABS after a design run: EA
    Public FlexuralFactor As Double = 1.0        'and EI (0.8, tau-b = 1)
    Public Notes As New List(Of String)         'unsupported features, assumptions

    'Load patterns combined with factors into one pattern (loads scaled)
    Public Function CombinePatterns(ByVal Terms As List(Of FemTerm), ByVal Name As String) As FemPattern
        Dim R As New FemPattern With {.Name = Name}
        For Each t In Terms
            Dim p As Integer = PatternIndex(t.Name)
            If p < 0 Then Continue For
            Dim Src = Patterns(p)
            R.SelfWeightMultiplier += t.Factor * Src.SelfWeightMultiplier
            For Each nl In Src.NodalLoads
                Dim c As New FemNodalLoad With {.Node = nl.Node}
                For d = 0 To 5 : c.F(d) = t.Factor * nl.F(d) : Next
                R.NodalLoads.Add(c)
            Next
            For Each dl In Src.DistLoads
                R.DistLoads.Add(New FemDistLoad With {.Element = dl.Element, .Dir = dl.Dir, .IsGlobal = dl.IsGlobal, .Relative = dl.Relative, .D1 = dl.D1, .D2 = dl.D2, .V1 = t.Factor * dl.V1, .V2 = t.Factor * dl.V2})
            Next
            For Each pl In Src.PointLoads
                R.PointLoads.Add(New FemPointLoadOnElement With {.Element = pl.Element, .Dir = pl.Dir, .IsGlobal = pl.IsGlobal, .Relative = pl.Relative, .Dist = pl.Dist, .Value = t.Factor * pl.Value, .IsMoment = pl.IsMoment})
            Next
        Next
        Return R
    End Function

    Public Function NodeIndex(ByVal Name As String) As Integer
        For i = 0 To Nodes.Count - 1
            If Nodes(i).Name = Name Then Return i
        Next
        Return -1
    End Function

    Public Function PatternIndex(ByVal Name As String) As Integer
        For i = 0 To Patterns.Count - 1
            If Patterns(i).Name = Name Then Return i
        Next
        Return -1
    End Function
End Class
