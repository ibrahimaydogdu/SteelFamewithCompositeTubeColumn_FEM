# Program Kuralları (geliştiriciler için)

Bu belge programı geliştirecek kişiler ve yapay zekâ ajanları içindir; koda dokunmadan önce okunmalıdır.
Diğer belgeler:
- değişiklik geçmişi: `DEGISIKLIKLER.md`;
- kullanım: `KULLANIM_KILAVUZU.md`;
- ajan düzeni: `Ajan/README.md`;
- görev kuyruğu: `Ajan/Gorevler/KUYRUK.md`.

## A. Bu projeye özgü kurallar

### A1. Çalışma düzeni
- Her iş önce bulgu ve öneri olarak sunulur, kullanıcı onaylayınca uygulanır.
- Kararı kullanıcı verir:
  - lisans;
  - dosya silme;
  - mimari değişiklik;
  - depoya girecek veri.
- Her değişiklik test edilir:
  - derleme;
  - birim veya matematik testi;
  - gerekiyorsa ETABS API ile **model kopyası** üzerinde test.
- Her aşama `DEGISIKLIKLER.md` dosyasına "Aşama N" başlığıyla ve test sonuçlarıyla yazılır. Ardından commit ve push yapılır (main).
- Açık ETABS oturumuna bağlanılmaz. Orijinal modeller değiştirilmez.
- Kişisel bilgiler dışarı gönderilmez.
- Başka kaynaklardan (Fortran, eski VB kodu) aktarılan kodda hatalar taşınmaz. Bulunan hatalar raporlanır.

### A2. Hedef (kullanıcı kararları, 2026-10-03)
- **Kolon tipi:** kompozit kolonlar **dolgulu tüp** olacak; çelik kutu (CFT, `FilledTube = 29`) veya boru (CFP, `FilledPipe = 30`) içine beton doldurulur.
  - Referanstaki gömülü kesit (`EncasedRectangle`) bu projenin hedefi değil.
- **Hibrit tasarım:** her kolon grubu ayrı ayrı **çelik veya kompozit** seçilebilir. Seçim kullanıcı tarafından ya da optimizasyonun bir değişkeni olarak yapılır.
  - Referansta ise tüm kolonlar birlikte ya çelik ya kompozitti (`FormInfo.CompositeColumns`).
- **Kompozit döşeme:** tasarımı isteğe bağlı olarak eklenebilir; sonraki aşamalarda ele alınacak.
- **SSO:** Fortran'daki Sosyal Örümcek Algoritması yöntem kataloğuna eklenecek.
- **Lisans ve depo:** lisans MIT. ETABS model dosyaları (`.EDB`, `.$et`) depoya girer; analiz çıktıları girmez.

### A3. Durum (Aşama 2)
- Kod, referans projenin (sürüm 2026.10.3) kopyası. Eklemeler:
  - 16. yöntem SSO (Aşama 3–4; `SocialSpider.vb`);
  - dolgulu tüp kolonlar (Aşama 5; `TubeColumn.vb`, `TubeSections.xml`). Kurallar A5'te;
  - D/C sınırı ve deprem süzgeci (Aşama 5.1). Kurallar A6'da;
  - hibrit kolonlar (Aşama 6; `HybridColumns.vb`). Kurallar A7'de.
- Kök ad alanı ve exe adı `FrameSap2000` olarak korundu, böylece ayar ve yedek dosyaları uyumlu kalıyor.
- Değişenler yalnızca şunlar:
  - ürün adı ve sürüm (0.5.0);
  - pencere başlığı;
  - proje/çözüm adları ve GUID'leri.
- Aşağıdaki B bölümü referansın kurallarıdır ve koda birebir uyar. CFT/CFP ve hibrit tasarım eklendikçe B bölümü güncellenecek. Şimdilik "kompozit" dendiğinde gömülü kesit kastediliyor.

### A5. Dolgulu tüp kolonlar (Aşama 5)
- **Mod ve kesit:**
  - `FormInfo.CompositeType` (`CompositeType_`: 0 = Encased, 1 = FilledTube; eski yedekler 0). `ETABS_Class.TubeMode` = CompositeColumns ve FilledTube.
  - Tüp modda kompozit grubun tasarım değişkeni `Tubes` listesinin indisidir; W listesi değildir.
  - Hiçbir yerde değişkenin katalogunu varsaymadan `WSections(Sect_Ind(v))` yazılmaz. Bunun yerine yardımcılar kullanılır: `IsTubeVar`, `CatalogCount`, `SecArea` (çelik alanı), `SecDepth`, `SecName`, `FindSection`, `ColumnGap` (kiriş başlığının sığacağı genişlik; tüpte yüz genişliği).
  - Kiriş değişkenleri her zaman W'dir.
- **Katalog** (`TubeSettings_.Catalog`):
  - kütüphanedeki STEEL_BOX / STEEL_PIPE kayıtları (`TF`/`TDES` tasarım kalınlığı) ve yapma kare kutular `BU<B>X<B>X<t>`;
  - `MinDimension`, λmax (I1.1a/b) ve aynı geometri süzgeçleri uygulanır;
  - sıralama çelik alanına göredir. Onarım adımları (G2, F2, F4) ve koruma döngüsü bu sıraya dayanır.
- **ETABS'te kesit:**
  - Arama sırasında `CreateGeneralSection` (`CompositeSection.Transformed`) kullanılır.
  - Finalde `CreateTubeSections` çalışır: `Filled Steel Tube` (t3, t2, tf, tw, FillMat) ve `Filled Steel Pipe` (t3 = D, tw) tablolarına boyutlar açıkça yazılır.
  - Tablolardaki `FromFile = Yes` seçeneği ETABS 22.6'da çalışmıyor (API testi, Aşama 5).
