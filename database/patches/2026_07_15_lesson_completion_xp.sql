-- ============================================================================
-- CRAFTORA DATABASE PATCH: LESSON COMPLETION XP
-- Date: 2026-07-15
-- Purpose: Binds lesson-completion XP to user_lesson_progress and guarantees
--          that each user can earn completion XP for a lesson only once.
-- Run as: PostgreSQL admin/superuser after the base schema and security patches.
-- ============================================================================

BEGIN;

DO $$
BEGIN
    IF EXISTS (
        SELECT 1
        FROM public.point_logs
        WHERE action_type = 'complete_lesson'
        GROUP BY user_id, reference_id
        HAVING COUNT(*) > 1
    ) THEN
        RAISE EXCEPTION
            'Cannot create uq_point_logs_complete_lesson_once: duplicate lesson completion point logs exist.';
    END IF;
END
$$;

CREATE UNIQUE INDEX IF NOT EXISTS uq_point_logs_complete_lesson_once
    ON public.point_logs USING btree (user_id, reference_id)
    WHERE action_type = 'complete_lesson';

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
        VALUES (NEW.user_id, 'complete_lesson', 2.0, NEW.course_lesson_id)
        ON CONFLICT (user_id, reference_id) WHERE action_type = 'complete_lesson'
        DO NOTHING
        RETURNING id INTO v_point_log_id;

        IF v_point_log_id IS NOT NULL THEN
            INSERT INTO public.user_points (user_id, total_points)
            VALUES (NEW.user_id, 2.0)
            ON CONFLICT (user_id) DO UPDATE
            SET total_points = public.user_points.total_points + 2.0,
                updated_at = CURRENT_TIMESTAMP;
        END IF;
    END IF;

    RETURN NEW;
END;
$$ LANGUAGE plpgsql SECURITY DEFINER SET search_path = public;

DROP TRIGGER IF EXISTS trg_points_on_lesson_completion ON public.user_lesson_progress;
CREATE TRIGGER trg_points_on_lesson_completion
AFTER INSERT OR UPDATE OF is_completed ON public.user_lesson_progress
FOR EACH ROW EXECUTE FUNCTION public.reward_lesson_completion();

COMMIT;
