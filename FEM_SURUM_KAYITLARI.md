# FEM sürümü — kayıtlar

Bu klasör, `SteelFamewithCompositeTubeColumn_ETABS` projesinin (0.5.1, commit 0e0b949'a kadar) kopyasıdır. Fark: arama sırasında ETABS yerine kendi FEM çözücüsü kullanılacak; ETABS yalnızca örnek verisini (geometri, yük) üretmek ve final sonucu doğrulamak için kalacak. Öneri ve kararlar: ETABS sürümündeki `Ajan/Gorevler/FEM_SURUMU_ONERI.md`.

**Kullanıcı kararları (2026-10-09):** klasör `SteelFamewithCompositeTubeColumn_FEM`; hazır matematik kütüphaneleri kullanılsın (MathNet.Numerics 5.0.0, MIT; `packages\` altında, net461 derlemesi); ilk aşama yalnızca çelik, doğrulama ile; ikinci aşamada kompozit ve hibrit; ETABS–FEM hata payı: analiz çıktılarında %0,5–1 hedef, tasarım D/C oranında ortalama ≤ %1, %95'lik dilim ≤ %2, en büyük ≤ %5 (kompozit ve güçlü kolon–zayıf kiriş için %3–5); yetenek kitine (`YETENEKLER_RAPORU.md`) göre araştırma konusu sonra belirlenecek.

## Yapı
| Proje | İçerik |
|---|---|
| `FemCore/` (kütüphane, ETABS'siz) | `FemModel.vb` (veri sınıfları), `FemElement.vb` (Timoshenko çerçeve elemanı, serbest uç yoğunlaştırma, yük vektörleri), `FemChain.vb` (uç ofsetli eleman zinciri), `FemStatic.vb` (doğrusal statik çözücü, rijit diyafram) |
| `FemTool/` (konsol, ETABS gerekir) | `EtabsReader.vb` (ETABS modelini FemModel'e okur), `Program.vb`: `selftest`, `static <model.EDB> [durumlar]`, `info`, `tables` |
| kök (`FrameSap2000`) | Önceki optimizasyon programı (henüz değişmedi) |

## Aşama F0–F1: okuyucu ve doğrusal statik FEM (2026-10-09)

**Eleman:** 3B Timoshenko çerçeve elemanı (EA, GJ, EI22, EI33, kayma alanları As2/As3), 12 serbestlik derecesi. Uç mafsalları (M2, M3 vb.) statik yoğunlaştırma ile. Eşdeğer düğüm yükleri 4 noktalı Gauss ile (yayılı yükler: yerel ve global yönler, trapez; tekil yük ve moment).

**ETABS davranışları (P4 ve 525M üzerinde ölçüldü, FEM bunlara göre kuruldu):**
1. **Yerel eksenler:** `FrameObj.GetTransformationMatrix` 9 değeri döndürür; yerel eksenler matrisin **sütunlarıdır** (satırlar değil). Eksen 1 eleman doğrultusudur.
2. **Uç ofsetleri (otomatik, kolon/kiriş yüzünden):** zincir modeli: eklem – rijit bağ (rz·a) – esnek kısa parça ((1−rz)·a) – **[mafsal]** açıklık – … Mafsallar ofsetin **sonunda** (yüzde) etkir; mafsallı bir kirişin uç kesme kuvveti ofset kolu kadar momentle eklem düğümüne iner (iç kiriş uçları için P4'te 6 kN·m mertebesi). rz = 0 ve rz = 1 deneyleri bu modelle birebir uyuşuyor.
3. **ETABS uç kuvvetleri** açıklık yüzlerinde (net boy) raporlanır; istasyon 0 ve net boy sonu.
4. **Kendi ağırlığı:** düşey elemanlarda eklem–eklem boy, diğerlerinde net boy (ofsetler hariç). Ölçüm: P4'te ETABS toplam Fz = 1001,17 kN = kolon 422,91 + kiriş net 578,27; 525M'de aynı kural tuttu.
5. **Eleman uç kuvveti işaret düzeni:** I ucunda −f, J ucunda +f (f = elemana eklemlerin uyguladığı kuvvet, yerel eksenler) ETABS P, V2, V3, T, M3 ile aynı işaretli; **M2 ters işaretli** (ETABS M2 = −(bu kural)).
6. **Eklemsiz noktalar:** rijit diyafram merkezi gibi hiçbir elemana bağlı olmayan düğümler (ETABS ekler) modele alınmaz.
7. **Rijit diyafram:** `PointObj.GetDiaphragm` seçeneği `DefinedDiaphragm` olan noktalar, `D1@kot` anahtarıyla kat kat bağlanır (kat başına 3 ana serbestlik derecesi: Ux, Uy, Rz).
8. **Kardinal nokta 8 (üst orta), `StiffTransform = False`:** rijitlik dönüştürülmez; ofset uygulanmaz (525M'de 300 kiriş böyle). `StiffTransform = True` olursa merkez ofseti (Ecc2 = ∓d/2) uygulanır (bu durum ETABS ile henüz denenmedi).
9. **Doğrudan Analiz (DAM) rijitlik azaltması:** bir çelik tasarım çalıştırıldıktan sonra ETABS sonraki analizlerde EA ve EI'yı **0,8** ile çarpar (kayma alanı ve burulma azaltılmaz; τb = 1). P4 modelinde tasarımdan önce azaltma yok, tasarım çalıştırıldıktan sonra var; 525M modelinde (tasarım sonuçları kayıtlı) baştan var. Etkisi: yerdeğiştirmeler ×1,25; kuvvetler değişmez. **Optimizasyon programı için sonuç:** ETABS analizi bu durumdan etkilenir (ilk değerlendirme azaltmasız, sonrakiler azaltmalı olabilir); FEM'de `FemModel.AxialFactor` ve `FlexuralFactor` (0,8) olarak uygulanır.

**Testler**
| Test | Sonuç |
|---|---|
| `FemTool selftest` (ETABS'siz): konsol, ankastre ucu tekil ve yayılı yük, basit mesnetli kiriş; Timoshenko kapalı çözümleri | 13 kontrol, hepsi bağıl hata < 1e-9 |
| **P4** (4 kat, 84 eleman, 16 mafsallı, 8 döndürülmüş kolon, otomatik ofset), DAM ile (tasarım çalıştırıldıktan sonra); Dead, Live, SDL, RoofLive, PartMass, WX, WY | yerdeğiştirme ≤ %0,003; dönme ≤ %0,025; mesnet tepkisi ≤ %0,002; tüm uç kuvvetleri ≤ %0,005 (maksimuma göre) |
| **525M** (25 kat, 525 eleman, kardinal nokta 8, ofsetler, diyafram yok), DAM ile; Dead, Live, Live_r, WindX, WindY | yerdeğiştirme, dönme, tepki ve uç kuvvetleri **%0,000** (4 anlamlı hane) |
| Süre | FEM çözümü P4: 100–140 ms (7–10 desen, yoğun Cholesky); 525M: ~1 s (1350 serbestlik derecesi, 5 desen). ETABS: P4 ~14 s, 525M 26–35 s |

**Sonraki:** F2 modal analiz ve tepki spektrumu; F3 ikinci mertebe (P-Δ / Direct Analysis); F4 AISC 360-22 çelik tasarım; sonra AISC 341, optimizasyon entegrasyonu, ETABS ile final doğrulama.
