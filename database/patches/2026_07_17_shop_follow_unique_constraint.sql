-- 2026-07-17
-- Follow relation integrity: one user can follow a shop at most once.
-- Run manually as the database admin before deploying the API change.

BEGIN;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conrelid = 'subscriptions'::regclass
          AND contype = 'u'
          AND conkey = ARRAY[
              (SELECT attnum FROM pg_attribute WHERE attrelid = 'subscriptions'::regclass AND attname = 'shop_id'),
              (SELECT attnum FROM pg_attribute WHERE attrelid = 'subscriptions'::regclass AND attname = 'user_id')
          ]
    ) THEN
        IF EXISTS (
            SELECT 1
            FROM pg_class
            WHERE oid = 'unique_subscription'::regclass
        ) THEN
            ALTER TABLE subscriptions
                ADD CONSTRAINT subscriptions_shop_id_user_id_key
                UNIQUE USING INDEX unique_subscription;
        ELSE
            ALTER TABLE subscriptions
                ADD CONSTRAINT subscriptions_shop_id_user_id_key UNIQUE (shop_id, user_id);
        END IF;
    END IF;
END;
$$;

-- Repair historical counter drift. The follow relation remains the source of truth.
UPDATE shops AS shop
SET follower_count = (
    SELECT COUNT(*)::integer
    FROM subscriptions AS subscription
    WHERE subscription.shop_id = shop.id
);

COMMIT;
