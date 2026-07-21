-- 2026-07-20
-- Immutable successful/failed seller subscription payment history for admin finance reporting.
-- Run manually with the admin/superuser database role before deploying this feature.

BEGIN;

CREATE TABLE IF NOT EXISTS seller_subscription_payments (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    subscription_id UUID NOT NULL REFERENCES seller_subscriptions(id) ON DELETE RESTRICT,
    shop_id UUID NOT NULL REFERENCES shops(id) ON DELETE RESTRICT,
    payment_provider VARCHAR(50) NOT NULL,
    provider_transaction_id VARCHAR(255),
    amount DECIMAL(12,2) NOT NULL CHECK (amount >= 0),
    currency VARCHAR(3) NOT NULL,
    status VARCHAR(30) NOT NULL CHECK (status IN ('succeeded', 'failed', 'refunded')),
    billing_period_start TIMESTAMP WITH TIME ZONE,
    billing_period_end TIMESTAMP WITH TIME ZONE,
    created_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT check_seller_subscription_payment_period CHECK (
        billing_period_start IS NULL OR
        billing_period_end IS NULL OR
        billing_period_end >= billing_period_start
    )
);

CREATE INDEX IF NOT EXISTS idx_seller_subscription_payments_subscription_date
    ON seller_subscription_payments(subscription_id, created_at DESC);

CREATE INDEX IF NOT EXISTS idx_seller_subscription_payments_status_date
    ON seller_subscription_payments(status, created_at DESC);

CREATE UNIQUE INDEX IF NOT EXISTS uq_seller_subscription_payments_provider_transaction
    ON seller_subscription_payments(payment_provider, provider_transaction_id)
    WHERE provider_transaction_id IS NOT NULL;

CREATE UNIQUE INDEX IF NOT EXISTS uq_seller_subscription_payments_subscription_period
    ON seller_subscription_payments(subscription_id, billing_period_start, billing_period_end)
    WHERE billing_period_start IS NOT NULL AND billing_period_end IS NOT NULL;

COMMIT;
