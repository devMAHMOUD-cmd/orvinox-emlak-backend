namespace CraftoraApi.Models.Elasticsearch;

public sealed class ShopDocument
{
    public Guid Id { get; set; }

    public string ShopName { get; set; } = string.Empty;

    public string Slug { get; set; } = string.Empty;

    public string? ShortDescription { get; set; }

    public string? LogoObjectKey { get; set; }

    public string? BannerObjectKey { get; set; }

    public bool IsActive { get; set; }

    public bool IsVerified { get; set; }

    public int FollowerCount { get; set; }
}
