BEGIN;

DROP POLICY IF EXISTS orders_insert_buyer ON public.orders;
CREATE POLICY orders_insert_buyer
ON public.orders
FOR INSERT
TO craftora_app
WITH CHECK (
    buyer_id =
        NULLIF(current_setting('app.current_user_id', true), '')::uuid
);

DROP POLICY IF EXISTS orders_update_buyer ON public.orders;
CREATE POLICY orders_update_buyer
ON public.orders
FOR UPDATE
TO craftora_app
USING (
    buyer_id =
        NULLIF(current_setting('app.current_user_id', true), '')::uuid
)
WITH CHECK (
    buyer_id =
        NULLIF(current_setting('app.current_user_id', true), '')::uuid
);

DROP POLICY IF EXISTS payments_insert_buyer_order ON public.payments;
CREATE POLICY payments_insert_buyer_order
ON public.payments
FOR INSERT
TO craftora_app
WITH CHECK (
    EXISTS (
        SELECT 1
        FROM public.orders
        WHERE orders.id = payments.order_id
          AND orders.buyer_id =
              NULLIF(current_setting('app.current_user_id', true), '')::uuid
    )
);

CREATE OR REPLACE FUNCTION public.set_order_invoice_url(
    p_order_id UUID,
    p_invoice_pdf_url TEXT)
RETURNS BOOLEAN
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = public, pg_temp
AS $$
BEGIN
    UPDATE public.orders
    SET invoice_pdf_url = p_invoice_pdf_url,
        updated_at = CURRENT_TIMESTAMP
    WHERE id = p_order_id;

    RETURN FOUND;
END;
$$;

REVOKE ALL
ON FUNCTION public.set_order_invoice_url(UUID, TEXT)
FROM PUBLIC;

GRANT EXECUTE
ON FUNCTION public.set_order_invoice_url(UUID, TEXT)
TO craftora_app;

COMMIT;
