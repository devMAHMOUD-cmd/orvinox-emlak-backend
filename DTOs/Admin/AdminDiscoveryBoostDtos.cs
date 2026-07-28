using System.ComponentModel.DataAnnotations;

namespace CraftoraApi.DTOs.Admin;

public sealed record AdminDiscoveryBoostRequestDto(
    [property: Required]
    [property: StringLength(20)]
    string ContentType,

    Guid ContentId,

    [property: Range(1, 100000)]
    int CreditAmount,

    DateTimeOffset? StartsAt,

    DateTimeOffset? EndsAt);

public sealed record AdminDiscoveryBoostDto(
    Guid BoostId,
    string ContentType,
    Guid ContentId,
    Guid ShopId,
    string? ContentTitle,
    int CreditTotal,
    int CreditRemaining,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    bool Enabled,
    DateTimeOffset? UpdatedAt);
