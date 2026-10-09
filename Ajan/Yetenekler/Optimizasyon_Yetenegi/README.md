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