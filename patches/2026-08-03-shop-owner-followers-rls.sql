BEGIN;

CREATE OR REPLACE FUNCTION public.can_shop_owner_view_subscription(p_shop_id uuid)
RETURNS boolean
LANGUAGE plpgsql
STABLE
SECURITY DEFINER
SET search_path = public, pg_temp
AS $$
DECLARE
    current_user_id_text text;
    current_user_id uuid;
BEGIN
    current_user_id_text := current_setting('app.current_user_id', true);

    IF current_user_id_text IS NULL OR btrim(current_user_id_text) = '' THEN
        RETURN false;
    END IF;

    BEGIN
        current_user_id := current_user_id_text::uuid;
    EXCEPTION
        WHEN invalid_text_representation THEN
            RETURN false;
    END;

    RETURN EXISTS (
        SELECT 1
        FROM public.shops AS shop
        WHERE shop.id = p_shop_id
          AND shop.user_id = current_user_id
    );
END;
$$;

REVOKE ALL ON FUNCTION public.can_shop_owner_view_subscription(uuid) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION public.can_shop_owner_view_subscription(uuid) TO craftora_app;

DROP POLICY IF EXISTS subscriptions_select_shop_owner
ON public.subscriptions;

CREATE POLICY subscriptions_select_shop_owner
ON public.subscriptions
FOR SELECT
TO craftora_app
USING (public.can_shop_owner_view_subscription(shop_id));

COMMIT;
