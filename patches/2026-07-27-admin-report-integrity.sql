BEGIN;

ALTER TABLE public.admin_reports
    DROP CONSTRAINT IF EXISTS admin_reports_type_check,
    DROP CONSTRAINT IF EXISTS admin_reports_reason_check,
    DROP CONSTRAINT IF EXISTS admin_reports_status_check;

ALTER TABLE public.admin_reports
    ADD CONSTRAINT admin_reports_type_check
        CHECK (type IN ('user', 'shop', 'product', 'media', 'course', 'comment')),
    ADD CONSTRAINT admin_reports_reason_check
        CHECK (reason IN ('spam', 'abuse', 'copyright', 'scam', 'other')),
    ADD CONSTRAINT admin_reports_status_check
        CHECK (status IN ('open', 'pending', 'reviewing', 'resolved', 'rejected'));

CREATE UNIQUE INDEX IF NOT EXISTS admin_reports_open_reporter_target_key
    ON public.admin_reports (reported_by_user_id, type, target_id)
    WHERE status IN ('open', 'pending', 'reviewing');

COMMIT;
