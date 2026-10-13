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

## Aşama F5 — AISC 341 (yerel, GitHub'a gönderilmedi)

**Kod:** `FemCore/FemSeismic.vb` (SCWB: `StrongColumnWeakBeam`, ampirik C0=1.5335, C1=2.23), `FemCore/FemDesign.vb` (`SeismicCompact`: Tablo D1.1 flanş/gövde sınırları, Ca; `LbRyOk`: D1.2b). Karşılaştırma: `FEM_SCWBCMP=1`, `FEM_SEISCMP=1` (designcmp).

**SCWB (BCMajor/BCMinor):** P4 MB20 rastgele kesitler, 4 tohum: ortalama hata %0.13–0.27, en büyük %0.52 (bir tohumda %2.26). ETABS bazı 1. kat kolonlarında oranı boş bırakıyor (tohum 1'de 10 değer); FEM bu satırlar için değer üretir (muhafazakâr), belgelenmiş fark.

**Kesit sıkılığı + Lb/ry (5 tohum × 84 eleman):** Lb/ry 412/414 uyumlu (2 sınır durumu FEM tarafı muhafazakâr: W16X31 kirişi). Sismik kompaktlık: moment çerçevesi elemanlarına uygulanır (mafsallı kirişler hariç); ETABS'in sınır durumdaki kolonları (Ca≈0.11–0.14) işaretlemesi için Ca'ya `CaCalibration=1.25` katsayısı; kalan fark 2/84 (sınır durumu, yalnız tohum 7).

**Kapsam dışı:** ContPl/DoublePl (süreklilik/ikiz levha) — optimizasyonu etkilemiyor.

## Aşama F6a–F6b — optimizatöre entegrasyon (yerel, 2026-10-12)

**Mimari:** `StructureEvaluator.vb` (`IStructureEvaluator`: kesit atama, analiz, düğüm yer değiştirmeleri, çelik tasarım oranları) ve `ETABSClass.Backends.vb`: `EtabsEvaluator` (mevcut ETABS kodu aynen, arayüzün arkasına alındı) ve `FemBackend` (FemCore'u kullanır). Onarım adımları, kısıtlar, ceza ve 16 yöntem aynı kaldı; yalnız dört ETABS çağrısı (E2 kesit atama, E3 analiz, JointDispl, tasarım özeti) arayüze bağlandı. Seçim: `App.config` → `Evaluator = Etabs | Fem` (config değeri ortam değişkenini geçersiz kılar). FEM'in işleyemediği model (kompozit/hibrit kolon, alan elemanı, Check Structure) otomatik ETABS'e döner ve ErrorLog'a nedeni yazılır. Model bir kez ETABS'ten okunur (`EtabsReader`, FemTool ile paylaşılan dosya); ETABS yalnızca başlangıçtaki otomatik seçim listesi tasarımı ve final doğrulama için çalışır.
**Final doğrulama (F6b):** `Opt_Finalize`: en iyi tasarım önce FEM'de yeniden değerlendirilir (log: "best design, FEM …"), sonra değerlendirici ETABS'e geçer (`SwitchToEtabs`), tüm analiz durumlarıyla, onarımsız analiz edilir (DAM durumunda, tasarım sonrası ETABS analizi) ve `_best.EDB` kaydedilir. ETABS cezası > 0 ise **guard**: ihlal eden grupların kesitleri (onarım adımı kuralları, en az 1 konum) büyütülür, geometrik kısıtlar onarılır, ETABS'te yeniden analiz edilir (en çok 6 adım). Sonuç `FinalCheck` ve rapora ETABS değerleriyle yazılır.

**Hız (8 çekirdek):**
| Model | ETABS (değerlendirme) | FEM | Not |
|---|---|---|---|
| P4 (84 eleman) | 13,7 s | **0,15 s** | analiz 45 ms + tasarım 100 ms |
| 525M (525 eleman, 3–4 P-Δ'lı kombinasyon) | 40–50 s | **0,35–0,45 s** | analiz 45–90 ms, tasarım + doğrusal olmayan çözümler 300–400 ms |
Neler: profil (skyline) Cholesky + ters Cuthill-McKee numaralandırma (yoğun 1350² çözümünden 7–9 ms); eleman zincirleri (`Parallel.For`) ve tasarım paralel; zincir inşasında MathNet yerine küçük yoğun çözücüler; P-Δ yinelemesi doğrusal çözümün eksenel kuvvetinden başlar; modal çözücü Solve'un çarpanını paylaşır; `gcServer` (zincir inşası 2,4 kat hızlandı).

**Doğruluk (P4, aynı tasarımlar iki değerlendiricide):**
| Büyüklük | FEM − ETABS |
|---|---|
| Kat ve tepe ötelemesi (80 değer) | ortalama +0,51 %, %95: +1,8 %, en büyük +3,4 % |
| Yakın uygun tasarımlar (f0.7, f0.9) ceza | 1,801 / 1,789 ve 1,429 / 1,439 (≤ %1) |
| SCWB oranı (220 değer, ETABS dolu olanlar) | ortalama +%4,0 (FEM yüksek), %95: +%10,8, en büyük +%15,8 |
| Final tasarım cezası (16 yöntemin en iyileri) | FEM'in uygun saydığı 6 tasarımın 5'i ETABS'te de uygun; biri ETABS'te %1 üzerinde (D/C 1,010); uygun olmayanlarda FEM cezası ETABS'in %1–3'ü kadar altında (bir tanesinde %14: D/C 1,204 / 1,233) |
- ETABS ve FEM değerlendiricileri aynı tasarımlarda **birebir** aynı değerleri veriyor mu: ETABS yolu özgün programla karşılaştırıldı (2 tasarım): maliyet, ceza, tüm kısıt ve grup oranları aynı.
- **Bilinen farklar:** (1) ETABS, kendi tasarım hatası olan elemanda oranı 0 verir ve BC'yi boş bırakır (uygulandı: hatalı eleman oranı 0, BC 0). (2) ETABS bazı sütunlarda (~%25, kural bulunamadı) BC'yi hiç vermiyor; FEM bir değer verir (genelde muhafazakâr). (3) FEM sismik kompaktlık hatasını ETABS'ten ~%6 fazla işaretler, ETABS'in işaretlediği hiçbirini kaçırmaz (672 eleman: 192 ikisi de, 38 yalnız FEM, 0 yalnız ETABS). (4) D/C oranları FEM'de ortalama %0,4–2,4 düşük: `FemRatioMargin` (App.config, varsayılan 1,0) ile çarpan eklenebilir.

**Tüm yöntemler (P4, SSO dışında 15 yöntem + SSO, 400 analiz, bellek 30, tohum 7):** hepsi çalıştı; yöntem başına 47–58 s (400+ analiz, 0,12 s/analiz). Tam SSO koşusu (1536 analiz, bellek 30): toplam 3 dk 35 sn (ETABS ile ≈ 5,5 saat). En iyi tasarım: FEM cezası 0, ETABS cezası 0, maliyet 550,66; `_best.EDB` kaydedildi.

**Final aşama testleri (P4):** (1) Normal koşular (SSO 1536 analiz; HS 1212 ve 950 analiz): en iyi tasarım ETABS'te de uygun (ceza 0), `_best.EDB` kaydedildi. (2) 16 yöntemin en iyileri ETABS'te kontrol edildi (yukarıda). Yöntem 0'ın en iyisi ETABS'te %1 üzerinde (ceza 0,0095): ilk koruma adımı (alana göre bir sonraki kesit) **daha sığ** bir kesit seçti ve cezayı artırdı (0,30) → kural değiştirildi: koruma adımı, Imajor ve Zmajor mevcut kesitten küçük olmayan ilk kesiti seçer; ikinci adımda uygun. (3) Yöntem 3, 15, 8'in en iyileri ETABS'te hafif ihlalli (0,034 / 0,038 / 0,125) ve ihlal eden gruplar üst sınırda olduğu için koruma adımı değişiklik yapamadı → **aday arşivi** eklendi: koşu sırasında FEM'de uygun bulunan en ucuz 20 farklı tasarım tutulur, final aşamada ETABS'te maliyet sırasıyla (en çok 10) denenir, ilk geçen son tasarımdır; hiçbiri geçmezse koruma adımları. (4) Zorlama deneyi (FemRatioMargin = 0,5: FEM oranları yarıya iniyor): ETABS cezası 0,88; 10 aday da ETABS'te uygunsuz (0,18–0,19); koruma adımı BM-INT-L6000-S01-02 grubunu W250X38.5 → W310X67 yaptı, ceza 0, maliyet %2 yüksek (566,34 / 555,40). Bütün yol çalışıyor; en kötü durumda final aşama 10 aday × 13 s + koruma ≈ 3 dk.

**Ölçüm notu:** süreler makine boşken ölçüldü; başka işlemler işlemciyi doldurduğunda (OCR işi: %70 çekirdek) FEM değerlendirmesi 5–8 kat yavaşladı (sunucu GC ve paralel döngüler çekirdek bekler).

**525M uçtan uca (25 kat, 525 eleman, 3 P-Δ'lı doğrusal olmayan kombinasyon, SSO, 516 analiz, bellek 20, tohum 5):** ETABS başlangıcı (otomatik seçim listesi tasarımı) 9,5 dk (değişmedi); optimizasyon döngüsü 3 dk 2 sn (0,35 s/analiz, onarım adımları dahil); en iyi tasarım maliyeti 8651,29. Final ETABS kontrolü: FEM cezası 0, ETABS cezası 0 (kat ötelemesi 0,974, tepe 0,728, D/C 0,715, geometrik 1,00 / 0,965); `_best.EDB` kaydedildi. ETABS ile aynı sayıda analiz yaklaşık 6 saat (40–50 s) sürerdi.

**P-Δ yedek çözümü (525M'de bulundu):** ilk denemede 525M'nin bütün rastgele başlangıç tasarımları başarısız (ceza 10) çıktı: tutarlı geometrik rijitlik eleman burkulmasını (P-δ) da yakalar, burkulma yükünü aşan kolonlu hafif tasarımda eleman içi matris pozitif tanımlı olmaz. ETABS'in P-Δ analizinde eleman burkulması yoktur (tasarım kontrolü B1 ile yapar), bu yüzden orada analiz tamamlanır ve kolon oranı yüksek çıkar. Çözüm: tutarlı matris başarısız olursa kombinasyon topaklı P/L geometrik rijitliğiyle yeniden çözülür (`FemEvaluator.TrySolveIterative`, `FemProfile` sayacı `pdelta.lumped fallback`); gerçek burkulma tasarım kontrolünde görünür (rastgele 525M tasarımlarında D/C 2–6). F3'te doğrulanan tutarlı matris, burkulma yükü altındaki tasarımlarda aynen kullanılır.

## Aşama F7 — kompozit ve hibrit kolonlar (yerel, 2026-10-12)

**Kod:** `FemSection.NoDesign` (kompozit kesit: çelik tasarım yok), `FemEvaluator.CombinationForces` (tasarım kombinasyonlarının istasyon kuvvetleri: statik, RS ±, doğrusal olmayan), arayüze `ReadFrameForces` (ETABS: `Results.FrameForce`, FEM: istasyon kuvvetleri; kN, mm), `G1_2_ConsComposite` iki hesaplayıcıyla çalışır; kompozit kesit `CompositeSec(...).Transformed()` özelliklerinden FemSection olur (A, As, J, I, ağırlık/kütle çarpanı). ETABS'e geçişte `ForgetAssignedSections`. FEM'de kompozit kesitlerde DAM (0,8) uygulanmaz.
**Doğrulama (P4, dolgulu tüp, aynı tasarım, ETABS / FEM):** kat ve tepe ötelemesi 0,645 / 0,648 ve 0,509 / 0,511 (DAM kompozit kesitlere uygulandığında ETABS'ten %3–6 yüksekti, uygulanmayınca %0,5); kompozit dayanım oranı 0,205 / 0,216 ve 0,231 / 0,232; detay oranı birebir. Grup oranları %−3…+5.

**Uçtan uca (P4, SSO, FEM):** dolgulu tüp: 608 analiz 3 dk 41 sn, ETABS final kompozit kontrolü uygun (PMM 0,09–0,33). Hibrit (kat grubu başına çelik/kompozit): 657 analiz 5 dk 12 sn; en iyi tasarım ETABS'te %0,6 aşımlı çıktı (BM-INT-L6000-S03-04); 10 aday da aynı ihlalde kaldı; koruma adımı geometrik kısıta takılıyordu (kolon boşluğuna sığmayan sonraki kesit geometrik onarımla geri alınıyordu) → koruma, rijitliği küçülmeyen **ve geometrik kısıtı sağlayan** ilk kesiti arar (W250X44.8 → W410X46.1), ETABS cezası 0, maliyet %0,7 yüksek, koşu 'başarıyla tamamlandı'.

## Aşama F8 — ETABS–FEM kalibrasyonu (2026-10-13)

**Veri:** `FemTool calibdump <model> <çıktı.jsonl> <tohumlar>` rastgele kesitli tasarımları ETABS'te (DAM durumu) ve FEM'de analiz eder; üye başına bir JSON satırı: ETABS sonuçları (oran, hata metni, SectClass, BCMajor/BCMinor), FEM geometrisi, kesit verisi, her tasarım kombinasyonundaki en büyük basınç. Üç küme (yalnız W310/W360 kolon, tüm W listesi, hafif kesit bandı) × 12 tasarım = 36 tasarım, 3024 üye (P4). Analiz betikleri `tools/calibration/` (calibfit.py: SCWB uydurma; calibcomp.py: kompaktlık/Lb-ry; calibblank.py: boş BC kuralı). *Not:* ilk veri üretiminde yeniden açılan model kilitli kaldığından `SetSection` etkisizdi ve tohumlar aynı tasarımı verdi; düzeltildi (`SetModelIsLocked(False)`), yukarıdaki sayılar düzeltilmiş verilerdir.

**Bulgular ve uygulananlar:**
1. **SCWB eksenel kuvveti:** ETABS Mpc için yalnız tepki spektrumlu (sismik) kombinasyonların basıncını kullanıyor (uydurmada yerçekimi kombinasyonlarının ağırlığı −0,02, sismiğin 1,02). Sabitler C0 = 1,5335 → 1,5347, C1 = 2,23 → 2,2542. Uydurma hatası: rms %0,45 (en büyük %5,0) → %0,15 (en büyük %1,0).
2. **Boş BC kuralı (25 % kolonda ETABS değer vermiyor):** BC yalnız kolonun **belirleyici kombinasyonu sismik (RS'li) olduğunda** veriliyor: 439 / 439 sismik belirleyicili kolonda değer var, 82 / 82 diğerlerinde (yerçekimi, rüzgâr) boş. Uygulandı (modelde RS durumu varsa).
3. **Sismik kompaktlık:** Ca için en iyi tanım: sismik kombinasyonların en büyük basıncı × 1,1 (önceki: tüm kombinasyonlar × 1,25). Moment çerçevesi kuralı: moment bağlantılı kirişi olmayan **ve üstünde kolon bulunan** kolon denetlenmez (en üst kat kolonu denetlenir; 3024 üyede yalnız 20 fark).
4. **Lb/ry:** 200 işaretli kirişin 16'sında FEM fazla işaretler (ETABS işaretlemiyor, nedeni çözülemedi; muhafazakâr).

**Bağımsız doğrulama (18 yeni tasarım, 1512 üye, ETABS'e karşı):** SCWB oranı rms %0,18 (en büyük %0,8; önceki: ortalama +%4, en büyük +%16); D/C oranı ortalama +%0,12 (%95: +%0,6; en büyük %1,7); sismik hata işaretleri 1512 üyede %97,9 uyumlu (18 yalnız FEM, 14 yalnız ETABS; önceki ≈ %6 fazla işaret). İyileştirici akışında 8 tasarımın cezası: ETABS 7,85 / 7,27 / 5,06 / 10,0 / 5,83 / 16,1 / 1,789 / 1,439 ↔ FEM 7,20 / 6,98 / 4,34 / 10,4 / 6,01 / 16,7 / 1,805 / 1,429 (önceki FEM: 7,20 / 10,8 / 2,84 / 10,4 / 7,2 / 16,7 / 1,801 / 1,429); uygun yakınındaki tasarımlarda fark ≤ %1.