- **Beton:** `ResolveTubeConcrete`. `TubeSettings.ConcreteMaterial` modelde varsa o, yoksa modelin ilk beton malzemesi.
- **Maliyet:** `TubeSettings.SteelUnitCost` × çelik [kN] + `ConcreteUnitCost` × beton [m³]. Formun *Steel* ve *Concrete* değerleri bunların yerine geçer.
- **Ub:** ilk tasarımdaki W kesitinin Fy·A değerine en az eşit Pno'lu ilk tüp + referanstaki kayma (`BOUND_SHIFT_MULTIPLIER`, `UPPER_BOUND_MULTIPLIER`); Lb = 0.
- **Değişmezlik:** W kesitli değişkenlerde (çelik ve gömülü mod) hesap referansla aynıdır. Bu, MathTest ve 525M gömülü mod regresyon testiyle doğrulanır.

### A6. D/C oranı sınırı ve deprem süzgeci (Aşama 5.1)
- **D/C oranı sınırı:**
  - ETABS, çelik ve kompozit kolon tasarımında oranı tercihlerdeki `DCLimit` ile karşılaştırır (varsayılan 0,95). Aşım, `DesignSteel.GetSummaryResults` sonucunda hata ya da uyarı olarak görünmez.
  - `InitializeRatioLimits` sınırları `Steel Frame Design Preferences - <kod>` ve `Composite Column Design Preferences - <kod>` tablolarının `DCLimit` alanından okur. App.config `DesignRatioLimit` > 0 ise önce bu değeri yazar; ETABS yeniden başlatılınca tekrar yazılır.
  - `Groups.PMMRatio` (çelik) = ETABS oranı / `SteelRatioLimit`.
  - Kompozit grup oranı = max(detay oranı, dayanım / `CompositeRatioLimit`). `CompositeStrength` ham oranı saklar (kalibrasyon için).
  - `ETABSRatioByVar` ve `ETABSCompositeRatios` = ETABS oranı / `CompositeRatioLimit`. Koruma döngüsü ve uyarılar > 1 ile çalışır.
  - Yeni bir oran eklenirse sınıra bölünmelidir.
- **Deprem süzgeci:**
  - `TubeSettings_.SeismicDuctility` (None / Moderate / High, varsayılan High).
  - Sınırlar TBDY 2018 Tablo 9.3'ün kompozit satırlarıdır (= AISC 341-10 D1.1): kutu b/t ≤ 1,4 / 2,26 √(E/Fy), boru D/t ≤ 0,076 / 0,15 E/Fy.
  - b: kütüphanedeki HSS'te B − 3t, yapma kutuda B − 2t (`SeismicSlenderness`).
  - Kaynak: kullanıcının paylaştığı TBDY 2018 Tablo 9.3 ve AISC 341-10 Tablo D1.1 (devam sayfası, "Composite Elements") görüntüleri (2026-10-04). İki kaynakta değerler aynı; kompozit satırlarda dipnot yok. AISC 341-16/22 metni elde değil.

### A7. Hibrit kolonlar (Aşama 6, `HybridColumns.vb`)
- **Mod:** `FormInfo.HybridColumns` (CompositeColumns ile birlikte), `TransitionMode` (PerStack / PerGroup), `GroupTypes` (grup adı → Optimize / Steel / Composite; eksik = Optimize).
- **Tasarım vektörü:** `[NG grup değişkeni][Optimize gruplarının kompozit kesit değişkeni][tip değişkenleri]`.
  - Grup değişkeni: sabit tipte o tipin kesiti, Optimize'da W kesiti.
  - Tip değişkeni: PerStack'te yığın başına 0 … (yığındaki Optimize sayısı); PerGroup'ta grup başına 0 / 1.
- **Değerlendirme:**
  - `EvaluateCore`, `GroupVector`/`DecodeHybrid` ile vektörü grup başına tek kesite çevirir. `Groups().IsComposite` (o tasarımın tipi) ve `GUb/GLb` (etkin tipin sınırları) burada kurulur.
  - Değerlendirme zinciri bu grup vektörüyle çalışır; sonunda `EncodeHybrid` onarılmış kesitleri vektöre geri yazar. Tip değişkenlerini onarım değiştirmez.
  - Dışarıdan vektör alan fonksiyonlar (`StepUpETABSFailures`, `CostBreakdown`, `DescribeDesign`) da `GroupVector` kullanır.
  - Değerlendirme zincirinde `Ub/Lb` değil `GUb/GLb` kullanılır. `Ub/Lb` optimizasyon yönteminin tam vektör sınırlarıdır.
- **ETABS ataması:** `AssignedComp`; tipi değişen grup yeniden atanır. Çeliğe dönen grubun tasarım prosedürü SteelFrameDesign yapılır. `LastAnalysedComp` tip değişince analiz atlamasını engeller.
- **Geometri:** `CtoCRatio`.
  - Aynı katalog: max(alan, derinlik oranı); bu, Aşama 6 öncesiyle aynıdır.
  - Tüp üstünde W: W derinliği / H, başlık / B.
