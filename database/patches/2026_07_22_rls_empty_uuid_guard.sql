-- Prevent anonymous requests from casting an empty RLS setting to uuid.
-- Existing policies are rewritten in place and remain restrictive for users
-- with a valid app.current_user_id.
DO $$
DECLARE
    policy_row record;
    rewritten_using text;
    rewritten_check text;
BEGIN
    FOR policy_row IN
        SELECT
            ns.nspname AS schema_name,
            cls.relname AS table_name,
            pol.polname AS policy_name,
            pg_get_expr(pol.polqual, pol.polrelid) AS using_expression,
            pg_get_expr(pol.polwithcheck, pol.polrelid) AS check_expression
        FROM pg_policy pol
        JOIN pg_class cls ON cls.oid = pol.polrelid
        JOIN pg_namespace ns ON ns.oid = cls.relnamespace
        WHERE ns.nspname = 'public'
          AND (
              pg_get_expr(pol.polqual, pol.polrelid) LIKE '%current_setting(''app.current_user_id''%'
              OR pg_get_expr(pol.polwithcheck, pol.polrelid) LIKE '%current_setting(''app.current_user_id''%'
          )
    LOOP
        rewritten_using := replace(
            policy_row.using_expression,
            'current_setting(''app.current_user_id''::text, true)::uuid',
            'NULLIF(current_setting(''app.current_user_id'', true), '''')::uuid'
        );
        rewritten_check := replace(
            policy_row.check_expression,
            'current_setting(''app.current_user_id''::text, true)::uuid',
            'NULLIF(current_setting(''app.current_user_id'', true), '''')::uuid'
        );

        IF policy_row.using_expression IS NOT NULL
           AND rewritten_using <> policy_row.using_expression THEN
            EXECUTE format(
                'ALTER POLICY %I ON %I.%I USING %s',
                policy_row.policy_name,
                policy_row.schema_name,
                policy_row.table_name,
                rewritten_using
            );
        END IF;

        IF policy_row.check_expression IS NOT NULL
           AND rewritten_check <> policy_row.check_expression THEN
            EXECUTE format(
                'ALTER POLICY %I ON %I.%I WITH CHECK %s',
                policy_row.policy_name,
                policy_row.schema_name,
                policy_row.table_name,
                rewritten_check
            );
        END IF;
    END LOOP;
END $$;
