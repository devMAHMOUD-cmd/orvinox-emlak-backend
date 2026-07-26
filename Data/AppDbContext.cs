using System;
using System.Collections.Generic;
using System.Text.Json;
using CraftoraApi.Models.Entities;
using CraftoraApi.Models.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace CraftoraApi.Data;

public partial class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<CartItem> CartItems { get; set; }

    public virtual DbSet<AnalyticsEvent> AnalyticsEvents { get; set; }

    public virtual DbSet<AdminCompetitionReward> AdminCompetitionRewards { get; set; }

    public virtual DbSet<Category> Categories { get; set; }

    public virtual DbSet<Contest> Contests { get; set; }

    public virtual DbSet<ContestResult> ContestResults { get; set; }

    public virtual DbSet<Coupon> Coupons { get; set; }

    public virtual DbSet<CouponUse> CouponUses { get; set; }

    public virtual DbSet<Course> Courses { get; set; }

    public virtual DbSet<CourseLesson> CourseLessons { get; set; }

    public virtual DbSet<CourseQuiz> CourseQuizzes { get; set; }

    public virtual DbSet<CourseSection> CourseSections { get; set; }

    public virtual DbSet<LessonResource> LessonResources { get; set; }

    public virtual DbSet<LessonProgress> LessonProgresses { get; set; }

    public virtual DbSet<LoginAttempt> LoginAttempts { get; set; }

    public virtual DbSet<IpLoginAttempt> IpLoginAttempts { get; set; }

    public virtual DbSet<MediaComment> MediaComments { get; set; }

    public virtual DbSet<MediaLike> MediaLikes { get; set; }

    public virtual DbSet<MediaSafe> MediaSaves { get; set; }

    public virtual DbSet<MediaWatchHistory> MediaWatchHistories { get; set; }

    public virtual DbSet<Medium> Media { get; set; }

    public virtual DbSet<Notification> Notifications { get; set; }

    public virtual DbSet<NotificationDelivery> NotificationDeliveries { get; set; }

    public virtual DbSet<Order> Orders { get; set; }

    public virtual DbSet<Payment> Payments { get; set; }

    public virtual DbSet<PointLog> PointLogs { get; set; }

    public virtual DbSet<Product> Products { get; set; }

    public virtual DbSet<ProductImage> ProductImages { get; set; }

    public virtual DbSet<ProductQa> ProductQas { get; set; }

    public virtual DbSet<Review> Reviews { get; set; }

    public virtual DbSet<SellerSubscription> SellerSubscriptions { get; set; }

    public virtual DbSet<SellerSubscriptionPlan> SellerSubscriptionPlans { get; set; }

    public virtual DbSet<SellerSubscriptionPayment> SellerSubscriptionPayments { get; set; }

    public virtual DbSet<SellerNotificationPreference> SellerNotificationPreferences { get; set; }

    public virtual DbSet<Shop> Shops { get; set; }

    public virtual DbSet<ShopVisit> ShopVisits { get; set; }

    public virtual DbSet<Subscription> Subscriptions { get; set; }

    public virtual DbSet<SupportTicket> SupportTickets { get; set; }

    public virtual DbSet<SupportTicketMessage> SupportTicketMessages { get; set; }

    public virtual DbSet<User> Users { get; set; }

    public virtual DbSet<UserDeviceToken> UserDeviceTokens { get; set; }

    public virtual DbSet<UserLibrary> UserLibraries { get; set; }

    public virtual DbSet<UserLessonProgress> UserLessonProgresses { get; set; }

    public virtual DbSet<UserPoint> UserPoints { get; set; }

    public virtual DbSet<UserSession> UserSessions { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder
            .HasPostgresExtension("citext")
            .HasPostgresExtension("uuid-ossp");

        modelBuilder.Entity<AdminCompetitionReward>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("admin_competition_rewards_pkey");

            entity.ToTable("admin_competition_rewards");

            entity.HasIndex(e => new { e.ContestId, e.UserId }, "uq_admin_competition_rewards_contest_user")
                .IsUnique();

            entity.Property(e => e.Id)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.Amount)
                .HasPrecision(12, 2)
                .HasColumnName("amount");
            entity.Property(e => e.CertificateUrl).HasColumnName("certificate_url");
            entity.Property(e => e.ContestId).HasColumnName("contest_id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("created_at");
            entity.Property(e => e.Currency)
                .HasMaxLength(3)
                .HasColumnName("currency");
            entity.Property(e => e.Note).HasColumnName("note");
            entity.Property(e => e.Rank).HasColumnName("rank");
            entity.Property(e => e.RewardType)
                .HasMaxLength(50)
                .HasColumnName("reward_type");
            entity.Property(e => e.UserId).HasColumnName("user_id");
        });

        modelBuilder.Entity<AnalyticsEvent>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("analytics_events_pkey");

            entity.ToTable("analytics_events");

            entity.HasIndex(e => new { e.ShopId, e.CreatedAt }, "idx_analytics_shop_date")
                .IsDescending(false, true);

            entity.HasIndex(e => new { e.ShopId, e.EventType, e.CreatedAt }, "idx_analytics_shop_event_date")
                .IsDescending(false, false, true);

            entity.HasIndex(e => new { e.ProductId, e.EventType, e.CreatedAt }, "idx_analytics_product_event_date")
                .IsDescending(false, false, true)
                .HasFilter("product_id IS NOT NULL");

            entity.HasIndex(e => new { e.MediaId, e.EventType, e.CreatedAt }, "idx_analytics_media_event_date")
                .IsDescending(false, false, true)
                .HasFilter("media_id IS NOT NULL");

            entity.HasIndex(e => e.OrderId, "idx_analytics_order")
                .HasFilter("order_id IS NOT NULL");

            entity.HasIndex(e => new { e.UserId, e.CreatedAt }, "idx_analytics_user_date")
                .IsDescending(false, true)
                .HasFilter("user_id IS NOT NULL");

            entity.HasIndex(e => new { e.SessionId, e.CreatedAt }, "idx_analytics_session_date")
                .IsDescending(false, true)
                .HasFilter("session_id IS NOT NULL");

            entity.HasIndex(e => new { e.ShopId, e.Source, e.CreatedAt }, "idx_analytics_shop_source_date")
                .IsDescending(false, false, true)
                .HasFilter("source IS NOT NULL");

            entity.HasIndex(e => new { e.ShopId, e.UtmSource, e.CreatedAt }, "idx_analytics_shop_utm_source_date")
                .IsDescending(false, false, true)
                .HasFilter("utm_source IS NOT NULL");

            entity.HasIndex(e => e.Metadata, "idx_analytics_metadata_gin")
                .HasMethod("gin");

            entity.Property(e => e.Id)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.ShopId).HasColumnName("shop_id");
            entity.Property(e => e.ProductId).HasColumnName("product_id");
            entity.Property(e => e.MediaId).HasColumnName("media_id");
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.OrderId).HasColumnName("order_id");
            entity.Property(e => e.EventType)
                .HasColumnType("analytics_event_type")
                .HasColumnName("event_type");
            entity.Property(e => e.SessionId)
                .HasMaxLength(100)
                .HasColumnName("session_id");
            entity.Property(e => e.Source)
                .HasMaxLength(100)
                .HasColumnName("source");
            entity.Property(e => e.Referrer).HasColumnName("referrer");
            entity.Property(e => e.UtmSource)
                .HasMaxLength(100)
                .HasColumnName("utm_source");
            entity.Property(e => e.UtmMedium)
                .HasMaxLength(100)
                .HasColumnName("utm_medium");
            entity.Property(e => e.UtmCampaign)
                .HasMaxLength(150)
                .HasColumnName("utm_campaign");
            entity.Property(e => e.DeviceType)
                .HasMaxLength(30)
                .HasColumnName("device_type");
            entity.Property(e => e.IpAddress).HasColumnName("ip_address");
            entity.Property(e => e.UserAgent).HasColumnName("user_agent");
            entity.Property(e => e.Metadata)
                .HasDefaultValueSql("'{}'::jsonb")
                .HasColumnType("jsonb")
                .HasColumnName("metadata");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("created_at");

            entity.HasOne(d => d.Shop).WithMany()
                .HasForeignKey(d => d.ShopId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("analytics_events_shop_id_fkey");

            entity.HasOne(d => d.Product).WithMany()
                .HasForeignKey(d => d.ProductId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("analytics_events_product_id_fkey");

            entity.HasOne(d => d.Media).WithMany()
                .HasForeignKey(d => d.MediaId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("analytics_events_media_id_fkey");

            entity.HasOne(d => d.User).WithMany()
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("analytics_events_user_id_fkey");

            entity.HasOne(d => d.Order).WithMany()
                .HasForeignKey(d => d.OrderId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("analytics_events_order_id_fkey");
        });

        modelBuilder.Entity<CartItem>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("cart_items_pkey");

            entity.ToTable("cart_items");

            entity.HasIndex(e => new { e.UserId, e.ProductId }, "cart_items_user_id_product_id_key").IsUnique();

            entity.HasIndex(e => e.UserId, "idx_cart_items_user");

            entity.Property(e => e.Id)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("created_at");
            entity.Property(e => e.ProductId).HasColumnName("product_id");
            entity.Property(e => e.Quantity)
                .HasDefaultValue(1)
                .HasColumnName("quantity");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("updated_at");
            entity.Property(e => e.UserId).HasColumnName("user_id");

            entity.HasOne(d => d.Product).WithMany(p => p.CartItems)
                .HasForeignKey(d => d.ProductId)
                .HasConstraintName("cart_items_product_id_fkey");

            entity.HasOne(d => d.User).WithMany(p => p.CartItems)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("cart_items_user_id_fkey");
        });

        modelBuilder.Entity<Category>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("categories_pkey");

            entity.ToTable("categories");

            entity.HasIndex(e => e.Slug, "categories_slug_key").IsUnique();

            entity.Property(e => e.Id)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("created_at");
            entity.Property(e => e.IsActive)
                .HasDefaultValue(true)
                .HasColumnName("is_active");
            entity.Property(e => e.Name)
                .HasMaxLength(100)
                .HasColumnName("name");
            entity.Property(e => e.ParentId).HasColumnName("parent_id");
            entity.Property(e => e.Slug)
                .HasColumnType("citext")
                .HasColumnName("slug");

            entity.HasOne(d => d.ParentCategory).WithMany(p => p.SubCategories)
                .HasForeignKey(d => d.ParentId)
                .HasConstraintName("categories_parent_id_fkey");
        });

        modelBuilder.Entity<Contest>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("contests_pkey");

            entity.ToTable("contests");

            entity.Property(e => e.Id)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.CreatedBy).HasColumnName("created_by");
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.EndDate).HasColumnName("end_date");
            entity.Property(e => e.IsActive)
                .HasDefaultValue(true)
                .HasColumnName("is_active");
            entity.Property(e => e.PrizePool).HasColumnName("prize_pool");
            entity.Property(e => e.RewardsHidden)
                .HasDefaultValue(false)
                .HasColumnName("rewards_hidden");
            entity.Property(e => e.StartDate).HasColumnName("start_date");
            entity.Property(e => e.Title)
                .HasMaxLength(255)
                .HasColumnName("title");

            entity.HasOne(d => d.CreatedByNavigation).WithMany(p => p.Contests)
                .HasForeignKey(d => d.CreatedBy)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("contests_created_by_fkey");
        });

        modelBuilder.Entity<ContestResult>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("contest_results_pkey");

            entity.ToTable("contest_results");

            entity.HasIndex(e => new { e.ContestId, e.UserId }, "contest_results_contest_id_user_id_key").IsUnique();

            entity.Property(e => e.Id)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.ContestId).HasColumnName("contest_id");
            entity.Property(e => e.JoinedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("joined_at");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("created_at");
            entity.Property(e => e.FinalRank).HasColumnName("final_rank");
            entity.Property(e => e.RewardClaimed)
                .HasDefaultValue(false)
                .HasColumnName("reward_claimed");
            entity.Property(e => e.TotalScore)
                .HasPrecision(12, 2)
                .HasColumnName("total_score");
            entity.Property(e => e.UserId).HasColumnName("user_id");

            entity.HasOne(d => d.Contest).WithMany(p => p.ContestResults)
                .HasForeignKey(d => d.ContestId)
                .HasConstraintName("contest_results_contest_id_fkey");

            entity.HasOne(d => d.User).WithMany(p => p.ContestResults)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("contest_results_user_id_fkey");
        });

        modelBuilder.Entity<Coupon>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("coupons_pkey");

            entity.ToTable("coupons");

            entity.HasIndex(e => e.Code, "idx_coupons_code");

            entity.HasIndex(e => e.ProductId, "idx_coupons_product");

            entity.HasIndex(e => new { e.ProductId, e.Code }, "unique_coupon_per_product").IsUnique();

            entity.Property(e => e.Id)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.Code)
                .HasMaxLength(50)
                .HasColumnName("code");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("created_at");
            entity.Property(e => e.DiscountType)
                .HasMaxLength(10)
                .HasColumnName("discount_type");
            entity.Property(e => e.DiscountValue)
                .HasPrecision(10, 2)
                .HasColumnName("discount_value");
            entity.Property(e => e.ExpiresAt).HasColumnName("expires_at");
            entity.Property(e => e.IsActive)
                .HasDefaultValue(true)
                .HasColumnName("is_active");
            entity.Property(e => e.MinimumCartAmount)
                .HasPrecision(10, 2)
                .HasDefaultValue(0m)
                .HasColumnName("minimum_cart_amount");
            entity.Property(e => e.MaxUses).HasColumnName("max_uses");
            entity.Property(e => e.ProductId).HasColumnName("product_id");
            entity.Property(e => e.ShopId).HasColumnName("shop_id");
            entity.Property(e => e.StartsAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("starts_at");
            entity.Property(e => e.UsedCount)
                .HasDefaultValue(0)
                .HasColumnName("used_count");

            entity.HasOne(d => d.Product).WithMany(p => p.Coupons)
                .HasForeignKey(d => d.ProductId)
                .HasConstraintName("coupons_product_id_fkey");

            entity.HasOne(d => d.Shop).WithMany(p => p.Coupons)
                .HasForeignKey(d => d.ShopId)
                .HasConstraintName("coupons_shop_id_fkey");
        });

        modelBuilder.Entity<CouponUse>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("coupon_uses_pkey");

            entity.ToTable("coupon_uses");

            entity.HasIndex(e => new { e.CouponId, e.UserId }, "coupon_uses_coupon_id_user_id_key").IsUnique();

            entity.Property(e => e.Id)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.CouponId).HasColumnName("coupon_id");
            entity.Property(e => e.OrderId).HasColumnName("order_id");
            entity.Property(e => e.UsedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("used_at");
            entity.Property(e => e.UserId).HasColumnName("user_id");

            entity.HasOne(d => d.Coupon).WithMany(p => p.CouponUses)
                .HasForeignKey(d => d.CouponId)
                .HasConstraintName("coupon_uses_coupon_id_fkey");

            entity.HasOne(d => d.Order).WithMany(p => p.CouponUses)
                .HasForeignKey(d => d.OrderId)
                .HasConstraintName("coupon_uses_order_id_fkey");

            entity.HasOne(d => d.User).WithMany(p => p.CouponUses)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("coupon_uses_user_id_fkey");
        });

        modelBuilder.Entity<Course>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("courses_pkey");

            entity.ToTable("courses");

            entity.HasIndex(e => e.ProductId, "idx_courses_product");

            entity.Property(e => e.Id)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("created_at");
            entity.Property(e => e.IsCertificateIncluded)
                .HasDefaultValue(false)
                .HasColumnName("is_certificate_included");
            entity.Property(e => e.Level)
                .HasMaxLength(50)
                .HasColumnName("level");
            entity.Property(e => e.ProductId).HasColumnName("product_id");
            entity.Property(e => e.TotalDurationInMinutes)
                .HasDefaultValue(0)
                .HasColumnName("total_duration_in_minutes");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");

            entity.HasOne(d => d.Product).WithMany(p => p.Courses)
                .HasForeignKey(d => d.ProductId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("courses_product_id_fkey");
        });

        modelBuilder.Entity<CourseLesson>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("course_lessons_pkey");

            entity.ToTable("course_lessons");

            entity.HasIndex(e => new { e.CourseSectionId, e.SortOrder }, "course_lessons_section_id_sort_order_key").IsUnique();

            entity.HasIndex(e => e.CourseSectionId, "idx_course_lessons_section");

            entity.Property(e => e.Id)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.CourseSectionId).HasColumnName("course_section_id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("created_at");
            entity.Property(e => e.DurationInSeconds)
                .HasDefaultValue(0)
                .HasColumnName("duration_in_seconds");
            entity.Property(e => e.IsActive)
                .HasDefaultValue(true)
                .HasColumnName("is_active");
            entity.Property(e => e.IsFreePreview)
                .HasDefaultValue(false)
                .HasColumnName("is_free_preview");
            entity.Property(e => e.SortOrder).HasColumnName("sort_order");
            entity.Property(e => e.Title)
                .HasMaxLength(255)
                .HasColumnName("title");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");
            entity.Property(e => e.VideoUrl).HasColumnName("video_url");

            entity.HasOne(d => d.CourseSection).WithMany(p => p.CourseLessons)
                .HasForeignKey(d => d.CourseSectionId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("course_lessons_section_id_fkey");
        });

        modelBuilder.Entity<CourseQuiz>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("course_quizzes_pkey");

            entity.ToTable("course_quizzes");

            entity.HasIndex(e => e.CourseSectionId, "idx_course_quizzes_section");

            entity.Property(e => e.Id)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.CourseSectionId).HasColumnName("course_section_id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("created_at");
            entity.Property(e => e.IsActive)
                .HasDefaultValue(true)
                .HasColumnName("is_active");
            entity.Property(e => e.PassingScore).HasColumnName("passing_score");
            entity.Property(e => e.Title)
                .HasMaxLength(255)
                .HasColumnName("title");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");

            entity.HasOne(d => d.CourseSection).WithMany(p => p.CourseQuizzes)
                .HasForeignKey(d => d.CourseSectionId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("course_quizzes_section_id_fkey");
        });

        modelBuilder.Entity<CourseSection>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("course_sections_pkey");

            entity.ToTable("course_sections");

            entity.HasIndex(e => new { e.CourseId, e.SortOrder }, "course_sections_course_id_sort_order_key").IsUnique();

            entity.HasIndex(e => e.CourseId, "idx_course_sections_course");

            entity.Property(e => e.Id)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.CourseId).HasColumnName("course_id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("created_at");
            entity.Property(e => e.IsActive)
                .HasDefaultValue(true)
                .HasColumnName("is_active");
            entity.Property(e => e.SortOrder).HasColumnName("sort_order");
            entity.Property(e => e.Title)
                .HasMaxLength(255)
                .HasColumnName("title");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");

            entity.HasOne(d => d.Course).WithMany(p => p.CourseSections)
                .HasForeignKey(d => d.CourseId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("course_sections_course_id_fkey");
        });

        modelBuilder.Entity<LessonResource>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("lesson_resources_pkey");

            entity.ToTable("lesson_resources");

            entity.HasIndex(e => e.CourseLessonId, "idx_lesson_resources_lesson");

            entity.Property(e => e.Id)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.CourseLessonId).HasColumnName("course_lesson_id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("created_at");
            entity.Property(e => e.FileUrl).HasColumnName("file_url");
            entity.Property(e => e.ResourceType)
                .HasMaxLength(50)
                .HasColumnName("resource_type");
            entity.Property(e => e.Title)
                .HasMaxLength(255)
                .HasColumnName("title");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");

            entity.HasOne(d => d.CourseLesson).WithMany(p => p.LessonResources)
                .HasForeignKey(d => d.CourseLessonId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("lesson_resources_lesson_id_fkey");
        });

        modelBuilder.Entity<LessonProgress>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("lesson_progress_pkey");

            entity.ToTable("lesson_progress");

            entity.HasIndex(e => new { e.UserId, e.LessonId }, "idx_lesson_progress_user");

            entity.HasIndex(e => new { e.UserId, e.LessonId }, "lesson_progress_user_id_lesson_id_key").IsUnique();

            entity.Property(e => e.Id)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.CompletedAt).HasColumnName("completed_at");
            entity.Property(e => e.IsCompleted)
                .HasDefaultValue(false)
                .HasColumnName("is_completed");
            entity.Property(e => e.LessonId).HasColumnName("lesson_id");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("updated_at");
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.WatchedSeconds)
                .HasDefaultValue(0)
                .HasColumnName("watched_seconds");

            entity.HasOne(d => d.Lesson).WithMany(p => p.LessonProgresses)
                .HasForeignKey(d => d.LessonId)
                .HasConstraintName("lesson_progress_lesson_id_fkey");

            entity.HasOne(d => d.User).WithMany(p => p.LessonProgresses)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("lesson_progress_user_id_fkey");
        });

        modelBuilder.Entity<UserLessonProgress>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("user_lesson_progress_pkey");

            entity.ToTable("user_lesson_progress");

            entity.HasIndex(e => new { e.UserId, e.CourseLessonId }, "user_lesson_progress_user_id_course_lesson_id_key").IsUnique();

            entity.Property(e => e.Id)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.CourseLessonId).HasColumnName("course_lesson_id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("created_at");
            entity.Property(e => e.IsCompleted)
                .HasDefaultValue(false)
                .HasColumnName("is_completed");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.WatchedSeconds)
                .HasDefaultValue(0)
                .HasColumnName("watched_seconds");

            entity.HasOne(d => d.CourseLesson).WithMany(p => p.UserLessonProgresses)
                .HasForeignKey(d => d.CourseLessonId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("user_lesson_progress_course_lesson_id_fkey");

            entity.HasOne(d => d.User).WithMany(p => p.UserLessonProgresses)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("user_lesson_progress_user_id_fkey");
        });

        modelBuilder.Entity<LoginAttempt>(entity =>
        {
            entity.HasKey(e => e.Email).HasName("login_attempts_pkey");

            entity.ToTable("login_attempts");

            entity.Property(e => e.Email)
                .HasColumnType("citext")
                .HasColumnName("email");
            entity.Property(e => e.AttemptCount)
                .HasDefaultValue(1)
                .HasColumnName("attempt_count");
            entity.Property(e => e.LastAttemptAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("last_attempt_at");
            entity.Property(e => e.IpAddress).HasColumnName("ip_address");
            entity.Property(e => e.LockedUntil).HasColumnName("locked_until");
        });

        modelBuilder.Entity<IpLoginAttempt>(entity =>
        {
            entity.HasKey(e => e.IpAddress).HasName("ip_login_attempts_pkey");
            entity.ToTable("ip_login_attempts");
            entity.Property(e => e.IpAddress)
                .HasConversion(
                    value => System.Net.IPAddress.Parse(value),
                    value => value.ToString())
                .HasColumnType("inet")
                .HasColumnName("ip_address");
            entity.Property(e => e.AttemptCount).HasDefaultValue(1).HasColumnName("attempt_count");
            entity.Property(e => e.LastAttemptAt).HasDefaultValueSql("CURRENT_TIMESTAMP").HasColumnName("last_attempt_at");
            entity.Property(e => e.LockedUntil).HasColumnName("locked_until");
        });

        modelBuilder.Entity<MediaComment>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("media_comments_pkey");

            entity.ToTable("media_comments");

            entity.Property(e => e.Id)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.CommentText).HasColumnName("comment_text");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("created_at");
            entity.Property(e => e.MediaId).HasColumnName("media_id");
            entity.Property(e => e.ParentCommentId).HasColumnName("parent_comment_id");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("updated_at");
            entity.Property(e => e.UserId).HasColumnName("user_id");

            entity.HasOne(d => d.Media).WithMany(p => p.MediaComments)
                .HasForeignKey(d => d.MediaId)
                .HasConstraintName("media_comments_media_id_fkey");

            entity.HasOne(d => d.User).WithMany(p => p.MediaComments)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("media_comments_user_id_fkey");

            entity.HasOne(d => d.ParentComment).WithMany(p => p.Replies)
                .HasForeignKey(d => d.ParentCommentId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("media_comments_parent_comment_id_fkey");
        });

        modelBuilder.Entity<MediaLike>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("media_likes_pkey");

            entity.ToTable("media_likes");

            entity.HasIndex(e => new { e.MediaId, e.UserId }, "media_likes_media_id_user_id_key").IsUnique();

            entity.Property(e => e.Id)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("created_at");
            entity.Property(e => e.MediaId).HasColumnName("media_id");
            entity.Property(e => e.UserId).HasColumnName("user_id");

            entity.HasOne(d => d.Media).WithMany(p => p.MediaLikes)
                .HasForeignKey(d => d.MediaId)
                .HasConstraintName("media_likes_media_id_fkey");

            entity.HasOne(d => d.User).WithMany(p => p.MediaLikes)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("media_likes_user_id_fkey");
        });

        modelBuilder.Entity<MediaSafe>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("media_saves_pkey");

            entity.ToTable("media_saves");

            entity.HasIndex(e => new { e.MediaId, e.UserId }, "media_saves_media_id_user_id_key").IsUnique();

            entity.Property(e => e.Id)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("created_at");
            entity.Property(e => e.MediaId).HasColumnName("media_id");
            entity.Property(e => e.UserId).HasColumnName("user_id");

            entity.HasOne(d => d.Media).WithMany(p => p.MediaSaves)
                .HasForeignKey(d => d.MediaId)
                .HasConstraintName("media_saves_media_id_fkey");

            entity.HasOne(d => d.User).WithMany(p => p.MediaSaves)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("media_saves_user_id_fkey");
        });

        modelBuilder.Entity<MediaWatchHistory>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("media_watch_history_pkey");

            entity.ToTable("media_watch_history");

            entity.HasIndex(e => new { e.UserId, e.MediaId }, "media_watch_history_user_id_media_id_key").IsUnique();

            entity.Property(e => e.Id)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.IsPointEarned)
                .HasDefaultValue(false)
                .HasColumnName("is_point_earned");
            entity.Property(e => e.MediaId).HasColumnName("media_id");
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.WatchedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("watched_at");

            entity.HasOne(d => d.Media).WithMany(p => p.MediaWatchHistories)
                .HasForeignKey(d => d.MediaId)
                .HasConstraintName("media_watch_history_media_id_fkey");

            entity.HasOne(d => d.User).WithMany(p => p.MediaWatchHistories)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("media_watch_history_user_id_fkey");
        });

        modelBuilder.Entity<Medium>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("media_pkey");

            entity.ToTable("media");

            entity.HasIndex(e => e.ProductId, "idx_media_product");

            entity.HasIndex(e => e.ShopId, "idx_media_shop");

            entity.Property(e => e.Id)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.Caption).HasColumnName("caption");
            entity.Property(e => e.CommentCount)
                .HasDefaultValue(0)
                .HasColumnName("comment_count");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("created_at");
            entity.Property(e => e.DurationSeconds)
                .HasDefaultValue(0)
                .HasColumnName("duration_seconds");
            entity.Property(e => e.Hashtags)
                .HasDefaultValueSql("'{}'::text[]")
                .HasColumnName("hashtags");
            entity.Property(e => e.IsActive)
                .HasDefaultValue(true)
                .HasColumnName("is_active");
            entity.Property(e => e.LikeCount)
                .HasDefaultValue(0)
                .HasColumnName("like_count");
            entity.Property(e => e.ProductId).HasColumnName("product_id");
            entity.Property(e => e.SaveCount)
                .HasDefaultValue(0)
                .HasColumnName("save_count");
            entity.Property(e => e.ShareCount)
                .HasDefaultValue(0)
                .HasColumnName("share_count");
            entity.Property(e => e.ShopId).HasColumnName("shop_id");
            entity.Property(e => e.Status)
                .HasColumnType("media_status")
                .HasDefaultValue(MediaStatus.Processing)
                .HasColumnName("status");
            entity.Property(e => e.ThumbnailUrl).HasColumnName("thumbnail_url");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("updated_at");
            entity.Property(e => e.VideoUrl).HasColumnName("video_url");
            entity.Property(e => e.ViewCount)
                .HasDefaultValue(0)
                .HasColumnName("view_count");

            entity.HasOne(d => d.Product).WithMany(p => p.Media)
                .HasForeignKey(d => d.ProductId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("media_product_id_fkey");

            entity.HasOne(d => d.Shop).WithMany(p => p.Media)
                .HasForeignKey(d => d.ShopId)
                .HasConstraintName("media_shop_id_fkey");
        });

        modelBuilder.Entity<Notification>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("notifications_pkey");

            entity.ToTable("notifications");

            entity.HasIndex(e => new { e.UserId, e.IsRead }, "idx_notifications_unread").HasFilter("(is_read = false)");

            entity.HasIndex(e => new { e.UserId, e.CreatedAt }, "idx_notifications_user").IsDescending(false, true);

            entity.Property(e => e.Id)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.Body).HasColumnName("body");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("created_at");
            entity.Property(e => e.IsRead)
                .HasDefaultValue(false)
                .HasColumnName("is_read");
            entity.Property(e => e.ReadAt).HasColumnName("read_at");
            entity.Property(e => e.ReferenceId).HasColumnName("reference_id");
            entity.Property(e => e.ReferenceType)
                .HasMaxLength(50)
                .HasColumnName("reference_type");
            entity.Property(e => e.Data)
                .HasColumnType("jsonb")
                .HasColumnName("data");
            entity.Property(e => e.Title)
                .HasMaxLength(255)
                .HasColumnName("title");
            entity.Property(e => e.Type)
                .HasMaxLength(50)
                .HasColumnName("type");
            entity.Property(e => e.UserId).HasColumnName("user_id");

            entity.HasOne(d => d.User).WithMany(p => p.Notifications)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("notifications_user_id_fkey");
        });

        modelBuilder.Entity<NotificationDelivery>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("notification_deliveries_pkey");

            entity.ToTable("notification_deliveries");

            entity.HasIndex(e => e.NotificationId, "idx_deliveries_notification");

            entity.HasIndex(e => e.Status, "idx_deliveries_pending").HasFilter("((status)::text = 'pending'::text)");

            entity.Property(e => e.Id)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.Channel)
                .HasMaxLength(20)
                .HasColumnName("channel");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("created_at");
            entity.Property(e => e.ErrorMessage).HasColumnName("error_message");
            entity.Property(e => e.NotificationId).HasColumnName("notification_id");
            entity.Property(e => e.Provider)
                .HasMaxLength(50)
                .HasColumnName("provider");
            entity.Property(e => e.ProviderMessageId)
                .HasMaxLength(255)
                .HasColumnName("provider_message_id");
            entity.Property(e => e.SentAt).HasColumnName("sent_at");
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .HasDefaultValueSql("'pending'::character varying")
                .HasColumnName("status");

            entity.HasOne(d => d.Notification).WithMany(p => p.NotificationDeliveries)
                .HasForeignKey(d => d.NotificationId)
                .HasConstraintName("notification_deliveries_notification_id_fkey");
        });

        modelBuilder.Entity<Order>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("orders_pkey");

            entity.ToTable("orders");

            entity.HasIndex(e => e.BuyerId, "idx_orders_buyer");

            entity.HasIndex(e => e.OrderNumber, "idx_orders_number");

            entity.HasIndex(e => e.ShopId, "idx_orders_shop");

            entity.HasIndex(e => e.Status, "idx_orders_status");

            entity.HasIndex(e => e.OrderNumber, "orders_order_number_key").IsUnique();

            entity.Property(e => e.Id)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.Amount)
                .HasPrecision(10, 2)
                .HasColumnName("amount");
            entity.Property(e => e.SubtotalAmount)
                .HasPrecision(10, 2)
                .HasColumnName("subtotal_amount");
            entity.Property(e => e.DiscountAmount)
                .HasPrecision(10, 2)
                .HasDefaultValueSql("0.00")
                .HasColumnName("discount_amount");
            entity.Property(e => e.BuyerId).HasColumnName("buyer_id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("created_at");
            entity.Property(e => e.Currency)
                .HasMaxLength(3)
                .HasDefaultValueSql("'USD'::character varying")
                .HasColumnName("currency");
            entity.Property(e => e.InvoicePdfUrl).HasColumnName("invoice_pdf_url");
            entity.Property(e => e.OrderNumber)
                .HasMaxLength(50)
                .HasColumnName("order_number");
            entity.Property(e => e.PlatformFee)
                .HasPrecision(10, 2)
                .HasDefaultValueSql("0.00")
                .HasColumnName("platform_fee");
            entity.Property(e => e.CommissionRate)
                .HasPrecision(6, 5)
                .HasColumnName("commission_rate");
            entity.Property(e => e.ProductId).HasColumnName("product_id");
            entity.Property(e => e.SellerEarnings)
                .HasPrecision(10, 2)
                .HasDefaultValueSql("0.00")
                .HasColumnName("seller_earnings");
            entity.Property(e => e.ShopId).HasColumnName("shop_id");
            entity.Property(e => e.SubscriptionPlanId).HasColumnName("subscription_plan_id");
            entity.Property(e => e.Status)
                .HasColumnType("order_status")
                .HasDefaultValue(OrderStatus.Pending)
                .HasColumnName("status");
            entity.Property(e => e.StripePaymentId)
                .HasMaxLength(255)
                .HasColumnName("stripe_payment_id");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.Buyer).WithMany(p => p.Orders)
                .HasForeignKey(d => d.BuyerId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("orders_buyer_id_fkey");

            entity.HasOne(d => d.Product).WithMany(p => p.Orders)
                .HasForeignKey(d => d.ProductId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("orders_product_id_fkey");

            entity.HasOne(d => d.Shop).WithMany(p => p.Orders)
                .HasForeignKey(d => d.ShopId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("orders_shop_id_fkey");
        });

        modelBuilder.Entity<Payment>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("payments_pkey");

            entity.ToTable("payments");

            entity.HasIndex(e => e.ProviderTransactionId, "idx_payments_transaction_id");

            entity.HasIndex(e => e.Status, "idx_payments_status");

            entity.HasIndex(e => e.OrderId, "payments_order_id_key").IsUnique();

            entity.HasIndex(e => e.ProviderTransactionId, "payments_provider_transaction_id_key").IsUnique();

            entity.Property(e => e.Id)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("created_at");
            entity.Property(e => e.ErrorMessage).HasColumnName("error_message");
            entity.Property(e => e.GrossAmount)
                .HasPrecision(10, 2)
                .HasColumnName("gross_amount");
            entity.Property(e => e.NetEarnings)
                .HasPrecision(10, 2)
                .HasColumnName("net_earnings");
            entity.Property(e => e.OrderId).HasColumnName("order_id");
            entity.Property(e => e.PaymentProvider)
                .HasMaxLength(50)
                .HasColumnName("payment_provider");
            entity.Property(e => e.PlatformFeeAmount)
                .HasPrecision(10, 2)
                .HasColumnName("platform_fee_amount");
            entity.Property(e => e.CommissionRate)
                .HasPrecision(6, 5)
                .HasColumnName("commission_rate");
            entity.Property(e => e.ProviderTransactionId)
                .HasMaxLength(255)
                .HasColumnName("provider_transaction_id");
            entity.Property(e => e.Status)
                .HasColumnType("payment_status_type")
                .HasDefaultValue(PaymentStatusType.Processing)
                .HasColumnName("status");
            entity.Property(e => e.SubscriptionPlanId).HasColumnName("subscription_plan_id");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.Order).WithOne(p => p.Payment)
                .HasForeignKey<Payment>(d => d.OrderId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("payments_order_id_fkey");
        });

        modelBuilder.Entity<PointLog>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("point_logs_pkey");

            entity.ToTable("point_logs");

            entity.HasIndex(e => new { e.UserId, e.CreatedAt }, "idx_point_logs_user_date");

            entity.Property(e => e.Id)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.ActionType)
                .HasMaxLength(50)
                .HasColumnName("action_type");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("created_at");
            entity.Property(e => e.PointsEarned)
                .HasPrecision(10, 2)
                .HasColumnName("points_earned");
            entity.Property(e => e.ReferenceId).HasColumnName("reference_id");
            entity.Property(e => e.UserId).HasColumnName("user_id");

            entity.HasOne(d => d.User).WithMany(p => p.PointLogs)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("point_logs_user_id_fkey");
        });

        modelBuilder.Entity<Product>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("products_pkey");

            entity.ToTable("products");

            entity.HasIndex(e => e.ShopId, "idx_products_shop");

            entity.Property(e => e.Id)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.CoverImageUrl).HasColumnName("cover_image_url");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("created_at");
            entity.Property(e => e.CategoryId).HasColumnName("category_id");
            entity.Property(e => e.Currency)
                .HasMaxLength(3)
                .HasDefaultValueSql("'USD'::character varying")
                .HasColumnName("currency");
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.DiscountEndsAt).HasColumnName("discount_ends_at");
            entity.Property(e => e.DiscountPrice)
                .HasPrecision(10, 2)
                .HasColumnName("discount_price");
            entity.Property(e => e.FileUrl).HasColumnName("file_url");
            entity.Property(e => e.IsActive)
                .HasDefaultValue(true)
                .HasColumnName("is_active");
            entity.Property(e => e.IsFeatured)
                .HasDefaultValue(false)
                .HasColumnName("is_featured");
            entity.Property(e => e.Metadata)
                .HasDefaultValueSql("'{}'::jsonb")
                .HasColumnType("jsonb")
                .HasColumnName("metadata");
            entity.Property(e => e.Price)
                .HasPrecision(10, 2)
                .HasColumnName("price");
            entity.Property(e => e.OriginalPrice)
                .HasPrecision(10, 2)
                .HasColumnName("original_price");
            entity.Property(e => e.PreviewVideoUrl).HasColumnName("preview_video_url");
            entity.Property(e => e.RatingAverage)
                .HasPrecision(3, 2)
                .HasDefaultValueSql("0.0")
                .HasColumnName("rating_average");
            entity.Property(e => e.ReviewCount)
                .HasDefaultValue(0)
                .HasColumnName("review_count");
            entity.Property(e => e.SalesCount)
                .HasDefaultValue(0)
                .HasColumnName("sales_count");
            entity.Property(e => e.ShopId).HasColumnName("shop_id");
            entity.Property(e => e.Status)
                .HasConversion<string>()
                .HasMaxLength(20)
                .HasDefaultValue(ProductStatus.Draft)
                .HasColumnName("status");
            entity.Property(e => e.Tags)
                .HasColumnType("text[]")
                .HasColumnName("tags");
            entity.Property(e => e.Title)
                .HasMaxLength(255)
                .HasColumnName("title");
            entity.Property(e => e.Type)
                .HasColumnType("product_type")
                .HasDefaultValue(ProductType.DigitalFile)
                .HasColumnName("type");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.Shop).WithMany(p => p.Products)
                .HasForeignKey(d => d.ShopId)
                .HasConstraintName("products_shop_id_fkey");

            entity.HasOne(d => d.Category).WithMany(p => p.Products)
                .HasForeignKey(d => d.CategoryId)
                .HasConstraintName("products_category_id_fkey");
        });

        modelBuilder.Entity<ProductImage>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("product_images_pkey");

            entity.ToTable("product_images");

            entity.HasIndex(e => e.ProductId, "idx_product_images_product");

            entity.HasIndex(e => new { e.ProductId, e.ObjectKey }, "product_images_product_id_object_key_key").IsUnique();

            entity.Property(e => e.Id)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("created_at");
            entity.Property(e => e.ObjectKey).HasColumnName("object_key");
            entity.Property(e => e.ProductId).HasColumnName("product_id");
            entity.Property(e => e.SortOrder).HasColumnName("sort_order");

            entity.HasOne(d => d.Product).WithMany(p => p.ProductImages)
                .HasForeignKey(d => d.ProductId)
                .HasConstraintName("product_images_product_id_fkey");
        });

        modelBuilder.Entity<ProductQa>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("product_qa_pkey");

            entity.ToTable("product_qa");

            entity.Property(e => e.Id)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("created_at");
            entity.Property(e => e.Message).HasColumnName("message");
            entity.Property(e => e.ParentId).HasColumnName("parent_id");
            entity.Property(e => e.ProductId).HasColumnName("product_id");
            entity.Property(e => e.UserId).HasColumnName("user_id");

            entity.HasOne(d => d.Parent).WithMany(p => p.InverseParent)
                .HasForeignKey(d => d.ParentId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("product_qa_parent_id_fkey");

            entity.HasOne(d => d.Product).WithMany(p => p.ProductQas)
                .HasForeignKey(d => d.ProductId)
                .HasConstraintName("product_qa_product_id_fkey");

            entity.HasOne(d => d.User).WithMany(p => p.ProductQas)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("product_qa_user_id_fkey");
        });

        modelBuilder.Entity<Review>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("reviews_pkey");

            entity.ToTable("reviews");

            entity.Property(e => e.Id)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.Comment).HasColumnName("comment");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("created_at");
            entity.Property(e => e.Images)
                .HasDefaultValueSql("'[]'::jsonb")
                .HasColumnType("jsonb")
                .HasColumnName("images")
                .HasConversion(
                    v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                    v => JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null) ?? new List<string>())
                .Metadata.SetValueComparer(new ValueComparer<List<string>>(
                    (left, right) => left != null && right != null && left.SequenceEqual(right),
                    value => value.Aggregate(0, (hash, item) => HashCode.Combine(hash, item.GetHashCode())),
                    value => value.ToList()));
            entity.Property(e => e.ProductId).HasColumnName("product_id");
            entity.Property(e => e.Rating).HasColumnName("rating");
            entity.Property(e => e.SellerReply).HasColumnName("seller_reply");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("updated_at");
            entity.Property(e => e.UserId).HasColumnName("user_id");

            entity.HasOne(d => d.Product).WithMany(p => p.Reviews)
                .HasForeignKey(d => d.ProductId)
                .HasConstraintName("reviews_product_id_fkey");

            entity.HasOne(d => d.User).WithMany(p => p.Reviews)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("reviews_user_id_fkey");
        });

        modelBuilder.Entity<SellerSubscriptionPlan>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("seller_subscription_plans_pkey");

            entity.ToTable("seller_subscription_plans");

            entity.HasIndex(e => e.Code, "seller_subscription_plans_code_key").IsUnique();
            entity.HasIndex(e => new { e.IsActive, e.SortOrder }, "idx_seller_subscription_plans_active_sort");

            entity.Property(e => e.Id)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.Code)
                .HasMaxLength(50)
                .HasColumnName("code");
            entity.Property(e => e.Name)
                .HasMaxLength(100)
                .HasColumnName("name");
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.MonthlyAmount)
                .HasPrecision(12, 2)
                .HasColumnName("monthly_amount");
            entity.Property(e => e.Currency)
                .HasMaxLength(3)
                .HasColumnName("currency");
            entity.Property(e => e.CommissionRate)
                .HasPrecision(6, 5)
                .HasColumnName("commission_rate");
            entity.Property(e => e.Features)
                .HasColumnType("text[]")
                .HasColumnName("features");
            entity.Property(e => e.SortOrder).HasColumnName("sort_order");
            entity.Property(e => e.IsActive).HasColumnName("is_active");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("created_at");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("updated_at");
        });

        modelBuilder.Entity<SellerSubscription>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("seller_subscriptions_pkey");

            entity.ToTable("seller_subscriptions");

            entity.HasIndex(e => e.ShopId, "seller_subscriptions_shop_id_key").IsUnique();

            entity.HasIndex(e => e.ProviderSubscriptionId, "seller_subscriptions_stripe_subscription_id_key").IsUnique();

            entity.Property(e => e.Id)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.Amount)
                .HasPrecision(10, 2)
                .HasDefaultValueSql("25.00")
                .HasColumnName("amount");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("created_at");
            entity.Property(e => e.Currency)
                .HasMaxLength(3)
                .HasDefaultValueSql("'USD'::character varying")
                .HasColumnName("currency");
            entity.Property(e => e.CurrentPeriodEnd).HasColumnName("current_period_end");
            entity.Property(e => e.GracePeriodEnd).HasColumnName("grace_period_end");
            entity.Property(e => e.ReminderSentAt).HasColumnName("reminder_sent_at");
            entity.Property(e => e.PaymentProvider)
                .HasMaxLength(50)
                .HasDefaultValueSql("'stripe'::character varying")
                .HasColumnName("payment_provider");
            entity.Property(e => e.ProviderSubscriptionId)
                .HasMaxLength(255)
                .HasColumnName("provider_subscription_id");
            entity.Property(e => e.PlanId).HasColumnName("plan_id");
            entity.Property(e => e.ShopId).HasColumnName("shop_id");
            entity.Property(e => e.Status)
                .HasColumnType("sub_status")
                .HasDefaultValue(SubStatus.Active)
                .HasColumnName("status");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.Shop).WithOne(p => p.SellerSubscription)
                .HasForeignKey<SellerSubscription>(d => d.ShopId)
                .HasConstraintName("seller_subscriptions_shop_id_fkey");

            entity.HasOne(d => d.Plan).WithMany(p => p.Subscriptions)
                .HasForeignKey(d => d.PlanId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("seller_subscriptions_plan_id_fkey");
        });

        modelBuilder.Entity<SellerSubscriptionPayment>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("seller_subscription_payments_pkey");

            entity.ToTable("seller_subscription_payments");

            entity.HasIndex(e => new { e.SubscriptionId, e.CreatedAt }, "idx_seller_subscription_payments_subscription_date")
                .IsDescending(false, true);
            entity.HasIndex(e => new { e.Status, e.CreatedAt }, "idx_seller_subscription_payments_status_date")
                .IsDescending(false, true);
            entity.HasIndex(e => new { e.PaymentProvider, e.ProviderTransactionId }, "uq_seller_subscription_payments_provider_transaction")
                .IsUnique()
                .HasFilter("provider_transaction_id IS NOT NULL");
            entity.HasIndex(e => new { e.SubscriptionId, e.BillingPeriodStart, e.BillingPeriodEnd }, "uq_seller_subscription_payments_subscription_period")
                .IsUnique()
                .HasFilter("billing_period_start IS NOT NULL AND billing_period_end IS NOT NULL");

            entity.Property(e => e.Id)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.Amount)
                .HasPrecision(12, 2)
                .HasColumnName("amount");
            entity.Property(e => e.CommissionRate)
                .HasPrecision(6, 5)
                .HasColumnName("commission_rate");
            entity.Property(e => e.BillingPeriodEnd).HasColumnName("billing_period_end");
            entity.Property(e => e.BillingPeriodStart).HasColumnName("billing_period_start");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("created_at");
            entity.Property(e => e.Currency)
                .HasMaxLength(3)
                .HasColumnName("currency");
            entity.Property(e => e.PaymentProvider)
                .HasMaxLength(50)
                .HasColumnName("payment_provider");
            entity.Property(e => e.ProviderTransactionId)
                .HasMaxLength(255)
                .HasColumnName("provider_transaction_id");
            entity.Property(e => e.PlanId).HasColumnName("plan_id");
            entity.Property(e => e.ShopId).HasColumnName("shop_id");
            entity.Property(e => e.Status)
                .HasMaxLength(30)
                .HasColumnName("status");
            entity.Property(e => e.SubscriptionId).HasColumnName("subscription_id");

            entity.HasOne(d => d.Plan).WithMany()
                .HasForeignKey(d => d.PlanId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("seller_subscription_payments_plan_id_fkey");
        });

        modelBuilder.Entity<SellerNotificationPreference>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("seller_notification_preferences_pkey");

            entity.ToTable("seller_notification_preferences");

            entity.HasIndex(e => e.UserId, "idx_seller_notification_preferences_user");

            entity.HasIndex(e => e.UserId, "seller_notification_preferences_user_id_key")
                .IsUnique();

            entity.Property(e => e.Id)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.OrderEmails)
                .HasDefaultValue(true)
                .HasColumnName("order_emails");
            entity.Property(e => e.WeeklyReportEmails)
                .HasDefaultValue(true)
                .HasColumnName("weekly_report_emails");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("created_at");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.User).WithOne(p => p.SellerNotificationPreference)
                .HasForeignKey<SellerNotificationPreference>(d => d.UserId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("seller_notification_preferences_user_id_fkey");
        });

        modelBuilder.Entity<Shop>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("shops_pkey");

            entity.ToTable("shops");

            entity.HasIndex(e => e.ShopName, "idx_shops_name");

            entity.HasIndex(e => e.Slug, "idx_shops_slug");

            entity.HasIndex(e => e.Slug, "shops_slug_key").IsUnique();

            entity.HasIndex(e => e.UserId, "shops_user_id_key").IsUnique();

            entity.Property(e => e.Id)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.AboutContent).HasColumnName("about_content");
            entity.Property(e => e.BannerUrl).HasColumnName("banner_url");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("created_at");
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.ExternalUrl)
                .HasMaxLength(255)
                .HasColumnName("external_url");
            entity.Property(e => e.FollowerCount)
                .HasDefaultValue(0)
                .HasColumnName("follower_count");
            entity.Property(e => e.IsActive)
                .HasDefaultValue(true)
                .HasColumnName("is_active");
            entity.Property(e => e.IsVerified)
                .HasDefaultValue(false)
                .HasColumnName("is_verified");
            entity.Property(e => e.LogoUrl).HasColumnName("logo_url");
            entity.Property(e => e.Rating)
                .HasPrecision(3, 2)
                .HasDefaultValueSql("0.0")
                .HasColumnName("rating");
            entity.Property(e => e.ShopName)
                .HasMaxLength(100)
                .HasColumnName("shop_name");
            entity.Property(e => e.ShortDescription)
                .HasMaxLength(255)
                .HasColumnName("short_description");
            entity.Property(e => e.Slug)
                .HasColumnType("citext")
                .HasColumnName("slug");
            entity.Property(e => e.SocialLinks)
                .HasDefaultValueSql("'{}'::jsonb")
                .HasColumnType("jsonb")
                .HasColumnName("social_links");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("updated_at");
            entity.Property(e => e.UserId).HasColumnName("user_id");

            entity.HasOne(d => d.User).WithOne(p => p.Shop)
                .HasForeignKey<Shop>(d => d.UserId)
                .HasConstraintName("shops_user_id_fkey");
        });

        modelBuilder.Entity<ShopVisit>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("shop_visits_pkey");

            entity.ToTable("shop_visits");

            entity.HasIndex(e => new { e.ShopId, e.VisitedAt }, "idx_shop_visits_composite");

            entity.Property(e => e.Id)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.IpAddress).HasColumnName("ip_address");
            entity.Property(e => e.ShopId).HasColumnName("shop_id");
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.VisitedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("visited_at");

            entity.HasOne(d => d.Shop).WithMany(p => p.ShopVisits)
                .HasForeignKey(d => d.ShopId)
                .HasConstraintName("shop_visits_shop_id_fkey");

            entity.HasOne(d => d.User).WithMany(p => p.ShopVisits)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("shop_visits_user_id_fkey");
        });

        modelBuilder.Entity<Subscription>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("subscriptions_pkey");

            entity.ToTable("subscriptions");

            entity.HasIndex(e => new { e.ShopId, e.UserId }, "unique_subscription").IsUnique();

            entity.Property(e => e.Id)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("created_at");
            entity.Property(e => e.ShopId).HasColumnName("shop_id");
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.WantsNotifications)
                .HasDefaultValue(true)
                .HasColumnName("wants_notifications");

            entity.HasOne(d => d.Shop).WithMany(p => p.Subscriptions)
                .HasForeignKey(d => d.ShopId)
                .HasConstraintName("subscriptions_shop_id_fkey");

            entity.HasOne(d => d.User).WithMany(p => p.Subscriptions)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("subscriptions_user_id_fkey");
        });

        modelBuilder.Entity<SupportTicket>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("support_tickets_pkey");

            entity.ToTable("support_tickets");

            entity.HasIndex(e => new { e.UserId, e.LastMessageAt }, "idx_support_tickets_user_last_message")
                .IsDescending(false, true);

            entity.HasIndex(e => new { e.Status, e.LastMessageAt }, "idx_support_tickets_status_last_message")
                .IsDescending(false, true);

            entity.Property(e => e.Id)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.ClosedAt).HasColumnName("closed_at");
            entity.Property(e => e.ClosedByUserId).HasColumnName("closed_by_user_id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("created_at");
            entity.Property(e => e.LastMessageAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("last_message_at");
            entity.Property(e => e.Status)
                .HasColumnType("support_ticket_status")
                .HasDefaultValue(SupportTicketStatus.Open)
                .HasColumnName("status");
            entity.Property(e => e.Subject)
                .HasMaxLength(200)
                .HasColumnName("subject");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("updated_at");
            entity.Property(e => e.UserId).HasColumnName("user_id");

            entity.HasOne(d => d.User).WithMany(p => p.SupportTickets)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("support_tickets_user_id_fkey");

            entity.HasOne(d => d.ClosedByUser).WithMany(p => p.ClosedSupportTickets)
                .HasForeignKey(d => d.ClosedByUserId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("support_tickets_closed_by_user_id_fkey");
        });

        modelBuilder.Entity<SupportTicketMessage>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("support_ticket_messages_pkey");

            entity.ToTable("support_ticket_messages");

            entity.HasIndex(e => new { e.TicketId, e.CreatedAt }, "idx_support_ticket_messages_ticket_created");

            entity.Property(e => e.Id)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("created_at");
            entity.Property(e => e.Message).HasColumnName("message");
            entity.Property(e => e.SenderId).HasColumnName("sender_id");
            entity.Property(e => e.SenderRole)
                .HasColumnType("support_message_sender_role")
                .HasColumnName("sender_role");
            entity.Property(e => e.TicketId).HasColumnName("ticket_id");

            entity.HasOne(d => d.Ticket).WithMany(p => p.Messages)
                .HasForeignKey(d => d.TicketId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("support_ticket_messages_ticket_id_fkey");

            entity.HasOne(d => d.Sender).WithMany(p => p.SupportTicketMessages)
                .HasForeignKey(d => d.SenderId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("support_ticket_messages_sender_id_fkey");
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("users_pkey");

            entity.ToTable("users");

            entity.HasIndex(e => e.Email, "users_email_key").IsUnique();

            entity.HasIndex(e => e.ProviderId, "idx_provider_id_not_null")
                .IsUnique()
                .HasFilter("provider_id IS NOT NULL");
            entity.Property(e => e.DeletedAt)
                .HasColumnName("deleted_at");

            entity.Property(e => e.Id)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.AuthProvider)
                .HasMaxLength(50)
                .HasDefaultValueSql("'email'::character varying")
                .HasColumnName("auth_provider");
            entity.Property(e => e.AvatarUrl).HasColumnName("avatar_url");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("created_at");
            entity.Property(e => e.Email)
                .HasColumnType("citext")
                .HasColumnName("email");
            entity.Property(e => e.FullName)
                .HasMaxLength(100)
                .HasColumnName("full_name");
            entity.Property(e => e.IsActive)
                .HasDefaultValue(true)
                .HasColumnName("is_active");
            entity.Property(e => e.IsEmailVerified)
                .HasDefaultValue(false)
                .HasColumnName("is_email_verified");
            entity.Property(e => e.LastLoginAt).HasColumnName("last_login_at");
            entity.Property(e => e.LockedUntil).HasColumnName("locked_until");
            entity.Property(e => e.LockReason).HasColumnName("lock_reason");
            entity.Property(e => e.PasswordHash).HasColumnName("password_hash");
            entity.Property(e => e.Preferences)
                .HasDefaultValueSql("'{}'::jsonb")
                .HasColumnType("jsonb")
                .HasColumnName("preferences");
            entity.Property(e => e.ProviderId)
                .HasMaxLength(255)
                .HasColumnName("provider_id");
            entity.Property(e => e.Role)
                .HasColumnType("user_role")
                .HasDefaultValue(UserRole.User)
                .HasColumnName("role");
            entity.Property(e => e.StripeAccountId)
                .HasMaxLength(255)
                .HasColumnName("stripe_account_id");
            entity.Property(e => e.StripeCustomerId)
                .HasMaxLength(255)
                .HasColumnName("stripe_customer_id");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("updated_at");
        });

        modelBuilder.Entity<UserDeviceToken>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("user_device_tokens_pkey");

            entity.ToTable("user_device_tokens");

            entity.HasIndex(e => e.UserId, "idx_device_tokens_user").HasFilter("(is_active = true)");

            entity.HasIndex(e => new { e.UserId, e.DeviceId }, "user_device_tokens_user_id_device_id_key").IsUnique();

            entity.Property(e => e.Id)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("created_at");
            entity.Property(e => e.DeviceId)
                .HasMaxLength(255)
                .HasColumnName("device_id");
            entity.Property(e => e.DeviceType)
                .HasMaxLength(20)
                .HasColumnName("device_type");
            entity.Property(e => e.IsActive)
                .HasDefaultValue(true)
                .HasColumnName("is_active");
            entity.Property(e => e.LastUsedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("last_used_at");
            entity.Property(e => e.Token).HasColumnName("token");
            entity.Property(e => e.UserId).HasColumnName("user_id");

            entity.HasOne(d => d.User).WithMany(p => p.UserDeviceTokens)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("user_device_tokens_user_id_fkey");
        });

        modelBuilder.Entity<UserLibrary>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("user_library_pkey");

            entity.ToTable("user_library");

            entity.HasIndex(e => new { e.UserId, e.LastAccessedAt }, "idx_user_library_accessed").IsDescending(false, true);

            entity.HasIndex(e => new { e.UserId, e.ProductId }, "user_library_user_id_product_id_key").IsUnique();

            entity.Property(e => e.Id)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.LastAccessedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("last_accessed_at");
            entity.Property(e => e.ProductId).HasColumnName("product_id");
            entity.Property(e => e.PurchasedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("purchased_at");
            entity.Property(e => e.UserId).HasColumnName("user_id");

            entity.HasOne(d => d.Product).WithMany(p => p.UserLibraries)
                .HasForeignKey(d => d.ProductId)
                .HasConstraintName("user_library_product_id_fkey");

            entity.HasOne(d => d.User).WithMany(p => p.UserLibraries)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("user_library_user_id_fkey");
        });

        modelBuilder.Entity<UserPoint>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("user_points_pkey");

            entity.ToTable("user_points");

            entity.HasIndex(e => e.UserId, "user_points_user_id_key").IsUnique();

            entity.Property(e => e.Id)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.CurrentRank)
                .HasDefaultValue(0)
                .HasColumnName("current_rank");
            entity.Property(e => e.CurrentStreak)
                .HasDefaultValue(0)
                .HasColumnName("current_streak");
            entity.Property(e => e.TotalPoints)
                .HasPrecision(12, 2)
                .HasDefaultValueSql("0.0")
                .HasColumnName("total_points");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("updated_at");
            entity.Property(e => e.UserId).HasColumnName("user_id");

            entity.HasOne(d => d.User).WithOne(p => p.UserPoint)
                .HasForeignKey<UserPoint>(d => d.UserId)
                .HasConstraintName("user_points_user_id_fkey");
        });

        modelBuilder.Entity<UserSession>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("user_sessions_pkey");

            entity.ToTable("user_sessions");
            entity.Property(e => e.IsRevoked)
                .HasDefaultValue(false)
                .HasColumnName("is_revoked");
            entity.Property(e => e.IpAddress)
                .HasColumnName("ip_address");
            entity.Property(e => e.Id)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("created_at");
            entity.Property(e => e.DeviceId)
                .HasMaxLength(255)
                .HasColumnName("device_id");
            entity.Property(e => e.ExpiresAt).HasColumnName("expires_at");
            entity.Property(e => e.IpAddress).HasColumnName("ip_address");
            entity.Property(e => e.RefreshToken).HasColumnName("refresh_token");
            entity.Property(e => e.UserAgent).HasColumnName("user_agent");
            entity.Property(e => e.UserId).HasColumnName("user_id");

            entity.HasOne(d => d.User).WithMany(p => p.UserSessions)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("user_sessions_user_id_fkey");
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
