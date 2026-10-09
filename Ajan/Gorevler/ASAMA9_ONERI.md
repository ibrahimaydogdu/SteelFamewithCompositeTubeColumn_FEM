# Aşama 9 — Model oluşturucu (ÖNERİ)

- **Tarih:** 2026-10-06.
- **Durum:** onaylandı (2026-10-07); uygulanıyor. Kararlar Bölüm 11'de.
- **Kullanıcı isteği (2026-10-06):**
  - akademik çalışmanın örneklerini üreten ayrı bir program;
  - yükleri (ölü, hareketli, kar, rüzgâr, deprem) ve kombinasyonları Amerikan şartnamelerine göre kendisi tanımlasın;
  - kesit havuzlarını kursun, elemanları gruplasın;
  - unutulabilecek diğer ayarları da kendisi yapsın.
- **Örnek sayısı:** makale çalışması başka bir sohbette belirlendikten sonra karar verilecek. Bu yüzden program **parametre tablosundan toplu üretim** yapabilmeli.
- **Test:** kullanıcı elle yapacak (bkz. Bölüm 8).
- **Dağıtım paketi:** şimdilik gerekmiyor; program exe'den çalıştırılacak. Eski "Aşama 9 — kılavuz, dağıtım" Aşama 10'a kayar.

## 1. Hedef
Parametrelerden, optimizasyon programının **hiçbir elle müdahale gerektirmeden** kullanabileceği bir ETABS modeli (`.EDB`) üretmek. Toplu modda bir tablodaki her satır için bir model üretilir. Program ayrıca her modelin özetini ve `RunBatch.ps1` için hazır koşu listesini yazar.

## 2. Mimari
| Konu | Öneri |
|---|---|
| Yer | Aynı depo, aynı solution içinde **ikinci proje**: `ModelBuilder/` klasörü, `ModelBuilder.exe`. Optimizasyon programına dokunulmaz. |
| Dil / ortam | VB.NET, .NET Framework 4.7.2, ETABS 22 OAPI (optimizasyon programıyla aynı). |
| Ortak kod | Başlangıçta yok, ETABS bağlantı kalıbı kopyalanır. İki program yalnızca **model sözleşmesiyle** birbirine bağlıdır: grup, kesit listesi ve kombinasyon adları (Bölüm 6). |
| Arayüz | 1) **Tablo girişi** (Excel/CSV): her satır bir örnek. 2) Küçük bir **form**: tek örnek kurma, önizleme (plan ve kat sayısı, grup sayısı), tablo seçip toplu üretim. 3) Komut satırı: `ModelBuilder.exe /batch ornekler.csv /out D:\Ornekler`. |
| Çıktılar | Her örnek için `<ad>.EDB`, `<ad>_rapor.txt` (ağırlık, periyotlar, taban kesmeleri, grup listesi). Ayrıca toplu `ornekler_ozet.csv` ve optimizasyon için `runs.csv` (RunBatch biçiminde). |

## 3. Geometri
- **Plan:** düzenli ızgara. X ve Y yönünde açıklık sayısı ve uzunlukları; açıklıklar farklı olabilir (ör. `9;6;9`).
- **Katlar:** kat sayısı, zemin katı yüksekliği, normal kat yüksekliği.
- **Mesnet:** ankastre.
- **Taşıyıcı sistem** (soru 2):
  - (a) **Uzay çerçeve:** bütün kiriş–kolon birleşimleri moment aktaran. 525M bu tiptedir.
  - (b) **Çevre çerçeveleri moment aktaran, iç kirişler mafsallı:** yerçekimi çerçevesi, ABD'de yaygın.
  - (c) İleride çaprazlı (merkezi) çerçeve. Bu öneriye dahil değil.
- **Tali kirişler** (isteğe bağlı): döşeme açıklığını bölen mafsallı kirişler. Varsayılan olarak yok; yük döşemeden doğrudan ana kirişlere aktarılır.
- **Döşeme** (soru 5): her katta tasarlanmayan bir döşeme (membrane / deck) ve **rijit diyafram**. Döşeme ağırlığı ve yük dağıtımı ETABS'e bırakılır.

