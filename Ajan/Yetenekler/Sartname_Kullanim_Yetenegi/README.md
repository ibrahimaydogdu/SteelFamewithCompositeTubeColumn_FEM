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