BEGIN;

CREATE TABLE IF NOT EXISTS public.resend_inbound_events (
    svix_id varchar(255) PRIMARY KEY,
    email_id uuid NOT NULL UNIQUE,
    sender_email varchar(255) NOT NULL,
    recipient_email varchar(255) NOT NULL,
    subject varchar(200),
    status varchar(20) NOT NULL DEFAULT 'processing',
    ticket_id uuid REFERENCES public.support_tickets(id) ON DELETE SET NULL,
    error_message varchar(1000),
    created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    processed_at timestamptz,
    CONSTRAINT resend_inbound_events_status_check
        CHECK (status IN ('processing', 'processed', 'unmatched', 'failed'))
);

CREATE INDEX IF NOT EXISTS idx_resend_inbound_events_status
    ON public.resend_inbound_events (status, updated_at);

ALTER TABLE public.resend_inbound_events ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.resend_inbound_events FORCE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS resend_inbound_events_backend_only
    ON public.resend_inbound_events;
CREATE POLICY resend_inbound_events_backend_only
    ON public.resend_inbound_events
    FOR ALL
    TO craftora_app
    USING (false)
    WITH CHECK (false);

CREATE OR REPLACE FUNCTION public.claim_resend_inbound_event(
    p_svix_id varchar,
    p_email_id uuid,
    p_sender_email varchar,
    p_recipient_email varchar,
    p_subject varchar
)
RETURNS boolean
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = public, pg_temp
AS $$
DECLARE
    v_rows integer;
BEGIN
    INSERT INTO public.resend_inbound_events (
        svix_id,
        email_id,
        sender_email,
        recipient_email,
        subject,
        status
    )
    VALUES (
        LEFT(p_svix_id, 255),
        p_email_id,
        LEFT(lower(p_sender_email), 255),
        LEFT(lower(p_recipient_email), 255),
        LEFT(p_subject, 200),
        'processing'
    )
    ON CONFLICT DO NOTHING;

    GET DIAGNOSTICS v_rows = ROW_COUNT;
    IF v_rows = 1 THEN
        RETURN true;
    END IF;

    UPDATE public.resend_inbound_events AS event
    SET status = 'processing',
        error_message = NULL,
        updated_at = CURRENT_TIMESTAMP
    WHERE event.email_id = p_email_id
      AND (
          event.status = 'failed'
          OR (
              event.status = 'processing'
              AND event.updated_at < CURRENT_TIMESTAMP - INTERVAL '10 minutes'
          )
      );

    GET DIAGNOSTICS v_rows = ROW_COUNT;
    RETURN v_rows = 1;
END;
$$;

CREATE OR REPLACE FUNCTION public.set_resend_inbound_event_status(
    p_email_id uuid,
    p_status varchar,
    p_error_message text,
    p_ticket_id uuid
)
RETURNS void
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = public, pg_temp
AS $$
BEGIN
    IF p_status NOT IN ('processing', 'processed', 'unmatched', 'failed') THEN
        RAISE EXCEPTION 'Invalid Resend inbound status';
    END IF;

    UPDATE public.resend_inbound_events
    SET status = p_status,
        error_message = LEFT(p_error_message, 1000),
        ticket_id = COALESCE(p_ticket_id, ticket_id),
        updated_at = CURRENT_TIMESTAMP,
        processed_at = CASE
            WHEN p_status IN ('processed', 'unmatched') THEN CURRENT_TIMESTAMP
            ELSE processed_at
        END
    WHERE email_id = p_email_id;
END;
$$;

CREATE OR REPLACE FUNCTION public.complete_resend_inbound_support(
    p_email_id uuid,
    p_user_id uuid,
    p_subject varchar,
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

    v_ticket_id := uuid_generate_v4();

    INSERT INTO public.support_tickets (
        id,
        user_id,
        subject,
        status,
        created_at,
        updated_at,
        last_message_at
    )
    VALUES (
        v_ticket_id,
        p_user_id,
        LEFT(p_subject, 200),
        'open'::support_ticket_status,
        v_now,
        v_now,
        v_now
    );

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

REVOKE ALL ON TABLE public.resend_inbound_events FROM PUBLIC;
REVOKE ALL ON FUNCTION public.claim_resend_inbound_event(varchar, uuid, varchar, varchar, varchar)
    FROM PUBLIC;
REVOKE ALL ON FUNCTION public.set_resend_inbound_event_status(uuid, varchar, text, uuid)
    FROM PUBLIC;
REVOKE ALL ON FUNCTION public.complete_resend_inbound_support(uuid, uuid, varchar, text)
    FROM PUBLIC;
REVOKE ALL ON FUNCTION public.append_resend_inbound_support(uuid, uuid, uuid, text)
    FROM PUBLIC;

GRANT EXECUTE ON FUNCTION public.claim_resend_inbound_event(varchar, uuid, varchar, varchar, varchar)
    TO craftora_app;
GRANT EXECUTE ON FUNCTION public.set_resend_inbound_event_status(uuid, varchar, text, uuid)
    TO craftora_app;
GRANT EXECUTE ON FUNCTION public.complete_resend_inbound_support(uuid, uuid, varchar, text)
    TO craftora_app;
GRANT EXECUTE ON FUNCTION public.append_resend_inbound_support(uuid, uuid, uuid, text)
    TO craftora_app;

COMMIT;
