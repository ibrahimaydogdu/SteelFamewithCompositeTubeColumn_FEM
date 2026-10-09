# AI IDE KODLAMA REHBERİ: AISC 360-22 Kompozit Kolon Tasarımı (ETABS v22 Referanslı)

## 1. MİMARİ VE GÖREV TANIMI (CONTEXT & OBJECTIVE)
Bu doküman, ANSI/AISC 360-22 ve ASCE 7-22 standartlarına tam uyumlu Kompozit Kolon (Beton Dolgulu Kutu/Boru ve Beton Gömülü I-Profil) boyutlandırma ve tahkik algoritmasını tanımlar.
**AI Asistanı Talimatı:** Bu dokümandaki veri yapılarını, tasarım denklemlerini ve kontrol akışlarını kullanarak `AISC360_22_CompositeDesign` modülünü nesne yönelimli (OOP) mimariyle implemente et. Varsayılan birim sistemi US Customary (kip, inch, ksi)'dir.

---

## 2. TEMEL TASARIM VE DİRENÇ KATSAYILARI (CONSTANTS & FACTORS)

### 2.1. LRFD ve ASD Katsayıları
```python
# LRFD Direnç Katsayıları (Phi - Tablo B-2 / Bölüm 3)
PHI_C = 0.75      # Eksenel Basınç (AISC I2.1b, I2.2b)
PHI_B = 0.90      # Eğilme (AISC I3.3, I3.4b)
PHI_T = 0.90      # Çekme Akma (AISC I2.1c, I2.2c)
PHI_V = 0.90      # Genel Kesme (AISC I4.2, G1)
PHI_V_ROLLED = 1.00 # Hadde I-Gövde Kesme (h/tw <= 2.24*sqrt(E/Fy)) (AISC G2.1a)
PHI_TOR = 0.90    # Burulma (AISC H3.1)

# ASD Güvenlik Katsayıları (Omega - Tablo B-3)
OMEGA_C = 2.00
OMEGA_B = 1.67
OMEGA_T = 1.67
OMEGA_V = 1.67
OMEGA_V_ROLLED = 1.50
OMEGA_TOR = 1.67

# Malzeme Sabitleri
E_S = 29000.0     # Çelik elastisite modülü (ksi)
DEFAULT_DC_LIMIT = 0.95 # ETABS varsayılan D/C sınır değeri
```

---

## 3. KESİT YEREL BURKULMA SINIFLANDIRMASI (CLASSIFICATION)
AISC 360-22 Tablo I1.1A ve Tablo I1.1B uyarınca elemanlar sınıflandırılır.