- **Yığınlar:** `InitializeGeometricCons` içindeki kolon–kolon çiftlerinden (`ColumnPairs`, SkipCtoC'den bağımsız) birleşik bileşenler bulunur ve en düşük kota göre sıralanır.
- **Sınırlar:** `Initialize_UBLB` her grup için hem çelik (`SteelUb/Lb`) hem kompozit (`CompUb/Lb`) sınırlarını hesaplar. Hibrit olmayan modlarda `Ub/Lb` bunlardan seçilir ve sonuç Aşama 5.1 ile aynıdır.
- **ACO:** `FullVarCount` / `FullVarArea`, vektörün sabit anlamına göre çalışır (o anki tipe göre değil).
- **Grup çakışması:** birden fazla grupta olan elemanları referanstaki `InitializeFrames` kontrolü zaten yakalıyor ("More group definition than 1" hatası). Ayrı bir kontrol eklenmedi.
- **Form:**
  - `ReadColumnGroups` (Shared), gizli ETABS ve model kopyasıyla kolon gruplarını okur;
  - `GroupTypeGrid` tablosu tipleri tutar;
  - Check Structure'da tipler sonuç dosyasındaki `[CFT ` / `[CFP ` / `[EC ` işaretlerinden alınır (`SetGroupTypes`).

### A4. Depo
- Depoya girmeyenler:
  - derleme çıktıları ve `packages/` (NuGet tarafından geri yüklenir);
  - CSI dosyaları (`AISC14M.xml`, ETABS DLL/exe, `.chm`);
  - ETABS analiz çıktıları.
- Test modelleri `Modeller/` klasöründe durur:
  - `525M/525Member`: 525 çubuk, 14 grup;
  - `CivilComp3/460Member`: 470 çubuk, 13 grup, CFT kolon `FSec1`.
- PowerShell betikleri BOM'lu UTF-8 olarak kaydedilmeli; Windows PowerShell 5.1 BOM'suz dosyada Türkçe karakterleri bozuyor.
- Test programları depoda değil. Aşama 2 testleri `DEGISIKLIKLER.md` dosyasında anlatılıyor.

---

## B. Referanstan devralınan program yapısı ve kodlama kuralları


### B1. Ortam
- VB.NET, .NET Framework 4.7.2, WinForms; eski tip `.vbproj`. `Option Explicit On`, `Option Strict Off`, `Option Infer On`.
- ETABS OAPI (`ETABSv1.dll`): **ETABS 22** (varsayılan) ve ETABS 19 desteklenir.
  - `.vbproj` içindeki `ETABSDir` özelliği ETABS 22 kuruluysa onu, değilse ETABS 19'u seçer (`/p:ETABSDir=...` ile değiştirilebilir).
  - ETABS 22 API'si .NET Standard 2.0'dır. `netstandard` referansı ve ETABS klasöründeki `Microsoft.Win32.Registry.dll` gerekir (net461 facade; exe yanına kopyalanır).
  - Program, derlendiği `ETABSv1.dll` ile aynı sürümdeki ETABS'e bağlanmalıdır. v22 DLL'i ile ETABS 19 denenmedi.
- Yollar `App.config` > `appSettings` içinde tanımlıdır. Ortam değişkeni yalnızca yedek olarak okunur.
  - `ETABSProgramPath` bulunamazsa kurulu en yeni ETABS kullanılır.
  - Kütüphane dosyası bulunamazsa aynı dosya adı o ETABS'in `Property Libraries` klasöründe aranır. İki durumda da günlüğe uyarı yazılır.
- ETABS 22'de kaydedilen model ETABS 19'da açılmaz.
- **Birimler:** program ETABS'i `kN_mm_C` birimine alır. Tüm hesaplar kN–mm'dir (gerilme kN/mm²; 1 MPa = 0,001). Kesit kütüphanesi mm olmalıdır.
- VB büyük/küçük harf duyarsızdır: aynı kapsamda `F` ve `f`, `MB` ve `Mb` **aynı değişkendir**.

### B2. Dosyalar ve sorumluluklar
| Dosya | Sorumluluk |
|---|---|
| `MainForm.vb` / `.Designer.vb` | Form girdisi → `FormInfo_`, doğrulama (`Control`), tohum, ana döngü, yedek okuma, Check Structure |
| `OptimizationClass.vb` | Metasezgiseller (HS, BBO, Whale, Dandelion), bellek güncelleme, global en iyi, yedek ve sonuç XML'i. **ETABS'e doğrudan erişmez.** |
| `ETABSClass.vb` (`ETABS_Class`) | ETABS ile ilgili her şey: modeli okuma, sınırlar, kesit atama, analiz, kısıtlar, ceza, maliyet, kompozit entegrasyonu |
| `CompositeColumn.vb` | Saf AISC 360-16 / 360-22 kompozit hesabı (`CompositeCode_`). **ETABS'e bağımlı değildir**, bağımsız test edilebilir. |
| `Structures.vb` | Veri yapıları (Structure / Enum). Yedek XML'e girdiği için alan adları değiştirilmemelidir. |
| `EncasedSections.xml` | Kompozit kolon ayarları ve birim maliyetler (`EncasedSettings_`) |

### İş parçacıkları (form ve koşu)
- Koşu (ETABS çağrıları, arama, final) `MainForm.RunWorker` içinde, arka plan STA iş parçacığında çalışır. ETABS COM nesneleri bu iş parçacığında oluşturulur ve **yalnızca bu iş parçacığından** çağrılır.
- Form denetimlerine yalnızca form iş parçacığında erişilir. İş parçacığı içinden `UI(...)` (bekleyen `Invoke`) veya `SetPhase` (`BeginInvoke`) kullanılır.
- Form girdisi (doğrulama, `FormInfo_Read`, yedek okuma) iş parçacığı başlamadan önce `Start_Click` / `PrepareRun` içinde okunur. İş parçacığı içindeki kod form değerlerini değil `FormInfo` alanını kullanır.
- `ETABS_Class.StatusHandler` durum satırını, `ETABS_Class.MessageHandler` mesaj kutularını form iş parçacığına yönlendirir. Mesaj kutusu için `ETABS_Class.ShowMessage` kullanılmalı, doğrudan `MsgBox` kullanılmamalıdır. Test programlarında bu işleyiciler boştur ve mesaj doğrudan gösterilir.
- Durdurma (`StopRequested`) değerlendirmeler arasında kontrol edilir (`StopNow`): ara yedek yazılır, ETABS `Close` ile kapatılır.

### B3. Temel veri modeli
- **Tasarım değişkeni** `Member_.DesignVariables(v)` = `WSections` listesindeki indeks. `WSections` alana (A) göre artan sıralıdır ve yalnızca `DESIGNATION = W` kesitlerini içerir.
- `v` (değişken indeksi) ≠ grup indeksi:
  - Değişkenden gruba: `SteelFrameDesignGroupIDs(v)`.
  - Grup adından değişkene: `VarIndex(GroupName)`.
  - `Sect_Ind(...)` dizisine **asla grup indeksiyle erişilmez**.
- Değişken olan gruplar: tüm üyeleri aynı tasarım prosedürüne sahip ve prosedürü *Steel Frame Design* olan çerçeve grupları.
- Kompozit grup (`Group_.IsComposite`): kompozit modda, tüm üyeleri düşey (Z) olan değişken gruplar.
- Arama sözlükleri (`PointIndex`, `FrameIndex`, `GroupIndex`, `VarIndex`) başlangıçta kurulur. Döngü içinde `ToList().FindIndex` gibi doğrusal aramalar kullanılmaz.

### B4. Değerlendirme akışı (değiştirirken korunmalı)
```
Evaluate(Member, applyRepair)
 └ önbellek (UseCache, yalnızca düzeltmeli değerlendirmede): anahtar = düzeltme ÖNCESİ vektör → isabet varsa ETABS çağrılmaz
 └ SetAndAnalyze(Sect_Ind, geometri düzeltmesi)   E1 geometri → E2 kesit ata (yalnızca değişen gruplar) → E3 analiz + durum kontrolü
 └ AnalysisFailed ise → Penalty = 10 (program durmaz)
 └ Penalty
     RepairMode = Sequential (eski akış, eski yedeklerde varsayılan):
       F_Evaluate_Drift : F1 göreli öteleme → (F2 + yeniden analiz) → F3 tepe ötelemesi → (F4 + yeniden analiz)
       G_Evaluate_PMM   : G1 çelik tasarım + G1_2 kompozit kontrol → (G2 + yeniden analiz + F + G1)
     RepairMode = Combined (formda varsayılan):
       CombinedRepair   : F1 + F3 + G1 → RepairSteps (F2/F4/G2 adımlarının değişken başına EN BÜYÜĞÜ) → tek yeniden analiz → F1 + F3 + G1
     H geometrik        : kolon-kolon ve kiriş-kolon oranları
     ceza = tüm kısıtlar, SON analiz durumu üzerinden
 └ Cost = CostStProfile (çelik modda ağırlık, kompozit modda göreli maliyet)
 └ PenalizedCost = Cost · (1 + Penalty)^3
```
- Sıralı modda bir değerlendirme en fazla 6 analiz ve 3 tasarım yapar; birleşik modda en fazla 2 analiz ve 2 tasarım. 525M modelinde süreler 68 s ve 26 s'dir.
- İki mod aynı başlangıç vektöründen farklı sonuçlar üretir. Karşılaştırmalı çalışmalarda mod `FormInfo.RepairMode` ile sabitlenir ve sonuç XML'inin yedeğinde saklanır.
- `SetAndAnalyze`, vektör `LastAnalysed` ile aynıysa analizi atlar. Modeli `SetAndAnalyze` dışında değiştiren her kod (çalışan durumlar, kesit dönüşümü, otomatik listeler) `InvalidateAnalysis()` çağırmalıdır.
- Düzeltme fonksiyonları (`F2`/`F4`/`G2`) yalnızca vektör gerçekten değiştiyse `True` döndürür.
- Geometri düzeltmesi (E1) değişkeni `[Lb, Ub]` içinde tutar (`NearestFeasible`).
- Formdaki sayılar `TryNum` ile okunur (invariant culture, "," → "."). `IsNumeric` / `CDbl` kullanılmaz.
- **P-Delta ve servis durumları** (`EnablePDelta`, `EnsureServiceLateralCases`) yalnızca çalışma kopyasında ve `InitializeLoadCases`'tan önce çalışır. Ön tanımlı P-Delta için OAPI'de setter yoktur; `P-Delta Option Definition` tablosu kullanılır. Servis durumları `SRV_<desen>` adını taşır.
- **Deprem öteleme büyütmesi** (`SetSeismicDriftFactors`, `DriftFactor`): yalnızca `LateralCasesOnly` modunda, deprem durumlarının U1/U2 değerleri `F1_1_UpdateJointDispCore` içinde `SeismicDriftAmplification` ile çarpılır.
- Çelik tasarım sonuçları tek `GetSummaryResults("All")` çağrısıyla okunur ve `FrameIndex` üzerinden gruplara dağıtılır.
- `RunCases` (çalışan durumlar) önbelleğe alınır. Çalışan durumları değiştiren kod `RunCases = Nothing` yapmalıdır.
- Algoritmalardaki normal dağılım `NormalRnd` (tohumlu `Rnd` + Box-Muller) ile üretilir; `New Random()` kullanılmaz.
- `E2` hangi kesitin atandığını `Assigned()` ile izler. Kesitler `E2` dışında atanırsa (otomatik listeler, `Initialize_UBLB`) `ForgetAssignedSections()` çağrılmalıdır.
- `E3` artık `File.Save` çağırmaz: model `WorkFile` üzerinden açıldığı için `RunAnalysis` dosya yolunu bilir. Analiz ETABS süreci içinde çalıştırılır (`SetSolverOption_3`, process 1), bu analiz başına yaklaşık %10 kazandırır.
- **Önbellek ve döngü sonu:** önbellek isabetleri analiz sayacını (`iter`) artırmaz. Ana döngü, art arda `MAX_STALL_LOOPS` (20) çevrimde yeni analiz yapılmazsa yakınsamış kabul edilip sonlanır.
- **SkipUnusedCases:** tasarım ve öteleme kontrolünde kullanılmayan yük durumları çözülmez. Kullanılan durumların başlangıç ve modal durumları, tüm Modal durumlar ve `~` ile başlayan iç durumlar korunur. Tanınmayan bir durum tipi varsa hiçbir durum kapatılmaz. `Opt_Finalize`, final analizinden önce `RestoreRunCases` çağırır; böylece `_best.EDB` tüm sonuçları içerir.
- **Süre ölçümü:** `Clock("ad")` ile her ETABS işleminin süresi ve çağrı sayısı tutulur, `Close` sırasında `Info: timing …` satırıyla yazılır. Yeni bir ETABS işlemi eklenirse süresi de ölçülmelidir.
Kurallar:
- Her yeniden analizden sonra, cezada kullanılan kısıtlar **yeniden hesaplanmalıdır**. Eski sonuçla ceza hesaplanmaz.
- Düzeltme (repair) adımları yalnızca `applyRepair = True` ve Check Structure kapalıyken çalışır. Final değerlendirmesi ve Check Structure düzeltme yapmaz.
- Düzeltme adımları `Sect_Ind` dizisini **yerinde** değiştirir; bu değişiklik bireye geri yazılır (Lamarck yaklaşımı). Adımlar değişkeni `[Lb, Ub]` aralığında tutar (`StepVariable`).
- API çağrıları `ret` döndürür; `ret <> 0` ise `Errorlogprint` ile kaydedilip yukarıya iletilir.
  - `Stop`, `MsgBox` veya yakalanmamış istisna kullanılmaz. `MsgBox` yalnızca formda, `Close` içinde ve `Opt_Finalize` içinde kullanılır.
  - `ETABS_Class.Quiet = True` ise `Close` ve `Opt_Finalize` mesaj kutusu göstermez (toplu koşular, testler).
- `OptimizationClass.LogError` ve `MainForm.LogError` mesajı `Errorlogprint`'e iletir. Kendi kendini çağıran bu tür metotlara dikkat edilmeli: Aşama 5'te bulunan sonsuz özyineleme her koşunun sonunda programı çökertiyordu.
- Tasarımın kötü olmasından kaynaklanan durumlar (analizin tamamlanmaması, tasarım sonucu olmaması) **hata değil, ceza** olarak ele alınır (`AnalysisFailed`).
- Sonuç okumadan önce çıktı seçimi yapılmalıdır:
  - Öteleme: `SelectOutputCases`.
  - Kompozit kuvvetler: `SelectOutput(dayanım kombinasyonları)`.
  - Modal, burkulma ve `~` ile başlayan durumlar yer değiştirme olarak okunmaz.

### B5. Kompozit kolon kuralları
- Formüller `CompositeColumn.vb` içindedir; ETABS kodu yalnızca kuvvetleri okuyup `CompositeMemberCheck.Ratio` fonksiyonunu çağırır.
- Yerel eksenler ETABS ile aynıdır: 2 ekseni derinlik (I kesitin gövdesi) yönündedir. M3 / V2 güçlü eksene, M2 / V3 zayıf eksene aittir. ETABS'te basınç eksenel kuvveti negatiftir.
- **Kesitin oluşturulması:**
  - ETABS 19 ve 22 API'si Section Designer ile kesit oluşturamaz (v22'de `cPropFrameSDShape` yalnızca `Get…` metotları içerir). Kompozit kesitler `SetGeneral` ile tanımlanır (`EC_<W adı>`, çelik malzeme).
  - Rijitlik: EI_eff / Es. Ağırlık ve kütle `SetModifiers` ile verilir (indeks 6 ve 7).
  - Her kesit bir koşuda bir kez oluşturulur (`CreatedSections`).
