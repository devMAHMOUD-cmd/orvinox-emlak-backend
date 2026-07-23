-- Craftora Katman 1 Tamir Seansi 2
-- Tarih: 2026-07-23
-- Bulgu kaynagi: schema review (canli DB dump + backend kod incelemesi)
-- Kapsam: schema drift, eksik UNIQUE yapilari ve cleanup mekanizmalari
-- Not: __EFMigrationsHistory grant daraltmasi migration akisi nedeniyle dahil edilmedi.

BEGIN;

-- === 1. orders.order_number UNIQUE ===

DO $$
DECLARE
  duplicate_group_count integer;
BEGIN
  SELECT COUNT(*)
  INTO duplicate_group_count
  FROM (
    SELECT order_number
    FROM public.orders
    GROUP BY order_number
    HAVING COUNT(*) > 1
  ) duplicates;

  IF duplicate_group_count > 0 THEN
    RAISE EXCEPTION
      'orders tablosunda cift order_number var (% grup), once temizle',
      duplicate_group_count;
  END IF;

  IF NOT EXISTS (
    SELECT 1
    FROM pg_constraint
    WHERE conrelid = 'public.orders'::regclass
      AND conname = 'orders_order_number_key'
  ) THEN
    ALTER TABLE public.orders
      ADD CONSTRAINT orders_order_number_key UNIQUE (order_number);
  END IF;
END $$;

-- === 2. shops.slug UNIQUE ===

DO $$
DECLARE
  duplicate_group_count integer;
BEGIN
  SELECT COUNT(*)
  INTO duplicate_group_count
  FROM (
    SELECT slug
    FROM public.shops
    GROUP BY slug
    HAVING COUNT(*) > 1
  ) duplicates;

  IF duplicate_group_count > 0 THEN
    RAISE EXCEPTION
      'shops tablosunda cift slug var (% grup), once temizle',
      duplicate_group_count;
  END IF;

  IF NOT EXISTS (
    SELECT 1
    FROM pg_constraint
    WHERE conrelid = 'public.shops'::regclass
      AND conname = 'shops_slug_key'
  ) THEN
    ALTER TABLE public.shops
      ADD CONSTRAINT shops_slug_key UNIQUE (slug);
  END IF;
END $$;

-- === 3. categories.slug UNIQUE ===

DO $$
DECLARE
  duplicate_group_count integer;
BEGIN
  SELECT COUNT(*)
  INTO duplicate_group_count
  FROM (
    SELECT slug
    FROM public.categories
    GROUP BY slug
    HAVING COUNT(*) > 1
  ) duplicates;

  IF duplicate_group_count > 0 THEN
    RAISE EXCEPTION
      'categories tablosunda cift slug var (% grup), once temizle',
      duplicate_group_count;
  END IF;

  IF NOT EXISTS (
    SELECT 1
    FROM pg_constraint
    WHERE conrelid = 'public.categories'::regclass
      AND conname = 'categories_slug_key'
  ) THEN
    ALTER TABLE public.categories
      ADD CONSTRAINT categories_slug_key UNIQUE (slug);
  END IF;
END $$;

-- === 4. user_library (user_id, product_id) UNIQUE ===

DO $$
DECLARE
  duplicate_group_count integer;
BEGIN
  SELECT COUNT(*)
  INTO duplicate_group_count
  FROM (
    SELECT user_id, product_id
    FROM public.user_library
    GROUP BY user_id, product_id
    HAVING COUNT(*) > 1
  ) duplicates;

  IF duplicate_group_count > 0 THEN
    RAISE EXCEPTION
      'user_library tablosunda cift user/product kaydi var (% grup), once temizle',
      duplicate_group_count;
  END IF;

  IF NOT EXISTS (
    SELECT 1
    FROM pg_constraint
    WHERE conrelid = 'public.user_library'::regclass
      AND conname = 'user_library_user_id_product_id_key'
  ) THEN
    ALTER TABLE public.user_library
      ADD CONSTRAINT user_library_user_id_product_id_key
      UNIQUE (user_id, product_id);
  END IF;
