using System;
using System.Collections.Generic;
using CraftoraApi.Models.Enums;

namespace CraftoraApi.Models.Entities;

public partial class Payment
{
    public Guid Id { get; set; }

    public Guid OrderId { get; set; }

    public string PaymentProvider { get; set; } = null!;

    public string? ProviderTransactionId { get; set; }

    public decimal GrossAmount { get; set; }

    public decimal PlatformFeeAmount { get; set; }

    public decimal NetEarnings { get; set; }

    public PaymentStatusType Status { get; set; }

    public string? ErrorMessage { get; set; }

    public DateTime? CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public virtual Order Order { get; set; } = null!;
}
