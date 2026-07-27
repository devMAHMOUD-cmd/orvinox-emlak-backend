# Craftora Backend Final Report

Date: 2026-07-27

## Final Status

The Craftora backend is production-ready for the currently approved scope.
The only intentionally deferred product integration is the real payment
provider. Mock payment remains enabled for development and acceptance testing.

Final production verification:

- API, PostgreSQL, Redis, RabbitMQ, and MassTransit health checks: Healthy
- Git production revision before this report: `c74dc56`
- Active RabbitMQ queues: empty, with expected consumers
- Legacy `file_processing_queue`: backed up and removed
- Pending or failed Resend inbound events: 0
- Final production error scan: no matching errors
- Local automated tests: 75 passed, 0 failed

## Completed Areas

### Authentication and Email

- Registration, email verification, login, refresh token, and Google login
- OTP and welcome email delivery through Resend
- Branded transactional email templates
- Secure Firebase configuration for push notifications
- Device token registration, invalid-token deactivation, and delivery tracking
- Admin-targeted email campaigns with preview, audience selection, queue-based
  delivery, idempotency, retry support, and recipient status tracking
- Incoming `support@craftoramedya.com` email conversion to support tickets
- Email replies routed back into the same support ticket through ticket-specific
  plus addressing, with quoted email history removed
- Resend webhook signature verification, replay protection, request size limit,
  registered-user matching, and admin notification
- Admin support replies delivered through in-app/push notifications and branded
  email

### Shops and Subscriptions

- Shop create, read, update, activation, logo, and banner workflows
- Safe logo and banner replacement/removal with object-storage cleanup
- Starter plan: USD 5 monthly and 20% sales commission
- Professional plan: USD 25 monthly and 2% sales commission
- Commission snapshots on orders and payments
- Subscription grace-period lifecycle
- Expired/unpaid subscription shop closure, seller-role downgrade, content
  deactivation, and session revocation
- Subscription renewal/reactivation behavior

### Products, Courses, and Uploads

- Digital-product create, update, publish, archive, and delete workflows
- Strict price, title, description, type, filename, object-key, and sort-order
  validation
- Product images and private downloadable files
- Course, section, lesson, resource, archive, and deletion workflows
- Course RLS ownership fixes
- Presigned uploads and upload completion verification
- MinIO public/private access boundaries and object cleanup
- Purchased-course access, lesson video/resource access, progress tracking, and
  irreversible completion state

### Reels and Social

- Reels/media create, process, feed, detail, and byte-range playback
- TikTok-style streaming support through HTTP range requests
- Follow, like, save, share, view, comment, reply, and delete operations
- Counter synchronization and cross-user notification protection
- Media and social RLS fixes

### Commerce

- Cart and direct checkout
- Digital-product quantity and duplicate-purchase protection
- Failed-payment state handling without library delivery or coupon consumption
- Successful purchase, library delivery, private download, invoice generation,
  and seller order views
- Plan-based platform fee and seller earnings snapshots
- Coupon validation, checkout locking, single-use enforcement, discounts, and
  final commission calculation
- Mock payment behavior retained intentionally until a real provider is chosen

### Community, Support, and Administration

- Product reviews, review updates, seller replies, and aggregate rating refresh
- Product and course Q&A with seller-only answers and buyer notifications
- In-app notification ownership and read-state protection
- User/admin support ticket messaging and closed-ticket protection
- Global/product/shop search, reindexing, filters, and input validation
- Seller analytics overview, funnel, traffic sources, top products, time series,
  and course completion metrics
- User reports, duplicate/self-report protection, moderation, target removal,
  audit logs, and finalized-report protection

### Security and Operations

- PostgreSQL RLS and SECURITY DEFINER functions with fixed search paths
- Validation for unsafe HTML, control characters, oversized bodies, numeric
  ranges, enum values, and upload fields
- 413 response for oversized request bodies
- Generic production errors without stack leakage
- Secrets moved to production environment variables and protected credential
  mounts
- Database and queue backups created before destructive maintenance
- Temporary E2E database records and MinIO objects removed

## Production Evidence

- Real product upload/create/update/delete and object cleanup: passed
- Real private-file purchase/download with matching file hashes: passed
- Real course purchase/access/progress with matching file hashes: passed
- Real Reels upload/feed/range playback and social actions: passed
- Real coupon failed/successful checkout behavior: passed
- Real invoice generation and PDF retrieval: passed
- Real Resend admin campaign to two selected recipients: sent successfully
- Real inbound email to `support@craftoramedya.com`: converted to a support
  ticket and generated admin notifications
- Real admin email reply delivered to Gmail; Gmail reply returned to the same
  support ticket and quoted-history cleanup verified
- Real Android Firebase token registered from a Samsung device; push delivery
  recorded as `sent` and displayed on the phone with the Craftora logo
- Final Craftora logo prepared as a safe SVG Tiny PS BIMI asset
- SPF, aligned DKIM, and DMARC verified as passing; DMARC enforcement and the
  public BIMI assertion were published and confirmed through independent DNS
  resolvers
- Final test records were removed after verification

## Deferred Outside Backend Closure

- Real payment provider credentials, webhooks, refunds, and settlement behavior
- Gmail CMC/VMC certificate issuance, which is an optional paid external
  certification

The optional Gmail CMC/VMC certificate does not block backend closure. The
mobile release will reuse the now-verified Firebase token registration and push
delivery contract.
