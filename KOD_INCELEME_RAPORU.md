# Kod İnceleme Raporu — Kompozit Kolonlu Çelik Çerçeve Optimizasyonu (SSO_CF / SFCS)

> **Not (Aşama 2, 2026-10-03):**
> - Aşama 1'de buraya eklenen "kolonlar gömülü kesittir" düzeltmesi **geri alındı**. Kullanıcı hedefi netleştirdi: kompozit kolonlar **dolgulu tüp** olacak, yani çelik kutu (CFT, `FilledTube = 29`) veya boru (CFP, `FilledPipe = 30`) içinde beton. Bu yüzden raporun CFT'ye ilişkin bulguları (A1.1, A1.7) geçerli.
> - Kolon grupları ayrı ayrı çelik veya kompozit seçilebilecek (hibrit tasarım). Kompozit döşeme isteğe bağlı.
> - İncelenen iskelet kod (`CFCS.vb`, `Class1.vb`, `SSO_CF.vb`) kullanıcı kararıyla Aşama 2'de bu depodan **kaldırıldı**. Yerine referans projenin (SteelFamewithCompositeColumn_ETABS) test edilmiş kodu alındı.
> - Rapor, kaldırılan kodun kaydı olarak duruyor. Satır numaraları orijinal `SFCS` ve `SSO_CF` klasörlerindeki dosyalara göre.
> - Bölüm 6'daki yol haritasının yerini `Ajan/Gorevler/AKIS_SEMASI.md` aldı.

- **Tarih:** 2026-10-03
- **Kapsam:** Henüz kod değiştirilmedi. Bu rapor yalnızca okuma ve ölçümlere dayanıyor.
- **Hedef ortam:** ETABS 22.6 (`ETABSv1.dll`, .NET Standard 2.0), VB.NET, Visual Studio 2026.
- **İlgili GitHub deposu:** `ibrahimaydogdu/SteelFamewithCompositeTubeColumn_ETABS`. Depo var ama boş; henüz commit yok.

---

## 1. İncelenen dosyalar

