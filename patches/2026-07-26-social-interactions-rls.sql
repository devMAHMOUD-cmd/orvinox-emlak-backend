BEGIN;

CREATE OR REPLACE FUNCTION public.sync_media_counters()
RETURNS trigger
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = public, pg_temp
AS $$
DECLARE
    target_media_id uuid;
BEGIN
    IF TG_OP = 'INSERT' THEN
        target_media_id := NEW.media_id;
    ELSE
        target_media_id := OLD.media_id;
    END IF;

    IF TG_TABLE_NAME = 'media_likes' THEN
        UPDATE public.media
        SET like_count = GREATEST(
                COALESCE(like_count, 0) +
                CASE WHEN TG_OP = 'INSERT' THEN 1 ELSE -1 END,
                0),
            updated_at = CURRENT_TIMESTAMP
        WHERE id = target_media_id;
    ELSIF TG_TABLE_NAME = 'media_saves' THEN
        UPDATE public.media
        SET save_count = GREATEST(
                COALESCE(save_count, 0) +
                CASE WHEN TG_OP = 'INSERT' THEN 1 ELSE -1 END,
                0),
            updated_at = CURRENT_TIMESTAMP
        WHERE id = target_media_id;
    ELSIF TG_TABLE_NAME = 'media_comments' THEN
        UPDATE public.media
        SET comment_count = GREATEST(
                COALESCE(comment_count, 0) +
                CASE WHEN TG_OP = 'INSERT' THEN 1 ELSE -1 END,
                0),
            updated_at = CURRENT_TIMESTAMP
        WHERE id = target_media_id;
    END IF;

    IF TG_OP = 'DELETE' THEN
        RETURN OLD;
    END IF;

    RETURN NEW;
END;
$$;

REVOKE ALL ON FUNCTION public.sync_media_counters() FROM PUBLIC;
GRANT EXECUTE ON FUNCTION public.sync_media_counters() TO craftora_app;

CREATE OR REPLACE FUNCTION public.increment_media_share_count(p_media_id uuid)
RETURNS integer
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = public, pg_temp
AS $$
DECLARE
    updated_count integer;
BEGIN
    UPDATE public.media AS media_row
    SET share_count = LEAST(COALESCE(media_row.share_count, 0) + 1, 2147483647),
        updated_at = CURRENT_TIMESTAMP
    FROM public.shops AS shop
    WHERE media_row.id = p_media_id
      AND media_row.shop_id = shop.id
      AND media_row.is_active = true
      AND media_row.status = 'ready'::public.media_status
      AND shop.is_active = true
    RETURNING media_row.share_count INTO updated_count;

    RETURN COALESCE(updated_count, -1);
END;
$$;

REVOKE ALL ON FUNCTION public.increment_media_share_count(uuid) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION public.increment_media_share_count(uuid) TO craftora_app;

COMMIT;
