BEGIN;

CREATE OR REPLACE FUNCTION public.complete_media_processing(
    p_media_id uuid,
    p_video_url text,
    p_thumbnail_url text)
RETURNS boolean
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = public, pg_temp
AS $$
DECLARE
    affected_rows integer;
BEGIN
    IF p_video_url IS NULL OR btrim(p_video_url) = '' THEN
        RETURN false;
    END IF;

    UPDATE public.media
    SET video_url = p_video_url,
        thumbnail_url = COALESCE(
            NULLIF(btrim(thumbnail_url), ''),
            NULLIF(btrim(p_thumbnail_url), '')),
        status = 'ready'::public.media_status,
        is_active = true,
        updated_at = CURRENT_TIMESTAMP
    WHERE id = p_media_id
      AND is_active = true;

    GET DIAGNOSTICS affected_rows = ROW_COUNT;
    RETURN affected_rows > 0;
END;
$$;

REVOKE ALL ON FUNCTION public.complete_media_processing(uuid, text, text) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION public.complete_media_processing(uuid, text, text) TO craftora_app;

COMMIT;