| Klasör | Dosya | Satır | Not |
|---|---|---|---|
| `SSO_CF\SSO_CF\` | `SSO_CF.vb` | 345 | Ana form kodu. ETABS 2016 API'sini (`ETABS2016`) kullanıyor. |
| | `SSO_CF.Designer.vb` | 1112 | Form tasarımı. Varsayılan yollar `G:\Copy\API\...` |
| | `SSO_CF.vbproj`, `app.config`, `My Project\*` | — | .NET 4.8. Varsayılan platform x86. |
| `SFCS\SSO_CF\` | `CFCS.vb` | 355 | Ana form kodu. ETABS v17+ API'sini (`ETABSv1`) kullanıyor. |
| | `CFCS.Designer.vb` | 1123 | Form tasarımı. Göreli yollar `ETABS\525M\...`, ek `Button1`. |
| | `Class1.vb` | 127 | `General`: AISC XML okuyucusu ve ETABS komut sınıfı taslağı. |
| | `CFCS.vbproj` | — | .NET 4.0 Client. |
| | `AISC14M.xml` | 1,27 MB | CSI'ın kesit veritabanı; ETABS kurulumundaki dosyayla aynı. |
| | `ETABS\525M\*`, `ETABS\CivilComp3\*` | — | Test modelleri (ETABS 2016 / 16.0.1), kesit listeleri, analiz çıktıları. |

İki klasörde de md belge, test projesi veya git deposu yok. İki çözüm aynı `ProjectGuid` değerini taşıyor (`{4A44446A-…}`). Yani biri ötekinden kopyalanmış. `SFCS\SSO_CF\bin` içindeki exe de eski `SSO_CF.exe` (2017).

---

## 2. Programın amacı ve akışı

**Amaç (form ve adlardan çıkarılan):**
- Çelik çerçevede kirişleri W profili, kolonları **beton dolgulu çelik kutu (CFT, "Filled Steel Tube")** olarak boyutlandırmak.
- Boyutlandırmayı **Social Spider Optimization (SSO)** ile yapmak ve en düşük maliyeti aramak.
- Kısıtlar: AISC gerilme oranları, tepe ve kat arası öteleme sınırları (1/300), yerdeğiştirme sınırı.
- Maliyet: çelik için $/kg, beton için $/m³.

**Gerçek durum:** Program bir **iskelet**. SSO algoritması hiç yazılmamış. Formdaki SSO parametreleri (örümcek sayısı, PF, maks. iterasyon, tolerans) yalnızca sayı mı diye denetleniyor, hiçbir yerde kullanılmıyor. `Start` düğmesi şunları yapıyor:

1. `Control`: dosya yolları ve sayısal alanlar denetlenir.
2. `Initilize`: yeni bir ETABS örneği açılır ve `File.OpenFile` ile **orijinal model** yüklenir.
3. `Read_Section`:
   - birimler kN-m yapılır;
   - grup, eleman ve düğüm sayıları okunur;
   - modeldeki tüm I kesitleri (`Proptype = 1`) ve "kompozit" sanılan kesitler (`Proptype = 27`) toplanır;
   - metre başına maliyetleri hesaplanır ve kesitler ucuzdan pahalıya sıralanır.
4. `evaluate`:
   - **sabit kodlanmış** tek bir tasarım (`sect_ind`, `IsComposite`) atanır;
   - **orijinal dosyanın üzerine kaydedilir**;
   - analiz ve çelik tasarımı çalıştırılır, çelik maliyeti hesaplanır (ama hiçbir yerde kullanılmaz);
   - ardından `DesignCompositeBeam.StartDesign` çağrılır.
5. Süre yazılır. SSO_CF'de ETABS kapatılır; SFCS'de açık kalır.

Kısıt değerlendirmesi, ceza fonksiyonu, amaç fonksiyonu, çıktı dosyası ve tablo doldurma **yok**.

**Kullanılan API çağrıları:**
- `cHelper.CreateObject` / `ApplicationStart`
- `File.OpenFile` / `File.Save`
- `GetModelIsLocked` / `SetModelIsLocked`
- `SetPresentUnits(6)`
- `GroupDef.Count` / `GetAssignments`
- `FrameObj.Count` / `SetSection` / `GetPoints`
- `PointElm.Count`
- `PointObj.GetCoordCartesian`
- `PropFrame.GetAllFrameProperties` / `GetSectProps`
- `Analyze.RunAnalysis`
- `DesignSteel.SetCode` / `StartDesign`
- `DesignCompositeBeam.StartDesign`
- `ApplicationExit`

**Algoritmalar:**
- Yalnızca Fortran'dan aktarılmış bir sıralama yordamı (`Sorting`) var: O(n²) ekleme sıralaması, `GoTo` ile.
- Optimizasyon algoritması yok.

---

## 3. Hangi klasör projenin amacına daha yakın?

| Ölçüt | `SSO_CF` (aktif klasör) | `SFCS` |
|---|---|---|
| API | ETABS 2016 (`ETABS2015/2016.dll`, `ETABS.exe` referansı) — ETABS 22 ile **çalışmaz** | `ETABSv1` — ETABS 22 ile aynı aile ✔ |
| .NET | 4.8 ✔ | 4.0 Client ✘ (ETABS 22 DLL'i .NET Standard 2.0, en az 4.7.2 gerekir) |
| Test modelleri | yok (G:\ yolları) | 525M ve CivilComp3 (460Member'da `Filled Steel Tube` kesiti var) ✔ |
| Kesit havuz dosyaları | yok | `WSections.txt`, `CompositeSections.txt` ✔ |
| Ek kod | — | `Class1.vb` (AISC XML okuyucu taslağı; çağrılmıyor) |
| Kritik ek hata | — | `Control` doğrulaması hiç durdurmuyor (B2.1) |

**Sonuç:** Amaca daha yakın olan **SFCS**: ETABSv1 API'si, CFT kesitli model ve kesit havuzları orada. İki klasördeki kodun anlamlı kısmı aynı ve ikisi de yaklaşık %10'luk bir iskelet.

**Daha önemli bir gözlem:** Önceki projede (`SteelFamewithCompositeColumn_ETABS`, sürüm 2026.10.3) bu projenin ihtiyaç duyduğu altyapının neredeyse tamamı test edilmiş halde duruyor:
- ETABS 22 bağlantısı ve model kopyasında çalışma;
- `DesignCompositeColumn` ile kompozit kolon tasarımı (AISC 360-22);
- grup tabanlı kesit atama, gerilme/öteleme kısıtları, ceza fonksiyonu;
- 15 metasezgisel yöntem, yedekten devam, Excel çıktısı.

O projenin "olası sonraki işler" listesinde "dolgulu kutu / boru kompozit kesitlerin optimizasyona bağlanması" maddesi de var. Bu nedenle mimari önerim (Bölüm 6) bu iskeleti onarmak değil, o altyapıyı temel almak. **Bu karar sizin.**

---

## 4. Bulgular (öncelik sırasıyla)

Satır numaraları aktif klasördeki `SSO_CF\SSO_CF\SSO_CF.vb` (**S**) ve `SFCS\SSO_CF\CFCS.vb` (**F**) dosyalarına göredir.

### Öncelik 1 — Sonucu bozan hatalar

**A1.1 Kompozit kesit filtresi yanlış tür kodu kullanıyor** (S:108, S:126; F:106, F:124)
- Kod `Proptype = 27` arıyor. Hem ETABS 2016'da hem ETABS 22'de `27 = BuiltupUHybrid`.
- Dolgulu kutu kodu `FilledTube = 29`. Bu değer ETABS 22 DLL'inden yansımayla okundu.
- Sonuç: kompozit kesit sayısı her zaman 0 çıkıyor ve kompozit havuz boş kalıyor.
- Kolon gruplarında `SectionName2_S(10)` erişimi `IndexOutOfRangeException` verir. Bu nedenle program bugünkü haliyle hiçbir modelde sonuna kadar çalışamaz.

**A1.2 Kesit, gruplara değil yanlış nesnelere atanıyor** (S:21-30, F:19-28)
- `FrameObj.SetSection(Name, …)` çağrısında `ItemType` verilmemiş. Varsayılan `Object` olduğu için `"0"…"13"` adlı **tekil çubuklara** atama yapılıyor.
- Modeldeki gruplar ise `"1"…"14"` adını taşıyor. Doğrusu `SetSection(CStr(i + 1), kesit, eItemType.Group)`.
- Ayrıca `Name` yerel değişken değil; formun `Me.Name` özelliği değiştiriliyor.
- Sonuç: atamalar ya başarısız oluyor (`ret ≠ 0` → `Stop`) ya da rastgele çubuklara gidiyor.

**A1.3 Kompozit kolonlar hiç tasarlanmıyor** (S:49-50, F:47-48)
- `DesignCompositeBeam.StartDesign` **kompozit kiriş** tasarımı.
- ETABS 22'de kolon için `DesignCompositeColumn` gerekiyor. Gerekli adımlar:
  - `SetCode`: ETABS 22'de kodlar AISC 360-22, CSA S16-19/24, Eurocode 4-2004, IS 11384-2022;
  - `SetComboStrength`;
  - `StartDesign`;
  - `GetSummaryResults`.
- ETABS 2016 API'sinde bu arayüz hiç yok. SSO_CF klasörü bu yüzden amaca uygun değil.
- Hata mesajı da yanlış: "DesignSteel.StartDesign".

**A1.4 Maliyet hesabı yanlış ve sonucu kullanılmıyor** (S:54-88, F:52-86)
- Tüm gruplar, kolonlar dahil, **çelik** maliyet dizisiyle (`cost_sec_steel_s`) fiyatlanıyor.
- `sect_ind` kompozit gruplarda kompozit listenin indisi olduğu halde çelik listede kullanılıyor.
- Kompozit maliyet dizisi `cost_sec_com_s` hiç kullanılmıyor.
- Hesaplanan `steelcost` hiçbir yere aktarılmıyor.
- Grupta çubuk dışı bir nesne varsa (`ObjectType ≠ 2`), bir önceki çubuğun `Point1/Point2` değerleriyle uzunluk **yeniden eklenir**.

**A1.5 Tasarım değişkenleri sabit ve modelle tutarsız** (S:9-12, F:7-10)
- `sect_ind` 15 eleman, `IsComposite` 14 eleman.
- 525M modeli bu sayıya uyuyor: gruplar 1–4 kiriş, 5–14 kolon (ölçüldü).
- 460Member modeli uymuyor: 13 grup, 1–5 kiriş.
- Kiriş/kolon ayrımı modelden okunmalı. Değişkenler optimizasyon algoritmasından gelmeli.

**A1.6 Kısıt ve amaç fonksiyonu yok**
- Gerilme oranları (`DesignSteel.GetSummaryResults`, `DesignCompositeColumn.GetSummaryResults`) okunmuyor.
- Öteleme ve yerdeğiştirme sonuçları (`Results.JointDispl` / `StoryDrifts`) okunmuyor.
- Formdaki `displimit`, `TS_Limit`, `IS_Limit` değerleri yalnızca denetleniyor; kullanılmıyor.
- Ceza fonksiyonu yok, dolayısıyla optimizasyonun amaç fonksiyonu da yok.

**A1.7 Kesit havuzu dosyaları okunmuyor; havuz modelle uyumsuz**
- `WSections.txt` ve `CompositeSections.txt` yalnızca var mı diye kontrol ediliyor (S:149-150).
- Havuz olarak modeldeki **tüm** I kesitleri alınıyor:
  - 525Member'da AISC14'ten ~100 inç adlı W14X500… profili;
  - kullanıcı kesitleri `SteelCol` ve `SteelBm`.
- `WSections.txt` ise metrik adlar içeriyor (`W100X19.3`…, 273 adet). Bu adlar modelde tanımlı değil.
- `CompositeSections.txt` 15 kutu kesit listeliyor (`300x300x12.7` … `1000x1000x31`). Bunlar hiçbir modelde yok.
  - 525M'de hiç CFT kesiti yok.
  - 460Member'da yalnızca `FSec1` (1000×1000×31,25) var.
- **API kısıtı:** ETABS 22 `cPropFrame`'de `SetTube` var ama **dolgulu kutu tanımlayan bir metot yok**. CFT havuzu ya modelde önceden tanımlanmalı ya da DatabaseTables ile oluşturulmalı. Hangisinin olacağı API testiyle doğrulanmalı.

**A1.8 Varsayılan model boş**
- Formdaki varsayılan model `525Memberv2.EDB`. Bu modelin `$et` dosyasında düğüm, çubuk ve grup **yok** (301 satır).
- Dolu modeller: `525Member` (525 çubuk, 14 grup) ve `460Member` (470 çubuk, 13 grup, CFT kolonlu).

**A1.9 Orijinal model üzerine yazılıyor** (S:33, F:31)
- `File.Save(saplocation.Text)` her değerlendirmede kullanıcının modelinin üzerine kaydediyor.
- Analiz dosyaları da (`.Y0x`, `.K~`, `.msh`) model klasörünü kirletiyor. Bunlar zaten mevcut.
- Çalışma bir geçici kopya üzerinde yapılmalı. Bu, proje kuralımızla da gerekli.

### Öncelik 2 — Çalışmayı engelleyen hatalar

**B2.1 SFCS'de doğrulama hiçbir zaman durdurmuyor** (F:146-161 ve F:322)
- `durdur` değişkeni Integer, ama hata durumunda ona `True` atanıyor. VB'de `True` sayıya çevrilince `-1` olur.
- `start_Click` ise `istop = 1` koşuluna bakıyor; bu koşul hiç sağlanmıyor.
- Sonuç: hatalı girişle program devam eder.

**B2.2 Doğrulamada tür dönüşümü hatası** (S:154-163, F:152-161)
- `Not IsNumeric(x) Or x = vbEmpty` ifadesinde `Or` kısa devre yapmaz, iki taraf da hesaplanır.
- Metin sayısal değilse (ör. "abc"), `"abc" = 0` karşılaştırması `InvalidCastException` atar.

**B2.3 Proje ETABS 22 ile derlenemiyor veya bağlanamıyor**
- SSO_CF şunlara başvuruyor: `ETABS2015.dll`, `ETABS2016.dll`, `ETABS.exe` (100 MB, `obj\x86\Debug` içinde), `CsiNaDesign.dll` (dosya yok).
- SFCS `obj\x64\Debug\ETABSv1.dll` dosyasına başvuruyor; bu dosya yok. Hedefi .NET 4.0 Client.
- Referans yolları `obj` klasörünü gösteriyor; temiz derlemede silinir.
- Varsayılan platform x86. ETABS 22 64 bit.

**B2.4 ETABS yolu yanlış** (S:172, F:172)
- `…\ETABS 2016\ETABS.exe` ve `…\ETABS v1\ETABS.exe` yollarının ikisi de bu makinede yok. Kurulu olan `…\ETABS 22\`.
- `CreateObjectProgID("CSI.ETABS.API.ETABSObject")` ile sürümden bağımsız hale getirilebilir.

**B2.5 `Stop` deyimleri**
- Kodda yaklaşık 20 yerde `MsgBox(...) : Stop` var.
- Hata ayıklayıcı yokken `Stop` uygulamayı keser veya JIT hata ayıklayıcı penceresi açar.
- Hata, ETABS'i kapatan ve kullanıcıya anlamlı mesaj veren bir yapıyla (Try/Finally, hata günlüğü) ele alınmalı.

**B2.6 Başlatma hataları yutuluyor** (S:165-215, F:163-216)
- ETABS başlatılamazsa `Return` ediliyor, ama çağıran `SapModel = Nothing` ile devam ediyor ve `NullReferenceException` alıyor.
- `OpenFile` dönüş değeri denetlenmiyor.
- SSO_CF'de `start_Click` içindeki yerel `ret` (S:319) hiç atanmıyor. Bu yüzden her zaman "completed successfully" yazıyor.

**B2.7 ETABS kapanmıyor veya askıda kalıyor**
- SFCS'de `ApplicationExit` yok (F:344-346), her çalıştırma bir ETABS örneğini açık bırakıyor.
- Herhangi bir istisnada ise iki sürümde de ETABS açık kalıyor (Try/Finally yok).

**B2.8 Varsayılan yollar geçersiz**
- SSO_CF'de `G:\Copy\API\…` yolları bu makinede yok.
- SFCS'de `ETABS\525M\…` göreli yolu exe'nin çalıştığı klasöre (`bin\x64\Debug`) göre çözülüyor ve bulunamıyor.

**B2.9 Tasarım kodu listesi ETABS 22 ile uyumsuz** (Designer)
- SFCS'deki liste eski adları içeriyor: `AISC-LRFD99`, `AISC-ASD89`…
- `AISC-ASD01` iki kez geçiyor ve bir öğe boş.
- Ayrıca kompozit kolon kodu için ayrı seçim gerekiyor (AISC 360-22).

**B2.10 Grup sayısı varsayımı** (S:92, F:90)
- `GroupDef.Count - 1` hesabı iki şeyi varsayıyor:
  - `ALL` grubu var;
  - diğer gruplar `1…n` diye sırayla adlandırılmış.
- Fazladan bir grup (ör. kullanıcı grubu) varsa her şey kayar.
- Doğrusu `GroupDef.GetNameList` ile adları okumak.

**B2.11 `Class1.vb` çağrılırsa çöker** (yalnızca SFCS)
- `InitilizeProg` atanmamış `Command` ve `Main` nesnelerini kullanıyor → `NullReferenceException`.
- `ETABSCommands.InitilizeETABS` alanlar yerine aynı adlı yerel değişkenlere atıyor. Bu yüzden `OpenFile` her zaman `Nothing.SapModel` üzerinde çalışır.
- `Sections.ReadSections` Private ve hiç çağrılmıyor. Ek sorunları:
  - `FileStream` kapatılmıyor;
  - `XmlDataDocument` artık kullanılmaması gereken (obsolete) bir sınıf;
  - XML düğümleri ad yerine sıra numarasıyla okunuyor, bu kırılgan;
  - dosya yolu göreli (`"AISC14M.xml"`).
- Şu an ölü kod.

### Öncelik 3 — Verim

**C3.1** Grup uzunlukları her değerlendirmede yeniden hesaplanıyor.
- Çubuk başına 3 API çağrısı yapılıyor; 525 çubukta değerlendirme başına yaklaşık 1.575 çağrı.
- Geometri değişmediği için bir kez hesaplanıp önbelleğe alınmalı.

**C3.2** `File.Save` her değerlendirmede çağrılıyor. Kopya bir kez kaydedilsin; `RunAnalysis` sonraki kayıtları zaten kendisi yapıyor.

**C3.3** `DesignSteel.SetCode` ve kombinasyon seçimleri her değerlendirmede yeniden yapılıyor. Bunlar başlangıçta bir kez yapılmalı. Önceki projedeki tuzak geçerli: yeniden açılan modelde kombinasyon seçimi siliniyor, tasarım öncesinde kontrol edilmeli.

**C3.4** Kesit ataması tek tek çubuklara değil, grup başına tek çağrıyla yapılmalı (`eItemType.Group`).

**C3.5** `Sorting` O(n²) çalışıyor ve iki sorunu var:
- tür belirtilmemiş `Object` diziler (`H1`, `H3`) kullanıyor;
- `1e10` gözcü değerine dayanıyor.

Yerine `Array.Sort(maliyet, indis)` kullanılabilir.

**C3.6** ETABS gizlenmiyor (`Hide` yorum satırında), arayüz her adımda yeniden çiziliyor. Koşu ayrıca UI iş parçacığında olduğu için form donuyor. Önceki projede bunlar çözülmüştü: arka plan iş parçacığı, Hide, durum satırı.

**C3.7** Analiz sonrası yalnızca gereken sonuçlar okunmalı. Örneğin ilgili kombinasyonlar `SetCaseSelectedForOutput` ile seçilmeli.

### Öncelik 4 — Temizlik

**D4.1** İki klasörde neredeyse aynı proje var ve ikisinin `ProjectGuid` değeri de aynı.
- SFCS çözümünün proje klasörü `SSO_CF` adını taşıyor.
- `RootNamespace` değeri `WindowsApplication1`.
- `AssemblyInfo` içinde "Hewlett-Packard Company 2017" yazıyor. Model başlığı `TITLE1` da "Hewlett-Packard Company".

**D4.2** Kullanılmayan kontroller:
- `ComboBox1`, `CheckBox1` (Spider Jump);
- `itera`, `tole`, `PrFemale`, `maxiter`, `NofSpider`;
- `BtoC`, `CtoC`, `dwarn`, `preanalysis`;
- `DataGridView1-3`, `OutputLoc`;
- işleyicisi olmayan `loadpoolfile` ve `Button3`;
- boş gövdeli `Button1`;
- hiç doldurulmayan `TextBox9` (tarih).

**D4.3** Zamanlayıcı hataları:
- `"hh"` 12 saatlik biçim veriyor;
- `timedmin = Minutes*60` adı yanıltıcı;
- `avtime` her zaman 0.

**D4.4** Yazım hataları: `Initilize`, `GroupLenght`, "Problem occured". Mesajlarda İngilizce ve Türkçe karışık.

**D4.5** `Option Strict Off`. `NoWarn` 10 uyarıyı bastırıyor, bu da örtük dönüşüm hatalarını gizliyor (B2.1 ve B2.2 bu yüzden fark edilmemiş).

**D4.6** Sabit kodlanmış değerler:
- çelik yoğunluğu 7849 kg/m³ (malzemeden okunabilir);
- birim maliyetler;
- sıralama gözcüsü.

**D4.7** Kaynak ağacında ikili ve geçici dosyalar duruyor:
- `ETABS.exe` (100 MB, iki kopya);
- `ETABS2015/2016.dll/.tlb`;
- `.vs`, `.suo`, `vshost`;
- modellerin analiz dosyaları (`.Y*`, `.K~*`, `.msh`, `.LOG`, `.OUT`, `.ebk`).

Bunlar git'e girmemeli; `.gitignore` gerekiyor.

**D4.8** `AISC14M.xml` CSI'ın dosyası. ETABS 22 kurulumunda zaten var (`Property Libraries\AISC14M.xml`; ayrıca AISC15/16 sürümleri).
- Depoya koymak yerine kurulumdan okunmalı.
- Dağıtım hakkı açısından da depoya eklenmemesi daha güvenli. **Karar sizin.**

---

## 5. Model ve veri dosyalarının durumu (ölçüm)

| Model | Kat | Çubuk | Grup (kiriş / kolon) | Kolon kesiti | Kombinasyonlar |
|---|---|---|---|---|---|
| `525M\525Member` | 25 | 525 | 1–4 kiriş / 5–14 kolon | Auto-select W (`A-LatCol`) — **CFT yok** | Comb1-3 (Steel) |
| `525M\525Member_steel` | 25 | 525 | aynı | aynı | aynı |
| `525M\525Memberv2` | — | **0** | **yok** | — | — |
| `CivilComp3\460Member` | — | 470 | 1–5 kiriş / 6–13 kolon | **`FSec1` Filled Steel Tube** 1000×1000×31,25 | Comb1 (Steel + Composite Column) |
| `CivilComp3\460Member_Steel` | — | 470 | aynı | çelik | — |
| `CivilComp3\CivilComp3_V12` | — | **0** | yok | — | — |

Diğer bilgiler:
- Modellerin hepsi ETABS 2016 (16.0.1) ile kaydedilmiş. ETABS 22 açınca dönüştürür; bu işlem yalnızca kopyada yapılmalı.
- Malzemeler A992Fy50 ve 4000Psi. Çelik tasarım kodu AISC 360-10 / SMF.
- 460Member'daki kirişlere yer tutucu olarak `HSS50.8X25.4X3.2` atanmış.

---

## 6. Önerilen yol haritası (onayınızı bekliyor)

**Karar 1 — Temel (mimari):**
- **(Önerilen) A:** Yeni proje, önceki projenin test edilmiş altyapısından türetilir. Bu altyapının parçaları: `ETABSClass`, `OptimizationClass`, `OptimizationMethods`, yedek, Excel, form. Önceki projedeki gömülü (encased) kompozit kolonun yerini **dolgulu kutu (CFT)** alır.
  - SSO, yeni yöntem olarak kataloğa eklenir.
  - Bu klasörlerdeki koddan yalnızca ihtiyaç duyulan parçalar ve test modelleri (460Member, 525Member) alınır.
- **B:** SFCS iskeleti onarılır ve üzerine inşa edilir. Bölüm 4'teki hataların hemen hepsi düzeltilmeli, SSO ve kısıtlar sıfırdan yazılmalı. Daha uzun sürer ve önceki projedeki işi yeniden yapar.

**Karar 2 — Klasör:**
- Çalışma klasörü aktif klasör `SSO_CF` mi olsun, `SFCS` mi?
- Ya da depo adına uygun yeni bir klasör mü açılsın (ör. `SteelFamewithCompositeTubeColumn_ETABS`)?
- Mevcut iki klasör yedek olarak olduğu gibi kalır; silme yapılmaz.

**Karar 3 — Depoya girecekler:**
- Kaynak kod, md belgeleri ve test modellerinin yalnızca `.EDB` dosyaları (analiz çıktıları değil).
- `AISC14M.xml` ve `ETABS*.dll/exe` girmez.
- Lisans: önceki projede MIT seçilmişti, burada da MIT mi?

**Onaydan sonra ilk aşamalar (taslak):**
1. **Aşama 1:** git deposu, `.gitignore`, md kayıt dosyaları, ETABS 22'de derlenen temel.
   - Test: derleme ve `460Member` kopyasının ETABS 22'de açılıp analiz edilmesi.
2. **Aşama 2:** CFT kesit havuzunun oluşturulması (DatabaseTables veya modelde hazır tanım), grup tabanlı atama, AISC 360-22 kompozit kolon tasarımı ve sonuç okuma.
   - Test: model kopyası üzerinde API testi.
3. **Aşama 3:** maliyet (çelik + dolgu beton), kısıtlar ve ceza; ETABS'siz CFT dayanım hesabı (AISC 360-22 I2.2), ETABS ile karşılaştırma.
4. **Aşama 4:** SSO yöntemi ve tüm yöntemlerle uçtan uca test.
