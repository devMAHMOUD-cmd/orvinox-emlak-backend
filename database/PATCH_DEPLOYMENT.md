# Database patch deployment

Production SQL patches are tracked in `public.schema_patch_history` with a
SHA-256 checksum. Do not edit an applied patch; add a new dated patch instead.

## Existing production database

The current production schema already contains the historical patches. Run the
following once after deploying this runner:

```bash
cd /var/www/craftora
CONFIRM_PATCH_BASELINE=I_HAVE_VERIFIED_THIS_DATABASE \
  bash ./scripts/apply-db-patches.sh baseline
bash ./scripts/apply-db-patches.sh status
```

Baseline mode refuses to run unless the latest discovery and inbound-email
schema markers exist.

## Normal deployment

After the one-time baseline, use:

```bash
cd /var/www/craftora
bash ./scripts/deploy-production.sh
```

The deployment creates and validates a custom-format PostgreSQL backup, builds
the API, applies EF migrations in migration-only mode, applies pending SQL
patches with checksum verification, recreates the API, and checks production
health.

Database administration defaults to the container-local `postgres` role.
Override it with `PATCH_POSTGRES_USER` or `BACKUP_POSTGRES_USER` when needed.

For a new database, start PostgreSQL first and run the normal deployment
without baseline mode. The deploy script loads `database/production-seed.sql`
only when the `public` schema has no tables. It then applies pending EF
migrations and every dated SQL patch in order. A non-empty database without EF
migration history is rejected instead of being modified ambiguously.
