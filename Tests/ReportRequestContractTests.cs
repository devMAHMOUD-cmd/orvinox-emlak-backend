using System.ComponentModel.DataAnnotations;
using CraftoraApi.DTOs.Report;
using Xunit;

namespace CraftoraApi.Tests;

public sealed class ReportRequestContractTests
{
    [Fact]
    public void Report_rejects_missing_target_type()
    {
        var request = new CreateReportDto(
            TargetType: string.Empty,
            TargetId: Guid.NewGuid(),
            Reason: "spam",
            Description: null);

        Assert.Contains(
            Validate(request),
            result => result.MemberNames.Contains(nameof(CreateReportDto.TargetType)));
    }

    [Fact]
    public void Report_rejects_oversized_description()
    {
        var request = new CreateReportDto(
            TargetType: "product",
            TargetId: Guid.NewGuid(),
            Reason: "spam",
            Description: new string('a', 5001));

        Assert.Contains(
            Validate(request),
            result => result.MemberNames.Contains(nameof(CreateReportDto.Description)));
    }

    private static List<ValidationResult> Validate(object instance)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(
            instance,
            new ValidationContext(instance),
            results,
            validateAllProperties: true);
        return results;
    }
}
