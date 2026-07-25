\set ON_ERROR_STOP on

BEGIN;

CREATE TABLE IF NOT EXISTS public.seller_subscription_plans
(
    id uuid PRIMARY KEY DEFAULT uuid_generate_v4(),
    code varchar(50) NOT NULL UNIQUE,
    name varchar(100) NOT NULL,
    description text,
    monthly_amount numeric(12,2) NOT NULL,
    currency varchar(3) NOT NULL DEFAULT 'USD',
    commission_rate numeric(6,5) NOT NULL,
    features text[] NOT NULL DEFAULT ARRAY[]::text[],
    sort_order integer NOT NULL DEFAULT 0,
    is_active boolean NOT NULL DEFAULT true,
    created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT seller_subscription_plans_amount_positive CHECK (monthly_amount > 0),
    CONSTRAINT seller_subscription_plans_commission_range CHECK (
        commission_rate >= 0 AND commission_rate <= 1
    )
);

INSERT INTO public.seller_subscription_plans
(
    id,
    code,
    name,
    description,
    monthly_amount,
    currency,
    commission_rate,
    features,
    sort_order,
    is_active
)
VALUES
(
    '10000000-0000-4000-8000-000000000005',
    'starter',
    'Başlangıç',
    'Düşük aylık ücretle satışa başlamak isteyen mağazalar için.',
    5.00,
    'USD',
    0.20000,
    ARRAY[
        'Mağaza açma ve yönetme',
        'Dijital ürün ve kurs satışı',
        'Satış başına %20 komisyon'
    ],
    10,
    true
),
(
    '10000000-0000-4000-8000-000000000025',
    'professional',
    'Profesyonel',
    'Daha düşük satış komisyonu isteyen aktif mağazalar için.',
    25.00,
    'USD',
    0.02000,
    ARRAY[
        'Mağaza açma ve yönetme',
        'Dijital ürün ve kurs satışı',
        'Satış başına %2 komisyon'
    ],
    20,
    true
)
ON CONFLICT (code) DO UPDATE
SET
    name = EXCLUDED.name,
    description = EXCLUDED.description,
    monthly_amount = EXCLUDED.monthly_amount,
    currency = EXCLUDED.currency,
    commission_rate = EXCLUDED.commission_rate,
    features = EXCLUDED.features,
    sort_order = EXCLUDED.sort_order,
    is_active = EXCLUDED.is_active,
    updated_at = CURRENT_TIMESTAMP;

CREATE INDEX IF NOT EXISTS idx_seller_subscription_plans_active_sort
    ON public.seller_subscription_plans (is_active, sort_order);

ALTER TABLE public.seller_subscription_plans ENABLE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS seller_subscription_plans_select_active
    ON public.seller_subscription_plans;

CREATE POLICY seller_subscription_plans_select_active
    ON public.seller_subscription_plans
    FOR SELECT
    USING (is_active = true);

ALTER TABLE public.seller_subscriptions
    ADD COLUMN IF NOT EXISTS plan_id uuid;

UPDATE public.seller_subscriptions
SET plan_id = CASE
    WHEN amount = 5.00
        THEN '10000000-0000-4000-8000-000000000005'::uuid
    ELSE '10000000-0000-4000-8000-000000000025'::uuid
END
WHERE plan_id IS NULL;

ALTER TABLE public.seller_subscriptions
    ALTER COLUMN plan_id SET NOT NULL;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'seller_subscriptions_plan_id_fkey'
          AND conrelid = 'public.seller_subscriptions'::regclass
    ) THEN
        ALTER TABLE public.seller_subscriptions
            ADD CONSTRAINT seller_subscriptions_plan_id_fkey
            FOREIGN KEY (plan_id)
            REFERENCES public.seller_subscription_plans(id)
            ON DELETE RESTRICT;
    END IF;
END
$$;

CREATE INDEX IF NOT EXISTS idx_seller_subscriptions_plan_id
    ON public.seller_subscriptions (plan_id);

ALTER TABLE public.seller_subscription_payments
    ADD COLUMN IF NOT EXISTS plan_id uuid,
    ADD COLUMN IF NOT EXISTS commission_rate numeric(6,5);

