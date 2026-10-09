# AI IDE KODLAMA REHBERİ: AISC 360-16 Kompozit Kolon Tasarımı (ETABS Referanslı)

## 1. GİRİŞ VE BAĞLAM (CONTEXT)
Bu doküman, AISC 360-16 yönetmeliğine göre Kompozit Kolon (Dolgulu Kutu/Boru ve Gömülü I-Kesit) tasarımı yapacak bir yazılım modülünün temel kurallarını içerir. 
**AI Asistanı İçin Talimat:** Bu dokümandaki formülleri ve mantık ağaçlarını kullanarak `CompositeColumn` sınıfı (class) ve ilgili metotları (methods) oluştur. Tüm hesaplamalarda kip-inç (kips, in, ksi) birim sistemi varsayılacaktır.

## 2. MALZEME VE GÜVENLİK KATSAYILARI (CONSTANTS)
Tasarım LRFD (Load and Resistance Factor Design) yöntemine göre yapılacaktır.

```python
# LRFD Direnç Katsayıları (Resistance Factors - Phi)
PHI_C = 0.75  # Basınç (Compression)
PHI_B = 0.90  # Eğilme (Bending/Flexure)
PHI_T = 0.90  # Çekme (Tension)
PHI_V = 0.90  # Kesme (Shear)
PHI_TOR = 0.90 # Burulma (Torsion)

# Malzeme Sabitleri
E_s = 29000.0  # Çelik Elastisite Modülü (ksi)
```

## 3. KESİT SINIFLANDIRMASI (SECTION CLASSIFICATION)
Kesitler yerel burkulma (local buckling) açısından Kompakt (Compact), Kompakt Olmayan (Noncompact) veya Narin (Slender) olarak sınıflandırılmalıdır.

### 3.1. Dolgulu Kutu Kesitler (Filled Box)
```python
def classify_filled_box(b, t, h, F_y, E_s, is_flexure=True):
    # b: flanş genişliği, h: gövde yüksekliği, t: et kalınlığı
    lambda_flange = b / t
    lambda_web = h / t
    
    # Flanş Sınırları (Eğilme ve Basınç için aynı)
    lambda_p_flange = 2.26 * sqrt(E_s / F_y)
    lambda_r_flange = 3.00 * sqrt(E_s / F_y)
    lambda_s_flange = 5.00 * sqrt(E_s / F_y)
    
    # Gövde Sınırları
    if is_flexure:
        lambda_p_web = 3.00 * sqrt(E_s / F_y)
        lambda_r_web = 5.70 * sqrt(E_s / F_y)
        lambda_s_web = 5.70 * sqrt(E_s / F_y)
    else: # Axial Compression
        lambda_p_web = 2.26 * sqrt(E_s / F_y)
        lambda_r_web = 3.00 * sqrt(E_s / F_y)
        lambda_s_web = 5.00 * sqrt(E_s / F_y)
        
    # Sınıflandırma Mantığı (En kritik olan seçilir)
    # Return: "Compact", "Noncompact", "Slender", "TooSlender"
```

### 3.2. Dolgulu Boru Kesitler (Filled Pipe)
```python
def classify_filled_pipe(D, t, F_y, E_s, is_flexure=True):
    lambda_val = D / t
    if is_flexure:
        lambda_p = 0.09 * E_s / F_y
        lambda_r = 0.31 * E_s / F_y
        lambda_s = 0.31 * E_s / F_y
    else: # Axial Compression
        lambda_p = 0.15 * E_s / F_y
        lambda_r = 0.19 * E_s / F_y
        lambda_s = 0.31 * E_s / F_y
```

## 4. İKİNCİ MERTEBE ETKİLER (P-DELTA AMPLIFICATION)
Gerekli dayanımlar (Required Strengths) $B_1$ ve $B_2$ katsayıları ile büyütülmelidir.