- Kompozit gruplar kesit atamasından sonra `SetDesignProcedure(…, 7)` ile ETABS çelik tasarımından çıkarılır. Yeniden açılışta `EC_` önekli kesitler yine değişken kabul edilir.
- **ETABS kompozit kolon tasarımı (ETABS 20+, `VerifyCompositeWithETABS`)**: yalnızca final değerlendirmesinde ve Check Structure'da çalışır.
  - Akış:
    1. Tasarımın General kesitleri aynı adla `EncasedRectangle` kesitlerine dönüştürülür.
    2. Kesitler yeniden atanır ve tasarım prosedürü `SetDesignProcedure(…, 13)` ile kompozit kolon yapılır. Aramada atanan 7 (No Design), kesit yeniden atanınca sıfırlanmaz.
    3. Analiz yapılır ve aynı analizde iç çözücü (`G1_ConsPMM`) çalıştırılır.
    4. `DesignCompositeColumn.SetCode` + `StartDesign` çalıştırılır.
    5. Sonuçlar `Composite Column Summary - <kod>` tablosundan okunur. Grup başına oran `ETABSRatioByVar` alanına yazılır.
    6. `Opt_Finalize` koruması: oranı 1'i aşan grup `StepUpETABSFailures` ile bir üst kesite (Ub içinde) çıkar. Ardından `Evaluate(applyRepair:=False)` ve yeniden doğrulama yapılır (en fazla `ETABS_GUARD_STEPS = 3`).
  - İç dayanım oranı `CompositeStrengthFactor` (App.config) ile çarpılır. `CompositeStrength` çarpılmış değerdir; kalibrasyon oranı hesaplanırken katsayıya bölünür.
  - API bilgileri:
    - Gömülü kesit ve donatı için OAPI setter yoktur. `SetRebarColumn` gömülü kesitte `ret = 1` döner. Kesit ve donatı `DatabaseTables` ile yazılır: `Conc Encasement Rectangle` ve `Concrete Column Reinforcing` tabloları.
    - `DesignCompositeColumn.GetSummaryResults` ETABS 22.6'da kaymış veri döndürür (çerçeve adı yerine kesit adı, PMM = 0). Sonuçlar tablodan okunur.
    - `SetDesignProcedure` değerleri `GetDesignProcedure` ile aynıdır: 0 = malzemeden varsayılan (gömülü kesitte 13), 7 = No Design, 13 = kompozit kolon. `1` gömülü kesitli bir grupta gizli ETABS'i kilitledi (muhtemelen onay penceresi); kullanılmamalı.
  - Tablo kuralları (`SetTable`):
    - İçe aktarma **tüm tabloyu değiştirir**: tabloda olmayan kayıtlar modelden silinir. Bu yüzden mevcut kayıtlar da geri yazılır.
    - Kilitli modelde tablo düzenlemesi hatasız ama **etkisiz** kalır; önce `SetModelIsLocked(False)` çağrılmalı.
    - Tablo düzenlemesi analiz sonuçlarını siler.
    - Boş alan varsayılan değil 0 olabilir; özellik çarpanları (`AMod` …) açıkça 1 yazılır.
  - Donatı yerleşimi iç çözücüyle aynıdır: yüz başına n çubuk (`NumBars3Dir = NumBars2Dir = n`). Net pas payı = `RebarCover − TieDiameter − çubuk/2`, yani çubuk merkezi yüzeyden `RebarCover` uzaklıktadır.
  - **Arama sırasında gömülü kesit kullanılmaz.** 525M modelinde 289 gömülü kesit tanımlıyken bir analiz 128 s sürdü (General section ile yaklaşık 10 s). 289 kesitin içe aktarılması yaklaşık 2 dakika, ETABS kompozit tasarımı 36–200 s sürdü.
  - İç çözücüde `PMMRatio = max(dayanım, detay)`. ETABS ile karşılaştırmada `CompositeStrength` (yalnızca dayanım) kullanılır, `CompositeDetailing` ayrıca gösterilir.
