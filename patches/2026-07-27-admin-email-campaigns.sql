BEGIN;

CREATE TABLE IF NOT EXISTS public.admin_email_campaigns (
    id uuid PRIMARY KEY DEFAULT uuid_generate_v4(),
    admin_user_id uuid NOT NULL REFERENCES public.users(id) ON DELETE RESTRICT,
    idempotency_key varchar(100) NOT NULL,
    audience varchar(20) NOT NULL,
    subject varchar(160) NOT NULL,
    message varchar(10000) NOT NULL,
    status varchar(20) NOT NULL DEFAULT 'queued',
    recipient_count integer NOT NULL DEFAULT 0,
    sent_count integer NOT NULL DEFAULT 0,
    failed_count integer NOT NULL DEFAULT 0,
    created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    started_at timestamptz,
    completed_at timestamptz,
    CONSTRAINT admin_email_campaigns_admin_idempotency_key
        UNIQUE (admin_user_id, idempotency_key),
    CONSTRAINT admin_email_campaigns_audience_check
        CHECK (audience IN ('all', 'users', 'sellers', 'selected')),
    CONSTRAINT admin_email_campaigns_status_check
        CHECK (status IN ('queued', 'sending', 'completed', 'completed_with_failures')),
    CONSTRAINT admin_email_campaigns_counts_check
        CHECK (
            recipient_count >= 0 AND
            sent_count >= 0 AND
            failed_count >= 0 AND
            sent_count + failed_count <= recipient_count
        )
);

CREATE TABLE IF NOT EXISTS public.admin_email_campaign_recipients (
    id uuid PRIMARY KEY DEFAULT uuid_generate_v4(),
    campaign_id uuid NOT NULL
        REFERENCES public.admin_email_campaigns(id) ON DELETE CASCADE,
    user_id uuid NOT NULL REFERENCES public.users(id) ON DELETE RESTRICT,
    email varchar(255) NOT NULL,
    full_name varchar(150),
    status varchar(20) NOT NULL DEFAULT 'pending',
    attempt_count integer NOT NULL DEFAULT 0,
    error_message varchar(1000),
    created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    sent_at timestamptz,
    CONSTRAINT admin_email_campaign_recipients_campaign_user_key
        UNIQUE (campaign_id, user_id),
    CONSTRAINT admin_email_campaign_recipients_status_check
        CHECK (status IN ('pending', 'sending', 'sent', 'failed')),
    CONSTRAINT admin_email_campaign_recipients_attempt_check
        CHECK (attempt_count >= 0)
);

CREATE INDEX IF NOT EXISTS idx_admin_email_campaigns_created
    ON public.admin_email_campaigns (created_at DESC);
CREATE INDEX IF NOT EXISTS idx_admin_email_campaign_recipients_status
    ON public.admin_email_campaign_recipients (campaign_id, status);

ALTER TABLE public.admin_email_campaigns ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.admin_email_campaigns FORCE ROW LEVEL SECURITY;
ALTER TABLE public.admin_email_campaign_recipients ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.admin_email_campaign_recipients FORCE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS admin_email_campaigns_owner
    ON public.admin_email_campaigns;
CREATE POLICY admin_email_campaigns_owner
    ON public.admin_email_campaigns
    FOR ALL
    TO craftora_app
    USING (
        admin_user_id =
        NULLIF(current_setting('app.current_user_id', true), '')::uuid
    )
    WITH CHECK (
        admin_user_id =
        NULLIF(current_setting('app.current_user_id', true), '')::uuid
        AND EXISTS (
            SELECT 1
            FROM public.users
            WHERE id = admin_user_id
              AND role::text = 'admin'
              AND is_active IS TRUE
              AND deleted_at IS NULL
        )
    );

DROP POLICY IF EXISTS admin_email_campaign_recipients_owner
    ON public.admin_email_campaign_recipients;
CREATE POLICY admin_email_campaign_recipients_owner
    ON public.admin_email_campaign_recipients
    FOR ALL
    TO craftora_app
    USING (
        EXISTS (
            SELECT 1
            FROM public.admin_email_campaigns
            WHERE id = campaign_id
        )
    )
    WITH CHECK (
        EXISTS (
            SELECT 1
            FROM public.admin_email_campaigns
            WHERE id = campaign_id
        )
    );

