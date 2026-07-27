BEGIN;

CREATE OR REPLACE FUNCTION public.get_active_user_device_tokens(
    p_user_id uuid
)
RETURNS TABLE(device_token text)
LANGUAGE sql
STABLE
SECURITY DEFINER
SET search_path = public, pg_temp
AS $function$
    SELECT token.token
    FROM public.user_device_tokens AS token
    WHERE token.user_id = p_user_id
      AND token.is_active = true
    ORDER BY token.last_used_at DESC NULLS LAST, token.created_at DESC;
$function$;

REVOKE ALL ON FUNCTION public.get_active_user_device_tokens(uuid) FROM PUBLIC;

DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'craftora_app') THEN
        GRANT EXECUTE
            ON FUNCTION public.get_active_user_device_tokens(uuid)
            TO craftora_app;
    END IF;
END
$$;

COMMIT;
