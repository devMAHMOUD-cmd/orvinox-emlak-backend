BEGIN;

ALTER TABLE public.content_discovery_scores
    ADD COLUMN IF NOT EXISTS boost_enabled boolean NOT NULL DEFAULT false,
    ADD COLUMN IF NOT EXISTS boost_credit_total integer NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS boost_credit_remaining integer NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS boost_starts_at timestamptz,
    ADD COLUMN IF NOT EXISTS boost_ends_at timestamptz,
    ADD COLUMN IF NOT EXISTS boosted_by_user_id uuid,
    ADD COLUMN IF NOT EXISTS boost_updated_at timestamptz;

ALTER TABLE public.discovery_events
    ADD COLUMN IF NOT EXISTS is_sponsored boolean NOT NULL DEFAULT false,
    ADD COLUMN IF NOT EXISTS boost_id uuid;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conrelid = 'public.content_discovery_scores'::regclass
          AND conname = 'content_discovery_scores_boost_credit_check'
    ) THEN
        ALTER TABLE public.content_discovery_scores
            ADD CONSTRAINT content_discovery_scores_boost_credit_check
            CHECK (
                boost_credit_total >= 0 AND
                boost_credit_remaining >= 0 AND
                boost_credit_remaining <= boost_credit_total
            );
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conrelid = 'public.content_discovery_scores'::regclass
          AND conname = 'content_discovery_scores_boost_dates_check'
    ) THEN
        ALTER TABLE public.content_discovery_scores
            ADD CONSTRAINT content_discovery_scores_boost_dates_check
            CHECK (
                boost_starts_at IS NULL OR
                boost_ends_at IS NULL OR
                boost_ends_at > boost_starts_at
            );
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conrelid = 'public.content_discovery_scores'::regclass
          AND conname = 'content_discovery_scores_boost_admin_fkey'
    ) THEN
        ALTER TABLE public.content_discovery_scores
            ADD CONSTRAINT content_discovery_scores_boost_admin_fkey
            FOREIGN KEY (boosted_by_user_id)
            REFERENCES public.users(id)
            ON DELETE SET NULL;
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conrelid = 'public.discovery_events'::regclass
          AND conname = 'discovery_events_boost_id_fkey'
    ) THEN
        ALTER TABLE public.discovery_events
            ADD CONSTRAINT discovery_events_boost_id_fkey
            FOREIGN KEY (boost_id)
            REFERENCES public.content_discovery_scores(id)
            ON DELETE RESTRICT;
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conrelid = 'public.discovery_events'::regclass
          AND conname = 'discovery_events_sponsored_shape_check'
    ) THEN
        ALTER TABLE public.discovery_events
            ADD CONSTRAINT discovery_events_sponsored_shape_check
            CHECK (
                (is_sponsored = false AND boost_id IS NULL) OR
                (is_sponsored = true AND boost_id IS NOT NULL)
            );
    END IF;
END
$$;

CREATE INDEX IF NOT EXISTS idx_content_discovery_scores_active_boost
    ON public.content_discovery_scores (
        boost_ends_at,
        boost_credit_remaining DESC,
        boost_updated_at DESC
    )
    WHERE boost_enabled = true;

CREATE INDEX IF NOT EXISTS idx_discovery_events_sponsored
    ON public.discovery_events (boost_id, created_at DESC)
    WHERE is_sponsored = true;

CREATE OR REPLACE FUNCTION public.set_discovery_boost(
    p_admin_user_id uuid,
    p_content_type text,
    p_content_id uuid,
    p_credit_amount integer,
    p_starts_at timestamptz,
    p_ends_at timestamptz)
RETURNS TABLE(
    boost_id uuid,
    result_content_type text,
    result_content_id uuid,
    result_shop_id uuid,
    credit_total integer,
    credit_remaining integer,
    starts_at timestamptz,
    ends_at timestamptz,
    enabled boolean)
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = public, pg_temp
AS $function$
DECLARE
    current_user_id uuid;
    target_shop_id uuid;
    normalized_starts_at timestamptz;
    normalized_ends_at timestamptz;
