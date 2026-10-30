# Performans Ölçme ve Değerlendirme

Türk Dili ve Edebiyatı dersi performans puanlarını, öğretmenin kullandığı uzun ölçüt
(rubrik) listelerini **3–5 ana gruba** indirgeyerek hızlıca değerlendirmeyi sağlayan,
**mobil öncelikli** bir web uygulamasıdır.

> Bu depo yalnızca **uygulama kodunu** içerir. Öğrenci listeleri ve puanlar depoda
> bulunmaz; bunlar kullanıcı tarafından Excel'den içe aktarılır veya elle eklenir.

---

## Öne Çıkan Özellikler

- **Basit puan girişi:** Her bileşendeki ölçütler 3–5 gruba bölünür; öğretmen her gruba
  **0–100** arası tek bir puan verir. Puan, gruptaki ölçütlere **maksimum puanları
  oranında** otomatik dağıtılır.
- **Hazır rubrik şablonları:** 9, 10 ve 11. sınıflar için ölçütler ve maksimum puanlar
  hazır gelir; düzenlenebilir (ölçüt ekle/sil, maksimum puan ve grup adı değiştir).
- **Excel uyumlu hesaplama:** Bileşen puanları 100'lük sisteme çevrilir; **1. ve 2.
  Performans** puanları yaygın kullanılan Excel formülleriyle birebir hesaplanır.
- **Öğrenci ve sınıf yönetimi:** Ekle/düzenle/sil, sınıf değiştir; liste **okul numarasına
  veya ada göre** sıralanabilir.
- **Excel'den öğrenci aktarma:** Dosya yükle veya bir klasördeki tüm `.xlsx` dosyalarını
  tara; sınıf adı otomatik algılanır, mükerrer kayıt oluşmaz.
- **Sonuç ve dışa aktarma:** Dönem sonu tablosu, sınıf ortalaması, CSV, yazdırma; ayrıca
  ölçüt düzenini koruyan **Excel (.xlsx)** ve **PDF** çıktısı.
- **Mobil öncelikli arayüz:** Alt sekme menüsü, kaydırıcı + sayı kutusu, hızlı puan
  düğmeleri ve canlı puan gösterimi.

---

## Teknolojiler

| Katman | Teknoloji |
|---|---|
| Uygulama | .NET 8 · ASP.NET Core **Razor Pages** |
| Veritabanı | **SQLite** · Entity Framework Core 8 |
| Excel | **ClosedXML** |
| PDF | **QuestPDF** |
| Ön yüz | Bootstrap 5 (yerel) · özel CSS · vanilla JavaScript |

---

## Kurulum

