BEGIN;

ALTER TABLE public.seller_notification_preferences
    ADD COLUMN IF NOT EXISTS order_notifications boolean NOT NULL DEFAULT true,
    ADD COLUMN IF NOT EXISTS like_notifications boolean NOT NULL DEFAULT true,
    ADD COLUMN IF NOT EXISTS comment_notifications boolean NOT NULL DEFAULT true,
    ADD COLUMN IF NOT EXISTS follow_notifications boolean NOT NULL DEFAULT true,
    ADD COLUMN IF NOT EXISTS new_content_notifications boolean NOT NULL DEFAULT true,
    ADD COLUMN IF NOT EXISTS question_answer_notifications boolean NOT NULL DEFAULT true;

COMMIT;
