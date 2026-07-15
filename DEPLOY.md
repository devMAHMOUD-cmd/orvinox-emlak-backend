# Craftora Deploy Checklist

Bu checklist, production deploy sirasinda veritabani adimlarinin unutulmamasi icin tutulur.

Backend artik acilista migration, hardening veya DDL calistirmaz. Yani API, veritabani semasini olusturmaz ve RLS/security patch uygulamaz. Veritabani kurulumu ve guvenlik patch'leri deploy oncesinde admin yetkili PostgreSQL kullanicisi ile elle calistirilmalidir.

## Sifirdan / Bos DB Kurulumu

### 1. Backup Al

Mevcut veri varsa once backup al.

```bash
docker exec -t postgres_server pg_dump -U admin -d CraftoraMobile > backup_$(date +%Y%m%d_%H%M%S).sql
```

PowerShell ornegi:

```powershell
docker exec -t postgres_server pg_dump -U admin -d CraftoraMobile | Out-File -Encoding utf8 backup_$(Get-Date -Format yyyyMMdd_HHmmss).sql
```

### 2. Sema Kur

Bos veritabani icin ana semayi admin ile kur.

```bash
docker exec -i postgres_server psql -U admin -d CraftoraMobile < mysql/craftora.sql
```

PowerShell ornegi:

```powershell
Get-Content .\mysql\craftora.sql | docker exec -i postgres_server psql -U admin -d CraftoraMobile
```

### 3. Hardening / Security Patch Uygula

RLS policy, trigger, constraint, index ve security definer fonksiyonlarini admin ile uygula.

```bash
docker exec -i postgres_server psql -U admin -d CraftoraMobile < database/patches/2026_07_05_live_db_security_sync.sql
```

PowerShell ornegi:

```powershell
Get-Content .\database\patches\2026_07_05_live_db_security_sync.sql | docker exec -i postgres_server psql -U admin -d CraftoraMobile
```

### 4. Runtime Rolunu Olustur

RLS icin kullanilacak non-superuser runtime rolunu admin ile olustur.

```bash
docker exec -i postgres_server psql -U admin -d CraftoraMobile < database/patches/2026_07_10_create_runtime_role.sql
```

PowerShell ornegi:

```powershell
Get-Content .\database\patches\2026_07_10_create_runtime_role.sql | docker exec -i postgres_server psql -U admin -d CraftoraMobile
```

### 5. TODO: GRANT Patch ve Connection String Degisikligi

Bu adim ileride eklenecek.

TODO:

- `craftora_app` rolune gerekli tablo, sequence ve schema GRANT'larini ver.
- Backend connection string'i admin yerine `craftora_app` kullanacak sekilde guncelle.
- `.env` icine runtime role bilgilerini ekle.
- RLS testlerini calistir.

### 6. Backend'i Baslat

DB kurulumu ve patch'ler tamamlandiktan sonra backend'i baslat.

```bash
docker compose up -d --build craftora_api nginx
```

Log kontrolu:

```bash
docker compose logs -f craftora_api
```

## Onemli Notlar

- Backend acilista artik sema olusturmaz.
- Backend acilista artik migration calistirmaz.
- Backend acilista artik `DatabaseHardening.ApplyAsync` calistirmaz.
- DB hazir degilse backend acilabilir, ancak endpointlerde `relation/table does not exist` hatalari verir.
- Migration, hardening, RLS policy ve trigger islemleri sadece admin/superuser yetkili kullanici ile calistirilmalidir.
- Runtime backend ileride `craftora_app` ile baglanacak. Bu rol `NOSUPERUSER` ve `NOBYPASSRLS` olacak; RLS'in gercekten devreye girmesi icin bu zorunludur.
- `admin` rolu migration/hardening gibi DDL isleri icin kalir; runtime API baglantisi icin kullanilmamalidir.

## Prod .env Güvenliği - ZORUNLU

