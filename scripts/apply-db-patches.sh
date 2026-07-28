#!/usr/bin/env bash
set -Eeuo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
ENV_FILE="${ENV_FILE:-${ROOT_DIR}/.env.production}"
COMPOSE_FILE="${COMPOSE_FILE:-${ROOT_DIR}/docker-compose.prod.yml}"
POSTGRES_SERVICE="${POSTGRES_SERVICE:-postgres}"
PATCH_POSTGRES_USER="${PATCH_POSTGRES_USER:-postgres}"
MODE="${1:-apply}"
LOCK_DIR="${ROOT_DIR}/.db-patch-lock"

if [[ "${MODE}" != "apply" && "${MODE}" != "status" && "${MODE}" != "baseline" ]]; then
  echo "Usage: $0 [apply|status|baseline]" >&2
  exit 2
fi

for command_name in docker sha256sum sort; do
  command -v "${command_name}" >/dev/null 2>&1 || {
    echo "Required command is missing: ${command_name}" >&2
    exit 1
  }
done

[[ -f "${ENV_FILE}" ]] || {
  echo "Environment file not found: ${ENV_FILE}" >&2
  exit 1
}

set -a
# shellcheck disable=SC1090
source "${ENV_FILE}"
set +a

[[ -f "${COMPOSE_FILE}" ]] || {
  echo "Compose file not found: ${COMPOSE_FILE}" >&2
  exit 1
}

if ! mkdir "${LOCK_DIR}" 2>/dev/null; then
  echo "Another database patch process is already running: ${LOCK_DIR}" >&2
  exit 1
fi
trap 'rmdir "${LOCK_DIR}" 2>/dev/null || true' EXIT

compose=(docker compose --env-file "${ENV_FILE}" -f "${COMPOSE_FILE}")
psql=("${compose[@]}" exec -T "${POSTGRES_SERVICE}" psql
  -U "${PATCH_POSTGRES_USER}"
  -d "${POSTGRES_DB:-craftora_db}"
  -v ON_ERROR_STOP=1
  -P pager=off)

"${psql[@]}" <<'SQL'
CREATE TABLE IF NOT EXISTS public.schema_patch_history (
    patch_name text PRIMARY KEY,
    checksum_sha256 char(64) NOT NULL,
    applied_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    applied_by text NOT NULL DEFAULT CURRENT_USER
);
REVOKE ALL ON TABLE public.schema_patch_history FROM PUBLIC;
SQL

mapfile -t patch_files < <(
  find "${ROOT_DIR}/patches" -maxdepth 1 -type f -name '*.sql' -print | sort
)

if [[ "${#patch_files[@]}" -eq 0 ]]; then
  echo "No SQL patches found."
  exit 0
fi

if [[ "${MODE}" == "baseline" ]]; then
  if [[ "${CONFIRM_PATCH_BASELINE:-}" != "I_HAVE_VERIFIED_THIS_DATABASE" ]]; then
    echo "Baseline refused. Set CONFIRM_PATCH_BASELINE=I_HAVE_VERIFIED_THIS_DATABASE." >&2
    exit 1
  fi

  schema_ready="$("${psql[@]}" -Atc "
    SELECT
      to_regclass('public.discovery_events') IS NOT NULL
      AND to_regclass('public.resend_inbound_events') IS NOT NULL
      AND to_regprocedure(
        'public.get_sponsored_discovery_candidates(uuid,integer)'
      ) IS NOT NULL;
  ")"
  if [[ "${schema_ready}" != "t" ]]; then
    echo "Baseline refused: expected latest production schema markers are missing." >&2
    exit 1
  fi
fi

pending_count=0
for patch_file in "${patch_files[@]}"; do
  patch_name="$(basename "${patch_file}")"
  checksum="$(sha256sum "${patch_file}" | awk '{print $1}')"
  recorded_checksum="$("${psql[@]}" -At \
    --set=patch_name="${patch_name}" <<'SQL'
SELECT checksum_sha256
FROM public.schema_patch_history
WHERE patch_name = :'patch_name';
SQL
)"

  if [[ -n "${recorded_checksum}" ]]; then
    if [[ "${recorded_checksum}" != "${checksum}" ]]; then
      echo "Checksum mismatch for applied patch: ${patch_name}" >&2
      exit 1
    fi
    printf 'APPLIED %s\n' "${patch_name}"
    continue
  fi

  pending_count=$((pending_count + 1))
  if [[ "${MODE}" == "status" ]]; then
    printf 'PENDING %s\n' "${patch_name}"
    continue
  fi

  if [[ "${MODE}" == "apply" ]]; then
    printf 'APPLYING %s\n' "${patch_name}"
    "${psql[@]}" < "${patch_file}"
  else
    printf 'BASELINING %s\n' "${patch_name}"
  fi

  "${psql[@]}" \
    --set=patch_name="${patch_name}" \
    --set=checksum="${checksum}" <<'SQL'
INSERT INTO public.schema_patch_history (patch_name, checksum_sha256)
VALUES (:'patch_name', :'checksum')
ON CONFLICT (patch_name) DO NOTHING;
SQL
done

if [[ "${MODE}" == "status" ]]; then
  echo "PENDING_COUNT=${pending_count}"
else
  echo "PATCH_OPERATION_OK mode=${MODE} processed=${pending_count}"
fi
