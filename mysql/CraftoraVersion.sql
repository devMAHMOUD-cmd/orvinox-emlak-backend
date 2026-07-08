--
-- PostgreSQL database dump
--

\restrict f4DQKSmXG9YrRWYUUWnZ90QjTuzcm0Xo0GxqzacGiaAKiM5i0Dkm6lRCnpfsNkn

-- Dumped from database version 16.14 (Debian 16.14-1.pgdg13+1)
-- Dumped by pg_dump version 17.6

-- Started on 2026-07-05 23:15:47

SET statement_timeout = 0;
SET lock_timeout = 0;
SET idle_in_transaction_session_timeout = 0;
SET transaction_timeout = 0;
SET client_encoding = 'UTF8';
SET standard_conforming_strings = on;
SELECT pg_catalog.set_config('search_path', '', false);
SET check_function_bodies = false;
SET xmloption = content;
SET client_min_messages = warning;
SET row_security = off;

--
-- TOC entry 2 (class 3079 OID 16513)
-- Name: citext; Type: EXTENSION; Schema: -; Owner: -
--

CREATE EXTENSION IF NOT EXISTS citext WITH SCHEMA public;


--
-- TOC entry 4328 (class 0 OID 0)
-- Dependencies: 2
-- Name: EXTENSION citext; Type: COMMENT; Schema: -; Owner: 
--

COMMENT ON EXTENSION citext IS 'data type for case-insensitive character strings';


--
-- TOC entry 3 (class 3079 OID 16618)
-- Name: uuid-ossp; Type: EXTENSION; Schema: -; Owner: -
--

CREATE EXTENSION IF NOT EXISTS "uuid-ossp" WITH SCHEMA public;


--
-- TOC entry 4329 (class 0 OID 0)
-- Dependencies: 3
-- Name: EXTENSION "uuid-ossp"; Type: COMMENT; Schema: -; Owner: 
--

COMMENT ON EXTENSION "uuid-ossp" IS 'generate universally unique identifiers (UUIDs)';


--
-- TOC entry 1093 (class 1247 OID 54335)
-- Name: analytics_event_type; Type: TYPE; Schema: public; Owner: admin
--

CREATE TYPE public.analytics_event_type AS ENUM (
    'shop_visit',
    'product_view',
    'add_to_cart',
    'checkout_started',
    'purchase_completed',
    'download_clicked'
);


ALTER TYPE public.analytics_event_type OWNER TO admin;

--
-- TOC entry 955 (class 1247 OID 16462)
-- Name: media_status; Type: TYPE; Schema: public; Owner: admin
--

CREATE TYPE public.media_status AS ENUM (
    'failed',
    'processing',
    'ready'
);


ALTER TYPE public.media_status OWNER TO admin;

--
-- TOC entry 958 (class 1247 OID 16470)
-- Name: order_status; Type: TYPE; Schema: public; Owner: admin
--

CREATE TYPE public.order_status AS ENUM (
    'completed',
    'failed',
    'pending',
    'refunded'
);


ALTER TYPE public.order_status OWNER TO admin;

--
-- TOC entry 961 (class 1247 OID 16480)
-- Name: payment_status_type; Type: TYPE; Schema: public; Owner: admin
--

CREATE TYPE public.payment_status_type AS ENUM (
    'failed',
    'processing',
    'refunded',
    'succeeded'
);


ALTER TYPE public.payment_status_type OWNER TO admin;

--
-- TOC entry 964 (class 1247 OID 16490)
-- Name: product_type; Type: TYPE; Schema: public; Owner: admin
--

CREATE TYPE public.product_type AS ENUM (
    'course',
    'digital_file'
);


ALTER TYPE public.product_type OWNER TO admin;

--
-- TOC entry 967 (class 1247 OID 16496)
-- Name: sub_status; Type: TYPE; Schema: public; Owner: admin
--

CREATE TYPE public.sub_status AS ENUM (
    'active',
    'canceled',
    'past_due',
    'unpaid'
);


ALTER TYPE public.sub_status OWNER TO admin;

--
-- TOC entry 970 (class 1247 OID 16506)
-- Name: user_role; Type: TYPE; Schema: public; Owner: admin
--

CREATE TYPE public.user_role AS ENUM (
    'admin',
    'seller',
    'user'
);


ALTER TYPE public.user_role OWNER TO admin;

--
-- TOC entry 335 (class 1255 OID 72457)
-- Name: award_seller_points(); Type: FUNCTION; Schema: public; Owner: admin
--

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
    VALUES (v_seller_id, 'receive_like', 0.5, NEW.media_id);
    
    INSERT INTO user_points (user_id, total_points) 
    VALUES (v_seller_id, 0.5)
    ON CONFLICT (user_id) DO UPDATE 
    SET total_points = user_points.total_points + 0.5, 
        updated_at = CURRENT_TIMESTAMP;
    
    RETURN NEW;
END;
$$;


ALTER FUNCTION public.award_seller_points() OWNER TO admin;

--
-- TOC entry 307 (class 1255 OID 72459)
-- Name: award_viewer_points(); Type: FUNCTION; Schema: public; Owner: admin
--

CREATE FUNCTION public.award_viewer_points() RETURNS trigger
    LANGUAGE plpgsql SECURITY DEFINER
    SET search_path TO 'public'
    AS $$
DECLARE v_daily_points DECIMAL;
BEGIN
    SELECT COALESCE(SUM(points_earned), 0) INTO v_daily_points 
    FROM point_logs 
    WHERE user_id = NEW.user_id 
      AND action_type = 'watch_reels' 
      AND created_at::date = CURRENT_DATE;

    IF v_daily_points < 120 THEN
        INSERT INTO point_logs (user_id, action_type, points_earned, reference_id) 
        VALUES (NEW.user_id, 'watch_reels', 1.0, NEW.media_id);
        
        INSERT INTO user_points (user_id, total_points) 
        VALUES (NEW.user_id, 1.0)
        ON CONFLICT (user_id) DO UPDATE 
        SET total_points = user_points.total_points + 1.0, 
            updated_at = CURRENT_TIMESTAMP;
        
        NEW.is_point_earned := TRUE;
    END IF;
    RETURN NEW;
END;
$$;


ALTER FUNCTION public.award_viewer_points() OWNER TO admin;

--
-- TOC entry 326 (class 1255 OID 26279)
-- Name: cleanup_old_shop_visits(); Type: FUNCTION; Schema: public; Owner: admin
--

CREATE FUNCTION public.cleanup_old_shop_visits() RETURNS void
    LANGUAGE plpgsql
    AS $$
BEGIN
    DELETE FROM shop_visits 
    WHERE visited_at < NOW() - INTERVAL '90 days';
END;
$$;


ALTER FUNCTION public.cleanup_old_shop_visits() OWNER TO admin;

--
-- TOC entry 311 (class 1255 OID 17355)
-- Name: deliver_product_to_library(); Type: FUNCTION; Schema: public; Owner: admin
--

CREATE FUNCTION public.deliver_product_to_library() RETURNS trigger
    LANGUAGE plpgsql SECURITY DEFINER
    SET search_path TO 'public'
    AS $$
BEGIN
    -- Sipariş 'completed' olunca ürünü kütüphaneye ekle
    IF (NEW.status = 'completed' AND (TG_OP = 'INSERT' OR OLD.status != 'completed')) THEN
        INSERT INTO user_library (user_id, product_id)
        VALUES (NEW.buyer_id, NEW.product_id)
        ON CONFLICT (user_id, product_id) DO NOTHING;
        
    -- Sipariş 'refunded' olunca ürünü kütüphaneden ÇIKAR
    ELSIF (NEW.status = 'refunded' AND TG_OP = 'UPDATE' AND OLD.status != 'refunded') THEN
        DELETE FROM user_library 
        WHERE user_id = NEW.buyer_id AND product_id = NEW.product_id;
    END IF;
    
    RETURN NEW;
END;
$$;


ALTER FUNCTION public.deliver_product_to_library() OWNER TO admin;

--
-- TOC entry 319 (class 1255 OID 17359)
-- Name: increment_coupon_usage(); Type: FUNCTION; Schema: public; Owner: admin
--

CREATE FUNCTION public.increment_coupon_usage() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
    UPDATE coupons SET used_count = used_count + 1 WHERE id = NEW.coupon_id;
    RETURN NEW;
END;
$$;


ALTER FUNCTION public.increment_coupon_usage() OWNER TO admin;

--
-- TOC entry 328 (class 1255 OID 54389)
-- Name: normalize_analytics_event_shop_id(); Type: FUNCTION; Schema: public; Owner: admin
--

CREATE FUNCTION public.normalize_analytics_event_shop_id() RETURNS trigger
    LANGUAGE plpgsql SECURITY DEFINER
    SET search_path TO 'public'
    AS $$
DECLARE
    v_product_shop_id UUID;
    v_order_shop_id UUID;
BEGIN
    -- product_id varsa ürünün gerçek shop_id'sini bul
    IF NEW.product_id IS NOT NULL THEN
        SELECT shop_id INTO v_product_shop_id
        FROM products
        WHERE id = NEW.product_id;

        IF v_product_shop_id IS NULL THEN
            RAISE EXCEPTION 'Analytics event product_id geçersiz: %', NEW.product_id;
        END IF;

        NEW.shop_id := v_product_shop_id;
    END IF;

    -- order_id varsa order'ın gerçek shop_id'sini bul
    IF NEW.order_id IS NOT NULL THEN
        SELECT shop_id INTO v_order_shop_id
        FROM orders
        WHERE id = NEW.order_id;

        IF v_order_shop_id IS NULL THEN
            RAISE EXCEPTION 'Analytics event order_id geçersiz: %', NEW.order_id;
        END IF;

        NEW.shop_id := v_order_shop_id;
    END IF;

    RETURN NEW;
END;
$$;


ALTER FUNCTION public.normalize_analytics_event_shop_id() OWNER TO admin;

--
-- TOC entry 271 (class 1255 OID 17358)
-- Name: prevent_duplicate_purchase(); Type: FUNCTION; Schema: public; Owner: admin
--

CREATE FUNCTION public.prevent_duplicate_purchase() RETURNS trigger
    LANGUAGE plpgsql SECURITY DEFINER
    SET search_path TO 'public'
    AS $$
BEGIN
    -- Kullanıcının kütüphanesinde bu ürün var mı?
    IF EXISTS (
        SELECT 1 FROM user_library 
        WHERE user_id = NEW.user_id AND product_id = NEW.product_id
    ) THEN
        RAISE EXCEPTION 'Bu ürün zaten kütüphanenizde mevcut!';
    END IF;
    RETURN NEW;
END;
$$;


ALTER FUNCTION public.prevent_duplicate_purchase() OWNER TO admin;

--
-- TOC entry 331 (class 1255 OID 17356)
-- Name: process_completed_order(); Type: FUNCTION; Schema: public; Owner: admin
--

CREATE FUNCTION public.process_completed_order() RETURNS trigger
    LANGUAGE plpgsql SECURITY DEFINER
    SET search_path TO 'public'
    AS $$
DECLARE 
    v_seller_id UUID;
BEGIN
    -- Satıcıyı bul
    SELECT user_id INTO v_seller_id FROM shops WHERE id = NEW.shop_id;

    -- ============ SİPARİŞ TAMAMLANDI ============
    IF (NEW.status = 'completed' AND (TG_OP = 'INSERT' OR OLD.status != 'completed')) THEN
        
        -- 1. Ürün satış sayacını artır
        UPDATE products SET sales_count = sales_count + 1 WHERE id = NEW.product_id;
        
        -- 2. Satıcıya 20 puan (UPSERT ile)
        INSERT INTO point_logs (user_id, action_type, points_earned, reference_id) 
        VALUES (v_seller_id, 'make_sale', 20.0, NEW.id);
        
        INSERT INTO user_points (user_id, total_points) 
        VALUES (v_seller_id, 20.0)
        ON CONFLICT (user_id) DO UPDATE 
        SET total_points = user_points.total_points + 20.0, 
            updated_at = CURRENT_TIMESTAMP;
    
    -- ============ SİPARİŞ İADE EDİLDİ (REFUND) ============
    ELSIF (NEW.status = 'refunded' AND TG_OP = 'UPDATE' AND OLD.status != 'refunded') THEN
        
        -- 1. Satış sayacını geri al (negatif korumalı)
        UPDATE products SET sales_count = GREATEST(sales_count - 1, 0) WHERE id = NEW.product_id;
        
        -- 2. Satıcının puanını geri al
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


ALTER FUNCTION public.process_completed_order() OWNER TO admin;

--
-- TOC entry 321 (class 1255 OID 80656)
-- Name: reward_lesson_completion(); Type: FUNCTION; Schema: public; Owner: admin
--

CREATE FUNCTION public.reward_lesson_completion() RETURNS trigger
    LANGUAGE plpgsql SECURITY DEFINER
    SET search_path TO 'public'
    AS $$
BEGIN
    -- Ders ilk kez tamamlandıysa (önceden false, şimdi true)
    IF (NEW.is_completed = TRUE AND OLD.is_completed = FALSE) THEN
        
        -- Öğrenciye 2 puan ver
        INSERT INTO point_logs (user_id, action_type, points_earned, reference_id)
        VALUES (NEW.user_id, 'complete_lesson', 2.0, NEW.lesson_id);
        
        INSERT INTO user_points (user_id, total_points) 
        VALUES (NEW.user_id, 2.0)
        ON CONFLICT (user_id) DO UPDATE 
        SET total_points = user_points.total_points + 2.0, 
            updated_at = CURRENT_TIMESTAMP;
        
        -- Tamamlanma saatini kaydet
        NEW.completed_at = CURRENT_TIMESTAMP;
    END IF;
    RETURN NEW;
END;
$$;


ALTER FUNCTION public.reward_lesson_completion() OWNER TO admin;

--
-- TOC entry 280 (class 1255 OID 17353)
-- Name: sync_follower_count(); Type: FUNCTION; Schema: public; Owner: admin
--

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


ALTER FUNCTION public.sync_follower_count() OWNER TO admin;

--
-- TOC entry 267 (class 1255 OID 17354)
-- Name: sync_media_counters(); Type: FUNCTION; Schema: public; Owner: admin
--

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


ALTER FUNCTION public.sync_media_counters() OWNER TO admin;

--
-- TOC entry 313 (class 1255 OID 17357)
-- Name: sync_order_status_from_payment(); Type: FUNCTION; Schema: public; Owner: admin
--

CREATE FUNCTION public.sync_order_status_from_payment() RETURNS trigger
    LANGUAGE plpgsql SECURITY DEFINER
    SET search_path TO 'public'
    AS $$
BEGIN
    -- Ödeme başarılı → siparişi tamamla
    IF (NEW.status = 'succeeded' AND (TG_OP = 'INSERT' OR OLD.status != 'succeeded')) THEN
        UPDATE orders SET status = 'completed' WHERE id = NEW.order_id;
        
    -- Ödeme iade → siparişi iade et
    ELSIF (NEW.status = 'refunded' AND OLD.status != 'refunded') THEN
        UPDATE orders SET status = 'refunded' WHERE id = NEW.order_id;
    END IF;
    
    RETURN NEW;
END;
$$;


ALTER FUNCTION public.sync_order_status_from_payment() OWNER TO admin;

--
-- TOC entry 281 (class 1255 OID 17352)
-- Name: update_updated_at_column(); Type: FUNCTION; Schema: public; Owner: admin
--

CREATE FUNCTION public.update_updated_at_column() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
    NEW.updated_at = CURRENT_TIMESTAMP;
    RETURN NEW;
END;
$$;


ALTER FUNCTION public.update_updated_at_column() OWNER TO admin;

SET default_tablespace = '';

SET default_table_access_method = heap;

--
-- TOC entry 253 (class 1259 OID 17346)
-- Name: __EFMigrationsHistory; Type: TABLE; Schema: public; Owner: admin
--

CREATE TABLE public."__EFMigrationsHistory" (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL
);


ALTER TABLE public."__EFMigrationsHistory" OWNER TO admin;

--
-- TOC entry 259 (class 1259 OID 64248)
-- Name: admin_audit_logs; Type: TABLE; Schema: public; Owner: admin
--

CREATE TABLE public.admin_audit_logs (
    id uuid DEFAULT public.uuid_generate_v4() NOT NULL,
    admin_user_id uuid,
    action character varying(100) NOT NULL,
    target_type character varying(50) NOT NULL,
    target_id uuid,
    metadata jsonb DEFAULT '{}'::jsonb,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP
);


ALTER TABLE public.admin_audit_logs OWNER TO admin;

--
-- TOC entry 262 (class 1259 OID 64284)
-- Name: admin_competition_rewards; Type: TABLE; Schema: public; Owner: admin
--

CREATE TABLE public.admin_competition_rewards (
    id uuid DEFAULT public.uuid_generate_v4() NOT NULL,
    contest_id uuid NOT NULL,
    user_id uuid NOT NULL,
    rank integer NOT NULL,
    reward_type character varying(50) NOT NULL,
    amount numeric(12,2),
    currency character varying(3),
    note text,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP
);


ALTER TABLE public.admin_competition_rewards OWNER TO admin;

--
-- TOC entry 257 (class 1259 OID 64213)
-- Name: admin_reports; Type: TABLE; Schema: public; Owner: admin
--

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


ALTER TABLE public.admin_reports OWNER TO admin;

--
-- TOC entry 258 (class 1259 OID 64229)
-- Name: admin_warnings; Type: TABLE; Schema: public; Owner: admin
--

CREATE TABLE public.admin_warnings (
    id uuid DEFAULT public.uuid_generate_v4() NOT NULL,
    user_id uuid NOT NULL,
    admin_user_id uuid,
    title character varying(255) NOT NULL,
    message text NOT NULL,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP
);


ALTER TABLE public.admin_warnings OWNER TO admin;

--
-- TOC entry 256 (class 1259 OID 54347)
-- Name: analytics_events; Type: TABLE; Schema: public; Owner: admin
--

CREATE TABLE public.analytics_events (
    id uuid DEFAULT public.uuid_generate_v4() NOT NULL,
    shop_id uuid NOT NULL,
    product_id uuid,
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
    CONSTRAINT check_analytics_product_events CHECK (((event_type <> ALL (ARRAY['product_view'::public.analytics_event_type, 'add_to_cart'::public.analytics_event_type, 'download_clicked'::public.analytics_event_type])) OR (product_id IS NOT NULL))),
    CONSTRAINT check_analytics_session_or_user CHECK (((user_id IS NOT NULL) OR (session_id IS NOT NULL) OR (ip_address IS NOT NULL)))
);


ALTER TABLE public.analytics_events OWNER TO admin;

--
-- TOC entry 233 (class 1259 OID 16890)
-- Name: cart_items; Type: TABLE; Schema: public; Owner: admin
--

CREATE TABLE public.cart_items (
    id uuid DEFAULT public.uuid_generate_v4() NOT NULL,
    user_id uuid NOT NULL,
    product_id uuid NOT NULL,
    quantity integer DEFAULT 1,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP,
    updated_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP
);


ALTER TABLE public.cart_items OWNER TO admin;

--
-- TOC entry 217 (class 1259 OID 16629)
-- Name: categories; Type: TABLE; Schema: public; Owner: admin
--

CREATE TABLE public.categories (
    id uuid DEFAULT public.uuid_generate_v4() NOT NULL,
    name character varying(100) NOT NULL,
    slug public.citext NOT NULL,
    parent_id uuid,
    is_active boolean DEFAULT true NOT NULL,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL
);


ALTER TABLE public.categories OWNER TO admin;

--
-- TOC entry 227 (class 1259 OID 16774)
-- Name: contest_results; Type: TABLE; Schema: public; Owner: admin
--

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


ALTER TABLE public.contest_results OWNER TO admin;

--
-- TOC entry 220 (class 1259 OID 16668)
-- Name: contests; Type: TABLE; Schema: public; Owner: admin
--

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


ALTER TABLE public.contests OWNER TO admin;

--
-- TOC entry 246 (class 1259 OID 17148)
-- Name: coupon_uses; Type: TABLE; Schema: public; Owner: admin
--

CREATE TABLE public.coupon_uses (
    id uuid DEFAULT public.uuid_generate_v4() NOT NULL,
    coupon_id uuid NOT NULL,
    user_id uuid NOT NULL,
    order_id uuid NOT NULL,
    used_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP
);


ALTER TABLE public.coupon_uses OWNER TO admin;

--
-- TOC entry 234 (class 1259 OID 16909)
-- Name: coupons; Type: TABLE; Schema: public; Owner: admin
--

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


ALTER TABLE public.coupons OWNER TO admin;

--
-- TOC entry 248 (class 1259 OID 17186)
-- Name: course_lessons; Type: TABLE; Schema: public; Owner: admin
--

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


ALTER TABLE public.course_lessons OWNER TO admin;

--
-- TOC entry 249 (class 1259 OID 17203)
-- Name: course_quizzes; Type: TABLE; Schema: public; Owner: admin
--

CREATE TABLE public.course_quizzes (
    id uuid DEFAULT public.uuid_generate_v4() NOT NULL,
    course_section_id uuid NOT NULL,
    title character varying(255) NOT NULL,
    passing_score integer NOT NULL,
    is_active boolean DEFAULT true NOT NULL,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    updated_at timestamp with time zone
);


ALTER TABLE public.course_quizzes OWNER TO admin;

--
-- TOC entry 241 (class 1259 OID 17063)
-- Name: course_sections; Type: TABLE; Schema: public; Owner: admin
--

CREATE TABLE public.course_sections (
    id uuid DEFAULT public.uuid_generate_v4() NOT NULL,
    course_id uuid NOT NULL,
    title character varying(255) NOT NULL,
    sort_order integer NOT NULL,
    is_active boolean DEFAULT true NOT NULL,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    updated_at timestamp with time zone
);


ALTER TABLE public.course_sections OWNER TO admin;

--
-- TOC entry 235 (class 1259 OID 16930)
-- Name: courses; Type: TABLE; Schema: public; Owner: admin
--

CREATE TABLE public.courses (
    id uuid DEFAULT public.uuid_generate_v4() NOT NULL,
    product_id uuid NOT NULL,
    level character varying(50) NOT NULL,
    total_duration_in_minutes integer DEFAULT 0 NOT NULL,
    is_certificate_included boolean DEFAULT false NOT NULL,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    updated_at timestamp with time zone
);


ALTER TABLE public.courses OWNER TO admin;

--
-- TOC entry 261 (class 1259 OID 64274)
-- Name: home_cards; Type: TABLE; Schema: public; Owner: admin
--

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


ALTER TABLE public.home_cards OWNER TO admin;

--
-- TOC entry 254 (class 1259 OID 26281)
-- Name: ip_login_attempts; Type: TABLE; Schema: public; Owner: admin
--

CREATE TABLE public.ip_login_attempts (
    ip_address inet NOT NULL,
    attempt_count integer DEFAULT 1,
    last_attempt_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP,
    locked_until timestamp with time zone
);


ALTER TABLE public.ip_login_attempts OWNER TO admin;

--
-- TOC entry 250 (class 1259 OID 17216)
-- Name: lesson_progress; Type: TABLE; Schema: public; Owner: admin
--

CREATE TABLE public.lesson_progress (
    id uuid DEFAULT public.uuid_generate_v4() NOT NULL,
    user_id uuid NOT NULL,
    lesson_id uuid NOT NULL,
    is_completed boolean DEFAULT false,
    watched_seconds integer DEFAULT 0,
    completed_at timestamp with time zone,
    updated_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP
);


ALTER TABLE public.lesson_progress OWNER TO admin;

--
-- TOC entry 251 (class 1259 OID 17235)
-- Name: lesson_resources; Type: TABLE; Schema: public; Owner: admin
--

CREATE TABLE public.lesson_resources (
    id uuid DEFAULT public.uuid_generate_v4() NOT NULL,
    course_lesson_id uuid NOT NULL,
    title character varying(255) NOT NULL,
    file_url text NOT NULL,
    resource_type character varying(50) NOT NULL,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    updated_at timestamp with time zone
);


ALTER TABLE public.lesson_resources OWNER TO admin;

--
-- TOC entry 218 (class 1259 OID 16644)
-- Name: login_attempts; Type: TABLE; Schema: public; Owner: admin
--

CREATE TABLE public.login_attempts (
    email public.citext NOT NULL,
    attempt_count integer DEFAULT 1,
    last_attempt_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP,
    ip_address inet,
    locked_until timestamp with time zone
);


ALTER TABLE public.login_attempts OWNER TO admin;

--
-- TOC entry 236 (class 1259 OID 16944)
-- Name: media; Type: TABLE; Schema: public; Owner: admin
--

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


ALTER TABLE public.media OWNER TO admin;

--
-- TOC entry 242 (class 1259 OID 17076)
-- Name: media_comments; Type: TABLE; Schema: public; Owner: admin
--

CREATE TABLE public.media_comments (
    id uuid DEFAULT public.uuid_generate_v4() NOT NULL,
    media_id uuid NOT NULL,
    user_id uuid NOT NULL,
    comment_text text NOT NULL,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP,
    updated_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP,
    parent_comment_id uuid
);


ALTER TABLE public.media_comments OWNER TO admin;

--
-- TOC entry 243 (class 1259 OID 17096)
-- Name: media_likes; Type: TABLE; Schema: public; Owner: admin
--

CREATE TABLE public.media_likes (
    id uuid DEFAULT public.uuid_generate_v4() NOT NULL,
    media_id uuid NOT NULL,
    user_id uuid NOT NULL,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP
);


ALTER TABLE public.media_likes OWNER TO admin;

--
-- TOC entry 244 (class 1259 OID 17113)
-- Name: media_saves; Type: TABLE; Schema: public; Owner: admin
--

CREATE TABLE public.media_saves (
    id uuid DEFAULT public.uuid_generate_v4() NOT NULL,
    media_id uuid NOT NULL,
    user_id uuid NOT NULL,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP
);


ALTER TABLE public.media_saves OWNER TO admin;

--
-- TOC entry 245 (class 1259 OID 17130)
-- Name: media_watch_history; Type: TABLE; Schema: public; Owner: admin
--

CREATE TABLE public.media_watch_history (
    id uuid DEFAULT public.uuid_generate_v4() NOT NULL,
    user_id uuid NOT NULL,
    media_id uuid NOT NULL,
    watched_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP,
    is_point_earned boolean DEFAULT false
);


ALTER TABLE public.media_watch_history OWNER TO admin;

--
-- TOC entry 228 (class 1259 OID 16792)
-- Name: notification_deliveries; Type: TABLE; Schema: public; Owner: admin
--

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


ALTER TABLE public.notification_deliveries OWNER TO admin;

--
-- TOC entry 221 (class 1259 OID 16682)
-- Name: notifications; Type: TABLE; Schema: public; Owner: admin
--

CREATE TABLE public.notifications (
    id uuid DEFAULT public.uuid_generate_v4() NOT NULL,
    user_id uuid NOT NULL,
    type character varying(50) NOT NULL,
    title character varying(255) NOT NULL,
    body text NOT NULL,
    reference_type character varying(50),
    reference_id uuid,
    is_read boolean DEFAULT false,
    read_at timestamp with time zone,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT check_notification_type CHECK (((type)::text = ANY ((ARRAY['sale_completed'::character varying, 'new_follower'::character varying, 'new_review'::character varying, 'new_question'::character varying, 'media_liked'::character varying, 'media_commented'::character varying, 'contest_result'::character varying, 'order_completed'::character varying, 'new_video'::character varying, 'new_product'::character varying, 'system'::character varying])::text[])))
);


ALTER TABLE public.notifications OWNER TO admin;

--
-- TOC entry 237 (class 1259 OID 16972)
-- Name: orders; Type: TABLE; Schema: public; Owner: admin
--

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


ALTER TABLE public.orders OWNER TO admin;

--
-- TOC entry 247 (class 1259 OID 17170)
-- Name: payments; Type: TABLE; Schema: public; Owner: admin
--

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


ALTER TABLE public.payments OWNER TO admin;

--
-- TOC entry 222 (class 1259 OID 16697)
-- Name: point_logs; Type: TABLE; Schema: public; Owner: admin
--

CREATE TABLE public.point_logs (
    id uuid DEFAULT public.uuid_generate_v4() NOT NULL,
    user_id uuid NOT NULL,
    action_type character varying(50) NOT NULL,
    points_earned numeric(10,2) NOT NULL,
    reference_id uuid,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP
);


ALTER TABLE public.point_logs OWNER TO admin;

--
-- TOC entry 255 (class 1259 OID 34933)
-- Name: product_images; Type: TABLE; Schema: public; Owner: admin
--

CREATE TABLE public.product_images (
    id uuid DEFAULT public.uuid_generate_v4() NOT NULL,
    product_id uuid NOT NULL,
    object_key text NOT NULL,
    sort_order integer NOT NULL,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP
);


ALTER TABLE public.product_images OWNER TO admin;

--
-- TOC entry 238 (class 1259 OID 17001)
-- Name: product_qa; Type: TABLE; Schema: public; Owner: admin
--

CREATE TABLE public.product_qa (
    id uuid DEFAULT public.uuid_generate_v4() NOT NULL,
    product_id uuid NOT NULL,
    user_id uuid NOT NULL,
    parent_id uuid,
    message text NOT NULL,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP
);


ALTER TABLE public.product_qa OWNER TO admin;

--
-- TOC entry 229 (class 1259 OID 16807)
-- Name: products; Type: TABLE; Schema: public; Owner: admin
--

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


ALTER TABLE public.products OWNER TO admin;

--
-- TOC entry 260 (class 1259 OID 64263)
-- Name: pulse_news; Type: TABLE; Schema: public; Owner: admin
--

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


ALTER TABLE public.pulse_news OWNER TO admin;

--
-- TOC entry 239 (class 1259 OID 17025)
-- Name: reviews; Type: TABLE; Schema: public; Owner: admin
--

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


ALTER TABLE public.reviews OWNER TO admin;

--
-- TOC entry 230 (class 1259 OID 16836)
-- Name: seller_subscriptions; Type: TABLE; Schema: public; Owner: admin
--

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


ALTER TABLE public.seller_subscriptions OWNER TO admin;

--
-- TOC entry 231 (class 1259 OID 16853)
-- Name: shop_visits; Type: TABLE; Schema: public; Owner: admin
--

CREATE TABLE public.shop_visits (
    id uuid DEFAULT public.uuid_generate_v4() NOT NULL,
    shop_id uuid NOT NULL,
    user_id uuid,
    ip_address inet,
    visited_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP
);


ALTER TABLE public.shop_visits OWNER TO admin;

--
-- TOC entry 223 (class 1259 OID 16709)
-- Name: shops; Type: TABLE; Schema: public; Owner: admin
--

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


ALTER TABLE public.shops OWNER TO admin;

--
-- TOC entry 232 (class 1259 OID 16872)
-- Name: subscriptions; Type: TABLE; Schema: public; Owner: admin
--

CREATE TABLE public.subscriptions (
    id uuid DEFAULT public.uuid_generate_v4() NOT NULL,
    shop_id uuid NOT NULL,
    user_id uuid NOT NULL,
    wants_notifications boolean DEFAULT true,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP
);


ALTER TABLE public.subscriptions OWNER TO admin;

--
-- TOC entry 224 (class 1259 OID 16729)
-- Name: user_device_tokens; Type: TABLE; Schema: public; Owner: admin
--

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


ALTER TABLE public.user_device_tokens OWNER TO admin;

--
-- TOC entry 252 (class 1259 OID 17249)
-- Name: user_lesson_progress; Type: TABLE; Schema: public; Owner: admin
--

CREATE TABLE public.user_lesson_progress (
    id uuid DEFAULT public.uuid_generate_v4() NOT NULL,
    user_id uuid NOT NULL,
    course_lesson_id uuid NOT NULL,
    is_completed boolean DEFAULT false NOT NULL,
    watched_seconds integer DEFAULT 0 NOT NULL,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    updated_at timestamp with time zone
);


ALTER TABLE public.user_lesson_progress OWNER TO admin;

--
-- TOC entry 240 (class 1259 OID 17045)
-- Name: user_library; Type: TABLE; Schema: public; Owner: admin
--

CREATE TABLE public.user_library (
    id uuid DEFAULT public.uuid_generate_v4() NOT NULL,
    user_id uuid NOT NULL,
    product_id uuid NOT NULL,
    purchased_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP,
    last_accessed_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP
);


ALTER TABLE public.user_library OWNER TO admin;

--
-- TOC entry 225 (class 1259 OID 16745)
-- Name: user_points; Type: TABLE; Schema: public; Owner: admin
--

CREATE TABLE public.user_points (
    id uuid DEFAULT public.uuid_generate_v4() NOT NULL,
    user_id uuid NOT NULL,
    total_points numeric(12,2) DEFAULT 0.0,
    current_rank integer DEFAULT 0,
    current_streak integer DEFAULT 0,
    updated_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP
);


ALTER TABLE public.user_points OWNER TO admin;

--
-- TOC entry 226 (class 1259 OID 16760)
-- Name: user_sessions; Type: TABLE; Schema: public; Owner: admin
--

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


ALTER TABLE public.user_sessions OWNER TO admin;

--
-- TOC entry 219 (class 1259 OID 16653)
-- Name: users; Type: TABLE; Schema: public; Owner: admin
--

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
    stripe_customer_id character varying(255),
    stripe_account_id character varying(255),
    preferences jsonb DEFAULT '{}'::jsonb,
    is_active boolean DEFAULT true,
    last_login_at timestamp with time zone,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP,
    updated_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP,
    deleted_at timestamp with time zone
);


ALTER TABLE public.users OWNER TO admin;

--
-- TOC entry 4312 (class 0 OID 17346)
-- Dependencies: 253
-- Data for Name: __EFMigrationsHistory; Type: TABLE DATA; Schema: public; Owner: admin
--

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


--
-- TOC entry 4318 (class 0 OID 64248)
-- Dependencies: 259
-- Data for Name: admin_audit_logs; Type: TABLE DATA; Schema: public; Owner: admin
--

COPY public.admin_audit_logs (id, admin_user_id, action, target_type, target_id, metadata, created_at) FROM stdin;
6656a199-154b-4f59-ab1f-6a52c62f5654	5ee97315-2807-4042-a155-fe8714193a23	update_home_cards	home_cards	\N	{"Cards": []}	2026-07-02 19:24:18.239824+00
61cecdbb-df67-4e40-ad73-e74e6844a7cf	5ee97315-2807-4042-a155-fe8714193a23	lock_user	user	e7acf4c1-1f6b-4346-b2aa-95956bec3dd7	{"Until": "2026-07-11T18:14:16.11Z", "Reason": "Guvenlik incelemesi"}	2026-07-04 18:14:16.30338+00
\.


--
-- TOC entry 4321 (class 0 OID 64284)
-- Dependencies: 262
-- Data for Name: admin_competition_rewards; Type: TABLE DATA; Schema: public; Owner: admin
--

