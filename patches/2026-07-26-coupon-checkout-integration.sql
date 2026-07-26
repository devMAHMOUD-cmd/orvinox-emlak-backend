BEGIN;

ALTER TABLE public.orders
    ADD COLUMN IF NOT EXISTS subtotal_amount numeric(10,2),
    ADD COLUMN IF NOT EXISTS discount_amount numeric(10,2);

UPDATE public.orders
SET
    subtotal_amount = COALESCE(subtotal_amount, amount),
    discount_amount = COALESCE(discount_amount, 0)
WHERE subtotal_amount IS NULL
   OR discount_amount IS NULL;

ALTER TABLE public.orders
    ALTER COLUMN subtotal_amount SET NOT NULL,
    ALTER COLUMN discount_amount SET NOT NULL,
    ALTER COLUMN discount_amount SET DEFAULT 0;

ALTER TABLE public.orders
    DROP CONSTRAINT IF EXISTS orders_subtotal_nonnegative,
    DROP CONSTRAINT IF EXISTS orders_discount_nonnegative,
    DROP CONSTRAINT IF EXISTS orders_discount_not_above_subtotal,
    DROP CONSTRAINT IF EXISTS orders_amount_matches_discount;

ALTER TABLE public.orders
    ADD CONSTRAINT orders_subtotal_nonnegative
        CHECK (subtotal_amount >= 0),
    ADD CONSTRAINT orders_discount_nonnegative
        CHECK (discount_amount >= 0),
    ADD CONSTRAINT orders_discount_not_above_subtotal
        CHECK (discount_amount <= subtotal_amount),
    ADD CONSTRAINT orders_amount_matches_discount
        CHECK (amount = subtotal_amount - discount_amount);

ALTER TABLE public.coupons ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.coupon_uses ENABLE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS coupons_select_available_or_owner ON public.coupons;
CREATE POLICY coupons_select_available_or_owner
ON public.coupons
FOR SELECT
TO craftora_app
USING (
    is_active = true
    OR shop_id IN (
        SELECT shops.id
        FROM public.shops
        WHERE shops.user_id =
            NULLIF(current_setting('app.current_user_id', true), '')::uuid
    )
);

DROP POLICY IF EXISTS coupons_insert_owner_or_admin ON public.coupons;
CREATE POLICY coupons_insert_owner_or_admin
ON public.coupons
FOR INSERT
TO craftora_app
WITH CHECK (
    (
        shop_id IN (
            SELECT shops.id
            FROM public.shops
            WHERE shops.user_id =
                NULLIF(current_setting('app.current_user_id', true), '')::uuid
        )
        AND EXISTS (
            SELECT 1
            FROM public.products
            WHERE products.id = coupons.product_id
              AND products.shop_id = coupons.shop_id
        )
    )
    OR EXISTS (
        SELECT 1
        FROM public.users
        WHERE users.id =
            NULLIF(current_setting('app.current_user_id', true), '')::uuid
          AND users.role::text = 'admin'
          AND users.is_active = true
    )
);

DROP POLICY IF EXISTS coupon_uses_select_own ON public.coupon_uses;
CREATE POLICY coupon_uses_select_own
ON public.coupon_uses
FOR SELECT
TO craftora_app
USING (
    user_id = NULLIF(current_setting('app.current_user_id', true), '')::uuid
);

DROP POLICY IF EXISTS "KullanÄ±cÄ± kendi adÄ±na kupon kullanabilir" ON public.coupon_uses;
DROP POLICY IF EXISTS "Kullanıcı kendi adına kupon kullanabilir" ON public.coupon_uses;
DROP POLICY IF EXISTS coupon_uses_insert_own ON public.coupon_uses;
CREATE POLICY coupon_uses_insert_own
ON public.coupon_uses
FOR INSERT
TO craftora_app
WITH CHECK (
    user_id = NULLIF(current_setting('app.current_user_id', true), '')::uuid
);

COMMIT;
