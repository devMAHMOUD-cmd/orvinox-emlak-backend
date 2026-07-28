BEGIN;

CREATE TABLE IF NOT EXISTS public.discovery_events (
    id uuid PRIMARY KEY DEFAULT uuid_generate_v4(),
    event_id uuid NOT NULL,
    user_id uuid,
    feed_session_id uuid NOT NULL,
    tracking_token_id uuid NOT NULL,
    content_type varchar(20) NOT NULL,
    content_id uuid NOT NULL,
    shop_id uuid NOT NULL,
    event_type varchar(30) NOT NULL,
    position integer NOT NULL,
    algorithm_version varchar(30) NOT NULL,
    dwell_ms integer,
    completion_rate numeric(6,5),
    visible_percentage integer,
    metadata jsonb NOT NULL DEFAULT '{}'::jsonb,
    created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT discovery_events_event_id_key UNIQUE (event_id),
    CONSTRAINT discovery_events_user_id_fkey
        FOREIGN KEY (user_id) REFERENCES public.users(id) ON DELETE SET NULL,
    CONSTRAINT discovery_events_shop_id_fkey
        FOREIGN KEY (shop_id) REFERENCES public.shops(id) ON DELETE CASCADE,
    CONSTRAINT discovery_events_content_type_check
        CHECK (content_type IN ('media', 'product', 'course', 'shop')),
    CONSTRAINT discovery_events_event_type_check
        CHECK (event_type IN (
            'impression',
            'playback_started',
            'playback_progress',
            'playback_ended',
            'playback_completed',
            'looped',
            'content_opened',
            'not_interested',
            'hide_shop'
        )),
    CONSTRAINT discovery_events_position_check CHECK (position >= 0),
    CONSTRAINT discovery_events_dwell_check
        CHECK (dwell_ms IS NULL OR dwell_ms BETWEEN 0 AND 21600000),
    CONSTRAINT discovery_events_completion_check
        CHECK (completion_rate IS NULL OR completion_rate BETWEEN 0 AND 1),
    CONSTRAINT discovery_events_visibility_check
        CHECK (visible_percentage IS NULL OR visible_percentage BETWEEN 0 AND 100),
    CONSTRAINT discovery_events_metadata_check
        CHECK (jsonb_typeof(metadata) = 'object')
);

CREATE INDEX IF NOT EXISTS idx_discovery_events_user_created
    ON public.discovery_events (user_id, created_at DESC);
CREATE INDEX IF NOT EXISTS idx_discovery_events_content_created
    ON public.discovery_events (content_type, content_id, created_at DESC);
CREATE INDEX IF NOT EXISTS idx_discovery_events_session
    ON public.discovery_events (feed_session_id, position, created_at);
CREATE INDEX IF NOT EXISTS idx_discovery_events_type_created
    ON public.discovery_events (event_type, created_at DESC);

CREATE TABLE IF NOT EXISTS public.content_discovery_scores (
    id uuid PRIMARY KEY DEFAULT uuid_generate_v4(),
    content_type varchar(20) NOT NULL,
    content_id uuid NOT NULL,
    shop_id uuid NOT NULL,
    quality_score numeric(12,6) NOT NULL DEFAULT 0,
    popularity_score numeric(12,6) NOT NULL DEFAULT 0,
    freshness_score numeric(12,6) NOT NULL DEFAULT 0,
    sample_size integer NOT NULL DEFAULT 0,
    computed_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT content_discovery_scores_content_key
        UNIQUE (content_type, content_id),
    CONSTRAINT content_discovery_scores_shop_id_fkey
        FOREIGN KEY (shop_id) REFERENCES public.shops(id) ON DELETE CASCADE,
    CONSTRAINT content_discovery_scores_content_type_check
        CHECK (content_type IN ('media', 'product', 'course', 'shop')),
    CONSTRAINT content_discovery_scores_sample_size_check
        CHECK (sample_size >= 0)
);

CREATE INDEX IF NOT EXISTS idx_content_discovery_scores_rank
    ON public.content_discovery_scores (
        content_type,
        quality_score DESC,
        popularity_score DESC,
        freshness_score DESC
    );