BEGIN
    current_user_id :=
        NULLIF(current_setting('app.current_user_id', true), '')::uuid;
    IF current_user_id IS NULL OR
       current_user_id <> p_admin_user_id OR
       NOT EXISTS (
           SELECT 1
           FROM public.users AS admin_user
           WHERE admin_user.id = p_admin_user_id
             AND admin_user.role::text = 'admin'
             AND admin_user.is_active = true
             AND admin_user.deleted_at IS NULL
       ) THEN
        RAISE EXCEPTION 'Discovery boost admin mismatch'
            USING ERRCODE = '42501';
    END IF;

    IF p_content_type NOT IN ('media', 'product', 'course') OR
       p_content_id IS NULL OR
       p_credit_amount IS NULL OR
       p_credit_amount < 1 OR
       p_credit_amount > 100000 THEN
        RAISE EXCEPTION 'Discovery boost request is invalid'
            USING ERRCODE = '23514';
    END IF;

    normalized_starts_at := COALESCE(p_starts_at, CURRENT_TIMESTAMP);
    normalized_ends_at := COALESCE(
        p_ends_at,
        normalized_starts_at + INTERVAL '7 days');
    IF normalized_ends_at <= normalized_starts_at OR
       normalized_ends_at > normalized_starts_at + INTERVAL '30 days' THEN
        RAISE EXCEPTION 'Discovery boost date range is invalid'
            USING ERRCODE = '23514';
    END IF;

    IF p_content_type = 'media' THEN
        SELECT medium.shop_id
        INTO target_shop_id
        FROM public.media AS medium
        JOIN public.shops AS shop ON shop.id = medium.shop_id
        LEFT JOIN public.products AS product ON product.id = medium.product_id
        WHERE medium.id = p_content_id
          AND medium.is_active = true
          AND medium.status = 'ready'::public.media_status
          AND shop.is_active = true
          AND (
              medium.product_id IS NULL OR
              (
                  product.is_active = true AND
                  product.status = 'Published'
              )
          );
    ELSIF p_content_type = 'product' THEN
        SELECT product.shop_id
        INTO target_shop_id
        FROM public.products AS product
        JOIN public.shops AS shop ON shop.id = product.shop_id
        WHERE product.id = p_content_id
          AND product.type = 'digital_file'::public.product_type
          AND product.is_active = true
          AND product.status = 'Published'
          AND shop.is_active = true;
    ELSE
        SELECT product.shop_id
        INTO target_shop_id
        FROM public.courses AS course
        JOIN public.products AS product ON product.id = course.product_id
        JOIN public.shops AS shop ON shop.id = product.shop_id
        WHERE course.id = p_content_id
          AND product.type = 'course'::public.product_type
          AND product.is_active = true
          AND product.status = 'Published'
          AND shop.is_active = true;
    END IF;

    IF target_shop_id IS NULL THEN
        RAISE EXCEPTION 'Discovery boost content is not eligible'
            USING ERRCODE = 'P0002';
    END IF;

    INSERT INTO public.content_discovery_scores AS score (
        content_type,
        content_id,
        shop_id,
        boost_enabled,
        boost_credit_total,
        boost_credit_remaining,
        boost_starts_at,
        boost_ends_at,
        boosted_by_user_id,
        boost_updated_at)
    VALUES (
        p_content_type,
        p_content_id,
        target_shop_id,
        true,
        p_credit_amount,
        p_credit_amount,
        normalized_starts_at,
        normalized_ends_at,
        p_admin_user_id,
        CURRENT_TIMESTAMP)
    ON CONFLICT (content_type, content_id)
    DO UPDATE SET
        shop_id = EXCLUDED.shop_id,
        boost_enabled = true,
        boost_credit_total = EXCLUDED.boost_credit_total,
        boost_credit_remaining = EXCLUDED.boost_credit_remaining,
        boost_starts_at = EXCLUDED.boost_starts_at,
        boost_ends_at = EXCLUDED.boost_ends_at,
        boosted_by_user_id = EXCLUDED.boosted_by_user_id,
        boost_updated_at = CURRENT_TIMESTAMP;

    INSERT INTO public.admin_audit_logs (
        admin_user_id,
        action,
        target_type,
        target_id,
        metadata)
    VALUES (
        p_admin_user_id,
        'set_discovery_boost',
        p_content_type,
        p_content_id,
        jsonb_build_object(
            'credits', p_credit_amount,
            'startsAt', normalized_starts_at,
            'endsAt', normalized_ends_at));

    RETURN QUERY
    SELECT
        score.id,
        score.content_type::text,
        score.content_id,
        score.shop_id,
        score.boost_credit_total,
        score.boost_credit_remaining,
        score.boost_starts_at,
        score.boost_ends_at,
        score.boost_enabled
    FROM public.content_discovery_scores AS score
    WHERE score.content_type = p_content_type
      AND score.content_id = p_content_id;
