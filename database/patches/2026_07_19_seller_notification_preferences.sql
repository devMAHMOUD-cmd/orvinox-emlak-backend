-- =========================================================================
-- Craftora seller notification preferences
-- Date: 2026-07-19
-- Purpose:
--   Stores persistent seller email preferences used by order email and weekly
--   report delivery. Run this patch manually as admin before using the API in
--   environments where the table does not exist yet.
-- =========================================================================

BEGIN;

CREATE TABLE IF NOT EXISTS seller_notification_preferences (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    order_emails BOOLEAN NOT NULL DEFAULT TRUE,
    weekly_report_emails BOOLEAN NOT NULL DEFAULT TRUE,
    created_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT seller_notification_preferences_user_id_key UNIQUE (user_id)
);

CREATE INDEX IF NOT EXISTS idx_seller_notification_preferences_user
    ON seller_notification_preferences(user_id);

DROP TRIGGER IF EXISTS set_seller_notification_preferences_updated_at
    ON seller_notification_preferences;

CREATE TRIGGER set_seller_notification_preferences_updated_at
BEFORE UPDATE ON seller_notification_preferences
FOR EACH ROW EXECUTE FUNCTION update_updated_at_column();

ALTER TABLE seller_notification_preferences ENABLE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS "Seller notification preferences select own"
    ON seller_notification_preferences;
CREATE POLICY "Seller notification preferences select own"
    ON seller_notification_preferences
    FOR SELECT
    USING (user_id = current_setting('app.current_user_id', true)::uuid);

DROP POLICY IF EXISTS "Seller notification preferences insert own"
    ON seller_notification_preferences;
CREATE POLICY "Seller notification preferences insert own"
    ON seller_notification_preferences
    FOR INSERT
    WITH CHECK (user_id = current_setting('app.current_user_id', true)::uuid);

DROP POLICY IF EXISTS "Seller notification preferences update own"
    ON seller_notification_preferences;
CREATE POLICY "Seller notification preferences update own"
    ON seller_notification_preferences
    FOR UPDATE
    USING (user_id = current_setting('app.current_user_id', true)::uuid)
    WITH CHECK (user_id = current_setting('app.current_user_id', true)::uuid);

COMMIT;