VPS'te `.env` olustururken asagidaki degerler MUTLAKA guclu, rastgele ve essiz olmalidir. Local ortamda kullanilan zayif degerler PROD ortaminda KULLANILMAMALIDIR:

- `POSTGRES_PASSWORD`
- `REDIS_PASSWORD`
- `MINIO_ROOT_PASSWORD`

Her biri icin ayri guclu sifre uret:

```bash
openssl rand -base64 24
```

`JWT_SECRET` su an yeterli uzunlukta olsa bile prod ortaminda yeni bir secret uretmek daha guvenlidir:

```bash
openssl rand -base64 48
```

Dis servis anahtarlarinda PROD anahtarlari kullanilmalidir; test/dev anahtarlari prod `.env` dosyasina yazilmamalidir:

- `GOOGLE_CLIENT_SECRET`
- `STRIPE_PUBLISHABLE_KEY`
- `STRIPE_SECRET_KEY`
- `STRIPE_WEBHOOK_SECRET`
- `RESEND_API_KEY`
- `SENTRY_DSN`

`.env` dosyasi ASLA git'e commit edilmemelidir. Dosya zaten `.gitignore` icinde olmali ve VPS uzerinde guvenli saklanmalidir.

## Elasticsearch Prod Güvenliği - ZORUNLU

> **UYARI:** Local ortamda Elasticsearch auth'suz (`xpack.security.enabled=false`) ve `9200` portu host'a acik olabilir. Bu yapilandirma PROD ortaminda KABUL EDILEMEZ; disaridan erisen biri index verilerini okuyabilir, degistirebilir veya silebilir.

Prod ortaminda asagidakilerden en az biri, tercihen hepsi uygulanmalidir:

1. `docker-compose.yml` icindeki Elasticsearch `ports` yayininin `9200:9200` satirini kaldirin. Backend zaten internal Docker network uzerinden `elasticsearch:9200` adresine baglanir; portun host'a acilmasina gerek yoktur.
2. Port yayinlanmak zorundaysa VPS firewall kurallariyla `9200` portunu internete kapatin; yalnizca gerekli ve guvenilir kaynaklardan erisime izin verin.
3. Ideal olarak `xpack.security.enabled=true` kullanin, Elasticsearch kullanicisi/sifresi tanimlayin ve backend connection string'ine ilgili credentials bilgilerini ekleyin.

Elasticsearch .NET client surumu `8.13.15`, sunucu surumu `8.13.0` ile uyumludur. Sunucu surumu degistirilirse client surumu de uyumluluk acisindan kontrol edilmelidir; uyumsuzluk sessiz arama veya indexleme hatalarina yol acabilir.

Elasticsearch index verisi kaybolursa, admin token ile `POST /api/admin/reindex-products` endpoint'i cagrilarak yayinlanmis ve aktif urunlerden yeniden olusturulabilir.

## Backup

PostgreSQL backup scriptleri `scripts/backup/` altindadir.

Prod Linux sunucuda once calistirma izni ver:

```bash
chmod +x scripts/backup/backup_postgres.sh scripts/backup/restore_postgres.sh
```

Manuel backup:

```bash
./scripts/backup/backup_postgres.sh
```

Ornek cron kurulumu, her gece 03:00:

```bash
crontab -e
```

```cron
0 3 * * * cd /opt/craftora/CoreBackendApi && /opt/craftora/CoreBackendApi/scripts/backup/backup_postgres.sh >> /opt/craftora/backups/logs/postgres_backup_cron.log 2>&1
```

Restore ornegi:

```bash
./scripts/backup/restore_postgres.sh /opt/craftora/backups/postgres/craftora_20260710_030000.dump CraftoraMobile_restore
```

TODO:

- Offsite upload ekle: Backblaze B2, Hetzner Storage Box, AWS S3 veya uzak MinIO.
- MinIO bucket backup icin `mc mirror` scripti ekle.
- Ayda bir restore testi yap ve sonucu kaydet.




423bf130-b4e5-4948-9d2f-c4d7b5a196ff