```python
from enum import Enum
import math

class SectionClass(Enum):
    COMPACT = 1
    NONCOMPACT = 2
    SLENDER = 3
    TOO_SLENDER = 4 # ETABS kapsamı dışındadır, hata üretmelidir.

def classify_filled_box(b, h, t, Fy, Es=E_S, is_flexure=False):
    """
    b: Kısa kenar flanş genişliği (B - 3t)
    h: Uzun kenar gövde genişliği (H - 3t)
    t: Tasarım et kalınlığı (ERW için 0.93*tnom, SAW için tnom)
    """
    lambda_flange = b / t
    lambda_web = h / t
    sqrt_E_Fy = math.sqrt(Es / Fy)
    
    # Flanş Sınırları (Eğilme ve Basınç için aynı)
    lp_f, lr_f, ls_f = 2.26 * sqrt_E_Fy, 3.00 * sqrt_E_Fy, 5.00 * sqrt_E_Fy
    
    # Gövde Sınırları
    if is_flexure:
        lp_w, lr_w, ls_w = 3.00 * sqrt_E_Fy, 5.70 * sqrt_E_Fy, 5.70 * sqrt_E_Fy
    else:
        lp_w, lr_w, ls_w = 2.26 * sqrt_E_Fy, 3.00 * sqrt_E_Fy, 5.00 * sqrt_E_Fy

    def get_class(l_val, lp, lr, ls):
        if l_val <= lp: return SectionClass.COMPACT
        elif l_val <= lr: return SectionClass.NONCOMPACT
        elif l_val <= ls: return SectionClass.SLENDER
        else: return SectionClass.TOO_SLENDER

    class_f = get_class(lambda_flange, lp_f, lr_f, ls_f)
    class_w = get_class(lambda_web, lp_w, lr_w, ls_w)
    
    return max(class_f, class_w, key=lambda c: c.value)

def classify_filled_pipe(D, t, Fy, Es=E_S, is_flexure=False):
    """
    D: Dış çap
    t: Tasarım et kalınlığı (0.93 * tnom)
    """
    lambda_p_ratio = D / t
    ratio_E_Fy = Es / Fy
    
    if is_flexure:
        lp = 0.09 * ratio_E_Fy
        lr = 0.31 * ratio_E_Fy
        ls = 0.31 * ratio_E_Fy
    else:
        lp = 0.15 * ratio_E_Fy
        lr = 0.19 * ratio_E_Fy
        ls = 0.31 * ratio_E_Fy

    if lambda_p_ratio <= lp: return SectionClass.COMPACT
    elif lambda_p_ratio <= lr: return SectionClass.NONCOMPACT
    elif lambda_p_ratio <= ls: return SectionClass.SLENDER
    else: return SectionClass.TOO_SLENDER
```

---

## 4. İKİNCİ MERTEBE ETKİLER VE KUVVET BÜYÜTMESİ ($B_1, B_2$)
Direct Analysis Method veya Amplified First-Order Analysis kullanımı için:

```python
def calculate_required_forces(M_nt, M_lt, P_nt, P_lt, Pe1, M1, M2, B2=1.0, is_braced_transverse=False, alpha=1.0):
    """
    M_nt, P_nt: Ötelenmesiz (no-translation) 1. mertebe sonuçları
    M_lt, P_lt: Yanal ötelenmeli (lateral-translation) 1. mertebe sonuçları
    Pe1: pi^2 * EI_eff / (K1 * L)^2
    M1, M2: Uç momentleri (|M1| <= |M2|)
    """
    if is_braced_transverse or M2 == 0:
        Cm = 1.0
    else:
        # M1/M2 çift eğrilikte pozitif (+), tek eğrilikte negatif (-)
        Cm = max(0.6 - 0.4 * (M1 / M2), 0.0)

    Pr_first = P_nt + P_lt
    denom = 1.0 - alpha * (Pr_first / Pe1)
    
    if denom <= 0:
        raise ValueError("Stabilite kaybı: alpha * Pr >= Pe1")
    
    B1 = max(Cm / denom, 1.0)
    
    # Büyütülmüş Kuvvetler
    Mr = B1 * M_nt + B2 * M_lt
    Pr = P_nt + B2 * P_lt
    return Pr, Mr
```

---

## 5. DOLGULU KESİTLER İÇİN NOMİNAL DAYANIM HESAPLARI (FILLED SECTIONS)

