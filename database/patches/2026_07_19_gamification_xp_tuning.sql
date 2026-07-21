-- 2026-07-19
-- Craftora gamification XP values, idempotency guards and historical repair.
-- Run manually with the admin/superuser database role before deploying the API.

BEGIN;

-- Retain each user's first watch reward for a reel and correct inflated wallet totals.
CREATE UNIQUE INDEX IF NOT EXISTS uq_user_points_user_id
    ON public.user_points(user_id);
WITH ranked AS (
    SELECT id,
           user_id,
           points_earned,
           ROW_NUMBER() OVER (
               PARTITION BY user_id, reference_id, action_type
               ORDER BY created_at, id) AS row_number
    FROM point_logs
    WHERE action_type = 'watch_reels'
), removed AS (
    DELETE FROM point_logs log
    USING ranked
    WHERE log.id = ranked.id
      AND ranked.row_number > 1
    RETURNING log.user_id, log.points_earned
), adjustments AS (
    SELECT user_id, SUM(points_earned) AS points_to_remove
    FROM removed
    GROUP BY user_id
)
UPDATE user_points points
SET total_points = GREATEST(points.total_points - adjustments.points_to_remove, 0),
    updated_at = CURRENT_TIMESTAMP
FROM adjustments
WHERE points.user_id = adjustments.user_id;

CREATE UNIQUE INDEX IF NOT EXISTS uq_point_logs_make_sale_once
    ON point_logs (user_id, reference_id)
    WHERE action_type = 'make_sale';

CREATE UNIQUE INDEX IF NOT EXISTS uq_point_logs_purchase_product_once
    ON point_logs (user_id, reference_id)
    WHERE action_type = 'purchase_product';

CREATE UNIQUE INDEX IF NOT EXISTS uq_point_logs_create_product_once
    ON point_logs (user_id, reference_id)
    WHERE action_type = 'create_product';

CREATE UNIQUE INDEX IF NOT EXISTS uq_point_logs_watch_reels_once
    ON point_logs (user_id, reference_id)
    WHERE action_type = 'watch_reels';

-- Backfill the previously missing buyer award exactly once per completed order.
WITH inserted AS (
    INSERT INTO point_logs (user_id, action_type, points_earned, reference_id, created_at)
    SELECT buyer_id, 'purchase_product', 5.0, id, created_at
    FROM orders
    WHERE status = 'completed'
    ON CONFLICT DO NOTHING
    RETURNING user_id, points_earned
), adjustments AS (
    SELECT user_id, SUM(points_earned) AS points_to_add
    FROM inserted
    GROUP BY user_id
)
INSERT INTO user_points (user_id, total_points, current_rank, current_streak, updated_at)
SELECT user_id, points_to_add, 0, 0, CURRENT_TIMESTAMP
FROM adjustments
ON CONFLICT (user_id) DO UPDATE
SET total_points = user_points.total_points + EXCLUDED.total_points,
    updated_at = EXCLUDED.updated_at;

CREATE OR REPLACE FUNCTION public.process_completed_order()
RETURNS TRIGGER AS $$
DECLARE
    v_seller_id UUID;
    v_point_log_id UUID;
BEGIN
    SELECT user_id INTO v_seller_id FROM shops WHERE id = NEW.shop_id;

    IF (NEW.status = 'completed' AND (TG_OP = 'INSERT' OR OLD.status != 'completed')) THEN
        UPDATE products SET sales_count = sales_count + 1 WHERE id = NEW.product_id;

        IF v_seller_id IS NOT NULL THEN
            INSERT INTO point_logs (user_id, action_type, points_earned, reference_id)
            VALUES (v_seller_id, 'make_sale', 20.0, NEW.id)
            ON CONFLICT DO NOTHING
            RETURNING id INTO v_point_log_id;

            IF v_point_log_id IS NOT NULL THEN
                INSERT INTO user_points (user_id, total_points)
                VALUES (v_seller_id, 20.0)
                ON CONFLICT (user_id) DO UPDATE
                SET total_points = user_points.total_points + 20.0,
                    updated_at = CURRENT_TIMESTAMP;
            END IF;
        END IF;
    ELSIF (NEW.status = 'refunded' AND TG_OP = 'UPDATE' AND OLD.status != 'refunded') THEN
        UPDATE products SET sales_count = GREATEST(sales_count - 1, 0) WHERE id = NEW.product_id;

        IF v_seller_id IS NOT NULL THEN
            INSERT INTO point_logs (user_id, action_type, points_earned, reference_id)
            VALUES (v_seller_id, 'refund_sale', -20.0, NEW.id);

            UPDATE user_points
            SET total_points = GREATEST(total_points - 20.0, 0),
                updated_at = CURRENT_TIMESTAMP
            WHERE user_id = v_seller_id;
        END IF;
    END IF;

    RETURN NEW;
