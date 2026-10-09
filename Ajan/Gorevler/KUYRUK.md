# Görev Kuyruğu

`[ ]` bekliyor · `[~]` sürüyor · `[x]` bitti · `(?)` kullanıcı kararı gerekli

## Aşama 1 — Ajan mimarisi ve depo (2026-10-03) ✔
- [x] SFCS, depo adıyla yeni klasöre kopyalandı.
- [x] `Ajan/kur_ajan_mimarisi.ps1` ile klasör mimarisi kuruldu.
- [x] Akış şeması taslağı, kayıt dosyaları, `.gitignore`. İlk commit ve push yapıldı.

## Aşama 2 — Referans kodun aktarılması (2026-10-03) ✔
- [x] Kullanıcı kararları işlendi: CFT/CFP, hibrit tasarım, kompozit döşeme isteğe bağlı, iskelet kod kaldırılacak, MIT, EDB depoya girecek.
- [x] Eski iskelet kaldırıldı. Modeller `Modeller/` klasörüne taşındı.
- [x] R1–R6 kopyalandı; ad, sürüm (0.2.0) ve GUID'ler uyarlandı.
- [x] Derleme: vbc ve MSBuild (VS 2026).
- [x] MathTest: referansla birebir aynı.
- [x] 525M kopyası üzerinde ETABS testi: referans derlemeyle karşılaştırıldı.

## Aşama 3–4 — Fortran SSO incelemesi ve çevirisi (2026-10-03) ✔
- [x] F1, F2 ve F3 karşılaştırıldı; 8 hata bulundu ve taşınmadı (`SSO_CEVIRI_NOTLARI.md`).
- [x] `SocialSpider.vb`; `OptMethod_.SocialSpider = 15`, katalog girdisi, `Member_.IsMale`, `AlgorithmState_.SpiderFemales`.
- [x] Testler: MathTest (diğer 15 yöntem değişmedi), SSO testi, form görüntüsü, ETABS uçtan uca testi.

## Aşama 5 — Dolgulu tüp kolon (CFT/CFP) (2026-10-03) ✔
- [x] Kararlar:
  - ETABS kütüphanesi + yapma kutular;
  - beton modelden;
  - boru kapalı;
  - kare kutu, en az 300 mm;
  - gömülü seçenek kalıyor (tek program).
- [x] `TubeColumn.vb`, `TubeSections.xml`, ETABSClass katalog yardımcıları, form tip seçimi.
- [x] Testler:
  - TubeTest 23/23;
  - MathTest ve gömülü mod regresyonu birebir aynı;
  - tüp modu uçtan uca: ETABS / iç hesap ≤ 1,041.
- [x] Grup 6 mesajı incelendi (2026-10-04). Neden: ETABS çelik ve kompozit tasarımında D/C oranını 1,0 ile değil **"D/C ratio limit"** ile karşılaştırıyor (modellerde 0,95). Program ise 1,0 kullanıyor; 0,95–1,0 arasındaki elemanları uygun sayıyor.
  - Kanıt: grup 6'nın beş kolonunda yalnızca PMM 0,969 olan işaretlendi, 0,858 işaretlenmedi.
  - API sınır aşımını göstermiyor: sınır 0,45 yapıldığında 0,52'lik elemanların hata ve uyarı alanları boş geldi.
  - Tercih numaraları: `DesignSteel.AISC360_22.GetPreference(37)` ve `DesignCompositeColumn.AISC360_22.GetPreference(18)`, ikisi de 0,95.
  - Sorun referanstan devralındı ve çelik elemanları da etkiliyor.
- [x] Aşama 5.1 (2026-10-04, 0.4.1):
  - D/C sınırı modelden okunuyor (`DCLimit`) ve oranlar sınıra bölünüyor;
  - TBDY 2018 Tablo 9.3 süneklik süzgeci eklendi (varsayılan yüksek).
  - Yeni 525M regresyon değerleri: 7564,91 / 1,4580 ve 7068,84 / 1,8032.

## Aşama 6 — Hibrit tasarım (2026-10-05, 0.5.0) ✔
- [x] Karar: hem kullanıcı hem optimizasyon değişkeni; "belirli kata kadar kompozit, üstü çelik" geçişinin optimizasyonu.
- [x] Öneri yazıldı: `ASAMA6_ONERI.md` (2026-10-05). İçeriği:
  - Steel / Composite / Hybrid modları;
  - yığın başına geçiş değişkeni;
  - Optimize gruplarda iki kesit değişkeni;
  - geçiş geometrisi kuralı;
  - grup çakışması denetimi.
