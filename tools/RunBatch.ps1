# Toplu koşu: bir koşu listesini (CSV) FrameSap2000.exe /batch ile, en fazla -Parallel koşu aynı anda olacak biçimde çalıştırır.
# Her koşu kendi klasöründe, modelin bir kopyasıyla çalışır (orijinal model değişmez). Listeyi elle yazarsınız.
#
# Kullanım (PowerShell):
#   powershell -ExecutionPolicy Bypass -File tools\RunBatch.ps1 -Runs D:\Kosular\liste.csv -Out D:\Kosular\Sonuc -Exe D:\Kosular\bin\FrameSap2000.exe -Parallel 3
#   -Status          koşmadan durumu yazar: her koşu için durum, analiz sayısı, en iyi maliyet (çalışan koşuları rahatsız etmez)
#   -Stop            -Out klasöründeki çalışan koşuları (ve kendi ETABS örneklerini) kapatır
#   -SummaryOnly     koşmaz, yalnızca <Out>\summary.csv yazar
#   -Resume          sonucu olmayan koşuları yedekten (<ad>.result.backup.xml) devam ettirir
#   -NoRetry         çöken koşuyu yeniden başlatmaz (varsayılan: bir kez yedekten devam eder)
#   -StartDelay 180  koşuların başlangıç aralığı [s] (ETABS'lerin aynı anda açılıp yarışmaması için)
#
# Liste (CSV) sütunları: ilk 7 zorunlu, kalanlar isteğe bağlı
#   Name         koşu adı (klasör ve dosya adı); yinelenmemeli
#   Model        model dosyası (.EDB); göreli yol, listenin klasörüne göre
#   Method       HarmornySearch | ArtificialBeeColony | SocialSpider | ... (OptMethod_ adı)
#   Mode         Steel | Composite | Hybrid
#   MaxAnalyses  analiz bütçesi      MemorySize  popülasyon / bellek      Seed  tohum
#   Transition   (Hybrid) PerStack | PerGroup          AbcLimit  (ABC) terk sınırı
#   Config       bu koşu için App.config değerleri: "Anahtar=değer;Anahtar2=değer2", örn.
#                SeismicDriftAmplification=5.5;UpperBoundMultiplier=1
#                Doluysa koşuya kendi exe kopyası verilir (<Out>\<Name>\bin); boşsa -Exe klasörü ortak kullanılır.
#
# Bir koşu normal biter ve "uygun tasarım bulunamadı" derse yeniden başlatılmaz (sonuç dosyası olmaz; bkz. -Status).
# Yalnızca çöken koşu (günlüğün sonunda Finished / Failed yok) bir kez yedekten devam eder.
param(
    [Parameter(Mandatory = $true)][string]$Runs,
    [Parameter(Mandatory = $true)][string]$Out,
    [string]$Exe = "",
    [string]$Template = "",
    [int]$Parallel = 3,
    [int]$StartDelay = 120,
    [switch]$Resume,
    [switch]$NoRetry,
    [switch]$Status,
    [switch]$Stop,
    [switch]$SummaryOnly
)
$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $PSScriptRoot
if (-not $Template) { $Template = Join-Path $PSScriptRoot 'batch_template.xml' }
if (-not $Exe) { $Exe = Join-Path $Root 'bin\Release\FrameSap2000.exe' }
$Exe = (Resolve-Path $Exe).Path
$RunsPath = (Resolve-Path $Runs).Path
New-Item -ItemType Directory -Force -Path $Out | Out-Null
$Out = (Resolve-Path $Out).Path
$List = @(Import-Csv $RunsPath)
$RunnerLog = Join-Path $Out 'runner.log'

function Say($text) {
    $line = "{0:yyyy-MM-dd HH:mm:ss} {1}" -f (Get-Date), $text
    Write-Output $line
    try { Add-Content -Path $RunnerLog -Value $line -Encoding UTF8 } catch { }
}

function Set-Node($xml, $path, $value) {
    $n = $xml.SelectSingleNode($path)
    if ($null -eq $n) { throw "XML alanı yok: $path" }
    $n.InnerText = [string]$value
}

# Çalışan toplu koşular (komut satırında /batch olanlar); elle açılmış form sayılmaz
function Get-BatchProcs {
    @(Get-CimInstance Win32_Process -Filter "Name='FrameSap2000.exe'" | Where-Object { $_.CommandLine -like '*/batch*' })
}

