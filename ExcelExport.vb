Imports System.IO
Imports System.Globalization
Imports System.Text.RegularExpressions
Imports DocumentFormat.OpenXml
Imports DocumentFormat.OpenXml.Packaging
Imports DocumentFormat.OpenXml.Spreadsheet

'Excel workbook (.xlsx, OpenXml SDK: no Excel installation needed) of an optimization result (<output>.xlsx) and of a
'Check Structure run (<output>.check.xlsx). If the file is open in Excel, the workbook is written as
'<name>_<yyyyMMdd_HHmmss>.xlsx; the file actually written is returned.
Public Module ExcelExport

    Private Const STYLE_HEADER As UInteger = 1
    Private Const STYLE_NUMBER As UInteger = 2

    Private Class Sheet_
        Public Name As String
        Public Rows As New List(Of Object())
        Public Header As Integer = 0          'row index of the header (bold), -1: none
        Public Sub New(n As String)
            Name = n
        End Sub
        Public Sub Add(ParamArray cells() As Object)
            Rows.Add(cells)
        End Sub
    End Class

    '______________________________________________________________________________________________
    'Optimization result
    Public Function WriteResult(ByVal FileName As String, ByVal R As ClassFinal, ByVal FI As MiscellaneousStructures.FormInfo_) As String
        Dim Sheets As New List(Of Sheet_)

        Dim S As New Sheet_("Summary") With {.Header = -1}
        S.Add("Steel frame optimization with composite columns (ETABS)")
        S.Add("Written", Date.Now.ToString("yyyy-MM-dd HH:mm:ss"))
        S.Add("Model", FI.FileList.ETABSFile)
        S.Add("Output", FI.FileList.OutputFile)
        S.Add("Method", FI.OptInfo.OptimizationMethod.ToString())
        S.Add("Seed", R.Seed)
        S.Add("Memory size", FI.OptInfo.MemorySize)
        S.Add("Analyses", R.Analyses)
        S.Add("Steel design code", FI.FrameInfo.SteelDesignCode)
        S.Add("Composite columns", If(FI.CompositeColumns, "yes (" & FI.CompositeCode.ToString() & ")", "no"))
        S.Add("Inter-story drift limit h /", FI.FrameInfo.InterStoryDriftR)
        S.Add("Top drift limit H /", FI.FrameInfo.TopStoryDriftR)
        S.Add("P-Delta", If(FI.PDelta, "yes", "no"))
        S.Add("Drift mode", FI.DriftComboMode.ToString())
        If FI.CompositeColumns AndAlso FI.Costs.IsSet Then
            S.Add("Unit costs", "steel " & Num(FI.Costs.Steel) & " /kN, rebar " & Num(FI.Costs.Rebar) & " /kN, concrete " & Num(FI.Costs.Concrete) & " /m3, formwork " & Num(FI.Costs.Formwork) & " /m2")
        End If
        S.Add()
        S.Add("Best design of the search: cost", R.GlobalBest.CostValue)
        S.Add("Best design of the search: penalty", R.GlobalBest.Penalty)
        If R.FinalCheck.DesignVariables IsNot Nothing Then
            S.Add("Final design (all cases, no repair): cost", R.FinalCheck.CostValue)
            S.Add("Final design: penalty", R.FinalCheck.Penalty)
        End If
        S.Add("Final design satisfies all checks", If(R.FinalFails, "NO - see ErrorLog.txt", "yes"))
        Sheets.Add(S)

        Sheets.Add(CostSheet(R.CostBreakdown, FI))

        Dim D As New Sheet_("Design")
        D.Add("Group", "Final design", "Best design of the search")
        Dim Search As Dictionary(Of String, String) = SplitLines(R.GlobalBestPrint)
        Dim Final As Dictionary(Of String, String) = SplitLines(R.FinalDesignPrint)
        For Each g In If(Final.Count > 0, Final.Keys, Search.Keys)
            D.Add(g, If(Final.ContainsKey(g), Final(g), ""), If(Search.ContainsKey(g), Search(g), ""))
        Next
        Sheets.Add(D)

        Dim C As New Sheet_("Constraints")
        C.Add("Constraint (final design)", "Value / limit")
        For Each kv In SplitLines(R.FinalConstraints)
            C.Add(kv.Key, ToNumber(kv.Value))
        Next
        Sheets.Add(C)

        If R.ETABSCompositeCheck IsNot Nothing AndAlso R.ETABSCompositeCheck.Count > 0 Then Sheets.Add(CompositeSheet(R.ETABSCompositeCheck))

        Dim H As New Sheet_("History")
        H.Add("Analysis", "Loop", "Best cost", "Penalty")
        If R.Histories IsNot Nothing Then
            For Each x In R.Histories
                H.Add(x.Iter, x.ILoop, x.Cost, x.Penalty)
            Next
        End If
        Sheets.Add(H)
        Return Write(FileName, Sheets)
    End Function

    '______________________________________________________________________________________________
    'Check Structure
    Public Function WriteCheck(ByVal FileName As String, ByVal P As ETABS_Print, ByVal FI As MiscellaneousStructures.FormInfo_) As String
        Dim Sheets As New List(Of Sheet_)
        Dim S As New Sheet_("Summary") With {.Header = -1}
        S.Add("Check Structure")
        S.Add("Written", Date.Now.ToString("yyyy-MM-dd HH:mm:ss"))
        S.Add("Model", FI.FileList.ETABSFile)
        S.Add("Checked output", FI.FileList.OutputFile)
        S.Add("Cost", P.Cost)
        S.Add("Penalty", P.Penalty)
        S.Add("Analysis finished", If(P.AnalysisFailed, "NO", "yes"))
        Sheets.Add(S)
        Sheets.Add(CostSheet(P.CostBreakdown, FI))
        Dim G As New Sheet_("Design ratios")
        G.Add("Group", "Design ratio")
        For i = 0 To System.Math.Min(P.GroupNames.Count, P.PMM_Ratios.Count) - 1
            G.Add(P.GroupNames(i), P.PMM_Ratios(i))
        Next
        Sheets.Add(G)
        Dim Dr As New Sheet_("Drifts")
        Dr.Add("Story", "Inter-story drift X [mm]", "Inter-story drift Y [mm]")
        For i = 0 To P.InterStoryDrifts.Count - 1
            Dim v = P.InterStoryDrifts(i)
            Dr.Add(i + 1, If(v.Count > 0, v(0), 0), If(v.Count > 1, v(1), 0))
        Next
        If P.TopStoryDrifts.Count >= 2 Then Dr.Add("Top", P.TopStoryDrifts(0), P.TopStoryDrifts(1))
        Sheets.Add(Dr)
        If P.ETABSCompositeCheck IsNot Nothing AndAlso P.ETABSCompositeCheck.Count > 0 Then Sheets.Add(CompositeSheet(P.ETABSCompositeCheck))
        Return Write(FileName, Sheets)
    End Function

    '______________________________________________________________________________________________
    Private Function CostSheet(ByVal Items As List(Of CostItem_), ByVal FI As MiscellaneousStructures.FormInfo_) As Sheet_
        Dim C As New Sheet_("Cost")
        C.Add("Group", "Section", "Type", "Members", "Length [m]", "Steel [kN]", "Rebar [kN]", "Concrete [m3]", "Formwork [m2]",
              "Steel cost", "Rebar cost", "Concrete cost", "Formwork cost", "Total cost", "Share [%]")
        If Items IsNot Nothing Then
            For Each x In Items
                C.Add(x.Group, x.Section, x.Kind, x.Members, x.Length_m, x.SteelWeight_kN, x.RebarWeight_kN, x.Concrete_m3, x.Formwork_m2,
                      x.SteelCost, x.RebarCost, x.ConcreteCost, x.FormworkCost, x.TotalCost, x.Share)
            Next
        End If
        C.Add()
        C.Add(If(FI.CompositeColumns, "Cost = steel x unit cost + rebar x unit cost + concrete x unit cost + formwork x unit cost (unit costs on the Summary sheet)",
                                      "Cost = steel weight [kN] (no composite columns)"))
        Return C
    End Function

    'lines "<group>: ETABS PMM 0.810, shear 0.055 | internal strength 0.550, detailing 0.828 | message"
    Private Function CompositeSheet(ByVal Lines As List(Of String)) As Sheet_
        Dim C As New Sheet_("ETABS composite")
        C.Add("Group", "ETABS PMM", "ETABS shear", "Internal strength", "Internal detailing", "ETABS message")
        Dim Rx As New Regex("^(?<g>[^:]+): ETABS PMM (?<pmm>[-0-9.]+), shear (?<v>[-0-9.]+) \| internal strength (?<s>[-0-9.]+), detailing (?<d>[-0-9.]+)(?: \| (?<m>.*))?$")
        For Each l In Lines
            Dim m = Rx.Match(l)
            If m.Success Then
                C.Add(m.Groups("g").Value, ToNumber(m.Groups("pmm").Value), ToNumber(m.Groups("v").Value), ToNumber(m.Groups("s").Value), ToNumber(m.Groups("d").Value), m.Groups("m").Value)
            Else
                C.Add(l)
            End If
        Next
        Return C
    End Function

    '"<key>: <value>" lines (GlobalBestPrint: the first two lines are headers)
    Private Function SplitLines(ByVal Lines As List(Of String)) As Dictionary(Of String, String)
        Dim D As New Dictionary(Of String, String)
        If Lines Is Nothing Then Return D
        For Each l In Lines
            If l.StartsWith("Global Best") OrElse l.StartsWith("Group Name") Then Continue For
            Dim p() As String = l.Split(New Char() {":"c}, 2)
            If p.Length = 2 Then D(p(0).Trim()) = p(1).Trim()
        Next
        Return D
    End Function

    Private Function ToNumber(ByVal s As String) As Object
        Dim x As Double
        If Double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, x) Then Return x
        Return s
    End Function

    Private Function Num(ByVal x As Double) As String
        Return x.ToString(CultureInfo.InvariantCulture)
    End Function

    '______________________________________________________________________________________________
    Private Function Write(ByVal FileName As String, ByVal Sheets As List(Of Sheet_)) As String
        Try
            WriteFile(FileName, Sheets)
            Return FileName
        Catch ex As IOException
            'the workbook is open in Excel: written under another name
            Dim Alt As String = Path.Combine(Path.GetDirectoryName(FileName), Path.GetFileNameWithoutExtension(FileName) & "_" & Date.Now.ToString("yyyyMMdd_HHmmss") & ".xlsx")
            WriteFile(Alt, Sheets)
            Return Alt
        End Try
    End Function

    Private Sub WriteFile(ByVal FileName As String, ByVal Sheets As List(Of Sheet_))
        Using Doc As SpreadsheetDocument = SpreadsheetDocument.Create(FileName, SpreadsheetDocumentType.Workbook)
            Dim WbPart As WorkbookPart = Doc.AddWorkbookPart()
            WbPart.Workbook = New Workbook()
            Dim StylePart = WbPart.AddNewPart(Of WorkbookStylesPart)()
            StylePart.Stylesheet = CreateStylesheet()
            Dim SheetList As Sheets = WbPart.Workbook.AppendChild(New Sheets())
            Dim Id As UInteger = 1
            For Each Sh In Sheets
                Dim WsPart As WorksheetPart = WbPart.AddNewPart(Of WorksheetPart)()
                Dim Data As New SheetData()
                Dim MaxLen As New Dictionary(Of Integer, Integer)
                For r = 0 To Sh.Rows.Count - 1
                    Dim Row As New Row() With {.RowIndex = CUInt(r + 1)}
                    Dim Cells() As Object = Sh.Rows(r)
                    For c = 0 To Cells.Length - 1
                        Dim Cell As Cell = MakeCell(Cells(c), c, r + 1, r = Sh.Header)
                        If Cell Is Nothing Then Continue For
                        Row.Append(Cell)
                        Dim Len As Integer = If(Cells(c) Is Nothing, 0, Text(Cells(c)).Length)
                        If Not MaxLen.ContainsKey(c) OrElse MaxLen(c) < Len Then MaxLen(c) = Len
                    Next
                    Data.Append(Row)
                Next
                Dim Ws As New Worksheet()
                If MaxLen.Count > 0 Then
                    Dim Cols As New Columns()
                    For Each kv In MaxLen.OrderBy(Function(k) k.Key)
                        Dim W As Double = System.Math.Min(System.Math.Max(kv.Value + 2, 10), 70)
                        Cols.Append(New Column() With {.Min = CUInt(kv.Key + 1), .Max = CUInt(kv.Key + 1), .Width = W, .CustomWidth = True})
                    Next
                    Ws.Append(Cols)
                End If
                Ws.Append(Data)
                WsPart.Worksheet = Ws
                SheetList.Append(New Sheet() With {.Id = WbPart.GetIdOfPart(WsPart), .SheetId = Id, .Name = Sh.Name})
                Id += 1UI
            Next
            WbPart.Workbook.Save()
        End Using
    End Sub

    Private Function Text(ByVal v As Object) As String
        If TypeOf v Is Double Then Return CDbl(v).ToString("G6", CultureInfo.InvariantCulture)
        Return Convert.ToString(v, CultureInfo.InvariantCulture)
    End Function

    Private Function MakeCell(ByVal v As Object, ByVal Col As Integer, ByVal RowNo As Integer, ByVal Header As Boolean) As Cell
        If v Is Nothing Then Return Nothing
        Dim Ref As String = ColumnName(Col) & RowNo
        Dim C As New Cell() With {.CellReference = Ref}
        If TypeOf v Is Double OrElse TypeOf v Is Integer OrElse TypeOf v Is Single OrElse TypeOf v Is Long Then
            Dim d As Double = Convert.ToDouble(v, CultureInfo.InvariantCulture)
            If Double.IsNaN(d) OrElse Double.IsInfinity(d) Then
                C.DataType = CellValues.InlineString
                C.InlineString = New InlineString(New DocumentFormat.OpenXml.Spreadsheet.Text(d.ToString(CultureInfo.InvariantCulture)))
            Else
                C.CellValue = New CellValue(d.ToString("R", CultureInfo.InvariantCulture))
                C.DataType = CellValues.Number
                If TypeOf v Is Double Then C.StyleIndex = STYLE_NUMBER
            End If
        Else
            C.DataType = CellValues.InlineString
            C.InlineString = New InlineString(New DocumentFormat.OpenXml.Spreadsheet.Text(Text(v)) With {.Space = SpaceProcessingModeValues.Preserve})
        End If
        If Header Then C.StyleIndex = STYLE_HEADER
        Return C
    End Function

    Private Function ColumnName(ByVal Col As Integer) As String
        Dim s As String = ""
        Dim n As Integer = Col + 1
        While n > 0
            Dim m As Integer = (n - 1) Mod 26
            s = Chr(65 + m) & s
            n = (n - 1) \ 26
        End While
        Return s
    End Function

    'cell formats: 0 default, 1 bold header, 2 number with 3 decimals
    Private Function CreateStylesheet() As Stylesheet
        Dim Fonts As New Fonts(New Font(), New Font(New Bold()))
        Dim Fills As New Fills(New Fill(New PatternFill() With {.PatternType = PatternValues.None}), New Fill(New PatternFill() With {.PatternType = PatternValues.Gray125}))
        Dim Borders As New Borders(New Border())
        Dim NumFmts As New NumberingFormats(New NumberingFormat() With {.NumberFormatId = 164UI, .FormatCode = "0.000"})
        Dim Formats As New CellFormats(New CellFormat(),
                                       New CellFormat() With {.FontId = 1UI, .ApplyFont = True},
                                       New CellFormat() With {.NumberFormatId = 164UI, .ApplyNumberFormat = True})
        Return New Stylesheet(NumFmts, Fonts, Fills, Borders, Formats)
    End Function
End Module
