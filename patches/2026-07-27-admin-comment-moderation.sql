BEGIN;

CREATE OR REPLACE FUNCTION public.delete_reported_media_comment(
    p_comment_id uuid
)
RETURNS boolean
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = public, pg_temp
AS $function$
DECLARE
    current_admin_user_id uuid;
BEGIN
    current_admin_user_id := NULLIF(
        NULLIF(current_setting('app.current_user_id', true), ''),
        '')::uuid;

    IF current_admin_user_id IS NULL
       OR NOT EXISTS (
           SELECT 1
           FROM public.users AS admin_user
           WHERE admin_user.id = current_admin_user_id
             AND admin_user.role::text = 'admin'
             AND admin_user.is_active IS TRUE
             AND admin_user.deleted_at IS NULL
       ) THEN
        RAISE EXCEPTION 'Admin authorization required.'
            USING ERRCODE = '42501';
    END IF;

    DELETE FROM public.media_comments AS comment
    WHERE comment.id = p_comment_id;

    RETURN FOUND;
END;
$function$;

REVOKE ALL
    ON FUNCTION public.delete_reported_media_comment(uuid)
    FROM PUBLIC;

DO $do$
BEGIN
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'craftora_app') THEN
        GRANT EXECUTE
            ON FUNCTION public.delete_reported_media_comment(uuid)
            TO craftora_app;
    END IF;
END
$do$;

COMMIT;
