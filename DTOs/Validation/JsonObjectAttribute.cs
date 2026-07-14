using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace CraftoraApi.DTOs.Validation;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class JsonObjectAttribute : ValidationAttribute
{
    public JsonObjectAttribute()
    {
        ErrorMessage = "Gecerli bir JSON nesnesi giriniz.";
    }

    public override bool IsValid(object? value)
    {
        if (value is null || string.IsNullOrWhiteSpace(value.ToString()))
        {
            return true;
        }

        if (value is not string json)
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
