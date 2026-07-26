using System.ComponentModel.DataAnnotations;
using CraftoraApi.DTOs.Interaction;
using Xunit;

namespace CraftoraApi.Tests;

public sealed class ReviewRequestValidationTests
{
    [Fact]
    public void Review_accepts_current_user_scoped_public_image_key()
    {
        var dto = new CreateReviewDto(
            ProductId: Guid.NewGuid(),
            Rating: 5,
            Comment: "Valid review",
            Images: ["users/11111111-1111-1111-1111-111111111111/public/review.png"]);

        Assert.Empty(Validate(dto));
    }

    [Fact]
    public void Review_rejects_private_or_malformed_image_key()
    {
        var dto = new CreateReviewDto(
            ProductId: Guid.NewGuid(),
            Rating: 5,
            Comment: "Valid review",
            Images: ["users/11111111-1111-1111-1111-111111111111/private/review.png"]);

        Assert.Contains(
            Validate(dto),
            result => result.MemberNames.Contains("Images"));
    }

    private static List<ValidationResult> Validate(object value)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(
            value,
            new ValidationContext(value),
            results,
            validateAllProperties: true);
        return results;
    }
}
