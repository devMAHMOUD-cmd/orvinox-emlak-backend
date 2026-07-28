BEGIN;

-- EnsureCreated represents model-level unique constraints as unique indexes.
-- Historical repair patches expect these names in pg_constraint, so promote
-- matching current-model indexes before those patches run on a clean database.
DO $$
DECLARE
    candidate record;
    index_is_compatible boolean;
BEGIN
    FOR candidate IN
        SELECT *
        FROM (
            VALUES
                ('users', 'users_email_key'),
                ('orders', 'orders_order_number_key'),
                ('shops', 'shops_slug_key'),
                ('categories', 'categories_slug_key'),
                ('user_library', 'user_library_user_id_product_id_key'),
                ('user_device_tokens', 'user_device_tokens_user_id_token_key')
        ) AS candidates(table_name, constraint_name)
    LOOP
        IF EXISTS (
            SELECT 1
            FROM pg_constraint
            WHERE conrelid =
                    format('public.%I', candidate.table_name)::regclass
              AND conname = candidate.constraint_name
        ) THEN
            CONTINUE;
        END IF;

        SELECT index_row.indisunique
               AND index_row.indisvalid
               AND index_row.indpred IS NULL
        INTO index_is_compatible
        FROM pg_index AS index_row
        JOIN pg_class AS index_class
          ON index_class.oid = index_row.indexrelid
        JOIN pg_namespace AS index_namespace
          ON index_namespace.oid = index_class.relnamespace
        WHERE index_namespace.nspname = 'public'
          AND index_class.relname = candidate.constraint_name;

        IF index_is_compatible IS TRUE THEN
            EXECUTE format(
                'ALTER TABLE public.%I ADD CONSTRAINT %I UNIQUE USING INDEX %I',
                candidate.table_name,
                candidate.constraint_name,
                candidate.constraint_name);
        END IF;
    END LOOP;
END
$$;

COMMIT;
