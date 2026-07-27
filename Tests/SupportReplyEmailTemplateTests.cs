using CraftoraApi.Services;
using Xunit;

namespace CraftoraApi.Tests;

public sealed class SupportReplyEmailTemplateTests
{
    [Fact]
    public void Build_encodes_user_controlled_content()
    {
        var html = SupportReplyEmailTemplate.Build(
            "<script>alert('name')</script>",
            "<b>Ticket</b>",
            "<img src=x onerror=alert(1)>");

        Assert.DoesNotContain("<script>alert", html);
        Assert.DoesNotContain("<b>Ticket</b>", html);
        Assert.DoesNotContain("<img src=x", html);
        Assert.Contains("&lt;script&gt;", html);
        Assert.Contains("&lt;b&gt;Ticket&lt;/b&gt;", html);
        Assert.Contains("&lt;img src=x", html);
    }

    [Fact]
    public void Build_preserves_reply_line_breaks()
    {
        var html = SupportReplyEmailTemplate.Build(
            "Mahmut",
            "Odeme sorusu",
            "Birinci satir\nIkinci satir");

        Assert.Contains("Birinci satir<br>Ikinci satir", html);
        Assert.Contains("craftora-email-logo.png", html);
    }
}
