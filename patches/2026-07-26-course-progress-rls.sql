BEGIN;

ALTER TABLE public.user_lesson_progress ENABLE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS user_library_update_own ON public.user_library;
CREATE POLICY user_library_update_own
ON public.user_library
FOR UPDATE
TO craftora_app
USING (
    user_id = NULLIF(current_setting('app.current_user_id', true), '')::uuid
)
WITH CHECK (
    user_id = NULLIF(current_setting('app.current_user_id', true), '')::uuid
);

DROP POLICY IF EXISTS user_lesson_progress_select_own
ON public.user_lesson_progress;
CREATE POLICY user_lesson_progress_select_own
ON public.user_lesson_progress
FOR SELECT
TO craftora_app
USING (
    user_id = NULLIF(current_setting('app.current_user_id', true), '')::uuid
);

DROP POLICY IF EXISTS user_lesson_progress_insert_own
ON public.user_lesson_progress;
CREATE POLICY user_lesson_progress_insert_own
ON public.user_lesson_progress
FOR INSERT
TO craftora_app
WITH CHECK (
    user_id = NULLIF(current_setting('app.current_user_id', true), '')::uuid
);

DROP POLICY IF EXISTS user_lesson_progress_update_own
ON public.user_lesson_progress;
CREATE POLICY user_lesson_progress_update_own
ON public.user_lesson_progress
FOR UPDATE
TO craftora_app
USING (
    user_id = NULLIF(current_setting('app.current_user_id', true), '')::uuid
)
WITH CHECK (
    user_id = NULLIF(current_setting('app.current_user_id', true), '')::uuid
);

COMMIT;
