using System.ComponentModel.DataAnnotations;
using CraftoraApi.DTOs.Course;
using Xunit;

namespace CraftoraApi.Tests;

public sealed class SellerCourseRequestValidationTests
{
    [Fact]
    public void Course_rejects_price_above_database_limit()
    {
        var request = new CreateSellerCourseDto
        {
            CategoryId = "category",
            Title = "Valid course",
            Description = "Valid description",
            Price = 100_000_000m,
            Tags = [],
            Level = "Beginner"
        };
        var results = new List<ValidationResult>();

        var isValid = Validator.TryValidateObject(
            request,
            new ValidationContext(request),
            results,
            validateAllProperties: true);

        Assert.False(isValid);
        Assert.Contains(results, result => result.MemberNames.Contains(nameof(request.Price)));
    }

    [Fact]
    public void Course_accepts_database_maximum_price()
    {
        var request = new CreateSellerCourseDto
        {
            CategoryId = "category",
            Title = "Valid course",
            Description = "Valid description",
            Price = 99_999_999.99m,
            Tags = [],
            Level = "Beginner"
        };
        var results = new List<ValidationResult>();

        var isValid = Validator.TryValidateObject(
            request,
            new ValidationContext(request),
            results,
            validateAllProperties: true);

        Assert.True(isValid);
    }

    [Fact]
    public void Course_rejects_oversized_section_collection()
    {
        var request = new CreateSellerCourseDto
        {
            CategoryId = "category",
            Title = "Valid course",
            Description = "Valid description",
            Price = 10m,
            Tags = [],
            Level = "Beginner",
            Sections = Enumerable.Range(0, 101)
                .Select(index => new CreateSellerCourseSectionDto
                {
                    Title = $"Section {index}"
                })
                .ToList()
        };
        var results = new List<ValidationResult>();

        var isValid = Validator.TryValidateObject(
            request,
            new ValidationContext(request),
            results,
            validateAllProperties: true);

        Assert.False(isValid);
        Assert.Contains(
            results,
            result => result.MemberNames.Contains(nameof(request.Sections)));
    }

    [Fact]
    public void Course_section_rejects_negative_sort_order()
    {
        var request = new CreateSellerCourseSectionDto
        {
            Title = "Valid section",
            SortOrder = -1
        };
        var results = new List<ValidationResult>();

        var isValid = Validator.TryValidateObject(
            request,
            new ValidationContext(request),
            results,
            validateAllProperties: true);

        Assert.False(isValid);
        Assert.Contains(
            results,
            result => result.MemberNames.Contains(nameof(request.SortOrder)));
    }
}
