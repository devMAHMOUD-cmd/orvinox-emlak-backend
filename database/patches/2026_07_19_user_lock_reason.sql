-- =========================================================================
-- Craftora user lock reason patch
-- Date: 2026-07-19
-- Purpose: Store admin lock reason for account-locked login UX.
-- Run as database admin before deploying runtime code.
-- =========================================================================

BEGIN;

ALTER TABLE users
    ADD COLUMN IF NOT EXISTS lock_reason TEXT;

COMMIT;