CREATE TABLE IF NOT EXISTS public.user_discovery_affinities (
    id uuid PRIMARY KEY DEFAULT uuid_generate_v4(),
    user_id uuid NOT NULL,
    affinity_type varchar(20) NOT NULL,
    affinity_key varchar(100) NOT NULL,
    score numeric(12,6) NOT NULL DEFAULT 0,
    signal_count integer NOT NULL DEFAULT 0,
    last_signal_at timestamptz,
    updated_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT user_discovery_affinities_key
        UNIQUE (user_id, affinity_type, affinity_key),
    CONSTRAINT user_discovery_affinities_user_id_fkey
        FOREIGN KEY (user_id) REFERENCES public.users(id) ON DELETE CASCADE,
    CONSTRAINT user_discovery_affinities_type_check
        CHECK (affinity_type IN ('category', 'shop', 'content_type')),
    CONSTRAINT user_discovery_affinities_signal_count_check
        CHECK (signal_count >= 0)
);

CREATE INDEX IF NOT EXISTS idx_user_discovery_affinities_rank
    ON public.user_discovery_affinities (user_id, affinity_type, score DESC);

CREATE TABLE IF NOT EXISTS public.user_discovery_feedback (
    id uuid PRIMARY KEY DEFAULT uuid_generate_v4(),
    user_id uuid NOT NULL,
    feedback_type varchar(30) NOT NULL,
    content_type varchar(20),
    content_id uuid,
    shop_id uuid,
    is_active boolean NOT NULL DEFAULT true,
    expires_at timestamptz,
    created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT user_discovery_feedback_user_id_fkey
        FOREIGN KEY (user_id) REFERENCES public.users(id) ON DELETE CASCADE,
    CONSTRAINT user_discovery_feedback_shop_id_fkey
        FOREIGN KEY (shop_id) REFERENCES public.shops(id) ON DELETE CASCADE,
    CONSTRAINT user_discovery_feedback_type_check
        CHECK (feedback_type IN ('not_interested', 'hide_shop')),
    CONSTRAINT user_discovery_feedback_content_type_check
        CHECK (
            content_type IS NULL OR
            content_type IN ('media', 'product', 'course', 'shop')
        ),
    CONSTRAINT user_discovery_feedback_shape_check
        CHECK (
            (
                feedback_type = 'not_interested' AND
                content_type IS NOT NULL AND
                content_id IS NOT NULL AND
                shop_id IS NOT NULL AND
                expires_at IS NULL
            )
            OR
            (
                feedback_type = 'hide_shop' AND
                content_type IS NULL AND
                content_id IS NULL AND
                shop_id IS NOT NULL AND
                expires_at IS NOT NULL
            )
        )
);

CREATE UNIQUE INDEX IF NOT EXISTS user_discovery_feedback_content_active_key
    ON public.user_discovery_feedback (user_id, content_type, content_id)
    WHERE feedback_type = 'not_interested' AND is_active = true;

CREATE UNIQUE INDEX IF NOT EXISTS user_discovery_feedback_shop_active_key
    ON public.user_discovery_feedback (user_id, shop_id)
    WHERE feedback_type = 'hide_shop' AND is_active = true;

CREATE INDEX IF NOT EXISTS idx_user_discovery_feedback_active
    ON public.user_discovery_feedback (user_id, feedback_type, expires_at)
    WHERE is_active = true;

ALTER TABLE public.discovery_events ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.content_discovery_scores ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.user_discovery_affinities ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.user_discovery_feedback ENABLE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS discovery_events_backend_only
ON public.discovery_events;
CREATE POLICY discovery_events_backend_only
ON public.discovery_events
USING (false)
WITH CHECK (false);

DROP POLICY IF EXISTS content_discovery_scores_backend_only
ON public.content_discovery_scores;
CREATE POLICY content_discovery_scores_backend_only
ON public.content_discovery_scores
USING (false)
WITH CHECK (false);

DROP POLICY IF EXISTS user_discovery_affinities_backend_only
ON public.user_discovery_affinities;
CREATE POLICY user_discovery_affinities_backend_only
ON public.user_discovery_affinities
USING (false)
WITH CHECK (false);

DROP POLICY IF EXISTS user_discovery_feedback_backend_only
ON public.user_discovery_feedback;
CREATE POLICY user_discovery_feedback_backend_only
ON public.user_discovery_feedback
USING (false)
WITH CHECK (false);

