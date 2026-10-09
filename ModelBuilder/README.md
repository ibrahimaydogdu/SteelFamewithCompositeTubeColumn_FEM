# ModelBuilder — ASCE 7-22 çerçeve örnekleri için ETABS modeli oluşturucu

Parametre tablosundan (CSV) ETABS 22 modelleri kurar. Her model, optimizasyon programının (`FrameSap2000.exe`, kök klasör) hiçbir elle düzeltme gerektirmeden kullanabileceği biçimdedir: kesit havuzları, eleman grupları, yükler, kombinasyonlar ve tasarım tercihleri hazırdır.

- **Amaç:** akademik çalışmada kullanılacak çok sayıda örneği aynı kurallarla üretmek (kat sayısı, açıklıklar, yük ve deprem parametreleri tablodan değişir).
- **ETABS oturumunuza dokunmaz:** her zaman yeni bir (gizlenebilir) ETABS örneği açar ve kendi açtığını kapatır.
- **Test:** esas doğrulama kullanıcıdadır. Geliştirme sırasında derleme, ETABS'siz plan denetimi, küçük örneklerle (3, 4 ve 15 katlı) uçtan uca kurulum, üretilen modellerin bağımsız bir programla yeniden açılıp denetlenmesi ve iyileştirici programda çalıştırılması yapıldı (`DEGISIKLIKLER.md`, Aşama 9).

## 1. Çalıştırma

`ModelBuilder.exe` (çözümün `ModelBuilder\bin\Release` klasöründe).

