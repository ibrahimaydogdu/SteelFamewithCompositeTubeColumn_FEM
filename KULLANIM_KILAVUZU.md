# KULLANIM KILAVUZU

> **Geliştirme durumu (Aşama 6, 0.5.0):** Referans projeye (SteelFamewithCompositeColumn_ETABS 2026.10.3) göre farklar:
> - 16. yöntem olarak eklenen SSO;
> - kompozit kolon tipi olarak **dolgulu tüp** (bölüm 7.5);
> - **hibrit kolonlar:** grup grup çelik / kompozit, geçiş katının optimizasyonu (bölüm 7.7).
>
> Gömülü kesit, seçenek olarak duruyor. Bu yüzden kompozit kolonlar gömülü kesitle (W profil + beton + donatı) çözülüyor.
> Bu projenin hedefleri sonraki aşamalarda eklenecek:
> - kolon grubu başına çelik/kompozit seçimi (hibrit tasarım);
> - Sosyal Örümcek Algoritması (SSO, Aşama 3–4'te 16. yöntem olarak eklendi);
> - isteğe bağlı kompozit döşeme.

Kompozit kolonlu uzay çelik çerçevelerin metasezgisel yöntemlerle optimum tasarımı (ETABS 22; ETABS 19 ile de çalışır).

Program sürümü: 0.5.0 (exe dosyasında sağ tık > *Özellikler > Ayrıntılar* ve `ErrorLog.txt` dosyasındaki `======== new run` satırı).

## İçindekiler
1. [Programın yaptığı iş](#1-programın-yaptığı-iş)
2. [Kurulum](#2-kurulum)
3. [Hızlı başlangıç: ilk koşu](#3-hızlı-başlangıç-ilk-koşu)
4. [ETABS modelinin hazırlanması](#4-etabs-modelinin-hazırlanması)
5. [Form](#5-form)
6. [Yöntem seçimi ve ayar önerileri](#6-yöntem-seçimi-ve-ayar-önerileri)
7. [Kompozit kolonlar](#7-kompozit-kolonlar)
8. [Koşuyu izleme, durdurma ve devam ettirme](#8-koşuyu-izleme-durdurma-ve-devam-ettirme)
9. [Çıktılar ve sonuçların yorumlanması](#9-çıktılar-ve-sonuçların-yorumlanması)
10. [Check Structure: bir tasarımı kontrol etme](#10-check-structure-bir-tasarımı-kontrol-etme)
11. [Sık karşılaşılan durumlar](#11-sık-karşılaşılan-durumlar)
- [Ek A. Geliştiriciler için: derleme ve dağıtım paketi](#ek-a-geliştiriciler-için-derleme-ve-dağıtım-paketi)

---

## 1. Programın yaptığı iş
Program, bir ETABS çelik çerçeve modelindeki tasarım gruplarına en hafif (kompozit modda en ucuz) kesitleri atar. Bunu yaparken çelik tasarım oranlarını, kompozit kolon dayanımını, öteleme sınırlarını ve geometri kısıtlarını sağlar. Kesitler bir W kesit kütüphanesinden seçilir. Her aday tasarım ETABS'te analiz edilir ve tasarımı yapılır.

Bir koşunun akışı:
1. **Çalışma kopyası.** Model geçici bir klasöre kopyalanır; sizin modeliniz değişmez.
2. **Hazırlık.** P-Delta, servis öteleme durumları, kombinasyonlar ve çözülecek durumlar çalışma kopyasında ayarlanır.
3. **İlk sınır tasarımı.** Gruplara otomatik kesit listeleri atanır, ETABS tasarımı yapılır. Her grubun arama aralığı (alt ve üst kesit sınırı) bu tasarıma göre belirlenir.
4. **Başlangıç belleği.** *Memory size* kadar rastgele tasarım üretilip analiz edilir.
5. **Arama döngüleri.** Seçilen yöntem her döngüde bellekteki her üye için yeni tasarım(lar) üretir. Her tasarım şu sırayla işlenir:
   - geometri düzeltmesi;
   - ETABS analizi ve tasarımı;
   - kısıt ihlali varsa düzeltme ve yeniden analiz;
   - ceza ve maliyet hesabı.

   Tekrarlanan tasarımlar önbellekten okunur. Yedek düzenli yazılır ve ETABS belirli aralıklarla yeniden başlatılır.
6. **Final.**
   - En iyi tasarım tüm yük durumlarıyla ve düzeltmesiz yeniden analiz edilir.
   - Kompozit kolonlar gerçek gömülü kesitlere çevrilip ETABS'in kompozit kolon tasarımıyla doğrulanır.
   - ETABS'te aşan kolon olursa kesiti büyütülür (koruma).
7. **Çıktılar.** Sonuç XML'i, Excel kitabı, `<model>_best.EDB` ve `ErrorLog.txt`.

**Amaç fonksiyonu:**
- **Çelik modu:** yapı çeliği ağırlığı (kN).
- **Kompozit mod:** çelik·birim maliyet + donatı·birim maliyet + beton hacmi·birim maliyet + kalıp alanı·birim maliyet (bkz. [7.3](#73-birim-maliyetler)).
- Kısıt ihlali cezası: `cezalı maliyet = maliyet · (1 + ceza)³`.
- Yalnızca cezası 0 olan (tüm kısıtları sağlayan) tasarımlar "en iyi tasarım" olabilir.

**Terimler**
| Terim | Anlamı |
|---|---|
| Tasarım grubu / değişken | ETABS'te aynı kesiti alan çubuklar. Her grup için bir kesit seçilir. |
| Kesit numarası | Gruba atanan kesitin, alana göre sıralı W kesit listesindeki sırası. |
| Analiz | Bir aday tasarımın ETABS'te analizi ve tasarımı. *Max. analyses* bu sayıyı sınırlar. |
| Bellek (popülasyon) | Yöntemin üzerinde çalıştığı tasarımlar kümesi |
| Döngü (loop) | Bellekteki her üye için yeni tasarım üretilen bir tur |
| Ceza | Kısıt aşım miktarı. 0 ise tasarım tüm kısıtları sağlar (uygun tasarım). |
| Final tasarım | Koşu sonunda tüm durumlarla yeniden analiz edilen ve ETABS ile doğrulanan en iyi tasarım |

---

## 2. Kurulum
Bu bölüm, programı derlenmiş hâliyle (klasör ya da zip olarak) alan kullanıcı içindir. Programı kaynak koddan derlemek için [Ek A](#ek-a-geliştiriciler-için-derleme-ve-dağıtım-paketi)'ya bakın.

### 2.1 Gereksinimler
| Gereksinim | Not |
|---|---|
| Windows 10/11 veya Windows Server, 64 bit | |
| **ETABS 22, kurulu ve lisanslı** | Program ETABS'i kendisi açar ve kullanır; ETABS olmadan çalışmaz. ETABS 22.6 ile test edildi. Program paketi ETABS 22 için hazırlanır; başka bir ETABS sürümü kullanılacaksa programın o sürümle derlenmesi önerilir (Ek A). |
| .NET Framework 4.7.2 veya sonrası | Windows 10/11'de hazır bulunur. |
| Bellek ve disk | En az 8 GB RAM (ETABS yaklaşık 1 GB kullanır). Geçici klasörde birkaç yüz MB boş alan. |

### 2.2 Program paketi: birlikte taşınması gereken dosyalar
Program tek bir exe dosyası değildir. Aşağıdaki dosyalar **aynı klasörde** durmalıdır. Exe dosyasını tek başına başka bir yere (ör. masaüstüne) kopyalamayın; kısayol oluşturun (2.3).

| Dosya | Gerekli mi? | Eksikse ne olur |
|---|---|---|
| `FrameSap2000.exe` | Evet | Programın kendisi |
| `ETABSv1.dll` | Evet | Form açılır, ama **Start**'a basınca *Could not load file or assembly 'ETABSv1'* hatası verir. |
| `Microsoft.Win32.Registry.dll` | Evet | `ETABSv1.dll` bu dosyaya ihtiyaç duyar; ETABS bağlantısı kurulamaz. |
| `DocumentFormat.OpenXml.dll` | Evet (Excel çıktısı) | Koşu tamamlanır ve sonuç XML'i yazılır, ancak Excel kitabı yazılamaz (`Warning: Excel workbook not written`). |
| `EncasedSections.xml` | Evet (kompozit kolonlar) | Kompozit ayarları ve birim maliyetler varsayılan değerlere döner. Günlükte uyarı yazılır. |
| `FrameSap2000.exe.config` | Önerilir | ETABS yolu ve diğer ayarlar (2.4). Dosya yoksa kurulu en yeni ETABS ve onun AISC16M kütüphanesi kullanılır, diğer ayarlar varsayılan alınır. |
| `*.pdb`, `*.xml` (OpenXml) | Hayır | Yalnızca hata ayıklama ve belge dosyaları |

### 2.3 Kurulum adımları
1. Kısa yollu bir klasör oluşturun, ör. `C:\SteelOpt`.
   - Windows'un 260 karakterlik yol sınırı aşılırsa ayar dosyası veya Excel kütüphanesi yüklenemeyebilir. Çok uzun klasör adlarından ve derin klasörlerden kaçının.
   - `C:\Program Files` altına koymayın; ayar dosyasını düzenlemek yönetici izni ister.
2. Program paketi zip olarak e-posta veya internetten geldiyse:
   - **önce zip dosyasına sağ tıklayın > *Özellikler* > en alttaki *Engellemeyi kaldır* (Unblock) kutusunu işaretleyin > Tamam**;
   - sonra zip'i klasöre çıkarın.

   Aksi hâlde Windows, DLL dosyalarının yüklenmesini engelleyebilir. Programı zip'in içinden çalıştırmayın.
3. `FrameSap2000.exe` dosyasını çift tıklayarak açın.
   - İlk açılışta *"Windows bilgisayarınızı korudu"* (SmartScreen) uyarısı çıkabilir: program imzalı değildir. *Ek bilgi > Yine de çalıştır* ile açın.
   - Antivirüs programı engellerse klasörü güvenilir listeye ekleyin.
4. ETABS standart yerine kurulu değilse (`C:\Program Files\Computers and Structures\ETABS 22\`) ayar dosyasındaki yolu düzeltin (2.4). Standart yerdeyse bir şey yapmanız gerekmez.
5. İsterseniz masaüstüne kısayol oluşturun: `FrameSap2000.exe` üzerinde sağ tık > *Gönder > Masaüstü (kısayol oluştur)*.
6. Kurulumu sınamak için kısa bir deneme koşusu yapın (bölüm 3).

### 2.4 Ayarlar (`FrameSap2000.exe.config`)
Ayar dosyası Not Defteri ile açılıp düzenlenebilir. Yalnızca `value="…"` kısımlarını değiştirin, dosyanın XML yapısını bozmayın. Ondalık ayırıcı nokta olmalıdır (`0.7`). Değişiklik, programı yeniden açınca geçerli olur.

| Anahtar | Varsayılan | Açıklama |
|---|---|---|
| `ETABSProgramPath` | `C:\Program Files\Computers and Structures\ETABS 22\ETABS.exe` | `ETABS.exe` yolu. Bulunamazsa kurulu en yeni ETABS kullanılır ve uyarı yazılır. |
| `SectionPropertyDataPath` | `…\ETABS 22\Property Libraries\AISC16M.xml` | Kesit kütüphanesi. CSI formatında ve birimi **mm** olmalıdır (ETABS 19: `AISC14M.xml`). |
| `BeamAutoSelectList`, `ColumnAutoSelectList` | `BeamSectionList`, `ColumnSectionList` | İlk sınır tasarımında kullanılan otomatik kesit listeleri (bkz. 4.4). |
| `WorkFolder` | boş | Çalışma kopyalarının klasörü. Boşsa `%TEMP%\SteelFrameOpt`. |
| `ServiceLateralFactor` | 1,0 | Programın oluşturduğu `SRV_<desen>` servis durumlarının yük katsayısı. Örneğin ASCE 7 servis rüzgârı için 0,6–0,7. |
| `SeismicDriftAmplification` | 1,0 | Servis öteleme modunda deprem durumlarının yerdeğiştirme büyütmesi. ASCE 7: Cd/Ie, TBDY 2018: R/I. |
| `CompositeStrengthFactor` | 1,0 | İç kompozit dayanım oranlarının çarpanı. Final ETABS kontrolünün önerdiği değer girilebilir (bkz. 7.4). |
| `UpperBoundMultiplier` | 0,23 | Bir grubun üst arama sınırı = ETABS dayanım tasarımındaki kesit + bu orandaki kesit listesi payı. Öteleme belirleyiciyse `1` (bütün liste). Literatürde her grup için tam W listesi yaygındır. |
| `WidenBoundsForDrift` | true | Başlangıçta, bütün değişkenler üst sınırdayken tasarım bir kez analiz edilir; öteleme sınırı aşılıyorsa üst sınırlar `UpperBoundMultiplier` kadar kademeli artırılır (en çok bütün liste). İlk sınırlarda uygun olan modeller değişmez. `false` kapatır. |
| `SeismicDuctilityFilter` | auto | AISC 341 süneklik süzgeci (Tablo D1.1): `auto` = yalnızca AISC 341 denetimleri modelde etkinse (SCWB oranları var) ve çerçeve tipi SMF (yüksek) veya IMF (orta) ise; `on` = her zaman; `off` = hiçbir zaman. Kompakt olmayan kesitler her analizden önce en yakın uygun kesitle değiştirilir |
| `SeismicColumnCa` | 0,3 | Süzgeçte kolon gövde sınırı için varsayılan eksenel yük oranı Pu/(phi Py); gerçek Ca daha büyükse kalan durumlar cezayla yakalanır |
| `DesignRatioLimit` | boş | Çelik ve kompozit kolon tasarımının **D/C oranı sınırı** (bkz. 7.6). Boşsa modelin ETABS tasarım tercihlerindeki değer kullanılır (ETABS varsayılanı 0,95). Bir sayı girilirse (örneğin AISC 360 için 1,0) bu değer çalışma kopyasının tercihlerine yazılır. |

Örnek: ETABS `D:\CSI\ETABS 22` klasörüne kurulduysa:
```xml
<add key="ETABSProgramPath" value="D:\CSI\ETABS 22\ETABS.exe" />
<add key="SectionPropertyDataPath" value="D:\CSI\ETABS 22\Property Libraries\AISC16M.xml" />
```

### 2.5 ETABS lisansı ve uzun koşular
- Program kendi ETABS örneğini açar (*Hide ETABS* seçiliyse görünmez) ve koşu sonunda kapatır. Açık olan kendi ETABS pencerelerinize dokunmaz.
- ETABS lisansınız aynı anda tek oturuma izin veriyorsa, koşu sırasında ETABS'i ayrıca açmayın. Lisans sorunu varsa önce ETABS'i elle açıp lisansın çalıştığını kontrol edin.
- Koşu sırasında Görev Yöneticisi'nde görünen ETABS süreci programındır; kapatmayın. Programın kendisi ETABS'i 100 analizde bir yeniden başlatır.
- **Uzun koşulardan önce:**
  - bilgisayarın uyku moduna geçmesini kapatın (*Ayarlar > Sistem > Güç > Uyku: Hiçbir zaman*);
  - mümkünse Windows Update yeniden başlatmalarını erteleyin.

  Kesinti olursa koşu yedekten devam eder (bölüm 8.3).
- Model OneDrive gibi senkronize bir klasörde olabilir. Analiz dosyaları geçici klasöre yazılır, senkronize klasör dolmaz.

---

## 3. Hızlı başlangıç: ilk koşu
1. **Modeli hazırlayın** (bölüm 4): gruplar, çelik tasarım prosedürü, yük desen tipleri, kombinasyonlar.
2. Programı açın. **MainPage** sekmesinde:
   - *ETABS Model* → **Select**: modeli (`*.EDB`) seçin.
   - *Output File* → **Create**: sonuç dosyasının adını ve yerini seçin (`*.xml`). Excel kitabı, yedek ve önbellek dosyaları da bu dosyanın yanına yazılır.
   - *Hide ETABS*: ETABS penceresini gizler (önerilir, biraz daha hızlıdır).
3. **Structural Properties** sekmesinde:
   - Öteleme sınırlarını girin; varsayılan H/300 ve h/300.
   - Çelik tasarım kodunu seçin; varsayılan AISC 360-22.
   - Kompozit kolon isteniyorsa *Composite columns* kutusunu işaretleyin, yanındaki listeden tipini seçin (*Filled tube* veya *Encased*) ve birim maliyetleri kontrol edin.
4. **Optimization parameters** sekmesinde:
   - Yöntemi seçin; ilk deneme için *Harmony Search* veya *Teaching-Learning*.
   - *Memory / population size* ve *Max. analyses* değerlerini girin. Deneme için 10 ve 100; gerçek koşu için bkz. bölüm 6.
5. **Start**'a basın.
   - Durum satırı o anki aşamayı gösterir.
   - İlk sınır tasarımı birkaç dakika sürebilir (525M modelinde 2–10 dakika).
   - Arama başlayınca analiz sayısı, en iyi maliyet ve kalan süre güncellenir.
6. Koşu bitince bir mesaj kutusu çıkar:
   - "API script completed successfully.": final tasarım tüm kontrolleri sağlıyor.
   - Bir uyarı: final tasarım bazı kontrolleri sağlamıyor; `ErrorLog.txt` dosyasına bakın.
7. **Sonuçlar:**
   - `<çıktı>.xlsx` dosyasını açın (bölüm 9).
   - `<model>_best.EDB`, en iyi tasarımın ETABS modelidir.

> **Deneme koşusu önerisi:** gerçek koşudan önce 10 üye / 100 analizlik kısa bir koşu yapın. Model hazırlığındaki eksikler (kombinasyon, malzeme, grup) ilk dakikalarda `ErrorLog.txt` dosyasında görünür.

---

## 4. ETABS modelinin hazırlanması
Program seçilen modeli **değiştirmez**:
- Koşu başında model geçici bir çalışma klasörüne kopyalanır (`%TEMP%\SteelFrameOpt\<model>_<tarih_saat>`) ve tüm analizler orada yapılır.
- Analiz dosyaları OneDrive gibi senkronize klasörleri doldurmaz.
- Çalışma klasörü koşu sonunda silinir; yolu `ErrorLog.txt` dosyasında `Info: working copy …` satırında yazar. Kesilen koşuların klasörleri 2 gün sonra bir sonraki koşuda silinir.
- En iyi tasarım, orijinal modelin yanına `<model>_best.EDB` adıyla kaydedilir.

### 4.1 Gruplar ve tasarım değişkenleri
- Aynı kesiti alacak çubukları bir grupta toplayın. Her çubuk yalnızca bir gruba ("All" dışında) ait olmalıdır.
- Grup içindeki çubukların tasarım prosedürü aynı olmalıdır.
- Tasarım prosedürü *Steel Frame Design* olan her grup bir **tasarım değişkeni** olur. Diğer gruplar (beton, No Design vb.) sabit kalır.
- Kompozit modda, tüm üyeleri düşey olan çelik gruplar gömülü kompozit kolon olarak tasarlanır. **Kolon ve kiriş gruplarını ayrı tutun.**
- Değişken sayısı arttıkça arama zorlaşır. Benzer çubukları (ör. iki katta bir aynı kolon) aynı grupta toplamak sonucu iyileştirir.

### 4.2 Malzemeler
- `A992Fy50` çeliği modelde tanımlı olmalıdır.
- Kompozit modda `EncasedSections.xml` içinde adı verilen beton (ör. `4000Psi`) ve donatı (ör. `A615Gr60`) malzemeleri de modelde bulunmalıdır.

### 4.3 Yükler ve kombinasyonlar
- Yük desenlerinin tipleri doğru olmalıdır: Dead, Live, **Wind**, **Quake**. Otomatik kombinasyonlar ve yatay yük tespiti bu tiplere göre yapılır.
- Modelde çelik tasarımı için işaretli *dayanım* kombinasyonları varsa bunlar kullanılır.
- Yoksa ve formda *Create default design combos* seçiliyse, program ETABS'in yönetmeliğe göre varsayılan kombinasyonlarını oluşturur (`DStlS…`, `DStlD…`).
- Kombinasyon yoksa ve bu seçenek kapalıysa program hata vererek durur.

### 4.4 Otomatik kesit listeleri (ilk sınır tasarımı)
- Başlangıç sınırları için bütün değişken kirişlere `BeamSectionList`, kolonlara `ColumnSectionList` atanır ve ETABS tasarımı yapılır.
- Listeler modelde yoksa kütüphanedeki **tüm W kesitleriyle** (AISC16M: 289 kesit) otomatik oluşturulur. İlk tasarım bu durumda uzun sürer: 525M modelinde (14 grup) 8 dakika ölçüldü.
- Daha kısa bir liste için listeyi ETABS'te kendiniz tanımlayın (*Define > Section Properties > Frame Sections > Auto Select List*) ve adını `App.config` dosyasına yazın. Örneğin 525M modelinde hazır `A-LatBm` / `A-LatCol` listeleri vardır.
- Grubun arama aralığı, bu tasarımda bulunan kesit ve tasarım oranına göre belirlenir. Kompozit gruplarda alt sınır en küçük kesittir: beton katkısıyla daha küçük profiller de yeterli olabilir.

### 4.5 Analiz
Kompozit kolonlarda B2 = 1 kabul edilir (`EncasedSections.xml`); bu yüzden analizde P-Delta etkisi olmalıdır. Formdaki *P-Delta analysis* seçeneği (varsayılan açık) bunu çalışma kopyasında sağlar ve kendi modelinizi değiştirmez.

### 4.6 AISC 341 güçlü kolon–zayıf kiriş (SCWB)
Tasarım tercihlerinde çerçeve tipi SMF/IMF ve AISC 341 hükümleri açıksa (*Ignore Seismic Code* = No) ve modelde depremli kombinasyonlar varsa ETABS her kolon için kiriş/kolon kapasite oranını hesaplar (`Steel Column Envelope` tablosunda `BCMajor`, `BCMinor`; sınır 1,0). Program bu oranı **sürekli bir kısıt** olarak kullanır: sınırı aşan kolon grubunun oranı, grubun tasarım oranına katılır (günlük ve kısıt özeti: "beam/column capacity ratio"); sınırı aşılan grup, ceza ve onarım adımlarıyla büyütülür. ETABS bu durumda "6 Steel frames with auto select sections failed stress/capacity check" sorusunu sorar; gizli ETABS'te program bunu otomatik "No" ile cevaplar. 525M gibi depremli kombinasyonu olmayan modellerde etkisi yoktur.

### 4.6b AISC 341 süneklik süzgeci
SMF/IMF çerçevelerde AISC 341 açıkken ETABS, kompakt olmayan kesitleri ("Section is not seismically compact for highly ductile members") ve Lb/ry sınırını aşan kirişleri ("Lb/ry > 0.095*E/(Ry*Fy)") tasarım hatası sayar. Program bu durumda (1) kesit süzgecini uygular (günlük: `Info: AISC 341 ductility filter ... N of 289 sections for beams, M for columns`) ve (2) her farklı ETABS tasarım hatasını `ErrorLog.txt`'ye grup ve kesit adıyla bir kez yazar. **Kirişlerin yanal desteğini modelde tanımlayın:** destek yoksa ETABS kirişi tüm açıklık boyunca desteksiz sayar ve Lb/ry sınırı çoğu kirişi eler (ModelBuilder `BeamBracingSpacing = 2,5 m` ile tasarım overwrite `LMinor` / `LTB` değerlerini yazar). Kendi modelinizde bunu ETABS'ta *Design Overwrites > Unbraced Length Ratio* ile girin.

### 4.7 Model kontrol listesi
- [ ] Değişken gruplar *Steel Frame Design*, kolon ve kiriş grupları ayrı
- [ ] `A992Fy50` (ve kompozit modda beton ile donatı malzemeleri) tanımlı
- [ ] Yük desen tipleri doğru (Wind / Quake)
- [ ] Dayanım kombinasyonları tanımlı ya da *Create default design combos* açık
- [ ] Model ETABS'te hatasız analiz ediliyor
- [ ] Deprem ötelemesi kontrol edilecekse `SeismicDriftAmplification` ayarlandı

---

## 5. Form

### 5.1 MainPage sekmesi
| Alan | Açıklama |
|---|---|
| *ETABS Model* / **Select** | Optimize edilecek model (`*.EDB`) |
| *Output File* / **Create** | Sonuç dosyası (`*.xml`). Excel kitabı, yedek ve önbellek bu dosyanın yanına yazılır. |
| **Excel** | Seçili çıktı dosyasından Excel kitabını yeniden oluşturur (ve varsa `.check.xml`'den Check Structure kitabını) |
| **Start / Stop** | Koşuyu başlatır; koşu sırasında **Stop** olur (bölüm 8) |
| *Hide ETABS* | ETABS penceresini gizler |
| *Load BackUp File* | Kesilen bir koşuya devam eder (bölüm 8.3) |
| *Check Structure Only* | Çıktı dosyasındaki tasarımı düzeltme yapmadan kontrol eder (bölüm 10) |
| *Date, Start, Finish, Av. analysis (s)* | Koşu tarihi, başlangıç ve bitiş saati, analiz başına ortalama süre |
| *Analyses, Best cost, Elapsed, Remaining* | Yapılan / en fazla analiz, en iyi uygun tasarımın maliyeti, geçen ve tahmini kalan süre |
| Listeler | Sol: en iyi tasarımın kesitleri. Sağ: iyileşme geçmişi (analiz, döngü, maliyet). |
| Durum satırı | O anki aşama ve aşamada geçen süre (bölüm 8.1) |

### 5.2 Structural Properties sekmesi
**Frame Properties**
- *Number of Joint / Members / Group / Section*: modelden okunan sayılar (salt okunur). *Group*, tasarım değişkeni olan grup sayısıdır.
- *Top St Drift (1/)*, *Inter St Drift (1/)*: tepe ve kat arası öteleme sınırları, H/oran ve h/oran (pozitif sayı).
- *Steel DesignCode*: çelik tasarım kodu.
  - ETABS 22: `AISC 360-22` (varsayılan) veya `AISC 360-16`.
  - ETABS 19 API'si bu iki kodu kabul etmez; `AISC 360-10` seçin.
  - Atanan kod `ErrorLog.txt` dosyasında `Info: steel design code …` satırında yazar.
- *Column to Column*: üst kat kolonu alt kattakinden büyük (alan ve derinlik) olamaz.
- *Beam to Column*: kiriş flanşı bağlandığı kolona sığmalıdır.
- Kutu işaretli değilse ilgili kısıt kullanılmaz.

**Analysis / Composite Options**
- *Composite columns*: kolon gruplarını kompozit kolon olarak tasarlar. Yanındaki listeden tip seçilir:
  - *Filled tube (box / pipe)* (varsayılan): beton dolgulu çelik kutu (veya boru). Bkz. bölüm 7.5.
  - *Encased (W + concrete + rebar)*: W profilin etrafı donatılı betonla kaplanır. Bkz. bölüm 7.1.
  - Dolgulu tüpte donatı ve kalıp yoktur; bu yüzden *Rebar* ve *Formwork* birim maliyet kutuları pasiftir.
- *Hybrid Columns* kutusu (aynı sekme, sol alt): kolon gruplarının çelik veya kompozit olması grup grup belirlenir. Ayrıntı: bölüm 7.7.
  - İşaretliyken **yalnızca kolonlar** kompozit olur: tüm üyeleri düşey olan çelik gruplar gömülü kompozit kolondur, kirişler ve diğer gruplar çelik kalır.
  - İşaretli değilse tüm gruplar çeliktir ve maliyet çelik ağırlığıdır (kN).
  - Koşunun kompozit çalıştığı, günlükteki `Info: composite columns (…) in groups [...]` satırından ve sonuçta `[EC …]` ile yazılan kesitlerden anlaşılır.
- *Composite code*: kompozit kontrolün yönetmelik sürümü, `AISC 360-16` veya `AISC 360-22` (varsayılan). Çelik tasarım kodundan bağımsızdır.
  - Gömülü kolonlarda iki sürümün formülleri aynıdır; sonuç değişmez.
  - Farklar dolgulu kutu ve boru kesitlerdedir. Bu kesitler henüz optimizasyona bağlı değildir.
  - Eski bir yedekten devam edilirse `AISC 360-16` kullanılır.
- *Create default design combos if model has none*: dayanım kombinasyonu yoksa ETABS'in varsayılan kombinasyonlarını oluşturur.
- *Drift check combos*: öteleme kontrolünde kullanılacak sonuçlar.
  - **Lateral load cases (service, unfactored)** (varsayılan): yükleri tümüyle rüzgâr/deprem desenlerinden oluşan doğrusal statik durumlar ve response spectrum durumları.
    - Modelde böyle bir durum yoksa program çalışma kopyasında her rüzgâr ve deprem deseni için katsayısız bir doğrusal durum (`SRV_<desen>`) oluşturur. Katsayı `ServiceLateralFactor` ayarıyla verilir.
    - Deprem durumlarının elastik ötelemesi `SeismicDriftAmplification` ile çarpılır (ASCE 7: Cd/Ie, TBDY 2018: R/I). Katsayı 1,0 bırakılırsa günlüğe uyarı yazılır. Rüzgâr durumları büyütülmez.
  - *Lateral (wind / earthquake) only*: yalnızca rüzgâr veya deprem içeren kombinasyonlar; bunlar yoksa bu tür yük durumları.
  - *All cases and combos*: modal, burkulma ve iç durumlar dışındaki tüm durum ve kombinasyonlar.
  - **Dikkat:** son iki mod katsayılı dayanım kombinasyonlarını da (ör. 1,2D + 1,6W) kullanır. Öteleme sınırları genellikle servis yükleri içindir; bu modlarda öteleme fazla tahmin edilip kolonlar gereğinden büyük çıkabilir.
- *Random seed*:
  - 0 girilirse her koşu farklı olur (saat bazlı). Pozitif bir sayı aynı başlangıcı tekrarlar.
  - Kullanılan tohum pencere başlığında, `ErrorLog.txt` dosyasında ve sonuçta yazar.
  - ETABS'in P-Delta çözümündeki çok küçük sayısal farklar nedeniyle aynı tohumlu iki koşu birebir aynı sonucu vermeyebilir.
- *Skip analysis cases not used by design / drift checks* (varsayılan açık): dayanım ve sehim kombinasyonlarında ve öteleme kontrolünde kullanılmayan yük durumları çözülmez.
  - Ön koşul durumları, Modal ve ETABS iç durumları (`~…`) her zaman çözülür.
  - Kapatılan durumlar günlükte listelenir. En iyi tasarım tüm durumlarla yeniden analiz edilir.
- *P-Delta analysis (nonlinear cases + preset P-Delta)* (varsayılan açık):
  - Nonlineer statik durumlara P-Delta atanır.
  - Doğrusal durumlar için ön tanımlı P-Delta "Non-iterative Based on Mass" yapılır; modelde başka bir yöntem tanımlıysa o korunur.
  - Değişiklikler günlükte `Info: P-Delta: …` satırında listelenir.

**Composite Cost (relative unit prices)**: kompozit modda birim maliyetler (bkz. [7.3](#73-birim-maliyetler)).

Sayı kutularında ondalık ayırıcı olarak "." veya "," kullanılabilir; Windows dil ayarından bağımsızdır.

### 5.3 Optimization parameters sekmesi
Sol tarafta **General** ve **Evaluation** grupları, sağ tarafta **Method parameters** grubu bulunur.

**General**
| Alan | Açıklama |
|---|---|
| *Method* | 16 yöntemden biri (bölüm 6) |
| *Memory / population size* | Bellek (popülasyon, koloni, sürü) büyüklüğü |
| *Max. analyses* | En fazla ETABS analizi; arama bu sayıya ulaşınca biter |
| *Memory Update* | Yeni tasarımın belleğe nasıl gireceği. Yalnızca HS, BBO, Whale ve Dandelion'da geçerlidir; diğer yöntemlerde soluk görünür (aşağıdaki tablo). |
| *Clear Duplicates* | Her döngü sonunda bellekteki aynı tasarımları atar, yerlerine rastgele tasarım üretir |
| *Levy Flight* | Yalnızca BBO, ABC, BSO ve Crow'da etkilidir; ne değiştirdiği Method parameters kutusunda yazar |
| *Test Math* | ETABS'siz matematik test problemi (geliştirme ve deneme amaçlı) |

*Memory Update* seçenekleri:
| Seçenek | Yeni tasarım hangi üyenin yerine geçer | Koşul |
|---|---|---|
| Directly with old member | Üretildiği üye | Her zaman |
| Directly with random member | Rastgele bir üye | Her zaman |
| Directly with worst member | En kötü üye | Her zaman |
| Greedy with old member | Üretildiği üye | Yalnızca daha iyiyse |
| Greedy with random member | Rastgele bir üye | Yalnızca daha iyiyse |
| **Greedy with worst member** (varsayılan) | En kötü üye | Yalnızca daha iyiyse |

**Method parameters**: seçili yöntemin adı, kısa açıklaması, kaynağı, kabul kuralı, Levy seçeneğinin etkisi ve parametreleri.
- Parametre adının üzerine gelince sınırlar ve açıklama görünür.
- Yöntem değiştirildiğinde girilen değerler kaybolmaz.
- Geçersiz değerde (sayı değil, sınır dışı, tam sayı değil) koşu başlamadan uyarı verilir.

**Evaluation**
- *Repair mode*: kısıt ihlallerinin düzeltilme şekli.
  - **Combined (faster)** (varsayılan): öteleme ve dayanım düzeltmeleri tek analizin sonuçlarından birlikte yapılır, ardından bir yeniden analiz. 525M modelinde değerlendirme başına yaklaşık 26 s.
  - **Sequential (original)**: her düzeltme adımından sonra ayrı analiz (en fazla 6 analiz, yaklaşık 68 s).
  - İki mod farklı sonuçlar verir. Karşılaştırılacak koşularda aynı mod kullanılmalıdır.
- *Reuse results of repeated designs* (varsayılan açık): daha önce değerlendirilmiş bir tasarım tekrar üretilirse ETABS çağrılmaz, saklanan sonuç kullanılır.
  - Bu değerlendirmeler analiz sayısına eklenmez.
  - Art arda 20 döngüde yeni tasarım değerlendirilmezse arama yakınsamış sayılır ve koşu biter.
- *Restart ETABS every … (0 = off)* (varsayılan 100): ETABS'in bellek kullanımı arama boyunca büyür (525M: 10 analizde 660 → 960 MB). Bu sayıda analizden sonra model kaydedilir, ETABS kapatılır ve yeni bir ETABS kaydedilen modeli açar.
  - Sonuçlar değişmez: aynı tasarım yeniden başlatmadan önce ve sonra aynı sonuçları verir.
  - Bir yeniden başlatma 525M modelinde 40–47 s sürer (100 analizde bir: yaklaşık %2 ek süre); bellek yaklaşık 580 MB'a iner.
  - Günlükte `Info: ETABS restart …` satırı yazar.
  - Yeniden başlatma kaydedilen `.EDB` ile yapılır. `.e2k` ile yeniden oluşturma ETABS 22.6'da model verilerini eksik taşıdığı için kullanılmaz.

---

## 6. Yöntem seçimi ve ayar önerileri

### 6.1 Yöntemler
| Yöntem | Kısaca | Kabul | Üye başına analiz | Parametreler (varsayılan) |
|---|---|---|---|---|
| Harmony Search (HS) | Bellekten seçme, perde ayarı, rastgele seçme | Memory update | 1 | PAR 0,6; HMCR 0,9; PAR türü Dynamic, HMCR türü Adaptive |
| Biogeography-Based (BBO) | Habitatlar arası göç, mutasyon | Memory update | 1 | mutasyon oranı 0,1 (Levy: mutasyon) |
| Whale (WOA) | Kuşatma, sarmal ve rastgele arama | Memory update | 1 | — |
| Dandelion (DO) | Yükselme, alçalma ve iniş aşamaları | Memory update | 1 | — |
| Artificial Bee Colony (ABC) | İşçi arı, gözcü arı (rulet), kâşif arı | Açgözlü | 2 (+ kâşifler) | terk sınırı 0 (= koloni × değişken), değişim oranı MR 0 (= tek değişken) (Levy: kâşif arı) |
| Ant Colony (ACO) | Feromon ve hafif kesit tercihiyle kesit seçimi | Arşivin en kötüsüyle | 1 | α 1, β 0,5, buharlaşma ρ 0,2, feromon bırakan oran 0,2 |
| Brain Storm (BSO) | Fikir kümeleri (k-means), bir veya iki kümeden yeni fikir | Açgözlü | 1 | küme 5; P(merkez değiştir) 0,2; P(tek küme) 0,8; P(merkez \| tek) 0,4; P(merkez \| iki) 0,5; logsig eğimi 20; adım 0,1 × aralık (Levy: değiştirilen merkez) |
| Crow Search (CSA) | Başka karganın belleğini izleme veya rastgele hareket | Açgözlü | 1 | farkındalık AP 0,1; uçuş uzunluğu fl 2 (Levy: rastgele hareket) |
| Firefly (FA) | Daha parlak ateş böceklerine doğru hareket | Açgözlü | 1 | α 0,2 × aralık; β0 1; βmin 0,2; γ 15 (normalize mesafe); α sönümü 0,97 / döngü |
| Grasshopper (GOA) | Sosyal etkileşim, daralan konfor bölgesi | Doğrudan | 1 | cMax 1; cMin 0,00004; f 0,5; l 1,5 |
| Teaching-Learning (TLBO) | Öğretmen ve öğrenci aşamaları (isteğe bağlı HS aşaması) | Açgözlü | 2 (HS ile 3) | öğretme faktörü 0 (= rastgele 1/2); HS aşaması hayır; HMCR 0,85; PAR 0,45 |
| Tree-Seed (TSA) | Ağaç başına tohumlar, en iyi tohum ağacın yerine | Açgözlü | 2–5 | arama eğilimi ST 0,1; ağaç başına tohum 2–5 |
| Grey Wolf (GWO) | Alfa, beta, delta kurtlarına göre hareket | Doğrudan (liderler saklanır) | 1 | başlangıç a 2 |
| Honey Badger (HBA) | Kazma (kardioid) ve bal aşamaları | Açgözlü | 1 | β 6; C 2 |
| Aquila (AO) | Yüksek süzülme, kontur uçuşu (Levy), alçak uçuş, yürüyerek yakalama | Açgözlü | 1 | α 0,1; δ 0,1 |
| Social Spider (SSO) | Dişi ve erkek örümcekler titreşimlere göre hareket eder (en yakın daha iyi örümcek, en iyi örümcek, en yakın dişi); baskın erkekler yarıçap içindeki dişilerle çiftleşir | Seçime bağlı: *If better* (varsayılan) açgözlü, *Always* doğrudan; yavru en kötü örümceğin yerine geçer | ≈1,1 (sıçramayla ≈1,5'e kadar) | PF 0,7; çiftleşme yarıçapı 0,5 × aralık; kabul *If better*; sıçrama hayır |

Açıklamalar:
- **Kabul kuralları:**
  - *Açgözlü*: yeni tasarım yalnızca daha iyiyse eskisinin yerine geçer.
  - *Doğrudan*: her zaman yerine geçer. GWO'da bulunan en iyi üç tasarım (alfa, beta, delta) ayrıca saklanır; en iyi tasarım kaybolmaz.
  - *Arşivin en kötüsüyle*: ACO'da yeni tasarım, bellekteki en kötü tasarımla karşılaştırılır.
- **Zamana bağlı katsayılar** (GWO `a`, GOA `c`, HBA yoğunluğu, BSO adımı, AO evreleri, Firefly α), yapılan analizin *Max. analyses* değerine oranıyla ilerler. *Max. analyses* değerini gerçekçi seçin: çok büyük bir değer, aramanın keşif aşamasında kalmasına yol açar.
- **Yöntem durumu** (ABC deneme sayaçları, ACO feromonu, GWO liderleri, SSO dişi sayısı ve örümceklerin cinsiyeti) yedeğe yazılır; *Load BackUp* ile kaldığı yerden devam edilir.
- **ABC terk sınırı:** varsayılan 0, "koloni × değişken" demektir (ör. 10 × 14 = 140 deneme). Kısa koşularda (birkaç yüz analiz) hiçbir kaynak bu sayıya ulaşmaz ve kâşif arı aşaması çalışmaz. Kısa koşularda 10–20 girin.
- Bütün yöntemler aynı değerlendirme altyapısını kullanır: geometri düzeltmesi, kısıt onarımı, önbellek, yedek ve ETABS yeniden başlatma.

### 6.2 Hangi yöntem?
- Kesin bir "en iyi yöntem" yoktur; sonuç modele ve ayarlara bağlıdır. Akademik karşılaştırmada her yöntemi aynı analiz sayısıyla ve birkaç farklı tohumla çalıştırıp ortalama ile en iyi sonucu raporlayın.
- **Başlangıç için:** Harmony Search (bu programın asıl yöntemi) ve TLBO. TLBO'nun ayarlanacak parametresi neredeyse yoktur.
- **Çok değişkenli modellerde** (20+ grup): TLBO, ABC ve TSA, üye başına birden fazla tasarım denedikleri için genellikle daha kararlı ilerler. Analiz bütçesini de o oranda hızlı harcarlar.
- **Hızlı yakınsama gereken kısa koşularda:** GWO, HBA ve AO, en iyi tasarıma güçlü biçimde yönelir; erken bir yerel optimuma takılma riski vardır.
- Levy seçeneği büyük sıçramalarla çeşitlilik ekler. Arama erken durgunlaşıyorsa açmayı deneyin.
- **SSO:**
  - Koloninin %65–90'ı dişidir; oran koşunun başında rastgele seçilir ve yedekte saklanır.
  - *Spider jump* (I. Aydoğdu'nun SSO_Column eki) çeşitliliği artırır, ama döngü başına analiz sayısını yaklaşık %40 artırır.
  - *Always* seçeneği özgün SSO'dur (Cuevas vd. 2013). *If better* seçeneği, Fortran kodundaki `greedyselection = 1` seçeneğiyle aynıdır; pahalı ETABS analizlerinde önerilir.

### 6.3 Bellek, analiz sayısı ve süre
- **Analiz süresi:** 525M modelinde (525 eleman, 14 grup) bir değerlendirme ortalama 13–25 s sürüyor. Bu süreye onarım, önbellek ve yeniden başlatmalar dahildir.
  - 1000 analiz: yaklaşık 4–7 saat.
  - 5000 analiz: 1–1,5 gün.
- **Süreye eklenen sabit aşamalar:** ilk sınır tasarımı (2–10 dakika; tüm gruplar 289 kesitlik listeyle tasarlandığında uzar, bkz. 4.4), başlangıç belleği (*Memory size* × analiz süresi) ve final doğrulaması (3–5 dakika).
- **Önerilen ayarlar:**

  | Koşu | Memory size | Max. analyses |
  |---|---|---|
  | Deneme | 10 | 100 |
  | Kısa | 20–30 | 500–1000 |
  | Makale / tez | 30–50 | 3000–10000 |

  Bellek, analiz sayısının yaklaşık %1–5'i olsun.
- Uzun koşularda *Restart ETABS every* 100 ve *Reuse results* açık kalsın. Yedek kendiliğinden yazılır; elektrik kesintisinde koşu kaldığı yerden sürer (bölüm 8.3).

---

## 7. Kompozit kolonlar

### 7.1 Gömülü kesitin oluşturulması (`EncasedSections.xml`)
Ayarlar exe'nin yanındaki `EncasedSections.xml` dosyasındadır. Dosya Not Defteri ile düzenlenebilir: yalnızca etiketler arasındaki değerleri değiştirin (ör. `<ConcreteCover>50</ConcreteCover>`), uzunluklar mm'dir, ondalık ayırıcı noktadır. Değişiklik bir sonraki koşuda geçerli olur. Dosyanın bir kopyasını saklamanız önerilir.

Her W kesiti için bir gömülü kesit üretilir:
- **Boyutlar:** H = d + 2·`ConcreteCover`, B = bf + 2·`ConcreteCover`. Sonuç `DimensionRounding` değerine yukarı yuvarlanır ve en az `MinDimension` olur.
- **Donatı:** çevreye dizilir. Yüz başına çubuk sayısı `MinBarsPerFace` değerinden başlar ve ρsr ≥ %0,4 olana kadar (en fazla `MaxBarsPerFace`) artırılır. Çap `RebarDiameter`, pas payı (çubuk merkezine) `RebarCover`.
- **Katsayılar:** `K22`, `K33` burkulma boyu katsayıları, `B2` yanal ötelemeli çerçeve büyütme katsayısıdır (P-Delta analizinde 1).
- **`FlexureMethod`:** gömülü kesitte eğilme dayanımı yöntemi.
  - `StrainCompatibility` (varsayılan, AISC I1.2b): ETABS kompozit kolon tasarımıyla uyumlu.
  - `PlasticStress` (I1.2a): önceki yöntem; zayıf eksende yaklaşık %19 yüksek Mn veriyordu.
- **`TieDiameter`, `TieSpacing`:** ETABS ile doğrulamada gömülü kesitin etriyeleri.
- **Birim maliyetlerin dosya varsayılanları:** `SteelUnitCost`, `RebarUnitCost`, `ConcreteUnitCost`, `FormworkUnitCost`.

Arama sırasında ETABS'te kesitler `EC_<W adı>` adında *General* kesit olarak görünür. Kesit notlarında beton ölçüsü ve donatı yazar. Rijitlikler dönüştürülmüş (EI_eff), ağırlık gerçek değerdir. Bu kolonlar ETABS'te "No Design" olarak işaretlidir; tasarım sonuçları programın çıktılarında yer alır.

### 7.2 Kontroller (AISC 360-16 / 360-22, LRFD)
- Eksenel basınç ve çekme (I2)
- Eğilme: şekil değiştirme uyumu (varsayılan) veya plastik gerilme dağılımı
- B1 büyütmeli H1-1 etkileşimi
- Kesme: çelik profil; güçlü eksende G2.1(b) Cv1
- Detay sınırları: As ≥ %1 Ag, ρsr ≥ %0,4

Kolonun oranı, tüm üyelerde ve kombinasyonlarda bu kontrollerin en büyüğüdür.

### 7.3 Birim maliyetler
Birim maliyetler **Structural Properties** sekmesindeki **Composite Cost (relative unit prices)** grubunda girilir.

| Alan | Birim | Varsayılan |
|---|---|---|
| *Steel* | kN çelik başına | 1 |
| *Rebar* | kN donatı başına | 0,5 |
| *Concrete* | m³ beton başına | 0,6 |
| *Formwork* | m² kalıp başına (kolon çevresi × boy) | 0,15 |

Varsayılanların dayandığı birim fiyat varsayımları:
- uygulanmış yapısal çelik 2,0 $/kg (≈ 204 $/kN);
- donatı 1,0 $/kg (≈ 102 $/kN);
- yerine konmuş C30 beton 120 $/m³;
- kolon kalıbı 30 $/m².

Oranlar, bu fiyatların çelik fiyatına bölünmesiyle bulundu (çelik = 1).

- Form açılırken alanlar `EncasedSections.xml` değerleriyle dolar. Koşuda formdaki değerler kullanılır ve günlüğe `Info: unit costs (form) …` satırıyla yazılır.
- Değerler negatif olamaz ve en az biri sıfırdan büyük olmalıdır.
- Değerler yedeğe kaydedilir. Eski yedeklerde bu alan yoktur; o durumda `EncasedSections.xml` değerleri kullanılır.

> **Dikkat:** optimum çözüm bu oranlara doğrudan bağlıdır. Çalışmanızın (ülke, yıl) fiyat oranlarını girin.

### 7.4 ETABS ile doğrulama, koruma ve kalibrasyon
Arama sırasında kolonlar hızlı iç çözücüyle kontrol edilir. Koşu sonunda (ve *Check Structure*'da) en iyi tasarım ETABS'in kendi kompozit kolon tasarımıyla doğrulanır (ETABS 20+):
- Kolonlar gerçek gömülü kesitlere (Concrete Encasement Rectangle + donatı) çevrilir, model yeniden analiz edilir ve ETABS kompozit tasarımı formdaki *Composite code* ile çalışır.
- Her grup için ETABS PMM ve kesme oranı, iç çözücünün dayanım ve detay oranlarıyla yan yana yazılır: `ErrorLog.txt` (`Info: ETABS composite check …`), sonuç XML'i, Excel'deki *ETABS composite* sayfası.
- Bu adım 525M modelinde yaklaşık 3 dakika sürer. `_best.EDB` gömülü kesitleri ve ETABS tasarım sonuçlarını içerir.
- **Fark:** 525M modelinde ETABS oranları iç çözücüden %0,5–14 yüksek çıktı. Fark çoğunlukla zayıf eksen moment kapasitesinden gelir.
- **Koruma:** ETABS oranı 1'i aşan grup otomatik olarak bir üst kesite çıkarılır. Seçilen kesit alanı daha büyük olan ve komşu kolonlar ile kiriş bağlantılarıyla geometri kısıtlarını sağlayan ilk kesittir. Tasarım yeniden analiz edilir ve doğrulanır; en fazla 3 adım yapılır.
  - Günlükte `Info: ETABS guard …` satırları görünür.
  - Excel'in *Design* sayfasında "Final design" sütunu korunmuş tasarımı gösterir.
- **Kalibrasyon:** günlükteki `ETABS / internal composite strength ratio (max)` satırı, iç çözücüyü ETABS ile uyumlu yapacak `CompositeStrengthFactor` değerini önerir (525M modelinde 1,08–1,12). Bu değeri ayar dosyasına yazarsanız arama ETABS ile uyumlu ve güvenli tarafta yürür; final koruması daha az devreye girer.

---

### 7.5 Dolgulu tüp kolonlar (`TubeSections.xml`)
Formda *Composite columns* + *Filled tube* seçildiğinde kolon gruplarının tasarım değişkeni, W listesi yerine bir **tüp kataloğundaki** kesittir. Katalog çelik alanına göre sıralanır. Ayarlar exe'nin yanındaki `TubeSections.xml` dosyasındadır.

**Katalog** (varsayılan ayarlarla 149 kesit):
- **Kütüphane kesitleri:** programın kesit kütüphanesindeki (`SectionPropertyDataPath`, ETABS 22'de `AISC16M.xml`) kare HSS kutular. Et kalınlığı kütüphanedeki tasarım kalınlığıdır (0,93 t).
  - `RectangularBoxes`: dikdörtgen kutular (varsayılan hayır).
  - `Pipes`: borular (varsayılan hayır). Moment çerçevesinde boruya kiriş bağlantısı (halka, diyafram) zordur ve ETABS 22.6 bazı borularda "Section is too slender" mesajı veriyor.
- **Yapma kutular** (`BuiltUpBoxes`): levhadan kaynaklı kare kutular. `BuiltUpMin`–`BuiltUpMax` arası, `BuiltUpStep` adımıyla (varsayılan 400–1000 mm, 50 mm adım), `BuiltUpThicknesses` levha kalınlıklarıyla (12, 15, 20, 25, 30, 35, 40, 50 mm).
  - Kütüphanedeki en büyük kare kutu 559 mm'dir. Çok katlı yapıların alt katları için yapma kutular gerekir.
- **`MinDimension`** (varsayılan 300 mm): en küçük dış boyut.
  - AISC 360-22 dolgulu kolon için bir alt sınır koymaz.
  - 300 mm, TBDY 2018 7.3.1.1'deki betonarme kolon alt sınırıdır. Ayrıca beton dökümü ve kiriş bağlantısı için uygulamada makul bir değerdir.
- AISC 360-22 Tablo I1.1a / I1.1b'deki λmax sınırını aşan kesitler katalogdan çıkarılır. Kompakt olmayan ve narin kesitler kalır; dayanımları I2.2 ve I3.4'e göre azaltılır.

**Malzemeler:**
- **Çelik:** modeldeki kolon çeliği (A992Fy50).
- **Dolgu betonu:** modelde tanımlı beton malzemesi. Birden fazla beton varsa ilki kullanılır; `ConcreteMaterial` ile başka biri seçilebilir.

**Kontroller** (iç çözücü, `CompositeColumn.vb` `FilledBox` / `FilledPipe`):
- I2.2: sınıf, Pno, EIeff (C3);
- I3.4: eğilme;
- I4: kesme; 360-22'de beton katkısı dahil;
- I5 / H1: etkileşim;
- en az %1 çelik oranı.

**Arama ve final:**
- Arama sırasında tüp kolonlar, dönüştürülmüş özellikli General kesit (`CFT_<ad>`) olarak analiz edilir.
- Finalde aynı adla gerçek **Filled Steel Tube / Pipe** kesitlerine çevrilir ve ETABS kompozit kolon tasarımıyla doğrulanır (bölüm 7.4).

**Maliyet:** çelik ağırlığı × *Steel* + beton hacmi × *Concrete*. Donatı ve kalıp yoktur.

**Arama sınırları:**
- Alt sınır kataloğun en küçük kesitidir.
- Üst sınır, ilk tasarımdaki W kesitinin Fy·As değerine en az eşit Pno'ya sahip ilk tüpten başlar; referanstaki gibi kayma payı eklenir.

**Deprem şartnamesi** (`SeismicDuctility`, varsayılan `High`): TBDY 2018 Tablo 9.3'ün "Kompozit Elemanlar" satırları uygulanır. Değerler AISC 341-10 Tablo D1.1'in "Composite Elements" satırlarıyla aynıdır.

| Düzey | Kutu cidarı b/t | Boru cidarı D/t | Varsayılan katalog (Fy = 345 MPa) |
|---|---|---|---|
| `High` (yüksek süneklik, λhd) | ≤ 1,4 √(E/Fy) = 33,7 | ≤ 0,076 E/Fy = 44,1 | 100 kesit (27 HSS + 73 yapma) |
| `Moderate` (sınırlı süneklik, λmd) | ≤ 2,26 √(E/Fy) = 54,4 | ≤ 0,15 E/Fy = 87,0 | 132 kesit |
| `None` (yalnızca AISC 360 λmax) | ≤ 5,0 √(E/Fy) | ≤ 0,31 E/Fy | 149 kesit |

- **b:** kütüphanedeki HSS kutularda B − 3t (köşe yarıçapı), yapma kutularda B − 2t.
- **Uygulanan değerler:** yüksek süneklikte 600 mm yapma kutu için levha en az 20 mm, 1000 mm için en az 30 mm.
- **Hangi düzey:** taşıyıcı sistemin süneklik düzeyi (moment çerçevesi, DTS) seçilen düzeyi belirler; kullanıcı bunu ayardan seçer.
- **Kapsam:** program yalnızca bu enkesit koşulunu uygular. AISC 341 / TBDY'nin diğer deprem koşulları (güçlü kolon–zayıf kiriş, bağlantı vb.) kontrol edilmez. ETABS'in kompozit kolon tasarımı da AISC 341'i kontrol etmez.

### 7.6 D/C oranı sınırı (ETABS ile tutarlılık)
- ETABS çelik çerçeve ve kompozit kolon tasarımında talep/kapasite oranını 1,0 ile değil, tasarım tercihlerindeki **D/C ratio limit** değeriyle karşılaştırır. Varsayılan 0,95'tir; *Design > ... Design Preferences* menüsünden değiştirilebilir.
- Program sınırı modelden okur (ya da ayar dosyasındaki `DesignRatioLimit` değerini modele yazar) ve bütün tasarım oranlarını bu sınıra böler. Programın raporladığı "oran" bu yüzden **oran / sınır** değeridir; 1 sınırdır.
- Böylece programın uygun bulduğu tasarım ETABS'te de uygun görünür. Sürüm 0.4.0'a kadar program 1,0 kullanıyordu: oranı 0,95–1,0 arasında kalan elemanlar ETABS'te "Combined D/C ratio exceeded" / aşırı gerilme olarak görünüyordu.
- Günlükte `Info: D/C ratio limits (...)` satırı kullanılan değerleri gösterir.

### 7.7 Hibrit kolonlar: kat kat çelik / kompozit
*Composite columns* işaretliyken *Hybrid: steel / composite per group* seçilirse her kolon grubu ayrı ayrı çelik (W) ya da kompozit (seçili tip: dolgulu tüp veya gömülü) olabilir.

**Grup tipleri** (tablo):
1. *Read column groups of the model* düğmesi, modeldeki kolon gruplarını (bütün elemanları düşey olan gruplar) gizli bir ETABS ile okur ve kat aralıklarıyla listeler. Model bir kopya üzerinde açılır; yaklaşık bir dakika sürer.
2. Her grup için *Type* seçilir:
   - *Optimize* (varsayılan): tipi optimizasyon belirler;
   - *Steel*: her zaman W profil;
   - *Composite*: her zaman kompozit.

   Tabloda olmayan kolon grupları *Optimize* sayılır.
3. Gruplamayı kullanıcı modelde yapar. Program modeldeki tasarım gruplarını olduğu gibi kullanır.

**Geçiş** (*Transition*):
- **Kolon yığını:** düşey olarak birbirine bağlanan kolon grupları. Program yığınları otomatik bulur ve alttan üste sıralar. 525M'de 2 yığın var: çevre (gruplar 5, 7, 9, 11, 13) ve orta (6, 8, 10, 12, 14).
- *Per stack (story)*: her yığın için bir **geçiş değişkeni** vardır. Yığının alttan o kadar *Optimize* grubu kompozit, üsttekiler çelik olur. Bu, "belirli kata kadar kompozit, üstü çelik" düzenidir; geçiş grup sınırlarında olur (525M'de 5, 10, 15 veya 20. kat).
- *Per group*: her *Optimize* grubunun kendi tip değişkeni (çelik / kompozit) vardır; her düzen mümkündür.
- Sabitlenmiş bir kompozit grup, aynı yığında sabitlenmiş bir çelik grubun üstündeyse günlüğe uyarı yazılır; karar kullanıcınındır.

**Değişkenler:**
- *Optimize* grubunun iki kesit değişkeni vardır: W kesiti ve kompozit kesit. Etkin tipe göre biri kullanılır; diğeri aramada bilgisini korur.
- 525M'de değişken sayısı 14'ten 26'ya çıkar (yığın bazında geçişte).

**Geçişte bağlantı:** çelik W kolon alttaki tüpün üzerine oturur. Kontrol: W derinliği ≤ tüp boyu H ve W başlık genişliği ≤ tüp genişliği B. Aynı tip gruplar arasında önceki kolon–kolon kuralları geçerlidir.

**Çıktı:**
- en iyi tasarımda her grubun kesiti ve tipi (`… [CFT …]`);
- her yığın için `Stack n (bottom to top …) = composite […], steel […]` satırı.

## 8. Koşuyu izleme, durdurma ve devam ettirme

### 8.1 İzleme
- Koşu arka planda çalışır. ETABS hesap yaparken de form yanıt verir ve alanlar canlı güncellenir. Koşu sırasında ayarlar kilitlenir.
- **Durum satırı** (ilerleme alanlarının altında) o anki aşamayı ve aşamada geçen süreyi gösterir. Aşamalar şunlardır:
  - *Starting ETABS*, *Opening the working copy*, *Reading the model*;
  - *Initial design with the auto select lists*, birkaç dakika sürebilir;
  - *Initial memory: design 3 / 100*;
  - *Search: loop 2, member 17 / 100*;
  - *Restarting ETABS (memory)*;
  - *Final analysis and ETABS check*, *ETABS composite column design …*, *ETABS guard step …*.

  Koşu bitince durum satırında *Finished*, *Stopped* veya *Failed (see ErrorLog.txt)* yazar.
- Ayrıntılı ilerleme `ErrorLog.txt` dosyasındadır (model klasöründe). Dosya koşu sürerken de okunabilir.

### 8.2 Durdurma
- Koşu sırasında **Start** düğmesi **Stop** olur. Stop, onaydan sonra koşuyu o anki değerlendirmenin sonunda durdurur: yedek yazılır, ETABS kapatılır ve koşuya devam edilebileceğini söyleyen bir mesaj çıkar.
- Koşu sürerken pencere kapatılırsa onay istenir; koşu aynı şekilde durdurulur ve program kapanır.
- Uzun ETABS aşamalarında (ilk sınır tasarımı, final kontrol) Stop ancak o aşama bitince etkili olur. Bu sırada durum satırında *Stopping after the current evaluation...* yazar.
- Programı Görev Yöneticisi'nden sonlandırmak ya da elektrik kesilmesi de yedeği bozmaz; son yedekten devam edilir.

### 8.3 Devam ettirme (*Load BackUp File*)
1. *Output File* alanında kesilen koşunun **aynı çıktı dosyasını** seçin. Yedek bu dosyanın yanında `<çıktı>.backup.xml` adıyla durur.
2. *Load BackUp File* kutusunu işaretleyip **Start**'a basın. Form ayarları yedekten okunur.
3. Devam etmeden önce bir **özet** gösterilir: model, kayıt zamanı, yöntem, tohum, analiz sayısı, en iyi maliyet. Yanlış dosya seçildiyse burada görülür; **No** ile vazgeçin.
4. Program yeni bir çalışma kopyası açar, ilk sınır tasarımını yeniden yapar ve arama belleğiyle devam eder. Sonuç önbelleği (`<çıktı>.cache.txt`) de okunur; analiz edilmiş tasarımlar tekrar analiz edilmez.

Yedeğin yazılması:
- Yedek her döngü sonunda ve döngü içinde 10 dakikada bir yazılır. Kesintide en çok son 10 dakikanın işi kaybolur; yarıda kalan döngü baştan yapılır.
- Yedek önce geçici dosyaya yazılıp sonra eskisinin yerine geçer. Önceki yedek `.backup.xml.bak` olarak saklanır.
- Yeni (yedeksiz) bir koşu, aynı çıktı dosyasına ait eski yedeği ve önbelleği siler.
- Rastgele sayı üretecinin durumu kaydedilemez: devam eden koşu, kesintisiz bir koşuyla birebir aynı sonucu vermez.

Yedek denetimleri:
| Durum | Program ne yapar |
|---|---|
| Son yedek bozuk veya tutarsız (bellek boş, değişken sayısı farklı, kesit numarası kütüphane dışında) | `.bak` denenir. İkisi de kullanılamazsa iki dosyanın hatasını gösteren bir mesaj çıkar ve koşu başlamaz. |
| Formda başka bir model seçili | Yedeğin modeli gösterilir ve o modelle devam edilip edilmeyeceği sorulur. |
| Model yedekten sonra değiştirilmiş (SHA-256 özeti farklı) | Uyarı çıkar: yedekteki tasarımlar değişen modele uymayabilir; devam kararı kullanıcıya bırakılır. |
| Tasarım grupları veya kesit kütüphanesi farklı (ETABS modeli okuduktan sonra) | Koşu açık bir mesajla durur. |
| Yedek başlangıç belleği sırasında yazılmış (eksik bellek) | Eksik tasarımlar üretilir ve yöntem durumu kurulur. |

### 8.4 Toplu koşu (`tools\RunBatch.ps1`)
Yöntem karşılaştırması gibi çok sayıda uzun koşuyu form açmadan, aynı anda birkaçı çalışacak biçimde yürütür. Koşu listesini (CSV) elle yazarsınız.

**Program tarafı:** `FrameSap2000.exe /batch <ayar.xml> [/resume]`
- `<ayar.xml>` formun ayarlarıdır (yedek ve sonuç dosyalarındaki `FormInfo_` biçimi); şablon `toolsatch_template.xml`. Betik bunu sizin için yazar.
- Mesaj kutusu açılmaz. Mesajlar `<ayar>.batch.log` dosyasına yazılır, sorular *Yes / OK* ile cevaplanır. Gizli ETABS'in soru kutuları otomatik cevaplanır (günlükte `Warning: ETABS dialog answered ...`).
- Koşu bitince program kapanır; koşu başarısızsa çıkış kodu 1 olur. `/resume` koşuya yedekten devam eder.

**Çalıştırma**
```
powershell -ExecutionPolicy Bypass -File tools\RunBatch.ps1 -Runs D:\Kosular\liste.csv -Out D:\Kosular\Sonuc -Exe D:\Kosularin\FrameSap2000.exe -Parallel 3 -StartDelay 180
```
| Seçenek | Anlamı |
|---|---|
| `-Parallel N` | sistemde aynı anda en çok N toplu koşu (varsayılan 3); başka pencerelerdeki toplu koşular da sayılır, elle açılan form sayılmaz |
| `-StartDelay s` | koşuların başlangıç aralığı (varsayılan 120 s) |
| `-Status` | koşmadan durumu yazar: durum (`başlamadı`, `çalışıyor`, `bitti`, `uygun tasarım yok`, `hata`, `durdu/çöktü`), analiz sayısı, en iyi maliyet; çalışan koşuları rahatsız etmez |
| `-Stop` | `-Out` klasöründeki çalışan koşuları ve kendi ETABS örneklerini kapatır (betiğin çalıştığı pencereyi de kapatın) |
| `-Resume` | sonucu olmayan koşuları yedekten devam ettirir |
| `-NoRetry` | çöken koşuyu yeniden başlatmaz |
| `-SummaryOnly` | koşmaz, yalnızca `summary.csv` yazar |

**Liste sütunları** (ilk 7 zorunlu)

| Sütun | Anlamı |
|---|---|
| Name | koşu adı (klasör ve dosya adı); yinelenmemeli |
| Model | model dosyası; göreli yol listenin klasörüne göre |
| Method | `OptMethod_` adı: `SocialSpider`, `HarmornySearch`, `ArtificialBeeColony`, … |
| Mode | `Steel` / `Composite` / `Hybrid` |
| MaxAnalyses, MemorySize, Seed | analiz bütçesi, popülasyon, tohum |
| Transition | hibritte `PerStack` / `PerGroup` (isteğe bağlı) |
| AbcLimit | ABC terk sınırı (isteğe bağlı) |
| Config | bu koşu için `App.config` değerleri: `Anahtar=değer;Anahtar2=değer2`, örn. `SeismicDriftAmplification=5.5;UpperBoundMultiplier=1`. Doluysa koşuya kendi exe kopyası verilir (`<Out>\<Name>in`); boşsa `-Exe` klasörü ortak kullanılır |

- Her koşu `<Out>\<Name>` klasöründe modelin **kopyasıyla** çalışır; orijinal model değişmez. `ErrorLog.txt`, sonuç, Excel ve `_best.EDB` dosyaları da bu klasöre yazılır. Betiğin kendi günlüğü `<Out>unner.log`.
- **Yeniden başlatma:** koşu normal biter ve "uygun tasarım bulunamadı" derse (sonuç dosyası olmaz) yeniden başlatılmaz; durumu `-Status` gösterir. Yalnızca çöken koşu (günlüğün sonunda `Finished` / `Failed` yok) bir kez yedekten devam eder.
- Sonucu olan koşular atlanır; liste sonradan uzatılıp betik yeniden çalıştırılabilir.
- Sonunda `<Out>\summary.csv` yazılır: durum, analiz sayısı, en iyi maliyet ve cezası, final analizdeki maliyet ve ceza, final durumu, süre (saat) ve hibrit yığınların geçişi.
- **Örnek modeller:** tablodan toplu model üretmek için ayrı program: `ModelBuilder` (kılavuz: `ModelBuilder\README.md`). Ürettiği `runs_template.csv` bu listenin biçimindedir.
- **Yol uzunluğu:** program 260 karakteri aşan yollara yazamaz. Betik, en uzun dosya yolu 230 karakteri aşarsa durur; `-Out` için kısa bir klasör seçin.
- Koşular sürerken aynı exe yeniden derlenemez. Bu nedenle exe'yi (`bin\Release` içeriğini) koşu klasörüne kopyalayıp `-Exe` ile o kopyayı verin.

---

## 9. Çıktılar ve sonuçların yorumlanması

### 9.1 Dosyalar
| Dosya | İçerik |
|---|---|
| `<çıktı>.xlsx` | Sonucun Excel kitabı (Excel kurulu olmasa da yazılır), bkz. 9.2. Dosya Excel'de açıksa `<çıktı>_<tarih>.xlsx` adıyla yazılır. |
| `<çıktı>.check.xlsx` | Check Structure kitabı: özet, maliyet, grup oranları, ötelemeler, ETABS kompozit kontrolü |
| `<çıktı>.xml` | Sonuç (makine okunur). Kitaptaki tüm bilgiler ve `Seed`, `GlobalBest`, `FinalCheck`, `FinalConstraints`, `CostBreakdown`, `FinalDesignPrint`, `ETABSCompositeCheck`, `FormInfo` (koşu ayarları). |
| `<model>_best.EDB` | En iyi (final) tasarımın ETABS modeli, orijinal modelin klasöründe |
| `ErrorLog.txt` | Model klasöründe; o klasördeki bütün koşular aynı dosyaya eklenir. Her koşu `Info: ======== new run <tarih>, program <sürüm>, model …, output … ========` satırıyla başlar. Satırlar `Info:`, `Warning:` veya `Error:` ile başlar. Koşu sonunda `Info: timing …` satırında süreler ve önbellek isabetleri, ABC'de kâşif arı sayısı yazar. Sorun bildirirken bu dosyayı ekleyin. |
| `<çıktı>.backup.xml` (+ `.bak`) | Kesilen koşuya devam için yedek |
| `<çıktı>.cache.txt` | Sonuç önbelleği; devam eden koşu okur, yeni koşu siler |

### 9.2 Excel kitabı (`<çıktı>.xlsx`)
| Sayfa | İçerik |
|---|---|
| *Summary* | Model, yöntem, tohum, bellek boyutu, analiz sayısı, kodlar, öteleme sınırları, birim maliyetler; aramanın en iyisi ile finalin maliyeti ve cezası; **"Final design satisfies all checks"** |
| *Cost* | Grup başına kesit, tür (çelik / kompozit), üye sayısı, uzunluk, çelik ve donatı ağırlığı, beton hacmi, kalıp alanı, kalem maliyetleri, toplam ve toplam içindeki pay; son satır toplam |
| *Design* | Grup başına final tasarımın ve aramanın en iyisinin kesitleri |
| *Constraints* | Final tasarımın belirleyici kısıtları (oran / sınır; 1'e yakın değerler kısıtın sınırda olduğunu gösterir) |
| *ETABS composite* | Kompozit gruplarda ETABS PMM ve kesme oranı, iç çözücü oranları, ETABS mesajı |
| *History* | İyileşme geçmişi (analiz, döngü, en iyi cezalı maliyet, ceza); yakınsama grafiği için |

Formdaki **Excel** düğmesi, seçili çıktı dosyasından kitabı yeniden oluşturur. Eski sürümlerin çıktılarında maliyet dökümü bulunmadığı için *Cost* sayfası boş kalır.

### 9.3 Sonuçların yorumlanması
- **Arama sonucu ve final farkı:**
  - "Best design of the search" aramada bulunan en iyi tasarımdır; maliyeti düzeltmeli değerlendirmeden gelir.
  - "Final design" bu tasarımın tüm durumlarla ve düzeltmesiz analizidir; ETABS koruması kesit değiştirdiyse o tasarımdır.
  - Raporlamada **final tasarımı** kullanın.
- **"Final design satisfies all checks" = NO** iki durumda çıkar:
  - final analizinde ceza > 0 (bir kısıt aşılıyor), ya da
  - ETABS kompozit kontrolünde bir grup 1'i aşıyor ve koruma 3 adımda çözemiyor.

  Nedeni *Constraints* ve *ETABS composite* sayfalarında, ayrıntısı `ErrorLog.txt` dosyasında görülür. Çözüm için `CompositeStrengthFactor` değerini önerilen değere ayarlayın veya daha uzun bir koşu yapın.
- **Belirleyici kısıtlar:** *Constraints* sayfasında 1'e yakın değerler tasarımı belirleyen kısıtlardır. Öteleme belirleyiciyse kolon ve kiriş rijitliği, dayanım oranı belirleyiciyse kesit dayanımı sınırdadır.
- **Yakınsama:** *History* sayfasından maliyetin analiz sayısıyla değişimi çizilebilir. Son analizlerde iyileşme yoksa arama yakınsamıştır. Hâlâ iyileşiyorsa daha fazla analiz faydalı olabilir.
- **Doğrulama:** `<model>_best.EDB` dosyasını ETABS'te açın. Çelik tasarımını ve (kompozit modda) kompozit kolon tasarımını ETABS'te görebilirsiniz.

---

## 10. Check Structure: bir tasarımı kontrol etme
Bir çıktı dosyasındaki tasarımı, aynı veya değiştirilmiş bir modelde düzeltme yapmadan kontrol eder.
1. *ETABS Model*: kontrol edilecek modeli seçin.
2. *Output File*: kesitleri okunacak sonuç dosyasını (`*.xml`) seçin.
3. *Check Structure Only* kutusunu işaretleyip **Start**'a basın.
4. Program kesitleri grup adlarına göre eşler, analiz ve tasarım yapar. Kompozit modda ETABS kompozit kolon doğrulaması da yapılır.
5. Sonuçlar `<çıktı>.check.xml` ve `<çıktı>.check.xlsx` dosyalarına yazılır. Tasarım kontrolleri sağlamıyorsa mesaj kutusu cezayı ve en büyük ETABS oranını gösterir.

---

## 11. Sık karşılaşılan durumlar
| Mesaj veya durum | Neden / çözüm |
|---|---|
| *"Windows bilgisayarınızı korudu"* | Program imzalı değil: *Ek bilgi > Yine de çalıştır* (2.3). |
| Program açılmıyor veya hemen kapanıyor | .NET Framework 4.7.2 kurulu mu kontrol edin. Zip'ten çıkarmadan önce *Engellemeyi kaldır* yapıldı mı (2.3)? |
| Mesaj: *Unhandled exception: … Could not load file or assembly 'ETABSv1'* | `ETABSv1.dll` veya `Microsoft.Win32.Registry.dll` exe'nin yanında değil (2.2). |
| `Cannot start a new instance of the program` / `Problem occurred on :ApplicationStart` | ETABS açılamadı: ETABS'i elle açıp lisansı kontrol edin; ayardaki ETABS yolunu kontrol edin. |
| `Warning: …EncasedSections.xml not found …` | `EncasedSections.xml` exe'nin yanında değil; kompozit ayarları varsayılan alındı (2.2). |
| `Info: ======== new run …` | Yeni bir koşunun başı; sürüm, model ve çıktı dosyası bilgisi |
| `Info: ETABS 22.x.x (…)` | Bağlanılan ETABS sürümü ve yolu |
| `ETABS program not found` / `Section property file not found` | Ayar dosyasındaki yolları düzeltin (bölüm 2.4). |
| `Warning: 'ETABSProgramPath' not found …, using …` | Ayardaki yol bulunamadı; kurulu en yeni ETABS kullanıldı. Ayarı düzeltin. |
| `DesignSteel.SetCode, code not available` | ETABS API'sinin desteklediği bir kod seçin (ETABS 22: AISC 360-22; ETABS 19: AISC 360-10). |
| `No strength design combination` | Kombinasyon tanımlayın veya *Create default design combos* seçeneğini açın. |
| `No lateral (wind/earthquake) combination` | Yük desenlerinin tipini Wind / Quake yapın veya öteleme modunu değiştirin. |
| `Warning: analysis not finished …` | Aday tasarım kararsız (ör. P-Delta yakınsamadı); ceza alır ve arama devam eder. Hata değildir. |
| `Warning: seismic drift cases … use elastic displacements` | `SeismicDriftAmplification` ayarlanmamış (1,0). Deprem ötelemesi kontrol ediliyorsa Cd/Ie veya R/I girin. |
| `Warning: ETABS / internal composite strength ratio (max) …` | İç kompozit çözücü ETABS'ten iyimser. Önerilen `CompositeStrengthFactor` değerini ayar dosyasına yazın (bölüm 7.4). |
| `Info: ETABS guard …` | Final tasarımda ETABS'te aşan kolon büyütüldü (bölüm 7.4). |
| `Info: steel design strength combinations selected again` | ETABS yeniden açılan modelde kombinasyon seçimini silmişti; program yeniden seçti. Bilgi amaçlıdır. |
| `Info: ETABS restart …` | Bellek için ETABS yeniden başlatıldı (bölüm 5.3). |
| `Info: ABC scout bees …` | ABC'de terk edilip yeniden üretilen kaynak sayısı. 0 ise terk sınırı bu koşu için büyük (bkz. 6.1). |
| `PropMaterial.GetOConcrete …` | `EncasedSections.xml` içindeki malzeme adı modelde yok. |
| `Design section … is not a W section` | Otomatik listede kütüphane dışı kesit var; o grup için orta kesit kullanılır. |
| `Warning: Excel workbook not written …` | Excel dosyası yazılamadı (ör. klasör yolu çok uzun veya `DocumentFormat.OpenXml.dll` eksik, bkz. 2.2). Sonuç XML'i yazılmıştır; formdaki **Excel** düğmesiyle tekrar deneyin. |
| Mesaj: *The backup cannot be used …* | Yedek ve `.bak` bozuk; yeni bir koşu başlatın. |
| Mesaj: *The backup belongs to another model …* | Formda başka bir model seçili. Yedeğin modeliyle devam etmek için **Yes**, vazgeçmek için **No**. |
| Mesaj: *The model was changed after the backup was written …* | Model yedekten sonra değişmiş. Değişiklik tasarım gruplarını etkiliyorsa **No** ile yeni koşu başlatın. |
| Mesaj: *The run cannot be continued: the design groups … differ* | Yedek bu modelin değil veya gruplar değişmiş; yeni bir koşu başlatın. |
| Form güncellenmiyor gibi görünüyor | Durum satırına bakın: uzun bir ETABS aşamasında (ilk sınır tasarımı, final) süre ilerler, sayılar ancak aşama bitince değişir. |
| Koşu bitti ama ETABS hâlâ açık | Normalde program kendi açtığı ETABS'i kapatır; kapanmazsa 60 s sonra sonlandırır. Sizin açtığınız ETABS'e dokunulmaz. |

---

## Ek A. Geliştiriciler için: derleme ve dağıtım paketi
**Derleme**
1. `SteelFrameWithCompositeTubeColumnsETABS.sln` dosyasını Visual Studio'da (2022 veya sonrası; Visual Studio 18 ile denendi) açın.
2. NuGet paketi `DocumentFormat.OpenXml` 2.18 ilk derlemede otomatik indirilir. İnmezse: *Tools > NuGet Package Manager > Restore*.
3. ETABS API'si (`ETABSv1.dll`) derleme sırasında kurulu ETABS'ten alınır: ETABS 22 varsa onun, yoksa ETABS 19'unki. Program derlendiği sürümün ETABS'ine bağlanır. ETABS 22'de kaydedilen bir model ETABS 19'da açılmaz.
4. Dağıtım için *Release* yapılandırmasıyla derleyin. Çıktı klasörü `bin\Release\` olur.
   - Üst araç çubuğunda yeşil **Start** düğmesinin solundaki açılır kutuda **Release** seçin. Kutu görünmüyorsa: *Build > Configuration Manager… > Active solution configuration: Release*.
   - Ardından *Build > Rebuild Solution*.
   - "Rebuild" her zaman o anda seçili yapılandırmayı derler: kutuda *Debug* seçiliyken yalnızca `bin\Debug\` güncellenir, `bin\Release\` boş kalır.
   - Debug derlemesi de çalışır. Ancak `bin\Debug\` klasöründe eski dosyalar birikmiş olabilir; paket için yalnızca aşağıdaki altı dosyayı alın.

**Dağıtım paketi**
1. Boş bir klasör oluşturun, ör. `SteelOpt_2026.10.3`.
2. `bin\Release\` klasöründen şu altı dosyayı bu klasöre kopyalayın:
   - `FrameSap2000.exe`
   - `FrameSap2000.exe.config`
   - `EncasedSections.xml`
   - `ETABSv1.dll`
   - `Microsoft.Win32.Registry.dll`
   - `DocumentFormat.OpenXml.dll`

   `.pdb` ve OpenXml `.xml` dosyaları gerekmez.
3. `LICENSE` dosyasını ve kullanım kılavuzunu klasöre ekleyin. Kılavuzun tarayıcıda açılan HTML hâli için:
   `python tools\md2html.py KULLANIM_KILAVUZU.md KULLANIM_KILAVUZU.html "Kullanım Kılavuzu"`.
   PDF isterseniz HTML'yi tarayıcıda açıp *Yazdır > PDF olarak kaydet* seçin.
4. Klasörü zip'leyip paylaşın. Alıcı bölüm 2.3'teki adımları izler (*Engellemeyi kaldır*, kısa klasör yolu).
5. Paylaşmadan önce paketi başka bir klasöre çıkarıp programı açarak deneyin. Mümkünse kısa bir deneme koşusu yapın.

Sürüm numarası `My Project\AssemblyInfo.vb` dosyasındadır (`AssemblyVersion`); yeni bir paket hazırlarken güncelleyin.
