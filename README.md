# Dpp Backend

ASP.NET Core 8 minimal API. Ürün kataloğu, CSV içe aktarma ve dijital ürün pasaportu uç noktalarını PostgreSQL üzerinden sunar.

## Gereksinimler

- .NET 8 SDK
- PostgreSQL 15 veya üzeri

## PostgreSQL ayarı

Bağlantı dizesi `appsettings.json` içindeki `ConnectionStrings:Dpp` alanında tanımlıdır. Uygulamayı başlatmadan önce `Username` ve `Password` değerlerini kendi PostgreSQL kullanıcı bilgilerinizle güncelleyin. 

Uygulama `dpp` veritabanına bağlanır. Veritabanı henüz yoksa oluşturmayı dener; bu nedenle PostgreSQL kullanıcısının `CREATEDB` yetkisi olmalıdır. Tablolar ilk başlatmada oluşturulur. `dpp` içinde ürün yoksa `Data/urunler_duzeltilmis_test.csv` içe aktarılır.

## Çalıştırma

### Visual Studio

`Dpp.sln` dosyasını açın ve üst araç çubuğundaki başlangıç profili olarak **http** seçip çalıştırın. Geliştirme ortamında Swagger tarayıcıda açılır.

### Terminal

Backend klasöründe çalıştırın:

```powershell
dotnet restore .\Dpp.csproj
dotnet run --launch-profile http --project .\Dpp.csproj
```

API `http://localhost:5080` adresinde dinler. Swagger arayüzü `http://localhost:5080/swagger` adresindedir.

## API uç noktaları

| Yöntem | Yol | Açıklama |
|---|---|---|
| `GET` | `/api/health` | Sağlık kontrolü |
| `GET` | `/api/products` | Ürün listesi |
| `GET` | `/api/products/{gtin}` | Ürün ayrıntısı |
| `POST` | `/api/imports/csv` | CSV yükleme (`multipart/form-data`, alan adı `file`) |
| `GET` | `/api/products/{gtin}/qr` | Ürünün QR kodu (PNG) |
| `GET` | `/dpp/{gtin}` | HTML ürün pasaportu |
| `GET` | `/dpp/{gtin}.jsonld` | Ürün pasaportu (JSON-LD) |

Ürün ayrıntısı, HTML ve JSON-LD uç noktalarında `?rol=yetkili` parametresi karbon bilgisini gösterir. 

## CSV biçimi

UTF-8 CSV dosyasının başlık satırı şu alanları içermelidir:

```text
gtin,model_adi,batch_no,elyaf_pamuk_yuzde,elyaf_elastan_yuzde,uretim_yeri,karbon_kg_co2e
```

GTIN, GS1 kontrol basamağı geçerli 14 haneli metin olmalıdır. Pamuk ve elastan değerleri 0–100 aralığında ve toplamları 100 olmalıdır. Model adı, lot numarası ve üretim yeri boş bırakılamaz; karbon değeri sayısal ve negatif olmayan olmalıdır. Dosya boyutu 10 MB ile sınırlıdır. Geçersiz satırlar içe aktarılmaz; sonuçta satır numarasıyla raporlanır.

## Veritabanı tabloları

- `products`: GTIN ve ürünün model/lot ilişkileri
- `product_models`: ürün modeli bilgileri
- `production_batches`: lot, üretim yeri, elyaf oranları ve karbon bilgisi

