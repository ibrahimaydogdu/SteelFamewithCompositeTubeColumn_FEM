# YETENEKLER RAPORU — Programın ne yapabildiği, neyi yapamadığı ve makale kapsamı için seçenekler

Sürüm 0.5.1 · 2026-10-09 · Depo: `ibrahimaydogdu/SteelFamewithCompositeTubeColumn_ETABS`

**Bu rapor ne için:** makalenin konusu ve kapsamı belirlenirken programın **gerçekte** neyi yapabildiğinin tek yerde görülmesi. Her bölümde şunlar ayrıdır: *var ve doğrulandı*, *var ama sınırlı*, *yok*. Sonuçlar yalnızca `DEGISIKLIKLER.md`'de kayıtlı koşulardan alınmıştır; kayıtta olmayan bir şey iddia edilmez. Ayrıntılı kullanım için `KULLANIM_KILAVUZU.md`, `ModelBuilder/README.md` ve `PROGRAM_KURALLARI.md` kaynak alınmalıdır.

## İçindekiler
1. [Bir bakışta](#1-bir-bakışta)
2. [Sistem mimarisi](#2-sistem-mimarisi)
3. [Tasarım değişkenleri ve kolon türleri](#3-tasarım-değişkenleri-ve-kolon-türleri)
4. [Kısıtlar ve yönetmelik kapsamı](#4-kısıtlar-ve-yönetmelik-kapsamı)
5. [Amaç fonksiyonu](#5-amaç-fonksiyonu)
6. [Optimizasyon yöntemleri](#6-optimizasyon-yöntemleri)
7. [Değerlendirme altyapısı ve uzun koşu dayanıklılığı](#7-değerlendirme-altyapısı-ve-uzun-koşu-dayanıklılığı)
8. [ModelBuilder: örnek üretici](#8-modelbuilder-örnek-üretici)
9. [Çıktılar](#9-çıktılar)
10. [Bugüne kadarki sonuçlar](#10-bugüne-kadarki-sonuçlar)
11. [Doğrulama durumu](#11-doğrulama-durumu)
12. [Sınırlar ve yapılamayanlar](#12-sınırlar-ve-yapılamayanlar)
13. [Süre ve kaynak tahminleri](#13-süre-ve-kaynak-tahminleri)
14. [Makale kapsamı için seçenekler](#14-makale-kapsamı-için-seçenekler)
15. [Karar gerektiren açık noktalar](#15-karar-gerektiren-açık-noktalar)

---

## 1. Bir bakışta

| Konu | Durum |
|---|---|
| Ne yapar | ETABS 22 modelindeki tasarım gruplarına ayrık kesit atar; her aday tasarım ETABS'te analiz edilir ve tasarlanır |
| Optimizasyon yöntemi | 16 metasezgisel yöntem, aynı değerlendirme altyapısı |
| Kolon türleri | Çelik (W), beton dolgulu tüp (CFT/CFP), gömülü kompozit (W + beton + donatı), **hibrit** (grup grup çelik/kompozit, geçiş katı optimize edilir) |
| Çelik tasarımı | ETABS AISC 360-22 / 360-16 / 360-10 (ETABS 19'da 360-10) |
| Kompozit tasarımı | Program içi hızlı çözücü (arama sırasında) + ETABS kompozit kolon tasarımı (final doğrulama) |
| Deprem hükümleri | AISC 341: güçlü kolon–zayıf kiriş (sürekli kısıt), kesit süneklik süzgeci, Lb/ry |
| Örnek üretimi | `ModelBuilder`: CSV tablosundan ASCE 7-22 yüklü, kombinasyonlu, gruplanmış parametrik çerçeve modelleri |
| Toplu koşu | `tools/RunBatch.ps1`: çok sayıda koşuyu paralel (varsayılan 3 ETABS) yürütür |
| Platform | Windows, .NET Framework 4.7.2, **ETABS 22 lisanslı kurulu olmalı** (program ETABS'siz çalışmaz) |
| Yan ürün | ETABS'siz matematik test problemi (*Test Math*) ile yöntemlerin kendi başına karşılaştırılması |

**Tek cümlelik özet:** program, ETABS'i değerlendirme çekirdeği olarak kullanan, 16 yöntemli, çelik/kompozit/hibrit kolonları ve AISC 341 deprem şartlarını destekleyen bir kesit optimizasyon çerçevesidir; maliyeti, her tasarımın ETABS'te gerçekten analiz edilmesidir (4 katlı örnekte analiz başına ~14 s, 25 katlı 525M modelinde ~35 s).

---

## 2. Sistem mimarisi

```
CSV tablosu ──► ModelBuilder ──► ETABS modeli (.EDB) + runs_template.csv
                                      │
Form (MainForm) / toplu mod (/batch) ─┤
                                      ▼
   OptimizationClass (16 yöntem) ──► ETABS_Class.Evaluate
        ▲                              │ geometri düzeltme → analiz → tasarım
        │ ceza + maliyet               │ → kısıt ihlali onarımı → yeniden analiz
        └──────────────────────────────┘
   Final: düzeltmesiz yeniden analiz + ETABS kompozit doğrulama + koruma
   Çıktı: XML, Excel, <model>_best.EDB, ErrorLog.txt
```

- **Dosyalar (kök klasör):** `OptimizationMethods.vb`, `OptimizationClass.vb`, `SocialSpider.vb` (yöntemler); `ETABSClass.vb` (değerlendirme, kısıtlar, sınırlar); `CompositeColumn.vb`, `TubeColumn.vb`, `HybridColumns.vb` (kompozit ve hibrit); `DialogGuard.vb` (gizli ETABS iletişim kutuları); `MainForm.vb` (form ve toplu mod); `ExcelExport.vb`.
- **İki program:** optimizasyon programı (`FrameSap2000.exe`) ve ayrı `ModelBuilder.exe`. Aynı depo ve çözüm.
- **Kullanıcının oturumuna dokunmaz:** her zaman kendi (gizlenebilir) ETABS örneğini açar, model kopyası üzerinde çalışır; orijinal model değişmez. En iyi tasarım `<model>_best.EDB` olarak yazılır.
- **Atıf:** referans proje `SteelFamewithCompositeColumn_ETABS` (2026.10.3) üzerine kuruludur; ek olarak SSO, dolgulu tüp, hibrit kolonlar, AISC 341 desteği, toplu koşu ve ModelBuilder eklenmiştir.

---

## 3. Tasarım değişkenleri ve kolon türleri

### 3.1 Değişken tanımı
- Bir **tasarım değişkeni = ETABS'te *Steel Frame Design* prosedürlü bir eleman grubu**. Her değişken ayrık bir kesit indisidir; tüm çubuklar aynı kesiti alır.
- Gruplamayı **kullanıcı** yapar (modelde); program grupları olduğu gibi alır. ModelBuilder köşe/kenar/iç × kat bandı ve kenar/iç × açıklık × kat bandı kuralıyla otomatik gruplar.
- Kolon ve kiriş grupları ayrı olmalıdır (kompozit modda tüm üyeleri düşey olan grup kolon sayılır).

### 3.2 Kesit kütüphanesi
- Çelik: AISC16M kütüphanesindeki **289 W kesit** (alana göre sıralı liste). Grup başına arama aralığı (alt/üst sınır) ilk ETABS tasarımına göre kurulur.
- Üst sınır: ETABS dayanım tasarımındaki kesit + `UpperBoundMultiplier` (0,23) payı. **Öteleme belirleyiciyse** `WidenBoundsForDrift` üst sınırları otomatik açar (en çok bütün listeye kadar).
- AISC 341 etkinse kiriş gruplarının alt sınırı açılır (hafif kiriş SCWB oranını düşürür).

### 3.3 Kolon türleri

| Tür | Arama sırasında | Final doğrulama | Maliyet |
|---|---|---|---|
| **Çelik (W)** | ETABS çelik tasarımı | ETABS çelik tasarımı | Çelik ağırlığı (kN) |
| **Dolgulu tüp (CFT/CFP)** — varsayılan kompozit tip | Program içi çözücü (AISC 360-16/22 I2.2, I3.4, I4, I5); kesit General (dönüştürülmüş özellikli) | Gerçek *Filled Steel Tube/Pipe* kesiti + ETABS kompozit kolon tasarımı | Çelik + beton |
| **Gömülü kompozit** | Program içi hızlı çözücü (şekil değiştirme uyumu / plastik gerilme) | Gerçek gömülü kesit + ETABS kompozit tasarımı | Çelik + donatı + beton + kalıp |
| **Hibrit** | Grup başına çelik/kompozit tipi veya yığın başına geçiş değişkeni | Kompozit gruplar ETABS kompozit tasarımıyla, çelik gruplar çelik tasarımıyla | Tipe göre |

**Dolgulu tüp kataloğu (`TubeSections.xml`):** varsayılan 149 kesit = kütüphaneden 45 kare HSS (≥ 300 mm) + 104 yapma (levhadan kaynaklı) kutu (400–1000 mm, 50 mm adım; levha 12–50 mm). İsteğe bağlı: dikdörtgen kutu, boru. AISC 360-22 Tablo I1.1a/b λmax'ını aşanlar atılır. **Deprem süneklik düzeyi** ayarlıdır: `High` (b/t ≤ 1,4√(E/Fy): katalog 100 kesit), `Moderate` (132 kesit), `None` (149 kesit). TBDY 2018 Tablo 9.3 ve AISC 341 Tablo D1.1 (kompozit elemanlar) ile aynı değerler.

**Hibrit tasarım (Aşama 6):**
- Her kolon grubu *Optimize / Steel / Composite* olabilir (formda tablo).
- **Geçiş:** kolon yığını (düşey bağlı gruplar, otomatik bulunur) başına bir geçiş değişkeni ("alttan şu gruba kadar kompozit, üstü çelik") **veya** grup başına tip değişkeni. İkisi de uygulandı; varsayılan yığın bazında.
- Optimize grubun iki kesit değişkeni vardır (W ve kompozit); etkin olan kullanılır. 525M'de değişken sayısı 14 → 26.
- Geçişte bağlantı kuralı: W kolon tüpün üstüne oturur (W derinliği ≤ tüp H, başlık ≤ B).
- Ters düzen (kompozit çeliğin üstünde) yalnızca uyarı üretir; karar kullanıcıdadır.

---

## 4. Kısıtlar ve yönetmelik kapsamı

| Kısıt | Nasıl uygulanır |
|---|---|
| **Çelik dayanım** (PMM, kesme) | ETABS tasarım oranı / **D/C sınırı** (modelden okunur, ETABS varsayılanı 0,95; `DesignRatioLimit` ile değiştirilir) |
| **Kompozit kolon dayanımı** | Arama sırasında iç çözücü; final ETABS ile; ETABS oranı 1'i aşan grup otomatik bir üst kesite çıkarılır (**koruma**, en çok 3 adım); `CompositeStrengthFactor` ile kalibrasyon önerisi |
| **Kat arası ve tepe ötelemesi** | Servis yanal yük durumları (`SRV_<desen>`; `ServiceLateralFactor`), deprem durumlarında `SeismicDriftAmplification` (ASCE 7: Cd/Ie, TBDY: R/I). Seçenekler: yalnızca yanal yükler / tüm durumlar |
| **Kolon–kolon geometrisi** | Üst kat kolonu alttakinden büyük olamaz (alan ve derinlik); tüp/W geçişinde özel kural |
| **Kiriş–kolon geometrisi** | Kiriş flanşı bağlandığı kolona sığar |
| **AISC 341 güçlü kolon–zayıf kiriş (SCWB)** | ETABS `BCMajor`/`BCMinor` oranı **sürekli kısıt** olarak okunur (sınır 1,0); grubun tasarım oranına katılır |
| **AISC 341 süneklik süzgeci (Tablo D1.1)** | SMF (yüksek) / IMF (orta) için kompakt olmayan kesitler analizden önce en yakın uygun kesitle değiştirilir; kirişte Ca = 0, kolonda Ca = 0,3 varsayımı; kalan durumlar ceza ile yakalanır |
| **AISC 341 Lb/ry (D1.2b)** | Modelde kiriş yanal desteği tanımlıdır (ModelBuilder 2,5 m); desteksiz modelde çoğu kesit elenir |
| **P-Delta / Direct Analysis** | Çalışma kopyasında P-Delta ayarlanır; kompozit kolonlarda B2 = 1 kabulü için gereklidir |

**Ceza ve uygunluk:** `cezalı maliyet = maliyet · (1 + ceza)³`. Yalnızca cezası 0 olan tasarım "en iyi tasarım" olabilir. Hiçbir uygun tasarım bulunamazsa koşu "uygun tasarım yok" ile biter (sonuç dosyası yazılmaz).

**Yönetmelik kapsamı (neler ETABS'e, neler programa ait):**
- Çelik dayanım ve AISC 341 denetimlerinin hesabı **ETABS'indir**; program sonuçları okur ve yönlendirir.
- Kompozit dayanım arama sırasında **programın kendi çözücüsüdür** (ETABS ile farkı ölçülmüştür, Bölüm 11).
- AISC 341'in diğer koşulları (bağlantı, bölgesel tasarım, kompozit moment çerçevesinin sismik detayları) **kontrol edilmez**; ETABS'in kompozit kolon tasarımı da AISC 341'i uygulamaz.
- TBDY 2018 yalnızca dolgulu tüp cidar narinliği sınırı olarak (Tablo 9.3) kullanılır; TBDY yük kombinasyonları veya spektrumu **ModelBuilder'da yoktur** (ASCE 7-22 kullanılır).

---

## 5. Amaç fonksiyonu

- **Çelik modu:** yapı çeliği ağırlığı (kN).
- **Kompozit/hibrit mod:** çelik·b.f. + donatı·b.f. + beton hacmi·b.f. + kalıp alanı·b.f. Varsayılan göreli birim fiyatlar: çelik 1 (kN başına), donatı 0,5, beton 0,6 (m³), kalıp 0,15 (m²); dayanak: 2 $/kg çelik, 1 $/kg donatı, 120 $/m³ beton, 30 $/m² kalıp. Dolgulu tüpte donatı ve kalıp yoktur.
- **Dikkat:** kompozit modda optimum **doğrudan birim fiyat oranlarına bağlıdır**. Makalede fiyat oranı bir duyarlılık parametresi olarak ele alınmalı; şu an tek bir fiyat kümesiyle koşulmuştur.
- Çok amaçlı (maliyet–öteleme, maliyet–CO₂ vb.) **yoktur**; tek amaç, ceza ile kısıtlı.

---

## 6. Optimizasyon yöntemleri

16 yöntem (aynı değerlendirme altyapısı, aynı kısıt onarımı, aynı önbellek/yedek):

| # | Yöntem | # | Yöntem |
|---|---|---|---|
| 1 | Harmony Search (HS; referans programın asıl yöntemi) | 9 | Firefly (FA) |
| 2 | Biogeography-Based (BBO) | 10 | Grasshopper (GOA) |
| 3 | Whale (WOA) | 11 | Teaching-Learning (TLBO; isteğe bağlı HS aşaması) |
| 4 | Dandelion (DO) | 12 | Tree-Seed (TSA) |
| 5 | Artificial Bee Colony (ABC) | 13 | Grey Wolf (GWO) |
| 6 | Ant Colony (ACO) | 14 | Honey Badger (HBA) |
| 7 | Brain Storm (BSO) | 15 | Aquila (AO) |
| 8 | Crow Search (CSA) | 16 | **Social Spider (SSO)** — yazarın Fortran kodundan çevrildi |

- **Ortak ayarlar:** bellek/popülasyon, en çok analiz, *Memory Update* (6 seçenek; HS/BBO/WOA/DO için geçerli), *Clear Duplicates*, *Levy Flight* (BBO, ABC, BSO, CSA), tohum. Her yöntemin kendi parametreleri formda ve belgelenmiş (KULLANIM_KILAVUZU 6.1).
- **SSO:** dişi/erkek hareketi, baskın erkek çiftleşmesi, isteğe bağlı *Spider jump*; kabul *If better* (açgözlü, önerilen) veya *Always* (özgün). Çeviride Fortran kodundaki **8 hata taşınmadı** (sınır kırpmasında kalan indis, yuvarlamanın yanlış konuma yazılması, en iyi örümcek/dişi karışması, rulet olasılıklarının 1'i aşması vb.; `SSO_CEVIRI_NOTLARI.md`). Fortran ile adım adım sayısal karşılaştırma **yapılamadı** (makinede Fortran derleyicisi yok); makale ve üç kaynak sürümle formül düzeyinde karşılaştırıldı.
- **Zamana bağlı katsayılar** (GWO a, GOA c, HBA, BSO adımı, AO, FA α) *Max. analyses*'a oranla ilerler.
- **Yöntem durumu** yedeğe yazılır (ABC sayaçları, ACO feromonu, GWO liderleri, SSO cinsiyetler); koşu kaldığı yerden sürer.
- **ETABS'siz karşılaştırma (*Test Math*):** dişli treni problemi, 3000 analiz, 20 üye, 3 tohum; 16 yöntem × Levy açık/kapalı. Yöntem davranışını ETABS maliyeti olmadan görmek için kullanılabilir; ancak yapı problemini temsil etmez.

---

## 7. Değerlendirme altyapısı ve uzun koşu dayanıklılığı

| Özellik | Açıklama |
|---|---|
| **Onarım modları** | *Combined* (varsayılan; en çok 2 analiz, 525M'de ~26 s) ve *Sequential* (orijinal; en çok 6 analiz, ~68 s). **İki mod farklı sonuç verir**; karşılaştırılan koşularda aynı mod kullanılmalı |
| **Sonuç önbelleği** | Aynı tasarım tekrar üretilirse ETABS çağrılmaz; analiz sayısına eklenmez. 20 döngü yeni tasarım yoksa yakınsama sayılır |
| **ETABS yeniden başlatma** | Her 100 analizde (ETABS belleği büyür); sonuçlar değişmez; ~%2 ek süre |
| **Yedek ve devam** | Her döngü sonu ve 10 dakikada bir; SHA-256, grup ve kütüphane denetimi; `Load BackUp File`. **Rastgele sayı üretecinin durumu saklanmaz**: devam eden koşu kesintisiz koşuyla birebir aynı olmaz |
| **Gizli iletişim kutuları** | `DialogGuard` ETABS'in Yes/No kutularını otomatik cevaplar (gizli ETABS'te koşu sonsuza dek takılabiliyordu; 70 dk takılma bulundu) |
| **Toplu koşu** | `FrameSap2000.exe /batch` + `tools/RunBatch.ps1`: CSV'den çok koşu, `-Parallel N`, her koşu kendi klasörü ve exe kopyası, `-Status`, `-Stop`, `-Resume`, `summary.csv`. Paralel koşuların birbirinin çalışma klasörünü silmesi hatası bulundu ve düzeltildi |
| **Yol uzunluğu** | 260 karakter sınırı; betik 230'u aşan yolu reddeder |
| **Check Structure** | Bir sonuç dosyasındaki tasarımı düzeltme yapmadan başka/aynı modelde yeniden kontrol eder |
| **Hata görünürlüğü** | Her farklı ETABS tasarım hatası `ErrorLog.txt`'ye grup ve kesit adıyla bir kez yazılır |

**Tekrarlanabilirlik uyarısı:** aynı tohumlu iki koşu ETABS'in P-Delta çözümündeki çok küçük sayısal farklar yüzünden birebir aynı olmayabilir. 525M regresyon taban değerleri 2026-10-07'de değişti (7564,91/1,4580 → 7556,76/1,4605; nedeni koda bağlı değil, eski sürüm de aynı değeri veriyor) ve yeni değerler taban alındı. Makalede **birden çok tohum** raporlanmalıdır.

---

## 8. ModelBuilder: örnek üretici

Amaç: akademik çalışmada çok sayıda örneği aynı kurallarla üretmek. Parametre tablosu (CSV) → ETABS modeli.

### 8.1 Parametre kümesi
Her satır bir örnek; ~50 parametre. Başlıcaları: açıklıklar (`BaysX`, `BaysY`, ör. `3x9 6`), kat sayısı (1–100), kat yükseklikleri, çerçeve sistemi (*Perimeter* — yalnızca çevre moment çerçevesi, iç kirişler mafsallı; *Space* — hepsi moment), iç kolon tabanı, kat bandı (grup sıklığı), havuz süzgeçleri (kiriş derinlik aralığı, kolon serisi, süneklik süzgeci), yükler (döşeme, ek ölü, cephe, hareketli, bölme, çatı, kar), rüzgâr (V, maruziyet, Kzt, Kd, G), deprem (SDS, SD1, S1, TL, zemin sınıfı, risk kategorisi), çerçeve sınıfı (**SMF/IMF/OMF**; R, Cd, Ω0 buna göre), Ie, ρ, kazara dışmerkezlik, kiriş yanal destek aralığı, AISC 341 açık/kapalı.

### 8.2 Üretilen model
- **Geometri:** düzenli ızgara, kat başına rijit diyafram, ankastre mesnet. **Döşeme modellenmez**; düşey yük kirişlere iki yönlü 45° kuralıyla üçgen/yamuk yayılı yük olarak verilir (elle doğrulandı: SDL 2390,4 kN, Live 898,6 kN).
- **Gruplar:** `COL-CORNER|EDGE|INTERIOR-Sxx-yy`, `BM-EDGE|INT-L<mm>-Sxx-yy`. Örnek: 15 kat, band 3, 9 ve 6 m açıklık → 15 kolon + 20 kiriş grubu (35 değişken).
- **Kesit havuzları:** 289 W'den süzülür. `SeismicFilter = High` için kiriş 168, kolon 46 kesit. Kolonlar düzlem içi güçlü eksene yönlendirilir (`ColumnOrientation = Auto`).
- **Yükler:** Dead, SDL, Live, RoofLive, Snow, PartMass; rüzgâr **ASCE 7-22 Bölüm 26–27 Directional Procedure** ile programda hesaplanıp kat düğümlerine uygulanır (ETABS 22.6 API'sinde otomatik ASCE 7-22 rüzgâr/deprem yoktur); deprem **ASCE 7-22 tepki spektrumu** (`RSX`, `RSY`, CQC, SRSS yönler, kazara dışmerkezlik) ve ELF taban kesmesi sayısal hesaplanarak RS ölçeklemesi (ASCE 7-22 12.9.1.4).
- **Kombinasyonlar:** ASCE 7-22 2.3.1 ve 2.3.6 — **35 kombinasyon** (kar varsa 53); AISC 360-22 çelik ve kompozit tasarımı için işaretli.
- **Tasarım tercihleri:** çerçeve tipi, SDC (Tablo 11.6-1/2), Ie, ρ, SDS, R, Ω0, Cd, LRFD, Direct Analysis Method; AISC 341 açıkken güçlü kolon–zayıf kiriş ve süneklik denetimleri ETABS'te etkin; kirişlerde 2,5 m yanal destek (`SetOverwrite` API'siyle).
- **Ön boyutlandırma:** RS ölçek çarpanı için bina rijitliği ikiye bölme aramasıyla bulunur (T ≤ Cu·Ta); 15 katlı örnekte çarpan 2,0/2,9 → 1,33/1,54'e indi. Çarpan **sabit bir sayıdır**.
- **Kontrol analizi:** geçici klasörde; periyot, kütle katılımı (< %90 ise mod sayısı artar), ELF/RS/rüzgâr kesmeleri, önerilen `SeismicDriftAmplification = Cd/Ie` rapora yazılır.

### 8.3 Kapsam dışı (ModelBuilder)
Çaprazlı sistemler, düzensiz plan ve geri çekmeler, kar birikmesi, zemin–yapı etkileşimi, hareketli yük azaltması, tali kirişler, **kompozit döşeme**, rüzgârın burulmalı durumları (Şekil 27.3-8 Durum 2 ve 4), esnek bina için ani rüzgâr faktörü hesabı (kullanıcı değeri), TBDY 2018 yükleri.

---

## 9. Çıktılar

| Dosya | İçerik |
|---|---|
| `<çıktı>.xlsx` | *Summary*, *Cost* (grup başına çelik/donatı/beton/kalıp ve maliyet payı), *Design* (final ve aramanın en iyisi), *Constraints* (oran/sınır), *ETABS composite* (ETABS ve iç çözücü oranları), *History* (yakınsama için) |
| `<çıktı>.xml` | Makine okunur sonuç; tohum, ayarlar (`FormInfo`), final kontroller |
| `<model>_best.EDB` | Final tasarım modeli (ETABS'te açılıp elle doğrulanabilir) |
| `ErrorLog.txt` | Zaman damgalı ayrıntılı günlük, süre dökümü, ETABS tasarım hataları |
| `<çıktı>.check.xlsx` | Check Structure çıktısı |
| `summary.csv` | Toplu koşularda her koşunun durumu, analiz sayısı, maliyet, ceza, süre, hibrit geçişler |

**Raporlama notu:** makalede aramanın en iyisi değil **final tasarım** (tüm durumlarla, düzeltmesiz yeniden analiz + ETABS doğrulaması) raporlanmalıdır; *History* sayfası yakınsama eğrisi için yeterlidir.

---

## 10. Bugüne kadarki sonuçlar

> Hepsi tek tohumlu (1) ve tek koşulu sonuçlardır; yöntem sıralaması için kesin hüküm değildir.

### 10.1 525M modeli (25 kat, 525 çubuk, 14 grup), 9 uzun koşu (Aşama 7)
AISC 360-22, D/C sınırı 0,95, 500 analiz bütçesi, popülasyon 20, tohum 1; 3 paralel ETABS, toplam ~18 saat. Maliyetler (çelik modunda kN, kompozit/hibritte göreli birim):

| Yöntem | Çelik | Kompozit (CFT) | Hibrit |
|---|---|---|---|
| SSO | 6061,54 | 5673,41 | 6497,86 |
| HS | 5984,18 | **5335,19** | 5486,70 |
| ABC (Limit 10) | 5867,16 | 5785,87 | 6279,56 |

- Dokuz koşunun hepsinde final analizde ceza 0 ve ETABS final kontrolleri geçti; ETABS kompozit tasarım/iç hesap oranı 0,997–1,000.
- Belirleyici kısıt çoğunlukla kat ötelemesi (oran/sınır 0,75–0,99).
- **Kompozit kolon üç yöntemde de çelikten ucuz**: HS'de %10,8, SSO'da %6,4, ABC'de %1,4 (**not:** çelik kN ile kompozit göreli birim aynı ölçekte değildir; karşılaştırma birim fiyat varsayımına bağlıdır).
- **Hibrit aynı bütçede kompozitten pahalı çıktı** (HS +%2,8, SSO +%14,5, ABC +%8,5). Bu bir **arama verimliliği** sonucudur: tümü-kompozit tasarım hibrit uzayının içindedir; değişken sayısı 14 → 26 olduğundan 500 analiz yetersiz kalmıştır. Makalede hibrit için daha büyük bütçe gerekir.
- Hibrit geçişler: SSO iki yığında da 25. kata kadar kompozit; HS çevre kolonlarında 1–20 kompozit, 21–25 çelik; ABC çevre kolonları tümüyle kompozit, orta kolon 1–15 kompozit.

### 10.2 ModelBuilder örneği P4 (4 kat, 2×6 ve 5/6 m, çevre SMF çerçeve, Cd/Ie = 5,5)
| Koşu | Sonuç |
|---|---|
| AISC 341 **kapalı**, SSO, 500 analiz, tohum 1 | Uygun tasarım; maliyet **2587,64**; öteleme/sınır 0,993 (öteleme belirleyici) |
| AISC 341 **açık**, desteksiz kiriş, süneklik süzgeci açık | 500 analizde **uygun tasarım yok** (Lb/ry hataları) |
| AISC 341 **açık**, 2,5 m kiriş destekli, süzgeç açık | **Uygun tasarım**; 534 analiz, 2,19 sa, maliyet **1726,90**; final ETABS kontrolü geçti |

**Bu iki maliyet doğrudan karşılaştırılamaz** (farklı modeller: 2587,64 desteksiz/AISC 341 kapalı modelde, 1726,90 destekli modelde). AISC 341 hükümlerinin gerçek maliyet etkisi için aynı destekli modelde `SeismicProvisions = No` koşusu **henüz yapılmadı** (kararla ertelendi).

### 10.3 Diğer ölçümler
- ModelBuilder 3, 4 ve 15 katlı örneklerle uçtan uca **kuruldu ve denetlendi**; yalnızca 4 katlı (P4) örnek optimizasyon programıyla çözüldü. 15 katlı örnek henüz optimize **edilmedi**.
- Üst sınır otomatik genişletme: 4 katlı örnekte 4 adımda bütün liste açıldı ve elle `UpperBoundMultiplier = 1` ile aynı maliyeti (2587,64) verdi.
- SCWB: köşe kolon oranı geometrik müdahalelerle 1,47 → 1,01'e indi (kolon döndürme + açılan kiriş alt sınırı).

---

## 11. Doğrulama durumu

| Alan | Yapılan doğrulama | Sonuç |
|---|---|---|
| Yöntemler (ETABS'siz) | *Test Math*: 16 yöntem × Levy × 3 tohum, referans çıktısıyla karşılaştırma | İlk 15 yöntem referansla **birebir aynı** |
| SSO çevirisi | 7 parametre bileşimi × 3 N × 3 tohum: sınır, durma, dişi sayısı, yedek gidiş-dönüşü | Hata yok; Fortran ile sayısal karşılaştırma **yok** |
| Dolgulu tüp iç çözücü | 23 ETABS'siz denetim: katalog, elle Pno (HSS 559×23,6: 23.564 kN; Ø711×25,4: 27.829 kN), λmax süzgeci | Geçti |
| Dolgulu tüp, ETABS ile | 10 kolon grubu PMM oranı | ETABS/iç ≤ 1,041; sonra 525M'de koşularda 0,997–1,000 |
| Gömülü kompozit, ETABS ile | 525M | ETABS oranı iç çözücüden %0,5–14 yüksek; kalibrasyon önerisi 1,08–1,12; koruma devrede |
| Hibrit | Tip–kesit–prosedür uyumu her değerlendirmede; geçişin çözülmesi | Uyumsuzluk 0; ETABS/iç ≤ %1 |
| ModelBuilder yükleri | Düşey reaksiyonlar ve rüzgâr elle | Tam eşleşme (bir hata yakalandı, düzeltildi) |
| ModelBuilder RS ölçeği/ELF | S3 elle kontrol (W = 3306 kN) | Elle hesapla tutuyor |
| ModelBuilder kombinasyonları | Katsayılar ve 35/35 seçim | Doğrulandı |
| Regresyon | 525M gömülü mod, tohum 12345, 2 değerlendirme | 7556,76/1,4605 ve 7281,65/1,5927 — tüm yeni özelliklerde değişmedi |

**Henüz bağımsız doğrulaması olmayanlar:** ModelBuilder'ın ASCE 7-22 rüzgâr kuvvetlerinin ETABS dışı bir kaynakla karşılaştırması (yalnızca elle kontrol); AISC 341 süneklik süzgecinin 289 kesit için ETABS'in "kompakt değil" kararıyla tam taraması (ayrıntılı kesit-kesit karşılaştırma yapılmadı; süzgeç k = 1,8·tf yaklaşımı kullanır, kalan farklar ceza ile yakalanır); P4 dışındaki ModelBuilder örneklerinin optimizasyonu.

---

## 12. Sınırlar ve yapılamayanlar

**Hesap ve model**
1. **ETABS bağımlılığı:** her tasarım ETABS'te analiz edilir; ETABS lisansı gerekir, analiz süresi koşu süresini belirler. Eşzamanlı koşu sayısı bu makinede 3.
2. **Tek amaçlı** ve **ayrık W/tüp** kesitler; sürekli değişken yok. Katlar arası kesit değişimi gruplamayla belirlenir (kullanıcı kararı).
3. **Döşeme ve kompozit kiriş yok:** döşeme modellenmez (kirişlere yük olarak verilir); kompozit döşeme programı ayrı bir proje olarak önerilecektir.
4. **Rijit diyafram, düzenli ızgara** (ModelBuilder); düzensiz plan, çaprazlı sistem yok.
5. **Deprem yükü ötelemesi:** `SeismicDriftAmplification` tüm koşu için **tek bir sayıdır** (Cd/Ie); farklı Cd/Ie'li örnekler ayrı koşulmalı.
6. **RS ölçek çarpanı sabittir:** ASCE 7-22 12.9.1.4 ölçeklemesi ön boyutlandırmaya göre bir kez hesaplanır; optimizasyon sırasında rijitlik değişince gerçek taban kesmesi oranı değişir (tasarıma bağlı yeniden ölçekleme yok).
7. **AISC 341 kapsamı:** SCWB, kesit süneklik süzgeci ve Lb/ry vardır; bağlantı, panel bölgesi, süreklilik levhası ve kompozit moment çerçevesinin sismik detayları yoktur. Kolonlarda Ca = 0,3 varsayımı muhafazakârdır; gerçek Ca daha büyükse ceza devreye girer.
8. **Kiriş yanal desteği** model varsayımıdır (2,5 m, ikincil kiriş/döşeme desteği); kullanıcı kendi modelinde elle tanımlamalıdır. AISC 341 açıkken desteksiz model çözümsüz kalabilir (4 katlı örnekte gösterildi).
9. **Gömülü kompozit kolonun arama sırasında iç çözücüsü ETABS'ten iyimserdir** (%0,5–14); koruma ve kalibrasyon bunu final aşamada kapatır.
10. **Dolgulu tüp:** donatı ve kalıp maliyeti yoktur; boruda kiriş bağlantısı ve ETABS 22.6'nın bazı borularda "çok narin" mesajı nedeniyle boru varsayılan kapalıdır.
11. **ETABS API sınırları:** ASCE 7-22 otomatik yük yok (ModelBuilder kendi hesaplıyor); tablo yoluyla içe aktarmada `LMinor`/`LTB` sessizce yok sayılıyor (API `SetOverwrite` ile aşıldı); `.e2k` ile yeniden oluşturma model verisini eksik taşıyor (kullanılmıyor).

**Deneysel tasarım ve sonuç yorumu**
12. Şu ana kadarki karşılaştırma verisi **tek tohumludur**. Yöntem sıralaması için en az 5–10 tohum gerekir (bütçe için Bölüm 13).
13. Hibrit sonuçlar 500 analizde yeterince yakınsamamıştır; hibritin kompozitten kötü çıkması bir yöntem/bütçe sonucudur, bir mühendislik sonucu değildir.
14. Devam eden (yedekten) koşular, RNG durumu yedeklenmediği için kesintisiz koşuyla aynı değildir; makale koşuları mümkünse kesintisiz yürütülmeli veya devam edilenler belirtilmelidir.
15. Harici hızlı çözücü (ETABS'siz arama, ETABS'le doğrulama) **gelecek çalışmadır** ve bu projede yapılmayacaktır (`Ajan/Gorevler/GELECEK_DIS_COZUCU.md`); süreç bitince tartışılacak.

---

## 13. Süre ve kaynak tahminleri

| Model | Analiz başına | 500 analiz | Not |
|---|---|---|---|
| 4 katlı P4 (ModelBuilder) | ~14 s | ~2 saat (2,0–2,2 sa ölçüldü) | AISC 341 açık: 534 analiz 2,19 sa |
| 525M (25 kat, 525 çubuk) | 13–35 s (Combined: ~26 s; Sequential: ~68 s) | ~4–7 saat/koşu | 9 koşu 3 paralel ≈ 18 saat ölçüldü |
| 15 katlı ModelBuilder örneği | ölçülmedi (modal çözüm ve 12–45 mod nedeniyle uzun olması beklenir) | bilinmiyor | **tahmin yok, deneme koşusu gerekir** |

- **Sabit ek süreler:** ilk sınır tasarımı (2–10 dk; 289 kesitlik listeyle uzar), başlangıç belleği, final doğrulaması (3–5 dk; kompozit kolon tasarımı 525M'de 3–14 dk).
- **Paralellik:** ETABS örneği başına ~1 GB RAM; 3 paralel koşu bu makinede doğrulandı. Daha fazlası denenmedi.
- **Önerilen koşu büyüklüğü (kılavuzdan):** deneme 10/100; kısa 20–30/500–1000; **makale/tez 30–50 üye, 3000–10000 analiz**. 5000 analiz 525M'de 1–1,5 gün sürer.
- **Hesaplama bütçesi örneği:** 16 yöntem × 5 tohum × 1000 analiz × 14 s = ~310 saat tek koşu eşdeğeri (4 katlı örnek); 3 paralel ile ~4,5 gün. Aynısı 525M'de (26 s) ~8 gün.

---

## 14. Makale kapsamı için seçenekler

Aşağıdaki başlıklar programın bugünkü yeteneğiyle **hemen** yapılabilecekleri ve ek geliştirme gerektirenleri ayırır. Süre tahminleri Bölüm 13'e dayanır.

### A. Metasezgisel yöntem karşılaştırması (yapıya özgü)
- **Soru:** 16 yöntemden hangisi çelik çerçeve sizing probleminde daha az analizle daha iyi sonuç verir?
- **Gereken:** 3–4 örnek (4, 10, 15 kat; çelik modu), 16 yöntem, 5–10 tohum, eşit analiz bütçesi, ortalama/en iyi/standart sapma, yakınsama eğrileri, istatistik test.
- **Hazır:** yöntemler, toplu koşu, ModelBuilder, *History* sayfası. **Eksik:** 15 katlı örneğin çözülebilirliği doğrulanmadı; bütçe büyük (Bölüm 13).
- **Risk:** hesap süresi; ETABS tekrarlanabilirlik farkı (çoklu tohumla yönetilir).

### B. Çelik / kompozit / hibrit kolon maliyet karşılaştırması
- **Soru:** dolgulu tüp kompozit kolon ve geçiş katının optimizasyonu maliyeti ne kadar azaltır?
- **Hazır:** üç mod, geçiş değişkeni, ETABS doğrulaması, 525M'de ön sonuç (kompozit %1,4–10,8 daha ucuz).
- **Eksik:** birim fiyat duyarlılığı (en az 3 fiyat oranı), hibrit için büyük bütçe, çelik ağırlığı (kN) ile kompozit göreli birimin **aynı paraya** çevrilmesi.
- **Risk:** sonuç fiyat oranlarına bağlı; çelik/kompozit karşılaştırması birim fiyat varsayımına dayanır; iç çözücünün ETABS'e göre iyimserliği (final korumayla kapatılıyor).

### C. AISC 341 deprem hükümlerinin tasarım maliyetine etkisi
- **Soru:** SCWB, süneklik ve Lb/ry kısıtları optimum maliyeti ne kadar artırır?
- **Hazır:** SCWB sürekli kısıt, süneklik süzgeci, destekli model, P4 AISC 341 açık sonucu (1726,90).
- **Eksik:** **aynı destekli modelde AISC 341 kapalı koşu** (kararla ertelendi); birden çok örnek ve tohum; SMF dışı (IMF/OMF) karşılaştırma.
- **Not:** bu başlık programın güncel güçlü yanıdır (AISC 341 açık örneklerin çözülmesi geçen hafta çözüldü); kapsamı dar tutulursa en hızlı tamamlanan başlıktır.

### D. Yeni yöntem odaklı çalışma (SSO'nun çerçeve optimizasyonuna uyarlanması)
- **Soru:** SSO ve SSO'nun parametreleri (PF, çiftleşme yarıçapı, kabul, sıçrama) yapı problemlerinde nasıl davranır?
- **Hazır:** SSO uygulandı, yedeklenir, parametreleri formda.
- **Eksik:** parametre taraması (çok koşu), diğer yöntemlerle adil karşılaştırma (A başlığıyla birleşir). 525M tek tohumlu sonuç: SSO çelik 6061,54 (HS 5984,18; ABC 5867,16 daha iyi), kompozit 5673,41 (HS 5335,19), hibrit 6497,86 (HS 5486,70) — **SSO bu üç koşuda en iyi değil**; makale "SSO üstündür" iddiasıyla yazılacaksa bu veri şimdilik desteklemiyor.

### E. Parametrik örnek üretici ve karşılaştırma veri seti (yazılım/veri makalesi)
- **Soru:** ASCE 7-22 yüklü parametrik çerçeve örnek kümesi ve bunların optimum tasarımları (benchmark).
- **Hazır:** ModelBuilder, `examples_summary.csv`, CSV ile toplu üretim, rapor.
- **Eksik:** ModelBuilder'ın yük hesabının bağımsız doğrulaması (rüzgâr, ELF), örneklerin birkaçının uzun koşusu, kapsam dışı listesinin makalede açıkça yazılması.

### F. Hibrit kolonlarda geçiş katı (en özgün katkı adayı)
- **Soru:** kompozit–çelik geçiş katı optimizasyonla nasıl belirlenir; yığın bazlı mı grup bazlı mı?
- **Hazır:** iki geçiş türü, ETABS doğrulaması, 525M'de ilk sonuçlar.
- **Eksik:** daha büyük bütçeli ve çoklu tohumlu koşular; hibrit arama uzayına özel iyileştirme (yakınsama zayıf).
- **Not:** hibrit sonucu şu an kompozitten kötüdür; bu bir bulgu olarak yazılabilir ama "hibrit daha iyidir" denemez.

### G. Kapsama alınmayacaklar (makalede "gelecek çalışma" denmeli)
Kompozit döşeme optimizasyonu (ayrı program önerisi), harici hızlı çözücü, çaprazlı/düzensiz sistemler, çok amaçlı optimizasyon, TBDY 2018 yük/kombinasyonları, tasarıma bağlı RS ölçekleme, rüzgârda burulma.

### Seçim için karar tablosu

| Başlık | Hazırlık düzeyi | Yaklaşık hesap yükü | Özgünlük (kendi değerlendirmem) |
|---|---|---|---|
| A. Yöntem karşılaştırması | Yüksek | Çok yüksek (≥ 300 saat) | Orta (benzer çalışmalar çok) |
| B. Çelik/kompozit/hibrit maliyet | Yüksek | Yüksek | Orta–yüksek |
| C. AISC 341 maliyet etkisi | Orta–yüksek (bir kapalı koşu eksik) | Düşük–orta | Orta–yüksek |
| D. SSO uyarlaması | Orta | Orta | Düşük (şu veri SSO'yu en iyi göstermiyor) |
| E. Örnek üretici / veri seti | Orta | Orta | Orta (yazılım odaklı) |
| F. Hibrit geçiş | Orta | Yüksek | **Yüksek** |

(Özgünlük sütunu kendi tahminimdir; literatür taraması yapılmadı.)

---

## 15. Karar gerektiren açık noktalar

1. **Kapsam ve başlık seçimi** (Bölüm 14); birden fazla başlığın birleşimi (ör. B + C + F) mümkündür; her ek başlık hesap yükünü artırır.
2. **Örnek kümesi:** hangi kat sayıları ve plan türleri? 525M mevcut model (referans), ModelBuilder ile 4/10/15 kat; 15+ katlı örneklerin çözülebilirliği ve süresi önce **deneme koşusuyla** ölçülmeli.
3. **Bütçe ve tohum sayısı:** makale kalitesi için ≥ 5 tohum; analiz bütçesi (1000/3000/5000).
4. **Birim fiyat varsayımı:** tek fiyat kümesi mi, duyarlılık mı?
5. **Karşılaştırma ölçütü:** maliyet mi, çelik ağırlığı mı; kompozit ve çelik aynı ölçekte nasıl verilecek?
6. **AISC 341 kapalı/açık karşılaştırmasının** aynı destekli modelde yapılması (kuyrukta).
7. **ETABS sürümü ve tekrarlanabilirlik:** ETABS 22.6; sonuçların bu sürüme bağlı olduğu makalede yazılmalı.
8. **Dış çözücü (gelecek çalışma)**: bu makalede yapılmayacak; yalnızca "gelecek çalışma" olarak anılacak.
9. **Kod ve veri paylaşımı:** MIT lisanslı depo mevcut; ETABS modelleri ve sonuç dosyalarının paylaşım biçimi kararlaştırılmalı.

---

*Bu rapor, depodaki kayıtlardan (`DEGISIKLIKLER.md`, `KULLANIM_KILAVUZU.md`, `ModelBuilder/README.md`, `PROGRAM_KURALLARI.md`, `Ajan/Gorevler/*`) derlenmiştir. Raporda geçen tüm sayılar kayıtlı testlerden alınmıştır; kayıtta bulunmayan bir tahmin açıkça "tahmin" olarak işaretlenmiştir.*