- [x] Kararlar alındı (yığın + grup bazında geçiş, kullanıcı gruplaması, ters düzende uyarı, 460 kalıyor, form tablosu) ve uygulandı.
- [x] Testler:
  - regresyon birebir aynı;
  - yığın bazında ve grup bazında ETABS testlerinde tip uyumsuzluğu 0;
  - uçtan uca test final doğrulamasıyla tamamlandı.

## Aşama 7 — Uçtan uca koşu (2026-10-06) ✔
- [x] Toplu koşu: `/batch` modu ve `tools/RunBatch.ps1`.
- [x] 9 uzun koşu (SSO / HS / ABC × çelik / kompozit / hibrit, 500 analiz, tohum 1); hepsinde final doğrulaması geçti. Sonuçlar `DEGISIKLIKLER.md`'de.
- [x] Paralel koşularda çalışma klasörünün silinmesi hatası bulundu ve düzeltildi.
- [ ] (İsteğe bağlı) Hibrit modda daha büyük bütçe ya da kompozit sonuçtan başlangıç; birden çok tohum.
## Aşama 8 — Kompozit döşeme: atlandı (2026-10-06)
- Kullanıcı kararı (2026-10-06): bu programa eklenmeyecek; proje sonunda **ayrı bir döşeme optimizasyon programı** öneri olarak sunulacak (aynı depoda ikinci proje, ortak optimizasyon yöntemleri; döşeme ağırlığı çerçeve programına girdi).
- Kompozit çerçeve kirişi (yanal rijitliğe katkı) istenirse kolonlardaki gibi bu programa seçenek olarak eklenebilir; o da proje sonu önerisine yazılacak.
## Aşama 9 — Model oluşturucu (ayrı program, örnek üretimi)
- [x] Kullanıcı isteği (2026-10-06): yükler (ASCE 7), kombinasyonlar, kesit havuzları, gruplama ve diğer ayarları otomatik kuran, tablodan toplu örnek üreten program. Testleri kullanıcı elle yapacak.
- [x] Öneri: `ASAMA9_ONERI.md`.
- [x] Kullanıcı kararları alındı (2026-10-07) ve uygulandı: 9.1 geometri, malzeme, havuzlar, gruplama · 9.2 yükler · 9.3 kombinasyonlar, tercihler, denetim · 9.4 form, toplu üretim, belgeler. Ayrıntı `DEGISIKLIKLER.md`.
- [x] Uçtan uca doğrulama: 4 katlı örnek optimizasyon programında 500 analizde uygun tasarım verdi.
- [x] **AISC 341 açık (SeismicProvisions = Yes) örnekleri çözülebilir yapıldı** (2026-10-08): SCWB sürekli kısıt, kiriş alt sınırı açma, süneklik süzgeci ve 2,5 m kiriş desteği (SetOverwrite). 4 katlı örnek 534 analizde uygun tasarım verdi (1726,90). `SeismicProvisions` varsayılanı Yes. Ayrıntı `DEGISIKLIKLER.md` Aşama 9 madde 7.
- [ ] **Karşılaştırma koşusu (kullanıcı kararı 2026-10-09):** aynı 2,5 m destekli 4 katlı modelde `SeismicProvisions = No` ile aynı tohumla (1) 500 analiz koşup AISC 341 hükümlerinin gerçek maliyet artışını (1726,90'a karşı) yan yana koymak. Model: ModelBuilder ile `SeismicProvisions=No`, `BeamBracingSpacing=2.5`.
- [ ] **Kubbe bacağı:** ana kampanya (full run) öncesi orijinal topoloji testlerine odaklanmak (kullanıcı notu 2026-10-09).
- [ ] **Kullanıcı kararı bekleyen / ileride:**
  - güçlü kolon–zayıf kiriş (AISC 341) denetiminin sürekli kısıt olarak optimizasyon programına eklenmesi (şimdilik `SeismicProvisions = No`);
  - tasarıma bağlı RS taban kesmesi ölçeklemesi (ASCE 7-22 12.9.1.4) optimizasyon programında;
  - öteleme belirleyiciyken üst arama sınırının otomatik genişletilmesi (şimdilik `UpperBoundMultiplier = 1` elle);
  - `SeismicDriftAmplification` ve `UpperBoundMultiplier` toplu koşuda örnek başına verilebilsin (şimdilik `App.config`, tek sayı);
  - ModelBuilder: burulmalı rüzgâr durumları, esnek bina için G_f, tali kirişler, düzensiz plan / çaprazlı sistem (kapsam dışı notu).

## Aşama 10 — Kılavuz, README, dağıtım (dağıtım paketi şimdilik gerekmiyor)
