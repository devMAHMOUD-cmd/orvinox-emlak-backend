BEGIN;

CREATE OR REPLACE FUNCTION public.lock_checkout_coupon(
    p_product_id uuid,
    p_code text
)
RETURNS SETOF public.coupons
LANGUAGE sql
SECURITY DEFINER
SET search_path = public, pg_temp
AS $$
    SELECT coupon.*
    FROM public.coupons AS coupon
    WHERE coupon.product_id = p_product_id
      AND coupon.code = p_code
      AND coupon.is_active = true
    FOR UPDATE;
$$;

REVOKE ALL ON FUNCTION public.lock_checkout_coupon(uuid, text) FROM PUBLIC;
REVOKE ALL ON FUNCTION public.lock_checkout_coupon(uuid, text) FROM craftora_app;
GRANT EXECUTE ON FUNCTION public.lock_checkout_coupon(uuid, text) TO craftora_app;

COMMIT;
