Imports System.Globalization
Imports System.IO
Imports System.Runtime.InteropServices
Imports System.Windows.Forms

'Command line:
'  ModelBuilder.exe                                            the form
'  ModelBuilder.exe /template <file.csv>                       writes a CSV with all columns and the default values
'  ModelBuilder.exe /plan <examples.csv> [/only <name>]        geometry and groups of every example (no ETABS)
'  ModelBuilder.exe /build <examples.csv> /out <folder> [/hide] [/overwrite] [/only <name>]
'                                                              one ETABS model <name>.EDB and report per example
Public Module Program
    <DllImport("kernel32.dll")>
    Private Function FreeConsole() As Boolean
    End Function

    Private LogFile As String

    Private Sub Say(ByVal Text As String)
        Dim Line As String = Date.Now.ToString("HH:mm:ss") & " " & Text
        Console.WriteLine(Line)
        If LogFile IsNot Nothing Then
            Try
                File.AppendAllText(LogFile, Line & Environment.NewLine)
            Catch
            End Try
        End If
    End Sub

    Private Function Arg(ByVal A() As String, ByVal Name As String) As String
        Dim i As Integer = Array.FindIndex(A, Function(x) x.Equals(Name, StringComparison.OrdinalIgnoreCase))
        Return If(i >= 0 AndAlso i + 1 < A.Length, A(i + 1), Nothing)
    End Function

    Private Function Has(ByVal A() As String, ByVal Name As String) As Boolean
        Return A.Any(Function(x) x.Equals(Name, StringComparison.OrdinalIgnoreCase))
    End Function

    <STAThread>
    Public Function Main(ByVal A() As String) As Integer
        If A.Length = 0 Then
            FreeConsole()
            Application.EnableVisualStyles()
            Application.Run(New BuilderForm)
            Return 0
        End If
        Console.OutputEncoding = System.Text.Encoding.UTF8
        Try
            If Has(A, "/template") Then
                Dim F As String = Arg(A, "/template")
                If F Is Nothing Then Return Usage()
                BuildParams_.WriteTemplate(F)
                Say("template written: " & F)
                Return 0
            End If
            Dim Csv As String = If(Has(A, "/plan"), Arg(A, "/plan"), If(Has(A, "/build"), Arg(A, "/build"), Nothing))
            If Csv Is Nothing Then Return Usage()
            Dim Errors As List(Of String) = Nothing
            Dim List = BuildParams_.ReadCsv(Csv, Errors)
            If Errors.Count > 0 Then
                For Each E In Errors : Say("error: " & E) : Next
                Return 1
            End If
            Dim Only As String = Arg(A, "/only")
            If Only IsNot Nothing Then List = List.Where(Function(p) p.Name.Equals(Only, StringComparison.OrdinalIgnoreCase)).ToList()
            If List.Count = 0 Then Say("no example to process") : Return 1
            If Has(A, "/plan") Then
                For Each P In List
                    Say("== " & P.Name)
                    Console.Write(BuildPlan_.Create(P).Describe())
                Next
                Return 0
            End If
            Dim OutDir As String = Arg(A, "/out")
            If OutDir Is Nothing Then Return Usage()
            Directory.CreateDirectory(OutDir)
            LogFile = Path.Combine(Path.GetFullPath(OutDir), "builder.log")
            Return BuildRunner.Run(List, OutDir, Has(A, "/hide"), Has(A, "/overwrite"), AddressOf Say, Nothing)
        Catch ex As Exception
            Say("error: " & ex.Message)
            Return 1
        End Try
    End Function

    Private Function Usage() As Integer
        Console.WriteLine("ModelBuilder.exe                       (form)")
        Console.WriteLine("ModelBuilder.exe /template <file.csv>")
        Console.WriteLine("ModelBuilder.exe /plan <examples.csv> [/only <name>]")
        Console.WriteLine("ModelBuilder.exe /build <examples.csv> /out <folder> [/hide] [/overwrite] [/only <name>]")
        Return 2
    End Function
End Module
