using System;

namespace CraftoraApi.Models.Entities;

public partial class ProductImage
{
    public Guid Id { get; set; }

    public Guid ProductId { get; set; }

    public string ObjectKey { get; set; } = null!;

    public int SortOrder { get; set; }

    public DateTime? CreatedAt { get; set; }

    public virtual Product Product { get; set; } = null!;
}
