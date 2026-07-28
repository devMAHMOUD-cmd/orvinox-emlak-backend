#!/usr/bin/env bash
set -Eeuo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
ENV_FILE="${ENV_FILE:-${ROOT_DIR}/.env.production}"
COMPOSE_FILE="${COMPOSE_FILE:-${ROOT_DIR}/docker-compose.prod.yml}"

[[ -f "${ENV_FILE}" ]] || {
  echo "Environment file not found: ${ENV_FILE}" >&2
  exit 1
}

set -a
# shellcheck disable=SC1090
source "${ENV_FILE}"
set +a

BACKUP_DIR="${BACKUP_DIR:-/backups}"
POSTGRES_CONTAINER="${POSTGRES_CONTAINER:-postgres_server}"
HEALTH_URL="${HEALTH_URL:-https://api.craftoramedya.com/health}"
TIMESTAMP="$(date +%Y%m%d_%H%M%S)"
BACKUP_PATH="${BACKUP_DIR}/craftora_pre_deploy_${TIMESTAMP}.dump"

compose=(docker compose --env-file "${ENV_FILE}" -f "${COMPOSE_FILE}")

mkdir -p "${BACKUP_DIR}"
umask 077

echo "Creating PostgreSQL backup: ${BACKUP_PATH}"
docker exec "${POSTGRES_CONTAINER}" pg_dump \
  -U "${POSTGRES_USER:-postgres}" \
  -d "${POSTGRES_DB:-craftora_db}" \
  --format=custom > "${BACKUP_PATH}"
test -s "${BACKUP_PATH}"
docker exec -i "${POSTGRES_CONTAINER}" pg_restore --list \
  < "${BACKUP_PATH}" >/dev/null

echo "Building API image."
"${compose[@]}" build craftora_api

echo "Applying EF Core migrations without starting hosted workers."
"${compose[@]}" run --rm --no-deps craftora_api --migrate-only

echo "Applying tracked SQL patches."
ENV_FILE="${ENV_FILE}" COMPOSE_FILE="${COMPOSE_FILE}" \
  bash "${ROOT_DIR}/scripts/apply-db-patches.sh" apply

echo "Starting the production API."
"${compose[@]}" up -d --no-deps --force-recreate craftora_api

for attempt in $(seq 1 18); do
  if curl -fsS --max-time 15 "${HEALTH_URL}" >/tmp/craftora-health.json; then
    cat /tmp/craftora-health.json
    echo
    echo "DEPLOYMENT_OK backup=${BACKUP_PATH}"
    exit 0
  fi
  sleep 5
done

echo "Deployment health check failed. Backup: ${BACKUP_PATH}" >&2
exit 1
