-- =========================================================================
-- CRAFTORA CANONICAL FRESH-INSTALL SCHEMA
-- Date: 2026-07-05
-- Source: Live PostgreSQL schema/data dump captured on 2026-07-05.
-- Purpose: Clean fresh-install schema for Craftora backend.
-- Notes:
--   - User/business COPY data was removed.
--   - Seed data kept only for categories and __EFMigrationsHistory.
--   - RLS test grants and pg_dump restrict markers were removed.
--   - Owner assignments were omitted to avoid requiring a local admin role.
-- =========================================================================

SET statement_timeout = 0;
SET lock_timeout = 0;
SET idle_in_transaction_session_timeout = 0;
SET client_encoding = 'UTF8';
SET standard_conforming_strings = on;
SET check_function_bodies = false;
SET client_min_messages = warning;
SET row_security = off;

BEGIN;


-- =========================================================================
-- EXTENSIONS
-- =========================================================================

CREATE EXTENSION IF NOT EXISTS citext WITH SCHEMA public;

COMMENT ON EXTENSION citext IS 'data type for case-insensitive character strings';

CREATE EXTENSION IF NOT EXISTS "uuid-ossp" WITH SCHEMA public;

COMMENT ON EXTENSION "uuid-ossp" IS 'generate universally unique identifiers (UUIDs)';


-- =========================================================================
-- ENUM TYPES
-- =========================================================================

CREATE TYPE public.analytics_event_type AS ENUM (
    'shop_visit',
    'product_view',
    'media_view',
    'add_to_cart',
    'checkout_started',
    'purchase_completed',
    'download_clicked'
);

CREATE TYPE public.media_status AS ENUM (
    'failed',
    'processing',
    'ready'
);

CREATE TYPE public.order_status AS ENUM (
    'completed',
    'failed',
    'pending',
    'refunded'
);

CREATE TYPE public.payment_status_type AS ENUM (
    'failed',
    'processing',
    'refunded',
    'succeeded'
);

CREATE TYPE public.product_type AS ENUM (
    'course',
    'digital_file'
);

CREATE TYPE public.sub_status AS ENUM (
    'active',
    'canceled',
    'past_due',
    'unpaid'
);

CREATE TYPE public.support_message_sender_role AS ENUM (
    'user',
    'admin'
);

CREATE TYPE public.support_ticket_status AS ENUM (
    'open',
    'answered',
    'closed'
);

CREATE TYPE public.user_role AS ENUM (
    'admin',
    'seller',
    'user'
);


-- =========================================================================
-- TRIGGER FUNCTIONS
-- =========================================================================

CREATE FUNCTION public.award_seller_points() RETURNS trigger
    LANGUAGE plpgsql SECURITY DEFINER
    SET search_path TO 'public'
    AS $$
DECLARE v_seller_id UUID;
BEGIN
    SELECT s.user_id INTO v_seller_id 
    FROM media m JOIN shops s ON m.shop_id = s.id 
    WHERE m.id = NEW.media_id;
    
    INSERT INTO point_logs (user_id, action_type, points_earned, reference_id) 
    VALUES (v_seller_id, 'receive_like', 2.0, NEW.id);
    
    INSERT INTO user_points (user_id, total_points) 
    VALUES (v_seller_id, 2.0)
    ON CONFLICT (user_id) DO UPDATE 
    SET total_points = user_points.total_points + 2.0,
        updated_at = CURRENT_TIMESTAMP;
    
    RETURN NEW;
END;
$$;

CREATE FUNCTION public.award_viewer_points() RETURNS trigger
    LANGUAGE plpgsql SECURITY DEFINER
    SET search_path TO 'public'
    AS $$
DECLARE
    v_daily_points DECIMAL;
    v_point_log_id UUID;
BEGIN
    SELECT COALESCE(SUM(points_earned), 0) INTO v_daily_points 
    FROM point_logs 
    WHERE user_id = NEW.user_id 
      AND action_type = 'watch_reels' 
      AND created_at::date = CURRENT_DATE;
    IF v_daily_points < 50 THEN
        INSERT INTO point_logs (user_id, action_type, points_earned, reference_id) 
        VALUES (NEW.user_id, 'watch_reels', 5.0, NEW.media_id)
        ON CONFLICT DO NOTHING
        RETURNING id INTO v_point_log_id;

        IF v_point_log_id IS NOT NULL THEN
            INSERT INTO user_points (user_id, total_points)
            VALUES (NEW.user_id, 5.0)
            ON CONFLICT (user_id) DO UPDATE
            SET total_points = user_points.total_points + 5.0,
                updated_at = CURRENT_TIMESTAMP;

            NEW.is_point_earned := TRUE;
        END IF;
    END IF;
    RETURN NEW;
END;
$$;

CREATE FUNCTION public.cleanup_old_shop_visits() RETURNS void
    LANGUAGE plpgsql
    AS $$
BEGIN
    DELETE FROM shop_visits 
    WHERE visited_at < NOW() - INTERVAL '90 days';
END;
$$;

CREATE FUNCTION public.deliver_product_to_library() RETURNS trigger
    LANGUAGE plpgsql SECURITY DEFINER
    SET search_path TO 'public'
    AS $$
BEGIN
    -- SipariÅŸ 'completed' olunca Ã¼rÃ¼nÃ¼ kÃ¼tÃ¼phaneye ekle
    IF (NEW.status = 'completed' AND (TG_OP = 'INSERT' OR OLD.status != 'completed')) THEN
        INSERT INTO user_library (user_id, product_id)
        VALUES (NEW.buyer_id, NEW.product_id)
        ON CONFLICT (user_id, product_id) DO NOTHING;
        
    -- SipariÅŸ 'refunded' olunca Ã¼rÃ¼nÃ¼ kÃ¼tÃ¼phaneden Ã‡IKAR
    ELSIF (NEW.status = 'refunded' AND TG_OP = 'UPDATE' AND OLD.status != 'refunded') THEN
        DELETE FROM user_library 
        WHERE user_id = NEW.buyer_id AND product_id = NEW.product_id;
    END IF;
    
    RETURN NEW;
END;
$$;

CREATE FUNCTION public.increment_coupon_usage() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
    UPDATE coupons SET used_count = used_count + 1 WHERE id = NEW.coupon_id;
    RETURN NEW;
END;
$$;

CREATE FUNCTION public.normalize_analytics_event_shop_id() RETURNS trigger
    LANGUAGE plpgsql SECURITY DEFINER
    SET search_path TO 'public'
    AS $$
DECLARE
    v_product_shop_id UUID;
    v_media_shop_id UUID;
    v_order_shop_id UUID;
BEGIN
    -- product_id varsa Ã¼rÃ¼nÃ¼n gerÃ§ek shop_id'sini bul
    IF NEW.product_id IS NOT NULL THEN
        SELECT shop_id INTO v_product_shop_id
        FROM products
        WHERE id = NEW.product_id;
        IF v_product_shop_id IS NULL THEN
            RAISE EXCEPTION 'Analytics event product_id geÃ§ersiz: %', NEW.product_id;
        END IF;
        NEW.shop_id := v_product_shop_id;
    END IF;
    IF NEW.media_id IS NOT NULL THEN
        SELECT shop_id INTO v_media_shop_id
        FROM media
        WHERE id = NEW.media_id;
        IF v_media_shop_id IS NULL THEN
            RAISE EXCEPTION 'Analytics event media_id gecersiz: %', NEW.media_id;
        END IF;
        NEW.shop_id := v_media_shop_id;
    END IF;
    -- order_id varsa order'Ä±n gerÃ§ek shop_id'sini bul
    IF NEW.order_id IS NOT NULL THEN
        SELECT shop_id INTO v_order_shop_id
        FROM orders
        WHERE id = NEW.order_id;
        IF v_order_shop_id IS NULL THEN
            RAISE EXCEPTION 'Analytics event order_id geÃ§ersiz: %', NEW.order_id;
        END IF;
        NEW.shop_id := v_order_shop_id;
    END IF;
    RETURN NEW;
END;
$$;

CREATE FUNCTION public.prevent_duplicate_purchase() RETURNS trigger
    LANGUAGE plpgsql SECURITY DEFINER
    SET search_path TO 'public'
    AS $$
BEGIN
    -- KullanÄ±cÄ±nÄ±n kÃ¼tÃ¼phanesinde bu Ã¼rÃ¼n var mÄ±?
    IF EXISTS (
        SELECT 1 FROM user_library 
        WHERE user_id = NEW.user_id AND product_id = NEW.product_id
    ) THEN
        RAISE EXCEPTION 'Bu Ã¼rÃ¼n zaten kÃ¼tÃ¼phanenizde mevcut!';
    END IF;
    RETURN NEW;
END;
$$;

CREATE FUNCTION public.process_completed_order() RETURNS trigger
    LANGUAGE plpgsql SECURITY DEFINER
    SET search_path TO 'public'
    AS $$
DECLARE 
    v_seller_id UUID;
    v_point_log_id UUID;
BEGIN
    -- SatÄ±cÄ±yÄ± bul
    SELECT user_id INTO v_seller_id FROM shops WHERE id = NEW.shop_id;
    -- ============ SÄ°PARÄ°Å TAMAMLANDI ============
    IF (NEW.status = 'completed' AND (TG_OP = 'INSERT' OR OLD.status != 'completed')) THEN
        
        -- 1. ÃœrÃ¼n satÄ±ÅŸ sayacÄ±nÄ± artÄ±r
        UPDATE products SET sales_count = sales_count + 1 WHERE id = NEW.product_id;
        
        -- 2. SatÄ±cÄ±ya 20 puan (UPSERT ile)
        INSERT INTO point_logs (user_id, action_type, points_earned, reference_id) 
        VALUES (v_seller_id, 'make_sale', 20.0, NEW.id)
        ON CONFLICT DO NOTHING
        RETURNING id INTO v_point_log_id;

        IF v_point_log_id IS NOT NULL THEN
            INSERT INTO user_points (user_id, total_points)
            VALUES (v_seller_id, 20.0)
            ON CONFLICT (user_id) DO UPDATE
            SET total_points = user_points.total_points + 20.0,
                updated_at = CURRENT_TIMESTAMP;
        END IF;
    
    -- ============ SÄ°PARÄ°Å Ä°ADE EDÄ°LDÄ° (REFUND) ============
    ELSIF (NEW.status = 'refunded' AND TG_OP = 'UPDATE' AND OLD.status != 'refunded') THEN
        
        -- 1. SatÄ±ÅŸ sayacÄ±nÄ± geri al (negatif korumalÄ±)
        UPDATE products SET sales_count = GREATEST(sales_count - 1, 0) WHERE id = NEW.product_id;
        
        -- 2. SatÄ±cÄ±nÄ±n puanÄ±nÄ± geri al
        INSERT INTO point_logs (user_id, action_type, points_earned, reference_id) 
        VALUES (v_seller_id, 'refund_sale', -20.0, NEW.id);
        
        UPDATE user_points 
        SET total_points = GREATEST(total_points - 20.0, 0), 
            updated_at = CURRENT_TIMESTAMP 
        WHERE user_id = v_seller_id;
        
    END IF;
    
    RETURN NEW;
END;
$$;

CREATE FUNCTION public.reward_lesson_completion() RETURNS trigger
    LANGUAGE plpgsql SECURITY DEFINER
    SET search_path TO 'public'
    AS $$
