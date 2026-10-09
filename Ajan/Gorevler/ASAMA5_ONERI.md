# Aşama 5 — Dolgulu tüp kolon (CFT/CFP): inceleme ve öneri

- **Tarih:** 2026-10-03.
- **Durum:** onaylandı ve uygulandı (Aşama 5; ayrıntılar `DEGISIKLIKLER.md` dosyasında). En küçük boyut 300 mm seçildi.

## 1. Kullanıcı kararları
- Tüp kesitler **ETABS kütüphanesinden** alınacak.
- Dolgu betonu **modelde tanımlı** beton malzemesi olacak.
- **Kutu ve boru:** "uygulama sorunu olmazsa ikisi de; kompozit kolon için uygun olan tüm kesitler." Değerlendirme Bölüm 4'te.

## 2. ETABS kütüphanesi (AISC16M.xml, ETABS 22)

| Tip | Adet | Boyut aralığı | AISC 360-22 Tablo I1.1a (Fy = 345 MPa) |
|---|---|---|---|
| Kutu (`STEEL_BOX`) | 525 (126 kare, 399 dikdörtgen) | 38–864 mm | 435 kompakt, 57 kompakt olmayan, 33 narin; sınırı aşan yok |
| Boru (`STEEL_PIPE`) | 240 | Ø21–711 mm | 240'ı da kompakt |

- En büyük **kare** kutu 559×559×23,6 mm (HSS558.8X558.8X25.4), en büyük boru Ø711×25,4 mm.
- Kütüphanedeki et kalınlığı tasarım kalınlığıdır (0,93 t; ör. 25,4 → 23,6 mm).
- **Boyut sınırı:** 559×559×23,6 dolgulu kutunun Pno değeri yaklaşık 24 MN (4000Psi beton ile). Referans koşusunda 25 katlı 525M'nin alt katlarında W1000X976 seçilmişti; bu profilin yalnızca çeliğinin karşılığı yaklaşık 43 MN.
  - Bu nedenle 525M'nin alt katlarında kütüphane kutuları yetmeyebilir.
  - Eski SFCS klasöründeki `CompositeSections.txt` dosyası 300×300×12,7 … 1000×1000×31 arasında **yapma (levhadan kaynaklı) kutular** listeliyor. Yüksek binalarda alt kat CFT kolonları uygulamada böyle yapılır.

## 3. API testi (460Member kopyası, ETABS 22.6)

