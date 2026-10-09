# Gelecek çalışma: harici FEM ve tasarım çözücü (not; mevcut projede yapılmayacak)

- **Tarih:** 2026-10-08.
- **Durum:** yalnızca not. Kullanıcı kararı: *bu işlem mevcut projede yapılmayacak*; süreç (Aşama 10 vb.) bitince birlikte tartışılacak.

## Fikir (kullanıcı)
Optimizasyon süresini kısaltmak için:
1. Optimizasyon sırasında **elle yazılmış harici bir FEM analiz programı ve tasarım çözücü** kullanmak (ETABS'e her aday tasarımda gitmemek).
2. **ETABS'i yalnızca doğrulama için** kullanmak (arama sonunda en iyi tasarımlar, ya da belirli aralıklarla).

## Neden önemli (bu projeden gözlemler)
- Analiz başına süre, tek tasarım için: 525M modelinde ~35 s (analiz ~20 s + çelik tasarım ~14 s); 4 katlı örnekte ~14 s. 500 analizlik bir koşu 2 saat ile birkaç saat sürüyor; 15–25 katlı örneklerde çok daha uzun.
- ETABS örnekleri başına bellek büyüyor (yeniden başlatma gerekiyor), lisans/örnek sayısı eşzamanlı koşuyu sınırlıyor (bu makinede 3).
- ETABS gizli pencerelerde bekleyebiliyor (bu projede `DialogGuard` ile giderildi); API'nin tuhaflıkları çok (örn. `PropFrame.GetNameList`, otomatik yük API'sinin eksikliği, tablo içe aktarma kuralları).

## Tasarım soruları (tartışma için)
1. **Doğruluk ölçütü:** harici çözücünün ETABS ile ne kadar uyuşması gerekir (kesit kuvvetleri, öteleme, D/C oranları)? Bu projede kompozit kolon iç hesabı için ETABS/iç hesap oranı ≤ %1–4 elde edildi ve sonda ETABS ile doğrulama yapıldı; aynı kalıp kullanılabilir.
2. **Analiz çekirdeği:** 3B çerçeve, rijit diyafram, P-Δ / Direct Analysis (ikinci mertebe, τb), modal analiz ve tepki spektrumu (CQC), kazara dışmerkezlik, ASCE 7-22 kombinasyonları. Hesabı hızlı yapmak için statik yoğunlaştırma / diyafram serbestlik dereceleri, kesit değişiminde kısmi yeniden çözüm (Sherman–Morrison) düşünülebilir.
3. **Tasarım çekirdeği:** AISC 360-22 (I ve H kesitler: kesit sınıfı, Cb, LTB, eğilme + eksenel etkileşimi, kesme), AISC 341 (güçlü kolon–zayıf kiriş, süneklik), kompozit kolon (I2.2, I3.4). Kütüphane kesit özellikleri AISC16M.xml'den okunur.
4. **ETABS ile döngü:** arama harici çözücüyle; her N analizde ya da en iyi k tasarım için ETABS doğrulaması; tutarsızlık varsa harici çözücünün **sistematik düzeltme çarpanı** (bu projede `CompositeStrengthFactor` benzeri) ya da ETABS sonucuyla yeniden başlatma.
5. **Mimari:** optimizasyon yöntemleri (16 yöntem) ve kesit/ gruplama altyapısı aynı kalır; yalnızca `ETABS_Class.Evaluate` yerine bir `IStructuralEvaluator` arayüzü: `EtabsEvaluator` (mevcut) ve `ExternalEvaluator`. Bu, `OptimizationClass`'ın ETABS'e doğrudan bağlılığının ayrılmasını gerektirir (şimdi `ETABSModel` alanı ve `ETABS_Class` türü).
6. **Doğrulama stratejisi:** aynı tasarım vektörü iki çözücüde; oranların dağılımı; en iyi tasarımın ETABS'te uygun çıkma oranı; yöntem sıralamasının iki çözücüde aynı kalması.
7. **Kapsam:** önce yalnızca çelik, sonra kompozit ve hibrit kolon; model oluşturucudaki (`ModelBuilder`) örneklerle (düzenli ızgara, çevre/uzay çerçeve) başlamak yeterli geometri çeşitliliği verir.

## Bu projede bu işe hazırlık olan şeyler
- `ModelBuilder` (CSV → parametrik ETABS modeli) ve `examples_summary.csv`: test örnekleri.
- ETABS sonuçları için taban değerler: 525M gömülü mod regresyonu; 4 katlı örnekte en iyi maliyet 2587,6 (SSO, 500 analiz).
- Kompozit kolon iç hesabı (`CompositeColumn.vb`) ve ETABS ile karşılaştırma altyapısı.
- Ölçekleme kuralları (ASCE 7-22 12.9.1.4) ve rüzgâr kuvvetleri `ModelBuilder`'da sayısal olarak hesaplanıyor (harici çözücüye taşınabilir).

## Henüz karar verilmemiş
- Harici çözücünün dili/ortamı (VB.NET aynı çözümde mi, C#/C++ ayrı kütüphane mi), paralel hesap (CPU çekirdekleri), yalnız çelik mi.
- Makalede ETABS ile karşılaştırma tablosu için hangi örneklerin kullanılacağı.
