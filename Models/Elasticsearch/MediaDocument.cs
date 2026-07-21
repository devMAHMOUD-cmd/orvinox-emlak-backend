namespace CraftoraApi.Models.Elasticsearch;

public sealed class MediaDocument
{
    public Guid Id { get; set; }

    public string? Caption { get; set; }

    public List<string> Hashtags { get; set; } = new();

    public Guid ShopId { get; set; }

    public string ShopName { get; set; } = string.Empty;

    public string? ShopSlug { get; set; }

    public Guid? ProductId { get; set; }

    public string? ProductTitle { get; set; }

    public string? ProductType { get; set; }

    public string? ThumbnailObjectKey { get; set; }

    public string? VideoObjectKey { get; set; }

    public string? ProductCoverImageObjectKey { get; set; }

    public bool IsActive { get; set; }

    public bool ShopIsActive { get; set; }

    public DateTime? CreatedAt { get; set; }

    public int ViewCount { get; set; }

    public int LikeCount { get; set; }

    public int SaveCount { get; set; }

    public int ShareCount { get; set; }
}