1. **Form:** argümansız çalıştırın. CSV dosyasını ve **kısa** bir çıktı klasörünü (en çok 150 karakter) seçin, *Plan (no ETABS)* ile geometriyi ve grupları görün, *Build models* ile modelleri kurun. *Write a template CSV* tüm sütunlarla bir şablon yazar.
2. **Komut satırı:**
   ```
   ModelBuilder.exe /template ornekler.csv
   ModelBuilder.exe /plan ornekler.csv [/only ad]
   ModelBuilder.exe /build ornekler.csv /out D:\Ornekler [/hide] [/overwrite] [/only ad]
   ```
   Git Bash kullanıyorsanız `/plan` gibi argümanlar yol sanılıp bozulur: önce `export MSYS_NO_PATHCONV=1` yazın (PowerShell ve cmd'de gerekmez).

Gerekenler: ETABS 22 (`ModelBuilder.exe.config` içinde `ETABSProgramPath`, `SectionPropertyDataPath`), .NET Framework 4.7.2.

## 2. CSV tablosu

Her satır bir örnektir; ilk satır sütun adlarıdır. Ayırıcı `,` ya da `;` (başlık satırındaki ilk bulunan); `;` ile ondalık virgül de kabul edilir. Boş hücre varsayılan değeri korur; `#` ile başlayan satırlar yorumdur. Bilinmeyen sütun, geçersiz değer ve yinelenen ad hata verir. Tüm sütunlarla şablon: `ModelBuilder.exe /template dosya.csv`.

Birimler: m, kN, kPa, MPa, m/s, g, s. Bay uzunlukları `3x9 6` biçiminde (9, 9, 9, 6 m).

| Sütun | Varsayılan | Anlamı |
|---|---|---|
| `Name` | Example | Örnek adı: model dosyası adı olur, yinelenmemeli |
| `BaysX`, `BaysY` | `3x9`, `9 6 9` | X ve Y yönünde açıklıklar |
| `Stories` | 15 | Kat sayısı (1–100) |
| `FirstStoryHeight`, `StoryHeight` | 4.5, 3.6 | Zemin kat ve normal kat yüksekliği |
| `FrameSystem` | Perimeter | `Perimeter`: çevre çerçeveleri moment aktarır, iç kirişler mafsallı (yerçekimi çerçevesi); `Space`: bütün birleşimler moment aktarır |
| `InteriorColumnBase` | Fixed | Perimeter sisteminde iç kolon tabanı: `Fixed` / `Pinned` |
| `BeamBracingSpacing` | 2.5 | Kirişlerin yanal destek aralığı (m; ikincil kiriş / döşeme desteği). Her kirişin desteksiz boyu, açıklığın `ceil(açıklık / aralık)` eşit parçaya bölünmesiyle alınır (tasarım overwrite `LMinor`, `LTB`). 0 = açıklık boyunca desteksiz. AISC 341 D1.2b SMF kirişlerinde Lb/ry'yi sınırlar; destek tanımlı değilse çoğu kesit reddedilir |
| `ColumnOrientation` | Auto | `Auto`: x = sabit çizgilerdeki kenar kolonlar (Y yönündeki çerçevelerin kolonları) 90° döndürülür; güçlü eksenleri çerçeve düzleminde olur. Köşe ve iç kolonlar varsayılan yönde kalır. `None`: hepsi varsayılan yönde |
| `StoryBand` | 3 | Kaç katta bir grup değişir |
| `ConcreteFc` | 27.58 | Tüp dolgu betonu f'c (MPa); malzeme `<psi>Psi` adıyla (27,58 = `4000Psi`) |
| `BeamMinDepth`, `BeamMaxDepth` | 250, 920 | Kiriş havuzu derinlik aralığı (mm) |
| `ColumnSeries` | `W310 W360` | Kolon havuzu W serileri (W12 ve W14) |
| `SeismicFilter` | High | Havuz kesitlerinin AISC 341-22 Tablo D1.1 kompaktlığı: `None` / `Moderate` / `High` |
| `ColumnCa` | 0.3 | Süzgeçte kolon gövdesi için varsayılan eksenel yük oranı Pu/(φPy) |
| `SlabWeight` | 3.0 | Döşeme öz ağırlığı (kPa) — döşeme modellenmez, yük kirişlere verilir |
| `SuperDead`, `RoofSuperDead` | 1.2, 1.2 | Ek ölü yük (kat, çatı; kPa) |
| `Cladding` | 4.0 | Çevre kirişlerine cephe çizgisel yükü (kN/m) |
| `LiveLoad`, `Partition` | 2.4, 0.72 | Hareketli yük ve bölme duvar payı (kPa; ASCE 7 Tablo 4.3-1, 4.3.2) |
| `RoofLive` | 0.96 | Çatı hareketli yükü (kPa) |
| `LiveFactor` | 0.5 | Kombinasyon 3–5 ve depremli kombinasyonlarda L katsayısı (f1); ASCE 7-22 baskısına göre 0.5 ya da 1.0 |
| `SnowGround`, `SnowExposure`, `SnowThermal`, `SnowImportance` | 0, 1, 1, 1 | Kar: pg (kPa; 0 = kar yok), Ce, Ct, Is; pf = 0.7·Ce·Ct·Is·pg |
| `WindSpeed` | 49 | Temel rüzgâr hızı V (m/s) |
| `WindExposure`, `WindKzt`, `WindKd`, `WindGust` | C, 1, 0.85, 0.85 | Arazi maruziyeti B/C/D, topografik faktör, yönsellik faktörü, ani rüzgâr faktörü G |
| `SDS`, `SD1`, `S1`, `TL` | 1.0, 0.6, 0.6, 8 | Tasarım spektral ivmeleri (g), S1 (g), uzun periyot geçişi (s) |
| `SiteClass` | D | Zemin sınıfı A–E |
| `SeismicProvisions` | Yes | `Yes`: ETABS AISC 341 hükümlerini de uygular (güçlü kolon–zayıf kiriş, süneklik, Lb/ry). Optimizasyon programı bunları yönetir: SCWB oranı sürekli kısıt, kesit süneklik süzgeci kesitleri eler; kirişlerin yanal desteği (`BeamBracingSpacing`) bu yüzden zorunludur. `No`: yalnızca AISC 360 dayanımı ve öteleme (karşılaştırma için) |
| `RiskCategory` | II | Risk kategorisi I–IV (deprem tasarım kategorisi için) |
| `FrameClass` | SMF | `SMF` / `IMF` / `OMF`; `R`, `Cd`, `Omega0` boşsa (0) buna göre: SMF 8 / 5,5 / 3; IMF 4,5 / 4 / 3; OMF 3,5 / 3 / 3 |
| `R`, `Cd`, `Omega0` | 0 | Katsayıları elle vermek için (0 = `FrameClass`'a göre) |
| `Ie`, `Rho` | 1, 1 | Önem katsayısı, artıklık katsayısı ρ |
| `EccentricityRatio` | 0.05 | Tepki spektrumu durumlarında kazara dışmerkezlik |
| `PreSize` | Yes | Ölçekleme analizinden önce ön boyutlandırma (Bölüm 4) |
| `SpectrumScaleMin` | 1.0 | Tepki spektrumu taban kesmesi ELF kesmesinin en az bu oranına ölçeklenir (ASCE 7-22 12.9.1.4); 0 = ölçekleme yok |
| `Modes` | 12 | Başlangıç mod sayısı (en az kat sayısı kadar alınır); kontrol analizinde kütle katılımı %90'ın altındaysa iki katına çıkarılır (en çok 3 × kat sayısı ve 60). Optimizasyon programı her analizde bu modları çözer, gereksiz çok mod süreyi uzatır |
| `DriftLimit`, `ServiceDriftRatio` | 0.02, 400 | Yalnızca raporda yazılır: kat ötelemesi sınırı (ASCE 7 Tablo 12.12-1), rüzgâr servis ötelemesi H/x |

## 3. Üretilen model

**Geometri ve sistem.** Düzenli ızgara; katlar `Story1…StoryN`; mesnetler ankastre (Perimeter sisteminde iç kolon tabanı `InteriorColumnBase`). Her katta rijit diyafram `D1`. Döşeme modellenmez: yükler kirişlere iki yönlü (45°) dağılımla üçgen/yamuk yayılı yük olarak verilir (ETABS'in boş alan yükü çerçeveye aktarılmadığı için bu yol seçildi).

**Gruplar** (her çubuk tek grupta; Steel Frame Design). Adlar: `COL-CORNER|EDGE|INTERIOR-S01-03`, `BM-EDGE|INT-L9000-S01-03` (açıklık mm). Kolonlar köşe / kenar / iç × kat bandı; kirişler kenar / iç × açıklık uzunluğu × kat bandı. Örnek: 15 kat, band 3, açıklıklar 9 ve 6 m → 15 kolon + 20 kiriş grubu (35 tasarım değişkeni).

**Kesit havuzları.** AISC16M kütüphanesinin 289 W kesitinden: `BeamSectionList` (derinlik aralığına uyanlar) ve `ColumnSectionList` (`ColumnSeries`), `SeismicFilter` süzgecinden geçenler (High: kiriş 168, kolon 46 kesit). Süzgeç AISC 341-22 Tablo D1.1 başlık ve gövde sınırlarını kullanır; kütüphanede k boyutu olmadığından gövde için k = 1,8·tf yaklaşımı kullanılır. Liste adları optimizasyon programının `App.config` adlarıyla aynıdır.

**Yük desenleri:** `Dead` (çerçeve öz ağırlığı), `SDL` (döşeme + ek ölü + cephe), `Live`, `RoofLive`, `Snow` (pg > 0 ise), `PartMass` (bölme duvar 0,48 kPa = 10 psf, yalnızca kütle için, ASCE 7 12.7.2), `WX`, `WY`.

**Rüzgâr (ASCE 7-22 Bölüm 26–27, Directional Procedure, kapalı bina).** ETABS 22.6 API'si otomatik ASCE 7-22 rüzgâr/deprem desenlerini kuramadığı için kuvvetler programda hesaplanır ve kat düğümlerine uygulanır: qz = 0,613·Kz·Kzt·V² (Ke = 1), Kz Tablo 26.10-1; sebat yüzünde qz·G·0,8, sotavent yüzünde qh·G·Cp (Tablo 27.3-1, L/B'ye göre), toplam en az 0,77 kPa (16 psf). Yük, kat düğümlerine tributary genişlikle dağıtılır (burulma yok). Kuvvetler raporda kat kat yazılıdır.

**Kütle ve modal.** Kütle: öz kütle + `SDL` + `PartMass` (+ pf > 1,44 kPa ise karın %20'si). `Modal` durumu.

**Deprem.** ASCE 7-22 tasarım spektrumu fonksiyonu (`ASCE722`, SDS, SD1, TL, zemin sınıfı) ve `RSX`, `RSY` tepki spektrumu durumları (Ie·g/R ölçeği, CQC, yönler SRSS, kazara dışmerkezlik). Taban kesmesi ölçeklemesi için ELF kesmesi ETABS deseni olmadan hesaplanır (Ta = 0,0724·h^0,8, Cu, Cs üst ve alt sınırları, S1 ≥ 0,6 g koşulu); RS durumları en az `SpectrumScaleMin × V`'ye ölçeklenir.

**Kombinasyonlar** (ASCE 7-22 2.3.1 ve 2.3.6, 35 adet, kar varsa 53): `C1` 1,4D; `C2_Lr|S` 1,2D + 1,6L + 0,5(Lr veya S); `C3L_…` ve `C3W_…` 1,2D + 1,6(Lr veya S) + (f1·L veya 0,5W); `C4_…` 1,2D + W + f1·L + 0,5(Lr veya S); `C5…` 0,9D + W; `C6_…` (1,2 + 0,2SDS)D + ρE + f1·L (+0,2S); `C7_…` (0,9 − 0,2SDS)D + ρE. D = `Dead` + `SDL`. W: ±WX, ±WY ve ±0,75WX ± 0,75WY (Şekil 27.3-8 Durum 3). E: RSX, RSY, RSX + 0,3RSY, 0,3RSX + RSY (RS sonuçlarının her iki işareti ETABS tarafından değerlendirilir). Hepsi AISC 360-22 çelik ve kompozit kolon tasarımı için işaretlidir.

**Tasarım tercihleri (AISC 360-22).** Çerçeve tipi `FrameClass`, deprem tasarım kategorisi (ASCE 7-22 Tablo 11.6-1/2; S1 ≥ 0,75 g ise E/F), Ie, ρ, SDS, R, Ω0, Cd, LRFD, Direct Analysis Method. D/C sınırı ETABS varsayılanı (0,95) kalır.

## 4. Kontrol analizi ve ölçekleme

Model geçici klasörde analiz edilir (çıktı klasöründe analiz dosyası kalmaz). Rapor `<ad>_report.txt` ve `builder.log` dosyalarında: periyotlar, kütle katılımı (< %90 ise uyarı), sismik ağırlık W, ELF kesmesi, RS kesmesi ve ölçek çarpanları, rüzgâr taban kesmeleri, önerilen `SeismicDriftAmplification = Cd/Ie`.

**Ön boyutlandırma (`PreSize`).** Yer tutucu kesitlerle bina çok esnek olur (15 katlı örnekte T1 = 4,5 s) ve RS kesmesi ELF kesmesinin çok altında kalır; çarpan 2–3 çıkar. Optimizasyon sırasında kesitler rijitleşeceğinden bu çarpan fazla büyük olur. Bu yüzden, bütün elemanların havuzlarında aynı **göreli indeksle** kesit aldığı ikiye bölme araması yapılır; hedef T ≤ Cu·Ta. Bulunan rijitlikteki bina için çarpan hesaplanır; sonra elemanlar tarafsız havuz medyanına geri alınır (yer tutucu; optimizasyon programı kendi ilk tasarımını yapar). Çarpan **sabit bir sayıdır**: optimizasyonda rijitlik değiştikçe gerçek taban kesmesi oranı da değişir. Her analizde yeniden ölçekleme optimizasyon programında henüz yoktur.

## 5. Çıktılar

Çıktı klasöründe:
- `<ad>.EDB` (+ `.$et`, `.ebk`, `.ico`) — model; analiz sonucu içermez, kilitsizdir.
- `<ad>_report.txt` — plan, gruplar, yükler, kombinasyonlar, kontrol analizi.
- `examples_summary.csv` — örnek başına bir satır: boyutlar, grup sayıları, havuz boyutları, ağırlık, periyotlar, ELF ve rüzgâr kesmeleri, ölçek çarpanları, SDC, R/Cd/Ie.
- `runs_template.csv` — optimizasyon programının toplu koşu listesi (`tools\RunBatch.ps1`). Yöntem, mod ve bütçe sütunlarını düzenleyip kullanın.
- `builder.log`.

## 6. Optimizasyon programıyla kullanım

1. `runs_template.csv` içinde `Method` (`SocialSpider`, `HarmornySearch`, `ArtificialBeeColony`, …), `Mode` (`Steel`, `Composite`, `Hybrid`), `MaxAnalyses`, `MemorySize`, `Seed` değerlerini ayarlayın.
2. `powershell -ExecutionPolicy Bypass -File tools\RunBatch.ps1 -Runs <runs.csv> -Out <kısa klasör> -Exe <exe kopyası>` (`KULLANIM_KILAVUZU.md` 8.4).
3. **Üst arama sınırı:** optimizasyon programı bir grubun üst sınırını ETABS'in *dayanım* tasarımından belirler; öteleme belirleyici olduğunda (deprem örnekleri) bu sınır yetmez ve en ağır sınır tasarımı bile uygunsuz kalır. `App.config` içinde `UpperBoundMultiplier = 1` yapın (bütün kesit listesi açılır). Arama aralığı büyüdüğü için uygun tasarımı bulmak daha çok analiz ister: 4 katlı örnekte 156 analiz yetmedi (en ağır kesitlerle tasarım uygun, maliyet 5573; ilk üst sınırda öteleme 1,11).
4. **Deprem ötelemesi:** optimizasyon programı RS durumlarının elastik ötelemesini `App.config` içindeki `SeismicDriftAmplification` ile çarpar. Değeri örneklerin `Cd/Ie` değerine (raporda yazılı; SMF, Ie = 1 için 5,5) ayarlayın. Bu değer programın tamamı için tek sayıdır; farklı Cd/Ie'li örnekleri ayrı koşturun.
5. Kompozit ve hibrit modlarda tüp dolgu betonu modeldeki malzemedir (`ConcreteFc` = 27,58 için hazır `4000Psi`; başka bir değerde `<psi>Psi` adıyla tanımlanır). Farklı bir beton adı kullanacaksanız `TubeSections.xml` içindeki `ConcreteMaterial` ayarına yazın.

## 7. Sınırlar ve kapsam dışı (ileride eklenebilir)

- **AISC 341 hükümleri varsayılan olarak açıktır** (`SeismicProvisions = Yes`). Optimizasyon programı güçlü kolon–zayıf kiriş oranını sürekli kısıt olarak okur ve AISC 341 süneklik süzgecini (Tablo D1.1) uygular. Kiriş yanal desteği `BeamBracingSpacing = 2,5 m` ile tanımlanır; desteksiz modelde ETABS kirişi tüm açıklık boyunca desteksiz sayar ve Lb/ry sınırı (D1.2b) örneği çözümsüz bırakır. Doğrulama: 4 katlı örnek, SSO, 500 analiz, destekli model → uygun tasarım (534 analiz, maliyet 1726,90); desteksiz model → uygun tasarım yok.

- Çaprazlı sistemler, düzensiz plan ve geri çekmeler, kar birikmesi, zemin–yapı etkileşimi, hareketli yük azaltması, tali kirişler ve kompozit döşeme **yok**.
- Rüzgârın burulmalı durumları (Şekil 27.3-8 Durum 2 ve 4) yok; esnek bina için ani rüzgâr faktörü hesaplanmaz (`WindGust` kullanıcı değeri).
- Rüzgâr hesabı programa aittir (ETABS otomatik yükü değildir); kuvvetler raporda listelenir, elle kontrol edilebilir.
- Perimeter sisteminde iç kirişler her iki uçta M2/M3 mafsallıdır, kolonlar sürekli ve (varsayılan) tabanda ankastredir; iç kolonlar süreklilik nedeniyle moment alabilir.
- Süzgeçteki gövde narinliği k = 1,8·tf yaklaşımıyla hesaplanır.
- Çıktı klasörü yolu en çok 150 karakter olmalıdır (.NET Framework 260 karakterden uzun yolları yazamaz).
