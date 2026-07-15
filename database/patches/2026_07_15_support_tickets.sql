-- ============================================================================
-- CRAFTORA DATABASE PATCH: SUPPORT TICKETS
-- Date: 2026-07-15
-- Purpose: Adds the support ticket conversation schema, indexes, RLS policies,
--          and the ticket updated_at trigger.
-- Run as: PostgreSQL admin/superuser after the existing schema/security patches.
-- Notes: This patch is idempotent and does not grant runtime-role permissions.
-- ============================================================================

BEGIN;

-- ============================================================================
-- ENUM TYPES
-- ============================================================================

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_type type
        INNER JOIN pg_namespace schema ON schema.oid = type.typnamespace
        WHERE schema.nspname = 'public' AND type.typname = 'support_ticket_status'
    ) THEN
        CREATE TYPE public.support_ticket_status AS ENUM ('open', 'answered', 'closed');
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM pg_type type
        INNER JOIN pg_namespace schema ON schema.oid = type.typnamespace
        WHERE schema.nspname = 'public' AND type.typname = 'support_message_sender_role'
    ) THEN
        CREATE TYPE public.support_message_sender_role AS ENUM ('user', 'admin');
    END IF;
END
$$;

-- ============================================================================
-- TABLES
-- ============================================================================

CREATE TABLE IF NOT EXISTS public.support_tickets (
    id uuid NOT NULL DEFAULT public.uuid_generate_v4(),
    user_id uuid NOT NULL,
    subject character varying(200) NOT NULL,
    status public.support_ticket_status NOT NULL DEFAULT 'open'::public.support_ticket_status,
    created_at timestamp with time zone NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at timestamp with time zone NOT NULL DEFAULT CURRENT_TIMESTAMP,
    last_message_at timestamp with time zone NOT NULL DEFAULT CURRENT_TIMESTAMP,
    closed_at timestamp with time zone,
    closed_by_user_id uuid
);

CREATE TABLE IF NOT EXISTS public.support_ticket_messages (
    id uuid NOT NULL DEFAULT public.uuid_generate_v4(),
    ticket_id uuid NOT NULL,
    sender_id uuid NOT NULL,
    sender_role public.support_message_sender_role NOT NULL,
    message text NOT NULL,
    created_at timestamp with time zone NOT NULL DEFAULT CURRENT_TIMESTAMP
);

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'support_tickets_pkey'
          AND conrelid = 'public.support_tickets'::regclass
    ) THEN
        ALTER TABLE ONLY public.support_tickets
            ADD CONSTRAINT support_tickets_pkey PRIMARY KEY (id);
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'support_ticket_messages_pkey'
          AND conrelid = 'public.support_ticket_messages'::regclass
    ) THEN
        ALTER TABLE ONLY public.support_ticket_messages
            ADD CONSTRAINT support_ticket_messages_pkey PRIMARY KEY (id);
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'support_tickets_subject_not_blank'
          AND conrelid = 'public.support_tickets'::regclass
    ) THEN
        ALTER TABLE ONLY public.support_tickets
            ADD CONSTRAINT support_tickets_subject_not_blank
            CHECK (char_length(btrim(subject)) BETWEEN 1 AND 200);
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'support_ticket_messages_message_not_blank'
          AND conrelid = 'public.support_ticket_messages'::regclass
    ) THEN
        ALTER TABLE ONLY public.support_ticket_messages
            ADD CONSTRAINT support_ticket_messages_message_not_blank
            CHECK (char_length(btrim(message)) BETWEEN 1 AND 5000);
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'support_tickets_user_id_fkey'
          AND conrelid = 'public.support_tickets'::regclass
    ) THEN
        ALTER TABLE ONLY public.support_tickets
            ADD CONSTRAINT support_tickets_user_id_fkey
            FOREIGN KEY (user_id) REFERENCES public.users(id) ON DELETE RESTRICT;
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'support_tickets_closed_by_user_id_fkey'
          AND conrelid = 'public.support_tickets'::regclass
    ) THEN
        ALTER TABLE ONLY public.support_tickets
            ADD CONSTRAINT support_tickets_closed_by_user_id_fkey
            FOREIGN KEY (closed_by_user_id) REFERENCES public.users(id) ON DELETE SET NULL;
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'support_ticket_messages_ticket_id_fkey'
          AND conrelid = 'public.support_ticket_messages'::regclass
    ) THEN
        ALTER TABLE ONLY public.support_ticket_messages
            ADD CONSTRAINT support_ticket_messages_ticket_id_fkey
            FOREIGN KEY (ticket_id) REFERENCES public.support_tickets(id) ON DELETE CASCADE;
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'support_ticket_messages_sender_id_fkey'
          AND conrelid = 'public.support_ticket_messages'::regclass
    ) THEN
        ALTER TABLE ONLY public.support_ticket_messages
            ADD CONSTRAINT support_ticket_messages_sender_id_fkey
            FOREIGN KEY (sender_id) REFERENCES public.users(id) ON DELETE RESTRICT;
    END IF;