| Konu | Sonuç |
|---|---|
| Tablo adları | `Frame Section Property Definitions - Filled Steel Tube` ve `- Filled Steel Pipe` (içe aktarılabilir). Alanlar: `Name, Material, FromFile, FileName, SectInFile, t3, t2, tf, tw, CornerRad, FillMat, …` (boruda `t3` = çap, `tw` = et kalınlığı). |
| Kesit ekleme | Boyutları verilen kutu ve boru kesitleri hatasız eklendi (`ApplyEditedTables`: 0 hata). `PropFrame.GetNameList(FilledTube/FilledPipe)` yeni kesitleri listeliyor. |
| Kütüphaneden alma (`FromFile = Yes`) | **Çalışmıyor:** satır `FromFile = No` olarak kaydedildi ve 1 uyarı verdi. Kütüphane XML'i program tarafından okunmalı ve boyutlar doğrudan yazılmalı. |
| `GetSectProps` | Dolgulu kesitte brüt dış alanı (b·h) döndürüyor; çelik alanı ve dönüştürülmüş özellikler için kullanılamaz. |
| Grup ataması | `FrameObj.SetSection(grup, kesit, Group)` → 0 |
| Analiz süresi (460Member) | yalnızca `FSec1` ile 32 s; 3 grup yeni dolgulu kesitle 37–50 s |
| ETABS kompozit kolon tasarımı (AISC 360-22) | 200 kolon **530 s**. Seçim 2 gruba (60 kolon) sınırlanınca 89 s, yani kolon başına 1,5–2,7 s. Her değerlendirmede kullanılamayacak kadar yavaş. |
| Sonuç tablosu | `Composite Column Summary - AISC 360-22`: PMMRatio, PRatio, MMajRatio, MMinRatio, VMajRatio, VMinRatio, Message. CFT_559x23.6 için PMM 0,019 (460Member'da yükler küçük). |
| **Boru uyarısı** | CFP_711x25.4 (D/t = 28, kompakt) için ETABS "Section is too slender -- (D/t) high" mesajı verdi ve kontrol yapmadı (oran 0). Nedeni bilinmiyor: ETABS hatası, birim ya da tablo alanı yorumu olabilir. Boru uygulanacaksa ayrıca incelenmeli. |

## 4. Boru kolonlara kiriş bağlantısı (kullanıcı sorusu)
- **Mafsallı (kesme) bağlantı:** boruya kaynaklı tek levha (shear tab) yaygın ve sorunsuzdur.
- **Moment bağlantısı:** 525M ve 460Member modelleri moment çerçevesidir (SMF). Boruda:
  - eğri yüzeye kaynak ve imalat toleransı güçtür;
  - dış halka veya diyafram levhası ya da boru içinden geçen levha gerekir; dolgu betonunun dökümü de zorlaşır;
  - AISC 341'de kompozit özel moment çerçevesi (C-SMF) için önceden onaylanmış bağlantı seçenekleri kutuya göre daha sınırlıdır.
- **Kutu:** düz yüzlü olduğu için bağlantılar (dış diyafram veya içinden geçen levha) daha alışılmış ve ekonomiktir.
- **Sonuç:** boru uygulanabilir, ama moment çerçevesinde bağlantı maliyeti ve detay zorluğu artar. Bu maliyet amaç fonksiyonunda yok. ETABS de boruda yukarıdaki uyarıyı verdi.
  - **Öneri:** kutu varsayılan olsun; boru ayarla açılabilsin (varsayılan kapalı).

## 5. Öneri (Aşama 5 kapsamı)
1. **Kesit havuzu** (`TubeSections.xml`, `EncasedSections.xml` benzeri):
   - Kaynak: ETABS `Property Libraries\AISC16M.xml` (ayardan değiştirilebilir). Program `STEEL_BOX` ve `STEEL_PIPE` kayıtlarını kendisi okur.
   - Süzgeç:
     - tipler: kutu (varsayılan açık), dikdörtgen kutu (varsayılan kapalı), boru (varsayılan kapalı);
     - en küçük dış boyut 200 mm (ayar);
     - Tablo I1.1a sınırını aşan kesit alınmaz.
   - Sıralama: çelik alanına göre.
   - İsteğe bağlı ek liste: **yapma kutular** (ör. `CompositeSections.txt` boyutları). Bölüm 6, soru 1.
2. **Malzeme:**
   - Dolgu betonu modeldeki beton malzemesidir. Modelde bir tane varsa o kullanılır; birden fazlaysa ayardaki ad ya da ilk beton malzemesi.
   - Çelik, kolon gruplarının modeldeki çelik malzemesidir.
3. **ETABS'te tanım:**
   - Yalnızca kullanılan kesitler oluşturulur: `CFT_<kütüphane adı>` / `CFP_<…>`.
   - Tanım `Filled Steel Tube/Pipe` tablolarına açık boyutlarla yazılır.
   - Referanstaki tablo kuralları geçerli: tüm tablo geri yazılır, model kilidi açılır.
4. **Arama sırasında:**
   - Kolonlar gerçek dolgulu kesitle analiz edilir. Analiz süresi kabul edilebilir; 525M'de ayrıca ölçülecek. Gerekirse referanstaki gibi General kesite geçilir.
   - Kontrol, **iç çözücüyle** yapılır: yeni `TubeColumn.vb` dosyası, AISC 360-22 kuralları:
     - I2.2 (sınıf, Pno, EIeff, C3, Pn);
     - I3.4 eğilme (plastik gerilme dağılımı ve narin kesit sınırları);
     - I4 kesme;
     - I5 etkileşim.
   - ETABS kompozit tasarımı yavaş olduğu için yalnızca **finalde** çalışır. Referanstaki koruma döngüsü ve kalibrasyon katsayısı aynen kullanılır.
5. **Maliyet:** çelik ağırlığı × çelik birim maliyeti + beton hacmi × beton birim maliyeti. Donatı ve kalıp yok.
6. **Form:** *Composite columns* seçeneğinde kesit tipi seçimi: dolgulu kutu / boru (ve Bölüm 6, soru 2'ye göre gömülü). Bu aşamada kompozit moddaki tüm kolon grupları tüp olur; grup başına seçim ve geçiş katı Aşama 6'da gelir.
7. **Testler:**
   - `TubeColumn.vb` birim testleri: AISC Design Examples dolgulu kolon örnekleri ve elle hesap.
   - ETABS ile oran karşılaştırması: 460Member ve 525M kopyaları.
   - Regresyon: MathTest ve 16 yöntem değişmemeli. Gömülü mod korunursa 525M referans değerleri (7121,40 / 2,0706) değişmemeli.

## 6. Kullanıcıya sorular
1. **Yapma kutu listesi:** Kütüphanedeki en büyük kare kutu 559 mm. Alt katlar için 600–1000 mm yapma kutular da havuza eklensin mi? Önerim: evet, ayrı ve isteğe bağlı bir liste.
2. **Gömülü kesit seçeneği:** Referanstan gelen gömülü kompozit kolon (W + beton + donatı) programda seçenek olarak kalsın mı? Önerim: kalsın; karşılaştırma çalışmalarında yararlı, bakım maliyeti düşük.
3. **Boru:** Varsayılan olarak kapalı olsun mu (öneri), yoksa kutu ve boru birlikte mi açılsın?
4. **Süzgeç varsayılanları:** yalnızca kare kutu ve en küçük dış boyut 200 mm uygun mu?
