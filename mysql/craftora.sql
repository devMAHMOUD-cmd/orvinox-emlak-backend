-- BİSMİLLAH: CRAFTORA VERİTABANI KURULUMU - BÖLÜM 1

-- 1. EKLENTİLER (EXTENSIONS)
CREATE EXTENSION IF NOT EXISTS "uuid-ossp"; -- UUID (rastgele benzersiz ID) oluşturmak için gerekli eklenti
CREATE EXTENSION IF NOT EXISTS "citext"; -- Büyük/küçük harf duyarsız, süper hızlı metin (email) araması için eklenti

-- 2. ÖZEL VERİ TİPLERİ (ENUMS)
CREATE TYPE user_role AS ENUM ('user', 'seller', 'admin'); -- Kullanıcı yetki seviyelerini belirlediğimiz sabit liste

-- 3. KULLANICILAR TABLOSU (USERS)
CREATE TABLE users (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(), -- Her kullanıcıya özel, tahmin edilemez şifreli kimlik numarası
    email CITEXT UNIQUE NOT NULL, -- Kullanıcının e-posta adresi (CITEXT: AHMET@gmail.com ile ahmet@gmail.com aynı sayılır)
    full_name VARCHAR(100), -- Kullanıcının ad ve soyadı bilgisi
    avatar_url TEXT, -- Profil fotoğrafının tutulduğu bulut (Storage) linki
    role user_role DEFAULT 'user', -- Sisteme kayıt olan herkes varsayılan olarak 'user' (normal müşteri) başlar
    auth_provider VARCHAR(50) DEFAULT 'email', -- Sisteme nereden kayıt oldu? (email, google, apple, facebook)
    provider_id VARCHAR(255) UNIQUE, -- Google/Apple gibi yerlerden gelen özel ID numarası
    password_hash TEXT, -- Eğer email ile kayıt olduysa, şifresinin kriptolanmış (kırılmaz) hali
    is_email_verified BOOLEAN DEFAULT FALSE, -- Email adresine giden kodu (OTP) doğru girdi mi?
    locked_until TIMESTAMP WITH TIME ZONE, -- Hacker saldırısı olursa hesabı şu saate kadar dondur (Brute-Force koruması)
    stripe_customer_id VARCHAR(255), -- Stripe (Ödeme) tarafındaki müşteri cüzdan kodu (Alışveriş için)
    stripe_account_id VARCHAR(255), -- Satıcı ise paranın yatacağı Stripe IBAN/Hesap kodu
    preferences JSONB DEFAULT '{}'::jsonb, -- Tema, dil, bildirim gibi mobil uygulama ayarlarının tutulduğu esnek depo
    is_active BOOLEAN DEFAULT TRUE, -- Hesap silinirse FALSE olur (Soft Delete), veriler gerçekten silinmez
    last_login_at TIMESTAMP WITH TIME ZONE, -- Sisteme en son ne zaman giriş yaptı?
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP, -- Hesabın oluşturulma (kayıt) tarihi
    updated_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP, -- Profilde yapılan en son değişikliğin tarihi
    
    -- AKILLI GÜVENLİK KURALI: 
    -- Eğer Google/Apple ile değil de normal email ile giriyorsa, şifre boş OLAMAZ!
    CONSTRAINT check_password_if_email CHECK (
        (auth_provider = 'email' AND password_hash IS NOT NULL) OR 
        (auth_provider != 'email')
    )
);

