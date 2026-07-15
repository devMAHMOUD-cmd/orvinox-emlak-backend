using System;
using System.Collections.Generic;
using CraftoraApi.Models.Enums;

namespace CraftoraApi.Models.Entities;

public partial class User
{
    public Guid Id { get; set; }

    public string Email { get; set; } = null!;

    public string? FullName { get; set; }

    public string? AvatarUrl { get; set; }

    public UserRole Role { get; set; }

    public string? AuthProvider { get; set; }

    public string? ProviderId { get; set; }

    public string? PasswordHash { get; set; }

    public bool? IsEmailVerified { get; set; }

    public DateTime? LockedUntil { get; set; }

    public string? StripeCustomerId { get; set; }

    public string? StripeAccountId { get; set; }

    public string? Preferences { get; set; }

    public bool? IsActive { get; set; }

    public DateTime? LastLoginAt { get; set; }

    public DateTime? DeletedAt { get; set; }

    public DateTime? CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public virtual ICollection<CartItem> CartItems { get; set; } = new List<CartItem>();

    public virtual ICollection<ContestResult> ContestResults { get; set; } = new List<ContestResult>();

    public virtual ICollection<Contest> Contests { get; set; } = new List<Contest>();

    public virtual ICollection<CouponUse> CouponUses { get; set; } = new List<CouponUse>();

    public virtual ICollection<LessonProgress> LessonProgresses { get; set; } = new List<LessonProgress>();

    public virtual ICollection<UserLessonProgress> UserLessonProgresses { get; set; } = new List<UserLessonProgress>();

    public virtual ICollection<MediaComment> MediaComments { get; set; } = new List<MediaComment>();

    public virtual ICollection<MediaLike> MediaLikes { get; set; } = new List<MediaLike>();

    public virtual ICollection<MediaSafe> MediaSaves { get; set; } = new List<MediaSafe>();

    public virtual ICollection<MediaWatchHistory> MediaWatchHistories { get; set; } = new List<MediaWatchHistory>();

    public virtual ICollection<Notification> Notifications { get; set; } = new List<Notification>();

    public virtual ICollection<Order> Orders { get; set; } = new List<Order>();

    public virtual ICollection<PointLog> PointLogs { get; set; } = new List<PointLog>();

    public virtual ICollection<ProductQa> ProductQas { get; set; } = new List<ProductQa>();

    public virtual ICollection<Review> Reviews { get; set; } = new List<Review>();

    public virtual Shop? Shop { get; set; }

    public virtual ICollection<ShopVisit> ShopVisits { get; set; } = new List<ShopVisit>();

    public virtual ICollection<Subscription> Subscriptions { get; set; } = new List<Subscription>();

    public virtual ICollection<SupportTicket> SupportTickets { get; set; } = new List<SupportTicket>();

    public virtual ICollection<SupportTicket> ClosedSupportTickets { get; set; } = new List<SupportTicket>();

    public virtual ICollection<SupportTicketMessage> SupportTicketMessages { get; set; } = new List<SupportTicketMessage>();

    public virtual ICollection<UserDeviceToken> UserDeviceTokens { get; set; } = new List<UserDeviceToken>();

    public virtual ICollection<UserLibrary> UserLibraries { get; set; } = new List<UserLibrary>();

    public virtual UserPoint? UserPoint { get; set; }

    public virtual ICollection<UserSession> UserSessions { get; set; } = new List<UserSession>();
}
