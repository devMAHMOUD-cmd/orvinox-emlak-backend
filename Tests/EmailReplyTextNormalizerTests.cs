using CraftoraApi.Services;
using Xunit;

namespace CraftoraApi.Tests;

public sealed class EmailReplyTextNormalizerTests
{
    [Fact]
    public void Turkish_gmail_reply_keeps_only_new_message()
    {
        const string value =
            "Gmail yanit zinciri basariyla calisiyor.\n\n" +
            "Craftora <noreply@craftoramedya.com>, 27 Tem 2026 Pzt, 22:39 tarihinde \u015Funu yazd\u0131:\n\n" +
            "> Eski destek mesaji";

        var normalized = EmailReplyTextNormalizer.TrimQuotedHistory(value);

        Assert.Equal("Gmail yanit zinciri basariyla calisiyor.", normalized);
    }

    [Fact]
    public void English_gmail_reply_keeps_only_new_message()
    {
        const string value =
            """
            The issue is resolved.

            On Mon, Jul 27, 2026 at 10:39 PM Craftora <noreply@craftoramedya.com> wrote:
            > Previous support message
            """;

        var normalized = EmailReplyTextNormalizer.TrimQuotedHistory(value);

        Assert.Equal("The issue is resolved.", normalized);
    }

    [Fact]
    public void Original_message_separator_is_removed()
    {
        const string value =
            """
            Tesekkur ederim.

            -----Original Message-----
            From: Craftora
            """;

        var normalized = EmailReplyTextNormalizer.TrimQuotedHistory(value);

        Assert.Equal("Tesekkur ederim.", normalized);
    }

    [Fact]
    public void Plain_message_is_not_changed()
    {
        const string value = "Yeni bir destek talebi olusturmak istiyorum.";

        var normalized = EmailReplyTextNormalizer.TrimQuotedHistory(value);

        Assert.Equal(value, normalized);
    }
}
