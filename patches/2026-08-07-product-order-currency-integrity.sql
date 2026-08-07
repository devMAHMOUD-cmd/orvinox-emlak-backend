BEGIN;

CREATE OR REPLACE FUNCTION pg_temp.try_product_currency(metadata_value jsonb)
RETURNS text
LANGUAGE plpgsql
AS $$
DECLARE
    parsed_currency text;
BEGIN
    IF metadata_value IS NULL OR jsonb_typeof(metadata_value) <> 'object' THEN
        RETURN NULL;
    END IF;

    parsed_currency := upper(btrim(metadata_value ->> 'currency'));
    IF parsed_currency IN ('TRY', 'TL') THEN
        RETURN 'TRY';
    END IF;
    IF parsed_currency = 'USD' THEN
        RETURN 'USD';
    END IF;
    RETURN NULL;
END;
$$;

CREATE TEMP TABLE corrected_product_currencies ON COMMIT DROP AS
SELECT
    product.id,
    product.currency AS old_currency,
    pg_temp.try_product_currency(product.metadata) AS correct_currency,
    coalesce(product.updated_at, product.created_at, '-infinity'::timestamptz) AS currency_effective_at
FROM public.products AS product
WHERE pg_temp.try_product_currency(product.metadata) IS NOT NULL
  AND upper(coalesce(product.currency, '')) IS DISTINCT FROM
      pg_temp.try_product_currency(product.metadata);

UPDATE public.products AS product
SET
    currency = correction.correct_currency,
    updated_at = CURRENT_TIMESTAMP
FROM corrected_product_currencies AS correction
WHERE product.id = correction.id;

UPDATE public.orders AS customer_order
SET
    currency = correction.correct_currency,
    updated_at = CURRENT_TIMESTAMP
FROM corrected_product_currencies AS correction
WHERE customer_order.product_id = correction.id
  AND coalesce(customer_order.created_at, '-infinity'::timestamptz) >= correction.currency_effective_at
  AND upper(coalesce(customer_order.currency, '')) IS NOT DISTINCT FROM
      upper(coalesce(correction.old_currency, ''));

DO $$
BEGIN
    IF EXISTS (
        SELECT 1
        FROM public.products AS product
        WHERE pg_temp.try_product_currency(product.metadata) IS NOT NULL
          AND upper(coalesce(product.currency, '')) IS DISTINCT FROM
              pg_temp.try_product_currency(product.metadata)
    ) THEN
        RAISE EXCEPTION 'Product currency correction verification failed.';
    END IF;
END;
$$;

COMMIT;
