using System.Text.Json;
using System.Text.Json.Serialization;

namespace CraftoraApi.Infrastructure.Security;

public sealed class SafeStringJsonConverter : JsonConverter<string>
{
    public override bool HandleNull => true;

    public override string? Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        var value = reader.GetString()
            ?? throw new JsonException("Metin degeri okunamadi.");

        if (PlainTextInputValidator.ContainsProhibitedContent(value))
        {
            throw new JsonException(
                "Metin alanlari guvensiz HTML, URL veya kontrol karakteri iceremez.");
        }

        return value;
    }

    public override void Write(
        Utf8JsonWriter writer,
        string value,
        JsonSerializerOptions options)
    {
        writer.WriteStringValue(value);
    }
}
