using CraftoraApi.Middleware;
using System.Text.RegularExpressions;

namespace CraftoraApi.Infrastructure.Security;

/// <summary>
/// Keeps user-visible text fields as plain text so they are safe if a client renders them as HTML.
/// </summary>
public static class PlainTextInputValidator
{
    private static readonly Regex DangerousContentPattern = new(
        @"<\s*(?:script|iframe|embed|object)\b|javascript\s*:|\bon(?:error|load|click|dblclick|mouseover|mouseout|mouseenter|mouseleave|mousedown|mouseup|mousemove|focus|blur|submit|change|input|keydown|keyup|keypress|touchstart|touchend|touchmove|pointerdown|pointerup|pointermove|pointerenter|pointerleave)\s*=",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex ProhibitedControlCharacterPattern = new(
        @"[\u0000-\u0008\u000B\u000C\u000E-\u001F\u007F]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static bool ContainsProhibitedContent(string value)
    {
        return DangerousContentPattern.IsMatch(value)
            || ProhibitedControlCharacterPattern.IsMatch(value);
    }

    public static string Require(string? value, string fieldName, int maxLength)
    {
        var normalized = Normalize(value, fieldName, maxLength);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new BadRequestException($"{fieldName} zorunludur.");
        }

        return normalized;
    }

    public static string? Optional(string? value, string fieldName, int maxLength)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : Normalize(value, fieldName, maxLength);
    }

    private static string Normalize(string? value, string fieldName, int maxLength)
    {
        if (maxLength <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxLength));
        }

        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length > maxLength)
        {
            throw new BadRequestException($"{fieldName} en fazla {maxLength} karakter olabilir.");
        }

        if (ContainsProhibitedContent(normalized))
        {
            throw new BadRequestException(
                $"{fieldName} guvensiz HTML, URL veya kontrol karakteri iceremez.");
        }

        return normalized;
    }
}