# ---- -Stop: bu klasördeki koşuları kapat
if ($Stop) {
    foreach ($p in (Get-BatchProcs | Where-Object { $_.CommandLine -like ('*' + $Out + '*') })) {
        $kids = @(Get-CimInstance Win32_Process -Filter "ParentProcessId=$($p.ProcessId)" | Where-Object { $_.Name -eq 'ETABS.exe' })
        Stop-Process -Id $p.ProcessId -Force
        foreach ($k in $kids) { Stop-Process -Id $k.ProcessId -Force }
        Say ("kapatıldı: pid {0} ({1})" -f $p.ProcessId, ($p.CommandLine -replace '.*\\([^\\]+)\.settings\.xml.*', '$1'))
    }
    Write-Output "Bu betiğin başka bir penceresi çalışıyorsa (yönetici) onu da kapatın; yoksa kapatılan koşuyu yeniden başlatabilir."
    return
}

# ---- koşu klasörleri ve ayar dosyaları
$Jobs = @()
foreach ($r in $List) {
    $Dir = Join-Path $Out $r.Name
    New-Item -ItemType Directory -Force -Path $Dir | Out-Null
    $Src = $r.Model
    if (-not [IO.Path]::IsPathRooted($Src)) { $Src = Join-Path (Split-Path -Parent $RunsPath) $Src }
    $Model = Join-Path $Dir ([IO.Path]::GetFileName($Src))
    $Result = Join-Path $Dir ($r.Name + '.result.xml')
    $Settings = Join-Path $Dir ($r.Name + '.settings.xml')
    $JobExe = $Exe
    if ($r.Config -and $r.Config.Trim()) { $JobExe = Join-Path (Join-Path $Dir 'bin') 'FrameSap2000.exe' }
    if (-not $SummaryOnly -and -not $Status -and -not (Test-Path $Result)) {
        # .NET Framework 260 karakterden uzun yollara yazamaz (en uzun dosya: <ad>.result.backup.xml.bak)
        $Longest = Join-Path $Dir ($r.Name + '.result.backup.xml.bak')
        if ($Longest.Length -gt 230) { throw "Yol çok uzun ($($Longest.Length) karakter, en çok 230): $Longest. -Out için daha kısa bir klasör seçin." }
        if (-not (Test-Path $Src)) { throw "Model bulunamadı: $Src" }
        if (-not (Test-Path $Model)) { Copy-Item $Src $Model }
        # koşuya özel App.config: exe klasörü kopyalanır, anahtarlar değiştirilir
        if ($r.Config -and $r.Config.Trim()) {
            $BinDir = Split-Path -Parent $JobExe
            if (-not (Test-Path $JobExe)) {
                New-Item -ItemType Directory -Force -Path $BinDir | Out-Null
                Copy-Item (Join-Path (Split-Path -Parent $Exe) '*') $BinDir -Recurse -Force
            }
            $CfgPath = $JobExe + '.config'
            $Cfg = Get-Content -Raw -Encoding UTF8 $CfgPath
            foreach ($kv in ($r.Config -split ';' | Where-Object { $_.Trim() })) {
                $k, $v = $kv.Split('=', 2)
                $k = $k.Trim(); $v = $v.Trim()
                $pat = '(<add\s+key="' + [regex]::Escape($k) + '"\s+value=")[^"]*(")'
                if ($Cfg -notmatch $pat) { throw "App.config anahtarı yok: '$k' (koşu $($r.Name))" }
                $Cfg = [regex]::Replace($Cfg, $pat, ('${1}' + $v + '${2}'))
            }
            Set-Content -Path $CfgPath -Value $Cfg -Encoding UTF8 -NoNewline
        }
        [xml]$x = Get-Content -Raw -Encoding UTF8 $Template
        Set-Node $x '/FormInfo_/FileList/ETABSFile' $Model
        Set-Node $x '/FormInfo_/FileList/OutputFile' $Result
        Set-Node $x '/FormInfo_/OptInfo/OptimizationMethod' $r.Method
        Set-Node $x '/FormInfo_/OptInfo/MaxFuncEvaluation' $r.MaxAnalyses
        Set-Node $x '/FormInfo_/OptInfo/MemorySize' $r.MemorySize
        Set-Node $x '/FormInfo_/Seed' $r.Seed
        Set-Node $x '/FormInfo_/HideETABS' 'true'
        Set-Node $x '/FormInfo_/CompositeColumns' $(if ($r.Mode -eq 'Steel') { 'false' } else { 'true' })
        Set-Node $x '/FormInfo_/HybridColumns' $(if ($r.Mode -eq 'Hybrid') { 'true' } else { 'false' })
        if ($r.Transition) { Set-Node $x '/FormInfo_/TransitionMode' $r.Transition }
        if ($r.AbcLimit) {
            $oi = $x.SelectSingleNode('/FormInfo_/OptInfo')
            $ps = $oi.SelectSingleNode('Params'); if ($null -eq $ps) { $ps = $oi.AppendChild($x.CreateElement('Params')) }
            $p = $ps.AppendChild($x.CreateElement('MethodParam_'))
            foreach ($kv in @(@('Method', 'ArtificialBeeColony'), @('Key', 'Limit'), @('Value', $r.AbcLimit))) {
                $e = $p.AppendChild($x.CreateElement($kv[0])); $e.InnerText = $kv[1]
            }
        }
        $x.Save($Settings)
    }
    $Jobs += [pscustomobject]@{ Name = $r.Name; Dir = $Dir; Settings = $Settings; Result = $Result; Exe = $JobExe; Row = $r }
}

