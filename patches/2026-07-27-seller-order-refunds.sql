BEGIN;

ALTER TABLE public.orders
    ADD COLUMN IF NOT EXISTS refunded_at timestamp with time zone,
    ADD COLUMN IF NOT EXISTS refund_reason text,
    ADD COLUMN IF NOT EXISTS refunded_by uuid;

ALTER TABLE public.payments
    ADD COLUMN IF NOT EXISTS provider_refund_id character varying(255),
    ADD COLUMN IF NOT EXISTS refunded_at timestamp with time zone;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conrelid = 'public.orders'::regclass
          AND conname = 'orders_refunded_by_fkey'
    ) THEN
        ALTER TABLE public.orders
            ADD CONSTRAINT orders_refunded_by_fkey
            FOREIGN KEY (refunded_by)
            REFERENCES public.users(id)
            ON DELETE SET NULL;
    END IF;
END
$$;

CREATE INDEX IF NOT EXISTS idx_orders_refunded_by
    ON public.orders(refunded_by)
    WHERE refunded_by IS NOT NULL;

CREATE UNIQUE INDEX IF NOT EXISTS payments_provider_refund_id_key
    ON public.payments(provider_refund_id)
    WHERE provider_refund_id IS NOT NULL;

CREATE OR REPLACE FUNCTION public.lock_seller_refundable_order(
    p_order_id uuid
)
RETURNS SETOF public.orders
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = public, pg_temp
AS $$
DECLARE
    v_order_id uuid;
BEGIN
    SELECT order_row.id
    INTO v_order_id
    FROM public.orders AS order_row
    JOIN public.shops AS shop ON shop.id = order_row.shop_id
    WHERE order_row.id = p_order_id
      AND order_row.status = 'completed'
      AND shop.user_id =
          NULLIF(current_setting('app.current_user_id', true), '')::uuid
    FOR UPDATE OF order_row;

    IF v_order_id IS NULL THEN
        RETURN;
    END IF;

    PERFORM 1
    FROM public.payments AS payment
    WHERE payment.order_id = v_order_id
      AND payment.status = 'succeeded'
    FOR UPDATE;

    IF NOT FOUND THEN
        RETURN;
    END IF;

    RETURN QUERY
    SELECT order_row.*
    FROM public.orders AS order_row
    WHERE order_row.id = v_order_id;
END;
$$;

CREATE OR REPLACE FUNCTION public.complete_seller_order_refund(
    p_order_id uuid,
    p_reason text,
    p_provider_refund_id text
)
RETURNS boolean
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = public, pg_temp
AS $$
DECLARE
    v_buyer_id uuid;
    v_current_user_id uuid;
BEGIN
    v_current_user_id :=
        NULLIF(current_setting('app.current_user_id', true), '')::uuid;

    IF v_current_user_id IS NULL
       OR NULLIF(BTRIM(p_reason), '') IS NULL
       OR CHAR_LENGTH(BTRIM(p_reason)) > 500
       OR NULLIF(BTRIM(p_provider_refund_id), '') IS NULL THEN
        RETURN false;
    END IF;

    SELECT order_row.buyer_id
    INTO v_buyer_id
    FROM public.orders AS order_row
    JOIN public.shops AS shop ON shop.id = order_row.shop_id
    WHERE order_row.id = p_order_id
      AND order_row.status = 'completed'
      AND shop.user_id = v_current_user_id
    FOR UPDATE OF order_row;

    IF v_buyer_id IS NULL THEN
        RETURN false;
    END IF;

    UPDATE public.payments
    SET
        status = 'refunded',
        provider_refund_id = BTRIM(p_provider_refund_id),
        refunded_at = CURRENT_TIMESTAMP,
        updated_at = CURRENT_TIMESTAMP
    WHERE order_id = p_order_id
      AND status = 'succeeded';

    IF NOT FOUND THEN
        RETURN false;
    END IF;

    UPDATE public.orders
    SET
        status = 'refunded',
        refunded_at = CURRENT_TIMESTAMP,
        refund_reason = BTRIM(p_reason),
        refunded_by = v_current_user_id,
        updated_at = CURRENT_TIMESTAMP
    WHERE id = p_order_id;

    IF EXISTS (
        SELECT 1
        FROM public.point_logs
        WHERE user_id = v_buyer_id
          AND action_type = 'purchase_product'
          AND reference_id = p_order_id
    )
    AND NOT EXISTS (
        SELECT 1
        FROM public.point_logs
        WHERE user_id = v_buyer_id
          AND action_type = 'refund_purchase'
          AND reference_id = p_order_id
    ) THEN
        INSERT INTO public.point_logs (
            user_id,
            action_type,
            points_earned,
            reference_id
        )
        VALUES (
            v_buyer_id,
            'refund_purchase',
            -5.0,
            p_order_id
        );

        UPDATE public.user_points
        SET
            total_points = GREATEST(total_points - 5.0, 0),
            updated_at = CURRENT_TIMESTAMP
        WHERE user_id = v_buyer_id;
    END IF;

    RETURN true;
END;
$$;

REVOKE ALL ON FUNCTION public.lock_seller_refundable_order(uuid) FROM PUBLIC;
REVOKE ALL ON FUNCTION public.lock_seller_refundable_order(uuid) FROM craftora_app;
GRANT EXECUTE ON FUNCTION public.lock_seller_refundable_order(uuid) TO craftora_app;

REVOKE ALL ON FUNCTION public.complete_seller_order_refund(uuid, text, text) FROM PUBLIC;
REVOKE ALL ON FUNCTION public.complete_seller_order_refund(uuid, text, text) FROM craftora_app;
GRANT EXECUTE ON FUNCTION public.complete_seller_order_refund(uuid, text, text) TO craftora_app;

COMMIT;
