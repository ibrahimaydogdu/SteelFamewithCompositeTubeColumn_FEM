# Aşama 6 — Hibrit kolon: geçiş katının optimizasyonu (ÖNERİ)

- **Tarih:** 2026-10-05.
- **Durum:** uygulandı (Aşama 6, 0.5.0; `DEGISIKLIKLER.md`). Geçiş hem yığın bazında hem grup bazında seçilebiliyor. Bölüm 3.6'daki ayrı çakışma kontrolü eklenmedi: referans program bunu zaten bildiriyor.

## 1. Hedef (kullanıcı kararı, 2026-10-03)
- Kolonun çelik mi kompozit (dolgulu tüp) mi olacağına **kullanıcı da karar verebilmeli, optimizasyon da**.
- Çalışmanın amacı: "**belirli kata kadar kompozit, üstü çelik**" düzeninin optimizasyonu. Yani kompozitten çeliğe geçiş katının, kesitlerle birlikte en düşük maliyete göre bulunması.

## 2. Modellerde kolon yapısı (ölçüm)

**525Member** (25 kat): kolon grupları 5 katlık bantlar halinde. İki dikey "kolon yığını" var:

| Yığın | Gruplar (alttan üste) | Kolon hattı |
|---|---|---|
| Çevre | 5 (kat 1–5), 7 (6–10), 9 (11–15), 11 (16–20), 13 (21–25) | 8 hat (C1, C3, C6–C11) |
| Orta | 6 (1–5), 8 (6–10), 10 (11–15), 12 (16–20), 14 (21–25) | 1 hat (C12) |

**460Member** (20 kat): 8 kolon grubu, 2–3 katlık bantlar. **Model hatası:**
- Grup 11 hem 5–6. katları hem 20. katı kapsıyor.
- 20. kattaki 10 kolon **hem grup 6'da hem grup 11'de**. Bir eleman iki tasarım grubundaysa iki farklı kesit ataması alır; hangisinin geçerli olacağı belirsizdir. Hibrit tasarımda tip de belirsiz olur.
- Bu durum bugünkü programda da sessizce yanlış sonuç verir.

## 3. Önerilen tasarım

### 3.1 Kolon tipi modları (form)
"Composite columns" seçimi üç moda genişler:

| Mod | Davranış |
|---|---|
| Steel | Bütün kolonlar W profil (bugünkü "kompozit kapalı"). |
| Composite | Bütün kolon grupları kompozit (bugünkü mod; dolgulu tüp veya gömülü). |
| **Hybrid** | Her kolon grubu için ayrı seçim: *Steel* / *Composite* / *Optimize* (varsayılan *Optimize*). |

Hibrit modda form, model okunduktan sonra kolon gruplarını bir tabloda listeler (grup, katlar, kolon sayısı, tip). Kullanıcının seçimleri yedeğe ve sonuç dosyasına yazılır.

### 3.2 Kolon yığınları ve geçiş katı değişkeni
- **Yığın:** düşey olarak birbirine bağlanan kolon grupları. Program bunları mevcut kolon–kolon (C-C) geometri ilişkilerinden otomatik bulur ve alttan üste sıralar. 525M'de 2 yığın (çevre, orta), her birinde 5 grup.
- **Geçiş değişkeni** t, yığın başına bir tam sayıdır (0 … n):
  - yığının alttan t grubu kompozit, üstündekiler çelik olur;
  - t = 0 tümü çelik, t = n tümü kompozit demektir;
  - geçiş yalnızca grup sınırlarında olabilir, çünkü bir grubun tek bir kesiti ve tipi var. 525M'de geçiş katları 5, 10, 15 ve 20. katlardır.
- Kullanıcının *Steel* ya da *Composite* diye sabitlediği gruplar geçiş değişkeninden etkilenmez. Sabitlenmiş bir kompozit grup bir çelik grubun üstünde kalırsa uyarı verilir ama izin verilir; bu kullanıcının kararıdır.
- **Seçenek** (Bölüm 5, soru 1): bütün yığınlar için **tek bir geçiş değişkeni**. Sonuç uygulamada daha sade olur (aynı katta geçiş) ve değişken sayısı azalır.

### 3.3 Kesit değişkenleri
- *Optimize* grubunun **iki kesit değişkeni** olur: W listesi indisi ve tüp kataloğu indisi. Geçiş değişkeninin belirlediği tip hangisiyse o değişken kullanılır; diğeri pasif kalır ama aramadaki bilgisini korur.
  - Gerekçe: tip değiştiğinde kesit "baştan" seçilmek zorunda kalmaz. 16 yöntemin hepsi tam sayı vektörüyle çalıştığı için yöntemlerde değişiklik gerekmez.
  - Maliyeti: değişken sayısı artar. 525M'de 14 değişken yerine 4 kiriş + 10 × 2 kolon + 2 geçiş = **26** değişken (tek geçiş seçeneğinde 25).
