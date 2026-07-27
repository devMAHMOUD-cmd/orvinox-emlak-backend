BEGIN;

CREATE OR REPLACE FUNCTION public.deactivate_user_device_token(
    p_device_token text
)
RETURNS boolean
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = public, pg_temp
AS $function$
BEGIN
    UPDATE public.user_device_tokens AS token
    SET is_active = false,
        last_used_at = CURRENT_TIMESTAMP
    WHERE token.token = p_device_token
      AND token.is_active = true;

    RETURN FOUND;
END;
$function$;

REVOKE ALL
    ON FUNCTION public.deactivate_user_device_token(text)
    FROM PUBLIC;

DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'craftora_app') THEN
        GRANT EXECUTE
            ON FUNCTION public.deactivate_user_device_token(text)
            TO craftora_app;
    END IF;
END
$$;

COMMIT;