END;
$function$;

CREATE OR REPLACE FUNCTION public.stop_discovery_boost(
    p_admin_user_id uuid,
    p_boost_id uuid)
RETURNS boolean
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = public, pg_temp
AS $function$
DECLARE
    current_user_id uuid;
    target_content_type text;
    target_content_id uuid;
BEGIN
    current_user_id :=
        NULLIF(current_setting('app.current_user_id', true), '')::uuid;
    IF current_user_id IS NULL OR
       current_user_id <> p_admin_user_id OR
       NOT EXISTS (
           SELECT 1
           FROM public.users AS admin_user
           WHERE admin_user.id = p_admin_user_id
             AND admin_user.role::text = 'admin'
             AND admin_user.is_active = true
             AND admin_user.deleted_at IS NULL
       ) THEN
        RAISE EXCEPTION 'Discovery boost admin mismatch'
            USING ERRCODE = '42501';
    END IF;

    UPDATE public.content_discovery_scores AS score
    SET
        boost_enabled = false,
        boost_updated_at = CURRENT_TIMESTAMP
    WHERE score.id = p_boost_id
      AND score.boost_enabled = true
    RETURNING score.content_type, score.content_id
    INTO target_content_type, target_content_id;

    IF target_content_id IS NULL THEN
        RETURN false;
    END IF;

    INSERT INTO public.admin_audit_logs (
        admin_user_id,
        action,
        target_type,
        target_id,
        metadata)
    VALUES (
        p_admin_user_id,
        'stop_discovery_boost',
        target_content_type,
        target_content_id,
        jsonb_build_object('boostId', p_boost_id));

    RETURN true;
END;
$function$;

CREATE OR REPLACE FUNCTION public.get_admin_discovery_boosts(
    p_admin_user_id uuid)
RETURNS TABLE(
    boost_id uuid,
    result_content_type text,
    result_content_id uuid,
    result_shop_id uuid,
    content_title text,
    credit_total integer,
    credit_remaining integer,
    starts_at timestamptz,
    ends_at timestamptz,
    enabled boolean,
    updated_at timestamptz)
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
    IF current_user_id IS NULL OR
       current_user_id <> p_admin_user_id OR
       NOT EXISTS (
           SELECT 1
           FROM public.users AS admin_user
           WHERE admin_user.id = p_admin_user_id
             AND admin_user.role::text = 'admin'
             AND admin_user.is_active = true
             AND admin_user.deleted_at IS NULL
       ) THEN
        RAISE EXCEPTION 'Discovery boost admin mismatch'
            USING ERRCODE = '42501';
    END IF;

    RETURN QUERY
    SELECT
        score.id,
        score.content_type::text,
        score.content_id,
        score.shop_id,
        (CASE score.content_type
            WHEN 'media' THEN (
                SELECT COALESCE(medium.caption, 'Reels')
                FROM public.media AS medium
                WHERE medium.id = score.content_id
            )
            WHEN 'product' THEN (
                SELECT product.title
                FROM public.products AS product
                WHERE product.id = score.content_id
            )
            WHEN 'course' THEN (
                SELECT product.title
                FROM public.courses AS course
                JOIN public.products AS product ON product.id = course.product_id
                WHERE course.id = score.content_id
            )
        END)::text,
        score.boost_credit_total,
        score.boost_credit_remaining,
        score.boost_starts_at,
        score.boost_ends_at,
        score.boost_enabled,
        score.boost_updated_at
    FROM public.content_discovery_scores AS score
    WHERE score.boost_updated_at IS NOT NULL
    ORDER BY score.boost_updated_at DESC;
END;
$function$;

CREATE OR REPLACE FUNCTION public.get_sponsored_discovery_candidates(
    p_user_id uuid,
    p_limit integer DEFAULT 10)
