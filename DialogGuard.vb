Imports System.Runtime.InteropServices
Imports System.Text
Imports System.Threading

'Answers the message boxes of a hidden ETABS instance. A message box of a hidden ETABS cannot be seen and blocks the API call
'for ever (e.g. "6 Steel frames with auto select sections failed stress/capacity check. Do you want to select them?" in the
'initial design), so a long unattended run stops without any message. Only the dialogs of the ETABS process started by the
'program are touched: a question with Yes / No buttons is answered No, a message with one OK button is answered OK, every
'other dialog is only logged. Every answer is logged as a warning.
Friend NotInheritable Class DialogGuard
    Private Delegate Function EnumProc(ByVal h As IntPtr, ByVal l As IntPtr) As Boolean
    <DllImport("user32.dll")>
    Private Shared Function EnumWindows(ByVal p As EnumProc, ByVal l As IntPtr) As Boolean
    End Function
    <DllImport("user32.dll")>
    Private Shared Function EnumChildWindows(ByVal p As IntPtr, ByVal cb As EnumProc, ByVal l As IntPtr) As Boolean
    End Function
    <DllImport("user32.dll")>
    Private Shared Function GetWindowThreadProcessId(ByVal h As IntPtr, ByRef pid As UInteger) As UInteger
    End Function
    <DllImport("user32.dll", CharSet:=CharSet.Unicode)>
    Private Shared Function GetWindowText(ByVal h As IntPtr, ByVal s As StringBuilder, ByVal n As Integer) As Integer
    End Function
    <DllImport("user32.dll", CharSet:=CharSet.Unicode)>
    Private Shared Function GetClassName(ByVal h As IntPtr, ByVal s As StringBuilder, ByVal n As Integer) As Integer
    End Function
    <DllImport("user32.dll")>
    Private Shared Function IsWindowVisible(ByVal h As IntPtr) As Boolean
    End Function
    <DllImport("user32.dll")>
    Private Shared Function PostMessage(ByVal h As IntPtr, ByVal m As Integer, ByVal w As IntPtr, ByVal l As IntPtr) As Boolean
    End Function

    Private Const BM_CLICK As Integer = &HF5
    Private Const POLL_MS As Integer = 2000

    Private Shared Worker As Thread
    Private Shared Stopping As Boolean
    Private Shared EtabsPid As Integer
    Private Shared Log As Action(Of String)
    Private Shared LastWindow As IntPtr

    'Starts the guard for the ETABS process Pid (a running guard is replaced)
    Public Shared Sub StartFor(ByVal Pid As Integer, ByVal LogLine As Action(Of String))
        [Stop]()
        If Pid <= 0 Then Return
        EtabsPid = Pid : Log = LogLine : Stopping = False
        Worker = New Thread(AddressOf Loop1) With {.IsBackground = True, .Name = "ETABS dialog guard"}
        Worker.Start()
    End Sub

    Public Shared Sub [Stop]()
        Stopping = True
        Worker = Nothing
    End Sub

    Private Shared Sub Loop1()
        Dim Mine As Thread = Thread.CurrentThread
        Do
            Thread.Sleep(POLL_MS)
            If Stopping OrElse Not Object.ReferenceEquals(Worker, Mine) Then Return
            Try
                Check()
            Catch
            End Try
        Loop
    End Sub

    Private Shared Sub Check()
        EnumWindows(Function(h, l)
                        Dim P As UInteger = 0
                        GetWindowThreadProcessId(h, P)
                        If CInt(P) <> EtabsPid OrElse Not IsWindowVisible(h) Then Return True
                        Dim Cls As New StringBuilder(64)
                        GetClassName(h, Cls, 64)
                        If Cls.ToString() <> "#32770" Then Return True
                        Dim Yes As IntPtr = IntPtr.Zero, No As IntPtr = IntPtr.Zero, Ok As IntPtr = IntPtr.Zero, Buttons As Integer = 0
                        Dim Msg As New StringBuilder
                        EnumChildWindows(h, Function(k, l2)
                                                Dim C As New StringBuilder(64), T As New StringBuilder(1024)
                                                GetClassName(k, C, 64) : GetWindowText(k, T, 1024)
                                                If C.ToString() = "Button" Then
                                                    Buttons += 1
                                                    Select Case T.ToString().Replace("&", "")
                                                        Case "Yes" : Yes = k
                                                        Case "No" : No = k
                                                        Case "OK" : Ok = k
                                                    End Select
                                                ElseIf C.ToString() = "Static" AndAlso T.Length > 0 Then
                                                    If Msg.Length > 0 Then Msg.Append(" ")
                                                    Msg.Append(T.ToString().Replace(vbCr, " ").Replace(vbLf, " "))
                                                End If
                                                Return True
                                            End Function, IntPtr.Zero)
                        Dim Answer As String = Nothing, Target As IntPtr = IntPtr.Zero
                        If Yes <> IntPtr.Zero AndAlso No <> IntPtr.Zero Then
                            Answer = "No" : Target = No
                        ElseIf Ok <> IntPtr.Zero AndAlso Buttons = 1 Then
                            Answer = "OK" : Target = Ok
                        End If
                        If Log IsNot Nothing AndAlso h <> LastWindow Then
                            LastWindow = h
                            Log("Warning: ETABS dialog" & If(Answer Is Nothing, " (not answered): ", " answered " & Answer & ": ") & Msg.ToString())
                        End If
                        If Target <> IntPtr.Zero Then PostMessage(Target, BM_CLICK, IntPtr.Zero, IntPtr.Zero)
                        Return True
                    End Function, IntPtr.Zero)
    End Sub
End Class
