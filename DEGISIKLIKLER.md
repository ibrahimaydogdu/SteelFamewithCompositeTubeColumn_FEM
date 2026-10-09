# Değişiklik Kaydı

Her iş "Aşama N" başlığıyla ve test sonuçlarıyla birlikte buraya yazılır. En yeni aşama en üstte durur.

---

## 2026-10-07 — Aşama 9: Model oluşturucu (`ModelBuilder`, ayrı program) ve iyileştirici düzeltmeleri (0.5.1)

**Kullanıcı kararları** (öneri: `Ajan/Gorevler/ASAMA9_ONERI.md`, Bölüm 11)
- ASCE 7-22; çevre çerçevesi varsayılan, uzay çerçeve seçenek; çerçeve sınıfı parametre (varsayılan SMF); tepki spektrumu; rijit diyafram (döşeme modellenmez); Direct Analysis Method; parametreler CSV'den; aynı depoda ikinci proje; derleme ve duman testi bende, esas test kullanıcıda.
- Kapsam dışı (ileride eklenebilir): çaprazlı sistemler, düzensiz plan ve geri çekmeler, kar birikmesi, zemin–yapı etkileşimi.
- Aşama 8 (kompozit döşeme) atlandı: proje sonunda ayrı program önerisi.

**Bulgular (ETABS 22.6 API)**
- **Otomatik yükler (2026-10-08 düzeltmesiyle):** ASCE 7-16 otomatik deprem API ile **kurulabiliyor**: `LoadPatterns.AutoSeismic.SetASCE716_1` çalışıyor (kod 0, desen "ASCE 7-16" olarak tanınıyor); eski `SetASCE716` 30 parametre varyantında da hata veriyor. **ASCE 7-22 için API metodu yok.** Rüzgâr için `cAutoWind` arayüzü boş: hiçbir yönetmelikte API ile otomatik rüzgâr kurulamıyor. Tablolarla da olmuyor (`AutoLoad` alanı içe aktarılamıyor). ASCE 7-22 tepki spektrumu fonksiyonu ve durumları tablo ile kuruluyor; rüzgâr ve ELF programda hesaplanıyor.
- Boş ETABS modelinde `A992Fy50` (Fy 344,7 MPa, E 199948 MPa), `4000Psi`, `A615Gr60`, `A416Gr270` malzemeleri ve `D1` rijit diyaframı zaten var. Yeniden tanımlamak `A992Fy50-1` yinelemesi ve `SetDiaphragm` hatası veriyor; var olanlar kullanılıyor.
- `AreaObj.AddByCoord` ile `PropName = "None"` boş (null) döşeme alanı üretiyor.
- `PropFrame.Count / GetNameList` içe aktarılan kesitleri 0 gösteriyor (API tuhaflığı); kesitler elemanlardan doğrulandı.

**9.1 — iskelet, geometri, havuzlar, gruplama**
- `ModelBuilder/` (VB.NET, .NET Framework 4.7.2, çözüme ikinci proje olarak eklendi):
  - `BuildParams.vb`: bütün parametreler ve varsayılanları, CSV okuma (`,` ya da `;` ayırıcı, `;` ile ondalık virgül, bilinmeyen sütun ve geçersiz değer hatası, yinelenen ad), `/template` ile şablon CSV;
  - `BuildPlan.vb`: ETABS'siz geometri ve gruplama planı;
  - `SectionPools.vb`: AISC16M kütüphanesinden W kesitler, kiriş ve kolon havuzları, AISC 341-22 Tablo D1.1 süneklik süzgeci;
  - `EtabsBuilder.vb`: yeni ETABS örneği (kullanıcının oturumuna dokunmaz), katlar, malzemeler, kesit içe aktarma, `BeamSectionList` / `ColumnSectionList`, elemanlar ve gruplar, mafsallı kirişler, mesnetler, rijit diyafram, boş döşeme alanları, kayıt;
  - `Program.vb`: `/template`, `/plan`, `/build ... /out ... [/hide] [/overwrite] [/only ad]`.