CREATE OR REPLACE FUNCTION public.record_discovery_event(
    p_event_id uuid,
    p_user_id uuid,
    p_feed_session_id uuid,
    p_tracking_token_id uuid,
    p_content_type text,
    p_content_id uuid,
    p_shop_id uuid,
    p_event_type text,
    p_position integer,
    p_algorithm_version text,
    p_dwell_ms integer,
    p_completion_rate numeric,
    p_visible_percentage integer,
    p_metadata jsonb)
RETURNS TABLE(event_record_id uuid, was_inserted boolean)
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = public, pg_temp
AS $function$
DECLARE
    current_user_id uuid;
    existing_user_id uuid;
    inserted_id uuid;
    content_is_valid boolean;
BEGIN
    current_user_id :=
        NULLIF(current_setting('app.current_user_id', true), '')::uuid;
    IF current_user_id IS NULL OR current_user_id <> p_user_id THEN
        RAISE EXCEPTION 'Discovery event user mismatch'
            USING ERRCODE = '42501';
    END IF;

    IF p_event_id IS NULL OR
       p_feed_session_id IS NULL OR
       p_tracking_token_id IS NULL OR
       p_content_id IS NULL OR
       p_shop_id IS NULL OR
       p_position < 0 OR
       p_content_type NOT IN ('media', 'product', 'course', 'shop') OR
       p_event_type NOT IN (
           'impression',
           'playback_started',
           'playback_progress',
           'playback_ended',
           'playback_completed',
           'looped',
           'content_opened',
           'not_interested',
           'hide_shop'
       ) THEN
        RAISE EXCEPTION 'Invalid discovery event payload'
            USING ERRCODE = '23514';
    END IF;

    IF p_content_type = 'media' THEN
        SELECT EXISTS (
            SELECT 1
            FROM public.media AS medium
            JOIN public.shops AS shop ON shop.id = medium.shop_id
            WHERE medium.id = p_content_id
              AND medium.shop_id = p_shop_id
              AND medium.is_active = true
              AND medium.status = 'ready'::public.media_status
              AND shop.is_active = true
        ) INTO content_is_valid;
    ELSIF p_content_type = 'product' THEN
        SELECT EXISTS (
            SELECT 1
            FROM public.products AS product
            JOIN public.shops AS shop ON shop.id = product.shop_id
            WHERE product.id = p_content_id
              AND product.shop_id = p_shop_id
              AND product.is_active = true
              AND product.status = 'Published'
              AND shop.is_active = true
        ) INTO content_is_valid;
    ELSIF p_content_type = 'course' THEN
        SELECT EXISTS (
            SELECT 1
            FROM public.courses AS course
            JOIN public.products AS product ON product.id = course.product_id
            JOIN public.shops AS shop ON shop.id = product.shop_id
            WHERE course.id = p_content_id
              AND product.shop_id = p_shop_id
              AND product.is_active = true
              AND product.status = 'Published'
              AND shop.is_active = true
        ) INTO content_is_valid;
    ELSE
        SELECT EXISTS (
            SELECT 1
            FROM public.shops AS shop
            WHERE shop.id = p_content_id
              AND shop.id = p_shop_id
              AND shop.is_active = true
        ) INTO content_is_valid;
    END IF;

    IF content_is_valid IS DISTINCT FROM true THEN
        RAISE EXCEPTION 'Discovery content is not available'
            USING ERRCODE = '23514';
    END IF;

    IF p_metadata IS NULL OR
       jsonb_typeof(p_metadata) <> 'object' OR
       octet_length(p_metadata::text) > 4096 THEN
        RAISE EXCEPTION 'Invalid discovery metadata'
            USING ERRCODE = '23514';
    END IF;

    SELECT event.user_id
    INTO existing_user_id
    FROM public.discovery_events AS event
    WHERE event.event_id = p_event_id;

    IF FOUND THEN
        IF existing_user_id IS DISTINCT FROM p_user_id THEN
            RAISE EXCEPTION 'Discovery event id belongs to another user'
                USING ERRCODE = '42501';
        END IF;

        SELECT event.id
        INTO event_record_id
        FROM public.discovery_events AS event
        WHERE event.event_id = p_event_id;
        was_inserted := false;
        RETURN NEXT;
        RETURN;
    END IF;

    INSERT INTO public.discovery_events (
        event_id,
        user_id,
        feed_session_id,
        tracking_token_id,
        content_type,
        content_id,
        shop_id,
        event_type,
        position,
        algorithm_version,
        dwell_ms,
        completion_rate,
        visible_percentage,
        metadata)
    VALUES (
        p_event_id,
        p_user_id,
        p_feed_session_id,
        p_tracking_token_id,
        p_content_type,
        p_content_id,
        p_shop_id,
        p_event_type,
        p_position,
        p_algorithm_version,
        p_dwell_ms,
        p_completion_rate,
        p_visible_percentage,
        p_metadata)
    ON CONFLICT (event_id) DO NOTHING
    RETURNING id INTO inserted_id;

    IF inserted_id IS NOT NULL THEN
        event_record_id := inserted_id;
        was_inserted := true;
        RETURN NEXT;
        RETURN;
    END IF;

    SELECT event.id, event.user_id
    INTO event_record_id, existing_user_id
    FROM public.discovery_events AS event
    WHERE event.event_id = p_event_id;

    IF existing_user_id IS DISTINCT FROM p_user_id THEN
        RAISE EXCEPTION 'Discovery event id belongs to another user'
            USING ERRCODE = '42501';
    END IF;

    was_inserted := false;
    RETURN NEXT;
