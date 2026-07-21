-- ============================================================================
-- CRAFTORA DATABASE PATCH: PRODUCT QUESTION ANSWER NOTIFICATIONS
-- Date: 2026-07-19
-- Purpose:
--   - Adds a jsonb payload column to notifications for navigation metadata.
--   - Allows product_question_answer notifications in the type constraint.
-- Run as: PostgreSQL admin/superuser.
-- ============================================================================

BEGIN;

ALTER TABLE public.notifications
    ADD COLUMN IF NOT EXISTS data jsonb;

ALTER TABLE public.notifications DROP CONSTRAINT IF EXISTS check_notification_type;
ALTER TABLE public.notifications ADD CONSTRAINT check_notification_type CHECK (type IN (
    'sale_completed',
    'new_follower',
    'new_review',
    'new_question',
    'media_liked',
    'media_commented',
    'contest_result',
    'order_completed',
    'new_video',
    'new_product',
    'product_question_answer',
    'system'
));

COMMIT;
