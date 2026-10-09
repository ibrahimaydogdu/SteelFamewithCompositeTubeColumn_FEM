Imports System.Xml
Imports System.IO
Imports System.Xml.Serialization
Public Class MainForm
    Public FormInfo As MiscellaneousStructures.FormInfo_
    Public ETABSModel As ETABS_Class
    Public OptClass As OptimizationClass
    Public ID_mem As Integer
    Private Const MAX_STALL_LOOPS As Integer = 20
    Private Const BACKUP_INTERVAL_MINUTES As Double = 10       'backup also during a loop (a loop of a large memory takes hours)
    Private BackupClock As Stopwatch
    Private ReadOnly AppTitle As String = "Steel Frame Optimization with Composite Tube Columns (ETABS)"
    'progress display: time and analysis count since the ETABS model is ready (backup: since the restart)
    Private RunClock As Stopwatch
    Private IterAtStart As Integer
    'The run (ETABS calls) works on a background thread: the form stays responsive and shows the progress. Controls
    'are only accessed on the form thread (UI / UIAsync). Stop (button or closing the form) ends the run after the
    'current evaluation: backup written, ETABS closed.
    Private Worker As Threading.Thread
    Private StopRequested As Boolean
    Private CloseAfterRun As Boolean
    Private PhaseText As String = "Ready"
    Private PhaseClock As Stopwatch
    Private Const STOP_TEXT As String = "Stop"
    Private StartText As String

    Public ReadOnly Property IsRunning As Boolean
        Get
            Return Worker IsNot Nothing AndAlso Worker.IsAlive
        End Get
    End Property

    'action on the form thread; the worker waits until it is done
    Private Sub UI(ByVal a As Action)
        If InvokeRequired Then Invoke(a) Else a()
    End Sub

    'current phase of the run (status line, updated every second with the time spent in the phase)
    Private Sub SetPhase(ByVal Text As String)
        Dim a As Action = Sub()
                              PhaseText = Text
                              PhaseClock = Stopwatch.StartNew()
                              ShowStatus()
                          End Sub
        If InvokeRequired Then BeginInvoke(a) Else a()
    End Sub

    Private Sub ShowStatus()
        Dim t As String = If(PhaseClock IsNot Nothing AndAlso IsRunning, " (" & FormatSpan(PhaseClock.Elapsed) & ")", "")
        StatusLabel.Text = PhaseText & t & If(StopRequested AndAlso IsRunning, Environment.NewLine & "Stopping after the current evaluation...", "")
    End Sub

    Private Sub UiTimer_Tick(sender As Object, e As EventArgs) Handles UiTimer.Tick
        ShowStatus()
        If RunClock IsNot Nothing AndAlso IsRunning Then ElapsedBox.Text = FormatSpan(RunClock.Elapsed)
    End Sub

    'input controls are locked during a run (progress fields and lists stay readable)
    Private Sub SetInputsEnabled(ByVal Enabled As Boolean)
        Dim Display As New HashSet(Of Windows.Forms.Control) From {TextBox1, BestCostBox, ElapsedBox, RemainingBox, DateBox, StartTimeBox, FinishTimeBox, AverageTimeBox,
                                                    NofJoint, nofmember, nofgroup, nofsection1, start}
        Dim Walk As Action(Of Windows.Forms.Control) = Nothing
        Walk = Sub(c As Windows.Forms.Control)
                   For Each ch As Windows.Forms.Control In c.Controls
                       If TypeOf ch Is TextBox OrElse TypeOf ch Is ComboBox OrElse TypeOf ch Is CheckBox OrElse TypeOf ch Is Button Then
                           If Not Display.Contains(ch) Then ch.Enabled = Enabled
                       Else
                           Walk(ch)
                       End If
                   Next
               End Sub
        Walk(Me)
    End Sub

    Private Sub MainForm_FormClosing(sender As Object, e As FormClosingEventArgs) Handles Me.FormClosing
        If Not IsRunning Then Return
        e.Cancel = True
        If StopRequested Then Return
        If MsgBox("A run is in progress. Stop it after the current evaluation and close the program?" & Environment.NewLine &
                  "The backup is kept: the run can be continued with 'Load BackUp File'.", MsgBoxStyle.YesNo Or MsgBoxStyle.Question) = MsgBoxResult.Yes Then
            StopRequested = True
            CloseAfterRun = True
            ShowStatus()
        End If
    End Sub

    '_______________________________________________________________________________________________
    'Optimization tab: method list and parameters from MethodCatalog. UiOpt keeps the values of every method while
    'the form is open (switching the method does not lose the edited values).
    Private UiOpt As OptimizationStructure_.OptInfo_
    Private PanelMethod As Object                   'method shown in ParamTable (Nothing: none)
    Private ReadOnly ParamControls As New Dictionary(Of String, Windows.Forms.Control)
    Private ReadOnly Tips As New ToolTip()

    Private Function SelectedMethod() As OptimizationStructure_.OptMethod_
        Return CType(Math.Max(Opt_method.SelectedIndex, 0), OptimizationStructure_.OptMethod_)
    End Function

    Private Sub InitMethodUi()
        Opt_method.DropDownStyle = ComboBoxStyle.DropDownList
        Opt_method.Items.Clear()
        For Each d In MethodCatalog.Methods.OrderBy(Function(x) CInt(x.Method))
            Opt_method.Items.Add(d.Name)
        Next
        If Opt_method.SelectedIndex < 0 Then Opt_method.SelectedIndex = 0
        BuildPanel()
    End Sub

    Private Sub Opt_method_SelectedIndexChanged(sender As Object, e As EventArgs) Handles Opt_method.SelectedIndexChanged
        If MethodCatalog.Methods Is Nothing OrElse Opt_method.Items.Count <> MethodCatalog.Methods.Count Then Return
        SavePanel()
        BuildPanel()
    End Sub

    'current panel values -> UiOpt
    Private Sub SavePanel()
        If PanelMethod Is Nothing Then Return
        Dim m As OptimizationStructure_.OptMethod_ = CType(PanelMethod, OptimizationStructure_.OptMethod_)
        For Each p In MethodCatalog.Find(m).Params
            Dim c As Windows.Forms.Control = Nothing
            If Not ParamControls.TryGetValue(p.Key, c) Then Continue For
            Dim v As Double
            If TypeOf c Is ComboBox Then
                v = Math.Max(CType(c, ComboBox).SelectedIndex, 0)
            ElseIf TryNum(c.Text, v) Then
            Else
                Continue For           'invalid text: checked by CheckParams
            End If
            MethodCatalog.SetParam(UiOpt, m, p.Key, v)
        Next
    End Sub

    Private Sub BuildPanel()
        Dim m As OptimizationStructure_.OptMethod_ = SelectedMethod()
        Dim d As MethodDef_ = MethodCatalog.Find(m)
        ParamTable.SuspendLayout()
        ParamTable.Controls.Clear()
        ParamTable.RowStyles.Clear()
        ParamControls.Clear()
        ParamTable.RowCount = d.Params.Count + 1          'last row: filler (takes the remaining height)
        For r = 0 To d.Params.Count - 1
            Dim p As ParamDef_ = d.Params(r)
            Dim v As Double = MethodCatalog.GetParam(UiOpt, m, p.Key)
            Dim Lbl As New Label With {.Text = p.Label, .AutoSize = True, .Anchor = AnchorStyles.Top Or AnchorStyles.Left, .Margin = New Padding(0, 6, 0, 0)}
            Dim C As Windows.Forms.Control
            If p.Choices IsNot Nothing Then
                Dim Cb As New ComboBox With {.DropDownStyle = ComboBoxStyle.DropDownList, .Dock = DockStyle.Fill}
                Cb.Items.AddRange(p.Choices)
                Cb.SelectedIndex = Math.Min(Math.Max(CInt(v), 0), p.Choices.Length - 1)
                C = Cb
            Else
                C = New TextBox With {.Text = Num(v), .TextAlign = HorizontalAlignment.Right, .Dock = DockStyle.Fill}
            End If
            Dim TipText As String = p.Label & If(p.Choices Is Nothing, " (" & Num(p.Min) & " .. " & Num(p.Max) & If(p.IsInteger, ", integer", "") & ")", "") & If(p.Tip IsNot Nothing, Environment.NewLine & p.Tip, "")
            Tips.SetToolTip(Lbl, TipText)
            Tips.SetToolTip(C, TipText)
            ParamTable.RowStyles.Add(New RowStyle(SizeType.Absolute, 30))
            ParamTable.Controls.Add(Lbl, 0, r)
            ParamTable.Controls.Add(C, 1, r)
            ParamControls(p.Key) = C
        Next
        ParamTable.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
        If d.Params.Count = 0 Then ParamTable.Controls.Add(New Label With {.Text = "(no parameters)", .AutoSize = True}, 0, 0)
        ParamTable.ResumeLayout()
        PanelMethod = m
        MethodInfo.Text = d.Name & Environment.NewLine & d.Description & Environment.NewLine & "Reference: " & d.Reference & Environment.NewLine &
                          "Acceptance: " & If(d.UsesMemoryUpdate, "'Memory update' setting", "own rule of the method (Memory update not used)") & Environment.NewLine &
                          "Levy flight: " & If(d.LevyNote, "not used")
        ApplyMethodUi()
    End Sub

    'Memory update / Levy flight only where the method uses them
    Private Sub ApplyMethodUi()
        Dim d As MethodDef_ = MethodCatalog.Find(SelectedMethod())
        If IsRunning Then Return
        MemoryUpdate.Enabled = d.UsesMemoryUpdate
        Levy_Flight.Enabled = d.LevyNote IsNot Nothing
        Tips.SetToolTip(Levy_Flight, If(d.LevyNote, "not used by this method"))
        Tips.SetToolTip(MemoryUpdate, If(d.UsesMemoryUpdate, "replacement of the memory by a new design", "not used: the method has its own acceptance rule"))
    End Sub

    'values of the parameter panel (Nothing: valid)
    Private Function CheckParams() As String
        Dim d As MethodDef_ = MethodCatalog.Find(SelectedMethod())
        For Each p In d.Params
            Dim c As Windows.Forms.Control = Nothing
            If Not ParamControls.TryGetValue(p.Key, c) OrElse TypeOf c Is ComboBox Then Continue For
            Dim v As Double
            If Not TryNum(c.Text, v) Then Return d.Name & ": '" & p.Label & "' must be a number"
            If v < p.Min OrElse v > p.Max Then Return d.Name & ": '" & p.Label & "' must be between " & Num(p.Min) & " and " & Num(p.Max)
            If p.IsInteger AndAlso v <> Math.Floor(v) Then Return d.Name & ": '" & p.Label & "' must be an integer"
        Next
        If d.Method = OptimizationStructure_.OptMethod_.TreeSeed Then
            Dim a, b As Double
            If TryNum(ParamControls("SeedsMin").Text, a) AndAlso TryNum(ParamControls("SeedsMax").Text, b) AndAlso a > b Then Return d.Name & ": seeds per tree, min must not exceed max"
        End If
        Return Nothing
    End Function

    Private Sub MainForm_Load(sender As Object, e As EventArgs) Handles MyBase.Load
        If DriftCombos.SelectedIndex < 0 Then DriftCombos.SelectedIndex = MiscellaneousStructures.DriftComboMode_.LateralCasesOnly
        If CompositeCodeBox.SelectedIndex < 0 Then CompositeCodeBox.SelectedIndex = CompositeCode_.AISC360_22
        If CompositeTypeBox.SelectedIndex < 0 Then CompositeTypeBox.SelectedIndex = CompositeType_.FilledTube
        If TransitionBox.SelectedIndex < 0 Then TransitionBox.SelectedIndex = TransitionMode_.PerStack
        InitGroupGrid()
        If RepairModeBox.SelectedIndex < 0 Then RepairModeBox.SelectedIndex = MiscellaneousStructures.RepairMode_.Combined
        'HS defaults of the old form (PAR 0.6, HMCR 0.9, Dynamic / Adaptive) come from the catalog
        UiOpt.Params = New List(Of MethodParam_)
        For Each d In MethodCatalog.Methods
            For Each p In d.Params
                MethodCatalog.SetParam(UiOpt, d.Method, p.Key, p.DefaultValue)
            Next
        Next
        If MemoryUpdate.SelectedIndex < 0 Then MemoryUpdate.SelectedIndex = OptimizationStructure_.MemoryUpdateType_.GreedyWorst
        InitMethodUi()
        'unit costs: defaults of EncasedSections.xml (placeholders), edited by the user
        Dim Defaults As EncasedSettings_ = EncasedSettings_.LoadOrDefault()
        CostSteelBox.Text = Num(Defaults.SteelUnitCost)
        CostRebarBox.Text = Num(Defaults.RebarUnitCost)
        CostConcreteBox.Text = Num(Defaults.ConcreteUnitCost)
        CostFormworkBox.Text = Num(Defaults.FormworkUnitCost)
    End Sub

    '_______________________________________________________________________________________________
    'Hybrid columns: type of every column group (Optimize / Steel / Composite), groups read from the model
    Private Shared ReadOnly GroupTypeNames() As String = {"Optimize", "Steel", "Composite"}

    Private Sub InitGroupGrid()
        If GroupTypeGrid.Columns.Count > 0 Then Return
        GroupTypeGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
        GroupTypeGrid.Columns.Add(New DataGridViewTextBoxColumn With {.Name = "Group", .HeaderText = "Grp", .ReadOnly = True, .FillWeight = 18})
        GroupTypeGrid.Columns.Add(New DataGridViewTextBoxColumn With {.Name = "Stories", .HeaderText = "Stories", .ReadOnly = True, .FillWeight = 34})
        Dim T As New DataGridViewComboBoxColumn With {.Name = "Type", .HeaderText = "Type", .FillWeight = 48, .FlatStyle = FlatStyle.Flat}
        T.Items.AddRange(GroupTypeNames)
        GroupTypeGrid.Columns.Add(T)
    End Sub

    'Rows: groups with their stories; the type of a group already in the table is kept
    Private Sub FillGroupGrid(ByVal Infos As IEnumerable(Of ColumnGroupInfo_), ByVal Types As IEnumerable(Of GroupTypeSetting_))
        InitGroupGrid()
        Dim Keep As New Dictionary(Of String, String)
        For Each t In If(Types, New GroupTypeSetting_() {})
            If t IsNot Nothing AndAlso t.GroupName IsNot Nothing Then Keep(t.GroupName) = GroupTypeNames(CInt(t.Type))
        Next
        GroupTypeGrid.Rows.Clear()
        For Each g In Infos
            Dim Tp As String = Nothing
            If Not Keep.TryGetValue(g.Name, Tp) Then Tp = GroupTypeNames(0)
            GroupTypeGrid.Rows.Add(g.Name, g.Stories, Tp)
        Next
    End Sub

    Private Function GroupTypesFromGrid() As List(Of GroupTypeSetting_)
        Dim L As New List(Of GroupTypeSetting_)
        For Each r As DataGridViewRow In GroupTypeGrid.Rows
            Dim Name As String = CStr(r.Cells("Group").Value)
            If String.IsNullOrWhiteSpace(Name) Then Continue For
            Dim k As Integer = Array.IndexOf(GroupTypeNames, CStr(r.Cells("Type").Value))
            L.Add(New GroupTypeSetting_ With {.GroupName = Name, .Type = CType(Math.Max(k, 0), GroupColumnType_)})
        Next
        Return L
    End Function

    'column groups of the selected model (hidden ETABS on a copy, about a minute)
    Private Sub ReadGroupsButton_Click(sender As Object, e As EventArgs) Handles ReadGroupsButton.Click
        If Not File.Exists(ModelFileBox.Text) Then : MsgBox("Select the ETABS model first") : Return : End If
        Dim ModelFile As String = ModelFileBox.Text
        Dim Old As List(Of GroupTypeSetting_) = GroupTypesFromGrid()
        ReadGroupsButton.Enabled = False
        ReadGroupsButton.Text = "Reading the model (ETABS) ..."
        Dim T As New Threading.Thread(Sub()
                                          Dim Msg As String = Nothing
                                          Dim L As List(Of ColumnGroupInfo_) = ETABS_Class.ReadColumnGroups(ModelFile, Msg)
                                          BeginInvoke(Sub()
                                                          FillGroupGrid(L, Old)
                                                          ReadGroupsButton.Text = "Read column groups of the model"
                                                          ReadGroupsButton.Enabled = True
                                                          If L.Count = 0 Then MsgBox(Msg)
                                                      End Sub)
                                      End Sub)
        T.SetApartmentState(Threading.ApartmentState.STA)
        T.IsBackground = True
        T.Start()
    End Sub

    'Filled tubes have no rebar and no formwork: their unit costs are not used
    Private Sub CompositeTypeBox_SelectedIndexChanged(sender As Object, e As EventArgs) Handles CompositeTypeBox.SelectedIndexChanged
        Dim Encased As Boolean = CompositeTypeBox.SelectedIndex = CompositeType_.Encased
        CostRebarBox.Enabled = Encased
        CostFormworkBox.Enabled = Encased
    End Sub

    'VB Rnd: Rnd(-1) followed by Randomize(seed) gives a repeatable sequence for the seed
    Private Sub SetRandomSeed(ByVal Seed As Integer)
        Rnd(-1)
        Randomize(Seed)
        ETABS_Class.SetSeed(Seed)
    End Sub
    Private Sub Start_Click(sender As Object, e As EventArgs) Handles start.Click
        If IsRunning Then
            'Stop
            If StopRequested Then Return
            If MsgBox("Stop the run after the current evaluation?" & Environment.NewLine &
                      "The backup is kept: the run can be continued with 'Load BackUp File'.", MsgBoxStyle.YesNo Or MsgBoxStyle.Question) = MsgBoxResult.Yes Then
                StopRequested = True
                ShowStatus()
            End If
            Return
        End If
        'form input on the form thread, the run on the worker
        Dim Check As Boolean = CheckStructure.Checked
        Dim ret As Integer = 0
        If Check Then
            If Control() = True Then Return
            FormInfo_Read()
        Else
            PrepareRun(ret)
            If ret <> 0 Then Return
        End If
        StopRequested = False
        CloseAfterRun = False
        RunFailed = False
        ETABSModel = Nothing
        ETABS_Class.MessageHandler = Sub(Text As String, Style As MsgBoxStyle) UI(Sub() MsgBox(Text, Style))
        ETABS_Class.StatusHandler = AddressOf SetPhase
        StartText = start.Text
        start.Text = STOP_TEXT
        SetInputsEnabled(False)
        UiTimer.Start()
        SetPhase(If(Check, "Check Structure: starting ETABS", "Starting ETABS"))
        Worker = New Threading.Thread(Sub() RunWorker(Check)) With {.IsBackground = True, .Name = "Optimization"}
        Worker.SetApartmentState(Threading.ApartmentState.STA)
        Worker.Start()
    End Sub

    Private Sub RunWorker(ByVal Check As Boolean)
        Try
            If Check Then
                Dim ret As Integer = 0
                Check_Structure(ret)
                If ret <> 0 Then
                    LogError("Error occurred in Check Structure")
                    CloseETABS(ret)
                End If
            Else
                RunAll()
            End If
        Catch ex As Exception
            'an unexpected exception must not leave ETABS and the working folder behind
            LogError("Unhandled exception: " & ex.ToString())
            CloseETABS(-1)
        Finally
            UI(AddressOf RunFinished)
        End Try
    End Sub

    Private Sub RunFinished()
        Worker = Nothing
        UiTimer.Stop()
        SetInputsEnabled(True)
        ApplyMethodUi()
        start.Text = If(StartText, "Start")
        PhaseText = If(RunFailed, "Failed (see ErrorLog.txt)", If(StopRequested, "Stopped", "Finished")) & " " & Date.Now.ToString("HH:mm:ss")
        PhaseClock = Nothing
        ShowStatus()
        If BatchMode Then
            BatchLog("Info: " & PhaseText)
            If RunFailed Then Environment.ExitCode = 1
        End If
        If CloseAfterRun Then Close()
    End Sub

    'stop requested (button / closing the form): backup, ETABS closed with a message
    Private Function StopNow() As Boolean
        If Not StopRequested Then Return False
        If OptClass IsNot Nothing AndAlso OptClass.Memory IsNot Nothing AndAlso OptClass.Memory.Count > 0 Then OptClass.Backup_Write(midLoop:=True)
        LogError("Info: run stopped by the user after " & If(OptClass Is Nothing, 0, OptClass.iter) & " analyses")
        If ETABSModel IsNot Nothing Then
            If CloseAfterRun Then ETABSModel.Quiet = True      'the program closes: no message
            ETABSModel.Close(0, "Run stopped. Continue it with 'Load BackUp File' (same output file).")
        End If
        Return True
    End Function

    Private Sub RunAll()
        Dim ret As Integer = 0
        Init(ret)
        If ret <> 0 Then
            LogError("Initialisation failed (see the previous messages)")
            CloseETABS(ret)
            Exit Sub
        End If
        If StopNow() Then Exit Sub
        UI(Sub() Me.Text = AppTitle & "  -  " & FormInfo.OptInfo.OptimizationMethod.ToString() & "  (seed " & FormInfo.Seed & ")")
        If ETABSModel IsNot Nothing Then ETABSModel.Errorlogprint("Info: run started, method " & FormInfo.OptInfo.OptimizationMethod.ToString() & ", seed " & FormInfo.Seed)
        'with the result cache a converged search may produce no new design: stop after MAX_STALL_LOOPS such loops
        Dim Stall As Integer = 0
        Do While OptClass.iter < FormInfo.OptInfo.MaxFuncEvaluation
            Dim IterBefore As Integer = OptClass.iter
            OptClass.ILoop += 1
            OptClass.Memory = OptClass.Memory.OrderBy(Function(c) c.PenalizedCost).ToList()
            For Imem = 0 To FormInfo.OptInfo.MemorySize - 1
                ID_mem = Imem
                SetPhase("Search: loop " & OptClass.ILoop & ", member " & (Imem + 1) & " / " & FormInfo.OptInfo.MemorySize)
                Opt_Main(ret)
                If ret <> 0 Then
                    LogError("Error occurred in Opt_Main")
                    CloseETABS(ret)
                    Exit Sub
                End If
                If StopNow() Then Exit Sub
                If BackupClock.Elapsed.TotalMinutes >= BACKUP_INTERVAL_MINUTES Then
                    OptClass.Backup_Write(midLoop:=True)
                    BackupClock.Restart()
                End If
            Next Imem

            If FormInfo.OptInfo.ClearDuplicates Then
                OptClass.ClearDuplicates(ret)
                If ret <> 0 Then
                    LogError("Error occurred in Clear Duplicates")
                    CloseETABS(ret)
                    Exit Sub
                End If
            End If
            OptClass.Backup_Write()
            BackupClock.Restart()
            Stall = If(OptClass.iter = IterBefore, Stall + 1, 0)
            If Stall >= MAX_STALL_LOOPS Then
                LogError("Info: search converged, no new design in " & MAX_STALL_LOOPS & " loops (" & OptClass.iter & " analyses)")
                Exit Do
            End If
        Loop
        SetPhase("Final analysis and ETABS check of the best design")
        OptClass.Opt_Finalize()
        UI(Sub() FinishTimeBox.Text = Date.Now.ToString("HH:mm:ss"))
    End Sub
    Private Sub Opt_Main(ByRef ret As Integer)
        OptClass.Main(ID_mem, ret)
        UI(AddressOf Write_form)
    End Sub

    'ETABS class may not exist yet (validation error, math test mode)
    Private Sub LogError(ByVal msg As String)
        If BatchMode Then BatchLog(msg)
        If ETABSModel IsNot Nothing Then
            ETABSModel.Errorlogprint(msg)
        ElseIf Not BatchMode Then
            UI(Sub() MsgBox(msg))
        End If
    End Sub

    '_______________________________________________________________________________________________
    'Batch mode (tools\RunBatch.ps1): FrameSap2000.exe /batch <settings.xml> [/resume]
    '  settings.xml: the run settings (serialized FormInfo_, as in the backup / output files); /resume continues the
    '  run from its backup (<output>.backup.xml). No message boxes: messages and questions go to <settings>.batch.log
    '  (questions are answered Yes / OK); the program closes when the run ends (exit code 1 if it failed).
    Private BatchMode As Boolean
    Private BatchFile As String

    Private Sub BatchLog(ByVal msg As String, Optional ByVal MustWrite As Boolean = False)
        Try
            File.AppendAllText(Path.ChangeExtension(BatchFile, ".batch.log"), Date.Now.ToString("yyyy-MM-dd HH:mm:ss") & " " & msg & Environment.NewLine)
        Catch When Not MustWrite
        End Try
    End Sub

    'MsgBox of the form: logged in batch mode (no window)
    Private Function MsgBox(ByVal Prompt As Object, Optional ByVal Buttons As MsgBoxStyle = MsgBoxStyle.OkOnly, Optional ByVal Title As Object = Nothing) As MsgBoxResult
        If Not BatchMode Then Return Interaction.MsgBox(Prompt, Buttons, Title)
        BatchLog("Message: " & CStr(Prompt).Replace(Environment.NewLine, " / "))
        Return If((Buttons And MsgBoxStyle.YesNo) = MsgBoxStyle.YesNo, MsgBoxResult.Yes, MsgBoxResult.Ok)
    End Function

    Private Sub MainForm_Shown(sender As Object, e As EventArgs) Handles Me.Shown
        Dim A() As String = Environment.GetCommandLineArgs()
        Dim i As Integer = Array.FindIndex(A, Function(x) x.Equals("/batch", StringComparison.OrdinalIgnoreCase))
        If i < 0 OrElse i + 1 >= A.Length Then Return
        BatchMode = True
        BatchFile = Path.GetFullPath(A(i + 1))
        Try
            'the log must be writable (e.g. paths over 260 characters are not: the run would go on without any messages)
            BatchLog("Info: batch run " & BatchFile & ", program " & GetType(MainForm).Assembly.GetName().Version.ToString(), MustWrite:=True)
            Using R As New StreamReader(BatchFile)
                FormInfo = CType(New XmlSerializer(GetType(MiscellaneousStructures.FormInfo_)).Deserialize(R), MiscellaneousStructures.FormInfo_)
            End Using
            FormInfo_Write()
            BackUp.Checked = A.Any(Function(x) x.Equals("/resume", StringComparison.OrdinalIgnoreCase))
            Text = AppTitle & "  -  batch " & Path.GetFileName(BatchFile)
            start.PerformClick()
            If Not IsRunning Then
                BatchLog("Error: the run did not start (see the messages above)")
                Environment.ExitCode = 1
                Close()
                Return
            End If
            ETABS_Class.MessageHandler = Sub(Text As String, Style As MsgBoxStyle) BatchLog("Message: " & Text)
            CloseAfterRun = True
        Catch ex As Exception
            BatchLog("Error: " & ex.Message)
            Environment.ExitCode = 1
            Close()
        End Try
    End Sub
    Private RunFailed As Boolean
    Private Sub CloseETABS(ByVal ret As Integer)
        If ret <> 0 Then RunFailed = True
        If ETABSModel IsNot Nothing Then ETABSModel.Close(ret)
    End Sub

    Private Sub LoadModelButton_Click(ByVal sender As System.Object, ByVal e As System.EventArgs) Handles LoadModelButton.Click
        Using openFileDialog1 As New Windows.Forms.OpenFileDialog With {
            .Filter = "ETABS File|*.EDB",
            .Title = "Select ETABS file"
        }
            If openFileDialog1.ShowDialog() = DialogResult.OK Then ModelFileBox.Text = openFileDialog1.FileName
        End Using
    End Sub
    Private Sub Loadoutput_Click(ByVal sender As System.Object, ByVal e As System.EventArgs) Handles loadoutput.Click
        Using saveFileDialog1 As New Windows.Forms.SaveFileDialog With {
            .Filter = "xml Files|*.xml",
            .Title = "Output Dosyasını Giriniz"
        }
            If saveFileDialog1.ShowDialog() = DialogResult.OK Then OutputLoc.Text = saveFileDialog1.FileName
        End Using
    End Sub

    'Excel workbook of an existing output file (result of an optimization or of Check Structure)
    Private Sub ExcelButton_Click(sender As Object, e As EventArgs) Handles ExcelButton.Click
        Dim F As String = OutputLoc.Text
        If String.IsNullOrWhiteSpace(F) OrElse Not File.Exists(F) Then : MsgBox("Select an existing output file (*.xml)") : Return : End If
        Try
            Dim Written As String
            Dim Check As String = Path.ChangeExtension(F, ".check.xml")
            Dim R As ClassFinal
            Using reader As New StreamReader(F)
                R = CType(New XmlSerializer(GetType(ClassFinal)).Deserialize(reader), ClassFinal)
            End Using
            'settings of the run (output files of older versions have none: model and composite option only)
            Dim FI As MiscellaneousStructures.FormInfo_ = R.FormInfo
            If String.IsNullOrEmpty(FI.FileList.ETABSFile) Then
                FI.FileList.ETABSFile = ModelFileBox.Text
                FI.CompositeColumns = R.GlobalBestPrint IsNot Nothing AndAlso R.GlobalBestPrint.Any(Function(l) l.Contains("[EC ") OrElse l.Contains("[CFT ") OrElse l.Contains("[CFP "))
                If FI.CompositeColumns AndAlso Not R.GlobalBestPrint.Any(Function(l) l.Contains("[EC ")) Then FI.CompositeType = CompositeType_.FilledTube
            End If
            FI.FileList.OutputFile = F
            Written = ExcelExport.WriteResult(Path.ChangeExtension(F, ".xlsx"), R, FI)
            Dim Msg As String = "Workbook written: " & Written
            If R.CostBreakdown Is Nothing Then Msg &= Environment.NewLine & "The output file has no cost breakdown (written by an older version): the Cost sheet is empty."
            If File.Exists(Check) Then
                Dim P As ETABS_Print
                Using reader As New StreamReader(Check)
                    P = CType(New XmlSerializer(GetType(ETABS_Print)).Deserialize(reader), ETABS_Print)
                End Using
                Msg &= Environment.NewLine & "Check Structure workbook: " & ExcelExport.WriteCheck(Path.ChangeExtension(F, ".check.xlsx"), P, FI)
            End If
            MsgBox(Msg)
        Catch ex As Exception
            MsgBox("The output file could not be exported: " & ex.Message)
        End Try
    End Sub

    'Form numbers with "." or "," as decimal separator, independent of the Windows culture
    '(with IsNumeric / CDbl a tr-TR Windows read "0.9" as 9 and "0.0636" as 636)
    Private Shared Function TryNum(ByVal text As String, ByRef value As Double) As Boolean
        If String.IsNullOrWhiteSpace(text) Then Return False
        Return Double.TryParse(text.Trim().Replace(","c, "."c), Globalization.NumberStyles.Float, Globalization.CultureInfo.InvariantCulture, value)
    End Function
    Private Shared Function IsValidNumber(ByVal text As String) As Boolean
        Dim x As Double
        Return TryNum(text, x)
    End Function
    Private Shared Function ToDbl(ByVal text As String) As Double
        Dim x As Double
        Return If(TryNum(text, x), x, 0)
    End Function
    Private Shared Function IsWholeNumber(ByVal text As String, ByVal Minimum As Double) As Boolean
        Dim x As Double
        Return TryNum(text, x) AndAlso x >= Minimum AndAlso x = Math.Floor(x) AndAlso x <= Integer.MaxValue
    End Function
    Private Shared Function Num(ByVal x As Double) As String
        Return x.ToString(Globalization.CultureInfo.InvariantCulture)
    End Function

    'Returns True if the form input is not valid
    Private Function Control() As Boolean
        Dim Durdur As Boolean = False
        '_______________________________________________________________________________________________
        'Control Input Output file locations
        If My.Computer.FileSystem.FileExists(ModelFileBox.Text) = False And TestwithMath.Checked = False Then
            MessageBox.Show("ETABS File Not Found: " & ModelFileBox.Text)
            Durdur = True
        End If
        Dim OutDir As String = Nothing
        Try
            OutDir = Path.GetDirectoryName(OutputLoc.Text)
        Catch
        End Try
        If String.IsNullOrEmpty(OutDir) OrElse My.Computer.FileSystem.DirectoryExists(OutDir) = False Then
            MessageBox.Show("Output File Not Found: " & OutputLoc.Text)
            Durdur = True
        End If
        '______________________________________________________________________________________________
        'Control Frame Parameters
        If TestwithMath.Checked = False Then
            If ToDbl(TS_LimitR.Text) <= 0 Then
                MsgBox("Top story drift limit (H / ratio) must be a positive number")
                Durdur = True
            End If
            If ToDbl(IS_LimitR.Text) <= 0 Then
                MsgBox("Inter story drift limit (h / ratio) must be a positive number")
                Durdur = True
            End If
            If String.IsNullOrWhiteSpace(Dcode_Steel.Text) Then
                MsgBox("Design Code Steel is not defined correctly")
                Durdur = True
            End If
            If HybridBox.Checked AndAlso Not CompositeColumns.Checked Then
                MsgBox("Hybrid columns need ""Composite columns"" (steel or composite per group)")
                Durdur = True
            End If
            If CompositeColumns.Checked Then
                Dim CostBoxes() As TextBox = {CostSteelBox, CostRebarBox, CostConcreteBox, CostFormworkBox}
                If CostBoxes.Any(Function(b) Not IsValidNumber(b.Text) OrElse ToDbl(b.Text) < 0) OrElse CostBoxes.All(Function(b) ToDbl(b.Text) = 0) Then
                    MsgBox("Composite unit costs must be non-negative numbers, at least one of them positive")
                    Durdur = True
                End If
            End If
        End If
        '___________________________________________________________________________________________
        'Control Optimization Parameters
        If Not IsWholeNumber(RestartBox.Text, 0) Then
            MsgBox("Restart ETABS every: number of analyses, a non-negative integer (0 = never)")
            Durdur = True
        End If
        If Not IsWholeNumber(MemSize.Text, 2) Then
            MsgBox("Memory size must be an integer of at least 2")
            Durdur = True
        End If
        If Not IsWholeNumber(SeedBox.Text, 0) Then
            MsgBox("Random seed must be a non-negative integer (0 = time based)")
            Durdur = True
        End If
        If Not IsWholeNumber(maxiter.Text, 1) Then
            MsgBox("Max. analyses must be a positive integer")
            Durdur = True
        End If
        Dim ParamProblem As String = CheckParams()
        If ParamProblem IsNot Nothing Then
            MsgBox(ParamProblem)
            Durdur = True
        End If
        Return Durdur
    End Function
    Private Sub FormInfo_Read()
        FormInfo = New MiscellaneousStructures.FormInfo_
        FormInfo.FileList.ETABSFile = ModelFileBox.Text
        FormInfo.FileList.OutputFile = OutputLoc.Text
        FormInfo.HideETABS = HideETABS.Checked
        FormInfo.CheckStructure = CheckStructure.Checked
        FormInfo.CompositeColumns = CompositeColumns.Checked
        FormInfo.CompositeCode = Math.Max(CompositeCodeBox.SelectedIndex, 0)
        FormInfo.CompositeType = CType(Math.Max(CompositeTypeBox.SelectedIndex, 0), CompositeType_)
        FormInfo.HybridColumns = HybridBox.Checked
        FormInfo.TransitionMode = CType(Math.Max(TransitionBox.SelectedIndex, 0), TransitionMode_)
        FormInfo.GroupTypes = GroupTypesFromGrid()
        FormInfo.RepairMode = Math.Max(RepairModeBox.SelectedIndex, 0)
        FormInfo.UseCache = ResultCache.Checked
        FormInfo.RestartEvery = CInt(ToDbl(RestartBox.Text))
        FormInfo.SkipUnusedCases = SkipCases.Checked
        FormInfo.Costs = New MiscellaneousStructures.UnitCosts_ With {.Steel = ToDbl(CostSteelBox.Text), .Rebar = ToDbl(CostRebarBox.Text),
                                                                      .Concrete = ToDbl(CostConcreteBox.Text), .Formwork = ToDbl(CostFormworkBox.Text)}
        FormInfo.AutoCombos = AutoCombos.Checked
        FormInfo.PDelta = PDeltaBox.Checked
        FormInfo.SkipCtoC = Not CtoC.Checked
        FormInfo.SkipBtoC = Not BtoC.Checked
        FormInfo.DriftComboMode = Math.Max(DriftCombos.SelectedIndex, 0)
        Dim Seed As Integer = CInt(ToDbl(SeedBox.Text))
        FormInfo.Seed = If(Seed > 0, Seed, Environment.TickCount And Integer.MaxValue)
        '_____________________________________________________________
        FormInfo.FrameInfo.TopStoryDriftR = ToDbl(TS_LimitR.Text)
        FormInfo.FrameInfo.InterStoryDriftR = ToDbl(IS_LimitR.Text)
        FormInfo.FrameInfo.SteelDesignCode = Dcode_Steel.Text
        '_____________________________________________________________
        FormInfo.OptInfo.MemorySize = CInt(ToDbl(MemSize.Text))
        FormInfo.OptInfo.MaxFuncEvaluation = CInt(ToDbl(maxiter.Text))
        FormInfo.OptInfo.MemoryUpdateType = MemoryUpdate.SelectedIndex
        FormInfo.OptInfo.OptimizationMethod = Opt_method.SelectedIndex
        FormInfo.OptInfo.LevyFlight = Levy_Flight.Checked
        FormInfo.OptInfo.TestWithMath = TestwithMath.Checked
        FormInfo.OptInfo.ClearDuplicates = Clear_Duplicates.Checked
        'parameters of the selected method (the values of all methods are kept in UiOpt while the form is open)
        SavePanel()
        For Each p In MethodCatalog.Find(FormInfo.OptInfo.OptimizationMethod).Params
            MethodCatalog.SetParam(FormInfo.OptInfo, FormInfo.OptInfo.OptimizationMethod, p.Key, MethodCatalog.GetParam(UiOpt, FormInfo.OptInfo.OptimizationMethod, p.Key))
        Next
    End Sub
    Private Sub FormInfo_Write()
        ModelFileBox.Text = FormInfo.FileList.ETABSFile
        OutputLoc.Text = FormInfo.FileList.OutputFile
        BackUp.Checked = True
        HideETABS.Checked = FormInfo.HideETABS
        CheckStructure.Checked = FormInfo.CheckStructure
        CompositeColumns.Checked = FormInfo.CompositeColumns
        CompositeCodeBox.SelectedIndex = FormInfo.CompositeCode
        CompositeTypeBox.SelectedIndex = FormInfo.CompositeType
        HybridBox.Checked = FormInfo.HybridColumns
        TransitionBox.SelectedIndex = FormInfo.TransitionMode
        If FormInfo.GroupTypes IsNot Nothing Then FillGroupGrid(FormInfo.GroupTypes.Select(Function(t) New ColumnGroupInfo_ With {.Name = t.GroupName, .Stories = ""}), FormInfo.GroupTypes)
        RepairModeBox.SelectedIndex = FormInfo.RepairMode
        ResultCache.Checked = FormInfo.UseCache
        RestartBox.Text = FormInfo.RestartEvery.ToString()
        SkipCases.Checked = FormInfo.SkipUnusedCases
        If FormInfo.Costs.IsSet Then        'old backups: keep the file defaults shown at start
            CostSteelBox.Text = Num(FormInfo.Costs.Steel)
            CostRebarBox.Text = Num(FormInfo.Costs.Rebar)
            CostConcreteBox.Text = Num(FormInfo.Costs.Concrete)
            CostFormworkBox.Text = Num(FormInfo.Costs.Formwork)
        End If
        AutoCombos.Checked = FormInfo.AutoCombos
        PDeltaBox.Checked = FormInfo.PDelta
        CtoC.Checked = Not FormInfo.SkipCtoC
        BtoC.Checked = Not FormInfo.SkipBtoC
        DriftCombos.SelectedIndex = FormInfo.DriftComboMode
        SeedBox.Text = FormInfo.Seed
        '_____________________________________________________________
        TS_LimitR.Text = Num(FormInfo.FrameInfo.TopStoryDriftR)
        IS_LimitR.Text = Num(FormInfo.FrameInfo.InterStoryDriftR)
        Dcode_Steel.Text = FormInfo.FrameInfo.SteelDesignCode
        '_____________________________________________________________
        MemSize.Text = FormInfo.OptInfo.MemorySize
        maxiter.Text = FormInfo.OptInfo.MaxFuncEvaluation
        MemoryUpdate.SelectedIndex = FormInfo.OptInfo.MemoryUpdateType
        Opt_method.SelectedIndex = FormInfo.OptInfo.OptimizationMethod
        Levy_Flight.Checked = FormInfo.OptInfo.LevyFlight
        TestwithMath.Checked = FormInfo.OptInfo.TestWithMath
        Clear_Duplicates.Checked = FormInfo.OptInfo.ClearDuplicates
        'method parameters of the backup
        For Each p In MethodCatalog.Find(FormInfo.OptInfo.OptimizationMethod).Params
            MethodCatalog.SetParam(UiOpt, FormInfo.OptInfo.OptimizationMethod, p.Key, MethodCatalog.GetParam(FormInfo.OptInfo, FormInfo.OptInfo.OptimizationMethod, p.Key))
        Next
        PanelMethod = Nothing
        BuildPanel()
    End Sub
    'Backup of the run whose output file is given on the form (<output>.backup.xml). Before the run continues the
    'user sees what is restored and is warned if the backup belongs to another model or the model was changed.
    Private RestoredModel As ModelIdentity_
    Private Function Backup_Read(ByRef Message As String) As Boolean
        Dim Results As Class_Backup = OptimizationClass.Backup_Read(OutputLoc.Text, Message)
        If Results Is Nothing Then Return False
        Dim FormModel As String = ModelFileBox.Text
        Dim BackupModel As String = If(Results.Model IsNot Nothing, Results.Model.ModelFile, Results.FormInfo.FileList.ETABSFile)
        If Not File.Exists(BackupModel) Then
            Message = "The model of the backup does not exist: " & BackupModel
            Return False
        End If
        If Not String.IsNullOrWhiteSpace(FormModel) AndAlso Not PathsEqual(FormModel, BackupModel) Then
            If MsgBox("The backup belongs to another model:" & Environment.NewLine & "  backup: " & BackupModel & Environment.NewLine & "  form:   " & FormModel &
                      Environment.NewLine & Environment.NewLine & "Continue the run of the backup with its model?", MsgBoxStyle.YesNo Or MsgBoxStyle.Exclamation) <> MsgBoxResult.Yes Then
                Message = "Load BackUp cancelled (other model)" : Return False
            End If
        End If
        If Results.Model IsNot Nothing AndAlso Results.Model.ModelHash IsNot Nothing Then
            Cursor = Cursors.WaitCursor
            Dim Hash As String = ETABS_Class.FileHash(BackupModel)
            Cursor = Cursors.Default
            If Hash <> Results.Model.ModelHash Then
                If MsgBox("The model was changed after the backup was written:" & Environment.NewLine & "  " & BackupModel & Environment.NewLine &
                          "  at the backup: " & Results.Model.ModelWriteTime.ToString("yyyy-MM-dd HH:mm") & ", " & Results.Model.ModelSize & " bytes" & Environment.NewLine &
                          "  now:           " & File.GetLastWriteTime(BackupModel).ToString("yyyy-MM-dd HH:mm") & ", " & New FileInfo(BackupModel).Length & " bytes" & Environment.NewLine & Environment.NewLine &
                          "The stored designs and results may not be valid for the changed model. Continue anyway?", MsgBoxStyle.YesNo Or MsgBoxStyle.Exclamation) <> MsgBoxResult.Yes Then
                    Message = "Load BackUp cancelled (model changed)" : Return False
                End If
            End If
        End If
        Dim Best As String = If(Double.IsInfinity(Results.GlobalBest.PenalizedCost) OrElse Results.GlobalBest.DesignVariables Is Nothing, "no feasible design yet", Results.GlobalBest.CostValue.ToString("F2"))
        If MsgBox("Continue this run?" & Environment.NewLine & Environment.NewLine &
                  "  model:     " & BackupModel & Environment.NewLine &
                  "  saved:     " & If(Results.SavedAt = Date.MinValue, "(old backup)", Results.SavedAt.ToString("yyyy-MM-dd HH:mm:ss")) & Environment.NewLine &
                  "  method:    " & Results.FormInfo.OptInfo.OptimizationMethod.ToString() & ", seed " & Results.FormInfo.Seed & ", memory " & Results.Memory.Count & Environment.NewLine &
                  "  analyses:  " & Results.iter & " / " & Results.FormInfo.OptInfo.MaxFuncEvaluation & ", loop " & Results.ILoop & Environment.NewLine &
                  "  best cost: " & Best & If(Results.Model Is Nothing, Environment.NewLine & Environment.NewLine & "Old backup without model data: the model is checked after ETABS is opened.", ""),
                  MsgBoxStyle.YesNo Or MsgBoxStyle.Question) <> MsgBoxResult.Yes Then
            Message = "Load BackUp cancelled" : Return False
        End If
        Results.FormInfo.FileList.ETABSFile = BackupModel
        Results.FormInfo.FileList.OutputFile = OutputLoc.Text      'the selected output (the files may have been moved)
        RestoredModel = Results.Model
        OptClass.Memory = Results.Memory
        FormInfo = Results.FormInfo
        OptClass.GlobalBest = Results.GlobalBest
        OptClass.GlobalBestPrint = Results.GlobalBestPrint
        OptClass.BestValue = Results.BestValue
        OptClass.Histories = Results.Histories
        OptClass.iter = Results.iter
        OptClass.ILoop = Results.ILoop
        Write_form()
        Return True
    End Function

    Private FromBackUp As Boolean
    Private BackupMessage As String

    Private Shared Function PathsEqual(ByVal a As String, ByVal b As String) As Boolean
        Try
            Return String.Equals(Path.GetFullPath(a).TrimEnd("\"c), Path.GetFullPath(b).TrimEnd("\"c), StringComparison.OrdinalIgnoreCase)
        Catch
            Return String.Equals(a, b, StringComparison.OrdinalIgnoreCase)
        End Try
    End Function

    'Restored run, after ETABS has read the model: the design variables of the backup must belong to its groups and
    'section library, otherwise the run cannot continue
    Private Function CheckRestoredModel() As String
        Dim Now_ As ModelIdentity_ = ETABSModel.Identity()
        If RestoredModel IsNot Nothing AndAlso RestoredModel.GroupNames IsNot Nothing Then
            If Not RestoredModel.GroupNames.SequenceEqual(Now_.GroupNames) Then
                Return "the design groups of the model (" & String.Join(", ", Now_.GroupNames) & ") differ from those of the backup (" & String.Join(", ", RestoredModel.GroupNames) & ")"
            End If
            If RestoredModel.SectionCount <> Now_.SectionCount Then Return "the section library has " & Now_.SectionCount & " W sections, the backup " & RestoredModel.SectionCount
        End If
        For Each M In OptClass.Memory
            If M.DesignVariables.Length <> Now_.GroupNames.Count Then Return "the backup has " & M.DesignVariables.Length & " design variables, the model " & Now_.GroupNames.Count & " design groups"
            If M.DesignVariables.Any(Function(x) x < 0 OrElse x >= Now_.SectionCount) Then Return "a design of the backup uses a section index outside the section library"
        Next
        'the model data of an old backup is completed for the next backups
        OptClass.Model = If(RestoredModel, Now_)
        If OptClass.Model.GroupNames Is Nothing Then OptClass.Model = Now_
        Return Nothing
    End Function

    'form thread: input check, backup, form values -> FormInfo
    Private Sub PrepareRun(ByRef ret As Integer)
        OptClass = New OptimizationClass()
        FromBackUp = BackUp.Checked
        BackupMessage = Nothing
        If FromBackUp Then
            If String.IsNullOrWhiteSpace(OutputLoc.Text) Then : MsgBox("Load BackUp: enter the output file of the interrupted run") : ret = -1 : Exit Sub : End If
            If Not Backup_Read(BackupMessage) Then : MsgBox(BackupMessage) : ret = -1 : Exit Sub : End If
            FormInfo_Write()
            If Control() = True Then : ret = -1 : Exit Sub : End If
        Else
            If Control() = True Then : ret = -1 : Exit Sub : End If
            FormInfo_Read()
        End If
        StartTimeBox.Text = FormInfo.TimerInfo.StartTime
    End Sub

    'worker: ETABS model, initial memory
    Private Sub Init(ByRef ret As Integer)
        'backup: the generator state cannot be restored, continue with a seed derived from the loop number
        SetRandomSeed(If(FromBackUp, FormInfo.Seed + OptClass.ILoop, FormInfo.Seed))
        OptClass.FormInfo = FormInfo
        OptClass.FileList = FormInfo.FileList
        If FormInfo.OptInfo.TestWithMath Then
            OptClass.Math_Init()
        Else
            ETABSModel = New ETABS_Class(FormInfo, ret)
            If ret <> 0 Then : LogError("Error occurred in ETABS_Class") : Exit Sub : End If
            OptClass.ETABSModel = ETABSModel
            OptClass.Ub = ETABSModel.Ub
            OptClass.Lb = ETABSModel.Lb
            If BackupMessage IsNot Nothing Then ETABSModel.Errorlogprint(BackupMessage)
            If FromBackUp Then
                Dim Problem As String = CheckRestoredModel()
                If Problem IsNot Nothing Then
                    LogError("The backup does not belong to this model: " & Problem)
                    ETABS_Class.ShowMessage("The run cannot be continued: " & Problem & ".", MsgBoxStyle.Critical)
                    ret = -1
                    Exit Sub
                End If
            Else
                OptClass.Model = ETABSModel.Identity()
            End If
            'result cache on disk: kept by a restarted run, cleared by a new one
            ETABSModel.AttachCacheFile(Path.ChangeExtension(FormInfo.FileList.OutputFile, ".cache.txt"), FromBackUp)
            UI(Sub()
                   StartTimeBox.Text = ETABSModel.FormInfo.TimerInfo.StartTime
                   ShowModelInfo()
               End Sub)
        End If
        UI(Sub() DateBox.Text = Date.Now.ToString("yyyy-MM-dd"))
        RunClock = Stopwatch.StartNew()
        BackupClock = Stopwatch.StartNew()
        IterAtStart = If(FromBackUp, OptClass.iter, 0)
        If FromBackUp Then
            CompleteRestoredMemory(ret)
            Exit Sub
        End If
        'a new run: the backup of an earlier run with this output file is no longer valid (the cache is cleared too)
        Dim OldBackup As String = OptimizationClass.BackupPath(FormInfo.FileList.OutputFile)
        For Each f In {OldBackup, OldBackup & ".bak"}
            If File.Exists(f) Then
                Try
                    File.Delete(f)
                    LogError("Info: backup of an earlier run removed: " & f)
                Catch ex As Exception
                    LogError("Warning: backup of an earlier run not removed: " & f & " (" & ex.Message & ")")
                End Try
            End If
        Next

        OptClass.Memory = New List(Of OptimizationStructure_.Member_)
        OptClass.BestValue = Double.PositiveInfinity
        OptClass.GlobalBest.PenalizedCost = Double.PositiveInfinity
        OptClass.Histories = New List(Of OptimizationStructure_.History_)
        ReDim OptClass.GlobalBest.DesignVariables(OptClass.Ub.Count - 1)
        OptClass.iter = 0
        For i = 0 To FormInfo.OptInfo.MemorySize - 1
            SetPhase("Initial memory: design " & (i + 1) & " / " & FormInfo.OptInfo.MemorySize)
            Dim Member As New OptimizationStructure_.Member_
            OptClass.RandomGenerate(Member, 0, ret)
            If ret <> 0 Then : LogError("Error occurred in RandomGenerate") : Exit Sub : End If
            OptClass.Memory.Add(Member)
            UI(AddressOf Write_form)
            If StopRequested Then Exit For
        Next i
        OptClass.ILoop = 0
        If FormInfo.OptInfo.OptimizationMethod = OptimizationStructure_.OptMethod_.HarmornySearch Then OptClass.Init_HarmonySearch()
        If FormInfo.OptInfo.OptimizationMethod = OptimizationStructure_.OptMethod_.BioGBasedO Then OptClass.Init_BioGeographyBased()
        OptClass.InitMethodState()
    End Sub
    'Restored run: a backup written while the initial memory was generated (stop, power failure) has fewer members and
    'no algorithm vectors yet: the memory is completed and the vectors are created
    Private Sub CompleteRestoredMemory(ByRef ret As Integer)
        If OptClass.GlobalBest.DesignVariables Is Nothing OrElse OptClass.GlobalBest.DesignVariables.Length <> OptClass.Ub.Length Then
            ReDim OptClass.GlobalBest.DesignVariables(OptClass.Ub.Length - 1)
            OptClass.GlobalBest.PenalizedCost = Double.PositiveInfinity
        End If
        If OptClass.Histories Is Nothing Then OptClass.Histories = New List(Of OptimizationStructure_.History_)
        Dim Missing As Integer = FormInfo.OptInfo.MemorySize - OptClass.Memory.Count
        If Missing > 0 Then LogError("Info: backup written during the initial memory: " & Missing & " designs generated now")
        While OptClass.Memory.Count < FormInfo.OptInfo.MemorySize
            SetPhase("Initial memory: design " & (OptClass.Memory.Count + 1) & " / " & FormInfo.OptInfo.MemorySize)
            Dim Member As New OptimizationStructure_.Member_
            OptClass.RandomGenerate(Member, 0, ret)
            If ret <> 0 Then : LogError("Error occurred in RandomGenerate") : Exit Sub : End If
            OptClass.Memory.Add(Member)
            UI(AddressOf Write_form)
            If StopRequested Then Exit Sub
        End While
        With FormInfo.OptInfo
            If .OptimizationMethod = OptimizationStructure_.OptMethod_.HarmornySearch AndAlso
               (.HarmonySearch.ParVec Is Nothing OrElse .HarmonySearch.ParVec.Length <> OptClass.Memory.Count OrElse
                .HarmonySearch.HMCRVec Is Nothing OrElse .HarmonySearch.HMCRVec.Length <> OptClass.Memory.Count) Then OptClass.Init_HarmonySearch()
            If .OptimizationMethod = OptimizationStructure_.OptMethod_.BioGBasedO AndAlso
               (.BioGeography.Mu Is Nothing OrElse .BioGeography.Mu.Length <> OptClass.Memory.Count) Then OptClass.Init_BioGeographyBased()
        End With
        OptClass.InitMethodState(OnlyIfMissing:=True)
    End Sub

    'Model size on the Structural Properties tab
    Private Sub ShowModelInfo()
        NofJoint.Text = ETABSModel.Points.Length.ToString()
        nofmember.Text = ETABSModel.Frames.Length.ToString()
        nofgroup.Text = ETABSModel.SteelFrameDesignGroupIDs.Count.ToString()
        nofsection1.Text = ETABSModel.WSections.Count.ToString()
    End Sub

    Private Shared Function FormatSpan(ByVal t As TimeSpan) As String
        Return If(t.Days > 0, t.Days & "d ", "") & t.Hours.ToString("00") & ":" & t.Minutes.ToString("00") & ":" & t.Seconds.ToString("00")
    End Function

    Private Sub Write_form()
        Dim MaxIter As Double = Math.Max(FormInfo.OptInfo.MaxFuncEvaluation, 1)
        TextBox1.Text = OptClass.iter & " / " & FormInfo.OptInfo.MaxFuncEvaluation
        ProgressBar1.Value = CInt(Math.Min(Math.Max(100.0 * OptClass.iter / MaxIter, 0), 100))
        If Not Double.IsInfinity(OptClass.GlobalBest.PenalizedCost) Then BestCostBox.Text = OptClass.GlobalBest.CostValue.ToString("F2")
        If RunClock IsNot Nothing Then
            ElapsedBox.Text = FormatSpan(RunClock.Elapsed)
            Dim Done As Integer = OptClass.iter - IterAtStart
            If Done > 0 Then
                Dim PerAnalysis As Double = RunClock.Elapsed.TotalSeconds / Done
                AverageTimeBox.Text = PerAnalysis.ToString("F1")
                RemainingBox.Text = FormatSpan(TimeSpan.FromSeconds(PerAnalysis * Math.Max(MaxIter - OptClass.iter, 0)))
            End If
        End If
        If OptClass.GlobalBest.PenalizedCost <> Double.PositiveInfinity AndAlso OptClass.GlobalBestPrint IsNot Nothing Then
            ListBox1.Items.Clear()
            For Each it In OptClass.GlobalBestPrint
                ListBox1.Items.Add(it)
            Next
        End If
        ListBox2.Items.Clear()
        ListBox2.Items.Add("History")
        ListBox2.Items.Add("Iter   Loop   Cost")
        For Each h In OptClass.Histories
            ListBox2.Items.Add(CStr(h.Iter) & " " & CStr(h.ILoop) & " " & CStr(h.Cost))
        Next
        Me.Refresh()
    End Sub

    'Checks the sections of an optimization output file (GlobalBestPrint) without modifying them
    'worker (form input read by Start_Click)
    Private Sub Check_Structure(ByRef ret As Integer)
        SetRandomSeed(FormInfo.Seed)
        ETABSModel = New ETABS_Class(FormInfo, ret)
        If ret <> 0 Then : LogError("Error occurred in ETABS_Class") : Exit Sub : End If
        UI(AddressOf ShowModelInfo)
        SetPhase("Check Structure: analysis and design of the sections of the output file")
        Dim Sect_ID() As Integer = Read_SectionID(ret)
        If ret <> 0 Then : LogError("Error occurred in Read_SectionID") : Exit Sub : End If
        ret = ETABSModel.SetAndAnalyze(Sect_ID, False)
        If ret <> 0 Then : LogError("Error occurred in SetAndAnalyze") : Exit Sub : End If
        Dim Penalty As Double = -1
        ETABSModel.Penalty(Penalty, Sect_ID, ret, applyRepair:=False)
        If ret <> 0 Then : LogError("Error occurred in Penalty") : Exit Sub : End If
        ret = ETABSModel.VerifyCompositeWithETABS()      'ETABS composite column design -> ETABS_print (check.xml)
        If ret <> 0 Then : LogError("Error occurred in VerifyCompositeWithETABS") : Exit Sub : End If
        ETABSModel.ETABS_print.Penalty = Penalty
        ETABSModel.ETABS_print.Cost = ETABSModel.CostStProfile(Sect_ID)
        ETABSModel.ETABS_print.AnalysisFailed = ETABSModel.AnalysisFailed
        ETABSModel.ETABS_print.CostBreakdown = ETABSModel.CostBreakdown(Sect_ID)
        ETABSModel.Errorlogprint("Info: checked design: cost " & Num(ETABSModel.ETABS_print.Cost) & ", penalty " & Num(Penalty) & If(ETABSModel.AnalysisFailed, " (analysis not finished)", ""))
        Dim serializer As New XmlSerializer(GetType(ETABS_Print))
        Using writer As New StreamWriter(Path.ChangeExtension(FormInfo.FileList.OutputFile, ".check.xml"))
            serializer.Serialize(writer, ETABSModel.ETABS_print)
        End Using
        Try
            ETABSModel.Errorlogprint("Info: check workbook " & ExcelExport.WriteCheck(Path.ChangeExtension(FormInfo.FileList.OutputFile, ".check.xlsx"), ETABSModel.ETABS_print, FormInfo))
        Catch ex As Exception
            ETABSModel.Errorlogprint("Warning: Excel workbook not written: " & ex.Message)
        End Try
        Dim ETABSMax As Double = If(ETABSModel.ETABSRatioByVar.Count > 0, ETABSModel.ETABSRatioByVar.Values.Max(), 0)
        Dim Fails As Boolean = Penalty > 0 OrElse ETABSMax > 1 OrElse ETABSModel.AnalysisFailed
        ETABSModel.Close(ret, If(Fails, "The checked design does not satisfy all checks (penalty " & Num(Penalty) & ", ETABS composite ratio max " & Num(ETABSMax) &
                                       "). See ErrorLog.txt and the .check.xml file.", Nothing))
    End Sub
    'Sections of an output file, matched by group name ("<GroupName>: <SectionName> [composite info]")
    Private Function Read_SectionID(ByRef ret As Integer) As Integer()
        Dim Sect_ID(ETABSModel.SteelFrameDesignGroupIDs.Count - 1) As Integer
        Dim OutputFile As String = FormInfo.FileList.OutputFile
        If Not File.Exists(OutputFile) Then : ETABSModel.Errorlogprint("Output file to check not found: " & OutputFile) : ret = -1 : Return Sect_ID : End If
        Dim xmldoc As New XmlDocument()
        xmldoc.Load(OutputFile)
        Dim xmlnode As XmlNodeList = xmldoc.GetElementsByTagName("GlobalBestPrint")
        If xmlnode.Count = 0 Then : ETABSModel.Errorlogprint("No GlobalBestPrint in " & OutputFile) : ret = -1 : Return Sect_ID : End If
        Dim ByGroup As New Dictionary(Of String, String)
        Dim CompGroups As New HashSet(Of String)
        For Each item As XmlNode In xmlnode(0).ChildNodes
            Dim parts() As String = item.InnerText.Split(":".ToCharArray(), 2)
            If parts.Length = 2 Then
                ByGroup(parts(0).Trim()) = parts(1).Trim().Split(" "c)(0)
                If parts(1).Contains("[CFT ") OrElse parts(1).Contains("[CFP ") OrElse parts(1).Contains("[EC ") Then CompGroups.Add(parts(0).Trim())
            End If
        Next
        'hybrid columns: the type of every group comes from the output file
        If ETABSModel.Hybrid Then ETABSModel.SetGroupTypes(ETABSModel.SteelFrameDesignGroupIDs.Select(Function(id) CompGroups.Contains(ETABSModel.Groups(id).GroupName)).ToArray())
        For j = 0 To ETABSModel.SteelFrameDesignGroupIDs.Count - 1
            Dim G As String = ETABSModel.Groups(ETABSModel.SteelFrameDesignGroupIDs(j)).GroupName
            Dim Sname As String = Nothing
            If Not ByGroup.TryGetValue(G, Sname) Then : ETABSModel.Errorlogprint("Group " & G & " not found in " & OutputFile) : ret = -1 : Continue For : End If
            Sect_ID(j) = ETABSModel.FindSection(j, Sname)
            If Sect_ID(j) < 0 Then : ETABSModel.Errorlogprint("Section not found in library: " & Sname) : ret = -1 : End If
        Next j
        Return Sect_ID
    End Function
End Class
