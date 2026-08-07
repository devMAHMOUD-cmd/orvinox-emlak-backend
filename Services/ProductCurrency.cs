using System.Text.Json;
using CraftoraApi.Middleware;

namespace CraftoraApi.Services;

public static class ProductCurrency
{
    public const string Default = "USD";

    public static string Resolve(string? requestedCurrency, string? metadata, string? storedCurrency = null)
    {
        if (!string.IsNullOrWhiteSpace(requestedCurrency))
        {
            return NormalizeOrThrow(requestedCurrency);
        }

        var metadataCurrency = ReadMetadataCurrency(metadata);
        if (metadataCurrency is not null)
        {
            return metadataCurrency;
        }

        return TryNormalize(storedCurrency) ?? Default;
    }

    private static string NormalizeOrThrow(string currency)
    {
        return TryNormalize(currency)
            ?? throw new BadRequestException("Para birimi yalnizca USD veya TRY olabilir.");
    }

    private static string? TryNormalize(string? currency)
    {
        if (string.IsNullOrWhiteSpace(currency)) return null;

        return currency.Trim().ToUpperInvariant() switch
        {
            "USD" or "$" => "USD",
            "TRY" or "TL" or "₺" => "TRY",
            _ => null
        };
    }

    private static string? ReadMetadataCurrency(string? metadata)
    {
        if (string.IsNullOrWhiteSpace(metadata)) return null;

        try
        {
            using var document = JsonDocument.Parse(metadata);
            if (!document.RootElement.TryGetProperty("currency", out var currencyElement) ||
                currencyElement.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            return TryNormalize(currencyElement.GetString());
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
