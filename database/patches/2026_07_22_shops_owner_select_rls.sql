-- Allow owners to read their own inactive shop while keeping inactive shops private.
BEGIN;

DROP POLICY IF EXISTS shops_select_owner ON shops;

CREATE POLICY shops_select_owner
ON shops
FOR SELECT
USING (
    is_active = TRUE
    OR user_id = NULLIF(NULLIF(current_setting('app.current_user_id', true), ''), '')::uuid
);

COMMIT;