# ---- bir koşunun durumu (günlüklerden)
function Get-JobState($j, $Cmd) {
    if (@($Cmd | Where-Object { $_.CommandLine -like ('*' + $j.Settings + '*') }).Count -gt 0) { return 'çalışıyor' }
    if (Test-Path $j.Result) { return 'bitti' }
    $Log = [IO.Path]::ChangeExtension($j.Settings, '.batch.log')
    if (-not (Test-Path $Log)) { return 'başlamadı' }
    $Last = (Get-Content $Log -Tail 3) -join ' '
    $El = Join-Path $j.Dir 'ErrorLog.txt'
    if ($Last -match 'Finished' -and (Test-Path $El) -and (Select-String -Path $El -Pattern 'no feasible design found' -Quiet)) { return 'uygun tasarım yok' }
    if ($Last -match 'Failed') { return 'hata' }
    if ($Last -match 'Finished') { return 'bitti (sonuç yok)' }
    return 'durdu/çöktü'
}

function Get-Progress($j) {
    $o = [ordered]@{ Analyses = ''; BestCost = ''; BestPenalty = '' }
    $Bak = Join-Path $j.Dir ($j.Name + '.result.backup.xml')
    if (Test-Path $Bak) {
        try {
            $s = [IO.File]::ReadAllText($Bak)
            # uygun tasarım yokken GlobalBest varsayılan değerlerde kalır (maliyet 0, ceza 0, PenalizedCost INF)
            $m = [regex]::Match($s, '<GlobalBest>.*?<CostValue>(.*?)</CostValue>\s*<Penalty>(.*?)</Penalty>\s*<PenalizedCost>(.*?)</PenalizedCost>', 'Singleline')
            if ($m.Success -and $m.Groups[3].Value -notmatch 'INF|NaN' -and $m.Groups[2].Value -eq '0') { $o.BestCost = $m.Groups[1].Value; $o.BestPenalty = '0' }
            elseif ($m.Success) { $o.BestPenalty = 'uygun tasarım yok' }
            $it = [regex]::Match($s, '<iter>(.*?)</iter>'); if ($it.Success) { $o.Analyses = $it.Groups[1].Value }
        } catch { }
    }
    [pscustomobject]$o
}

if ($Status) {
    $Cmd = Get-BatchProcs
    $Rows = foreach ($j in $Jobs) {
        $p = Get-Progress $j
        [pscustomobject]@{ Name = $j.Name; Method = $j.Row.Method; Mode = $j.Row.Mode; Budget = $j.Row.MaxAnalyses; State = (Get-JobState $j $Cmd); Analyses = $p.Analyses; BestCost = $p.BestCost; BestPenalty = $p.BestPenalty }
    }
    $Rows | Format-Table -AutoSize | Out-String -Width 220
    return
}

