# Craftora Backend Handoff

Date: 2026-07-16
Repository: `C:\sus\CoreBackendApi`
Branch: `main`
HEAD: `800a63f` (`DEPLOY: guncellemeler`)

## Read This First

- The large backend/security work is committed on `main` (mainly commits
  `81aa931..800a63f`). Do not reset or revert it.
- The only current uncommitted file is `AUTH_SYSTEM_ANALYSIS.md`. It is a
  separate user-authored analysis document; leave it untouched unless asked.
- `Program.cs` deliberately no longer runs migrations, `EnsureCreated`, or
  `DatabaseHardening` at startup. Database DDL/security changes are applied by
  an admin through the canonical schema and versioned SQL patches.
- Recent builds passed with `dotnet build CoreBackendApi.csproj --no-restore
  --no-incremental -m:1 -v:minimal` and reported zero errors/warnings.

## Delivered Backend Work

### Database, RLS, and deployment

- Rebuilt `mysql/craftora.sql` as the canonical fresh-install schema from the
  live PostgreSQL dump. It includes tables, enums, functions, triggers, RLS,
  policies, indexes, constraints, categories seed data, and EF migration history.
- Updated `Data/DatabaseHardening.cs` to match the live security model:
  `SECURITY DEFINER SET search_path=public` trigger functions, refund handling,
  tolerant finance constraints, RLS policies, and missing indexes.
- Added versioned patches:
  - `2026_07_05_live_db_security_sync.sql`
  - `2026_07_10_create_runtime_role.sql`
  - `2026_07_15_lesson_completion_xp.sql`
  - `2026_07_15_reviews_unique_user_product.sql`
  - `2026_07_15_support_tickets.sql`
- Added a planned non-superuser runtime role, `craftora_app`. The app connection
  has not been switched to it yet; required grants and the runtime connection
  change must be completed as a separate, carefully tested rollout.
- Added `DEPLOY.md` and Linux PostgreSQL backup/restore scripts under
  `scripts/backup/`.

### Authentication, authorization, and platform security

- Fixed JWT inbound claim mapping so `AdminOnly` and `SellerOnly` policies read
  the JWT `role` claim correctly.
- Registered previously missing services in DI, including admin/seller/course
  paths that otherwise produced runtime 500 errors.
- Refresh tokens are now SHA-256 hashed before storage in `user_sessions`.
  Login/Google login/refresh rotation write hashes; refresh/logout hash the
  supplied token before lookup. Existing plaintext refresh tokens are no longer
  accepted and require a fresh login after deployment.
- `/auth/me` seller role is now based on active shop plus active subscription,
  consistent with login.
- Seller role is assigned only after a successful subscription payment. Creating
  an inactive shop no longer grants seller role.
- Added/strengthened rate limits, input validation, plain-text dangerous-pattern
  validation, HTML encoding for OTP mail values, config-driven CORS allowlist,
  protected reports, and public DTO boundaries.
- Disabled the unsafe generic upload delete endpoint and made the infrastructure
  test controller unavailable outside Development.

### Orders, payments, reviews, and library access

- Fixed checkout completion persistence: successful order/payment state is saved
  inside the transaction before commit.
- Added Redis per-user checkout lock with fail-closed behavior.
- Prevented self-purchase of a seller's own product.
- Changed multi-item checkout behavior: each successful product is persisted and
  removed from the cart immediately; failed items remain in the cart. This avoids
  charging an already-paid item again on retry.
- Converted duplicate-purchase PostgreSQL trigger errors to clean 400 responses.
- Fixed coupon usage race protection.
- Reviews now require `user_library` ownership. A DB unique constraint prevents
  duplicate `(user_id, product_id)` reviews; duplicate conflicts return cleanly.
- Added protected digital product download endpoint:
  `GET /api/product/{id}/download-url`.
- Purchased products/courses retain buyer access even after the product/shop is
  archived, while public listings hide inactive shops.

### Courses and learning access

- Added seller course CRUD, sections, lessons, resources, public course list,
  featured courses, public detail, buyer "my courses", and lesson playback DTOs/
  services/controllers.
- Removed legacy public course routes that leaked curriculum and video object keys.
- Course progress now verifies purchase/library access before saving progress;
  course owner/admin exceptions remain supported for testing.
- Added the lesson-completion XP function/trigger for `user_lesson_progress`.
  It is idempotent, checks library ownership, and uses a partial unique index to
  prevent duplicate `complete_lesson` point logs.
- Student quiz responses no longer expose correct answers before submission.

### Reels/media

- Extended media DTOs and mappings for public URLs, product/shop context,
  hashtags, thumbnails, original-file fallback, counters, likes/saves, comments,
  and replies.
- Added one-level comment replies with parent/media validation and nested comment
  responses. Comment-count ownership remains in database triggers.
- Removed service-side manual like/save/comment counter arithmetic so database
  triggers are the single counter owner.
- Made watch-history insert idempotent via `ON CONFLICT`; view count still records
  every view while XP is only awarded on the first qualifying watch.
- Added public inactive-shop protection:
  - Feed and public shop-media queries require `shop.IsActive == true`.
  - Seller `GetMyMediaAsync` still returns the owner's active media even when the
    shop is inactive.
  - Feed cache now uses `media:feed:version`, invalidates on upload/delete, and
    rechecks shop activity on cache hits to prevent stale Redis exposure.

