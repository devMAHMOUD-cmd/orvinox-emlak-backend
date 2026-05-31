using System;
using System.Collections.Generic;

namespace CraftoraApi.Models.Entities;

public partial class ProductQa
{
    public Guid Id { get; set; }

    public Guid ProductId { get; set; }

    public Guid UserId { get; set; }

    public Guid? ParentId { get; set; }

    public string Message { get; set; } = null!;

    public DateTime? CreatedAt { get; set; }

    public virtual ICollection<ProductQa> InverseParent { get; set; } = new List<ProductQa>();

    public virtual ProductQa? Parent { get; set; }

    public virtual Product Product { get; set; } = null!;

    public virtual User User { get; set; } = null!;
}
