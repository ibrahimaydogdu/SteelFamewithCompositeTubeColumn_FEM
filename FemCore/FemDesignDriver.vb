Imports System.Collections.Generic

'Design ratios of one member (envelope over the combinations and stations)
Public Class MemberRatios
    Public PMM, P, MMaj, MMin, VMaj, VMin As Double
    Public PMMCombo As String
    Public PMMStation As Double
End Class

Partial Public Class FemStatic
    Private LoadCache As Dictionary(Of Integer, List(Of LocalLoad)())
    Public TraceElement As Integer = -1

    Private Function CachedLoads(ByVal ei As Integer) As List(Of LocalLoad)()
        If LoadCache Is Nothing Then LoadCache = New Dictionary(Of Integer, List(Of LocalLoad)())
        Dim L As List(Of LocalLoad)() = Nothing
        If Not LoadCache.TryGetValue(ei, L) Then
            L = ElementLoads(ei, M.Elements(ei))
            LoadCache(ei) = L
        End If
        Return L
    End Function

    'Internal forces of pattern p at the distances xs from the start of the clear span of element ei: rows N (tension +), V2, V3, T, M2, M3
    'from the equilibrium of the part of the span before the cut (face forces + applied loads); M2 has the opposite sign of ETABS' M2
    Public Function StationForces(ByVal R As StaticResults, ByVal p As Integer, ByVal ei As Integer, ByVal xs() As Double) As Double()()
        Dim E = M.Elements(ei)
        Dim f = R.FaceForce(p)(ei)
        Dim a As Double = If(E.OffsetI + E.OffsetJ < E.Length, E.OffsetI, 0.0)
        Dim loads = CachedLoads(ei)(p)
        Dim Res(xs.Length - 1)() As Double
        For j = 0 To xs.Length - 1
            Dim x As Double = xs(j)
            Dim SF(2) As Double, Sm(2) As Double
            For c = 0 To 2 : SF(c) = -f(c) : Sm(c) = -f(3 + c) : Next
            'x (e1 x F_I) with e1 x (F1,F2,F3) = (0, -F3, F2)
            Sm(1) += x * (-f(2)) : Sm(2) += x * f(1)
            For Each ld In loads
                If ld.IsPoint Then
                    Dim xi As Double = ld.X1 - a
                    If xi > 0 AndAlso xi <= x + 0.000000001 Then
                        For c = 0 To 2 : SF(c) -= ld.P(c) : Sm(c) -= ld.M(c) : Next
                        Sm(1) -= (xi - x) * (-ld.P(2)) : Sm(2) -= (xi - x) * ld.P(1)
                    End If
                Else
                    Dim s0 As Double = Math.Max(ld.X1 - a, 0), s1 As Double = Math.Min(ld.X2 - a, x)
                    If s1 - s0 <= 0.000000001 Then Continue For
                    Dim gx() As Double = {-0.5773502691896257, 0.5773502691896257}
                    For g = 0 To 1
                        Dim xi As Double = 0.5 * (s0 + s1) + 0.5 * (s1 - s0) * gx(g)
                        Dim w As Double = 0.5 * (s1 - s0)
                        Dim tt As Double = If(ld.X2 - ld.X1 > 0, (xi + a - ld.X1) / (ld.X2 - ld.X1), 0.0)
                        Dim q(2) As Double
                        For c = 0 To 2 : q(c) = ld.Q1(c) + (ld.Q2(c) - ld.Q1(c)) * tt : Next
                        For c = 0 To 2 : SF(c) -= w * q(c) : Next
                        Sm(1) -= w * (xi - x) * (-q(2)) : Sm(2) -= w * (xi - x) * q(1)
                    Next
                End If
            Next
            Res(j) = New Double() {SF(0), SF(1), SF(2), Sm(0), Sm(1), Sm(2)}
        Next
        Return Res
    End Function

    'Stations of ETABS (P4, 525M): vertical members 3 stations over the clear length, other members at most 0.5 m apart
    Public Function StationLocations(ByVal ei As Integer) As Double()
        Dim E = M.Elements(ei)
        Dim Lc As Double = ClearLength(E)
        Dim n As Integer = If(Math.Abs(E.Axes(0, 2)) > 0.9, 2, Math.Max(2, CInt(Math.Ceiling(Lc / 0.5 - 0.000001))))
        Dim xs(n) As Double
        For i = 0 To n : xs(i) = Lc * i / n : Next
        Return xs
    End Function

    Public Function ClearLength(ByVal E As FemElement) As Double
        Return If(E.OffsetI + E.OffsetJ < E.Length, E.Length - E.OffsetI - E.OffsetJ, E.Length)
    End Function

    '_______________________________________________________________________________________________
    'Design ratios of all members for combinations of the static patterns (sums of pattern results)
    Public Function DesignMembers(ByVal R As StaticResults, ByVal Combos As List(Of FemCombo), ByVal Prefs As DesignPrefs) As MemberRatios()
        Dim Out(M.Elements.Count - 1) As MemberRatios
        For ei = 0 To M.Elements.Count - 1
            Out(ei) = DesignElement(R, ei, Combos, Prefs)
        Next
        Return Out
    End Function

    Public Function DesignElement(ByVal R As StaticResults, ByVal ei As Integer, ByVal Combos As List(Of FemCombo), ByVal Prefs As DesignPrefs) As MemberRatios
        Dim E = M.Elements(ei)
        Dim S = E.Section
        Dim Res As New MemberRatios
        Dim Lc As Double = ClearLength(E)
        Dim xs() As Double = StationLocations(ei)
        Dim LMaj As Double = If(E.UnbracedMajor > 0, E.UnbracedMajor, Lc), LMin As Double = If(E.UnbracedMinor > 0, E.UnbracedMinor, Lc), LLtb As Double = If(E.UnbracedLTB > 0, E.UnbracedLTB, Lc)
        Dim nSeg As Integer = Math.Max(1, CInt(Math.Ceiling(Lc / Math.Max(LLtb, 0.001) - 0.000001)))
        Dim Lseg As Double = Lc / nSeg
        Dim Pnc As Double = CompressionStrength(S, LMaj, LMin)
        Dim PcComp As Double = Prefs.PhiComp * Pnc
        Dim PcTen As Double = Math.Min(Prefs.PhiTY * S.Fy * S.A, Prefs.PhiTF * S.Fu * S.A)
        Dim Mn2 As Double = MinorFlexure(S)
        Dim Vn2, Vn3, Ph2, Ph3 As Double
        Shear(S, Vn2, Ph2, Vn3, Ph3)
        Dim McMin As Double = Prefs.PhiBend * Mn2
        For Each cmb In Combos
            'station forces of the combination (static part) at all stations and at the sampling points of the Cb segments
            Dim nS As Integer = xs.Length
            Dim nCb As Integer = nSeg * 4 + 1
            Dim allX As New List(Of Double)(xs)
            For k = 0 To nSeg - 1
                For q = 0 To 4 : allX.Add(k * Lseg + q * Lseg / 4) : Next
            Next
            Dim Sf(allX.Count - 1)() As Double
            For j = 0 To allX.Count - 1 : Sf(j) = New Double(5) {} : Next
            For Each t In cmb.Terms
                Dim p As Integer = M.PatternIndex(t.Name)
                If p < 0 Then Continue For
                Dim sp = StationForces(R, p, ei, allX.ToArray())
                For j = 0 To allX.Count - 1 : For c = 0 To 5 : Sf(j)(c) += t.Factor * sp(j)(c) : Next : Next
            Next
            'Cb and Mn3 per segment
            Dim Mc3(nSeg - 1) As Double
            For k = 0 To nSeg - 1
                Dim m5(4) As Double
                For q = 0 To 4 : m5(q) = Math.Abs(Sf(nS + k * 5 + q)(5)) : Next
                Dim mmax As Double = 0
                For j = 0 To nS - 1
                    If xs(j) >= k * Lseg - 0.000001 AndAlso xs(j) <= (k + 1) * Lseg + 0.000001 Then mmax = Math.Max(mmax, Math.Abs(Sf(j)(5)))
                Next
                mmax = Math.Max(mmax, m5.Max())
                Dim Cb As Double = CbFactor(mmax, m5(1), m5(2), m5(3))
                Mc3(k) = Prefs.PhiBend * MajorFlexure(S, Lseg, Cb)
            Next
            For j = 0 To nS - 1
                Dim sf1 = Sf(j)
                Dim Pu As Double = -sf1(0)                     'compression positive
                Dim Pr As Double = Math.Abs(Pu)
                Dim Pc As Double = If(Pu > 0, PcComp, PcTen)
                Dim k As Integer = Math.Min(nSeg - 1, CInt(Math.Floor(xs(j) / Lseg + 0.000001)))
                Dim r3 As Double = Math.Abs(sf1(5)) / Mc3(k)
                Dim r2 As Double = Math.Abs(sf1(4)) / McMin
                Dim rp As Double = Pr / Pc
                Dim pmm As Double = If(rp >= 0.2, rp + 8.0 / 9.0 * (r3 + r2), rp / 2 + r3 + r2)
                If pmm > Res.PMM Then
                    Res.PMM = pmm : Res.P = rp : Res.MMaj = r3 : Res.MMin = r2 : Res.PMMCombo = cmb.Name : Res.PMMStation = xs(j)
                End If
                If ei = TraceElement Then Console.WriteLine("     " & cmb.Name.PadRight(18) & " x=" & xs(j).ToString("0.000") & " N " & sf1(0).ToString("0.00").PadLeft(9) & " V2 " & sf1(1).ToString("0.00").PadLeft(8) & " V3 " & sf1(2).ToString("0.00").PadLeft(8) & " M2 " & sf1(4).ToString("0.00").PadLeft(8) & " M3 " & sf1(5).ToString("0.00").PadLeft(8) & "  Pc " & Pc.ToString("0.0") & " Mc3 " & Mc3(k).ToString("0.0") & " Mc2 " & McMin.ToString("0.0") & "  pmm " & pmm.ToString("0.0000"))
                Res.VMaj = Math.Max(Res.VMaj, Math.Abs(sf1(1)) / (Ph2 * Vn2))
                Res.VMin = Math.Max(Res.VMin, Math.Abs(sf1(2)) / (Ph3 * Vn3))
            Next
        Next
        Return Res
    End Function

End Class
