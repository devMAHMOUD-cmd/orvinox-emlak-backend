BEGIN;

CREATE OR REPLACE FUNCTION public.process_seller_subscription_lifecycle()
RETURNS TABLE(
    transition text,
    subscription_id uuid,
    shop_id uuid,
    user_id uuid,
    product_ids uuid[],
    media_ids uuid[]
)
LANGUAGE plpgsql
VOLATILE
SECURITY DEFINER
SET search_path = public, pg_temp
AS $function$
DECLARE
    candidate record;
    effective_grace_end timestamptz;
BEGIN
    FOR candidate IN
        SELECT
            subscription.id AS subscription_id,
            subscription.shop_id,
            shop.user_id,
            subscription.status,
            subscription.current_period_end,
            subscription.grace_period_end
        FROM public.seller_subscriptions AS subscription
        JOIN public.shops AS shop
          ON shop.id = subscription.shop_id
        WHERE (
                subscription.status = 'active'::sub_status
                AND subscription.current_period_end <= CURRENT_TIMESTAMP
              )
           OR (
                subscription.status = 'past_due'::sub_status
                AND COALESCE(
                    subscription.grace_period_end,
                    subscription.current_period_end + INTERVAL '7 days'
                ) <= CURRENT_TIMESTAMP
              )
        FOR UPDATE OF subscription, shop SKIP LOCKED
    LOOP
        effective_grace_end := COALESCE(
            candidate.grace_period_end,
            candidate.current_period_end + INTERVAL '7 days'
        );

        IF candidate.status = 'active'::sub_status
           AND effective_grace_end > CURRENT_TIMESTAMP THEN
            UPDATE public.seller_subscriptions
            SET
                status = 'past_due'::sub_status,
                grace_period_end = effective_grace_end,
                updated_at = CURRENT_TIMESTAMP
            WHERE id = candidate.subscription_id;

            transition := 'past_due';
            subscription_id := candidate.subscription_id;
            shop_id := candidate.shop_id;
            user_id := candidate.user_id;
            product_ids := ARRAY[]::uuid[];
            media_ids := ARRAY[]::uuid[];
            RETURN NEXT;
            CONTINUE;
        END IF;

        UPDATE public.seller_subscriptions
        SET
            status = 'unpaid'::sub_status,
            grace_period_end = effective_grace_end,
            updated_at = CURRENT_TIMESTAMP
        WHERE id = candidate.subscription_id;

        UPDATE public.shops
        SET
            is_active = false,
            updated_at = CURRENT_TIMESTAMP
        WHERE id = candidate.shop_id;

        UPDATE public.users
        SET
            role = 'user'::user_role,
            updated_at = CURRENT_TIMESTAMP
        WHERE id = candidate.user_id
          AND role = 'seller'::user_role;

        UPDATE public.user_sessions
        SET is_revoked = true
        WHERE user_id = candidate.user_id
          AND is_revoked IS DISTINCT FROM true;

        transition := 'unpaid';
        subscription_id := candidate.subscription_id;
        shop_id := candidate.shop_id;
        user_id := candidate.user_id;
        product_ids := ARRAY(
            SELECT product.id
            FROM public.products AS product
            WHERE product.shop_id = candidate.shop_id
        );
        media_ids := ARRAY(
            SELECT medium.id
            FROM public.media AS medium
            WHERE medium.shop_id = candidate.shop_id
        );
        RETURN NEXT;
    END LOOP;
END;
$function$;

REVOKE ALL ON FUNCTION public.process_seller_subscription_lifecycle() FROM PUBLIC;

DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'craftora_app') THEN
        GRANT EXECUTE
            ON FUNCTION public.process_seller_subscription_lifecycle()
            TO craftora_app;
    END IF;
END
$$;

COMMIT;
