BEGIN;

WITH ranked_tokens AS (
    SELECT
        id,
        ROW_NUMBER() OVER (
            PARTITION BY token
            ORDER BY
                (is_active = true) DESC,
                last_used_at DESC NULLS LAST,
                created_at DESC NULLS LAST,
                id DESC
        ) AS row_number
    FROM public.user_device_tokens
)
DELETE FROM public.user_device_tokens AS device_token
USING ranked_tokens
WHERE device_token.id = ranked_tokens.id
  AND ranked_tokens.row_number > 1;

DROP INDEX IF EXISTS public.user_device_tokens_token_key;

CREATE UNIQUE INDEX user_device_tokens_token_key
    ON public.user_device_tokens (token);

CREATE OR REPLACE FUNCTION public.upsert_user_device_token(
    p_user_id uuid,
    p_token text,
    p_device_type varchar
)
RETURNS uuid
LANGUAGE plpgsql
VOLATILE
SECURITY DEFINER
SET search_path = public, pg_temp
AS $function$
DECLARE
    result_id uuid;
BEGIN
    IF p_user_id IS NULL
       OR NOT EXISTS (
           SELECT 1
           FROM public.users AS app_user
           WHERE app_user.id = p_user_id
             AND app_user.is_active = true
             AND app_user.deleted_at IS NULL
       ) THEN
        RAISE EXCEPTION 'Invalid device token user.';
    END IF;

    IF p_token IS NULL
       OR LENGTH(BTRIM(p_token)) < 16
       OR LENGTH(BTRIM(p_token)) > 4096 THEN
        RAISE EXCEPTION 'Invalid device token.';
    END IF;

    IF LOWER(BTRIM(p_device_type)) NOT IN ('android', 'ios', 'web') THEN
        RAISE EXCEPTION 'Invalid device type.';
    END IF;

    INSERT INTO public.user_device_tokens (
        user_id,
        token,
        device_type,
        device_id,
        is_active,
        last_used_at,
        created_at
    )
    VALUES (
        p_user_id,
        BTRIM(p_token),
        LOWER(BTRIM(p_device_type)),
        NULL,
        true,
        CURRENT_TIMESTAMP,
        CURRENT_TIMESTAMP
    )
    ON CONFLICT (token)
    DO UPDATE SET
        user_id = EXCLUDED.user_id,
        device_type = EXCLUDED.device_type,
        device_id = NULL,
        is_active = true,
        last_used_at = CURRENT_TIMESTAMP
    RETURNING id INTO result_id;

    RETURN result_id;
END;
$function$;

CREATE OR REPLACE FUNCTION public.record_notification_delivery(
    p_notification_id uuid,
    p_status varchar,
    p_provider varchar,
    p_error_message text
)
RETURNS uuid
LANGUAGE plpgsql
VOLATILE
SECURITY DEFINER
SET search_path = public, pg_temp
AS $function$
DECLARE
    result_id uuid;
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM public.notifications AS notification
        WHERE notification.id = p_notification_id
    ) THEN
        RAISE EXCEPTION 'Notification not found.';
    END IF;

    IF p_status NOT IN ('sent', 'failed', 'partial', 'mocked', 'skipped') THEN
        RAISE EXCEPTION 'Invalid notification delivery status.';
    END IF;

    INSERT INTO public.notification_deliveries (
        notification_id,
        channel,
        status,
        provider,
        error_message,
        sent_at,
        created_at
    )
    VALUES (
        p_notification_id,
        'push',
        p_status,
        p_provider,
        LEFT(p_error_message, 2000),
        CASE WHEN p_status IN ('sent', 'partial', 'mocked') THEN CURRENT_TIMESTAMP END,
        CURRENT_TIMESTAMP
    )
    RETURNING id INTO result_id;

    RETURN result_id;
END;
$function$;

REVOKE ALL ON FUNCTION public.upsert_user_device_token(uuid, text, varchar) FROM PUBLIC;
REVOKE ALL ON FUNCTION public.record_notification_delivery(uuid, varchar, varchar, text) FROM PUBLIC;

DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'craftora_app') THEN
        GRANT EXECUTE
            ON FUNCTION public.upsert_user_device_token(uuid, text, varchar)
            TO craftora_app;
        GRANT EXECUTE
            ON FUNCTION public.record_notification_delivery(uuid, varchar, varchar, text)
            TO craftora_app;
    END IF;
END
$$;

COMMIT;
