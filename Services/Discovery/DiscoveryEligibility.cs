using System.Linq.Expressions;
using CraftoraApi.Models.Entities;
using CraftoraApi.Models.Enums;

namespace CraftoraApi.Services.Discovery;

public static class DiscoveryEligibility
{
    public static readonly Expression<Func<Medium, bool>> ReadyMedia = media =>
        media.IsActive == true &&
        media.Status == MediaStatus.Ready &&
        media.Shop.IsActive == true;
}
