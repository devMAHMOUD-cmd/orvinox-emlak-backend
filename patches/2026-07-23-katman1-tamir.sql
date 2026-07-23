-- Craftora Katman 1 Tamir Seansi
-- Tarih: 2026-07-23
-- Uygulama: docker exec -i postgres_server psql -U postgres -d craftora_db < patch.sql
-- Rollback: /backups/craftora_2026-07-23.sql.gz yedeginden geri yukleme
--
-- Tum degisiklikler tek transaction icindedir. Bir hata olursa rollback olur.

BEGIN;

-- === GRUP A: Missing constraints ===

DO $$
BEGIN
  IF EXISTS (
    SELECT 1
    FROM public.users
    GROUP BY email
    HAVING COUNT(*) > 1
  ) THEN
    RAISE EXCEPTION 'A1 iptal: users tablosunda cift email var, once temizle';
  END IF;

  IF NOT EXISTS (
    SELECT 1
    FROM pg_constraint
    WHERE conrelid = 'public.users'::regclass
      AND conname = 'users_email_key'
  ) THEN
    ALTER TABLE public.users
      ADD CONSTRAINT users_email_key UNIQUE (email);
  END IF;
END $$;

CREATE UNIQUE INDEX IF NOT EXISTS users_auth_provider_provider_id_key
  ON public.users (auth_provider, provider_id)
  WHERE provider_id IS NOT NULL;

DO $$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'products_price_nonnegative') THEN
    ALTER TABLE public.products
      ADD CONSTRAINT products_price_nonnegative CHECK (price >= 0);
  END IF;
  IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'products_rating_average_range') THEN
    ALTER TABLE public.products
      ADD CONSTRAINT products_rating_average_range CHECK (rating_average BETWEEN 0 AND 5);
  END IF;
  IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'products_counts_nonnegative') THEN
    ALTER TABLE public.products
      ADD CONSTRAINT products_counts_nonnegative CHECK (review_count >= 0 AND sales_count >= 0);
  END IF;
  IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'media_counts_nonnegative') THEN
    ALTER TABLE public.media
      ADD CONSTRAINT media_counts_nonnegative CHECK (
        view_count >= 0 AND like_count >= 0 AND save_count >= 0
        AND comment_count >= 0 AND share_count >= 0
      );
  END IF;
END $$;

ALTER TABLE public.products
  ALTER COLUMN tags SET DEFAULT '{}'::text[];

ALTER TABLE public.users
  ALTER COLUMN created_at SET NOT NULL,
  ALTER COLUMN updated_at SET NOT NULL,
  ALTER COLUMN is_active SET NOT NULL,
  ALTER COLUMN is_email_verified SET NOT NULL;

-- === GRUP B: Missing FK indexes ===

CREATE INDEX IF NOT EXISTS idx_shops_user_id ON public.shops (user_id);
CREATE INDEX IF NOT EXISTS idx_payments_order_id ON public.payments (order_id);
CREATE INDEX IF NOT EXISTS idx_media_likes_media_id ON public.media_likes (media_id);
CREATE INDEX IF NOT EXISTS idx_media_saves_media_id ON public.media_saves (media_id);
CREATE INDEX IF NOT EXISTS idx_media_watch_history_user_id ON public.media_watch_history (user_id);
CREATE INDEX IF NOT EXISTS idx_seller_subscriptions_shop_id ON public.seller_subscriptions (shop_id);
CREATE INDEX IF NOT EXISTS idx_seller_subscription_payments_shop_id ON public.seller_subscription_payments (shop_id);
CREATE INDEX IF NOT EXISTS idx_reviews_product_id ON public.reviews (product_id);
CREATE INDEX IF NOT EXISTS idx_admin_audit_logs_admin_user_id ON public.admin_audit_logs (admin_user_id);
CREATE INDEX IF NOT EXISTS idx_admin_competition_rewards_user_id ON public.admin_competition_rewards (user_id);
CREATE INDEX IF NOT EXISTS idx_admin_reports_reported_by_user_id ON public.admin_reports (reported_by_user_id);
CREATE INDEX IF NOT EXISTS idx_admin_warnings_admin_user_id ON public.admin_warnings (admin_user_id);
CREATE INDEX IF NOT EXISTS idx_coupon_uses_coupon_id ON public.coupon_uses (coupon_id);
CREATE INDEX IF NOT EXISTS idx_support_ticket_messages_sender_id ON public.support_ticket_messages (sender_id);
CREATE INDEX IF NOT EXISTS idx_support_tickets_closed_by_user_id ON public.support_tickets (closed_by_user_id);
CREATE INDEX IF NOT EXISTS idx_contest_results_contest_id ON public.contest_results (contest_id);
CREATE INDEX IF NOT EXISTS idx_user_lesson_progress_user_id ON public.user_lesson_progress (user_id);