COPY public.admin_competition_rewards (id, contest_id, user_id, rank, reward_type, amount, currency, note, created_at) FROM stdin;
\.


--
-- TOC entry 4316 (class 0 OID 64213)
-- Dependencies: 257
-- Data for Name: admin_reports; Type: TABLE DATA; Schema: public; Owner: admin
--

COPY public.admin_reports (id, type, target_id, target_title, reported_by_user_id, reason, description, status, created_at, updated_at) FROM stdin;
\.


--
-- TOC entry 4317 (class 0 OID 64229)
-- Dependencies: 258
-- Data for Name: admin_warnings; Type: TABLE DATA; Schema: public; Owner: admin
--

COPY public.admin_warnings (id, user_id, admin_user_id, title, message, created_at) FROM stdin;
\.


--
-- TOC entry 4315 (class 0 OID 54347)
-- Dependencies: 256
-- Data for Name: analytics_events; Type: TABLE DATA; Schema: public; Owner: admin
--

COPY public.analytics_events (id, shop_id, product_id, user_id, order_id, event_type, session_id, source, referrer, utm_source, utm_medium, utm_campaign, device_type, ip_address, user_agent, metadata, created_at) FROM stdin;
ffba8928-aafa-4bc8-81be-6a54b4c28863	9c3432a1-66b8-4856-bce0-9ef44654b69f	88959f5f-ee94-47b0-830a-106e94b7aec9	df19ab97-ed7e-48ef-979d-b14e0d9d1641	\N	add_to_cart	\N	backend	\N	\N	\N	\N	\N	\N	\N	{}	2026-06-23 17:31:42.053202+00
7a0565f1-bf79-469c-a633-d20f3d172db5	9c3432a1-66b8-4856-bce0-9ef44654b69f	88959f5f-ee94-47b0-830a-106e94b7aec9	df19ab97-ed7e-48ef-979d-b14e0d9d1641	\N	checkout_started	\N	backend	\N	\N	\N	\N	\N	\N	\N	{}	2026-06-23 17:31:43.051251+00
b6a2a5af-a3ec-4abe-9635-ffb613a34569	9c3432a1-66b8-4856-bce0-9ef44654b69f	\N	df19ab97-ed7e-48ef-979d-b14e0d9d1641	23aa834c-746b-4fa5-a126-3dc749ebb142	purchase_completed	\N	backend	\N	\N	\N	\N	\N	\N	\N	{}	2026-06-23 17:31:43.189652+00
2d876190-7388-4c0f-bced-c3f9e2271d65	9c3432a1-66b8-4856-bce0-9ef44654b69f	88959f5f-ee94-47b0-830a-106e94b7aec9	df19ab97-ed7e-48ef-979d-b14e0d9d1641	\N	download_clicked	\N	backend	\N	\N	\N	\N	\N	\N	\N	{}	2026-06-23 17:32:21.841621+00
c5a37f1b-215f-47ff-9294-6948ddcc0428	2fc28588-622f-40e5-8ee6-79ed0613c8fa	5ebe3845-e80d-4d79-bf5d-63947f802908	df19ab97-ed7e-48ef-979d-b14e0d9d1641	\N	add_to_cart	\N	backend	\N	\N	\N	\N	\N	\N	\N	{}	2026-06-23 17:43:58.999925+00
e010c415-27b3-4bae-8a91-fb8f84a966c1	2fc28588-622f-40e5-8ee6-79ed0613c8fa	5ebe3845-e80d-4d79-bf5d-63947f802908	df19ab97-ed7e-48ef-979d-b14e0d9d1641	\N	checkout_started	\N	backend	\N	\N	\N	\N	\N	\N	\N	{}	2026-06-23 17:44:00.419328+00
2a25c932-146a-4ec2-8aee-cd5a1ea3ec52	2fc28588-622f-40e5-8ee6-79ed0613c8fa	\N	df19ab97-ed7e-48ef-979d-b14e0d9d1641	a85b9b67-8ec6-40ce-9ea4-af91aeb4f1f4	purchase_completed	\N	backend	\N	\N	\N	\N	\N	\N	\N	{}	2026-06-23 17:44:00.501072+00
d6cfe90b-9f94-48f5-99a1-0ea40c5636a1	f9160c3b-3bd7-49ad-8ac8-f98c8db5d48d	5ef46d22-8943-4960-87c5-2a160bd06740	e592612f-44a8-4c00-9710-5499bb39f25c	\N	add_to_cart	\N	backend	\N	\N	\N	\N	\N	\N	\N	{}	2026-06-23 17:46:14.050263+00
48f85223-3a13-424f-adc7-11c2aae3cbce	f9160c3b-3bd7-49ad-8ac8-f98c8db5d48d	5ef46d22-8943-4960-87c5-2a160bd06740	e592612f-44a8-4c00-9710-5499bb39f25c	\N	checkout_started	\N	backend	\N	\N	\N	\N	\N	\N	\N	{}	2026-06-23 17:46:14.385523+00
e85cfb78-fff9-4b64-8039-edbb39ce1037	f9160c3b-3bd7-49ad-8ac8-f98c8db5d48d	\N	e592612f-44a8-4c00-9710-5499bb39f25c	1381b9fa-e306-4548-a0a7-4d6e6f8460c6	purchase_completed	\N	backend	\N	\N	\N	\N	\N	\N	\N	{}	2026-06-23 17:46:14.435993+00
10ad75e8-0e12-45e2-8209-0dee351f2798	2fc28588-622f-40e5-8ee6-79ed0613c8fa	5ebe3845-e80d-4d79-bf5d-63947f802908	e592612f-44a8-4c00-9710-5499bb39f25c	\N	add_to_cart	\N	backend	\N	\N	\N	\N	\N	\N	\N	{}	2026-06-23 17:54:16.644623+00
a4a146d8-0211-4148-90c1-552999d5496c	2fc28588-622f-40e5-8ee6-79ed0613c8fa	5ebe3845-e80d-4d79-bf5d-63947f802908	e592612f-44a8-4c00-9710-5499bb39f25c	\N	checkout_started	\N	backend	\N	\N	\N	\N	\N	\N	\N	{}	2026-06-23 17:54:16.73885+00
bc6c509a-8531-4ff6-a57b-0366115a84ce	2fc28588-622f-40e5-8ee6-79ed0613c8fa	\N	e592612f-44a8-4c00-9710-5499bb39f25c	5d778a5f-3f9d-4a89-9cd0-562a45623552	purchase_completed	\N	backend	\N	\N	\N	\N	\N	\N	\N	{}	2026-06-23 17:54:16.746452+00
fee84ebd-d91c-4b6b-a735-51a654f87b7f	be43220d-cf62-4f0f-9eb2-39c4eb5a3be2	07614073-8751-469e-ad10-dcccd6f5c5d0	df19ab97-ed7e-48ef-979d-b14e0d9d1641	\N	add_to_cart	\N	backend	\N	\N	\N	\N	\N	\N	\N	{}	2026-06-23 19:58:38.659466+00
b554abb6-ae67-49c0-9314-92999cba8dfc	be43220d-cf62-4f0f-9eb2-39c4eb5a3be2	07614073-8751-469e-ad10-dcccd6f5c5d0	df19ab97-ed7e-48ef-979d-b14e0d9d1641	\N	checkout_started	\N	backend	\N	\N	\N	\N	\N	\N	\N	{}	2026-06-23 19:58:38.946411+00
413d49a8-77a7-4585-b92f-f043d8d13c12	be43220d-cf62-4f0f-9eb2-39c4eb5a3be2	\N	df19ab97-ed7e-48ef-979d-b14e0d9d1641	12b62f6c-c6a5-4147-9c60-4bf4d8ce2c69	purchase_completed	\N	backend	\N	\N	\N	\N	\N	\N	\N	{}	2026-06-23 19:58:38.9602+00
3d71bcb6-3299-4aff-9900-ea39f3efb5ef	be43220d-cf62-4f0f-9eb2-39c4eb5a3be2	07614073-8751-469e-ad10-dcccd6f5c5d0	df19ab97-ed7e-48ef-979d-b14e0d9d1641	\N	download_clicked	\N	backend	\N	\N	\N	\N	\N	\N	\N	{}	2026-06-23 19:58:52.868507+00
718fd541-9b44-4dda-bbd1-c4ad4dca353f	2fc28588-622f-40e5-8ee6-79ed0613c8fa	423bf130-b4e5-4948-9d2f-c4d7b5a196ff	5ee97315-2807-4042-a155-fe8714193a23	\N	add_to_cart	\N	backend	\N	\N	\N	\N	\N	\N	\N	{}	2026-06-24 16:22:09.848321+00
abd67e54-df0b-4f2e-8e5a-f720f2a8200b	2fc28588-622f-40e5-8ee6-79ed0613c8fa	423bf130-b4e5-4948-9d2f-c4d7b5a196ff	e592612f-44a8-4c00-9710-5499bb39f25c	\N	add_to_cart	\N	backend	\N	\N	\N	\N	\N	\N	\N	{}	2026-06-24 16:22:55.932936+00
eb66d463-358b-4eca-a201-5e41f512ef01	2fc28588-622f-40e5-8ee6-79ed0613c8fa	423bf130-b4e5-4948-9d2f-c4d7b5a196ff	e592612f-44a8-4c00-9710-5499bb39f25c	\N	checkout_started	\N	backend	\N	\N	\N	\N	\N	\N	\N	{}	2026-06-24 16:22:56.148219+00
6ff13d8a-ef6d-40ff-b040-ba84ede6f39e	2fc28588-622f-40e5-8ee6-79ed0613c8fa	\N	e592612f-44a8-4c00-9710-5499bb39f25c	4d34d7a0-3ff9-4231-9732-1b56c6a85e9e	purchase_completed	\N	backend	\N	\N	\N	\N	\N	\N	\N	{}	2026-06-24 16:22:56.216211+00
43f4b877-1587-4ee9-b7d3-1a46f00911e4	2fc28588-622f-40e5-8ee6-79ed0613c8fa	e1abe8c0-5561-4f55-bd52-de6b105b76fe	e7acf4c1-1f6b-4346-b2aa-95956bec3dd7	\N	add_to_cart	\N	backend	\N	\N	\N	\N	\N	\N	\N	{}	2026-06-24 16:57:02.422267+00
f05c1dc3-8fe1-44d9-9a0b-7b357fb9d6d1	2fc28588-622f-40e5-8ee6-79ed0613c8fa	e1abe8c0-5561-4f55-bd52-de6b105b76fe	e7acf4c1-1f6b-4346-b2aa-95956bec3dd7	\N	checkout_started	\N	backend	\N	\N	\N	\N	\N	\N	\N	{}	2026-06-24 16:57:02.486735+00
ce1c76b4-c950-499d-9c3e-cccae4686ae6	2fc28588-622f-40e5-8ee6-79ed0613c8fa	\N	e7acf4c1-1f6b-4346-b2aa-95956bec3dd7	eed29adc-4112-4efa-b072-f168e4964831	purchase_completed	\N	backend	\N	\N	\N	\N	\N	\N	\N	{}	2026-06-24 16:57:02.495172+00
32f94a11-a90e-4d3c-9008-9992d0e77410	2fc28588-622f-40e5-8ee6-79ed0613c8fa	423bf130-b4e5-4948-9d2f-c4d7b5a196ff	5ee97315-2807-4042-a155-fe8714193a23	\N	add_to_cart	\N	backend	\N	\N	\N	\N	\N	\N	\N	{}	2026-06-28 16:00:21.672718+00
a89a40db-a8c6-4168-bb4e-53276a0dd3eb	2fc28588-622f-40e5-8ee6-79ed0613c8fa	e1abe8c0-5561-4f55-bd52-de6b105b76fe	df19ab97-ed7e-48ef-979d-b14e0d9d1641	\N	add_to_cart	\N	backend	\N	\N	\N	\N	\N	\N	\N	{}	2026-06-28 18:18:15.026055+00
d7357aff-5e6c-4c0f-be89-eb08a6f47291	2fc28588-622f-40e5-8ee6-79ed0613c8fa	e1abe8c0-5561-4f55-bd52-de6b105b76fe	df19ab97-ed7e-48ef-979d-b14e0d9d1641	\N	checkout_started	\N	backend	\N	\N	\N	\N	\N	\N	\N	{}	2026-06-28 18:18:15.625839+00
69a6ae16-71d3-4011-8e76-d342abcc0f6d	2fc28588-622f-40e5-8ee6-79ed0613c8fa	\N	df19ab97-ed7e-48ef-979d-b14e0d9d1641	b086d9d7-ec4c-475c-84d7-3c115220b42d	purchase_completed	\N	backend	\N	\N	\N	\N	\N	\N	\N	{}	2026-06-28 18:18:15.689168+00
a5c9a614-94c0-417d-b3d5-e8c755fdd46e	2fc28588-622f-40e5-8ee6-79ed0613c8fa	423bf130-b4e5-4948-9d2f-c4d7b5a196ff	df19ab97-ed7e-48ef-979d-b14e0d9d1641	\N	add_to_cart	\N	backend	\N	\N	\N	\N	\N	\N	\N	{}	2026-06-28 18:35:45.62481+00
af2a84e0-176e-43ea-9b78-ee7ddc154316	2fc28588-622f-40e5-8ee6-79ed0613c8fa	423bf130-b4e5-4948-9d2f-c4d7b5a196ff	df19ab97-ed7e-48ef-979d-b14e0d9d1641	\N	checkout_started	\N	backend	\N	\N	\N	\N	\N	\N	\N	{}	2026-06-28 18:35:45.715812+00
10abcba6-3e9a-4aed-9cef-aa14be8e5194	2fc28588-622f-40e5-8ee6-79ed0613c8fa	\N	df19ab97-ed7e-48ef-979d-b14e0d9d1641	a713d2d0-04ff-4db1-a819-be320d932aa2	purchase_completed	\N	backend	\N	\N	\N	\N	\N	\N	\N	{}	2026-06-28 18:35:45.748487+00
b2f35f14-0779-4d53-9a6c-dec48e9340d9	2fc28588-622f-40e5-8ee6-79ed0613c8fa	423bf130-b4e5-4948-9d2f-c4d7b5a196ff	e7acf4c1-1f6b-4346-b2aa-95956bec3dd7	\N	add_to_cart	\N	backend	\N	\N	\N	\N	\N	\N	\N	{}	2026-06-28 19:06:04.284061+00
329c9472-d0a3-4afa-8d58-254bd72bc2f3	2fc28588-622f-40e5-8ee6-79ed0613c8fa	423bf130-b4e5-4948-9d2f-c4d7b5a196ff	e7acf4c1-1f6b-4346-b2aa-95956bec3dd7	\N	checkout_started	\N	backend	\N	\N	\N	\N	\N	\N	\N	{}	2026-06-28 19:06:04.467279+00
41d193c9-79e6-4682-865c-4a081edf5d4e	2fc28588-622f-40e5-8ee6-79ed0613c8fa	\N	e7acf4c1-1f6b-4346-b2aa-95956bec3dd7	b7d9ad8e-a148-44b6-b435-e842a51f4760	purchase_completed	\N	backend	\N	\N	\N	\N	\N	\N	\N	{}	2026-06-28 19:06:04.485344+00
d210410b-9781-4d5e-89ef-95aeea4b027d	2fc28588-622f-40e5-8ee6-79ed0613c8fa	c2fa330d-ec90-450e-b051-059636ac3ed7	e592612f-44a8-4c00-9710-5499bb39f25c	\N	add_to_cart	\N	backend	\N	\N	\N	\N	\N	\N	\N	{}	2026-06-30 19:38:55.166253+00
35186d85-1e54-4c13-9451-4a361743c4cd	2fc28588-622f-40e5-8ee6-79ed0613c8fa	c2fa330d-ec90-450e-b051-059636ac3ed7	e592612f-44a8-4c00-9710-5499bb39f25c	\N	checkout_started	\N	backend	\N	\N	\N	\N	\N	\N	\N	{}	2026-06-30 19:38:59.394794+00
3c6d6c9d-1ce1-4412-9828-9a753cf43caa	2fc28588-622f-40e5-8ee6-79ed0613c8fa	\N	e592612f-44a8-4c00-9710-5499bb39f25c	f1d03b27-6f0f-47ae-8d05-ac94e19007c1	purchase_completed	\N	backend	\N	\N	\N	\N	\N	\N	\N	{}	2026-06-30 19:38:59.760509+00
c1893639-94f8-4178-9c8a-10f3cde91a3b	be43220d-cf62-4f0f-9eb2-39c4eb5a3be2	86093178-173e-4c26-8bd8-6f270cef116d	e592612f-44a8-4c00-9710-5499bb39f25c	\N	add_to_cart	\N	backend	\N	\N	\N	\N	\N	\N	\N	{}	2026-07-01 18:11:24.908245+00
684fe0c3-4f78-4448-a66f-8f5026abe251	be43220d-cf62-4f0f-9eb2-39c4eb5a3be2	86093178-173e-4c26-8bd8-6f270cef116d	e592612f-44a8-4c00-9710-5499bb39f25c	\N	checkout_started	\N	backend	\N	\N	\N	\N	\N	\N	\N	{}	2026-07-01 18:11:25.99083+00
ac108d5c-a197-4754-8d7b-3d77896c4d96	be43220d-cf62-4f0f-9eb2-39c4eb5a3be2	\N	e592612f-44a8-4c00-9710-5499bb39f25c	f88e2864-af85-418a-8e49-a448e744cd28	purchase_completed	\N	backend	\N	\N	\N	\N	\N	\N	\N	{}	2026-07-01 18:11:26.165335+00
\.


--
-- TOC entry 4292 (class 0 OID 16890)
-- Dependencies: 233
-- Data for Name: cart_items; Type: TABLE DATA; Schema: public; Owner: admin
--

COPY public.cart_items (id, user_id, product_id, quantity, created_at, updated_at) FROM stdin;
8f6e08d8-b437-46e9-a5d7-b0cdb9b5540d	5ee97315-2807-4042-a155-fe8714193a23	423bf130-b4e5-4948-9d2f-c4d7b5a196ff	1	2026-06-28 16:00:21.591702+00	2026-06-28 16:00:21.591749+00
\.


--
-- TOC entry 4276 (class 0 OID 16629)
-- Dependencies: 217
-- Data for Name: categories; Type: TABLE DATA; Schema: public; Owner: admin
--

COPY public.categories (id, name, slug, parent_id, is_active, created_at) FROM stdin;
1bcc9c55-9cbf-45f8-aa1e-45e3bb310a49	Education	education	\N	t	2026-05-31 20:09:08.61454+00
926f634d-b3c5-41d1-9217-de73c23bf1ef	Media & Video	media-video	\N	t	2026-05-31 20:09:08.61454+00
6854ccd8-7726-4737-8e7f-7981d052df58	Software Development	software-development	\N	t	2026-05-31 20:09:08.614474+00
282e01ad-6500-44af-9a2f-e99831464e7a	Design Assets	design-assets	\N	t	2026-05-31 20:09:08.614539+00
686d2433-b518-4c18-94b3-a1ab155f4a20	Growth Marketing	growth-marketing	\N	t	2026-05-31 20:09:08.614539+00
\.


--
-- TOC entry 4286 (class 0 OID 16774)
-- Dependencies: 227
-- Data for Name: contest_results; Type: TABLE DATA; Schema: public; Owner: admin
--

COPY public.contest_results (id, contest_id, user_id, final_rank, total_score, reward_claimed, created_at, joined_at) FROM stdin;
\.


--
-- TOC entry 4279 (class 0 OID 16668)
-- Dependencies: 220
-- Data for Name: contests; Type: TABLE DATA; Schema: public; Owner: admin
--

COPY public.contests (id, title, start_date, end_date, prize_pool, is_active, created_by, description, rewards_hidden) FROM stdin;
\.


--
-- TOC entry 4305 (class 0 OID 17148)
-- Dependencies: 246
-- Data for Name: coupon_uses; Type: TABLE DATA; Schema: public; Owner: admin
--

COPY public.coupon_uses (id, coupon_id, user_id, order_id, used_at) FROM stdin;
\.


--
-- TOC entry 4293 (class 0 OID 16909)
-- Dependencies: 234
-- Data for Name: coupons; Type: TABLE DATA; Schema: public; Owner: admin
--

COPY public.coupons (id, product_id, shop_id, code, discount_type, discount_value, minimum_cart_amount, max_uses, used_count, starts_at, expires_at, is_active, created_at) FROM stdin;
\.


--
-- TOC entry 4307 (class 0 OID 17186)
-- Dependencies: 248
-- Data for Name: course_lessons; Type: TABLE DATA; Schema: public; Owner: admin
--

COPY public.course_lessons (id, course_section_id, title, video_url, duration_in_seconds, sort_order, is_free_preview, is_active, created_at, updated_at) FROM stdin;
02cad8cc-9845-4523-af3f-d332393f13bf	91d63fa9-084c-4604-bfaf-1ae5cd9ba883	baslik4	courses_or_products/c0612d81-5dcc-4ce6-a938-68bf648ddcba_Vertical_9_16_aspect_ratio_cinematic_video_for_Reels._A_sleek_C__programming_PDF_book_cover_is_displ_seed4278527437.mp4	0	4	f	t	2026-06-24 15:59:48.396953+00	2026-06-24 15:59:48.396953+00
205f5995-54c0-48a9-b0a2-cac09079d0b1	7a06f54e-41db-4941-a5a9-d8aa079ce803	Ders 1	courses_or_products/5760d5ce-b956-4949-92f9-a18f0401f176_Kayt-2025-11-25-215123.mp4	0	1	t	t	2026-06-24 15:59:48.396837+00	2026-06-24 15:59:48.39685+00
2398a8fb-7b89-4317-8626-24a343602909	33f48ed5-5d83-424a-bb3f-5bb664dd0c33	bvaslik3	courses_or_products/d6a9e6fc-6714-4b2e-bd90-1d2f64402696_A_dynamic_and_fast-paced_Instagram_Reels_video._A_3D_purple_C__logo_violently_bursts_out_from_a_digi_seed4199154912-1.mp	0	1	t	t	2026-06-24 15:59:48.39696+00	2026-06-24 15:59:48.39696+00
44d98c0d-ee0d-49ed-9a8b-f42e26d0bbfe	91d63fa9-084c-4604-bfaf-1ae5cd9ba883	baslik3	courses_or_products/da69b243-2519-481e-b52c-8820dc0a4ec3_Ekran-Kayd-2026-03-16-200353.mp4	0	3	f	t	2026-06-24 15:59:48.396951+00	2026-06-24 15:59:48.396951+00
98724e96-1b9b-40f0-a999-31f5b64fcc2a	7a06f54e-41db-4941-a5a9-d8aa079ce803	Ders 2	courses_or_products/ed804498-2a6e-4fe6-af6c-15c6037b34f0_Kayt-2025-11-25-215123.mp4	0	2	f	t	2026-06-24 15:59:48.396899+00	2026-06-24 15:59:48.396899+00
afcb5068-a850-49fa-ab01-8462e7c29cce	91d63fa9-084c-4604-bfaf-1ae5cd9ba883	baslik1	courses_or_products/c05b942e-3974-4346-b469-9bd646f2be72_Ekran-Kayd-2026-03-16-200353.mp4	0	1	t	t	2026-06-24 15:59:48.396946+00	2026-06-24 15:59:48.396946+00
c06691f5-421c-46e3-b223-96de20ee1a52	91d63fa9-084c-4604-bfaf-1ae5cd9ba883	baslik5	courses_or_products/6474dd6f-a708-46ce-b6af-70e80f1ca04d_Vertical_9_16_aspect_ratio_cinematic_video_for_Reels._A_sleek_C__programming_PDF_book_cover_is_displ_seed4278527437.mp4	0	5	f	t	2026-06-24 15:59:48.396955+00	2026-06-24 15:59:48.396955+00
e92b4d3e-5358-4c6a-b1ae-6f8d99bf0afd	7a06f54e-41db-4941-a5a9-d8aa079ce803	Ders 3	courses_or_products/d3e1dcd6-c20b-4a7f-9b36-b8e7a0111d50_Ekran-Kayd-2026-03-16-200353.mp4	0	3	f	t	2026-06-24 15:59:48.396901+00	2026-06-24 15:59:48.396901+00
ed9b5b14-0140-47e7-a862-0c3dd4733fe0	33f48ed5-5d83-424a-bb3f-5bb664dd0c33	baslik2	courses_or_products/0fc0af0a-9926-409a-bf3a-b1e6611e16e0_Vertical_9_16_aspect_ratio._The_purple_C__logo_on_the_book_cover_transforms_into_a_glowing_energy_co_seed934031659.mp4	0	2	f	t	2026-06-24 15:59:48.396962+00	2026-06-24 15:59:48.396962+00
fc3193bf-9e33-493e-9c36-743213dd182d	91d63fa9-084c-4604-bfaf-1ae5cd9ba883	baslik2	courses_or_products/47d2ede5-259c-4ec5-8c94-0e44a7afbe6d_Ekran-Kayd-2026-03-05-141750.mp4	0	2	f	t	2026-06-24 15:59:48.396948+00	2026-06-24 15:59:48.396948+00
0d96cd83-840f-46bd-ab9d-eb54a0b66df2	7d8101bc-a237-4b34-bd7b-a50a2a9d8efc	baslik2	courses_or_products/3893fcf2-da1f-4824-b958-3f7def05872a_Kayt-2025-11-25-215123.mp4	0	1	t	t	2026-06-24 16:05:49.325744+00	2026-06-24 16:05:49.325745+00
20f7f320-9545-4f64-9682-86bcc6f3f684	7d8101bc-a237-4b34-bd7b-a50a2a9d8efc	baslik2	courses_or_products/aa9d1679-e6b1-48d3-8fd5-d4a172bda7ba_A_dynamic_and_fast-paced_Instagram_Reels_video._A_3D_purple_C__logo_violently_bursts_out_from_a_digi_seed4199154912-1.mp	0	2	f	t	2026-06-24 16:05:49.477777+00	2026-06-24 16:05:49.477777+00
a0374cd2-af8a-4ea2-8d93-a628273dbc06	830d7f26-85d5-4495-93b2-89d3399ac6f9	baslik2	courses_or_products/0257fd62-00a3-446c-88c8-09b27ae98758_Vertical_9_16_aspect_ratio._The_purple_C__logo_on_the_book_cover_transforms_into_a_glowing_energy_co_seed934031659.mp4	0	1	t	t	2026-06-24 16:05:49.961279+00	2026-06-24 16:05:49.961279+00
\.


--
-- TOC entry 4308 (class 0 OID 17203)
-- Dependencies: 249
-- Data for Name: course_quizzes; Type: TABLE DATA; Schema: public; Owner: admin
--

COPY public.course_quizzes (id, course_section_id, title, passing_score, is_active, created_at, updated_at) FROM stdin;
\.


--
-- TOC entry 4300 (class 0 OID 17063)
-- Dependencies: 241
-- Data for Name: course_sections; Type: TABLE DATA; Schema: public; Owner: admin
--

COPY public.course_sections (id, course_id, title, sort_order, is_active, created_at, updated_at) FROM stdin;
33f48ed5-5d83-424a-bb3f-5bb664dd0c33	51c8421d-3c01-41f0-9385-14f7378cddff	react temel	3	t	2026-06-24 15:59:48.396958+00	2026-06-24 15:59:48.396958+00
7a06f54e-41db-4941-a5a9-d8aa079ce803	51c8421d-3c01-41f0-9385-14f7378cddff	javascript temelleri	1	t	2026-06-24 15:59:48.395934+00	2026-06-24 15:59:48.395934+00
91d63fa9-084c-4604-bfaf-1ae5cd9ba883	51c8421d-3c01-41f0-9385-14f7378cddff	javascript dom	2	t	2026-06-24 15:59:48.396944+00	2026-06-24 15:59:48.396944+00
7d8101bc-a237-4b34-bd7b-a50a2a9d8efc	de32991a-28ff-4430-95a9-b33460ae733f	baslik1	1	t	2026-06-24 16:05:49.055107+00	2026-06-24 16:05:49.055108+00
830d7f26-85d5-4495-93b2-89d3399ac6f9	de32991a-28ff-4430-95a9-b33460ae733f	bnolum3	2	t	2026-06-24 16:05:49.774687+00	2026-06-24 16:05:49.774688+00
\.


--
-- TOC entry 4294 (class 0 OID 16930)
-- Dependencies: 235
-- Data for Name: courses; Type: TABLE DATA; Schema: public; Owner: admin
--

COPY public.courses (id, product_id, level, total_duration_in_minutes, is_certificate_included, created_at, updated_at) FROM stdin;
51c8421d-3c01-41f0-9385-14f7378cddff	e1abe8c0-5561-4f55-bd52-de6b105b76fe	Advanced	0	t	2026-06-24 15:59:48.394608+00	2026-06-24 15:59:48.39463+00
de32991a-28ff-4430-95a9-b33460ae733f	423bf130-b4e5-4948-9d2f-c4d7b5a196ff	Intermediate	0	t	2026-06-24 16:05:48.654373+00	2026-06-24 16:05:48.654374+00
\.


--
-- TOC entry 4320 (class 0 OID 64274)
-- Dependencies: 261
-- Data for Name: home_cards; Type: TABLE DATA; Schema: public; Owner: admin
--

COPY public.home_cards (id, title, description, icon, action_type, sort_order, is_active, updated_at) FROM stdin;
\.


--
-- TOC entry 4313 (class 0 OID 26281)
-- Dependencies: 254
-- Data for Name: ip_login_attempts; Type: TABLE DATA; Schema: public; Owner: admin
--

COPY public.ip_login_attempts (ip_address, attempt_count, last_attempt_at, locked_until) FROM stdin;
\.


--
-- TOC entry 4309 (class 0 OID 17216)
-- Dependencies: 250
-- Data for Name: lesson_progress; Type: TABLE DATA; Schema: public; Owner: admin
--

COPY public.lesson_progress (id, user_id, lesson_id, is_completed, watched_seconds, completed_at, updated_at) FROM stdin;
\.


--
-- TOC entry 4310 (class 0 OID 17235)
-- Dependencies: 251
-- Data for Name: lesson_resources; Type: TABLE DATA; Schema: public; Owner: admin
--

COPY public.lesson_resources (id, course_lesson_id, title, file_url, resource_type, created_at, updated_at) FROM stdin;
f2c5278a-f066-491a-9d5b-4526d5a985aa	0d96cd83-840f-46bd-ab9d-eb54a0b66df2	6245ae1c-b7dc-42a5-82e6-c65e4e7702f6_Untitled	courses_or_products/5ce05372-275d-4e99-bca9-e182eb67cfa1_6245ae1c-b7dc-42a5-82e6-c65e4e7702f6_Untitled.pdf	Document	2026-06-24 16:05:49.326811+00	2026-06-24 16:05:49.326813+00
\.


--
-- TOC entry 4277 (class 0 OID 16644)
-- Dependencies: 218
-- Data for Name: login_attempts; Type: TABLE DATA; Schema: public; Owner: admin
--

COPY public.login_attempts (email, attempt_count, last_attempt_at, ip_address, locked_until) FROM stdin;
\.


--
-- TOC entry 4295 (class 0 OID 16944)
-- Dependencies: 236
-- Data for Name: media; Type: TABLE DATA; Schema: public; Owner: admin
--

