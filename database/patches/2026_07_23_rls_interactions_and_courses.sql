-- Complete owner policies required by seller course editing and media counters.
BEGIN;

DROP POLICY IF EXISTS media_update_owner ON media;
CREATE POLICY media_update_owner ON media FOR UPDATE
USING (
    EXISTS (
        SELECT 1
        FROM shops
        WHERE shops.id = media.shop_id
          AND shops.user_id = NULLIF(current_setting('app.current_user_id', true), '')::uuid
    )
)
WITH CHECK (
    EXISTS (
        SELECT 1
        FROM shops
        WHERE shops.id = media.shop_id
          AND shops.user_id = NULLIF(current_setting('app.current_user_id', true), '')::uuid
    )
);

DROP POLICY IF EXISTS seller_course_sections_manage ON course_sections;
CREATE POLICY seller_course_sections_manage ON course_sections
USING (
    EXISTS (
        SELECT 1
        FROM products
        JOIN shops ON shops.id = products.shop_id
        WHERE products.id = course_sections.course_id
          AND shops.user_id = NULLIF(current_setting('app.current_user_id', true), '')::uuid
    )
)
WITH CHECK (
    EXISTS (
        SELECT 1
        FROM products
        JOIN shops ON shops.id = products.shop_id
        WHERE products.id = course_sections.course_id
          AND shops.user_id = NULLIF(current_setting('app.current_user_id', true), '')::uuid
    )
);

DROP POLICY IF EXISTS seller_course_lessons_manage ON course_lessons;
CREATE POLICY seller_course_lessons_manage ON course_lessons
USING (
    EXISTS (
        SELECT 1
        FROM course_sections
        JOIN products ON products.id = course_sections.course_id
        JOIN shops ON shops.id = products.shop_id
        WHERE course_sections.id = course_lessons.course_section_id
          AND shops.user_id = NULLIF(current_setting('app.current_user_id', true), '')::uuid
    )
)
WITH CHECK (
    EXISTS (
        SELECT 1
        FROM course_sections
        JOIN products ON products.id = course_sections.course_id
        JOIN shops ON shops.id = products.shop_id
        WHERE course_sections.id = course_lessons.course_section_id
          AND shops.user_id = NULLIF(current_setting('app.current_user_id', true), '')::uuid
    )
);

COMMIT;
