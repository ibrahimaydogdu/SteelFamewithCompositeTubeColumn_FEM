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


## Aşama F2: modal analiz ve tepki spektrumu (2026-10-09)

**Kod:** `FemCore/FemModal.vb` (`FemStatic` kısmi sınıfı): topaklanmış düğüm kütleleri, alt uzay yinelemesi (Cholesky ile; MathNet), CQC tepki spektrumu, kazara dışmerkezlik. `EtabsReader`: kütle kaynağı tablosu, spektrum fonksiyonları (`Func.GetValues`), RS ve modal durumlar. `FemTool modal|rs <model>`.

**Kütle (ETABS davranışı, ölçüldü):**
- Kütle kaynağı tablosu (`Mass Source Definition`): kendi kütlesi, seçili yük desenleri × çarpan (P4: SDL ×1, PartMass ×1), `IncVertical = No` → düşey kütle yok.
- Eleman kütlesi uçlara yarı yarıya, kendi ağırlığı ile aynı boy kuralı (düşey eleman tam boy, diğerleri net boy). Yük kütlesi: desenin eşdeğer düğüm yükünün düşey bileşeni / g (g = 9,80665).
- Dönme kütlesi düğümlerde yok; diyafram dönme ataleti düğüm kütlelerinden gelir.

**Modal sonuçlar (DAM 0,8 ile):**
| Model | Periyot hatası | Katılım oranı |
|---|---|---|
| P4 (12 mod) | 0,0000 % (yatay modlar), −0,016 % (burulma modları) | UX, UY aynı (4 hane) |
| 525M (12 mod, diyafram yok) | 0,0000 % | aynı |

Süre: P4 0,5 s; 525M 8,7 s (yoğun matrislerle; birleştirme aşamasında hızlandırılacak).