UPDATE public.seller_subscription_payments AS payment
SET
    plan_id = subscription.plan_id,
    commission_rate = plan.commission_rate
FROM public.seller_subscriptions AS subscription
JOIN public.seller_subscription_plans AS plan
  ON plan.id = subscription.plan_id
WHERE payment.subscription_id = subscription.id
  AND (payment.plan_id IS NULL OR payment.commission_rate IS NULL);

ALTER TABLE public.seller_subscription_payments
    ALTER COLUMN plan_id SET NOT NULL,
    ALTER COLUMN commission_rate SET NOT NULL;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'seller_subscription_payments_plan_id_fkey'
          AND conrelid = 'public.seller_subscription_payments'::regclass
    ) THEN
        ALTER TABLE public.seller_subscription_payments
            ADD CONSTRAINT seller_subscription_payments_plan_id_fkey
            FOREIGN KEY (plan_id)
            REFERENCES public.seller_subscription_plans(id)
            ON DELETE RESTRICT;
    END IF;
END
$$;

ALTER TABLE public.seller_subscription_payments
    DROP CONSTRAINT IF EXISTS seller_subscription_payments_commission_range;

ALTER TABLE public.seller_subscription_payments
    ADD CONSTRAINT seller_subscription_payments_commission_range
    CHECK (commission_rate >= 0 AND commission_rate <= 1);

CREATE INDEX IF NOT EXISTS idx_seller_subscription_payments_plan_id
    ON public.seller_subscription_payments (plan_id);

ALTER TABLE public.orders
    ADD COLUMN IF NOT EXISTS subscription_plan_id uuid,
    ADD COLUMN IF NOT EXISTS commission_rate numeric(6,5);

UPDATE public.orders
SET commission_rate = CASE
    WHEN amount > 0 AND platform_fee IS NOT NULL
        THEN LEAST(GREATEST(platform_fee / amount, 0), 1)
    ELSE NULL
END
WHERE commission_rate IS NULL;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'orders_subscription_plan_id_fkey'
          AND conrelid = 'public.orders'::regclass
    ) THEN
        ALTER TABLE public.orders
            ADD CONSTRAINT orders_subscription_plan_id_fkey
            FOREIGN KEY (subscription_plan_id)
            REFERENCES public.seller_subscription_plans(id)
            ON DELETE RESTRICT;
    END IF;
END
$$;

ALTER TABLE public.orders
    DROP CONSTRAINT IF EXISTS orders_commission_rate_range;

ALTER TABLE public.orders
    ADD CONSTRAINT orders_commission_rate_range
    CHECK (commission_rate IS NULL OR (commission_rate >= 0 AND commission_rate <= 1));

CREATE INDEX IF NOT EXISTS idx_orders_subscription_plan_id
    ON public.orders (subscription_plan_id)
    WHERE subscription_plan_id IS NOT NULL;

ALTER TABLE public.payments
    ADD COLUMN IF NOT EXISTS subscription_plan_id uuid,
    ADD COLUMN IF NOT EXISTS commission_rate numeric(6,5);

UPDATE public.payments AS payment
SET
    subscription_plan_id = order_row.subscription_plan_id,
    commission_rate = order_row.commission_rate
FROM public.orders AS order_row
WHERE payment.order_id = order_row.id
  AND (
      payment.subscription_plan_id IS NULL
      OR payment.commission_rate IS NULL
  );

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'payments_subscription_plan_id_fkey'
          AND conrelid = 'public.payments'::regclass
    ) THEN
        ALTER TABLE public.payments
            ADD CONSTRAINT payments_subscription_plan_id_fkey
            FOREIGN KEY (subscription_plan_id)
            REFERENCES public.seller_subscription_plans(id)
            ON DELETE RESTRICT;
    END IF;
END
$$;

ALTER TABLE public.payments
    DROP CONSTRAINT IF EXISTS payments_commission_rate_range;

ALTER TABLE public.payments
    ADD CONSTRAINT payments_commission_rate_range
    CHECK (commission_rate IS NULL OR (commission_rate >= 0 AND commission_rate <= 1));

