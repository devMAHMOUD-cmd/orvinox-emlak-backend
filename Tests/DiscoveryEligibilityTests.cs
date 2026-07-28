using CraftoraApi.Models.Entities;
using CraftoraApi.Models.Enums;
using CraftoraApi.Services.Discovery;
using Xunit;

namespace CraftoraApi.Tests;

public sealed class DiscoveryEligibilityTests
{
    private static readonly Func<Medium, bool> IsReadyForDiscovery =
        DiscoveryEligibility.ReadyMedia.Compile();

    [Fact]
    public void Ready_active_media_from_active_shop_is_eligible()
    {
        var media = CreateMedia(MediaStatus.Ready, mediaIsActive: true, shopIsActive: true);

        Assert.True(IsReadyForDiscovery(media));
    }

    [Theory]
    [InlineData(MediaStatus.Processing, true, true)]
    [InlineData(MediaStatus.Failed, true, true)]
    [InlineData(MediaStatus.Ready, false, true)]
    [InlineData(MediaStatus.Ready, true, false)]
    public void Unavailable_media_is_not_eligible(
        MediaStatus status,
        bool mediaIsActive,
        bool shopIsActive)
    {
        var media = CreateMedia(status, mediaIsActive, shopIsActive);

        Assert.False(IsReadyForDiscovery(media));
    }

    private static Medium CreateMedia(
        MediaStatus status,
        bool mediaIsActive,
        bool shopIsActive)
    {
        return new Medium
        {
            Status = status,
            IsActive = mediaIsActive,
            Shop = new Shop
            {
                IsActive = shopIsActive
            }
        };
    }
}
