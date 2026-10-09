Imports System.Globalization
Imports System.IO
Imports System.Text

'Builds the examples of a list (used by the command line and by the form) and writes the summary files.
Public Class BuildRunner
    Public Shared Function Run(ByVal List As List(Of BuildParams_), ByVal OutDir As String, ByVal Hide As Boolean, ByVal Overwrite As Boolean,
                               ByVal Say As Action(Of String), ByVal Cancelled As Func(Of Boolean)) As Integer
        Directory.CreateDirectory(OutDir)
        OutDir = Path.GetFullPath(OutDir)
        If OutDir.Length > 150 Then Say("error: the output folder path is too long (" & OutDir.Length & " characters, at most 150)") : Return 1
        Dim Todo As New List(Of BuildParams_)
        For Each P In List
            If File.Exists(Path.Combine(OutDir, P.Name & ".EDB")) AndAlso Not Overwrite Then
                Say("skipped (the model exists, use overwrite): " & P.Name)
            Else
                Todo.Add(P)
            End If
        Next
        If Todo.Count = 0 Then Return 0
        Dim Exe As String = EtabsBuilder.FindETABS()
        Dim LibFile As String = If(Exe Is Nothing, Nothing, EtabsBuilder.FindLibrary(Exe))
        If LibFile Is Nothing Then Say("error: ETABS or its section library not found (ETABSProgramPath, SectionPropertyDataPath)") : Return 1
        Dim Builder As New EtabsBuilder With {.Log = Say, .Hide = Hide}
        Dim Failed As Integer, Done As New List(Of Tuple(Of BuildParams_, BuildPlan_, EtabsBuilder.AuditResult_, Integer, Integer))
        Try
            Builder.Start()
            For Each P In Todo
                If Cancelled IsNot Nothing AndAlso Cancelled() Then Say("stopped by the user") : Exit For
                Say("== " & P.Name)
                Try
                    Dim Plan = BuildPlan_.Create(P)
                    Builder.Build(Plan, LibFile, OutDir)
                    Done.Add(Tuple.Create(P, Plan, Builder.Result, Builder.BeamPoolSize, Builder.ColumnPoolSize))
                    Say("done: " & P.Name)
                Catch ex As Exception
                    Failed += 1
                    Say("FAILED: " & P.Name & ": " & ex.Message)
                End Try
            Next
        Catch ex As Exception
            Say("error: " & ex.Message)
            Failed += 1
        Finally
            Builder.Shutdown()
        End Try
        If Done.Count > 0 Then WriteSummary(OutDir, Done, Say)
        Say(Done.Count & " of " & Todo.Count & " models built" & If(Failed > 0, ", " & Failed & " failed", ""))
        Return If(Failed = 0, 0, 1)
    End Function

    Private Shared Function N(ByVal x As Double, Optional ByVal Fmt As String = "0.###") As String
        Return x.ToString(Fmt, CultureInfo.InvariantCulture)
    End Function

    'examples_summary.csv: one row per model; runs_template.csv: the list of the optimization program (tools\RunBatch.ps1) to edit
    Private Shared Sub WriteSummary(ByVal OutDir As String, ByVal Done As List(Of Tuple(Of BuildParams_, BuildPlan_, EtabsBuilder.AuditResult_, Integer, Integer)), ByVal Say As Action(Of String))
        Try
            Dim Sb As New StringBuilder
            Sb.AppendLine("Name,Model,Stories,PlanX_m,PlanY_m,Height_m,FrameSystem,StoryBand,ColumnGroups,BeamGroups,DesignVariables,BeamPool,ColumnPool,SeismicWeight_kN,Tx_s,Ty_s,Modes,ModalParticipation,ELF_Vx_kN,ELF_Vy_kN,RSScaleX,RSScaleY,WindVx_kN,WindVy_kN,FrameClass,SDC,R,Cd,Ie,DriftAmplification_CdOverIe")
            Dim Runs As New StringBuilder
            Runs.AppendLine("Name,Model,Method,Mode,MaxAnalyses,MemorySize,Seed,Transition,AbcLimit")
            For Each d In Done
                Dim P = d.Item1, Plan = d.Item2, R = d.Item3
                Sb.AppendLine(String.Join(",", {P.Name, P.Name & ".EDB", P.Stories.ToString(), N(Plan.XLines.Last()), N(Plan.YLines.Last()), N(Plan.Height), P.FrameSystem, P.StoryBand.ToString(), Plan.ColumnGroups.Count().ToString(), Plan.BeamGroups.Count().ToString(),
                    Plan.Groups.Count.ToString(), d.Item4.ToString(), d.Item5.ToString(), N(R.Weight, "0"), N(R.PeriodX, "0.000"), N(R.PeriodY, "0.000"), R.Modes.ToString(), N(R.Participation, "0.000"), N(R.ElfX, "0"), N(R.ElfY, "0"),
                    N(R.ScaleX, "0.0000"), N(R.ScaleY, "0.0000"), N(R.WindX, "0"), N(R.WindY, "0"), P.FrameClass, EtabsBuilder.SeismicDesignCategory(P), N(P.EffR()), N(P.EffCd()), N(P.Ie), N(P.EffCd() / P.Ie)}))
                Runs.AppendLine(String.Join(",", {P.Name, Path.Combine(OutDir, P.Name & ".EDB"), "HarmornySearch", "Steel", "500", "20", "1", "", ""}))
            Next
            File.WriteAllText(Path.Combine(OutDir, "examples_summary.csv"), Sb.ToString(), New UTF8Encoding(True))
            File.WriteAllText(Path.Combine(OutDir, "runs_template.csv"), Runs.ToString(), New UTF8Encoding(True))
            Say("summary written: examples_summary.csv, runs_template.csv (edit the methods and modes, then use tools\RunBatch.ps1)")
        Catch ex As Exception
            Say("Warning: the summary files could not be written: " & ex.Message)
        End Try
    End Sub
End Class
