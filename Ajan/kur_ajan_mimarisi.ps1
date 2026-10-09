# Ajan klasör mimarisini kurar (Adım 1).
# Kullanım (proje kökünde):  powershell -ExecutionPolicy Bypass -File Ajan\kur_ajan_mimarisi.ps1
# Var olan dosyaların üzerine yazmaz; tekrar çalıştırmak güvenlidir.

$ErrorActionPreference = 'Stop'
$Kok = Join-Path (Split-Path -Parent $PSScriptRoot) 'Ajan'
if ((Split-Path -Leaf $PSScriptRoot) -eq 'Ajan') { $Kok = $PSScriptRoot }

# klasör yolu -> README içeriği
$Yapi = [ordered]@{
    '' = @'
# Ajan

Projenin çalışma mimarisi. Ajan (geliştirici yapay zekâ veya insan) işi şu döngüyle yürütür:

1. `Gorevler/KUYRUK.md` içinden sıradaki görevi alır.
2. Gerekli bilgiyi `Yetenekler/` (nasıl yapılır) ve `Hafiza/` (ne biliniyor) altından okur.
3. Görevi uygular ve test eder. Sonucu kökteki `DEGISIKLIKLER.md` dosyasına "Aşama N" başlığıyla yazar.
4. `Hafiza/` ve `Gorevler/` dosyalarını günceller, ardından commit ve push yapar.

| Klasör | İçerik |
|---|---|
| `Yetenekler/Optimizasyon_Yetenegi` | Metasezgisel yöntemler (SSO dahil), ceza fonksiyonu, kesit havuzu, test problemleri |
| `Yetenekler/ETABS_Kullanim_Yetenegi` | ETABS 22 OAPI kuralları, model kopyası, analiz/tasarım çağrıları, bilinen tuzaklar |
| `Yetenekler/Sartname_Kullanim_Yetenegi` | AISC 360-22 (ve 360-16) dolgulu tüp (CFT/CFP) kompozit kolon ve çelik eleman kuralları |
| `Hafiza` | Proje durumu, kararlar, referans değerler, ölçüm sonuçları |
| `Gorevler` | Akış şeması ve adım adım görev kuyruğu |
'@
    'Yetenekler' = @'
# Yetenekler

Her alt klasör bir yetenektir. Her yetenek şunları içerir:

- `README.md`: amaç, kaynaklar ve kurallar;
- ilgili kodun hangi kaynak dosyada olduğu;
- yeteneğin testleri.
'@
    'Yetenekler/Optimizasyon_Yetenegi' = @'
# Optimizasyon Yeteneği

Bu yetenek şunları kapsar:

- **Yöntemler:** referans projedeki 15 metasezgisel yöntem (`OptimizationMethods.vb`, `MethodCatalog`) ve **Sosyal Örümcek Optimizasyonu (SSO)**. SSO, Fortran kaynağından aktarılacak.
- **Tasarım değişkenleri:** grup başına kesit indisi. Kiriş grupları için W havuzu. Kolon gruplarında hibrit seçim yapılır: W profil ya da dolgulu tüp (çelik kutu/boru + beton) havuzu.
- **Amaç:** maliyet (çelik $/kg, beton $/m³, donatı, kalıp). Kısıtlar ceza fonksiyonuyla eklenir.
- **ETABS'siz test:** dişli treni ve benzeri matematik problemleri (`TestWithMath`).

Kaynaklar:

- Referans: `..\SteelFamewithCompositeColumn_ETABS\OptimizationMethods.vb`, `OptimizationClass.vb`.
- Fortran SSO kaynakları (`Algoritmalar\fortran\SocialSpider\`):
  - `SSO_Frame\SSO.f90`: çerçeve sürümü, 2018;
  - `SSO_Column\SSO.f90`: kolon sürümü, 2015;
  - `SSO.m`: MATLAB sürümü.
'@
    'Yetenekler/ETABS_Kullanim_Yetenegi' = @'
# ETABS Kullanım Yeteneği

Bu yetenek şunları kapsar:

- ETABS 22.6 OAPI (`ETABSv1.dll`) ile bağlantı;
- **yalnızca model kopyası** üzerinde çalışma: geçici klasöre kopyalanır, orijinal model ve açık ETABS oturumuna dokunulmaz;
- grup tabanlı kesit atama, analiz, çelik ve kompozit kolon tasarımı (`DesignSteel`, `DesignCompositeColumn`);
- öteleme ve yerdeğiştirme okuma;
- yeniden başlatma ve bellek yönetimi.

Kaynak: referans projedeki `ETABSClass.vb` dosyası ve onun PROGRAM_KURALLARI.md Bölüm 4 ve 9.

Bilinen tuzaklar (referans projede ölçülmüş):

- E2K gidiş-dönüşü modeli kayıplı değiştiriyor; bu yüzden yalnızca EDB kullanılmalı.
- Yeniden açılan modelde kombinasyon seçimi siliniyor; her tasarımdan önce kontrol edilmeli.
- `eFramePropType` değerleri: I = 1, Box = 6, Pipe = 7, **FilledTube = 29**, **FilledPipe = 30**, EncasedRectangle = 31. Dolgulu kesit için OAPI'de Set metodu yok; DatabaseTables yolu denenecek.
'@
    'Yetenekler/Sartname_Kullanim_Yetenegi' = @'
# Şartname Kullanım Yeteneği

Bu yetenek şu kuralları kapsar:

- **AISC 360-22 (ve 360-16):** dolgulu kompozit kolonlar (Bölüm I: I1, I2.2, I5) ve çelik elemanlar (Bölüm D, E, F, G, H).
- **Kesit kuralları (CFT/CFP):** kompakt/narin/çok narin sınıfı (Tablo I1.1a), en az çelik oranı (%1), beton dayanımı sınırları, çelik akma dayanımı sınırı.
- **Dayanımlar:**
  - eksenel basınç (Pno, Pe, EIeff);
  - eğilme (plastik gerilme dağılımı veya şekil değiştirme uyumu);
  - etkileşim (H1 / I5).
- **Servis kısıtları:** öteleme ve yerdeğiştirme sınırları, deprem ötelemesinin büyütülmesi.

Kaynaklar:

- referans projedeki `AISC360_22_Composite_Column_Rules.md`, `AISC360_16_Composite_Column_Rules.md` ve `CompositeColumn.vb` (gömülü kesit içindir; CFT/CFP için aynı yapıyla genişletilecek);
- Fortran: `Algoritmalar\fortran\SocialSpider\Composite_Design\SSO_Frame_Com\Source1.f90` (`filled_composite`, `CompsiteAxialCapacity`).
'@
    'Hafiza' = @'
# Hafıza

Bu klasörde projenin kalıcı bilgisi tutulur:

- `PROJE_DURUMU.md`: güncel durum ve verilen kararlar;
- referans değerler ve ölçüm sonuçları.

Geçici çalışma dosyaları buraya değil, sistemin geçici klasörüne yazılır.
'@
    'Gorevler' = @'
# Görevler

- `AKIS_SEMASI.md`: geliştirme akış şeması (pipeline).
- `KUYRUK.md`: adım adım görev kuyruğu.

Görev durumları: `[ ]` bekliyor, `[~]` sürüyor, `[x]` bitti.
'@
}

foreach ($Yol in $Yapi.Keys) {
    $Klasor = if ($Yol) { Join-Path $Kok $Yol } else { $Kok }
    New-Item -ItemType Directory -Force -Path $Klasor | Out-Null
    $Readme = Join-Path $Klasor 'README.md'
    if (-not (Test-Path $Readme)) {
        [IO.File]::WriteAllText($Readme, $Yapi[$Yol], (New-Object Text.UTF8Encoding($false)))
        Write-Output "oluşturuldu: $Readme"
    } else {
        Write-Output "zaten var:   $Readme"
    }
}
