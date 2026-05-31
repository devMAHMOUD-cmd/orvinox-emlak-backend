using Microsoft.EntityFrameworkCore;

namespace CraftoraApi.Data;

public static class DatabaseHardening
{
    public static async Task ApplyAsync(AppDbContext db)
    {
        await db.Database.ExecuteSqlRawAsync("""
            CREATE EXTENSION IF NOT EXISTS "uuid-ossp";
            CREATE EXTENSION IF NOT EXISTS "citext";

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
                    IF TG_OP = 'INSERT' THEN UPDATE media SET like_count = like_count + 1 WHERE id = NEW.media_id;
                    ELSIF TG_OP = 'DELETE' THEN UPDATE media SET like_count = GREATEST(like_count - 1, 0) WHERE id = OLD.media_id; END IF;
                ELSIF TG_TABLE_NAME = 'media_saves' THEN
                    IF TG_OP = 'INSERT' THEN UPDATE media SET save_count = save_count + 1 WHERE id = NEW.media_id;
                    ELSIF TG_OP = 'DELETE' THEN UPDATE media SET save_count = GREATEST(save_count - 1, 0) WHERE id = OLD.media_id; END IF;
                ELSIF TG_TABLE_NAME = 'media_comments' THEN
                    IF TG_OP = 'INSERT' THEN UPDATE media SET comment_count = comment_count + 1 WHERE id = NEW.media_id;
                    ELSIF TG_OP = 'DELETE' THEN UPDATE media SET comment_count = GREATEST(comment_count - 1, 0) WHERE id = OLD.media_id; END IF;
                END IF;
                RETURN NULL;
            END;
            $$ LANGUAGE plpgsql;

            CREATE OR REPLACE FUNCTION deliver_product_to_library()
            RETURNS TRIGGER AS $$
            BEGIN
                IF (NEW.status = 'completed' AND (TG_OP = 'INSERT' OR OLD.status != 'completed')) THEN
                    INSERT INTO user_library (user_id, product_id)
                    VALUES (NEW.buyer_id, NEW.product_id)
                    ON CONFLICT (user_id, product_id) DO NOTHING;
                END IF;
                RETURN NEW;
            END;
            $$ LANGUAGE plpgsql;

            CREATE OR REPLACE FUNCTION process_completed_order()
            RETURNS TRIGGER AS $$
            DECLARE v_seller_id UUID;
            BEGIN
                IF (NEW.status = 'completed' AND (TG_OP = 'INSERT' OR OLD.status != 'completed')) THEN
                    UPDATE products SET sales_count = sales_count + 1 WHERE id = NEW.product_id;
                    SELECT user_id INTO v_seller_id FROM shops WHERE id = NEW.shop_id;

                    IF v_seller_id IS NOT NULL THEN
                        INSERT INTO point_logs (user_id, action_type, points_earned, reference_id)
                        VALUES (v_seller_id, 'make_sale', 20.0, NEW.id);

                        INSERT INTO user_points (user_id, total_points)
                        VALUES (v_seller_id, 20.0)
                        ON CONFLICT (user_id) DO UPDATE
                        SET total_points = user_points.total_points + 20.0,
                            updated_at = CURRENT_TIMESTAMP;
                    END IF;
                END IF;
                RETURN NEW;
            END;
            $$ LANGUAGE plpgsql;

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
            $$ LANGUAGE plpgsql;

            CREATE OR REPLACE FUNCTION prevent_duplicate_purchase()
            RETURNS TRIGGER AS $$
            BEGIN
                IF EXISTS (SELECT 1 FROM user_library WHERE user_id = NEW.user_id AND product_id = NEW.product_id) THEN
                    RAISE EXCEPTION 'Bu ürün zaten kütüphanenizde mevcut!';
                END IF;
                RETURN NEW;
            END;
            $$ LANGUAGE plpgsql;

            CREATE OR REPLACE FUNCTION increment_coupon_usage()
            RETURNS TRIGGER AS $$
            BEGIN
                UPDATE coupons SET used_count = used_count + 1 WHERE id = NEW.coupon_id;
                RETURN NEW;
            END;
            $$ LANGUAGE plpgsql;
            """);

        await db.Database.ExecuteSqlRawAsync("""
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

            DROP POLICY IF EXISTS users_select_active ON users;
            CREATE POLICY users_select_active ON users FOR SELECT USING (is_active = TRUE);

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
            CREATE POLICY cart_manage_own ON cart_items FOR ALL USING (user_id = current_setting('app.current_user_id', true)::uuid);

            DROP POLICY IF EXISTS notifications_manage_own ON notifications;
            CREATE POLICY notifications_manage_own ON notifications FOR ALL USING (user_id = current_setting('app.current_user_id', true)::uuid);

            DROP POLICY IF EXISTS device_tokens_manage_own ON user_device_tokens;
            CREATE POLICY device_tokens_manage_own ON user_device_tokens FOR ALL USING (user_id = current_setting('app.current_user_id', true)::uuid);
            """);
    }
}