- **Gruplama:** kolonlar köşe / kenar / iç × kat bandı; kirişler kenar / iç × açıklık uzunluğu × kat bandı. Adlar `COL-CORNER-S01-03`, `BM-EDGE-L9000-S01-03`.
- **Çevre çerçevesi:** kenar kirişleri moment aktarır, iç kirişlerin iki ucunda M2 ve M3 mafsallı (kolonlar sürekli). İç kolon tabanı sabit ya da mafsallı (parametre).
- **Süneklik süzgeci:** kütüphanede k boyutu yok; gövde için k = 1,8 tf yaklaşımı kullanılıyor (W14x90 ve W36x135'te ±%5). Yüksek süneklikte havuzlar: kiriş 168, kolon 46 kesit (289 W kesitten).

**Testler**
- Derleme (MSBuild Release): hata yok.
- ETABS'siz: şablon, hata denetimi (bilinmeyen sütun, kötü değer), `;` ayırıcılı CSV. 15 katlı 3×3 açıklıklı örnekte plan elle doğrulandı: 240 kolon, 360 kiriş (180 moment, 180 mafsallı), 135 alan, 15 kolon + 20 kiriş grubu; kenar/iç kiriş sayıları hesapla tuttu.
- ETABS duman testi (gizli ETABS; modeller bağımsız bir denetim programıyla yeniden açıldı):

  | Örnek | Çerçeve | Eleman | Alan | Grup | Mafsallı kiriş | Mesnet | Diyaframlı düğüm |
  |---|---|---|---|---|---|---|---|
  | S3 (3 kat, 2×2 açıklık) | uzay | 63 | 12 | 15 | 0 | 9 sabit | 27 |
  | P4 (4 kat, 6 / 5 m) | çevre | 84 | 16 | 14 | 16 | 8 sabit + 1 mafsallı | 36 |

  - Grup içi eleman sayıları plan ile aynı; bütün elemanlar *Steel Frame Design* prosedüründe; kesitler (W310X313, W840X226) kütüphane ölçüleri ve A992Fy50 ile doğru; otomatik kesit listeleri 168 ve 46 kesit.
- Bulunan sorunlar: `Exists`, `Lib`, `ON`, `Short`, `Dir`, `T`/`Row` VB adları; sınıf alanı `R` ile döngü değişkeni `r` çakışması; Git Bash `/plan` gibi argümanları yola çeviriyor (`MSYS_NO_PATHCONV=1` gerekli); yol uzunluğu denetimi (çıktı klasörü en çok 150 karakter).

**9.2 — yükler**
- `Loads.vb`: desenler `Dead` (öz ağırlık), `SDL`, `Live`, `RoofLive`, `Snow`, `PartMass`, `WX`, `WY`.
  - Düşey yükler **kirişlere** iki yönlü 45° kuralıyla üçgen / yamuk yayılı yük olarak veriliyor. ETABS'in boş alan yükü (`SetLoadUniform`) çerçeveye aktarılmıyor ve `SetLoadUniformToFrame` bu sürümde -100 veriyor; bu yüzden boş alan nesneleri de kaldırıldı.
  - Rüzgâr: ASCE 7-22 Bölüm 26–27 Directional Procedure (qz, Kz Tablo 26.10-1, Cp Tablo 27.3-1, asgari 16 psf), kat düğümlerine tributary genişlikle kuvvet.
  - Kütle kaynağı: öz kütle + `SDL` + `PartMass` (+ pf > 1,44 kPa ise karın %20'si). Modal durum.
  - ASCE 7-22 tepki spektrumu fonksiyonu ve `RSX` / `RSY` durumları (tablo içe aktarma; tablo yazımı oku–değiştir–yaz: içe aktarma, tabloda olmayan satırları siliyor).
- **Doğrulama (analiz, S3 örneği):** düşey reaksiyonlar elle hesapla aynı: `SDL` 2390,4 kN (= 4,2 kPa × 432 m² + 4 kN/m × 144 m), `Live` 898,6, `RoofLive` 138,2, `PartMass` 138,2 kN; `WX` = `WY` = −178,4 kN. Bu doğrulama bir hatayı yakaladı (Y yönü rüzgâr toplamı VB'de döngü içi `Dim` sıfırlanmadığı için 357 kN yazılıyordu).

**9.3 — kombinasyonlar, tasarım tercihleri, kontrol analizi**
- `Combos.vb`: ASCE 7-22 2.3.1 ve 2.3.6, **35 kombinasyon** (kar varsa 53); AISC 360-22 çelik ve kompozit kolon tasarımı için hepsi işaretli. Tercihler: çerçeve tipi, SDC, Ie, ρ, SDS, R, Ω0, Cd, LRFD, Direct Analysis. Bağımsız denetim: katsayılar (ör. `C6_RSX+0.3RSY` = 1,4 D + 1,0 RSX + 0,3 RSY + 0,5 Live; `C7_…` 0,7 D), 35 / 35 seçili, kod `AISC 360-22`, model kilitsiz.
- `Audit.vb`: geçici klasörde kontrol analizi (çıktı klasöründe analiz dosyası kalmaz); mod sayısı `max(Modes, kat)` ile başlar, kütle katılımı < %90 ise iki katına çıkar (en çok 3 × kat ve 60); ELF taban kesmesi sayısal (Ta = 0,0724 h^0,8, Cu, Cs sınırları); RS durumları ≥ `SpectrumScaleMin × V`'ye ölçeklenir. S3 elle kontrol: W = 3306 kN, Cs = 0,125, V = 413 kN; ölçeklemeden sonra RS kesmesi 413 kN.
- **Ön boyutlandırma (`PreSize`).** Yer tutucu kesitlerle 15 katlı örnek çok esnek (T1 = 4,5 s) ve ölçek çarpanı 2,0 / 2,9 çıkıyordu. Tek göreli havuz indeksi ikiye bölme ile T ≤ Cu·Ta için aranıyor; çarpan 1,33 / 1,54'e indi. Sonra kesitler tarafsız medyana geri alınıyor. Çarpan sabit bir sayıdır; tasarıma bağlı ölçekleme iyileştiricide yok (Aşama 10 önerisi).

**9.4 — arayüz ve çıktılar**
- Form (argümansız açılır): CSV ve çıktı klasörü seçimi, plan, kurulum, durdurma, şablon CSV. Görsel olarak denetlendi.
- `examples_summary.csv` ve optimizasyon programının toplu koşu listesi biçiminde `runs_template.csv`; her örnek için `<ad>_report.txt`.
- `ModelBuilder/README.md` (Türkçe kılavuz), `ModelBuilder/Ornek_Liste.csv` (5 örnek: 5, 10, 15, 20 kat, uzay çerçeve).
- Süreler (gizli ETABS, 8 çekirdek): 3 ve 4 katlı örnek yaklaşık 3,5 dk, 15 katlı örnek yaklaşık 3 dk (ön boyutlandırma erken çıkışla).

**İyileştiricide bulunan ve düzeltilen hatalar (0.5.1)**
1. **`PropFrame.GetNameList(N, Adlar)` türsüz çağrıda her zaman 0 döndürüyor.** İyileştirici "modelde olan kesitleri atla" denetimini bu yüzden hiç yapmıyordu. 525M'de çakışma yoktu (modelin kesit adları W14X…, kütüphanenin metrik adları W360X…). ModelBuilder modellerinde havuz kesitleri zaten tanımlı olduğundan `ImportProp` "W250X25.3" için hata verip koşuyu başlatmadan bitiriyordu. Düzeltme: adlar `I` türüyle sorgulanıyor ve `ImportProp` hata verirse `GetISection` ile varlık doğrulanıp atlanıyor. Günlükte "Info: N of 289 library sections are already defined in the model".
2. **ETABS'in gizli soru kutusu koşuyu sonsuza dek bekletiyordu.** Üretilen modelde ilk tasarım sırasında "6 Steel frames with auto select sections failed stress/capacity check. Do you want to select them?" kutusu açıldı; ETABS gizli olduğu için kutu görünmüyor, koşu 70 dakika "takılı" kaldı (CPU 0). Yeni `DialogGuard.vb`: gizli ETABS örneğinde görünür kutuları izler; Yes/No sorusuna **No**, tek OK'li iletiye **OK** der, diğerlerini yalnızca günlüğe yazar. Test: aynı model, yeni derleme; günlük: "Warning: ETABS dialog answered No: …", koşu 6 dakikada aramaya geçti.
   - Kutunun nedeni bulundu (aşağıda, madde 3).
3. **ETABS AISC 341 güçlü kolon–zayıf kiriş (SCWB) denetimi aramayı çözümsüz yapıyordu.** ModelBuilder tercihleri `SMF` + SDC D yazınca ve modelde depremli kombinasyonlar olunca ETABS, kenar kolon gruplarında "Beam/Column capacity ratio exceeds limit" hatası verdi (6 eleman: `COL-EDGE-S01-02`, `COL-EDGE-S03-04`). Optimizasyon programı hata veren grubu `1 + hata oranı` ile cezalandırıyor (ratio 2,000); denetim geçti/kaldı olduğundan arama yönlenemedi: 4 katlı örnekte 183 analizde uygun tasarım bulunamadı (5 grup en ağır kesit W920X1377'ye dayandı). 525M modeli aynı varsayılanları taşıyor ama depremli kombinasyonu olmadığı için sorun çıkmamıştı.
   - Çözüm: ModelBuilder'a `SeismicProvisions` parametresi (varsayılan **No**): ETABS "Ignore Seismic Code" = Yes, yani AISC 341 hükümleri (SCWB vb.) kapalı; AISC 360 dayanımı, öteleme ve ASCE 7-22 yükleri aynen geçerli, AISC 341 kompaktlık süzgeci havuzlarda kalıyor. `Yes` ile açılabilir. Doğrulama: aynı model, kapalıyken probe tasarımında soru kutusu çıkmadı, hata mesajı yok, en yüksek oran 0,938 (C6 depremli kombinasyon).
   - **Karar gerektirir:** SCWB'yi sürekli bir kısıt olarak (ETABS'in kolon/kiriş kapasite oranını okuyarak) iyileştiriciye eklemek ayrı bir geliştirme.
4. **Üst arama sınırı öteleme belirleyici olduğunda yetmiyor.** SCWB kapalıyken aynı örnekte (4 katlı, Cd/Ie = 5,5) 159 analizde yine uygun tasarım çıkmadı. En iyi tasarımın tek ihlali göreli kat ötelemesiydi (sınırın 1,12 katı; dayanım oranı 0,58). **Bütün değişkenler üst sınırdayken** de tasarım uygunsuzdu (öteleme 1,11; dayanım 0,38): sınır, ETABS'in *dayanıma göre* yaptığı ilk tasarımın kesiti + 0,23·(N−1) sıra olarak belirleniyor ve öteleme hiç hesaba katılmıyor, yani problem sınırlar yüzünden çözümsüz.
   - Düzeltme: `App.config` anahtarı `UpperBoundMultiplier` (varsayılan **0,23** = eski davranış, regresyon etkilenmez). Öteleme belirleyiciyse (deprem örnekleri) `1` yapılırsa bütün kesit listesi açılır.
   - **Sonuç (aynı 4 katlı örnek, `UpperBoundMultiplier = 1`, SSO, popülasyon 20, 156 analiz): yine uygun tasarım bulunamadı.** Problemin çözülebildiği ayrıca doğrulandı: bütün gruplar en ağır kesitteyken (W920X1377) tasarım uygun (öteleme 0,26, dayanım 0,11, maliyet 5573); üst sınır tasarımı ise maliyet 640'ta öteleme 1,11. Uygun bölge bu ikisinin arasında; kısa bütçe (yaklaşık 7 döngü) geniş aralıkta ona ulaşamadı. 500 analizlik koşu bu gözleme göre denenecek.
   - **500 analizlik doğrulama koşusu (4 katlı P4, `UpperBoundMultiplier = 1`, `SeismicDriftAmplification = 5,5`, SSO, popülasyon 20, tohum 1): başarılı.** 522 analiz, 1 sa 59 dk (analiz başına ~14 s). İlk uygun tasarım aramanın ilk yarısında bulundu; en iyi maliyet **2587,64**, ceza 0, son ETABS kontrolü geçti (`FinalFails = false`). Kısıtlar: kat ötelemesi/sınır 0,993 (belirleyici), tepe ötelemesi 0,878, dayanım oranı 0,205, geometrik 1,000 / 0,997. Çözüm 640 (uygunsuz) ile 5573 (hepsi W920X1377) arasında.
   - Sonuç: ModelBuilder örneği optimizasyon programıyla uçtan uca çalışıyor (`SeismicProvisions = No`, `UpperBoundMultiplier = 1`, `SeismicDriftAmplification = Cd/Ie` ile).
   - Süre notu: 4 katlı örnekte 500 analiz ≈ 2 sa; 15 katlı örnekte analiz başına süre çok daha uzun olacağından (525M'de ~35 s) bir koşu saatler sürer.
   - **Üst sınır otomatik genişletme** (`WidenBoundsForDrift`, kullanıcı kararı: tam W listesi literatürde yaygın) uygulandı: başlangıçta üst sınır tasarımı bir kez analiz edilir; ötelemeyi aşıyorsa sınırlar 0,23·(N−1) kademeyle artırılır. 4 katlı örnekte 4 adımda tüm liste açıldı ve SSO, 500 analizde (522 analiz) elle `UpperBoundMultiplier = 1` ile bulunanla **aynı** en iyi maliyeti verdi: **2587,6353**. 525M regresyonu etkilenmedi (7556,76 / 1,4605 ve 7281,65 / 1,5927; genişletme tetiklenmedi, başlangıç yalnızca ~1 dk uzadı).
5. **Güçlü kolon–zayıf kiriş (SCWB) sürekli kısıt olarak eklendi** (kullanıcı onayı 2026-10-08). ETABS `Steel Column Envelope` tablosundan `BCMajor` / `BCMinor` okunur (ilk bakışta: 0,48 … 1,14; sınır 1); yalnızca bir eleman "Beam/Column capacity ratio exceeds limit" hatası verdiğinde okunur (diğer modellerde ek maliyet yok). Grubun oranına (`PMMRatio`) katılır, hata sayısı cezasından çıkarılır; kısıt özetinde "beam/column capacity ratio (AISC 341 strong column - weak beam, max)" satırı.
   - İlk denemede (AISC 341 açık, 4 katlı örnek) **500 analizde uygun tasarım yine bulunamadı** (534 analiz). Neden: ModelBuilder kolon yönlendirmesini ayarlamıyordu; Y yönü çerçevelerinin kenar kolonları zayıf eksenden eğiliyor ve BCMinor en ağır W360X1299 ile bile 1,14 kalıyordu.
   - **Düzeltme: ModelBuilder `ColumnOrientation = Auto`** (varsayılan): x = sabit çizgilerdeki kenar kolonlar 90° döndürülür (4 katlı örnekte 8 kolon). Aynı model, aynı otomatik seçim tasarımı: **hiçbir elemanda hata kalmadı** (en yüksek BCMajor 0,96; soru kutusu çıkmadı).
   - **SCWB etkinken kiriş alt sınırı açıldı** (`SCWBActive`): başlangıç tasarımından sonra kolonlarda kiriş/kolon oranı varsa kiriş gruplarının alt sınırı en hafif kesite indirilir (daha hafif kiriş SCWB oranını düşürür; ETABS'in dayanım tasarımı bu yönde alt sınır olamaz). 525M'de tetiklenmez: regresyon yine 7556,76 / 1,4605 ve 7281,65 / 1,5927.
   - **Sonuç (AISC 341 açık, 4 katlı, SSO, 500 analiz; 531 analiz): uygun tasarım yine çıkmadı.** En iyi tasarımın ayrıntısı: köşe kolon SCWB oranı 1,47 → **1,01** (alt sınır açılınca neredeyse sağlandı); öteleme 0,15, dayanım rahat; ancak üst katlardaki iki kenar kiriş grubu (`BM-EDGE-L6000-S03-04` W690X457, `BM-EDGE-L5000-S03-04` W920X289) ve bir iç kolon grubu ETABS **tasarım hatası** (oran 2,000 = hata cezası) verdi. Büyük olasılıkla AISC 341 süneklik (kompaktlık) denetimi: iyileştirici 289 W kesitin hepsini deneyebiliyor, ETABS SMF kirişlerinde yüksek süneklik şartını sağlamayan kesitlere hata veriyor. ModelBuilder'ın havuzlarında bu süzgeç var, iyileştiricinin kesit listesinde yok. (Hata metni ayrıca okunmadı: varsayım.)
   - **Hata metinleri okundu** (iyileştirici artık her farklı ETABS tasarım hata metnini grup ve kesit adıyla `ErrorLog.txt`'ye bir kez yazıyor: `Warning: ETABS design error in group ...`). İki farklı hata çıktı: (1) `Section is not seismically compact for highly ductile members (AISC 341-16 E3.5a, D1.1, Table D1.1)` (iç kolon grubu, W760X134): süneklik/kompaktlık; (2) `Lb/ry > 0.095*E/(Ry*Fy) (AISC 341 E3.4b, D1.2b)` (5 m'lik kenar kiriş, W920X289): **kiriş yanal destek kuralı**; modelde döşeme olmadığı için kirişler tam açıklık boyunca desteksiz sayılıyor. Yani yalnızca süneklik süzgeci yetmez: kirişlerin yanal destek aralığı da modelde tanımlanmalı. (İlk tahminim, "yalnızca süneklik/kompaktlık", yarı doğruydu.)
   - **Sonuç:** AISC 341 **kapalı** (`SeismicProvisions = No`, varsayılan) iken örnek 500 analizde çözülüyor (2587,64). AISC 341 açık kullanım için iyileştiriciye AISC 341 süneklik süzgeci (kesit listesini yüksek süneklik şartını sağlayanlarla sınırlama) ya da hata türünü sürekli orana çevirme gerekir; kullanıcı kararı bekliyor (`KUYRUK.md`).
6. **Dikkat:** `RunBatch.ps1` "uygun tasarım yok" nedeniyle sonuçsuz biten koşuyu bir kez yedekten yeniden başlatır; bu durumda koşu aynı bütçeyle ikinci kez çalışır (bu oturumda eski bir koşunun yeniden başlatması yeni koşuyu bir saat bekletti). Yöneticiyi kapatırken ilgili süreçleri de kapatın.
7. **AISC 341 süneklik süzgeci eklendi** (kullanıcı onayı 2026-10-08; `ETABSClass.vb`: `MeetsDuctility`, `IsDuctile`, `InitializeDuctilityFilter`, `E1_Modifier_Ductility`, `NearestFeasible`). `App.config`: `SeismicDuctilityFilter` (auto | on | off, varsayılan auto: yalnızca SCWB etkinken ve çerçeve tipi SMF/IMF ise) ve `SeismicColumnCa` (0,3, kolon gövde sınırı için Pu/(phi Py) varsayımı).
   - Sınırlar: başlık bf/2tf ≤ 0,32 (yüksek) / 0,40 (orta) √(E/RyFy); gövde h/tw Tablo D1.1 (kirişte Ca = 0, kolonda Ca = 0,3); h = d − 2·1,8·tf. SMF'de 289 kesitten 235'i kiriş, 214'ü kolon için uygun. Uygun olmayan kesit analizden önce en yakın uygun kesitle değiştirilir; kompozit gruplar ve tüp değişkenleri etkilenmez.
   - **Kiriş yanal desteği hatası bulundu ve düzeltildi:** ModelBuilder'ın yazdığı 2,5 m destek ETABS'a hiç ulaşmıyordu. Tasarım overwrite tablosu `LMinor` / `LTB` alanlarını içe aktarmada sessizce yok sayıyor (sonuç 0 kalıyor). Çözüm: `DesignSteel.AISC360_22.SetOverwrite` (madde 27 = LMinor, 28 = LTB, `eItemType.Objects`); kesit sıfırlamasından (RestoreMedianSections) sonra da uygulanır. Doğrulama: 36 kiriş 1/3, 12 kiriş 1/2 olarak kayıtlı.
   - Tek tasarım değerlendirmesi: önceki başarısız tasarım yeni modelde ceza 0, ETABS tasarım hatası yok.
   - **Sonuç (4 katlı P4, AISC 341 açık, SMF, SSO, popülasyon 20, tohum 1, 500 analiz, `SeismicDriftAmplification = 5,5`):** destekli model → **uygun tasarım**, 534 analiz, 2,19 sa, maliyet **1726,90**, ceza 0, son ETABS kontrolü geçti. Desteksiz model (süzgeç açık) → 500 analizde uygun tasarım yok (kalan hatalar: W920X289 kenar kirişi Lb/ry ve bir iç kolonda kompaktlık). Bu 2 koşu aynı tohumla; AISC 341 kapalı P4 (2587,64) farklı modelde olduğu için doğrudan kıyaslanamaz.
   - **Karar (kullanıcı, 2026-10-09):** ModelBuilder `SeismicProvisions` varsayılanı **Yes**; `BeamBracingSpacing` 2,5 m sabit. Adil karşılaştırma için AISC 341 kapalı (`SeismicProvisions = No`) senaryo aynı 2,5 m destekli modelde aynı tohumla koşulacak (kuyrukta).
   - Gerileme (525M gömülü mod, tohum 12345): **7556,76 / 1,4605 ve 7281,65 / 1,5927** (değişmedi; süzgeç devreye girmedi). Sürüm 0.5.1.0.
3. Sürüm 0.5.1.0.

**Regresyon (525M gömülü mod, tohum 12345, 2 değerlendirme)**
- Beklenen (Aşama 5.1'den beri): 7564,91 / 1,4580 ve 7068,84 / 1,8032.
- Bugünkü değerler: **7556,76 / 1,4605 ve 7281,65 / 1,5927**. Tasarımlar neredeyse aynı, yalnızca bir grupta bir kesit farklı (eval 2, `var2`: W310X97 / W310X74).
- **Eski (0.4.1) test programı da bugün aynı değerleri veriyor** (kontrol koşusu, aynı koşullar): fark yeni koda bağlı değil.
  - "Eşzamanlı ETABS örnekleri" varsayımı **çürütüldü**: ETABS tek başına çalışırken yapılan yeni tekrar da aynı değerleri verdi (7556,76 / 1,4605 ve 7281,65 / 1,5927; başlangıç tasarımı 511 s).
  - Neden **bilinmiyor**: kod aynı (eski ve yeni exe aynı sonucu veriyor), model dosyası aynı, günlükler aynı; fark 5 Ekim'den bugüne ortamdaki bir değişiklikten (ETABS ya da işletim sistemi tarafı) geliyor. Tasarım vektörleri neredeyse aynı, yalnızca bir grupta kesit farklı olduğundan en olası yer, ETABS'in otomatik seçim tasarımına bağlı başlangıç sınırları (Lb/Ub).
  - **Yeni taban değerleri (0.5.1, 2026-10-07, 525M gömülü, tohum 12345, 2 değerlendirme): 7556,76 / 1,4605 ve 7281,65 / 1,5927.** Sonraki regresyonlar bunlarla karşılaştırılacak; eski değerler tarihçe olarak kalır.

---

## 2026-10-05 — Aşama 7: Toplu koşu ve uzun karşılaştırma (SSO / HS / ABC)

**Kullanıcı kararları:** HS ve ABC karşılaştırma için uygun. Uzun koşu bütçesi 500 analiz; modlar çelik, kompozit ve hibrit; 1 tohum (gerekirse artırılacak); aynı anda 3 ETABS.

**Kod**
- `MainForm`: toplu koşu modu `FrameSap2000.exe /batch <ayar.xml> [/resume]`.
  - Ayarlar XML'den okunur ve koşu başlatılır.
  - Mesaj kutuları ve sorular `<ayar>.batch.log` dosyasına yazılır; sorular Yes / OK ile cevaplanır.
  - Koşu bitince program kapanır; başarısızlıkta çıkış kodu 1 olur.
  - İlk günlük satırı yazılamazsa program hemen kapanır. Örnek neden: 260 karakteri aşan yol.
- Yeni `tools/RunBatch.ps1`:
  - koşu listesi (CSV) → her koşu için ayrı klasör, model kopyası ve ayar dosyası;
  - en çok N paralel süreç, aralıklı başlatma, `-Resume`, `-SummaryOnly`;
  - `summary.csv`;
  - uzun yol denetimi (en çok 230 karakter).
- `tools/batch_template.xml`: formun varsayılan ayarları. `tools/Asama7_runs.csv`: 9 koşu (3 yöntem × 3 mod; 500 analiz, popülasyon 20, tohum 1; ABC Limit = 10).
- `KULLANIM_KILAVUZU.md` 8.4.

**Testler**
- Derleme (MSBuild Release): hata yok.
- **Duman testi:** 2 paralel koşu, 525M kopyası, 12 analiz, popülasyon 4.

  | Koşu | Analiz | Maliyet | Final ceza | Final | Süre |
  |---|---|---|---|---|---|
  | SSO hibrit | 15 | 8360,45 | 0 | geçti | 0,16 sa |
  | ABC çelik | 15 | 8597,98 | 0 | geçti | 0,13 sa |

  - Ayarlar sonuç dosyasına doğru yansıdı: yöntem, bütçe, popülasyon, mod, `HideETABS`, ABC `Limit`.
  - Programlar kendiliğinden kapandı; `summary.csv` doğru (hibrit yığın satırları dahil).
  - Aynı anda iki ETABS örneği lisans sorunu olmadan çalıştı.
  - Analiz süresi 2 paralel koşuda yaklaşık 45 s.
- **Bulunan sorun:** geçici klasördeki uzun yolda (8.3 kısa ad açılınca 260 karakter) günlük ve yedek yazılamadı.
  - Koşu sürdü ama yalnızca uyarı verdi.
  - Düzeltme: günlük denetimi ve betikteki yol sınırı.
  - Test: 260 karakterlik yolda program ETABS açmadan çıkış kodu 1 ile kapandı; betik yolu reddetti.
- **Paralel koşu hatası (uzun koşularda bulundu, düzeltildi):**
  - Her koşu açılışta 2 günden eski çalışma klasörlerini (`%TEMP%\SteelFrameOpt`) siliyor (`DeleteStaleWorkDirs`). Klasörün yaşı dosyaların son yazma tarihinden ölçülüyordu.
  - `File.Copy` ise kopyaya kaynak modelin eski tarihini taşıyor. Bu yüzden başka bir koşu, henüz ilk analizini yazmamış bir koşunun klasörünü eski sanıp sildi.
  - Sonuç: SSO_Steel 99. analizde, ETABS'in yeniden başlatılması sırasında durdu (`File.Save (restart)`). SSO_Composite ve HS_Steel'in klasörleri de silindi.
  - Düzeltme (`ETABSClass.vb`):
    - kopyanın tarihi şimdiye ayarlanıyor;
    - klasöre sahip sürecin numarası yazılıyor (`owner.pid`);
    - sahibi çalışan bir klasör hiçbir zaman silinmiyor (`OwnerAlive`: süreç adı ve başlangıç zamanı da denetleniyor).
  - Birim testi (yansıma ile): canlı sahipli eski klasör korundu; ölü ya da sahipsiz eski klasör silindi; yeni klasör korundu.
- `RunBatch.ps1` güncellemesi:
  - paralel sınır sistemdeki bütün `FrameSap2000` süreçlerine göre sayılıyor;
  - başka bir süreçte çalışan koşu bekleniyor;
  - sonuçsuz biten koşu yedeğinden bir kez yeniden başlatılıyor.

  Böylece çalışan koşulara dokunmadan yönetici yenilenebildi.
- **Uzun koşular:** 2026-10-05 21:18'de başladı (`D:\Kosular\Asama7`).
  - 22:35'te düzeltilmiş exe ile devam edildi: SSO_Steel ve SSO_Composite yedeğinden, HS_Steel baştan.
  - SSO_Hybrid eski exe ile sürdü; klasörü etkilenmedi.
  - Bütün koşular 2026-10-06 15:31'de bitti (yaklaşık 18 saat, 3 paralel ETABS).

**Uzun koşu sonuçları** (525Member kopyası, AISC 360-22, D/C sınırı 0,95, 500 analiz bütçesi, popülasyon 20, tohum 1)

| Yöntem | Çelik | Kompozit (CFT) | Hibrit |
|---|---|---|---|
| SSO | 6061,54 (527 analiz) | 5673,41 (528) | 6497,86 (503) |
| HS | 5984,18 (513) | **5335,19** (506) | 5486,70 (517) |
| ABC (Limit 10) | 5867,16 (509) | 5785,87 (501) | 6279,56 (538) |

- **Final doğrulama:** 9 koşunun hepsinde final analizde ceza 0; ETABS ile final kontroller geçti.
  - Kompozit ve hibrit koşularda ETABS kompozit kolon tasarımının iç hesaba oranı 0,997–1,000.
  - Belirleyici kısıt çoğunlukla göreli kat ötelemesi: öteleme / sınır 0,75–0,99. Çelik tasarım oranı / D/C sınırı en çok 0,99.
- **Hibrit geçişler** (yığın 1: çevre kolonları, 8 hat; yığın 2: orta kolon, 1 hat; 5 katlık gruplar):
  - **SSO:** iki yığın da 25 kata kadar kompozit.
  - **HS:** çevre kolonları 1–20. kat kompozit, 21–25 çelik; orta kolon bütünüyle kompozit.
  - **ABC:** çevre kolonları bütünüyle kompozit; orta kolon 1–15. kat kompozit, 16–25 çelik.
- **Değerlendirme:**
  - Kompozit kolon, üç yöntemde de çelikten ucuz. En iyi değerlerle %10,8 (HS: 5335 / 5984); SSO'da %6,4, ABC'de %1,4.
  - HS kompozit ve hibrit modda en iyi sonucu verdi; çelikte ABC en iyi (5867).
  - **Hibrit**, aynı bütçede kompozitten daha pahalı çıktı: HS'de +%2,8, SSO'da +%14,5, ABC'de +%8,5.
    - Bu bir arama verimliliği sonucudur: tümü kompozit tasarım hibrit uzayının içindedir, yani hibrit optimumu kompozitten pahalı olamaz.
    - Hibrit modda değişken sayısı 14'ten 26'ya çıkıyor. 500 analiz bu uzay için az kalıyor.
  - Sonuçlar tek tohumludur; yöntem sıralaması için kesin hüküm değildir.
  - SSO_Steel ve SSO_Composite yedekten devam etti. Rastgele sayı üretecinin durumu yedeklenmediği için bu iki koşu kesintisiz bir koşunun birebir aynısı değildir.
  - `summary.csv`'deki süre yalnızca son başlatmanın süresidir.
- Koşu klasörü: `D:\Kosular\Asama7` (her koşuda sonuç XML'i, Excel, `_best.EDB`, ErrorLog). Klasör depoya alınmadı.

---

## 2026-10-05 — Aşama 6: Hibrit kolonlar (grup grup çelik / kompozit, geçiş katının optimizasyonu)

**Kullanıcı kararları** (öneri: `Ajan/Gorevler/ASAMA6_ONERI.md`)
- **Geçiş:** hem yığın bazında hem grup bazında olabilir; iki seçenek olarak eklendi, varsayılan yığın bazında.
- **Gruplama:** kullanıcıya bırakılıyor; program modeldeki grupları kullanıyor.
  - İleride bir model oluşturucu yazılacak. Plan: kolonlarda köşe / kenar / iç, kirişlerde kenar / iç; farklı uzunluklar için gruplar çoğaltılır (`AKIS_SEMASI.md` Bölüm 5).
- **Ters düzen** (kompozit grup çeliğin üstüne sabitlenmiş): yalnızca uyarı.
- **460Member:** şimdilik olduğu gibi kalıyor.
- **Tip seçimi:** formda bir tabloyla.
- **İki kesit değişkeni:** öneriye itiraz gelmediği için uygulandı.

**Kod**
- Yeni `HybridColumns.vb` (`Partial Class ETABS_Class`):
  - **tipler:** `TransitionMode_` (PerStack / PerGroup), `GroupColumnType_` (Optimize / Steel / Composite), `GroupTypeSetting_`, `ColumnGroupInfo_`;
  - **kolon yığınları:** kolon–kolon çiftlerinden birleşik bileşenler, en düşük kota göre sıralı;
  - **tasarım vektörü:** `[grup değişkenleri][Optimize gruplarının kompozit kesiti][geçiş / tip değişkenleri]`;
  - **çözme ve geri yazma:** `DecodeHybrid` / `EncodeHybrid` / `GroupVector`;
  - **çıktı ve yardımcılar:** `DescribeDesign` (yığın satırları dahil), `CtoCRatio` (W kolon tüpün üzerinde: W derinliği ≤ H, başlık ≤ B), ACO için `FullVarCount/FullVarArea`, Check Structure için `SetGroupTypes`;
  - **form:** `ReadColumnGroups` (gizli ETABS ve model kopyasıyla kolon grupları).
- `ETABSClass.vb`:
  - `Initialize_UBLB` her grup için çelik ve kompozit sınırlarını ayrı ayrı hesaplıyor; hibrit olmayan modlarda sonuç öncekiyle aynı.
  - Değerlendirme zinciri grup vektörüyle çalışıyor; zincirde `GUb/GLb` kullanılıyor.
  - Tipi değişen grup yeniden atanıyor (`AssignedComp`); çeliğe dönen grubun tasarım prosedürü SteelFrameDesign yapılıyor. `LastAnalysedComp` eklendi.
  - Kolon–kolon geometrisinde `CtoCRatio` kullanılıyor.
  - Gruplarda `IsColumn` alanı var.
- `Structures.vb`: `Group_.IsColumn`; `FormInfo.HybridColumns`, `TransitionMode`, `GroupTypes`.
- `OptimizationClass.vb`: en iyi ve final tasarımın yazımı `DescribeDesign` ile.
- `OptimizationMethods.vb`: ACO sezgiseli tam vektör anlamıyla.
- `MainForm`:
  - "Hybrid Columns" kutusu: hibrit seçimi, geçiş modu, "Read column groups of the model" düğmesi, grup tipi tablosu;
  - doğrulama: hibrit için Composite columns gerekli;
  - Check Structure'da tipler sonuç dosyasındaki `[CFT ` / `[CFP ` / `[EC ` işaretlerinden okunuyor.
  - Frame Properties kutusu kısaltıldı.
- Önerideki ayrı grup çakışması kontrolü eklenmedi. Referanstaki `InitializeFrames` birden fazla grupta olan elemanları zaten bildiriyor ("More group definition than 1"; 460Member'da 10 eleman). Eklediğim özet kontrolü bu nedenle kaldırdım.
- Sürüm 0.5.0. Belgeler:
  - kılavuz: 5.2, yeni 7.7;
  - kurallar: A7;
  - `README.md`.

**Testler**

| Test | Sonuç |
|---|---|
| vbc ve MSBuild `Rebuild` | 0 hata, 0 uyarı; exe 0.5.0.0 |
| MathTest | Aşama 3–4 ile birebir aynı |
| **Gömülü mod regresyonu** (525M, tohum 12345) | 7564,91 / 1,4580 ve 7068,84 / 1,8032; önbellek tekrarı Aşama 5.1 ile **birebir aynı** |
| Grup okuyucu (`ReadColumnGroups`) | 525M: 10 kolon grubu, 0–70000 mm kotları ve katları doğru. 460Member: 8 grup; grup 11'in katları "Story20, Story6, Story5" (çakışma görünüyor). |
| **Hibrit, yığın bazında** (525M, 3 değerlendirme) | ayrıntılar aşağıda |
| **Hibrit, grup bazında, sabit tiplerle** (5 = Composite, 12 = Steel, 14 = Composite; 2 değerlendirme) | 28 değişken (7 Optimize grup için 7 tip değişkeni); sabit tipler uygulandı; "group 14 is fixed composite above a fixed steel group" uyarısı yazıldı; uyumsuzluk 0 |
| **Hibrit uçtan uca** (525M, SSO, 4 örümcek, 16 analiz, final) | ayrıntılar aşağıda |
| Form | grup tablosu dolduruluyor; önceki tip seçimleri korunuyor (5 = Composite, 13 = Steel); tablodan tipler doğru geri okunuyor; sütunlar sığıyor (ekran görüntüsüyle kontrol edildi) |

Hibrit, yığın bazında testinin ayrıntıları:
- 26 değişken; yığınlar 5-7-9-11-13 | 6-8-10-12-14.
- Geçiş değerleri doğru çözüldü. Örneğin t = (5, 1) → çevre yığınının tamamı kompozit, orta yığında yalnızca 6 kompozit.
- Her değerlendirmede ETABS'teki kesit ve tasarım prosedürü tiple **birebir uyumlu**: kompozit grupta `CFT_…` ve prosedür 7, çelik grupta W ve prosedür 1. Uyumsuzluk 0.
- Tipi değişen gruplar doğru güncellendi: grup 5 tüpten W1100X499'a döndü ve prosedürü 1 oldu.
- Önbellek tekrarı aynı sonucu verdi.

Hibrit uçtan uca testinin ayrıntıları:
- **Final tasarım:** çevre yığını tümüyle kompozit; orta yığında 6 ve 8 kompozit, 10–12–14 çelik.
- **Final doğrulaması:** yalnızca 7 kompozit grup Filled Steel Tube kesitine çevrildi ve ETABS kompozit tasarımıyla kontrol edildi. ETABS / iç hesap farkı ≤ %1 (ör. 0,635 / 0,637). Çelik gruplar ETABS çelik tasarımında kaldı.
- **Çıktılar:** sonuç XML'i, Excel, `_best.EDB` ve yedek yazıldı; yığın satırları çıktıda var.
- **Toplam süre:** 1954 s.
- Kısa koşuda uygun tasarım yok (final cezası 0,911); bu bir optimizasyon sonucu değil.

Notlar:
- 460Member, öteleme kontrolü "yalnızca yanal yükler" modunda başlatılamıyor. Modelde rüzgâr ya da deprem yük durumu yok: "No lateral (wind/earthquake) combination or case found". Kolonları kompozit kolon tasarım prosedüründe olduğu için zaten tasarım değişkeni sayılmıyor.
- Test programım bu başlatma hatasında çöktü ve açtığı gizli ETABS örneğini kapatamadı. Yalnızca testin başlattığı bu örnek, başlangıç saatiyle doğrulanarak kapatıldı. Kullanıcının ETABS oturumu yoktu; programın kendi kodu etkilenmedi.

---

## 2026-10-04 — Aşama 5.1: D/C oranı sınırı (ETABS ile tutarlılık) ve deprem şartnamesi süneklik süzgeci

**1. D/C oranı sınırı (kullanıcı: "1. notu incele, ciddi sorun")**
- **Bulgu:** Aşama 5 testinde grup 6 için ETABS "Combined D/C ratio exceeded" mesajı verdi, oysa PMM oranı 0,969'du.
  - ETABS'in AISC 360-22 kompozit kolon ve çelik çerçeve el kitaplarına göre ETABS oranı 1,0 ile değil, tasarım tercihlerindeki **D/C ratio limit** (varsayılan 0,95) ile karşılaştırıyor.
  - Grup 6'nın beş kolonunda yalnızca 0,969 işaretlendi; 0,858 işaretlenmedi.
  - Sınır kopyada 0,45 yapıldığında 0,455–0,52'lik çelik elemanlar için `DesignSteel.GetSummaryResults` hata ve uyarı alanlarını **boş** döndürdü. Program sınır aşımını göremiyordu.
  - Modellerde `SRLIMIT 0.95`.
  - Sonuç: program (referanstan beri) oranı 0,95–1,0 arasındaki çelik ve kompozit elemanları uygun sayıyordu; ETABS'te bu elemanlar yetersiz görünüyordu.
- **Düzeltme** (kullanıcı onayı: sınır modelden):
  - `InitializeRatioLimits` / `RatioLimit`, sınırları `Steel Frame Design Preferences - <kod>` ve `Composite Column Design Preferences - <kod>` tablolarının `DCLimit` alanından okuyor. Alan AISC 360-22, 360-16 ve 360-10'da aynı.
  - App.config `DesignRatioLimit`: boş = modeldeki sınır; sayı = çalışma kopyasına yazılır. ETABS yeniden başlatılınca tekrar yazılır.
  - Çelik grubunun oranı = ETABS oranı / çelik sınırı.
  - Kompozit grubun oranı = max(detay oranı, dayanım / kompozit sınırı). `CompositeStrength` ham kalır; kalibrasyonda kullanılıyor.
  - ETABS doğrulama oranları (`ETABSRatioByVar`, koruma döngüsü, uyarılar) = oran / sınır.
  - Günlük: `Info: D/C ratio limits (...)` satırı ve ETABS kontrol satırlarında "(D/C limit 0.95)".
- **API bilgileri:**
  - `DesignSteel.AISC360_22.GetPreference(37)` ve `DesignCompositeColumn.AISC360_22.GetPreference(18)` da aynı değeri veriyor. Kod tablolardan okuduğu için çelik koduna bağlı değil.
  - Tablo yazımı tercihlerde yalnızca `DCLimit` değerini değiştiriyor (yazmadan önce ve sonra alan alan karşılaştırıldı).

**2. Deprem şartnamesi süneklik süzgeci (kullanıcı isteği)**
- **Kaynak:** kullanıcının paylaştığı TBDY 2018 Tablo 9.3, "Kompozit Elemanlar" satırları (AISC 341-10 Tablo D1.1 ile aynı):

  | Düzey | Kutu cidarı b/t | Boru cidarı D/t |
  |---|---|---|
  | Yüksek (λhd) | 1,4 √(E/Fy) | 0,076 E/Fy |
  | Sınırlı (λmd) | 2,26 √(E/Fy) | 0,15 E/Fy |

  - Doğrulama: kullanıcının daha sonra gönderdiği AISC 341-10 Tablo D1.1 devam sayfası ("Walls of rectangular / round filled composite members") aynı değerleri veriyor; kompozit satırlarda dipnot yok.
  - AISC 341-16 bağlantısının (Scribd) içeriği okunamadı.
- **Kod:**
  - `TubeSettings_.SeismicDuctility` (None / Moderate / High; kullanıcı kararıyla varsayılan **High**);
  - `SeismicLimit`, `SeismicSlenderness` (b: kütüphanedeki HSS'te B − 3t, yapma kutuda B − 2t);
  - katalog süzgeci ve günlük satırı;
  - `TubeSections.xml` ayarı.
- **Sonuç** (Fy = 345 MPa):
  - katalog yüksek sünekliğe göre 100 kesit (27 HSS + 73 yapma), sınırlı sünekliğe göre 132, süzgeç yokken 149;
  - örnek: yüksek süneklikte 600 mm yapma kutuda levha en az 20 mm, 1000 mm'de en az 30 mm.

Sürüm 0.4.1. Belgeler:
- kılavuz: 2.4 (`DesignRatioLimit`), 7.5 deprem tablosu, yeni 7.6;
- kurallar: A6;
- `README.md`.

**Testler**

| Test | Sonuç |
|---|---|
| vbc ve MSBuild `Rebuild` | 0 hata, 0 uyarı; exe 0.4.1.0 |
| MathTest | Aşama 3–4 ile birebir aynı |
| TubeTest (31 kontrol) | hepsi geçti. Yüksek: 100 kesit, hepsi b/t ≤ 33,7; BU1000X1000X25 (b/t = 38) atıldı, BU1000X1000X30 (31,3) kaldı. Sınırlı: 132. Süzgeçsiz: 149. Yüksek düzeyde borular D/t ≤ 44,1 (57 boru). XML varsayılanı High. |
| **525M, gömülü mod, sınır modelden** (ETABS 22.6, tohum 12345) | sınırlar 0,95 / 0,95 okundu. **Yeni regresyon değerleri:** 7564,91 / 1,4580 ve 7068,84 / 1,8032; önbellek tekrarı aynı. Oranlar 0,95'e göre ölçekleniyor (ör. 0,805 / 0,95 = 0,847). |
| **525M, tüp modu uçtan uca** (SSO, 4 örümcek, 12 analiz, final) | ayrıntılar aşağıda |
| `DesignRatioLimit = 1.0` | sınır her iki tercih tablosuna yazıldı ve geri okundu (1 / 1). Sonuç: 7348,43 / 1,8238 ve 6886,35 / 2,4916. Eski referanstan (7121,40 / 2,0706) farkının nedeni aşağıda. |

Tüp modu uçtan uca testinin ayrıntıları:
- katalog 100 kesit, sınırlar 0,95;
- 10 kolon grubunda ETABS / iç hesap PMM oranları:

  | Grup | ETABS | İç hesap |
  |---|---|---|
  | 5 | 0,788 | 0,790 |
  | 6 | 0,777 | 0,780 |
  | 9 | 0,678 | 0,684 |
  | 14 | 0,171 | 0,172 |

  Fark %1'in altında ve iç hesap her grupta biraz daha güvenli tarafta; kalibrasyon uyarısı yok.
- Hiçbir kolonda "Combined D/C ratio exceeded" mesajı yok.
- Kısa koşuda final cezası 0,5565; bu ceza öteleme kısıtından geliyor, bu bir optimizasyon sonucu değil.

`DesignRatioLimit = 1.0` testindeki farkın nedeni:
- Başlangıç sınırları (Ub/Lb) ETABS'in otomatik kesit seçimiyle bulunuyor ve ETABS bu seçimde de D/C sınırını kullanıyor.
- Sınır 1,0 olunca ETABS daha hafif kesitler seçiyor (ör. grup 5: W760X314 → W360X314; grup 8: W760X284 → W690X265); üst sınırlar değişiyor (ör. grup 7: 240 → 231).
- Bu, ayrı bir çalıştırmayla doğrulandı.
- Eski referans "ETABS 0,95, program 1,0" karışımından geliyordu; tek ve tutarlı bir ayarla yeniden üretilemez. Bundan sonra regresyon değeri olarak 7564,91 / 1,4580 kullanılacak.

---

## 2026-10-03 — Aşama 5: Dolgulu tüp (CFT/CFP) kompozit kolonlar

**Kullanıcı kararları** (inceleme ve öneri: `Ajan/Gorevler/ASAMA5_ONERI.md`)
- Kesitler ETABS kütüphanesinden alınacak. Program, yapma (levhadan kaynaklı) kutuları kendisi üretecek.
- Dolgu betonu modelde tanımlı beton malzemesi olacak.
- Boru varsayılan olarak kapalı, ayarla açılabilir. Moment çerçevesinde boruya kiriş bağlantısı zor.
- Süzgeç: kare kutu.
- En küçük boyut 300 mm. Kullanıcının sorusu üzerine araştırıldı:
  - AISC 360-22 dolgulu kolon için alt sınır koymuyor;
  - TBDY 2018 7.3.1.1 betonarme kolon için 300 mm istiyor (kompozit kolonlar Bölüm 9'da);
  - beton dökümü ve kiriş bağlantısı da 200 mm'lik kolonu zorlaştırıyor.
- Gömülü kesit seçeneği **kalıyor**. Tek program, formda kompozit tip seçimi.

**Kod**
- Yeni `TubeColumn.vb`:
  - `CompositeType_` (Encased / FilledTube), `TubeShape_`, `TubeSection_`;
  - `TubeSettings_` ve kesit kataloğu: kütüphanedeki STEEL_BOX / STEEL_PIPE (tasarım kalınlığı) + yapma kutular `BU<B>X<B>X<t>`, `MinDimension` ve Tablo I1.1a/b λmax süzgeci, çelik alanına göre sıralama.
- Yeni `TubeSections.xml` ayar dosyası (exe yanına kopyalanır).
- Dayanım hesabı için referanstaki `FilledBox` / `FilledPipe` sınıfları kullanıldı (I2.2, I3.4, I4, I5). Formüller değişmedi.
- `ETABSClass.vb`:
  - tüp modda kompozit grubun değişkeni `Tubes` listesinin indisi;
  - W listesine bağlı yerler katalog yardımcılarıyla genelleştirildi: `IsTubeVar`, `CatalogCount`, `SecArea`, `SecDepth`, `SecName`, `FindSection`, `ColumnGap`. Etkilenen yerler:
    - geometri onarımı ve geometri cezası;
    - `NearestFeasible`, onarım adımları (`RepairSteps`, F2, F4, G2);
    - final koruması (`StepUpETABSFailures`, `GeometryFits`);
    - kesit ataması, maliyet ve maliyet dökümü, değişken açıklaması (`HSS… [CFT 559x559x23.6]`).
  - `CompositeSec`: tüp ya da gömülü kesit.
  - Ub/Lb: tüp grubunda Lb = 0. Ub, ilk tasarımdaki W kesitinin Fy·A değerine en az eşit Pno'lu ilk tüpten başlar; referanstaki kayma payı eklenir.
  - Dolgu betonu `ResolveTubeConcrete` ile bulunur: ayardaki ad modelde varsa o, yoksa modelin ilk beton malzemesi. Tüp modda donatı malzemesi okunmaz.
  - Final: `CreateTubeSections`, General kesitleri aynı adla `Filled Steel Tube/Pipe` tablolarına yazar (açık boyutlar; `FromFile` ETABS 22.6'da çalışmıyor). Ardından ETABS kompozit kolon tasarımı yapılır.
- `Structures.vb`: `FormInfo.CompositeType`. Eski yedeklerde değer 0 olduğu için gömülü kabul edilir.
- `MainForm`:
  - "Composite columns" kutusunun yanına tip seçimi eklendi; varsayılan *Filled tube (box / pipe)*;
  - tüp seçiliyken *Rebar* / *Formwork* maliyet kutuları pasif;
  - sonuç dosyasından tip tanıma (`[CFT ` / `[CFP `);
  - Check Structure'da kesitler `FindSection` ile okunur.
- `OptimizationMethods.vb`: ACO'nun "hafif kesit" tercihi değişkenin kataloğunu kullanıyor (`AcoHeuristic(d, s)`). W değişkenlerinde sonuç aynı.
- Sürüm 0.4.0. Belgeler:
  - kılavuzda 5.2 ve yeni 7.5;
  - `PROGRAM_KURALLARI.md` A5;
  - `README.md`.

**Testler**

| Test | Sonuç |
|---|---|
| vbc (uygulama, MathTest, ETest, TubeTest, form testi, uçtan uca test) | hata yok |
| MathTest (16 yöntem × Levy açık/kapalı × 3 tohum) | Aşama 3–4 çıktısıyla **birebir aynı** |
| TubeTest (ETABS'siz, 23 kontrol) | hepsi geçti. Varsayılan katalog 149 kesit: kütüphaneden 45 kare HSS (≥ 300 mm) + 104 yapma kutu. Elle hesapla Pno: HSS 559×23,6 için 23.564 kN, Ø711×25,4 boru için 27.829 kN (C2 = 0,95). λmax süzgeci: BU1000X1000X6 atıldı, BU400X400X6 kaldı. Boru ve dikdörtgen kutu seçenekleri çalışıyor. XML ayarları okunuyor. |
| Form testi | "Composite columns" + *Filled tube (box / pipe)* görünüyor; *Rebar* / *Formwork* kutuları pasif |
| **Gömülü mod regresyonu** (ETABS 22.6, 525Member kopyası, tohum 12345, 2 değerlendirme) | 7121,40 / 2,0706 ve 7092,09 / 1,8054; kompozit oranlar ve önbellek tekrarı Aşama 2 referansıyla **birebir aynı** |
| **Tüp modu uçtan uca** (525Member kopyası, AISC 360-22, SSO, 4 örümcek, 12 analiz, `Opt_Finalize` dahil) | Ayrıntılar aşağıda. |

Tüp modu uçtan uca testinin ayrıntıları:
- **Başlangıç:**
  - katalog 149 kesit, dolgu betonu 4000Psi (modelden);
  - sınırlar: kiriş W[12–163]; tüp grupları [0–34] … [0–71] (149 kesit içinde).
- **Arama sırasında:** General kesitler (`CFT_…`); 6 kez kesit oluşturma, ortalama 4,9 s.
- **Final:**
  - 7 kesit `Filled Steel Tube` tablosuyla oluşturuldu, tasarım prosedürü 13;
  - `m_best.$et` dosyasında örnek kesit: `SHAPE "Filled Steel Tube" D 550 B 550 TF 20 TW 20 FILLMATERIAL "4000Psi"`.
- **İç hesap / ETABS karşılaştırması** (10 kolon grubu, PMM):

  | Grup | ETABS | İç hesap |
  |---|---|---|
  | 5 | 0,862 | 0,868 |
  | 6 | 0,969 | 0,975 |
  | 7 | 0,654 | 0,658 |
  | 8 | 0,839 | 0,844 |
  | 9 | 0,475 | 0,456 |
  | 10 | 0,636 | 0,611 |
  | 11 | 0,291 | 0,296 |
  | 12 | 0,486 | 0,491 |
  | 13 | 0,180 | 0,188 |
  | 14 | 0,342 | 0,349 |

  En büyük ETABS / iç hesap oranı 1,041; program `CompositeStrengthFactor = 1,04` önerdi. Gömülü kesitte bu oran yaklaşık 1,04–1,12 idi; dolgulu kutuda uyum daha iyi.
- **Süreler:**
  - analiz ortalaması 21,9 s;
  - ETABS kompozit kolon tasarımı (yalnızca finalde, 225 kolon) **849 s**;
  - toplam 2022 s.
- **Çıktılar:** sonuç XML'i, Excel, `_best.EDB` ve yedek yazıldı; çalışma klasörü silindi, ETABS kapandı.

Notlar:
- 12 analizlik bütçeyle uygun (cezasız) tasarım bulunamadı; final cezası 0,8365. Test, final yolunu en iyi bellek üyesiyle çalıştırdı. Bu kısa test bir optimizasyon sonucu değildir.
- Grup 6 için ETABS PMM oranı 0,969 iken "Combined D/C ratio exceeded" mesajı verdi. Mesajın hangi istasyon veya kontrolden geldiği incelenmeli.
- **Sismik süneklik:** AISC 341 (TBDY Bölüm 9) kompozit moment çerçevesinde dolgulu kolonlar için daha sıkı genişlik/kalınlık sınırları (yüksek / orta süneklik) koyar. Katalog şu an yalnızca AISC 360 λmax süzgecini uyguluyor. İstenirse ayar olarak eklenebilir.

---

## 2026-10-03 — Aşama 3–4: Sosyal Örümcek Algoritması (SSO) — Fortran'dan çeviri, 16. yöntem

**İstek:** "Fortran kodunu bulduysan çevir. Referans ETABS programında 15 adet optimizasyon programı var. 16. olarak SSO ekle." Plandaki Aşama 3 (inceleme) ile Aşama 4 (çeviri) birlikte yapıldı.

**Kaynaklar** (`fortran\SocialSpider\`):
- `SSO_Column\SSO.f90` (2015);
- `SSO_Frame\SSO.f90` (2018);
- `SSO.m`;
- Cuevas vd. (2013).

Karşılaştırma, çeviri kararları ve taşınmayan 8 hata `Ajan/Yetenekler/Optimizasyon_Yetenegi/SSO_CEVIRI_NOTLARI.md` dosyasında. Taşınmayan hataların başlıcaları:
- sınır kırpmasında önceki döngüden kalan `k` indisi;
- yuvarlamanın yeni tasarım yerine eski konuma yazılması;
- en iyi örümcek ile en iyi dişinin karışması ve dizi dışına taşma;
- toplamı 1'i aşan rulet olasılıkları.

**Kod**
- Yeni dosya `SocialSpider.vb` (`Partial Class OptimizationClass`):
  - `Main_SocialSpider`: dişi ve erkek hareketleri;
  - `SpiderMating`: baskın erkeklerin çiftleşmesi;
  - isteğe bağlı `SpiderJump` (SSO_Column eki);
  - yardımcılar: ağırlık, titreşim uzaklığı, medyan, cinsiyet dengesi.
- `Structures.vb`:
  - `OptMethod_.SocialSpider = 15`;
  - `Member_.IsMale`: örümceğin cinsiyeti; bellek her döngüde sıralandığı için üyeyle birlikte taşınıyor;
  - `AlgorithmState_.SpiderFemales`: dişi sayısı, yedeğe girer.
  - Alanlar yalnızca eklendi; eski yedekler okunabilir.
- `OptimizationClass.Main`: SSO çağrısı eklendi.
- `OptimizationMethods.vb`:
  - `InitMethodState` SSO durumunu kuruyor;
  - katalog girdisi eklendi: PF 0,7; çiftleşme yarıçapı 0,5 × aralık; kabul *If better* / *Always*; *Spider jump* hayır.
- `.vbproj`: `SocialSpider.vb` eklendi.
- Sürüm 0.3.0.
- Belgeler:
  - `KULLANIM_KILAVUZU.md`: 6.1 yöntem tablosu, 6.2 SSO önerileri, durum notu;
  - `README.md`: 16 yöntem;
  - `PROGRAM_KURALLARI.md`: A3, B6;
  - `Ajan/`: akış şeması, kuyruk, hafıza.

**Kullanıcı kararları (bu aşamada alındı, sonraki aşamalara işlendi)**
- **Aşama 5:** tüp kesitler ETABS kütüphanesinden alınacak.
- **Aşama 6:** kolon tipi hem kullanıcı tarafından belirlenebilecek hem de optimizasyon değişkeni olacak. Amaç, "belirli kata kadar kompozit, üstü çelik" geçişinin optimizasyonu. Taslak tasarım `Ajan/Gorevler/AKIS_SEMASI.md` Bölüm 4'te.

**Testler**

| Test | Sonuç |
|---|---|
| vbc derlemesi (uygulama, MathTest, SSO testi, form testi, uçtan uca test) | hata yok |
| MSBuild (VS 2026) `Rebuild` | 0 hata, 0 uyarı |
| MathTest (dişli treni, 3000 analiz, 20 üye, 3 tohum) | ilk 30 satır (15 yöntem) Aşama 2 referans çıktısıyla **birebir aynı**. SSO: 2,31E-11 / 1,18E-09 / 2,70E-12, Levy ile aynı (SSO Levy kullanmıyor); hata yok. |
| SSO testi: 7 parametre bileşimi × N = 20/5/2 × 3 tohum | hata, sınır dışı değişken veya durma yok; dişi sayısı her döngüde korundu (`genderErr = 0`); yedek XML gidiş-dönüşünde cinsiyetler ve dişi sayısı aynı. Döngü başına analiz ≈ 1,0–1,15·N, sıçramayla ≈ 1,35–1,5·N. |
| Form testi (ekran görüntüsü) | yöntem listesinde 16 öğe, sonuncusu "Social Spider (SSO)"; parametre kutusu katalogdan kuruluyor. Kabul seçeneğinin metni kutuya sığmıyordu; kısaltıldı. |
| ETABS 22.6 uçtan uca: `525Member` kopyası, kompozit mod, AISC 360-22, tohum 12345, 4 örümcek, 12 analiz, `Opt_Finalize` dahil | başlangıç 490 s; başlangıç belleği 8 analiz, en iyi 8670,7; 1. döngü sonunda 15 analiz, uygun tasarım 8670,71. Final ETABS kompozit doğrulaması çalıştı; sonuç XML'i, Excel, `_best.EDB` ve yedek yazıldı; çalışma klasörü silindi, ETABS kapandı. Toplam 1458 s. Yedekte `SocialSpider`, `SpiderFemales = 2` ve 2 dişi / 2 erkek. |

Notlar:
- Uçtan uca testteki tek uyarı, referansta da bilinen kalibrasyon önerisi: ETABS / iç kompozit oranı en fazla 1,040, öneri `CompositeStrengthFactor = 1,04`. Referans kurallarında önerilen aralık 1,08–1,12.
- Bu makinede Fortran derleyicisi (gfortran, Intel ifort/ifx, flang) olmadığı için Fortran ile adım adım sayısal karşılaştırma yapılamadı. Davranış, makale ve üç kaynak sürümle formül düzeyinde karşılaştırıldı.

---

## 2026-10-03 — Aşama 2: Referans kodun aktarılması, eski iskeletin kaldırılması

**Kullanıcı kararları (Aşama 1 sorularına cevap)**
- **Kolon tipi:** kompozit kolonlar **dolgulu tüp** olacak; çelik kutu (CFT) veya boru (CFP) içine beton doldurulur. Aşama 1'de raporlara eklenen "gömülü kesit" düzeltmesi bu nedenle geri alındı.
- **Hibrit tasarım:** her kolon grubu ayrı ayrı çelik veya kompozit seçilebilecek.
- **Kompozit döşeme:** isteğe bağlı.
- **Depo adı:** adındaki "Tube" kolonların tüp olmasını anlatıyor; ad kalıyor.
- **Eski iskelet kod:** kaldırılacak.
- **Lisans:** ücretsiz → MIT.
- **ETABS modelleri:** depoya girebilir.

**Yapılanlar**
- Eski iskelet kaldırıldı: `SSO_CF/` (`CFCS.vb`, `Class1.vb`, form, proje dosyaları, `AISC14M.xml`) ve `CFCS.sln`.
- Test modelleri `Modeller/525M` ve `Modeller/CivilComp3` altına taşındı. Modeller `.EDB` ve `.$et` olarak depoya girdi; analiz çıktıları ve `.ebk` girmedi (`.gitignore`).
- Referans projeden (SteelFamewithCompositeColumn_ETABS 2026.10.3) şu dosyalar **bayt düzeyinde aynı** kopyalandı (`cmp` ile kontrol edildi):
  - kaynak kod: `ApplicationEvents.vb`, `CompositeColumn.vb`, `ETABSClass.vb`, `ExcelExport.vb`, `MainForm.*`, `OptimizationClass.vb`, `OptimizationMethods.vb`, `Structures.vb`, `My Project/*`;
  - ayar ve proje dosyaları: `App.config`, `EncasedSections.xml`, `packages.config`, `.vbproj`, `.sln`;
  - belgeler ve araçlar: `AISC360_16/22_Composite_Column_Rules.md`, `LICENSE` (MIT), `tools/md2html.py`.
- Uyarlamalar (davranışı değiştirmez):
  - proje ve çözüm `SteelFrameWithCompositeTubeColumns(.vbproj / ETABS.sln)` olarak yeniden adlandırıldı; proje, çözüm ve assembly GUID'leri yenilendi;
  - `AssemblyInfo`: ürün adı `SteelFrameWithCompositeTubeColumns`, telif "© 2026 Ibrahim Aydogdu", sürüm **0.2.0.0**;
  - pencere başlığı "Steel Frame Optimization with Composite Tube Columns (ETABS)" oldu.
  - Kök ad alanı ve exe adı `FrameSap2000` olarak korundu, böylece ayar ve yedek dosyalarıyla uyum sürüyor.
- Belgeler:
  - `PROGRAM_KURALLARI.md`: A bölümü projeye özgü kurallar, hedef, durum ve depo düzenini anlatıyor; B bölümü referansın kuralları.
  - `KULLANIM_KILAVUZU.md` ve `README.md` referanstan alındı; başlarına geliştirme durumu notu eklendi.
  - `KOD_INCELEME_RAPORU.md` notu güncellendi.
  - `Ajan/` altındaki akış şeması, görev kuyruğu, hafıza ve yetenek açıklamaları CFT/CFP ve hibrit hedefe göre yenilendi. Mimari betiği de aynı içeriği üretecek şekilde güncellendi.

**Testler**

| Test | Sonuç |
|---|---|
| vbc derlemesi (uygulama, MathTest, ETest; yeni ve referans kaynaklar) | 6/6 başarılı, hata yok |
| MSBuild (Visual Studio 2026), `Rebuild`, Debug | 0 hata, 0 uyarı. Exe'de ürün adı ve sürüm 0.2.0.0 doğru. |
| Form açılış testi | form "…Composite Tube Columns (ETABS)" başlığıyla açıldı ve yanıt veriyor |
| MathTest: dişli treni, 15 yöntem × Levy açık/kapalı × 3 tohum, 3000 değerlendirme | yeni ve referans derlemenin çıktısı **birebir aynı** (30 satır, `cmp`); hata yok |
| ETABS 22.6: `525Member` kopyası, kompozit mod, AISC 360-22, tohum 12345, 2 değerlendirme ve önbellekten tekrar | yeni ve referans derleme **birebir aynı** (aşağıda) |
| Mimari betiği (geçici klasörde yeniden çalıştırma) | 7 README üretildi, hepsi depodakilerle aynı |
| Model dosyaları | testlerden sonra `Modeller/525M/525Member.EDB` SFCS'deki orijinalle aynı. Testler kopya üzerinde çalıştı. |

ETABS testinin ayrıntıları:

| | Referans derleme | Yeni derleme |
|---|---|---|
| Değerlendirme 1: maliyet / ceza | 7121,40 / 2,0706 | 7121,40 / 2,0706 |
| Değerlendirme 2: maliyet / ceza | 7092,09 / 1,8054 | 7092,09 / 1,8054 |
| Kompozit oranları (2) | 0,828 0,805 0,955 0,915 0,915 0,915 0,700 0,788 0,637 0,963 | aynı |
| Önbellekten tekrar | 7121,40 / 2,0706 (0 s) | aynı |
| Süre: başlangıç / toplam | 517 s / 770 s | 480 s / 715 s |
| Çalışma klasörünün silinmesi, ETABS'in kapanması | evet | evet |

Notlar:
- Bu değerler referansın Aşama 3–4 kayıtlarındaki 7906,97 / 1,3548 değerlerinden farklı. Fark beklenen bir durum: referans kod o ölçümden sonra P-Delta, servis ötelemesi, kombinasyon ve onarım değişiklikleri aldı.
- Eski oturumdan kalan ETest programı, referansta sonradan kaldırılan `FrameInfo.DispLimit` alanını kullanıyordu. Testin kopyası bu satır olmadan derlendi.

---

## 2026-10-03 — Aşama 1: Proje klasörü, ajan mimarisi, kayıt düzeni

**Yapılanlar**
- Çalışma dizini olarak **SFCS** seçildi. Gerekçeler:
  - ETABS 22 ile aynı API ailesi (`ETABSv1`);
  - test modelleri (`525M`, `CivilComp3`);
  - kesit havuzu dosyaları.

  SSO_CF ise ETABS 2016 API'sine bağlı ve kompozit kolon tasarım arayüzü yok.
- SFCS, depo adıyla aynı olan `SteelFamewithCompositeTubeColumn_ETABS` klasörüne kopyalandı. `bin`, `obj`, `.vs` ve `*.suo` dosyaları alınmadı. Orijinal SFCS ve SSO_CF klasörlerine dokunulmadı.
- `Ajan/kur_ajan_mimarisi.ps1` betiği yazıldı ve çalıştırıldı. Betik şu klasörleri kuruyor ve her birine amaç açıklaması (`README.md`) koyuyor:
  - `Ajan/`;
  - `Ajan/Yetenekler/Optimizasyon_Yetenegi`, `Ajan/Yetenekler/ETABS_Kullanim_Yetenegi`, `Ajan/Yetenekler/Sartname_Kullanim_Yetenegi`;
  - `Ajan/Hafiza`;
  - `Ajan/Gorevler`.
- Yazılan dosyalar:
  - `Ajan/Gorevler/AKIS_SEMASI.md`: referanstan çekilecek veriler ve aşamalar (taslak);
  - `Ajan/Gorevler/KUYRUK.md`;
  - `Ajan/Hafiza/PROJE_DURUMU.md`.
- Kök kayıt dosyaları oluşturuldu: `DEGISIKLIKLER.md`, `PROGRAM_KURALLARI.md`, `KULLANIM_KILAVUZU.md`. Eski iskeletin inceleme raporu `KOD_INCELEME_RAPORU.md` aktif klasörden buraya taşındı.
- `.gitignore` eklendi. Depoya girmeyenler:
  - derleme çıktıları;
  - ETABS ikili dosyaları;
  - CSI'ın `AISC14M.xml` dosyası;
  - ETABS model ve analiz dosyaları.
- İlk commit ve push yapıldı (main).

**Tespit**
- Referans proje (`SteelFamewithCompositeColumn_ETABS`) kolonları zaten istenen tipte çözüyor: W profil + beton + donatı, yani `EncasedRectangle`.
- `KOD_INCELEME_RAPORU.md` raporundaki CFT (dolgulu kutu) varsayımı bu nedenle geçersiz. Raporun başına düzeltme notu eklendi.

**Testler**
- Mimari betiği temiz klasörde çalıştırıldı: 7 README oluşturuldu.
- Betik ikinci kez çalıştırıldı: 7 dosyanın hepsi için "zaten var" çıktı, üzerine yazma olmadı.
- Türkçe karakterler Windows PowerShell 5.1'de doğru göründü. Betik BOM'lu UTF-8 olarak kaydedildi.
- Kod değişmediği için derleme testi yapılmadı. Derleme Aşama 2'de, referans kod aktarıldıktan sonra yapılacak.