```python
def calculate_amplified_forces(P_nt, P_lt, M_nt, M_lt, P_e1, M_a, M_b, alpha=1.0):
    # C_m Katsayısı
    C_m = 0.6 - 0.4 * (M_a / M_b) # M_a/M_b is positive for reverse curvature
    
    # B_1 Katsayısı (Sway önlenmiş)
    P_r_first_order = P_nt + P_lt
    B_1 = C_m / (1 - (alpha * P_r_first_order) / P_e1)
    B_1 = max(B_1, 1.0)
    
    # B_2 Katsayısı kullanıcı tarafından girilir veya P_story üzerinden hesaplanır.
    # M_r ve P_r hesaplaması
    M_r = B_1 * M_nt + B_2 * M_lt
    P_r = P_nt + B_2 * P_lt
    
    return P_r, M_r
```

## 5. NOMİNAL DAYANIMLAR: DOLGULU KESİTLER (FILLED SECTIONS)

### 5.1. Eksenel Basınç Dayanımı ($P_n$)
```python
def calculate_compressive_strength_filled(section_type, classification, A_s, A_c, A_sr, F_y, f_c, E_s, E_c, K, L):
    # C2 Katsayısı
    C_2 = 0.85 if section_type == "Box" else 0.90
    
    # P_p ve P_y Hesaplaması
    P_p = F_y * A_s + C_2 * f_c * (A_c + A_sr * (E_s / E_c))
    P_y = F_y * A_s + 0.7 * f_c * (A_c + A_sr * (E_s / E_c))
    
    # P_no Hesaplaması (Sınıflandırmaya göre)
    if classification == "Compact":
        P_no = P_p
    elif classification == "Noncompact":
        # lambda, lambda_p, lambda_r değerleri ilgili elemandan alınır
        P_no = P_p - (P_p - P_y) * ((lambda_val - lambda_p) / (lambda_r - lambda_p))**2
    elif classification == "Slender":
        F_cr = calculate_F_cr_slender(section_type, b, t, D, F_y, E_s)
        P_no = F_cr * A_s + 0.7 * f_c * (A_c + A_sr * (E_s / E_c))
        
    # Efektif Rijitlik (EI_eff)
    C_3 = min(0.45 + 3 * (A_s / (A_c + A_s)), 0.90)
    EI_eff = E_s * I_s + E_s * I_sr + C_3 * E_c * I_c
    
    # Euler Burkulma Yükü
    P_e = (math.pi**2 * EI_eff) / (K * L)**2
    
    # Nominal Basınç Dayanımı (P_n)
    if P_no / P_e <= 2.25:
        P_n = P_no * (0.658 ** (P_no / P_e))
    else:
        P_n = 0.877 * P_e
        
    return P_n * PHI_C
```

### 5.2. Eğilme Dayanımı ($M_n$)
```python
def calculate_flexural_strength_filled(classification, M_p, M_y, M_cr, lambda_val, lambda_p, lambda_r):
    if classification == "Compact":
        M_n = M_p
    elif classification == "Noncompact":
        M_n = M_p - (M_p - M_y) * ((lambda_val - lambda_p) / (lambda_r - lambda_p))
    elif classification == "Slender":
        M_n = M_cr
        
    return M_n * PHI_B

# M_p Hesaplaması (Kutu Kesit İçin)
# M_p = M_D - F_y * Z_sn - 0.5 * (0.85 * f_c * Z_cn)
# M_D = F_y * Z_s + 0.5 * (0.85 * f_c * Z_c)
```

### 5.3. Kesme Dayanımı ($V_n$)
```python
def calculate_shear_strength_filled(section_type, F_y, A_w, h, t_w, E_s):
    if section_type == "Box":
        k_v = 5.0
        limit1 = 1.10 * sqrt(k_v * E_s / F_y)
        limit2 = 1.37 * sqrt(k_v * E_s / F_y)
        
        ratio = h / t_w
        if ratio <= limit1:
            C_v = 1.0
        elif ratio <= limit2:
            C_v = limit1 / ratio
        else:
            C_v = (1.51 * E_s * k_v) / ((ratio**2) * F_y)
            
        V_n = 0.6 * F_y * A_w * C_v
        
    elif section_type == "Pipe":
        F_cr = max(0.78 * E_s / (D/t)**1.5, 0.6 * F_y) # F_cr <= 0.6Fy
        V_n = F_cr * (A_g / 2)
        
    return V_n * PHI_V
```