CREATE INDEX IF NOT EXISTS idx_payments_subscription_plan_id
    ON public.payments (subscription_plan_id)
    WHERE subscription_plan_id IS NOT NULL;

CREATE OR REPLACE FUNCTION public.get_shop_commission_snapshot(p_shop_id uuid)
RETURNS TABLE(plan_id uuid, commission_rate numeric)
LANGUAGE sql
STABLE
SECURITY DEFINER
SET search_path = public, pg_temp
AS $function$
    SELECT subscription.plan_id, plan.commission_rate
    FROM public.seller_subscriptions AS subscription
    JOIN public.seller_subscription_plans AS plan
      ON plan.id = subscription.plan_id
    JOIN public.shops AS shop
      ON shop.id = subscription.shop_id
    WHERE subscription.shop_id = p_shop_id
      AND subscription.status = 'active'::sub_status
      AND subscription.current_period_end > CURRENT_TIMESTAMP
      AND shop.is_active = true
    LIMIT 1;
$function$;

CREATE OR REPLACE FUNCTION public.expire_seller_subscriptions()
RETURNS TABLE(
    subscription_id uuid,
    shop_id uuid,
    user_id uuid,
    product_ids uuid[],
    media_ids uuid[]
)
LANGUAGE sql
VOLATILE
SECURITY DEFINER
SET search_path = public, pg_temp
AS $function$
    WITH candidates AS MATERIALIZED (
        SELECT
            subscription.id AS subscription_id,
            subscription.shop_id,
            shop.user_id,
            ARRAY(
                SELECT product.id
                FROM public.products AS product
                WHERE product.shop_id = subscription.shop_id
            ) AS product_ids,
            ARRAY(
                SELECT media.id
                FROM public.media AS media
                WHERE media.shop_id = subscription.shop_id
            ) AS media_ids
        FROM public.seller_subscriptions AS subscription
        JOIN public.shops AS shop
          ON shop.id = subscription.shop_id
        WHERE subscription.status IN ('active'::sub_status, 'past_due'::sub_status)
          AND subscription.current_period_end <= CURRENT_TIMESTAMP
        FOR UPDATE OF subscription, shop
    ),
    updated_subscriptions AS (
        UPDATE public.seller_subscriptions AS subscription
        SET
            status = 'unpaid'::sub_status,
            grace_period_end = NULL,
            updated_at = CURRENT_TIMESTAMP
        FROM candidates
        WHERE subscription.id = candidates.subscription_id
        RETURNING subscription.id
    ),
    updated_shops AS (
        UPDATE public.shops AS shop
        SET
            is_active = false,
            updated_at = CURRENT_TIMESTAMP
        FROM candidates
        WHERE shop.id = candidates.shop_id
        RETURNING shop.id
    )
    SELECT
        candidates.subscription_id,
        candidates.shop_id,
        candidates.user_id,
        candidates.product_ids,
        candidates.media_ids
    FROM candidates
    JOIN updated_subscriptions
      ON updated_subscriptions.id = candidates.subscription_id
    JOIN updated_shops
      ON updated_shops.id = candidates.shop_id;
$function$;

REVOKE ALL ON FUNCTION public.get_shop_commission_snapshot(uuid) FROM PUBLIC;
REVOKE ALL ON FUNCTION public.expire_seller_subscriptions() FROM PUBLIC;
REVOKE ALL ON public.seller_subscription_plans FROM PUBLIC;

DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'craftora_app') THEN
        GRANT SELECT ON public.seller_subscription_plans TO craftora_app;
        GRANT EXECUTE ON FUNCTION public.get_shop_commission_snapshot(uuid) TO craftora_app;
        GRANT EXECUTE ON FUNCTION public.expire_seller_subscriptions() TO craftora_app;
    END IF;
END
$$;

DROP TRIGGER IF EXISTS set_seller_subscription_plans_updated_at
    ON public.seller_subscription_plans;

CREATE TRIGGER set_seller_subscription_plans_updated_at
BEFORE UPDATE ON public.seller_subscription_plans
FOR EACH ROW
EXECUTE FUNCTION public.update_updated_at_column();

COMMIT;