RETURNS TABLE(
    boost_id uuid,
    content_type text,
    content_id uuid,
    shop_id uuid)
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
        RAISE EXCEPTION 'Sponsored discovery user mismatch'
            USING ERRCODE = '42501';
    END IF;

    IF p_limit IS NULL OR p_limit < 1 OR p_limit > 50 THEN
        RAISE EXCEPTION 'Sponsored discovery limit is invalid'
            USING ERRCODE = '23514';
    END IF;

    RETURN QUERY
    WITH active_content AS (
        SELECT
            score.id AS candidate_boost_id,
            score.content_type::text AS candidate_content_type,
            score.content_id AS candidate_content_id,
            score.shop_id AS candidate_shop_id,
            medium.product_id AS candidate_product_id,
            score.boost_ends_at
        FROM public.content_discovery_scores AS score
        JOIN public.media AS medium
          ON score.content_type = 'media'
         AND medium.id = score.content_id
        JOIN public.shops AS shop ON shop.id = medium.shop_id
        LEFT JOIN public.products AS product ON product.id = medium.product_id
        WHERE score.boost_enabled = true
          AND score.boost_credit_remaining > 0
          AND score.boost_starts_at <= CURRENT_TIMESTAMP
          AND score.boost_ends_at > CURRENT_TIMESTAMP
          AND medium.is_active = true
          AND medium.status = 'ready'::public.media_status
          AND shop.is_active = true
          AND shop.user_id <> p_user_id
          AND (
              medium.product_id IS NULL OR
              (
                  product.is_active = true AND
                  product.status = 'Published'
              )
          )

        UNION ALL

        SELECT
            score.id,
            score.content_type::text,
            score.content_id,
            score.shop_id,
            product.id,
            score.boost_ends_at
        FROM public.content_discovery_scores AS score
        JOIN public.products AS product
          ON score.content_type = 'product'
         AND product.id = score.content_id
        JOIN public.shops AS shop ON shop.id = product.shop_id
        WHERE score.boost_enabled = true
          AND score.boost_credit_remaining > 0
          AND score.boost_starts_at <= CURRENT_TIMESTAMP
          AND score.boost_ends_at > CURRENT_TIMESTAMP
          AND product.type = 'digital_file'::public.product_type
          AND product.is_active = true
          AND product.status = 'Published'
          AND shop.is_active = true
          AND shop.user_id <> p_user_id

        UNION ALL

        SELECT
            score.id,
            score.content_type::text,
            score.content_id,
            score.shop_id,
            product.id,
            score.boost_ends_at
        FROM public.content_discovery_scores AS score
        JOIN public.courses AS course
          ON score.content_type = 'course'
         AND course.id = score.content_id
        JOIN public.products AS product ON product.id = course.product_id
        JOIN public.shops AS shop ON shop.id = product.shop_id
        WHERE score.boost_enabled = true
          AND score.boost_credit_remaining > 0
          AND score.boost_starts_at <= CURRENT_TIMESTAMP
          AND score.boost_ends_at > CURRENT_TIMESTAMP
          AND product.type = 'course'::public.product_type
          AND product.is_active = true
          AND product.status = 'Published'
          AND shop.is_active = true
          AND shop.user_id <> p_user_id
    ),
    eligible AS (
        SELECT candidate.*
        FROM active_content AS candidate
        WHERE NOT EXISTS (
            SELECT 1
            FROM public.user_library AS library
            WHERE library.user_id = p_user_id
              AND library.product_id = candidate.candidate_product_id
        )
          AND NOT EXISTS (
              SELECT 1
              FROM public.user_discovery_feedback AS feedback
              WHERE feedback.user_id = p_user_id
                AND feedback.is_active = true
                AND (
                    feedback.expires_at IS NULL OR
                    feedback.expires_at > CURRENT_TIMESTAMP
                )
                AND (
                    (
                        feedback.feedback_type = 'not_interested' AND
                        feedback.content_type = candidate.candidate_content_type AND
                        feedback.content_id = candidate.candidate_content_id
                    )
                    OR
                    (
                        feedback.feedback_type = 'hide_shop' AND
                        feedback.shop_id = candidate.candidate_shop_id
                    )
                )
          )
    )
    SELECT
        eligible.candidate_boost_id,
        eligible.candidate_content_type,
        eligible.candidate_content_id,
        eligible.candidate_shop_id
    FROM eligible
    ORDER BY
        eligible.boost_ends_at ASC,
        md5(
            p_user_id::text ||
            eligible.candidate_boost_id::text ||
            CURRENT_DATE::text)
    LIMIT p_limit;
