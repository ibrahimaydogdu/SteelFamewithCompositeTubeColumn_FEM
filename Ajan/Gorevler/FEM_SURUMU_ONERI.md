# Öneri: ETABS'siz arama yapan FEM sürümü (yeni klasör) — bulgular ve plan

- **Tarih:** 2026-10-09 · **Durum:** ÖNERİ — kullanıcı onayı bekleniyor, kod yazılmadı.
- **İstek (kullanıcı):** programın aynı yeteneklerini koruyan yeni bir sürüm, ayrı klasörde. Fark: arama sırasında ETABS yerine kendi FEM çözücüsü. ETABS yalnızca (1) örnek verisini (geometri, yük) üretmek ve (2) final sonucu doğrulamak için; en iyi çözümün ETABS dosyası kaydedilecek. ETABS–FEM hata payı %2 (esnetilebilir veya sertleştirilebilir).
- Kullanıcının 2026-10-08 kararıyla uyumlu: bu iş mevcut projede değil, ayrı klasörde yapılır (`GELECEK_DIS_COZUCU.md`).

## 1. Bulgular (ölçüldü)

| Konu | Bulgu |
|---|---|
| Ölçülen model özellikleri (525M ve P4, kopya üzerinde) | Yalnızca çubuk elemanlar (alan nesnesi 0), tek rijit diyafram `D1`, mesnet 9 nokta, kesit değiştiricisi (modifier) 0. P4'te 16 çubukta uç mafsalı; 525M'de mafsal yok. Döndürülmüş yerel eksen: 525M'de 75, P4'te 8 çubuk |
| Uç ofsetleri | Her iki modelde otomatik uç ofseti var, **rijit bölge faktörü 0** (ofset var ama rijit sayılmıyor): rijitlik hesabında eksen-eksen boy, tasarımda net boy ve Lb ilişkisi izlenmeli |
| Yük durumları | P4: 35 kombinasyon, RSX/RSY, WX/WY düğüm yükleri, Modal. **525M: yalnızca 3 kombinasyon, Modal, rüzgâr desenleri** (RS yok); öteleme `SRV_` durumlarıyla kontrol ediliyor |
| Hız kazancı (tahmin) | P4: bir değerlendirme 14 s (ETABS); kendi FEM'le (≈300 serbestlik derecesi, 35 kombinasyon, 84 eleman) onlarca ms olması beklenir (yaklaşık 100–500×). 525M: 26–35 s → yaklaşık 0,3–1 s beklenir (≈30–100×; 1400 serbestlik derecesi, mod analizi). **Tahmin; ölçülmedi** |
| Kazanç anlamı | 500 analizlik koşu 2–6 saat yerine dakikalar; makale için 16 yöntem × 10 tohum × 10000 analiz mümkün hâle gelir |

## 2. Ne yapılacak (kapsam önerisi)

**Mimari:** yeni klasör (`SteelFrameFEM`; aynı depo, kardeş klasör) = mevcut projenin kopyası. `ETABS_Class.Evaluate` yerine bir değerlendirici arayüzü (`IStructuralEvaluator`): `FemEvaluator` (arama) ve mevcut `EtabsEvaluator` (başlangıç verisi + final). 16 yöntem, hibrit mantığı, kısıt onarımı, önbellek, yedek, toplu koşu, çıktılar **değişmeden** kalır.

**ETABS'ten bir kez okunan veri** (API): düğümler, çubuklar (uç bağlantı, mafsal, yerel eksen, uç ofseti), kesit özellikleri ve malzeme, mesnetler, diyafram, kütle kaynağı, yük desenleri ve düğüm/çubuk yükleri, yük durumları ve kombinasyonlar, RS fonksiyonu, tasarım tercihleri ve overwrite'lar (LMinor, LTB, K), D/C sınırı.

**FEM çekirdeği:**
1. 3B Euler–Bernoulli (kayma deformasyonlu) çerçeve elemanı, 12 serbestlik derecesi, mafsallar, uç ofsetleri, kesit özellikleri; rijit diyafram kısıtı; seyrek/bantlı çözücü.
2. Modal analiz (kütle kaynağı), tepki spektrumu (CQC, kazara dışmerkezlik).
3. İkinci mertebe: Direct Analysis Method (rijitlik azaltma τb ve ilave yatay yükler), P-Δ.
4. Kombinasyonlar (35 / 53).
5. **Tasarım:** AISC 360-22 çelik (kesit sınıfı, eksenel, eğilme, LTB ve Cb, kesme, H1 etkileşimi, istasyonlar). Kompozit kolon: mevcut iç çözücü (`CompositeColumn.vb`) FEM kuvvetleriyle çalışır.
6. AISC 341: güçlü kolon–zayıf kiriş oranı, süneklik süzgeci, Lb/ry; kat ötelemeleri.

**Doğrulama ve final:** ETABS ile FEM aynı tasarım vektöründe karşılaştırılır; her aşamanın kabul ölçütü ölçülü hata istatistiğidir. Koşu sonunda en iyi tasarım ETABS'te yeniden analiz/tasarım edilir; aşan grup varsa mevcut **koruma** adımları çalışır; `_best.EDB` kaydedilir. FEM–ETABS fark çarpanı (mevcut `CompositeStrengthFactor` gibi) ile arama ETABS'e güvenli tarafta yürütülür.

