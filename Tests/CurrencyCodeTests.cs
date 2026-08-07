using CraftoraApi.Services;
using Xunit;

namespace CraftoraApi.Tests;

public sealed class CurrencyCodeTests
{
    [Theory]
    [InlineData("try", "TRY")]
    [InlineData("TL", "TRY")]
    [InlineData(" usd ", "USD")]
    [InlineData("EUR", "EUR")]
    public void Normalize_returns_canonical_currency_code(string input, string expected)
    {
        Assert.Equal(expected, CurrencyCode.Normalize(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Normalize_defaults_missing_currency_to_usd(string? input)
    {
        Assert.Equal("USD", CurrencyCode.Normalize(input));
    }
}