COPY public.media (id, shop_id, product_id, video_url, thumbnail_url, view_count, like_count, save_count, comment_count, created_at, updated_at, is_active, caption, hashtags, duration_seconds, status, share_count) FROM stdin;
433caadb-7bce-4656-a89a-43f9d97802f3	9c3432a1-66b8-4856-bce0-9ef44654b69f	88959f5f-ee94-47b0-830a-106e94b7aec9	courses_or_products/2a9e7a41-0be0-4a62-85ff-927e16562e98_Vertical_9_16_aspect_ratio_cinematic_video_for_Reels._A_sleek_C__programming_PDF_book_cover_is_displ_seed4278527437.mp4	uploads/2918467b-c154-47f1-a85a-5e5e8741e88a_WhatsApp-Image-2026-05-26-at-16.32.21.jpeg	6	1	1	1	2026-06-23 17:29:50.9309+00	2026-06-23 17:59:08.35665+00	f	asd asd asd asd asd asd	{as,das}	0	ready	0
1bcff3c1-fb48-4d69-8af8-46320e556e8c	9c3432a1-66b8-4856-bce0-9ef44654b69f	\N	courses_or_products/6b045f10-d82d-4a62-a5d0-843ba312a380_Vertical_9_16_aspect_ratio._The_purple_C__logo_on_the_book_cover_transforms_into_a_glowing_energy_co_seed934031659.mp4	uploads/7ae14462-6289-496f-8964-78edd0b0cd4e_kodlamaa.jpg	8	0	0	0	2026-06-23 18:17:18.131418+00	2026-06-23 18:51:35.527504+00	f	HAFTA SONU SÜRPRİZİ!	{indirim,craftora}	0	ready	0
a864da3a-343d-4fd6-9715-6f3e94ab1661	2fc28588-622f-40e5-8ee6-79ed0613c8fa	5ebe3845-e80d-4d79-bf5d-63947f802908	courses_or_products/b61187a8-db60-4eab-bf19-f9384f02b5c8_Vertical_9_16_aspect_ratio._The_purple_C__logo_on_the_book_cover_transforms_into_a_glowing_energy_co_seed3808780903.mp4	uploads/75f99d42-59e4-48dd-a415-0d4931766716_anakara-medipol-uni-logo-1024x512.png	4	0	0	0	2026-06-23 17:17:10.842709+00	2026-06-23 19:16:34.25356+00	f	selam kankam	{merhaba,python,asda,sd}	0	ready	0
e744b0f1-909f-4a4f-b883-7f876c83b8b1	f9160c3b-3bd7-49ad-8ac8-f98c8db5d48d	5ef46d22-8943-4960-87c5-2a160bd06740	courses_or_products/1608a3de-6475-445a-9150-c554aa0739f5_Vertical_9_16_aspect_ratio_cinematic_video_for_Reels._A_sleek_C__programming_PDF_book_cover_is_displ_seed4278527437.mp4	uploads/19ef86fb-9547-4304-841b-c343bcfa7e6b_MEDIPOL.png	19	0	0	0	2026-06-23 17:31:09.881095+00	2026-06-23 19:37:23.952198+00	f	asd as dasd as dasasd	{as,das,dasdasdasdas}	0	ready	0
4d76be43-790f-4def-96fc-4d92bb4c6bdd	9c3432a1-66b8-4856-bce0-9ef44654b69f	\N	courses_or_products/df965542-6df7-4150-9aaf-9acc23dbc765_Vertical_9_16_aspect_ratio_cinematic_video_for_Reels._A_sleek_C__programming_PDF_book_cover_is_displ_seed4278527437.mp4	uploads/a7b19eb1-978f-4636-97fe-74bb784e57a1_indir.jpg	11	0	0	0	2026-06-23 18:07:02.041941+00	2026-06-23 19:11:13.553677+00	f	Senin favorin hangisi?, Yorumlarda buluşalım	{craftora,satis}	0	ready	0
ac4d2bef-d957-4bc5-8255-5a6af2c1b21e	9c3432a1-66b8-4856-bce0-9ef44654b69f	081500d3-c9a9-49e5-b5d7-ec363b144368	courses_or_products/57e6ed18-2138-413e-a0e1-9770a2f8dcb2_Vertical_9_16_aspect_ratio._The_purple_C__logo_on_the_book_cover_transforms_into_a_glowing_energy_co_seed934031659.mp4	uploads/effd8193-017e-4a25-938d-5fbe7628856e_kodlamaa.jpg	6	1	1	1	2026-06-23 19:13:00.38709+00	2026-06-28 17:28:17.768514+00	t	Hemen incelemek ve sepete eklemek için videonun üzerindeki ürün etiketine tıklayabilirsin.	{tasarim,craftora}	0	ready	0
f11c0e8c-5c77-46f9-923c-088a1bc87e60	9c3432a1-66b8-4856-bce0-9ef44654b69f	\N	courses_or_products/3abb2a8a-97b0-48bc-97f5-011f062b8391_Vertical_9_16_aspect_ratio._The_purple_C__logo_on_the_book_cover_transforms_into_a_glowing_energy_co_seed3808780903.mp4	uploads/2e38338b-93f4-49c2-99e1-9333e65cfd05_masa.jpg	16	0	0	0	2026-06-23 18:05:18.5738+00	2026-06-23 19:11:19.311456+00	f	Videodaki etikete tıkla, Uygulamadan çıkmadan al	{tasarim,craftora}	0	ready	0
47a21bc2-b45a-4fb3-b98a-656dd0f6d2e4	2fc28588-622f-40e5-8ee6-79ed0613c8fa	5ebe3845-e80d-4d79-bf5d-63947f802908	courses_or_products/a235bb06-0bc7-4b99-a93f-80fcd3e711a8_Vertical_9_16_aspect_ratio._The_purple_C__logo_on_the_book_cover_transforms_into_a_glowing_energy_co_seed3808780903.mp4	uploads/c8c61acd-4215-4f01-b3fd-f43ce9d7e792_WhatsApp-Image-2026-05-26-at-16.32.21.jpeg	4	0	0	0	2026-06-23 17:12:33.782726+00	2026-06-23 19:16:40.405937+00	f	kes lan  keeees	{sa,dasd,asd,asdas,das,dasasd}	0	ready	0
6a197cf8-e55a-4f13-8d05-d6dced7b4439	2fc28588-622f-40e5-8ee6-79ed0613c8fa	5ebe3845-e80d-4d79-bf5d-63947f802908	courses_or_products/cca25769-3695-42c0-ae0e-2e480096cb69_Vertical_9_16_aspect_ratio._The_purple_C__logo_on_the_book_cover_transforms_into_a_glowing_energy_co_seed3808780903.mp4	uploads/ef11e8ae-c95f-4519-8ac2-4a36b4402bdb_Screenshot_20260514_211356_Instagram.jpg	7	0	0	0	2026-06-23 17:10:29.776838+00	2026-06-23 19:16:45.001802+00	f	bu ne	{asd}	0	ready	0
072285d3-0dd3-4b28-9310-53f9c7b3357c	9c3432a1-66b8-4856-bce0-9ef44654b69f	\N	courses_or_products/62211534-4b2e-4b3d-8713-2f4238412c69_A_dynamic_and_fast-paced_Instagram_Reels_video._A_3D_purple_C__logo_violently_bursts_out_from_a_digi_seed4199154912-1.mp	uploads/e169912b-57c2-4eeb-8488-418ab6f31ea9_masa.jpg	8	0	0	0	2026-06-23 18:24:14.174347+00	2026-06-23 18:51:22.683096+00	f	kodlama yazilim kaynagi	{tasarim,kurs,craftora}	0	ready	0
984adebb-0b82-4496-ad95-a51448c760d1	9c3432a1-66b8-4856-bce0-9ef44654b69f	4496e543-1afe-40e4-bd6b-910300bbd320	courses_or_products/7118c84f-461f-4d92-b772-5077c911b506_Vertical_9_16_aspect_ratio._The_purple_C__logo_on_the_book_cover_transforms_into_a_glowing_energy_co_seed934031659.mp4	uploads/bf5762f3-3d1d-4d6e-8636-ca73241ba809_63481e8c2ba072245f7f3d11_3.png	112	2	2	1	2026-06-24 03:29:30.639651+00	2026-07-04 18:08:09.331721+00	t	merhaba bu bır test urunu	{test,urun}	0	ready	0
e9325c09-5243-49b4-9bec-fb5c4a72cc1d	be43220d-cf62-4f0f-9eb2-39c4eb5a3be2	07614073-8751-469e-ad10-dcccd6f5c5d0	courses_or_products/789d5ecf-7a7e-4c2a-8f97-f85984549aca_Vertical_9_16_aspect_ratio_cinematic_video_for_Reels._A_sleek_C__programming_PDF_book_cover_is_displ_seed4278527437.mp4	uploads/4cbddc9d-2415-4c95-a39a-642b3f5fd85d_indir.jpg	54	2	2	1	2026-06-23 19:36:38.140321+00	2026-07-04 18:09:08.924071+00	t		{}	0	ready	0
dfc33ae6-35a4-446e-ad64-442dae6fd9ab	2fc28588-622f-40e5-8ee6-79ed0613c8fa	c2fa330d-ec90-450e-b051-059636ac3ed7	courses_or_products/460088f4-a5ba-49c8-a158-fa85b712b9b8_Vertical_9_16_aspect_ratio_cinematic_video_for_Reels._A_sleek_C__programming_PDF_book_cover_is_displ_seed4278527437.mp4	uploads/c3b0cc09-ac92-40b4-bb7d-462256713aed_farkli.avif	22	1	0	0	2026-06-23 19:27:53.265573+00	2026-07-04 18:13:08.697415+00	t		{craftora}	0	ready	0
2ebe21d3-c6c9-4d91-b6ab-a3f43a57dd62	9c3432a1-66b8-4856-bce0-9ef44654b69f	f78fde16-311a-43da-af6f-1a149569f4c2	courses_or_products/f5274e6e-2535-439b-bb38-ccf826b6c314_A_dynamic_and_fast-paced_Instagram_Reels_video._A_3D_purple_C__logo_violently_bursts_out_from_a_digi_seed4199154912-1.mp	uploads/cc664992-28d9-42d5-bccb-70ed8816b5e3_indir.jpg	7	1	0	1	2026-06-23 19:14:18.309067+00	2026-07-01 18:18:55.011132+00	t	Hızlı tükeniyor, elini çabuk tut!	{reels,algoritmasi}	0	ready	0
35dfb355-99e0-479b-b824-608c52f4e11e	2fc28588-622f-40e5-8ee6-79ed0613c8fa	3ee6ec9f-3490-41b0-9b59-c18404086f90	courses_or_products/efd3463a-9ed1-4c19-930f-f5e685a41030_Vertical_9_16_aspect_ratio._The_purple_C__logo_on_the_book_cover_transforms_into_a_glowing_energy_co_seed3808780903.mp4	uploads/f850ae6d-7662-42ee-9417-7b65be78e27b_indir.jpg	18	0	0	0	2026-06-23 19:27:12.070788+00	2026-07-01 18:18:55.007143+00	t		{kurs}	0	ready	0
728f0ecf-4a7b-4109-bd4f-384aca425617	be43220d-cf62-4f0f-9eb2-39c4eb5a3be2	86093178-173e-4c26-8bd8-6f270cef116d	courses_or_products/8dc09daa-2738-422d-90cc-cf277a0b6bbe_Vertical_9_16_aspect_ratio._The_purple_C__logo_on_the_book_cover_transforms_into_a_glowing_energy_co_seed934031659.mp4	uploads/d64e4431-ecbd-4851-a960-bd1360ad55bc_63481e8c2ba072245f7f3d11_3.png	31	1	0	2	2026-06-23 19:35:37.954368+00	2026-07-01 18:18:55.000323+00	t	Eğitim, masterclass ve e-kitap satanlar için	{}	0	ready	0
\.


--
-- TOC entry 4301 (class 0 OID 17076)
-- Dependencies: 242
-- Data for Name: media_comments; Type: TABLE DATA; Schema: public; Owner: admin
--

COPY public.media_comments (id, media_id, user_id, comment_text, created_at, updated_at, parent_comment_id) FROM stdin;
8106c5f1-07e9-4b46-a374-66b33c39e358	433caadb-7bce-4656-a89a-43f9d97802f3	df19ab97-ed7e-48ef-979d-b14e0d9d1641	yorum geldi	2026-06-23 17:31:26.321044+00	2026-06-23 17:31:26.321093+00	\N
c2a74416-c976-4e1a-bd1e-12d978e9cb7c	e9325c09-5243-49b4-9bec-fb5c4a72cc1d	e592612f-44a8-4c00-9710-5499bb39f25c	harıka bır urun	2026-06-24 03:23:50.111387+00	2026-06-24 03:23:50.111388+00	\N
e82bf345-e1a4-46d8-916f-82ed11856ba2	984adebb-0b82-4496-ad95-a51448c760d1	e592612f-44a8-4c00-9710-5499bb39f25c	urunu henuz almadım oneren varmı	2026-06-24 03:30:15.871009+00	2026-06-24 03:30:15.871009+00	\N
1d8cc5ab-3779-4f89-bea4-14c15057c37f	728f0ecf-4a7b-4109-bd4f-384aca425617	5ee97315-2807-4042-a155-fe8714193a23	hvjfjg	2026-06-24 08:28:14.938471+00	2026-06-24 08:28:14.938507+00	\N
9a5633bb-afea-4047-928e-f8aaa6744a6e	ac4d2bef-d957-4bc5-8255-5a6af2c1b21e	e592612f-44a8-4c00-9710-5499bb39f25c	amk	2026-06-28 16:11:42.540622+00	2026-06-28 16:11:42.540698+00	\N
99ec200e-6c49-4ce6-a0c0-56c4803a0a56	728f0ecf-4a7b-4109-bd4f-384aca425617	e592612f-44a8-4c00-9710-5499bb39f25c	hepsıu  burada	2026-07-01 18:11:03.992198+00	2026-07-01 18:11:03.992234+00	\N
2a59e835-8654-42ec-a050-fb48885f2793	2ebe21d3-c6c9-4d91-b6ab-a3f43a57dd62	e592612f-44a8-4c00-9710-5499bb39f25c	kes  yarrak	2026-07-01 18:18:45.173502+00	2026-07-01 18:18:45.173502+00	\N
\.


--
-- TOC entry 4302 (class 0 OID 17096)
-- Dependencies: 243
-- Data for Name: media_likes; Type: TABLE DATA; Schema: public; Owner: admin
--

COPY public.media_likes (id, media_id, user_id, created_at) FROM stdin;
eb65d42d-bb31-4036-ab7f-8cc638b15ce9	433caadb-7bce-4656-a89a-43f9d97802f3	df19ab97-ed7e-48ef-979d-b14e0d9d1641	2026-06-23 17:31:30.741972+00
d74fea89-fbdd-48f5-ad7a-163a51e10ee0	e9325c09-5243-49b4-9bec-fb5c4a72cc1d	e592612f-44a8-4c00-9710-5499bb39f25c	2026-06-24 03:23:55.362254+00
bc09dfbf-f8d8-44ff-a2f5-d8c4765a3d5e	984adebb-0b82-4496-ad95-a51448c760d1	e592612f-44a8-4c00-9710-5499bb39f25c	2026-06-24 03:29:56.269776+00
c2ee170c-4420-4fbe-83ff-7b973cc1a69d	984adebb-0b82-4496-ad95-a51448c760d1	5ee97315-2807-4042-a155-fe8714193a23	2026-06-24 08:27:39.330522+00
200c9f51-add8-464c-ae41-0d6d2c205844	e9325c09-5243-49b4-9bec-fb5c4a72cc1d	5ee97315-2807-4042-a155-fe8714193a23	2026-06-24 15:42:30.540381+00
9109fa00-f595-4503-a5c8-3d42c9461adc	ac4d2bef-d957-4bc5-8255-5a6af2c1b21e	e592612f-44a8-4c00-9710-5499bb39f25c	2026-06-28 16:11:32.001094+00
2694f849-6149-44e6-b27e-468aa970b6b2	dfc33ae6-35a4-446e-ad64-442dae6fd9ab	e592612f-44a8-4c00-9710-5499bb39f25c	2026-06-30 19:38:53.658516+00
d6a580c0-b939-451c-b864-5ece78de30a0	728f0ecf-4a7b-4109-bd4f-384aca425617	e592612f-44a8-4c00-9710-5499bb39f25c	2026-07-01 18:10:58.382104+00
dc900a1a-1bd6-4d3e-a531-652ea1dd64b9	2ebe21d3-c6c9-4d91-b6ab-a3f43a57dd62	e592612f-44a8-4c00-9710-5499bb39f25c	2026-07-01 18:18:37.747187+00
\.


--
-- TOC entry 4303 (class 0 OID 17113)
-- Dependencies: 244
-- Data for Name: media_saves; Type: TABLE DATA; Schema: public; Owner: admin
--

COPY public.media_saves (id, media_id, user_id, created_at) FROM stdin;
f7698777-c21c-4dd6-a586-501f0620bf73	433caadb-7bce-4656-a89a-43f9d97802f3	df19ab97-ed7e-48ef-979d-b14e0d9d1641	2026-06-23 17:31:20.315134+00
079e91bf-859a-483b-9586-b5eb16d7e1b2	e9325c09-5243-49b4-9bec-fb5c4a72cc1d	e592612f-44a8-4c00-9710-5499bb39f25c	2026-06-24 03:23:53.923927+00
ae0d7e4e-bd33-4aba-9a98-76e335d5e084	984adebb-0b82-4496-ad95-a51448c760d1	e592612f-44a8-4c00-9710-5499bb39f25c	2026-06-24 03:29:56.920946+00
9d7814a7-97bb-40e5-a667-b98d4fbb7c20	e9325c09-5243-49b4-9bec-fb5c4a72cc1d	5ee97315-2807-4042-a155-fe8714193a23	2026-06-24 15:42:30.82277+00
06b4ffcd-4778-4814-8c0b-ff88a0311e59	984adebb-0b82-4496-ad95-a51448c760d1	5ee97315-2807-4042-a155-fe8714193a23	2026-06-24 15:42:40.418046+00
81d60af9-2b8d-48dc-9447-217b46f4d0ad	ac4d2bef-d957-4bc5-8255-5a6af2c1b21e	e592612f-44a8-4c00-9710-5499bb39f25c	2026-06-28 16:11:36.618939+00
\.


--
-- TOC entry 4304 (class 0 OID 17130)
-- Dependencies: 245
-- Data for Name: media_watch_history; Type: TABLE DATA; Schema: public; Owner: admin
--

COPY public.media_watch_history (id, user_id, media_id, watched_at, is_point_earned) FROM stdin;
08170cff-e75c-42ae-b041-90bcc8152adb	5ee97315-2807-4042-a155-fe8714193a23	6a197cf8-e55a-4f13-8d05-d6dced7b4439	2026-06-23 17:10:49.8581+00	f
bf3efd30-cbde-4578-be28-2be0720b7b66	5ee97315-2807-4042-a155-fe8714193a23	a864da3a-343d-4fd6-9715-6f3e94ab1661	2026-06-23 17:17:17.23837+00	f
b8ea923f-aa1a-4125-a83b-f4e9a24ebddc	5ee97315-2807-4042-a155-fe8714193a23	47a21bc2-b45a-4fb3-b98a-656dd0f6d2e4	2026-06-23 17:17:18.694274+00	f
0fe070f6-fddd-4926-a2d2-e271a2876886	e592612f-44a8-4c00-9710-5499bb39f25c	433caadb-7bce-4656-a89a-43f9d97802f3	2026-06-23 17:30:08.676388+00	f
4d8167e8-afd5-452a-ab1c-2f2fbc41fddd	e592612f-44a8-4c00-9710-5499bb39f25c	a864da3a-343d-4fd6-9715-6f3e94ab1661	2026-06-23 17:30:10.413297+00	f
2d1edfab-40e2-40c1-add1-a5791355c965	e592612f-44a8-4c00-9710-5499bb39f25c	47a21bc2-b45a-4fb3-b98a-656dd0f6d2e4	2026-06-23 17:30:14.340373+00	f
ece289f0-66f1-4507-a0de-7942cc192d51	e592612f-44a8-4c00-9710-5499bb39f25c	6a197cf8-e55a-4f13-8d05-d6dced7b4439	2026-06-23 17:30:19.571384+00	f
a583ab36-e808-4683-a5b4-e368247caf5c	df19ab97-ed7e-48ef-979d-b14e0d9d1641	e744b0f1-909f-4a4f-b883-7f876c83b8b1	2026-06-23 17:31:16.697707+00	f
3742da53-f2f8-4f2c-9736-b767c9357bb5	df19ab97-ed7e-48ef-979d-b14e0d9d1641	433caadb-7bce-4656-a89a-43f9d97802f3	2026-06-23 17:31:18.425315+00	f
03da277b-eab3-41e7-94b3-01b45ab6e6f9	df19ab97-ed7e-48ef-979d-b14e0d9d1641	a864da3a-343d-4fd6-9715-6f3e94ab1661	2026-06-23 17:43:55.573412+00	f
b30f6c4f-970a-4308-b409-f7f49a7ac100	e592612f-44a8-4c00-9710-5499bb39f25c	e744b0f1-909f-4a4f-b883-7f876c83b8b1	2026-06-23 17:45:32.477931+00	f
6d5fcc22-d1be-42ce-b99f-5187fe7ce544	e592612f-44a8-4c00-9710-5499bb39f25c	f11c0e8c-5c77-46f9-923c-088a1bc87e60	2026-06-23 18:05:24.188909+00	f
196e1ca2-6e1d-4636-ace8-5f16f76aaf67	e592612f-44a8-4c00-9710-5499bb39f25c	4d76be43-790f-4def-96fc-4d92bb4c6bdd	2026-06-23 18:07:10.118355+00	f
81bee992-2813-477f-bde4-ce61ff558984	e592612f-44a8-4c00-9710-5499bb39f25c	1bcff3c1-fb48-4d69-8af8-46320e556e8c	2026-06-23 18:17:23.467094+00	f
c968b600-0b4d-40e5-bc97-b7c3d86e7d08	e592612f-44a8-4c00-9710-5499bb39f25c	072285d3-0dd3-4b28-9310-53f9c7b3357c	2026-06-23 18:24:56.228718+00	f
89d10bc1-f781-45e5-a225-d6d78f0680f6	e592612f-44a8-4c00-9710-5499bb39f25c	ac4d2bef-d957-4bc5-8255-5a6af2c1b21e	2026-06-23 19:13:03.933179+00	f
2f63bbf0-b599-4785-ab11-0ad73b41744e	e7acf4c1-1f6b-4346-b2aa-95956bec3dd7	728f0ecf-4a7b-4109-bd4f-384aca425617	2026-06-23 19:35:42.161886+00	f
ef9521c3-afb9-4b1c-8a43-3f91cff26f06	e7acf4c1-1f6b-4346-b2aa-95956bec3dd7	dfc33ae6-35a4-446e-ad64-442dae6fd9ab	2026-06-23 19:35:57.328816+00	f
4008c19b-cdc9-4827-9b3b-ddbdcce9ad47	e7acf4c1-1f6b-4346-b2aa-95956bec3dd7	35dfb355-99e0-479b-b824-608c52f4e11e	2026-06-23 19:36:00.626307+00	f
6ad444fe-78da-41c1-ae9d-31b0cfe64cdf	e7acf4c1-1f6b-4346-b2aa-95956bec3dd7	2ebe21d3-c6c9-4d91-b6ab-a3f43a57dd62	2026-06-23 19:36:03.214031+00	f
da735476-13c5-4964-b4c3-99f6f2762178	e7acf4c1-1f6b-4346-b2aa-95956bec3dd7	ac4d2bef-d957-4bc5-8255-5a6af2c1b21e	2026-06-23 19:36:04.654894+00	f
4fd04f5f-21b9-48c4-bba8-891f7b3f704e	e7acf4c1-1f6b-4346-b2aa-95956bec3dd7	e9325c09-5243-49b4-9bec-fb5c4a72cc1d	2026-06-23 19:36:43.418035+00	f
fe8a53d9-b1db-42d5-aa4e-aa2fb119a8e2	df19ab97-ed7e-48ef-979d-b14e0d9d1641	e9325c09-5243-49b4-9bec-fb5c4a72cc1d	2026-06-23 19:40:54.092446+00	f
d85804c7-89b8-489b-bbc5-475958827066	df19ab97-ed7e-48ef-979d-b14e0d9d1641	728f0ecf-4a7b-4109-bd4f-384aca425617	2026-06-23 19:40:58.987383+00	f
32374f18-010c-4391-ac5f-e0c499a51ff2	df19ab97-ed7e-48ef-979d-b14e0d9d1641	dfc33ae6-35a4-446e-ad64-442dae6fd9ab	2026-06-23 19:41:02.027145+00	f
95712c10-7233-4065-b26d-79af87b0bd5b	df19ab97-ed7e-48ef-979d-b14e0d9d1641	35dfb355-99e0-479b-b824-608c52f4e11e	2026-06-23 19:41:03.72468+00	f
e53351df-5420-41b8-9435-e783fa948dac	df19ab97-ed7e-48ef-979d-b14e0d9d1641	2ebe21d3-c6c9-4d91-b6ab-a3f43a57dd62	2026-06-23 19:41:04.791593+00	f
a19d1e87-87be-4ddf-b2ba-8ac53d604f21	5ee97315-2807-4042-a155-fe8714193a23	35dfb355-99e0-479b-b824-608c52f4e11e	2026-06-24 08:27:27.667834+00	f
6c25a53c-463b-4e0c-bb4a-e3012ef0e922	5ee97315-2807-4042-a155-fe8714193a23	728f0ecf-4a7b-4109-bd4f-384aca425617	2026-06-24 08:28:07.155302+00	f
35e7219c-e823-46c4-9558-29ebb595497a	5ee97315-2807-4042-a155-fe8714193a23	2ebe21d3-c6c9-4d91-b6ab-a3f43a57dd62	2026-06-24 15:40:53.291402+00	f
e00bbb40-18a4-42bb-b07b-668e6b5066d5	e7acf4c1-1f6b-4346-b2aa-95956bec3dd7	984adebb-0b82-4496-ad95-a51448c760d1	2026-06-24 16:27:43.455144+00	f
f0c233a5-be05-4018-a076-27d5ed07bb60	df19ab97-ed7e-48ef-979d-b14e0d9d1641	984adebb-0b82-4496-ad95-a51448c760d1	2026-06-28 18:21:06.787004+00	f
0487e069-1180-4f8d-9e75-b743f3695189	e592612f-44a8-4c00-9710-5499bb39f25c	e9325c09-5243-49b4-9bec-fb5c4a72cc1d	2026-06-24 03:23:32.965176+00	t
0d490145-a7b9-4a0c-b5f3-5fb76cacba23	e592612f-44a8-4c00-9710-5499bb39f25c	984adebb-0b82-4496-ad95-a51448c760d1	2026-06-24 03:29:47.125115+00	t
6ec9929b-7427-4605-82f1-f0ef0d42cb26	e592612f-44a8-4c00-9710-5499bb39f25c	728f0ecf-4a7b-4109-bd4f-384aca425617	2026-06-28 16:11:27.543588+00	t
f544737d-674a-4567-b90d-ef50c0f208b0	e592612f-44a8-4c00-9710-5499bb39f25c	dfc33ae6-35a4-446e-ad64-442dae6fd9ab	2026-06-28 16:11:28.128608+00	t
3eaa1cc2-3bf0-42f9-9e14-06a70cc0e4e1	e592612f-44a8-4c00-9710-5499bb39f25c	35dfb355-99e0-479b-b824-608c52f4e11e	2026-06-28 16:11:29.077314+00	t
a7da7d1b-ee58-4543-9b22-4fbf96e363c5	e592612f-44a8-4c00-9710-5499bb39f25c	2ebe21d3-c6c9-4d91-b6ab-a3f43a57dd62	2026-06-23 19:14:34.245581+00	t
e919390a-bc08-4306-b9bc-d76a184bcd2b	5ee97315-2807-4042-a155-fe8714193a23	984adebb-0b82-4496-ad95-a51448c760d1	2026-06-24 07:55:48.457817+00	t
0958addc-285d-41af-bc2e-43419ddb8f1a	5ee97315-2807-4042-a155-fe8714193a23	e9325c09-5243-49b4-9bec-fb5c4a72cc1d	2026-06-24 08:28:04.277871+00	t
0f44e863-15ca-4fd6-9f02-7bd9fdd6c486	5ee97315-2807-4042-a155-fe8714193a23	dfc33ae6-35a4-446e-ad64-442dae6fd9ab	2026-06-23 19:27:56.29255+00	t
\.


--
-- TOC entry 4287 (class 0 OID 16792)
-- Dependencies: 228
-- Data for Name: notification_deliveries; Type: TABLE DATA; Schema: public; Owner: admin
--

COPY public.notification_deliveries (id, notification_id, channel, status, provider, provider_message_id, error_message, sent_at, created_at) FROM stdin;
\.


--
-- TOC entry 4280 (class 0 OID 16682)
-- Dependencies: 221
-- Data for Name: notifications; Type: TABLE DATA; Schema: public; Owner: admin
--

COPY public.notifications (id, user_id, type, title, body, reference_type, reference_id, is_read, read_at, created_at) FROM stdin;
dc5f7060-ec2a-4be2-9b1a-a96400e02db3	e592612f-44a8-4c00-9710-5499bb39f25c	media_liked	Videonuz yeni bir begeni aldi!	Paylastiginiz video yeni bir begeni aldi.	media	433caadb-7bce-4656-a89a-43f9d97802f3	f	\N	2026-06-23 17:31:21.473459+00
d3c2423e-b60b-4f36-94a9-93417084840b	e592612f-44a8-4c00-9710-5499bb39f25c	media_commented	Videonuz yeni bir yorum aldi!	Paylastiginiz videoya yeni bir yorum geldi.	media	433caadb-7bce-4656-a89a-43f9d97802f3	f	\N	2026-06-23 17:31:26.553512+00
6321b436-93f0-461a-ad87-a4d1957d5ccf	e592612f-44a8-4c00-9710-5499bb39f25c	media_liked	Videonuz yeni bir begeni aldi!	Paylastiginiz video yeni bir begeni aldi.	media	433caadb-7bce-4656-a89a-43f9d97802f3	f	\N	2026-06-23 17:31:30.84907+00
9fcb16d4-eb12-4523-9d4f-246aa9915e57	df19ab97-ed7e-48ef-979d-b14e0d9d1641	order_completed	Siparişiniz tamamlandı	yazilim ve tasarim plani ürününüz kütüphanenize eklendi.	order	23aa834c-746b-4fa5-a126-3dc749ebb142	f	\N	2026-06-23 17:31:43.288602+00
8ff57d90-0c38-4a83-a2cf-fdb922e6a1bc	e592612f-44a8-4c00-9710-5499bb39f25c	order_completed	Yeni sipariş aldınız	Muhammet Damla, yazilim ve tasarim plani ürününü satın aldı.	order	23aa834c-746b-4fa5-a126-3dc749ebb142	f	\N	2026-06-23 17:31:43.303714+00
e55c43aa-2f85-4386-99fc-5f4fe9a7eb63	df19ab97-ed7e-48ef-979d-b14e0d9d1641	order_completed	Siparişiniz tamamlandı	uiyt7utfty ürününüz kütüphanenize eklendi.	order	a85b9b67-8ec6-40ce-9ea4-af91aeb4f1f4	f	\N	2026-06-23 17:44:00.815643+00
d8b7cca6-4852-44d6-9665-24c78145bd65	5ee97315-2807-4042-a155-fe8714193a23	order_completed	Yeni sipariş aldınız	Muhammet Damla, uiyt7utfty ürününü satın aldı.	order	a85b9b67-8ec6-40ce-9ea4-af91aeb4f1f4	f	\N	2026-06-23 17:44:01.353335+00
6f01a540-e262-45d6-8daa-5a0aba4649da	e592612f-44a8-4c00-9710-5499bb39f25c	order_completed	Siparişiniz tamamlandı	sdf asdf sdf sdf ürününüz kütüphanenize eklendi.	order	1381b9fa-e306-4548-a0a7-4d6e6f8460c6	f	\N	2026-06-23 17:46:14.498635+00
e20b2871-6612-4e96-84b0-1a333d7497ff	df19ab97-ed7e-48ef-979d-b14e0d9d1641	order_completed	Yeni sipariş aldınız	Damca Yes, sdf asdf sdf sdf ürününü satın aldı.	order	1381b9fa-e306-4548-a0a7-4d6e6f8460c6	f	\N	2026-06-23 17:46:14.612962+00
19c84ca1-3286-4cc6-9f09-adf828ed9204	e592612f-44a8-4c00-9710-5499bb39f25c	order_completed	Siparişiniz tamamlandı	uiyt7utfty ürününüz kütüphanenize eklendi.	order	5d778a5f-3f9d-4a89-9cd0-562a45623552	f	\N	2026-06-23 17:54:16.77335+00
b288f2ae-b40b-45dd-b163-077d7fdd8c44	5ee97315-2807-4042-a155-fe8714193a23	order_completed	Yeni sipariş aldınız	Damca Yes, uiyt7utfty ürününü satın aldı.	order	5d778a5f-3f9d-4a89-9cd0-562a45623552	f	\N	2026-06-23 17:54:16.805907+00
fefb5074-ed8b-40f5-8bb2-7580a5f62faf	df19ab97-ed7e-48ef-979d-b14e0d9d1641	new_video	ScriptHouse yeni bir video paylaÅŸtÄ±!	Takip ettiÄŸiniz maÄŸaza yeni bir video paylaÅŸtÄ±.	media	f11c0e8c-5c77-46f9-923c-088a1bc87e60	f	\N	2026-06-23 18:05:18.666145+00
bdbe4403-56ef-4cba-b959-f254d6134248	df19ab97-ed7e-48ef-979d-b14e0d9d1641	new_video	ScriptHouse yeni bir video paylaÅŸtÄ±!	Takip ettiÄŸiniz maÄŸaza yeni bir video paylaÅŸtÄ±.	media	4d76be43-790f-4def-96fc-4d92bb4c6bdd	f	\N	2026-06-23 18:07:02.069565+00
1051cab4-a88b-472d-8ddc-07c5c13add0c	df19ab97-ed7e-48ef-979d-b14e0d9d1641	new_video	ScriptHouse yeni bir video paylaÅŸtÄ±!	Takip ettiÄŸiniz maÄŸaza yeni bir video paylaÅŸtÄ±.	media	1bcff3c1-fb48-4d69-8af8-46320e556e8c	f	\N	2026-06-23 18:17:18.190208+00
3f3bd468-2a1d-4e19-9039-704d3a399927	df19ab97-ed7e-48ef-979d-b14e0d9d1641	new_video	ScriptHouse yeni bir video paylaÅŸtÄ±!	Takip ettiÄŸiniz maÄŸaza yeni bir video paylaÅŸtÄ±.	media	072285d3-0dd3-4b28-9310-53f9c7b3357c	f	\N	2026-06-23 18:24:14.225422+00
737a726b-3cb2-46d1-8932-5e4fbae30423	df19ab97-ed7e-48ef-979d-b14e0d9d1641	new_video	ScriptHouse yeni bir video paylaÅŸtÄ±!	Takip ettiÄŸiniz maÄŸaza yeni bir video paylaÅŸtÄ±.	media	ac4d2bef-d957-4bc5-8255-5a6af2c1b21e	f	\N	2026-06-23 19:13:00.477186+00
23deace1-5595-43a6-b5fa-02b86e36142f	df19ab97-ed7e-48ef-979d-b14e0d9d1641	new_video	ScriptHouse yeni bir video paylaÅŸtÄ±!	Takip ettiÄŸiniz maÄŸaza yeni bir video paylaÅŸtÄ±.	media	2ebe21d3-c6c9-4d91-b6ab-a3f43a57dd62	f	\N	2026-06-23 19:14:18.376242+00
6a16ef0a-f311-4071-b932-651d3d955bd4	df19ab97-ed7e-48ef-979d-b14e0d9d1641	order_completed	Siparişiniz tamamlandı	arka Kimliği (Brand Board) Tasarım Şablonu (Figma/PSD) ürününüz kütüphanenize eklendi.	order	12b62f6c-c6a5-4147-9c60-4bf4d8ce2c69	f	\N	2026-06-23 19:58:39.013167+00
740fd779-2fbe-4011-9dd4-e83c18532239	e7acf4c1-1f6b-4346-b2aa-95956bec3dd7	order_completed	Yeni sipariş aldınız	Muhammet Damla, arka Kimliği (Brand Board) Tasarım Şablonu (Figma/PSD) ürününü satın aldı.	order	12b62f6c-c6a5-4147-9c60-4bf4d8ce2c69	f	\N	2026-06-23 19:58:39.187452+00
86d4bfe2-1871-4e0b-bac7-18345734ccbb	e7acf4c1-1f6b-4346-b2aa-95956bec3dd7	media_liked	Videonuz yeni bir begeni aldi!	Paylastiginiz video yeni bir begeni aldi.	media	e9325c09-5243-49b4-9bec-fb5c4a72cc1d	f	\N	2026-06-24 03:23:42.413201+00
736c319d-6e42-4ef7-a54e-01f0f5f8e56b	e7acf4c1-1f6b-4346-b2aa-95956bec3dd7	media_commented	Videonuz yeni bir yorum aldi!	Paylastiginiz videoya yeni bir yorum geldi.	media	e9325c09-5243-49b4-9bec-fb5c4a72cc1d	f	\N	2026-06-24 03:23:50.146742+00
9d638b1c-21e6-42ca-9c37-9030a4e4eff7	e7acf4c1-1f6b-4346-b2aa-95956bec3dd7	media_liked	Videonuz yeni bir begeni aldi!	Paylastiginiz video yeni bir begeni aldi.	media	e9325c09-5243-49b4-9bec-fb5c4a72cc1d	f	\N	2026-06-24 03:23:55.4756+00
dba6813f-9cc5-423a-a5e0-144bdb18e053	df19ab97-ed7e-48ef-979d-b14e0d9d1641	new_video	ScriptHouse yeni bir video paylaÅŸtÄ±!	Takip ettiÄŸiniz maÄŸaza yeni bir video paylaÅŸtÄ±.	media	984adebb-0b82-4496-ad95-a51448c760d1	f	\N	2026-06-24 03:29:30.686929+00
a867ce34-9f6a-4f7a-897b-c1107892c0f0	e592612f-44a8-4c00-9710-5499bb39f25c	media_liked	Videonuz yeni bir begeni aldi!	Paylastiginiz video yeni bir begeni aldi.	media	984adebb-0b82-4496-ad95-a51448c760d1	f	\N	2026-06-24 08:27:39.626387+00
1e62d0d2-06b3-43b2-a0b2-fb42045a5157	e7acf4c1-1f6b-4346-b2aa-95956bec3dd7	media_commented	Videonuz yeni bir yorum aldi!	Paylastiginiz videoya yeni bir yorum geldi.	media	728f0ecf-4a7b-4109-bd4f-384aca425617	f	\N	2026-06-24 08:28:15.072451+00
f2582710-9b21-4d4e-9f00-dc3f32da000a	e7acf4c1-1f6b-4346-b2aa-95956bec3dd7	media_liked	Videonuz yeni bir begeni aldi!	Paylastiginiz video yeni bir begeni aldi.	media	e9325c09-5243-49b4-9bec-fb5c4a72cc1d	f	\N	2026-06-24 15:42:30.98197+00
48f9941a-88cc-49e5-9465-8145a1d6634c	e592612f-44a8-4c00-9710-5499bb39f25c	order_completed	Siparişiniz tamamlandı	kaka ürününüz kütüphanenize eklendi.	order	4d34d7a0-3ff9-4231-9732-1b56c6a85e9e	f	\N	2026-06-24 16:22:56.286675+00
440f6bbf-9e84-4448-bf74-4973b83c761c	5ee97315-2807-4042-a155-fe8714193a23	order_completed	Yeni sipariş aldınız	Damca Yes, kaka ürününü satın aldı.	order	4d34d7a0-3ff9-4231-9732-1b56c6a85e9e	f	\N	2026-06-24 16:22:56.318662+00
a4f0851a-ce78-49e8-b373-c3a1835a0c27	e7acf4c1-1f6b-4346-b2aa-95956bec3dd7	order_completed	Siparişiniz tamamlandı	react Query kursu ürününüz kütüphanenize eklendi.	order	eed29adc-4112-4efa-b072-f168e4964831	f	\N	2026-06-24 16:57:02.52021+00
a8772153-37b9-422f-9eed-9c772592bd91	5ee97315-2807-4042-a155-fe8714193a23	order_completed	Yeni sipariş aldınız	Mahmut Damlaki, react Query kursu ürününü satın aldı.	order	eed29adc-4112-4efa-b072-f168e4964831	f	\N	2026-06-24 16:57:02.542262+00
8f8315b7-b210-4d3e-957d-c4b925f4d4b8	df19ab97-ed7e-48ef-979d-b14e0d9d1641	order_completed	Siparişiniz tamamlandı	react Query kursu ürününüz kütüphanenize eklendi.	order	b086d9d7-ec4c-475c-84d7-3c115220b42d	f	\N	2026-06-28 18:18:15.78727+00
e5956070-b41b-4e98-be25-d25add11dc47	5ee97315-2807-4042-a155-fe8714193a23	order_completed	Yeni sipariş aldınız	Muhammet Damla, react Query kursu ürününü satın aldı.	order	b086d9d7-ec4c-475c-84d7-3c115220b42d	f	\N	2026-06-28 18:18:16.385366+00
59247e04-f97a-4dc8-a87e-11653370e7c4	df19ab97-ed7e-48ef-979d-b14e0d9d1641	order_completed	Siparişiniz tamamlandı	kaka ürününüz kütüphanenize eklendi.	order	a713d2d0-04ff-4db1-a819-be320d932aa2	f	\N	2026-06-28 18:35:45.796479+00
50e17bb9-513c-4d2a-af1c-506807311882	5ee97315-2807-4042-a155-fe8714193a23	order_completed	Yeni sipariş aldınız	Muhammet Damla, kaka ürününü satın aldı.	order	a713d2d0-04ff-4db1-a819-be320d932aa2	f	\N	2026-06-28 18:35:45.839726+00
fa172b1b-9874-4d6e-819b-1158e0d88391	e7acf4c1-1f6b-4346-b2aa-95956bec3dd7	order_completed	Siparişiniz tamamlandı	kaka ürününüz kütüphanenize eklendi.	order	b7d9ad8e-a148-44b6-b435-e842a51f4760	f	\N	2026-06-28 19:06:04.616445+00
42fa42af-5f33-432c-aa9b-ca703a9d3a50	5ee97315-2807-4042-a155-fe8714193a23	order_completed	Yeni sipariş aldınız	Mahmut Damlaki, kaka ürününü satın aldı.	order	b7d9ad8e-a148-44b6-b435-e842a51f4760	f	\N	2026-06-28 19:06:04.70894+00
1b1cce69-aaec-4c92-9fc5-d0eddb454cde	5ee97315-2807-4042-a155-fe8714193a23	media_liked	Videonuz yeni bir begeni aldi!	Paylastiginiz video yeni bir begeni aldi.	media	dfc33ae6-35a4-446e-ad64-442dae6fd9ab	f	\N	2026-06-30 19:38:56.57114+00
c90319b3-ee7b-4b9d-a2e3-3ddcbabc66d6	e592612f-44a8-4c00-9710-5499bb39f25c	order_completed	Siparişiniz tamamlandı	aura jewel ürününüz kütüphanenize eklendi.	order	f1d03b27-6f0f-47ae-8d05-ac94e19007c1	f	\N	2026-06-30 19:39:00.158297+00
c9625aa4-2f3c-4dc3-8638-457d5d08e626	5ee97315-2807-4042-a155-fe8714193a23	order_completed	Yeni sipariş aldınız	Damca Yes, aura jewel ürününü satın aldı.	order	f1d03b27-6f0f-47ae-8d05-ac94e19007c1	f	\N	2026-06-30 19:39:01.775203+00
e94484fe-6958-4184-9d11-597fdf9a8a1f	e7acf4c1-1f6b-4346-b2aa-95956bec3dd7	media_liked	Videonuz yeni bir begeni aldi!	Paylastiginiz video yeni bir begeni aldi.	media	728f0ecf-4a7b-4109-bd4f-384aca425617	f	\N	2026-07-01 18:10:59.184539+00
8abae1e4-665f-43ed-a90d-af145b7d3172	e7acf4c1-1f6b-4346-b2aa-95956bec3dd7	media_commented	Videonuz yeni bir yorum aldi!	Paylastiginiz videoya yeni bir yorum geldi.	media	728f0ecf-4a7b-4109-bd4f-384aca425617	f	\N	2026-07-01 18:11:04.25267+00
310817eb-7e79-441f-94cd-58287f710efa	e592612f-44a8-4c00-9710-5499bb39f25c	order_completed	Siparişiniz tamamlandı	Growth Hub ürününüz kütüphanenize eklendi.	order	f88e2864-af85-418a-8e49-a448e744cd28	f	\N	2026-07-01 18:11:26.379276+00
7a8e352b-7f00-4026-94d3-a678e4b27898	e7acf4c1-1f6b-4346-b2aa-95956bec3dd7	order_completed	Yeni sipariş aldınız	Damca Yes, Growth Hub ürününü satın aldı.	order	f88e2864-af85-418a-8e49-a448e744cd28	f	\N	2026-07-01 18:11:26.564581+00
d9870e1c-651f-4638-b086-ec46d9cfde0a	df19ab97-ed7e-48ef-979d-b14e0d9d1641	sale_completed	Yeni Satış! 🎉	Test bildirimi	order	2cec166b-4d56-4ad4-9510-6c6689c8218e	f	\N	2026-07-05 17:09:18.766626+00
\.


