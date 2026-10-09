Imports System.Drawing
Imports System.IO
Imports System.Threading
Imports System.Windows.Forms

'Small form of the model builder (started when the program has no arguments): CSV of the examples, output folder, plan, build.
Public Class BuilderForm
    Inherits Form

    Private CsvBox As New TextBox, OutBox As New TextBox, LogBox As New TextBox
    Private HideBox As New CheckBox With {.Text = "Hide ETABS", .Checked = True, .AutoSize = True}
    Private OverwriteBox As New CheckBox With {.Text = "Overwrite existing models", .AutoSize = True}
    Private PlanButton As New Button With {.Text = "Plan (no ETABS)", .Width = 130}
    Private BuildButton As New Button With {.Text = "Build models", .Width = 130}
    Private StopButton As New Button With {.Text = "Stop", .Width = 80, .Enabled = False}
    Private TemplateButton As New Button With {.Text = "Write a template CSV", .Width = 150}
    Private Working As Boolean
    Private StopRequested As Boolean

    Public Sub New()
        Text = "Model Builder - ASCE 7-22 frame examples for ETABS"
        Width = 900 : Height = 640
        MinimumSize = New Size(700, 480)
        Font = New Font("Segoe UI", 9)
        Dim Table As New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = 3, .RowCount = 6, .Padding = New Padding(10)}
        Table.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 110))
        Table.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        Table.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 90))
        For i = 0 To 4 : Table.RowStyles.Add(New RowStyle(SizeType.AutoSize)) : Next
        Table.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
        CsvBox.Dock = DockStyle.Fill : OutBox.Dock = DockStyle.Fill
        Dim Browse1 As New Button With {.Text = "Browse...", .Dock = DockStyle.Fill}
        Dim Browse2 As New Button With {.Text = "Browse...", .Dock = DockStyle.Fill}
        Table.Controls.Add(MakeLabel("Examples (CSV)"), 0, 0) : Table.Controls.Add(CsvBox, 1, 0) : Table.Controls.Add(Browse1, 2, 0)
        Table.Controls.Add(MakeLabel("Output folder"), 0, 1) : Table.Controls.Add(OutBox, 1, 1) : Table.Controls.Add(Browse2, 2, 1)
        Dim Opt As New FlowLayoutPanel With {.AutoSize = True, .Dock = DockStyle.Fill}
        Opt.Controls.AddRange(New Control() {HideBox, OverwriteBox})
        Table.Controls.Add(Opt, 1, 2) : Table.SetColumnSpan(Opt, 2)
        Dim Btn As New FlowLayoutPanel With {.AutoSize = True, .Dock = DockStyle.Fill}
        Btn.Controls.AddRange(New Control() {PlanButton, BuildButton, StopButton, TemplateButton})
        Table.Controls.Add(Btn, 0, 3) : Table.SetColumnSpan(Btn, 3)
        Dim Hint As New Label With {.Text = "The output folder path must be short (at most 150 characters). Each example gives <name>.EDB and <name>_report.txt; the folder also gets examples_summary.csv and runs_template.csv.",
                                    .AutoSize = True, .MaximumSize = New Size(850, 0)}
        Table.Controls.Add(Hint, 0, 4) : Table.SetColumnSpan(Hint, 3)
        LogBox.Multiline = True : LogBox.ReadOnly = True : LogBox.ScrollBars = ScrollBars.Both : LogBox.WordWrap = False
        LogBox.Dock = DockStyle.Fill : LogBox.Font = New Font("Consolas", 9)
        Table.Controls.Add(LogBox, 0, 5) : Table.SetColumnSpan(LogBox, 3)
        Controls.Add(Table)

        AddHandler Browse1.Click, AddressOf BrowseCsv
        AddHandler Browse2.Click, AddressOf BrowseFolder
        AddHandler PlanButton.Click, AddressOf ShowPlan
        AddHandler BuildButton.Click, AddressOf StartBuild
        AddHandler StopButton.Click, AddressOf RequestStop
        AddHandler TemplateButton.Click, AddressOf WriteTemplate
        AddHandler FormClosing, AddressOf HandleClosing
    End Sub

    Private Shared Function MakeLabel(ByVal Caption As String) As Label
        Return New Label With {.Text = Caption, .AutoSize = True, .Anchor = AnchorStyles.Left, .Margin = New Padding(0, 6, 0, 6)}
    End Function

    Private Sub HandleClosing(ByVal sender As Object, ByVal e As FormClosingEventArgs)
        If Working AndAlso MessageBox.Show("A build is running. Closing now leaves ETABS running in the background. Close anyway?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) <> DialogResult.Yes Then e.Cancel = True
    End Sub

    Private Sub RequestStop(ByVal sender As Object, ByVal e As EventArgs)
        StopRequested = True
        Append("stop requested: the build ends after the current example")
    End Sub

    Private Sub Append(ByVal Line As String)
        If InvokeRequired Then
            BeginInvoke(New Action(Of String)(AddressOf Append), Line)
            Return
        End If
        LogBox.AppendText(Line & Environment.NewLine)
    End Sub

    Private Sub BrowseCsv(ByVal sender As Object, ByVal e As EventArgs)
        Using D As New OpenFileDialog With {.Filter = "CSV files (*.csv)|*.csv|All files|*.*", .Title = "Examples table"}
            If D.ShowDialog() = DialogResult.OK Then CsvBox.Text = D.FileName
        End Using
    End Sub

    Private Sub BrowseFolder(ByVal sender As Object, ByVal e As EventArgs)
        Using D As New FolderBrowserDialog With {.Description = "Output folder for the models"}
            If D.ShowDialog() = DialogResult.OK Then OutBox.Text = D.SelectedPath
        End Using
    End Sub

    Private Sub WriteTemplate(ByVal sender As Object, ByVal e As EventArgs)
        Using D As New SaveFileDialog With {.Filter = "CSV files (*.csv)|*.csv", .FileName = "examples.csv", .Title = "Template of the examples table"}
            If D.ShowDialog() = DialogResult.OK Then
                BuildParams_.WriteTemplate(D.FileName)
                CsvBox.Text = D.FileName
                Append("template written: " & D.FileName & " (one example row with the default values; add rows, one per example)")
            End If
        End Using
    End Sub

    Private Function ReadList() As List(Of BuildParams_)
        If Not File.Exists(CsvBox.Text) Then Append("error: choose the examples CSV file") : Return Nothing
        Dim Errors As List(Of String) = Nothing
        Dim L = BuildParams_.ReadCsv(CsvBox.Text, Errors)
        If Errors.Count > 0 Then
            For Each Er In Errors : Append("error: " & Er) : Next
            Return Nothing
        End If
        Return L
    End Function

    Private Sub ShowPlan(ByVal sender As Object, ByVal e As EventArgs)
        Dim L = ReadList()
        If L Is Nothing Then Return
        For Each P In L
            Append("== " & P.Name)
            Append(BuildPlan_.Create(P).Describe())
        Next
    End Sub

    Private Sub StartBuild(ByVal sender As Object, ByVal e As EventArgs)
        If Working Then Return
        Dim L = ReadList()
        If L Is Nothing Then Return
        If OutBox.Text.Trim() = "" Then Append("error: choose the output folder") : Return
        Dim Out As String = OutBox.Text.Trim(), Hidden As Boolean = HideBox.Checked, Over As Boolean = OverwriteBox.Checked
        Working = True : StopRequested = False
        BuildButton.Enabled = False : PlanButton.Enabled = False : StopButton.Enabled = True
        Dim Worker As New Thread(Sub()
                                     Try
                                         BuildRunner.Run(L, Out, Hidden, Over, AddressOf Append, Function() StopRequested)
                                     Catch ex As Exception
                                         Append("error: " & ex.Message)
                                     End Try
                                     BeginInvoke(New Action(AddressOf Finished))
                                 End Sub)
        Worker.IsBackground = True
        Worker.Start()
    End Sub

    Private Sub Finished()
        Working = False
        BuildButton.Enabled = True : PlanButton.Enabled = True : StopButton.Enabled = False
    End Sub
End Class
