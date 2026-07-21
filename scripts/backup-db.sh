#!/usr/bin/env bash
set -Eeuo pipefail

# Craftora production PostgreSQL backup.
# Password is read from ENV_FILE when present; no password is stored in this script.

POSTGRES_CONTAINER="${POSTGRES_CONTAINER:-postgres_server}"
POSTGRES_USER="${POSTGRES_USER:-admin}"
POSTGRES_DB="${POSTGRES_DB:-CraftoraMobile}"
BACKUP_DIR="${BACKUP_DIR:-/backups}"
RETENTION_DAYS="${RETENTION_DAYS:-7}"
ENV_FILE="${ENV_FILE:-/opt/craftora/.env.production}"

if [[ -f "${ENV_FILE}" ]]; then
  set -a
  # shellcheck disable=SC1090
  source "${ENV_FILE}"
  set +a
fi

TIMESTAMP="$(date +%F)"
BACKUP_FILE="craftora_${TIMESTAMP}.sql.gz"
TEMP_FILE="${BACKUP_DIR}/.${BACKUP_FILE}.tmp"
BACKUP_PATH="${BACKUP_DIR}/${BACKUP_FILE}"
LOG_FILE="${BACKUP_DIR}/backup-db.log"

umask 077
mkdir -p "${BACKUP_DIR}"

log() {
  printf '[%s] %s\n' "$(date --iso-8601=seconds)" "$*" | tee -a "${LOG_FILE}"
}

cleanup() {
  rm -f "${TEMP_FILE}"
}

on_error() {
  log "ERROR: PostgreSQL backup failed at line ${1}."
  exit 1
}

trap cleanup EXIT
trap 'on_error ${LINENO}' ERR

docker_pg_args=()
if [[ -n "${POSTGRES_PASSWORD:-}" ]]; then
  docker_pg_args+=(--env "PGPASSWORD=${POSTGRES_PASSWORD}")
fi

log "PostgreSQL backup started. database=${POSTGRES_DB}, container=${POSTGRES_CONTAINER}"

docker exec "${docker_pg_args[@]}" "${POSTGRES_CONTAINER}" \
  pg_dump -U "${POSTGRES_USER}" -d "${POSTGRES_DB}" --format=plain \
  | gzip -9 > "${TEMP_FILE}"

if [[ ! -s "${TEMP_FILE}" ]]; then
  log "ERROR: backup file is empty: ${TEMP_FILE}"
  exit 1
fi

mv "${TEMP_FILE}" "${BACKUP_PATH}"
find "${BACKUP_DIR}" -type f -name 'craftora_*.sql.gz' \
  -mtime "+${RETENTION_DAYS}" -delete

log "PostgreSQL backup completed. path=${BACKUP_PATH}"
