namespace CraftoraApi.Models.Elasticsearch;

public sealed class ProductDocument
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string Type { get; set; } = string.Empty;

    public decimal Price { get; set; }

    public Guid CategoryId { get; set; }

    public Guid ShopId { get; set; }

    public string? ShopName { get; set; }

    public bool IsActive { get; set; }

    public bool IsPublished { get; set; }

    public bool ShopIsActive { get; set; }
}