- Yeni bir kesit tipi (FilledBox, FilledPipe) optimizasyona bağlanacaksa:
  - Ayrı bir katalog ve değişken uzayı gerekir.
  - `Encased(SecID)`, `CostStProfile`, `DescribeVariable` ve `H_Evaluate_GeometricPenalty` genelleştirilmelidir.
- **Yönetmelik sürümü:** `CompositeSection.Code` (`AISC360_16` = 0, `AISC360_22` = 1).
  - Akış: formdaki *Composite code* → `FormInfo.CompositeCode` → `ETABS_Class.Encased()` kesite atar.
  - Sürüme bağlı kod yalnızca üç yerdedir:
    - `ConcreteShear` (I4-1, Kc)
    - `BalancePoint` + `InteractionRatio` (I5-1a/b)
    - `DesignTorsion` + `CompositeMemberCheck.Ratio` (H3-6)
  - Yeni bir fark eklenirse aynı yerlere `If Code = CompositeCode_.AISC360_22` ile eklenir. İki sürümün ortak formülleri çoğaltılmaz.
  - Gömülü kesitlerde iki sürüm **aynı sonucu** verir (testle doğrulandı). 360-16 modu önceki sürümün sonuçlarını birebir korumalıdır.
  - Eski yedeklerde bu alan yoktur; değer 0 = 360-16 olarak okunur.
