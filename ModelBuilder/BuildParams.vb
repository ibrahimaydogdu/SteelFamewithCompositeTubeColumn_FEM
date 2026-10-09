Imports System.Globalization
Imports System.IO
Imports System.Reflection

'Parameters of one example = one row of the CSV table. The field names are the CSV column names (case-insensitive);
'a column that is missing keeps the default below. Units: m, kN, kPa, MPa, g (accelerations), s.
Public Class BuildParams_
    Public Name As String = "Example"

    '--- geometry
    Public BaysX As String = "3x9"                  'bay lengths in x [m]; "3x9 6" = 9, 9, 9, 6
    Public BaysY As String = "9 6 9"                'bay lengths in y [m]
    Public Stories As Integer = 15
    Public FirstStoryHeight As Double = 4.5         '[m]
    Public StoryHeight As Double = 3.6              '[m]

    '--- structural system and grouping
    Public FrameSystem As String = "Perimeter"      'Perimeter: moment frames on the perimeter, pinned interior beams (gravity frame) | Space: all beam-column joints are moment joints
    Public InteriorColumnBase As String = "Fixed"   'base of the interior (gravity) columns of the Perimeter system: Fixed | Pinned
    Public BeamBracingSpacing As Double = 2.5       'lateral bracing spacing of the beams [m] (secondary beams / deck): the unbraced length of a beam is its span divided into ceil(span / spacing) equal parts; 0 = unbraced over the whole span. AISC 341 D1.2b limits Lb/ry of the SMF beams
    Public ColumnOrientation As String = "Auto"    'Auto: the edge columns of the lines x = const (frames in the y direction) are rotated by 90 degrees, so that their strong axis lies in the plane of the frame; corner and interior columns keep the default (strong axis in the x frames) | None: all columns keep the default
    Public StoryBand As Integer = 3                 'stories per member group (the groups change every StoryBand stories)

    '--- materials and section pools
    Public ConcreteFc As Double = 27.58             'f'c of the tube fill [MPa]; the material is named <psi>Psi (27.58 MPa = 4000Psi)
    Public BeamMinDepth As Double = 250             'beam pool: W sections with depth >= [mm]
    Public BeamMaxDepth As Double = 920             'beam pool: depth <= [mm] (W920 = W36)
    Public ColumnSeries As String = "W310 W360"     'column pool: W series (labels W310X..., W360X...; W12 and W14)
    Public SeismicFilter As String = "High"         'AISC 341-22 Table D1.1 compactness of the pool sections: None | Moderate | High
    Public ColumnCa As Double = 0.3                 'axial load ratio Pu/(phi Py) assumed for the web limit of the columns in the filter

    '--- gravity loads
    Public SlabWeight As Double = 3.0               'slab self weight [kPa]: the slab is not modeled (null areas); it is part of DEAD
    Public SuperDead As Double = 1.2                'superimposed dead load [kPa] (finishes, services, ceiling)
    Public RoofSuperDead As Double = 1.2            'superimposed dead load of the roof [kPa]
    Public Cladding As Double = 4.0                 'cladding line load on the perimeter beams [kN/m]
    Public LiveLoad As Double = 2.4                 'floor live load [kPa] (ASCE 7 Table 4.3-1, office 50 psf)
    Public Partition As Double = 0.72               'movable partition allowance [kPa], added to the live load (ASCE 7 4.3.2: 15 psf)
    Public RoofLive As Double = 0.96                'roof live load [kPa] (20 psf)
    Public LiveFactor As Double = 0.5               'load factor of L in ASCE 7 combinations 3 to 5 and in the seismic combinations (check the edition: 0.5 or 1.0)

    '--- snow (ASCE 7 Chapter 7, flat roof): pf = 0.7 Ce Ct Is pg
    Public SnowGround As Double = 0.0               'ground snow load pg [kPa]; 0 = no snow load
    Public SnowExposure As Double = 1.0             'Ce
    Public SnowThermal As Double = 1.0              'Ct
    Public SnowImportance As Double = 1.0           'Is

    '--- wind (ASCE 7-22 Chapter 27, ETABS automatic wind loads)
    Public WindSpeed As Double = 49.0               'basic wind speed V [m/s] (49 m/s = 110 mph)
    Public WindExposure As String = "C"             'exposure category: B | C | D
    Public WindKzt As Double = 1.0                  'topographic factor
    Public WindKd As Double = 0.85                  'wind directionality factor
    Public WindGust As Double = 0.85                'gust-effect factor (0.85 rigid); flexible buildings need a computed value

    '--- seismic (ASCE 7-22 Chapter 12; response spectrum analysis, equivalent lateral force as the scaling reference)
    Public SDS As Double = 1.0                      'design spectral acceleration, short period [g]
    Public SD1 As Double = 0.6                      'design spectral acceleration, 1 s [g]
    Public S1 As Double = 0.6                       'mapped MCER spectral acceleration, 1 s [g]
    Public TL As Double = 8.0                       'long-period transition period [s]
    Public SiteClass As String = "D"                'A | B | C | D | E
    Public FrameClass As String = "SMF"             'SMF | IMF | OMF; sets R, Cd, Omega0 when they are empty (0)
    Public R As Double = 0                          'response modification coefficient (0 = by FrameClass)
    Public Cd As Double = 0                         'deflection amplification factor (0 = by FrameClass)
    Public Omega0 As Double = 0                     'overstrength factor (0 = by FrameClass)
    Public SeismicProvisions As Boolean = True      'Yes (default): ETABS also applies the AISC 341 provisions of the framing type (strong column - weak beam, ductility, Lb/ry). The optimizer handles them (SCWB constraint, ductility filter; beams need lateral bracing, BeamBracingSpacing). No: AISC 360 strength + drift only
    Public RiskCategory As String = "II"            'risk category I | II | III | IV (seismic design category)
    Public Ie As Double = 1.0                       'importance factor
    Public Rho As Double = 1.0                      'redundancy factor
    Public EccentricityRatio As Double = 0.05       'accidental eccentricity
    Public PreSize As Boolean = True                'size the members before the check analysis: one relative pool index for all members, found so that T = Cu Ta (the spectrum scaling then belongs to a realistic stiffness)
    Public SpectrumScaleMin As Double = 1.0         'the response spectrum base shear is scaled to at least this fraction of the equivalent lateral force base shear (ASCE 7-22 12.9.1.4)

    '--- design and analysis
    Public Modes As Integer = 12                    'modal analysis: number of modes
    Public DriftLimit As Double = 0.02              'story drift limit (ASCE 7 Table 12.12-1: 0.020 hsx, risk category I/II); written to the report
    Public ServiceDriftRatio As Double = 400       'wind service drift limit H/x (report only)

    '--- effective seismic parameters (from FrameClass when the value is 0)
    Public Function EffR() As Double
        Return If(R > 0, R, If(FrameClass = "IMF", 4.5, If(FrameClass = "OMF", 3.5, 8.0)))
    End Function
    Public Function EffCd() As Double
        Return If(Cd > 0, Cd, If(FrameClass = "IMF", 4.0, If(FrameClass = "OMF", 3.0, 5.5)))
    End Function
    Public Function EffOmega0() As Double
        Return If(Omega0 > 0, Omega0, 3.0)
    End Function

    'Bay lengths of "3x9 6" -> 9, 9, 9, 6
    Public Shared Function ParseBays(ByVal Text As String, ByRef Message As String) As List(Of Double)
        Dim L As New List(Of Double)
        For Each T In (If(Text, "")).Split({" "c, ";"c, "/"c}, StringSplitOptions.RemoveEmptyEntries)
            Dim Count As Integer = 1, Len As Double, V As String = T.Replace(",", ".")
            Dim p As Integer = V.IndexOfAny({"x"c, "X"c})
            If p > 0 Then
                If Not Integer.TryParse(V.Substring(0, p), NumberStyles.Integer, CultureInfo.InvariantCulture, Count) OrElse Count < 1 Then Message = "bay '" & T & "' is not valid" : Return Nothing
                V = V.Substring(p + 1)
            End If
            If Not Double.TryParse(V, NumberStyles.Float, CultureInfo.InvariantCulture, Len) OrElse Len <= 0 Then Message = "bay '" & T & "' is not valid" : Return Nothing
            For i = 1 To Count : L.Add(Len) : Next
        Next
        If L.Count = 0 Then Message = "no bay" : Return Nothing
        Return L
    End Function

    'Errors of the parameters (empty list = valid)
    Public Function Validate() As List(Of String)
        Dim E As New List(Of String), M As String = Nothing
        If String.IsNullOrWhiteSpace(Name) OrElse Name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 Then E.Add("Name '" & Name & "' is not a valid file name")
        If ParseBays(BaysX, M) Is Nothing Then E.Add("BaysX: " & M)
        If ParseBays(BaysY, M) Is Nothing Then E.Add("BaysY: " & M)
        If Stories < 1 OrElse Stories > 100 Then E.Add("Stories must be 1 to 100")
        If FirstStoryHeight <= 0 OrElse StoryHeight <= 0 Then E.Add("story heights must be positive")
        If Not {"Perimeter", "Space"}.Contains(FrameSystem) Then E.Add("FrameSystem must be Perimeter or Space")
        If Not {"Fixed", "Pinned"}.Contains(InteriorColumnBase) Then E.Add("InteriorColumnBase must be Fixed or Pinned")
        If Not {"Auto", "None"}.Contains(ColumnOrientation) Then E.Add("ColumnOrientation must be Auto or None")
        If BeamBracingSpacing < 0 Then E.Add("BeamBracingSpacing must be 0 or positive")
        If StoryBand < 1 Then E.Add("StoryBand must be at least 1")
        If ConcreteFc < 10 OrElse ConcreteFc > 100 Then E.Add("ConcreteFc must be 10 to 100 MPa")
        If BeamMinDepth <= 0 OrElse BeamMaxDepth < BeamMinDepth Then E.Add("beam depth range is not valid")
        If String.IsNullOrWhiteSpace(ColumnSeries) Then E.Add("ColumnSeries is empty")
        If Not {"None", "Moderate", "High"}.Contains(SeismicFilter) Then E.Add("SeismicFilter must be None, Moderate or High")
        If Not {"B", "C", "D"}.Contains(WindExposure) Then E.Add("WindExposure must be B, C or D")
        If Not {"A", "B", "C", "D", "E"}.Contains(SiteClass) Then E.Add("SiteClass must be A to E")
        If Not {"SMF", "IMF", "OMF"}.Contains(FrameClass) Then E.Add("FrameClass must be SMF, IMF or OMF")
        If SDS <= 0 OrElse SD1 <= 0 OrElse TL <= 0 Then E.Add("SDS, SD1 and TL must be positive")
        If WindSpeed <= 0 Then E.Add("WindSpeed must be positive")
        If Ie <= 0 Then E.Add("Ie must be positive")
        If Not {"I", "II", "III", "IV"}.Contains(RiskCategory) Then E.Add("RiskCategory must be I, II, III or IV")
        If Modes < 3 Then E.Add("Modes must be at least 3")
        Return E
    End Function

    '_______________________________________________________________________________________________
    'CSV: the first row holds the column names. The delimiter is "," or ";" (Excel of some locales): the first one found in the header.
    'With ";" a decimal comma is accepted. Empty cells keep the default. Rows whose first cell starts with "#" are comments.
    Public Shared Function ReadCsv(ByVal FileName As String, ByRef Errors As List(Of String)) As List(Of BuildParams_)
        Dim Result As New List(Of BuildParams_)
        Errors = New List(Of String)
        Dim Lines = File.ReadAllLines(FileName, System.Text.Encoding.UTF8).Where(Function(l) l.Trim().Length > 0 AndAlso Not l.TrimStart().StartsWith("#")).ToList()
        If Lines.Count < 2 Then Errors.Add("the CSV needs a header row and at least one example row") : Return Result
        Dim Delim As Char = If(Lines(0).IndexOf(","c) >= 0, ","c, ";"c)
        Dim Header = SplitCsv(Lines(0), Delim).Select(Function(h) h.Trim()).ToList()
        Dim Fields = GetType(BuildParams_).GetFields(BindingFlags.Public Or BindingFlags.Instance).ToDictionary(Function(f) f.Name.ToLowerInvariant())
        For Each h In Header
            If Not Fields.ContainsKey(h.ToLowerInvariant()) Then Errors.Add("unknown column '" & h & "'")
        Next
        If Not Header.Any(Function(h) h.Equals("Name", StringComparison.OrdinalIgnoreCase)) Then Errors.Add("column 'Name' is missing")
        If Errors.Count > 0 Then Return Result
        Dim Names As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        For Row = 1 To Lines.Count - 1
            Dim Cells = SplitCsv(Lines(Row), Delim)
            Dim P As New BuildParams_
            For c = 0 To Math.Min(Header.Count, Cells.Count) - 1
                Dim V As String = Cells(c).Trim()
                If V.Length = 0 Then Continue For
                Dim F = Fields(Header(c).ToLowerInvariant())
                Try
                    If F.FieldType Is GetType(String) Then
                        F.SetValue(P, V)
                    ElseIf F.FieldType Is GetType(Integer) Then
                        F.SetValue(P, Integer.Parse(V, NumberStyles.Integer, CultureInfo.InvariantCulture))
                    ElseIf F.FieldType Is GetType(Boolean) Then
                        Dim Lw As String = V.ToLowerInvariant()
                        If {"yes", "true", "1", "evet"}.Contains(Lw) Then
                            F.SetValue(P, True)
                        ElseIf {"no", "false", "0", "hayir", "hayır"}.Contains(Lw) Then
                            F.SetValue(P, False)
                        Else
                            Throw New FormatException()
                        End If
                    ElseIf F.FieldType Is GetType(Double) Then
                        F.SetValue(P, Double.Parse(If(Delim = ";"c, V.Replace(","c, "."c), V), NumberStyles.Float, CultureInfo.InvariantCulture))
                    End If
                Catch ex As FormatException
                    Errors.Add("row " & (Row + 1) & ", column " & Header(c) & ": '" & V & "' is not a valid value")
                End Try
            Next
            For Each m In P.Validate() : Errors.Add("row " & (Row + 1) & " (" & P.Name & "): " & m) : Next
            If Not Names.Add(P.Name) Then Errors.Add("row " & (Row + 1) & ": the name '" & P.Name & "' is used twice")
            Result.Add(P)
        Next
        Return Result
    End Function

    'Splits a CSV line (double quotes protect the delimiter)
    Private Shared Function SplitCsv(ByVal Line As String, ByVal Delim As Char) As List(Of String)
        Dim L As New List(Of String), Sb As New System.Text.StringBuilder, InQ As Boolean
        For Each ch In Line
            If ch = """"c Then
                InQ = Not InQ
            ElseIf ch = Delim AndAlso Not InQ Then
                L.Add(Sb.ToString()) : Sb.Clear()
            Else
                Sb.Append(ch)
            End If
        Next
        L.Add(Sb.ToString())
        Return L
    End Function

    'Writes a template CSV: the header with all columns and one example row with the defaults
    Public Shared Sub WriteTemplate(ByVal FileName As String)
        Dim P As New BuildParams_
        Dim Fs = GetType(BuildParams_).GetFields(BindingFlags.Public Or BindingFlags.Instance)
        Dim Cell = Function(f As FieldInfo) As String
                       Dim v = f.GetValue(P)
                       Dim s As String = If(TypeOf v Is Double, CDbl(v).ToString("0.###", CultureInfo.InvariantCulture), CStr(v.ToString()))
                       Return If(s.Contains(","), """" & s & """", s)
                   End Function
        File.WriteAllLines(FileName, {String.Join(",", Fs.Select(Function(f) f.Name)), String.Join(",", Fs.Select(Cell))}, New System.Text.UTF8Encoding(True))
    End Sub
End Class
