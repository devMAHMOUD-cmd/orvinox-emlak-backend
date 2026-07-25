BEGIN;

CREATE OR REPLACE FUNCTION public.rotate_user_session(
    p_current_hash text,
    p_next_hash text,
    p_next_expires_at timestamp with time zone,
    p_device_id text,
    p_ip_address inet,
    p_user_agent text)
RETURNS uuid
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = public, pg_temp
AS $$
DECLARE
    rotated_user_id uuid;
BEGIN
    UPDATE public.user_sessions
    SET refresh_token = p_next_hash,
        expires_at = p_next_expires_at,
        device_id = COALESCE(p_device_id, device_id),
        ip_address = COALESCE(p_ip_address, ip_address),
        user_agent = COALESCE(p_user_agent, user_agent)
    WHERE refresh_token = p_current_hash
      AND is_revoked IS NOT TRUE
      AND expires_at > CURRENT_TIMESTAMP
    RETURNING user_id INTO rotated_user_id;

    RETURN rotated_user_id;
END;
$$;

REVOKE ALL ON FUNCTION public.rotate_user_session(
    text,
    text,
    timestamp with time zone,
    text,
    inet,
    text) FROM PUBLIC;

GRANT EXECUTE ON FUNCTION public.rotate_user_session(
    text,
    text,
    timestamp with time zone,
    text,
    inet,
    text) TO craftora_app;

CREATE OR REPLACE FUNCTION public.revoke_user_session(p_refresh_token_hash text)
RETURNS boolean
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = public, pg_temp
AS $$
DECLARE
    affected_rows integer;
BEGIN
    UPDATE public.user_sessions
    SET is_revoked = true
    WHERE refresh_token = p_refresh_token_hash
      AND is_revoked IS NOT TRUE;

    GET DIAGNOSTICS affected_rows = ROW_COUNT;
    RETURN affected_rows > 0;
END;
$$;

REVOKE ALL ON FUNCTION public.revoke_user_session(text) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION public.revoke_user_session(text) TO craftora_app;

COMMIT;
