-- ============================================================================
-- CRAFTORA DATABASE PATCH: ANALYTICS MEDIA VIEW EVENTS
-- Date: 2026-07-19
-- Purpose:
--   - Adds media_view to analytics_event_type.
--   - Adds analytics_events.media_id for date-based reel view analytics.
--   - Updates analytics normalization so media_id resolves the owning shop.
-- Run as: PostgreSQL admin/superuser.
-- ============================================================================

ALTER TYPE public.analytics_event_type ADD VALUE IF NOT EXISTS 'media_view';

BEGIN;

ALTER TABLE public.analytics_events
    ADD COLUMN IF NOT EXISTS media_id uuid;

ALTER TABLE public.analytics_events DROP CONSTRAINT IF EXISTS check_analytics_product_events;
ALTER TABLE public.analytics_events ADD CONSTRAINT check_analytics_product_events CHECK (
    event_type <> ALL (ARRAY[
        'product_view'::public.analytics_event_type,
        'add_to_cart'::public.analytics_event_type,
        'download_clicked'::public.analytics_event_type
    ])
    OR product_id IS NOT NULL
);

ALTER TABLE public.analytics_events DROP CONSTRAINT IF EXISTS check_analytics_media_events;
ALTER TABLE public.analytics_events ADD CONSTRAINT check_analytics_media_events CHECK (
    event_type <> 'media_view'::public.analytics_event_type
    OR media_id IS NOT NULL
);

ALTER TABLE public.analytics_events DROP CONSTRAINT IF EXISTS analytics_events_media_id_fkey;
ALTER TABLE public.analytics_events
    ADD CONSTRAINT analytics_events_media_id_fkey
    FOREIGN KEY (media_id) REFERENCES public.media(id) ON DELETE SET NULL;

CREATE INDEX IF NOT EXISTS idx_analytics_media_event_date
    ON public.analytics_events USING btree (media_id, event_type, created_at DESC)
    WHERE media_id IS NOT NULL;

CREATE OR REPLACE FUNCTION public.normalize_analytics_event_shop_id()
RETURNS TRIGGER AS $$
DECLARE
    v_product_shop_id UUID;
    v_media_shop_id UUID;
    v_order_shop_id UUID;
BEGIN
    IF NEW.product_id IS NOT NULL THEN
        SELECT shop_id INTO v_product_shop_id
        FROM products
        WHERE id = NEW.product_id;

        IF v_product_shop_id IS NULL THEN
            RAISE EXCEPTION 'Analytics event product_id gecersiz: %', NEW.product_id;
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

    IF NEW.order_id IS NOT NULL THEN
        SELECT shop_id INTO v_order_shop_id
        FROM orders
        WHERE id = NEW.order_id;

        IF v_order_shop_id IS NULL THEN
            RAISE EXCEPTION 'Analytics event order_id gecersiz: %', NEW.order_id;
        END IF;

        NEW.shop_id := v_order_shop_id;
    END IF;

    RETURN NEW;
END;
$$ LANGUAGE plpgsql SECURITY DEFINER SET search_path = public;

COMMIT;