--
-- TOC entry 4296 (class 0 OID 16972)
-- Dependencies: 237
-- Data for Name: orders; Type: TABLE DATA; Schema: public; Owner: admin
--

COPY public.orders (id, buyer_id, product_id, shop_id, order_number, amount, currency, platform_fee, seller_earnings, status, stripe_payment_id, invoice_pdf_url, created_at, updated_at) FROM stdin;
23aa834c-746b-4fa5-a126-3dc749ebb142	df19ab97-ed7e-48ef-979d-b14e0d9d1641	88959f5f-ee94-47b0-830a-106e94b7aec9	9c3432a1-66b8-4856-bce0-9ef44654b69f	ORD-2026-5564CEA9C2F14	1299.00	USD	12.99	1286.01	completed	\N	https://192.168.1.207:9000/invoices/invoices/23aa834c-746b-4fa5-a126-3dc749ebb142.pdf?X-Amz-Expires=604800&X-Amz-Algorithm=AWS4-HMAC-SHA256&X-Amz-Credential=admin%2F20260623%2Fus-east-1%2Fs3%2Faws4_request&X-Amz-Date=20260623T173147Z&X-Amz-SignedHeaders=host&X-Amz-Signature=e42abff1725a4f9083b6db21fca39589e0bea01d2e519a637b739de0c2704553	2026-06-23 17:31:42.80576+00	2026-06-23 17:31:47.24738+00
a85b9b67-8ec6-40ce-9ea4-af91aeb4f1f4	df19ab97-ed7e-48ef-979d-b14e0d9d1641	5ebe3845-e80d-4d79-bf5d-63947f802908	2fc28588-622f-40e5-8ee6-79ed0613c8fa	ORD-2026-109714178B7A4	67456.00	USD	674.56	66781.44	completed	\N	https://192.168.1.207:9000/invoices/invoices/a85b9b67-8ec6-40ce-9ea4-af91aeb4f1f4.pdf?X-Amz-Expires=604800&X-Amz-Algorithm=AWS4-HMAC-SHA256&X-Amz-Credential=admin%2F20260623%2Fus-east-1%2Fs3%2Faws4_request&X-Amz-Date=20260623T174403Z&X-Amz-SignedHeaders=host&X-Amz-Signature=043d35dbb5e693a4f5de0824d11bfd2122228c25a77cc358192813f62fcadfa2	2026-06-23 17:44:00.165319+00	2026-06-23 17:44:03.780391+00
b086d9d7-ec4c-475c-84d7-3c115220b42d	df19ab97-ed7e-48ef-979d-b14e0d9d1641	e1abe8c0-5561-4f55-bd52-de6b105b76fe	2fc28588-622f-40e5-8ee6-79ed0613c8fa	ORD-2026-BE8E7F2A9A2D4	1299.00	USD	12.99	1286.01	completed	\N	https://localhost:9000/invoices/invoices/b086d9d7-ec4c-475c-84d7-3c115220b42d.pdf?X-Amz-Expires=604800&X-Amz-Algorithm=AWS4-HMAC-SHA256&X-Amz-Credential=admin%2F20260628%2Fus-east-1%2Fs3%2Faws4_request&X-Amz-Date=20260628T181817Z&X-Amz-SignedHeaders=host&X-Amz-Signature=f822a3d55961a36840b278a3c89bf735e3183f422785777aa440c3e2658449ed	2026-06-28 18:18:15.406475+00	2026-06-28 18:18:17.283414+00
1381b9fa-e306-4548-a0a7-4d6e6f8460c6	e592612f-44a8-4c00-9710-5499bb39f25c	5ef46d22-8943-4960-87c5-2a160bd06740	f9160c3b-3bd7-49ad-8ac8-f98c8db5d48d	ORD-2026-387308D97D264	1243.00	USD	12.43	1230.57	completed	\N	https://192.168.1.207:9000/invoices/invoices/1381b9fa-e306-4548-a0a7-4d6e6f8460c6.pdf?X-Amz-Expires=604800&X-Amz-Algorithm=AWS4-HMAC-SHA256&X-Amz-Credential=admin%2F20260623%2Fus-east-1%2Fs3%2Faws4_request&X-Amz-Date=20260623T174615Z&X-Amz-SignedHeaders=host&X-Amz-Signature=c674983bdd6ed03f2099d84e871011dca0c66672a1882cf82255f3e3d99f8d7d	2026-06-23 17:46:14.303522+00	2026-06-23 17:46:15.301634+00
5d778a5f-3f9d-4a89-9cd0-562a45623552	e592612f-44a8-4c00-9710-5499bb39f25c	5ebe3845-e80d-4d79-bf5d-63947f802908	2fc28588-622f-40e5-8ee6-79ed0613c8fa	ORD-2026-0FBA523ABFEC4	67456.00	USD	674.56	66781.44	completed	\N	https://192.168.1.207:9000/invoices/invoices/5d778a5f-3f9d-4a89-9cd0-562a45623552.pdf?X-Amz-Expires=604800&X-Amz-Algorithm=AWS4-HMAC-SHA256&X-Amz-Credential=admin%2F20260623%2Fus-east-1%2Fs3%2Faws4_request&X-Amz-Date=20260623T175417Z&X-Amz-SignedHeaders=host&X-Amz-Signature=bb30b98ee9971a173edd52c67b77a0ed558b7174c5dd5af6a66e28340dac7569	2026-06-23 17:54:16.71443+00	2026-06-23 17:54:17.152529+00
12b62f6c-c6a5-4147-9c60-4bf4d8ce2c69	df19ab97-ed7e-48ef-979d-b14e0d9d1641	07614073-8751-469e-ad10-dcccd6f5c5d0	be43220d-cf62-4f0f-9eb2-39c4eb5a3be2	ORD-2026-FA3E994C6FE54	45.00	USD	0.45	44.55	completed	\N	https://192.168.1.207:9000/invoices/invoices/12b62f6c-c6a5-4147-9c60-4bf4d8ce2c69.pdf?X-Amz-Expires=604800&X-Amz-Algorithm=AWS4-HMAC-SHA256&X-Amz-Credential=admin%2F20260623%2Fus-east-1%2Fs3%2Faws4_request&X-Amz-Date=20260623T195839Z&X-Amz-SignedHeaders=host&X-Amz-Signature=045a8ca26b75d4d8bc5e52c833c79b0ffc544701fc54f4ddfa69e55ec8014457	2026-06-23 19:58:38.848447+00	2026-06-23 19:58:39.869301+00
4d34d7a0-3ff9-4231-9732-1b56c6a85e9e	e592612f-44a8-4c00-9710-5499bb39f25c	423bf130-b4e5-4948-9d2f-c4d7b5a196ff	2fc28588-622f-40e5-8ee6-79ed0613c8fa	ORD-2026-B9935E1D3E614	1299.00	USD	12.99	1286.01	completed	\N	https://localhost:9000/invoices/invoices/4d34d7a0-3ff9-4231-9732-1b56c6a85e9e.pdf?X-Amz-Expires=604800&X-Amz-Algorithm=AWS4-HMAC-SHA256&X-Amz-Credential=admin%2F20260624%2Fus-east-1%2Fs3%2Faws4_request&X-Amz-Date=20260624T162258Z&X-Amz-SignedHeaders=host&X-Amz-Signature=8ce03d62e7bc8b001c8cc9d44ddea347d54306575533348496afddfbee49e836	2026-06-24 16:22:55.988158+00	2026-06-24 16:22:58.520451+00
a713d2d0-04ff-4db1-a819-be320d932aa2	df19ab97-ed7e-48ef-979d-b14e0d9d1641	423bf130-b4e5-4948-9d2f-c4d7b5a196ff	2fc28588-622f-40e5-8ee6-79ed0613c8fa	ORD-2026-D7750E38143F4	1299.00	USD	12.99	1286.01	completed	\N	https://localhost:9000/invoices/invoices/a713d2d0-04ff-4db1-a819-be320d932aa2.pdf?X-Amz-Expires=604800&X-Amz-Algorithm=AWS4-HMAC-SHA256&X-Amz-Credential=admin%2F20260628%2Fus-east-1%2Fs3%2Faws4_request&X-Amz-Date=20260628T183546Z&X-Amz-SignedHeaders=host&X-Amz-Signature=f1169c10f1f52b0821aec8b88cdfc7720747b55bf0af68524050142d13db0da8	2026-06-28 18:35:45.689268+00	2026-06-28 18:35:46.588447+00
eed29adc-4112-4efa-b072-f168e4964831	e7acf4c1-1f6b-4346-b2aa-95956bec3dd7	e1abe8c0-5561-4f55-bd52-de6b105b76fe	2fc28588-622f-40e5-8ee6-79ed0613c8fa	ORD-2026-3ADAC78171A64	1299.00	USD	12.99	1286.01	completed	\N	https://localhost:9000/invoices/invoices/eed29adc-4112-4efa-b072-f168e4964831.pdf?X-Amz-Expires=604800&X-Amz-Algorithm=AWS4-HMAC-SHA256&X-Amz-Credential=admin%2F20260624%2Fus-east-1%2Fs3%2Faws4_request&X-Amz-Date=20260624T165703Z&X-Amz-SignedHeaders=host&X-Amz-Signature=131b6c8664a49c5ae4b5a99aa4984d47f43751e466e957a9e2ece4678d94a804	2026-06-24 16:57:02.466874+00	2026-06-24 16:57:03.188137+00
b7d9ad8e-a148-44b6-b435-e842a51f4760	e7acf4c1-1f6b-4346-b2aa-95956bec3dd7	423bf130-b4e5-4948-9d2f-c4d7b5a196ff	2fc28588-622f-40e5-8ee6-79ed0613c8fa	ORD-2026-6B31E99E58274	1299.00	USD	12.99	1286.01	completed	\N	https://localhost:9000/invoices/invoices/b7d9ad8e-a148-44b6-b435-e842a51f4760.pdf?X-Amz-Expires=604800&X-Amz-Algorithm=AWS4-HMAC-SHA256&X-Amz-Credential=admin%2F20260628%2Fus-east-1%2Fs3%2Faws4_request&X-Amz-Date=20260628T190606Z&X-Amz-SignedHeaders=host&X-Amz-Signature=8896fe23b10fe82d857ce765275a8f38bbee370b428fa11d40005393081f33bf	2026-06-28 19:06:04.372116+00	2026-06-28 19:06:06.286648+00
f88e2864-af85-418a-8e49-a448e744cd28	e592612f-44a8-4c00-9710-5499bb39f25c	86093178-173e-4c26-8bd8-6f270cef116d	be43220d-cf62-4f0f-9eb2-39c4eb5a3be2	ORD-2026-8156151D26ED4	70.00	USD	0.70	69.30	completed	\N	http://localhost:9000/invoices/invoices/f88e2864-af85-418a-8e49-a448e744cd28.pdf?X-Amz-Expires=604800&X-Amz-Algorithm=AWS4-HMAC-SHA256&X-Amz-Credential=admin%2F20260701%2Fus-east-1%2Fs3%2Faws4_request&X-Amz-Date=20260701T181128Z&X-Amz-SignedHeaders=host&X-Amz-Signature=cc0c04d5b6ce53365b47bc1fe82dd04562194998645cc1aceeb2da3d556beb2b	2026-07-01 18:11:25.628509+00	2026-07-01 18:11:28.467291+00
f1d03b27-6f0f-47ae-8d05-ac94e19007c1	e592612f-44a8-4c00-9710-5499bb39f25c	c2fa330d-ec90-450e-b051-059636ac3ed7	2fc28588-622f-40e5-8ee6-79ed0613c8fa	ORD-2026-2C3F4A3E1DC84	799.00	USD	7.99	791.01	completed	\N	http://localhost:9000/invoices/invoices/f1d03b27-6f0f-47ae-8d05-ac94e19007c1.pdf?X-Amz-Expires=604800&X-Amz-Algorithm=AWS4-HMAC-SHA256&X-Amz-Credential=admin%2F20260630%2Fus-east-1%2Fs3%2Faws4_request&X-Amz-Date=20260630T193910Z&X-Amz-SignedHeaders=host&X-Amz-Signature=6d3f8c9cc27893c330d7c89f45ffa570631654a56931093fabcc4c4e538019ca	2026-06-30 19:38:57.977559+00	2026-06-30 19:39:11.099216+00
\.


--
-- TOC entry 4306 (class 0 OID 17170)
-- Dependencies: 247
-- Data for Name: payments; Type: TABLE DATA; Schema: public; Owner: admin
--

COPY public.payments (id, order_id, payment_provider, provider_transaction_id, gross_amount, platform_fee_amount, net_earnings, status, error_message, created_at, updated_at) FROM stdin;
48b5f74f-23dc-48d8-868c-2c6cbdae04d2	23aa834c-746b-4fa5-a126-3dc749ebb142	mock	txn_mock_c6e09f61481b4a7f950dd8f23dbb3ea4	1299.00	12.99	1286.01	succeeded	\N	2026-06-23 17:31:43.061524+00	2026-06-23 17:31:43.061576+00
49d55860-a895-4959-8e1a-b49a48b76020	a85b9b67-8ec6-40ce-9ea4-af91aeb4f1f4	mock	txn_mock_79aacec981ec4374b5a24bcfa51a4a2d	67456.00	674.56	66781.44	succeeded	\N	2026-06-23 17:44:00.497959+00	2026-06-23 17:44:00.49796+00
ad6d3a10-d4b5-4d7e-986c-4860ee16bcbb	1381b9fa-e306-4548-a0a7-4d6e6f8460c6	mock	txn_mock_b98c7b2f77474ee58254b18c58ca3b0b	1243.00	12.43	1230.57	succeeded	\N	2026-06-23 17:46:14.433902+00	2026-06-23 17:46:14.433903+00
6d41c4bf-ebf1-4566-bad7-a9db29d0c284	5d778a5f-3f9d-4a89-9cd0-562a45623552	mock	txn_mock_32718e725d1649c193acd08aceb4919c	67456.00	674.56	66781.44	succeeded	\N	2026-06-23 17:54:16.744849+00	2026-06-23 17:54:16.744849+00
c77742be-50e2-4402-970d-a91bcc8a6a10	12b62f6c-c6a5-4147-9c60-4bf4d8ce2c69	mock	txn_mock_e2daea04ac684299bb9fcb6b25f7e67b	45.00	0.45	44.55	succeeded	\N	2026-06-23 19:58:38.953171+00	2026-06-23 19:58:38.953172+00
85c689e2-21fa-4b7e-a5ec-9405dcc732bf	4d34d7a0-3ff9-4231-9732-1b56c6a85e9e	mock	txn_mock_02446ff3aaab41b3885cdd9a6d6c8d7e	1299.00	12.99	1286.01	succeeded	\N	2026-06-24 16:22:56.161438+00	2026-06-24 16:22:56.161464+00
748b0b94-d457-464d-9ed7-02dd3b3dc7b5	eed29adc-4112-4efa-b072-f168e4964831	mock	txn_mock_98ad0fa1bc474f73a3382770b41e5fe2	1299.00	12.99	1286.01	succeeded	\N	2026-06-24 16:57:02.492326+00	2026-06-24 16:57:02.492327+00
0eaa0f1a-032f-4d0c-b62c-980647c35aac	b086d9d7-ec4c-475c-84d7-3c115220b42d	mock	txn_mock_ad38d82c8b4e4a66b536ed906e710e37	1299.00	12.99	1286.01	succeeded	\N	2026-06-28 18:18:15.638643+00	2026-06-28 18:18:15.638668+00
a2104fc2-833e-4be3-9cca-9dced7dc2b92	a713d2d0-04ff-4db1-a819-be320d932aa2	mock	txn_mock_a0adb435c6a14750b573cce4d6e7738f	1299.00	12.99	1286.01	succeeded	\N	2026-06-28 18:35:45.746178+00	2026-06-28 18:35:45.746179+00
429369f4-88dc-4634-886b-d386034f4aa3	b7d9ad8e-a148-44b6-b435-e842a51f4760	mock	txn_mock_e4006cdc34e640e9a14e71f341ca2370	1299.00	12.99	1286.01	succeeded	\N	2026-06-28 19:06:04.475036+00	2026-06-28 19:06:04.475037+00
5c646510-85fe-4262-af29-9185f673602c	f1d03b27-6f0f-47ae-8d05-ac94e19007c1	mock	txn_mock_22a7893829e94313b9f5cc028cf8f30f	799.00	7.99	791.01	succeeded	\N	2026-06-30 19:38:59.522766+00	2026-06-30 19:38:59.522832+00
869efdba-a4e0-49d8-b640-388cdf1aba99	f88e2864-af85-418a-8e49-a448e744cd28	mock	txn_mock_f0ceea62a75a45a298d59a75c3c95533	70.00	0.70	69.30	succeeded	\N	2026-07-01 18:11:26.013402+00	2026-07-01 18:11:26.013458+00
\.


--
-- TOC entry 4281 (class 0 OID 16697)
-- Dependencies: 222
-- Data for Name: point_logs; Type: TABLE DATA; Schema: public; Owner: admin
--

COPY public.point_logs (id, user_id, action_type, points_earned, reference_id, created_at) FROM stdin;
d3f72eda-7765-4920-9cf4-debbe82cc264	e592612f-44a8-4c00-9710-5499bb39f25c	make_sale	20.00	23aa834c-746b-4fa5-a126-3dc749ebb142	2026-06-23 17:31:42.800654+00
d7adafcc-c553-4104-aa91-8c2254dc0e75	5ee97315-2807-4042-a155-fe8714193a23	make_sale	20.00	a85b9b67-8ec6-40ce-9ea4-af91aeb4f1f4	2026-06-23 17:44:00.163024+00
ee343bf3-d539-4744-b570-41e7bd0f505e	df19ab97-ed7e-48ef-979d-b14e0d9d1641	make_sale	20.00	1381b9fa-e306-4548-a0a7-4d6e6f8460c6	2026-06-23 17:46:14.302797+00
d2c19f4d-0b6c-421c-8c4d-44fa3d5a401c	5ee97315-2807-4042-a155-fe8714193a23	make_sale	20.00	5d778a5f-3f9d-4a89-9cd0-562a45623552	2026-06-23 17:54:16.709394+00
e531f467-da9e-40dd-8862-1aba57203441	e7acf4c1-1f6b-4346-b2aa-95956bec3dd7	make_sale	20.00	12b62f6c-c6a5-4147-9c60-4bf4d8ce2c69	2026-06-23 19:58:38.814224+00
5389ea67-de0a-4a56-b4e6-2a29cbf81b21	5ee97315-2807-4042-a155-fe8714193a23	make_sale	20.00	4d34d7a0-3ff9-4231-9732-1b56c6a85e9e	2026-06-24 16:22:55.982329+00
92fd4bc5-61b0-44d0-861f-8a5a39730670	5ee97315-2807-4042-a155-fe8714193a23	make_sale	20.00	eed29adc-4112-4efa-b072-f168e4964831	2026-06-24 16:57:02.463652+00
4b1cc9b6-4ad5-4d7d-a4d3-8d1dcc913a5b	5ee97315-2807-4042-a155-fe8714193a23	make_sale	20.00	b086d9d7-ec4c-475c-84d7-3c115220b42d	2026-06-28 18:18:15.389662+00
065ec3d3-5b27-4f35-a8e7-f4d024faa95f	5ee97315-2807-4042-a155-fe8714193a23	make_sale	20.00	a713d2d0-04ff-4db1-a819-be320d932aa2	2026-06-28 18:35:45.687084+00
dcff26ab-2f6c-4182-9162-99f5125ca1ae	5ee97315-2807-4042-a155-fe8714193a23	make_sale	20.00	b7d9ad8e-a148-44b6-b435-e842a51f4760	2026-06-28 19:06:04.370091+00
d4625a82-5732-4956-9684-b5a6e7bf4bc9	5ee97315-2807-4042-a155-fe8714193a23	make_sale	20.00	f1d03b27-6f0f-47ae-8d05-ac94e19007c1	2026-06-30 19:38:57.881602+00
4a323fa5-a35a-4e2d-9602-a08512f152de	e592612f-44a8-4c00-9710-5499bb39f25c	watch_reels	1.00	e9325c09-5243-49b4-9bec-fb5c4a72cc1d	2026-07-01 18:10:26.070273+00
8f1ad0ee-a2e3-4bd9-adef-cbf5906f4205	e592612f-44a8-4c00-9710-5499bb39f25c	watch_reels	1.00	984adebb-0b82-4496-ad95-a51448c760d1	2026-07-01 18:10:25.687962+00
40dbf05b-67bb-4301-ad31-c085857f969c	e592612f-44a8-4c00-9710-5499bb39f25c	watch_reels	1.00	728f0ecf-4a7b-4109-bd4f-384aca425617	2026-07-01 18:10:41.899755+00
fe4dfa06-3857-4314-8149-53f52efd477e	e592612f-44a8-4c00-9710-5499bb39f25c	watch_reels	1.00	dfc33ae6-35a4-446e-ad64-442dae6fd9ab	2026-07-01 18:10:48.551912+00
4b848f16-700a-4a2f-881c-afe42cfaf032	e7acf4c1-1f6b-4346-b2aa-95956bec3dd7	receive_like	1.00	728f0ecf-4a7b-4109-bd4f-384aca425617	2026-07-01 18:10:59.104496+00
5cbc0627-8556-443e-816f-cb7a25ad17d5	e7acf4c1-1f6b-4346-b2aa-95956bec3dd7	make_sale	20.00	f88e2864-af85-418a-8e49-a448e744cd28	2026-07-01 18:11:25.624863+00
e02bdc2d-ad6e-4130-9a91-1de1677ec9a2	e592612f-44a8-4c00-9710-5499bb39f25c	purchase_product	5.00	f88e2864-af85-418a-8e49-a448e744cd28	2026-07-01 18:11:26.287507+00
7bec4b71-3df8-4990-8c2b-be45c97c4a48	e592612f-44a8-4c00-9710-5499bb39f25c	create_product	10.00	974e0e3f-e909-47f4-9660-201210efaffa	2026-07-01 18:13:26.694246+00
5c91e5a0-d4e0-484f-851c-96f010969acf	e592612f-44a8-4c00-9710-5499bb39f25c	watch_reels	1.00	35dfb355-99e0-479b-b824-608c52f4e11e	2026-07-01 18:18:23.399957+00
ea817acf-79e0-44a5-93ba-ae50777658d5	e592612f-44a8-4c00-9710-5499bb39f25c	watch_reels	1.00	2ebe21d3-c6c9-4d91-b6ab-a3f43a57dd62	2026-07-01 18:18:36.468957+00
e2378c49-20b1-4aea-b47f-201128f9f794	e592612f-44a8-4c00-9710-5499bb39f25c	create_product	10.00	e8c5d0e5-8f88-4d4b-b6b9-ec886832674a	2026-07-01 18:22:06.27673+00
c176551f-f3f6-46bd-bd73-ed4eeffdd360	5ee97315-2807-4042-a155-fe8714193a23	watch_reels	1.00	984adebb-0b82-4496-ad95-a51448c760d1	2026-07-02 19:22:00.921939+00
9ab3bd71-b59a-4542-ae4d-523d7283995f	5ee97315-2807-4042-a155-fe8714193a23	watch_reels	1.00	e9325c09-5243-49b4-9bec-fb5c4a72cc1d	2026-07-02 19:36:43.887622+00
d1ae2c42-93c0-4358-b3fb-e1d65206a91a	5ee97315-2807-4042-a155-fe8714193a23	watch_reels	1.00	dfc33ae6-35a4-446e-ad64-442dae6fd9ab	2026-07-04 18:12:38.694707+00
\.


--
-- TOC entry 4314 (class 0 OID 34933)
-- Dependencies: 255
-- Data for Name: product_images; Type: TABLE DATA; Schema: public; Owner: admin
--

COPY public.product_images (id, product_id, object_key, sort_order, created_at) FROM stdin;
e1fee59e-e3b8-4a07-bd05-8430eb3bf006	88959f5f-ee94-47b0-830a-106e94b7aec9	uploads/56cdaf2f-d26c-4931-b4e7-5c3d99817a13_myLogo.png.jpg	1	2026-06-23 15:26:10.444366+00
dc5b5942-2fb3-4272-8b08-4822b0424f94	88959f5f-ee94-47b0-830a-106e94b7aec9	uploads/6fa4a147-5eb9-4d3d-b25c-fb4c27db88da_Ekran-grnts-2026-06-22-203149.png	1	2026-06-23 15:26:10.444879+00
e6015cb5-cabe-4099-bca4-e122b28899a4	5ebe3845-e80d-4d79-bf5d-63947f802908	uploads/8e4135bb-8528-4d68-b60b-584ab830f367_3d-space-wallpaper-preview.jpg	1	2026-06-23 15:38:05.331655+00
80d7f25b-47f5-4abd-b306-f0ad03842ffc	5ef46d22-8943-4960-87c5-2a160bd06740	uploads/85a5d626-db15-46bf-a5a6-0c3419dbeaeb_WhatsApp-Image-2026-05-26-at-16.32.21.jpeg	1	2026-06-23 17:19:44.096644+00
fed07c1f-a85e-40f5-a2c2-e634f43af3f6	5ef46d22-8943-4960-87c5-2a160bd06740	uploads/71bb6308-1d27-47c8-95fa-46667cbbf823_Screenshot_20260514_211356_Instagram.jpg	1	2026-06-23 17:19:44.096659+00
f1d71b0b-79dc-4389-987a-e15e092f744f	f78fde16-311a-43da-af6f-1a149569f4c2	uploads/e760807d-6809-44a7-b207-342b20267dd4_indir.jpg	1	2026-06-23 18:58:46.66467+00
89ff6aae-2ab4-4e06-96f4-b61643499981	081500d3-c9a9-49e5-b5d7-ec363b144368	uploads/e5e23d18-db55-4df6-ab49-e696d1bad911_kodlamaa.jpg	1	2026-06-23 19:01:50.79757+00
4de532b9-faed-4c50-bde9-a74462fb9aa9	c2fa330d-ec90-450e-b051-059636ac3ed7	uploads/cf76c1a8-9d8f-4454-9b37-9899bf9752bd_masa.jpg	1	2026-06-23 19:21:40.28963+00
77b89d08-0245-4712-8780-91d40c5334e5	3ee6ec9f-3490-41b0-9b59-c18404086f90	uploads/9e831fff-178a-4e79-86dc-9cea0fbb4633_51c01f3d91a44d481f619a65b0ce63c8.jpg	1	2026-06-23 19:24:48.325658+00
1359633d-8bae-41ff-b4d7-a2670e906270	07614073-8751-469e-ad10-dcccd6f5c5d0	uploads/040971cc-1935-4906-820c-dd3c316b54f2_farkli.avif	1	2026-06-23 19:32:08.42785+00
2c52ab86-48b1-4f75-a05d-0bf91499685a	86093178-173e-4c26-8bd8-6f270cef116d	uploads/51e70686-8632-4cd1-a85e-8565cd94ea03_63481e8c2ba072245f7f3d11_3.png	1	2026-06-23 19:34:35.725057+00
27649a4a-3fa7-456e-8f50-dd99b40558a6	4496e543-1afe-40e4-bd6b-910300bbd320	uploads/9ad6fb9d-5ada-4079-b44c-58c251e8c60c_63481e8c2ba072245f7f3d11_3.png	1	2026-06-24 03:25:36.973574+00
4ac8a41e-f413-4071-820f-e3b719ea129d	4496e543-1afe-40e4-bd6b-910300bbd320	uploads/1afe2171-33a1-4335-a744-85ec68ad63b4_indir.jpg	1	2026-06-24 03:25:36.977612+00
189816b9-98e4-4454-804d-c20d13374d70	bd0ec36e-11b8-43ce-9303-6a536dd0caab	uploads/80c2240f-ea17-459c-a0cf-d05e24ee4483_360_F_1542323292_vpxI2BypeFHgSZUvuSRlJLACFn0yzp6d.jpg	1	2026-06-30 19:44:53.962011+00
54f279b5-b7ec-4690-8589-3a8d69aa9fed	bd0ec36e-11b8-43ce-9303-6a536dd0caab	uploads/e383feff-21d1-4ea0-843e-2c5054c4a7ca_360_F_321783813_D2uHZH7KyK47nkxbLoUrLAFf1gdDSFPW.jpg	1	2026-06-30 19:44:53.953403+00
57c97c74-d663-4103-bb93-efb78ce777ce	bd0ec36e-11b8-43ce-9303-6a536dd0caab	uploads/0f8dd0db-08b6-4f71-9fee-2901286c579c_10_Great_Sites_Built_with_Laravel_Framework_0e893c2354.webp	1	2026-06-30 19:44:53.953395+00
8cfa50f9-a458-49b7-ae0e-3ff3ca3c4e1b	974e0e3f-e909-47f4-9660-201210efaffa	uploads/ec9040cf-c45c-4794-b0fd-41ac913f7072_desktop-wallpaper-nature-high-quality.jpg	1	2026-07-01 18:13:27.91662+00
b43513c0-6b14-49d5-9fe9-4a32bac39e09	974e0e3f-e909-47f4-9660-201210efaffa	uploads/c3927b5c-0ecf-4e75-a0fa-52ebe1faa3c2_desktop-wallpaper-black-lamborghini-matte-black-lamborghini-1.jpg	1	2026-07-01 18:13:27.916598+00
db7bd285-a5ca-43cd-a55c-7a2daaf55219	e8c5d0e5-8f88-4d4b-b6b9-ec886832674a	uploads/1cf91c8f-e644-45ed-a773-45440857bb2b_360_F_755314809_ev2oIO9OYNPpX0vUwQZu7J7h5oWEKwGI.jpg	1	2026-07-01 18:22:07.601239+00
99f04906-5219-4c9b-95b9-43b404f9b242	e8c5d0e5-8f88-4d4b-b6b9-ec886832674a	uploads/e6e7c1fa-784a-4bf1-b724-770e96328efc_7cM6SF.jpg	2	2026-07-01 18:22:07.647771+00
\.