- Rehber (`AISC360_16_…md`, `AISC360_22_…md`) ile AISC metni çelişirse yönetmelik esas alınır ve fark `DEGISIKLIKLER.md` dosyasına yazılır.
  - 360-22 metni elde değildir. Rehberden alınıp doğrulanamayan noktalar DEGISIKLIKLER.md'de "doğrulanmalı" olarak işaretlidir.
- Formüllerde değişiklik yapıldıktan sonra doğrulama testi tekrarlanmalıdır: gömülü W10x45 PSDM ile kapalı form arasındaki fark %1'in altında kalmalıdır (bkz. DEGISIKLIKLER.md).
- Gömülü kesitte Mn, `FlexureMethod` ile seçilir: `StrainCompatibility` (varsayılan) veya `PlasticStress`. `EncasedSettings_.Build` bu değeri kesite atar. `Transformed()` içindeki plastik modül (General section) PSDM ile kalır. Bu değer ETABS'te kullanılmaz; kesit 'No Design' durumundadır.
- ETABS ile karşılaştırma yöntemi: `GetOverwrite` ile ETABS'in Cm, B1, φPn, φPnt değerleri okunur; eğilme kapasitesi `Composite Column Summary` tablosundaki oranlardan geri hesaplanır (DEGISIKLIKLER.md, Aşama 7).
- Regresyon testi: aynı kesit ve kuvvet setinde 360-16 modu, git'teki önceki `CompositeColumn.vb` ile birebir aynı çıktıyı vermelidir. Fark yalnızca bilinçli düzeltmelerden kaynaklanabilir.

