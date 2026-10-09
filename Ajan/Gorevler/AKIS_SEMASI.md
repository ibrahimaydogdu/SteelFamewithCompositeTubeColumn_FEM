# Akış Şeması (Pipeline)

- **Durum:** 2026-10-03. Aşama 1–4 tamamlandı. Aşama 5 ve 6 için kullanıcı kararları alındı (Bölüm 4).
- **Referans:** `..\SteelFamewithCompositeColumn_ETABS`, sürüm 2026.10.3. Aşama 1–17 tamamlanmış ve test edilmiş.

## 0. Hedef (kullanıcı kararları, 2026-10-03)

| Konu | Referans proje | Bu proje |
|---|---|---|
| Kompozit kolon tipi | gömülü: W profil + beton + donatı (`EncasedRectangle`) | **dolgulu tüp:** çelik kutu (CFT, `FilledTube = 29`) veya boru (CFP, `FilledPipe = 30`) + beton |
| Kolon tipi seçimi | tüm kolonlar birlikte çelik **ya da** kompozit (`FormInfo.CompositeColumns`) | **hibrit:** kolon grubu başına çelik/kompozit; hem kullanıcı belirleyebilir hem optimizasyon değişkeni olabilir. Amaç: "belirli kata kadar kompozit, üstü çelik" geçişinin optimizasyonu. |
| Döşeme | — | isteğe bağlı: kompozit döşeme tasarımı |
| Yöntemler | 15 metasezgisel | 15 + **Sosyal Örümcek Algoritması (SSO)**; Fortran'dan çevrildi (Aşama 3–4) |
| Lisans / depo | MIT | MIT; ETABS modelleri (`.EDB`, `.$et`) depoya girer |

## 1. Referanstan çekilen ve çekilecek veriler

| # | Kaynak (referans) | Hedef yetenek | Durum |
|---|---|---|---|
| R1 | `PROGRAM_KURALLARI.md` | tümü | Aşama 2'de alındı (Bölüm B) |
| R2 | `AISC360_22_…md`, `AISC360_16_…md` | Şartname | Aşama 2'de alındı. Gömülü kesit içindir; CFT/CFP için I2.2 özeti Aşama 4'te eklenecek. |
| R3 | `CompositeColumn.vb`, `EncasedSections.xml` | Şartname | Aşama 2'de alındı. CFT/CFP hesabı aynı yapıyla eklenecek. |
| R4 | `ETABSClass.vb` | ETABS | Aşama 2'de alındı |
| R5 | `OptimizationClass.vb`, `OptimizationMethods.vb`, `Structures.vb` | Optimizasyon | Aşama 2'de alındı |
| R6 | `MainForm*`, `ExcelExport.vb`, `App.config`, `ApplicationEvents.vb` | arayüz | Aşama 2'de alındı |
| R7 | `DEGISIKLIKLER.md` | Hafıza | referans değerler ve bilinen farklar `Hafiza/` altına özetlenecek |
| R8 | Referans projenin derleme ve test yöntemi | ETABS / Hafıza | Aşama 2'de kullanıldı. MathTest ve ETest güncellendi. |

Fortran kaynakları (`Algoritmalar\fortran\SocialSpider\`):

| # | Dosya | İçerik | Not |
|---|---|---|---|
| F1 | `SSO_Column\SSO.f90` (2015) | SSO ana döngüsü: dişi/erkek örümcekler, titreşim, çiftleşme | `subroutine SSO`; asıl referans |
| F2 | `SSO_Frame\SSO.f90` (2018) | çerçeve sürümü, bağımlı gruplar | alt programın adı yanlışlıkla `Cuckoo`; F1 ile karşılaştırılacak |
| F3 | `SSO.m` (MATLAB) | algoritmanın özgün tanımı (Cuevas vd. 2013) | çapraz kontrol |
| F4 | `Composite_Design\SSO_Frame_Com\Source1.f90` | `filled_composite`, `encased_composite` | CFT formülleri için çapraz kontrol |

## 2. Geliştirme aşamaları

```
Aşama 1  Ajan mimarisi + depo + kayıt dosyaları                          [bitti]
Aşama 2  Referans kodun aktarılması, eski iskeletin kaldırılması          [bitti]
   │       test: derleme, MathTest = referans, 525M kopyası ETABS testi = referans
Aşama 3–4 Fortran SSO incelemesi + VB.NET çevirisi (16. yöntem)                [bitti]
         notlar: Yetenekler/Optimizasyon_Yetenegi/SSO_CEVIRI_NOTLARI.md
Aşama 5  Dolgulu tüp kolon (CFT/CFP)                                         [bitti]
   │       - kesit kataloğu: kutu/boru profil listesi + beton
   │       - ETABS'te kesit tanımı (OAPI'de Set metodu yok → DatabaseTables; API testiyle doğrulanacak)
   │       - iç çözücü: AISC 360-22 I2.2 (kompakt/narin sınıfı, Pno, EIeff, Mn, etkileşim)
   │       - ETABS DesignCompositeColumn ile karşılaştırma ve kalibrasyon
