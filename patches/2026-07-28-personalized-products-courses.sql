BEGIN;

CREATE OR REPLACE FUNCTION public.get_personalized_product_candidates(
    p_user_id uuid,
    p_content_type text,
    p_limit integer DEFAULT 500)
RETURNS TABLE(
    content_id uuid,
    product_id uuid,
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

    IF p_content_type NOT IN ('product', 'course') OR
       p_limit IS NULL OR p_limit < 1 OR p_limit > 500 THEN
        RAISE EXCEPTION 'Discovery product ranking request is invalid'
            USING ERRCODE = '23514';
    END IF;

    RETURN QUERY
    WITH base_content AS (
        SELECT
            product.id AS candidate_content_id,
            product.id AS candidate_product_id,
            product.shop_id AS candidate_shop_id,
            product.category_id,
            product.rating_average,
            COALESCE(product.review_count, 0)::numeric AS review_count,
            COALESCE(product.sales_count, 0)::numeric AS sales_count,
            product.is_featured,
            COALESCE(product.created_at, CURRENT_TIMESTAMP) AS created_at
        FROM public.products AS product
        JOIN public.shops AS shop ON shop.id = product.shop_id
        WHERE p_content_type = 'product'
          AND product.type = 'digital_file'::public.product_type
          AND product.is_active = true
          AND product.status = 'Published'
          AND shop.is_active = true
          AND shop.user_id <> p_user_id

        UNION ALL

        SELECT
            course.id AS candidate_content_id,
            product.id AS candidate_product_id,
            product.shop_id AS candidate_shop_id,
            product.category_id,
            product.rating_average,
            COALESCE(product.review_count, 0)::numeric AS review_count,
            COALESCE(product.sales_count, 0)::numeric AS sales_count,
            product.is_featured,
            COALESCE(product.created_at, CURRENT_TIMESTAMP) AS created_at
        FROM public.courses AS course
        JOIN public.products AS product ON product.id = course.product_id
        JOIN public.shops AS shop ON shop.id = product.shop_id
        WHERE p_content_type = 'course'
          AND product.type = 'course'::public.product_type
          AND product.is_active = true
          AND product.status = 'Published'
          AND shop.is_active = true
          AND shop.user_id <> p_user_id
    ),
    eligible AS (
        SELECT base.*
        FROM base_content AS base
        WHERE NOT EXISTS (
            SELECT 1
            FROM public.user_library AS library
            WHERE library.user_id = p_user_id
              AND library.product_id = base.candidate_product_id
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
                        feedback.content_type = p_content_type AND
                        feedback.content_id = base.candidate_content_id
                    )
                    OR
                    (
                        feedback.feedback_type = 'hide_shop' AND
                        feedback.shop_id = base.candidate_shop_id
                    )
                )
          )
    ),
    event_stats AS (
        SELECT
            event.content_id AS event_content_id,
            COUNT(*) FILTER (
                WHERE event.event_type = 'impression'
            )::numeric AS impressions,
            COUNT(*) FILTER (
                WHERE event.event_type = 'content_opened'
            )::numeric AS opens
        FROM public.discovery_events AS event
        WHERE event.content_type = p_content_type
          AND event.created_at >= CURRENT_TIMESTAMP - INTERVAL '90 days'
        GROUP BY event.content_id
    ),
    scored AS (
        SELECT
            eligible.*,
            (
                (
                    COALESCE(eligible.rating_average, 4.0) *
                    eligible.review_count +
                    4.0 * 5.0
                ) /
                (eligible.review_count + 5.0) / 5.0 * 1.50
                +
                LN(1.0 + eligible.sales_count) * 0.35
                +
                (
                    (COALESCE(stats.opens, 0) + 2.0) /
                    (COALESCE(stats.impressions, 0) + 20.0)
                ) * 1.20
                +
                EXP(
                    -GREATEST(
                        EXTRACT(EPOCH FROM (
                            CURRENT_TIMESTAMP - eligible.created_at
                        )) / 86400.0,
                        0
                    ) / 45.0
                ) * 0.60
                +
                CASE WHEN eligible.is_featured = true THEN 0.25 ELSE 0 END
                +
                LEAST(5, GREATEST(-5, COALESCE(shop_affinity.score, 0))) * 0.35
                +
                LEAST(5, GREATEST(-5, COALESCE(category_affinity.score, 0))) * 0.55
                +
                LEAST(5, GREATEST(-5, COALESCE(type_affinity.score, 0))) * 0.15
                +
                CASE WHEN subscription.id IS NOT NULL THEN 1.10 ELSE 0 END
                +
                (
                    hashtextextended(
                        eligible.candidate_content_id::text ||
                        p_user_id::text ||
                        CURRENT_DATE::text,
                        0
                    ) & 1023
                )::numeric / 1000000.0
            )::numeric AS raw_score,
            CASE
                WHEN subscription.id IS NOT NULL THEN 'followed_shop'
                WHEN COALESCE(category_affinity.score, 0) > 0
                    THEN 'category_affinity'
                WHEN COALESCE(stats.opens, 0) > 0
                    THEN 'engagement_quality'
                WHEN eligible.created_at >= CURRENT_TIMESTAMP - INTERVAL '14 days'
                    THEN 'fresh'
                ELSE 'popular'
            END AS reason
        FROM eligible
        LEFT JOIN event_stats AS stats
          ON stats.event_content_id = eligible.candidate_content_id
        LEFT JOIN public.user_discovery_affinities AS shop_affinity
          ON shop_affinity.user_id = p_user_id
         AND shop_affinity.affinity_type = 'shop'
         AND shop_affinity.affinity_key = eligible.candidate_shop_id::text
        LEFT JOIN public.user_discovery_affinities AS category_affinity
          ON category_affinity.user_id = p_user_id
         AND category_affinity.affinity_type = 'category'
         AND category_affinity.affinity_key = eligible.category_id::text
        LEFT JOIN public.user_discovery_affinities AS type_affinity
          ON type_affinity.user_id = p_user_id
         AND type_affinity.affinity_type = 'content_type'
         AND type_affinity.affinity_key = p_content_type
        LEFT JOIN public.subscriptions AS subscription
          ON subscription.user_id = p_user_id
         AND subscription.shop_id = eligible.candidate_shop_id
    ),
    diversified AS (
        SELECT
            scored.*,
            scored.raw_score -
            (
                ROW_NUMBER() OVER (
                    PARTITION BY scored.candidate_shop_id
                    ORDER BY scored.raw_score DESC, scored.candidate_content_id
                ) - 1
            ) * 0.35 AS adjusted_score
        FROM scored
    )
    SELECT
        diversified.candidate_content_id,
        diversified.candidate_product_id,
        diversified.candidate_shop_id,
        ROUND(diversified.adjusted_score, 6),
        diversified.reason
    FROM diversified
    ORDER BY
        diversified.adjusted_score DESC,
        diversified.candidate_content_id
    LIMIT p_limit;
END;
$function$;

REVOKE ALL ON FUNCTION public.get_personalized_product_candidates(
    uuid,
    text,
    integer)
FROM PUBLIC;

DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'craftora_app') THEN
        GRANT EXECUTE ON FUNCTION public.get_personalized_product_candidates(
            uuid,
            text,
            integer)
        TO craftora_app;
    END IF;
END
$$;

COMMIT;
