# Proje Durumu

**Son güncelleme:** 2026-10-05, Aşama 6 (hibrit kolonlar, 0.5.0).

## Kimlik

- **Konu:** Çelik çerçevelerin optimizasyonu, **hibrit kompozit kolon** yaklaşımıyla. Kolon grupları ayrı ayrı çelik (W) veya **dolgulu tüp** seçilebilir; dolgulu tüp, çelik kutu (CFT) ya da boru (CFP) içine beton doldurularak elde edilir. Kirişler W profili. Kompozit döşeme bu programın kapsamı dışında; proje sonunda ayrı program olarak önerilecek.
- **Ortam:** ETABS 22.6, VB.NET, .NET Framework 4.7.2, Visual Studio 2026.
- **Klasör:** `...\MVS2010\ETABS\SteelFamewithCompositeTubeColumn_ETABS`.
- **Depo:** github.com/ibrahimaydogdu/SteelFamewithCompositeTubeColumn_ETABS (main).
- **Sürüm:** 0.2.0. Kod, referans projenin (2026.10.3) değiştirilmemiş kopyası.

## Verilen kararlar (2026-10-03)

| Konu | Karar |
|---|---|
| Çalışma dizini | SFCS kopyalanarak depo adıyla yeni klasör açıldı. SFCS ve SSO_CF orijinal haliyle duruyor. |
| Kolon tipi | dolgulu tüp (CFT/CFP); gömülü kesit değil |
| Kolon tipi seçimi | grup başına çelik veya kompozit (hibrit). Hem kullanıcı belirleyebilir hem optimizasyon değişkeni. Amaç: "belirli kata kadar kompozit, üstü çelik" geçişinin optimizasyonu (Aşama 6). |
| Tüp kesit kaynağı | ETABS kütüphanesi (`Property Libraries`, ör. AISC16M) (Aşama 5) |
| SSO | Fortran'dan çevrildi, 16. yöntem (Aşama 3–4) |
| Tüp kataloğu (Aşama 5) | kütüphanedeki kare HSS (≥ 300 mm) + yapma kare kutular 400–1000 mm; boru ve dikdörtgen kutu kapalı; beton modelden |
| Gömülü kesit | seçenek olarak kalıyor; tek program, formda tip seçimi |
| D/C oranı sınırı (Aşama 5.1) | modelin ETABS tercihinden (`DCLimit`, 0,95); oranlar sınıra bölünür. `DesignRatioLimit` ile sabitlenebilir. |
| Hibrit kolonlar (Aşama 6) | form tablosunda grup tipi (Optimize / Steel / Composite); geçiş yığın bazında (varsayılan) ya da grup bazında; gruplamayı kullanıcı modelde yapar |
| Deprem süzgeci (Aşama 5.1) | TBDY 2018 Tablo 9.3 kompozit satırları; varsayılan yüksek süneklik (katalog 100 kesit) |
| Kompozit döşeme | bu programa eklenmeyecek; proje sonunda ayrı program önerisi (karar 2026-10-06) |
| Depo adındaki "Tube" | kolonların tüp olmasını anlatıyor (çekirdek perde değil); ad kalıyor |
| Eski iskelet kod | kaldırıldı (Aşama 2) |
| Lisans | ücretsiz → MIT |
| ETABS dosyaları | depoya girer (`.EDB`, `.$et`); analiz çıktıları girmez |
| Kök ad alanı ve exe adı | `FrameSap2000` olarak korundu; ayar ve yedek dosyalarıyla uyum için |

## Referans ölçümler

Aşama 2'de ölçüldü. Ayrıntı için bkz. `DEGISIKLIKLER.md`.

- MathTest (dişli treni problemi): 15 yöntem × Levy açık/kapalı × 3 tohum, 3000 değerlendirme. Referans ve yeni derlemenin çıktısı **birebir aynı**.
- Tüp modu (Aşama 5, 525M kopyası): ETABS / iç hesap PMM oranı en fazla 1,041. Finalde ETABS kompozit tasarımı 225 kolon için 849 s.
- 525Member kopyası (gömülü mod, AISC 360-22, tohum 12345, 2 değerlendirme): Aşama 5.1'den beri **7564,91 / 1,4580 ve 7068,84 / 1,8032** (D/C sınırı 0,95). Aşama 2–5'teki 7121,40 / 2,0706 değeri eski 1,0 sınırına aitti.
- ETABS'in ilk açılışı ve 289 W kesitli ilk tasarım yaklaşık 520 s sürüyor. Bir değerlendirme 70–140 s.

## Önemli bilgiler

- ETABS 22 `eFramePropType` değerleri: I = 1, Box = 6, Pipe = 7, FilledTube = 29, FilledPipe = 30, EncasedRectangle = 31.
- OAPI'de dolgulu kesit tanımlayan bir `Set` metodu yok (yalnızca `SetTube` var). Referans proje gömülü kesiti `DatabaseTables` ile yazıyor; dolgulu kesit için de aynı yol denenecek.
- Dolgulu kesit tabloları: `Frame Section Property Definitions - Filled Steel Tube` (t3, t2, tf, tw, FillMat) ve `- Filled Steel Pipe` (t3 = çap, tw). Açık boyutla ekleme çalışıyor; `FromFile = Yes` çalışmıyor. `GetSectProps` brüt b·h alanını döndürüyor.
- ETABS kompozit kolon tasarımı kolon başına 1,5–2,7 s; arama sırasında kullanılamaz.
- Boru CFP_711x25.4 için ETABS "Section is too slender -- (D/t) high" mesajı verdi (D/t = 28); incelenmeli.
- AISC16M'de en büyük kare kutu 559×559×23,6 mm, en büyük boru Ø711×25,4 mm.
- `DesignCompositeColumn` için ETABS 22 kodları: AISC 360-22, CSA S16-19/24, Eurocode 4-2004, IS 11384-2022. `GetSummaryResults` ETABS 22.6'da kaymış veri döndürüyor; sonuçlar tablodan okunmalı (referans PROGRAM_KURALLARI B5).
