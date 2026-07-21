-- ============================================================================
-- CRAFTORA DATABASE PATCH: REVIEW MAXIMUM AND LEGACY IMAGE CLEANUP
-- Date: 2026-07-19
-- Purpose:
--   - Removes the obsolete one-review-per-user/product unique index.
--   - Removes legacy storage.craftora.com image references with no valid
--     public-assets object in the current storage deployment.
-- Run as: PostgreSQL admin/superuser.
-- ============================================================================

BEGIN;

DROP INDEX IF EXISTS public.unique_user_review;

UPDATE public.reviews
SET images = COALESCE(
    (
        SELECT jsonb_agg(to_jsonb(image_url))
        FROM jsonb_array_elements_text(COALESCE(reviews.images, '[]'::jsonb)) AS image_url
        WHERE lower(image_url) NOT LIKE '%storage.craftora.com%'
    ),
    '[]'::jsonb
)
WHERE COALESCE(reviews.images, '[]'::jsonb)::text ILIKE '%storage.craftora.com%';

COMMIT;
