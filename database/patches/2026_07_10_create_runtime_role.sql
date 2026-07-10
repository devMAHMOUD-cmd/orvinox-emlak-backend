-- ============================================================================
-- Craftora Runtime Role Creation
-- Date: 2026-07-10
-- Purpose:
--   Creates the non-superuser PostgreSQL runtime role that will be used by the
--   API after the RLS migration is completed.
--
-- Important:
--   Run this script as the existing privileged admin role.
--   This script only creates/normalizes the role. The backend does NOT use this
--   role yet. Grants, RLS policy changes, and connection string changes will be
--   handled in later steps.
-- ============================================================================

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_roles
        WHERE rolname = 'craftora_app'
    ) THEN
        CREATE ROLE craftora_app
            WITH
            LOGIN
            PASSWORD 'Craftora_App_2026_xK9mP2vL'
            NOSUPERUSER
            NOBYPASSRLS
            NOCREATEDB
            NOCREATEROLE;
    END IF;
END
$$;

ALTER ROLE craftora_app
    WITH
    LOGIN
    NOSUPERUSER
    NOBYPASSRLS
    NOCREATEDB
    NOCREATEROLE;
