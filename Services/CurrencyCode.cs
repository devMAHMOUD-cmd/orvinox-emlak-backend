namespace CraftoraApi.Services;

public static class CurrencyCode
{
    public static string Normalize(string? currency)
    {
        var normalized = string.IsNullOrWhiteSpace(currency)
            ? "USD"
            : currency.Trim().ToUpperInvariant();

        return normalized == "TL" ? "TRY" : normalized;
    }
}
