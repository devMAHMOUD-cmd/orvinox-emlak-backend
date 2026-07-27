using CraftoraApi.DTOs.Admin;
using CraftoraApi.Services;
using CraftoraApi.Validators;
using Xunit;

namespace CraftoraApi.Tests;

public sealed class AdminEmailCampaignTests
{
    [Fact]
    public void Template_encodes_untrusted_content_and_preserves_line_breaks()
    {
        var html = AdminEmailTemplate.Build(
            "<Admin>",
            "Birinci satir\n<script>alert(1)</script>");

        Assert.Contains("&lt;Admin&gt;", html);
        Assert.Contains("Birinci satir<br>&lt;script&gt;alert(1)&lt;/script&gt;", html);
        Assert.DoesNotContain("<script>alert(1)</script>", html);
    }

    [Theory]
    [InlineData("all")]
    [InlineData("users")]
    [InlineData("sellers")]
    public void Send_validator_accepts_supported_audiences(string audience)
    {
        var validator = new AdminEmailCampaignSendRequestValidator();
        var result = validator.Validate(new AdminEmailCampaignSendRequestDto(
            audience,
            "Craftora duyurusu",
            "Yeni bir platform duyurusu.",
            "release-2026-07-27"));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Send_validator_rejects_invalid_audience_and_idempotency_key()
    {
        var validator = new AdminEmailCampaignSendRequestValidator();
        var result = validator.Validate(new AdminEmailCampaignSendRequestDto(
            "admins",
            "Craftora duyurusu",
            "Yeni bir platform duyurusu.",
            "bosluklu anahtar"));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.PropertyName == "Audience");
        Assert.Contains(result.Errors, error => error.PropertyName == "IdempotencyKey");
    }
}
