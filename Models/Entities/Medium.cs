using System;
using System.Collections.Generic;

using CraftoraApi.Models.Enums;

namespace CraftoraApi.Models.Entities;

public partial class Medium
{
    public Guid Id { get; set; }

    public Guid ShopId { get; set; }

    public Guid? ProductId { get; set; }

    public string VideoUrl { get; set; } = null!;

    public string? ThumbnailUrl { get; set; }

    public int? ViewCount { get; set; }

    public int? LikeCount { get; set; }

    public int? SaveCount { get; set; }

    public int? ShareCount { get; set; }

    public int? CommentCount { get; set; }

    public DateTime? CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public bool? IsActive { get; set; }

    public string? Caption { get; set; }

    public List<string>? Hashtags { get; set; }

    public int? DurationSeconds { get; set; }

    public MediaStatus Status { get; set; }

    public virtual ICollection<MediaComment> MediaComments { get; set; } = new List<MediaComment>();

    public virtual ICollection<MediaLike> MediaLikes { get; set; } = new List<MediaLike>();

    public virtual ICollection<MediaSafe> MediaSaves { get; set; } = new List<MediaSafe>();

    public virtual ICollection<MediaWatchHistory> MediaWatchHistories { get; set; } = new List<MediaWatchHistory>();

    public virtual Product? Product { get; set; }

    public virtual Shop Shop { get; set; } = null!;
}