## 4. Malzeme ve kesit havuzları
- **Çelik:** `A992Fy50`.
- **Dolgu betonu:** f'c parametresi (varsayılan `4000Psi`). Optimizasyon programı betonu modelden okur (Aşama 5 kararı).
- **Kesit havuzları** (ETABS *Auto Select List*):
  - **Kiriş listesi:** W profilleri, derinlik aralığı açıklığa göre süzülür; varsayılan olarak derinlik ≥ L/30, en derin W36 ile sınırlı.
  - **Kolon listesi:** W12–W14 ailesi (W14 tipik).
  - İsteğe bağlı **deprem süzgeci:** AISC 341 Tablo D1.1'e göre süneklik sınıfını sağlamayan W profilleri havuzdan çıkarılır. Sınıf soru 3'e bağlı.
  - Listelerin adları optimizasyon programının `App.config` dosyasındaki adlarla aynı olur: `BeamSectionList` / `ColumnSectionList`. Böylece ilk sınır tasarımı da kısalır.
- **Tüp kesitler:** optimizasyon programının kendi kataloğundan gelir (`TubeSections.xml`). Model oluşturucu bunlara dokunmaz.

## 5. Gruplama (kullanıcının planı, `AKIS_SEMASI.md` Bölüm 5)
| Eleman | Gruplar | Ayrıca |
|---|---|---|
| Kolon | köşe / kenar / iç | kat bandı (ör. 5 katta bir), kolonlar düşeyde aynı hatta kalır |
| Kiriş | kenar / iç | uzunluk sınıfı (farklı açıklık uzunlukları ayrı grup), kat bandı; (b) sisteminde moment ve mafsallı kirişler ayrı |
| Tali kiriş | tek grup ya da kat bandına göre | varsa |

- Her çubuk tam olarak bir tasarım grubunda olur. Program bunu kurduktan sonra denetler.
- **Grup adları anlaşılır olur:** `COL-CORNER-S01-05`, `BM-EDGE-L9.0-S06-10` gibi. Ad yalnızca okunabilirlik içindir; optimizasyon programı grupları adla değil içerikle kullanır.
- **Hibrit kolonlar:** köşe / kenar / iç gruplarından optimizasyon programı **3 kolon yığını** bulur; her yığın kendi geçiş katını alır.
- **Grup sayısı örneği:** 15 kat, 3 katlık bant → 5 bant; 3 kolon sınıfı × 5 = 15 kolon grubu, 2 kiriş sınıfı × 2 uzunluk × 5 = 20 kiriş grubu (açıklıklar 9 m ve 6 m).

## 6. Yükler, kütle ve kombinasyonlar (ASCE 7 + AISC 360-22)
**Yük desenleri** (ETABS tipleriyle; optimizasyon programı yanal yükleri tipinden tanır):

| Desen | Tip | İçerik (varsayılanlar parametredir) |
|---|---|---|
| DEAD | Dead | öz ağırlık (çelik ve döşeme) |
| SDL | Superimposed Dead | kaplama, tesisat, tavan (ör. 1,0 kPa); cephe yükü çevre kirişlerine çizgisel (ör. 4 kN/m) |
| LIVE | Live (Reducible) | ofis 2,4 kPa + bölme duvar 0,72 kPa (ASCE 7 Tablo 4.3-1, 4.3.2); hareketli yük azaltması isteğe bağlı |
| LROOF | Roof Live | 0,96 kPa |
| SNOW | Snow | düz çatı kar yükü pf = 0,7·Ce·Ct·Is·pg. Gerekli girdiler pg, Ce, Ct ve Is. Kar birikmesi (drift) yok. |
| WX, WY | Wind | **ETABS otomatik rüzgâr, ASCE 7.** Girdiler: V, Exposure, Kzt, Kd, Ke ve rijit diyafram. |
| EX, EY | Seismic | **ETABS otomatik deprem, ASCE 7 eşdeğer yanal kuvvet.** Girdiler: Ss, S1, zemin sınıfı, R, Cd, Ω0, Ie ve TL. ±%5 kaza dışmerkezliği. |

- **Kütle kaynağı:** DEAD + SDL. pf > 1,44 kPa (30 psf) ise kar yükünün %20'si de eklenir (ASCE 7 12.7.2).
- **Modal analiz:** periyotlar raporda yer alır; tepki spektrumu analizi isteğe bağlıdır (soru 4).
- **Dayanım kombinasyonları:** ASCE 7 2.3.1, depremle 2.3.6.
  - Kombinasyonlar açıkça ve denklem numarasıyla adlandırılır, ör. `LRFD-2.3.1-3a`, `LRFD-2.3.6-6`.
  - Rüzgâr ve deprem ± yönlü yazılır.
  - Deprem kombinasyonlarında ρ (soru 3) ve 0,2·SDS·D düşey deprem etkisi bulunur.
  - Kombinasyonlar çelik tasarımı ve kompozit kolon tasarımı için işaretlenir.
  - Gerekçe: ETABS'in varsayılan kombinasyonları yerine açık tanım, makalede kaynak gösterilebilir ve denetlenebilir.
