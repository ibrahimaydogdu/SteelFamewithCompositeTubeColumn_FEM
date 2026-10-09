Imports System.Globalization
Imports System.Xml.Serialization

'=====================================================================================================
' Concrete-filled steel tube columns (CFT box, CFP pipe): section catalog and settings (TubeSections.xml).
' Strength: FilledBox / FilledPipe of CompositeColumn.vb (AISC 360-16 / 360-22 I2.2, I3.4, I4, I5).
' Catalog: square (optionally rectangular) boxes and (optionally) pipes of the CSI section library
' (STEEL_BOX / STEEL_PIPE, design wall thickness) and generated built-up boxes welded from plates.
' Sections beyond lambda_max of Table I1.1a / I1.1b are not used. Sorted by steel area.
'=====================================================================================================

'Default 0 = encased: backups written before the option existed keep their behavior
Public Enum CompositeType_
    Encased = 0
    FilledTube = 1
End Enum

Public Enum TubeShape_
    Box = 0
    Pipe = 1
End Enum

'Width-to-thickness limits of the walls of filled composite members for seismic design: TBDY 2018 Table 9.3 (composite
'members) = AISC 341-10 Table D1.1. Box: b/t <= 1.4 sqrt(E/Fy) (high) / 2.26 sqrt(E/Fy) (moderate); pipe: D/t <= 0.076 E/Fy /
'0.15 E/Fy. b: flat width, B - 3t for library HSS (corner radius), B - 2t for built-up boxes.
Public Enum SeismicDuctility_
    None = 0
    Moderate = 1
    High = 2
End Enum

Public Class TubeSection_
    Public Name As String           'library label (HSS...) or built-up box BU<H>X<B>X<t>
    Public Shape As TubeShape_
    Public H As Double              'outer depth (pipe: diameter) [mm]
    Public B As Double              'outer width (pipe: diameter) [mm]
    Public t As Double              'design wall thickness [mm]
    Public BuiltUp As Boolean

    Public ReadOnly Property SteelArea As Double
        Get
            If Shape = TubeShape_.Pipe Then Return Math.PI / 4 * (H ^ 2 - (H - 2 * t) ^ 2)
            Return B * H - (B - 2 * t) * (H - 2 * t)
        End Get
    End Property
    Public ReadOnly Property ConcreteArea As Double
        Get
            If Shape = TubeShape_.Pipe Then Return Math.PI / 4 * (H - 2 * t) ^ 2
            Return (B - 2 * t) * (H - 2 * t)
        End Get
    End Property

    Public Function Build(ByVal Mat As CompositeMaterial_) As CompositeSection
        If Shape = TubeShape_.Pipe Then Return New FilledPipe(H, t, Mat) With {.Name = Name}
        Return New FilledBox(H, B, t, Mat) With {.Name = Name}
    End Function

    'Printable size, e.g. "CFT 559x559x23.6" / "CFP 711x25.4"
    Public Function Describe() As String
        Dim F = Function(x As Double) x.ToString("0.#", CultureInfo.InvariantCulture)
        If Shape = TubeShape_.Pipe Then Return "CFP " & F(H) & "x" & F(t)
        Return "CFT " & F(H) & "x" & F(B) & "x" & F(t)
    End Function
End Class