## 3. Hata payı (%2) için görüşüm
- **Analiz çıktıları** (periyot, yerdeğiştirme, mesnet tepkileri, çubuk kuvvetleri): doğrusal analizde aynı eleman formülasyonuyla **%0,5–1** yakalanır; %2 rahat. İkinci mertebe (DAM) için %2 gerçekçi.
- **Tasarım D/C oranı:** %2 bir **hedef**; ETABS kapalı kaynak olduğundan Cb, istasyon seçimi, DAM ayrıntıları ve kesit sınıfı sınırları deneysel olarak eşlenir. Önerdiğim ölçüt: aynı N ≥ 30 rastgele tasarımda **ortalama ≤ %1, %95'lik dilim ≤ %2, en büyük ≤ %5**, ve FEM ETABS'e göre **iyimser olmamalı** (FEM oranı ≥ ETABS oranı − %2). Aşan fark için fark çarpanı kullanılır.
- **Gevşetme önerisi:** kompozit kolon ve SCWB için %3–5 (ETABS'in kendi iç yaklaşımları yüzünden); ana dayanım oranlarında %2 kalsın. Sertleştirme önerisi yok (%1 fazla maliyetli, kazanç az).
- Kalan fark son ETABS doğrulaması ve korumayla kapatılır; bu yüzden %2 aşılırsa sonuç yanlış olmaz, yalnızca final korumaya daha çok iş düşer.

## 4. Aşamalar ve süre tahmini
Süreler çalışma günü, belirsizlik ±%50 (ETABS davranışının eşlenmesi öngörülemez).

| Aşama | İçerik | Kabul ölçütü (ETABS ile) | Süre |
|---|---|---|---|
| F0 | Yeni klasör, arayüz ayrımı, ETABS veri okuyucu, desteklenmeyen özellikte reddetme | Okunan model ETABS sayılarıyla aynı (düğüm, çubuk, kesit, yük toplamı) | 1,5–2 |
| F1 | Doğrusal statik FEM | Mesnet tepkileri, yerdeğiştirmeler, çubuk uç kuvvetleri ≤ %1 (P4, 525M, tüm desenler) | 2–3 |
| F2 | Modal + tepki spektrumu | Periyotlar ≤ %1, RS taban kesmesi ≤ %2 | 2 |
| F3 | İkinci mertebe (DAM / P-Δ) | Kombinasyonlu kuvvet ve ötelemeler ≤ %2 | 2–3 |
| F4 | AISC 360-22 çelik tasarım | Grup D/C: ölçüt Bölüm 3 (100 rastgele tasarım) | 4–6 |
| F5 | AISC 341 (SCWB, süneklik, Lb/ry), kompozit/hibrit entegrasyonu | SCWB oranı ≤ %3; kompozit ≤ %3–5 | 3–4 |
| F6 | Optimizasyon entegrasyonu, final ETABS doğrulama + koruma, `_best.EDB` | P4 ve 525M'de aynı tasarım için maliyet/ceza FEM ≈ ETABS; yöntem sıralaması korunuyor mu | 3–4 |
| F7 | Doğrulama kampanyası ve belgeler | 500+ analiz koşuları iki sürümde karşılaştırma | 3–4 |

**Toplam yaklaşık 20–28 çalışma günü.** İlk kullanılabilir sürüm (çelik, P4 ve 525M; F0–F4 + F6): yaklaşık 14–20 gün. Her aşama sonunda test, kayıt ve push yapılır; aşama aşama rapor verilir.

## 5. Riskler
1. **ETABS tasarım ayrıntıları** (Cb, istasyonlar, DAM'in ayrıntıları, "Program Determined" seçimleri) kapalı; eşleme deneyle yapılır, %2 her kesitte garanti edilemez. Önlem: fark çarpanı + final ETABS koruması.
2. **Model sınıfı sınırı:** yalnızca çubuk elemanlı, rijit diyaframlı, mesnetleri tanımlı modeller. Alan elemanı, esnek diyafram, doğrusal olmayan eleman, rijit bölge faktörü > 0, kesit değiştiricisi → program açık mesajla reddeder (ölçülen iki modelde yok).
3. **Optimum ETABS'te uygunsuz çıkabilir:** FEM'in iyimserliği; final korumaya ek olarak son aşamada birkaç ETABS tabanlı yerel arama (ör. en iyi 5 tasarım ETABS ile doğrulanır) eklenebilir.
4. **Kompozit/hibrit için** FEM kuvvetleri yeterlidir, ancak ETABS'te General kesit (dönüştürülmüş özellik) ile analiz ediliyordu; FEM'de aynı eşdeğer EI/EA kullanılmalı (F5).
5. **Süre:** bu iş yaklaşık üç dört haftalıktır; makale koşuları ETABS sürümüyle beklenebilir veya FEM sürümü hazır olunca yapılır. Karar kullanıcıda.

## 6. Onay istenen kararlar
1. **Yer:** aynı depoda kardeş klasör `SteelFrameFEM` (önerim) mi, ayrı depo mu?
2. **Lineer cebir:** MathNet.Numerics (MIT lisanslı NuGet; yoğun/seyrek çözücü, özdeğer) kullanılsın mı, yoksa tümü kendi kodum mu? Önerim: MathNet + kendi alt uzay yinelemesi. (Lisans kararı sende.)
3. **İlk sürüm kapsamı:** çelik (W), önce ModelBuilder tipi modeller ve 525M; kompozit/hibrit ikinci aşama (F5). Uygun mu?
4. **Hata payı ölçütü:** Bölüm 3'teki ölçüt (ortalama ≤ %1, %95'lik dilim ≤ %2, en büyük ≤ %5; kompozit/SCWB %3–5).
5. **Başlama zamanı:** makale kapsamı belli olmadan başlansın mı, yoksa kapsam kararından sonra mı?
