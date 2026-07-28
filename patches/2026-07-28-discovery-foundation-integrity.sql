BEGIN;

LOCK TABLE public.media_watch_history IN SHARE ROW EXCLUSIVE MODE;
LOCK TABLE public.user_lesson_progress IN SHARE ROW EXCLUSIVE MODE;
LOCK TABLE public.media_likes IN SHARE ROW EXCLUSIVE MODE;
LOCK TABLE public.media_saves IN SHARE ROW EXCLUSIVE MODE;

CREATE TEMP TABLE merged_media_watch_history
ON COMMIT DROP
AS
SELECT
    (array_agg(history.id ORDER BY history.watched_at NULLS LAST, history.id))[1] AS keeper_id,
    history.user_id,
    history.media_id,
    MIN(history.watched_at) AS watched_at,
    BOOL_OR(COALESCE(history.is_point_earned, false)) AS is_point_earned
FROM public.media_watch_history AS history
GROUP BY history.user_id, history.media_id
HAVING COUNT(*) > 1;

UPDATE public.media_watch_history AS history
SET
    watched_at = merged.watched_at,
    is_point_earned = merged.is_point_earned
FROM merged_media_watch_history AS merged
WHERE history.id = merged.keeper_id;

DELETE FROM public.media_watch_history AS history
USING merged_media_watch_history AS merged
WHERE history.user_id = merged.user_id
  AND history.media_id = merged.media_id
  AND history.id <> merged.keeper_id;

CREATE TEMP TABLE merged_user_lesson_progress
ON COMMIT DROP
AS
SELECT
    (array_agg(progress.id ORDER BY progress.created_at, progress.id))[1] AS keeper_id,
    progress.user_id,
    progress.course_lesson_id,
    BOOL_OR(progress.is_completed) AS is_completed,
    MAX(progress.watched_seconds) AS watched_seconds,
    MIN(progress.created_at) AS created_at,
    MAX(COALESCE(progress.updated_at, progress.created_at)) AS updated_at
FROM public.user_lesson_progress AS progress
GROUP BY progress.user_id, progress.course_lesson_id
HAVING COUNT(*) > 1;

UPDATE public.user_lesson_progress AS progress
SET
    is_completed = merged.is_completed,
    watched_seconds = merged.watched_seconds,
    created_at = merged.created_at,
    updated_at = merged.updated_at
FROM merged_user_lesson_progress AS merged
WHERE progress.id = merged.keeper_id;

DELETE FROM public.user_lesson_progress AS progress
USING merged_user_lesson_progress AS merged
WHERE progress.user_id = merged.user_id
  AND progress.course_lesson_id = merged.course_lesson_id
  AND progress.id <> merged.keeper_id;

DO $$
DECLARE
    media_like_duplicates bigint;
    media_save_duplicates bigint;
BEGIN
    SELECT COUNT(*)
    INTO media_like_duplicates
    FROM (
        SELECT media_like.user_id, media_like.media_id
        FROM public.media_likes AS media_like
        GROUP BY media_like.user_id, media_like.media_id
        HAVING COUNT(*) > 1
    ) AS duplicate_groups;

    SELECT COUNT(*)
    INTO media_save_duplicates
    FROM (
        SELECT media_save.user_id, media_save.media_id
        FROM public.media_saves AS media_save
        GROUP BY media_save.user_id, media_save.media_id
        HAVING COUNT(*) > 1
    ) AS duplicate_groups;

    IF media_like_duplicates <> 0 OR media_save_duplicates <> 0 THEN
        RAISE EXCEPTION
            'Manual social deduplication required: media_likes=%, media_saves=%',
            media_like_duplicates,
            media_save_duplicates;
    END IF;
END
$$;

CREATE UNIQUE INDEX IF NOT EXISTS media_watch_history_user_id_media_id_key
    ON public.media_watch_history (user_id, media_id);

CREATE UNIQUE INDEX IF NOT EXISTS user_lesson_progress_user_id_course_lesson_id_key
    ON public.user_lesson_progress (user_id, course_lesson_id);

CREATE UNIQUE INDEX IF NOT EXISTS media_likes_media_id_user_id_key
    ON public.media_likes (media_id, user_id);

CREATE UNIQUE INDEX IF NOT EXISTS media_saves_media_id_user_id_key
    ON public.media_saves (media_id, user_id);

DO $$
DECLARE
    media_watch_duplicates bigint;
    lesson_progress_duplicates bigint;
BEGIN
    SELECT COUNT(*)
    INTO media_watch_duplicates
    FROM (
        SELECT history.user_id, history.media_id
        FROM public.media_watch_history AS history
        GROUP BY history.user_id, history.media_id
        HAVING COUNT(*) > 1
    ) AS duplicate_groups;

    SELECT COUNT(*)
    INTO lesson_progress_duplicates
    FROM (
        SELECT progress.user_id, progress.course_lesson_id
        FROM public.user_lesson_progress AS progress
        GROUP BY progress.user_id, progress.course_lesson_id
        HAVING COUNT(*) > 1
    ) AS duplicate_groups;

    IF media_watch_duplicates <> 0 OR lesson_progress_duplicates <> 0 THEN
        RAISE EXCEPTION
            'Discovery integrity verification failed: media_watch=%, lesson_progress=%',
            media_watch_duplicates,
            lesson_progress_duplicates;
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM pg_catalog.pg_class AS index_class
        JOIN pg_catalog.pg_index AS index_definition
          ON index_definition.indexrelid = index_class.oid
        JOIN pg_catalog.pg_namespace AS index_namespace
          ON index_namespace.oid = index_class.relnamespace
        WHERE index_class.relname = 'media_watch_history_user_id_media_id_key'
          AND index_namespace.nspname = 'public'
          AND index_definition.indisunique
    ) THEN
        RAISE EXCEPTION 'media_watch_history unique index is missing';
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM pg_catalog.pg_class AS index_class
        JOIN pg_catalog.pg_index AS index_definition
          ON index_definition.indexrelid = index_class.oid
        JOIN pg_catalog.pg_namespace AS index_namespace
          ON index_namespace.oid = index_class.relnamespace
        WHERE index_class.relname = 'user_lesson_progress_user_id_course_lesson_id_key'
          AND index_namespace.nspname = 'public'
          AND index_definition.indisunique
    ) THEN
        RAISE EXCEPTION 'user_lesson_progress unique index is missing';
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM pg_catalog.pg_class AS index_class
        JOIN pg_catalog.pg_index AS index_definition
          ON index_definition.indexrelid = index_class.oid
        JOIN pg_catalog.pg_namespace AS index_namespace
          ON index_namespace.oid = index_class.relnamespace
        WHERE index_class.relname = 'media_likes_media_id_user_id_key'
          AND index_namespace.nspname = 'public'
          AND index_definition.indisunique
    ) THEN
        RAISE EXCEPTION 'media_likes unique index is missing';
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM pg_catalog.pg_class AS index_class
        JOIN pg_catalog.pg_index AS index_definition
          ON index_definition.indexrelid = index_class.oid
        JOIN pg_catalog.pg_namespace AS index_namespace
          ON index_namespace.oid = index_class.relnamespace
        WHERE index_class.relname = 'media_saves_media_id_user_id_key'
          AND index_namespace.nspname = 'public'
          AND index_definition.indisunique
    ) THEN
        RAISE EXCEPTION 'media_saves unique index is missing';
    END IF;
END
$$;

COMMIT;
