BEGIN;

-- Replace legacy/mojibake management policies without touching public read policies.
DO $$
DECLARE
    policy_record record;
BEGIN
    FOR policy_record IN
        SELECT tablename, policyname
        FROM pg_policies
        WHERE schemaname = 'public'
          AND tablename IN ('course_sections', 'course_lessons')
          AND cmd <> 'SELECT'
    LOOP
        EXECUTE format(
            'DROP POLICY %I ON public.%I',
            policy_record.policyname,
            policy_record.tablename);
    END LOOP;
END
$$;

CREATE OR REPLACE FUNCTION public.current_seller_owns_course(
    p_course_id uuid)
RETURNS boolean
LANGUAGE sql
STABLE
SECURITY DEFINER
SET search_path = public, pg_temp
AS $$
    SELECT EXISTS (
        SELECT 1
        FROM public.courses c
        JOIN public.products p ON p.id = c.product_id
        JOIN public.shops s ON s.id = p.shop_id
        WHERE c.id = p_course_id
          AND s.user_id = NULLIF(
              current_setting('app.current_user_id', true),
              '')::uuid
    );
$$;

CREATE OR REPLACE FUNCTION public.current_seller_owns_course_section(
    p_section_id uuid)
RETURNS boolean
LANGUAGE sql
STABLE
SECURITY DEFINER
SET search_path = public, pg_temp
AS $$
    SELECT EXISTS (
        SELECT 1
        FROM public.course_sections cs
        JOIN public.courses c ON c.id = cs.course_id
        JOIN public.products p ON p.id = c.product_id
        JOIN public.shops s ON s.id = p.shop_id
        WHERE cs.id = p_section_id
          AND s.user_id = NULLIF(
              current_setting('app.current_user_id', true),
              '')::uuid
    );
$$;

REVOKE ALL ON FUNCTION public.current_seller_owns_course(uuid) FROM PUBLIC;
REVOKE ALL ON FUNCTION public.current_seller_owns_course_section(uuid) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION public.current_seller_owns_course(uuid) TO craftora_app;
GRANT EXECUTE ON FUNCTION public.current_seller_owns_course_section(uuid) TO craftora_app;

CREATE POLICY seller_course_sections_manage
ON public.course_sections
FOR ALL
TO craftora_app
USING (public.current_seller_owns_course(course_id))
WITH CHECK (public.current_seller_owns_course(course_id));

CREATE POLICY seller_course_lessons_manage
ON public.course_lessons
FOR ALL
TO craftora_app
USING (public.current_seller_owns_course_section(course_section_id))
WITH CHECK (public.current_seller_owns_course_section(course_section_id));

COMMIT;
