BEGIN;

CREATE OR REPLACE FUNCTION public.append_resend_inbound_support(
    p_email_id uuid,
    p_user_id uuid,
    p_ticket_id uuid,
    p_message text
)
RETURNS uuid
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = public, pg_temp
AS $$
DECLARE
    v_event public.resend_inbound_events%ROWTYPE;
    v_ticket_id uuid;
    v_now timestamptz := CURRENT_TIMESTAMP;
BEGIN
    SELECT *
    INTO v_event
    FROM public.resend_inbound_events
    WHERE email_id = p_email_id
    FOR UPDATE;

    IF NOT FOUND THEN
        RAISE EXCEPTION 'Resend inbound event not found';
    END IF;

    IF v_event.status = 'processed' AND v_event.ticket_id IS NOT NULL THEN
        RETURN v_event.ticket_id;
    END IF;

    IF v_event.status <> 'processing' THEN
        RAISE EXCEPTION 'Resend inbound event is not processable';
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM public.users
        WHERE id = p_user_id
          AND is_active IS TRUE
          AND is_email_verified IS TRUE
          AND deleted_at IS NULL
    ) THEN
        RAISE EXCEPTION 'Inbound support user is not active and verified';
    END IF;

    UPDATE public.support_tickets
    SET status = 'open'::support_ticket_status,
        closed_at = NULL,
        closed_by_user_id = NULL,
        updated_at = v_now,
        last_message_at = v_now
    WHERE id = p_ticket_id
      AND user_id = p_user_id
    RETURNING id INTO v_ticket_id;

    IF v_ticket_id IS NULL THEN
        RETURN NULL;
    END IF;

    INSERT INTO public.support_ticket_messages (
        id,
        ticket_id,
        sender_id,
        sender_role,
        message,
        created_at
    )
    VALUES (
        uuid_generate_v4(),
        v_ticket_id,
        p_user_id,
        'user'::support_message_sender_role,
        LEFT(p_message, 5000),
        v_now
    );

    UPDATE public.resend_inbound_events
    SET status = 'processed',
        ticket_id = v_ticket_id,
        error_message = NULL,
        updated_at = v_now,
        processed_at = v_now
    WHERE email_id = p_email_id;

    RETURN v_ticket_id;
END;
$$;

REVOKE ALL ON FUNCTION public.append_resend_inbound_support(uuid, uuid, uuid, text)
    FROM PUBLIC;

GRANT EXECUTE ON FUNCTION public.append_resend_inbound_support(uuid, uuid, uuid, text)
    TO craftora_app;

COMMIT;
