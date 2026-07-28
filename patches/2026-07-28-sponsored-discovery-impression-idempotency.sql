BEGIN;

CREATE UNIQUE INDEX IF NOT EXISTS
    discovery_events_sponsored_impression_token_key
ON public.discovery_events (tracking_token_id)
WHERE is_sponsored = true
  AND event_type = 'impression';

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

    IF p_is_sponsored AND p_event_type = 'impression' THEN
        PERFORM pg_advisory_xact_lock(
            hashtextextended(p_tracking_token_id::text, 0));

        SELECT event.id
        INTO inserted_event_id
        FROM public.discovery_events AS event
        WHERE event.tracking_token_id = p_tracking_token_id
          AND event.user_id = p_user_id
          AND event.feed_session_id = p_feed_session_id
          AND event.content_type = p_content_type
          AND event.content_id = p_content_id
          AND event.shop_id = p_shop_id
          AND event.event_type = 'impression'
          AND event.is_sponsored = true
          AND event.boost_id = p_boost_id;

        IF inserted_event_id IS NOT NULL THEN
            RETURN QUERY SELECT inserted_event_id, false;
            RETURN;
        END IF;
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

REVOKE ALL ON FUNCTION public.record_discovery_event(
    uuid, uuid, uuid, uuid, text, uuid, uuid, text,
    integer, text, integer, numeric, integer, jsonb, boolean, uuid)
FROM PUBLIC;

DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'craftora_app') THEN
        GRANT EXECUTE ON FUNCTION public.record_discovery_event(
            uuid, uuid, uuid, uuid, text, uuid, uuid, text,
            integer, text, integer, numeric, integer, jsonb, boolean, uuid)
        TO craftora_app;
    END IF;
END
$$;

COMMIT;
