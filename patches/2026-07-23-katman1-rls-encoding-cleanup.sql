-- Craftora Katman 1 RLS encoding cleanup
-- Yalnizca belirtilen tablolardaki mojibake policy isimlerini siler.

BEGIN;

DO $$
DECLARE
  policy_record record;
BEGIN
  FOR policy_record IN
    SELECT schemaname, tablename, policyname
    FROM pg_policies
    WHERE schemaname = 'public'
      AND (
        (
          tablename = 'cart_items'
          AND policyname LIKE '%' || chr(196) || '%'
          AND policyname LIKE '%sepet%'
        )
        OR (
          tablename = 'notifications'
          AND policyname LIKE '%' || chr(196) || '%'
          AND policyname LIKE '%bildirim%'
        )
        OR (
          tablename = 'users'
          AND policyname LIKE '%' || chr(196) || '%'
          AND policyname LIKE '%herkes%'
        )
        OR (
          tablename = 'user_device_tokens'
          AND policyname LIKE '%' || chr(196) || '%'
          AND policyname LIKE '%cihaz%'
        )
      )
  LOOP
    EXECUTE format(
      'DROP POLICY IF EXISTS %I ON %I.%I',
      policy_record.policyname,
      policy_record.schemaname,
      policy_record.tablename
    );
  END LOOP;
END $$;

COMMIT;