-- === GRUP C: Function security ===

DO $$
BEGIN
  IF to_regprocedure('public.increment_coupon_usage()') IS NOT NULL THEN
    ALTER FUNCTION public.increment_coupon_usage() SECURITY DEFINER;
    ALTER FUNCTION public.increment_coupon_usage() SET search_path = public, pg_temp;
  END IF;

  IF to_regprocedure('public.sync_follower_count()') IS NOT NULL THEN
    ALTER FUNCTION public.sync_follower_count() SECURITY DEFINER;
    ALTER FUNCTION public.sync_follower_count() SET search_path = public, pg_temp;
  END IF;
END $$;

-- === GRUP D: RLS policy cleanup ===

DROP POLICY IF EXISTS "KullanÃ„Â±cÃ„Â± kendi sepetini yÃƒÂ¶netebilir" ON public.cart_items;
DROP POLICY IF EXISTS "KullanÃ„Â±cÃ„Â± bildirimini okundu yapabilir" ON public.notifications;
DROP POLICY IF EXISTS "KullanÃ„Â±cÃ„Â± kendi bildirimlerini gÃƒÂ¶rebilir" ON public.notifications;
DROP POLICY IF EXISTS "Aktif kullanÃ„Â±cÃ„Â±larÃ„Â± herkes gÃƒÂ¶rebilir" ON public.users;
DROP POLICY IF EXISTS "KullanÃ„Â±cÃ„Â± kendi cihazlarÃ„Â±nÃ„Â± yÃƒÂ¶netebilir" ON public.user_device_tokens;

-- === GRUP E: Duplicate RLS policies ===

DROP POLICY IF EXISTS "seller_course_lessons_manage" ON public.course_lessons;
DROP POLICY IF EXISTS "seller_course_sections_manage" ON public.course_sections;

-- === GRUP F: Homepage seed data ===

INSERT INTO public.home_cards (id, title, description, icon, action_type, sort_order, is_active)
VALUES
  ('welcome-creator', 'Sahneni yukari tasi', 'Urunlerini ve yetenegini Craftora vitriniyle bulustur.', 'sparkles', 'shop', 10, true),
  ('discover-courses', 'Yeni bir beceri kesfet', 'Craftora Akademi ile ogrenmeye bugun basla.', 'book-open', 'course', 20, true),
  ('watch-reels', 'Trend icerikleri izle', 'Ureticilerin yeni reels videolarini kesfet.', 'play', 'reel', 30, true)
ON CONFLICT (id) DO NOTHING;

INSERT INTO public.pulse_news (id, title, description, meta, icon, is_published, is_new_until)
VALUES
  ('00000000-0000-0000-0000-000000000101', 'Craftora''ya hos geldin', 'Uret, paylas ve dijital vitrininle yeni kitlelere ulas.', 'Baslangic', 'sparkles', true, CURRENT_TIMESTAMP + INTERVAL '30 days'),
  ('00000000-0000-0000-0000-000000000102', 'Vitrinini guclendir', 'Magazana urun, kurs ve reels ekleyerek sahneni olustur.', 'Uretici ipucu', 'storefront', true, CURRENT_TIMESTAMP + INTERVAL '30 days'),
  ('00000000-0000-0000-0000-000000000103', 'Kesfetmeye basla', 'Trend icerikleri izle, yeni beceriler ogren ve ilham al.', 'Craftora Akademi', 'play', true, CURRENT_TIMESTAMP + INTERVAL '30 days')
ON CONFLICT (id) DO NOTHING;

COMMIT;