--
-- TOC entry 4297 (class 0 OID 17001)
-- Dependencies: 238
-- Data for Name: product_qa; Type: TABLE DATA; Schema: public; Owner: admin
--

COPY public.product_qa (id, product_id, user_id, parent_id, message, created_at) FROM stdin;
\.


--
-- TOC entry 4288 (class 0 OID 16807)
-- Dependencies: 229
-- Data for Name: products; Type: TABLE DATA; Schema: public; Owner: admin
--

COPY public.products (id, shop_id, category_id, type, title, description, metadata, price, original_price, currency, cover_image_url, preview_video_url, file_url, rating_average, review_count, sales_count, is_active, is_featured, status, tags, created_at, updated_at, discount_price, discount_ends_at) FROM stdin;
f78fde16-311a-43da-af6f-1a149569f4c2	9c3432a1-66b8-4856-bce0-9ef44654b69f	1bcc9c55-9cbf-45f8-aa1e-45e3bb310a49	digital_file	Reels Algoritması	Sıfırdan Zirveye Reels Algoritması Masterclass\n\nSıfırdan Zirveye Reels Algoritması Masterclass	{"currency": "TRY"}	120.00	150.00	USD	uploads/e760807d-6809-44a7-b207-342b20267dd4_indir.jpg	uploads/5f5cc059-27ae-444b-94bb-3adaff907f69_A_dynamic_and_fast-paced_Instagram_Reels_video._A_3D_purple_C__logo_violently_bursts_out_from_a_digi_seed4199154912-1.mp	courses_or_products/c0f6b4df-f88b-44a2-967b-8c112a3520d2_project.json	0.00	0	0	t	f	Published	{preset}	2026-06-23 18:58:46.312454+00	2026-06-23 18:58:46.312454+00	\N	\N
88959f5f-ee94-47b0-830a-106e94b7aec9	9c3432a1-66b8-4856-bce0-9ef44654b69f	926f634d-b3c5-41d1-9217-de73c23bf1ef	digital_file	yazilim ve tasarim plani	harika tasarimlar var\n\nharika rtasaifjh sdoifhsdk flsdf	{"currency": "TRY"}	1299.00	1799.00	USD	uploads/56cdaf2f-d26c-4931-b4e7-5c3d99817a13_myLogo.png.jpg	uploads/7d75e811-1053-4a27-8b3d-e08fef3d80f4_Vertical_9_16_aspect_ratio._The_purple_C__logo_on_the_book_cover_transforms_into_a_glowing_energy_co_seed3808780903.mp4	courses_or_products/f0db6728-74a1-4422-ab18-ddac237bdbd4_merhab.7z	0.00	0	1	f	f	Published	{"sidra ya sidra sidray  ya sidraaaa"}	2026-06-23 15:26:09.856527+00	2026-06-23 15:26:09.856527+00	\N	\N
081500d3-c9a9-49e5-b5d7-ec363b144368	9c3432a1-66b8-4856-bce0-9ef44654b69f	282e01ad-6500-44af-9a2f-e99831464e7a	digital_file	kurs	Freelancer'lar İçin Müşteri Bulma Sanatı: Mini Kurs\n\nFreelancer'lar İçin Müşteri Bulma Sanatı: Mini Kurs	{"currency": "TRY"}	239.99	400.00	USD	uploads/e5e23d18-db55-4df6-ab49-e696d1bad911_kodlamaa.jpg	uploads/6d4cfa09-a3bd-47d6-bfc5-e8c5725c29ae_Vertical_9_16_aspect_ratio._The_purple_C__logo_on_the_book_cover_transforms_into_a_glowing_energy_co_seed934031659.mp4	courses_or_products/8e880f35-3d05-4fd9-92d5-2079637b56bd_admin.html	0.00	0	0	t	f	Published	{creator}	2026-06-23 19:01:50.05159+00	2026-06-23 19:01:50.05159+00	\N	\N
5ebe3845-e80d-4d79-bf5d-63947f802908	2fc28588-622f-40e5-8ee6-79ed0613c8fa	686d2433-b518-4c18-94b3-a1ab155f4a20	digital_file	uiyt7utfty	jiyhj\n\njfgvrtoikyhjkjlogty	{"currency": "TRY"}	67456.00	78567.00	USD	uploads/8e4135bb-8528-4d68-b60b-584ab830f367_3d-space-wallpaper-preview.jpg	uploads/73d98901-ddb2-403e-9750-658973412437_Kayt-2025-11-25-215123.mp4	courses_or_products/85797570-693a-4e84-b7a2-a30c3fcd5199_6245ae1c-b7dc-42a5-82e6-c65e4e7702f6_Untitled.pdf	0.00	0	2	f	f	Published	{"njmkgh hjg jkh ghgj hg f"}	2026-06-23 15:38:04.98274+00	2026-06-23 15:38:04.98274+00	\N	\N
3ee6ec9f-3490-41b0-9b59-c18404086f90	2fc28588-622f-40e5-8ee6-79ed0613c8fa	686d2433-b518-4c18-94b3-a1ab155f4a20	digital_file	pixel&plan	Sepette ekstra sürprizler de var.\n\nSepette ekstra sürprizler de var.	{"currency": "TRY"}	600.00	799.00	USD	uploads/9e831fff-178a-4e79-86dc-9cea0fbb4633_51c01f3d91a44d481f619a65b0ce63c8.jpg	\N	\N	0.00	0	0	t	f	Published	{}	2026-06-23 19:24:48.139873+00	2026-06-23 19:24:48.139873+00	\N	\N
5ef46d22-8943-4960-87c5-2a160bd06740	f9160c3b-3bd7-49ad-8ac8-f98c8db5d48d	6854ccd8-7726-4737-8e7f-7981d052df58	digital_file	sdf asdf sdf sdf	sdaf sdf sdf sd\n\nf sdf sdf sdf sdf	{"currency": "TRY"}	1243.00	23455.00	USD	uploads/85a5d626-db15-46bf-a5a6-0c3419dbeaeb_WhatsApp-Image-2026-05-26-at-16.32.21.jpeg	uploads/90e4b797-f963-4c10-a5b2-2fd65f5eff7d_Vertical_9_16_aspect_ratio_cinematic_video_for_Reels._A_sleek_C__programming_PDF_book_cover_is_displ_seed4278527437.mp4	courses_or_products/409c4528-1952-410d-bf46-3b3343f93d09_6245ae1c-b7dc-42a5-82e6-c65e4e7702f6_Untitled.pdf	0.00	0	1	f	f	Published	{"asd asd as dasd"}	2026-06-23 17:19:43.499623+00	2026-06-23 17:19:43.499623+00	\N	\N
4496e543-1afe-40e4-bd6b-910300bbd320	9c3432a1-66b8-4856-bce0-9ef44654b69f	686d2433-b518-4c18-94b3-a1ab155f4a20	digital_file	test urun	kısa test\n\nuzun test	{"currency": "TRY"}	2183.00	2345.00	USD	uploads/9ad6fb9d-5ada-4079-b44c-58c251e8c60c_63481e8c2ba072245f7f3d11_3.png	uploads/13983af8-38d3-4fa9-999e-fbaa0eefe293_Vertical_9_16_aspect_ratio_cinematic_video_for_Reels._A_sleek_C__programming_PDF_book_cover_is_displ_seed4278527437.mp4	courses_or_products/e0602e68-8b2b-440d-abae-20a2df5c6084_merhab.7z	0.00	0	0	t	f	Published	{"sdfg sdfsd"}	2026-06-24 03:25:36.678271+00	2026-06-24 03:25:36.678271+00	\N	\N
c2fa330d-ec90-450e-b051-059636ac3ed7	2fc28588-622f-40e5-8ee6-79ed0613c8fa	926f634d-b3c5-41d1-9217-de73c23bf1ef	digital_file	aura jewel	Hemen incelemek ve sepete eklemek için videonun üzerindeki ürün etiketine tıklayabilirsin.\n\nHemen incelemek ve sepete eklemek için videonun üzerindeki ürün etiketine tıklayabilirsin.	{"currency": "TRY"}	799.00	1000.00	USD	uploads/cf76c1a8-9d8f-4454-9b37-9899bf9752bd_masa.jpg	uploads/c3055c34-0acd-4d90-89ae-d43b26edb336_Vertical_9_16_aspect_ratio_cinematic_video_for_Reels._A_sleek_C__programming_PDF_book_cover_is_displ_seed4278527437.mp4	courses_or_products/c9cf0751-3cb3-4994-8c82-1e9a49051f14_admin.html	0.00	0	1	t	f	Published	{preset}	2026-06-23 19:21:39.853779+00	2026-06-23 19:21:39.853779+00	\N	\N
423bf130-b4e5-4948-9d2f-c4d7b5a196ff	2fc28588-622f-40e5-8ee6-79ed0613c8fa	686d2433-b518-4c18-94b3-a1ab155f4a20	course	kaka	sdfk hsadjkf hsdkljf hdskljf hjskdl shdkl asdhkl sd	{"currency": "TRY"}	1299.00	2999.00	USD	uploads/3f11a187-6895-415b-99dc-8e8821d1ecf5_51c01f3d91a44d481f619a65b0ce63c8.jpg	uploads/8bc765c6-aad4-4fb5-8e2f-358588585c47_Kayt-2025-11-25-215123.mp4	\N	0.00	0	3	t	f	Published	{"dfvsdfs gfdg dfg df"}	2026-06-24 16:05:48.65435+00	2026-06-24 16:05:48.65435+00	\N	\N
e1abe8c0-5561-4f55-bd52-de6b105b76fe	2fc28588-622f-40e5-8ee6-79ed0613c8fa	926f634d-b3c5-41d1-9217-de73c23bf1ef	course	react Query kursu	merhaba	{"currency": "TRY"}	1299.00	2299.00	USD	uploads/039ee009-646a-4ab7-8545-457ae51d34b4_7cM6SF.jpg	uploads/9c1f1b03-ee7f-485b-bc62-9d9fe732eb85_Kayt-2025-11-25-215123.mp4	\N	0.00	0	2	t	f	Published	{"gfsdf gfdgdf df  dfgd fg"}	2026-06-24 15:59:48.393965+00	2026-06-24 15:59:48.394002+00	\N	\N
bd0ec36e-11b8-43ce-9303-6a536dd0caab	9c3432a1-66b8-4856-bce0-9ef44654b69f	926f634d-b3c5-41d1-9217-de73c23bf1ef	digital_file	merhaba	asd asdasdasdasd\n\nasda sd df sgdf gsdf  sgjghj ghj ghjgh	{"currency": "TRY"}	1287.00	4356.00	USD	uploads/0f8dd0db-08b6-4f71-9fee-2901286c579c_10_Great_Sites_Built_with_Laravel_Framework_0e893c2354.webp	uploads/32c990cd-0ab5-4d30-af15-c2b288bbb9e1_Kayt-2025-11-25-215123.mp4	courses_or_products/b635373f-0e40-42f9-ac02-1a28573219e3_6245ae1c-b7dc-42a5-82e6-c65e4e7702f6_Untitled.pdf	0.00	0	0	t	f	Published	{"sgdfgfghfg h fg fgh gfh"}	2026-06-30 19:44:51.919031+00	2026-06-30 19:44:51.919031+00	\N	\N
86093178-173e-4c26-8bd8-6f270cef116d	be43220d-cf62-4f0f-9eb2-39c4eb5a3be2	926f634d-b3c5-41d1-9217-de73c23bf1ef	digital_file	Growth Hub	Eğitim, masterclass ve e-kitap satanlar için\n\nEğitim, masterclass ve e-kitap satanlar için	{"currency": "USD"}	70.00	130.00	USD	uploads/51e70686-8632-4cd1-a85e-8565cd94ea03_63481e8c2ba072245f7f3d11_3.png	uploads/15ee0775-87d0-4f43-afab-887c0dfb4dd6_Vertical_9_16_aspect_ratio._The_purple_C__logo_on_the_book_cover_transforms_into_a_glowing_energy_co_seed934031659.mp4	courses_or_products/79f513fe-1ecf-4d3d-9081-dd2c26d2987c_admin.html	0.00	0	1	t	f	Published	{}	2026-06-23 19:34:35.548565+00	2026-06-23 19:34:35.548565+00	\N	\N
974e0e3f-e909-47f4-9660-201210efaffa	9c3432a1-66b8-4856-bce0-9ef44654b69f	282e01ad-6500-44af-9a2f-e99831464e7a	digital_file	yarrak	muhendıs\n\nuzun muhendıs	{"currency": "TRY"}	2345.00	5456.00	USD	uploads/ec9040cf-c45c-4794-b0fd-41ac913f7072_desktop-wallpaper-nature-high-quality.jpg	uploads/58242b19-90b4-46d0-a863-c5f34437473c_Kayt-2025-11-25-215123.mp4	courses_or_products/fa5b60ad-55c5-4dce-be75-1ad1b902fa8e_6245ae1c-b7dc-42a5-82e6-c65e4e7702f6_Untitled.pdf	0.00	0	0	t	f	Published	{klhşklşklş}	2026-07-01 18:13:26.639651+00	2026-07-01 18:13:26.639651+00	\N	\N
e8c5d0e5-8f88-4d4b-b6b9-ec886832674a	9c3432a1-66b8-4856-bce0-9ef44654b69f	686d2433-b518-4c18-94b3-a1ab155f4a20	digital_file	son bir urun	merhaba\n\nmerhab urun	{"currency": "TRY"}	534.00	34536.00	USD	uploads/e6e7c1fa-784a-4bf1-b724-770e96328efc_7cM6SF.jpg	uploads/da109895-86b5-40c3-bf41-62b781ffef6b_Kayt-2025-11-25-215123.mp4	courses_or_products/b35eec1c-f89d-43d8-b203-4f964464b45e_6245ae1c-b7dc-42a5-82e6-c65e4e7702f6_Untitled.pdf	0.00	0	0	t	f	Published	{"ghj gh fgh jgh"}	2026-07-01 18:22:06.17197+00	2026-07-01 18:22:06.17197+00	\N	\N
07614073-8751-469e-ad10-dcccd6f5c5d0	be43220d-cf62-4f0f-9eb2-39c4eb5a3be2	686d2433-b518-4c18-94b3-a1ab155f4a20	digital_file	arka Kimliği (Brand Board) Tasarım Şablonu (Figma/PSD)	Videodaki etikete tıkla, Uygulamadan çıkmadan al\n\narka Kimliği (Brand Board) Tasarım Şablonu (Figma/PSD)	{"currency": "USD"}	45.00	80.00	USD	uploads/040971cc-1935-4906-820c-dd3c316b54f2_farkli.avif	uploads/93cd6223-f8c1-4ada-bf11-f83d08dcbc9a_Vertical_9_16_aspect_ratio._The_purple_C__logo_on_the_book_cover_transforms_into_a_glowing_energy_co_seed3808780903.mp4	courses_or_products/a56a2242-356a-4624-b616-2c0707e0c290_admin.html	0.00	0	1	t	f	Published	{}	2026-06-23 19:32:08.172292+00	2026-06-23 19:32:08.172292+00	\N	\N
\.


--
-- TOC entry 4319 (class 0 OID 64263)
-- Dependencies: 260
-- Data for Name: pulse_news; Type: TABLE DATA; Schema: public; Owner: admin
--

COPY public.pulse_news (id, title, description, meta, icon, is_published, is_new_until, created_at, updated_at) FROM stdin;
\.


--
-- TOC entry 4298 (class 0 OID 17025)
-- Dependencies: 239
-- Data for Name: reviews; Type: TABLE DATA; Schema: public; Owner: admin
--

COPY public.reviews (id, product_id, user_id, rating, comment, seller_reply, created_at, updated_at, images) FROM stdin;
0621fbc7-f566-4967-ade5-8b838284532f	07614073-8751-469e-ad10-dcccd6f5c5d0	df19ab97-ed7e-48ef-979d-b14e0d9d1641	5	Harika bir ürün, çok beğendim!	\N	2026-07-04 17:29:53.257989+00	2026-07-04 17:29:53.257989+00	["https://storage.craftora.com/reviews/foto1.jpg", "https://storage.craftora.com/reviews/foto2.jpg"]
\.


--
-- TOC entry 4289 (class 0 OID 16836)
-- Dependencies: 230
-- Data for Name: seller_subscriptions; Type: TABLE DATA; Schema: public; Owner: admin
--

COPY public.seller_subscriptions (id, shop_id, provider_subscription_id, status, current_period_end, grace_period_end, amount, currency, created_at, updated_at, payment_provider, reminder_sent_at) FROM stdin;
a49bfc7f-6b11-407c-992f-8799334b6831	9c3432a1-66b8-4856-bce0-9ef44654b69f	sub_mock_73c1ba4b7e004d71b5b01a0776a16663	active	2026-07-23 15:23:43.733457+00	\N	25.00	USD	2026-06-23 15:23:43.652456+00	2026-06-23 15:23:43.733758+00	stripe_mock	\N
5212beb9-7c0e-4884-b388-8aeca0a9c446	2fc28588-622f-40e5-8ee6-79ed0613c8fa	sub_mock_e7050365bd944b17a52ac80a0b4bb485	active	2026-07-23 15:34:42.95148+00	\N	25.00	USD	2026-06-23 15:34:42.888973+00	2026-06-23 15:34:42.951484+00	stripe_mock	\N
ef0f3e45-4faf-4de9-b6e4-60e92017e7c5	be43220d-cf62-4f0f-9eb2-39c4eb5a3be2	sub_mock_fbf859247c424bfa98b60162968d31c5	active	2026-07-23 15:43:16.107829+00	\N	25.00	USD	2026-06-23 15:43:16.107357+00	2026-06-23 15:43:16.107832+00	stripe_mock	\N
1485f5f6-af86-4685-9eaa-deb393115c8c	f9160c3b-3bd7-49ad-8ac8-f98c8db5d48d	sub_mock_7ecc8560548b4dc18ef5c7259ce1d89f	active	2026-07-23 17:18:05.644821+00	\N	25.00	USD	2026-06-23 17:18:05.432223+00	2026-06-23 17:18:05.645074+00	stripe_mock	\N
\.


--
-- TOC entry 4290 (class 0 OID 16853)
-- Dependencies: 231
-- Data for Name: shop_visits; Type: TABLE DATA; Schema: public; Owner: admin
--

COPY public.shop_visits (id, shop_id, user_id, ip_address, visited_at) FROM stdin;
\.


--
-- TOC entry 4282 (class 0 OID 16709)
-- Dependencies: 223
-- Data for Name: shops; Type: TABLE DATA; Schema: public; Owner: admin
--

COPY public.shops (id, user_id, shop_name, slug, external_url, short_description, description, about_content, social_links, logo_url, banner_url, follower_count, rating, is_verified, is_active, created_at, updated_at) FROM stdin;
f9160c3b-3bd7-49ad-8ac8-f98c8db5d48d	df19ab97-ed7e-48ef-979d-b14e0d9d1641	orivon x	orivon-x			sd fsd fsd	\N	{"tiktok": null, "twitter": null, "website": null, "youtube": null, "instagram": null}	\N	\N	0	0.00	f	t	2026-06-23 17:18:03.133831+00	2026-07-04 17:05:50.987188+00
2fc28588-622f-40e5-8ee6-79ed0613c8fa	5ee97315-2807-4042-a155-fe8714193a23	preset lab	preset-lab			sdhag fjksdg fjkhsgd fjhsdg f	\N	{"tiktok": null, "twitter": null, "website": null, "youtube": null, "instagram": null}	uploads/a3a77a64-e1b5-4239-89f1-cd08ead5cbf4_1500700-3840x2160-desktop-4k-shooter-game-wallpaper-photo.jpg	uploads/12d8df23-7e82-47c6-90ae-eda670a74ffe_51c01f3d91a44d481f619a65b0ce63c8.jpg	0	0.00	f	t	2026-06-23 15:34:37.288866+00	2026-06-24 15:37:35.542054+00
9c3432a1-66b8-4856-bce0-9ef44654b69f	e592612f-44a8-4c00-9710-5499bb39f25c	ScriptHouse	scripthouse			uzun merhabaa sdas das das dasdas das	\N	{"tiktok": null, "twitter": null, "website": null, "youtube": null, "instagram": null}	uploads/0b741a42-ca26-46b4-a529-245bebed74bc_10_Great_Sites_Built_with_Laravel_Framework_0e893c2354.webp	uploads/92b83445-729a-4f0d-bca8-e05f81542984_masa.jpg	4	0.00	f	t	2026-06-23 15:23:36.455755+00	2026-06-24 15:40:23.12704+00
be43220d-cf62-4f0f-9eb2-39c4eb5a3be2	e7acf4c1-1f6b-4346-b2aa-95956bec3dd7	craftora studio	craftora-studio		digital urunler	sundugun ururnleri ve hedef kitleni anlat	\N	{"tiktok": null, "twitter": null, "website": null, "youtube": null, "instagram": null}	\N	\N	4	0.00	f	t	2026-06-23 15:43:07.622422+00	2026-06-24 15:40:30.324966+00
\.


--
-- TOC entry 4291 (class 0 OID 16872)
-- Dependencies: 232
-- Data for Name: subscriptions; Type: TABLE DATA; Schema: public; Owner: admin
--

COPY public.subscriptions (id, shop_id, user_id, wants_notifications, created_at) FROM stdin;
086343e2-0f80-42ea-83ad-cc58c1a1a66d	9c3432a1-66b8-4856-bce0-9ef44654b69f	df19ab97-ed7e-48ef-979d-b14e0d9d1641	t	2026-06-23 17:31:34.009247+00
73300b43-411f-49d9-825b-69671b103abf	be43220d-cf62-4f0f-9eb2-39c4eb5a3be2	e592612f-44a8-4c00-9710-5499bb39f25c	t	2026-06-24 03:23:41.702533+00
5ac7b350-44cb-4305-b05a-cf065538a491	9c3432a1-66b8-4856-bce0-9ef44654b69f	5ee97315-2807-4042-a155-fe8714193a23	t	2026-06-24 15:40:23.12704+00
ac6921e5-a75a-4bcf-abc9-53391033dad0	be43220d-cf62-4f0f-9eb2-39c4eb5a3be2	5ee97315-2807-4042-a155-fe8714193a23	t	2026-06-24 15:40:30.324966+00
\.


--
-- TOC entry 4283 (class 0 OID 16729)
-- Dependencies: 224
-- Data for Name: user_device_tokens; Type: TABLE DATA; Schema: public; Owner: admin
--

COPY public.user_device_tokens (id, user_id, token, device_type, device_id, is_active, last_used_at, created_at) FROM stdin;
\.


--
-- TOC entry 4311 (class 0 OID 17249)
-- Dependencies: 252
-- Data for Name: user_lesson_progress; Type: TABLE DATA; Schema: public; Owner: admin
--

COPY public.user_lesson_progress (id, user_id, course_lesson_id, is_completed, watched_seconds, created_at, updated_at) FROM stdin;
\.


--
-- TOC entry 4299 (class 0 OID 17045)
-- Dependencies: 240
-- Data for Name: user_library; Type: TABLE DATA; Schema: public; Owner: admin
--

COPY public.user_library (id, user_id, product_id, purchased_at, last_accessed_at) FROM stdin;
442c1995-7925-41f1-a92e-d35983fa3fe3	df19ab97-ed7e-48ef-979d-b14e0d9d1641	88959f5f-ee94-47b0-830a-106e94b7aec9	2026-06-23 17:31:42.800654+00	2026-06-23 17:32:21.673888+00
ea8942a1-a794-44c7-b609-69a85a646367	df19ab97-ed7e-48ef-979d-b14e0d9d1641	5ebe3845-e80d-4d79-bf5d-63947f802908	2026-06-23 17:44:00.163024+00	2026-06-23 17:44:00.163024+00
4ee2a425-78b0-4c50-87c5-d954c3def317	e592612f-44a8-4c00-9710-5499bb39f25c	5ef46d22-8943-4960-87c5-2a160bd06740	2026-06-23 17:46:14.302797+00	2026-06-23 17:46:14.302797+00
53ce7a23-0cd2-4451-867c-9fbe2067d141	e592612f-44a8-4c00-9710-5499bb39f25c	5ebe3845-e80d-4d79-bf5d-63947f802908	2026-06-23 17:54:16.709394+00	2026-06-23 17:54:16.709394+00
64d783f0-48e2-4dd3-9ba4-c1db3c15d438	e592612f-44a8-4c00-9710-5499bb39f25c	423bf130-b4e5-4948-9d2f-c4d7b5a196ff	2026-06-24 16:22:55.982329+00	2026-06-24 16:22:55.982329+00
eaf5c37a-6873-43af-831f-9b2a0a39b439	e7acf4c1-1f6b-4346-b2aa-95956bec3dd7	e1abe8c0-5561-4f55-bd52-de6b105b76fe	2026-06-24 16:57:02.463652+00	2026-06-24 17:02:09.173247+00
c0089b1f-4e17-4ed2-a7fc-f78e6bcb7a00	df19ab97-ed7e-48ef-979d-b14e0d9d1641	e1abe8c0-5561-4f55-bd52-de6b105b76fe	2026-06-28 18:18:15.389662+00	2026-06-28 18:18:15.389662+00
794d3dec-9121-4058-a7e0-ccd0216458d7	df19ab97-ed7e-48ef-979d-b14e0d9d1641	423bf130-b4e5-4948-9d2f-c4d7b5a196ff	2026-06-28 18:35:45.687084+00	2026-06-28 18:35:45.687084+00
8fbb61f2-1822-4607-bb5d-ba2fa13df345	e7acf4c1-1f6b-4346-b2aa-95956bec3dd7	423bf130-b4e5-4948-9d2f-c4d7b5a196ff	2026-06-28 19:06:04.370091+00	2026-06-28 19:06:04.370091+00
d81712ed-b455-4629-a83a-4e4651102f0a	e592612f-44a8-4c00-9710-5499bb39f25c	c2fa330d-ec90-450e-b051-059636ac3ed7	2026-06-30 19:38:57.881602+00	2026-06-30 19:38:57.881602+00
5bb34081-5f25-4045-aa03-7a60570f8a7c	e592612f-44a8-4c00-9710-5499bb39f25c	86093178-173e-4c26-8bd8-6f270cef116d	2026-07-01 18:11:25.624863+00	2026-07-01 18:11:25.624863+00
\.


--
-- TOC entry 4284 (class 0 OID 16745)
-- Dependencies: 225
-- Data for Name: user_points; Type: TABLE DATA; Schema: public; Owner: admin
--

COPY public.user_points (id, user_id, total_points, current_rank, current_streak, updated_at) FROM stdin;
58be3e2f-87df-4402-8b0f-ea5fe55e8671	5ee97315-2807-4042-a155-fe8714193a23	163.00	0	0	2026-07-04 18:12:39.239367+00
fbe92a06-5cae-4dce-b65e-b46f41896cfb	e592612f-44a8-4c00-9710-5499bb39f25c	50.50	0	0	2026-07-04 19:41:15.92716+00
cd849cd1-48c7-4f5c-8e75-da9e3c421b2b	df19ab97-ed7e-48ef-979d-b14e0d9d1641	21.00	0	0	2026-07-04 19:42:30.854072+00
6f4a30da-dfeb-4e7f-a434-2c2f5cb0a002	e7acf4c1-1f6b-4346-b2aa-95956bec3dd7	41.00	0	0	2026-07-05 16:56:25.951948+00
\.


--
-- TOC entry 4285 (class 0 OID 16760)
-- Dependencies: 226
-- Data for Name: user_sessions; Type: TABLE DATA; Schema: public; Owner: admin
--

COPY public.user_sessions (id, user_id, refresh_token, device_id, ip_address, user_agent, expires_at, created_at, is_revoked) FROM stdin;
03794a88-d96d-41f3-b1a3-10204291e9ff	e592612f-44a8-4c00-9710-5499bb39f25c	k4A2agyKX4v59+jdzArMXWmCG4yxd+uFt91NSVOyOac=	\N	\N	\N	2026-07-23 15:23:38.420651+00	2026-06-23 15:22:40.758019+00	t
45b3185c-6d24-4618-aca7-8d1d3073ee2e	e592612f-44a8-4c00-9710-5499bb39f25c	Xx97lvWxlUVfgMWBcV7VoHlHh0aMetyjhjen2PptGWg=	\N	\N	\N	2026-07-28 17:12:28.449067+00	2026-06-28 16:00:44.111748+00	f
ca01ad23-1028-43f2-aa6f-c37138e8f457	df19ab97-ed7e-48ef-979d-b14e0d9d1641	FHxSGNpAuKoSu6HMIWL+QZccNRJwMY71EnIH/RESATk=	\N	\N	\N	2026-07-28 19:03:00.92223+00	2026-06-28 18:14:56.005492+00	f
65a08f9e-0c9c-4995-b9ce-748a391d302c	e7acf4c1-1f6b-4346-b2aa-95956bec3dd7	Co/14m7OD35tuprfKqbj51bfu+KykVYtv8gvd/DXOPI=	\N	\N	\N	2026-07-28 19:20:37.122712+00	2026-06-28 19:05:08.151153+00	f
2a7ea401-f95c-4136-8824-41371b8f1dff	e7acf4c1-1f6b-4346-b2aa-95956bec3dd7	Bf9w8sUNuTeP73Jv20AQx8CAsOByE1jgv+/pr05ikEg=	\N	\N	\N	2026-07-29 17:39:09.353851+00	2026-06-29 17:39:11.573449+00	f
21263ae6-9583-4032-937c-e19ef9696aae	df19ab97-ed7e-48ef-979d-b14e0d9d1641	GJEjP9s/VFWSZvPswzWLxORETWP87ThG3ior5NAGUFg=	\N	\N	\N	2026-07-23 16:04:26.380087+00	2026-06-23 15:44:13.417867+00	t
cdcf77ba-7be9-4c11-ad54-16137862e4ca	df19ab97-ed7e-48ef-979d-b14e0d9d1641	Z4uTlaRdMZOGaytM97Qhp77QzkhgDDhn4xRozzIqC+c=	\N	\N	\N	2026-07-23 17:30:41.312416+00	2026-06-23 17:30:41.315796+00	f
0c3ebbdc-c4fd-4c58-88bf-7788cf8555ee	e592612f-44a8-4c00-9710-5499bb39f25c	bfj4nC+SHix9JDlDdLfMjolr60xIZK09iAf/qvEeZvM=	\N	\N	\N	2026-07-30 19:41:14.751632+00	2026-06-30 19:41:14.801327+00	f
4c09d749-593d-4d71-b801-493a97ad3726	e592612f-44a8-4c00-9710-5499bb39f25c	ZljepyTiNI26Wr7r4g1aG/dxupj6PZSK8JlVkqRN+mk=	\N	\N	\N	2026-07-31 18:40:51.495078+00	2026-07-01 18:10:21.998518+00	f
2450715d-15da-4677-a82f-eecb11abdb8e	df19ab97-ed7e-48ef-979d-b14e0d9d1641	1A8+Xir+nTwaMgay+wCsv9LdzhsakI17mpuC+7EcWBs=	\N	\N	\N	2026-07-24 03:19:39.304768+00	2026-06-23 20:23:09.130003+00	f
e5591b5c-c52f-425e-a464-300968f52682	e592612f-44a8-4c00-9710-5499bb39f25c	wTbzTCyEXLl04m6CcLoZ05w4jOui0UvP4YsB06RFxPU=	\N	\N	\N	2026-07-24 03:23:32.566227+00	2026-06-24 03:23:32.59778+00	f
a560afe0-f629-4bd2-8203-cf27b2682612	5ee97315-2807-4042-a155-fe8714193a23	xNeZTwj5GzpFHl/ovPdIrwXJElwwNw+7+T9jiD9J5iA=	\N	\N	\N	2026-07-24 07:55:44.257189+00	2026-06-24 07:55:45.056513+00	f
1bf697f4-e16b-4877-8a6d-8dd497635e24	5ee97315-2807-4042-a155-fe8714193a23	Ug36lC/DGOEPbZs9JiIBnFUu2wajB4I8fTOxyMAnwgU=	\N	\N	\N	2026-07-24 08:27:25.964461+00	2026-06-24 08:27:26.514661+00	f
1bdf2f7d-e800-499a-99e6-934049541574	5ee97315-2807-4042-a155-fe8714193a23	9+zNvUVqz3/WSQ2xuEQO9rQZF26JoFLcK9aylXDZ5as=	\N	\N	\N	2026-07-24 15:27:40.623422+00	2026-06-24 14:57:00.726167+00	f
cc238860-6979-412d-823c-22702b967536	5ee97315-2807-4042-a155-fe8714193a23	mWbWrZW7fD0hRHkE4hMZu8ddHFaxt1qXYt1tBUjXVBU=	\N	\N	\N	2026-07-24 16:18:32.676616+00	2026-06-24 15:47:19.915436+00	f
0ddcec23-215e-4ab0-8906-f9ecac781266	e592612f-44a8-4c00-9710-5499bb39f25c	K3DQZaATYC+CXPXIx+nQfxJgnO922e47f2qYbOCvCGQ=	\N	\N	\N	2026-07-24 16:22:30.96919+00	2026-06-24 16:22:30.984559+00	f
f3413907-177d-431a-97c4-88032ef4afb5	5ee97315-2807-4042-a155-fe8714193a23	mqqhz6xLEM5Aiv9f9ejR9fmg9A+b6k3r5M1dwCHsHlY=	\N	\N	\N	2026-08-03 18:08:10.982718+00	2026-07-02 19:21:58.740255+00	f
70eaa4db-7b66-4cd0-98cf-4344a5bd52d2	e7acf4c1-1f6b-4346-b2aa-95956bec3dd7	vP52k+MgWlYWsyQsqb8k22GUt05+DGiv37YIqLk4gGM=	\N	\N	\N	2026-07-28 15:59:26.478397+00	2026-06-24 16:24:19.078094+00	f
0daf40be-7818-49fd-822a-0c5092815086	5ee97315-2807-4042-a155-fe8714193a23	0RuXhW+mizE0hnhsKzeQA9z62n6TDWEmL3xHo4fQ7lk=	\N	\N	\N	2026-08-03 18:12:14.114779+00	2026-07-04 18:12:15.438845+00	f
3db2715b-f55d-4959-82f0-8b3da73ec7e9	5ee97315-2807-4042-a155-fe8714193a23	yIxaFltAyNCpIyUjWBptnjOy0zQG9eOp3zevbl4kdII=	\N	\N	\N	2026-08-03 18:12:14.035662+00	2026-07-04 18:12:15.438499+00	f
\.