### 5.1. Eksenel Basınç Dayanımı ($P_n$)
```python
def calculate_Pn_filled(sec_type, sec_class, As, Ac, Asr, Fy, Fysr, fc, Is, Ic, Isr, K, L, b_or_D, t, Es=E_S):
    # Beton Rijitliği Katsayısı C3 (AISC I2-13)
    Ag = As + Ac + Asr
    C3 = min(0.45 + 3.0 * ((As + Asr) / Ag), 0.90)
    
    # Efektif Eğilme Rijitliği (AISC I2-12)
    Ec = (145**1.5) * 0.033 * math.sqrt(fc * 1000) / 1000 # veya malzeme kütüphanesinden Ec (ksi)
    EI_eff = Es * Is + Es * Isr + C3 * Ec * Ic
    
    # Euler Burkulma Yükü (AISC I2-5)
    Pe = (math.pi**2 * EI_eff) / ((K * L)**2)
    
    # AISC 360-22: Kutu için C2 = 0.85, Boru için C2 = 0.95
    C2 = 0.85 if sec_type == "BOX" else 0.95
    
    Pp = Fy * As + C2 * fc * (Ac + Asr * (Es / Ec))
    Py = Fy * As + 0.70 * fc * (Ac + Asr * (Es / Ec))
    
    # Kesit Basınç Kapasitesi Pno (AISC I2.2b)
    if sec_class == SectionClass.COMPACT:
        Pno = Pp
    elif sec_class == SectionClass.NONCOMPACT:
        # lambda limitleri ilgili kesit sınıfından alınır
        Pno = Pp - (Pp - Py) * ((lambda_val - lambda_p) / (lambda_r - lambda_p))**2
    elif sec_class == SectionClass.SLENDER:
        if sec_type == "BOX":
            Fcr = 9.0 * Es / ((b_or_D / t)**2)
        else: # PIPE
            Fcr = 0.72 * Fy / (((b_or_D / t) * (Fy / Es))**0.2)
        Pno = Fcr * As + 0.70 * fc * (Ac + Asr * (Es / Ec))

    # Global Burkulma Tahkiki (AISC I2-2, I2-3)
    if Pno / Pe <= 2.25:
        Pn = Pno * (0.658**(Pno / Pe))
    else:
        Pn = 0.877 * Pe
        
    return Pn, PHI_C * Pn
```

### 5.2. Eğilme Dayanımı ($M_n$)
```python
def calculate_Mn_filled(sec_class, Mp, My, Mcr, lambda_val, lambda_p, lambda_r):
    """
    AISC 360-22 Denklem (I4.4b)
    """
    if sec_class == SectionClass.COMPACT:
        Mn = Mp
    elif sec_class == SectionClass.NONCOMPACT:
        Mn = Mp - (Mp - My) * ((lambda_val - lambda_p) / (lambda_r - lambda_p))
    elif sec_class == SectionClass.SLENDER:
        Mn = Mcr
    return Mn, PHI_B * Mn
```

### 5.3. Kesme Dayanımı ($V_n$) - *AISC 360-22 GÜNCELLENMİŞ MODEL*
*Önemli Değişiklik:* AISC 360-22'de dolgulu kompozit kesitlerin kesme dayanımına beton dolgu katkısı ($0.06 K_c A_c \sqrt{f'_c}$) eklenmiştir (Denklem I4-1).

```python
def calculate_Vn_filled(sec_type, sec_class, Fy, fc, As, Ac, H, B, tf, tw, D, t, Mu, Vu, d):
    """
    AISC 360-22 Bölüm 3.5.4 & Denklem (I4-1)
    Vn = 0.6 * Av * Fy + 0.06 * Kc * Ac * sqrt(f'c)
    """
    # 1. Çelik Kesme Alanı (Av)
    if sec_type == "BOX":
        # Majör eksen (Y ekseni boyunca kesme) varsayımı:
        h_shear = H - 3.0 * tf
        t_shear = tw
        Av = 2.0 * h_shear * t_shear
    else: # PIPE
        Av = 2.0 * As / math.pi

    # 2. Kc Katsayısı (Kompaktlık ve M/(V*d) oranına bağlı)
    if sec_class != SectionClass.COMPACT or Vu == 0:
        Kc = 1.0
    else:
        shear_span = abs(Mu / (Vu * d)) if (Vu * d) != 0 else 1.0
        
        if sec_type == "BOX":
            if shear_span >= 0.7:
                Kc = 1.0
            elif shear_span >= 0.5:
                Kc = 1.0 + ((10.0 - 1.0) * (shear_span - 0.5)) / (0.7 - 0.5)
            else:
                Kc = 10.0
        else: # PIPE
            if shear_span >= 0.7:
                Kc = 1.0
            elif shear_span >= 0.5:
                Kc = 1.0 + ((9.0 - 1.0) * (shear_span - 0.5)) / (0.7 - 0.5)
            else:
                Kc = 9.0

    # 3. Nominal Kesme Dayanımı
    # Not: f'c psi yerine ksi cinsindeyse kök içi dönüşümüne dikkat edilmelidir.
    # Formül f'c (psi) bazlıdır: 0.06 * Kc * Ac * sqrt(f'c_ksi * 1000) / 1000
    concrete_contrib = 0.06 * Kc * Ac * math.sqrt(fc * 1000.0) / 1000.0
    steel_contrib = 0.60 * Av * Fy
    
    Vn = steel_contrib + concrete_contrib
    return Vn, PHI_V * Vn
```

