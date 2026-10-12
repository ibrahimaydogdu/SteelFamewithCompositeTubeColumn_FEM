Imports System.Collections.Generic

'Design ratios of one member (envelope over the combinations and stations)
Public Class MemberRatios
    Public PMM, P, MMaj, MMin, VMaj, VMin As Double
    Public PMMCombo As String
    Public PMMStation As Double
    Public VMajCombo As String
    Public PuMax As Double                  'largest compression over all combinations and stations (kN)
End Class

Partial Public Class FemStatic
    Private ReadOnly LoadCache As New System.Collections.Concurrent.ConcurrentDictionary(Of Integer, List(Of LocalLoad)())
    Public TraceElement As Integer = -1

    Private Function CachedLoads(ByVal ei As Integer) As List(Of LocalLoad)()
        Return LoadCache.GetOrAdd(ei, Function(k) ElementLoads(k, M.Elements(k)))
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

    'Station forces (no loads on the span) from face forces: N, V2, V3, T, M2, M3 at distance x from the start of the clear span
    Private Shared Function StationFromFace(ByVal f() As Double, ByVal x As Double) As Double()
        Return New Double() {-f(0), -f(1), -f(2), -f(3), -f(4) - x * f(2), -f(5) + x * f(1)}
    End Function

    'Station force magnitudes of a response spectrum case at the distances xs (CQC of the modes, plus the accidental torsion by absolute value)
    Public Function RSStationForces(ByVal Rr As RSResults, ByVal ei As Integer, ByVal xs() As Double) As Double()()
        Dim nm As Integer = Rr.ModalFace.Length
        Dim Res(xs.Length - 1)() As Double
        For j = 0 To xs.Length - 1
            Dim modal(nm - 1)() As Double
            For k = 0 To nm - 1 : modal(k) = StationFromFace(Rr.ModalFace(k)(ei), xs(j)) : Next
            Dim v(5) As Double
            For c = 0 To 5
                Dim sm As Double = 0
                For i = 0 To nm - 1 : For m2 = 0 To nm - 1 : sm += Rr.Rho(i, m2) * modal(i)(c) * modal(m2)(c) : Next : Next
                v(c) = Math.Sqrt(Math.Max(sm, 0))
            Next
            If Rr.TorsionFace IsNot Nothing Then
                Dim t = StationFromFace(Rr.TorsionFace(ei), xs(j))
                For c = 0 To 5 : v(c) += Math.Abs(t(c)) : Next
            End If
            Res(j) = v
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

    'Diagnostic: from the ETABS major-axis ratio of a beam in pure bending (governing combination cmbName) infer the Cb ETABS used for several
    'assumptions about the unbraced length, next to the Cb of the moment diagram (|M| at the quarter points of the segment that holds the maximum)
    Public Function CbDiagnostic(ByVal R As StaticResults, ByVal ei As Integer, ByVal Cmb As FemCombo, ByVal EtabsRatio As Double, ByVal Prefs As DesignPrefs) As String
        Dim E = M.Elements(ei), S = E.Section
        Dim Lc As Double = ClearLength(E)
        Dim a As Double = E.OffsetI
        Dim LLtb As Double = If(E.UnbracedLTB > 0, E.UnbracedLTB, Lc)
        Dim xs(400) As Double
        For i = 0 To 400 : xs(i) = Lc * i / 400 : Next
        Dim Sf(400)() As Double
        For j = 0 To 400 : Sf(j) = New Double(5) {} : Next
        For Each t In Cmb.Terms
            Dim p As Integer = M.PatternIndex(t.Name)
            If p < 0 Then Continue For
            Dim sp = StationForces(R, p, ei, xs)
            For j = 0 To 400 : For c = 0 To 5 : Sf(j)(c) += t.Factor * sp(j)(c) : Next : Next
        Next
        Dim jm As Integer = 0
        For j = 0 To 400 : If Math.Abs(Sf(j)(5)) > Math.Abs(Sf(jm)(5)) Then jm = j
        Next
        Dim Mmax As Double = Math.Abs(Sf(jm)(5)), xm As Double = xs(jm)
        Dim MnImplied As Double = Mmax / (EtabsRatio * Prefs.PhiBend)
        Dim Mp As Double = S.Fy * S.Z33
        Dim Inv = Function(lb As Double) As String
                      If MnImplied >= Mp * 0.9999 Then Return "Mp"
                      Dim lo As Double = 1.0, hi As Double = 3.0
                      If MajorFlexure(S, lb, 1.0) > MnImplied Then Return "<1"
                      If MajorFlexure(S, lb, 3.0) < MnImplied Then Return ">3"
                      For it = 1 To 60
                          Dim mid As Double = 0.5 * (lo + hi)
                          If MajorFlexure(S, lb, mid) < MnImplied Then lo = mid Else hi = mid
                      Next
                      Return (0.5 * (lo + hi)).ToString("0.000")
                  End Function
        Dim nEq As Integer = Math.Max(1, CInt(Math.Ceiling(Lc / Math.Max(LLtb, 0.001) - 0.000001)))
        Dim LbEq As Double = Lc / nEq
        'joint coordinate segment that holds the maximum
        Dim xj As Double = xm + a
        Dim k0 As Integer = Math.Min(CInt(Math.Floor(xj / LLtb)), 50)
        Dim sj0 As Double = Math.Max(k0 * LLtb, a) - a, sj1 As Double = Math.Min((k0 + 1) * LLtb, a + Lc) - a
        'Cb of the moment diagram in that segment
        Dim Msamp = Function(x As Double) As Double
                        Dim jj As Integer = Math.Min(400, CInt(Math.Round(x / Lc * 400)))
                        Return Math.Abs(Sf(jj)(5))
                    End Function
        Dim mmaxSeg As Double = 0
        For j = 0 To 400 : If xs(j) >= sj0 - 0.000001 AndAlso xs(j) <= sj1 + 0.000001 Then mmaxSeg = Math.Max(mmaxSeg, Math.Abs(Sf(j)(5)))
        Next
        Dim L1 As Double = sj1 - sj0
        Dim cbSeg As Double = CbFactor(mmaxSeg, Msamp(sj0 + 0.25 * L1), Msamp(sj0 + 0.5 * L1), Msamp(sj0 + 0.75 * L1))
        Dim cbWhole As Double = CbFactor(Mmax, Msamp(0.25 * Lc), Msamp(0.5 * Lc), Msamp(0.75 * Lc))
        Return E.Name & " " & S.Name & " Lc " & Lc.ToString("0.00") & " Ltb " & LLtb.ToString("0.00") & " nEq " & nEq & " Mmax " & Mmax.ToString("0.0") & " at " & xm.ToString("0.00") & " (joint x " & xj.ToString("0.00") & ")  implied Cb: [Lb=" & L1.ToString("0.00") & " jointSeg] " & Inv(L1) & " [Lb=" & LbEq.ToString("0.00") & " equal] " & Inv(LbEq) & "   my Cb: seg " & cbSeg.ToString("0.000") & " whole " & cbWhole.ToString("0.000")
    End Function

    '_______________________________________________________________________________________________
    'Design ratios of all members for combinations of the static patterns (sums of pattern results)
    Public Function DesignMembers(ByVal R As StaticResults, ByVal Combos As List(Of FemCombo), ByVal Prefs As DesignPrefs, Optional ByVal RS As Dictionary(Of String, RSResults) = Nothing) As MemberRatios()
        Dim Out(M.Elements.Count - 1) As MemberRatios
        System.Threading.Tasks.Parallel.For(0, M.Elements.Count, Sub(ei) Out(ei) = DesignElement(R, ei, Combos, Prefs, RS))
        Return Out
    End Function

    Public Function DesignElement(ByVal R As StaticResults, ByVal ei As Integer, ByVal Combos As List(Of FemCombo), ByVal Prefs As DesignPrefs, Optional ByVal RS As Dictionary(Of String, RSResults) = Nothing) As MemberRatios
        Dim E = M.Elements(ei)
        Dim S = E.Section
        Dim Res As New MemberRatios
        Dim Lc As Double = ClearLength(E)
        Dim xs() As Double = StationLocations(ei)
        Dim LMaj As Double = If(E.UnbracedMajor > 0, E.UnbracedMajor, Lc), LMin As Double = If(E.UnbracedMinor > 0, E.UnbracedMinor, Lc), LLtb As Double = If(E.UnbracedLTB > 0, E.UnbracedLTB, Lc)
        'unbraced segments (measured against ETABS): the clear span is divided into n = ceil(clear length / LTB length) equal parts, Cb = 1 when n > 1
        Dim SegStart As New List(Of Double), SegEnd As New List(Of Double)
        Dim nEq As Integer = Math.Max(1, CInt(Math.Ceiling(Lc / Math.Max(LLtb, 0.001) - 0.000001)))
        For k = 0 To nEq - 1 : SegStart.Add(Lc * k / nEq) : SegEnd.Add(Lc * (k + 1) / nEq) : Next
        Dim nSeg As Integer = SegStart.Count
        Dim Lseg As Double = Lc / nSeg
        Dim Pnc As Double = CompressionStrength(S, LMaj, LMin)
        Dim PcComp As Double = Prefs.PhiComp * Pnc
        Dim PcTen As Double = Math.Min(Prefs.PhiTY * S.Fy * S.A, Prefs.PhiTF * S.Fu * S.A)
        Dim Mn2 As Double = MinorFlexure(S)
        Dim Vn2, Vn3, Ph2, Ph3 As Double
        Shear(S, Vn2, Ph2, Vn3, Ph3)
        Dim McMin As Double = Prefs.PhiBend * Mn2
        For Each cmb In Combos
            If cmb.Nonlinear Then Continue For
            Dim nS As Integer = xs.Length
            Dim allX As New List(Of Double)(xs)
            'ETABS (measured): where the LTB length is overwritten (Lb < clear span) Cb is evaluated for a window of length Lb centred on the
            'station (moved inside the span); otherwise for the whole span
            Dim Overwritten As Boolean = LLtb < Lc - 0.000001
            Dim Lwin As Double = If(Overwritten, LLtb, Lc)
            Dim W0(nS - 1) As Double
            For j = 0 To nS - 1
                W0(j) = If(Overwritten, Math.Min(Math.Max(xs(j) - Lwin / 2, 0.0), Lc - Lwin), 0.0)
                For q = 1 To 3 : allX.Add(W0(j) + Lwin * q / 4) : Next
            Next
            Dim Sf(allX.Count - 1)() As Double
            For j = 0 To allX.Count - 1 : Sf(j) = New Double(5) {} : Next
            For Each t In cmb.Terms
                Dim p As Integer = M.PatternIndex(t.Name)
                If p < 0 Then Continue For
                Dim sp = StationForces(R, p, ei, allX.ToArray())
                For j = 0 To allX.Count - 1 : For c = 0 To 5 : Sf(j)(c) += t.Factor * sp(j)(c) : Next : Next
            Next
            'load sets: static combination, or static +/- the response spectrum magnitudes (all components together)
            Dim Sets As New List(Of Double()())
            If cmb.RSTerms.Count = 0 OrElse RS Is Nothing Then
                If cmb.RSTerms.Count > 0 Then Continue For
                Sets.Add(Sf)
            Else
                Dim Mag(allX.Count - 1)() As Double
                For j = 0 To allX.Count - 1 : Mag(j) = New Double(5) {} : Next
                For Each rt In cmb.RSTerms
                    Dim rr As RSResults = Nothing
                    If Not RS.TryGetValue(rt.Name, rr) Then Continue For
                    Dim sp = RSStationForces(rr, ei, allX.ToArray())
                    For j = 0 To allX.Count - 1 : For c = 0 To 5 : Mag(j)(c) += Math.Abs(rt.Factor) * sp(j)(c) : Next : Next
                Next
                'ETABS takes the worst magnitude of every component independently (max and min of the response spectrum results)
                Dim S2(allX.Count - 1)() As Double
                For j = 0 To allX.Count - 1
                    S2(j) = New Double(5) {}
                    For c = 0 To 5 : S2(j)(c) = Sf(j)(c) + If(Sf(j)(c) >= 0, 1.0, -1.0) * Mag(j)(c) : Next
                Next
                Sets.Add(S2)
            End If
            For Each SetF In Sets
                Dim Mc3(nS - 1) As Double
                For j = 0 To nS - 1
                    Dim mA As Double = Math.Abs(SetF(nS + 3 * j)(5)), mB As Double = Math.Abs(SetF(nS + 3 * j + 1)(5)), mC As Double = Math.Abs(SetF(nS + 3 * j + 2)(5))
                    Dim mmax As Double = Math.Max(mA, Math.Max(mB, mC))
                    For q = 0 To nS - 1
                        If xs(q) >= W0(j) - 0.000001 AndAlso xs(q) <= W0(j) + Lwin + 0.000001 Then mmax = Math.Max(mmax, Math.Abs(SetF(q)(5)))
                    Next
                    Dim Cb As Double = CbFactor(mmax, mA, mB, mC)
                    Mc3(j) = Prefs.PhiBend * MajorFlexure(S, Lwin, Cb)
                Next
                For j = 0 To nS - 1
                    Dim sf1 = SetF(j)
                    Dim Pu As Double = -sf1(0)
                    Dim Pr As Double = Math.Abs(Pu)
                    Dim Pc As Double = If(Pu > 0, PcComp, PcTen)
                    Dim k As Integer = j
                    Dim r3 As Double = Math.Abs(sf1(5)) / Mc3(k)
                    Dim r2 As Double = Math.Abs(sf1(4)) / McMin
                    If Pu > Res.PuMax Then Res.PuMax = Pu
                    Dim rp As Double = Pr / Pc
                    Dim pmm As Double = If(rp >= 0.2, rp + 8.0 / 9.0 * (r3 + r2), rp / 2 + r3 + r2)
                    If ei = TraceElement Then Console.WriteLine("     " & cmb.Name.PadRight(18) & " x=" & xs(j).ToString("0.000") & " N " & sf1(0).ToString("0.00").PadLeft(9) & " V2 " & sf1(1).ToString("0.00").PadLeft(8) & " V3 " & sf1(2).ToString("0.00").PadLeft(8) & " M2 " & sf1(4).ToString("0.00").PadLeft(8) & " M3 " & sf1(5).ToString("0.00").PadLeft(8) & "  Pc " & Pc.ToString("0.0") & " Mc3 " & Mc3(k).ToString("0.0") & " Mc2 " & McMin.ToString("0.0") & "  pmm " & pmm.ToString("0.0000"))
                    If pmm > Res.PMM Then
                        Res.PMM = pmm : Res.P = rp : Res.MMaj = r3 : Res.MMin = r2 : Res.PMMCombo = cmb.Name : Res.PMMStation = xs(j)
                    End If
                    Dim v2 As Double = Math.Abs(sf1(1)) / (Ph2 * Vn2)
                    If v2 > Res.VMaj Then Res.VMaj = v2 : Res.VMajCombo = cmb.Name
                    Res.VMin = Math.Max(Res.VMin, Math.Abs(sf1(2)) / (Ph3 * Vn3))
                Next
            Next
        Next
        Return Res
    End Function

End Class