--
-- TOC entry 4278 (class 0 OID 16653)
-- Dependencies: 219
-- Data for Name: users; Type: TABLE DATA; Schema: public; Owner: admin
--

COPY public.users (id, email, full_name, avatar_url, role, auth_provider, provider_id, password_hash, is_email_verified, locked_until, stripe_customer_id, stripe_account_id, preferences, is_active, last_login_at, created_at, updated_at, deleted_at) FROM stdin;
5ee97315-2807-4042-a155-fe8714193a23	developermd315@gmail.com	developer	\N	admin	google	110730149076244370759	\N	t	\N	\N	\N	{}	t	2026-07-04 18:12:14.114779+00	2026-06-23 15:34:06.868644+00	2026-07-04 18:12:15.438845+00	\N
e7acf4c1-1f6b-4346-b2aa-95956bec3dd7	mahmutdamlaki1@gmail.com	Mahmut Damlaki	\N	seller	google	114777209531157441049	\N	t	2026-07-11 18:14:16.11+00	\N	\N	{}	t	2026-06-29 17:39:09.353851+00	2026-06-23 15:41:07.025033+00	2026-07-04 18:14:16.281542+00	\N
df19ab97-ed7e-48ef-979d-b14e0d9d1641	damlamuhammet1@gmail.com	Muhammet Damla	\N	seller	google	106538515568077451337	\N	t	\N	\N	\N	{}	t	2026-06-28 18:14:55.987411+00	2026-06-23 15:44:13.30277+00	2026-06-28 18:14:56.005492+00	\N
e592612f-44a8-4c00-9710-5499bb39f25c	yesdamca@gmail.com	Damca Yes	\N	seller	google	113501122577283353530	\N	t	\N	\N	\N	{}	t	2026-07-01 18:10:19.736952+00	2026-06-23 15:22:40.481552+00	2026-07-01 18:10:21.998518+00	\N
95892358-56d1-4f9a-b705-5adc24ab5c09	test_trigger@craftora.com	Dogru Kullanici	\N	user	email	\N	hash123	f	\N	\N	\N	{}	t	\N	2026-07-03 19:38:53.439963+00	2026-07-03 19:44:09.759519+00	\N
\.


--
-- TOC entry 3920 (class 2606 OID 17350)
-- Name: __EFMigrationsHistory PK___EFMigrationsHistory; Type: CONSTRAINT; Schema: public; Owner: admin
--

ALTER TABLE ONLY public."__EFMigrationsHistory"
    ADD CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId");


--
-- TOC entry 3946 (class 2606 OID 64257)
-- Name: admin_audit_logs admin_audit_logs_pkey; Type: CONSTRAINT; Schema: public; Owner: admin
--

ALTER TABLE ONLY public.admin_audit_logs
    ADD CONSTRAINT admin_audit_logs_pkey PRIMARY KEY (id);


--
-- TOC entry 3954 (class 2606 OID 64292)
-- Name: admin_competition_rewards admin_competition_rewards_pkey; Type: CONSTRAINT; Schema: public; Owner: admin
--

ALTER TABLE ONLY public.admin_competition_rewards
    ADD CONSTRAINT admin_competition_rewards_pkey PRIMARY KEY (id);


--
-- TOC entry 3940 (class 2606 OID 64223)
-- Name: admin_reports admin_reports_pkey; Type: CONSTRAINT; Schema: public; Owner: admin
--

ALTER TABLE ONLY public.admin_reports
    ADD CONSTRAINT admin_reports_pkey PRIMARY KEY (id);


--
-- TOC entry 3943 (class 2606 OID 64237)
-- Name: admin_warnings admin_warnings_pkey; Type: CONSTRAINT; Schema: public; Owner: admin
--

ALTER TABLE ONLY public.admin_warnings
    ADD CONSTRAINT admin_warnings_pkey PRIMARY KEY (id);


--
-- TOC entry 3929 (class 2606 OID 54359)
-- Name: analytics_events analytics_events_pkey; Type: CONSTRAINT; Schema: public; Owner: admin
--

ALTER TABLE ONLY public.analytics_events
    ADD CONSTRAINT analytics_events_pkey PRIMARY KEY (id);


--
-- TOC entry 3829 (class 2606 OID 16898)
-- Name: cart_items cart_items_pkey; Type: CONSTRAINT; Schema: public; Owner: admin
--

ALTER TABLE ONLY public.cart_items
    ADD CONSTRAINT cart_items_pkey PRIMARY KEY (id);


--
-- TOC entry 3767 (class 2606 OID 16638)
-- Name: categories categories_pkey; Type: CONSTRAINT; Schema: public; Owner: admin
--

ALTER TABLE ONLY public.categories
    ADD CONSTRAINT categories_pkey PRIMARY KEY (id);


--
-- TOC entry 3804 (class 2606 OID 16781)
-- Name: contest_results contest_results_pkey; Type: CONSTRAINT; Schema: public; Owner: admin
--

ALTER TABLE ONLY public.contest_results
    ADD CONSTRAINT contest_results_pkey PRIMARY KEY (id);


--
-- TOC entry 3777 (class 2606 OID 16676)
-- Name: contests contests_pkey; Type: CONSTRAINT; Schema: public; Owner: admin
--

ALTER TABLE ONLY public.contests
    ADD CONSTRAINT contests_pkey PRIMARY KEY (id);


--
-- TOC entry 3893 (class 2606 OID 17154)
-- Name: coupon_uses coupon_uses_pkey; Type: CONSTRAINT; Schema: public; Owner: admin
--

ALTER TABLE ONLY public.coupon_uses
    ADD CONSTRAINT coupon_uses_pkey PRIMARY KEY (id);


--
-- TOC entry 3834 (class 2606 OID 16919)
-- Name: coupons coupons_pkey; Type: CONSTRAINT; Schema: public; Owner: admin
--

ALTER TABLE ONLY public.coupons
    ADD CONSTRAINT coupons_pkey PRIMARY KEY (id);


--
-- TOC entry 3901 (class 2606 OID 17197)
-- Name: course_lessons course_lessons_pkey; Type: CONSTRAINT; Schema: public; Owner: admin
--

ALTER TABLE ONLY public.course_lessons
    ADD CONSTRAINT course_lessons_pkey PRIMARY KEY (id);


--
-- TOC entry 3905 (class 2606 OID 17210)
-- Name: course_quizzes course_quizzes_pkey; Type: CONSTRAINT; Schema: public; Owner: admin
--

ALTER TABLE ONLY public.course_quizzes
    ADD CONSTRAINT course_quizzes_pkey PRIMARY KEY (id);


--
-- TOC entry 3869 (class 2606 OID 17070)
-- Name: course_sections course_sections_pkey; Type: CONSTRAINT; Schema: public; Owner: admin
--

ALTER TABLE ONLY public.course_sections
    ADD CONSTRAINT course_sections_pkey PRIMARY KEY (id);


--
-- TOC entry 3839 (class 2606 OID 16938)
-- Name: courses courses_pkey; Type: CONSTRAINT; Schema: public; Owner: admin
--

ALTER TABLE ONLY public.courses
    ADD CONSTRAINT courses_pkey PRIMARY KEY (id);


--
-- TOC entry 3952 (class 2606 OID 64283)
-- Name: home_cards home_cards_pkey; Type: CONSTRAINT; Schema: public; Owner: admin
--

ALTER TABLE ONLY public.home_cards
    ADD CONSTRAINT home_cards_pkey PRIMARY KEY (id);


--
-- TOC entry 3923 (class 2606 OID 26289)
-- Name: ip_login_attempts ip_login_attempts_pkey; Type: CONSTRAINT; Schema: public; Owner: admin
--

ALTER TABLE ONLY public.ip_login_attempts
    ADD CONSTRAINT ip_login_attempts_pkey PRIMARY KEY (ip_address);


--
-- TOC entry 3910 (class 2606 OID 17224)
-- Name: lesson_progress lesson_progress_pkey; Type: CONSTRAINT; Schema: public; Owner: admin
--

ALTER TABLE ONLY public.lesson_progress
    ADD CONSTRAINT lesson_progress_pkey PRIMARY KEY (id);


--
-- TOC entry 3914 (class 2606 OID 17243)
-- Name: lesson_resources lesson_resources_pkey; Type: CONSTRAINT; Schema: public; Owner: admin
--

ALTER TABLE ONLY public.lesson_resources
    ADD CONSTRAINT lesson_resources_pkey PRIMARY KEY (id);


--
-- TOC entry 3770 (class 2606 OID 16652)
-- Name: login_attempts login_attempts_pkey; Type: CONSTRAINT; Schema: public; Owner: admin
--

ALTER TABLE ONLY public.login_attempts
    ADD CONSTRAINT login_attempts_pkey PRIMARY KEY (email);


--
-- TOC entry 3876 (class 2606 OID 17085)
-- Name: media_comments media_comments_pkey; Type: CONSTRAINT; Schema: public; Owner: admin
--

ALTER TABLE ONLY public.media_comments
    ADD CONSTRAINT media_comments_pkey PRIMARY KEY (id);


--
-- TOC entry 3880 (class 2606 OID 17102)
-- Name: media_likes media_likes_pkey; Type: CONSTRAINT; Schema: public; Owner: admin
--

ALTER TABLE ONLY public.media_likes
    ADD CONSTRAINT media_likes_pkey PRIMARY KEY (id);


--
-- TOC entry 3844 (class 2606 OID 16961)
-- Name: media media_pkey; Type: CONSTRAINT; Schema: public; Owner: admin
--

ALTER TABLE ONLY public.media
    ADD CONSTRAINT media_pkey PRIMARY KEY (id);


--
-- TOC entry 3884 (class 2606 OID 17119)
-- Name: media_saves media_saves_pkey; Type: CONSTRAINT; Schema: public; Owner: admin
--

ALTER TABLE ONLY public.media_saves
    ADD CONSTRAINT media_saves_pkey PRIMARY KEY (id);


--
-- TOC entry 3887 (class 2606 OID 17137)
-- Name: media_watch_history media_watch_history_pkey; Type: CONSTRAINT; Schema: public; Owner: admin
--

ALTER TABLE ONLY public.media_watch_history
    ADD CONSTRAINT media_watch_history_pkey PRIMARY KEY (id);


--
-- TOC entry 3808 (class 2606 OID 16801)
-- Name: notification_deliveries notification_deliveries_pkey; Type: CONSTRAINT; Schema: public; Owner: admin
--

ALTER TABLE ONLY public.notification_deliveries
    ADD CONSTRAINT notification_deliveries_pkey PRIMARY KEY (id);


--
-- TOC entry 3781 (class 2606 OID 16691)
-- Name: notifications notifications_pkey; Type: CONSTRAINT; Schema: public; Owner: admin
--

ALTER TABLE ONLY public.notifications
    ADD CONSTRAINT notifications_pkey PRIMARY KEY (id);


--
-- TOC entry 3852 (class 2606 OID 16985)
-- Name: orders orders_pkey; Type: CONSTRAINT; Schema: public; Owner: admin
--

ALTER TABLE ONLY public.orders
    ADD CONSTRAINT orders_pkey PRIMARY KEY (id);


--
-- TOC entry 3898 (class 2606 OID 17180)
-- Name: payments payments_pkey; Type: CONSTRAINT; Schema: public; Owner: admin
--

ALTER TABLE ONLY public.payments
    ADD CONSTRAINT payments_pkey PRIMARY KEY (id);


--
-- TOC entry 3784 (class 2606 OID 16703)
-- Name: point_logs point_logs_pkey; Type: CONSTRAINT; Schema: public; Owner: admin
--

ALTER TABLE ONLY public.point_logs
    ADD CONSTRAINT point_logs_pkey PRIMARY KEY (id);


--
-- TOC entry 3926 (class 2606 OID 34941)
-- Name: product_images product_images_pkey; Type: CONSTRAINT; Schema: public; Owner: admin
--

ALTER TABLE ONLY public.product_images
    ADD CONSTRAINT product_images_pkey PRIMARY KEY (id);


--
-- TOC entry 3857 (class 2606 OID 17009)
-- Name: product_qa product_qa_pkey; Type: CONSTRAINT; Schema: public; Owner: admin
--

ALTER TABLE ONLY public.product_qa
    ADD CONSTRAINT product_qa_pkey PRIMARY KEY (id);


--
-- TOC entry 3812 (class 2606 OID 16825)
-- Name: products products_pkey; Type: CONSTRAINT; Schema: public; Owner: admin
--

ALTER TABLE ONLY public.products
    ADD CONSTRAINT products_pkey PRIMARY KEY (id);


--
-- TOC entry 3950 (class 2606 OID 64273)
-- Name: pulse_news pulse_news_pkey; Type: CONSTRAINT; Schema: public; Owner: admin
--

ALTER TABLE ONLY public.pulse_news
    ADD CONSTRAINT pulse_news_pkey PRIMARY KEY (id);


--
-- TOC entry 3860 (class 2606 OID 17034)
-- Name: reviews reviews_pkey; Type: CONSTRAINT; Schema: public; Owner: admin
--

ALTER TABLE ONLY public.reviews
    ADD CONSTRAINT reviews_pkey PRIMARY KEY (id);


--
-- TOC entry 3816 (class 2606 OID 16847)
-- Name: seller_subscriptions seller_subscriptions_pkey; Type: CONSTRAINT; Schema: public; Owner: admin
--

ALTER TABLE ONLY public.seller_subscriptions
    ADD CONSTRAINT seller_subscriptions_pkey PRIMARY KEY (id);


--
-- TOC entry 3822 (class 2606 OID 16861)
-- Name: shop_visits shop_visits_pkey; Type: CONSTRAINT; Schema: public; Owner: admin
--

ALTER TABLE ONLY public.shop_visits
    ADD CONSTRAINT shop_visits_pkey PRIMARY KEY (id);


--
-- TOC entry 3788 (class 2606 OID 16723)
-- Name: shops shops_pkey; Type: CONSTRAINT; Schema: public; Owner: admin
--

ALTER TABLE ONLY public.shops
    ADD CONSTRAINT shops_pkey PRIMARY KEY (id);


--
-- TOC entry 3825 (class 2606 OID 16879)
-- Name: subscriptions subscriptions_pkey; Type: CONSTRAINT; Schema: public; Owner: admin
--

ALTER TABLE ONLY public.subscriptions
    ADD CONSTRAINT subscriptions_pkey PRIMARY KEY (id);


--
-- TOC entry 3793 (class 2606 OID 16739)
-- Name: user_device_tokens user_device_tokens_pkey; Type: CONSTRAINT; Schema: public; Owner: admin
--

ALTER TABLE ONLY public.user_device_tokens
    ADD CONSTRAINT user_device_tokens_pkey PRIMARY KEY (id);


--
-- TOC entry 3917 (class 2606 OID 17257)
-- Name: user_lesson_progress user_lesson_progress_pkey; Type: CONSTRAINT; Schema: public; Owner: admin
--

ALTER TABLE ONLY public.user_lesson_progress
    ADD CONSTRAINT user_lesson_progress_pkey PRIMARY KEY (id);


--
-- TOC entry 3865 (class 2606 OID 17052)
-- Name: user_library user_library_pkey; Type: CONSTRAINT; Schema: public; Owner: admin
--

ALTER TABLE ONLY public.user_library
    ADD CONSTRAINT user_library_pkey PRIMARY KEY (id);


--
-- TOC entry 3796 (class 2606 OID 16754)
-- Name: user_points user_points_pkey; Type: CONSTRAINT; Schema: public; Owner: admin
--

ALTER TABLE ONLY public.user_points
    ADD CONSTRAINT user_points_pkey PRIMARY KEY (id);


--
-- TOC entry 3800 (class 2606 OID 16768)
-- Name: user_sessions user_sessions_pkey; Type: CONSTRAINT; Schema: public; Owner: admin
--

ALTER TABLE ONLY public.user_sessions
    ADD CONSTRAINT user_sessions_pkey PRIMARY KEY (id);


--
-- TOC entry 3774 (class 2606 OID 16667)
-- Name: users users_pkey; Type: CONSTRAINT; Schema: public; Owner: admin
--

ALTER TABLE ONLY public.users
    ADD CONSTRAINT users_pkey PRIMARY KEY (id);


--
-- TOC entry 3827 (class 1259 OID 17269)
-- Name: IX_cart_items_product_id; Type: INDEX; Schema: public; Owner: admin
--

CREATE INDEX "IX_cart_items_product_id" ON public.cart_items USING btree (product_id);


--
-- TOC entry 3765 (class 1259 OID 17272)
-- Name: IX_categories_parent_id; Type: INDEX; Schema: public; Owner: admin
--

CREATE INDEX "IX_categories_parent_id" ON public.categories USING btree (parent_id);


--
-- TOC entry 3801 (class 1259 OID 17274)
-- Name: IX_contest_results_user_id; Type: INDEX; Schema: public; Owner: admin
--

CREATE INDEX "IX_contest_results_user_id" ON public.contest_results USING btree (user_id);


--
-- TOC entry 3775 (class 1259 OID 17275)
-- Name: IX_contests_created_by; Type: INDEX; Schema: public; Owner: admin
--

CREATE INDEX "IX_contests_created_by" ON public.contests USING btree (created_by);


--
-- TOC entry 3889 (class 1259 OID 17277)
-- Name: IX_coupon_uses_order_id; Type: INDEX; Schema: public; Owner: admin
--

CREATE INDEX "IX_coupon_uses_order_id" ON public.coupon_uses USING btree (order_id);


--
-- TOC entry 3890 (class 1259 OID 17278)
-- Name: IX_coupon_uses_user_id; Type: INDEX; Schema: public; Owner: admin
--

CREATE INDEX "IX_coupon_uses_user_id" ON public.coupon_uses USING btree (user_id);


--
-- TOC entry 3832 (class 1259 OID 17279)
-- Name: IX_coupons_shop_id; Type: INDEX; Schema: public; Owner: admin
--

CREATE INDEX "IX_coupons_shop_id" ON public.coupons USING btree (shop_id);


--
-- TOC entry 3907 (class 1259 OID 17289)
-- Name: IX_lesson_progress_lesson_id; Type: INDEX; Schema: public; Owner: admin
--

CREATE INDEX "IX_lesson_progress_lesson_id" ON public.lesson_progress USING btree (lesson_id);


--
-- TOC entry 3871 (class 1259 OID 17295)
-- Name: IX_media_comments_media_id; Type: INDEX; Schema: public; Owner: admin
--

CREATE INDEX "IX_media_comments_media_id" ON public.media_comments USING btree (media_id);


--
-- TOC entry 3872 (class 1259 OID 17296)
-- Name: IX_media_comments_user_id; Type: INDEX; Schema: public; Owner: admin
--

CREATE INDEX "IX_media_comments_user_id" ON public.media_comments USING btree (user_id);


--
-- TOC entry 3877 (class 1259 OID 17297)
-- Name: IX_media_likes_user_id; Type: INDEX; Schema: public; Owner: admin
--

CREATE INDEX "IX_media_likes_user_id" ON public.media_likes USING btree (user_id);


--
-- TOC entry 3881 (class 1259 OID 17299)
-- Name: IX_media_saves_user_id; Type: INDEX; Schema: public; Owner: admin
--

CREATE INDEX "IX_media_saves_user_id" ON public.media_saves USING btree (user_id);


--
-- TOC entry 3885 (class 1259 OID 17301)
-- Name: IX_media_watch_history_media_id; Type: INDEX; Schema: public; Owner: admin
--

CREATE INDEX "IX_media_watch_history_media_id" ON public.media_watch_history USING btree (media_id);


--
-- TOC entry 3845 (class 1259 OID 17307)
-- Name: IX_orders_product_id; Type: INDEX; Schema: public; Owner: admin
--

CREATE INDEX "IX_orders_product_id" ON public.orders USING btree (product_id);


--
-- TOC entry 3853 (class 1259 OID 17318)
-- Name: IX_product_qa_parent_id; Type: INDEX; Schema: public; Owner: admin
--

CREATE INDEX "IX_product_qa_parent_id" ON public.product_qa USING btree (parent_id);


--
-- TOC entry 3854 (class 1259 OID 17319)
-- Name: IX_product_qa_product_id; Type: INDEX; Schema: public; Owner: admin
--

CREATE INDEX "IX_product_qa_product_id" ON public.product_qa USING btree (product_id);


--
-- TOC entry 3855 (class 1259 OID 17320)
-- Name: IX_product_qa_user_id; Type: INDEX; Schema: public; Owner: admin
--

CREATE INDEX "IX_product_qa_user_id" ON public.product_qa USING btree (user_id);


--
-- TOC entry 3809 (class 1259 OID 17321)
-- Name: IX_products_category_id; Type: INDEX; Schema: public; Owner: admin
--

CREATE INDEX "IX_products_category_id" ON public.products USING btree (category_id);


--
-- TOC entry 3858 (class 1259 OID 17323)
-- Name: IX_reviews_user_id; Type: INDEX; Schema: public; Owner: admin
--

CREATE INDEX "IX_reviews_user_id" ON public.reviews USING btree (user_id);


--
-- TOC entry 3819 (class 1259 OID 17327)
-- Name: IX_shop_visits_user_id; Type: INDEX; Schema: public; Owner: admin
--

CREATE INDEX "IX_shop_visits_user_id" ON public.shop_visits USING btree (user_id);


--
-- TOC entry 3823 (class 1259 OID 17333)
-- Name: IX_subscriptions_user_id; Type: INDEX; Schema: public; Owner: admin
--

CREATE INDEX "IX_subscriptions_user_id" ON public.subscriptions USING btree (user_id);


--
-- TOC entry 3915 (class 1259 OID 17337)
-- Name: IX_user_lesson_progress_course_lesson_id; Type: INDEX; Schema: public; Owner: admin
--

CREATE INDEX "IX_user_lesson_progress_course_lesson_id" ON public.user_lesson_progress USING btree (course_lesson_id);


--
-- TOC entry 3862 (class 1259 OID 17339)
-- Name: IX_user_library_product_id; Type: INDEX; Schema: public; Owner: admin
--

CREATE INDEX "IX_user_library_product_id" ON public.user_library USING btree (product_id);


--
-- TOC entry 3798 (class 1259 OID 17343)
-- Name: IX_user_sessions_user_id; Type: INDEX; Schema: public; Owner: admin
--

CREATE INDEX "IX_user_sessions_user_id" ON public.user_sessions USING btree (user_id);


--
-- TOC entry 3830 (class 1259 OID 17268)
-- Name: cart_items_user_id_product_id_key; Type: INDEX; Schema: public; Owner: admin
--

CREATE UNIQUE INDEX cart_items_user_id_product_id_key ON public.cart_items USING btree (user_id, product_id);


--
-- TOC entry 3768 (class 1259 OID 17271)
-- Name: categories_slug_key; Type: INDEX; Schema: public; Owner: admin
--

CREATE UNIQUE INDEX categories_slug_key ON public.categories USING btree (slug);


--
-- TOC entry 3802 (class 1259 OID 17273)
-- Name: contest_results_contest_id_user_id_key; Type: INDEX; Schema: public; Owner: admin
--

CREATE UNIQUE INDEX contest_results_contest_id_user_id_key ON public.contest_results USING btree (contest_id, user_id);


--
-- TOC entry 3891 (class 1259 OID 17276)
-- Name: coupon_uses_coupon_id_user_id_key; Type: INDEX; Schema: public; Owner: admin
--

CREATE UNIQUE INDEX coupon_uses_coupon_id_user_id_key ON public.coupon_uses USING btree (coupon_id, user_id);


--
-- TOC entry 3902 (class 1259 OID 17283)
-- Name: course_lessons_section_id_sort_order_key; Type: INDEX; Schema: public; Owner: admin
--

CREATE UNIQUE INDEX course_lessons_section_id_sort_order_key ON public.course_lessons USING btree (course_section_id, sort_order);


--
-- TOC entry 3867 (class 1259 OID 17286)
-- Name: course_sections_course_id_sort_order_key; Type: INDEX; Schema: public; Owner: admin
--

CREATE UNIQUE INDEX course_sections_course_id_sort_order_key ON public.course_sections USING btree (course_id, sort_order);


--
-- TOC entry 3947 (class 1259 OID 64305)
-- Name: idx_admin_audit_logs_created; Type: INDEX; Schema: public; Owner: admin
--

CREATE INDEX idx_admin_audit_logs_created ON public.admin_audit_logs USING btree (created_at DESC);


--
-- TOC entry 3941 (class 1259 OID 64303)
-- Name: idx_admin_reports_status_type; Type: INDEX; Schema: public; Owner: admin
--

CREATE INDEX idx_admin_reports_status_type ON public.admin_reports USING btree (status, type);


--
-- TOC entry 3944 (class 1259 OID 64304)
-- Name: idx_admin_warnings_user; Type: INDEX; Schema: public; Owner: admin
--

CREATE INDEX idx_admin_warnings_user ON public.admin_warnings USING btree (user_id, created_at DESC);


--
-- TOC entry 3930 (class 1259 OID 54388)
-- Name: idx_analytics_metadata; Type: INDEX; Schema: public; Owner: admin
--

CREATE INDEX idx_analytics_metadata ON public.analytics_events USING gin (metadata);


--
-- TOC entry 3931 (class 1259 OID 54383)
-- Name: idx_analytics_order; Type: INDEX; Schema: public; Owner: admin
--

CREATE INDEX idx_analytics_order ON public.analytics_events USING btree (order_id) WHERE (order_id IS NOT NULL);


--
-- TOC entry 3932 (class 1259 OID 54382)
-- Name: idx_analytics_product_event_date; Type: INDEX; Schema: public; Owner: admin
--

CREATE INDEX idx_analytics_product_event_date ON public.analytics_events USING btree (product_id, event_type, created_at DESC) WHERE (product_id IS NOT NULL);


--
-- TOC entry 3933 (class 1259 OID 54385)
-- Name: idx_analytics_session_date; Type: INDEX; Schema: public; Owner: admin
--

CREATE INDEX idx_analytics_session_date ON public.analytics_events USING btree (session_id, created_at DESC) WHERE (session_id IS NOT NULL);


--
-- TOC entry 3934 (class 1259 OID 54380)
-- Name: idx_analytics_shop_date; Type: INDEX; Schema: public; Owner: admin
--

CREATE INDEX idx_analytics_shop_date ON public.analytics_events USING btree (shop_id, created_at DESC);


--
-- TOC entry 3935 (class 1259 OID 54381)
-- Name: idx_analytics_shop_event_date; Type: INDEX; Schema: public; Owner: admin
--

CREATE INDEX idx_analytics_shop_event_date ON public.analytics_events USING btree (shop_id, event_type, created_at DESC);


--
-- TOC entry 3936 (class 1259 OID 54386)
-- Name: idx_analytics_shop_source_date; Type: INDEX; Schema: public; Owner: admin
--

CREATE INDEX idx_analytics_shop_source_date ON public.analytics_events USING btree (shop_id, source, created_at DESC) WHERE (source IS NOT NULL);


--
-- TOC entry 3937 (class 1259 OID 54387)
-- Name: idx_analytics_shop_utm_source_date; Type: INDEX; Schema: public; Owner: admin
--

CREATE INDEX idx_analytics_shop_utm_source_date ON public.analytics_events USING btree (shop_id, utm_source, created_at DESC) WHERE (utm_source IS NOT NULL);


--
-- TOC entry 3938 (class 1259 OID 54384)
-- Name: idx_analytics_user_date; Type: INDEX; Schema: public; Owner: admin
--

CREATE INDEX idx_analytics_user_date ON public.analytics_events USING btree (user_id, created_at DESC) WHERE (user_id IS NOT NULL);


--
-- TOC entry 3831 (class 1259 OID 17270)
-- Name: idx_cart_items_user; Type: INDEX; Schema: public; Owner: admin
--

CREATE INDEX idx_cart_items_user ON public.cart_items USING btree (user_id);


--
-- TOC entry 3835 (class 1259 OID 17280)
-- Name: idx_coupons_code; Type: INDEX; Schema: public; Owner: admin
--

CREATE INDEX idx_coupons_code ON public.coupons USING btree (code);


--
-- TOC entry 3836 (class 1259 OID 17281)
-- Name: idx_coupons_product; Type: INDEX; Schema: public; Owner: admin
--

CREATE INDEX idx_coupons_product ON public.coupons USING btree (product_id);


--
-- TOC entry 3903 (class 1259 OID 17284)
-- Name: idx_course_lessons_section; Type: INDEX; Schema: public; Owner: admin
--

CREATE INDEX idx_course_lessons_section ON public.course_lessons USING btree (course_section_id);


--
-- TOC entry 3906 (class 1259 OID 17285)
-- Name: idx_course_quizzes_section; Type: INDEX; Schema: public; Owner: admin
--

CREATE INDEX idx_course_quizzes_section ON public.course_quizzes USING btree (course_section_id);


--
-- TOC entry 3870 (class 1259 OID 17287)
-- Name: idx_course_sections_course; Type: INDEX; Schema: public; Owner: admin
--

CREATE INDEX idx_course_sections_course ON public.course_sections USING btree (course_id);


--
-- TOC entry 3840 (class 1259 OID 17288)
-- Name: idx_courses_product; Type: INDEX; Schema: public; Owner: admin
--

CREATE INDEX idx_courses_product ON public.courses USING btree (product_id);


--
-- TOC entry 3805 (class 1259 OID 17303)
-- Name: idx_deliveries_notification; Type: INDEX; Schema: public; Owner: admin
--

CREATE INDEX idx_deliveries_notification ON public.notification_deliveries USING btree (notification_id);


--
-- TOC entry 3806 (class 1259 OID 17304)
-- Name: idx_deliveries_pending; Type: INDEX; Schema: public; Owner: admin
--

CREATE INDEX idx_deliveries_pending ON public.notification_deliveries USING btree (status) WHERE ((status)::text = 'pending'::text);


--
-- TOC entry 3791 (class 1259 OID 17335)
-- Name: idx_device_tokens_user; Type: INDEX; Schema: public; Owner: admin
--

CREATE INDEX idx_device_tokens_user ON public.user_device_tokens USING btree (user_id) WHERE (is_active = true);


--
-- TOC entry 3921 (class 1259 OID 26290)
-- Name: idx_ip_attempts_locked_until; Type: INDEX; Schema: public; Owner: admin
--

CREATE INDEX idx_ip_attempts_locked_until ON public.ip_login_attempts USING btree (locked_until) WHERE (locked_until IS NOT NULL);


--
-- TOC entry 3908 (class 1259 OID 17290)
-- Name: idx_lesson_progress_user; Type: INDEX; Schema: public; Owner: admin
--

CREATE INDEX idx_lesson_progress_user ON public.lesson_progress USING btree (user_id, lesson_id);


--
-- TOC entry 3912 (class 1259 OID 17292)
-- Name: idx_lesson_resources_lesson; Type: INDEX; Schema: public; Owner: admin
--

CREATE INDEX idx_lesson_resources_lesson ON public.lesson_resources USING btree (course_lesson_id);


--
-- TOC entry 3873 (class 1259 OID 55004)
-- Name: idx_media_comments_media_parent_created; Type: INDEX; Schema: public; Owner: admin
--

CREATE INDEX idx_media_comments_media_parent_created ON public.media_comments USING btree (media_id, parent_comment_id, created_at);


--
-- TOC entry 3874 (class 1259 OID 55003)
-- Name: idx_media_comments_parent; Type: INDEX; Schema: public; Owner: admin
--

CREATE INDEX idx_media_comments_parent ON public.media_comments USING btree (parent_comment_id);


--
-- TOC entry 3841 (class 1259 OID 17293)
-- Name: idx_media_product; Type: INDEX; Schema: public; Owner: admin
--

CREATE INDEX idx_media_product ON public.media USING btree (product_id);


--
-- TOC entry 3842 (class 1259 OID 17294)
-- Name: idx_media_shop; Type: INDEX; Schema: public; Owner: admin
--

CREATE INDEX idx_media_shop ON public.media USING btree (shop_id);


--
-- TOC entry 3778 (class 1259 OID 17305)
-- Name: idx_notifications_unread; Type: INDEX; Schema: public; Owner: admin
--

CREATE INDEX idx_notifications_unread ON public.notifications USING btree (user_id, is_read) WHERE (is_read = false);


--
-- TOC entry 3779 (class 1259 OID 17306)
-- Name: idx_notifications_user; Type: INDEX; Schema: public; Owner: admin
--

CREATE INDEX idx_notifications_user ON public.notifications USING btree (user_id, created_at DESC);


--
-- TOC entry 3846 (class 1259 OID 17308)
-- Name: idx_orders_buyer; Type: INDEX; Schema: public; Owner: admin
--

CREATE INDEX idx_orders_buyer ON public.orders USING btree (buyer_id);


