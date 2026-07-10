#!/usr/bin/env bash
set -euo pipefail

# Usage:
#   ./scripts/backup/restore_postgres.sh /opt/craftora/backups/postgres/craftora_20260710_030000.dump CraftoraMobile_restore
#   ./scripts/backup/restore_postgres.sh --force /opt/craftora/backups/postgres/craftora_20260710_030000.dump CraftoraMobile
#
# Restores a custom-format pg_dump into an existing target database.
# The target database should be empty or disposable.

POSTGRES_CONTAINER="${POSTGRES_CONTAINER:-postgres_server}"
POSTGRES_USER="${POSTGRES_USER:-admin}"
PROTECTED_DATABASES="${PROTECTED_DATABASES:-CraftoraMobile}"
FORCE_RESTORE="false"

if [ "${1:-}" = "--force" ] || [ "${1:-}" = "-y" ]; then
  FORCE_RESTORE="true"
  shift
fi

if [ "$#" -ne 2 ]; then
  echo "Usage: $0 [--force|-y] <backup-file.dump> <target-database>"
  exit 2
fi

BACKUP_FILE_PATH="$1"
TARGET_DB="$2"
CONTAINER_RESTORE_PATH="/tmp/craftora_restore_$(date +%Y%m%d_%H%M%S).dump"

if [ ! -f "${BACKUP_FILE_PATH}" ]; then
  echo "ERROR: backup file not found: ${BACKUP_FILE_PATH}"
  exit 1
fi

is_protected_database() {
  local db_name="$1"
  local protected_db

  IFS=',' read -ra protected_items <<< "${PROTECTED_DATABASES}"
  for protected_db in "${protected_items[@]}"; do
    protected_db="$(echo "${protected_db}" | xargs)"
    if [ "${db_name}" = "${protected_db}" ]; then
      return 0
    fi
  done

  return 1
}

if is_protected_database "${TARGET_DB}" && [ "${FORCE_RESTORE}" != "true" ]; then
  echo "DIKKAT: '${TARGET_DB}' korumali/canli bir veritabani."
  echo "Bu islem mevcut veriyi SILIP uzerine yazacak."
  printf "Devam etmek icin tam olarak 'EVET' yazin: "
  read -r CONFIRMATION

  if [ "${CONFIRMATION}" != "EVET" ]; then
    echo "Restore iptal edildi."
    exit 1
  fi
fi

cleanup_container_file() {
  docker exec "${POSTGRES_CONTAINER}" rm -f "${CONTAINER_RESTORE_PATH}" >/dev/null 2>&1 || true
}

trap cleanup_container_file EXIT

echo "Copying backup into PostgreSQL container..."
docker cp "${BACKUP_FILE_PATH}" "${POSTGRES_CONTAINER}:${CONTAINER_RESTORE_PATH}"

echo "Restoring backup into database '${TARGET_DB}'..."
docker exec "${POSTGRES_CONTAINER}" pg_restore \
  -U "${POSTGRES_USER}" \
  -d "${TARGET_DB}" \
  --clean \
  --if-exists \
  --no-owner \
  "${CONTAINER_RESTORE_PATH}"

echo "Restore completed successfully. database=${TARGET_DB}"
