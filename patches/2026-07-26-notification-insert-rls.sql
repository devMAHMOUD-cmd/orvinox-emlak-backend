BEGIN;

CREATE OR REPLACE FUNCTION public.create_notification(
    p_user_id uuid,
    p_type varchar,
    p_title varchar,
    p_body text,
    p_reference_type varchar,
    p_reference_id uuid,
    p_data jsonb)
RETURNS uuid
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = public, pg_temp
AS $$
DECLARE
    notification_id uuid;
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM public.users
        WHERE id = p_user_id
          AND is_active = true
          AND deleted_at IS NULL
    ) THEN
        RAISE EXCEPTION 'Notification target user not found or inactive'
            USING ERRCODE = 'P0002';
    END IF;

    INSERT INTO public.notifications (
        user_id,
        type,
        title,
        body,
        reference_type,
        reference_id,
        data,
        is_read,
        created_at)
    VALUES (
        p_user_id,
        p_type,
        p_title,
        p_body,
        p_reference_type,
        p_reference_id,
        p_data,
        false,
        CURRENT_TIMESTAMP)
    RETURNING id INTO notification_id;

    RETURN notification_id;
END;
$$;

REVOKE ALL ON FUNCTION public.create_notification(
    uuid, varchar, varchar, text, varchar, uuid, jsonb) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION public.create_notification(
    uuid, varchar, varchar, text, varchar, uuid, jsonb) TO craftora_app;

COMMIT;
