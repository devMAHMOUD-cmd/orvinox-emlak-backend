BEGIN;

CREATE OR REPLACE FUNCTION public.upsert_discovery_affinity(
    p_user_id uuid,
    p_affinity_type text,
    p_affinity_key text,
    p_signal_weight numeric,
    p_signal_at timestamptz)
RETURNS void
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = public, pg_temp
AS $function$
BEGIN
    IF p_user_id IS NULL OR
       p_affinity_type NOT IN ('category', 'shop', 'content_type') OR
       NULLIF(BTRIM(p_affinity_key), '') IS NULL OR
       p_signal_weight = 0 THEN
        RETURN;
    END IF;

    INSERT INTO public.user_discovery_affinities AS affinity (
        user_id,
        affinity_type,
        affinity_key,
        score,
        signal_count,
        last_signal_at,
        updated_at)
    VALUES (
        p_user_id,
        p_affinity_type,
        p_affinity_key,
        p_signal_weight,
        1,
        p_signal_at,
        CURRENT_TIMESTAMP)
    ON CONFLICT (user_id, affinity_type, affinity_key)
    DO UPDATE SET
        score =
            affinity.score *
            EXP(
                -LN(10.0) *
                GREATEST(
                    EXTRACT(EPOCH FROM (
                        EXCLUDED.last_signal_at -
                        COALESCE(affinity.last_signal_at, affinity.updated_at)
                    )) / 86400.0,
                    0
                ) / 30.0
            ) +
            EXCLUDED.score,
        signal_count = affinity.signal_count + 1,
        last_signal_at = EXCLUDED.last_signal_at,
        updated_at = CURRENT_TIMESTAMP;
END;
$function$;

CREATE OR REPLACE FUNCTION public.update_discovery_affinities_from_event()
RETURNS trigger
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = public, pg_temp
AS $function$
DECLARE
    signal_weight numeric;
    category_id uuid;
    media_duration integer;
    fast_skip_threshold_ms integer;
BEGIN
    IF NEW.user_id IS NULL OR
       NEW.event_type IN ('not_interested', 'hide_shop') THEN
        RETURN NEW;
    END IF;

    signal_weight := CASE NEW.event_type
        WHEN 'impression' THEN 0.02
        WHEN 'playback_started' THEN 0.05
        WHEN 'playback_progress' THEN
            LEAST(COALESCE(NEW.completion_rate, 0), 1) * 0.30
        WHEN 'playback_completed' THEN 1.20
        WHEN 'looped' THEN 1.50
        WHEN 'content_opened' THEN 0.70
        ELSE 0
    END;

    IF NEW.event_type = 'playback_ended' AND NEW.content_type = 'media' THEN
        SELECT COALESCE(medium.duration_seconds, 15)
        INTO media_duration
        FROM public.media AS medium
        WHERE medium.id = NEW.content_id;

        fast_skip_threshold_ms :=
            ROUND(GREATEST(3, LEAST(8, COALESCE(media_duration, 15) * 0.13)) * 1000);
        signal_weight := CASE
            WHEN COALESCE(NEW.dwell_ms, 0) < fast_skip_threshold_ms THEN -0.40
            ELSE LEAST(COALESCE(NEW.completion_rate, 0), 1) * 0.20
        END;
    END IF;

    IF signal_weight = 0 THEN
        RETURN NEW;
    END IF;

    IF NEW.content_type = 'media' THEN
        SELECT product.category_id
        INTO category_id
        FROM public.media AS medium
        JOIN public.products AS product ON product.id = medium.product_id
        WHERE medium.id = NEW.content_id;
    ELSIF NEW.content_type = 'product' THEN
        SELECT product.category_id
        INTO category_id
        FROM public.products AS product
        WHERE product.id = NEW.content_id;
    ELSIF NEW.content_type = 'course' THEN
        SELECT product.category_id
        INTO category_id
        FROM public.courses AS course
        JOIN public.products AS product ON product.id = course.product_id
        WHERE course.id = NEW.content_id;
    END IF;

    PERFORM public.upsert_discovery_affinity(
        NEW.user_id,
        'shop',
        NEW.shop_id::text,
        signal_weight,
        NEW.created_at);
    PERFORM public.upsert_discovery_affinity(
        NEW.user_id,
        'content_type',
        NEW.content_type,
        signal_weight,
        NEW.created_at);

    IF category_id IS NOT NULL THEN
        PERFORM public.upsert_discovery_affinity(
            NEW.user_id,
            'category',
            category_id::text,
            signal_weight,
            NEW.created_at);
    END IF;

    RETURN NEW;
END;
$function$;

