Imports System.Globalization
Imports System.IO
Imports System.Text.RegularExpressions

'W section of the ETABS property library (AISC16M.xml, mm)
Public Class LibSection_
    Public Label As String      'W360X134
    Public Series As String     'W360
    Public D, BF, TF, TW, A As Double
End Class

'Section pools (Auto Select Lists) of the beams and the columns
Public Class SectionPools
    'A992 (AISC 341-22 Table A3.1: Ry = 1.1) and the ETABS A992Fy50 material (E = 199948 MPa)
    Public Const E_STEEL As Double = 199948.0
    Public Const FY_STEEL As Double = 344.738
    Public Const RY_STEEL As Double = 1.1

    Private Shared ReadOnly WLabel As New Regex("^W\d+X[\d.]+$")

    'All W sections of the library
    Public Shared Function ReadW(ByVal LibFile As String) As List(Of LibSection_)
        Dim Ns As XNamespace = "http://www.csiberkeley.com"
        Dim Doc As XDocument = XDocument.Load(LibFile)
        Dim Num = Function(e As XElement, n As String) As Double
                      Dim x As XElement = e.Element(Ns + n)
                      Return If(x Is Nothing, 0.0, Double.Parse(x.Value, NumberStyles.Float, CultureInfo.InvariantCulture))
                  End Function
        Dim L As New List(Of LibSection_)
        For Each e In Doc.Descendants(Ns + "STEEL_I_SECTION")
            Dim Lab As String = e.Element(Ns + "LABEL").Value
            If Not WLabel.IsMatch(Lab) Then Continue For
            L.Add(New LibSection_ With {.Label = Lab, .Series = Lab.Substring(0, Lab.IndexOf("X"c)), .D = Num(e, "D"), .BF = Num(e, "BF"), .TF = Num(e, "TF"), .TW = Num(e, "TW"), .A = Num(e, "A")})
        Next
        Return L
    End Function

    'AISC 341-22 Table D1.1 (rolled I-shapes, flexure or flexure + compression): True if the section meets the limit of the ductility level.
    '  flange: b/t = bf / 2 tf <= 0.32 (High) or 0.40 (Moderate) sqrt(E / Ry Fy)
    '  web:    h/tw with Ca = Pu / phi Py (0 for beams):
    '     High:     Ca <= 0.114: 2.57 sqrt(E/RyFy) (1 - 1.04 Ca);  Ca > 0.114: 0.88 sqrt(E/RyFy) (2.68 - Ca) >= 1.57 sqrt(E/RyFy)
    '     Moderate: Ca <= 0.114: 3.96 sqrt(E/RyFy) (1 - 3.04 Ca);  Ca > 0.114: 1.29 sqrt(E/RyFy) (2.12 - Ca) >= 1.57 sqrt(E/RyFy)
    'h is the clear web height; the library has no k dimension, so k = 1.8 tf is used (W14x90 and W36x135 within 5 %).
    Public Shared Function MeetsDuctility(ByVal S As LibSection_, ByVal Level As String, ByVal Ca As Double) As Boolean
        If Level = "None" Then Return True
        Dim Root As Double = Math.Sqrt(E_STEEL / (RY_STEEL * FY_STEEL))
        Dim High As Boolean = (Level = "High")
        If S.BF / (2 * S.TF) > If(High, 0.32, 0.4) * Root Then Return False
        Dim H As Double = S.D - 2 * 1.8 * S.TF
        Dim Lim As Double
        If Ca <= 0.114 Then
            Lim = If(High, 2.57 * (1 - 1.04 * Ca), 3.96 * (1 - 3.04 * Ca)) * Root
        Else
            Lim = Math.Max(If(High, 0.88 * (2.68 - Ca), 1.29 * (2.12 - Ca)), 1.57) * Root
        End If
        Return H / S.TW <= Lim
    End Function

    Public Shared Function BeamPool(ByVal All As List(Of LibSection_), ByVal P As BuildParams_) As List(Of LibSection_)
        Return All.Where(Function(s) s.D >= P.BeamMinDepth AndAlso s.D <= P.BeamMaxDepth AndAlso MeetsDuctility(s, P.SeismicFilter, 0)).OrderBy(Function(s) s.A).ToList()
    End Function

    Public Shared Function ColumnPool(ByVal All As List(Of LibSection_), ByVal P As BuildParams_) As List(Of LibSection_)
        Dim Series = P.ColumnSeries.Split({" "c, ";"c, ","c}, StringSplitOptions.RemoveEmptyEntries).Select(Function(x) x.Trim().ToUpperInvariant()).ToList()
        Return All.Where(Function(s) Series.Contains(s.Series) AndAlso MeetsDuctility(s, P.SeismicFilter, P.ColumnCa)).OrderBy(Function(s) s.A).ToList()
    End Function
End Class