END;
$function$;

CREATE OR REPLACE FUNCTION public.set_discovery_feedback(
    p_user_id uuid,
    p_feedback_type text,
    p_content_type text,
    p_content_id uuid,
    p_shop_id uuid)
RETURNS TABLE(
    feedback_id uuid,
    result_feedback_type text,
    result_content_type text,
    result_content_id uuid,
    result_shop_id uuid,
    result_expires_at timestamptz,
    result_created_at timestamptz)
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = public, pg_temp
AS $function$
DECLARE
    current_user_id uuid;
    feedback_record public.user_discovery_feedback%ROWTYPE;
BEGIN
    current_user_id :=
        NULLIF(current_setting('app.current_user_id', true), '')::uuid;
    IF current_user_id IS NULL OR current_user_id <> p_user_id THEN
        RAISE EXCEPTION 'Discovery feedback user mismatch'
            USING ERRCODE = '42501';
    END IF;

    IF p_feedback_type = 'not_interested' THEN
        IF p_content_type NOT IN ('media', 'product', 'course', 'shop') OR
           p_content_id IS NULL OR
           p_shop_id IS NULL THEN
            RAISE EXCEPTION 'Invalid not_interested payload'
                USING ERRCODE = '23514';
        END IF;

        INSERT INTO public.user_discovery_feedback (
            user_id,
            feedback_type,
            content_type,
            content_id,
            shop_id,
            expires_at)
        VALUES (
            p_user_id,
            'not_interested',
            p_content_type,
            p_content_id,
            p_shop_id,
            NULL)
        ON CONFLICT (user_id, content_type, content_id)
            WHERE feedback_type = 'not_interested' AND is_active = true
        DO UPDATE SET
            shop_id = EXCLUDED.shop_id,
            updated_at = CURRENT_TIMESTAMP
        RETURNING * INTO feedback_record;
    ELSIF p_feedback_type = 'hide_shop' THEN
        IF p_shop_id IS NULL THEN
            RAISE EXCEPTION 'Invalid hide_shop payload'
                USING ERRCODE = '23514';
        END IF;

        INSERT INTO public.user_discovery_feedback (
            user_id,
            feedback_type,
            content_type,
            content_id,
            shop_id,
            expires_at)
        VALUES (
            p_user_id,
            'hide_shop',
            NULL,
            NULL,
            p_shop_id,
            CURRENT_TIMESTAMP + INTERVAL '30 days')
        ON CONFLICT (user_id, shop_id)
            WHERE feedback_type = 'hide_shop' AND is_active = true
        DO UPDATE SET
            expires_at = CURRENT_TIMESTAMP + INTERVAL '30 days',
            updated_at = CURRENT_TIMESTAMP
        RETURNING * INTO feedback_record;
    ELSE
        RAISE EXCEPTION 'Invalid discovery feedback type'
            USING ERRCODE = '23514';
    END IF;

    feedback_id := feedback_record.id;
    result_feedback_type := feedback_record.feedback_type;
    result_content_type := feedback_record.content_type;
    result_content_id := feedback_record.content_id;
    result_shop_id := feedback_record.shop_id;
    result_expires_at := feedback_record.expires_at;
    result_created_at := feedback_record.created_at;
    RETURN NEXT;