- **Sınırlar:** W değişkenleri bugünkü başlangıç tasarımından; tüp değişkenleri Aşama 5'teki gibi; geçiş değişkeni 0 … n.
- **Alternatif** (önerilmez): grup başına tek değişken; tip değişince kesit eşdeğer kapasiteli kesite eşlenir. Değişken sayısı aynı kalır, ama tip değişimi aramada büyük sıçramalara yol açar.

### 3.4 Kısıtlar ve onarım
- **Geçişte geometri:** üstteki W kolon alttaki tüpün üzerine oturur. Koşul: W'nin derinliği ve başlık genişliği ≤ tüpün dış boyutu (ek levhası ile birleşim). Farklı tipler arasında alan karşılaştırması yapılmaz. Aynı tip gruplar arasında bugünkü kurallar geçerlidir.
- **Kiriş–kolon:** etkin tipe göre (W başlığı ya da tüp yüzü).
- **Onarım** (PMM, öteleme): yalnızca etkin kesit değişkeni büyütülür. Geçiş değişkenini optimizasyon yöntemi belirler; onarım değiştirmez.
- **Tasarım:**
  - çelik gruplar ETABS çelik tasarımıyla;
  - kompozit gruplar iç hesapla (arama) ve ETABS kompozit kolon tasarımıyla (final);
  - oranlar Aşama 5.1'deki D/C sınırına göre değerlendirilir.
- Tip değişen grupta ETABS tasarım prosedürü de değişir (çelik ↔ "No Design" / kompozit). Atama önbelleği (tip, kesit) çiftine göre tutulur.

### 3.5 Maliyet ve çıktılar
- **Maliyet:** her grup etkin tipine göre hesaplanır: W çeliği; tüpte çelik + beton.
- **Çıktılar:**
  - yığın başına geçiş, ör. "Çevre yığını: kat 1–10 kompozit (CFT), 11–25 çelik";
  - Excel'de grup tipi sütunu ve geçiş tablosu;
  - en iyi tasarım modeli (`_best.EDB`) tiplerine göre kurulur.

### 3.6 Model denetimi (yeni)
Başlangıçta bir çerçeve elemanı birden fazla tasarım değişkeni grubundaysa program durur ve elemanları listeler. Bu, 460Member'daki gibi hataları yakalar ve bütün modlarda geçerli olur.

## 4. Testler
1. **Yığın bulma:** 525M'de 2 yığın × 5 grup, doğru sıralama; 460Member'da çakışma uyarısı.
2. **Regresyon:**
   - Steel ve Composite modları bugünkü sonuçları birebir vermeli (525M gömülü: 7564,91 / 1,4580);
   - MathTest değişmemeli.
3. **Hibrit, ETABS uçtan uca** (525M kopyası):
   - geçiş değişkenine göre tip ve tasarım prosedürü ataması;
   - tip değişen grupta kesit ve prosedürün doğru güncellenmesi;
   - final doğrulamasında çelik ve kompozit tasarımın birlikte çalışması;
   - çıktıda geçiş katının yazılması.
4. **Form:** hibrit tablo, kullanıcı sabitlemeleri, yedekten geri yükleme.

## 5. Kullanıcıya sorular
1. Geçiş değişkeni **yığın başına** mı olsun (önerim; çevre ve orta kolonlar farklı katta geçebilir), **bütün yapı için tek** mi, yoksa ikisi de seçenek olarak mı (varsayılan yığın başına)?
2. *Optimize* gruplarda **iki kesit değişkeni** yaklaşımı uygun mu?
3. Kullanıcı bir kompozit grubu bir çelik grubun **üstüne** sabitlerse yalnızca uyarı mı verilsin, yoksa engellensin mi?
4. **460Member** düzeltilsin mi? Önerim: modeli değiştirmeden programa çakışma denetimini eklemek. Model dosyasını siz ETABS'te düzeltin ya da ben bir kopyasında 20. kat kolonlarını grup 11'den çıkarıp `Modeller/` altına ayrı bir model olarak ekleyeyim.
5. Hibrit tip seçimi formda bir **tabloyla** mı yapılsın (önerim), yoksa ayar dosyasındaki grup listeleriyle mi?