--
-- TOC entry 3847 (class 1259 OID 17309)
-- Name: idx_orders_number; Type: INDEX; Schema: public; Owner: admin
--

CREATE INDEX idx_orders_number ON public.orders USING btree (order_number);


--
-- TOC entry 3848 (class 1259 OID 17310)
-- Name: idx_orders_shop; Type: INDEX; Schema: public; Owner: admin
--

CREATE INDEX idx_orders_shop ON public.orders USING btree (shop_id);


--
-- TOC entry 3849 (class 1259 OID 17311)
-- Name: idx_orders_status; Type: INDEX; Schema: public; Owner: admin
--

CREATE INDEX idx_orders_status ON public.orders USING btree (status);


--
-- TOC entry 3894 (class 1259 OID 17313)
-- Name: idx_payments_status; Type: INDEX; Schema: public; Owner: admin
--

CREATE INDEX idx_payments_status ON public.payments USING btree (status);


--
-- TOC entry 3895 (class 1259 OID 17314)
-- Name: idx_payments_transaction_id; Type: INDEX; Schema: public; Owner: admin
--

CREATE INDEX idx_payments_transaction_id ON public.payments USING btree (provider_transaction_id);


--
-- TOC entry 3782 (class 1259 OID 17317)
-- Name: idx_point_logs_user_date; Type: INDEX; Schema: public; Owner: admin
--

CREATE INDEX idx_point_logs_user_date ON public.point_logs USING btree (user_id, created_at);


--
-- TOC entry 3924 (class 1259 OID 34947)
-- Name: idx_product_images_product; Type: INDEX; Schema: public; Owner: admin
--

CREATE INDEX idx_product_images_product ON public.product_images USING btree (product_id);


--
-- TOC entry 3810 (class 1259 OID 17322)
-- Name: idx_products_shop; Type: INDEX; Schema: public; Owner: admin
--

CREATE INDEX idx_products_shop ON public.products USING btree (shop_id);


--
-- TOC entry 3771 (class 1259 OID 26277)
-- Name: idx_provider_id_not_null; Type: INDEX; Schema: public; Owner: admin
--

CREATE UNIQUE INDEX idx_provider_id_not_null ON public.users USING btree (provider_id) WHERE (provider_id IS NOT NULL);


--
-- TOC entry 3948 (class 1259 OID 64306)
-- Name: idx_pulse_news_published; Type: INDEX; Schema: public; Owner: admin
--

CREATE INDEX idx_pulse_news_published ON public.pulse_news USING btree (is_published, created_at DESC);


--
-- TOC entry 3813 (class 1259 OID 80659)
-- Name: idx_seller_subs_grace; Type: INDEX; Schema: public; Owner: admin
--

CREATE INDEX idx_seller_subs_grace ON public.seller_subscriptions USING btree (grace_period_end) WHERE (grace_period_end IS NOT NULL);


--
-- TOC entry 3814 (class 1259 OID 80658)
-- Name: idx_seller_subs_period; Type: INDEX; Schema: public; Owner: admin
--

CREATE INDEX idx_seller_subs_period ON public.seller_subscriptions USING btree (status, current_period_end);


--
-- TOC entry 3820 (class 1259 OID 17328)
-- Name: idx_shop_visits_composite; Type: INDEX; Schema: public; Owner: admin
--

CREATE INDEX idx_shop_visits_composite ON public.shop_visits USING btree (shop_id, visited_at);


--
-- TOC entry 3785 (class 1259 OID 17329)
-- Name: idx_shops_name; Type: INDEX; Schema: public; Owner: admin
--

CREATE INDEX idx_shops_name ON public.shops USING btree (shop_name);


--
-- TOC entry 3786 (class 1259 OID 17330)
-- Name: idx_shops_slug; Type: INDEX; Schema: public; Owner: admin
--

CREATE INDEX idx_shops_slug ON public.shops USING btree (slug);


--
-- TOC entry 3863 (class 1259 OID 17340)
-- Name: idx_user_library_accessed; Type: INDEX; Schema: public; Owner: admin
--

CREATE INDEX idx_user_library_accessed ON public.user_library USING btree (user_id, last_accessed_at DESC);


--
-- TOC entry 3911 (class 1259 OID 17291)
-- Name: lesson_progress_user_id_lesson_id_key; Type: INDEX; Schema: public; Owner: admin
--

CREATE UNIQUE INDEX lesson_progress_user_id_lesson_id_key ON public.lesson_progress USING btree (user_id, lesson_id);


--
-- TOC entry 3878 (class 1259 OID 17298)
-- Name: media_likes_media_id_user_id_key; Type: INDEX; Schema: public; Owner: admin
--

CREATE UNIQUE INDEX media_likes_media_id_user_id_key ON public.media_likes USING btree (media_id, user_id);


--
-- TOC entry 3882 (class 1259 OID 17300)
-- Name: media_saves_media_id_user_id_key; Type: INDEX; Schema: public; Owner: admin
--

CREATE UNIQUE INDEX media_saves_media_id_user_id_key ON public.media_saves USING btree (media_id, user_id);


--
-- TOC entry 3888 (class 1259 OID 17302)
-- Name: media_watch_history_user_id_media_id_key; Type: INDEX; Schema: public; Owner: admin
--

CREATE UNIQUE INDEX media_watch_history_user_id_media_id_key ON public.media_watch_history USING btree (user_id, media_id);


--
-- TOC entry 3850 (class 1259 OID 17312)
-- Name: orders_order_number_key; Type: INDEX; Schema: public; Owner: admin
--

CREATE UNIQUE INDEX orders_order_number_key ON public.orders USING btree (order_number);


--
-- TOC entry 3896 (class 1259 OID 17315)
-- Name: payments_order_id_key; Type: INDEX; Schema: public; Owner: admin
--

CREATE UNIQUE INDEX payments_order_id_key ON public.payments USING btree (order_id);


--
-- TOC entry 3899 (class 1259 OID 17316)
-- Name: payments_provider_transaction_id_key; Type: INDEX; Schema: public; Owner: admin
--

CREATE UNIQUE INDEX payments_provider_transaction_id_key ON public.payments USING btree (provider_transaction_id);


--
-- TOC entry 3927 (class 1259 OID 34948)
-- Name: product_images_product_id_object_key_key; Type: INDEX; Schema: public; Owner: admin
--

CREATE UNIQUE INDEX product_images_product_id_object_key_key ON public.product_images USING btree (product_id, object_key);


--
-- TOC entry 3817 (class 1259 OID 17325)
-- Name: seller_subscriptions_shop_id_key; Type: INDEX; Schema: public; Owner: admin
--

CREATE UNIQUE INDEX seller_subscriptions_shop_id_key ON public.seller_subscriptions USING btree (shop_id);


--
-- TOC entry 3818 (class 1259 OID 17326)
-- Name: seller_subscriptions_stripe_subscription_id_key; Type: INDEX; Schema: public; Owner: admin
--

CREATE UNIQUE INDEX seller_subscriptions_stripe_subscription_id_key ON public.seller_subscriptions USING btree (provider_subscription_id);


--
-- TOC entry 3789 (class 1259 OID 17331)
-- Name: shops_slug_key; Type: INDEX; Schema: public; Owner: admin
--

CREATE UNIQUE INDEX shops_slug_key ON public.shops USING btree (slug);


--
-- TOC entry 3790 (class 1259 OID 17332)
-- Name: shops_user_id_key; Type: INDEX; Schema: public; Owner: admin
--

CREATE UNIQUE INDEX shops_user_id_key ON public.shops USING btree (user_id);


--
-- TOC entry 3837 (class 1259 OID 17282)
-- Name: unique_coupon_per_product; Type: INDEX; Schema: public; Owner: admin
--

CREATE UNIQUE INDEX unique_coupon_per_product ON public.coupons USING btree (product_id, code);


--
-- TOC entry 3826 (class 1259 OID 17334)
-- Name: unique_subscription; Type: INDEX; Schema: public; Owner: admin
--

CREATE UNIQUE INDEX unique_subscription ON public.subscriptions USING btree (shop_id, user_id);


--
-- TOC entry 3861 (class 1259 OID 17324)
-- Name: unique_user_review; Type: INDEX; Schema: public; Owner: admin
--

CREATE UNIQUE INDEX unique_user_review ON public.reviews USING btree (product_id, user_id);


--
-- TOC entry 3794 (class 1259 OID 17336)
-- Name: user_device_tokens_user_id_device_id_key; Type: INDEX; Schema: public; Owner: admin
--

CREATE UNIQUE INDEX user_device_tokens_user_id_device_id_key ON public.user_device_tokens USING btree (user_id, device_id);


--
-- TOC entry 3918 (class 1259 OID 17338)
-- Name: user_lesson_progress_user_id_course_lesson_id_key; Type: INDEX; Schema: public; Owner: admin
--

CREATE UNIQUE INDEX user_lesson_progress_user_id_course_lesson_id_key ON public.user_lesson_progress USING btree (user_id, course_lesson_id);


--
-- TOC entry 3866 (class 1259 OID 17341)
-- Name: user_library_user_id_product_id_key; Type: INDEX; Schema: public; Owner: admin
--

CREATE UNIQUE INDEX user_library_user_id_product_id_key ON public.user_library USING btree (user_id, product_id);


--
-- TOC entry 3797 (class 1259 OID 17342)
-- Name: user_points_user_id_key; Type: INDEX; Schema: public; Owner: admin
--

CREATE UNIQUE INDEX user_points_user_id_key ON public.user_points USING btree (user_id);


--
-- TOC entry 3772 (class 1259 OID 17344)
-- Name: users_email_key; Type: INDEX; Schema: public; Owner: admin
--

CREATE UNIQUE INDEX users_email_key ON public.users USING btree (email);


--
-- TOC entry 4026 (class 2620 OID 72467)
-- Name: cart_items set_cart_updated_at; Type: TRIGGER; Schema: public; Owner: admin
--

CREATE TRIGGER set_cart_updated_at BEFORE UPDATE ON public.cart_items FOR EACH ROW EXECUTE FUNCTION public.update_updated_at_column();


--
-- TOC entry 4032 (class 2620 OID 72448)
-- Name: media_comments set_media_comments_updated_at; Type: TRIGGER; Schema: public; Owner: admin
--

CREATE TRIGGER set_media_comments_updated_at BEFORE UPDATE ON public.media_comments FOR EACH ROW EXECUTE FUNCTION public.update_updated_at_column();


--
-- TOC entry 4028 (class 2620 OID 72464)
-- Name: orders set_orders_updated_at; Type: TRIGGER; Schema: public; Owner: admin
--

CREATE TRIGGER set_orders_updated_at BEFORE UPDATE ON public.orders FOR EACH ROW EXECUTE FUNCTION public.update_updated_at_column();


--
-- TOC entry 4039 (class 2620 OID 72465)
-- Name: payments set_payments_updated_at; Type: TRIGGER; Schema: public; Owner: admin
--

CREATE TRIGGER set_payments_updated_at BEFORE UPDATE ON public.payments FOR EACH ROW EXECUTE FUNCTION public.update_updated_at_column();


--
-- TOC entry 4031 (class 2620 OID 72413)
-- Name: reviews set_reviews_updated_at; Type: TRIGGER; Schema: public; Owner: admin
--

CREATE TRIGGER set_reviews_updated_at BEFORE UPDATE ON public.reviews FOR EACH ROW EXECUTE FUNCTION public.update_updated_at_column();


--
-- TOC entry 4024 (class 2620 OID 72466)
-- Name: seller_subscriptions set_seller_sub_updated_at; Type: TRIGGER; Schema: public; Owner: admin
--

CREATE TRIGGER set_seller_sub_updated_at BEFORE UPDATE ON public.seller_subscriptions FOR EACH ROW EXECUTE FUNCTION public.update_updated_at_column();


--
-- TOC entry 4023 (class 2620 OID 72463)
-- Name: shops set_shops_updated_at; Type: TRIGGER; Schema: public; Owner: admin
--

CREATE TRIGGER set_shops_updated_at BEFORE UPDATE ON public.shops FOR EACH ROW EXECUTE FUNCTION public.update_updated_at_column();


--
-- TOC entry 4022 (class 2620 OID 72462)
-- Name: users set_users_updated_at; Type: TRIGGER; Schema: public; Owner: admin
--

CREATE TRIGGER set_users_updated_at BEFORE UPDATE ON public.users FOR EACH ROW EXECUTE FUNCTION public.update_updated_at_column();


--
-- TOC entry 4029 (class 2620 OID 72473)
-- Name: orders trg_auto_deliver_product; Type: TRIGGER; Schema: public; Owner: admin
--

CREATE TRIGGER trg_auto_deliver_product AFTER INSERT OR UPDATE ON public.orders FOR EACH ROW EXECUTE FUNCTION public.deliver_product_to_library();


--
-- TOC entry 4027 (class 2620 OID 72475)
-- Name: cart_items trg_check_already_owned; Type: TRIGGER; Schema: public; Owner: admin
--

CREATE TRIGGER trg_check_already_owned BEFORE INSERT OR UPDATE ON public.cart_items FOR EACH ROW EXECUTE FUNCTION public.prevent_duplicate_purchase();


--
-- TOC entry 4038 (class 2620 OID 72476)
-- Name: coupon_uses trg_increment_coupon_usage; Type: TRIGGER; Schema: public; Owner: admin
--

CREATE TRIGGER trg_increment_coupon_usage AFTER INSERT ON public.coupon_uses FOR EACH ROW EXECUTE FUNCTION public.increment_coupon_usage();


--
-- TOC entry 4033 (class 2620 OID 72471)
-- Name: media_comments trg_media_comment_counter; Type: TRIGGER; Schema: public; Owner: admin
--

CREATE TRIGGER trg_media_comment_counter AFTER INSERT OR DELETE ON public.media_comments FOR EACH ROW EXECUTE FUNCTION public.sync_media_counters();


--
-- TOC entry 4034 (class 2620 OID 72469)
-- Name: media_likes trg_media_like_counter; Type: TRIGGER; Schema: public; Owner: admin
--

CREATE TRIGGER trg_media_like_counter AFTER INSERT OR DELETE ON public.media_likes FOR EACH ROW EXECUTE FUNCTION public.sync_media_counters();


--
-- TOC entry 4036 (class 2620 OID 72470)
-- Name: media_saves trg_media_save_counter; Type: TRIGGER; Schema: public; Owner: admin
--

CREATE TRIGGER trg_media_save_counter AFTER INSERT OR DELETE ON public.media_saves FOR EACH ROW EXECUTE FUNCTION public.sync_media_counters();


--
-- TOC entry 4041 (class 2620 OID 54390)
-- Name: analytics_events trg_normalize_analytics_event_shop_id; Type: TRIGGER; Schema: public; Owner: admin
--

CREATE TRIGGER trg_normalize_analytics_event_shop_id BEFORE INSERT ON public.analytics_events FOR EACH ROW EXECUTE FUNCTION public.normalize_analytics_event_shop_id();


--
-- TOC entry 4030 (class 2620 OID 72472)
-- Name: orders trg_on_order_completed; Type: TRIGGER; Schema: public; Owner: admin
--

CREATE TRIGGER trg_on_order_completed AFTER INSERT OR UPDATE ON public.orders FOR EACH ROW EXECUTE FUNCTION public.process_completed_order();


--
-- TOC entry 4035 (class 2620 OID 72458)
-- Name: media_likes trg_points_on_like; Type: TRIGGER; Schema: public; Owner: admin
--

CREATE TRIGGER trg_points_on_like AFTER INSERT ON public.media_likes FOR EACH ROW EXECUTE FUNCTION public.award_seller_points();


--
-- TOC entry 4037 (class 2620 OID 72460)
-- Name: media_watch_history trg_points_on_watch; Type: TRIGGER; Schema: public; Owner: admin
--

CREATE TRIGGER trg_points_on_watch BEFORE INSERT ON public.media_watch_history FOR EACH ROW EXECUTE FUNCTION public.award_viewer_points();


--
-- TOC entry 4025 (class 2620 OID 72468)
-- Name: subscriptions trg_sync_followers; Type: TRIGGER; Schema: public; Owner: admin
--

CREATE TRIGGER trg_sync_followers AFTER INSERT OR DELETE ON public.subscriptions FOR EACH ROW EXECUTE FUNCTION public.sync_follower_count();


--
-- TOC entry 4040 (class 2620 OID 72474)
-- Name: payments trg_sync_order_on_payment; Type: TRIGGER; Schema: public; Owner: admin
--

CREATE TRIGGER trg_sync_order_on_payment AFTER INSERT OR UPDATE ON public.payments FOR EACH ROW EXECUTE FUNCTION public.sync_order_status_from_payment();


--
-- TOC entry 4019 (class 2606 OID 64258)
-- Name: admin_audit_logs admin_audit_logs_admin_user_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: admin
--

ALTER TABLE ONLY public.admin_audit_logs
    ADD CONSTRAINT admin_audit_logs_admin_user_id_fkey FOREIGN KEY (admin_user_id) REFERENCES public.users(id) ON DELETE SET NULL;


--
-- TOC entry 4020 (class 2606 OID 64293)
-- Name: admin_competition_rewards admin_competition_rewards_contest_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: admin
--

ALTER TABLE ONLY public.admin_competition_rewards
    ADD CONSTRAINT admin_competition_rewards_contest_id_fkey FOREIGN KEY (contest_id) REFERENCES public.contests(id) ON DELETE CASCADE;


--
-- TOC entry 4021 (class 2606 OID 64298)
-- Name: admin_competition_rewards admin_competition_rewards_user_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: admin
--

ALTER TABLE ONLY public.admin_competition_rewards
    ADD CONSTRAINT admin_competition_rewards_user_id_fkey FOREIGN KEY (user_id) REFERENCES public.users(id) ON DELETE CASCADE;


--
-- TOC entry 4016 (class 2606 OID 64224)
-- Name: admin_reports admin_reports_reported_by_user_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: admin
--

ALTER TABLE ONLY public.admin_reports
    ADD CONSTRAINT admin_reports_reported_by_user_id_fkey FOREIGN KEY (reported_by_user_id) REFERENCES public.users(id) ON DELETE SET NULL;


--
-- TOC entry 4017 (class 2606 OID 64243)
-- Name: admin_warnings admin_warnings_admin_user_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: admin
--

ALTER TABLE ONLY public.admin_warnings
    ADD CONSTRAINT admin_warnings_admin_user_id_fkey FOREIGN KEY (admin_user_id) REFERENCES public.users(id) ON DELETE SET NULL;


--
-- TOC entry 4018 (class 2606 OID 64238)
-- Name: admin_warnings admin_warnings_user_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: admin
--

ALTER TABLE ONLY public.admin_warnings
    ADD CONSTRAINT admin_warnings_user_id_fkey FOREIGN KEY (user_id) REFERENCES public.users(id) ON DELETE CASCADE;


--
-- TOC entry 4012 (class 2606 OID 54375)
-- Name: analytics_events analytics_events_order_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: admin
--

ALTER TABLE ONLY public.analytics_events
    ADD CONSTRAINT analytics_events_order_id_fkey FOREIGN KEY (order_id) REFERENCES public.orders(id) ON DELETE SET NULL;


--
-- TOC entry 4013 (class 2606 OID 54365)
-- Name: analytics_events analytics_events_product_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: admin
--

ALTER TABLE ONLY public.analytics_events
    ADD CONSTRAINT analytics_events_product_id_fkey FOREIGN KEY (product_id) REFERENCES public.products(id) ON DELETE SET NULL;


--
-- TOC entry 4014 (class 2606 OID 54360)
-- Name: analytics_events analytics_events_shop_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: admin
--

ALTER TABLE ONLY public.analytics_events
    ADD CONSTRAINT analytics_events_shop_id_fkey FOREIGN KEY (shop_id) REFERENCES public.shops(id) ON DELETE CASCADE;


--
-- TOC entry 4015 (class 2606 OID 54370)
-- Name: analytics_events analytics_events_user_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: admin
--

ALTER TABLE ONLY public.analytics_events
    ADD CONSTRAINT analytics_events_user_id_fkey FOREIGN KEY (user_id) REFERENCES public.users(id) ON DELETE SET NULL;


--
-- TOC entry 3973 (class 2606 OID 16899)
-- Name: cart_items cart_items_product_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: admin
--

ALTER TABLE ONLY public.cart_items
    ADD CONSTRAINT cart_items_product_id_fkey FOREIGN KEY (product_id) REFERENCES public.products(id) ON DELETE CASCADE;


--
-- TOC entry 3974 (class 2606 OID 16904)
-- Name: cart_items cart_items_user_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: admin
--

ALTER TABLE ONLY public.cart_items
    ADD CONSTRAINT cart_items_user_id_fkey FOREIGN KEY (user_id) REFERENCES public.users(id) ON DELETE CASCADE;


--
-- TOC entry 3955 (class 2606 OID 16639)
-- Name: categories categories_parent_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: admin
--

ALTER TABLE ONLY public.categories
    ADD CONSTRAINT categories_parent_id_fkey FOREIGN KEY (parent_id) REFERENCES public.categories(id);


--
-- TOC entry 3963 (class 2606 OID 16782)
-- Name: contest_results contest_results_contest_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: admin
--

ALTER TABLE ONLY public.contest_results
    ADD CONSTRAINT contest_results_contest_id_fkey FOREIGN KEY (contest_id) REFERENCES public.contests(id) ON DELETE CASCADE;


--
-- TOC entry 3964 (class 2606 OID 16787)
-- Name: contest_results contest_results_user_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: admin
--

ALTER TABLE ONLY public.contest_results
    ADD CONSTRAINT contest_results_user_id_fkey FOREIGN KEY (user_id) REFERENCES public.users(id) ON DELETE CASCADE;


--
-- TOC entry 3956 (class 2606 OID 16677)
-- Name: contests contests_created_by_fkey; Type: FK CONSTRAINT; Schema: public; Owner: admin
--

ALTER TABLE ONLY public.contests
    ADD CONSTRAINT contests_created_by_fkey FOREIGN KEY (created_by) REFERENCES public.users(id) ON DELETE SET NULL;


--
-- TOC entry 4000 (class 2606 OID 17155)
-- Name: coupon_uses coupon_uses_coupon_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: admin
--

ALTER TABLE ONLY public.coupon_uses
    ADD CONSTRAINT coupon_uses_coupon_id_fkey FOREIGN KEY (coupon_id) REFERENCES public.coupons(id) ON DELETE CASCADE;


--
-- TOC entry 4001 (class 2606 OID 17160)
-- Name: coupon_uses coupon_uses_order_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: admin
--

ALTER TABLE ONLY public.coupon_uses
    ADD CONSTRAINT coupon_uses_order_id_fkey FOREIGN KEY (order_id) REFERENCES public.orders(id) ON DELETE CASCADE;


--
-- TOC entry 4002 (class 2606 OID 17165)
-- Name: coupon_uses coupon_uses_user_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: admin
--

ALTER TABLE ONLY public.coupon_uses
    ADD CONSTRAINT coupon_uses_user_id_fkey FOREIGN KEY (user_id) REFERENCES public.users(id) ON DELETE CASCADE;


--
-- TOC entry 3975 (class 2606 OID 16920)
-- Name: coupons coupons_product_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: admin
--

ALTER TABLE ONLY public.coupons
    ADD CONSTRAINT coupons_product_id_fkey FOREIGN KEY (product_id) REFERENCES public.products(id) ON DELETE CASCADE;


--
-- TOC entry 3976 (class 2606 OID 16925)
-- Name: coupons coupons_shop_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: admin
--

ALTER TABLE ONLY public.coupons
    ADD CONSTRAINT coupons_shop_id_fkey FOREIGN KEY (shop_id) REFERENCES public.shops(id) ON DELETE CASCADE;


--
-- TOC entry 4004 (class 2606 OID 17198)
-- Name: course_lessons course_lessons_section_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: admin
--

ALTER TABLE ONLY public.course_lessons
    ADD CONSTRAINT course_lessons_section_id_fkey FOREIGN KEY (course_section_id) REFERENCES public.course_sections(id) ON DELETE CASCADE;


--
-- TOC entry 4005 (class 2606 OID 17211)
-- Name: course_quizzes course_quizzes_section_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: admin
--

ALTER TABLE ONLY public.course_quizzes
    ADD CONSTRAINT course_quizzes_section_id_fkey FOREIGN KEY (course_section_id) REFERENCES public.course_sections(id) ON DELETE CASCADE;


--
-- TOC entry 3990 (class 2606 OID 17071)
-- Name: course_sections course_sections_course_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: admin
--

ALTER TABLE ONLY public.course_sections
    ADD CONSTRAINT course_sections_course_id_fkey FOREIGN KEY (course_id) REFERENCES public.courses(id) ON DELETE CASCADE;


--
-- TOC entry 3977 (class 2606 OID 16939)
-- Name: courses courses_product_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: admin
--

ALTER TABLE ONLY public.courses
    ADD CONSTRAINT courses_product_id_fkey FOREIGN KEY (product_id) REFERENCES public.products(id) ON DELETE CASCADE;


--
-- TOC entry 4006 (class 2606 OID 17225)
-- Name: lesson_progress lesson_progress_lesson_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: admin
--

ALTER TABLE ONLY public.lesson_progress
    ADD CONSTRAINT lesson_progress_lesson_id_fkey FOREIGN KEY (lesson_id) REFERENCES public.course_lessons(id) ON DELETE CASCADE;


--
-- TOC entry 4007 (class 2606 OID 17230)
-- Name: lesson_progress lesson_progress_user_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: admin
--

ALTER TABLE ONLY public.lesson_progress
    ADD CONSTRAINT lesson_progress_user_id_fkey FOREIGN KEY (user_id) REFERENCES public.users(id) ON DELETE CASCADE;


--
-- TOC entry 4008 (class 2606 OID 17244)
-- Name: lesson_resources lesson_resources_lesson_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: admin
--

ALTER TABLE ONLY public.lesson_resources
    ADD CONSTRAINT lesson_resources_lesson_id_fkey FOREIGN KEY (course_lesson_id) REFERENCES public.course_lessons(id) ON DELETE CASCADE;


--
-- TOC entry 3991 (class 2606 OID 17086)
-- Name: media_comments media_comments_media_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: admin
--

ALTER TABLE ONLY public.media_comments
    ADD CONSTRAINT media_comments_media_id_fkey FOREIGN KEY (media_id) REFERENCES public.media(id) ON DELETE CASCADE;


--
-- TOC entry 3992 (class 2606 OID 54998)
-- Name: media_comments media_comments_parent_comment_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: admin
--

ALTER TABLE ONLY public.media_comments
    ADD CONSTRAINT media_comments_parent_comment_id_fkey FOREIGN KEY (parent_comment_id) REFERENCES public.media_comments(id) ON DELETE CASCADE;


--
-- TOC entry 3993 (class 2606 OID 17091)
-- Name: media_comments media_comments_user_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: admin
--

ALTER TABLE ONLY public.media_comments
    ADD CONSTRAINT media_comments_user_id_fkey FOREIGN KEY (user_id) REFERENCES public.users(id) ON DELETE CASCADE;


--
-- TOC entry 3994 (class 2606 OID 17103)
-- Name: media_likes media_likes_media_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: admin
--

ALTER TABLE ONLY public.media_likes
    ADD CONSTRAINT media_likes_media_id_fkey FOREIGN KEY (media_id) REFERENCES public.media(id) ON DELETE CASCADE;


--
-- TOC entry 3995 (class 2606 OID 17108)
-- Name: media_likes media_likes_user_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: admin
--

ALTER TABLE ONLY public.media_likes
    ADD CONSTRAINT media_likes_user_id_fkey FOREIGN KEY (user_id) REFERENCES public.users(id) ON DELETE CASCADE;


--
-- TOC entry 3978 (class 2606 OID 16962)
-- Name: media media_product_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: admin
--

ALTER TABLE ONLY public.media
    ADD CONSTRAINT media_product_id_fkey FOREIGN KEY (product_id) REFERENCES public.products(id) ON DELETE SET NULL;


--
-- TOC entry 3996 (class 2606 OID 17120)
-- Name: media_saves media_saves_media_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: admin
--

ALTER TABLE ONLY public.media_saves
    ADD CONSTRAINT media_saves_media_id_fkey FOREIGN KEY (media_id) REFERENCES public.media(id) ON DELETE CASCADE;


--
-- TOC entry 3997 (class 2606 OID 17125)
-- Name: media_saves media_saves_user_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: admin
--

ALTER TABLE ONLY public.media_saves
    ADD CONSTRAINT media_saves_user_id_fkey FOREIGN KEY (user_id) REFERENCES public.users(id) ON DELETE CASCADE;


--
-- TOC entry 3979 (class 2606 OID 16967)
-- Name: media media_shop_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: admin
--

ALTER TABLE ONLY public.media
    ADD CONSTRAINT media_shop_id_fkey FOREIGN KEY (shop_id) REFERENCES public.shops(id) ON DELETE CASCADE;


--
-- TOC entry 3998 (class 2606 OID 17138)
-- Name: media_watch_history media_watch_history_media_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: admin
--

ALTER TABLE ONLY public.media_watch_history
    ADD CONSTRAINT media_watch_history_media_id_fkey FOREIGN KEY (media_id) REFERENCES public.media(id) ON DELETE CASCADE;


--
-- TOC entry 3999 (class 2606 OID 17143)
-- Name: media_watch_history media_watch_history_user_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: admin
--

ALTER TABLE ONLY public.media_watch_history
    ADD CONSTRAINT media_watch_history_user_id_fkey FOREIGN KEY (user_id) REFERENCES public.users(id) ON DELETE CASCADE;


--
-- TOC entry 3965 (class 2606 OID 16802)
-- Name: notification_deliveries notification_deliveries_notification_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: admin
--

ALTER TABLE ONLY public.notification_deliveries
    ADD CONSTRAINT notification_deliveries_notification_id_fkey FOREIGN KEY (notification_id) REFERENCES public.notifications(id) ON DELETE CASCADE;


--
-- TOC entry 3957 (class 2606 OID 16692)
-- Name: notifications notifications_user_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: admin
--

ALTER TABLE ONLY public.notifications
    ADD CONSTRAINT notifications_user_id_fkey FOREIGN KEY (user_id) REFERENCES public.users(id) ON DELETE CASCADE;


--
-- TOC entry 3980 (class 2606 OID 16986)
-- Name: orders orders_buyer_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: admin
--

ALTER TABLE ONLY public.orders
    ADD CONSTRAINT orders_buyer_id_fkey FOREIGN KEY (buyer_id) REFERENCES public.users(id) ON DELETE RESTRICT;


--
-- TOC entry 3981 (class 2606 OID 16991)
-- Name: orders orders_product_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: admin
--

ALTER TABLE ONLY public.orders
    ADD CONSTRAINT orders_product_id_fkey FOREIGN KEY (product_id) REFERENCES public.products(id) ON DELETE RESTRICT;


--
-- TOC entry 3982 (class 2606 OID 16996)
-- Name: orders orders_shop_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: admin
--

ALTER TABLE ONLY public.orders
    ADD CONSTRAINT orders_shop_id_fkey FOREIGN KEY (shop_id) REFERENCES public.shops(id) ON DELETE RESTRICT;


--
-- TOC entry 4003 (class 2606 OID 17181)
-- Name: payments payments_order_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: admin
--

ALTER TABLE ONLY public.payments
    ADD CONSTRAINT payments_order_id_fkey FOREIGN KEY (order_id) REFERENCES public.orders(id) ON DELETE RESTRICT;


--
-- TOC entry 3958 (class 2606 OID 16704)
-- Name: point_logs point_logs_user_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: admin
--

ALTER TABLE ONLY public.point_logs
    ADD CONSTRAINT point_logs_user_id_fkey FOREIGN KEY (user_id) REFERENCES public.users(id) ON DELETE CASCADE;


--
-- TOC entry 4011 (class 2606 OID 34942)
-- Name: product_images product_images_product_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: admin
--

ALTER TABLE ONLY public.product_images
    ADD CONSTRAINT product_images_product_id_fkey FOREIGN KEY (product_id) REFERENCES public.products(id) ON DELETE CASCADE;


--
-- TOC entry 3983 (class 2606 OID 17010)
-- Name: product_qa product_qa_parent_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: admin
--

ALTER TABLE ONLY public.product_qa
    ADD CONSTRAINT product_qa_parent_id_fkey FOREIGN KEY (parent_id) REFERENCES public.product_qa(id) ON DELETE CASCADE;


--
-- TOC entry 3984 (class 2606 OID 17015)
-- Name: product_qa product_qa_product_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: admin
--

ALTER TABLE ONLY public.product_qa
    ADD CONSTRAINT product_qa_product_id_fkey FOREIGN KEY (product_id) REFERENCES public.products(id) ON DELETE CASCADE;


--
-- TOC entry 3985 (class 2606 OID 17020)
-- Name: product_qa product_qa_user_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: admin
--

ALTER TABLE ONLY public.product_qa
    ADD CONSTRAINT product_qa_user_id_fkey FOREIGN KEY (user_id) REFERENCES public.users(id) ON DELETE CASCADE;


--
-- TOC entry 3966 (class 2606 OID 16826)
-- Name: products products_category_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: admin
--

ALTER TABLE ONLY public.products
    ADD CONSTRAINT products_category_id_fkey FOREIGN KEY (category_id) REFERENCES public.categories(id) ON DELETE CASCADE;


--
-- TOC entry 3967 (class 2606 OID 16831)
-- Name: products products_shop_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: admin
--

ALTER TABLE ONLY public.products
    ADD CONSTRAINT products_shop_id_fkey FOREIGN KEY (shop_id) REFERENCES public.shops(id) ON DELETE CASCADE;


--
-- TOC entry 3986 (class 2606 OID 17035)
-- Name: reviews reviews_product_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: admin
--

ALTER TABLE ONLY public.reviews
    ADD CONSTRAINT reviews_product_id_fkey FOREIGN KEY (product_id) REFERENCES public.products(id) ON DELETE CASCADE;


--
-- TOC entry 3987 (class 2606 OID 17040)
-- Name: reviews reviews_user_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: admin
--

ALTER TABLE ONLY public.reviews
    ADD CONSTRAINT reviews_user_id_fkey FOREIGN KEY (user_id) REFERENCES public.users(id) ON DELETE CASCADE;


--
-- TOC entry 3968 (class 2606 OID 16848)
-- Name: seller_subscriptions seller_subscriptions_shop_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: admin
--

ALTER TABLE ONLY public.seller_subscriptions
    ADD CONSTRAINT seller_subscriptions_shop_id_fkey FOREIGN KEY (shop_id) REFERENCES public.shops(id) ON DELETE CASCADE;


--
-- TOC entry 3969 (class 2606 OID 16862)
-- Name: shop_visits shop_visits_shop_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: admin
--

ALTER TABLE ONLY public.shop_visits
    ADD CONSTRAINT shop_visits_shop_id_fkey FOREIGN KEY (shop_id) REFERENCES public.shops(id) ON DELETE CASCADE;


