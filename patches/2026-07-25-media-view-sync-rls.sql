BEGIN;

CREATE OR REPLACE FUNCTION public.increment_media_view_count(
    p_media_id uuid,
    p_increment bigint)
RETURNS boolean
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = public, pg_temp
AS $$
DECLARE
    affected_rows integer;
BEGIN
    IF p_increment <= 0 THEN
        RETURN false;
    END IF;

    UPDATE public.media
    SET view_count = LEAST(
            COALESCE(view_count, 0)::bigint + LEAST(p_increment, 2147483647),
            2147483647)::integer,
        updated_at = CURRENT_TIMESTAMP
    WHERE id = p_media_id
      AND is_active = true;

    GET DIAGNOSTICS affected_rows = ROW_COUNT;
    RETURN affected_rows > 0;
END;
$$;

REVOKE ALL ON FUNCTION public.increment_media_view_count(uuid, bigint) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION public.increment_media_view_count(uuid, bigint) TO craftora_app;

COMMIT;