DECLARE v_point_log_id UUID;
BEGIN
    IF NEW.is_completed = TRUE
        AND (TG_OP = 'INSERT' OR OLD.is_completed IS DISTINCT FROM TRUE) THEN
        IF NOT EXISTS (
            SELECT 1
            FROM user_library library_item
            JOIN course_lessons lesson ON lesson.id = NEW.course_lesson_id
            JOIN course_sections section ON section.id = lesson.course_section_id
            JOIN courses course ON course.id = section.course_id
            WHERE library_item.user_id = NEW.user_id
              AND library_item.product_id = course.product_id
        ) THEN
            RETURN NEW;
        END IF;

        INSERT INTO point_logs (user_id, action_type, points_earned, reference_id)
        VALUES (NEW.user_id, 'complete_lesson', 5.0, NEW.course_lesson_id)
        ON CONFLICT (user_id, reference_id) WHERE action_type = 'complete_lesson'
        DO NOTHING
        RETURNING id INTO v_point_log_id;

        IF v_point_log_id IS NOT NULL THEN
            INSERT INTO user_points (user_id, total_points)
            VALUES (NEW.user_id, 5.0)
            ON CONFLICT (user_id) DO UPDATE
            SET total_points = user_points.total_points + 5.0,
                updated_at = CURRENT_TIMESTAMP;
        END IF;
    END IF;
    RETURN NEW;
END;
$$;

CREATE FUNCTION public.sync_follower_count() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
    IF (TG_OP = 'INSERT') THEN
        UPDATE shops SET follower_count = follower_count + 1 WHERE id = NEW.shop_id;
    ELSIF (TG_OP = 'DELETE') THEN
        UPDATE shops SET follower_count = GREATEST(follower_count - 1, 0) WHERE id = OLD.shop_id;
    END IF;
    RETURN NULL;
END;
$$;

CREATE FUNCTION public.sync_media_counters() RETURNS trigger
    LANGUAGE plpgsql SECURITY DEFINER
    SET search_path TO 'public'
    AS $$
BEGIN
    IF TG_TABLE_NAME = 'media_likes' THEN
        IF TG_OP = 'INSERT' THEN 
            UPDATE media SET like_count = like_count + 1 WHERE id = NEW.media_id;
        ELSIF TG_OP = 'DELETE' THEN 
            UPDATE media SET like_count = GREATEST(like_count - 1, 0) WHERE id = OLD.media_id; 
        END IF;
    ELSIF TG_TABLE_NAME = 'media_saves' THEN
        IF TG_OP = 'INSERT' THEN 
            UPDATE media SET save_count = save_count + 1 WHERE id = NEW.media_id;
        ELSIF TG_OP = 'DELETE' THEN 
            UPDATE media SET save_count = GREATEST(save_count - 1, 0) WHERE id = OLD.media_id; 
        END IF;
    ELSIF TG_TABLE_NAME = 'media_comments' THEN
        IF TG_OP = 'INSERT' THEN 
            UPDATE media SET comment_count = comment_count + 1 WHERE id = NEW.media_id;
        ELSIF TG_OP = 'DELETE' THEN 
            UPDATE media SET comment_count = GREATEST(comment_count - 1, 0) WHERE id = OLD.media_id; 
        END IF;
    END IF;
    RETURN NULL;
END;
$$;

CREATE FUNCTION public.sync_order_status_from_payment() RETURNS trigger
    LANGUAGE plpgsql SECURITY DEFINER
    SET search_path TO 'public'
    AS $$
BEGIN
    -- Ã–deme baÅŸarÄ±lÄ± â†’ sipariÅŸi tamamla
    IF (NEW.status = 'succeeded' AND (TG_OP = 'INSERT' OR OLD.status != 'succeeded')) THEN
        UPDATE orders SET status = 'completed' WHERE id = NEW.order_id;
        
    -- Ã–deme iade â†’ sipariÅŸi iade et
    ELSIF (NEW.status = 'refunded' AND OLD.status != 'refunded') THEN
        UPDATE orders SET status = 'refunded' WHERE id = NEW.order_id;
    END IF;
    
    RETURN NEW;
END;
$$;

CREATE FUNCTION public.is_current_app_admin() RETURNS boolean
    LANGUAGE sql STABLE SECURITY DEFINER
    SET search_path TO 'public'
    AS $$
    SELECT EXISTS (
        SELECT 1
        FROM public.users user_record
        WHERE user_record.id = current_setting('app.current_user_id', true)::uuid
          AND user_record.role = 'admin'::public.user_role
          AND user_record.is_active = TRUE
          AND user_record.deleted_at IS NULL
          AND (user_record.locked_until IS NULL OR user_record.locked_until <= CURRENT_TIMESTAMP)
    );
$$;

CREATE FUNCTION public.update_updated_at_column() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
    NEW.updated_at = CURRENT_TIMESTAMP;
    RETURN NEW;
END;
$$;
SET default_tablespace = '';
SET default_table_access_method = heap;


-- =========================================================================
-- TABLES
-- =========================================================================

CREATE TABLE public."__EFMigrationsHistory" (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL
);

