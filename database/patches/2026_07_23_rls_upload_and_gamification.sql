-- Allow authenticated application flows to persist seller media and XP while
-- keeping the runtime role itself subject to RLS.
BEGIN;

CREATE OR REPLACE FUNCTION public.award_points_for_user(
    p_user_id uuid,
    p_action_type text,
    p_points numeric,
    p_reference_id uuid,
    p_prevent_duplicate boolean DEFAULT false
)
RETURNS integer
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = public
AS $$
DECLARE
    inserted_count integer := 0;
BEGIN
    IF p_user_id IS NULL OR p_action_type IS NULL OR btrim(p_action_type) = '' OR p_points <= 0 THEN
        RAISE EXCEPTION 'Invalid gamification award arguments';
    END IF;

    IF NOT EXISTS (SELECT 1 FROM users WHERE id = p_user_id) THEN
        RAISE EXCEPTION 'Gamification user does not exist';
    END IF;

    IF p_prevent_duplicate AND p_reference_id IS NOT NULL THEN
        INSERT INTO point_logs (user_id, action_type, points_earned, reference_id, created_at)
        VALUES (p_user_id, p_action_type, p_points, p_reference_id, timezone('utc', now()))
        ON CONFLICT DO NOTHING;
    ELSE
        INSERT INTO point_logs (user_id, action_type, points_earned, reference_id, created_at)
        VALUES (p_user_id, p_action_type, p_points, p_reference_id, timezone('utc', now()));
    END IF;

    GET DIAGNOSTICS inserted_count = ROW_COUNT;

    IF inserted_count = 1 THEN
        INSERT INTO user_points (user_id, total_points, current_rank, current_streak, updated_at)
        VALUES (p_user_id, p_points, 0, 0, timezone('utc', now()))
        ON CONFLICT (user_id) DO UPDATE
        SET total_points = user_points.total_points + EXCLUDED.total_points,
            updated_at = EXCLUDED.updated_at;
    END IF;

    RETURN inserted_count;
END;
$$;

REVOKE ALL ON FUNCTION public.award_points_for_user(uuid, text, numeric, uuid, boolean) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION public.award_points_for_user(uuid, text, numeric, uuid, boolean) TO craftora_app;

DROP POLICY IF EXISTS media_insert_owner ON media;
CREATE POLICY media_insert_owner ON media FOR INSERT
WITH CHECK (
    EXISTS (
        SELECT 1
        FROM shops
        WHERE shops.id = media.shop_id
          AND shops.user_id = NULLIF(current_setting('app.current_user_id', true), '')::uuid
          AND shops.is_active = true
    )
);

COMMIT;