END;
$function$;

CREATE OR REPLACE FUNCTION public.get_active_discovery_feedback(p_user_id uuid)
RETURNS TABLE(
    feedback_id uuid,
    feedback_type text,
    content_type text,
    content_id uuid,
    shop_id uuid,
    expires_at timestamptz,
    created_at timestamptz)
LANGUAGE plpgsql
STABLE
SECURITY DEFINER
SET search_path = public, pg_temp
AS $function$
DECLARE
    current_user_id uuid;
BEGIN
    current_user_id :=
        NULLIF(current_setting('app.current_user_id', true), '')::uuid;
    IF current_user_id IS NULL OR current_user_id <> p_user_id THEN
        RAISE EXCEPTION 'Discovery feedback user mismatch'
            USING ERRCODE = '42501';
    END IF;

    RETURN QUERY
    SELECT
        feedback.id,
        feedback.feedback_type::text,
        feedback.content_type::text,
        feedback.content_id,
        feedback.shop_id,
        feedback.expires_at,
        feedback.created_at
    FROM public.user_discovery_feedback AS feedback
    WHERE feedback.user_id = p_user_id
      AND feedback.is_active = true
      AND (
          feedback.expires_at IS NULL OR
          feedback.expires_at > CURRENT_TIMESTAMP
      )
    ORDER BY feedback.created_at DESC;
END;
$function$;

CREATE OR REPLACE FUNCTION public.remove_discovery_feedback(
    p_user_id uuid,
    p_feedback_id uuid)
RETURNS boolean
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = public, pg_temp
AS $function$
DECLARE
    current_user_id uuid;
    affected_rows integer;
BEGIN
    current_user_id :=
        NULLIF(current_setting('app.current_user_id', true), '')::uuid;
    IF current_user_id IS NULL OR current_user_id <> p_user_id THEN
        RAISE EXCEPTION 'Discovery feedback user mismatch'
            USING ERRCODE = '42501';
    END IF;

    UPDATE public.user_discovery_feedback AS feedback
    SET
        is_active = false,
        updated_at = CURRENT_TIMESTAMP
    WHERE feedback.id = p_feedback_id
      AND feedback.user_id = p_user_id
      AND feedback.is_active = true;

    GET DIAGNOSTICS affected_rows = ROW_COUNT;
    RETURN affected_rows > 0;
END;
$function$;

REVOKE ALL ON TABLE public.discovery_events FROM PUBLIC;
REVOKE ALL ON TABLE public.content_discovery_scores FROM PUBLIC;
REVOKE ALL ON TABLE public.user_discovery_affinities FROM PUBLIC;
REVOKE ALL ON TABLE public.user_discovery_feedback FROM PUBLIC;

REVOKE ALL ON FUNCTION public.record_discovery_event(
    uuid, uuid, uuid, uuid, text, uuid, uuid, text,
    integer, text, integer, numeric, integer, jsonb)
FROM PUBLIC;
REVOKE ALL ON FUNCTION public.set_discovery_feedback(
    uuid, text, text, uuid, uuid)
FROM PUBLIC;
REVOKE ALL ON FUNCTION public.get_active_discovery_feedback(uuid)
FROM PUBLIC;
REVOKE ALL ON FUNCTION public.remove_discovery_feedback(uuid, uuid)
FROM PUBLIC;

DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'craftora_app') THEN
        GRANT EXECUTE ON FUNCTION public.record_discovery_event(
            uuid, uuid, uuid, uuid, text, uuid, uuid, text,
            integer, text, integer, numeric, integer, jsonb)
        TO craftora_app;
        GRANT EXECUTE ON FUNCTION public.set_discovery_feedback(
            uuid, text, text, uuid, uuid)
        TO craftora_app;
        GRANT EXECUTE ON FUNCTION public.get_active_discovery_feedback(uuid)
        TO craftora_app;
        GRANT EXECUTE ON FUNCTION public.remove_discovery_feedback(uuid, uuid)
        TO craftora_app;
    END IF;
END
$$;

COMMIT;
