-- ============================================================================
-- CRAFTORA DATABASE PATCH: REVIEW UNIQUENESS
-- Date: 2026-07-15
-- Purpose: Enforces one review per user and product at the database level.
-- Run as: PostgreSQL admin/superuser after the base schema and security patches.
-- Notes: This patch is idempotent. It intentionally stops if duplicate legacy
--        reviews exist so no historical data is silently deleted.
-- ============================================================================

BEGIN;

DO $$
BEGIN
    IF EXISTS (
        SELECT 1
        FROM public.reviews
        GROUP BY product_id, user_id
        HAVING COUNT(*) > 1
    ) THEN
        RAISE EXCEPTION
            'Cannot create unique_user_review: duplicate reviews exist for at least one user/product pair.';
    END IF;
END
$$;

CREATE UNIQUE INDEX IF NOT EXISTS unique_user_review
    ON public.reviews USING btree (product_id, user_id);

COMMIT;
