namespace CraftoraApi.Infrastructure.Security;

public static class SupportAddressResolver
{
    public static SupportAddressMatch? Resolve(
        IEnumerable<string> recipients,
        string supportAddress)
    {
        var normalizedSupportAddress = NormalizeEmail(supportAddress);
        var atIndex = normalizedSupportAddress.LastIndexOf('@');
        if (atIndex <= 0 || atIndex == normalizedSupportAddress.Length - 1)
        {
            return null;
        }

        var localPart = normalizedSupportAddress[..atIndex];
        var domain = normalizedSupportAddress[(atIndex + 1)..];
        SupportAddressMatch? exactMatch = null;

        foreach (var value in recipients)
        {
            var recipient = NormalizeEmail(value);
            if (recipient == normalizedSupportAddress)
            {
                exactMatch = new SupportAddressMatch(recipient, null);
                continue;
            }

            var prefix = $"{localPart}+";
            if (!recipient.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
                !recipient.EndsWith($"@{domain}", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var ticketText = recipient[
                prefix.Length..^(domain.Length + 1)];
            if (Guid.TryParse(ticketText, out var ticketId))
            {
                return new SupportAddressMatch(recipient, ticketId);
            }
        }

        return exactMatch;
    }

    private static string NormalizeEmail(string value)
    {
        var trimmed = value.Trim();
        var start = trimmed.LastIndexOf('<');
        var end = trimmed.LastIndexOf('>');
        var address = start >= 0 && end > start
            ? trimmed[(start + 1)..end]
            : trimmed;
        return address.Trim().ToLowerInvariant();
    }
}

public sealed record SupportAddressMatch(
    string Address,
    Guid? TicketId);
