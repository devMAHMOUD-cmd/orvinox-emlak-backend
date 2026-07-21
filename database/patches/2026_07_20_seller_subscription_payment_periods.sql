-- 2026-07-20
-- Adds verified billing-period identity to the subscription payment ledger.
-- Run manually with the admin/superuser database role after the initial ledger patch.
-- Legacy subscriptions deliberately remain without fabricated payment rows or periods.

BEGIN;

ALTER TABLE seller_subscription_payments
    ADD COLUMN IF NOT EXISTS billing_period_start TIMESTAMP WITH TIME ZONE,
    ADD COLUMN IF NOT EXISTS billing_period_end TIMESTAMP WITH TIME ZONE;

ALTER TABLE seller_subscription_payments
    DROP CONSTRAINT IF EXISTS check_seller_subscription_payment_period;

ALTER TABLE seller_subscription_payments
    ADD CONSTRAINT check_seller_subscription_payment_period
    CHECK (
        billing_period_start IS NULL OR
        billing_period_end IS NULL OR
        billing_period_end >= billing_period_start
    );

CREATE UNIQUE INDEX IF NOT EXISTS uq_seller_subscription_payments_subscription_period
    ON seller_subscription_payments(subscription_id, billing_period_start, billing_period_end)
    WHERE billing_period_start IS NOT NULL AND billing_period_end IS NOT NULL;

COMMIT;
