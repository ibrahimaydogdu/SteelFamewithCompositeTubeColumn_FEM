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