### B6. Algoritmalar
- **Yöntem kataloğu** (`OptimizationMethods.vb`, `MethodCatalog`): her yöntemin adı, açıklaması, kaynağı, parametreleri (`ParamDef_`: anahtar, etiket, varsayılan, sınırlar, tam sayı / seçenek), `UsesMemoryUpdate` ve `LevyNote` bilgisi buradadır. Form parametre kutusunu buradan kurar. Yeni bir yöntem için: `OptMethod_` sonuna değer, katalogda tanım, `Main_<yöntem>` ve `OptimizationClass.Main` içinde çağrı eklenir.
- Parametre değerleri: HS ve BBO kendi yapılarındadır (eski yedeklerle uyum), diğerleri `OptInfo.Params` (`MethodParam_`) içindedir. Okuma ve yazma `MethodCatalog.GetParam` / `SetParam` ile yapılır; eksik değerde katalog varsayılanı kullanılır.
- Yöntem durumu `OptInfo.State` (`AlgorithmState_`) alanındadır ve yedeğe girer: ABC `Trials`, ACO `Pheromone`, GWO `Leaders`, SSO `SpiderFemales`. SSO'da örümceğin cinsiyeti `Member_.IsMale` alanındadır; bellek her döngüde sıralandığı için sıra numarası kullanılamaz. Yeni bir tasarım üyenin yerine geçerken cinsiyeti korunur (`Cand.IsMale = Memory(hedef).IsMale`). Diğer yöntemler bu alanı kullanmaz. `InitMethodState` başlangıç belleğinden sonra, `InitMethodState(True)` yedekten devamda eksik veya boyutu yanlış durumu kurar. `ClearDuplicates` belleği değiştirirse üye başına durum (ABC) sıfırlanır.
- Yeni yöntemler (ABC, ACO, BSO, Crow, Firefly, GOA, TLBO, TSA, GWO, HBA, AO) sürekli konumu `ToMember` ile yuvarlayıp sınırlara kırpar. Değerlendirme `EvaluateOnly` (onarım, önbellek, global en iyi) ve `EvalAt(üye, hedef, açgözlü)` ile yapılır; formdaki Memory update bunlarda kullanılmaz.
- Kaynak VB programlarındaki (SteelStruc klasörü) algoritmalar literatüre göre yeniden yazıldı; oradaki hatalar (DEGISIKLIKLER Aşama 15) taşınmadı.
- **SSO** (`SocialSpider.vb`):
  - Fortran kaynağından çevrildi. Çeviri kararları ve taşınmayan hatalar: `Ajan/Yetenekler/Optimizasyon_Yetenegi/SSO_CEVIRI_NOTLARI.md`.
  - Çiftleşme ve sıçrama, döngünün son üyesinden (`Imem = Memory.Count - 1`) sonra çalışır.
  - Kolonide zaten bulunan bir tasarım analiz edilmez; bir değişkeni bir kesit kaydırılır.
- `GlobalBest` yalnızca **cezasız** çözümlerle güncellenir. O zamana kadar değişkenleri 0'dır ve `PenalizedCost = ∞` olur.
- En iyi çözüme yönelen adımlar (Dandelion iniş aşaması, Levy uçuşu, Whale lideri) `OptimizationClass.Leader()` kullanır. Leader, uygun çözüm varsa `GlobalBest`'i, yoksa belleğin en iyisini döndürür. Doğrudan `GlobalBest.DesignVariables` kullanılmaz.

### B7. Rastgelelik
- Tüm rastgele sayılar VB `Rnd()` (algoritmalar) ve `ETABS_Class.Rng` (geometri düzeltmesi) üzerinden üretilir. Her ikisi de `MainForm.SetRandomSeed` ile tohumlanır.
- Yeni kodda `New Random()` oluşturulmaz; aynı tohum aynı sonucu vermelidir.

### B8. Dosya ve çıktı kuralları
- **Çalışma kopyası:** girdi modeli hiçbir zaman değiştirilmez.
  - `InitializeETABS` modeli `WorkFolder` klasörüne kopyalar (`App.config`; boşsa `%TEMP%\SteelFrameOpt`). Klasör adı `<model>_<yyyyMMdd_HHmmss>` biçimindedir. ETABS bu kopyayı (`WorkFile`) açar.
  - `E3_Analysis` her analizde **`WorkFile`** üzerine kaydeder. Model dosyasına kaydetme yalnızca `WorkFile` ve `_best.EDB` için yapılır.
  - Çalışma klasörü `Shutdown` içinde (`Close` çağırır), ETABS kapandıktan sonra silinir. Silinemezse uyarı yazılır, koşu bozulmaz.
  - ETABS'i kapatan her yol `Close` veya `Shutdown` üzerinden geçmelidir; aksi halde geçici klasör kalır.
  - ETABS örneği yalnızca `StartInstance` ile açılır ve `ExitInstance` ile kapanır. `ExitInstance` kendi sürecini (`EtabsPid`, `CreateObject` öncesi ve sonrası süreç listesinin farkı) bekler, 60 s sonra hâlâ çalışıyorsa sonlandırır; kullanıcının ETABS'ine dokunulmaz.
  - **ETABS yeniden başlatma** (`RestartETABS`, `FormInfo.RestartEvery`): `SetAndAnalyze` içinde, analizden önce. Model kaydedilir → `ExitInstance` → `StartInstance` → `OpenFile(WorkFile)` → `SessionSettings` (birim, çözücü) + `ReapplyDesignSettings` (çelik kodu, kombinasyonlar, çalıştırılmayan durumlar) → `InvalidateAnalysis`. `Assigned()` geçerli kalır (kesitler modelde).
  - ETABS 22.6 yeniden açılan modelde çelik dayanım kombinasyonu seçimini ilk tasarımdan önce siliyor. `G1_1_Design` her tasarımdan önce seçili sayıyı kontrol eder ve gerekirse yeniden seçer.
  - `.e2k` ile yeniden oluşturma kullanılmaz (kaldırıldı): ETABS 22.6'da `.e2k` gömülü kesit verisini ve bazı model verilerini taşımıyor; 525M'de yerdeğiştirme %15 farklı çıktı.
  - `CreateWorkCopy`, 2 günden uzun süre yazılmamış eski çalışma klasörlerini siler (`DeleteStaleWorkDirs`).