-- 4. KULLANICI OTURUMLARI TABLOSU (USER SESSIONS)
CREATE TABLE user_sessions (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(), -- Oturuma ait benzersiz ID
    user_id UUID REFERENCES users(id) ON DELETE CASCADE, -- Bu oturum hangi kullanıcıya ait? (Kullanıcı silinirse oturum da silinir)
    refresh_token TEXT NOT NULL, -- Kullanıcıyı her seferinde şifre girmekten kurtaran uzun yetki anahtarı
    device_id VARCHAR(255), -- Kullanıcının girdiği telefonun veya bilgisayarın benzersiz cihaz kodu
    ip_address INET, -- Güvenlik için kullanıcının girdiği internet IP adresi
    user_agent TEXT, -- Hangi tarayıcıdan (Chrome/Safari) veya işletim sisteminden (iOS/Android) giriyor?
    expires_at TIMESTAMP WITH TIME ZONE NOT NULL, -- Bu oturumun (token'ın) son kullanma tarihi (Örn: 30 gün sonra biter)
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP -- Bu oturumun açıldığı anın tarihi
);

-- 5. HATALI GİRİŞ DENEMELERİ TABLOSU (LOGIN ATTEMPTS)
CREATE TABLE login_attempts (
    email CITEXT PRIMARY KEY, -- Hangi e-posta adresine saldırı yapılıyor/deneniyor?
    attempt_count INT DEFAULT 1, -- Kaç kere yanlış şifre girildi?
    last_attempt_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP -- En son hatalı giriş denemesi ne zaman yapıldı?
);


-- BİSMİLLAH: CRAFTORA VERİTABANI KURULUMU - BÖLÜM 2

-- ==========================================
-- 1. İNDEKS KAVŞAKLARI (PERFORMANS VE HIZ)
-- Milyonlarca veri içinde aramaları milisaniyelere düşüren arama motorları
-- ==========================================

-- Sosyal medya ile giriş yapanları anında bulmak için B-Tree İndeksi
CREATE INDEX idx_users_provider_id ON users(provider_id);

-- Mobil JSON ayarlarında ("Karanlık mod açık mı?") süper hızlı arama yapmak için GIN İndeksi
CREATE INDEX idx_users_preferences ON users USING GIN (preferences);

-- Bir kullanıcının açık olan oturumlarını şıp diye bulmak için
CREATE INDEX idx_user_sessions_user_id ON user_sessions(user_id);

-- Gelen Refresh Token'ın veritabanında olup olmadığını saliselik sürede doğrulamak için
CREATE INDEX idx_user_sessions_token ON user_sessions(refresh_token);


-- ==========================================
-- 2. TETİKLEYİCİLER (OTOMASYON - TRIGGERS)
-- Geliştirici hata yapsa bile veritabanının kendi kendini düzeltmesini sağlayan robotlar
-- ==========================================

-- Önce bir "Tarih Güncelleyen Robot (Fonksiyon)" üretiyoruz
CREATE OR REPLACE FUNCTION update_updated_at_column()
RETURNS TRIGGER AS $$
BEGIN
    NEW.updated_at = CURRENT_TIMESTAMP; -- Yeni verinin updated_at sütununu şu anki saat yap
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

-- Şimdi bu robotu 'users' tablosuna bağlıyoruz: "Her UPDATE işleminden hemen ÖNCE bu robotu çalıştır"
CREATE TRIGGER set_users_updated_at
BEFORE UPDATE ON users
FOR EACH ROW
EXECUTE FUNCTION update_updated_at_column();


-- ==========================================
-- 3. SATIR BAZLI GÜVENLİK (RLS - ROW LEVEL SECURITY)
-- Hackerları veritabanı kapısında durduran çelik yeleğimiz
-- ==========================================

-- Tablolarda RLS kalkanını aktif ediyoruz
ALTER TABLE users ENABLE ROW LEVEL SECURITY;
ALTER TABLE user_sessions ENABLE ROW LEVEL SECURITY;

-- KURAL 1: Kullanıcı Profillerini Görme (SELECT)
-- Herkes (sisteme giriş yapmamış anonim biri dahil) aktif kullanıcıların profilini görebilir
CREATE POLICY "Aktif kullanıcıları herkes görebilir" 
ON users FOR SELECT 
USING (is_active = TRUE);

-- KURAL 2: Profil Güncelleme (UPDATE)
-- (Not: Backend kodumuzda, sisteme giriş yapan kişinin ID'sini 'app.current_user_id' adında bir veritabanı değişkenine atayacağız)
-- Kullanıcı SADECE kendi satırındaki verileri (kendi ID'si eşleşiyorsa) değiştirebilir
CREATE POLICY "Kullanıcı sadece kendi profilini güncelleyebilir" 
ON users FOR UPDATE 
USING (id = current_setting('app.current_user_id', true)::uuid);

-- KURAL 3: Oturumları Görme ve Silme (SESSION GİZLİLİĞİ)
-- Oturumlar (Token'lar) aşırı gizlidir. Sadece sahibi kendi token'ını görebilir ve silebilir (Çıkış yapma)
CREATE POLICY "Kullanıcı sadece kendi oturumlarını görebilir" 
ON user_sessions FOR SELECT 
USING (user_id = current_setting('app.current_user_id', true)::uuid);

CREATE POLICY "Kullanıcı sadece kendi oturumlarını silebilir" 
ON user_sessions FOR DELETE 
USING (user_id = current_setting('app.current_user_id', true)::uuid);



SELECT full_name, created_at, updated_at FROM users;






-- CRAFTORA VERİTABANI KURULUMU - BÖLÜM 3 (MAĞAZA EKOSİSTEMİ)

-- 1. MAĞAZALAR TABLOSU (SHOPS)
CREATE TABLE shops (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(), -- Mağaza kimlik numarası
    user_id UUID UNIQUE NOT NULL REFERENCES users(id) ON DELETE CASCADE, -- Mağaza sahibi (1 kullanıcı = 1 mağaza kuralı UNIQUE ile sağlandı)
    shop_name VARCHAR(100) NOT NULL, -- Mağazanın görünen adı
    slug CITEXT UNIQUE NOT NULL, -- URL adresi (Örn: craftora.com/magza-adi). CITEXT sayesinde büyük/küçük harf duyarsız ve hızlıdır.
    external_url VARCHAR(255), -- Varsa harici web sitesi linki
    short_description VARCHAR(255), -- Mağaza kartlarında görünecek kısa özet
    description TEXT, -- Mağaza ana açıklama metni
    about_content TEXT, -- HTML destekli zengin "Hakkımızda" içeriği
    social_links JSONB DEFAULT '{}'::jsonb, -- Instagram, TikTok vb. linklerin tutulduğu esnek JSON deposu
    logo_url TEXT, -- Mağaza logosunun bulut linki
    banner_url TEXT, -- Mağaza kapak fotoğrafının bulut linki
    follower_count INT DEFAULT 0, -- PERFORMANS: Her seferinde sayım yapmamak için otomatik güncellenen takipçi sayısı
    rating DECIMAL(3,2) DEFAULT 0.0, -- PERFORMANS: Mağaza puan ortalaması (Örn: 4.85)
    is_verified BOOLEAN DEFAULT FALSE, -- CTO DOKUNUŞU: Mavi Tik (Onaylı Mağaza) durumu
    is_active BOOLEAN DEFAULT TRUE, -- Mağaza donduruldu mu?
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP, -- Kuruluş tarihi
    updated_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP -- Son düzenleme tarihi
);

-- 2. ABONELİKLER TABLOSU (SUBSCRIPTIONS)
CREATE TABLE subscriptions (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    shop_id UUID NOT NULL REFERENCES shops(id) ON DELETE CASCADE, -- Takip edilen mağaza
    user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE, -- Takip eden kullanıcı
    wants_notifications BOOLEAN DEFAULT TRUE, -- CTO DOKUNUŞU: Zil butonu (Yeni ürün bildirimi gelsin mi?)
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    
    -- Bir kullanıcı bir mağazayı sadece bir kez takip edebilir:
    CONSTRAINT unique_subscription UNIQUE (shop_id, user_id)
);

-- 3. MAĞAZA ZİYARETLERİ TABLOSU (SHOP_VISITS)
CREATE TABLE shop_visits (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    shop_id UUID NOT NULL REFERENCES shops(id) ON DELETE CASCADE,
    user_id UUID REFERENCES users(id) ON DELETE SET NULL, -- Üye olan ziyaretçi (Nullable: Üye olmayanlar için boş kalabilir)
    ip_address INET, -- CTO DOKUNUŞU: Üye olmayan anonim ziyaretçileri IP üzerinden takip etmek için
    visited_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP -- Ziyaret saati
);


-- 4. İNDEKS KAVŞAKLARI (HIZ)
CREATE INDEX idx_shops_slug ON shops(slug); -- Mağaza URL aramalarını ışık hızına çıkarır
CREATE INDEX idx_shop_visits_composite ON shop_visits(shop_id, visited_at); -- Satıcı paneli grafiklerini hızlandırır
-- Kullanıcıların arama çubuğunda mağaza adıyla arama yapmasını hızlandırmak için:
CREATE INDEX idx_shops_name ON shops(shop_name);

-- 5. OTOMATİK ABONE SAYACI (TRIGGER FUNCTION)
CREATE OR REPLACE FUNCTION sync_follower_count()
RETURNS TRIGGER AS $$
BEGIN
    IF (TG_OP = 'INSERT') THEN
        UPDATE shops SET follower_count = follower_count + 1 WHERE id = NEW.shop_id;
    ELSIF (TG_OP = 'DELETE') THEN
        UPDATE shops SET follower_count = follower_count - 1 WHERE id = OLD.shop_id;
    END IF;
    RETURN NULL;
END;
$$ LANGUAGE plpgsql;

-- Takip etme/çıkma anında sayacı çalıştır
CREATE TRIGGER trg_sync_followers
AFTER INSERT OR DELETE ON subscriptions
FOR EACH ROW EXECUTE FUNCTION sync_follower_count();

-- Mağaza updated_at tetikleyicisi
CREATE TRIGGER set_shops_updated_at
BEFORE UPDATE ON shops
FOR EACH ROW EXECUTE FUNCTION update_updated_at_column();



-- 6. GÜVENLİK KALKANLARI (RLS)
ALTER TABLE shops ENABLE ROW LEVEL SECURITY;
ALTER TABLE subscriptions ENABLE ROW LEVEL SECURITY;
ALTER TABLE shop_visits ENABLE ROW LEVEL SECURITY;

-- Mağazaları herkes görebilir ama sadece sahibi düzenleyebilir
CREATE POLICY "Aktif mağazalar herkese açıktır" ON shops FOR SELECT USING (is_active = TRUE);
CREATE POLICY "Mağaza sahibi dükkanını yönetebilir" ON shops FOR UPDATE 
USING (user_id = current_setting('app.current_user_id', true)::uuid);

-- Abonelik ve Ziyaret gizliliği: Sadece mağaza sahibi görebilir
CREATE POLICY "Satıcı kendi abonelerini görebilir" ON subscriptions FOR SELECT 
USING (shop_id IN (SELECT id FROM shops WHERE user_id = current_setting('app.current_user_id', true)::uuid));

CREATE POLICY "Satıcı kendi trafiğini görebilir" ON shop_visits FOR SELECT 
USING (shop_id IN (SELECT id FROM shops WHERE user_id = current_setting('app.current_user_id', true)::uuid));




-- SADECE YARIŞMALAR TABLOSUNU SİSTEME BAĞLAMA YAMASI (İzole adayı kurtarıyoruz)
ALTER TABLE contests
ADD COLUMN created_by UUID REFERENCES users(id) ON DELETE SET NULL;




-- CRAFTORA VERİTABANI KURULUMU - BÖLÜM 4 (ÜRÜNLER VE KURSLAR)

-- 1. YENİ VERİ TİPLERİ (ENUMS)
CREATE TYPE product_type AS ENUM ('digital_file', 'course');
CREATE TYPE media_status AS ENUM ('processing', 'ready', 'failed'); -- Videolar işlenirken bozuk görünmesin diye

-- 2. ANA ÜRÜNLER TABLOSU (PRODUCTS)
CREATE TABLE products (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    shop_id UUID NOT NULL REFERENCES shops(id) ON DELETE CASCADE,
    type product_type DEFAULT 'digital_file',
    title VARCHAR(255) NOT NULL,
    description TEXT,
    metadata JSONB DEFAULT '{}'::jsonb, -- CTO DOKUNUŞU: E-kitap sayfası, 3D model formatı gibi sınırsız özellikleri buraya gömeceğiz
    price DECIMAL(10,2) NOT NULL,
    currency VARCHAR(3) DEFAULT 'USD',
    cover_image_url TEXT,
    file_url TEXT, -- Dijital dosya ise indirme linki (Kurs ise NULL kalır)
    rating_average DECIMAL(3,2) DEFAULT 0.0, -- OTOPİLOT: Müşteri ana sayfada gezerken hesap yapmakla uğraşmayacak
    review_count INT DEFAULT 0, -- OTOPİLOT: Toplam yorum sayısı
    sales_count INT DEFAULT 0, -- Çok satanları bulmak için
    is_active BOOLEAN DEFAULT TRUE, -- Satıcı ürünü silse bile kütüphaneler bozulmasın diye Soft Delete yapıyoruz
    is_featured BOOLEAN DEFAULT FALSE,
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT check_price_positive CHECK (price >= 0) -- Fiyat asla eksi olamaz kalkanı!
);


-- 3. KURS BÖLÜMLERİ (Örn: C++ Döngüler)
CREATE TABLE course_sections (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    product_id UUID NOT NULL REFERENCES products(id) ON DELETE CASCADE,
    title VARCHAR(255) NOT NULL,
    sort_order INT NOT NULL, -- Uygulamada hangi sırada görünecek? (1, 2, 3)
    
    UNIQUE(product_id, sort_order) -- Aynı kurs içinde aynı sıra numarası yanlışlıkla girilmesin
);

-- 4. KURS DERSLERİ / VİDEOLARI (Örn: For Döngüsü)
CREATE TABLE course_lessons (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    section_id UUID NOT NULL REFERENCES course_sections(id) ON DELETE CASCADE,
    title VARCHAR(255) NOT NULL,
    video_url TEXT,
    document_url TEXT, -- Varsa ders notu (PDF)
    duration_seconds INT DEFAULT 0,
    is_free_preview BOOLEAN DEFAULT FALSE, -- Ücretsiz tanıtım videosu mu?
    sort_order INT NOT NULL,
    status media_status DEFAULT 'ready', -- Video işlenme durumu
    
    UNIQUE(section_id, sort_order)
);


-- 5. DEĞERLENDİRMELER (Yıldız ve Yorum - Kesin Kurallı)
CREATE TABLE reviews (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    product_id UUID NOT NULL REFERENCES products(id) ON DELETE CASCADE,
    user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    rating INT NOT NULL,
    comment TEXT,
    seller_reply TEXT, -- Satıcının tek bir yanıt hakkı var (Uzatılamaz)
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    
    CONSTRAINT check_rating_range CHECK (rating >= 1 AND rating <= 5), -- Yıldız 1-5 arası olmak ZORUNDA
    CONSTRAINT unique_user_review UNIQUE (product_id, user_id) -- 1 Kullanıcı ürüne SADECE 1 KERE puan verebilir
);

-- 6. SORU VE CEVAP (Kullanıcı ve Satıcı Karşılıklı Sohbeti)
CREATE TABLE product_qa (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    product_id UUID NOT NULL REFERENCES products(id) ON DELETE CASCADE,
    user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    parent_id UUID REFERENCES product_qa(id) ON DELETE CASCADE, -- Eğer yanıtsa hangi mesaja yanıt?
    message TEXT NOT NULL,
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);




-- =========================================================================
-- CRAFTORA VERİTABANI KURULUMU - BÖLÜM 5 (MEDYA, REELS VE OYUNLAŞTIRMA)
-- =========================================================================

-- -------------------------------------------------------------------------
-- 1. MEDYA VE ETKİLEŞİM TABLOLARI (SOSYAL MEDYA MOTORU)
-- -------------------------------------------------------------------------

-- REELS VİDEOLARI
CREATE TABLE media (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    shop_id UUID NOT NULL REFERENCES shops(id) ON DELETE CASCADE,
    product_id UUID REFERENCES products(id) ON DELETE SET NULL, -- Videoda satılan ürün
    video_url TEXT NOT NULL,
    thumbnail_url TEXT,
    view_count INT DEFAULT 0, 
    like_count INT DEFAULT 0, 
    save_count INT DEFAULT 0, 
    comment_count INT DEFAULT 0, -- CTO DOKUNUŞU: Yorum sayacı
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    is_active BOOLEAN DEFAULT TRUE
);

-- REELS BEĞENİLERİ
CREATE TABLE media_likes (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    media_id UUID NOT NULL REFERENCES media(id) ON DELETE CASCADE,
    user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    UNIQUE(media_id, user_id) 
);

-- REELS KAYDETMELERİ
CREATE TABLE media_saves (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    media_id UUID NOT NULL REFERENCES media(id) ON DELETE CASCADE,
    user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    UNIQUE(media_id, user_id)
);

-- REELS YORUMLARI (CTO DOKUNUŞU)
CREATE TABLE media_comments (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    media_id UUID NOT NULL REFERENCES media(id) ON DELETE CASCADE,
    user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    comment_text TEXT NOT NULL,
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);

-- İZLEME GEÇMİŞİ (Günlük Puan Limiti ve Algoritma İçin)
CREATE TABLE media_watch_history (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    media_id UUID NOT NULL REFERENCES media(id) ON DELETE CASCADE,
    watched_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    is_point_earned BOOLEAN DEFAULT FALSE,
    UNIQUE(user_id, media_id) -- Keşfet'te aynı video bir daha çıkmasın diye
);

-- -------------------------------------------------------------------------
-- 2. OYUNLAŞTIRMA VE LİDERLİK TABLOLARI (GAMIFICATION)
-- -------------------------------------------------------------------------

-- KULLANICI PUAN CÜZDANI
CREATE TABLE user_points (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    user_id UUID UNIQUE NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    total_points DECIMAL(12,2) DEFAULT 0.0, 
    current_rank INT DEFAULT 0, 
    current_streak INT DEFAULT 0, -- CTO DOKUNUŞU: Kaç gündür üst üste giriyor (Ateş serisi)
    updated_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);

-- PUAN KAYIT DEFTERİ (Geçmiş)
CREATE TABLE point_logs (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    action_type VARCHAR(50) NOT NULL, 
    points_earned DECIMAL(10,2) NOT NULL,
    reference_id UUID, 
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);

-- YARIŞMALAR VE SONUÇLAR (Senin yakaladığın efsane köprü!)
CREATE TABLE contests (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    title VARCHAR(255) NOT NULL,
    start_date TIMESTAMP WITH TIME ZONE NOT NULL,
    end_date TIMESTAMP WITH TIME ZONE NOT NULL,
    prize_pool TEXT,
    is_active BOOLEAN DEFAULT TRUE
);

CREATE TABLE contest_results (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    contest_id UUID NOT NULL REFERENCES contests(id) ON DELETE CASCADE,
    user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    final_rank INT,
    total_score DECIMAL(12,2),
    reward_claimed BOOLEAN DEFAULT FALSE,
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    UNIQUE(contest_id, user_id)
);

-- -------------------------------------------------------------------------
-- 3. OTOPİLOT ROBOTLARI VE İNDEKS KAVŞAKLARI
-- -------------------------------------------------------------------------

CREATE INDEX idx_media_shop ON media(shop_id);
CREATE INDEX idx_media_product ON media(product_id);
CREATE INDEX idx_point_logs_user_date ON point_logs(user_id, created_at);

-- OTOPİLOT 1: MEDYA SAYAÇLARI (Like, Save ve Yorumları Otomatik Sayar)
CREATE OR REPLACE FUNCTION sync_media_counters() RETURNS TRIGGER AS $$
BEGIN
    IF TG_TABLE_NAME = 'media_likes' THEN
        IF TG_OP = 'INSERT' THEN UPDATE media SET like_count = like_count + 1 WHERE id = NEW.media_id;
        ELSIF TG_OP = 'DELETE' THEN UPDATE media SET like_count = like_count - 1 WHERE id = OLD.media_id; END IF;
    ELSIF TG_TABLE_NAME = 'media_saves' THEN
        IF TG_OP = 'INSERT' THEN UPDATE media SET save_count = save_count + 1 WHERE id = NEW.media_id;
        ELSIF TG_OP = 'DELETE' THEN UPDATE media SET save_count = save_count - 1 WHERE id = OLD.media_id; END IF;
    ELSIF TG_TABLE_NAME = 'media_comments' THEN
        IF TG_OP = 'INSERT' THEN UPDATE media SET comment_count = comment_count + 1 WHERE id = NEW.media_id;
        ELSIF TG_OP = 'DELETE' THEN UPDATE media SET comment_count = comment_count - 1 WHERE id = OLD.media_id; END IF;
    END IF;
    RETURN NULL;
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER trg_media_like_counter AFTER INSERT OR DELETE ON media_likes FOR EACH ROW EXECUTE FUNCTION sync_media_counters();
CREATE TRIGGER trg_media_save_counter AFTER INSERT OR DELETE ON media_saves FOR EACH ROW EXECUTE FUNCTION sync_media_counters();
CREATE TRIGGER trg_media_comment_counter AFTER INSERT OR DELETE ON media_comments FOR EACH ROW EXECUTE FUNCTION sync_media_counters();

-- OTOPİLOT 2: SATICI PUAN ROBOTU (Like Aldıkça 0.5 Kazanır, UPSERT mantığıyla)
CREATE OR REPLACE FUNCTION award_seller_points() RETURNS TRIGGER AS $$
DECLARE v_seller_id UUID;
BEGIN
    SELECT s.user_id INTO v_seller_id FROM media m JOIN shops s ON m.shop_id = s.id WHERE m.id = NEW.media_id;
    
    INSERT INTO point_logs (user_id, action_type, points_earned, reference_id) VALUES (v_seller_id, 'receive_like', 0.5, NEW.media_id);
    
    -- ON CONFLICT: Cüzdanı yoksa yarat, varsa üstüne ekle (UPSERT)
    INSERT INTO user_points (user_id, total_points) VALUES (v_seller_id, 0.5)
    ON CONFLICT (user_id) DO UPDATE SET total_points = user_points.total_points + 0.5, updated_at = CURRENT_TIMESTAMP;
    
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;
CREATE TRIGGER trg_points_on_like AFTER INSERT ON media_likes FOR EACH ROW EXECUTE FUNCTION award_seller_points();

-- OTOPİLOT 3: İZLEYİCİ PUAN ROBOTU (Günlük Limit: 120)
CREATE OR REPLACE FUNCTION award_viewer_points() RETURNS TRIGGER AS $$
DECLARE v_daily_points DECIMAL;
BEGIN
    SELECT COALESCE(SUM(points_earned), 0) INTO v_daily_points FROM point_logs 
    WHERE user_id = NEW.user_id AND action_type = 'watch_reels' AND created_at::date = CURRENT_DATE;

    IF v_daily_points < 120 THEN
        INSERT INTO point_logs (user_id, action_type, points_earned, reference_id) VALUES (NEW.user_id, 'watch_reels', 1.0, NEW.media_id);
        
        INSERT INTO user_points (user_id, total_points) VALUES (NEW.user_id, 1.0)
        ON CONFLICT (user_id) DO UPDATE SET total_points = user_points.total_points + 1.0, updated_at = CURRENT_TIMESTAMP;
        
        NEW.is_point_earned := TRUE;
    END IF;
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;
CREATE TRIGGER trg_points_on_watch BEFORE INSERT ON media_watch_history FOR EACH ROW EXECUTE FUNCTION award_viewer_points();


-- -------------------------------------------------------------------------
-- 4. ÇELİK YELEKLER (RLS POLICIES) - SENİN YAKALADIĞIN EKSİK!
-- -------------------------------------------------------------------------
ALTER TABLE media ENABLE ROW LEVEL SECURITY;
ALTER TABLE media_likes ENABLE ROW LEVEL SECURITY;
ALTER TABLE media_saves ENABLE ROW LEVEL SECURITY;
ALTER TABLE media_comments ENABLE ROW LEVEL SECURITY;
ALTER TABLE media_watch_history ENABLE ROW LEVEL SECURITY;
ALTER TABLE user_points ENABLE ROW LEVEL SECURITY;
ALTER TABLE point_logs ENABLE ROW LEVEL SECURITY;

-- MEDYA (REELS)
CREATE POLICY "Aktif videolar herkese açık" ON media FOR SELECT USING (is_active = TRUE);
CREATE POLICY "Satıcı kendi videosunu yönetebilir" ON media FOR ALL 
USING (shop_id IN (SELECT id FROM shops WHERE user_id = current_setting('app.current_user_id', true)::uuid));

-- BEĞENİ VE KAYDETMELER (GİZLİLİK)
CREATE POLICY "Herkes kendi beğeni/kayıtlarını görebilir" ON media_likes FOR SELECT USING (user_id = current_setting('app.current_user_id', true)::uuid);
CREATE POLICY "Herkes kendi beğeni/kayıtlarını yapabilir" ON media_likes FOR ALL USING (user_id = current_setting('app.current_user_id', true)::uuid);

CREATE POLICY "Herkes kendi kaydettiklerini görebilir" ON media_saves FOR SELECT USING (user_id = current_setting('app.current_user_id', true)::uuid);
CREATE POLICY "Herkes kendi kaydettiklerini yönetebilir" ON media_saves FOR ALL USING (user_id = current_setting('app.current_user_id', true)::uuid);

-- YORUMLAR
CREATE POLICY "Yorumları herkes okuyabilir" ON media_comments FOR SELECT USING (true);
CREATE POLICY "Kullanıcı kendi yorumunu silebilir/düzenleyebilir" ON media_comments FOR ALL USING (user_id = current_setting('app.current_user_id', true)::uuid);

-- OYUNLAŞTIRMA VE LİDERLİK TABLOSU GÜVENLİĞİ
CREATE POLICY "Liderlik tablosunu herkes görebilir" ON user_points FOR SELECT USING (true);
-- DİKKAT: user_points tablosuna UPDATE kuralı yazmıyoruz! Çünkü puanları API değil, sadece veritabanı Trigger'ları (Robotlar) verebilir. Hacker puanını artıramaz!

CREATE POLICY "Kullanıcı sadece kendi puan geçmişini görebilir" ON point_logs FOR SELECT USING (user_id = current_setting('app.current_user_id', true)::uuid);





-- =========================================================================
-- CRAFTORA VERİTABANI KURULUMU - BÖLÜM 6 (SİPARİŞLER VE FİNANS)
-- =========================================================================

-- 1. SİPARİŞ DURUMLARI (ENUM)
CREATE TYPE order_status AS ENUM ('pending', 'completed', 'failed', 'refunded');

-- 2. SİPARİŞLER TABLOSU (ORDERS)
CREATE TABLE orders (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    buyer_id UUID NOT NULL REFERENCES users(id) ON DELETE RESTRICT, -- MÜHENDİSLİK: Kullanıcı silinse bile fatura silinmez!
    product_id UUID NOT NULL REFERENCES products(id) ON DELETE RESTRICT, -- Ürün silinse bile sipariş geçmişi kalır!
    shop_id UUID NOT NULL REFERENCES shops(id) ON DELETE RESTRICT,
    order_number VARCHAR(50) UNIQUE NOT NULL, -- Örn: CRAFT-2026-XYZ123
    
    -- FİNANSAL BÖLÜNME (MUHASEBE)
    amount DECIMAL(10,2) NOT NULL, -- Müşterinin ödediği toplam para (Örn: 100.00)
    currency VARCHAR(3) DEFAULT 'USD',
    platform_fee DECIMAL(10,2) DEFAULT 0.00, -- Craftora'nın cebine giren komisyon (Örn: 10.00)
    seller_earnings DECIMAL(10,2) DEFAULT 0.00, -- Satıcının Stripe hesabına yatacak para (Örn: 90.00)
    
    status order_status DEFAULT 'pending',
    stripe_payment_id VARCHAR(255), -- İade ve iptaller için banka işlem numarası
    invoice_pdf_url TEXT, -- Kesilen e-faturanın PDF linki
    
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    
    -- GÜVENLİK KALKANLARI
    CONSTRAINT check_amount_positive CHECK (amount >= 0),
    CONSTRAINT check_fee_logic CHECK (platform_fee + seller_earnings = amount) -- Toplam tutar, kesintilerle eşleşmek ZORUNDA!
);

-- 3. İNDEKS KAVŞAKLARI (PERFORMANS VE ARAMA HIZI)
CREATE INDEX idx_orders_buyer ON orders(buyer_id); -- Müşterinin "Siparişlerim" sayfasını hızlandırır
CREATE INDEX idx_orders_shop ON orders(shop_id); -- Satıcının "Gelen Siparişler" tablosunu hızlandırır
CREATE INDEX idx_orders_number ON orders(order_number); -- Müşteri hizmetlerinin fatura no ile arama yapması için
CREATE INDEX idx_orders_status ON orders(status);

-- 4. OTOPİLOT ROBOTLARI (OTOMASYON)

-- Saat Güncelleyici
CREATE TRIGGER set_orders_updated_at
BEFORE UPDATE ON orders
FOR EACH ROW EXECUTE FUNCTION update_updated_at_column();

-- EFSANE ROBOT: Sipariş "Completed" (Tamamlandı) olunca çalışır!
CREATE OR REPLACE FUNCTION process_completed_order()
RETURNS TRIGGER AS $$
DECLARE 
    v_seller_id UUID;
BEGIN
    -- Eğer sipariş durumu 'completed' olarak güncellendiyse (veya direkt eklendiyse)
    IF (NEW.status = 'completed' AND (TG_OP = 'INSERT' OR OLD.status != 'completed')) THEN
        
        -- 1. Ürünün satış sayacını (sales_count) 1 artır
        UPDATE products SET sales_count = sales_count + 1 WHERE id = NEW.product_id;
        
        -- 2. Satıcıyı bul
        SELECT user_id INTO v_seller_id FROM shops WHERE id = NEW.shop_id;
        
        -- 3. Satıcıya Oyunlaştırma Modülünden 20 PUAN kazandır! (make_sale aksiyonu)
        INSERT INTO point_logs (user_id, action_type, points_earned, reference_id) 
        VALUES (v_seller_id, 'make_sale', 20.0, NEW.id);
        
        UPDATE user_points SET total_points = total_points + 20.0, updated_at = CURRENT_TIMESTAMP 
        WHERE user_id = v_seller_id;
        
    END IF;
    
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

-- Robotu Siparişler Tablosuna Bağlayalım
CREATE TRIGGER trg_on_order_completed
AFTER INSERT OR UPDATE ON orders
FOR EACH ROW EXECUTE FUNCTION process_completed_order();


-- 5. ÇELİK YELEKLER (RLS - FİNANSAL GİZLİLİK)
ALTER TABLE orders ENABLE ROW LEVEL SECURITY;

-- KURAL 1: Alıcı (Müşteri) SADECE kendi verdiği siparişleri ve faturalarını görebilir
CREATE POLICY "Alıcılar kendi siparişlerini görebilir" ON orders FOR SELECT 
USING (buyer_id = current_setting('app.current_user_id', true)::uuid);

-- KURAL 2: Satıcı SADECE kendi dükkanına gelen siparişleri görebilir
CREATE POLICY "Satıcılar kendi mağaza siparişlerini görebilir" ON orders FOR SELECT 
USING (shop_id IN (SELECT id FROM shops WHERE user_id = current_setting('app.current_user_id', true)::uuid));

-- DİKKAT (CTO KURALI): Kullanıcılar (Alıcı veya Satıcı) sipariş silebilir veya durumunu değiştirebilir mi? ASLA!
-- RLS kalkanında INSERT, UPDATE ve DELETE kurallarını YAZMIYORUZ. 
-- Bu sayede sadece Backend Sunucumuz (Stripe'dan ödeme onayı alınca) siparişi güncelleyebilir. Hacker fiyata veya duruma müdahale edemez.




-- 1. ÖDEME DURUMLARI (ENUM)
CREATE TYPE payment_status_type AS ENUM ('processing', 'succeeded', 'failed', 'refunded');

-- 2. ANA ÖDEMELER TABLOSU (PAYMENTS)
CREATE TABLE payments (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    order_id UUID UNIQUE NOT NULL REFERENCES orders(id) ON DELETE RESTRICT, -- UNIQUE: Bir siparişin SADECE BİR ödeme kaydı olur!
    payment_provider VARCHAR(50) NOT NULL, -- 'stripe', 'iyzico', 'paypal'
    provider_transaction_id VARCHAR(255) UNIQUE, -- Bankanın verdiği efsanevi, kopyalanamaz dekont/işlem numarası
    
    gross_amount DECIMAL(10,2) NOT NULL, -- Karttan çekilen brüt para
    platform_fee_amount DECIMAL(10,2) NOT NULL, -- Banka+Craftora kesintisi
    net_earnings DECIMAL(10,2) NOT NULL, -- Satıcının hesabına yatacak net para
    
    status payment_status_type DEFAULT 'processing',
    error_message TEXT, -- Eğer işlem failed olursa bankanın gönderdiği hata kodu ("Bakiye yetersiz" vb.)
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    
    -- GÜVENLİK KALKANLARI
    CONSTRAINT check_gross_positive CHECK (gross_amount >= 0),
    CONSTRAINT check_payment_math CHECK (gross_amount = platform_fee_amount + net_earnings) -- Muhasebe matematiği ASLA şaşamaz!
);

-- 3. İNDEKS KAVŞAKLARI (PERFORMANS)
CREATE INDEX idx_payments_transaction_id ON payments(provider_transaction_id); -- Bankadan gelen Webhook'ları salisede bulmak için
CREATE INDEX idx_payments_status ON payments(status);

-- 4. OTOPİLOT ROBOTLARI (DOMİNO ETKİSİ)

-- Saat Güncelleyici
CREATE TRIGGER set_payments_updated_at
BEFORE UPDATE ON payments
FOR EACH ROW EXECUTE FUNCTION update_updated_at_column();

-- DOMİNO ROBOTU: Ödeme başarılı olursa, Siparişi de Tamamla!
CREATE OR REPLACE FUNCTION sync_order_status_from_payment()
RETURNS TRIGGER AS $$
BEGIN
    -- Eğer banka ödemesi 'succeeded' olduysa
    IF (NEW.status = 'succeeded' AND (TG_OP = 'INSERT' OR OLD.status != 'succeeded')) THEN
        
        -- Gidip Orders (Sipariş) tablosundaki durumu da 'completed' yapıyoruz.
        -- DİKKAT: Bu UPDATE işlemi, bir önceki aşamada yazdığımız Puan Dağıtma robotunu tetikleyecek!
        UPDATE orders SET status = 'completed' WHERE id = NEW.order_id;
        
    -- Eğer banka 'refunded' (İade) dediyse, siparişi de iptal et
    ELSIF (NEW.status = 'refunded' AND OLD.status != 'refunded') THEN
        UPDATE orders SET status = 'refunded' WHERE id = NEW.order_id;
    END IF;
    
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER trg_sync_order_on_payment
AFTER INSERT OR UPDATE ON payments
FOR EACH ROW EXECUTE FUNCTION sync_order_status_from_payment();


-- 5. ÇELİK YELEKLER (RLS - FİNANSAL GİZLİLİK)
ALTER TABLE payments ENABLE ROW LEVEL SECURITY;

-- KURAL 1: Alıcı sadece KENDİ siparişine bağlı ödeme dekontunu görebilir
CREATE POLICY "Alıcılar dekontunu görebilir" ON payments FOR SELECT 
USING (order_id IN (SELECT id FROM orders WHERE buyer_id = current_setting('app.current_user_id', true)::uuid));

-- KURAL 2: Satıcı sadece KENDİ dükkanına ait satışların ödeme/komisyon dökümünü görebilir
CREATE POLICY "Satıcılar kendi gelir dökümlerini görebilir" ON payments FOR SELECT 
USING (order_id IN (SELECT id FROM orders WHERE shop_id IN (SELECT id FROM shops WHERE user_id = current_setting('app.current_user_id', true)::uuid)));

-- DİKKAT: INSERT, UPDATE, DELETE KESİNLİKLE YOK! Ödeme durumunu sadece Stripe webhook'larından gelen veriyi işleyen arka uç (Backend) kodumuz yapabilir.


INSERT INTO payments (order_id, payment_provider, provider_transaction_id, gross_amount, platform_fee_amount, net_earnings, status)
VALUES (
    (SELECT id FROM orders WHERE order_number = 'PENDING-ORD-002'),
    'stripe',
    'ch_basarili_islem_123',
    100.00,
    10.00,
    90.00,
    'succeeded' -- İŞTE BU KELİME DOMİNOYI BAŞLATACAK!
);


SELECT order_number, status FROM orders WHERE order_number = 'PENDING-ORD-002';

-- SONUÇ 2: C++ Kursunun satış sayısı tekrar artmış mı?
SELECT title, sales_count FROM products WHERE title = 'Sıfırdan İleri Seviye C++ Eğitimi';

-- SONUÇ 3: Ahmet'in cüzdanına ekstra 20 puan daha (Toplam 40.50) gelmiş mi?
SELECT total_points FROM user_points WHERE user_id = (SELECT id FROM users WHERE email = 'ahmet.yilmaz@gmail.com');




-- =========================================================================
-- CRAFTORA VERİTABANI KURULUMU - BÖLÜM 7 (KÜTÜPHANE VE EĞİTİM)
-- =========================================================================

-- -------------------------------------------------------------------------
-- 1. TABLOLAR (MİMARİ)
-- -------------------------------------------------------------------------

-- KULLANICI KÜTÜPHANESİ (SATIN ALINANLAR)
CREATE TABLE user_library (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    product_id UUID NOT NULL REFERENCES products(id) ON DELETE CASCADE,
    purchased_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    last_accessed_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP, -- CTO DOKUNUŞU: Kaldığın yerden devam et!
    
    UNIQUE(user_id, product_id) -- Bir kullanıcı aynı ürüne iki kere sahip olamaz
);

-- DERS İLERLEMESİ (VİDEO İZLEME SÜRELERİ)
CREATE TABLE lesson_progress (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    lesson_id UUID NOT NULL REFERENCES course_lessons(id) ON DELETE CASCADE,
    is_completed BOOLEAN DEFAULT FALSE,
    watched_seconds INT DEFAULT 0,
    completed_at TIMESTAMP WITH TIME ZONE, -- Ne zaman bitirdi?
    updated_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    
    UNIQUE(user_id, lesson_id) -- Bir kullanıcı bir ders için sadece bir kayıt tutabilir
);

-- -------------------------------------------------------------------------
-- 2. İNDEKS KAVŞAKLARI (PERFORMANS)
-- -------------------------------------------------------------------------

CREATE INDEX idx_user_library_accessed ON user_library(user_id, last_accessed_at DESC); -- "Devam Et" rafını saniyede yükler
CREATE INDEX idx_lesson_progress_user ON lesson_progress(user_id, lesson_id);

-- -------------------------------------------------------------------------
-- 3. OTOPİLOT ROBOTLARI (OTOMATİK TESLİMAT VE PUAN)
-- -------------------------------------------------------------------------

-- ROBOT 1: Saat Güncelleyici
CREATE TRIGGER set_progress_updated_at
BEFORE UPDATE ON lesson_progress
FOR EACH ROW EXECUTE FUNCTION update_updated_at_column();

-- EFSANE ROBOT 2: OTOMATİK DİJİTAL TESLİMAT (Sipariş Onaylanınca Çalışır)
CREATE OR REPLACE FUNCTION deliver_product_to_library()
RETURNS TRIGGER AS $$
BEGIN
    -- Sipariş 'completed' statüsüne geçtiyse:
    IF (NEW.status = 'completed' AND (TG_OP = 'INSERT' OR OLD.status != 'completed')) THEN
        -- Ürünü alıcının kütüphanesine ekle (Eğer zaten varsa hata verme, sessizce geç: ON CONFLICT DO NOTHING)
        INSERT INTO user_library (user_id, product_id)
        VALUES (NEW.buyer_id, NEW.product_id)
        ON CONFLICT (user_id, product_id) DO NOTHING;
    END IF;
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER trg_auto_deliver_product
AFTER INSERT OR UPDATE ON orders
FOR EACH ROW EXECUTE FUNCTION deliver_product_to_library();


-- EFSANE ROBOT 3: ÖĞRENCİ PUAN SİSTEMİ (Ders Bitince 2 Puan Verir)
CREATE OR REPLACE FUNCTION reward_lesson_completion()
RETURNS TRIGGER AS $$
BEGIN
    -- Eğer ders ŞU AN tamamlandıysa (Önceden false idi, şimdi true olduysa)
    IF (NEW.is_completed = TRUE AND OLD.is_completed = FALSE) THEN
        
        -- Müşteriye 2 Puan ver (action_type: 'complete_lesson')
        INSERT INTO point_logs (user_id, action_type, points_earned, reference_id)
        VALUES (NEW.user_id, 'complete_lesson', 2.0, NEW.lesson_id);
        
        -- Cüzdanı güncelle (UPSERT - Cüzdanı yoksa yarat)
        INSERT INTO user_points (user_id, total_points) VALUES (NEW.user_id, 2.0)
        ON CONFLICT (user_id) DO UPDATE SET total_points = user_points.total_points + 2.0, updated_at = CURRENT_TIMESTAMP;
        
        -- Tamamlanma saatini şu anki saat yap
        NEW.completed_at = CURRENT_TIMESTAMP;
        
    END IF;
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

-- Bu robotu sadece UPDATE işleminde çalıştırıyoruz (Videoyu izledikçe güncellenecek çünkü)
CREATE TRIGGER trg_reward_on_lesson_complete
BEFORE UPDATE ON lesson_progress
FOR EACH ROW EXECUTE FUNCTION reward_lesson_completion();


-- -------------------------------------------------------------------------
-- 4. ÇELİK YELEKLER (RLS - KORSAN KALKANI)
-- -------------------------------------------------------------------------

ALTER TABLE user_library ENABLE ROW LEVEL SECURITY;
ALTER TABLE lesson_progress ENABLE ROW LEVEL SECURITY;

-- KÜTÜPHANE GÜVENLİĞİ: Kullanıcı KENDİ kütüphanesini görebilir. 
-- DİKKAT: INSERT veya DELETE yok! Ürünü sadece sistem (Orders tablosundaki Trigger) ekleyebilir.
CREATE POLICY "Kullanıcı kendi kütüphanesini görebilir" ON user_library FOR SELECT 
USING (user_id = current_setting('app.current_user_id', true)::uuid);

-- DERS İLERLEMESİ GÜVENLİĞİ
CREATE POLICY "Kullanıcı kendi ilerlemesini görebilir" ON lesson_progress FOR SELECT 
USING (user_id = current_setting('app.current_user_id', true)::uuid);

-- Kullanıcı sadece kendi ders ilerlemesini yaratabilir ve güncelleyebilir (İzlediği saniyeyi kaydetmek için)
CREATE POLICY "Kullanıcı kendi ilerlemesini güncelleyebilir" ON lesson_progress FOR ALL 
USING (user_id = current_setting('app.current_user_id', true)::uuid);




-- =========================================================================
-- CRAFTORA VERİTABANI KURULUMU - BÖLÜM 8 (SATICI ABONELİKLERİ / SAAS)
-- =========================================================================

-- 1. ABONELİK DURUMLARI (ENUM)
CREATE TYPE sub_status AS ENUM ('active', 'past_due', 'canceled', 'unpaid');

-- 2. SATICI ABONELİKLERİ TABLOSU
CREATE TABLE seller_subscriptions (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    shop_id UUID UNIQUE NOT NULL REFERENCES shops(id) ON DELETE CASCADE, -- Bir mağazanın tek abonelik kaydı olur
    stripe_subscription_id VARCHAR(255) UNIQUE, -- CTO DOKUNUŞU: Bankadaki (Stripe) otomatik çekim talimatının kodu
    
    status sub_status DEFAULT 'active',
    current_period_end TIMESTAMP WITH TIME ZONE NOT NULL, -- Bu ayki paketin bitiş tarihi
    grace_period_end TIMESTAMP WITH TIME ZONE, -- 7 Günlük ek süre (Fatura ödenmezse dükkanı hemen kapatmamak için)
    
    amount DECIMAL(10,2) DEFAULT 25.00, -- Aylık ücret
    currency VARCHAR(3) DEFAULT 'USD',
    
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    
    CONSTRAINT check_sub_amount_positive CHECK (amount >= 0)
);

-- 3. OTOPİLOT ROBOTU (SAAT GÜNCELLEYİCİ)
CREATE TRIGGER set_seller_sub_updated_at
BEFORE UPDATE ON seller_subscriptions
FOR EACH ROW EXECUTE FUNCTION update_updated_at_column();

-- 4. ÇELİK YELEK (RLS - FİNANSAL GİZLİLİK)
ALTER TABLE seller_subscriptions ENABLE ROW LEVEL SECURITY;

-- KURAL: Satıcı SADECE kendi dükkanının abonelik faturasını/durumunu görebilir.
-- DİKKAT: INSERT, UPDATE, DELETE yok! Aboneliği sadece Stripe'dan gelen Webhook (Backend) güncelleyebilir.
CREATE POLICY "Satıcılar kendi abonelik durumlarını görebilir" ON seller_subscriptions FOR SELECT 
USING (shop_id IN (SELECT id FROM shops WHERE user_id = current_setting('app.current_user_id', true)::uuid));


-- 1. Sütunun adındaki "Stripe" kelimesini atıp evrensel (Provider) ismine çeviriyoruz:
ALTER TABLE seller_subscriptions 
RENAME COLUMN stripe_subscription_id TO provider_subscription_id;

-- 2. Bu aboneliğin hangi bankadan (Iyzico mu, Stripe mı) yapıldığını bilmek için sağlayıcı sütununu ekliyoruz:
ALTER TABLE seller_subscriptions 
ADD COLUMN payment_provider VARCHAR(50) DEFAULT 'stripe'; -- Satıcının kaydolduğu pos firması (Örn: 'iyzico')






-- =========================================================================
-- CRAFTORA VERİTABANI KURULUMU - BÖLÜM 9 (AKILLI SEPET / CART ITEMS)
-- =========================================================================

-- 1. SEPET ÜRÜNLERİ TABLOSU
CREATE TABLE cart_items (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    product_id UUID NOT NULL REFERENCES products(id) ON DELETE CASCADE,
    quantity INT DEFAULT 1, 
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    
    -- GÜVENLİK VE MANTIK KALKANLARI
    CONSTRAINT check_quantity_positive CHECK (quantity > 0), -- Miktar eksi veya sıfır olamaz!
    UNIQUE(user_id, product_id) -- Aynı ürün sepete ikinci kez ayrı satır olarak eklenmesin
);

-- 2. İNDEKS (PERFORMANS)
CREATE INDEX idx_cart_items_user ON cart_items(user_id); -- Sepet sayfasını salisede açmak için

-- 3. OTOPİLOT ROBOTLARI 

-- Robot A: Saat Güncelleyici (Terk edilmiş sepetleri bulmak için çok kritik)
CREATE TRIGGER set_cart_updated_at
BEFORE UPDATE ON cart_items
FOR EACH ROW EXECUTE FUNCTION update_updated_at_column();

-- Robot B: ZEKİ MÜŞTERİ KORUMASI (Zaten sahip olunan ürünü sepete aldırtmaz!)
CREATE OR REPLACE FUNCTION prevent_duplicate_purchase()
RETURNS TRIGGER AS $$
BEGIN
    -- Kullanıcının kütüphanesinde bu ürün var mı diye kontrol et
    IF EXISTS (SELECT 1 FROM user_library WHERE user_id = NEW.user_id AND product_id = NEW.product_id) THEN
        RAISE EXCEPTION 'Bu ürün zaten kütüphanenizde mevcut!';
    END IF;
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER trg_check_already_owned
BEFORE INSERT OR UPDATE ON cart_items
FOR EACH ROW EXECUTE FUNCTION prevent_duplicate_purchase();

-- 4. ÇELİK YELEKLER (RLS - GÜVENLİK)
ALTER TABLE cart_items ENABLE ROW LEVEL SECURITY;

-- KURAL 1: Kullanıcı sadece KENDİ sepetindeki ürünleri görebilir
CREATE POLICY "Kullanıcılar kendi sepetini görebilir" ON cart_items FOR SELECT 
USING (user_id = current_setting('app.current_user_id', true)::uuid);

-- KURAL 2: Kullanıcı sadece KENDİ sepetine ürün ekleyebilir/çıkarabilir/miktar güncelleyebilir
CREATE POLICY "Kullanıcılar kendi sepetini yönetebilir" ON cart_items FOR ALL 
USING (user_id = current_setting('app.current_user_id', true)::uuid);


CREATE TABLE coupons (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    
    -- Hangi ürüne ait?
    product_id UUID NOT NULL REFERENCES products(id) ON DELETE CASCADE,
    -- Kuponu kim oluşturdu? (Güvenlik için)
    shop_id UUID NOT NULL REFERENCES shops(id) ON DELETE CASCADE,
    
    -- Kupon Kodu
    code VARCHAR(50) NOT NULL,
    
    -- İndirim Tipi
    discount_type VARCHAR(10) NOT NULL, -- 'percent' veya 'fixed'
    discount_value DECIMAL(10,2) NOT NULL, -- %20 için 20.00, 10$ için 10.00
    
    -- Kullanım Limiti
    max_uses INT DEFAULT NULL, -- NULL = sınırsız
    used_count INT DEFAULT 0,  -- Kaç kişi kullandı?
    
    -- Geçerlilik Tarihi
    starts_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    expires_at TIMESTAMP WITH TIME ZONE DEFAULT NULL, -- NULL = süresiz
    
    is_active BOOLEAN DEFAULT TRUE,
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    
    -- Güvenlik Kalkanlari
    CONSTRAINT check_discount_type CHECK (discount_type IN ('percent', 'fixed')),
    CONSTRAINT check_discount_value CHECK (discount_value > 0),
    CONSTRAINT check_percent_max CHECK (discount_type != 'percent' OR discount_value <= 100),
    CONSTRAINT unique_coupon_per_product UNIQUE (product_id, code) -- Aynı üründe aynı kod olamaz
);

-- Kişi başı 1 kez kullanım takibi
CREATE TABLE coupon_uses (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    coupon_id UUID NOT NULL REFERENCES coupons(id) ON DELETE CASCADE,
    user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    order_id UUID NOT NULL REFERENCES orders(id) ON DELETE CASCADE,
    used_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    
    UNIQUE(coupon_id, user_id) -- Kişi başı 1 kez!
);

-- İndeksler
CREATE INDEX idx_coupons_product ON coupons(product_id);
CREATE INDEX idx_coupons_code ON coupons(code);

-- Otopilot: Kullanıldıkça sayacı artır
CREATE OR REPLACE FUNCTION increment_coupon_usage()
RETURNS TRIGGER AS $$
BEGIN
    UPDATE coupons SET used_count = used_count + 1 WHERE id = NEW.coupon_id;
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER trg_increment_coupon_usage
AFTER INSERT ON coupon_uses
FOR EACH ROW EXECUTE FUNCTION increment_coupon_usage();

-- RLS
ALTER TABLE coupons ENABLE ROW LEVEL SECURITY;
ALTER TABLE coupon_uses ENABLE ROW LEVEL SECURITY;

-- Kuponları herkes görebilir (Sepette kod girerken)
CREATE POLICY "Aktif kuponlar herkese açık" ON coupons FOR SELECT 
USING (is_active = TRUE);

-- Sadece mağaza sahibi kendi ürününe kupon ekleyebilir
CREATE POLICY "Satıcı kendi kuponlarını yönetebilir" ON coupons FOR ALL 
USING (shop_id IN (
    SELECT id FROM shops 
    WHERE user_id = current_setting('app.current_user_id', true)::uuid
));

-- Kupon kullanım geçmişi sadece alıcıya özel
CREATE POLICY "Kullanıcı kendi kupon geçmişini görebilir" ON coupon_uses FOR SELECT 
USING (user_id = current_setting('app.current_user_id', true)::uuid);


CREATE TABLE notifications (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    
    -- Bildirim Tipi
    type VARCHAR(50) NOT NULL,
    -- 'sale_completed', 'new_follower', 'new_review', 
    -- 'new_question', 'media_liked', 'media_commented',
    -- 'contest_result', 'order_completed'
    
    -- Bildirim İçeriği
    title VARCHAR(255) NOT NULL,        -- "Yeni Satış! 🎉"
    body TEXT NOT NULL,                 -- "Ali, C++ Kursunu satın aldı"
    
    -- Hangi içeriğe ait? (Tıklayınca nereye gitsin?)
    reference_type VARCHAR(50),         -- 'order', 'media', 'product', 'shop', 'contest'
    reference_id UUID,                  -- İlgili kaydın ID'si
    
    -- Durum
    is_read BOOLEAN DEFAULT FALSE,
    read_at TIMESTAMP WITH TIME ZONE,
    
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    
    -- Güvenlik
    CONSTRAINT check_notification_type CHECK (type IN (
        'sale_completed', 'new_follower', 'new_review',
        'new_question', 'media_liked', 'media_commented',
        'contest_result', 'order_completed'
    ))
);

-- İndeksler
CREATE INDEX idx_notifications_user ON notifications(user_id, created_at DESC);
CREATE INDEX idx_notifications_unread ON notifications(user_id, is_read) 
    WHERE is_read = FALSE; -- Sadece okunmamışları hızlı bulmak için


CREATE TABLE notification_deliveries (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    notification_id UUID NOT NULL REFERENCES notifications(id) ON DELETE CASCADE,
    
    -- Kanal
    channel VARCHAR(20) NOT NULL, -- 'push', 'email', 'in_app'
    
    -- Durum
    status VARCHAR(20) DEFAULT 'pending', -- 'pending', 'sent', 'failed'
    
    -- Gönderim Detayı
    provider VARCHAR(50),         -- 'firebase', 'sendgrid', 'resend'
    provider_message_id VARCHAR(255), -- Sağlayıcının verdiği mesaj ID'si
    error_message TEXT,           -- Başarısız olursa neden?
    
    sent_at TIMESTAMP WITH TIME ZONE,
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    
    CONSTRAINT check_channel CHECK (channel IN ('push', 'email', 'in_app')),
    CONSTRAINT check_status CHECK (status IN ('pending', 'sent', 'failed'))
);

CREATE INDEX idx_deliveries_notification ON notification_deliveries(notification_id);
CREATE INDEX idx_deliveries_pending ON notification_deliveries(status) 
    WHERE status = 'pending'; -- Bekleyen gönderimleri hızlı bulmak için


CREATE TABLE user_device_tokens (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    
    token TEXT NOT NULL,                    -- Firebase FCM token
    device_type VARCHAR(20) NOT NULL,       -- 'ios', 'android', 'web'
    device_id VARCHAR(255),                 -- Cihaz ID'si
    
    is_active BOOLEAN DEFAULT TRUE,
    last_used_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    
    UNIQUE(user_id, device_id),             -- Aynı cihaz 2 kez kayıt olmasın
    CONSTRAINT check_device_type CHECK (device_type IN ('ios', 'android', 'web'))
);

CREATE INDEX idx_device_tokens_user ON user_device_tokens(user_id) 
    WHERE is_active = TRUE;



ALTER TABLE notifications ENABLE ROW LEVEL SECURITY;
ALTER TABLE notification_deliveries ENABLE ROW LEVEL SECURITY;
ALTER TABLE user_device_tokens ENABLE ROW LEVEL SECURITY;

-- Kullanıcı sadece kendi bildirimlerini görebilir
CREATE POLICY "Kullanıcı kendi bildirimlerini görebilir" 
ON notifications FOR SELECT 
USING (user_id = current_setting('app.current_user_id', true)::uuid);

-- Kullanıcı kendi bildirimini okundu yapabilir
CREATE POLICY "Kullanıcı bildirimini okundu yapabilir" 
ON notifications FOR UPDATE
USING (user_id = current_setting('app.current_user_id', true)::uuid);

-- Gönderim geçmişi sadece sistem görür (Policy yok = sadece backend erişir)

-- Kullanıcı kendi cihaz tokenlarını yönetebilir
CREATE POLICY "Kullanıcı kendi tokenlarını yönetebilir" 
ON user_device_tokens FOR ALL
USING (user_id = current_setting('app.current_user_id', true)::uuid);




CraftoraApi/
  src/
    Controllers/
      AuthController.cs
      ShopController.cs
      ProductController.cs
      MediaController.cs
      OrderController.cs
      CartController.cs
      CouponController.cs
      NotificationController.cs
      
    Services/
      AuthService.cs
      ShopService.cs
      ProductService.cs
      MediaService.cs
      OrderService.cs
      StorageService.cs      ← MinIO
      EmailService.cs        ← SendGrid/Resend
      CacheService.cs        ← Redis
      NotificationService.cs
      
    Models/                  ← DB modelleri (EF Core)
    DTOs/                    ← Request/Response modelleri
    Middleware/
      ExceptionMiddleware.cs
      RateLimitMiddleware.cs
    Data/
      AppDbContext.cs
      Migrations/
    Consumers/               ← RabbitMQ işçileri
      EmailConsumer.cs
      VideoProcessConsumer.cs
      InvoiceConsumer.cs
      
  docker-compose.yml
  appsettings.json
  appsettings.Development.json
  appsettings.Production.json





Aşama 1 → Altyapı
  ✦ Docker Compose kur
    (PostgreSQL + Redis + RabbitMQ + MinIO)
  ✦ .NET projesi oluştur
  ✦ EF Core + Migration
  ✦ Exception Middleware
  ✦ Serilog (structured logging)

Aşama 2 → Auth
  ✦ Register (email doğrulama ile)
  ✦ Login (JWT access + refresh token)
  ✦ Google OAuth
  ✦ Apple OAuth
  ✦ Refresh token endpoint
  ✦ Logout

Aşama 3 → MinIO (Dosya Depolama)
  ✦ MinIO bağlantısı
  ✦ Presigned URL üretimi
  ✦ Video upload
  ✦ Resim upload

Aşama 4 → Core CRUD
  ✦ Mağaza (shop) yönetimi
  ✦ Ürün yönetimi
  ✦ Kurs + bölüm + ders
  ✦ Sepet

Aşama 5 → Redis
  ✦ Mağaza profili cache
  ✦ Popüler ürünler cache
  ✦ Token blacklist

Aşama 6 → RabbitMQ
  ✦ Email kuyruğu
  ✦ Video işleme kuyruğu
  ✦ Fatura kuyruğu
  ✦ Bildirim kuyruğu

Aşama 7 → Ödeme
  ✦ Stripe entegrasyonu
  ✦ Webhook handler
  ✦ Sipariş akışı

Aşama 8 → Medya/Reels
  ✦ Video feed
  ✦ Beğeni/kaydetme/yorum
  ✦ İzleme geçmişi

Aşama 9 → Bildirimler
  ✦ Firebase push
  ✦ Email bildirimleri
  ✦ Uygulama içi


















CRAFTORA BACKEND Geli■tirme Plan■ ve ■lerleme Raporu TikTok + Shopify + Udemy Benzeri Sosyal Ticaret Platformu ■ TEKNOLOJ■ STACK Teknoloji Görev Durum .NET 9 (C#) Ana Backend API ■ Kurulu PostgreSQL 16 Ana Veritaban■ (28 tablo) ■ Kurulu Redis 7 Cache + Token Blacklist ■ Kurulu RabbitMQ 3 Mesaj Kuyru■u ■ Kurulu MinIO Dosya Depolama (S3 uyumlu) ■ Kurulu Elasticsearch 8.13 Arama Motoru ■ Kurulu Nginx Reverse Proxy ■ Kurulu Serilog Structured Logging ■ Kurulu EF Core 9 ORM (Database First) ■ Kurulu MassTransit RabbitMQ Entegrasyonu ■ Kurulu JWT Bearer Kimlik Do■rulama ■ Kurulu FluentValidation Input Validasyonu ■ Kurulu ■ PROJE HAKKINDA Craftora, kullan■c■lar■n video izlerken ürün sat■n alabildi■i sosyal ticaret platformudur. TikTok'un video ak■■■, Shopify'■n ma■aza altyap■s■ ve Udemy'nin kurs sistemi tek çat■ alt■nda birle■tirilmi■tir. Sat■c■lar video yükleyerek ürünlerini tan■t■r, izleyiciler an■nda sat■n alabilir. ■■ VER■TABANI ÖZET■ (28 Tablo) Bölüm Tablolar Kullan■c■ Sistemi users, user_sessions, login_attempts Ma■aza shops, subscriptions, shop_visits Ürünler products, course_sections, course_lessons, reviews, product_qa Medya/Reels media, media_likes, media_saves, media_comments, media_watch_history Oyunla■t■rma user_points, point_logs, contests, contest_results Sipari■/Ödeme orders, payments Kütüphane user_library, lesson_progress SaaS Abonelik seller_subscriptions Sepet cart_items Kupon coupons, coupon_uses Bildirim notifications, notification_deliveries, user_device_tokens-- 1. EKLENTİLER (EXTENSIONS) CREATE EXTENSION IF NOT EXISTS "uuid-ossp"; -- UUID (rastgele benzersiz ID) oluşturmak için gerekli eklenti CREATE EXTENSION IF NOT EXISTS "citext"; -- Büyük/küçük harf duyarsız, süper hızlı metin (email) araması için eklenti-- 2. ÖZEL VERİ TİPLERİ (ENUMS) CREATE TYPE user_role AS ENUM ('user', 'seller', 'admin'); -- Kullanıcı yetki seviyelerini belirlediğimiz sabit liste-- 3. KULLANICILAR TABLOSU (USERS) CREATE TABLE users ( id UUID PRIMARY KEY DEFAULT uuid_generate_v4(), -- Her kullanıcıya özel, tahmin edilemez şifreli kimlik numarası email CITEXT UNIQUE NOT NULL, -- Kullanıcının e-posta adresi (CITEXT: AHMET@gmail.com ile ahmet@gmail.com aynı sayılır) full_name VARCHAR(100), -- Kullanıcının ad ve soyadı bilgisi avatar_url TEXT, -- Profil fotoğrafının tutulduğu bulut (Storage) linki role user_role DEFAULT 'user', -- Sisteme kayıt olan herkes varsayılan olarak 'user' (normal müşteri) başlar auth_provider VARCHAR(50) DEFAULT 'email', -- Sisteme nereden kayıt oldu? (email, google, apple, facebook) provider_id VARCHAR(255) UNIQUE, -- Google/Apple gibi yerlerden gelen özel ID numarası password_hash TEXT, -- Eğer email ile kayıt olduysa, şifresinin kriptolanmış (kırılmaz) hali is_email_verified BOOLEAN DEFAULT FALSE, -- Email adresine giden kodu (OTP) doğru girdi mi? locked_until TIMESTAMP WITH TIME ZONE, -- Hacker saldırısı olursa hesabı şu saate kadar dondur (Brute-Force koruması) stripe_customer_id VARCHAR(255), -- Stripe (Ödeme) tarafındaki müşteri cüzdan kodu (Alışveriş için) stripe_account_id VARCHAR(255), -- Satıcı ise paranın yatacağı Stripe IBAN/Hesap kodu preferences JSONB DEFAULT '{}'::jsonb, -- Tema, dil, bildirim gibi mobil uygulama ayarlarının tutulduğu esnek depo is_active BOOLEAN DEFAULT TRUE, -- Hesap silinirse FALSE olur (Soft Delete), veriler gerçekten silinmez last_login_at TIMESTAMP WITH TIME ZONE, -- Sisteme en son ne zaman giriş yaptı? created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP, -- Hesabın oluşturulma (kayıt) tarihi updated_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP, -- Profilde yapılan en son değişikliğin tarihi-- AKILLI GÜVENLİK KURALI: -- Eğer Google/Apple ile değil de normal email ile giriyorsa, şifre boş OLAMAZ! CONSTRAINT check_password_if_email CHECK ( (auth_provider = 'email' AND password_hash IS NOT NULL) OR (auth_provider != 'email') ));-- 4. KULLANICI OTURUMLARI TABLOSU (USER SESSIONS) CREATE TABLE user_sessions ( id UUID PRIMARY KEY DEFAULT uuid_generate_v4(), -- Oturuma ait benzersiz ID user_id UUID REFERENCES users(id) ON DELETE CASCADE, -- Bu oturum hangi kullanıcıya ait? (Kullanıcı silinirse oturum da silinir) refresh_token TEXT NOT NULL, -- Kullanıcıyı her seferinde şifre girmekten kurtaran uzun yetki anahtarı device_id VARCHAR(255), -- Kullanıcının girdiği telefonun veya bilgisayarın benzersiz cihaz kodu ip_address INET, -- Güvenlik için kullanıcının girdiği internet IP adresi user_agent TEXT, -- Hangi tarayıcıdan (Chrome/Safari) veya işletim sisteminden (iOS/Android) giriyor? expires_at TIMESTAMP WITH TIME ZONE NOT NULL, -- Bu oturumun (token'ın) son kullanma tarihi (Örn: 30 gün sonra biter) created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP -- Bu oturumun açıldığı anın tarihi );-- 5. HATALI GİRİŞ DENEMELERİ TABLOSU (LOGIN ATTEMPTS) CREATE TABLE login_attempts ( email CITEXT PRIMARY KEY, -- Hangi e-posta adresine saldırı yapılıyor/deneniyor? attempt_count INT DEFAULT 1, -- Kaç kere yanlış şifre girildi? last_attempt_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP -- En son hatalı giriş denemesi ne zaman yapıldı? );-- BİSMİLLAH: CRAFTORA VERİTABANI KURULUMU - BÖLÜM 2-- ========================================== -- 1. İNDEKS KAVŞAKLARI (PERFORMANS VE HIZ) -- Milyonlarca veri içinde aramaları milisaniyelere düşüren arama motorları -- ==========================================-- Sosyal medya ile giriş yapanları anında bulmak için B-Tree İndeksi CREATE INDEX idx_users_provider_id ON users(provider_id);-- Mobil JSON ayarlarında ("Karanlık mod açık mı?") süper hızlı arama yapmak için GIN İndeksi CREATE INDEX idx_users_preferences ON users USING GIN (preferences);-- Bir kullanıcının açık olan oturumlarını şıp diye bulmak için CREATE INDEX idx_user_sessions_user_id ON user_sessions(user_id);-- Gelen Refresh Token'ın veritabanında olup olmadığını saliselik sürede doğrulamak için CREATE INDEX idx_user_sessions_token ON user_sessions(refresh_token);-- ========================================== -- 2. TETİKLEYİCİLER (OTOMASYON - TRIGGERS) -- Geliştirici hata yapsa bile veritabanının kendi kendini düzeltmesini sağlayan robotlar -- ==========================================-- Önce bir "Tarih Güncelleyen Robot (Fonksiyon)" üretiyoruz CREATE OR REPLACE FUNCTION update_updated_at_column() RETURNS TRIGGER AS $$ BEGIN NEW.updated_at = CURRENT_TIMESTAMP; -- Yeni verinin updated_at sütununu şu anki saat yap RETURN NEW; END; $$ LANGUAGE plpgsql;-- Şimdi bu robotu 'users' tablosuna bağlıyoruz: "Her UPDATE işleminden hemen ÖNCE bu robotu çalıştır" CREATE TRIGGER set_users_updated_at BEFORE UPDATE ON users FOR EACH ROW EXECUTE FUNCTION update_updated_at_column();-- ========================================== -- 3. SATIR BAZLI GÜVENLİK (RLS - ROW LEVEL SECURITY) -- Hackerları veritabanı kapısında durduran çelik yeleğimiz -- ==========================================-- Tablolarda RLS kalkanını aktif ediyoruz ALTER TABLE users ENABLE ROW LEVEL SECURITY; ALTER TABLE user_sessions ENABLE ROW LEVEL SECURITY;-- KURAL 1: Kullanıcı Profillerini Görme (SELECT) -- Herkes (sisteme giriş yapmamış anonim biri dahil) aktif kullanıcıların profilini görebilir CREATE POLICY "Aktif kullanıcıları herkes görebilir" ON users FOR SELECT USING (is_active = TRUE);-- KURAL 2: Profil Güncelleme (UPDATE) -- (Not: Backend kodumuzda, sisteme giriş yapan kişinin ID'sini 'app.current_user_id' adında bir veritabanı değişkenine atayacağız) -- Kullanıcı SADECE kendi satırındaki verileri (kendi ID'si eşleşiyorsa) değiştirebilir CREATE POLICY "Kullanıcı sadece kendi profilini güncelleyebilir" ON users FOR UPDATE USING (id = current_setting('app.current_user_id', true)::uuid);-- KURAL 3: Oturumları Görme ve Silme (SESSION GİZLİLİĞİ) -- Oturumlar (Token'lar) aşırı gizlidir. Sadece sahibi kendi token'ını görebilir ve silebilir (Çıkış yapma) CREATE POLICY "Kullanıcı sadece kendi oturumlarını görebilir" ON user_sessions FOR SELECT USING (user_id = current_setting('app.current_user_id', true)::uuid);CREATE POLICY "Kullanıcı sadece kendi oturumlarını silebilir" ON user_sessions FOR DELETE USING (user_id = current_setting('app.current_user_id', true)::uuid);SELECT full_name, created_at, updated_at FROM users;-- CRAFTORA VERİTABANI KURULUMU - BÖLÜM 3 (MAĞAZA EKOSİSTEMİ)-- 1. MAĞAZALAR TABLOSU (SHOPS) CREATE TABLE shops ( id UUID PRIMARY KEY DEFAULT uuid_generate_v4(), -- Mağaza kimlik numarası user_id UUID UNIQUE NOT NULL REFERENCES users(id) ON DELETE CASCADE, -- Mağaza sahibi (1 kullanıcı = 1 mağaza kuralı UNIQUE ile sağlandı) shop_name VARCHAR(100) NOT NULL, -- Mağazanın görünen adı slug CITEXT UNIQUE NOT NULL, -- URL adresi (Örn: craftora.com/magza-adi). CITEXT sayesinde büyük/küçük harf duyarsız ve hızlıdır. external_url VARCHAR(255), -- Varsa harici web sitesi linki short_description VARCHAR(255), -- Mağaza kartlarında görünecek kısa özet description TEXT, -- Mağaza ana açıklama metni about_content TEXT, -- HTML destekli zengin "Hakkımızda" içeriği social_links JSONB DEFAULT '{}'::jsonb, -- Instagram, TikTok vb. linklerin tutulduğu esnek JSON deposu logo_url TEXT, -- Mağaza logosunun bulut linki banner_url TEXT, -- Mağaza kapak fotoğrafının bulut linki follower_count INT DEFAULT 0, -- PERFORMANS: Her seferinde sayım yapmamak için otomatik güncellenen takipçi sayısı rating DECIMAL(3,2) DEFAULT 0.0, -- PERFORMANS: Mağaza puan ortalaması (Örn: 4.85) is_verified BOOLEAN DEFAULT FALSE, -- CTO DOKUNUŞU: Mavi Tik (Onaylı Mağaza) durumu is_active BOOLEAN DEFAULT TRUE, -- Mağaza donduruldu mu? created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP, -- Kuruluş tarihi updated_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP -- Son düzenleme tarihi );-- 2. ABONELİKLER TABLOSU (SUBSCRIPTIONS) CREATE TABLE subscriptions ( id UUID PRIMARY KEY DEFAULT uuid_generate_v4(), shop_id UUID NOT NULL REFERENCES shops(id) ON DELETE CASCADE, -- Takip edilen mağaza user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE, -- Takip eden kullanıcı wants_notifications BOOLEAN DEFAULT TRUE, -- CTO DOKUNUŞU: Zil butonu (Yeni ürün bildirimi gelsin mi?) created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,-- Bir kullanıcı bir mağazayı sadece bir kez takip edebilir: CONSTRAINT unique_subscription UNIQUE (shop_id, user_id));-- 3. MAĞAZA ZİYARETLERİ TABLOSU (SHOP_VISITS) CREATE TABLE shop_visits ( id UUID PRIMARY KEY DEFAULT uuid_generate_v4(), shop_id UUID NOT NULL REFERENCES shops(id) ON DELETE CASCADE, user_id UUID REFERENCES users(id) ON DELETE SET NULL, -- Üye olan ziyaretçi (Nullable: Üye olmayanlar için boş kalabilir) ip_address INET, -- CTO DOKUNUŞU: Üye olmayan anonim ziyaretçileri IP üzerinden takip etmek için visited_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP -- Ziyaret saati );-- 4. İNDEKS KAVŞAKLARI (HIZ) CREATE INDEX idx_shops_slug ON shops(slug); -- Mağaza URL aramalarını ışık hızına çıkarır CREATE INDEX idx_shop_visits_composite ON shop_visits(shop_id, visited_at); -- Satıcı paneli grafiklerini hızlandırır -- Kullanıcıların arama çubuğunda mağaza adıyla arama yapmasını hızlandırmak için: CREATE INDEX idx_shops_name ON shops(shop_name);-- 5. OTOMATİK ABONE SAYACI (TRIGGER FUNCTION) CREATE OR REPLACE FUNCTION sync_follower_count() RETURNS TRIGGER AS $$ BEGIN IF (TG_OP = 'INSERT') THEN UPDATE shops SET follower_count = follower_count + 1 WHERE id = NEW.shop_id; ELSIF (TG_OP = 'DELETE') THEN UPDATE shops SET follower_count = follower_count - 1 WHERE id = OLD.shop_id; END IF; RETURN NULL; END; $$ LANGUAGE plpgsql;-- Takip etme/çıkma anında sayacı çalıştır CREATE TRIGGER trg_sync_followers AFTER INSERT OR DELETE ON subscriptions FOR EACH ROW EXECUTE FUNCTION sync_follower_count();-- Mağaza updated_at tetikleyicisi CREATE TRIGGER set_shops_updated_at BEFORE UPDATE ON shops FOR EACH ROW EXECUTE FUNCTION update_updated_at_column();-- 6. GÜVENLİK KALKANLARI (RLS) ALTER TABLE shops ENABLE ROW LEVEL SECURITY; ALTER TABLE subscriptions ENABLE ROW LEVEL SECURITY; ALTER TABLE shop_visits ENABLE ROW LEVEL SECURITY;-- Mağazaları herkes görebilir ama sadece sahibi düzenleyebilir CREATE POLICY "Aktif mağazalar herkese açıktır" ON shops FOR SELECT USING (is_active = TRUE); CREATE POLICY "Mağaza sahibi dükkanını yönetebilir" ON shops FOR UPDATE USING (user_id = current_setting('app.current_user_id', true)::uuid);-- Abonelik ve Ziyaret gizliliği: Sadece mağaza sahibi görebilir CREATE POLICY "Satıcı kendi abonelerini görebilir" ON subscriptions FOR SELECT USING (shop_id IN (SELECT id FROM shops WHERE user_id = current_setting('app.current_user_id', true)::uuid));CREATE POLICY "Satıcı kendi trafiğini görebilir" ON shop_visits FOR SELECT USING (shop_id IN (SELECT id FROM shops WHERE user_id = current_setting('app.current_user_id', true)::uuid));-- SADECE YARIŞMALAR TABLOSUNU SİSTEME BAĞLAMA YAMASI (İzole adayı kurtarıyoruz) ALTER TABLE contests ADD COLUMN created_by UUID REFERENCES users(id) ON DELETE SET NULL;-- CRAFTORA VERİTABANI KURULUMU - BÖLÜM 4 (ÜRÜNLER VE KURSLAR)-- 1. YENİ VERİ TİPLERİ (ENUMS) CREATE TYPE product_type AS ENUM ('digital_file', 'course'); CREATE TYPE media_status AS ENUM ('processing', 'ready', 'failed'); -- Videolar işlenirken bozuk görünmesin diye-- 2. ANA ÜRÜNLER TABLOSU (PRODUCTS) CREATE TABLE products ( id UUID PRIMARY KEY DEFAULT uuid_generate_v4(), shop_id UUID NOT NULL REFERENCES shops(id) ON DELETE CASCADE, type product_type DEFAULT 'digital_file', title VARCHAR(255) NOT NULL, description TEXT, metadata JSONB DEFAULT '{}'::jsonb, -- CTO DOKUNUŞU: E-kitap sayfası, 3D model formatı gibi sınırsız özellikleri buraya gömeceğiz price DECIMAL(10,2) NOT NULL, currency VARCHAR(3) DEFAULT 'USD', cover_image_url TEXT, file_url TEXT, -- Dijital dosya ise indirme linki (Kurs ise NULL kalır) rating_average DECIMAL(3,2) DEFAULT 0.0, -- OTOPİLOT: Müşteri ana sayfada gezerken hesap yapmakla uğraşmayacak review_count INT DEFAULT 0, -- OTOPİLOT: Toplam yorum sayısı sales_count INT DEFAULT 0, -- Çok satanları bulmak için is_active BOOLEAN DEFAULT TRUE, -- Satıcı ürünü silse bile kütüphaneler bozulmasın diye Soft Delete yapıyoruz is_featured BOOLEAN DEFAULT FALSE, created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP, updated_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,CONSTRAINT check_price_positive CHECK (price >= 0) -- Fiyat asla eksi olamaz kalkanı!);-- 3. KURS BÖLÜMLERİ (Örn: C++ Döngüler) CREATE TABLE course_sections ( id UUID PRIMARY KEY DEFAULT uuid_generate_v4(), product_id UUID NOT NULL REFERENCES products(id) ON DELETE CASCADE, title VARCHAR(255) NOT NULL, sort_order INT NOT NULL, -- Uygulamada hangi sırada görünecek? (1, 2, 3)UNIQUE(product_id, sort_order) -- Aynı kurs içinde aynı sıra numarası yanlışlıkla girilmesin);-- 4. KURS DERSLERİ / VİDEOLARI (Örn: For Döngüsü) CREATE TABLE course_lessons ( id UUID PRIMARY KEY DEFAULT uuid_generate_v4(), section_id UUID NOT NULL REFERENCES course_sections(id) ON DELETE CASCADE, title VARCHAR(255) NOT NULL, video_url TEXT, document_url TEXT, -- Varsa ders notu (PDF) duration_seconds INT DEFAULT 0, is_free_preview BOOLEAN DEFAULT FALSE, -- Ücretsiz tanıtım videosu mu? sort_order INT NOT NULL, status media_status DEFAULT 'ready', -- Video işlenme durumuUNIQUE(section_id, sort_order));-- 5. DEĞERLENDİRMELER (Yıldız ve Yorum - Kesin Kurallı) CREATE TABLE reviews ( id UUID PRIMARY KEY DEFAULT uuid_generate_v4(), product_id UUID NOT NULL REFERENCES products(id) ON DELETE CASCADE, user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE, rating INT NOT NULL, comment TEXT, seller_reply TEXT, -- Satıcının tek bir yanıt hakkı var (Uzatılamaz) created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP, updated_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,CONSTRAINT check_rating_range CHECK (rating >= 1 AND rating <= 5), -- Yıldız 1-5 arası olmak ZORUNDA CONSTRAINT unique_user_review UNIQUE (product_id, user_id) -- 1 Kullanıcı ürüne SADECE 1 KERE puan verebilir);-- 6. SORU VE CEVAP (Kullanıcı ve Satıcı Karşılıklı Sohbeti) CREATE TABLE product_qa ( id UUID PRIMARY KEY DEFAULT uuid_generate_v4(), product_id UUID NOT NULL REFERENCES products(id) ON DELETE CASCADE, user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE, parent_id UUID REFERENCES product_qa(id) ON DELETE CASCADE, -- Eğer yanıtsa hangi mesaja yanıt? message TEXT NOT NULL, created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP );-- ========================================================================= -- CRAFTORA VERİTABANI KURULUMU - BÖLÜM 5 (MEDYA, REELS VE OYUNLAŞTIRMA) -- =========================================================================-- 1. MEDYA VE ETKİLEŞİM TABLOLARI (SOSYAL MEDYA MOTORU)-- REELS VİDEOLARI CREATE TABLE media ( id UUID PRIMARY KEY DEFAULT uuid_generate_v4(), shop_id UUID NOT NULL REFERENCES shops(id) ON DELETE CASCADE, product_id UUID REFERENCES products(id) ON DELETE SET NULL, -- Videoda satılan ürün video_url TEXT NOT NULL, thumbnail_url TEXT, view_count INT DEFAULT 0, like_count INT DEFAULT 0, save_count INT DEFAULT 0, comment_count INT DEFAULT 0, -- CTO DOKUNUŞU: Yorum sayacı created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP, updated_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP, is_active BOOLEAN DEFAULT TRUE );-- REELS BEĞENİLERİ CREATE TABLE media_likes ( id UUID PRIMARY KEY DEFAULT uuid_generate_v4(), media_id UUID NOT NULL REFERENCES media(id) ON DELETE CASCADE, user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE, created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP, UNIQUE(media_id, user_id) );-- REELS KAYDETMELERİ CREATE TABLE media_saves ( id UUID PRIMARY KEY DEFAULT uuid_generate_v4(), media_id UUID NOT NULL REFERENCES media(id) ON DELETE CASCADE, user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE, created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP, UNIQUE(media_id, user_id) );-- REELS YORUMLARI (CTO DOKUNUŞU) CREATE TABLE media_comments ( id UUID PRIMARY KEY DEFAULT uuid_generate_v4(), media_id UUID NOT NULL REFERENCES media(id) ON DELETE CASCADE, user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE, comment_text TEXT NOT NULL, created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP, updated_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP );-- İZLEME GEÇMİŞİ (Günlük Puan Limiti ve Algoritma İçin) CREATE TABLE media_watch_history ( id UUID PRIMARY KEY DEFAULT uuid_generate_v4(), user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE, media_id UUID NOT NULL REFERENCES media(id) ON DELETE CASCADE, watched_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP, is_point_earned BOOLEAN DEFAULT FALSE, UNIQUE(user_id, media_id) -- Keşfet'te aynı video bir daha çıkmasın diye );-- 2. OYUNLAŞTIRMA VE LİDERLİK TABLOLARI (GAMIFICATION)-- KULLANICI PUAN CÜZDANI CREATE TABLE user_points ( id UUID PRIMARY KEY DEFAULT uuid_generate_v4(), user_id UUID UNIQUE NOT NULL REFERENCES users(id) ON DELETE CASCADE, total_points DECIMAL(12,2) DEFAULT 0.0, current_rank INT DEFAULT 0, current_streak INT DEFAULT 0, -- CTO DOKUNUŞU: Kaç gündür üst üste giriyor (Ateş serisi) updated_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP );-- PUAN KAYIT DEFTERİ (Geçmiş) CREATE TABLE point_logs ( id UUID PRIMARY KEY DEFAULT uuid_generate_v4(), user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE, action_type VARCHAR(50) NOT NULL, points_earned DECIMAL(10,2) NOT NULL, reference_id UUID, created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP );-- YARIŞMALAR VE SONUÇLAR (Senin yakaladığın efsane köprü!) CREATE TABLE contests ( id UUID PRIMARY KEY DEFAULT uuid_generate_v4(), title VARCHAR(255) NOT NULL, start_date TIMESTAMP WITH TIME ZONE NOT NULL, end_date TIMESTAMP WITH TIME ZONE NOT NULL, prize_pool TEXT, is_active BOOLEAN DEFAULT TRUE );CREATE TABLE contest_results ( id UUID PRIMARY KEY DEFAULT uuid_generate_v4(), contest_id UUID NOT NULL REFERENCES contests(id) ON DELETE CASCADE, user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE, final_rank INT, total_score DECIMAL(12,2), reward_claimed BOOLEAN DEFAULT FALSE, created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP, UNIQUE(contest_id, user_id) );-- 3. OTOPİLOT ROBOTLARI VE İNDEKS KAVŞAKLARICREATE INDEX idx_media_shop ON media(shop_id); CREATE INDEX idx_media_product ON media(product_id); CREATE INDEX idx_point_logs_user_date ON point_logs(user_id, created_at);-- OTOPİLOT 1: MEDYA SAYAÇLARI (Like, Save ve Yorumları Otomatik Sayar) CREATE OR REPLACE FUNCTION sync_media_counters() RETURNS TRIGGER AS $$ BEGIN IF TG_TABLE_NAME = 'media_likes' THEN IF TG_OP = 'INSERT' THEN UPDATE media SET like_count = like_count + 1 WHERE id = NEW.media_id; ELSIF TG_OP = 'DELETE' THEN UPDATE media SET like_count = like_count - 1 WHERE id = OLD.media_id; END IF; ELSIF TG_TABLE_NAME = 'media_saves' THEN IF TG_OP = 'INSERT' THEN UPDATE media SET save_count = save_count + 1 WHERE id = NEW.media_id; ELSIF TG_OP = 'DELETE' THEN UPDATE media SET save_count = save_count - 1 WHERE id = OLD.media_id; END IF; ELSIF TG_TABLE_NAME = 'media_comments' THEN IF TG_OP = 'INSERT' THEN UPDATE media SET comment_count = comment_count + 1 WHERE id = NEW.media_id; ELSIF TG_OP = 'DELETE' THEN UPDATE media SET comment_count = comment_count - 1 WHERE id = OLD.media_id; END IF; END IF; RETURN NULL; END; $$ LANGUAGE plpgsql;CREATE TRIGGER trg_media_like_counter AFTER INSERT OR DELETE ON media_likes FOR EACH ROW EXECUTE FUNCTION sync_media_counters(); CREATE TRIGGER trg_media_save_counter AFTER INSERT OR DELETE ON media_saves FOR EACH ROW EXECUTE FUNCTION sync_media_counters(); CREATE TRIGGER trg_media_comment_counter AFTER INSERT OR DELETE ON media_comments FOR EACH ROW EXECUTE FUNCTION sync_media_counters();-- OTOPİLOT 2: SATICI PUAN ROBOTU (Like Aldıkça 0.5 Kazanır, UPSERT mantığıyla) CREATE OR REPLACE FUNCTION award_seller_points() RETURNS TRIGGER AS $$ DECLARE v_seller_id UUID; BEGIN SELECT s.user_id INTO v_seller_id FROM media m JOIN shops s ON m.shop_id = s.id WHERE m.id = NEW.media_id;INSERT INTO point_logs (user_id, action_type, points_earned, reference_id) VALUES (v_seller_id, 'receive_like', 0.5, NEW.media_id); -- ON CONFLICT: Cüzdanı yoksa yarat, varsa üstüne ekle (UPSERT) INSERT INTO user_points (user_id, total_points) VALUES (v_seller_id, 0.5) ON CONFLICT (user_id) DO UPDATE SET total_points = user_points.total_points + 0.5, updated_at = CURRENT_TIMESTAMP; RETURN NEW;END; $$ LANGUAGE plpgsql; CREATE TRIGGER trg_points_on_like AFTER INSERT ON media_likes FOR EACH ROW EXECUTE FUNCTION award_seller_points();-- OTOPİLOT 3: İZLEYİCİ PUAN ROBOTU (Günlük Limit: 120) CREATE OR REPLACE FUNCTION award_viewer_points() RETURNS TRIGGER AS $$ DECLARE v_daily_points DECIMAL; BEGIN SELECT COALESCE(SUM(points_earned), 0) INTO v_daily_points FROM point_logs WHERE user_id = NEW.user_id AND action_type = 'watch_reels' AND created_at::date = CURRENT_DATE;IF v_daily_points < 120 THEN INSERT INTO point_logs (user_id, action_type, points_earned, reference_id) VALUES (NEW.user_id, 'watch_reels', 1.0, NEW.media_id); INSERT INTO user_points (user_id, total_points) VALUES (NEW.user_id, 1.0) ON CONFLICT (user_id) DO UPDATE SET total_points = user_points.total_points + 1.0, updated_at = CURRENT_TIMESTAMP; NEW.is_point_earned := TRUE; END IF; RETURN NEW;END; $$ LANGUAGE plpgsql; CREATE TRIGGER trg_points_on_watch BEFORE INSERT ON media_watch_history FOR EACH ROW EXECUTE FUNCTION award_viewer_points();-- 4. ÇELİK YELEKLER (RLS POLICIES) - SENİN YAKALADIĞIN EKSİK!ALTER TABLE media ENABLE ROW LEVEL SECURITY; ALTER TABLE media_likes ENABLE ROW LEVEL SECURITY; ALTER TABLE media_saves ENABLE ROW LEVEL SECURITY; ALTER TABLE media_comments ENABLE ROW LEVEL SECURITY; ALTER TABLE media_watch_history ENABLE ROW LEVEL SECURITY; ALTER TABLE user_points ENABLE ROW LEVEL SECURITY; ALTER TABLE point_logs ENABLE ROW LEVEL SECURITY;-- MEDYA (REELS) CREATE POLICY "Aktif videolar herkese açık" ON media FOR SELECT USING (is_active = TRUE); CREATE POLICY "Satıcı kendi videosunu yönetebilir" ON media FOR ALL USING (shop_id IN (SELECT id FROM shops WHERE user_id = current_setting('app.current_user_id', true)::uuid));-- BEĞENİ VE KAYDETMELER (GİZLİLİK) CREATE POLICY "Herkes kendi beğeni/kayıtlarını görebilir" ON media_likes FOR SELECT USING (user_id = current_setting('app.current_user_id', true)::uuid); CREATE POLICY "Herkes kendi beğeni/kayıtlarını yapabilir" ON media_likes FOR ALL USING (user_id = current_setting('app.current_user_id', true)::uuid);CREATE POLICY "Herkes kendi kaydettiklerini görebilir" ON media_saves FOR SELECT USING (user_id = current_setting('app.current_user_id', true)::uuid); CREATE POLICY "Herkes kendi kaydettiklerini yönetebilir" ON media_saves FOR ALL USING (user_id = current_setting('app.current_user_id', true)::uuid);-- YORUMLAR CREATE POLICY "Yorumları herkes okuyabilir" ON media_comments FOR SELECT USING (true); CREATE POLICY "Kullanıcı kendi yorumunu silebilir/düzenleyebilir" ON media_comments FOR ALL USING (user_id = current_setting('app.current_user_id', true)::uuid);-- OYUNLAŞTIRMA VE LİDERLİK TABLOSU GÜVENLİĞİ CREATE POLICY "Liderlik tablosunu herkes görebilir" ON user_points FOR SELECT USING (true); -- DİKKAT: user_points tablosuna UPDATE kuralı yazmıyoruz! Çünkü puanları API değil, sadece veritabanı Trigger'ları (Robotlar) verebilir. Hacker puanını artıramaz!CREATE POLICY "Kullanıcı sadece kendi puan geçmişini görebilir" ON point_logs FOR SELECT USING (user_id = current_setting('app.current_user_id', true)::uuid);-- ========================================================================= -- CRAFTORA VERİTABANI KURULUMU - BÖLÜM 6 (SİPARİŞLER VE FİNANS) -- =========================================================================-- 1. SİPARİŞ DURUMLARI (ENUM) CREATE TYPE order_status AS ENUM ('pending', 'completed', 'failed', 'refunded');-- 2. SİPARİŞLER TABLOSU (ORDERS) CREATE TABLE orders ( id UUID PRIMARY KEY DEFAULT uuid_generate_v4(), buyer_id UUID NOT NULL REFERENCES users(id) ON DELETE RESTRICT, -- MÜHENDİSLİK: Kullanıcı silinse bile fatura silinmez! product_id UUID NOT NULL REFERENCES products(id) ON DELETE RESTRICT, -- Ürün silinse bile sipariş geçmişi kalır! shop_id UUID NOT NULL REFERENCES shops(id) ON DELETE RESTRICT, order_number VARCHAR(50) UNIQUE NOT NULL, -- Örn: CRAFT-2026-XYZ123-- FİNANSAL BÖLÜNME (MUHASEBE) amount DECIMAL(10,2) NOT NULL, -- Müşterinin ödediği toplam para (Örn: 100.00) currency VARCHAR(3) DEFAULT 'USD', platform_fee DECIMAL(10,2) DEFAULT 0.00, -- Craftora'nın cebine giren komisyon (Örn: 10.00) seller_earnings DECIMAL(10,2) DEFAULT 0.00, -- Satıcının Stripe hesabına yatacak para (Örn: 90.00) status order_status DEFAULT 'pending', stripe_payment_id VARCHAR(255), -- İade ve iptaller için banka işlem numarası invoice_pdf_url TEXT, -- Kesilen e-faturanın PDF linki created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP, updated_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP, -- GÜVENLİK KALKANLARI CONSTRAINT check_amount_positive CHECK (amount >= 0), CONSTRAINT check_fee_logic CHECK (platform_fee + seller_earnings = amount) -- Toplam tutar, kesintilerle eşleşmek ZORUNDA!);-- 3. İNDEKS KAVŞAKLARI (PERFORMANS VE ARAMA HIZI) CREATE INDEX idx_orders_buyer ON orders(buyer_id); -- Müşterinin "Siparişlerim" sayfasını hızlandırır CREATE INDEX idx_orders_shop ON orders(shop_id); -- Satıcının "Gelen Siparişler" tablosunu hızlandırır CREATE INDEX idx_orders_number ON orders(order_number); -- Müşteri hizmetlerinin fatura no ile arama yapması için CREATE INDEX idx_orders_status ON orders(status);-- 4. OTOPİLOT ROBOTLARI (OTOMASYON)-- Saat Güncelleyici CREATE TRIGGER set_orders_updated_at BEFORE UPDATE ON orders FOR EACH ROW EXECUTE FUNCTION update_updated_at_column();-- EFSANE ROBOT: Sipariş "Completed" (Tamamlandı) olunca çalışır! CREATE OR REPLACE FUNCTION process_completed_order() RETURNS TRIGGER AS $$ DECLARE v_seller_id UUID; BEGIN -- Eğer sipariş durumu 'completed' olarak güncellendiyse (veya direkt eklendiyse) IF (NEW.status = 'completed' AND (TG_OP = 'INSERT' OR OLD.status != 'completed')) THEN -- 1. Ürünün satış sayacını (sales_count) 1 artır UPDATE products SET sales_count = sales_count + 1 WHERE id = NEW.product_id; -- 2. Satıcıyı bul SELECT user_id INTO v_seller_id FROM shops WHERE id = NEW.shop_id; -- 3. Satıcıya Oyunlaştırma Modülünden 20 PUAN kazandır! (make_sale aksiyonu) INSERT INTO point_logs (user_id, action_type, points_earned, reference_id) VALUES (v_seller_id, 'make_sale', 20.0, NEW.id); UPDATE user_points SET total_points = total_points + 20.0, updated_at = CURRENT_TIMESTAMP WHERE user_id = v_seller_id; END IF; RETURN NEW;END; $$ LANGUAGE plpgsql;-- Robotu Siparişler Tablosuna Bağlayalım CREATE TRIGGER trg_on_order_completed AFTER INSERT OR UPDATE ON orders FOR EACH ROW EXECUTE FUNCTION process_completed_order();-- 5. ÇELİK YELEKLER (RLS - FİNANSAL GİZLİLİK) ALTER TABLE orders ENABLE ROW LEVEL SECURITY;-- KURAL 1: Alıcı (Müşteri) SADECE kendi verdiği siparişleri ve faturalarını görebilir CREATE POLICY "Alıcılar kendi siparişlerini görebilir" ON orders FOR SELECT USING (buyer_id = current_setting('app.current_user_id', true)::uuid);-- KURAL 2: Satıcı SADECE kendi dükkanına gelen siparişleri görebilir CREATE POLICY "Satıcılar kendi mağaza siparişlerini görebilir" ON orders FOR SELECT USING (shop_id IN (SELECT id FROM shops WHERE user_id = current_setting('app.current_user_id', true)::uuid));-- DİKKAT (CTO KURALI): Kullanıcılar (Alıcı veya Satıcı) sipariş silebilir veya durumunu değiştirebilir mi? ASLA! -- RLS kalkanında INSERT, UPDATE ve DELETE kurallarını YAZMIYORUZ. -- Bu sayede sadece Backend Sunucumuz (Stripe'dan ödeme onayı alınca) siparişi güncelleyebilir. Hacker fiyata veya duruma müdahale edemez.-- 1. ÖDEME DURUMLARI (ENUM) CREATE TYPE payment_status_type AS ENUM ('processing', 'succeeded', 'failed', 'refunded');-- 2. ANA ÖDEMELER TABLOSU (PAYMENTS) CREATE TABLE payments ( id UUID PRIMARY KEY DEFAULT uuid_generate_v4(), order_id UUID UNIQUE NOT NULL REFERENCES orders(id) ON DELETE RESTRICT, -- UNIQUE: Bir siparişin SADECE BİR ödeme kaydı olur! payment_provider VARCHAR(50) NOT NULL, -- 'stripe', 'iyzico', 'paypal' provider_transaction_id VARCHAR(255) UNIQUE, -- Bankanın verdiği efsanevi, kopyalanamaz dekont/işlem numarasıgross_amount DECIMAL(10,2) NOT NULL, -- Karttan çekilen brüt para platform_fee_amount DECIMAL(10,2) NOT NULL, -- Banka+Craftora kesintisi net_earnings DECIMAL(10,2) NOT NULL, -- Satıcının hesabına yatacak net para status payment_status_type DEFAULT 'processing', error_message TEXT, -- Eğer işlem failed olursa bankanın gönderdiği hata kodu ("Bakiye yetersiz" vb.) created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP, updated_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP, -- GÜVENLİK KALKANLARI CONSTRAINT check_gross_positive CHECK (gross_amount >= 0), CONSTRAINT check_payment_math CHECK (gross_amount = platform_fee_amount + net_earnings) -- Muhasebe matematiği ASLA şaşamaz!);-- 3. İNDEKS KAVŞAKLARI (PERFORMANS) CREATE INDEX idx_payments_transaction_id ON payments(provider_transaction_id); -- Bankadan gelen Webhook'ları salisede bulmak için CREATE INDEX idx_payments_status ON payments(status);-- 4. OTOPİLOT ROBOTLARI (DOMİNO ETKİSİ)-- Saat Güncelleyici CREATE TRIGGER set_payments_updated_at BEFORE UPDATE ON payments FOR EACH ROW EXECUTE FUNCTION update_updated_at_column();-- DOMİNO ROBOTU: Ödeme başarılı olursa, Siparişi de Tamamla! CREATE OR REPLACE FUNCTION sync_order_status_from_payment() RETURNS TRIGGER AS $$ BEGIN -- Eğer banka ödemesi 'succeeded' olduysa IF (NEW.status = 'succeeded' AND (TG_OP = 'INSERT' OR OLD.status != 'succeeded')) THEN -- Gidip Orders (Sipariş) tablosundaki durumu da 'completed' yapıyoruz. -- DİKKAT: Bu UPDATE işlemi, bir önceki aşamada yazdığımız Puan Dağıtma robotunu tetikleyecek! UPDATE orders SET status = 'completed' WHERE id = NEW.order_id; -- Eğer banka 'refunded' (İade) dediyse, siparişi de iptal et ELSIF (NEW.status = 'refunded' AND OLD.status != 'refunded') THEN UPDATE orders SET status = 'refunded' WHERE id = NEW.order_id; END IF; RETURN NEW;END; $$ LANGUAGE plpgsql;CREATE TRIGGER trg_sync_order_on_payment AFTER INSERT OR UPDATE ON payments FOR EACH ROW EXECUTE FUNCTION sync_order_status_from_payment();-- 5. ÇELİK YELEKLER (RLS - FİNANSAL GİZLİLİK) ALTER TABLE payments ENABLE ROW LEVEL SECURITY;-- KURAL 1: Alıcı sadece KENDİ siparişine bağlı ödeme dekontunu görebilir CREATE POLICY "Alıcılar dekontunu görebilir" ON payments FOR SELECT USING (order_id IN (SELECT id FROM orders WHERE buyer_id = current_setting('app.current_user_id', true)::uuid));-- KURAL 2: Satıcı sadece KENDİ dükkanına ait satışların ödeme/komisyon dökümünü görebilir CREATE POLICY "Satıcılar kendi gelir dökümlerini görebilir" ON payments FOR SELECT USING (order_id IN (SELECT id FROM orders WHERE shop_id IN (SELECT id FROM shops WHERE user_id = current_setting('app.current_user_id', true)::uuid)));-- DİKKAT: INSERT, UPDATE, DELETE KESİNLİKLE YOK! Ödeme durumunu sadece Stripe webhook'larından gelen veriyi işleyen arka uç (Backend) kodumuz yapabilir.INSERT INTO payments (order_id, payment_provider, provider_transaction_id, gross_amount, platform_fee_amount, net_earnings, status) VALUES ( (SELECT id FROM orders WHERE order_number = 'PENDING-ORD-002'), 'stripe', 'ch_basarili_islem_123', 100.00, 10.00, 90.00, 'succeeded' -- İŞTE BU KELİME DOMİNOYI BAŞLATACAK! );SELECT order_number, status FROM orders WHERE order_number = 'PENDING-ORD-002';-- SONUÇ 2: C++ Kursunun satış sayısı tekrar artmış mı? SELECT title, sales_count FROM products WHERE title = 'Sıfırdan İleri Seviye C++ Eğitimi';-- SONUÇ 3: Ahmet'in cüzdanına ekstra 20 puan daha (Toplam 40.50) gelmiş mi? SELECT total_points FROM user_points WHERE user_id = (SELECT id FROM users WHERE email = 'ahmet.yilmaz@gmail.com');-- ========================================================================= -- CRAFTORA VERİTABANI KURULUMU - BÖLÜM 7 (KÜTÜPHANE VE EĞİTİM) -- =========================================================================-- 1. TABLOLAR (MİMARİ)-- KULLANICI KÜTÜPHANESİ (SATIN ALINANLAR) CREATE TABLE user_library ( id UUID PRIMARY KEY DEFAULT uuid_generate_v4(), user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE, product_id UUID NOT NULL REFERENCES products(id) ON DELETE CASCADE, purchased_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP, last_accessed_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP, -- CTO DOKUNUŞU: Kaldığın yerden devam et!UNIQUE(user_id, product_id) -- Bir kullanıcı aynı ürüne iki kere sahip olamaz);-- DERS İLERLEMESİ (VİDEO İZLEME SÜRELERİ) CREATE TABLE lesson_progress ( id UUID PRIMARY KEY DEFAULT uuid_generate_v4(), user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE, lesson_id UUID NOT NULL REFERENCES course_lessons(id) ON DELETE CASCADE, is_completed BOOLEAN DEFAULT FALSE, watched_seconds INT DEFAULT 0, completed_at TIMESTAMP WITH TIME ZONE, -- Ne zaman bitirdi? updated_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,UNIQUE(user_id, lesson_id) -- Bir kullanıcı bir ders için sadece bir kayıt tutabilir);-- 2. İNDEKS KAVŞAKLARI (PERFORMANS)CREATE INDEX idx_user_library_accessed ON user_library(user_id, last_accessed_at DESC); -- "Devam Et" rafını saniyede yükler CREATE INDEX idx_lesson_progress_user ON lesson_progress(user_id, lesson_id);-- 3. OTOPİLOT ROBOTLARI (OTOMATİK TESLİMAT VE PUAN)-- ROBOT 1: Saat Güncelleyici CREATE TRIGGER set_progress_updated_at BEFORE UPDATE ON lesson_progress FOR EACH ROW EXECUTE FUNCTION update_updated_at_column();-- EFSANE ROBOT 2: OTOMATİK DİJİTAL TESLİMAT (Sipariş Onaylanınca Çalışır) CREATE OR REPLACE FUNCTION deliver_product_to_library() RETURNS TRIGGER AS $$ BEGIN -- Sipariş 'completed' statüsüne geçtiyse: IF (NEW.status = 'completed' AND (TG_OP = 'INSERT' OR OLD.status != 'completed')) THEN -- Ürünü alıcının kütüphanesine ekle (Eğer zaten varsa hata verme, sessizce geç: ON CONFLICT DO NOTHING) INSERT INTO user_library (user_id, product_id) VALUES (NEW.buyer_id, NEW.product_id) ON CONFLICT (user_id, product_id) DO NOTHING; END IF; RETURN NEW; END; $$ LANGUAGE plpgsql;CREATE TRIGGER trg_auto_deliver_product AFTER INSERT OR UPDATE ON orders FOR EACH ROW EXECUTE FUNCTION deliver_product_to_library();-- EFSANE ROBOT 3: ÖĞRENCİ PUAN SİSTEMİ (Ders Bitince 2 Puan Verir) CREATE OR REPLACE FUNCTION reward_lesson_completion() RETURNS TRIGGER AS $$ BEGIN -- Eğer ders ŞU AN tamamlandıysa (Önceden false idi, şimdi true olduysa) IF (NEW.is_completed = TRUE AND OLD.is_completed = FALSE) THEN -- Müşteriye 2 Puan ver (action_type: 'complete_lesson') INSERT INTO point_logs (user_id, action_type, points_earned, reference_id) VALUES (NEW.user_id, 'complete_lesson', 2.0, NEW.lesson_id); -- Cüzdanı güncelle (UPSERT - Cüzdanı yoksa yarat) INSERT INTO user_points (user_id, total_points) VALUES (NEW.user_id, 2.0) ON CONFLICT (user_id) DO UPDATE SET total_points = user_points.total_points + 2.0, updated_at = CURRENT_TIMESTAMP; -- Tamamlanma saatini şu anki saat yap NEW.completed_at = CURRENT_TIMESTAMP; END IF; RETURN NEW;END; $$ LANGUAGE plpgsql;-- Bu robotu sadece UPDATE işleminde çalıştırıyoruz (Videoyu izledikçe güncellenecek çünkü) CREATE TRIGGER trg_reward_on_lesson_complete BEFORE UPDATE ON lesson_progress FOR EACH ROW EXECUTE FUNCTION reward_lesson_completion();-- 4. ÇELİK YELEKLER (RLS - KORSAN KALKANI)ALTER TABLE user_library ENABLE ROW LEVEL SECURITY; ALTER TABLE lesson_progress ENABLE ROW LEVEL SECURITY;-- KÜTÜPHANE GÜVENLİĞİ: Kullanıcı KENDİ kütüphanesini görebilir. -- DİKKAT: INSERT veya DELETE yok! Ürünü sadece sistem (Orders tablosundaki Trigger) ekleyebilir. CREATE POLICY "Kullanıcı kendi kütüphanesini görebilir" ON user_library FOR SELECT USING (user_id = current_setting('app.current_user_id', true)::uuid);-- DERS İLERLEMESİ GÜVENLİĞİ CREATE POLICY "Kullanıcı kendi ilerlemesini görebilir" ON lesson_progress FOR SELECT USING (user_id = current_setting('app.current_user_id', true)::uuid);-- Kullanıcı sadece kendi ders ilerlemesini yaratabilir ve güncelleyebilir (İzlediği saniyeyi kaydetmek için) CREATE POLICY "Kullanıcı kendi ilerlemesini güncelleyebilir" ON lesson_progress FOR ALL USING (user_id = current_setting('app.current_user_id', true)::uuid);-- ========================================================================= -- CRAFTORA VERİTABANI KURULUMU - BÖLÜM 8 (SATICI ABONELİKLERİ / SAAS) -- =========================================================================-- 1. ABONELİK DURUMLARI (ENUM) CREATE TYPE sub_status AS ENUM ('active', 'past_due', 'canceled', 'unpaid');-- 2. SATICI ABONELİKLERİ TABLOSU CREATE TABLE seller_subscriptions ( id UUID PRIMARY KEY DEFAULT uuid_generate_v4(), shop_id UUID UNIQUE NOT NULL REFERENCES shops(id) ON DELETE CASCADE, -- Bir mağazanın tek abonelik kaydı olur stripe_subscription_id VARCHAR(255) UNIQUE, -- CTO DOKUNUŞU: Bankadaki (Stripe) otomatik çekim talimatının kodustatus sub_status DEFAULT 'active', current_period_end TIMESTAMP WITH TIME ZONE NOT NULL, -- Bu ayki paketin bitiş tarihi grace_period_end TIMESTAMP WITH TIME ZONE, -- 7 Günlük ek süre (Fatura ödenmezse dükkanı hemen kapatmamak için) amount DECIMAL(10,2) DEFAULT 25.00, -- Aylık ücret currency VARCHAR(3) DEFAULT 'USD', created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP, updated_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP, CONSTRAINT check_sub_amount_positive CHECK (amount >= 0));-- 3. OTOPİLOT ROBOTU (SAAT GÜNCELLEYİCİ) CREATE TRIGGER set_seller_sub_updated_at BEFORE UPDATE ON seller_subscriptions FOR EACH ROW EXECUTE FUNCTION update_updated_at_column();-- 4. ÇELİK YELEK (RLS - FİNANSAL GİZLİLİK) ALTER TABLE seller_subscriptions ENABLE ROW LEVEL SECURITY;-- KURAL: Satıcı SADECE kendi dükkanının abonelik faturasını/durumunu görebilir. -- DİKKAT: INSERT, UPDATE, DELETE yok! Aboneliği sadece Stripe'dan gelen Webhook (Backend) güncelleyebilir. CREATE POLICY "Satıcılar kendi abonelik durumlarını görebilir" ON seller_subscriptions FOR SELECT USING (shop_id IN (SELECT id FROM shops WHERE user_id = current_setting('app.current_user_id', true)::uuid));-- 1. Sütunun adındaki "Stripe" kelimesini atıp evrensel (Provider) ismine çeviriyoruz: ALTER TABLE seller_subscriptions RENAME COLUMN stripe_subscription_id TO provider_subscription_id;-- 2. Bu aboneliğin hangi bankadan (Iyzico mu, Stripe mı) yapıldığını bilmek için sağlayıcı sütununu ekliyoruz: ALTER TABLE seller_subscriptions ADD COLUMN payment_provider VARCHAR(50) DEFAULT 'stripe'; -- Satıcının kaydolduğu pos firması (Örn: 'iyzico')-- ========================================================================= -- CRAFTORA VERİTABANI KURULUMU - BÖLÜM 9 (AKILLI SEPET / CART ITEMS) -- =========================================================================-- 1. SEPET ÜRÜNLERİ TABLOSU CREATE TABLE cart_items ( id UUID PRIMARY KEY DEFAULT uuid_generate_v4(), user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE, product_id UUID NOT NULL REFERENCES products(id) ON DELETE CASCADE, quantity INT DEFAULT 1, created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP, updated_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,-- GÜVENLİK VE MANTIK KALKANLARI CONSTRAINT check_quantity_positive CHECK (quantity > 0), -- Miktar eksi veya sıfır olamaz! UNIQUE(user_id, product_id) -- Aynı ürün sepete ikinci kez ayrı satır olarak eklenmesin);-- 2. İNDEKS (PERFORMANS) CREATE INDEX idx_cart_items_user ON cart_items(user_id); -- Sepet sayfasını salisede açmak için-- 3. OTOPİLOT ROBOTLARI-- Robot A: Saat Güncelleyici (Terk edilmiş sepetleri bulmak için çok kritik) CREATE TRIGGER set_cart_updated_at BEFORE UPDATE ON cart_items FOR EACH ROW EXECUTE FUNCTION update_updated_at_column();-- Robot B: ZEKİ MÜŞTERİ KORUMASI (Zaten sahip olunan ürünü sepete aldırtmaz!) CREATE OR REPLACE FUNCTION prevent_duplicate_purchase() RETURNS TRIGGER AS $$ BEGIN -- Kullanıcının kütüphanesinde bu ürün var mı diye kontrol et IF EXISTS (SELECT 1 FROM user_library WHERE user_id = NEW.user_id AND product_id = NEW.product_id) THEN RAISE EXCEPTION 'Bu ürün zaten kütüphanenizde mevcut!'; END IF; RETURN NEW; END; $$ LANGUAGE plpgsql;CREATE TRIGGER trg_check_already_owned BEFORE INSERT OR UPDATE ON cart_items FOR EACH ROW EXECUTE FUNCTION prevent_duplicate_purchase();-- 4. ÇELİK YELEKLER (RLS - GÜVENLİK) ALTER TABLE cart_items ENABLE ROW LEVEL SECURITY;-- KURAL 1: Kullanıcı sadece KENDİ sepetindeki ürünleri görebilir CREATE POLICY "Kullanıcılar kendi sepetini görebilir" ON cart_items FOR SELECT USING (user_id = current_setting('app.current_user_id', true)::uuid);-- KURAL 2: Kullanıcı sadece KENDİ sepetine ürün ekleyebilir/çıkarabilir/miktar güncelleyebilir CREATE POLICY "Kullanıcılar kendi sepetini yönetebilir" ON cart_items FOR ALL USING (user_id = current_setting('app.current_user_id', true)::uuid);CREATE TABLE coupons ( id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),-- Hangi ürüne ait? product_id UUID NOT NULL REFERENCES products(id) ON DELETE CASCADE, -- Kuponu kim oluşturdu? (Güvenlik için) shop_id UUID NOT NULL REFERENCES shops(id) ON DELETE CASCADE, -- Kupon Kodu code VARCHAR(50) NOT NULL, -- İndirim Tipi discount_type VARCHAR(10) NOT NULL, -- 'percent' veya 'fixed' discount_value DECIMAL(10,2) NOT NULL, -- %20 için 20.00, 10$ için 10.00 -- Kullanım Limiti max_uses INT DEFAULT NULL, -- NULL = sınırsız used_count INT DEFAULT 0, -- Kaç kişi kullandı? -- Geçerlilik Tarihi starts_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP, expires_at TIMESTAMP WITH TIME ZONE DEFAULT NULL, -- NULL = süresiz is_active BOOLEAN DEFAULT TRUE, created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP, -- Güvenlik Kalkanlari CONSTRAINT check_discount_type CHECK (discount_type IN ('percent', 'fixed')), CONSTRAINT check_discount_value CHECK (discount_value > 0), CONSTRAINT check_percent_max CHECK (discount_type != 'percent' OR discount_value <= 100), CONSTRAINT unique_coupon_per_product UNIQUE (product_id, code) -- Aynı üründe aynı kod olamaz);-- Kişi başı 1 kez kullanım takibi CREATE TABLE coupon_uses ( id UUID PRIMARY KEY DEFAULT uuid_generate_v4(), coupon_id UUID NOT NULL REFERENCES coupons(id) ON DELETE CASCADE, user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE, order_id UUID NOT NULL REFERENCES orders(id) ON DELETE CASCADE, used_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,UNIQUE(coupon_id, user_id) -- Kişi başı 1 kez!);-- İndeksler CREATE INDEX idx_coupons_product ON coupons(product_id); CREATE INDEX idx_coupons_code ON coupons(code);-- Otopilot: Kullanıldıkça sayacı artır CREATE OR REPLACE FUNCTION increment_coupon_usage() RETURNS TRIGGER AS $$ BEGIN UPDATE coupons SET used_count = used_count + 1 WHERE id = NEW.coupon_id; RETURN NEW; END; $$ LANGUAGE plpgsql;CREATE TRIGGER trg_increment_coupon_usage AFTER INSERT ON coupon_uses FOR EACH ROW EXECUTE FUNCTION increment_coupon_usage();-- RLS ALTER TABLE coupons ENABLE ROW LEVEL SECURITY; ALTER TABLE coupon_uses ENABLE ROW LEVEL SECURITY;-- Kuponları herkes görebilir (Sepette kod girerken) CREATE POLICY "Aktif kuponlar herkese açık" ON coupons FOR SELECT USING (is_active = TRUE);-- Sadece mağaza sahibi kendi ürününe kupon ekleyebilir CREATE POLICY "Satıcı kendi kuponlarını yönetebilir" ON coupons FOR ALL USING (shop_id IN ( SELECT id FROM shops WHERE user_id = current_setting('app.current_user_id', true)::uuid ));-- Kupon kullanım geçmişi sadece alıcıya özel CREATE POLICY "Kullanıcı kendi kupon geçmişini görebilir" ON coupon_uses FOR SELECT USING (user_id = current_setting('app.current_user_id', true)::uuid);CREATE TABLE notifications ( id UUID PRIMARY KEY DEFAULT uuid_generate_v4(), user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,-- Bildirim Tipi type VARCHAR(50) NOT NULL, -- 'sale_completed', 'new_follower', 'new_review', -- 'new_question', 'media_liked', 'media_commented', -- 'contest_result', 'order_completed' -- Bildirim İçeriği title VARCHAR(255) NOT NULL, -- "Yeni Satış! 🎉" body TEXT NOT NULL, -- "Ali, C++ Kursunu satın aldı" -- Hangi içeriğe ait? (Tıklayınca nereye gitsin?) reference_type VARCHAR(50), -- 'order', 'media', 'product', 'shop', 'contest' reference_id UUID, -- İlgili kaydın ID'si -- Durum is_read BOOLEAN DEFAULT FALSE, read_at TIMESTAMP WITH TIME ZONE, created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP, -- Güvenlik CONSTRAINT check_notification_type CHECK (type IN ( 'sale_completed', 'new_follower', 'new_review', 'new_question', 'media_liked', 'media_commented', 'contest_result', 'order_completed' )));-- İndeksler CREATE INDEX idx_notifications_user ON notifications(user_id, created_at DESC); CREATE INDEX idx_notifications_unread ON notifications(user_id, is_read) WHERE is_read = FALSE; -- Sadece okunmamışları hızlı bulmak içinCREATE TABLE notification_deliveries ( id UUID PRIMARY KEY DEFAULT uuid_generate_v4(), notification_id UUID NOT NULL REFERENCES notifications(id) ON DELETE CASCADE,-- Kanal channel VARCHAR(20) NOT NULL, -- 'push', 'email', 'in_app' -- Durum status VARCHAR(20) DEFAULT 'pending', -- 'pending', 'sent', 'failed' -- Gönderim Detayı provider VARCHAR(50), -- 'firebase', 'sendgrid', 'resend' provider_message_id VARCHAR(255), -- Sağlayıcının verdiği mesaj ID'si error_message TEXT, -- Başarısız olursa neden? sent_at TIMESTAMP WITH TIME ZONE, created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP, CONSTRAINT check_channel CHECK (channel IN ('push', 'email', 'in_app')), CONSTRAINT check_status CHECK (status IN ('pending', 'sent', 'failed')));CREATE INDEX idx_deliveries_notification ON notification_deliveries(notification_id); CREATE INDEX idx_deliveries_pending ON notification_deliveries(status) WHERE status = 'pending'; -- Bekleyen gönderimleri hızlı bulmak içinCREATE TABLE user_device_tokens ( id UUID PRIMARY KEY DEFAULT uuid_generate_v4(), user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,token TEXT NOT NULL, -- Firebase FCM token device_type VARCHAR(20) NOT NULL, -- 'ios', 'android', 'web' device_id VARCHAR(255), -- Cihaz ID'si is_active BOOLEAN DEFAULT TRUE, last_used_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP, created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP, UNIQUE(user_id, device_id), -- Aynı cihaz 2 kez kayıt olmasın CONSTRAINT check_device_type CHECK (device_type IN ('ios', 'android', 'web')));CREATE INDEX idx_device_tokens_user ON user_device_tokens(user_id) WHERE is_active = TRUE;ALTER TABLE notifications ENABLE ROW LEVEL SECURITY; ALTER TABLE notification_deliveries ENABLE ROW LEVEL SECURITY; ALTER TABLE user_device_tokens ENABLE ROW LEVEL SECURITY;-- Kullanıcı sadece kendi bildirimlerini görebilir CREATE POLICY "Kullanıcı kendi bildirimlerini görebilir" ON notifications FOR SELECT USING (user_id = current_setting('app.current_user_id', true)::uuid);-- Kullanıcı kendi bildirimini okundu yapabilir CREATE POLICY "Kullanıcı bildirimini okundu yapabilir" ON notifications FOR UPDATE USING (user_id = current_setting('app.current_user_id', true)::uuid);-- Gönderim geçmişi sadece sistem görür (Policy yok = sadece backend erişir)-- Kullanıcı kendi cihaz tokenlarını yönetebilir CREATE POLICY "Kullanıcı kendi tokenlarını yönetebilir" ON user_device_tokens FOR ALL USING (user_id = current_setting('app.current_user_id', true)::uuid);DOCKER CONTAINER DURUMU Container Port Durum postgres_server 5432 ■ Healthy craftora_minio 9000/9001 ■ Healthy craftora_redis 6379 ■ Healthy craftora_rabbitmq 5672/15672 ■ Healthy craftora_elasticsearch 9200 ■ Healthy craftora_nginx 80/443 ■ Kurulu craftora_api 8080 ■ Henüz yokA■AMA 1 — ALTYAPI'n■n sonuna geldik. dotnet build ba■ar■yla tamamland■. S■radaki ad■m: UserRole ve di■er ENUM'lar■ entity'lere ba■lamak, ard■ndan ServiceExtensions'a AppDbContext'i ekleyip A■AMA 2 AUTH'a geçmek. ■ TAMAMLANAN ADIMLAR ■ Docker Compose kurulumu (PostgreSQL, MinIO, Redis, RabbitMQ, Elasticsearch, Nginx) ■ .NET 9 projesi olu■turuldu ve klasör yap■s■ kuruldu ■ appsettings.json, appsettings.Development.json, appsettings.Production.json yaz■ld■ ■ .gitignore hassas dosyalar için güncellendi ■ Extensions/ServiceExtensions.cs — tüm servis kay■tlar■ (JWT, Redis, RabbitMQ, MinIO, CORS, Rate Limiting) ■ Extensions/MiddlewareExtensions.cs — middleware pipeline (Security headers, CORS, Auth, Swagger vb.) ■ Program.cs — Serilog yap■land■rmas■, Kestrel ayarlar■, uygulama ba■lang■c■ ■ Middleware/CraftoraExceptions.cs — 9 özel exception s■n■f■ (NotFoundException, UnauthorizedException vb.) ■ Middleware/ExceptionMiddleware.cs — Global hata yakalay■c■ (Dev/Prod ayr■m■, Serilog entegrasyonu) ■ Data/AppDbContext.cs — Scaffold ile DB'den otomatik üretildi (28 tablo, tüm ili■kiler) ■ Models/Entities/ — 31 entity s■n■f■ scaffold ile üretildi ■ Models/Enums/ — UserRole, ProductType, MediaStatus, OrderStatus, PaymentStatus, SubStatus ■ dotnet build — Ba■ar■l■! ■■ ■UAN YAPILIYOR ■ ENUM'lar■ entity'lere ba■lama (UserRole → User.cs, ProductType → Product.cs vb.) ■ AppDbContext'e ENUM mapping'lerini ekleme ■ ServiceExtensions'a AppDbContext kayd■n■ aktif etme ■ RLS Middleware (her request'te SET app.current_user_id)Craftora API projemizde (.NET 9) AŞAMA 1 altyapısının son adımındayız. PostgreSQL veritabanında oluşturduğumuz özel ENUM tiplerini C# tarafına bağlamamız gerekiyor. Scaffold komutu bunları string olarak çekti, şimdi onları gerçek C# enum'larına çevireceğiz.Lütfen bana şu 4 adımın kodlarını eksiksiz olarak ver:Models/Enums klasöründe oluşturulacak şu 6 Enum sınıfının kodları:UserRole (user, seller, admin)ProductType (digital_file, course)MediaStatus (processing, ready, failed)OrderStatus (pending, completed, failed, refunded)PaymentStatusType (processing, succeeded, failed, refunded)SubStatus (active, past_due, canceled, unpaid)Scaffold edilmiş Entity'lerdeki değişiklikler (Sadece değişen satırları göster):User.cs (string Role -> UserRole Role)Product.cs (string Type -> ProductType Type)CourseLesson.cs (string Status -> MediaStatus Status)Order.cs (string Status -> OrderStatus Status)Payment.cs (string Status -> PaymentStatusType Status)SellerSubscription.cs (string Status -> SubStatus Status)Data/AppDbContext.cs dosyası içinde OnModelCreating metodunda PostgreSQL Enum eşleştirmeleri (HasPostgresEnum).Extensions/ServiceExtensions.cs dosyası içinde Npgsql veritabanı bağlantısı kurulurken NpgsqlDataSourceBuilder ile bu Enum'ların global olarak sisteme kaydedilmesi (MapEnum).Lütfen sadece eklenecek/değişecek kodları ve dosya yollarını ver, gereksiz açıklamalardan kaçın. Clean code prensiplerine sadık kal.
No tasks in progress


 CRAFTORA BACKEND
Geli■tirme Plan■ ve ■lerleme Raporu
TikTok + Shopify + Udemy Benzeri Sosyal Ticaret Platformu
■ TEKNOLOJ■ STACK
Teknoloji Görev Durum
.NET 9 (C#) Ana Backend API ■ Kurulu
PostgreSQL 16 Ana Veritaban■ (28 tablo) ■ Kurulu
Redis 7 Cache + Token Blacklist ■ Kurulu
RabbitMQ 3 Mesaj Kuyru■u ■ Kurulu
MinIO Dosya Depolama (S3 uyumlu) ■ Kurulu
Elasticsearch 8.13 Arama Motoru ■ Kurulu
Nginx Reverse Proxy ■ Kurulu
Serilog Structured Logging ■ Kurulu
EF Core 9 ORM (Database First) ■ Kurulu
MassTransit RabbitMQ Entegrasyonu ■ Kurulu
JWT Bearer Kimlik Do■rulama ■ Kurulu
FluentValidation Input Validasyonu ■ Kurulu
■ PROJE HAKKINDA
Craftora, kullan■c■lar■n video izlerken ürün sat■n alabildi■i sosyal ticaret platformudur. TikTok'un video
ak■■■, Shopify'■n ma■aza altyap■s■ ve Udemy'nin kurs sistemi tek çat■ alt■nda birle■tirilmi■tir.
Sat■c■lar video yükleyerek ürünlerini tan■t■r, izleyiciler an■nda sat■n alabilir.
■■ VER■TABANI ÖZET■ (28 Tablo)
Bölüm Tablolar
Kullan■c■ Sistemi users, user_sessions, login_attempts
Ma■aza shops, subscriptions, shop_visits
Ürünler products, course_sections, course_lessons, reviews, product_qa
Medya/Reels media, media_likes, media_saves, media_comments, media_watch_history
Oyunla■t■rma user_points, point_logs, contests, contest_results
Sipari■/Ödeme orders, payments
Kütüphane user_library, lesson_progress
SaaS Abonelik seller_subscriptions
Sepet cart_items
Kupon coupons, coupon_uses
Bildirim notifications, notification_deliveries, user_device_tokens



-- 1. EKLENTİLER (EXTENSIONS)
CREATE EXTENSION IF NOT EXISTS "uuid-ossp"; -- UUID (rastgele benzersiz ID) oluşturmak için gerekli eklenti
CREATE EXTENSION IF NOT EXISTS "citext"; -- Büyük/küçük harf duyarsız, süper hızlı metin (email) araması için eklenti

-- 2. ÖZEL VERİ TİPLERİ (ENUMS)
CREATE TYPE user_role AS ENUM ('user', 'seller', 'admin'); -- Kullanıcı yetki seviyelerini belirlediğimiz sabit liste

-- 3. KULLANICILAR TABLOSU (USERS)
CREATE TABLE users (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(), -- Her kullanıcıya özel, tahmin edilemez şifreli kimlik numarası
    email CITEXT UNIQUE NOT NULL, -- Kullanıcının e-posta adresi (CITEXT: AHMET@gmail.com ile ahmet@gmail.com aynı sayılır)
    full_name VARCHAR(100), -- Kullanıcının ad ve soyadı bilgisi
    avatar_url TEXT, -- Profil fotoğrafının tutulduğu bulut (Storage) linki
    role user_role DEFAULT 'user', -- Sisteme kayıt olan herkes varsayılan olarak 'user' (normal müşteri) başlar
    auth_provider VARCHAR(50) DEFAULT 'email', -- Sisteme nereden kayıt oldu? (email, google, apple, facebook)
    provider_id VARCHAR(255) UNIQUE, -- Google/Apple gibi yerlerden gelen özel ID numarası
    password_hash TEXT, -- Eğer email ile kayıt olduysa, şifresinin kriptolanmış (kırılmaz) hali
    is_email_verified BOOLEAN DEFAULT FALSE, -- Email adresine giden kodu (OTP) doğru girdi mi?
    locked_until TIMESTAMP WITH TIME ZONE, -- Hacker saldırısı olursa hesabı şu saate kadar dondur (Brute-Force koruması)
    stripe_customer_id VARCHAR(255), -- Stripe (Ödeme) tarafındaki müşteri cüzdan kodu (Alışveriş için)
    stripe_account_id VARCHAR(255), -- Satıcı ise paranın yatacağı Stripe IBAN/Hesap kodu
    preferences JSONB DEFAULT '{}'::jsonb, -- Tema, dil, bildirim gibi mobil uygulama ayarlarının tutulduğu esnek depo
    is_active BOOLEAN DEFAULT TRUE, -- Hesap silinirse FALSE olur (Soft Delete), veriler gerçekten silinmez
    last_login_at TIMESTAMP WITH TIME ZONE, -- Sisteme en son ne zaman giriş yaptı?
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP, -- Hesabın oluşturulma (kayıt) tarihi
    updated_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP, -- Profilde yapılan en son değişikliğin tarihi
    
    -- AKILLI GÜVENLİK KURALI: 
    -- Eğer Google/Apple ile değil de normal email ile giriyorsa, şifre boş OLAMAZ!
    CONSTRAINT check_password_if_email CHECK (
        (auth_provider = 'email' AND password_hash IS NOT NULL) OR 
        (auth_provider != 'email')
    )
);

-- 4. KULLANICI OTURUMLARI TABLOSU (USER SESSIONS)
CREATE TABLE user_sessions (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(), -- Oturuma ait benzersiz ID
    user_id UUID REFERENCES users(id) ON DELETE CASCADE, -- Bu oturum hangi kullanıcıya ait? (Kullanıcı silinirse oturum da silinir)
    refresh_token TEXT NOT NULL, -- Kullanıcıyı her seferinde şifre girmekten kurtaran uzun yetki anahtarı
    device_id VARCHAR(255), -- Kullanıcının girdiği telefonun veya bilgisayarın benzersiz cihaz kodu
    ip_address INET, -- Güvenlik için kullanıcının girdiği internet IP adresi
    user_agent TEXT, -- Hangi tarayıcıdan (Chrome/Safari) veya işletim sisteminden (iOS/Android) giriyor?
    expires_at TIMESTAMP WITH TIME ZONE NOT NULL, -- Bu oturumun (token'ın) son kullanma tarihi (Örn: 30 gün sonra biter)
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP -- Bu oturumun açıldığı anın tarihi
);

-- 5. HATALI GİRİŞ DENEMELERİ TABLOSU (LOGIN ATTEMPTS)
CREATE TABLE login_attempts (
    email CITEXT PRIMARY KEY, -- Hangi e-posta adresine saldırı yapılıyor/deneniyor?
    attempt_count INT DEFAULT 1, -- Kaç kere yanlış şifre girildi?
    last_attempt_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP -- En son hatalı giriş denemesi ne zaman yapıldı?
);


-- BİSMİLLAH: CRAFTORA VERİTABANI KURULUMU - BÖLÜM 2

-- ==========================================
-- 1. İNDEKS KAVŞAKLARI (PERFORMANS VE HIZ)
-- Milyonlarca veri içinde aramaları milisaniyelere düşüren arama motorları
-- ==========================================

-- Sosyal medya ile giriş yapanları anında bulmak için B-Tree İndeksi
CREATE INDEX idx_users_provider_id ON users(provider_id);

-- Mobil JSON ayarlarında ("Karanlık mod açık mı?") süper hızlı arama yapmak için GIN İndeksi
CREATE INDEX idx_users_preferences ON users USING GIN (preferences);

-- Bir kullanıcının açık olan oturumlarını şıp diye bulmak için
CREATE INDEX idx_user_sessions_user_id ON user_sessions(user_id);

-- Gelen Refresh Token'ın veritabanında olup olmadığını saliselik sürede doğrulamak için
CREATE INDEX idx_user_sessions_token ON user_sessions(refresh_token);


-- ==========================================
-- 2. TETİKLEYİCİLER (OTOMASYON - TRIGGERS)
-- Geliştirici hata yapsa bile veritabanının kendi kendini düzeltmesini sağlayan robotlar
-- ==========================================

-- Önce bir "Tarih Güncelleyen Robot (Fonksiyon)" üretiyoruz
CREATE OR REPLACE FUNCTION update_updated_at_column()
RETURNS TRIGGER AS $$
BEGIN
    NEW.updated_at = CURRENT_TIMESTAMP; -- Yeni verinin updated_at sütununu şu anki saat yap
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

-- Şimdi bu robotu 'users' tablosuna bağlıyoruz: "Her UPDATE işleminden hemen ÖNCE bu robotu çalıştır"
CREATE TRIGGER set_users_updated_at
BEFORE UPDATE ON users
FOR EACH ROW
EXECUTE FUNCTION update_updated_at_column();


-- ==========================================
-- 3. SATIR BAZLI GÜVENLİK (RLS - ROW LEVEL SECURITY)
-- Hackerları veritabanı kapısında durduran çelik yeleğimiz
-- ==========================================

-- Tablolarda RLS kalkanını aktif ediyoruz
ALTER TABLE users ENABLE ROW LEVEL SECURITY;
ALTER TABLE user_sessions ENABLE ROW LEVEL SECURITY;

-- KURAL 1: Kullanıcı Profillerini Görme (SELECT)
-- Herkes (sisteme giriş yapmamış anonim biri dahil) aktif kullanıcıların profilini görebilir
CREATE POLICY "Aktif kullanıcıları herkes görebilir" 
ON users FOR SELECT 
USING (is_active = TRUE);

-- KURAL 2: Profil Güncelleme (UPDATE)
-- (Not: Backend kodumuzda, sisteme giriş yapan kişinin ID'sini 'app.current_user_id' adında bir veritabanı değişkenine atayacağız)
-- Kullanıcı SADECE kendi satırındaki verileri (kendi ID'si eşleşiyorsa) değiştirebilir
CREATE POLICY "Kullanıcı sadece kendi profilini güncelleyebilir" 
ON users FOR UPDATE 
USING (id = current_setting('app.current_user_id', true)::uuid);

-- KURAL 3: Oturumları Görme ve Silme (SESSION GİZLİLİĞİ)
-- Oturumlar (Token'lar) aşırı gizlidir. Sadece sahibi kendi token'ını görebilir ve silebilir (Çıkış yapma)
CREATE POLICY "Kullanıcı sadece kendi oturumlarını görebilir" 
ON user_sessions FOR SELECT 
USING (user_id = current_setting('app.current_user_id', true)::uuid);

CREATE POLICY "Kullanıcı sadece kendi oturumlarını silebilir" 
ON user_sessions FOR DELETE 
USING (user_id = current_setting('app.current_user_id', true)::uuid);



SELECT full_name, created_at, updated_at FROM users;






-- CRAFTORA VERİTABANI KURULUMU - BÖLÜM 3 (MAĞAZA EKOSİSTEMİ)

-- 1. MAĞAZALAR TABLOSU (SHOPS)
CREATE TABLE shops (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(), -- Mağaza kimlik numarası
    user_id UUID UNIQUE NOT NULL REFERENCES users(id) ON DELETE CASCADE, -- Mağaza sahibi (1 kullanıcı = 1 mağaza kuralı UNIQUE ile sağlandı)
    shop_name VARCHAR(100) NOT NULL, -- Mağazanın görünen adı
    slug CITEXT UNIQUE NOT NULL, -- URL adresi (Örn: craftora.com/magza-adi). CITEXT sayesinde büyük/küçük harf duyarsız ve hızlıdır.
    external_url VARCHAR(255), -- Varsa harici web sitesi linki
    short_description VARCHAR(255), -- Mağaza kartlarında görünecek kısa özet
    description TEXT, -- Mağaza ana açıklama metni
    about_content TEXT, -- HTML destekli zengin "Hakkımızda" içeriği
    social_links JSONB DEFAULT '{}'::jsonb, -- Instagram, TikTok vb. linklerin tutulduğu esnek JSON deposu
    logo_url TEXT, -- Mağaza logosunun bulut linki
    banner_url TEXT, -- Mağaza kapak fotoğrafının bulut linki
    follower_count INT DEFAULT 0, -- PERFORMANS: Her seferinde sayım yapmamak için otomatik güncellenen takipçi sayısı
    rating DECIMAL(3,2) DEFAULT 0.0, -- PERFORMANS: Mağaza puan ortalaması (Örn: 4.85)
    is_verified BOOLEAN DEFAULT FALSE, -- CTO DOKUNUŞU: Mavi Tik (Onaylı Mağaza) durumu
    is_active BOOLEAN DEFAULT TRUE, -- Mağaza donduruldu mu?
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP, -- Kuruluş tarihi
    updated_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP -- Son düzenleme tarihi
);

-- 2. ABONELİKLER TABLOSU (SUBSCRIPTIONS)
CREATE TABLE subscriptions (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    shop_id UUID NOT NULL REFERENCES shops(id) ON DELETE CASCADE, -- Takip edilen mağaza
    user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE, -- Takip eden kullanıcı
    wants_notifications BOOLEAN DEFAULT TRUE, -- CTO DOKUNUŞU: Zil butonu (Yeni ürün bildirimi gelsin mi?)
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    
    -- Bir kullanıcı bir mağazayı sadece bir kez takip edebilir:
    CONSTRAINT unique_subscription UNIQUE (shop_id, user_id)
);

-- 3. MAĞAZA ZİYARETLERİ TABLOSU (SHOP_VISITS)
CREATE TABLE shop_visits (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    shop_id UUID NOT NULL REFERENCES shops(id) ON DELETE CASCADE,
    user_id UUID REFERENCES users(id) ON DELETE SET NULL, -- Üye olan ziyaretçi (Nullable: Üye olmayanlar için boş kalabilir)
    ip_address INET, -- CTO DOKUNUŞU: Üye olmayan anonim ziyaretçileri IP üzerinden takip etmek için
    visited_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP -- Ziyaret saati
);


-- 4. İNDEKS KAVŞAKLARI (HIZ)
CREATE INDEX idx_shops_slug ON shops(slug); -- Mağaza URL aramalarını ışık hızına çıkarır
CREATE INDEX idx_shop_visits_composite ON shop_visits(shop_id, visited_at); -- Satıcı paneli grafiklerini hızlandırır
-- Kullanıcıların arama çubuğunda mağaza adıyla arama yapmasını hızlandırmak için:
CREATE INDEX idx_shops_name ON shops(shop_name);

-- 5. OTOMATİK ABONE SAYACI (TRIGGER FUNCTION)
CREATE OR REPLACE FUNCTION sync_follower_count()
RETURNS TRIGGER AS $$
BEGIN
    IF (TG_OP = 'INSERT') THEN
        UPDATE shops SET follower_count = follower_count + 1 WHERE id = NEW.shop_id;
    ELSIF (TG_OP = 'DELETE') THEN
        UPDATE shops SET follower_count = follower_count - 1 WHERE id = OLD.shop_id;
    END IF;
    RETURN NULL;
END;
$$ LANGUAGE plpgsql;

-- Takip etme/çıkma anında sayacı çalıştır
CREATE TRIGGER trg_sync_followers
AFTER INSERT OR DELETE ON subscriptions
FOR EACH ROW EXECUTE FUNCTION sync_follower_count();

-- Mağaza updated_at tetikleyicisi
CREATE TRIGGER set_shops_updated_at
BEFORE UPDATE ON shops
FOR EACH ROW EXECUTE FUNCTION update_updated_at_column();



-- 6. GÜVENLİK KALKANLARI (RLS)
ALTER TABLE shops ENABLE ROW LEVEL SECURITY;
ALTER TABLE subscriptions ENABLE ROW LEVEL SECURITY;
ALTER TABLE shop_visits ENABLE ROW LEVEL SECURITY;

-- Mağazaları herkes görebilir ama sadece sahibi düzenleyebilir
CREATE POLICY "Aktif mağazalar herkese açıktır" ON shops FOR SELECT USING (is_active = TRUE);
CREATE POLICY "Mağaza sahibi dükkanını yönetebilir" ON shops FOR UPDATE 
USING (user_id = current_setting('app.current_user_id', true)::uuid);

-- Abonelik ve Ziyaret gizliliği: Sadece mağaza sahibi görebilir
CREATE POLICY "Satıcı kendi abonelerini görebilir" ON subscriptions FOR SELECT 
USING (shop_id IN (SELECT id FROM shops WHERE user_id = current_setting('app.current_user_id', true)::uuid));

CREATE POLICY "Satıcı kendi trafiğini görebilir" ON shop_visits FOR SELECT 
USING (shop_id IN (SELECT id FROM shops WHERE user_id = current_setting('app.current_user_id', true)::uuid));




-- SADECE YARIŞMALAR TABLOSUNU SİSTEME BAĞLAMA YAMASI (İzole adayı kurtarıyoruz)
ALTER TABLE contests
ADD COLUMN created_by UUID REFERENCES users(id) ON DELETE SET NULL;




-- CRAFTORA VERİTABANI KURULUMU - BÖLÜM 4 (ÜRÜNLER VE KURSLAR)

-- 1. YENİ VERİ TİPLERİ (ENUMS)
CREATE TYPE product_type AS ENUM ('digital_file', 'course');
CREATE TYPE media_status AS ENUM ('processing', 'ready', 'failed'); -- Videolar işlenirken bozuk görünmesin diye

-- 2. ANA ÜRÜNLER TABLOSU (PRODUCTS)
CREATE TABLE products (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    shop_id UUID NOT NULL REFERENCES shops(id) ON DELETE CASCADE,
    type product_type DEFAULT 'digital_file',
    title VARCHAR(255) NOT NULL,
    description TEXT,
    metadata JSONB DEFAULT '{}'::jsonb, -- CTO DOKUNUŞU: E-kitap sayfası, 3D model formatı gibi sınırsız özellikleri buraya gömeceğiz
    price DECIMAL(10,2) NOT NULL,
    currency VARCHAR(3) DEFAULT 'USD',
    cover_image_url TEXT,
    file_url TEXT, -- Dijital dosya ise indirme linki (Kurs ise NULL kalır)
    rating_average DECIMAL(3,2) DEFAULT 0.0, -- OTOPİLOT: Müşteri ana sayfada gezerken hesap yapmakla uğraşmayacak
    review_count INT DEFAULT 0, -- OTOPİLOT: Toplam yorum sayısı
    sales_count INT DEFAULT 0, -- Çok satanları bulmak için
    is_active BOOLEAN DEFAULT TRUE, -- Satıcı ürünü silse bile kütüphaneler bozulmasın diye Soft Delete yapıyoruz
    is_featured BOOLEAN DEFAULT FALSE,
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    
    CONSTRAINT check_price_positive CHECK (price >= 0) -- Fiyat asla eksi olamaz kalkanı!
);


-- 3. KURS BÖLÜMLERİ (Örn: C++ Döngüler)
CREATE TABLE course_sections (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    product_id UUID NOT NULL REFERENCES products(id) ON DELETE CASCADE,
    title VARCHAR(255) NOT NULL,
    sort_order INT NOT NULL, -- Uygulamada hangi sırada görünecek? (1, 2, 3)
    
    UNIQUE(product_id, sort_order) -- Aynı kurs içinde aynı sıra numarası yanlışlıkla girilmesin
);

-- 4. KURS DERSLERİ / VİDEOLARI (Örn: For Döngüsü)
CREATE TABLE course_lessons (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    section_id UUID NOT NULL REFERENCES course_sections(id) ON DELETE CASCADE,
    title VARCHAR(255) NOT NULL,
    video_url TEXT,
    document_url TEXT, -- Varsa ders notu (PDF)
    duration_seconds INT DEFAULT 0,
    is_free_preview BOOLEAN DEFAULT FALSE, -- Ücretsiz tanıtım videosu mu?
    sort_order INT NOT NULL,
    status media_status DEFAULT 'ready', -- Video işlenme durumu
    
    UNIQUE(section_id, sort_order)
);


-- 5. DEĞERLENDİRMELER (Yıldız ve Yorum - Kesin Kurallı)
CREATE TABLE reviews (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    product_id UUID NOT NULL REFERENCES products(id) ON DELETE CASCADE,
    user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    rating INT NOT NULL,
    comment TEXT,
    seller_reply TEXT, -- Satıcının tek bir yanıt hakkı var (Uzatılamaz)
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    
    CONSTRAINT check_rating_range CHECK (rating >= 1 AND rating <= 5), -- Yıldız 1-5 arası olmak ZORUNDA
    CONSTRAINT unique_user_review UNIQUE (product_id, user_id) -- 1 Kullanıcı ürüne SADECE 1 KERE puan verebilir
);

-- 6. SORU VE CEVAP (Kullanıcı ve Satıcı Karşılıklı Sohbeti)
CREATE TABLE product_qa (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    product_id UUID NOT NULL REFERENCES products(id) ON DELETE CASCADE,
    user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    parent_id UUID REFERENCES product_qa(id) ON DELETE CASCADE, -- Eğer yanıtsa hangi mesaja yanıt?
    message TEXT NOT NULL,
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);




-- =========================================================================
-- CRAFTORA VERİTABANI KURULUMU - BÖLÜM 5 (MEDYA, REELS VE OYUNLAŞTIRMA)
-- =========================================================================

-- -------------------------------------------------------------------------
-- 1. MEDYA VE ETKİLEŞİM TABLOLARI (SOSYAL MEDYA MOTORU)
-- -------------------------------------------------------------------------

-- REELS VİDEOLARI
CREATE TABLE media (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    shop_id UUID NOT NULL REFERENCES shops(id) ON DELETE CASCADE,
    product_id UUID REFERENCES products(id) ON DELETE SET NULL, -- Videoda satılan ürün
    video_url TEXT NOT NULL,
    thumbnail_url TEXT,
    view_count INT DEFAULT 0, 
    like_count INT DEFAULT 0, 
    save_count INT DEFAULT 0, 
    comment_count INT DEFAULT 0, -- CTO DOKUNUŞU: Yorum sayacı
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    is_active BOOLEAN DEFAULT TRUE
);

-- REELS BEĞENİLERİ
CREATE TABLE media_likes (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    media_id UUID NOT NULL REFERENCES media(id) ON DELETE CASCADE,
    user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    UNIQUE(media_id, user_id) 
);

-- REELS KAYDETMELERİ
CREATE TABLE media_saves (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    media_id UUID NOT NULL REFERENCES media(id) ON DELETE CASCADE,
    user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    UNIQUE(media_id, user_id)
);

-- REELS YORUMLARI (CTO DOKUNUŞU)
CREATE TABLE media_comments (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    media_id UUID NOT NULL REFERENCES media(id) ON DELETE CASCADE,
    user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    comment_text TEXT NOT NULL,
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);

-- İZLEME GEÇMİŞİ (Günlük Puan Limiti ve Algoritma İçin)
CREATE TABLE media_watch_history (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    media_id UUID NOT NULL REFERENCES media(id) ON DELETE CASCADE,
    watched_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    is_point_earned BOOLEAN DEFAULT FALSE,
    UNIQUE(user_id, media_id) -- Keşfet'te aynı video bir daha çıkmasın diye
);

-- -------------------------------------------------------------------------
-- 2. OYUNLAŞTIRMA VE LİDERLİK TABLOLARI (GAMIFICATION)
-- -------------------------------------------------------------------------

-- KULLANICI PUAN CÜZDANI
CREATE TABLE user_points (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    user_id UUID UNIQUE NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    total_points DECIMAL(12,2) DEFAULT 0.0, 
    current_rank INT DEFAULT 0, 
    current_streak INT DEFAULT 0, -- CTO DOKUNUŞU: Kaç gündür üst üste giriyor (Ateş serisi)
    updated_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);

-- PUAN KAYIT DEFTERİ (Geçmiş)
CREATE TABLE point_logs (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    action_type VARCHAR(50) NOT NULL, 
    points_earned DECIMAL(10,2) NOT NULL,
    reference_id UUID, 
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);

-- YARIŞMALAR VE SONUÇLAR (Senin yakaladığın efsane köprü!)
CREATE TABLE contests (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    title VARCHAR(255) NOT NULL,
    start_date TIMESTAMP WITH TIME ZONE NOT NULL,
    end_date TIMESTAMP WITH TIME ZONE NOT NULL,
    prize_pool TEXT,
    is_active BOOLEAN DEFAULT TRUE
);

CREATE TABLE contest_results (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    contest_id UUID NOT NULL REFERENCES contests(id) ON DELETE CASCADE,
    user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    final_rank INT,
    total_score DECIMAL(12,2),
    reward_claimed BOOLEAN DEFAULT FALSE,
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    UNIQUE(contest_id, user_id)
);

-- -------------------------------------------------------------------------
-- 3. OTOPİLOT ROBOTLARI VE İNDEKS KAVŞAKLARI
-- -------------------------------------------------------------------------

CREATE INDEX idx_media_shop ON media(shop_id);
CREATE INDEX idx_media_product ON media(product_id);
CREATE INDEX idx_point_logs_user_date ON point_logs(user_id, created_at);

-- OTOPİLOT 1: MEDYA SAYAÇLARI (Like, Save ve Yorumları Otomatik Sayar)
CREATE OR REPLACE FUNCTION sync_media_counters() RETURNS TRIGGER AS $$
BEGIN
    IF TG_TABLE_NAME = 'media_likes' THEN
        IF TG_OP = 'INSERT' THEN UPDATE media SET like_count = like_count + 1 WHERE id = NEW.media_id;
        ELSIF TG_OP = 'DELETE' THEN UPDATE media SET like_count = like_count - 1 WHERE id = OLD.media_id; END IF;
    ELSIF TG_TABLE_NAME = 'media_saves' THEN
        IF TG_OP = 'INSERT' THEN UPDATE media SET save_count = save_count + 1 WHERE id = NEW.media_id;
        ELSIF TG_OP = 'DELETE' THEN UPDATE media SET save_count = save_count - 1 WHERE id = OLD.media_id; END IF;
    ELSIF TG_TABLE_NAME = 'media_comments' THEN
        IF TG_OP = 'INSERT' THEN UPDATE media SET comment_count = comment_count + 1 WHERE id = NEW.media_id;
        ELSIF TG_OP = 'DELETE' THEN UPDATE media SET comment_count = comment_count - 1 WHERE id = OLD.media_id; END IF;
    END IF;
    RETURN NULL;
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER trg_media_like_counter AFTER INSERT OR DELETE ON media_likes FOR EACH ROW EXECUTE FUNCTION sync_media_counters();
CREATE TRIGGER trg_media_save_counter AFTER INSERT OR DELETE ON media_saves FOR EACH ROW EXECUTE FUNCTION sync_media_counters();
CREATE TRIGGER trg_media_comment_counter AFTER INSERT OR DELETE ON media_comments FOR EACH ROW EXECUTE FUNCTION sync_media_counters();

-- OTOPİLOT 2: SATICI PUAN ROBOTU (Like Aldıkça 0.5 Kazanır, UPSERT mantığıyla)
CREATE OR REPLACE FUNCTION award_seller_points() RETURNS TRIGGER AS $$
DECLARE v_seller_id UUID;
BEGIN
    SELECT s.user_id INTO v_seller_id FROM media m JOIN shops s ON m.shop_id = s.id WHERE m.id = NEW.media_id;
    
    INSERT INTO point_logs (user_id, action_type, points_earned, reference_id) VALUES (v_seller_id, 'receive_like', 0.5, NEW.media_id);
    
    -- ON CONFLICT: Cüzdanı yoksa yarat, varsa üstüne ekle (UPSERT)
    INSERT INTO user_points (user_id, total_points) VALUES (v_seller_id, 0.5)
    ON CONFLICT (user_id) DO UPDATE SET total_points = user_points.total_points + 0.5, updated_at = CURRENT_TIMESTAMP;
    
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;
CREATE TRIGGER trg_points_on_like AFTER INSERT ON media_likes FOR EACH ROW EXECUTE FUNCTION award_seller_points();

-- OTOPİLOT 3: İZLEYİCİ PUAN ROBOTU (Günlük Limit: 120)
CREATE OR REPLACE FUNCTION award_viewer_points() RETURNS TRIGGER AS $$
DECLARE v_daily_points DECIMAL;
BEGIN
    SELECT COALESCE(SUM(points_earned), 0) INTO v_daily_points FROM point_logs 
    WHERE user_id = NEW.user_id AND action_type = 'watch_reels' AND created_at::date = CURRENT_DATE;

    IF v_daily_points < 120 THEN
        INSERT INTO point_logs (user_id, action_type, points_earned, reference_id) VALUES (NEW.user_id, 'watch_reels', 1.0, NEW.media_id);
        
        INSERT INTO user_points (user_id, total_points) VALUES (NEW.user_id, 1.0)
        ON CONFLICT (user_id) DO UPDATE SET total_points = user_points.total_points + 1.0, updated_at = CURRENT_TIMESTAMP;
        
        NEW.is_point_earned := TRUE;
    END IF;
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;
CREATE TRIGGER trg_points_on_watch BEFORE INSERT ON media_watch_history FOR EACH ROW EXECUTE FUNCTION award_viewer_points();


-- -------------------------------------------------------------------------
-- 4. ÇELİK YELEKLER (RLS POLICIES) - SENİN YAKALADIĞIN EKSİK!
-- -------------------------------------------------------------------------
ALTER TABLE media ENABLE ROW LEVEL SECURITY;
ALTER TABLE media_likes ENABLE ROW LEVEL SECURITY;
ALTER TABLE media_saves ENABLE ROW LEVEL SECURITY;
ALTER TABLE media_comments ENABLE ROW LEVEL SECURITY;
ALTER TABLE media_watch_history ENABLE ROW LEVEL SECURITY;
ALTER TABLE user_points ENABLE ROW LEVEL SECURITY;
ALTER TABLE point_logs ENABLE ROW LEVEL SECURITY;

-- MEDYA (REELS)
CREATE POLICY "Aktif videolar herkese açık" ON media FOR SELECT USING (is_active = TRUE);
CREATE POLICY "Satıcı kendi videosunu yönetebilir" ON media FOR ALL 
USING (shop_id IN (SELECT id FROM shops WHERE user_id = current_setting('app.current_user_id', true)::uuid));

-- BEĞENİ VE KAYDETMELER (GİZLİLİK)
CREATE POLICY "Herkes kendi beğeni/kayıtlarını görebilir" ON media_likes FOR SELECT USING (user_id = current_setting('app.current_user_id', true)::uuid);
CREATE POLICY "Herkes kendi beğeni/kayıtlarını yapabilir" ON media_likes FOR ALL USING (user_id = current_setting('app.current_user_id', true)::uuid);

CREATE POLICY "Herkes kendi kaydettiklerini görebilir" ON media_saves FOR SELECT USING (user_id = current_setting('app.current_user_id', true)::uuid);
CREATE POLICY "Herkes kendi kaydettiklerini yönetebilir" ON media_saves FOR ALL USING (user_id = current_setting('app.current_user_id', true)::uuid);

-- YORUMLAR
CREATE POLICY "Yorumları herkes okuyabilir" ON media_comments FOR SELECT USING (true);
CREATE POLICY "Kullanıcı kendi yorumunu silebilir/düzenleyebilir" ON media_comments FOR ALL USING (user_id = current_setting('app.current_user_id', true)::uuid);

-- OYUNLAŞTIRMA VE LİDERLİK TABLOSU GÜVENLİĞİ
CREATE POLICY "Liderlik tablosunu herkes görebilir" ON user_points FOR SELECT USING (true);
-- DİKKAT: user_points tablosuna UPDATE kuralı yazmıyoruz! Çünkü puanları API değil, sadece veritabanı Trigger'ları (Robotlar) verebilir. Hacker puanını artıramaz!

CREATE POLICY "Kullanıcı sadece kendi puan geçmişini görebilir" ON point_logs FOR SELECT USING (user_id = current_setting('app.current_user_id', true)::uuid);





-- =========================================================================
-- CRAFTORA VERİTABANI KURULUMU - BÖLÜM 6 (SİPARİŞLER VE FİNANS)
-- =========================================================================

-- 1. SİPARİŞ DURUMLARI (ENUM)
CREATE TYPE order_status AS ENUM ('pending', 'completed', 'failed', 'refunded');

-- 2. SİPARİŞLER TABLOSU (ORDERS)
CREATE TABLE orders (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    buyer_id UUID NOT NULL REFERENCES users(id) ON DELETE RESTRICT, -- MÜHENDİSLİK: Kullanıcı silinse bile fatura silinmez!
    product_id UUID NOT NULL REFERENCES products(id) ON DELETE RESTRICT, -- Ürün silinse bile sipariş geçmişi kalır!
    shop_id UUID NOT NULL REFERENCES shops(id) ON DELETE RESTRICT,
    order_number VARCHAR(50) UNIQUE NOT NULL, -- Örn: CRAFT-2026-XYZ123
    
    -- FİNANSAL BÖLÜNME (MUHASEBE)
    amount DECIMAL(10,2) NOT NULL, -- Müşterinin ödediği toplam para (Örn: 100.00)
    currency VARCHAR(3) DEFAULT 'USD',
    platform_fee DECIMAL(10,2) DEFAULT 0.00, -- Craftora'nın cebine giren komisyon (Örn: 10.00)
    seller_earnings DECIMAL(10,2) DEFAULT 0.00, -- Satıcının Stripe hesabına yatacak para (Örn: 90.00)
    
    status order_status DEFAULT 'pending',
    stripe_payment_id VARCHAR(255), -- İade ve iptaller için banka işlem numarası
    invoice_pdf_url TEXT, -- Kesilen e-faturanın PDF linki
    
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    
    -- GÜVENLİK KALKANLARI
    CONSTRAINT check_amount_positive CHECK (amount >= 0),
    CONSTRAINT check_fee_logic CHECK (platform_fee + seller_earnings = amount) -- Toplam tutar, kesintilerle eşleşmek ZORUNDA!
);

-- 3. İNDEKS KAVŞAKLARI (PERFORMANS VE ARAMA HIZI)
CREATE INDEX idx_orders_buyer ON orders(buyer_id); -- Müşterinin "Siparişlerim" sayfasını hızlandırır
CREATE INDEX idx_orders_shop ON orders(shop_id); -- Satıcının "Gelen Siparişler" tablosunu hızlandırır
CREATE INDEX idx_orders_number ON orders(order_number); -- Müşteri hizmetlerinin fatura no ile arama yapması için
CREATE INDEX idx_orders_status ON orders(status);

-- 4. OTOPİLOT ROBOTLARI (OTOMASYON)

-- Saat Güncelleyici
CREATE TRIGGER set_orders_updated_at
BEFORE UPDATE ON orders
FOR EACH ROW EXECUTE FUNCTION update_updated_at_column();

-- EFSANE ROBOT: Sipariş "Completed" (Tamamlandı) olunca çalışır!
CREATE OR REPLACE FUNCTION process_completed_order()
RETURNS TRIGGER AS $$
DECLARE 
    v_seller_id UUID;
BEGIN
    -- Eğer sipariş durumu 'completed' olarak güncellendiyse (veya direkt eklendiyse)
    IF (NEW.status = 'completed' AND (TG_OP = 'INSERT' OR OLD.status != 'completed')) THEN
        
        -- 1. Ürünün satış sayacını (sales_count) 1 artır
        UPDATE products SET sales_count = sales_count + 1 WHERE id = NEW.product_id;
        
        -- 2. Satıcıyı bul
        SELECT user_id INTO v_seller_id FROM shops WHERE id = NEW.shop_id;
        
        -- 3. Satıcıya Oyunlaştırma Modülünden 20 PUAN kazandır! (make_sale aksiyonu)
        INSERT INTO point_logs (user_id, action_type, points_earned, reference_id) 
        VALUES (v_seller_id, 'make_sale', 20.0, NEW.id);
        
        UPDATE user_points SET total_points = total_points + 20.0, updated_at = CURRENT_TIMESTAMP 
        WHERE user_id = v_seller_id;
        
    END IF;
    
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

-- Robotu Siparişler Tablosuna Bağlayalım
CREATE TRIGGER trg_on_order_completed
AFTER INSERT OR UPDATE ON orders
FOR EACH ROW EXECUTE FUNCTION process_completed_order();


-- 5. ÇELİK YELEKLER (RLS - FİNANSAL GİZLİLİK)
ALTER TABLE orders ENABLE ROW LEVEL SECURITY;

-- KURAL 1: Alıcı (Müşteri) SADECE kendi verdiği siparişleri ve faturalarını görebilir
CREATE POLICY "Alıcılar kendi siparişlerini görebilir" ON orders FOR SELECT 
USING (buyer_id = current_setting('app.current_user_id', true)::uuid);

-- KURAL 2: Satıcı SADECE kendi dükkanına gelen siparişleri görebilir
CREATE POLICY "Satıcılar kendi mağaza siparişlerini görebilir" ON orders FOR SELECT 
USING (shop_id IN (SELECT id FROM shops WHERE user_id = current_setting('app.current_user_id', true)::uuid));

-- DİKKAT (CTO KURALI): Kullanıcılar (Alıcı veya Satıcı) sipariş silebilir veya durumunu değiştirebilir mi? ASLA!
-- RLS kalkanında INSERT, UPDATE ve DELETE kurallarını YAZMIYORUZ. 
-- Bu sayede sadece Backend Sunucumuz (Stripe'dan ödeme onayı alınca) siparişi güncelleyebilir. Hacker fiyata veya duruma müdahale edemez.




-- 1. ÖDEME DURUMLARI (ENUM)
CREATE TYPE payment_status_type AS ENUM ('processing', 'succeeded', 'failed', 'refunded');

-- 2. ANA ÖDEMELER TABLOSU (PAYMENTS)
CREATE TABLE payments (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    order_id UUID UNIQUE NOT NULL REFERENCES orders(id) ON DELETE RESTRICT, -- UNIQUE: Bir siparişin SADECE BİR ödeme kaydı olur!
    payment_provider VARCHAR(50) NOT NULL, -- 'stripe', 'iyzico', 'paypal'
    provider_transaction_id VARCHAR(255) UNIQUE, -- Bankanın verdiği efsanevi, kopyalanamaz dekont/işlem numarası
    
    gross_amount DECIMAL(10,2) NOT NULL, -- Karttan çekilen brüt para
    platform_fee_amount DECIMAL(10,2) NOT NULL, -- Banka+Craftora kesintisi
    net_earnings DECIMAL(10,2) NOT NULL, -- Satıcının hesabına yatacak net para
    
    status payment_status_type DEFAULT 'processing',
    error_message TEXT, -- Eğer işlem failed olursa bankanın gönderdiği hata kodu ("Bakiye yetersiz" vb.)
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    
    -- GÜVENLİK KALKANLARI
    CONSTRAINT check_gross_positive CHECK (gross_amount >= 0),
    CONSTRAINT check_payment_math CHECK (gross_amount = platform_fee_amount + net_earnings) -- Muhasebe matematiği ASLA şaşamaz!
);

-- 3. İNDEKS KAVŞAKLARI (PERFORMANS)
CREATE INDEX idx_payments_transaction_id ON payments(provider_transaction_id); -- Bankadan gelen Webhook'ları salisede bulmak için
CREATE INDEX idx_payments_status ON payments(status);

-- 4. OTOPİLOT ROBOTLARI (DOMİNO ETKİSİ)

-- Saat Güncelleyici
CREATE TRIGGER set_payments_updated_at
BEFORE UPDATE ON payments
FOR EACH ROW EXECUTE FUNCTION update_updated_at_column();

-- DOMİNO ROBOTU: Ödeme başarılı olursa, Siparişi de Tamamla!
CREATE OR REPLACE FUNCTION sync_order_status_from_payment()
RETURNS TRIGGER AS $$
BEGIN
    -- Eğer banka ödemesi 'succeeded' olduysa
    IF (NEW.status = 'succeeded' AND (TG_OP = 'INSERT' OR OLD.status != 'succeeded')) THEN
        
        -- Gidip Orders (Sipariş) tablosundaki durumu da 'completed' yapıyoruz.
        -- DİKKAT: Bu UPDATE işlemi, bir önceki aşamada yazdığımız Puan Dağıtma robotunu tetikleyecek!
        UPDATE orders SET status = 'completed' WHERE id = NEW.order_id;
        
    -- Eğer banka 'refunded' (İade) dediyse, siparişi de iptal et
    ELSIF (NEW.status = 'refunded' AND OLD.status != 'refunded') THEN
        UPDATE orders SET status = 'refunded' WHERE id = NEW.order_id;
    END IF;
    
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER trg_sync_order_on_payment
AFTER INSERT OR UPDATE ON payments
FOR EACH ROW EXECUTE FUNCTION sync_order_status_from_payment();


-- 5. ÇELİK YELEKLER (RLS - FİNANSAL GİZLİLİK)
ALTER TABLE payments ENABLE ROW LEVEL SECURITY;

-- KURAL 1: Alıcı sadece KENDİ siparişine bağlı ödeme dekontunu görebilir
CREATE POLICY "Alıcılar dekontunu görebilir" ON payments FOR SELECT 
USING (order_id IN (SELECT id FROM orders WHERE buyer_id = current_setting('app.current_user_id', true)::uuid));

-- KURAL 2: Satıcı sadece KENDİ dükkanına ait satışların ödeme/komisyon dökümünü görebilir
CREATE POLICY "Satıcılar kendi gelir dökümlerini görebilir" ON payments FOR SELECT 
USING (order_id IN (SELECT id FROM orders WHERE shop_id IN (SELECT id FROM shops WHERE user_id = current_setting('app.current_user_id', true)::uuid)));

-- DİKKAT: INSERT, UPDATE, DELETE KESİNLİKLE YOK! Ödeme durumunu sadece Stripe webhook'larından gelen veriyi işleyen arka uç (Backend) kodumuz yapabilir.


INSERT INTO payments (order_id, payment_provider, provider_transaction_id, gross_amount, platform_fee_amount, net_earnings, status)
VALUES (
    (SELECT id FROM orders WHERE order_number = 'PENDING-ORD-002'),
    'stripe',
    'ch_basarili_islem_123',
    100.00,
    10.00,
    90.00,
    'succeeded' -- İŞTE BU KELİME DOMİNOYI BAŞLATACAK!
);


SELECT order_number, status FROM orders WHERE order_number = 'PENDING-ORD-002';

-- SONUÇ 2: C++ Kursunun satış sayısı tekrar artmış mı?
SELECT title, sales_count FROM products WHERE title = 'Sıfırdan İleri Seviye C++ Eğitimi';

-- SONUÇ 3: Ahmet'in cüzdanına ekstra 20 puan daha (Toplam 40.50) gelmiş mi?
SELECT total_points FROM user_points WHERE user_id = (SELECT id FROM users WHERE email = 'ahmet.yilmaz@gmail.com');




-- =========================================================================
-- CRAFTORA VERİTABANI KURULUMU - BÖLÜM 7 (KÜTÜPHANE VE EĞİTİM)
-- =========================================================================

-- -------------------------------------------------------------------------
-- 1. TABLOLAR (MİMARİ)
-- -------------------------------------------------------------------------

-- KULLANICI KÜTÜPHANESİ (SATIN ALINANLAR)
CREATE TABLE user_library (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    product_id UUID NOT NULL REFERENCES products(id) ON DELETE CASCADE,
    purchased_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    last_accessed_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP, -- CTO DOKUNUŞU: Kaldığın yerden devam et!
    
    UNIQUE(user_id, product_id) -- Bir kullanıcı aynı ürüne iki kere sahip olamaz
);

-- DERS İLERLEMESİ (VİDEO İZLEME SÜRELERİ)
CREATE TABLE lesson_progress (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    lesson_id UUID NOT NULL REFERENCES course_lessons(id) ON DELETE CASCADE,
    is_completed BOOLEAN DEFAULT FALSE,
    watched_seconds INT DEFAULT 0,
    completed_at TIMESTAMP WITH TIME ZONE, -- Ne zaman bitirdi?
    updated_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    
    UNIQUE(user_id, lesson_id) -- Bir kullanıcı bir ders için sadece bir kayıt tutabilir
);

-- -------------------------------------------------------------------------
-- 2. İNDEKS KAVŞAKLARI (PERFORMANS)
-- -------------------------------------------------------------------------

CREATE INDEX idx_user_library_accessed ON user_library(user_id, last_accessed_at DESC); -- "Devam Et" rafını saniyede yükler
CREATE INDEX idx_lesson_progress_user ON lesson_progress(user_id, lesson_id);

-- -------------------------------------------------------------------------
-- 3. OTOPİLOT ROBOTLARI (OTOMATİK TESLİMAT VE PUAN)
-- -------------------------------------------------------------------------

-- ROBOT 1: Saat Güncelleyici
CREATE TRIGGER set_progress_updated_at
BEFORE UPDATE ON lesson_progress
FOR EACH ROW EXECUTE FUNCTION update_updated_at_column();

-- EFSANE ROBOT 2: OTOMATİK DİJİTAL TESLİMAT (Sipariş Onaylanınca Çalışır)
CREATE OR REPLACE FUNCTION deliver_product_to_library()
RETURNS TRIGGER AS $$
BEGIN
    -- Sipariş 'completed' statüsüne geçtiyse:
    IF (NEW.status = 'completed' AND (TG_OP = 'INSERT' OR OLD.status != 'completed')) THEN
        -- Ürünü alıcının kütüphanesine ekle (Eğer zaten varsa hata verme, sessizce geç: ON CONFLICT DO NOTHING)
        INSERT INTO user_library (user_id, product_id)
        VALUES (NEW.buyer_id, NEW.product_id)
        ON CONFLICT (user_id, product_id) DO NOTHING;
    END IF;
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER trg_auto_deliver_product
AFTER INSERT OR UPDATE ON orders
FOR EACH ROW EXECUTE FUNCTION deliver_product_to_library();


-- EFSANE ROBOT 3: ÖĞRENCİ PUAN SİSTEMİ (Ders Bitince 2 Puan Verir)
CREATE OR REPLACE FUNCTION reward_lesson_completion()
RETURNS TRIGGER AS $$
BEGIN
    -- Eğer ders ŞU AN tamamlandıysa (Önceden false idi, şimdi true olduysa)
    IF (NEW.is_completed = TRUE AND OLD.is_completed = FALSE) THEN
        
        -- Müşteriye 2 Puan ver (action_type: 'complete_lesson')
        INSERT INTO point_logs (user_id, action_type, points_earned, reference_id)
        VALUES (NEW.user_id, 'complete_lesson', 2.0, NEW.lesson_id);
        
        -- Cüzdanı güncelle (UPSERT - Cüzdanı yoksa yarat)
        INSERT INTO user_points (user_id, total_points) VALUES (NEW.user_id, 2.0)
        ON CONFLICT (user_id) DO UPDATE SET total_points = user_points.total_points + 2.0, updated_at = CURRENT_TIMESTAMP;
        
        -- Tamamlanma saatini şu anki saat yap
        NEW.completed_at = CURRENT_TIMESTAMP;
        
    END IF;
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

-- Bu robotu sadece UPDATE işleminde çalıştırıyoruz (Videoyu izledikçe güncellenecek çünkü)
CREATE TRIGGER trg_reward_on_lesson_complete
BEFORE UPDATE ON lesson_progress
FOR EACH ROW EXECUTE FUNCTION reward_lesson_completion();


-- -------------------------------------------------------------------------
-- 4. ÇELİK YELEKLER (RLS - KORSAN KALKANI)
-- -------------------------------------------------------------------------

ALTER TABLE user_library ENABLE ROW LEVEL SECURITY;
ALTER TABLE lesson_progress ENABLE ROW LEVEL SECURITY;

-- KÜTÜPHANE GÜVENLİĞİ: Kullanıcı KENDİ kütüphanesini görebilir. 
-- DİKKAT: INSERT veya DELETE yok! Ürünü sadece sistem (Orders tablosundaki Trigger) ekleyebilir.
CREATE POLICY "Kullanıcı kendi kütüphanesini görebilir" ON user_library FOR SELECT 
USING (user_id = current_setting('app.current_user_id', true)::uuid);

-- DERS İLERLEMESİ GÜVENLİĞİ
CREATE POLICY "Kullanıcı kendi ilerlemesini görebilir" ON lesson_progress FOR SELECT 
USING (user_id = current_setting('app.current_user_id', true)::uuid);

-- Kullanıcı sadece kendi ders ilerlemesini yaratabilir ve güncelleyebilir (İzlediği saniyeyi kaydetmek için)
CREATE POLICY "Kullanıcı kendi ilerlemesini güncelleyebilir" ON lesson_progress FOR ALL 
USING (user_id = current_setting('app.current_user_id', true)::uuid);




-- =========================================================================
-- CRAFTORA VERİTABANI KURULUMU - BÖLÜM 8 (SATICI ABONELİKLERİ / SAAS)
-- =========================================================================

-- 1. ABONELİK DURUMLARI (ENUM)
CREATE TYPE sub_status AS ENUM ('active', 'past_due', 'canceled', 'unpaid');

-- 2. SATICI ABONELİKLERİ TABLOSU
CREATE TABLE seller_subscriptions (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    shop_id UUID UNIQUE NOT NULL REFERENCES shops(id) ON DELETE CASCADE, -- Bir mağazanın tek abonelik kaydı olur
    stripe_subscription_id VARCHAR(255) UNIQUE, -- CTO DOKUNUŞU: Bankadaki (Stripe) otomatik çekim talimatının kodu
    
    status sub_status DEFAULT 'active',
    current_period_end TIMESTAMP WITH TIME ZONE NOT NULL, -- Bu ayki paketin bitiş tarihi
    grace_period_end TIMESTAMP WITH TIME ZONE, -- 7 Günlük ek süre (Fatura ödenmezse dükkanı hemen kapatmamak için)
    
    amount DECIMAL(10,2) DEFAULT 25.00, -- Aylık ücret
    currency VARCHAR(3) DEFAULT 'USD',
    
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    
    CONSTRAINT check_sub_amount_positive CHECK (amount >= 0)
);

-- 3. OTOPİLOT ROBOTU (SAAT GÜNCELLEYİCİ)
CREATE TRIGGER set_seller_sub_updated_at
BEFORE UPDATE ON seller_subscriptions
FOR EACH ROW EXECUTE FUNCTION update_updated_at_column();

-- 4. ÇELİK YELEK (RLS - FİNANSAL GİZLİLİK)
ALTER TABLE seller_subscriptions ENABLE ROW LEVEL SECURITY;

-- KURAL: Satıcı SADECE kendi dükkanının abonelik faturasını/durumunu görebilir.
-- DİKKAT: INSERT, UPDATE, DELETE yok! Aboneliği sadece Stripe'dan gelen Webhook (Backend) güncelleyebilir.
CREATE POLICY "Satıcılar kendi abonelik durumlarını görebilir" ON seller_subscriptions FOR SELECT 
USING (shop_id IN (SELECT id FROM shops WHERE user_id = current_setting('app.current_user_id', true)::uuid));


-- 1. Sütunun adındaki "Stripe" kelimesini atıp evrensel (Provider) ismine çeviriyoruz:
ALTER TABLE seller_subscriptions 
RENAME COLUMN stripe_subscription_id TO provider_subscription_id;

-- 2. Bu aboneliğin hangi bankadan (Iyzico mu, Stripe mı) yapıldığını bilmek için sağlayıcı sütununu ekliyoruz:
ALTER TABLE seller_subscriptions 
ADD COLUMN payment_provider VARCHAR(50) DEFAULT 'stripe'; -- Satıcının kaydolduğu pos firması (Örn: 'iyzico')






-- =========================================================================
-- CRAFTORA VERİTABANI KURULUMU - BÖLÜM 9 (AKILLI SEPET / CART ITEMS)
-- =========================================================================

-- 1. SEPET ÜRÜNLERİ TABLOSU
CREATE TABLE cart_items (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    product_id UUID NOT NULL REFERENCES products(id) ON DELETE CASCADE,
    quantity INT DEFAULT 1, 
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    
    -- GÜVENLİK VE MANTIK KALKANLARI
    CONSTRAINT check_quantity_positive CHECK (quantity > 0), -- Miktar eksi veya sıfır olamaz!
    UNIQUE(user_id, product_id) -- Aynı ürün sepete ikinci kez ayrı satır olarak eklenmesin
);

-- 2. İNDEKS (PERFORMANS)
CREATE INDEX idx_cart_items_user ON cart_items(user_id); -- Sepet sayfasını salisede açmak için

-- 3. OTOPİLOT ROBOTLARI 

-- Robot A: Saat Güncelleyici (Terk edilmiş sepetleri bulmak için çok kritik)
CREATE TRIGGER set_cart_updated_at
BEFORE UPDATE ON cart_items
FOR EACH ROW EXECUTE FUNCTION update_updated_at_column();

-- Robot B: ZEKİ MÜŞTERİ KORUMASI (Zaten sahip olunan ürünü sepete aldırtmaz!)
CREATE OR REPLACE FUNCTION prevent_duplicate_purchase()
RETURNS TRIGGER AS $$
BEGIN
    -- Kullanıcının kütüphanesinde bu ürün var mı diye kontrol et
    IF EXISTS (SELECT 1 FROM user_library WHERE user_id = NEW.user_id AND product_id = NEW.product_id) THEN
        RAISE EXCEPTION 'Bu ürün zaten kütüphanenizde mevcut!';
    END IF;
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER trg_check_already_owned
BEFORE INSERT OR UPDATE ON cart_items
FOR EACH ROW EXECUTE FUNCTION prevent_duplicate_purchase();

-- 4. ÇELİK YELEKLER (RLS - GÜVENLİK)
ALTER TABLE cart_items ENABLE ROW LEVEL SECURITY;

-- KURAL 1: Kullanıcı sadece KENDİ sepetindeki ürünleri görebilir
CREATE POLICY "Kullanıcılar kendi sepetini görebilir" ON cart_items FOR SELECT 
USING (user_id = current_setting('app.current_user_id', true)::uuid);

-- KURAL 2: Kullanıcı sadece KENDİ sepetine ürün ekleyebilir/çıkarabilir/miktar güncelleyebilir
CREATE POLICY "Kullanıcılar kendi sepetini yönetebilir" ON cart_items FOR ALL 
USING (user_id = current_setting('app.current_user_id', true)::uuid);


CREATE TABLE coupons (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    
    -- Hangi ürüne ait?
    product_id UUID NOT NULL REFERENCES products(id) ON DELETE CASCADE,
    -- Kuponu kim oluşturdu? (Güvenlik için)
    shop_id UUID NOT NULL REFERENCES shops(id) ON DELETE CASCADE,
    
    -- Kupon Kodu
    code VARCHAR(50) NOT NULL,
    
    -- İndirim Tipi
    discount_type VARCHAR(10) NOT NULL, -- 'percent' veya 'fixed'
    discount_value DECIMAL(10,2) NOT NULL, -- %20 için 20.00, 10$ için 10.00
    
    -- Kullanım Limiti
    max_uses INT DEFAULT NULL, -- NULL = sınırsız
    used_count INT DEFAULT 0,  -- Kaç kişi kullandı?
    
    -- Geçerlilik Tarihi
    starts_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    expires_at TIMESTAMP WITH TIME ZONE DEFAULT NULL, -- NULL = süresiz
    
    is_active BOOLEAN DEFAULT TRUE,
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    
    -- Güvenlik Kalkanlari
    CONSTRAINT check_discount_type CHECK (discount_type IN ('percent', 'fixed')),
    CONSTRAINT check_discount_value CHECK (discount_value > 0),
    CONSTRAINT check_percent_max CHECK (discount_type != 'percent' OR discount_value <= 100),
    CONSTRAINT unique_coupon_per_product UNIQUE (product_id, code) -- Aynı üründe aynı kod olamaz
);

-- Kişi başı 1 kez kullanım takibi
CREATE TABLE coupon_uses (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    coupon_id UUID NOT NULL REFERENCES coupons(id) ON DELETE CASCADE,
    user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    order_id UUID NOT NULL REFERENCES orders(id) ON DELETE CASCADE,
    used_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    
    UNIQUE(coupon_id, user_id) -- Kişi başı 1 kez!
);

-- İndeksler
CREATE INDEX idx_coupons_product ON coupons(product_id);
CREATE INDEX idx_coupons_code ON coupons(code);

-- Otopilot: Kullanıldıkça sayacı artır
CREATE OR REPLACE FUNCTION increment_coupon_usage()
RETURNS TRIGGER AS $$
BEGIN
    UPDATE coupons SET used_count = used_count + 1 WHERE id = NEW.coupon_id;
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER trg_increment_coupon_usage
AFTER INSERT ON coupon_uses
FOR EACH ROW EXECUTE FUNCTION increment_coupon_usage();

ALTER TABLE coupons ENABLE ROW LEVEL SECURITY;
ALTER TABLE coupon_uses ENABLE ROW LEVEL SECURITY;

-- Kuponları herkes görebilir (Sepette kod girerken)
CREATE POLICY "Aktif kuponlar herkese açık" ON coupons FOR SELECT 
USING (is_active = TRUE);

-- Sadece mağaza sahibi kendi ürününe kupon ekleyebilir
CREATE POLICY "Satıcı kendi kuponlarını yönetebilir" ON coupons FOR ALL 
USING (shop_id IN (
    SELECT id FROM shops 
    WHERE user_id = current_setting('app.current_user_id', true)::uuid
));

-- Kupon kullanım geçmişi sadece alıcıya özel
CREATE POLICY "Kullanıcı kendi kupon geçmişini görebilir" ON coupon_uses FOR SELECT 
USING (user_id = current_setting('app.current_user_id', true)::uuid);


CREATE TABLE notifications (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),yapay zeka 
    user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    type VARCHAR(50) NOT NULL,
    title VARCHAR(255) NOT NULL,        -- "Yeni Satış! 🎉"
    body TEXT NOT NULL,                 -- "Ali, C++ Kursunu satın aldı"
    
    -- Hangi içeriğe ait? (Tıklayınca nereye gitsin?)
    reference_type VARCHAR(50),         -- 'order', 'media', 'product', 'shop', 'contest'
    reference_id UUID,                  -- İlgili kaydın ID'si
    
    -- Durum
    is_read BOOLEAN DEFAULT FALSE,
    read_at TIMESTAMP WITH TIME ZONE,
    
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    
    -- Güvenlik
    CONSTRAINT check_notification_type CHECK (type IN (
        'sale_completed', 'new_follower', 'new_review',
        'new_question', 'media_liked', 'media_commented',
        'contest_result', 'order_completed'
    ))
);

-- İndeksler
CREATE INDEX idx_notifications_user ON notifications(user_id, created_at DESC);
CREATE INDEX idx_notifications_unread ON notifications(user_id, is_read) 
    WHERE is_read = FALSE; -- Sadece okunmamışları hızlı bulmak için


CREATE TABLE notification_deliveries (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    notification_id UUID NOT NULL REFERENCES notifications(id) ON DELETE CASCADE,
    
    -- Kanal
    channel VARCHAR(20) NOT NULL, -- 'push', 'email', 'in_app'
    
    -- Durum
    status VARCHAR(20) DEFAULT 'pending', -- 'pending', 'sent', 'failed'
    
    -- Gönderim Detayı
    provider VARCHAR(50),         -- 'firebase', 'sendgrid', 'resend'
    provider_message_id VARCHAR(255), -- Sağlayıcının verdiği mesaj ID'si
    error_message TEXT,           -- Başarısız olursa neden?
    
    sent_at TIMESTAMP WITH TIME ZONE,
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    
    CONSTRAINT check_channel CHECK (channel IN ('push', 'email', 'in_app')),
    CONSTRAINT check_status CHECK (status IN ('pending', 'sent', 'failed'))
);

CREATE INDEX idx_deliveries_notification ON notification_deliveries(notification_id);
CREATE INDEX idx_deliveries_pending ON notification_deliveries(status) 
    WHERE status = 'pending'; -- Bekleyen gönderimleri hızlı bulmak için


CREATE TABLE user_device_tokens (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    
    token TEXT NOT NULL,                    -- Firebase FCM token
    device_type VARCHAR(20) NOT NULL,       -- 'ios', 'android', 'web'
    device_id VARCHAR(255),                 -- Cihaz ID'si
    
    is_active BOOLEAN DEFAULT TRUE,
    last_used_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    
    UNIQUE(user_id, device_id),             -- Aynı cihaz 2 kez kayıt olmasın
    CONSTRAINT check_device_type CHECK (device_type IN ('ios', 'android', 'web'))
);

CREATE INDEX idx_device_tokens_user ON user_device_tokens(user_id) 
    WHERE is_active = TRUE;

ALTER TABLE notifications ENABLE ROW LEVEL SECURITY;
ALTER TABLE notification_deliveries ENABLE ROW LEVEL SECURITY;
ALTER TABLE user_device_tokens ENABLE ROW LEVEL SECURITY;
CREATE POLICY "Kullanıcı kendi bildirimlerini görebilir" 
ON notifications FOR SELECT 
USING (user_id = current_setting('app.current_user_id', true)::uuid);
CREATE POLICY "Kullanıcı bildirimini okundu yapabilir" 
ON notifications FOR UPDATE
USING (user_id = current_setting('app.current_user_id', true)::uuid);
CREATE POLICY "Kullanıcı kendi tokenlarını yönetebilir" 
ON user_device_tokens FOR ALL
USING (user_id = current_setting('app.current_user_id', true)::uuid);