# ---- koşular: sistemde en fazla $Parallel toplu koşu, $StartDelay aralıkla
if (-not $SummaryOnly) {
    $Pending = [Collections.Generic.List[object]]::new()
    foreach ($j in $Jobs) { if (-not (Test-Path $j.Result)) { $Pending.Add($j) } }
    $Tries = @{}
    $LastStart = [datetime]::MinValue
    Say ("{0} koşu bekliyor, en çok {1} aynı anda (sistemdeki diğer toplu koşular da sayılır)" -f $Pending.Count, $Parallel)
    while ($Pending.Count -gt 0) {
        $Cmd = Get-BatchProcs
        # biten ya da bırakılan koşuları listeden çıkar
        foreach ($j in @($Pending)) {
            if ($Tries[$j.Name] -lt 1) { continue }
            $st = Get-JobState $j $Cmd
            if ($st -eq 'çalışıyor') { continue }
            if ($st -eq 'bitti') { $Pending.Remove($j) | Out-Null; Say "bitti: $($j.Name)" }
            elseif ($st -eq 'uygun tasarım yok' -or $st -eq 'bitti (sonuç yok)') { $Pending.Remove($j) | Out-Null; Say "sonuçsuz bitti ($st): $($j.Name)" }
            elseif ($NoRetry -or $Tries[$j.Name] -ge 2) { $Pending.Remove($j) | Out-Null; Say "bırakıldı ($st): $($j.Name)" }
        }
        $Free = $Parallel - $Cmd.Count
        if ($Pending.Count -gt 0 -and $Free -gt 0 -and ((Get-Date) - $LastStart).TotalSeconds -ge $StartDelay) {
            $j = $Pending | Where-Object { (Get-JobState $_ $Cmd) -ne 'çalışıyor' } | Select-Object -First 1
            if ($j) {
                $a = @('/batch', ('"' + $j.Settings + '"'))
                $Bak = [IO.Path]::ChangeExtension($j.Result, '.backup.xml')
                $Retry = $Tries[$j.Name] -ge 1
                if (($Resume -or $Retry) -and (Test-Path $Bak)) { $a += '/resume' }
                $p = Start-Process -FilePath $j.Exe -ArgumentList $a -WorkingDirectory (Split-Path -Parent $j.Exe) -PassThru
                $Tries[$j.Name] = 1 + [int]$Tries[$j.Name]
                Say ("başladı: {0} (pid {1}{2})" -f $j.Name, $p.Id, $(if ($a -contains '/resume') { ', yedekten' } else { '' }))
                $LastStart = Get-Date
            }
        }
        Start-Sleep -Seconds 30
    }
    Say "bütün koşular bitti"
}

# ---- özet
$Cmd = Get-BatchProcs
$Rows = foreach ($j in $Jobs) {
    $o = [ordered]@{ Name = $j.Name; Method = $j.Row.Method; Mode = $j.Row.Mode; Seed = $j.Row.Seed; MaxAnalyses = $j.Row.MaxAnalyses; State = (Get-JobState $j $Cmd) }
    if (Test-Path $j.Result) {
        [xml]$x = Get-Content -Raw -Encoding UTF8 $j.Result
        $c = $x.ClassFinal
        $o.Analyses = $c.Analyses
        $o.BestCost = $c.GlobalBest.CostValue
        $o.BestPenalty = $c.GlobalBest.Penalty
        $o.FinalCost = $c.FinalCheck.CostValue
        $o.FinalPenalty = $c.FinalCheck.Penalty
        $o.FinalFails = $c.FinalFails
        $o.Stacks = (@($c.GlobalBestPrint.string) | Where-Object { $_ -like 'Stack *' }) -join ' | '
    } else { $o.Analyses = ''; $o.BestCost = ''; $o.BestPenalty = ''; $o.FinalCost = ''; $o.FinalPenalty = ''; $o.FinalFails = 'no result'; $o.Stacks = '' }
    # süre: ErrorLog.txt'nin son "run time d.hh:mm:ss" satırı (optimizasyon; başlangıç hazırlığı hariç)
    $o.Hours = ''
    $El = Join-Path $j.Dir 'ErrorLog.txt'
    if (Test-Path $El) {
        $m = Select-String -Path $El -Pattern 'run time (\d+)\.(\d\d):(\d\d):(\d\d)' | Select-Object -Last 1
        if ($m) { $g = $m.Matches[0].Groups; $o.Hours = [math]::Round([int]$g[1].Value * 24 + [int]$g[2].Value + [int]$g[3].Value / 60 + [int]$g[4].Value / 3600, 2) }
    }
    [pscustomobject]$o
}
$Sum = Join-Path $Out 'summary.csv'
$Rows | Export-Csv -NoTypeInformation -Encoding UTF8 $Sum
$Rows | Select-Object Name, State, Analyses, BestCost, FinalCost, FinalPenalty, FinalFails, Hours | Format-Table -AutoSize | Out-String -Width 200
Write-Output "özet: $Sum"