END $$;

-- === 5. user_sessions.refresh_token UNIQUE INDEX ===
-- AuthService refresh token'i SHA-256 hex olarak saklar; mevcut degerler 64 karakterdir.

DO $$
DECLARE
  duplicate_group_count integer;
BEGIN
  SELECT COUNT(*)
  INTO duplicate_group_count
  FROM (
    SELECT refresh_token
    FROM public.user_sessions
    WHERE refresh_token IS NOT NULL
    GROUP BY refresh_token
    HAVING COUNT(*) > 1
  ) duplicates;

  IF duplicate_group_count > 0 THEN
    RAISE EXCEPTION
      'user_sessions tablosunda cift refresh_token var (% grup), once temizle',
      duplicate_group_count;
  END IF;
END $$;

CREATE UNIQUE INDEX IF NOT EXISTS user_sessions_refresh_token_key
  ON public.user_sessions (refresh_token);

-- === 6. user_device_tokens (user_id, token) UNIQUE ===
-- NotificationService kayit eslestirmesini user_id + token ile yapar.

DO $$
DECLARE
  duplicate_group_count integer;
BEGIN
  SELECT COUNT(*)
  INTO duplicate_group_count
  FROM (
    SELECT user_id, token
    FROM public.user_device_tokens
    GROUP BY user_id, token
    HAVING COUNT(*) > 1
  ) duplicates;

  IF duplicate_group_count > 0 THEN
    RAISE EXCEPTION
      'user_device_tokens tablosunda cift user/token kaydi var (% grup), once temizle',
      duplicate_group_count;
  END IF;

  IF NOT EXISTS (
    SELECT 1
    FROM pg_constraint
    WHERE conrelid = 'public.user_device_tokens'::regclass
      AND conname = 'user_device_tokens_user_id_token_key'
  ) THEN
    ALTER TABLE public.user_device_tokens
      ADD CONSTRAINT user_device_tokens_user_id_token_key
      UNIQUE (user_id, token);
  END IF;
END $$;

-- === 7b. Login attempt cleanup function ===

CREATE OR REPLACE FUNCTION public.cleanup_old_login_attempts()
RETURNS void
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = public, pg_temp
AS $$
BEGIN
  DELETE FROM public.login_attempts
  WHERE last_attempt_at < CURRENT_TIMESTAMP - INTERVAL '30 days';

  DELETE FROM public.ip_login_attempts
  WHERE last_attempt_at < CURRENT_TIMESTAMP - INTERVAL '30 days';

  DELETE FROM public.login_attempts
  WHERE locked_until IS NOT NULL
    AND locked_until < CURRENT_TIMESTAMP - INTERVAL '1 day';

  DELETE FROM public.ip_login_attempts
  WHERE locked_until IS NOT NULL
    AND locked_until < CURRENT_TIMESTAMP - INTERVAL '1 day';
END;
$$;

-- === 7a. Optional pg_cron schedules ===

DO $$
BEGIN
  IF EXISTS (SELECT 1 FROM pg_extension WHERE extname = 'pg_cron') THEN
    PERFORM cron.schedule(
      'cleanup_shop_visits',
      '0 4 * * *',
      'SELECT public.cleanup_old_shop_visits();'
    );
    PERFORM cron.schedule(
      'cleanup_login_attempts',
      '30 4 * * *',
      'SELECT public.cleanup_old_login_attempts();'
    );
  ELSE
    RAISE NOTICE
      'pg_cron extension bulunamadi; cleanup fonksiyonlari manuel/dis cron ile cagrilmali';
  END IF;
END $$;

-- === 8. __EFMigrationsHistory grants ===
-- Bilerek atlandi: Program.cs pending migration bulursa runtime connection ile
-- Database.MigrateAsync() cagiriyor. Runtime write grant'lerini kaldirmak deploy'u kirabilir.

COMMIT;
