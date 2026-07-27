using System.Text.Json;

namespace CraftoraApi.Infrastructure.Security;

public static class JsonInputSafetyValidator
{
    public static bool ContainsProhibitedContent(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String =>
                PlainTextInputValidator.ContainsProhibitedContent(
                    element.GetString() ?? string.Empty),
            JsonValueKind.Object =>
                element.EnumerateObject().Any(property =>
                    PlainTextInputValidator.ContainsProhibitedContent(property.Name)
                    || ContainsProhibitedContent(property.Value)),
            JsonValueKind.Array =>
                element.EnumerateArray().Any(ContainsProhibitedContent),
            _ => false
        };
    }
}