- **Öteleme kontrolü:** optimizasyon programının bugünkü düzeni kullanılır.
  - Rüzgâr için servis ötelemesi WX/WY ile yapılır (program `SRV_` durumlarını kendisi kurar).
  - Deprem için ASCE 7 12.8.6'ya göre Cd/Ie büyütmesi uygulanır. Model oluşturucu, Cd ve Ie'yi örneğin optimizasyon ayar dosyasına yazar.
  - Öteleme sınırları (H/400, h/400 ya da ASCE 7 Tablo 12.12-1) parametredir.

## 7. "Unutulabilecek" ayarlar (otomatik)
- Rijit diyaframlar (her kat), kütle kaynağı, modal durum.
- **Çelik tasarım tercihleri:**
  - AISC 360-22;
  - analiz yöntemi: Direct Analysis (soru 6);
  - çerçeve tipi SMF / IMF / OMF, deprem tasarım kategorisine göre;
  - D/C sınırı. Optimizasyon programı bu sınırı modelden okur (Aşama 5.1).
- **Kompozit kolon tasarım tercihleri:** AISC 360-22.
- Tasarım prosedürleri: kolon ve kirişler *Steel Frame Design*; tali kirişler ayrı grupta.
- Mafsallı kirişlerde uç serbestlikleri (sistem b).
- Birim sistemi: kN-m. ETABS kesit kütüphanesi `AISC16M`.
- **Son denetim:** model gizli ETABS'te bir kez analiz edilir ve şunlar raporlanır:
  - analiz hatası ya da uyarı;
  - grup çakışması / grupsuz çubuk;
  - yük deseni tipleri, kombinasyon işaretleri;
  - toplam ağırlık, ilk 3 periyot, taban kesmeleri (deprem ve rüzgâr).

## 8. Test (kullanıcının isteğiyle sınırlı)
- Kullanıcı modelleri ETABS'te elle denetleyecek ve optimizasyon koşularını kendisi yapacak.
- **Benden:** yalnızca derleme ve tek bir küçük örnek (ör. 3 kat, 2 × 2 açıklık) üretilip modelin açıldığının ve analizin hatasız tamamlandığının görülmesi. Bu da istenmezse atlanır (soru 9).

## 9. Uygulama adımları (her biri ayrı commit)
| Adım | İçerik |
|---|---|
| 9.1 | Proje iskeleti, parametre yapısı (XML/CSV), geometri, malzeme, kesit havuzları, gruplama |
| 9.2 | Yük desenleri: ölü, ek ölü, hareketli, çatı, kar; otomatik rüzgâr ve deprem; kütle, diyafram, modal |
| 9.3 | Kombinasyonlar, tasarım tercihleri, son denetim ve rapor |
| 9.4 | Form, toplu üretim, `runs.csv`, kılavuz bölümü, DEGISIKLIKLER |

