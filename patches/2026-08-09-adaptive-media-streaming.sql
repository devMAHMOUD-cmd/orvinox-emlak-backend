BEGIN;

ALTER TABLE public.media
    ADD COLUMN IF NOT EXISTS optimized_video_url text;

ALTER TABLE public.media
    ADD COLUMN IF NOT EXISTS hls_url text;

CREATE OR REPLACE FUNCTION public.complete_media_stream_processing(
    p_media_id uuid,
    p_optimized_video_url text,
    p_hls_url text,
    p_thumbnail_url text,
    p_duration_seconds integer)
RETURNS boolean
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = public, pg_temp
AS $$
DECLARE
    affected_rows integer;
BEGIN
    IF p_media_id IS NULL
       OR NULLIF(btrim(p_optimized_video_url), '') IS NULL
       OR NULLIF(btrim(p_hls_url), '') IS NULL THEN
        RAISE EXCEPTION 'Media streaming output is incomplete.';
    END IF;

    UPDATE public.media
    SET optimized_video_url = p_optimized_video_url,
        hls_url = p_hls_url,
        thumbnail_url = COALESCE(NULLIF(btrim(p_thumbnail_url), ''), thumbnail_url),
        duration_seconds = COALESCE(p_duration_seconds, duration_seconds),
        status = 'ready'::public.media_status,
        is_active = true,
        updated_at = CURRENT_TIMESTAMP
    WHERE id = p_media_id;

    GET DIAGNOSTICS affected_rows = ROW_COUNT;
    RETURN affected_rows = 1;
END;
$$;

REVOKE ALL ON FUNCTION public.complete_media_stream_processing(
    uuid, text, text, text, integer) FROM PUBLIC;
REVOKE ALL ON FUNCTION public.complete_media_stream_processing(
    uuid, text, text, text, integer) FROM craftora_app;
GRANT EXECUTE ON FUNCTION public.complete_media_stream_processing(
    uuid, text, text, text, integer) TO craftora_app;

COMMIT;
