BEGIN;

CREATE OR REPLACE FUNCTION public.get_seller_course_lesson_progress(
    p_shop_id uuid
)
RETURNS TABLE(
    course_id uuid,
    lesson_id uuid,
    student_user_id uuid,
    is_completed boolean
)
LANGUAGE sql
STABLE
SECURITY DEFINER
SET search_path = public, pg_temp
AS $function$
    SELECT
        section.course_id,
        lesson.id,
        progress.user_id,
        progress.is_completed
    FROM public.user_lesson_progress AS progress
    INNER JOIN public.course_lessons AS lesson
        ON lesson.id = progress.course_lesson_id
    INNER JOIN public.course_sections AS section
        ON section.id = lesson.course_section_id
    INNER JOIN public.courses AS course
        ON course.id = section.course_id
    INNER JOIN public.products AS product
        ON product.id = course.product_id
    WHERE product.shop_id = p_shop_id
      AND section.is_active = true
      AND lesson.is_active = true
      AND EXISTS (
          SELECT 1
          FROM public.shops AS owner_shop
          WHERE owner_shop.id = p_shop_id
            AND owner_shop.user_id = NULLIF(
                current_setting('app.current_user_id', true),
                '')::uuid
      );
$function$;

REVOKE ALL
    ON FUNCTION public.get_seller_course_lesson_progress(uuid)
    FROM PUBLIC;

DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'craftora_app') THEN
        GRANT EXECUTE
            ON FUNCTION public.get_seller_course_lesson_progress(uuid)
            TO craftora_app;
    END IF;
END
$$;

COMMIT;
