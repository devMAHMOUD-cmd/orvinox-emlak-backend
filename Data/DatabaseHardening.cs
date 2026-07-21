using Microsoft.EntityFrameworkCore;

namespace CraftoraApi.Data;

public static class DatabaseHardening
{
    public static async Task ApplyAsync(AppDbContext db)
    {
        await db.Database.ExecuteSqlRawAsync("""
            CREATE EXTENSION IF NOT EXISTS "uuid-ossp";
            CREATE EXTENSION IF NOT EXISTS "citext";

            ALTER TABLE contests ADD COLUMN IF NOT EXISTS description TEXT;
            ALTER TABLE contests ADD COLUMN IF NOT EXISTS rewards_hidden BOOLEAN DEFAULT FALSE;
            ALTER TABLE contest_results ADD COLUMN IF NOT EXISTS joined_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP;
            ALTER TABLE media ADD COLUMN IF NOT EXISTS share_count INT DEFAULT 0;

            CREATE TABLE IF NOT EXISTS admin_reports (
                id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
                type VARCHAR(50) NOT NULL,
                target_id UUID NOT NULL,
                target_title TEXT,
                reported_by_user_id UUID REFERENCES users(id) ON DELETE SET NULL,
                reason VARCHAR(50) NOT NULL,
                description TEXT,
                status VARCHAR(20) NOT NULL DEFAULT 'open',
                created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
                updated_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
            );

            CREATE TABLE IF NOT EXISTS admin_warnings (
                id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
                user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
                admin_user_id UUID REFERENCES users(id) ON DELETE SET NULL,
                title VARCHAR(255) NOT NULL,
                message TEXT NOT NULL,
                created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
            );

            CREATE TABLE IF NOT EXISTS admin_audit_logs (
                id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
                admin_user_id UUID REFERENCES users(id) ON DELETE SET NULL,
                action VARCHAR(100) NOT NULL,
                target_type VARCHAR(50) NOT NULL,
                target_id UUID,
                metadata JSONB DEFAULT '{{}}'::jsonb,
                created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
            );

            CREATE TABLE IF NOT EXISTS pulse_news (
                id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
                title VARCHAR(255) NOT NULL,
                description TEXT,
                meta VARCHAR(100),
                icon VARCHAR(50),
                is_published BOOLEAN NOT NULL DEFAULT false,
                is_new_until TIMESTAMP WITH TIME ZONE,
                created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
                updated_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
            );

            CREATE TABLE IF NOT EXISTS home_cards (
                id VARCHAR(80) PRIMARY KEY,
                title VARCHAR(255) NOT NULL,
                description TEXT,
                icon VARCHAR(50),
                action_type VARCHAR(50),
                sort_order INT NOT NULL DEFAULT 0,
                is_active BOOLEAN NOT NULL DEFAULT true,
                updated_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
            );

            CREATE TABLE IF NOT EXISTS admin_competition_rewards (
                id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
                contest_id UUID NOT NULL REFERENCES contests(id) ON DELETE CASCADE,
                user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
                rank INT NOT NULL,
                reward_type VARCHAR(50) NOT NULL,
                amount DECIMAL(12,2),
                currency VARCHAR(3),
                note TEXT,
                certificate_url TEXT,
                created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
            );

            ALTER TABLE admin_competition_rewards ADD COLUMN IF NOT EXISTS certificate_url TEXT;
            ALTER TABLE admin_competition_rewards DROP CONSTRAINT IF EXISTS check_admin_competition_rewards_type;
            ALTER TABLE admin_competition_rewards
                ADD CONSTRAINT check_admin_competition_rewards_type
                CHECK (reward_type IN ('money', 'premium_1_month', 'certificate'));
            CREATE UNIQUE INDEX IF NOT EXISTS uq_admin_competition_rewards_contest_user
                ON admin_competition_rewards (contest_id, user_id);

            CREATE INDEX IF NOT EXISTS idx_admin_reports_status_type ON admin_reports(status, type);
            CREATE INDEX IF NOT EXISTS idx_admin_warnings_user ON admin_warnings(user_id, created_at DESC);
            CREATE INDEX IF NOT EXISTS idx_admin_audit_logs_created ON admin_audit_logs(created_at DESC);
            CREATE INDEX IF NOT EXISTS idx_pulse_news_published ON pulse_news(is_published, created_at DESC);
            CREATE INDEX IF NOT EXISTS idx_seller_subs_period ON seller_subscriptions(status, current_period_end);
            CREATE INDEX IF NOT EXISTS idx_seller_subs_grace ON seller_subscriptions(grace_period_end) WHERE grace_period_end IS NOT NULL;
            CREATE INDEX IF NOT EXISTS idx_media_comments_media_parent_created ON media_comments(media_id, parent_comment_id, created_at);
            CREATE INDEX IF NOT EXISTS idx_media_comments_parent ON media_comments(parent_comment_id);
            CREATE INDEX IF NOT EXISTS idx_ip_attempts_locked_until ON ip_login_attempts(locked_until) WHERE locked_until IS NOT NULL;
            CREATE UNIQUE INDEX IF NOT EXISTS uq_point_logs_complete_lesson_once
                ON point_logs(user_id, reference_id)
                WHERE action_type = 'complete_lesson';
            CREATE UNIQUE INDEX IF NOT EXISTS uq_point_logs_make_sale_once
                ON point_logs(user_id, reference_id)
                WHERE action_type = 'make_sale';
            CREATE UNIQUE INDEX IF NOT EXISTS uq_point_logs_purchase_product_once
                ON point_logs(user_id, reference_id)
                WHERE action_type = 'purchase_product';
            CREATE UNIQUE INDEX IF NOT EXISTS uq_point_logs_create_product_once
                ON point_logs(user_id, reference_id)
                WHERE action_type = 'create_product';
            CREATE UNIQUE INDEX IF NOT EXISTS uq_point_logs_watch_reels_once
                ON point_logs(user_id, reference_id)
                WHERE action_type = 'watch_reels';

            ALTER TABLE orders DROP CONSTRAINT IF EXISTS check_fee_logic;
            ALTER TABLE orders ADD CONSTRAINT check_fee_logic CHECK (ABS(amount - (platform_fee + seller_earnings)) <= 0.01);

            ALTER TABLE payments DROP CONSTRAINT IF EXISTS check_payment_math;
            ALTER TABLE payments ADD CONSTRAINT check_payment_math CHECK (ABS(gross_amount - (platform_fee_amount + net_earnings)) <= 0.01);

            ALTER TABLE seller_subscriptions DROP CONSTRAINT IF EXISTS check_grace_after_period;
            ALTER TABLE seller_subscriptions ADD CONSTRAINT check_grace_after_period CHECK (grace_period_end IS NULL OR grace_period_end >= current_period_end);

            ALTER TABLE notifications DROP CONSTRAINT IF EXISTS check_notification_type;
            ALTER TABLE notifications ADD CONSTRAINT check_notification_type CHECK (type IN (
                'sale_completed',
                'new_follower',
                'new_review',
                'new_question',
                'media_liked',
                'media_commented',
                'contest_result',
                'order_completed',
                'new_video',
                'new_product',
                'product_question_answer',
                'system'
            ));

            CREATE OR REPLACE FUNCTION update_updated_at_column()
            RETURNS TRIGGER AS $$
            BEGIN
                NEW.updated_at = CURRENT_TIMESTAMP;
                RETURN NEW;
            END;
            $$ LANGUAGE plpgsql;

            CREATE OR REPLACE FUNCTION sync_follower_count()
            RETURNS TRIGGER AS $$
            BEGIN
                IF (TG_OP = 'INSERT') THEN
                    UPDATE shops SET follower_count = follower_count + 1 WHERE id = NEW.shop_id;
                ELSIF (TG_OP = 'DELETE') THEN
                    UPDATE shops SET follower_count = GREATEST(follower_count - 1, 0) WHERE id = OLD.shop_id;
                END IF;
                RETURN NULL;
            END;
            $$ LANGUAGE plpgsql;

            CREATE OR REPLACE FUNCTION sync_media_counters()
            RETURNS TRIGGER AS $$
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
            $$ LANGUAGE plpgsql SECURITY DEFINER SET search_path = public;

            CREATE OR REPLACE FUNCTION deliver_product_to_library()
            RETURNS TRIGGER AS $$
            BEGIN
                IF (NEW.status = 'completed' AND (TG_OP = 'INSERT' OR OLD.status != 'completed')) THEN
                    INSERT INTO user_library (user_id, product_id)
                    VALUES (NEW.buyer_id, NEW.product_id)
                    ON CONFLICT (user_id, product_id) DO NOTHING;
                ELSIF (NEW.status = 'refunded' AND TG_OP = 'UPDATE' AND OLD.status != 'refunded') THEN
                    DELETE FROM user_library
                    WHERE user_id = NEW.buyer_id AND product_id = NEW.product_id;
                END IF;

                RETURN NEW;
            END;
            $$ LANGUAGE plpgsql SECURITY DEFINER SET search_path = public;

            CREATE OR REPLACE FUNCTION process_completed_order()
            RETURNS TRIGGER AS $$
            DECLARE v_seller_id UUID; v_point_log_id UUID;
            BEGIN
                SELECT user_id INTO v_seller_id FROM shops WHERE id = NEW.shop_id;

                IF (NEW.status = 'completed' AND (TG_OP = 'INSERT' OR OLD.status != 'completed')) THEN
                    UPDATE products SET sales_count = sales_count + 1 WHERE id = NEW.product_id;

                    IF v_seller_id IS NOT NULL THEN
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
                    END IF;
                ELSIF (NEW.status = 'refunded' AND TG_OP = 'UPDATE' AND OLD.status != 'refunded') THEN
                    UPDATE products SET sales_count = GREATEST(sales_count - 1, 0) WHERE id = NEW.product_id;

                    IF v_seller_id IS NOT NULL THEN
                        INSERT INTO point_logs (user_id, action_type, points_earned, reference_id)
                        VALUES (v_seller_id, 'refund_sale', -20.0, NEW.id);

                        UPDATE user_points
                        SET total_points = GREATEST(total_points - 20.0, 0),
                            updated_at = CURRENT_TIMESTAMP
                        WHERE user_id = v_seller_id;
                    END IF;
                END IF;

                RETURN NEW;
            END;
            $$ LANGUAGE plpgsql SECURITY DEFINER SET search_path = public;

            CREATE OR REPLACE FUNCTION sync_order_status_from_payment()
            RETURNS TRIGGER AS $$
            BEGIN
                IF (NEW.status = 'succeeded' AND (TG_OP = 'INSERT' OR OLD.status != 'succeeded')) THEN
                    UPDATE orders SET status = 'completed' WHERE id = NEW.order_id;
                ELSIF (NEW.status = 'refunded' AND (TG_OP = 'INSERT' OR OLD.status != 'refunded')) THEN
                    UPDATE orders SET status = 'refunded' WHERE id = NEW.order_id;
                END IF;

                RETURN NEW;
            END;
            $$ LANGUAGE plpgsql SECURITY DEFINER SET search_path = public;

            CREATE OR REPLACE FUNCTION prevent_duplicate_purchase()
            RETURNS TRIGGER AS $$
            BEGIN
                IF EXISTS (SELECT 1 FROM user_library WHERE user_id = NEW.user_id AND product_id = NEW.product_id) THEN
                    RAISE EXCEPTION 'Bu urun zaten kutuphanenizde mevcut!';
                END IF;

                RETURN NEW;
            END;
            $$ LANGUAGE plpgsql SECURITY DEFINER SET search_path = public;

            CREATE OR REPLACE FUNCTION award_seller_points()
            RETURNS TRIGGER AS $$
            DECLARE v_seller_id UUID;
            BEGIN
                SELECT s.user_id INTO v_seller_id
                FROM media m
                JOIN shops s ON m.shop_id = s.id
                WHERE m.id = NEW.media_id;

                IF v_seller_id IS NOT NULL THEN
                    INSERT INTO point_logs (user_id, action_type, points_earned, reference_id)
                    VALUES (v_seller_id, 'receive_like', 2.0, NEW.id);

                    INSERT INTO user_points (user_id, total_points)
                    VALUES (v_seller_id, 2.0)
                    ON CONFLICT (user_id) DO UPDATE
                    SET total_points = user_points.total_points + 2.0,
                        updated_at = CURRENT_TIMESTAMP;
                END IF;

                RETURN NEW;
            END;
            $$ LANGUAGE plpgsql SECURITY DEFINER SET search_path = public;

            CREATE OR REPLACE FUNCTION award_viewer_points()
            RETURNS TRIGGER AS $$
            DECLARE v_daily_points DECIMAL; v_point_log_id UUID;
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
            $$ LANGUAGE plpgsql SECURITY DEFINER SET search_path = public;

            CREATE OR REPLACE FUNCTION normalize_analytics_event_shop_id()
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

            CREATE OR REPLACE FUNCTION reward_lesson_completion()
            RETURNS TRIGGER AS $$
            DECLARE
                v_point_log_id UUID;
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
            $$ LANGUAGE plpgsql SECURITY DEFINER SET search_path = public;

            CREATE OR REPLACE FUNCTION increment_coupon_usage()
            RETURNS TRIGGER AS $$
            BEGIN
                UPDATE coupons SET used_count = used_count + 1 WHERE id = NEW.coupon_id;
                RETURN NEW;
            END;
            $$ LANGUAGE plpgsql;
            """);

        await db.Database.ExecuteSqlRawAsync("""
            CREATE OR REPLACE FUNCTION is_current_app_admin()
            RETURNS boolean
            LANGUAGE sql
            STABLE
            SECURITY DEFINER
            SET search_path = public
            AS $$
                SELECT EXISTS (
                    SELECT 1
                    FROM users user_record
                    WHERE user_record.id = current_setting('app.current_user_id', true)::uuid
                      AND user_record.role = 'admin'::user_role
                      AND user_record.is_active = TRUE
                      AND user_record.deleted_at IS NULL
                      AND (user_record.locked_until IS NULL OR user_record.locked_until <= CURRENT_TIMESTAMP)
                );
            $$;

            DROP TRIGGER IF EXISTS set_users_updated_at ON users;
            CREATE TRIGGER set_users_updated_at BEFORE UPDATE ON users FOR EACH ROW EXECUTE FUNCTION update_updated_at_column();

            DROP TRIGGER IF EXISTS set_shops_updated_at ON shops;
            CREATE TRIGGER set_shops_updated_at BEFORE UPDATE ON shops FOR EACH ROW EXECUTE FUNCTION update_updated_at_column();

            DROP TRIGGER IF EXISTS set_orders_updated_at ON orders;
            CREATE TRIGGER set_orders_updated_at BEFORE UPDATE ON orders FOR EACH ROW EXECUTE FUNCTION update_updated_at_column();

            DROP TRIGGER IF EXISTS set_payments_updated_at ON payments;
            CREATE TRIGGER set_payments_updated_at BEFORE UPDATE ON payments FOR EACH ROW EXECUTE FUNCTION update_updated_at_column();

            DROP TRIGGER IF EXISTS set_seller_sub_updated_at ON seller_subscriptions;
            CREATE TRIGGER set_seller_sub_updated_at BEFORE UPDATE ON seller_subscriptions FOR EACH ROW EXECUTE FUNCTION update_updated_at_column();

            DROP TRIGGER IF EXISTS set_cart_updated_at ON cart_items;
            CREATE TRIGGER set_cart_updated_at BEFORE UPDATE ON cart_items FOR EACH ROW EXECUTE FUNCTION update_updated_at_column();

            DROP TRIGGER IF EXISTS set_reviews_updated_at ON reviews;
            CREATE TRIGGER set_reviews_updated_at BEFORE UPDATE ON reviews FOR EACH ROW EXECUTE FUNCTION update_updated_at_column();

            DROP TRIGGER IF EXISTS set_media_comments_updated_at ON media_comments;
            CREATE TRIGGER set_media_comments_updated_at BEFORE UPDATE ON media_comments FOR EACH ROW EXECUTE FUNCTION update_updated_at_column();

            DROP TRIGGER IF EXISTS set_support_tickets_updated_at ON support_tickets;
            CREATE TRIGGER set_support_tickets_updated_at BEFORE UPDATE ON support_tickets FOR EACH ROW EXECUTE FUNCTION update_updated_at_column();

            DROP TRIGGER IF EXISTS trg_sync_followers ON subscriptions;
            CREATE TRIGGER trg_sync_followers AFTER INSERT OR DELETE ON subscriptions FOR EACH ROW EXECUTE FUNCTION sync_follower_count();

            DROP TRIGGER IF EXISTS trg_media_like_counter ON media_likes;
            CREATE TRIGGER trg_media_like_counter AFTER INSERT OR DELETE ON media_likes FOR EACH ROW EXECUTE FUNCTION sync_media_counters();

            DROP TRIGGER IF EXISTS trg_media_save_counter ON media_saves;
            CREATE TRIGGER trg_media_save_counter AFTER INSERT OR DELETE ON media_saves FOR EACH ROW EXECUTE FUNCTION sync_media_counters();

            DROP TRIGGER IF EXISTS trg_media_comment_counter ON media_comments;
            CREATE TRIGGER trg_media_comment_counter AFTER INSERT OR DELETE ON media_comments FOR EACH ROW EXECUTE FUNCTION sync_media_counters();

            DROP TRIGGER IF EXISTS trg_on_order_completed ON orders;
            CREATE TRIGGER trg_on_order_completed AFTER INSERT OR UPDATE ON orders FOR EACH ROW EXECUTE FUNCTION process_completed_order();

            DROP TRIGGER IF EXISTS trg_auto_deliver_product ON orders;
            CREATE TRIGGER trg_auto_deliver_product AFTER INSERT OR UPDATE ON orders FOR EACH ROW EXECUTE FUNCTION deliver_product_to_library();

            DROP TRIGGER IF EXISTS trg_sync_order_on_payment ON payments;
            CREATE TRIGGER trg_sync_order_on_payment AFTER INSERT OR UPDATE ON payments FOR EACH ROW EXECUTE FUNCTION sync_order_status_from_payment();

            DROP TRIGGER IF EXISTS trg_check_already_owned ON cart_items;
            CREATE TRIGGER trg_check_already_owned BEFORE INSERT OR UPDATE ON cart_items FOR EACH ROW EXECUTE FUNCTION prevent_duplicate_purchase();

            DROP TRIGGER IF EXISTS trg_increment_coupon_usage ON coupon_uses;
            CREATE TRIGGER trg_increment_coupon_usage AFTER INSERT ON coupon_uses FOR EACH ROW EXECUTE FUNCTION increment_coupon_usage();

            DROP TRIGGER IF EXISTS trg_points_on_like ON media_likes;
            CREATE TRIGGER trg_points_on_like AFTER INSERT ON media_likes FOR EACH ROW EXECUTE FUNCTION award_seller_points();

            DROP TRIGGER IF EXISTS trg_points_on_watch ON media_watch_history;
            CREATE TRIGGER trg_points_on_watch BEFORE INSERT ON media_watch_history FOR EACH ROW EXECUTE FUNCTION award_viewer_points();

            DROP TRIGGER IF EXISTS trg_points_on_lesson_completion ON user_lesson_progress;
            CREATE TRIGGER trg_points_on_lesson_completion
            AFTER INSERT OR UPDATE OF is_completed ON user_lesson_progress
            FOR EACH ROW EXECUTE FUNCTION reward_lesson_completion();

            DROP TRIGGER IF EXISTS trg_normalize_analytics_event_shop_id ON analytics_events;
            CREATE TRIGGER trg_normalize_analytics_event_shop_id BEFORE INSERT ON analytics_events FOR EACH ROW EXECUTE FUNCTION normalize_analytics_event_shop_id();
            """);

        await db.Database.ExecuteSqlRawAsync("""
            ALTER TABLE users ENABLE ROW LEVEL SECURITY;
            ALTER TABLE user_sessions ENABLE ROW LEVEL SECURITY;
            ALTER TABLE shops ENABLE ROW LEVEL SECURITY;
            ALTER TABLE subscriptions ENABLE ROW LEVEL SECURITY;
            ALTER TABLE shop_visits ENABLE ROW LEVEL SECURITY;
            ALTER TABLE media ENABLE ROW LEVEL SECURITY;
            ALTER TABLE media_likes ENABLE ROW LEVEL SECURITY;
            ALTER TABLE media_saves ENABLE ROW LEVEL SECURITY;
            ALTER TABLE media_comments ENABLE ROW LEVEL SECURITY;
            ALTER TABLE media_watch_history ENABLE ROW LEVEL SECURITY;
            ALTER TABLE user_points ENABLE ROW LEVEL SECURITY;
            ALTER TABLE point_logs ENABLE ROW LEVEL SECURITY;
            ALTER TABLE orders ENABLE ROW LEVEL SECURITY;
            ALTER TABLE payments ENABLE ROW LEVEL SECURITY;
            ALTER TABLE user_library ENABLE ROW LEVEL SECURITY;
            ALTER TABLE cart_items ENABLE ROW LEVEL SECURITY;
            ALTER TABLE coupons ENABLE ROW LEVEL SECURITY;
            ALTER TABLE coupon_uses ENABLE ROW LEVEL SECURITY;
            ALTER TABLE notifications ENABLE ROW LEVEL SECURITY;
            ALTER TABLE notification_deliveries ENABLE ROW LEVEL SECURITY;
            ALTER TABLE user_device_tokens ENABLE ROW LEVEL SECURITY;
            ALTER TABLE support_tickets ENABLE ROW LEVEL SECURITY;
            ALTER TABLE support_ticket_messages ENABLE ROW LEVEL SECURITY;

            DROP POLICY IF EXISTS users_select_active ON users;
            DROP POLICY IF EXISTS "Aktif kullanıcıları herkes görebilir" ON users;
            CREATE POLICY "Aktif kullanıcıları herkes görebilir" ON users FOR SELECT USING (is_active = TRUE AND deleted_at IS NULL);

            DROP POLICY IF EXISTS users_update_own ON users;
            CREATE POLICY users_update_own ON users FOR UPDATE USING (id = current_setting('app.current_user_id', true)::uuid);

            DROP POLICY IF EXISTS sessions_select_own ON user_sessions;
            CREATE POLICY sessions_select_own ON user_sessions FOR SELECT USING (user_id = current_setting('app.current_user_id', true)::uuid);

            DROP POLICY IF EXISTS sessions_delete_own ON user_sessions;
            CREATE POLICY sessions_delete_own ON user_sessions FOR DELETE USING (user_id = current_setting('app.current_user_id', true)::uuid);

            DROP POLICY IF EXISTS shops_select_active ON shops;
            CREATE POLICY shops_select_active ON shops FOR SELECT USING (is_active = TRUE);

            DROP POLICY IF EXISTS shops_update_owner ON shops;
            CREATE POLICY shops_update_owner ON shops FOR UPDATE USING (user_id = current_setting('app.current_user_id', true)::uuid);

            DROP POLICY IF EXISTS media_select_active ON media;
            CREATE POLICY media_select_active ON media FOR SELECT USING (is_active = TRUE);

            DROP POLICY IF EXISTS cart_manage_own ON cart_items;
            DROP POLICY IF EXISTS "Kullanıcı kendi sepetini yönetebilir" ON cart_items;
            CREATE POLICY "Kullanıcı kendi sepetini yönetebilir" ON cart_items
                USING (user_id = current_setting('app.current_user_id', true)::uuid)
                WITH CHECK (user_id = current_setting('app.current_user_id', true)::uuid);

            DROP POLICY IF EXISTS notifications_manage_own ON notifications;
            DROP POLICY IF EXISTS "Kullanıcı kendi bildirimlerini görebilir" ON notifications;
            CREATE POLICY "Kullanıcı kendi bildirimlerini görebilir" ON notifications FOR SELECT
                USING (user_id = current_setting('app.current_user_id', true)::uuid);

            DROP POLICY IF EXISTS "Kullanıcı bildirimini okundu yapabilir" ON notifications;
            CREATE POLICY "Kullanıcı bildirimini okundu yapabilir" ON notifications FOR UPDATE
                USING (user_id = current_setting('app.current_user_id', true)::uuid);

            DROP POLICY IF EXISTS device_tokens_manage_own ON user_device_tokens;
            DROP POLICY IF EXISTS "Kullanıcı kendi cihazlarını yönetebilir" ON user_device_tokens;
            CREATE POLICY "Kullanıcı kendi cihazlarını yönetebilir" ON user_device_tokens
                USING (user_id = current_setting('app.current_user_id', true)::uuid)
                WITH CHECK (user_id = current_setting('app.current_user_id', true)::uuid);

            DROP POLICY IF EXISTS support_tickets_select_own ON support_tickets;
            CREATE POLICY support_tickets_select_own ON support_tickets FOR SELECT
                USING (user_id = current_setting('app.current_user_id', true)::uuid);

            DROP POLICY IF EXISTS support_tickets_insert_own ON support_tickets;
            CREATE POLICY support_tickets_insert_own ON support_tickets FOR INSERT
                WITH CHECK (user_id = current_setting('app.current_user_id', true)::uuid);

            DROP POLICY IF EXISTS support_tickets_update_own ON support_tickets;
            CREATE POLICY support_tickets_update_own ON support_tickets FOR UPDATE
                USING (user_id = current_setting('app.current_user_id', true)::uuid)
                WITH CHECK (user_id = current_setting('app.current_user_id', true)::uuid);

            DROP POLICY IF EXISTS support_tickets_admin_select ON support_tickets;
            CREATE POLICY support_tickets_admin_select ON support_tickets FOR SELECT
                USING (is_current_app_admin());

            DROP POLICY IF EXISTS support_tickets_admin_update ON support_tickets;
            CREATE POLICY support_tickets_admin_update ON support_tickets FOR UPDATE
                USING (is_current_app_admin())
                WITH CHECK (is_current_app_admin());

            DROP POLICY IF EXISTS support_ticket_messages_select_own ON support_ticket_messages;
            CREATE POLICY support_ticket_messages_select_own ON support_ticket_messages FOR SELECT
                USING (
                    EXISTS (
                        SELECT 1
                        FROM support_tickets ticket
                        WHERE ticket.id = support_ticket_messages.ticket_id
                          AND ticket.user_id = current_setting('app.current_user_id', true)::uuid
                    )
                );

            DROP POLICY IF EXISTS support_ticket_messages_insert_own ON support_ticket_messages;
            CREATE POLICY support_ticket_messages_insert_own ON support_ticket_messages FOR INSERT
                WITH CHECK (
                    sender_id = current_setting('app.current_user_id', true)::uuid
                    AND sender_role = 'user'::support_message_sender_role
                    AND EXISTS (
                        SELECT 1
                        FROM support_tickets ticket
                        WHERE ticket.id = support_ticket_messages.ticket_id
                          AND ticket.user_id = current_setting('app.current_user_id', true)::uuid
                    )
                );

            DROP POLICY IF EXISTS support_ticket_messages_admin_select ON support_ticket_messages;
            CREATE POLICY support_ticket_messages_admin_select ON support_ticket_messages FOR SELECT
                USING (is_current_app_admin());

            DROP POLICY IF EXISTS support_ticket_messages_admin_insert ON support_ticket_messages;
            CREATE POLICY support_ticket_messages_admin_insert ON support_ticket_messages FOR INSERT
                WITH CHECK (
                    is_current_app_admin()
                    AND sender_id = current_setting('app.current_user_id', true)::uuid
                    AND sender_role = 'admin'::support_message_sender_role
                );
            """);
    }
}
