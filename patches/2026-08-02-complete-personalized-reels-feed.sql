BEGIN;

CREATE OR REPLACE FUNCTION public.get_complete_personalized_media_candidates(
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
    WITH primary_ranked AS (
        SELECT
            candidate.media_id AS candidate_media_id,
            candidate.shop_id AS candidate_shop_id,
            candidate.ranking_score AS candidate_score,
            candidate.ranking_reason AS candidate_reason
        FROM public.get_personalized_media_candidates(
            p_user_id,
            500) AS candidate
    ),
    fallback_ranked AS (
        SELECT
            medium.id AS candidate_media_id,
            medium.shop_id AS candidate_shop_id,
            (
                (
                    COALESCE(medium.like_count, 0)::numeric +
                    COALESCE(medium.save_count, 0)::numeric * 2.0 +
                    COALESCE(medium.comment_count, 0)::numeric * 1.5 +
                    COALESCE(medium.share_count, 0)::numeric * 1.5 +
                    1.6
                ) /
                (COALESCE(medium.view_count, 0)::numeric + 20.0) * 4.0
                +
                EXP(
                    -GREATEST(
                        EXTRACT(EPOCH FROM (
                            CURRENT_TIMESTAMP -
                            COALESCE(medium.created_at, CURRENT_TIMESTAMP)
                        )) / 86400.0,
                        0
                    ) / 30.0
                ) * 0.70
                +
                LN(
                    1.0 +
                    COALESCE(medium.like_count, 0)::numeric +
                    COALESCE(medium.save_count, 0)::numeric +
                    COALESCE(medium.comment_count, 0)::numeric +
                    COALESCE(medium.share_count, 0)::numeric
                ) * 0.12
                - CASE WHEN shop.user_id = p_user_id THEN 0.45 ELSE 0 END
                - CASE WHEN EXISTS (
                    SELECT 1
                    FROM public.user_library AS library
                    WHERE library.user_id = p_user_id
                      AND library.product_id = medium.product_id
                ) THEN 0.25 ELSE 0 END
                +
                (
                    hashtextextended(
                        medium.id::text || p_user_id::text || CURRENT_DATE::text,
                        0
                    ) & 1023
                )::numeric / 1000000.0
            )::numeric AS candidate_score,
            CASE
                WHEN shop.user_id = p_user_id THEN 'own_shop_fallback'
                WHEN EXISTS (
                    SELECT 1
                    FROM public.user_library AS library
                    WHERE library.user_id = p_user_id
                      AND library.product_id = medium.product_id
                ) THEN 'purchased_fallback'
                WHEN medium.created_at >= CURRENT_TIMESTAMP - INTERVAL '7 days'
                    THEN 'fresh_fallback'
                ELSE 'popular_fallback'
            END AS candidate_reason
        FROM public.media AS medium
        JOIN public.shops AS shop ON shop.id = medium.shop_id
        LEFT JOIN public.products AS product ON product.id = medium.product_id
        WHERE medium.is_active = true
          AND medium.status = 'ready'::public.media_status
          AND shop.is_active = true
          AND (
              medium.product_id IS NULL OR
              (product.is_active = true AND product.status = 'Published')
          )
          AND NOT EXISTS (
              SELECT 1
              FROM primary_ranked AS ranked
              WHERE ranked.candidate_media_id = medium.id
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
    complete_ranked AS (
        SELECT * FROM primary_ranked
        UNION ALL
        SELECT * FROM fallback_ranked
    )
    SELECT
        candidate.candidate_media_id,
        candidate.candidate_shop_id,
        ROUND(candidate.candidate_score, 6),
        candidate.candidate_reason
    FROM complete_ranked AS candidate
    ORDER BY
        candidate.candidate_score DESC,
        candidate.candidate_media_id
    LIMIT p_limit;
END;
$function$;

REVOKE ALL ON FUNCTION public.get_complete_personalized_media_candidates(
    uuid,
    integer)
FROM PUBLIC;

DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'craftora_app') THEN
        GRANT EXECUTE ON FUNCTION
            public.get_complete_personalized_media_candidates(uuid, integer)
        TO craftora_app;
    END IF;
END
$$;

COMMIT;