---

## 6. GÖMÜLÜ KESİTLER İÇİN NOMİNAL DAYANIMLAR (ENCASED I-SECTIONS)

- **Yerel Burkulma:** Gömülü I-profiller her zaman **Kompakt (Compact)** kabul edilir (AISC I1.2).
- **Eksenel Basınç ($P_{no}$):** $P_{no} = F_y A_s + F_{ysr} A_{sr} + 0.85 f'_c A_c$ (AISC I2-4)
- **Efektif Rijitlik ($EI_{eff}$):**
  $$C_1 = 0.25 + 3\left(\frac{A_s + A_{sr}}{A_g}\right) \le 0.7$$
  $$EI_{eff} = E_s I_s + E_s I_{sr} + C_1 E_c I_c$$
- **Majör Eğilme ($M_p$):** Plastik Tarafsız Eksen ($h_n$) derinliğine göre belirlenir:
  1. $h_n \le \frac{d}{2} - t_f$ (Flanşların altında, gövdede)
  2. $\frac{d}{2} - t_f < h_n \le \frac{d}{2}$ (Flanş içinde)
  3. $h_n > \frac{d}{2}$ (Flanşın üstünde, betonda)
- **Kesme Dayanımı:** Çelik gövde kesme dayanımı, beton enine donatı katkısı ($V_c$) ve kompozit sistem birlikte kontrol edilerek en elverişli olanı esas alınır (AISC I4.1).

---

## 7. BİRLEŞİK KUVVETLER ETKİLEŞİMİ (COMBINED FORCES - D/C RATIO)

### 7.1. Kompakt Kesitler için P-M Etkileşimi (AISC H1-1a / H1-1b)
```python
def check_interaction_compact(Pr, Pc, Mrx, Mcx, Mry, Mcy):
    axial_ratio = Pr / Pc
    if axial_ratio >= 0.2:
        dc_ratio = axial_ratio + (8.0 / 9.0) * ((Mrx / Mcx) + (Mry / Mcy))
    else:
        dc_ratio = (axial_ratio / 2.0) + ((Mrx / Mcx) + (Mry / Mcy))
    return dc_ratio
```

### 7.2. Narin veya Kompakt Olmayan Dolgulu Kesitler (AISC 360-22 Bölüm I5-1)
*Önemli Değişiklik:* AISC 360-22 standartlarında noncompact ve slender dolgulu kolonlar için yeni $c_{sr}, c_p, c_m$ parametreleri içeren bilinear denklem kontrolü zorunludur:

```python
def check_interaction_noncompact_slender_filled(sec_type, Pr, Pc, Mrx, Mcx, Mry, Mcy, As, Fy, Asr, Fysr, Ac, fc):
    """
    AISC 360-22 Denklemleri (I5-1a, I5-1b, I5-2, Tablo I5.1)
    """
    # 1. c_sr Parametresi (AISC I5-2)
    csr = (As * Fy + Asr * Fysr) / (Ac * fc)
    
    # 2. c_p Parametresi (Tablo I5.1)
    if sec_type == "BOX":
        cp = 0.17 / (csr**0.4)
    else: # PIPE
        cp = 0.27 / (csr**0.4)
        
    # 3. c_m Parametresi (Tablo I5.1)
    if csr >= 0.5:
        cm = (1.06 / (csr**0.11)) if sec_type == "BOX" else (1.10 / (csr**0.08))
    else:
        cm = (0.90 / (csr**0.36)) if sec_type == "BOX" else (0.95 / (csr**0.32))

    # 4. Etkileşim Oranı (AISC I5-1a, I5-1b)
    axial_ratio = Pr / Pc
    bending_sum = (Mrx / Mcx) + (Mry / Mcy)
    
    if axial_ratio >= cp:
        dc_ratio = axial_ratio + ((1.0 - cp) / cm) * bending_sum
    else:
        dc_ratio = ((1.0 - cm) / cp) * axial_ratio + bending_sum
        
    return dc_ratio
```

