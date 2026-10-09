# Sosyal Örümcek Algoritması (SSO): Fortran'dan VB.NET'e çeviri notları

- **Tarih:** 2026-10-03 (Aşama 3–4).
- **Kod:** `SocialSpider.vb` (`OptimizationClass.Main_SocialSpider`). Program içindeki adı "Social Spider (SSO)"; 16. yöntem, `OptMethod_.SocialSpider = 15`.

## 1. Kaynaklar

Kaynaklar `myprojects\Algoritmalar\fortran\SocialSpider\` klasöründe:

| Kod | Dosya | Tarih | Notlar |
|---|---|---|---|
| F1 | `SSO_Column\SSO.f90` | 2015 | Tek popülasyon dizisi `Pop` (önce dişiler, sonra erkekler). Ek bir **spider jump** adımı var. |
| F2 | `SSO_Frame\SSO.f90` | 2018 | Çerçeve sürümü: ayrı `f` ve `m` dizileri, bağımlı grup kontrolleri, aynı tasarımı yeniden analiz etmeme (`isame`). Alt programın adı yanlışlıkla `Cuckoo`. |
| F3 | `SSO.m` | 2015 | MATLAB sürümü: sürekli ve ayrık değişkenler, hareketlerden sonra tüm popülasyonun yeniden değerlendirilmesi. |
| — | Cuevas, Cienfuegos, Zaldívar, Pérez-Cisneros (2013), *A swarm optimization algorithm inspired in the behavior of the social-spider*, Expert Systems with Applications 40, 6374–6384 | — | algoritmanın özgün tanımı |

## 2. Çeviride alınan kararlar

| Konu | Uygulama | Kaynak |
|---|---|---|
| Dişi sayısı | `Nf = floor((0.9 − rand·0.25)·N)`, 1 ile N−1 arasında; koşu başında bir kez | F1–F3, makale |
| Ağırlık | `w = (en kötü − J)/(en kötü − en iyi)`; J ceza eklenmiş maliyet. Tüm maliyetler eşitse w = 1, sonsuz maliyette w = 0. | makale (F1–F3'te 1/Obj ile aynı sıralama) |
| Titreşim | `Vib = w_k·exp(−d²)`; d, arama uzayının köşegeniyle normalize edilmiş uzaklık | F1–F3 |
| Dişi hareketi | `rand < PF` ise çekim, değilse itme: `f ± [α·Vibc·(sc − f) + β·Vibb·(sb − f)] + δ·(rand − ½)`. sc: daha büyük ağırlıklı en yakın örümcek; sb: **en iyi örümcek**. | makale; F1/F2 (sc tanımı) |
| Erkek hareketi | Baskın erkek (ağırlığı erkeklerin medyanından büyük): `m + α·Vibf·(sf − m) + δ·(rand − ½)`. Diğerleri: `m + α·(erkeklerin ağırlıklı ortalaması − m)`. | F1–F3, makale |
| Çiftleşme | Her baskın erkek için, her değişkende `|f − m| ≤ yarıçap·(Ub − Lb)` koşulunu sağlayan dişiler seçilir. Değişken, erkek ve bu dişiler arasından ağırlıklara göre rulet ile alınır. Yavru en kötü örümcekten iyiyse onun yerine geçer ve **cinsiyetini alır**. | F1/F2 (değişken bazında pencere, yarıçap 0,5); makale (yerine geçme ve cinsiyet) |
| Kabul | *If better* (varsayılan) ya da *Always* | F2 `greedyselection` / makale |
| Spider jump (isteğe bağlı) | Her değişken `0,7 + 0,25·w` olasılıkla korunur, yoksa rastgele seçilir. Yeni örümcek en kötüden iyiyse onun yerine geçer. | F1 `spiderjumpEq = 1` |
| Aynı tasarım | Kolonide zaten bulunan bir tasarım analiz edilmez; bir değişken bir kesit kaydırılır. | F2'deki `isame` kontrolünün geliştirilmiş hali. Fortran bu durumda hiç analiz yapmıyordu; ilerleme duruyordu. |
| Ayrık değişken | Sürekli adım yuvarlanır ve kesit sınırlarına kırpılır (`ToMember`) | diğer 15 yöntemle aynı |
| Kısıt | Programın ceza ve onarım altyapısı kullanılır. Fortran'daki uyarlanabilir tolerans (`ErrCalc`, `Tolerans`) alınmadı. | çerçeve altyapısı |

## 3. Fortran kodundaki hatalar (taşınmadı)

1. **Sınır kırpması yanlış değişkene uygulanıyor** (F2, satır 153–155, 202–204, 274–276, 354–356). `Sect(k)` ve `LimitUpSect/LowSect` kullanılıyor. `CheckColCons ≠ 1` ise `k`, önceki döngüden kalan değer, sınırlar da başlangıç döngüsündeki son grubun sınırları. Sonuç olarak değişkenler kendi sınırlarına kırpılmıyor.
2. **Yuvarlama yanlış diziye yazılıyor** (F1 satır 159 ve 163, F2 satır 268 ve 272). Yukarı yuvarlama `Sect(j)` yerine eski konuma (`m(i,j)` / `Pop(i+Nf,j)`) yazılıyor. Hem yeni tasarım yuvarlanmıyor hem de eski erkek bozuluyor.
3. **İtme hareketinin yuvarlama koşulu tutarsız** (F1 satır 120, F2 satır 201). Koşulda `f(iBestF,j) − sect(j)` ve `− f(i,j)` geçiyor; hareketin kendisinde ise `f(iBestF,j) − f(i,j)` var.
4. **En iyi örümcek ve en iyi dişi karışıyor.** Titreşim `Vibb` en iyi örümcekle (`ibestS`) hesaplanıyor, hareket ise en iyi dişiye (`iBestF`) doğru yapılıyor. F2'de en iyi örümcek erkekse `f(ibestS,j)` dişi dizisinin dışına taşıyor (satır 116). Çeviride ikisi de en iyi örümcek (makale).
5. **Rulet olasılıklarının toplamı 1'den büyük.** `Ps(ik+1) = WM(i)/top` hesabında `top` erkeğin ağırlığını içermiyor. Çeviride olasılıklar erkek dahil normalize ediliyor (`Roulette_wheel`).
6. **Aynı `rand` iki kez kullanılıyor** (F1 satır 115–116). PF kararı ve gürültü terimi `δ·(rand − 0.5)` aynı sayıyı kullanıyor. Gürültü bu yüzden kararla ilişkili. Çeviride ayrı sayılar kullanılıyor.
7. **Alt programın adı yanlış:** F2'de `subroutine Cuckoo` (Guguk kuşu algoritmasından kopyalanmış).
8. **Sabit üst sınır:** `iterationmax = 500` (tolerans azaltma), `UpValue = 272` (kesit sayısı kodun içine yazılmış). Çeviride yok.

## 4. Testler (Aşama 3–4)

- **MathTest** (dişli treni, 4 değişken, 3000 analiz, 20 örümcek, 3 tohum): SSO hatasız çalıştı; en iyi değerler 2,31E-11, 1,18E-09 ve 2,70E-12. Diğer 15 yöntemin çıktısı Aşama 2'deki referans çıktıyla birebir aynı kaldı.
- **SSO testi:** 7 parametre bileşimi × N = 20/5/2 × 3 tohum. Sonuçlar:
  - hata, sınır dışı değişken veya durma yok;
  - dişi sayısı her döngüde korundu;
  - yedek XML gidiş-dönüşünde cinsiyetler ve dişi sayısı aynı kaldı.
  - Döngü başına analiz sayısı ≈ 1,0–1,15·N; sıçramayla ≈ 1,35–1,5·N.
- **Form:** "Social Spider (SSO)" listede 16. sırada; parametre kutusu katalogdan kuruluyor.
- **ETABS uçtan uca testi:** sonuçlar `DEGISIKLIKLER.md`, Aşama 3–4'te.