- Çıktılar:
  - `ErrorLog.txt` (model klasöründe). `Errorlogprint` mesajı `Info:` veya `Warning:` ile başlamıyorsa başına `Error:` ekler. Bilgi ve uyarı mesajları bu öneklerle yazılmalıdır.
  - Yedek `<çıktı>.backup.xml` (`OptimizationClass.BackupPath`): geçici dosyaya yazılır, eskisi `.bak` olur (`Backup_Write`); okuma `.bak`'a düşer (`Backup_Read`). Çevrim içindeki zaman tabanlı yedek (`midLoop`) `ILoop - 1` yazar, devamda çevrim tekrarlanır.
  - Yedekte `Model` (`ModelIdentity_`: model yolu, boyut, tarih, SHA-256, grup adları, kesit sayısı) bulunur. `CheckContent` içerik denetimi yapar, uymazsa `.bak` kullanılır. Form `Backup_Read` içinde model farkını ve özeti sorar; `CheckRestoredModel` ETABS'ten sonra grupları ve kütüphaneyi karşılaştırır.
  - `CostBreakdown`, `CostStProfile` ile aynı miktarları ve birim maliyetleri kullanmalıdır (toplam satırı = değerlendirme maliyeti). Birinde değişiklik yapılırsa diğeri de güncellenmelidir.
  - Excel kitapları `ExcelExport.vb` (OpenXml SDK) ile yazılır: `<çıktı>.xlsx` (`Yazdir_Final`), `<çıktı>.check.xlsx` (Check Structure).
  - Sonuç önbelleği `<çıktı>.cache.txt` (`AttachCacheFile`, `AddToCache`): satır başına bir kayıt, yalnızca ekleme; okunamayan satırlar atlanır.
  - sonuç XML'i
  - `<model>_best.EDB` (girdi modelinin klasöründe)
  - Check Structure çıktısı: `<çıktı>.check.xml`
- `GlobalBestPrint` biçimi `"Grup: <W adı> [ek bilgi]"` şeklindedir. `Read_SectionID` ilk kelimeyi W adı olarak okur; biçim bozulmamalıdır.
- `Structures.vb` içindeki alanlar XML serileştirmesine girer. Alan silmek veya yeniden adlandırmak eski yedekleri bozar; yeni alan eklemek güvenlidir.

### B9. Derleme ve test (Visual Studio olmadan)
- Derleme kontrolü: Roslyn `dotnet "<sdk>/Roslyn/bincore/vbc.dll"`; referanslar .NETFramework v4.7.2 reference assemblies ve `ETABSv1.dll`. `dotnet msbuild` bu projede resx üretemez.
- ETABS testleri modelin **kopyası** üzerinde yapılır. Test sonunda `ApplicationExit` çağrılmalı ve arkada kalan `ETABS.exe` olmamalıdır.
- Çelik kodu:
  - ETABS 19 API'sinde `AISC 360-16` yoktur; `AISC 360-10` kullanılır.
  - ETABS 22, `AISC 360-22` ve `AISC 360-16` kodlarını kabul eder.
  - v22'de `SetCode` bazı adları normalleştirir (ör. `Eurocode 3-2005` → `EN 1993-1-1:2005`). Atanan kod `GetCode` ile okunup günlüğe yazılır. Geçersiz ad `ret = 1` döndürür.
- `vbc` derlemesinde referans yolları boşluk içerir; yanıt dosyasında (`@args.rsp`) tırnak içinde yazılmalıdır.
- Test kodundaki `Module` üye adları (`F`, `Mat`, `W` …) büyük/küçük harf duyarsızlığı yüzünden kaynak koddaki adlarla çakışabilir.
- **GUI kontrolü:** `MainForm.Designer.vb` elle düzenlenir.
  - Değişiklikten sonra form bir test programında açılır ve her sekme `DrawToBitmap` ile PNG'ye kaydedilip incelenir.
  - Formun resource dosyası (`.resx`) yoktur; form bu yüzden test programında açılabilir.
  - Yeni kontroller için dört yere ekleme yapılır: `Me.X = New …`, ebeveynin `Controls.Add`, özellik bloğu ve `Friend WithEvents`.
- **Uçtan uca optimizasyon testi:** `OptimizationClass` formdaki `Init` ve ana döngü gibi kurulur ve `Opt_Finalize` ile bitirilir. `Quiet = True` olmadan `Close` mesaj kutusunda bekler.
- Test programının stdout'u `<model>.out` adıyla yazılmamalıdır. ETABS'in analiz dosyası `<model>.OUT` onun üzerine yazar (Windows büyük/küçük harf ayırmaz).