### Search, analytics, home, seller dashboard, admin

- Added global Elasticsearch search across products, courses, media, and shops.
  Public search filters published/active products and active shops.
- Aligned .NET Elasticsearch client to 8.13.15 for Elasticsearch 8.13.0,
  strengthened response/error logging, and added admin-only product reindex:
  `POST /api/admin/reindex-products`.
- Added seller dashboard services/controllers/DTOs for orders, customers,
  analytics, courses, and media.
- Added admin dashboard/user/report/home-card/pulse-news/competition operations,
  including audit logging and safeguards against locking/suspending/deleting self,
  another admin, or the last active admin.
- Added analytics event allowlist for anonymous tracking and report authentication,
  rate limiting, and duplicate-open-report protection.
- Added real-data home/trending endpoints and gamification profile/competition
  APIs with transaction/row-lock idempotency for competition finalization.

### Support tickets

- Added complete support ticket system:
  `support_tickets`, `support_ticket_messages`, C# entities/enums/mapping, DTOs,
  service/interface, DI registration, user and admin controllers, RLS/index patch.
- User routes are ownership-filtered (IDOR-safe); admin routes use `AdminOnly`.
- Ticket messages/status changes use transaction and row-lock logic.
- Admin replies send best-effort user notifications after commit and write an
  in-transaction audit entry.

### Infrastructure and storage

- Added public MinIO endpoint configuration (`PublicEndpoint`/`PublicUseSSL`) so
  presigned URLs use device-reachable HTTP/HTTPS addresses instead of container
  localhost. Internal backend-to-MinIO routing remains separate.
- S3/MinIO CORS configuration failures are warnings per bucket, not fatal startup
  errors.
- RabbitMQ healthcheck was relaxed to avoid false `unhealthy` status.
- Added deployment notes for strong production secrets and Elasticsearch security.

## Important Invariants: Do Not Regress

1. Do not re-enable automatic DB DDL/migrations/hardening in app startup.
2. Do not store plaintext refresh tokens or add a plaintext fallback lookup.
3. Database triggers own media/follower/order-related counters and XP side effects;
   do not add manual counter increments without checking the trigger first.
4. Public product/media/search results must exclude inactive shops. Owner/library
   access is intentionally different from public visibility.
5. Checkout lock, self-purchase prevention, per-item cart removal, and completed
   status persistence are all required together.
6. Seller access requires active shop plus active subscription; creating a shop
   alone must not give seller privileges.
7. Course/file presigned URLs must be generated only after entitlement checks.

## Deployment / Verification Checklist

1. Apply `mysql/craftora.sql` only for an empty database. For an existing DB,
   apply the versioned patches in `database/patches/` with an admin role and
   `ON_ERROR_STOP=1`.
2. Confirm the lesson XP and review uniqueness patches were applied to each target
   environment; never assume repo presence means the live DB was patched.
3. Configure production `.env` secrets, CORS allowed origins, MinIO public URL,
   and Elasticsearch security according to `DEPLOY.md`.
4. Reindex products after deploying ES/search changes:
   `POST /api/admin/reindex-products` with an admin token.
5. Run the build command above, then bring up Docker services. Verify RabbitMQ,
   PostgreSQL, MinIO, Redis, Elasticsearch, and API health.
6. When moving to `craftora_app`, first apply grants/policies and test every
   authenticated flow under RLS before changing the production connection string.

## Known Follow-ups / Deliberate Scope Boundaries

- Payment providers remain mock/provider-agnostic until real Stripe/Iyzico work.
- Production HLS transcoding and automatic video-frame thumbnail generation still
  need a real media-processing rollout; the current video workflow must be tested
  with production storage/CDN settings.
- Elasticsearch is currently an internal development-style deployment unless its
  port is closed/firewalled and xpack credentials are enabled in production.
- RLS runtime-role rollout is prepared but not yet activated.

### Email branding follow-up

- Authentication emails currently use the temporary logo at
  `wwwroot/email-assets/craftora-email-logo.png`. Replace this file in place when
  the final Craftora logo is delivered so existing email templates keep working.
- The Gmail sender avatar cannot be changed by email HTML. After the final logo is
  approved, prepare a square BIMI-compatible SVG, verify SPF/DKIM alignment, move
  DMARC from monitoring to `p=quarantine` or `p=reject` with `pct=100`, obtain a
  VMC or CMC, and publish the `default._bimi` TXT record.
- Do not start the BIMI certificate/DNS rollout with the temporary logo because
  the certificate is tied to the approved mark.

## Useful Git References

- Database hardening: `81aa931`
- Admin/seller/analytics/search/course feature bundle: `db2985c`
- Security fixes and checkout lock: `104828a`, `3c2ff7a`, `6977f05`
- Support ticket implementation: `11ff31c`, `a034032`, `1258b08`, `5c0b2de`
- Checkout/review/course/library fixes: `21f5542`, `242d631`, `4f120b9`, `7641289`
- Refresh token hashing: `7ced920`
- Inactive public product/media protections: `b269730`, `deb5cb1`
