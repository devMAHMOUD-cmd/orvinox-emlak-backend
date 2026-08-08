using System.ComponentModel.DataAnnotations;
using CraftoraApi.DTOs.Shop;

namespace CraftoraApi.DTOs.Subscription;

public sealed record StartShopSubscriptionRequestDto(
    [property: Required]
    CreateShopDto Shop,

    [property: Required]
    StartSubscriptionRequestDto Payment);
