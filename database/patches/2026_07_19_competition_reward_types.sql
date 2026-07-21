-- 2026-07-19
-- Competition reward types, certificate storage reference and duplicate protection.
-- Run manually with an admin/superuser database role before deploying this feature.

BEGIN;

ALTER TABLE admin_competition_rewards
    ADD COLUMN IF NOT EXISTS certificate_url TEXT;

ALTER TABLE admin_competition_rewards
    DROP CONSTRAINT IF EXISTS check_admin_competition_rewards_type;

ALTER TABLE admin_competition_rewards
    ADD CONSTRAINT check_admin_competition_rewards_type
    CHECK (reward_type IN ('money', 'premium_1_month', 'certificate'));

CREATE UNIQUE INDEX IF NOT EXISTS uq_admin_competition_rewards_contest_user
    ON admin_competition_rewards (contest_id, user_id);

COMMIT;