**Gereksinim:** [.NET 8 SDK](https://dotnet.microsoft.com/download)

```bash
cd PerfApp
dotnet run
```

Ardından tarayıcıdan açın:

```
http://localhost:5000
```

Farklı bir adres/port için:

```bash
dotnet run --urls http://127.0.0.1:5199
```

İlk çalıştırmada:

1. Ölçüt şablonları (`Data/rubrics.seed.json`) yüklenir ve bileşenler 3–5 gruba bölünür.
2. Veritabanı `PerfApp/App_Data/perf.db` otomatik oluşturulur.
3. (Depoda Excel dosyası yoksa) sınıf/öğrenci oluşmaz — öğrencileri **Excel'den Aktar**
   ekranından veya elle ekleyebilirsiniz.

---

## Nasıl Çalışır? (Hesaplama Mantığı)

Her performans bileşeni (K1, K2, Y1, Y2, B1, B2, D) bir ölçüt listesinden oluşur ve
uygulamada 3–5 gruba indirgenir.

- **Ölçüte dağıtım:** `ölçüt puanı = grup puanı / 100 × ölçütün maksimum puanı`
- **Bileşen puanı (100'lük):** `round(toplam kazanılan / bileşen maksimumu × 100)`

**1. Performans (P1)** — K1, K2, Y1, Y2:

| Sınıf | Formül |
|---|---|
| 9  | Her bileşenin %25'i (her bileşen ayrı yuvarlanır) |
| 10 | Dört bileşenin ortalaması |
| 11 | Her bileşenin %25'i (yuvarlanmadan) |

**2. Performans (P2):** `B1×0,33 + B2×0,33 + D×0,34`

Bir bileşendeki tüm gruplar puanlanmadan o bileşen tamamlanmış sayılmaz.

---

## Excel'den Öğrenci Aktarma

1. **Excel'den Aktar** ekranından bir `.xlsx` dosyası seçin.
2. Uygulama, dosyadaki öğrenci listesinden **okul no** ve **ad-soyad** sütunlarını bulur;
   sınıf adını (`9/A`, `10/B` gibi) sayfa başlıklarından otomatik çıkarır.
3. Önizlemeyi kontrol edip içe aktarın:
   - **Listeyi değiştir:** Mevcut liste silinir, dosyadaki liste yazılır.
   - **Ekle:** Yalnızca eksik öğrenciler eklenir (mükerrer kayıt oluşmaz).

**Toplu aktarma:** "Klasörü tara" düğmesi, uygulamanın üst klasöründeki tüm `.xlsx`
dosyalarını tarar. Puanlar hiçbir zaman içe aktarılmaz; yalnızca öğrenci listesi alınır.

---

## Dışa Aktarma

Not girişleri tamamlandıktan sonra **Dışa Aktar** bölümünden sınıf bazında iki çıktı alınır:

- **Excel (.xlsx):** Her bileşen için ayrı sayfa — yalnızca **ölçüt maddeleri**, `MAKS`
  sütunu ve dağıtılan puanlar; ardından `TOPLAM` ve `BİLEŞEN PUANI (100)` satırları. Ayrıca
  **Özet** (bileşen + 1./2. Performans) ve **Öğrenci Bilgileri** sayfaları.
- **PDF:** Yazdırılabilir rapor — özet tablosu ve her bileşen için ölçüt tablosu.

Gruplar çıktıda gösterilmez; yalnızca ölçüt maddeleri listelenir. Öğrenci sırası ekrandaki
sıralama seçimine uyar. Puanı girilmemiş öğrencilerin hücreleri boş bırakılır.

---

## Proje Yapısı

```
PerfApp/
├─ Program.cs                 Uygulama başlangıcı, DI, tohumlama
├─ Data/
│  ├─ AppDbContext.cs         EF şeması ve ilişkiler
│  └─ rubrics.seed.json       9/10/11. sınıf ölçüt şablonları
├─ Models/Entities.cs         Veri modelleri
├─ Services/
│  ├─ Scoring.cs              Bileşen / 1. / 2. Performans hesaplamaları
│  ├─ RubricGrouper.cs        Ölçütleri 3–5 gruba bölme
│  ├─ RubricService.cs        Grupları yeniden oluşturma
│  ├─ SeedService.cs          Başlangıç tohumlama
│  ├─ StudentImportService.cs Excel okuma ve içe aktarma
│  ├─ StudentOrdering.cs      Öğrenci sıralaması
│  ├─ ExportService.cs        Excel + PDF dışa aktarma
│  └─ Ui.cs                   Biçimlendirme yardımcıları
├─ Pages/                     Razor Pages (Ana Sayfa, Sınıflar, Öğrenciler,
│                             Not Girişi, Sonuçlar, Ölçütler, Dışa Aktar, İçe Aktar)
└─ wwwroot/                   CSS, JavaScript ve yerel kütüphaneler
```

---

## Yapılandırma

Okul adı, dışa aktarılan belgelerin başlığında görünür. Boş bırakılırsa yalnızca sınıf adı
yazılır. `PerfApp/appsettings.json`:

```json
{ "OkulAdi": "" }
```

---

## Notlar

- Uygulama şu an **1. dönem** için yapılandırılmıştır; veri modeli ileriye dönüktür.
- Veritabanını sıfırlamak için uygulamayı durdurup `PerfApp/App_Data/perf.db` dosyasını
  silin; sonraki çalıştırmada yeniden oluşturulur.
- Depoda kişisel veri (öğrenci adı, numarası, puanı) **barındırılmaz**.