### 7.3. Borularda Çift Eksenli Eğilme Kuralı (SRSS Exception)
ETABS v22 kurallarına göre, dairesel boru dolgulu kesitlerde majör ve minör momentler cebirsel toplanmaz; vektörel bileşke (SRSS) alınır:
$$\left(\frac{M_r}{M_c}\right)_{resultant} = \sqrt{\left(\frac{M_{rx}}{M_{cx}}\right)^2 + \left(\frac{M_{ry}}{M_{cy}}\right)^2}$$

### 7.4. Burulma Etkisi Altındaki Kutu Kesitler (AISC H3-6)
Eğer $T_r > 0.2 T_c$ ise burulma ihmal edilemez:
$$\frac{P_r}{P_c} + \left(\frac{M_{rx}}{M_{cx}} + \frac{M_{ry}}{M_{cy}}\right) + \left(\frac{V_{rx}}{V_{cx}} + \frac{V_{ry}}{V_{cy}} + \frac{T_r}{T_c}\right)^2 \le 1.0$$

---

## 8. AI KOD ÜRETİMİ İÇİN UYGULAMA REHBERİ (EXECUTION ROADMAP)
1. **Model Katmanı:** 
   - `FilledBoxColumn`, `FilledPipeColumn`, `EncasedIColumn` sınıflarını `BaseCompositeColumn` taban sınıfından türet.
2. **Hesaplama Sırası:**
   - Adım 1: Geometrik özellikleri hesapla ($A_s, A_c, I_s, I_c, b/t, h/t, D/t$).
   - Adım 2: Kesit yerel burkulma sınıflandırmasını yap (`classify_*`).
   - Adım 3: İkinci mertebe katsayılarını uygula ($B_1, B_2 \to P_r, M_r$).
   - Adım 4: Eksenel ($P_n$), Eğilme ($M_n$), Kesme ($V_n$, AISC 360-22 beton katkılı) ve Burulma ($T_n$) kapasitelerini türet.
   - Adım 5: Kesit sınıfına göre uygun etkileşim denklemini (`check_interaction_*`) çağır.
   - Adım 6: D/C oranını $0.95$ eşik değeri ile kontrol et.
```

***

### 360-16 ve 360-22 Arasındaki Kodlamayı Etkileyen Kritik Farklar:
1. **Dolgulu Kesit Kesme Dayanımı ($V_n$):** 360-16'da beton katkısı tamamen ihmal edilip sadece çelik cidar ($V_n = 0.6 F_y A_w C_v$) esas alınırken; 360-22'de **$V_n = 0.6 A_v F_y + 0.06 K_c A_c \sqrt{f'_c}$** formülü ile beton katkısı ve kesit narinliğine göre $K_c$ katsayısı devreye girmiştir [p. 46].
2. **Kompakt Olmayan/Narin Kesit Etkileşimi:** 360-22'de dolgulu narin kesitler için klasik H1-1 denklemleri yerine **$c_{sr}, c_p, c_m$** katsayılarını içeren **AISC I5-1a ve I5-1b** etkileşim denklemleri zorunlu kılınmıştır [p. 63-64].
3. **Boru Kesit $C_2$ Katsayısı:** $P_p$ hesaplanırken boru kesitler için $C_2$ katsayısı 360-16'da $0.90$ iken, 360-22'de **$0.95$**'e çıkarılmıştır [p. 36].