## 10. Kullanıcıya sorular
1. **ASCE 7 sürümü:** ASCE 7-22 mi, ASCE 7-16 mı? ETABS 22.6'nın desteklediği otomatik yük tipleri önce API'de denetlenir.
2. **Taşıyıcı sistem:** (a) uzay çerçeve, (b) çevre moment çerçevesi + iç mafsallı kirişler, ya da ikisi de seçenek mi? Önerim: ikisi de seçenek, varsayılan (b).
3. **Deprem:** deprem tasarım kategorisi ve çerçeve sınıfı (SMF: R = 8, Cd = 5,5; IMF; OMF) örnekten örneğe değişecek mi? ρ = 1,0 mı, 1,3 mü?
4. **Deprem analizi:** yalnızca eşdeğer yanal kuvvet mi, yoksa tepki spektrumu da mı (12.9.4'e göre ölçeklenmiş)?
5. **Döşeme:** tasarlanmayan döşeme ve rijit diyafram uygun mu, yoksa döşeme modellenmeyip yük kirişlere mi verilsin?
6. **Çelik tasarım yöntemi:** Direct Analysis Method (AISC 360 Bölüm C, ETABS varsayılanı) uygun mu?
7. **Parametre girişi:** Excel/CSV tablosu ve form birlikte uygun mu? Excel dosyası mı (her sütun bir parametre), yoksa CSV mi tercih edilir?
8. **Proje yeri:** aynı depo ve solution içinde ikinci proje (`ModelBuilder/`) uygun mu?
9. **Test:** derleme ve tek küçük örnekle duman testini benim yapmam uygun mu, yoksa tamamen size mi bırakayım?
10. **Kapsam dışında bırakılanlar** (onay): çaprazlı sistemler, düzensiz planlar ve geri çekmeler, kar birikmesi, temel ve zemin etkileşimi. Bunlardan biri gerekli mi?

## 11. Kullanıcı kararları (2026-10-07)
1. **ASCE 7-22.**
2. **Taşıyıcı sistem:** varsayılan çevre çerçevesi (b); uzay çerçeve (a) seçenek olarak kalır.
3. **Çerçeve sınıfı:** parametre; varsayılan **SMF** (R = 8, Cd = 5,5, Ω0 = 3). Yüksek katlı yapıya uygun olan sınıf SMF'dir; ρ varsayılan 1,0 (parametre).
4. **Deprem analizi: tepki spektrumu.** Kullanıcı görüşümü sordu: ASCE 7'de tasarım kategorisi D–F ve T > 3,5·Ts olan esnek yüksek katlı yapılarda eşdeğer yanal kuvvet yöntemi tek başına yetmiyor (Tablo 12.6-1). Bu nedenle:
   - tepki spektrumu (modal, CQC) ana deprem yükü;
   - eşdeğer yanal kuvvet yalnızca ölçekleme referansı olarak tanımlanır (taban kesmesi V; 12.9.1.4);
   - kat ötelemeleri 12.8.6'ya göre Cd/Ie ile büyütülür.
5. **Döşeme:** rijit diyafram (döşeme tasarlanmaz).
6. **Çelik tasarım:** Direct Analysis Method.
7. **Parametre girişi:** CSV.
8. **Yer:** aynı depoda ikinci proje (`ModelBuilder/`).
9. **Test:** derleme ve duman testini ben yaparım; esas testi kullanıcı yapar.
10. **Kapsam dışı (ileride dahil edilebilir; not):** çaprazlı sistemler, düzensiz plan ve geri çekmeler, kar birikmesi (drift), zemin–yapı etkileşimi.

## 12. Uygulama sırasında bulunan değişiklik (2026-10-07)
ETABS 22.6'da **otomatik** ASCE 7-22 yük desenleri API ile kurulamıyor:
- `LoadPatterns.AutoSeismic`: ASCE 7-16 için `SetASCE716_1` çalışıyor (eski `SetASCE716` hata veriyor); ASCE 7-22 için metot yok. `AutoWind` arayüzü boş.
- Veritabanı tablolarında `AutoLoad` alanı içe aktarılamıyor (alan açıklaması: "auto load patterns are not imported"); otomatik desen satırları sessizce reddediliyor.

Bu yüzden Bölüm 6 şöyle uygulanıyor:
- **Deprem:** ASCE 7-22 tepki spektrumu fonksiyonu (`Functions - Response Spectrum - ASCE7-22` tablosu) ve `RSX` / `RSY` tepki spektrumu durumları ETABS'te kuruluyor. Eşdeğer yanal kuvvet ETABS deseni olmadan, ASCE 7-22 12.8'e göre sayısal hesaplanıyor (Ta, Cu, Cs, V) ve spektrum durumu en az `SpectrumScaleMin × V` taban kesmesine ölçekleniyor (12.9.1.4). Hesap raporda yazılı.
- **Rüzgâr:** ASCE 7-22 Bölüm 26–27 (Directional Procedure, kapalı bina) basınçları program tarafından hesaplanıyor; kat düğümlerine kuvvet olarak uygulanıyor (rüzgâr alan yüzünde basınç, sotavent yüzde emme; 16 psf asgari yük). Kuvvetler raporda kat kat listeleniyor. Durumlar: WX, WY (Durum 1) ve %75'lik eşzamanlı durum (Durum 3, kombinasyonlarda 0,75 WX + 0,75 WY). Burulmalı Durum 2 ve 4 sonraki sürüme bırakıldı.
- **Hareketli yük azaltması, gerçek rüzgâr tüneli / esnek bina gust faktörü:** kapsam dışı; `WindGust` kullanıcı değeridir.
