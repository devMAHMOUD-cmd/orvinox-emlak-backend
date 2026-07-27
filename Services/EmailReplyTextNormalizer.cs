using System.Text.RegularExpressions;

namespace CraftoraApi.Services;

public static partial class EmailReplyTextNormalizer
{
    public static string TrimQuotedHistory(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var cutIndex = value.Length;
        foreach (var regex in QuotedHistoryMarkers())
        {
            var match = regex.Match(value);
            if (match.Success && match.Index < cutIndex)
            {
                cutIndex = match.Index;
            }
        }

        return value[..cutIndex].Trim();
    }

    private static IEnumerable<Regex> QuotedHistoryMarkers()
    {
        yield return EnglishGmailReplyHeaderRegex();
        yield return TurkishGmailReplyHeaderRegex();
        yield return OriginalMessageSeparatorRegex();
        yield return QuotedLineRegex();
    }

    [GeneratedRegex(
        @"(?im)^\s*On\s+[^\r\n]+\s+wrote:\s*$",
        RegexOptions.CultureInvariant)]
    private static partial Regex EnglishGmailReplyHeaderRegex();

    [GeneratedRegex(
        @"(?im)^\s*[^\r\n]*<[^>\r\n]+>,[^\r\n]*tarihinde\s+\u015Funu\s+yazd\u0131:\s*$",
        RegexOptions.CultureInvariant)]
    private static partial Regex TurkishGmailReplyHeaderRegex();

    [GeneratedRegex(
        @"(?im)^\s*-{2,}\s*Original Message\s*-{2,}\s*$",
        RegexOptions.CultureInvariant)]
    private static partial Regex OriginalMessageSeparatorRegex();

    [GeneratedRegex(
        @"(?m)^\s*>",
        RegexOptions.CultureInvariant)]
    private static partial Regex QuotedLineRegex();
}