**Tepki spektrumu (P4, RSX ve RSY):**
- Eşdeğer ivme = ölçek çarpanı (API'den, m/s², g dahil) × spektrum ordinatı (tablo, doğrusal aradeğerleme); mod genliği Γ·a/ω²; CQC (sönüm 0,05, durumdan okunur).
- **Kazara dışmerkezlik yok (ecc = 0):** yerdeğiştirme, taban kesmesi (518,558 kN / 436,352 kN) ve tüm uç kuvvetleri **%0,000**.
- **Kazara dışmerkezlik = 0,05 (ModelBuilder varsayılanı):** ETABS, her diyafram için statik burulma tepkisini RS sonucuna **mutlak değerle ekliyor**. Burulma momenti M_k = F_k · ecc · L_k (F_k: diyaframın CQC atalet kuvveti; L_k: yön doğrultusuna dik diyafram boyutu, düğümlerin sınırlayıcı kutusu). ETABS'in `~Torsion<RS>` durumu sıfır döndürüyor (otomatik desen `~RSXECC`, değerleri API'den okunamıyor), bu yüzden kural varsayım–deney ile bulundu: yerdeğiştirme hatası %5,7 → **%0,20**; uç kuvvetleri ≤ %0,3–0,8 (burulma kuvveti T: tamamı 0,1 kN·m mertebesinde, %1,5–2).
- Kalan %0,2 farkın nedeni araştırılmadı (burulma çarpanı 0,95–0,98 deneyinde hata %0,05–0,10'a iniyor; muhtemelen F_k tanımı); hata payı içinde olduğundan şimdilik bırakıldı.
- Süre: tek RS durumu 6–13 ms (dışmerkezlik dahil 80–220 ms: burulma için ikinci bir statik çözüm yapılıyor, birleştirmede önbelleğe alınacak).

**Notlar:** ETABS otomatik desenleri (`~LLRF`, `~RSXECC`) analizden sonra oluşuyor; tasarım çalıştırıp kilit açılınca siliniyor. Diyaframa bağlı olup eleman bağlamayan düğümler (ETABS'in diyafram merkezi noktaları) artık yalnızca yük taşımak için diyafram eşlemesine alınıyor.


## Aşama F3: ikinci mertebe (P-Δ) (2026-10-09)

**Kod:** `FemElementMath.GeometricStiffness` (birim çekme kuvveti için tutarlı geometrik rijitlik: iki eğilme düzleminde kübik alan, burulma terimi P·Ip/A); zincire `Axial` parametresi (her parçanın rijitliğine P·Kg eklenir); `FemCore/FemPDelta.vb`: `UseMassPDelta` ve `SolveIterative`; `FemModel.CombinePatterns`.

**ETABS'in P-Δ davranışı (ölçüldü):**
1. **Ön ayar "Non-iterative Based on Mass"** (iyileştiricinin `EnablePDelta` ayarı): yanal kütle × g'nin düğümlere düşey yük olarak uygulanmasıyla bulunan eleman eksenel kuvvetleri, **bütün doğrusal durumların** (statik, modal, tepki spektrumu) geometrik rijitliğini belirler. Modal periyotlar da uzar (P4: 0,71697 → 0,72145 s).
2. **Doğrusal olmayan statik durum, P-Δ açık** (525M'nin üç durumu): durumun kendi yük birleşiminin dengesinde yinelemeli eksenel kuvvetler. FEM'de eksenel kuvvetler yerdeğiştirme değişimi < 1e-6 olana dek yinelenir.

**Sonuçlar (ETABS ile karşılaştırma; DAM 0,8 etkin):**
| Durum | Hata |
|---|---|
| P4, kütle tabanlı P-Δ, statik WX / WY | yerdeğiştirme 0,15 / 0,28 %; dönme 0,43 / 0,74 %; uç kuvvetleri ≤ %0,5 (V2, M3). P-Δ olmadan fark: %0,84 / %1,26 |
| P4, modal (mod 1–4) | periyot hatası 0,05–0,36 % |
| P4, RSX / RSY (dışmerkezlikli, P-Δ'lı) | yerdeğiştirme 0,23 / 0,26 %; V2, M2, M3 ≤ %0,7–1,2 |
| 525M, P-Δ'lı doğrusal olmayan durumlar: rüzgâr X / Y | yerdeğiştirme 0,12 / 0,37 %; uç kuvvetleri ≤ %0,1–0,4 |
| 525M, yalnız yerçekimi durumu (1,2D+1,6L+0,5Lr) | düşey yerdeğiştirme %0,19, ancak yatay sehim farkı nedeniyle en büyük yerdeğiştirme hatası %1,8; uç kuvvetleri ≤ %0,5 |

- Geometrik rijitlik biçimleri denendi: tutarlı (kübik) matris; yalnızca P/L (topaklı). Tutarlı olan genel olarak daha tutarlı (P4 statik 0,15/0,28 % ↔ topaklı 0,09/0,17 %, ama 525M'de dönmeler topaklıda %1,2–1,4'e çıkıyor), bu yüzden tutarlı olan varsayılan. Burulma terimi P4 sonuçlarını değiştirmedi.
- P ölçeği taraması (0,8–1,3) belirgin bir düzeltme göstermedi: kalan %0,1–0,3 fark eksenel kuvvet büyüklüğünden değil, ETABS'in kapalı ayrıntılarından geliyor.
- **Süre (henüz hızlandırılmadı):** 525M'de bir P-Δ'lı durumun yinelemesi 15–22 s (her yinelemede matris yeniden kuruluyor, yoğun Cholesky). Entegrasyon aşamasında eleman matrislerinin önbelleğe alınması ve bantlı çözücü ile saniyenin altına indirilecek.

**Sonraki:** F4 AISC 360-22 çelik tasarım (D/C oranı doğrulaması); AISC 341; optimizasyon entegrasyonu ve ETABS ile final doğrulama.

### F3 düzeltmesi: kütle tabanlı P-Δ'nın gerçek biçimi (2026-10-10)
Rastgele kesitli (hafif, esnek) P4 modeliyle yapılan karşılaştırma, eleman geometrik rijitliğiyle modellenen kütle tabanlı P-Δ'nın %1–3 yerdeğiştirme hatası verdiğini gösterdi. ETABS'in `~...` merkez noktalarında rapor ettiği "mesnet tepkileri" incelendi: değerler **W_k · u_k / z_k** ile birebir eşleşti (W_k: kat ağırlığı, u_k: kat yerdeğiştirmesi, z_k: kotun tabandan yüksekliği). Yani ETABS'in "Non-iterative Based on Mass" seçeneği:
- elemanlara geometrik rijitlik eklemez; her **kütleli düğümün iki yanal ötelemesine −m·g/z** terimi eklenir (z: tabandan kot); bu terim bütün doğrusal durumlara (statik, modal, tepki spektrumu) uygulanır;
- eleman uç kuvvetleri ve mesnet tepkileri bu terim olmadan elastik rijitlikten hesaplanır; P-Δ kuvvetleri tepki olarak diyafram merkez noktalarında görünür (bu noktalar gerçek mesnet değildir, karşılaştırmaya alınmaz).

Bu biçimle sonuçlar: P4 statik (varsayılan ve rastgele kesitler): yerdeğiştirme, dönme ve **bütün eleman uç kuvvetleri %0,000**; modal periyot hatası 0,0000 % (burulma modları −0,016 %); RSX/RSY yerdeğiştirme %0,10–0,12, kuvvetler ≤ %0,2–0,5. Önceki eleman-geometrik-rijitlik yaklaşımı yalnızca P-Δ'lı **doğrusal olmayan statik durumlar** için (525M) kullanılır (yukarıdaki tablo).

## Aşama F4: AISC 360-22 çelik tasarım (2026-10-10)

**Kod:** `FemCore/FemDesign.vb` (kesit sınıfı, basınç E3/E7, tek eksenli eğilme F2/F3/F6, kesme G2/G6, Cb), `FemDesignDriver.vb` (istasyon kuvvetleri, kombinasyonlar, H1 etkileşimi, eleman zarfı), `FemPDelta.vb` (`DesignNonlinear`). Okuyucu: kesit özellikleri (S, Z, r, fillet), dayanım kombinasyonları, ön tercihler (φ, D/C sınırı), tasarım overwrite'ları (LMajor, LMinor, LTB). `FemTool designcmp <model>`: ETABS tasarım oranları ile karşılaştırma.

**ETABS davranışları (ölçüldü):**
1. **İstasyonlar:** düşey elemanlarda net boyda 3 istasyon (uçlar ve orta), diğerlerinde en çok 0,5 m aralık (12–16 aralık). İstasyon kuvvetleri yüz kuvvetlerinden ve yüklerden denge ile.
2. **Tepki spektrumlu kombinasyonlar:** RS sonuçları (CQC + kazara dışmerkezlik) **bileşen bileşen en kötü büyüklükle** statik kısma eklenir: |statik| + RS (her bileşen için bağımsız). Eksenel kuvvet, M2 ve M3 aynı işaret çifti içinde birlikte alınmaz.
3. **Basınç/etkileşim:** K = 1 (DAM); Pr/Pc ≥ 0,2 için H1-1a, aksi hâlde H1-1b; tek ayrı "P oranı" olarak Pr/(2Pc) raporlanır. B1 genliği (P-δ) gözlenen örneklerde etkisiz. Çekme: min(φ Fy Ag, φ Fu Ag).
4. **Yanal burkulma:** varsayılan desteksiz boy = net boy; Cb tüm boy için. **LTB overwrite'ı (ModelBuilder 2,5 m desteği) varsa:** boy = ratio × eklem-eklem boy ve Cb her istasyon için, **istasyon merkezli, Lb uzunluklu pencerede** (çeyrek noktalar, pencere açıklık içine kaydırılır) hesaplanır. Bu kural 5 ve 6 m kirişlerde birebir tuttu.
5. P-δ'ın istasyon momentlerine etkisi (eksenel kuvvet × kiriş kord dönmesi) ETABS'te var, FEM'de yok (aşağıda).

**Sonuçlar (ETABS ile D/C oranı karşılaştırması; PMM oranı = tasarım zarfı):**
| Model / tasarım | Üye | Ortalama bağıl hata | %95 | En büyük |
|---|---|---|---|---|
| P4, rastgele kesitli 8 tasarım (kolon + kiriş, gravite, rüzgâr ve RS kombinasyonları, desteksiz ve destekli) | 50–80 | 0,03–0,77 % | 0,10–2,54 % | 0,17–2,74 % |
| P4 desteksiz model, 2 rastgele tasarım | 34–48 | 0,08 / 0,16 % | — | 0,69 / 0,70 % |
| 525M (25 kat, 525 eleman, P-Δ'lı doğrusal olmayan kombinasyonlar, otomatik seçim listeleri çözülmüş) | 525 | **0,64 %** | **2,29 %** | 6,34 % (50 kiriş %2–6 arası, FEM daha düşük) |
| Kesme oranı (tüm üyeler) | — | 0,08–0,75 % (P4), **0,10 % (525M)** | — | 3,7 % (P4), 0,34 % (525M) |

- **Ön koşul:** modelde otomatik seçim listesi varsa ETABS tasarım kesitini analiz kesitinden farklı seçer; karşılaştırma için tasarım kesitleri çerçevelere atanıp yeniden analiz edilmelidir (iyileştirici zaten açık kesit atıyor).
- **Bilinen sınır (525M'de 50 kiriş, FEM %2–6 düşük):** eksenel kuvvetli (çekme/basınç) kirişlerde ETABS, M2 diyagramına P × kord dönmesi terimini ekliyor (örnek: M2 eğimi 3,06 kN·m/m, V3 = 2,50); FEM yalnızca birinci mertebe denge kullanıyor. P4'te etkisi yok (kütle tabanlı P-Δ elemanlara eksenel geometrik rijitlik eklemiyor). Entegrasyondan önce eklenecek veya fark çarpanıyla kapatılacak.
- **Süre:** 525M'de 525 üye × 3 P-Δ'lı kombinasyon: 10 s (çoğu P-Δ yinelemesi); P4: 70–190 ms.

**Sonraki:** F5 AISC 341 (SCWB oranı, süneklik süzgeci, Lb/ry) ve kompozit/hibrit; F6 optimizasyon entegrasyonu (önbellek, hız) ve final ETABS doğrulaması.
