using CraftoraApi.Middleware;
using CraftoraApi.Services;
using Xunit;

namespace CraftoraApi.Tests;

public sealed class ProductCurrencyTests
{
    [Theory]
    [InlineData("TRY", "TRY")]
    [InlineData("TL", "TRY")]
    [InlineData("USD", "USD")]
    public void Requested_currency_is_normalized(string input, string expected)
    {
        Assert.Equal(expected, ProductCurrency.Resolve(input, null));
    }

    [Fact]
    public void Legacy_metadata_currency_wins_over_stale_stored_currency()
    {
        Assert.Equal("TRY", ProductCurrency.Resolve(null, "{\"currency\":\"TRY\"}", "USD"));
    }

    [Fact]
    public void Missing_currency_defaults_to_usd()
    {
        Assert.Equal("USD", ProductCurrency.Resolve(null, null));
    }

    [Fact]
    public void Unsupported_requested_currency_is_rejected()
    {
        Assert.Throws<BadRequestException>(() => ProductCurrency.Resolve("GBP", null));
    }
}