END;
$function$;

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
    p_metadata jsonb,
    p_is_sponsored boolean,
    p_boost_id uuid)
RETURNS TABLE(event_record_id uuid, was_inserted boolean)
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = public, pg_temp
AS $function$
DECLARE
    inserted_event_id uuid;
    inserted boolean;
BEGIN
    IF COALESCE(p_is_sponsored, false) <> (p_boost_id IS NOT NULL) THEN
        RAISE EXCEPTION 'Discovery sponsored event shape is invalid'
            USING ERRCODE = '23514';
    END IF;

    SELECT result.event_record_id, result.was_inserted
    INTO inserted_event_id, inserted
    FROM public.record_discovery_event(
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
        p_metadata) AS result;

    IF p_is_sponsored THEN
        IF NOT EXISTS (
            SELECT 1
            FROM public.discovery_events AS event
            WHERE event.id = inserted_event_id
              AND event.user_id = p_user_id
              AND event.feed_session_id = p_feed_session_id
              AND event.tracking_token_id = p_tracking_token_id
              AND event.content_type = p_content_type
              AND event.content_id = p_content_id
              AND event.shop_id = p_shop_id
              AND event.event_type = p_event_type
              AND event.position = p_position
        ) THEN
            RAISE EXCEPTION 'Discovery sponsored event context is invalid'
                USING ERRCODE = '23514';
        END IF;

        IF NOT EXISTS (
            SELECT 1
            FROM public.content_discovery_scores AS score
            WHERE score.id = p_boost_id
              AND score.content_type = p_content_type
              AND score.content_id = p_content_id
              AND score.shop_id = p_shop_id
        ) THEN
            RAISE EXCEPTION 'Discovery boost token context is invalid'
                USING ERRCODE = '23514';
        END IF;

        UPDATE public.discovery_events AS event
        SET
            is_sponsored = true,
            boost_id = p_boost_id
        WHERE event.id = inserted_event_id;

        IF inserted AND p_event_type = 'impression' THEN
            UPDATE public.content_discovery_scores AS score
            SET
                boost_credit_remaining = score.boost_credit_remaining - 1,
                boost_enabled = (score.boost_credit_remaining - 1) > 0,
                boost_updated_at = CURRENT_TIMESTAMP
            WHERE score.id = p_boost_id
              AND score.boost_enabled = true
              AND score.boost_credit_remaining > 0
              AND score.boost_starts_at <= CURRENT_TIMESTAMP
              AND score.boost_ends_at > CURRENT_TIMESTAMP;
        END IF;
    END IF;

    RETURN QUERY SELECT inserted_event_id, inserted;
END;
$function$;

REVOKE ALL ON FUNCTION public.set_discovery_boost(
    uuid, text, uuid, integer, timestamptz, timestamptz)
FROM PUBLIC;
REVOKE ALL ON FUNCTION public.stop_discovery_boost(uuid, uuid)
FROM PUBLIC;
REVOKE ALL ON FUNCTION public.get_admin_discovery_boosts(uuid)
FROM PUBLIC;
REVOKE ALL ON FUNCTION public.get_sponsored_discovery_candidates(uuid, integer)
FROM PUBLIC;
REVOKE ALL ON FUNCTION public.record_discovery_event(
    uuid, uuid, uuid, uuid, text, uuid, uuid, text,
    integer, text, integer, numeric, integer, jsonb, boolean, uuid)
FROM PUBLIC;

DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'craftora_app') THEN
        GRANT EXECUTE ON FUNCTION public.set_discovery_boost(
            uuid, text, uuid, integer, timestamptz, timestamptz)
        TO craftora_app;
        GRANT EXECUTE ON FUNCTION public.stop_discovery_boost(uuid, uuid)
        TO craftora_app;
        GRANT EXECUTE ON FUNCTION public.get_admin_discovery_boosts(uuid)
        TO craftora_app;
        GRANT EXECUTE ON FUNCTION public.get_sponsored_discovery_candidates(uuid, integer)
        TO craftora_app;
        GRANT EXECUTE ON FUNCTION public.record_discovery_event(
            uuid, uuid, uuid, uuid, text, uuid, uuid, text,
            integer, text, integer, numeric, integer, jsonb, boolean, uuid)
        TO craftora_app;
    END IF;
END
$$;

COMMIT;
