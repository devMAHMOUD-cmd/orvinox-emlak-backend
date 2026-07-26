BEGIN;

CREATE OR REPLACE FUNCTION public.refresh_product_review_stats(
    p_product_id UUID)
RETURNS VOID
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = public, pg_temp
AS $$
BEGIN
    UPDATE public.products
    SET review_count = (
            SELECT COUNT(*)::INTEGER
            FROM public.reviews
            WHERE product_id = p_product_id
        ),
        rating_average = COALESCE((
            SELECT ROUND(AVG(rating)::NUMERIC, 2)
            FROM public.reviews
            WHERE product_id = p_product_id
        ), 0)
    WHERE id = p_product_id;
END;
$$;

CREATE OR REPLACE FUNCTION public.set_review_seller_reply(
    p_review_id UUID,
    p_seller_user_id UUID,
    p_seller_reply TEXT)
RETURNS BOOLEAN
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = public, pg_temp
AS $$
BEGIN
    UPDATE public.reviews review
    SET seller_reply = p_seller_reply,
        updated_at = CURRENT_TIMESTAMP
    FROM public.products product
    JOIN public.shops shop ON shop.id = product.shop_id
    WHERE review.id = p_review_id
      AND product.id = review.product_id
      AND shop.user_id = p_seller_user_id;

    RETURN FOUND;
END;
$$;

REVOKE ALL
ON FUNCTION public.refresh_product_review_stats(UUID)
FROM PUBLIC;

REVOKE ALL
ON FUNCTION public.set_review_seller_reply(UUID, UUID, TEXT)
FROM PUBLIC;

GRANT EXECUTE
ON FUNCTION public.refresh_product_review_stats(UUID)
TO craftora_app;

GRANT EXECUTE
ON FUNCTION public.set_review_seller_reply(UUID, UUID, TEXT)
TO craftora_app;

COMMIT;
