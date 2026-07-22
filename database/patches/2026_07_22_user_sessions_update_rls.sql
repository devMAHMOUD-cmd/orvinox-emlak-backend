-- Refresh-token rotation updates the caller's own session row.
BEGIN;

DROP POLICY IF EXISTS sessions_update_own ON user_sessions;

CREATE POLICY sessions_update_own
ON user_sessions
FOR UPDATE
USING (
    user_id = NULLIF(NULLIF(current_setting('app.current_user_id', true), ''), '')::uuid
)
WITH CHECK (
    user_id = NULLIF(NULLIF(current_setting('app.current_user_id', true), ''), '')::uuid
);

COMMIT;
