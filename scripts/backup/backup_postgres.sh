#!/usr/bin/env bash
set -euo pipefail

# Craftora PostgreSQL backup script for production Linux servers.
# Creates a custom-format pg_dump inside the PostgreSQL container, copies it to
# the host backup directory, and removes local backups older than RETENTION_DAYS.

POSTGRES_CONTAINER="${POSTGRES_CONTAINER:-postgres_server}"
POSTGRES_USER="${POSTGRES_USER:-admin}"
POSTGRES_DB="${POSTGRES_DB:-CraftoraMobile}"
BACKUP_DIR="${BACKUP_DIR:-/opt/craftora/backups/postgres}"
LOG_DIR="${LOG_DIR:-/opt/craftora/backups/logs}"
RETENTION_DAYS="${RETENTION_DAYS:-7}"

TIMESTAMP="$(date +%Y%m%d_%H%M%S)"
BACKUP_FILE="craftora_${TIMESTAMP}.dump"
CONTAINER_BACKUP_PATH="/tmp/${BACKUP_FILE}"
HOST_BACKUP_PATH="${BACKUP_DIR}/${BACKUP_FILE}"
LOG_FILE="${LOG_DIR}/postgres_backup.log"

log() {
  echo "[$(date --iso-8601=seconds)] $*" | tee -a "${LOG_FILE}"
}

mkdir -p "${BACKUP_DIR}" "${LOG_DIR}"

cleanup_container_file() {
  docker exec "${POSTGRES_CONTAINER}" rm -f "${CONTAINER_BACKUP_PATH}" >/dev/null 2>&1 || true
}

trap cleanup_container_file EXIT

log "PostgreSQL backup started. database=${POSTGRES_DB}, container=${POSTGRES_CONTAINER}, file=${BACKUP_FILE}"

if ! docker exec "${POSTGRES_CONTAINER}" pg_dump -U "${POSTGRES_USER}" -d "${POSTGRES_DB}" -Fc -f "${CONTAINER_BACKUP_PATH}"; then
  log "ERROR: pg_dump failed."
  exit 1
fi

if ! docker cp "${POSTGRES_CONTAINER}:${CONTAINER_BACKUP_PATH}" "${HOST_BACKUP_PATH}"; then
  log "ERROR: docker cp failed."
  exit 1
fi

if [ ! -s "${HOST_BACKUP_PATH}" ]; then
  log "ERROR: backup file is empty or missing: ${HOST_BACKUP_PATH}"
  exit 1
fi

find "${BACKUP_DIR}" -type f -name "craftora_*.dump" -mtime +"${RETENTION_DAYS}" -delete

# TODO: Add offsite upload here, for example Backblaze B2, Hetzner Storage Box,
# AWS S3, or a remote MinIO target.

log "PostgreSQL backup completed successfully. path=${HOST_BACKUP_PATH}"