Aşama 6  Hibrit tasarım: kolon grubu başına çelik/kompozit                    [bitti]
   │       - seçenekler: (a) grup başına kullanıcı seçimi; (b) tip değişkeni optimizasyona eklenir
   │       - maliyet: çelik + beton (+ kalıp yok, tüp kalıp işlevi görür)
Aşama 7  Uçtan uca koşu: SSO + diğer yöntemler, final ETABS doğrulaması, Excel  [bitti]
Aşama 8  Kompozit döşeme: atlandı; proje sonunda ayrı program önerisi          [karar 2026-10-06]
Aşama 9  Model oluşturucu program (ayrı exe; öneri ASAMA9_ONERI.md; bkz. Bölüm 5)
Aşama 10 Kılavuz, README (dağıtım paketi şimdilik gerekmiyor)
```

Her aşamada izlenen adımlar:
1. Öneri sunulur ve onay beklenir.
2. Uygulanır.
3. Test programlarıyla doğrulanır.
4. `DEGISIKLIKLER.md` dosyasına "Aşama N" olarak yazılır.
5. Hafıza ve kuyruk güncellenir.
6. Commit ve push yapılır.

## 3. Yeteneklere göre kurallar (özet)

- **ETABS:**
  - yalnızca geçici kopya üzerinde çalışılır; açık ETABS oturumuna bağlanılmaz;
  - yeniden başlatma için EDB kullanılır;
  - kombinasyon seçimi her tasarımdan önce kontrol edilir.
- **Şartname:**
  - AISC 360-22 varsayılandır, 360-16 seçilebilir;
  - formül değişikliğinden sonra regresyon testi yapılır.
- **Optimizasyon:**
  - her yöntem `MethodCatalog` üzerinden çağrılır;
  - tohum kayıt altına alınır;
  - ETABS çözücü gürültüsü yaklaşık 1e-13.

## 4. Açık sorular (ilgili aşamada sorulacak)

1. **Aşama 5 — karar (kullanıcı, 2026-10-03):** Tüp kesitler **ETABS kütüphanesinden** alınacak.
   - Kaynak: ETABS kurulumundaki `Property Libraries`, örn. `AISC16M.xml`. HSS dikdörtgen/kare kesitler CFT için, boru (Pipe/HSS round) kesitler CFP için kullanılacak.
   - Hâlâ açık: beton sınıfı (modeldeki beton malzemesi mi?) ve kütüphane süzgeci (yalnızca kare kutu mu, dikdörtgen de mi; en küçük boyut).
2. **Aşama 6 — karar (kullanıcı, 2026-10-03):** Kolon tipi **hem kullanıcı tarafından belirlenebilecek hem de optimizasyon değişkeni olacak.** Amaç, "belirli kata kadar kompozit, üstü çelik" geçişinin optimizasyonu. Tasarım taslağı (Aşama 6'da onaya sunulacak):
   - Her kolon grubu için kullanıcı seçimi: *Steel* / *Composite* / *Optimize*.
   - *Optimize* gruplar kolon hatlarına ve kata göre sıralanır. Her hat için bir **geçiş katı değişkeni** k: k'nın altındaki gruplar kompozit, üstündekiler çelik (k = 0: tümü çelik, k = en üst: tümü kompozit). Böylece "alt katlar kompozit, üst katlar çelik" kurgusu tek değişkenle aranır.
   - Kesit değişkeni grup tipine göre W havuzundan ya da tüp havuzundan seçilir.
   - Maliyet: çelik + beton (tüp kalıp görevi gördüğü için kalıp yok).
3. **Aşama 8:** atlandı. Kullanıcı kararı (2026-10-06): bu programa eklenmeyecek; proje sonunda **ayrı bir döşeme optimizasyon programı** öneri olarak sunulacak (aynı depoda ikinci proje, ortak optimizasyon yöntemleri; döşeme ağırlığı çerçeve programına girdi).

## 5. Gelecek iş: model oluşturucu (kullanıcının planı, 2026-10-05)
- ETABS modelini parametrelerden kuran ayrı bir program yazılacak.
- **Otomatik gruplama planı:**
  - **Kolonlar:** köşe / kenar / iç olmak üzere 3 grup. 525M'de bugün 2 grup var (çevre, orta).
  - **Kirişler:** kenar / iç olmak üzere 2 grup.
  - Farklı uzunlukta kiriş ya da kolon varsa gruplar aynı mantıkla çoğaltılır (ör. uzunluk sınıfına göre).
  - Kat bantları, yani kaç katta bir grup değişeceği, ayrıca tanımlanacak.
- Aşama 6'dan itibaren optimizasyon programı gruplamayı kullanıcıya bırakır, modeldeki grupları olduğu gibi kullanır.
- 460Member'daki grup çakışması (20. kat kolonları hem grup 6'da hem grup 11'de) şimdilik düzeltilmiyor. Kullanıcı ileride model oluşturucuyla yeni model kuracak.