END
$$;

-- ============================================================================
-- INDEXES AND TRIGGERS
-- ============================================================================

CREATE INDEX IF NOT EXISTS idx_support_tickets_user_last_message
    ON public.support_tickets (user_id, last_message_at DESC);

CREATE INDEX IF NOT EXISTS idx_support_tickets_status_last_message
    ON public.support_tickets (status, last_message_at DESC);

CREATE INDEX IF NOT EXISTS idx_support_ticket_messages_ticket_created
    ON public.support_ticket_messages (ticket_id, created_at);

DROP TRIGGER IF EXISTS set_support_tickets_updated_at ON public.support_tickets;
CREATE TRIGGER set_support_tickets_updated_at
    BEFORE UPDATE ON public.support_tickets
    FOR EACH ROW EXECUTE FUNCTION public.update_updated_at_column();

-- ============================================================================
-- RLS
-- ============================================================================

CREATE OR REPLACE FUNCTION public.is_current_app_admin()
RETURNS boolean
LANGUAGE sql
STABLE
SECURITY DEFINER
SET search_path = public
AS $$
    SELECT EXISTS (
        SELECT 1
        FROM public.users user_record
        WHERE user_record.id = current_setting('app.current_user_id', true)::uuid
          AND user_record.role = 'admin'::public.user_role
          AND user_record.is_active = TRUE
          AND user_record.deleted_at IS NULL
          AND (user_record.locked_until IS NULL OR user_record.locked_until <= CURRENT_TIMESTAMP)
    );
$$;

ALTER TABLE public.support_tickets ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.support_ticket_messages ENABLE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS support_tickets_select_own ON public.support_tickets;
CREATE POLICY support_tickets_select_own ON public.support_tickets FOR SELECT
    USING (user_id = current_setting('app.current_user_id', true)::uuid);

DROP POLICY IF EXISTS support_tickets_insert_own ON public.support_tickets;
CREATE POLICY support_tickets_insert_own ON public.support_tickets FOR INSERT
    WITH CHECK (user_id = current_setting('app.current_user_id', true)::uuid);

DROP POLICY IF EXISTS support_tickets_update_own ON public.support_tickets;
CREATE POLICY support_tickets_update_own ON public.support_tickets FOR UPDATE
    USING (user_id = current_setting('app.current_user_id', true)::uuid)
    WITH CHECK (user_id = current_setting('app.current_user_id', true)::uuid);

DROP POLICY IF EXISTS support_tickets_admin_select ON public.support_tickets;
CREATE POLICY support_tickets_admin_select ON public.support_tickets FOR SELECT
    USING (public.is_current_app_admin());

DROP POLICY IF EXISTS support_tickets_admin_update ON public.support_tickets;
CREATE POLICY support_tickets_admin_update ON public.support_tickets FOR UPDATE
    USING (public.is_current_app_admin())
    WITH CHECK (public.is_current_app_admin());

DROP POLICY IF EXISTS support_ticket_messages_select_own ON public.support_ticket_messages;
CREATE POLICY support_ticket_messages_select_own ON public.support_ticket_messages FOR SELECT
    USING (
        EXISTS (
            SELECT 1
            FROM public.support_tickets ticket
            WHERE ticket.id = support_ticket_messages.ticket_id
              AND ticket.user_id = current_setting('app.current_user_id', true)::uuid
        )
    );

DROP POLICY IF EXISTS support_ticket_messages_insert_own ON public.support_ticket_messages;
CREATE POLICY support_ticket_messages_insert_own ON public.support_ticket_messages FOR INSERT
    WITH CHECK (
        sender_id = current_setting('app.current_user_id', true)::uuid
        AND sender_role = 'user'::public.support_message_sender_role
        AND EXISTS (
            SELECT 1
            FROM public.support_tickets ticket
            WHERE ticket.id = support_ticket_messages.ticket_id
              AND ticket.user_id = current_setting('app.current_user_id', true)::uuid
        )
    );

DROP POLICY IF EXISTS support_ticket_messages_admin_select ON public.support_ticket_messages;
CREATE POLICY support_ticket_messages_admin_select ON public.support_ticket_messages FOR SELECT
    USING (public.is_current_app_admin());

DROP POLICY IF EXISTS support_ticket_messages_admin_insert ON public.support_ticket_messages;
CREATE POLICY support_ticket_messages_admin_insert ON public.support_ticket_messages FOR INSERT
    WITH CHECK (
        public.is_current_app_admin()
        AND sender_id = current_setting('app.current_user_id', true)::uuid
        AND sender_role = 'admin'::public.support_message_sender_role
    );

COMMIT;
