# Craftora Backup Scripts

Bu klasordeki scriptler prod Linux sunucu icindir.

## PostgreSQL Backup

```bash
chmod +x scripts/backup/backup_postgres.sh scripts/backup/restore_postgres.sh
./scripts/backup/backup_postgres.sh
```

Varsayilanlar:

- Container: `postgres_server`
- Database: `CraftoraMobile`
- User: `admin`
- Backup klasoru: `/opt/craftora/backups/postgres`
- Retention: 7 gun

Gerekirse environment variable ile degistirilebilir:

```bash
POSTGRES_DB=CraftoraMobile POSTGRES_USER=admin BACKUP_DIR=/opt/craftora/backups/postgres ./scripts/backup/backup_postgres.sh
```

## PostgreSQL Restore

Hedef database onceden olusturulmus ve bos/disposable olmalidir.

```bash
./scripts/backup/restore_postgres.sh /opt/craftora/backups/postgres/craftora_20260710_030000.dump CraftoraMobile_restore
```

## Windows Local Test Notu

Bu scriptler bash icin yazildi. Windows local ortamda test icin en pratik yol:

1. Git Bash veya WSL kullan.
2. Docker Desktop calisir durumda olsun.
3. Repo kokunden scripti calistir.

Alternatif olarak PowerShell ile manuel test:

```powershell
docker exec postgres_server pg_dump -U admin -d CraftoraMobile -Fc -f /tmp/craftora_backup.dump
docker cp postgres_server:/tmp/craftora_backup.dump .\craftora_backup.dump
```

Restore testi:

```powershell
docker cp .\craftora_backup.dump postgres_server:/tmp/craftora_restore.dump
docker exec postgres_server pg_restore -U admin -d CraftoraMobile_restore --clean --if-exists --no-owner /tmp/craftora_restore.dump
```
