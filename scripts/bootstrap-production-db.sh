#!/usr/bin/env bash
set -Eeuo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
ENV_FILE="${ENV_FILE:-${ROOT_DIR}/.env.production}"
COMPOSE_FILE="${COMPOSE_FILE:-${ROOT_DIR}/docker-compose.prod.yml}"
POSTGRES_SERVICE="${POSTGRES_SERVICE:-postgres}"
SEED_FILE="${ROOT_DIR}/database/production-seed.sql"
POST_SEED_PATCHES=(
  "${ROOT_DIR}/database/patches/2026_07_19_seller_notification_preferences.sql"
  "${ROOT_DIR}/database/patches/2026_07_20_seller_subscription_payments.sql"
  "${ROOT_DIR}/database/patches/2026_07_20_seller_subscription_payment_periods.sql"
)

[[ -f "${ENV_FILE}" ]] || {
  echo "Environment file not found: ${ENV_FILE}" >&2
  exit 1
}
[[ -f "${COMPOSE_FILE}" ]] || {
  echo "Compose file not found: ${COMPOSE_FILE}" >&2
  exit 1
}
[[ -f "${SEED_FILE}" ]] || {
  echo "Production seed file not found: ${SEED_FILE}" >&2
  exit 1
}
for post_seed_patch in "${POST_SEED_PATCHES[@]}"; do
  [[ -f "${post_seed_patch}" ]] || {
    echo "Post-seed patch file not found: ${post_seed_patch}" >&2
    exit 1
  }
done

set -a
# shellcheck disable=SC1090
source "${ENV_FILE}"
set +a

compose=(docker compose --env-file "${ENV_FILE}" -f "${COMPOSE_FILE}")
psql=("${compose[@]}" exec -T "${POSTGRES_SERVICE}" psql
  -U "${POSTGRES_USER}"
  -d "${POSTGRES_DB}"
  -v ON_ERROR_STOP=1
  -P pager=off)

table_count="$("${psql[@]}" -Atc "
  SELECT COUNT(*)
  FROM pg_tables
  WHERE schemaname = 'public';
")"

if [[ "${table_count}" == "0" ]]; then
  echo "Bootstrapping empty PostgreSQL database from production seed."
  "${psql[@]}" < "${SEED_FILE}"
  for post_seed_patch in "${POST_SEED_PATCHES[@]}"; do
    echo "Applying clean-install prerequisite: $(basename "${post_seed_patch}")"
    "${psql[@]}" < "${post_seed_patch}"
  done
  echo "DATABASE_BOOTSTRAP_OK source=database/production-seed.sql"
  exit 0
fi

migration_history_exists="$("${psql[@]}" -Atc "
  SELECT to_regclass('public.\"__EFMigrationsHistory\"') IS NOT NULL;
")"
if [[ "${migration_history_exists}" != "t" ]]; then
  echo "Database is not empty but EF migration history is missing; refusing bootstrap." >&2
  exit 1
fi

echo "DATABASE_BOOTSTRAP_SKIPPED existing_tables=${table_count}"