<XmlRoot("TubeSettings")>
Public Class TubeSettings_
    Public Library As String = ""               'empty: section library of the program (SectionPropertyDataPath)
    Public SquareBoxes As Boolean = True
    Public RectangularBoxes As Boolean = False
    Public Pipes As Boolean = False              'beam moment connections to pipes are harder to detail (see the user guide)
    Public MinDimension As Double = 300          '[mm] smallest outer dimension (TBDY 2018 7.3.1.1 minimum of RC columns, connections)
    Public BuiltUpBoxes As Boolean = True        'square boxes welded from plates (larger than the library HSS)
    Public BuiltUpMin As Double = 400            '[mm]
    Public BuiltUpMax As Double = 1000           '[mm]
    Public BuiltUpStep As Double = 50            '[mm]
    Public BuiltUpThicknesses As String = "12,15,20,25,30,35,40,50"      '[mm] plate thicknesses
    Public ConcreteMaterial As String = ""       'empty: concrete material of the model
    Public SeismicDuctility As SeismicDuctility_ = SeismicDuctility_.High     'TBDY 2018 Table 9.3 / AISC 341 wall slenderness
    Public K22 As Double = 1.0
    Public K33 As Double = 1.0
    Public B2 As Double = 1.0
    Public BoxPrefix As String = "CFT_"
    Public PipePrefix As String = "CFP_"
    'Relative unit costs of the objective function (composite mode, steel = 1); the form values replace them
    Public SteelUnitCost As Double = 1.0         'per kN of structural steel
    Public ConcreteUnitCost As Double = 0.6      'per m³ of concrete

    Public Shared Function DefaultPath() As String
        Return IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TubeSections.xml")
    End Function

    Public Shared Function LoadOrDefault() As TubeSettings_
        Try
            If IO.File.Exists(DefaultPath()) Then Return Load(DefaultPath())
        Catch
        End Try
        Return New TubeSettings_()
    End Function

    Public Shared Function Load(ByVal FilePath As String) As TubeSettings_
        Dim serializer As New XmlSerializer(GetType(TubeSettings_))
        Using reader As New IO.StreamReader(FilePath)
            Return CType(serializer.Deserialize(reader), TubeSettings_)
        End Using
    End Function

    Public Function Prefix(ByVal Shape As TubeShape_) As String
        Return If(Shape = TubeShape_.Pipe, PipePrefix, BoxPrefix)
    End Function

    'Wall slenderness limit of the seismic ductility level (Double.MaxValue: no limit)
    Public Function SeismicLimit(ByVal Shape As TubeShape_, ByVal Mat As CompositeMaterial_) As Double
        If SeismicDuctility = SeismicDuctility_.None Then Return Double.MaxValue
        Dim High As Boolean = SeismicDuctility = SeismicDuctility_.High
        If Shape = TubeShape_.Pipe Then Return If(High, 0.076, 0.15) * Mat.Es / Mat.Fy
        Return If(High, 1.4, 2.26) * Math.Sqrt(Mat.Es / Mat.Fy)
    End Function

    'Wall slenderness for the seismic check: pipe D/t, box b/t of the wider wall
    Public Shared Function SeismicSlenderness(ByVal S As TubeSection_) As Double
        If S.Shape = TubeShape_.Pipe Then Return S.H / S.t
        Return (Math.Max(S.H, S.B) - If(S.BuiltUp, 2, 3) * S.t) / S.t
    End Function

    'Library sections (mm) and built-up boxes; sections beyond lambda_max (compression or flexure) are removed.
    'Mat: Fy and Es of the column steel for the classification.
    Public Function Catalog(ByVal LibraryFile As String, ByVal Mat As CompositeMaterial_, ByRef Log As String) As List(Of TubeSection_)
        Dim L As New List(Of TubeSection_)
        Dim NLib As Integer = 0, NBu As Integer = 0, NSlender As Integer = 0, NSeismic As Integer = 0
        If Not String.IsNullOrWhiteSpace(LibraryFile) AndAlso IO.File.Exists(LibraryFile) Then
            Dim doc As XDocument = XDocument.Load(LibraryFile)
            Dim ns As XNamespace = doc.Root.Name.Namespace
            Dim units As String = CStr(doc.Root.Element(ns + "CONTROL")?.Element(ns + "LENGTH_UNITS"))
            If units IsNot Nothing AndAlso units.Trim().ToLowerInvariant() <> "mm" Then
                Throw New IO.InvalidDataException("Tube section library length units must be mm, found: " & units)
            End If
            Dim Num = Function(e As XElement, tag As String) As Double
                          Dim x As XElement = e.Element(ns + tag)
                          If x Is Nothing Then Return 0
                          Return Double.Parse(x.Value, NumberStyles.Float, CultureInfo.InvariantCulture)
                      End Function
            If SquareBoxes OrElse RectangularBoxes Then
                For Each e In doc.Root.Elements(ns + "STEEL_BOX")
                    Dim S As New TubeSection_ With {.Name = CStr(e.Element(ns + "LABEL")), .Shape = TubeShape_.Box,
                                                    .H = Num(e, "HT"), .B = Num(e, "B"), .t = Math.Max(Num(e, "TF"), Num(e, "TW"))}
                    Dim Square As Boolean = Math.Abs(S.H - S.B) < 0.5
                    If (Square AndAlso SquareBoxes) OrElse (Not Square AndAlso RectangularBoxes) Then L.Add(S)
                Next
            End If
            If Pipes Then
                For Each e In doc.Root.Elements(ns + "STEEL_PIPE")
                    Dim D As Double = Num(e, "OD")
                    L.Add(New TubeSection_ With {.Name = CStr(e.Element(ns + "LABEL")), .Shape = TubeShape_.Pipe, .H = D, .B = D, .t = Num(e, "TDES")})
                Next
            End If
            NLib = L.Count
        End If
        If BuiltUpBoxes AndAlso BuiltUpStep > 0 Then
            Dim Ts As List(Of Double) = BuiltUpThicknesses.Split(",;".ToCharArray(), StringSplitOptions.RemoveEmptyEntries).
                Select(Function(s) Double.Parse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture)).Distinct().OrderBy(Function(x) x).ToList()
            Dim Bv As Double = BuiltUpMin
            While Bv <= BuiltUpMax + 0.001
                For Each tp In Ts
                    Dim F = Function(x As Double) x.ToString("0.#", CultureInfo.InvariantCulture)
                    L.Add(New TubeSection_ With {.Name = "BU" & F(Bv) & "X" & F(Bv) & "X" & F(tp), .Shape = TubeShape_.Box, .H = Bv, .B = Bv, .t = tp, .BuiltUp = True})
                    NBu += 1
                Next
                Bv += BuiltUpStep
            End While
        End If
        'smallest dimension, positive thickness, lambda_max (I1.1a / I1.1b)
        Dim Before As Integer = L.Count
        L = L.Where(Function(s) s.t > 0 AndAlso Math.Min(s.H, s.B) >= MinDimension - 0.001 AndAlso s.t < Math.Min(s.H, s.B) / 2).ToList()
        Dim Kept As New List(Of TubeSection_)
        For Each s In L
            Dim C As CompositeSection = s.Build(Mat)
            If C.Classify(False) = CompositeClass_.TooSlender OrElse C.Classify(True) = CompositeClass_.TooSlender Then NSlender += 1 : Continue For
            If SeismicSlenderness(s) > SeismicLimit(s.Shape, Mat) Then NSeismic += 1 : Continue For
            Kept.Add(s)
        Next
        'one section per geometry (library and built-up may coincide), sorted by steel area
        Kept = Kept.GroupBy(Function(s) s.Shape & "|" & Math.Round(s.H, 1) & "|" & Math.Round(s.B, 1) & "|" & Math.Round(s.t, 2)).
                    Select(Function(g) g.OrderBy(Function(s) If(s.BuiltUp, 1, 0)).First()).
                    OrderBy(Function(s) s.SteelArea).ThenBy(Function(s) s.H).ToList()
        Log = "tube catalog: " & Kept.Count & " sections (library " & NLib & ", built-up " & NBu & "; removed: " &
              (Before - L.Count) & " below " & MinDimension & " mm, " & NSlender & " beyond lambda_max, " & NSeismic & " beyond the " &
              SeismicDuctility.ToString().ToLowerInvariant() & " ductility limit (TBDY 2018 Table 9.3), duplicates merged)"
        Return Kept
    End Function
End Class