CREATE TABLE public.admin_audit_logs (
    id uuid DEFAULT public.uuid_generate_v4() NOT NULL,
    admin_user_id uuid,
    action character varying(100) NOT NULL,
    target_type character varying(50) NOT NULL,
    target_id uuid,
    metadata jsonb DEFAULT '{}'::jsonb,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE public.admin_competition_rewards (
    id uuid DEFAULT public.uuid_generate_v4() NOT NULL,
    contest_id uuid NOT NULL,
    user_id uuid NOT NULL,
    rank integer NOT NULL,
    reward_type character varying(50) NOT NULL,
    amount numeric(12,2),
    currency character varying(3),
    note text,
    certificate_url text,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE public.admin_reports (
    id uuid DEFAULT public.uuid_generate_v4() NOT NULL,
    type character varying(50) NOT NULL,
    target_id uuid NOT NULL,
    target_title text,
    reported_by_user_id uuid,
    reason character varying(50) NOT NULL,
    description text,
    status character varying(20) DEFAULT 'open'::character varying NOT NULL,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP,
    updated_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE public.admin_warnings (
    id uuid DEFAULT public.uuid_generate_v4() NOT NULL,
    user_id uuid NOT NULL,
    admin_user_id uuid,
    title character varying(255) NOT NULL,
    message text NOT NULL,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE public.analytics_events (
    id uuid DEFAULT public.uuid_generate_v4() NOT NULL,
    shop_id uuid NOT NULL,
    product_id uuid,
    media_id uuid,
    user_id uuid,
    order_id uuid,
    event_type public.analytics_event_type NOT NULL,
    session_id character varying(100),
    source character varying(100),
    referrer text,
    utm_source character varying(100),
    utm_medium character varying(100),
    utm_campaign character varying(150),
    device_type character varying(30),
    ip_address inet,
    user_agent text,
    metadata jsonb DEFAULT '{}'::jsonb,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT check_analytics_order_events CHECK (((event_type <> 'purchase_completed'::public.analytics_event_type) OR (order_id IS NOT NULL))),
    CONSTRAINT check_analytics_media_events CHECK (((event_type <> 'media_view'::public.analytics_event_type) OR (media_id IS NOT NULL))),
    CONSTRAINT check_analytics_product_events CHECK (((event_type <> ALL (ARRAY['product_view'::public.analytics_event_type, 'add_to_cart'::public.analytics_event_type, 'download_clicked'::public.analytics_event_type])) OR (product_id IS NOT NULL))),
    CONSTRAINT check_analytics_session_or_user CHECK (((user_id IS NOT NULL) OR (session_id IS NOT NULL) OR (ip_address IS NOT NULL)))
);

CREATE TABLE public.cart_items (
    id uuid DEFAULT public.uuid_generate_v4() NOT NULL,
    user_id uuid NOT NULL,
    product_id uuid NOT NULL,
    quantity integer DEFAULT 1,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP,
    updated_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE public.categories (
    id uuid DEFAULT public.uuid_generate_v4() NOT NULL,
    name character varying(100) NOT NULL,
    slug public.citext NOT NULL,
    parent_id uuid,
    is_active boolean DEFAULT true NOT NULL,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL
);

CREATE TABLE public.contest_results (
    id uuid DEFAULT public.uuid_generate_v4() NOT NULL,
    contest_id uuid NOT NULL,
    user_id uuid NOT NULL,
    final_rank integer,
    total_score numeric(12,2),
    reward_claimed boolean DEFAULT false,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP,
    joined_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE public.contests (
    id uuid DEFAULT public.uuid_generate_v4() NOT NULL,
    title character varying(255) NOT NULL,
    start_date timestamp with time zone NOT NULL,
    end_date timestamp with time zone NOT NULL,
    prize_pool text,
    is_active boolean DEFAULT true,
    created_by uuid,
    description text,
    rewards_hidden boolean DEFAULT false
);

CREATE TABLE public.coupon_uses (
    id uuid DEFAULT public.uuid_generate_v4() NOT NULL,
    coupon_id uuid NOT NULL,
    user_id uuid NOT NULL,
    order_id uuid NOT NULL,
    used_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE public.coupons (
    id uuid DEFAULT public.uuid_generate_v4() NOT NULL,
    product_id uuid NOT NULL,
    shop_id uuid NOT NULL,
    code character varying(50) NOT NULL,
    discount_type character varying(10) NOT NULL,
    discount_value numeric(10,2) NOT NULL,
    minimum_cart_amount numeric(10,2) DEFAULT 0.0,
    max_uses integer,
    used_count integer DEFAULT 0,
    starts_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP,
    expires_at timestamp with time zone,
    is_active boolean DEFAULT true,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE public.course_lessons (
    id uuid DEFAULT public.uuid_generate_v4() NOT NULL,
    course_section_id uuid NOT NULL,
    title character varying(255) NOT NULL,
    video_url text,
    duration_in_seconds integer DEFAULT 0 NOT NULL,
    sort_order integer NOT NULL,
    is_free_preview boolean DEFAULT false NOT NULL,
    is_active boolean DEFAULT true NOT NULL,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    updated_at timestamp with time zone
);

CREATE TABLE public.course_quizzes (
    id uuid DEFAULT public.uuid_generate_v4() NOT NULL,
    course_section_id uuid NOT NULL,
    title character varying(255) NOT NULL,
    passing_score integer NOT NULL,
    is_active boolean DEFAULT true NOT NULL,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    updated_at timestamp with time zone
);

CREATE TABLE public.course_sections (
    id uuid DEFAULT public.uuid_generate_v4() NOT NULL,
    course_id uuid NOT NULL,
    title character varying(255) NOT NULL,
    sort_order integer NOT NULL,
    is_active boolean DEFAULT true NOT NULL,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    updated_at timestamp with time zone
);

CREATE TABLE public.courses (
    id uuid DEFAULT public.uuid_generate_v4() NOT NULL,
    product_id uuid NOT NULL,
    level character varying(50) NOT NULL,
    total_duration_in_minutes integer DEFAULT 0 NOT NULL,
    is_certificate_included boolean DEFAULT false NOT NULL,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    updated_at timestamp with time zone
);

CREATE TABLE public.home_cards (
    id character varying(80) NOT NULL,
    title character varying(255) NOT NULL,
    description text,
    icon character varying(50),
    action_type character varying(50),
    sort_order integer DEFAULT 0 NOT NULL,
    is_active boolean DEFAULT true NOT NULL,
    updated_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE public.ip_login_attempts (
    ip_address inet NOT NULL,
    attempt_count integer DEFAULT 1,
    last_attempt_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP,
    locked_until timestamp with time zone
);

CREATE TABLE public.lesson_progress (
    id uuid DEFAULT public.uuid_generate_v4() NOT NULL,
    user_id uuid NOT NULL,
    lesson_id uuid NOT NULL,
    is_completed boolean DEFAULT false,
    watched_seconds integer DEFAULT 0,
    completed_at timestamp with time zone,
    updated_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE public.lesson_resources (
    id uuid DEFAULT public.uuid_generate_v4() NOT NULL,
    course_lesson_id uuid NOT NULL,
    title character varying(255) NOT NULL,
    file_url text NOT NULL,
    resource_type character varying(50) NOT NULL,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    updated_at timestamp with time zone
);

CREATE TABLE public.login_attempts (
    email public.citext NOT NULL,
    attempt_count integer DEFAULT 1,
    last_attempt_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP,
    ip_address inet,
    locked_until timestamp with time zone
);

CREATE TABLE public.media (
    id uuid DEFAULT public.uuid_generate_v4() NOT NULL,
    shop_id uuid NOT NULL,
    product_id uuid,
    video_url text NOT NULL,
    thumbnail_url text,
    view_count integer DEFAULT 0,
    like_count integer DEFAULT 0,
    save_count integer DEFAULT 0,
    comment_count integer DEFAULT 0,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP,
    updated_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP,
    is_active boolean DEFAULT true,
    caption text,
    hashtags text[] DEFAULT '{}'::text[],
    duration_seconds integer DEFAULT 0,
    status public.media_status DEFAULT 'processing'::public.media_status NOT NULL,
    share_count integer DEFAULT 0
);

CREATE TABLE public.media_comments (
    id uuid DEFAULT public.uuid_generate_v4() NOT NULL,
    media_id uuid NOT NULL,
    user_id uuid NOT NULL,
    comment_text text NOT NULL,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP,
    updated_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP,
    parent_comment_id uuid
);

CREATE TABLE public.media_likes (
    id uuid DEFAULT public.uuid_generate_v4() NOT NULL,
    media_id uuid NOT NULL,
    user_id uuid NOT NULL,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE public.media_saves (
    id uuid DEFAULT public.uuid_generate_v4() NOT NULL,
    media_id uuid NOT NULL,
    user_id uuid NOT NULL,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE public.media_watch_history (
    id uuid DEFAULT public.uuid_generate_v4() NOT NULL,
    user_id uuid NOT NULL,
    media_id uuid NOT NULL,
    watched_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP,
    is_point_earned boolean DEFAULT false
);

CREATE TABLE public.notification_deliveries (
    id uuid DEFAULT public.uuid_generate_v4() NOT NULL,
    notification_id uuid NOT NULL,
    channel character varying(20) NOT NULL,
    status character varying(20) DEFAULT 'pending'::character varying,
    provider character varying(50),
    provider_message_id character varying(255),
    error_message text,
    sent_at timestamp with time zone,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE public.notifications (
    id uuid DEFAULT public.uuid_generate_v4() NOT NULL,
    user_id uuid NOT NULL,
    type character varying(50) NOT NULL,
    title character varying(255) NOT NULL,
    body text NOT NULL,
    reference_type character varying(50),
    reference_id uuid,
    data jsonb,
    is_read boolean DEFAULT false,
    read_at timestamp with time zone,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT check_notification_type CHECK (((type)::text = ANY ((ARRAY['sale_completed'::character varying, 'new_follower'::character varying, 'new_review'::character varying, 'new_question'::character varying, 'media_liked'::character varying, 'media_commented'::character varying, 'contest_result'::character varying, 'order_completed'::character varying, 'new_video'::character varying, 'new_product'::character varying, 'product_question_answer'::character varying, 'system'::character varying])::text[])))
);

CREATE TABLE public.orders (
    id uuid DEFAULT public.uuid_generate_v4() NOT NULL,
    buyer_id uuid NOT NULL,
    product_id uuid NOT NULL,
    shop_id uuid NOT NULL,
    order_number character varying(50) NOT NULL,
    amount numeric(10,2) NOT NULL,
    currency character varying(3) DEFAULT 'USD'::character varying,
    platform_fee numeric(10,2) DEFAULT 0.00,
    seller_earnings numeric(10,2) DEFAULT 0.00,
    status public.order_status DEFAULT 'pending'::public.order_status NOT NULL,
    stripe_payment_id character varying(255),
    invoice_pdf_url text,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP,
    updated_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT check_fee_logic CHECK ((abs((amount - (platform_fee + seller_earnings))) <= 0.01))
);

CREATE TABLE public.payments (
    id uuid DEFAULT public.uuid_generate_v4() NOT NULL,
    order_id uuid NOT NULL,
    payment_provider character varying(50) NOT NULL,
    provider_transaction_id character varying(255),
    gross_amount numeric(10,2) NOT NULL,
    platform_fee_amount numeric(10,2) NOT NULL,
    net_earnings numeric(10,2) NOT NULL,
    status public.payment_status_type DEFAULT 'processing'::public.payment_status_type NOT NULL,
    error_message text,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP,
    updated_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT check_payment_math CHECK ((abs((gross_amount - (platform_fee_amount + net_earnings))) <= 0.01))
);

CREATE TABLE public.point_logs (
    id uuid DEFAULT public.uuid_generate_v4() NOT NULL,
    user_id uuid NOT NULL,
    action_type character varying(50) NOT NULL,
    points_earned numeric(10,2) NOT NULL,
    reference_id uuid,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE public.product_images (
    id uuid DEFAULT public.uuid_generate_v4() NOT NULL,
    product_id uuid NOT NULL,
    object_key text NOT NULL,
    sort_order integer NOT NULL,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE public.product_qa (
    id uuid DEFAULT public.uuid_generate_v4() NOT NULL,
    product_id uuid NOT NULL,
    user_id uuid NOT NULL,
    parent_id uuid,
    message text NOT NULL,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE public.products (
    id uuid DEFAULT public.uuid_generate_v4() NOT NULL,
    shop_id uuid NOT NULL,
    category_id uuid NOT NULL,
    type public.product_type DEFAULT 'digital_file'::public.product_type NOT NULL,
    title character varying(255) NOT NULL,
    description text,
    metadata jsonb DEFAULT '{}'::jsonb,
    price numeric(10,2) NOT NULL,
    original_price numeric(10,2),
    currency character varying(3) DEFAULT 'USD'::character varying,
    cover_image_url text,
    preview_video_url text,
    file_url text,
    rating_average numeric(3,2) DEFAULT 0.0,
    review_count integer DEFAULT 0,
    sales_count integer DEFAULT 0,
    is_active boolean DEFAULT true,
    is_featured boolean DEFAULT false,
    status character varying(20) DEFAULT 'Draft'::character varying NOT NULL,
    tags text[] NOT NULL,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP,
    updated_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP,
    discount_price numeric(10,2),
    discount_ends_at timestamp with time zone
);

CREATE TABLE public.pulse_news (
    id uuid DEFAULT public.uuid_generate_v4() NOT NULL,
    title character varying(255) NOT NULL,
    description text,
    meta character varying(100),
    icon character varying(50),
    is_published boolean DEFAULT false NOT NULL,
    is_new_until timestamp with time zone,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP,
    updated_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE public.reviews (
    id uuid DEFAULT public.uuid_generate_v4() NOT NULL,
    product_id uuid NOT NULL,
    user_id uuid NOT NULL,
    rating integer NOT NULL,
    comment text,
    seller_reply text,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP,
    updated_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP,
    images jsonb DEFAULT '[]'::jsonb
);

CREATE TABLE public.seller_subscriptions (
    id uuid DEFAULT public.uuid_generate_v4() NOT NULL,
    shop_id uuid NOT NULL,
    provider_subscription_id character varying(255),
    status public.sub_status DEFAULT 'active'::public.sub_status NOT NULL,
    current_period_end timestamp with time zone NOT NULL,
    grace_period_end timestamp with time zone,
    amount numeric(10,2) DEFAULT 25.00,
    currency character varying(3) DEFAULT 'USD'::character varying,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP,
    updated_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP,
    payment_provider character varying(50) DEFAULT 'stripe'::character varying,
    reminder_sent_at timestamp with time zone,
    CONSTRAINT check_grace_after_period CHECK (((grace_period_end IS NULL) OR (grace_period_end >= current_period_end)))
);

CREATE TABLE public.shop_visits (
    id uuid DEFAULT public.uuid_generate_v4() NOT NULL,
    shop_id uuid NOT NULL,
    user_id uuid,
    ip_address inet,
    visited_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE public.shops (
    id uuid DEFAULT public.uuid_generate_v4() NOT NULL,
    user_id uuid NOT NULL,
    shop_name character varying(100) NOT NULL,
    slug public.citext NOT NULL,
    external_url character varying(255),
    short_description character varying(255),
    description text,
    about_content text,
    social_links jsonb DEFAULT '{}'::jsonb,
    logo_url text,
    banner_url text,
    follower_count integer DEFAULT 0,
    rating numeric(3,2) DEFAULT 0.0,
    is_verified boolean DEFAULT false,
    is_active boolean DEFAULT true,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP,
    updated_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE public.subscriptions (
    id uuid DEFAULT public.uuid_generate_v4() NOT NULL,
    shop_id uuid NOT NULL,
    user_id uuid NOT NULL,
    wants_notifications boolean DEFAULT true,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE public.support_ticket_messages (
    id uuid DEFAULT public.uuid_generate_v4() NOT NULL,
    ticket_id uuid NOT NULL,
    sender_id uuid NOT NULL,
    sender_role public.support_message_sender_role NOT NULL,
    message text NOT NULL,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    CONSTRAINT support_ticket_messages_message_not_blank CHECK ((char_length(btrim(message)) BETWEEN 1 AND 5000))
);

CREATE TABLE public.support_tickets (
    id uuid DEFAULT public.uuid_generate_v4() NOT NULL,
    user_id uuid NOT NULL,
    subject character varying(200) NOT NULL,
    status public.support_ticket_status DEFAULT 'open'::public.support_ticket_status NOT NULL,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    updated_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    last_message_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    closed_at timestamp with time zone,
    closed_by_user_id uuid,
    CONSTRAINT support_tickets_subject_not_blank CHECK ((char_length(btrim(subject)) BETWEEN 1 AND 200))
);

CREATE TABLE public.user_device_tokens (
    id uuid DEFAULT public.uuid_generate_v4() NOT NULL,
    user_id uuid NOT NULL,
    token text NOT NULL,
    device_type character varying(20) NOT NULL,
    device_id character varying(255),
    is_active boolean DEFAULT true,
    last_used_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE public.user_lesson_progress (
    id uuid DEFAULT public.uuid_generate_v4() NOT NULL,
    user_id uuid NOT NULL,
    course_lesson_id uuid NOT NULL,
    is_completed boolean DEFAULT false NOT NULL,
    watched_seconds integer DEFAULT 0 NOT NULL,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    updated_at timestamp with time zone
);

CREATE TABLE public.user_library (
    id uuid DEFAULT public.uuid_generate_v4() NOT NULL,
    user_id uuid NOT NULL,
    product_id uuid NOT NULL,
    purchased_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP,
    last_accessed_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE public.user_points (
    id uuid DEFAULT public.uuid_generate_v4() NOT NULL,
    user_id uuid NOT NULL,
    total_points numeric(12,2) DEFAULT 0.0,
    current_rank integer DEFAULT 0,
    current_streak integer DEFAULT 0,
    updated_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE public.user_sessions (
    id uuid DEFAULT public.uuid_generate_v4() NOT NULL,
    user_id uuid,
    refresh_token text NOT NULL,
    device_id character varying(255),
    ip_address inet,
    user_agent text,
    expires_at timestamp with time zone NOT NULL,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP,
    is_revoked boolean DEFAULT false
);

CREATE TABLE public.users (
    id uuid DEFAULT public.uuid_generate_v4() NOT NULL,
    email public.citext NOT NULL,
    full_name character varying(100),
    avatar_url text,
    role public.user_role DEFAULT 'user'::public.user_role NOT NULL,
    auth_provider character varying(50) DEFAULT 'email'::character varying,
    provider_id character varying(255),
    password_hash text,
    is_email_verified boolean DEFAULT false,
    locked_until timestamp with time zone,
    lock_reason text,
    stripe_customer_id character varying(255),
    stripe_account_id character varying(255),
    preferences jsonb DEFAULT '{}'::jsonb,
    is_active boolean DEFAULT true,
    last_login_at timestamp with time zone,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP,
    updated_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP,
    deleted_at timestamp with time zone
);


-- =========================================================================
-- SEED DATA
-- =========================================================================

COPY public."__EFMigrationsHistory" ("MigrationId", "ProductVersion") FROM stdin;
20260521082539_AddPreviewVideoToProduct	9.0.0
20260521091507_AddEcommerceFeaturesToProduct	9.0.0
20260521122829_AddCourseModuleEntities	9.0.0
20260521135244_AddCourseProgressTracking	9.0.0
20260523154722_AddCouponMinimumCartAmount	9.0.0
20260524152119_FinalSchemaUpdate	9.0.0
20260524153943_InitialCreate	9.0.0
20260524161125_FinalSetup	9.0.0
20260608183145_AddReminderSentAtToSellerSubscription	9.0.0
20260610182651_AddProductImages	9.0.0
\.

COPY public.categories (id, name, slug, parent_id, is_active, created_at) FROM stdin;
1bcc9c55-9cbf-45f8-aa1e-45e3bb310a49	Education	education	\N	t	2026-05-31 20:09:08.61454+00
926f634d-b3c5-41d1-9217-de73c23bf1ef	Media & Video	media-video	\N	t	2026-05-31 20:09:08.61454+00
6854ccd8-7726-4737-8e7f-7981d052df58	Software Development	software-development	\N	t	2026-05-31 20:09:08.614474+00
282e01ad-6500-44af-9a2f-e99831464e7a	Design Assets	design-assets	\N	t	2026-05-31 20:09:08.614539+00
686d2433-b518-4c18-94b3-a1ab155f4a20	Growth Marketing	growth-marketing	\N	t	2026-05-31 20:09:08.614539+00
\.


-- =========================================================================
-- CONSTRAINTS
-- =========================================================================

ALTER TABLE ONLY public."__EFMigrationsHistory"
    ADD CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId");

ALTER TABLE ONLY public.admin_audit_logs
    ADD CONSTRAINT admin_audit_logs_pkey PRIMARY KEY (id);

ALTER TABLE ONLY public.admin_competition_rewards
    ADD CONSTRAINT admin_competition_rewards_pkey PRIMARY KEY (id);

ALTER TABLE ONLY public.admin_competition_rewards
    ADD CONSTRAINT check_admin_competition_rewards_type CHECK (((reward_type)::text = ANY ((ARRAY['money'::character varying, 'premium_1_month'::character varying, 'certificate'::character varying])::text[])));

ALTER TABLE ONLY public.admin_reports
    ADD CONSTRAINT admin_reports_pkey PRIMARY KEY (id);

ALTER TABLE ONLY public.admin_warnings
    ADD CONSTRAINT admin_warnings_pkey PRIMARY KEY (id);

ALTER TABLE ONLY public.analytics_events
    ADD CONSTRAINT analytics_events_pkey PRIMARY KEY (id);

ALTER TABLE ONLY public.cart_items
    ADD CONSTRAINT cart_items_pkey PRIMARY KEY (id);

ALTER TABLE ONLY public.categories
    ADD CONSTRAINT categories_pkey PRIMARY KEY (id);

ALTER TABLE ONLY public.contest_results
    ADD CONSTRAINT contest_results_pkey PRIMARY KEY (id);

ALTER TABLE ONLY public.contests
    ADD CONSTRAINT contests_pkey PRIMARY KEY (id);

ALTER TABLE ONLY public.coupon_uses
    ADD CONSTRAINT coupon_uses_pkey PRIMARY KEY (id);

ALTER TABLE ONLY public.coupons
    ADD CONSTRAINT coupons_pkey PRIMARY KEY (id);

ALTER TABLE ONLY public.course_lessons
    ADD CONSTRAINT course_lessons_pkey PRIMARY KEY (id);

ALTER TABLE ONLY public.course_quizzes
    ADD CONSTRAINT course_quizzes_pkey PRIMARY KEY (id);

ALTER TABLE ONLY public.course_sections
    ADD CONSTRAINT course_sections_pkey PRIMARY KEY (id);

ALTER TABLE ONLY public.courses
    ADD CONSTRAINT courses_pkey PRIMARY KEY (id);

ALTER TABLE ONLY public.home_cards
    ADD CONSTRAINT home_cards_pkey PRIMARY KEY (id);

ALTER TABLE ONLY public.ip_login_attempts
    ADD CONSTRAINT ip_login_attempts_pkey PRIMARY KEY (ip_address);

ALTER TABLE ONLY public.lesson_progress
    ADD CONSTRAINT lesson_progress_pkey PRIMARY KEY (id);

ALTER TABLE ONLY public.lesson_resources
    ADD CONSTRAINT lesson_resources_pkey PRIMARY KEY (id);

ALTER TABLE ONLY public.login_attempts
    ADD CONSTRAINT login_attempts_pkey PRIMARY KEY (email);

ALTER TABLE ONLY public.media_comments
    ADD CONSTRAINT media_comments_pkey PRIMARY KEY (id);

ALTER TABLE ONLY public.media_likes
    ADD CONSTRAINT media_likes_pkey PRIMARY KEY (id);

ALTER TABLE ONLY public.media
    ADD CONSTRAINT media_pkey PRIMARY KEY (id);

ALTER TABLE ONLY public.media_saves
    ADD CONSTRAINT media_saves_pkey PRIMARY KEY (id);

ALTER TABLE ONLY public.media_watch_history
    ADD CONSTRAINT media_watch_history_pkey PRIMARY KEY (id);

ALTER TABLE ONLY public.notification_deliveries
    ADD CONSTRAINT notification_deliveries_pkey PRIMARY KEY (id);

ALTER TABLE ONLY public.notifications
    ADD CONSTRAINT notifications_pkey PRIMARY KEY (id);

ALTER TABLE ONLY public.orders
    ADD CONSTRAINT orders_pkey PRIMARY KEY (id);

ALTER TABLE ONLY public.payments
    ADD CONSTRAINT payments_pkey PRIMARY KEY (id);

ALTER TABLE ONLY public.point_logs
    ADD CONSTRAINT point_logs_pkey PRIMARY KEY (id);

ALTER TABLE ONLY public.product_images
    ADD CONSTRAINT product_images_pkey PRIMARY KEY (id);

ALTER TABLE ONLY public.product_qa
    ADD CONSTRAINT product_qa_pkey PRIMARY KEY (id);

ALTER TABLE ONLY public.products
    ADD CONSTRAINT products_pkey PRIMARY KEY (id);

ALTER TABLE ONLY public.pulse_news
    ADD CONSTRAINT pulse_news_pkey PRIMARY KEY (id);

ALTER TABLE ONLY public.reviews
    ADD CONSTRAINT reviews_pkey PRIMARY KEY (id);

ALTER TABLE ONLY public.seller_subscriptions
    ADD CONSTRAINT seller_subscriptions_pkey PRIMARY KEY (id);

ALTER TABLE ONLY public.shop_visits
    ADD CONSTRAINT shop_visits_pkey PRIMARY KEY (id);

ALTER TABLE ONLY public.shops
    ADD CONSTRAINT shops_pkey PRIMARY KEY (id);

ALTER TABLE ONLY public.subscriptions
    ADD CONSTRAINT subscriptions_pkey PRIMARY KEY (id);

ALTER TABLE ONLY public.subscriptions
    ADD CONSTRAINT unique_subscription UNIQUE (shop_id, user_id);

ALTER TABLE ONLY public.support_ticket_messages
    ADD CONSTRAINT support_ticket_messages_pkey PRIMARY KEY (id);

ALTER TABLE ONLY public.support_tickets
    ADD CONSTRAINT support_tickets_pkey PRIMARY KEY (id);

ALTER TABLE ONLY public.user_device_tokens
    ADD CONSTRAINT user_device_tokens_pkey PRIMARY KEY (id);

ALTER TABLE ONLY public.user_lesson_progress
    ADD CONSTRAINT user_lesson_progress_pkey PRIMARY KEY (id);

ALTER TABLE ONLY public.user_library
    ADD CONSTRAINT user_library_pkey PRIMARY KEY (id);

ALTER TABLE ONLY public.user_points
    ADD CONSTRAINT user_points_pkey PRIMARY KEY (id);

ALTER TABLE ONLY public.user_sessions
    ADD CONSTRAINT user_sessions_pkey PRIMARY KEY (id);

ALTER TABLE ONLY public.users
    ADD CONSTRAINT users_pkey PRIMARY KEY (id);


-- =========================================================================
-- INDEXES
-- =========================================================================

CREATE INDEX "IX_cart_items_product_id" ON public.cart_items USING btree (product_id);

CREATE INDEX "IX_categories_parent_id" ON public.categories USING btree (parent_id);

CREATE INDEX "IX_contest_results_user_id" ON public.contest_results USING btree (user_id);

CREATE UNIQUE INDEX uq_admin_competition_rewards_contest_user ON public.admin_competition_rewards USING btree (contest_id, user_id);

CREATE INDEX "IX_contests_created_by" ON public.contests USING btree (created_by);

CREATE INDEX "IX_coupon_uses_order_id" ON public.coupon_uses USING btree (order_id);

CREATE INDEX "IX_coupon_uses_user_id" ON public.coupon_uses USING btree (user_id);

CREATE INDEX "IX_coupons_shop_id" ON public.coupons USING btree (shop_id);

CREATE INDEX "IX_lesson_progress_lesson_id" ON public.lesson_progress USING btree (lesson_id);

CREATE INDEX "IX_media_comments_media_id" ON public.media_comments USING btree (media_id);

CREATE INDEX "IX_media_comments_user_id" ON public.media_comments USING btree (user_id);

CREATE INDEX "IX_media_likes_user_id" ON public.media_likes USING btree (user_id);

CREATE INDEX "IX_media_saves_user_id" ON public.media_saves USING btree (user_id);

CREATE INDEX "IX_media_watch_history_media_id" ON public.media_watch_history USING btree (media_id);

CREATE INDEX "IX_orders_product_id" ON public.orders USING btree (product_id);

CREATE INDEX "IX_product_qa_parent_id" ON public.product_qa USING btree (parent_id);

CREATE INDEX "IX_product_qa_product_id" ON public.product_qa USING btree (product_id);

CREATE INDEX "IX_product_qa_user_id" ON public.product_qa USING btree (user_id);

CREATE INDEX "IX_products_category_id" ON public.products USING btree (category_id);

CREATE INDEX "IX_reviews_user_id" ON public.reviews USING btree (user_id);

CREATE INDEX "IX_shop_visits_user_id" ON public.shop_visits USING btree (user_id);

CREATE INDEX "IX_subscriptions_user_id" ON public.subscriptions USING btree (user_id);

CREATE INDEX "IX_user_lesson_progress_course_lesson_id" ON public.user_lesson_progress USING btree (course_lesson_id);

CREATE INDEX "IX_user_library_product_id" ON public.user_library USING btree (product_id);

CREATE INDEX "IX_user_sessions_user_id" ON public.user_sessions USING btree (user_id);

CREATE INDEX idx_admin_audit_logs_created ON public.admin_audit_logs USING btree (created_at DESC);

CREATE INDEX idx_admin_reports_status_type ON public.admin_reports USING btree (status, type);

CREATE INDEX idx_admin_warnings_user ON public.admin_warnings USING btree (user_id, created_at DESC);

CREATE INDEX idx_analytics_metadata ON public.analytics_events USING gin (metadata);

CREATE INDEX idx_analytics_media_event_date ON public.analytics_events USING btree (media_id, event_type, created_at DESC) WHERE (media_id IS NOT NULL);

CREATE INDEX idx_analytics_order ON public.analytics_events USING btree (order_id) WHERE (order_id IS NOT NULL);

CREATE INDEX idx_analytics_product_event_date ON public.analytics_events USING btree (product_id, event_type, created_at DESC) WHERE (product_id IS NOT NULL);

CREATE INDEX idx_analytics_session_date ON public.analytics_events USING btree (session_id, created_at DESC) WHERE (session_id IS NOT NULL);

CREATE INDEX idx_analytics_shop_date ON public.analytics_events USING btree (shop_id, created_at DESC);

CREATE INDEX idx_analytics_shop_event_date ON public.analytics_events USING btree (shop_id, event_type, created_at DESC);

CREATE INDEX idx_analytics_shop_source_date ON public.analytics_events USING btree (shop_id, source, created_at DESC) WHERE (source IS NOT NULL);

CREATE INDEX idx_analytics_shop_utm_source_date ON public.analytics_events USING btree (shop_id, utm_source, created_at DESC) WHERE (utm_source IS NOT NULL);

CREATE INDEX idx_analytics_user_date ON public.analytics_events USING btree (user_id, created_at DESC) WHERE (user_id IS NOT NULL);

CREATE INDEX idx_cart_items_user ON public.cart_items USING btree (user_id);

CREATE INDEX idx_coupons_code ON public.coupons USING btree (code);

CREATE INDEX idx_coupons_product ON public.coupons USING btree (product_id);

CREATE INDEX idx_course_lessons_section ON public.course_lessons USING btree (course_section_id);

CREATE INDEX idx_course_quizzes_section ON public.course_quizzes USING btree (course_section_id);

CREATE INDEX idx_course_sections_course ON public.course_sections USING btree (course_id);

CREATE INDEX idx_courses_product ON public.courses USING btree (product_id);

CREATE INDEX idx_deliveries_notification ON public.notification_deliveries USING btree (notification_id);

CREATE INDEX idx_deliveries_pending ON public.notification_deliveries USING btree (status) WHERE ((status)::text = 'pending'::text);

CREATE INDEX idx_device_tokens_user ON public.user_device_tokens USING btree (user_id) WHERE (is_active = true);

CREATE INDEX idx_ip_attempts_locked_until ON public.ip_login_attempts USING btree (locked_until) WHERE (locked_until IS NOT NULL);

CREATE INDEX idx_lesson_progress_user ON public.lesson_progress USING btree (user_id, lesson_id);

CREATE INDEX idx_lesson_resources_lesson ON public.lesson_resources USING btree (course_lesson_id);

CREATE INDEX idx_media_comments_media_parent_created ON public.media_comments USING btree (media_id, parent_comment_id, created_at);

CREATE INDEX idx_media_comments_parent ON public.media_comments USING btree (parent_comment_id);

CREATE INDEX idx_media_product ON public.media USING btree (product_id);

CREATE INDEX idx_media_shop ON public.media USING btree (shop_id);

CREATE INDEX idx_notifications_unread ON public.notifications USING btree (user_id, is_read) WHERE (is_read = false);

CREATE INDEX idx_notifications_user ON public.notifications USING btree (user_id, created_at DESC);

CREATE INDEX idx_orders_buyer ON public.orders USING btree (buyer_id);

CREATE INDEX idx_orders_number ON public.orders USING btree (order_number);

CREATE INDEX idx_orders_shop ON public.orders USING btree (shop_id);

CREATE INDEX idx_orders_status ON public.orders USING btree (status);

CREATE INDEX idx_payments_status ON public.payments USING btree (status);

CREATE INDEX idx_payments_transaction_id ON public.payments USING btree (provider_transaction_id);

CREATE INDEX idx_point_logs_user_date ON public.point_logs USING btree (user_id, created_at);

CREATE UNIQUE INDEX uq_point_logs_complete_lesson_once ON public.point_logs USING btree (user_id, reference_id) WHERE (action_type = 'complete_lesson');

CREATE UNIQUE INDEX uq_point_logs_make_sale_once ON public.point_logs USING btree (user_id, reference_id) WHERE (action_type = 'make_sale');

CREATE UNIQUE INDEX uq_point_logs_purchase_product_once ON public.point_logs USING btree (user_id, reference_id) WHERE (action_type = 'purchase_product');

CREATE UNIQUE INDEX uq_point_logs_create_product_once ON public.point_logs USING btree (user_id, reference_id) WHERE (action_type = 'create_product');

CREATE UNIQUE INDEX uq_point_logs_watch_reels_once ON public.point_logs USING btree (user_id, reference_id) WHERE (action_type = 'watch_reels');

CREATE INDEX idx_product_images_product ON public.product_images USING btree (product_id);

CREATE INDEX idx_products_shop ON public.products USING btree (shop_id);

CREATE INDEX idx_pulse_news_published ON public.pulse_news USING btree (is_published, created_at DESC);

CREATE INDEX idx_seller_subs_grace ON public.seller_subscriptions USING btree (grace_period_end) WHERE (grace_period_end IS NOT NULL);

CREATE INDEX idx_seller_subs_period ON public.seller_subscriptions USING btree (status, current_period_end);

CREATE INDEX idx_shop_visits_composite ON public.shop_visits USING btree (shop_id, visited_at);

CREATE INDEX idx_shops_name ON public.shops USING btree (shop_name);

CREATE INDEX idx_shops_slug ON public.shops USING btree (slug);

CREATE INDEX idx_support_ticket_messages_ticket_created ON public.support_ticket_messages USING btree (ticket_id, created_at);

CREATE INDEX idx_support_tickets_status_last_message ON public.support_tickets USING btree (status, last_message_at DESC);

CREATE INDEX idx_support_tickets_user_last_message ON public.support_tickets USING btree (user_id, last_message_at DESC);

CREATE INDEX idx_user_library_accessed ON public.user_library USING btree (user_id, last_accessed_at DESC);


-- =========================================================================
-- TRIGGERS
-- =========================================================================

CREATE TRIGGER set_cart_updated_at BEFORE UPDATE ON public.cart_items FOR EACH ROW EXECUTE FUNCTION public.update_updated_at_column();

CREATE TRIGGER set_media_comments_updated_at BEFORE UPDATE ON public.media_comments FOR EACH ROW EXECUTE FUNCTION public.update_updated_at_column();

CREATE TRIGGER set_orders_updated_at BEFORE UPDATE ON public.orders FOR EACH ROW EXECUTE FUNCTION public.update_updated_at_column();

CREATE TRIGGER set_payments_updated_at BEFORE UPDATE ON public.payments FOR EACH ROW EXECUTE FUNCTION public.update_updated_at_column();

CREATE TRIGGER set_reviews_updated_at BEFORE UPDATE ON public.reviews FOR EACH ROW EXECUTE FUNCTION public.update_updated_at_column();

CREATE TRIGGER set_seller_sub_updated_at BEFORE UPDATE ON public.seller_subscriptions FOR EACH ROW EXECUTE FUNCTION public.update_updated_at_column();

CREATE TRIGGER set_shops_updated_at BEFORE UPDATE ON public.shops FOR EACH ROW EXECUTE FUNCTION public.update_updated_at_column();

CREATE TRIGGER set_support_tickets_updated_at BEFORE UPDATE ON public.support_tickets FOR EACH ROW EXECUTE FUNCTION public.update_updated_at_column();

CREATE TRIGGER set_users_updated_at BEFORE UPDATE ON public.users FOR EACH ROW EXECUTE FUNCTION public.update_updated_at_column();

CREATE TRIGGER trg_auto_deliver_product AFTER INSERT OR UPDATE ON public.orders FOR EACH ROW EXECUTE FUNCTION public.deliver_product_to_library();

CREATE TRIGGER trg_check_already_owned BEFORE INSERT OR UPDATE ON public.cart_items FOR EACH ROW EXECUTE FUNCTION public.prevent_duplicate_purchase();

CREATE TRIGGER trg_increment_coupon_usage AFTER INSERT ON public.coupon_uses FOR EACH ROW EXECUTE FUNCTION public.increment_coupon_usage();

CREATE TRIGGER trg_media_comment_counter AFTER INSERT OR DELETE ON public.media_comments FOR EACH ROW EXECUTE FUNCTION public.sync_media_counters();

CREATE TRIGGER trg_media_like_counter AFTER INSERT OR DELETE ON public.media_likes FOR EACH ROW EXECUTE FUNCTION public.sync_media_counters();

CREATE TRIGGER trg_media_save_counter AFTER INSERT OR DELETE ON public.media_saves FOR EACH ROW EXECUTE FUNCTION public.sync_media_counters();

CREATE TRIGGER trg_normalize_analytics_event_shop_id BEFORE INSERT ON public.analytics_events FOR EACH ROW EXECUTE FUNCTION public.normalize_analytics_event_shop_id();

CREATE TRIGGER trg_on_order_completed AFTER INSERT OR UPDATE ON public.orders FOR EACH ROW EXECUTE FUNCTION public.process_completed_order();

CREATE TRIGGER trg_points_on_like AFTER INSERT ON public.media_likes FOR EACH ROW EXECUTE FUNCTION public.award_seller_points();

CREATE TRIGGER trg_points_on_watch BEFORE INSERT ON public.media_watch_history FOR EACH ROW EXECUTE FUNCTION public.award_viewer_points();

CREATE TRIGGER trg_points_on_lesson_completion AFTER INSERT OR UPDATE OF is_completed ON public.user_lesson_progress FOR EACH ROW EXECUTE FUNCTION public.reward_lesson_completion();

CREATE TRIGGER trg_sync_followers AFTER INSERT OR DELETE ON public.subscriptions FOR EACH ROW EXECUTE FUNCTION public.sync_follower_count();

CREATE TRIGGER trg_sync_order_on_payment AFTER INSERT OR UPDATE ON public.payments FOR EACH ROW EXECUTE FUNCTION public.sync_order_status_from_payment();


-- =========================================================================
-- ROW LEVEL SECURITY
-- =========================================================================

ALTER TABLE public.analytics_events ENABLE ROW LEVEL SECURITY;

ALTER TABLE public.cart_items ENABLE ROW LEVEL SECURITY;

ALTER TABLE public.categories ENABLE ROW LEVEL SECURITY;

ALTER TABLE public.contest_results ENABLE ROW LEVEL SECURITY;

ALTER TABLE public.contests ENABLE ROW LEVEL SECURITY;

ALTER TABLE public.coupon_uses ENABLE ROW LEVEL SECURITY;

ALTER TABLE public.coupons ENABLE ROW LEVEL SECURITY;

ALTER TABLE public.course_lessons ENABLE ROW LEVEL SECURITY;

ALTER TABLE public.course_sections ENABLE ROW LEVEL SECURITY;

ALTER TABLE public.ip_login_attempts ENABLE ROW LEVEL SECURITY;

ALTER TABLE public.lesson_progress ENABLE ROW LEVEL SECURITY;

ALTER TABLE public.login_attempts ENABLE ROW LEVEL SECURITY;

ALTER TABLE public.media ENABLE ROW LEVEL SECURITY;

ALTER TABLE public.media_comments ENABLE ROW LEVEL SECURITY;

ALTER TABLE public.media_likes ENABLE ROW LEVEL SECURITY;

ALTER TABLE public.media_saves ENABLE ROW LEVEL SECURITY;

ALTER TABLE public.media_watch_history ENABLE ROW LEVEL SECURITY;

ALTER TABLE public.notification_deliveries ENABLE ROW LEVEL SECURITY;

ALTER TABLE public.notifications ENABLE ROW LEVEL SECURITY;

ALTER TABLE public.orders ENABLE ROW LEVEL SECURITY;

ALTER TABLE public.payments ENABLE ROW LEVEL SECURITY;

ALTER TABLE public.point_logs ENABLE ROW LEVEL SECURITY;

ALTER TABLE public.product_qa ENABLE ROW LEVEL SECURITY;

ALTER TABLE public.products ENABLE ROW LEVEL SECURITY;

ALTER TABLE public.reviews ENABLE ROW LEVEL SECURITY;

ALTER TABLE public.shop_visits ENABLE ROW LEVEL SECURITY;

ALTER TABLE public.shops ENABLE ROW LEVEL SECURITY;

ALTER TABLE public.subscriptions ENABLE ROW LEVEL SECURITY;

ALTER TABLE public.support_ticket_messages ENABLE ROW LEVEL SECURITY;

ALTER TABLE public.support_tickets ENABLE ROW LEVEL SECURITY;

ALTER TABLE public.user_device_tokens ENABLE ROW LEVEL SECURITY;

ALTER TABLE public.user_library ENABLE ROW LEVEL SECURITY;

ALTER TABLE public.user_points ENABLE ROW LEVEL SECURITY;

ALTER TABLE public.user_sessions ENABLE ROW LEVEL SECURITY;

ALTER TABLE public.users ENABLE ROW LEVEL SECURITY;


-- =========================================================================
-- RLS POLICIES
-- =========================================================================

CREATE POLICY "Aktif kullanÄ±cÄ±larÄ± herkes gÃ¶rebilir" ON public.users FOR SELECT USING (((is_active = true) AND (deleted_at IS NULL)));

CREATE POLICY "Aktif Ã¼rÃ¼nler herkese aÃ§Ä±k" ON public.products FOR SELECT USING ((is_active = true));

CREATE POLICY "AlÄ±cÄ±lar dekontunu gÃ¶rebilir" ON public.payments FOR SELECT USING ((order_id IN ( SELECT orders.id
   FROM public.orders
  WHERE (orders.buyer_id = (current_setting('app.current_user_id'::text, true))::uuid))));

CREATE POLICY "AlÄ±cÄ±lar kendi sipariÅŸlerini gÃ¶rebilir" ON public.orders FOR SELECT USING ((buyer_id = (current_setting('app.current_user_id'::text, true))::uuid));

CREATE POLICY "Herkes ziyaret kaydÄ± oluÅŸturabilir" ON public.shop_visits FOR INSERT WITH CHECK (((user_id IS NULL) OR (user_id = (current_setting('app.current_user_id'::text, true))::uuid)));

CREATE POLICY "Kategoriler herkese aÃ§Ä±k" ON public.categories FOR SELECT USING ((is_active = true));

CREATE POLICY "KullanÄ±cÄ± beÄŸeni yapabilir/silebilir" ON public.media_likes USING ((user_id = (current_setting('app.current_user_id'::text, true))::uuid)) WITH CHECK ((user_id = (current_setting('app.current_user_id'::text, true))::uuid));

CREATE POLICY "KullanÄ±cÄ± beÄŸenileri gÃ¶rebilir" ON public.media_likes FOR SELECT USING ((user_id = (current_setting('app.current_user_id'::text, true))::uuid));

CREATE POLICY "KullanÄ±cÄ± bildirimini okundu yapabilir" ON public.notifications FOR UPDATE USING ((user_id = (current_setting('app.current_user_id'::text, true))::uuid));

CREATE POLICY "KullanÄ±cÄ± izleme geÃ§miÅŸi oluÅŸturabilir" ON public.media_watch_history FOR INSERT WITH CHECK ((user_id = (current_setting('app.current_user_id'::text, true))::uuid));

CREATE POLICY "KullanÄ±cÄ± kaydedebilir/silebilir" ON public.media_saves USING ((user_id = (current_setting('app.current_user_id'::text, true))::uuid)) WITH CHECK ((user_id = (current_setting('app.current_user_id'::text, true))::uuid));

CREATE POLICY "KullanÄ±cÄ± kayÄ±tlarÄ± gÃ¶rebilir" ON public.media_saves FOR SELECT USING ((user_id = (current_setting('app.current_user_id'::text, true))::uuid));

CREATE POLICY "KullanÄ±cÄ± kendi adÄ±na kupon kullanabilir" ON public.coupon_uses FOR INSERT WITH CHECK ((user_id = (current_setting('app.current_user_id'::text, true))::uuid));

CREATE POLICY "KullanÄ±cÄ± kendi analytics eventini oluÅŸturabilir" ON public.analytics_events FOR INSERT WITH CHECK (((user_id IS NULL) OR (user_id = (current_setting('app.current_user_id'::text, true))::uuid)));

CREATE POLICY "KullanÄ±cÄ± kendi bildirimlerini gÃ¶rebilir" ON public.notifications FOR SELECT USING ((user_id = (current_setting('app.current_user_id'::text, true))::uuid));

CREATE POLICY "KullanÄ±cÄ± kendi cihazlarÄ±nÄ± yÃ¶netebilir" ON public.user_device_tokens USING ((user_id = (current_setting('app.current_user_id'::text, true))::uuid)) WITH CHECK ((user_id = (current_setting('app.current_user_id'::text, true))::uuid));

CREATE POLICY "KullanÄ±cÄ± kendi ilerlemesini gÃ¶rebilir" ON public.lesson_progress FOR SELECT USING ((user_id = (current_setting('app.current_user_id'::text, true))::uuid));

CREATE POLICY "KullanÄ±cÄ± kendi ilerlemesini yÃ¶netebilir" ON public.lesson_progress USING ((user_id = (current_setting('app.current_user_id'::text, true))::uuid)) WITH CHECK ((user_id = (current_setting('app.current_user_id'::text, true))::uuid));

CREATE POLICY "KullanÄ±cÄ± kendi izleme geÃ§miÅŸini gÃ¶rebilir" ON public.media_watch_history FOR SELECT USING ((user_id = (current_setting('app.current_user_id'::text, true))::uuid));

CREATE POLICY "KullanÄ±cÄ± kendi kÃ¼tÃ¼phanesini gÃ¶rebilir" ON public.user_library FOR SELECT USING ((user_id = (current_setting('app.current_user_id'::text, true))::uuid));

CREATE POLICY "KullanÄ±cÄ± kendi maÄŸazasÄ±nÄ± aÃ§abilir" ON public.shops FOR INSERT WITH CHECK ((user_id = (current_setting('app.current_user_id'::text, true))::uuid));

CREATE POLICY "KullanÄ±cÄ± kendi puan geÃ§miÅŸini gÃ¶rebilir" ON public.point_logs FOR SELECT USING ((user_id = (current_setting('app.current_user_id'::text, true))::uuid));

CREATE POLICY "KullanÄ±cÄ± kendi sepetini yÃ¶netebilir" ON public.cart_items USING ((user_id = (current_setting('app.current_user_id'::text, true))::uuid)) WITH CHECK ((user_id = (current_setting('app.current_user_id'::text, true))::uuid));

CREATE POLICY "KullanÄ±cÄ± kendi sorusunu silebilir" ON public.product_qa FOR DELETE USING ((user_id = (current_setting('app.current_user_id'::text, true))::uuid));

CREATE POLICY "KullanÄ±cÄ± kendi takibini silebilir" ON public.subscriptions FOR DELETE USING ((user_id = (current_setting('app.current_user_id'::text, true))::uuid));

CREATE POLICY "KullanÄ±cÄ± kendi takip listesini gÃ¶rebilir" ON public.subscriptions FOR SELECT USING ((user_id = (current_setting('app.current_user_id'::text, true))::uuid));

CREATE POLICY "KullanÄ±cÄ± kendi yarÄ±ÅŸma sonucunu gÃ¶rebilir" ON public.contest_results FOR INSERT WITH CHECK ((user_id = (current_setting('app.current_user_id'::text, true))::uuid));

CREATE POLICY "KullanÄ±cÄ± kendi yorumunu gÃ¼ncelleyebilir" ON public.reviews FOR UPDATE USING ((user_id = (current_setting('app.current_user_id'::text, true))::uuid));

CREATE POLICY "KullanÄ±cÄ± kendi yorumunu silebilir" ON public.reviews FOR DELETE USING ((user_id = (current_setting('app.current_user_id'::text, true))::uuid));

CREATE POLICY "KullanÄ±cÄ± kendi yorumunu yÃ¶netebilir" ON public.media_comments USING ((user_id = (current_setting('app.current_user_id'::text, true))::uuid)) WITH CHECK ((user_id = (current_setting('app.current_user_id'::text, true))::uuid));

CREATE POLICY "KullanÄ±cÄ± kendisi iÃ§in takip oluÅŸturabilir" ON public.subscriptions FOR INSERT WITH CHECK ((user_id = (current_setting('app.current_user_id'::text, true))::uuid));

CREATE POLICY "KullanÄ±cÄ± soru sorabilir" ON public.product_qa FOR INSERT WITH CHECK ((user_id = (current_setting('app.current_user_id'::text, true))::uuid));

CREATE POLICY "KullanÄ±cÄ± yorum yazabilir" ON public.media_comments FOR INSERT WITH CHECK ((user_id = (current_setting('app.current_user_id'::text, true))::uuid));

CREATE POLICY "KullanÄ±cÄ± yorum yazabilir" ON public.reviews FOR INSERT WITH CHECK ((user_id = (current_setting('app.current_user_id'::text, true))::uuid));

CREATE POLICY "Kurs bÃ¶lÃ¼mleri herkese aÃ§Ä±k" ON public.course_sections FOR SELECT USING (true);

CREATE POLICY "Kurs dersleri herkese aÃ§Ä±k" ON public.course_lessons FOR SELECT USING (true);

CREATE POLICY "Liderlik tablosunu herkes gÃ¶rebilir" ON public.user_points FOR SELECT USING (true);

CREATE POLICY "SatÄ±cÄ± kendi analytics verilerini gÃ¶rebilir" ON public.analytics_events FOR SELECT USING ((shop_id IN ( SELECT shops.id
   FROM public.shops
  WHERE (shops.user_id = (current_setting('app.current_user_id'::text, true))::uuid))));

CREATE POLICY "SatÄ±cÄ± kendi Ã¼rÃ¼nlerini yÃ¶netebilir" ON public.products USING ((shop_id IN ( SELECT shops.id
   FROM public.shops
  WHERE (shops.user_id = (current_setting('app.current_user_id'::text, true))::uuid))));

CREATE POLICY "SatÄ±cÄ± kurs bÃ¶lÃ¼mlerini yÃ¶netebilir" ON public.course_sections USING ((course_id IN ( SELECT products.id
   FROM public.products
  WHERE (products.shop_id IN ( SELECT shops.id
           FROM public.shops
          WHERE (shops.user_id = (current_setting('app.current_user_id'::text, true))::uuid))))));

CREATE POLICY "SatÄ±cÄ± kurs derslerini yÃ¶netebilir" ON public.course_lessons USING ((course_section_id IN ( SELECT course_sections.id
   FROM public.course_sections
  WHERE (course_sections.course_id IN ( SELECT courses.id
           FROM public.courses
          WHERE (courses.product_id IN ( SELECT products.id
                   FROM public.products
                  WHERE (products.shop_id IN ( SELECT shops.id
                           FROM public.shops
                          WHERE (shops.user_id = (current_setting('app.current_user_id'::text, true))::uuid))))))))));

CREATE POLICY "SatÄ±cÄ±lar kendi abonelik durumlarÄ±nÄ± gÃ¶rebilir" ON public.seller_subscriptions FOR SELECT USING ((shop_id IN ( SELECT shops.id
   FROM public.shops
  WHERE (shops.user_id = (current_setting('app.current_user_id'::text, true))::uuid))));

CREATE POLICY "SatÄ±cÄ±lar kendi gelir dÃ¶kÃ¼mlerini gÃ¶rebilir" ON public.payments FOR SELECT USING ((order_id IN ( SELECT orders.id
   FROM public.orders
  WHERE (orders.shop_id IN ( SELECT shops.id
           FROM public.shops
          WHERE (shops.user_id = (current_setting('app.current_user_id'::text, true))::uuid))))));

CREATE POLICY "SatÄ±cÄ±lar kendi maÄŸaza sipariÅŸlerini gÃ¶rebilir" ON public.orders FOR SELECT USING ((shop_id IN ( SELECT shops.id
   FROM public.shops
  WHERE (shops.user_id = (current_setting('app.current_user_id'::text, true))::uuid))));

CREATE POLICY "Sorular herkese aÃ§Ä±k" ON public.product_qa FOR SELECT USING (true);

CREATE POLICY "YarÄ±ÅŸma sonuÃ§larÄ± herkese aÃ§Ä±k" ON public.contest_results FOR SELECT USING (true);

CREATE POLICY "YarÄ±ÅŸmalar herkese aÃ§Ä±k" ON public.contests FOR SELECT USING ((is_active = true));

CREATE POLICY "Yorumlar herkese aÃ§Ä±k" ON public.reviews FOR SELECT USING (true);

CREATE POLICY "YorumlarÄ± herkes okuyabilir" ON public.media_comments FOR SELECT USING (true);

CREATE POLICY ip_login_attempts_backend_only ON public.ip_login_attempts USING (false);

CREATE POLICY login_attempts_backend_only ON public.login_attempts USING (false);

CREATE POLICY media_select_active ON public.media FOR SELECT USING ((is_active = true));

CREATE POLICY notification_deliveries_backend_only ON public.notification_deliveries USING (false);

CREATE POLICY support_ticket_messages_admin_insert ON public.support_ticket_messages FOR INSERT WITH CHECK (((public.is_current_app_admin() AND (sender_id = (current_setting('app.current_user_id'::text, true))::uuid)) AND (sender_role = 'admin'::public.support_message_sender_role)));

CREATE POLICY support_ticket_messages_admin_select ON public.support_ticket_messages FOR SELECT USING (public.is_current_app_admin());

CREATE POLICY support_ticket_messages_insert_own ON public.support_ticket_messages FOR INSERT WITH CHECK (((sender_id = (current_setting('app.current_user_id'::text, true))::uuid) AND (sender_role = 'user'::public.support_message_sender_role) AND (EXISTS ( SELECT 1
   FROM public.support_tickets ticket
  WHERE ((ticket.id = support_ticket_messages.ticket_id) AND (ticket.user_id = (current_setting('app.current_user_id'::text, true))::uuid))))));

CREATE POLICY support_ticket_messages_select_own ON public.support_ticket_messages FOR SELECT USING ((EXISTS ( SELECT 1
   FROM public.support_tickets ticket
  WHERE ((ticket.id = support_ticket_messages.ticket_id) AND (ticket.user_id = (current_setting('app.current_user_id'::text, true))::uuid)))));

CREATE POLICY support_tickets_admin_select ON public.support_tickets FOR SELECT USING (public.is_current_app_admin());

CREATE POLICY support_tickets_admin_update ON public.support_tickets FOR UPDATE USING (public.is_current_app_admin()) WITH CHECK (public.is_current_app_admin());

CREATE POLICY support_tickets_insert_own ON public.support_tickets FOR INSERT WITH CHECK ((user_id = (current_setting('app.current_user_id'::text, true))::uuid));

CREATE POLICY support_tickets_select_own ON public.support_tickets FOR SELECT USING ((user_id = (current_setting('app.current_user_id'::text, true))::uuid));

CREATE POLICY support_tickets_update_own ON public.support_tickets FOR UPDATE USING ((user_id = (current_setting('app.current_user_id'::text, true))::uuid)) WITH CHECK ((user_id = (current_setting('app.current_user_id'::text, true))::uuid));

CREATE POLICY sessions_delete_own ON public.user_sessions FOR DELETE USING ((user_id = (current_setting('app.current_user_id'::text, true))::uuid));

CREATE POLICY sessions_select_own ON public.user_sessions FOR SELECT USING ((user_id = (current_setting('app.current_user_id'::text, true))::uuid));

CREATE POLICY shops_select_active ON public.shops FOR SELECT USING ((is_active = true));

CREATE POLICY shops_update_owner ON public.shops FOR UPDATE USING ((user_id = (current_setting('app.current_user_id'::text, true))::uuid));

CREATE POLICY users_update_own ON public.users FOR UPDATE USING ((id = (current_setting('app.current_user_id'::text, true))::uuid));


-- =========================================================================
-- FOREIGN KEYS
-- =========================================================================

ALTER TABLE ONLY public.admin_audit_logs
    ADD CONSTRAINT admin_audit_logs_admin_user_id_fkey FOREIGN KEY (admin_user_id) REFERENCES public.users(id) ON DELETE SET NULL;

ALTER TABLE ONLY public.admin_competition_rewards
    ADD CONSTRAINT admin_competition_rewards_contest_id_fkey FOREIGN KEY (contest_id) REFERENCES public.contests(id) ON DELETE CASCADE;

ALTER TABLE ONLY public.admin_competition_rewards
    ADD CONSTRAINT admin_competition_rewards_user_id_fkey FOREIGN KEY (user_id) REFERENCES public.users(id) ON DELETE CASCADE;

ALTER TABLE ONLY public.admin_reports
    ADD CONSTRAINT admin_reports_reported_by_user_id_fkey FOREIGN KEY (reported_by_user_id) REFERENCES public.users(id) ON DELETE SET NULL;

ALTER TABLE ONLY public.admin_warnings
    ADD CONSTRAINT admin_warnings_admin_user_id_fkey FOREIGN KEY (admin_user_id) REFERENCES public.users(id) ON DELETE SET NULL;

ALTER TABLE ONLY public.admin_warnings
    ADD CONSTRAINT admin_warnings_user_id_fkey FOREIGN KEY (user_id) REFERENCES public.users(id) ON DELETE CASCADE;

ALTER TABLE ONLY public.analytics_events
    ADD CONSTRAINT analytics_events_order_id_fkey FOREIGN KEY (order_id) REFERENCES public.orders(id) ON DELETE SET NULL;

ALTER TABLE ONLY public.analytics_events
    ADD CONSTRAINT analytics_events_product_id_fkey FOREIGN KEY (product_id) REFERENCES public.products(id) ON DELETE SET NULL;

ALTER TABLE ONLY public.analytics_events
    ADD CONSTRAINT analytics_events_media_id_fkey FOREIGN KEY (media_id) REFERENCES public.media(id) ON DELETE SET NULL;

ALTER TABLE ONLY public.analytics_events
    ADD CONSTRAINT analytics_events_shop_id_fkey FOREIGN KEY (shop_id) REFERENCES public.shops(id) ON DELETE CASCADE;

ALTER TABLE ONLY public.analytics_events
    ADD CONSTRAINT analytics_events_user_id_fkey FOREIGN KEY (user_id) REFERENCES public.users(id) ON DELETE SET NULL;

ALTER TABLE ONLY public.cart_items
    ADD CONSTRAINT cart_items_product_id_fkey FOREIGN KEY (product_id) REFERENCES public.products(id) ON DELETE CASCADE;

ALTER TABLE ONLY public.cart_items
    ADD CONSTRAINT cart_items_user_id_fkey FOREIGN KEY (user_id) REFERENCES public.users(id) ON DELETE CASCADE;

ALTER TABLE ONLY public.categories
    ADD CONSTRAINT categories_parent_id_fkey FOREIGN KEY (parent_id) REFERENCES public.categories(id);

ALTER TABLE ONLY public.contest_results
    ADD CONSTRAINT contest_results_contest_id_fkey FOREIGN KEY (contest_id) REFERENCES public.contests(id) ON DELETE CASCADE;

ALTER TABLE ONLY public.contest_results
    ADD CONSTRAINT contest_results_user_id_fkey FOREIGN KEY (user_id) REFERENCES public.users(id) ON DELETE CASCADE;

ALTER TABLE ONLY public.contests
    ADD CONSTRAINT contests_created_by_fkey FOREIGN KEY (created_by) REFERENCES public.users(id) ON DELETE SET NULL;

ALTER TABLE ONLY public.coupon_uses
    ADD CONSTRAINT coupon_uses_coupon_id_fkey FOREIGN KEY (coupon_id) REFERENCES public.coupons(id) ON DELETE CASCADE;

ALTER TABLE ONLY public.coupon_uses
    ADD CONSTRAINT coupon_uses_order_id_fkey FOREIGN KEY (order_id) REFERENCES public.orders(id) ON DELETE CASCADE;

ALTER TABLE ONLY public.coupon_uses
    ADD CONSTRAINT coupon_uses_user_id_fkey FOREIGN KEY (user_id) REFERENCES public.users(id) ON DELETE CASCADE;

ALTER TABLE ONLY public.coupons
    ADD CONSTRAINT coupons_product_id_fkey FOREIGN KEY (product_id) REFERENCES public.products(id) ON DELETE CASCADE;

ALTER TABLE ONLY public.coupons
    ADD CONSTRAINT coupons_shop_id_fkey FOREIGN KEY (shop_id) REFERENCES public.shops(id) ON DELETE CASCADE;

ALTER TABLE ONLY public.course_lessons
    ADD CONSTRAINT course_lessons_section_id_fkey FOREIGN KEY (course_section_id) REFERENCES public.course_sections(id) ON DELETE CASCADE;

ALTER TABLE ONLY public.course_quizzes
    ADD CONSTRAINT course_quizzes_section_id_fkey FOREIGN KEY (course_section_id) REFERENCES public.course_sections(id) ON DELETE CASCADE;

ALTER TABLE ONLY public.course_sections
    ADD CONSTRAINT course_sections_course_id_fkey FOREIGN KEY (course_id) REFERENCES public.courses(id) ON DELETE CASCADE;

ALTER TABLE ONLY public.courses
    ADD CONSTRAINT courses_product_id_fkey FOREIGN KEY (product_id) REFERENCES public.products(id) ON DELETE CASCADE;

ALTER TABLE ONLY public.lesson_progress
    ADD CONSTRAINT lesson_progress_lesson_id_fkey FOREIGN KEY (lesson_id) REFERENCES public.course_lessons(id) ON DELETE CASCADE;

ALTER TABLE ONLY public.lesson_progress
    ADD CONSTRAINT lesson_progress_user_id_fkey FOREIGN KEY (user_id) REFERENCES public.users(id) ON DELETE CASCADE;

ALTER TABLE ONLY public.lesson_resources
    ADD CONSTRAINT lesson_resources_lesson_id_fkey FOREIGN KEY (course_lesson_id) REFERENCES public.course_lessons(id) ON DELETE CASCADE;

ALTER TABLE ONLY public.media_comments
    ADD CONSTRAINT media_comments_media_id_fkey FOREIGN KEY (media_id) REFERENCES public.media(id) ON DELETE CASCADE;

ALTER TABLE ONLY public.media_comments
    ADD CONSTRAINT media_comments_parent_comment_id_fkey FOREIGN KEY (parent_comment_id) REFERENCES public.media_comments(id) ON DELETE CASCADE;

ALTER TABLE ONLY public.media_comments
    ADD CONSTRAINT media_comments_user_id_fkey FOREIGN KEY (user_id) REFERENCES public.users(id) ON DELETE CASCADE;

ALTER TABLE ONLY public.media_likes
    ADD CONSTRAINT media_likes_media_id_fkey FOREIGN KEY (media_id) REFERENCES public.media(id) ON DELETE CASCADE;

ALTER TABLE ONLY public.media_likes
    ADD CONSTRAINT media_likes_user_id_fkey FOREIGN KEY (user_id) REFERENCES public.users(id) ON DELETE CASCADE;

ALTER TABLE ONLY public.media
    ADD CONSTRAINT media_product_id_fkey FOREIGN KEY (product_id) REFERENCES public.products(id) ON DELETE SET NULL;

ALTER TABLE ONLY public.media_saves
    ADD CONSTRAINT media_saves_media_id_fkey FOREIGN KEY (media_id) REFERENCES public.media(id) ON DELETE CASCADE;

ALTER TABLE ONLY public.media_saves
    ADD CONSTRAINT media_saves_user_id_fkey FOREIGN KEY (user_id) REFERENCES public.users(id) ON DELETE CASCADE;

ALTER TABLE ONLY public.media
    ADD CONSTRAINT media_shop_id_fkey FOREIGN KEY (shop_id) REFERENCES public.shops(id) ON DELETE CASCADE;

ALTER TABLE ONLY public.media_watch_history
    ADD CONSTRAINT media_watch_history_media_id_fkey FOREIGN KEY (media_id) REFERENCES public.media(id) ON DELETE CASCADE;

ALTER TABLE ONLY public.media_watch_history
    ADD CONSTRAINT media_watch_history_user_id_fkey FOREIGN KEY (user_id) REFERENCES public.users(id) ON DELETE CASCADE;

ALTER TABLE ONLY public.notification_deliveries
    ADD CONSTRAINT notification_deliveries_notification_id_fkey FOREIGN KEY (notification_id) REFERENCES public.notifications(id) ON DELETE CASCADE;

ALTER TABLE ONLY public.notifications
    ADD CONSTRAINT notifications_user_id_fkey FOREIGN KEY (user_id) REFERENCES public.users(id) ON DELETE CASCADE;

ALTER TABLE ONLY public.orders
    ADD CONSTRAINT orders_buyer_id_fkey FOREIGN KEY (buyer_id) REFERENCES public.users(id) ON DELETE RESTRICT;

ALTER TABLE ONLY public.orders
    ADD CONSTRAINT orders_product_id_fkey FOREIGN KEY (product_id) REFERENCES public.products(id) ON DELETE RESTRICT;

ALTER TABLE ONLY public.orders
    ADD CONSTRAINT orders_shop_id_fkey FOREIGN KEY (shop_id) REFERENCES public.shops(id) ON DELETE RESTRICT;

ALTER TABLE ONLY public.payments
    ADD CONSTRAINT payments_order_id_fkey FOREIGN KEY (order_id) REFERENCES public.orders(id) ON DELETE RESTRICT;

ALTER TABLE ONLY public.point_logs
    ADD CONSTRAINT point_logs_user_id_fkey FOREIGN KEY (user_id) REFERENCES public.users(id) ON DELETE CASCADE;

ALTER TABLE ONLY public.product_images
    ADD CONSTRAINT product_images_product_id_fkey FOREIGN KEY (product_id) REFERENCES public.products(id) ON DELETE CASCADE;

ALTER TABLE ONLY public.product_qa
    ADD CONSTRAINT product_qa_parent_id_fkey FOREIGN KEY (parent_id) REFERENCES public.product_qa(id) ON DELETE CASCADE;

ALTER TABLE ONLY public.product_qa
    ADD CONSTRAINT product_qa_product_id_fkey FOREIGN KEY (product_id) REFERENCES public.products(id) ON DELETE CASCADE;

ALTER TABLE ONLY public.product_qa
    ADD CONSTRAINT product_qa_user_id_fkey FOREIGN KEY (user_id) REFERENCES public.users(id) ON DELETE CASCADE;

ALTER TABLE ONLY public.products
    ADD CONSTRAINT products_category_id_fkey FOREIGN KEY (category_id) REFERENCES public.categories(id) ON DELETE CASCADE;

ALTER TABLE ONLY public.products
    ADD CONSTRAINT products_shop_id_fkey FOREIGN KEY (shop_id) REFERENCES public.shops(id) ON DELETE CASCADE;

ALTER TABLE ONLY public.reviews
    ADD CONSTRAINT reviews_product_id_fkey FOREIGN KEY (product_id) REFERENCES public.products(id) ON DELETE CASCADE;

ALTER TABLE ONLY public.reviews
    ADD CONSTRAINT reviews_user_id_fkey FOREIGN KEY (user_id) REFERENCES public.users(id) ON DELETE CASCADE;

ALTER TABLE ONLY public.seller_subscriptions
    ADD CONSTRAINT seller_subscriptions_shop_id_fkey FOREIGN KEY (shop_id) REFERENCES public.shops(id) ON DELETE CASCADE;

ALTER TABLE ONLY public.shop_visits
    ADD CONSTRAINT shop_visits_shop_id_fkey FOREIGN KEY (shop_id) REFERENCES public.shops(id) ON DELETE CASCADE;

ALTER TABLE ONLY public.shop_visits
    ADD CONSTRAINT shop_visits_user_id_fkey FOREIGN KEY (user_id) REFERENCES public.users(id) ON DELETE SET NULL;

ALTER TABLE ONLY public.shops
    ADD CONSTRAINT shops_user_id_fkey FOREIGN KEY (user_id) REFERENCES public.users(id) ON DELETE CASCADE;

ALTER TABLE ONLY public.subscriptions
    ADD CONSTRAINT subscriptions_shop_id_fkey FOREIGN KEY (shop_id) REFERENCES public.shops(id) ON DELETE CASCADE;

ALTER TABLE ONLY public.subscriptions
    ADD CONSTRAINT subscriptions_user_id_fkey FOREIGN KEY (user_id) REFERENCES public.users(id) ON DELETE CASCADE;

ALTER TABLE ONLY public.support_ticket_messages
    ADD CONSTRAINT support_ticket_messages_sender_id_fkey FOREIGN KEY (sender_id) REFERENCES public.users(id) ON DELETE RESTRICT;

ALTER TABLE ONLY public.support_ticket_messages
    ADD CONSTRAINT support_ticket_messages_ticket_id_fkey FOREIGN KEY (ticket_id) REFERENCES public.support_tickets(id) ON DELETE CASCADE;

ALTER TABLE ONLY public.support_tickets
    ADD CONSTRAINT support_tickets_closed_by_user_id_fkey FOREIGN KEY (closed_by_user_id) REFERENCES public.users(id) ON DELETE SET NULL;

ALTER TABLE ONLY public.support_tickets
    ADD CONSTRAINT support_tickets_user_id_fkey FOREIGN KEY (user_id) REFERENCES public.users(id) ON DELETE RESTRICT;

ALTER TABLE ONLY public.user_device_tokens
    ADD CONSTRAINT user_device_tokens_user_id_fkey FOREIGN KEY (user_id) REFERENCES public.users(id) ON DELETE CASCADE;

ALTER TABLE ONLY public.user_lesson_progress
    ADD CONSTRAINT user_lesson_progress_course_lesson_id_fkey FOREIGN KEY (course_lesson_id) REFERENCES public.course_lessons(id) ON DELETE CASCADE;

ALTER TABLE ONLY public.user_lesson_progress
    ADD CONSTRAINT user_lesson_progress_user_id_fkey FOREIGN KEY (user_id) REFERENCES public.users(id) ON DELETE CASCADE;

ALTER TABLE ONLY public.user_library
    ADD CONSTRAINT user_library_product_id_fkey FOREIGN KEY (product_id) REFERENCES public.products(id) ON DELETE CASCADE;

ALTER TABLE ONLY public.user_library
    ADD CONSTRAINT user_library_user_id_fkey FOREIGN KEY (user_id) REFERENCES public.users(id) ON DELETE CASCADE;

ALTER TABLE ONLY public.user_points
    ADD CONSTRAINT user_points_user_id_fkey FOREIGN KEY (user_id) REFERENCES public.users(id) ON DELETE CASCADE;

ALTER TABLE ONLY public.user_sessions
    ADD CONSTRAINT user_sessions_user_id_fkey FOREIGN KEY (user_id) REFERENCES public.users(id) ON DELETE CASCADE;


COMMIT;