CREATE OR REPLACE FUNCTION public.claim_admin_email_campaign_recipient(
    p_recipient_id uuid
)
RETURNS TABLE (
    campaign_id uuid,
    email varchar,
    full_name varchar,
    subject varchar,
    message varchar
)
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = public, pg_temp
AS $$
BEGIN
    RETURN QUERY
    WITH claimed AS (
        UPDATE public.admin_email_campaign_recipients AS recipient
        SET status = 'sending',
            attempt_count = recipient.attempt_count + 1,
            error_message = NULL
        WHERE recipient.id = p_recipient_id
          AND recipient.status IN ('pending', 'failed')
        RETURNING recipient.campaign_id,
                  recipient.email,
                  recipient.full_name
    ),
    started AS (
        UPDATE public.admin_email_campaigns AS campaign
        SET status = 'sending',
            started_at = COALESCE(campaign.started_at, CURRENT_TIMESTAMP),
            completed_at = NULL
        WHERE campaign.id IN (SELECT claimed.campaign_id FROM claimed)
        RETURNING campaign.id, campaign.subject, campaign.message
    )
    SELECT claimed.campaign_id,
           claimed.email,
           claimed.full_name,
           started.subject,
           started.message
    FROM claimed
    JOIN started ON started.id = claimed.campaign_id;
END;
$$;

CREATE OR REPLACE FUNCTION public.complete_admin_email_campaign_recipient(
    p_recipient_id uuid,
    p_succeeded boolean,
    p_error_message text
)
RETURNS void
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = public, pg_temp
AS $$
DECLARE
    v_campaign_id uuid;
    v_recipient_count integer;
    v_sent_count integer;
    v_failed_count integer;
BEGIN
    UPDATE public.admin_email_campaign_recipients AS recipient
    SET status = CASE WHEN p_succeeded THEN 'sent' ELSE 'failed' END,
        sent_at = CASE WHEN p_succeeded THEN CURRENT_TIMESTAMP ELSE NULL END,
        error_message = CASE
            WHEN p_succeeded THEN NULL
            ELSE LEFT(COALESCE(p_error_message, 'E-posta gonderilemedi.'), 1000)
        END
    WHERE recipient.id = p_recipient_id
      AND recipient.status = 'sending'
    RETURNING recipient.campaign_id INTO v_campaign_id;

    IF v_campaign_id IS NULL THEN
        RETURN;
    END IF;

    SELECT campaign.recipient_count,
           COUNT(*) FILTER (WHERE recipient.status = 'sent')::integer,
           COUNT(*) FILTER (WHERE recipient.status = 'failed')::integer
    INTO v_recipient_count, v_sent_count, v_failed_count
    FROM public.admin_email_campaigns AS campaign
    JOIN public.admin_email_campaign_recipients AS recipient
      ON recipient.campaign_id = campaign.id
    WHERE campaign.id = v_campaign_id
    GROUP BY campaign.recipient_count;

    UPDATE public.admin_email_campaigns
    SET sent_count = v_sent_count,
        failed_count = v_failed_count,
        status = CASE
            WHEN v_sent_count + v_failed_count < v_recipient_count THEN 'sending'
            WHEN v_failed_count = 0 THEN 'completed'
            ELSE 'completed_with_failures'
        END,
        completed_at = CASE
            WHEN v_sent_count + v_failed_count = v_recipient_count
                THEN CURRENT_TIMESTAMP
            ELSE NULL
        END
    WHERE id = v_campaign_id;
END;
$$;

REVOKE ALL ON FUNCTION public.claim_admin_email_campaign_recipient(uuid)
    FROM PUBLIC;
REVOKE ALL ON FUNCTION public.complete_admin_email_campaign_recipient(uuid, boolean, text)
    FROM PUBLIC;
GRANT EXECUTE ON FUNCTION public.claim_admin_email_campaign_recipient(uuid)
    TO craftora_app;
GRANT EXECUTE ON FUNCTION public.complete_admin_email_campaign_recipient(uuid, boolean, text)
    TO craftora_app;

GRANT SELECT, INSERT, UPDATE ON public.admin_email_campaigns TO craftora_app;
GRANT SELECT, INSERT, UPDATE ON public.admin_email_campaign_recipients TO craftora_app;

COMMIT;