--
-- TOC entry 3970 (class 2606 OID 16867)
-- Name: shop_visits shop_visits_user_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: admin
--

ALTER TABLE ONLY public.shop_visits
    ADD CONSTRAINT shop_visits_user_id_fkey FOREIGN KEY (user_id) REFERENCES public.users(id) ON DELETE SET NULL;


--
-- TOC entry 3959 (class 2606 OID 16724)
-- Name: shops shops_user_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: admin
--

ALTER TABLE ONLY public.shops
    ADD CONSTRAINT shops_user_id_fkey FOREIGN KEY (user_id) REFERENCES public.users(id) ON DELETE CASCADE;


--
-- TOC entry 3971 (class 2606 OID 16880)
-- Name: subscriptions subscriptions_shop_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: admin
--

ALTER TABLE ONLY public.subscriptions
    ADD CONSTRAINT subscriptions_shop_id_fkey FOREIGN KEY (shop_id) REFERENCES public.shops(id) ON DELETE CASCADE;


--
-- TOC entry 3972 (class 2606 OID 16885)
-- Name: subscriptions subscriptions_user_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: admin
--

ALTER TABLE ONLY public.subscriptions
    ADD CONSTRAINT subscriptions_user_id_fkey FOREIGN KEY (user_id) REFERENCES public.users(id) ON DELETE CASCADE;


--
-- TOC entry 3960 (class 2606 OID 16740)
-- Name: user_device_tokens user_device_tokens_user_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: admin
--

ALTER TABLE ONLY public.user_device_tokens
    ADD CONSTRAINT user_device_tokens_user_id_fkey FOREIGN KEY (user_id) REFERENCES public.users(id) ON DELETE CASCADE;


--
-- TOC entry 4009 (class 2606 OID 17258)
-- Name: user_lesson_progress user_lesson_progress_course_lesson_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: admin
--

ALTER TABLE ONLY public.user_lesson_progress
    ADD CONSTRAINT user_lesson_progress_course_lesson_id_fkey FOREIGN KEY (course_lesson_id) REFERENCES public.course_lessons(id) ON DELETE CASCADE;


--
-- TOC entry 4010 (class 2606 OID 17263)
-- Name: user_lesson_progress user_lesson_progress_user_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: admin
--

ALTER TABLE ONLY public.user_lesson_progress
    ADD CONSTRAINT user_lesson_progress_user_id_fkey FOREIGN KEY (user_id) REFERENCES public.users(id) ON DELETE CASCADE;


--
-- TOC entry 3988 (class 2606 OID 17053)
-- Name: user_library user_library_product_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: admin
--

ALTER TABLE ONLY public.user_library
    ADD CONSTRAINT user_library_product_id_fkey FOREIGN KEY (product_id) REFERENCES public.products(id) ON DELETE CASCADE;


--
-- TOC entry 3989 (class 2606 OID 17058)
-- Name: user_library user_library_user_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: admin
--

ALTER TABLE ONLY public.user_library
    ADD CONSTRAINT user_library_user_id_fkey FOREIGN KEY (user_id) REFERENCES public.users(id) ON DELETE CASCADE;


--
-- TOC entry 3961 (class 2606 OID 16755)
-- Name: user_points user_points_user_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: admin
--

ALTER TABLE ONLY public.user_points
    ADD CONSTRAINT user_points_user_id_fkey FOREIGN KEY (user_id) REFERENCES public.users(id) ON DELETE CASCADE;


--
-- TOC entry 3962 (class 2606 OID 16769)
-- Name: user_sessions user_sessions_user_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: admin
--

ALTER TABLE ONLY public.user_sessions
    ADD CONSTRAINT user_sessions_user_id_fkey FOREIGN KEY (user_id) REFERENCES public.users(id) ON DELETE CASCADE;


--
-- TOC entry 4252 (class 3256 OID 26280)
-- Name: users Aktif kullanıcıları herkes görebilir; Type: POLICY; Schema: public; Owner: admin
--

CREATE POLICY "Aktif kullanıcıları herkes görebilir" ON public.users FOR SELECT USING (((is_active = true) AND (deleted_at IS NULL)));


--
-- TOC entry 4227 (class 3256 OID 72393)
-- Name: products Aktif ürünler herkese açık; Type: POLICY; Schema: public; Owner: admin
--

CREATE POLICY "Aktif ürünler herkese açık" ON public.products FOR SELECT USING ((is_active = true));


--
-- TOC entry 4265 (class 3256 OID 72491)
-- Name: payments Alıcılar dekontunu görebilir; Type: POLICY; Schema: public; Owner: admin
--

CREATE POLICY "Alıcılar dekontunu görebilir" ON public.payments FOR SELECT USING ((order_id IN ( SELECT orders.id
   FROM public.orders
  WHERE (orders.buyer_id = (current_setting('app.current_user_id'::text, true))::uuid))));


--
-- TOC entry 4263 (class 3256 OID 72489)
-- Name: orders Alıcılar kendi siparişlerini görebilir; Type: POLICY; Schema: public; Owner: admin
--

CREATE POLICY "Alıcılar kendi siparişlerini görebilir" ON public.orders FOR SELECT USING ((buyer_id = (current_setting('app.current_user_id'::text, true))::uuid));


--
-- TOC entry 4220 (class 3256 OID 72387)
-- Name: shop_visits Herkes ziyaret kaydı oluşturabilir; Type: POLICY; Schema: public; Owner: admin
--

CREATE POLICY "Herkes ziyaret kaydı oluşturabilir" ON public.shop_visits FOR INSERT WITH CHECK (((user_id IS NULL) OR (user_id = (current_setting('app.current_user_id'::text, true))::uuid)));


--
-- TOC entry 4240 (class 3256 OID 72412)
-- Name: categories Kategoriler herkese açık; Type: POLICY; Schema: public; Owner: admin
--

CREATE POLICY "Kategoriler herkese açık" ON public.categories FOR SELECT USING ((is_active = true));


--
-- TOC entry 4246 (class 3256 OID 72450)
-- Name: media_likes Kullanıcı beğeni yapabilir/silebilir; Type: POLICY; Schema: public; Owner: admin
--

CREATE POLICY "Kullanıcı beğeni yapabilir/silebilir" ON public.media_likes USING ((user_id = (current_setting('app.current_user_id'::text, true))::uuid)) WITH CHECK ((user_id = (current_setting('app.current_user_id'::text, true))::uuid));


--
-- TOC entry 4256 (class 3256 OID 72449)
-- Name: media_likes Kullanıcı beğenileri görebilir; Type: POLICY; Schema: public; Owner: admin
--

CREATE POLICY "Kullanıcı beğenileri görebilir" ON public.media_likes FOR SELECT USING ((user_id = (current_setting('app.current_user_id'::text, true))::uuid));


--
-- TOC entry 4273 (class 3256 OID 80663)
-- Name: notifications Kullanıcı bildirimini okundu yapabilir; Type: POLICY; Schema: public; Owner: admin
--

CREATE POLICY "Kullanıcı bildirimini okundu yapabilir" ON public.notifications FOR UPDATE USING ((user_id = (current_setting('app.current_user_id'::text, true))::uuid));


--
-- TOC entry 4250 (class 3256 OID 72443)
-- Name: media_watch_history Kullanıcı izleme geçmişi oluşturabilir; Type: POLICY; Schema: public; Owner: admin
--

CREATE POLICY "Kullanıcı izleme geçmişi oluşturabilir" ON public.media_watch_history FOR INSERT WITH CHECK ((user_id = (current_setting('app.current_user_id'::text, true))::uuid));


--
-- TOC entry 4258 (class 3256 OID 72452)
-- Name: media_saves Kullanıcı kaydedebilir/silebilir; Type: POLICY; Schema: public; Owner: admin
--

CREATE POLICY "Kullanıcı kaydedebilir/silebilir" ON public.media_saves USING ((user_id = (current_setting('app.current_user_id'::text, true))::uuid)) WITH CHECK ((user_id = (current_setting('app.current_user_id'::text, true))::uuid));


--
-- TOC entry 4247 (class 3256 OID 72451)
-- Name: media_saves Kullanıcı kayıtları görebilir; Type: POLICY; Schema: public; Owner: admin
--

CREATE POLICY "Kullanıcı kayıtları görebilir" ON public.media_saves FOR SELECT USING ((user_id = (current_setting('app.current_user_id'::text, true))::uuid));


--
-- TOC entry 4241 (class 3256 OID 72414)
-- Name: coupon_uses Kullanıcı kendi adına kupon kullanabilir; Type: POLICY; Schema: public; Owner: admin
--

CREATE POLICY "Kullanıcı kendi adına kupon kullanabilir" ON public.coupon_uses FOR INSERT WITH CHECK ((user_id = (current_setting('app.current_user_id'::text, true))::uuid));


--
-- TOC entry 4245 (class 3256 OID 54392)
-- Name: analytics_events Kullanıcı kendi analytics eventini oluşturabilir; Type: POLICY; Schema: public; Owner: admin
--

CREATE POLICY "Kullanıcı kendi analytics eventini oluşturabilir" ON public.analytics_events FOR INSERT WITH CHECK (((user_id IS NULL) OR (user_id = (current_setting('app.current_user_id'::text, true))::uuid)));


--
-- TOC entry 4272 (class 3256 OID 80662)
-- Name: notifications Kullanıcı kendi bildirimlerini görebilir; Type: POLICY; Schema: public; Owner: admin
--

CREATE POLICY "Kullanıcı kendi bildirimlerini görebilir" ON public.notifications FOR SELECT USING ((user_id = (current_setting('app.current_user_id'::text, true))::uuid));


--
-- TOC entry 4274 (class 3256 OID 80665)
-- Name: user_device_tokens Kullanıcı kendi cihazlarını yönetebilir; Type: POLICY; Schema: public; Owner: admin
--

CREATE POLICY "Kullanıcı kendi cihazlarını yönetebilir" ON public.user_device_tokens USING ((user_id = (current_setting('app.current_user_id'::text, true))::uuid)) WITH CHECK ((user_id = (current_setting('app.current_user_id'::text, true))::uuid));


--
-- TOC entry 4268 (class 3256 OID 80654)
-- Name: lesson_progress Kullanıcı kendi ilerlemesini görebilir; Type: POLICY; Schema: public; Owner: admin
--

CREATE POLICY "Kullanıcı kendi ilerlemesini görebilir" ON public.lesson_progress FOR SELECT USING ((user_id = (current_setting('app.current_user_id'::text, true))::uuid));


--
-- TOC entry 4269 (class 3256 OID 80655)
-- Name: lesson_progress Kullanıcı kendi ilerlemesini yönetebilir; Type: POLICY; Schema: public; Owner: admin
--

CREATE POLICY "Kullanıcı kendi ilerlemesini yönetebilir" ON public.lesson_progress USING ((user_id = (current_setting('app.current_user_id'::text, true))::uuid)) WITH CHECK ((user_id = (current_setting('app.current_user_id'::text, true))::uuid));


--
-- TOC entry 4251 (class 3256 OID 72444)
-- Name: media_watch_history Kullanıcı kendi izleme geçmişini görebilir; Type: POLICY; Schema: public; Owner: admin
--

CREATE POLICY "Kullanıcı kendi izleme geçmişini görebilir" ON public.media_watch_history FOR SELECT USING ((user_id = (current_setting('app.current_user_id'::text, true))::uuid));


--
-- TOC entry 4267 (class 3256 OID 80653)
-- Name: user_library Kullanıcı kendi kütüphanesini görebilir; Type: POLICY; Schema: public; Owner: admin
--

CREATE POLICY "Kullanıcı kendi kütüphanesini görebilir" ON public.user_library FOR SELECT USING ((user_id = (current_setting('app.current_user_id'::text, true))::uuid));


--
-- TOC entry 4221 (class 3256 OID 72388)
-- Name: shops Kullanıcı kendi mağazasını açabilir; Type: POLICY; Schema: public; Owner: admin
--

CREATE POLICY "Kullanıcı kendi mağazasını açabilir" ON public.shops FOR INSERT WITH CHECK ((user_id = (current_setting('app.current_user_id'::text, true))::uuid));


--
-- TOC entry 4262 (class 3256 OID 72456)
-- Name: point_logs Kullanıcı kendi puan geçmişini görebilir; Type: POLICY; Schema: public; Owner: admin
--

CREATE POLICY "Kullanıcı kendi puan geçmişini görebilir" ON public.point_logs FOR SELECT USING ((user_id = (current_setting('app.current_user_id'::text, true))::uuid));


--
-- TOC entry 4271 (class 3256 OID 80661)
-- Name: cart_items Kullanıcı kendi sepetini yönetebilir; Type: POLICY; Schema: public; Owner: admin
--

CREATE POLICY "Kullanıcı kendi sepetini yönetebilir" ON public.cart_items USING ((user_id = (current_setting('app.current_user_id'::text, true))::uuid)) WITH CHECK ((user_id = (current_setting('app.current_user_id'::text, true))::uuid));


--
-- TOC entry 4235 (class 3256 OID 72405)
-- Name: product_qa Kullanıcı kendi sorusunu silebilir; Type: POLICY; Schema: public; Owner: admin
--

CREATE POLICY "Kullanıcı kendi sorusunu silebilir" ON public.product_qa FOR DELETE USING ((user_id = (current_setting('app.current_user_id'::text, true))::uuid));


--
-- TOC entry 4219 (class 3256 OID 72386)
-- Name: subscriptions Kullanıcı kendi takibini silebilir; Type: POLICY; Schema: public; Owner: admin
--

CREATE POLICY "Kullanıcı kendi takibini silebilir" ON public.subscriptions FOR DELETE USING ((user_id = (current_setting('app.current_user_id'::text, true))::uuid));


--
-- TOC entry 4223 (class 3256 OID 72391)
-- Name: subscriptions Kullanıcı kendi takip listesini görebilir; Type: POLICY; Schema: public; Owner: admin
--

CREATE POLICY "Kullanıcı kendi takip listesini görebilir" ON public.subscriptions FOR SELECT USING ((user_id = (current_setting('app.current_user_id'::text, true))::uuid));


--
-- TOC entry 4255 (class 3256 OID 72447)
-- Name: contest_results Kullanıcı kendi yarışma sonucunu görebilir; Type: POLICY; Schema: public; Owner: admin
--

CREATE POLICY "Kullanıcı kendi yarışma sonucunu görebilir" ON public.contest_results FOR INSERT WITH CHECK ((user_id = (current_setting('app.current_user_id'::text, true))::uuid));


--
-- TOC entry 4231 (class 3256 OID 72397)
-- Name: reviews Kullanıcı kendi yorumunu güncelleyebilir; Type: POLICY; Schema: public; Owner: admin
--

CREATE POLICY "Kullanıcı kendi yorumunu güncelleyebilir" ON public.reviews FOR UPDATE USING ((user_id = (current_setting('app.current_user_id'::text, true))::uuid));


--
-- TOC entry 4232 (class 3256 OID 72398)
-- Name: reviews Kullanıcı kendi yorumunu silebilir; Type: POLICY; Schema: public; Owner: admin
--

CREATE POLICY "Kullanıcı kendi yorumunu silebilir" ON public.reviews FOR DELETE USING ((user_id = (current_setting('app.current_user_id'::text, true))::uuid));


--
-- TOC entry 4260 (class 3256 OID 72454)
-- Name: media_comments Kullanıcı kendi yorumunu yönetebilir; Type: POLICY; Schema: public; Owner: admin
--

CREATE POLICY "Kullanıcı kendi yorumunu yönetebilir" ON public.media_comments USING ((user_id = (current_setting('app.current_user_id'::text, true))::uuid)) WITH CHECK ((user_id = (current_setting('app.current_user_id'::text, true))::uuid));


--
-- TOC entry 4218 (class 3256 OID 72385)
-- Name: subscriptions Kullanıcı kendisi için takip oluşturabilir; Type: POLICY; Schema: public; Owner: admin
--

CREATE POLICY "Kullanıcı kendisi için takip oluşturabilir" ON public.subscriptions FOR INSERT WITH CHECK ((user_id = (current_setting('app.current_user_id'::text, true))::uuid));


--
-- TOC entry 4234 (class 3256 OID 72404)
-- Name: product_qa Kullanıcı soru sorabilir; Type: POLICY; Schema: public; Owner: admin
--

CREATE POLICY "Kullanıcı soru sorabilir" ON public.product_qa FOR INSERT WITH CHECK ((user_id = (current_setting('app.current_user_id'::text, true))::uuid));


--
-- TOC entry 4249 (class 3256 OID 72442)
-- Name: media_comments Kullanıcı yorum yazabilir; Type: POLICY; Schema: public; Owner: admin
--

CREATE POLICY "Kullanıcı yorum yazabilir" ON public.media_comments FOR INSERT WITH CHECK ((user_id = (current_setting('app.current_user_id'::text, true))::uuid));


--
-- TOC entry 4230 (class 3256 OID 72396)
-- Name: reviews Kullanıcı yorum yazabilir; Type: POLICY; Schema: public; Owner: admin
--

CREATE POLICY "Kullanıcı yorum yazabilir" ON public.reviews FOR INSERT WITH CHECK ((user_id = (current_setting('app.current_user_id'::text, true))::uuid));


--
-- TOC entry 4236 (class 3256 OID 72406)
-- Name: course_sections Kurs bölümleri herkese açık; Type: POLICY; Schema: public; Owner: admin
--

CREATE POLICY "Kurs bölümleri herkese açık" ON public.course_sections FOR SELECT USING (true);


--
-- TOC entry 4238 (class 3256 OID 72409)
-- Name: course_lessons Kurs dersleri herkese açık; Type: POLICY; Schema: public; Owner: admin
--

CREATE POLICY "Kurs dersleri herkese açık" ON public.course_lessons FOR SELECT USING (true);


--
-- TOC entry 4261 (class 3256 OID 72455)
-- Name: user_points Liderlik tablosunu herkes görebilir; Type: POLICY; Schema: public; Owner: admin
--

CREATE POLICY "Liderlik tablosunu herkes görebilir" ON public.user_points FOR SELECT USING (true);


--
-- TOC entry 4244 (class 3256 OID 54391)
-- Name: analytics_events Satıcı kendi analytics verilerini görebilir; Type: POLICY; Schema: public; Owner: admin
--

CREATE POLICY "Satıcı kendi analytics verilerini görebilir" ON public.analytics_events FOR SELECT USING ((shop_id IN ( SELECT shops.id
   FROM public.shops
  WHERE (shops.user_id = (current_setting('app.current_user_id'::text, true))::uuid))));


--
-- TOC entry 4228 (class 3256 OID 72394)
-- Name: products Satıcı kendi ürünlerini yönetebilir; Type: POLICY; Schema: public; Owner: admin
--

CREATE POLICY "Satıcı kendi ürünlerini yönetebilir" ON public.products USING ((shop_id IN ( SELECT shops.id
   FROM public.shops
  WHERE (shops.user_id = (current_setting('app.current_user_id'::text, true))::uuid))));


--
-- TOC entry 4237 (class 3256 OID 72407)
-- Name: course_sections Satıcı kurs bölümlerini yönetebilir; Type: POLICY; Schema: public; Owner: admin
--

CREATE POLICY "Satıcı kurs bölümlerini yönetebilir" ON public.course_sections USING ((course_id IN ( SELECT products.id
   FROM public.products
  WHERE (products.shop_id IN ( SELECT shops.id
           FROM public.shops
          WHERE (shops.user_id = (current_setting('app.current_user_id'::text, true))::uuid))))));


--
-- TOC entry 4239 (class 3256 OID 72410)
-- Name: course_lessons Satıcı kurs derslerini yönetebilir; Type: POLICY; Schema: public; Owner: admin
--

CREATE POLICY "Satıcı kurs derslerini yönetebilir" ON public.course_lessons USING ((course_section_id IN ( SELECT course_sections.id
   FROM public.course_sections
  WHERE (course_sections.course_id IN ( SELECT courses.id
           FROM public.courses
          WHERE (courses.product_id IN ( SELECT products.id
                   FROM public.products
                  WHERE (products.shop_id IN ( SELECT shops.id
                           FROM public.shops
                          WHERE (shops.user_id = (current_setting('app.current_user_id'::text, true))::uuid))))))))));


--
-- TOC entry 4270 (class 3256 OID 80657)
-- Name: seller_subscriptions Satıcılar kendi abonelik durumlarını görebilir; Type: POLICY; Schema: public; Owner: admin
--

CREATE POLICY "Satıcılar kendi abonelik durumlarını görebilir" ON public.seller_subscriptions FOR SELECT USING ((shop_id IN ( SELECT shops.id
   FROM public.shops
  WHERE (shops.user_id = (current_setting('app.current_user_id'::text, true))::uuid))));


--
-- TOC entry 4266 (class 3256 OID 72492)
-- Name: payments Satıcılar kendi gelir dökümlerini görebilir; Type: POLICY; Schema: public; Owner: admin
--

CREATE POLICY "Satıcılar kendi gelir dökümlerini görebilir" ON public.payments FOR SELECT USING ((order_id IN ( SELECT orders.id
   FROM public.orders
  WHERE (orders.shop_id IN ( SELECT shops.id
           FROM public.shops
          WHERE (shops.user_id = (current_setting('app.current_user_id'::text, true))::uuid))))));


--
-- TOC entry 4264 (class 3256 OID 72490)
-- Name: orders Satıcılar kendi mağaza siparişlerini görebilir; Type: POLICY; Schema: public; Owner: admin
--

CREATE POLICY "Satıcılar kendi mağaza siparişlerini görebilir" ON public.orders FOR SELECT USING ((shop_id IN ( SELECT shops.id
   FROM public.shops
  WHERE (shops.user_id = (current_setting('app.current_user_id'::text, true))::uuid))));


--
-- TOC entry 4233 (class 3256 OID 72403)
-- Name: product_qa Sorular herkese açık; Type: POLICY; Schema: public; Owner: admin
--

CREATE POLICY "Sorular herkese açık" ON public.product_qa FOR SELECT USING (true);


--
-- TOC entry 4254 (class 3256 OID 72446)
-- Name: contest_results Yarışma sonuçları herkese açık; Type: POLICY; Schema: public; Owner: admin
--

CREATE POLICY "Yarışma sonuçları herkese açık" ON public.contest_results FOR SELECT USING (true);


--
-- TOC entry 4253 (class 3256 OID 72445)
-- Name: contests Yarışmalar herkese açık; Type: POLICY; Schema: public; Owner: admin
--

CREATE POLICY "Yarışmalar herkese açık" ON public.contests FOR SELECT USING ((is_active = true));


--
-- TOC entry 4229 (class 3256 OID 72395)
-- Name: reviews Yorumlar herkese açık; Type: POLICY; Schema: public; Owner: admin
--

CREATE POLICY "Yorumlar herkese açık" ON public.reviews FOR SELECT USING (true);


--
-- TOC entry 4259 (class 3256 OID 72453)
-- Name: media_comments Yorumları herkes okuyabilir; Type: POLICY; Schema: public; Owner: admin
--

CREATE POLICY "Yorumları herkes okuyabilir" ON public.media_comments FOR SELECT USING (true);


--
-- TOC entry 4217 (class 0 OID 54347)
-- Dependencies: 256
-- Name: analytics_events; Type: ROW SECURITY; Schema: public; Owner: admin
--

ALTER TABLE public.analytics_events ENABLE ROW LEVEL SECURITY;

--
-- TOC entry 4200 (class 0 OID 16890)
-- Dependencies: 233
-- Name: cart_items; Type: ROW SECURITY; Schema: public; Owner: admin
--

ALTER TABLE public.cart_items ENABLE ROW LEVEL SECURITY;

--
-- TOC entry 4185 (class 0 OID 16629)
-- Dependencies: 217
-- Name: categories; Type: ROW SECURITY; Schema: public; Owner: admin
--

ALTER TABLE public.categories ENABLE ROW LEVEL SECURITY;

--
-- TOC entry 4195 (class 0 OID 16774)
-- Dependencies: 227
-- Name: contest_results; Type: ROW SECURITY; Schema: public; Owner: admin
--

ALTER TABLE public.contest_results ENABLE ROW LEVEL SECURITY;

--
-- TOC entry 4188 (class 0 OID 16668)
-- Dependencies: 220
-- Name: contests; Type: ROW SECURITY; Schema: public; Owner: admin
--

ALTER TABLE public.contests ENABLE ROW LEVEL SECURITY;

--
-- TOC entry 4212 (class 0 OID 17148)
-- Dependencies: 246
-- Name: coupon_uses; Type: ROW SECURITY; Schema: public; Owner: admin
--

ALTER TABLE public.coupon_uses ENABLE ROW LEVEL SECURITY;

--
-- TOC entry 4201 (class 0 OID 16909)
-- Dependencies: 234
-- Name: coupons; Type: ROW SECURITY; Schema: public; Owner: admin
--

ALTER TABLE public.coupons ENABLE ROW LEVEL SECURITY;

--
-- TOC entry 4214 (class 0 OID 17186)
-- Dependencies: 248
-- Name: course_lessons; Type: ROW SECURITY; Schema: public; Owner: admin
--

ALTER TABLE public.course_lessons ENABLE ROW LEVEL SECURITY;

--
-- TOC entry 4207 (class 0 OID 17063)
-- Dependencies: 241
-- Name: course_sections; Type: ROW SECURITY; Schema: public; Owner: admin
--

ALTER TABLE public.course_sections ENABLE ROW LEVEL SECURITY;

--
-- TOC entry 4216 (class 0 OID 26281)
-- Dependencies: 254
-- Name: ip_login_attempts; Type: ROW SECURITY; Schema: public; Owner: admin
--

ALTER TABLE public.ip_login_attempts ENABLE ROW LEVEL SECURITY;

--
-- TOC entry 4222 (class 3256 OID 72389)
-- Name: ip_login_attempts ip_login_attempts_backend_only; Type: POLICY; Schema: public; Owner: admin
--

CREATE POLICY ip_login_attempts_backend_only ON public.ip_login_attempts USING (false);


--
-- TOC entry 4215 (class 0 OID 17216)
-- Dependencies: 250
-- Name: lesson_progress; Type: ROW SECURITY; Schema: public; Owner: admin
--

ALTER TABLE public.lesson_progress ENABLE ROW LEVEL SECURITY;

--
-- TOC entry 4186 (class 0 OID 16644)
-- Dependencies: 218
-- Name: login_attempts; Type: ROW SECURITY; Schema: public; Owner: admin
--

ALTER TABLE public.login_attempts ENABLE ROW LEVEL SECURITY;

--
-- TOC entry 4248 (class 3256 OID 72357)
-- Name: login_attempts login_attempts_backend_only; Type: POLICY; Schema: public; Owner: admin
--

CREATE POLICY login_attempts_backend_only ON public.login_attempts USING (false);


--
-- TOC entry 4202 (class 0 OID 16944)
-- Dependencies: 236
-- Name: media; Type: ROW SECURITY; Schema: public; Owner: admin
--

ALTER TABLE public.media ENABLE ROW LEVEL SECURITY;

--
-- TOC entry 4208 (class 0 OID 17076)
-- Dependencies: 242
-- Name: media_comments; Type: ROW SECURITY; Schema: public; Owner: admin
--

ALTER TABLE public.media_comments ENABLE ROW LEVEL SECURITY;

--
-- TOC entry 4209 (class 0 OID 17096)
-- Dependencies: 243
-- Name: media_likes; Type: ROW SECURITY; Schema: public; Owner: admin
--

ALTER TABLE public.media_likes ENABLE ROW LEVEL SECURITY;

--
-- TOC entry 4210 (class 0 OID 17113)
-- Dependencies: 244
-- Name: media_saves; Type: ROW SECURITY; Schema: public; Owner: admin
--

ALTER TABLE public.media_saves ENABLE ROW LEVEL SECURITY;

--
-- TOC entry 4243 (class 3256 OID 72483)
-- Name: media media_select_active; Type: POLICY; Schema: public; Owner: admin
--

CREATE POLICY media_select_active ON public.media FOR SELECT USING ((is_active = true));


--
-- TOC entry 4211 (class 0 OID 17130)
-- Dependencies: 245
-- Name: media_watch_history; Type: ROW SECURITY; Schema: public; Owner: admin
--

ALTER TABLE public.media_watch_history ENABLE ROW LEVEL SECURITY;

--
-- TOC entry 4196 (class 0 OID 16792)
-- Dependencies: 228
-- Name: notification_deliveries; Type: ROW SECURITY; Schema: public; Owner: admin
--

ALTER TABLE public.notification_deliveries ENABLE ROW LEVEL SECURITY;

--
-- TOC entry 4275 (class 3256 OID 80666)
-- Name: notification_deliveries notification_deliveries_backend_only; Type: POLICY; Schema: public; Owner: admin
--

CREATE POLICY notification_deliveries_backend_only ON public.notification_deliveries USING (false);


--
-- TOC entry 4189 (class 0 OID 16682)
-- Dependencies: 221
-- Name: notifications; Type: ROW SECURITY; Schema: public; Owner: admin
--

ALTER TABLE public.notifications ENABLE ROW LEVEL SECURITY;

--
-- TOC entry 4203 (class 0 OID 16972)
-- Dependencies: 237
-- Name: orders; Type: ROW SECURITY; Schema: public; Owner: admin
--

ALTER TABLE public.orders ENABLE ROW LEVEL SECURITY;

--
-- TOC entry 4213 (class 0 OID 17170)
-- Dependencies: 247
-- Name: payments; Type: ROW SECURITY; Schema: public; Owner: admin
--

ALTER TABLE public.payments ENABLE ROW LEVEL SECURITY;

--
-- TOC entry 4190 (class 0 OID 16697)
-- Dependencies: 222
-- Name: point_logs; Type: ROW SECURITY; Schema: public; Owner: admin
--

ALTER TABLE public.point_logs ENABLE ROW LEVEL SECURITY;

--
-- TOC entry 4204 (class 0 OID 17001)
-- Dependencies: 238
-- Name: product_qa; Type: ROW SECURITY; Schema: public; Owner: admin
--

ALTER TABLE public.product_qa ENABLE ROW LEVEL SECURITY;

--
-- TOC entry 4197 (class 0 OID 16807)
-- Dependencies: 229
-- Name: products; Type: ROW SECURITY; Schema: public; Owner: admin
--

ALTER TABLE public.products ENABLE ROW LEVEL SECURITY;

--
-- TOC entry 4205 (class 0 OID 17025)
-- Dependencies: 239
-- Name: reviews; Type: ROW SECURITY; Schema: public; Owner: admin
--

ALTER TABLE public.reviews ENABLE ROW LEVEL SECURITY;

--
-- TOC entry 4225 (class 3256 OID 72480)
-- Name: user_sessions sessions_delete_own; Type: POLICY; Schema: public; Owner: admin
--

CREATE POLICY sessions_delete_own ON public.user_sessions FOR DELETE USING ((user_id = (current_setting('app.current_user_id'::text, true))::uuid));


--
-- TOC entry 4224 (class 3256 OID 72479)
-- Name: user_sessions sessions_select_own; Type: POLICY; Schema: public; Owner: admin
--

CREATE POLICY sessions_select_own ON public.user_sessions FOR SELECT USING ((user_id = (current_setting('app.current_user_id'::text, true))::uuid));


--
-- TOC entry 4198 (class 0 OID 16853)
-- Dependencies: 231
-- Name: shop_visits; Type: ROW SECURITY; Schema: public; Owner: admin
--

ALTER TABLE public.shop_visits ENABLE ROW LEVEL SECURITY;

--
-- TOC entry 4191 (class 0 OID 16709)
-- Dependencies: 223
-- Name: shops; Type: ROW SECURITY; Schema: public; Owner: admin
--

ALTER TABLE public.shops ENABLE ROW LEVEL SECURITY;

--
-- TOC entry 4226 (class 3256 OID 72481)
-- Name: shops shops_select_active; Type: POLICY; Schema: public; Owner: admin
--

CREATE POLICY shops_select_active ON public.shops FOR SELECT USING ((is_active = true));


--
-- TOC entry 4242 (class 3256 OID 72482)
-- Name: shops shops_update_owner; Type: POLICY; Schema: public; Owner: admin
--

CREATE POLICY shops_update_owner ON public.shops FOR UPDATE USING ((user_id = (current_setting('app.current_user_id'::text, true))::uuid));


--
-- TOC entry 4199 (class 0 OID 16872)
-- Dependencies: 232
-- Name: subscriptions; Type: ROW SECURITY; Schema: public; Owner: admin
--

ALTER TABLE public.subscriptions ENABLE ROW LEVEL SECURITY;

--
-- TOC entry 4192 (class 0 OID 16729)
-- Dependencies: 224
-- Name: user_device_tokens; Type: ROW SECURITY; Schema: public; Owner: admin
--

ALTER TABLE public.user_device_tokens ENABLE ROW LEVEL SECURITY;

--
-- TOC entry 4206 (class 0 OID 17045)
-- Dependencies: 240
-- Name: user_library; Type: ROW SECURITY; Schema: public; Owner: admin
--

ALTER TABLE public.user_library ENABLE ROW LEVEL SECURITY;

--
-- TOC entry 4193 (class 0 OID 16745)
-- Dependencies: 225
-- Name: user_points; Type: ROW SECURITY; Schema: public; Owner: admin
--

ALTER TABLE public.user_points ENABLE ROW LEVEL SECURITY;

--
-- TOC entry 4194 (class 0 OID 16760)
-- Dependencies: 226
-- Name: user_sessions; Type: ROW SECURITY; Schema: public; Owner: admin
--

ALTER TABLE public.user_sessions ENABLE ROW LEVEL SECURITY;

--
-- TOC entry 4187 (class 0 OID 16653)
-- Dependencies: 219
-- Name: users; Type: ROW SECURITY; Schema: public; Owner: admin
--

ALTER TABLE public.users ENABLE ROW LEVEL SECURITY;

--
-- TOC entry 4257 (class 3256 OID 72478)
-- Name: users users_update_own; Type: POLICY; Schema: public; Owner: admin
--

CREATE POLICY users_update_own ON public.users FOR UPDATE USING ((id = (current_setting('app.current_user_id'::text, true))::uuid));


--
-- TOC entry 4327 (class 0 OID 0)
-- Dependencies: 7
-- Name: SCHEMA public; Type: ACL; Schema: -; Owner: pg_database_owner
--

GRANT USAGE ON SCHEMA public TO test_app_user;


--
-- TOC entry 4330 (class 0 OID 0)
-- Dependencies: 219
-- Name: TABLE users; Type: ACL; Schema: public; Owner: admin
--

GRANT SELECT,UPDATE ON TABLE public.users TO test_app_user;


-- Completed on 2026-07-05 23:15:49

--
-- PostgreSQL database dump complete
--

\unrestrict f4DQKSmXG9YrRWYUUWnZ90QjTuzcm0Xo0GxqzacGiaAKiM5i0Dkm6lRCnpfsNkn