DROP TRIGGER IF EXISTS discovery_events_update_affinities
ON public.discovery_events;
CREATE TRIGGER discovery_events_update_affinities
AFTER INSERT ON public.discovery_events
FOR EACH ROW
EXECUTE FUNCTION public.update_discovery_affinities_from_event();

CREATE OR REPLACE FUNCTION public.get_personalized_media_candidates(
    p_user_id uuid,
    p_limit integer DEFAULT 500)
RETURNS TABLE(
    media_id uuid,
    shop_id uuid,
    ranking_score numeric,
    ranking_reason text)
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
        RAISE EXCEPTION 'Discovery ranking user mismatch'
            USING ERRCODE = '42501';
    END IF;

    IF p_limit IS NULL OR p_limit < 1 OR p_limit > 500 THEN
        RAISE EXCEPTION 'Discovery candidate limit is invalid'
            USING ERRCODE = '23514';
    END IF;

    RETURN QUERY
    WITH event_stats AS (
        SELECT
            event.content_id,
            COUNT(*) FILTER (
                WHERE event.event_type = 'impression'
            )::numeric AS impression_count,
            COUNT(*) FILTER (
                WHERE event.event_type = 'playback_completed'
            )::numeric AS completion_count,
            COUNT(*) FILTER (
                WHERE event.event_type = 'looped'
            )::numeric AS loop_count,
            COUNT(*) FILTER (
                WHERE
                    event.event_type = 'playback_ended' AND
                    COALESCE(event.dwell_ms, 0) <
                    ROUND(
                        GREATEST(
                            3,
                            LEAST(
                                8,
                                COALESCE(medium.duration_seconds, 15) * 0.13
                            )
                        ) * 1000
                    )
            )::numeric AS fast_skip_count
        FROM public.discovery_events AS event
        JOIN public.media AS medium
          ON medium.id = event.content_id
         AND event.content_type = 'media'
        WHERE event.created_at >= CURRENT_TIMESTAMP - INTERVAL '90 days'
        GROUP BY event.content_id
    ),
    relational_category_signals AS (
        SELECT signal.category_id, SUM(signal.weight)::numeric AS score
        FROM (
            SELECT product.category_id, 0.35::numeric AS weight
            FROM public.media_likes AS media_like
            JOIN public.media AS medium ON medium.id = media_like.media_id
            JOIN public.products AS product ON product.id = medium.product_id
            WHERE media_like.user_id = p_user_id

            UNION ALL

            SELECT product.category_id, 0.60::numeric AS weight
            FROM public.media_saves AS media_save
            JOIN public.media AS medium ON medium.id = media_save.media_id
            JOIN public.products AS product ON product.id = medium.product_id
            WHERE media_save.user_id = p_user_id

            UNION ALL

            SELECT product.category_id, 0.12::numeric AS weight
            FROM public.media_watch_history AS watch
            JOIN public.media AS medium ON medium.id = watch.media_id
            JOIN public.products AS product ON product.id = medium.product_id
            WHERE watch.user_id = p_user_id

            UNION ALL

            SELECT product.category_id, 0.45::numeric AS weight
            FROM public.user_library AS library
            JOIN public.products AS product ON product.id = library.product_id
            WHERE library.user_id = p_user_id
        ) AS signal
        GROUP BY signal.category_id
    ),
    eligible AS (
        SELECT
            medium.id AS candidate_media_id,
            medium.shop_id AS candidate_shop_id,
            product.category_id,
            COALESCE(medium.view_count, 0)::numeric AS view_count,
            COALESCE(medium.like_count, 0)::numeric AS like_count,
            COALESCE(medium.save_count, 0)::numeric AS save_count,
            COALESCE(medium.comment_count, 0)::numeric AS comment_count,
            COALESCE(medium.share_count, 0)::numeric AS share_count,
            COALESCE(medium.created_at, CURRENT_TIMESTAMP) AS created_at,
            COALESCE(stats.impression_count, 0) AS impression_count,
            COALESCE(stats.completion_count, 0) AS completion_count,
            COALESCE(stats.loop_count, 0) AS loop_count,
            COALESCE(stats.fast_skip_count, 0) AS fast_skip_count,
            COALESCE(shop_affinity.score, 0)::numeric AS shop_affinity,
            COALESCE(category_affinity.score, 0)::numeric AS category_affinity,
            COALESCE(type_affinity.score, 0)::numeric AS type_affinity,
            COALESCE(category_signal.score, 0)::numeric AS relational_category_score,
            EXISTS (
                SELECT 1
                FROM public.subscriptions AS subscription
                WHERE subscription.user_id = p_user_id
                  AND subscription.shop_id = medium.shop_id
            ) AS follows_shop
        FROM public.media AS medium
        JOIN public.shops AS shop ON shop.id = medium.shop_id
        LEFT JOIN public.products AS product ON product.id = medium.product_id
        LEFT JOIN event_stats AS stats ON stats.content_id = medium.id
        LEFT JOIN public.user_discovery_affinities AS shop_affinity
          ON shop_affinity.user_id = p_user_id
         AND shop_affinity.affinity_type = 'shop'
         AND shop_affinity.affinity_key = medium.shop_id::text
        LEFT JOIN public.user_discovery_affinities AS category_affinity
          ON category_affinity.user_id = p_user_id
         AND category_affinity.affinity_type = 'category'
         AND category_affinity.affinity_key = product.category_id::text
        LEFT JOIN public.user_discovery_affinities AS type_affinity
          ON type_affinity.user_id = p_user_id
         AND type_affinity.affinity_type = 'content_type'
         AND type_affinity.affinity_key = 'media'
        LEFT JOIN relational_category_signals AS category_signal
          ON category_signal.category_id = product.category_id
        WHERE medium.is_active = true
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
          AND NOT EXISTS (
              SELECT 1
              FROM public.user_library AS library
              WHERE library.user_id = p_user_id
                AND library.product_id = medium.product_id
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
                        feedback.content_type = 'media' AND
                        feedback.content_id = medium.id
                    )
                    OR
                    (
                        feedback.feedback_type = 'hide_shop' AND
                        feedback.shop_id = medium.shop_id
                    )
                )
          )
    ),
    scored AS (
        SELECT
            eligible.candidate_media_id,
            eligible.candidate_shop_id,
            (
                (
                    eligible.like_count +
                    eligible.save_count * 2.0 +
                    eligible.comment_count * 1.5 +
                    eligible.share_count * 1.5 +
                    eligible.completion_count * 2.0 +
                    eligible.loop_count * 2.5 +
                    1.6
                ) /
                (
                    GREATEST(
                        eligible.view_count,
                        eligible.impression_count
                    ) + 20.0
                ) * 4.0
                +
                EXP(
                    -GREATEST(
                        EXTRACT(EPOCH FROM (
                            CURRENT_TIMESTAMP - eligible.created_at
                        )) / 86400.0,
                        0
                    ) / 30.0
                ) * 0.70
                +
                LN(
                    1.0 +
                    eligible.like_count +
                    eligible.save_count +
                    eligible.comment_count +
                    eligible.share_count
                ) * 0.12
                +
                LEAST(5, GREATEST(-5, eligible.shop_affinity)) * 0.35
                +
                LEAST(5, GREATEST(-5, eligible.category_affinity)) * 0.50
                +
                LEAST(5, GREATEST(-5, eligible.type_affinity)) * 0.15
                +
                LEAST(5, eligible.relational_category_score) * 0.25
                +
                CASE WHEN eligible.follows_shop THEN 1.25 ELSE 0 END
                -
                (
                    eligible.fast_skip_count /
                    (eligible.impression_count + 5.0)
                ) * 0.80
                +
                (
                    hashtextextended(
                        eligible.candidate_media_id::text ||
                        p_user_id::text ||
                        CURRENT_DATE::text,
                        0
                    ) & 1023
                )::numeric / 1000000.0
            )::numeric AS score,
            CASE
                WHEN eligible.follows_shop THEN 'followed_shop'
                WHEN eligible.category_affinity > 0 OR
                     eligible.relational_category_score > 0
                    THEN 'category_affinity'
                WHEN eligible.completion_count > 0 OR
                     eligible.loop_count > 0
                    THEN 'engagement_quality'
                WHEN eligible.created_at >= CURRENT_TIMESTAMP - INTERVAL '7 days'
                    THEN 'fresh'
                ELSE 'popular'
            END AS reason
        FROM eligible
    )
    SELECT
        scored.candidate_media_id,
        scored.candidate_shop_id,
        ROUND(scored.score, 6),
        scored.reason
    FROM scored
    ORDER BY
        scored.score DESC,
        scored.candidate_media_id
    LIMIT p_limit;
END;
$function$;

REVOKE ALL ON FUNCTION public.upsert_discovery_affinity(
    uuid, text, text, numeric, timestamptz)
FROM PUBLIC;
REVOKE ALL ON FUNCTION public.update_discovery_affinities_from_event()
FROM PUBLIC;
REVOKE ALL ON FUNCTION public.get_personalized_media_candidates(uuid, integer)
FROM PUBLIC;

DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'craftora_app') THEN
        GRANT EXECUTE ON FUNCTION public.get_personalized_media_candidates(
            uuid,
            integer)
        TO craftora_app;
    END IF;
END
$$;

COMMIT;