END;
$$ LANGUAGE plpgsql SECURITY DEFINER SET search_path = public;

CREATE OR REPLACE FUNCTION public.award_seller_points()
RETURNS TRIGGER AS $$
DECLARE
    v_seller_id UUID;
BEGIN
    SELECT s.user_id INTO v_seller_id
    FROM media m
    JOIN shops s ON m.shop_id = s.id
    WHERE m.id = NEW.media_id;

    IF v_seller_id IS NOT NULL THEN
        INSERT INTO point_logs (user_id, action_type, points_earned, reference_id)
        VALUES (v_seller_id, 'receive_like', 2.0, NEW.id);

        INSERT INTO user_points (user_id, total_points)
        VALUES (v_seller_id, 2.0)
        ON CONFLICT (user_id) DO UPDATE
        SET total_points = user_points.total_points + 2.0,
            updated_at = CURRENT_TIMESTAMP;
    END IF;

    RETURN NEW;
END;
$$ LANGUAGE plpgsql SECURITY DEFINER SET search_path = public;

CREATE OR REPLACE FUNCTION public.award_viewer_points()
RETURNS TRIGGER AS $$
DECLARE
    v_daily_points DECIMAL;
    v_point_log_id UUID;
BEGIN
    SELECT COALESCE(SUM(points_earned), 0) INTO v_daily_points
    FROM point_logs
    WHERE user_id = NEW.user_id
      AND action_type = 'watch_reels'
      AND created_at::date = CURRENT_DATE;

    IF v_daily_points < 50 THEN
        INSERT INTO point_logs (user_id, action_type, points_earned, reference_id)
        VALUES (NEW.user_id, 'watch_reels', 5.0, NEW.media_id)
        ON CONFLICT DO NOTHING
        RETURNING id INTO v_point_log_id;

        IF v_point_log_id IS NOT NULL THEN
            INSERT INTO user_points (user_id, total_points)
            VALUES (NEW.user_id, 5.0)
            ON CONFLICT (user_id) DO UPDATE
            SET total_points = user_points.total_points + 5.0,
                updated_at = CURRENT_TIMESTAMP;
            NEW.is_point_earned := TRUE;
        END IF;
    END IF;

    RETURN NEW;
END;
$$ LANGUAGE plpgsql SECURITY DEFINER SET search_path = public;

CREATE OR REPLACE FUNCTION public.reward_lesson_completion()
RETURNS TRIGGER AS $$
DECLARE
    v_point_log_id UUID;
BEGIN
    IF NEW.is_completed = TRUE
        AND (TG_OP = 'INSERT' OR OLD.is_completed IS DISTINCT FROM TRUE) THEN
        IF NOT EXISTS (
            SELECT 1
            FROM public.user_library library_item
            JOIN public.course_lessons lesson ON lesson.id = NEW.course_lesson_id
            JOIN public.course_sections section ON section.id = lesson.course_section_id
            JOIN public.courses course ON course.id = section.course_id
            WHERE library_item.user_id = NEW.user_id
              AND library_item.product_id = course.product_id
        ) THEN
            RETURN NEW;
        END IF;

        INSERT INTO public.point_logs (user_id, action_type, points_earned, reference_id)
        VALUES (NEW.user_id, 'complete_lesson', 5.0, NEW.course_lesson_id)
        ON CONFLICT (user_id, reference_id) WHERE action_type = 'complete_lesson'
        DO NOTHING
        RETURNING id INTO v_point_log_id;

        IF v_point_log_id IS NOT NULL THEN
            INSERT INTO public.user_points (user_id, total_points)
            VALUES (NEW.user_id, 5.0)
            ON CONFLICT (user_id) DO UPDATE
            SET total_points = public.user_points.total_points + 5.0,
                updated_at = CURRENT_TIMESTAMP;
        END IF;
    END IF;

    RETURN NEW;
END;
$$ LANGUAGE plpgsql SECURITY DEFINER SET search_path = public;

COMMIT;