## 6. NOMİNAL DAYANIMLAR: GÖMÜLÜ KESİTLER (ENCASED SECTIONS)
Gömülü I-Kesitler her zaman "Kompakt (Compact)" kabul edilir.

### 6.1. Eksenel Basınç Dayanımı ($P_n$)
```python
def calculate_compressive_strength_encased(A_s, A_c, A_sr, F_y, F_ysr, f_c, E_s, E_c, I_s, I_sr, I_c, K, L):
    P_no = F_y * A_s + F_ysr * A_sr + 0.85 * f_c * A_c
    
    C_1 = min(0.25 + 3 * (A_s / (A_c + A_s)), 0.70)
    EI_eff = E_s * I_s + E_s * I_sr + C_1 * E_c * I_c
    
    P_e = (math.pi**2 * EI_eff) / (K * L)**2
    
    if P_no / P_e <= 2.25:
        P_n = P_no * (0.658 ** (P_no / P_e))
    else:
        P_n = 0.877 * P_e
        
    return P_n * PHI_C
```

### 6.2. Eğilme Dayanımı ($M_n$)
Plastik Tarafsız Eksen (PNA) konumuna göre hesaplanır.
```python
# AI Instruction: PNA'nın flanşın içinde mi yoksa altında mı olduğunu kontrol et.
# h_n formülleri PDF Şekil 3-9 ve 3-10'a göre kodlanmalıdır.
# M_p = M_D - Z_sn * F_y - 0.5 * Z_cn * (0.85 * f_c)
```

## 7. BİRLEŞİK KUVVETLER ETKİLEŞİMİ (INTERACTION EQUATIONS - D/C RATIO)
AISC H1.1 denklemleri kullanılarak Talep/Kapasite (Demand/Capacity - D/C) oranı hesaplanır.

```python
def calculate_interaction_ratio(P_r, P_c, M_r33, M_c33, M_r22, M_c22):
    # P_r: Gerekli Eksenel Kuvvet (Amplified)
    # P_c: Tasarım Eksenel Kapasitesi (Phi * P_n)
    # M_r: Gerekli Eğilme Momenti (Amplified)
    # M_c: Tasarım Eğilme Kapasitesi (Phi * M_n)
    
    axial_ratio = P_r / P_c
    
    if axial_ratio >= 0.2:
        dc_ratio = axial_ratio + (8.0 / 9.0) * ((M_r33 / M_c33) + (M_r22 / M_c22))
    else:
        dc_ratio = (axial_ratio / 2.0) + ((M_r33 / M_c33) + (M_r22 / M_c22))
        
    return dc_ratio
    # AI Instruction: Eğer Boru (Pipe) kesit ise, M_r33 ve M_r22 vektörel olarak 
    # (SRSS - Karelerin Toplamının Karekökü) birleştirilerek tek bir M_r gibi işleme sokulmalıdır.
```

## 8. AI İÇİN UYGULAMA ADIMLARI (IMPLEMENTATION STEPS)
1. **Veri Modeli:** `CompositeSection` adında bir base class oluştur. `FilledBox`, `FilledPipe`, `EncasedIShape` sınıflarını bundan türet (inherit).
2. **Geometri ve Malzeme:** Sınıf başlatıcılarında (constructor) $b, h, t, D, A_s, A_c, A_{sr}, F_y, f'_c$ gibi özellikleri tanımla.
3. **Metotlar:** Yukarıdaki pseudo-code bloklarını sınıf metotları olarak implemente et.
4. **Kontrol:** Her tasarım adımında D/C oranını hesapla. D/C > 0.95 (varsayılan limit) ise kesiti yetersiz (Fail) olarak işaretle.
```

Bu yapı, bir LLM'in (Büyük Dil Modeli) okuyup doğrudan Python, C# veya C++ sınıflarına dönüştürebileceği kadar modüler ve matematiksel olarak net bir şekilde hazırlanmıştır. Formüller tamamen sağladığınız PDF'in 3. Bölümündeki AISC 360-16 kurallarından çekilmiştir.