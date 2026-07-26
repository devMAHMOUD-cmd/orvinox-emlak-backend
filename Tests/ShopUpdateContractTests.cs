using System.Text.Json;
using CraftoraApi.DTOs.Shop;
using Xunit;

namespace CraftoraApi.Tests;

public sealed class ShopUpdateContractTests
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    [Fact]
    public void Existing_update_payload_keeps_asset_removal_disabled()
    {
        var dto = JsonSerializer.Deserialize<UpdateShopDto>(
            """
            {
              "shopName": "Craftora Test",
              "logoUrl": "users/00000000-0000-0000-0000-000000000001/public/logo.png",
              "bannerUrl": null
            }
            """,
            JsonOptions);

        Assert.NotNull(dto);
        Assert.False(dto.RemoveLogo);
        Assert.False(dto.RemoveBanner);
    }

    [Fact]
    public void Update_payload_can_explicitly_remove_shop_assets()
    {
        var dto = JsonSerializer.Deserialize<UpdateShopDto>(
            """
            {
              "logoUrl": null,
              "bannerUrl": null,
              "removeLogo": true,
              "removeBanner": true
            }
            """,
            JsonOptions);

        Assert.NotNull(dto);
        Assert.True(dto.RemoveLogo);
        Assert.True(dto.RemoveBanner);
    }
}
