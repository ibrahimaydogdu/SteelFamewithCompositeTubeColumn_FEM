Namespace My
    ' The following events are available for MyApplication:
    ' Startup: Raised when the application starts, before the startup form is created.
    ' Shutdown: Raised after all application forms are closed.  This event is not raised if the application terminates abnormally.
    ' UnhandledException: Raised if the application encounters an unhandled exception.
    ' StartupNextInstance: Raised when launching a single-instance application and the application is already active. 
    ' NetworkAvailabilityChanged: Raised when the network connection is connected or disconnected.
    Partial Friend Class MyApplication
        'Last line of defence (exceptions outside Start_Click): log, close ETABS and remove the working folder
        Private Sub MyApplication_UnhandledException(sender As Object, e As ApplicationServices.UnhandledExceptionEventArgs) Handles Me.UnhandledException
            Dim F As Global.FrameSap2000.MainForm = TryCast(Me.MainForm, Global.FrameSap2000.MainForm)
            If F IsNot Nothing AndAlso F.ETABSModel IsNot Nothing Then
                F.ETABSModel.Errorlogprint("Unhandled exception: " & e.Exception.ToString())
                F.ETABSModel.Shutdown()
            End If
        End Sub
    End Class
End Namespace
