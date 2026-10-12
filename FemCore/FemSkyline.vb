Imports System.Collections.Generic

'Symmetric positive definite matrix in profile (skyline) storage: row i keeps the columns First(i) .. i. The Cholesky factor has the same
'profile, so the cost depends on the bandwidth only; the DOF are ordered with the reverse Cuthill-McKee algorithm before the assembly.
Public Class SkylineMatrix
    Public ReadOnly N As Integer
    Public ReadOnly First() As Integer
    Public ReadOnly Row()() As Double
    Public Factored As Boolean
    Public FailedRow As Integer = -1            'first row with a non-positive pivot after a failed Factor
    Private DiagOriginal() As Double

    Public Sub New(ByVal Size As Integer, ByVal FirstColumn() As Integer)
        N = Size
        First = FirstColumn
        ReDim Row(N - 1)
        For i = 0 To N - 1 : Row(i) = New Double(i - First(i)) {} : Next
    End Sub

    Public Sub Add(ByVal i As Integer, ByVal j As Integer, ByVal v As Double)
        If j > i Then Dim t As Integer = i : i = j : j = t
        Row(i)(j - First(i)) += v
    End Sub

    Public Function Diag(ByVal i As Integer) As Double
        Return Row(i)(i - First(i))
    End Function

    Public Function Clone() As SkylineMatrix
        Dim C As New SkylineMatrix(N, First)
        For i = 0 To N - 1 : Array.Copy(Row(i), C.Row(i), Row(i).Length) : Next
        Return C
    End Function

    'In place Cholesky factorisation K = L L'; False: a pivot is not positive (unstable structure), FailedRow tells where
    Public Function Factor() As Boolean
        ReDim DiagOriginal(N - 1)
        For i = 0 To N - 1 : DiagOriginal(i) = Diag(i) : Next
        For i = 0 To N - 1
            Dim fi As Integer = First(i)
            Dim ri() As Double = Row(i)
            For j = fi To i
                Dim fj As Integer = First(j)
                Dim rj() As Double = Row(j)
                Dim k0 As Integer = Math.Max(fi, fj)
                Dim s As Double = ri(j - fi)
                For k = k0 To j - 1 : s -= ri(k - fi) * rj(k - fj) : Next
                If j < i Then
                    ri(j - fi) = s / rj(j - fj)
                Else
                    If s <= 1.0E-12 * Math.Abs(DiagOriginal(i)) OrElse Double.IsNaN(s) Then FailedRow = i : Return False
                    ri(j - fi) = Math.Sqrt(s)
                End If
            Next
        Next
        Factored = True
        Return True
    End Function

    'Solution of K x = b with the factor
    Public Function Solve(ByVal b() As Double) As Double()
        Dim y(N - 1) As Double
        For i = 0 To N - 1
            Dim fi As Integer = First(i)
            Dim ri() As Double = Row(i)
            Dim s As Double = b(i)
            For k = fi To i - 1 : s -= ri(k - fi) * y(k) : Next
            y(i) = s / ri(i - fi)
        Next
        For i = N - 1 To 0 Step -1
            Dim fi As Integer = First(i)
            Dim ri() As Double = Row(i)
            y(i) /= ri(i - fi)
            Dim xi As Double = y(i)
            For k = fi To i - 1 : y(k) -= ri(k - fi) * xi : Next
        Next
        Return y
    End Function

    Public Function ToDense() As Double(,)
        Dim D(N - 1, N - 1) As Double
        For i = 0 To N - 1
            For j = First(i) To i
                D(i, j) = Row(i)(j - First(i)) : D(j, i) = D(i, j)
            Next
        Next
        Return D
    End Function

    'Reverse Cuthill-McKee ordering of a graph: returns Perm(old) = new index. Neighbours: adjacency lists (without the node itself).
    Public Shared Function ReverseCuthillMcKee(ByVal Adj As List(Of Integer)()) As Integer()
        Dim n As Integer = Adj.Length
        Dim Order As New List(Of Integer)(n)
        Dim Visited(n - 1) As Boolean
        Dim Degree(n - 1) As Integer
        For i = 0 To n - 1 : Degree(i) = Adj(i).Count : Next
        Dim Remaining As List(Of Integer) = Enumerable.Range(0, n).OrderBy(Function(i) Degree(i)).ToList()
        For Each Start In Remaining
            If Visited(Start) Then Continue For
            'pseudo-peripheral start node: repeat the breadth first search from the last node of the previous level structure
            Dim S As Integer = Start
            Dim LastDepth As Integer = -1
            For attempt = 0 To 4
                Dim Depth As Integer = 0
                Dim Far As Integer = LevelFarthest(Adj, Degree, S, Depth)
                If Depth <= LastDepth Then Exit For
                LastDepth = Depth : S = Far
            Next
            Dim Queue As New Queue(Of Integer)
            Queue.Enqueue(S) : Visited(S) = True
            While Queue.Count > 0
                Dim v As Integer = Queue.Dequeue()
                Order.Add(v)
                For Each w In Adj(v).Where(Function(x) Not Visited(x)).OrderBy(Function(x) Degree(x))
                    If Not Visited(w) Then Visited(w) = True : Queue.Enqueue(w)
                Next
            End While
        Next
        Dim Perm(n - 1) As Integer
        For k = 0 To n - 1 : Perm(Order(n - 1 - k)) = k : Next
        Return Perm
    End Function

    'Node of the last level (smallest degree) of the breadth first search from S, and the number of levels
    Private Shared Function LevelFarthest(ByVal Adj As List(Of Integer)(), ByVal Degree() As Integer, ByVal S As Integer, ByRef Levels As Integer) As Integer
        Dim Dist As New Dictionary(Of Integer, Integer)
        Dim Queue As New Queue(Of Integer)
        Queue.Enqueue(S) : Dist(S) = 0
        Dim Last As Integer = S
        While Queue.Count > 0
            Dim v As Integer = Queue.Dequeue()
            For Each w In Adj(v)
                If Not Dist.ContainsKey(w) Then Dist(w) = Dist(v) + 1 : Queue.Enqueue(w)
            Next
        End While
        Dim Max As Integer = Dist.Values.Max()
        Levels = Max
        Dim Best As Integer = S, BestDeg As Integer = Integer.MaxValue
        For Each kv In Dist
            If kv.Value = Max AndAlso Degree(kv.Key) < BestDeg Then Best = kv.Key : BestDeg = Degree(kv.Key)
        Next
        Return Best
    End Function
End Class
