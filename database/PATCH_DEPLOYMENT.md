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

For a new database, start PostgreSQL first and run the normal deployment
without baseline mode. EF migrations create the base schema and the SQL patch
runner applies every dated patch in order.
