using System.Text.Json;
using CraftoraApi.Infrastructure.Security;
using Xunit;

namespace CraftoraApi.Tests;

public sealed class SafeStringJsonConverterTests
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    [Theory]
    [InlineData("""{"value":"<script>alert(1)</script>"}""")]
    [InlineData("""{"value":"<img src=x onerror=alert(1)>"}""")]
    [InlineData("""{"value":"javascript:alert(1)"}""")]
    [InlineData("""{"value":"null\u0000byte"}""")]
    public void Unsafe_string_values_fail_json_binding(string json)
    {
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<TestPayload>(json, Options));
    }

    [Theory]
    [InlineData("Craftora 🚀")]
    [InlineData("Robert'); DROP TABLE users; --")]
    [InlineData("Birinci satır\nİkinci satır\tdevam")]
    public void Safe_unicode_and_plain_text_values_are_preserved(string value)
    {
        var json = JsonSerializer.Serialize(new TestPayload(value), Options);
        var payload = JsonSerializer.Deserialize<TestPayload>(json, Options);

        Assert.NotNull(payload);
        Assert.Equal(value, payload.Value);
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
        options.Converters.Add(new SafeStringJsonConverter());
        return options;
    }

    private sealed record TestPayload(string Value);
}
