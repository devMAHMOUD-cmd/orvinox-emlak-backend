using System.ComponentModel.DataAnnotations;
using CraftoraApi.DTOs.Search;
using Xunit;

namespace CraftoraApi.Tests;

public sealed class SearchRequestContractTests
{
    [Fact]
    public void Product_search_rejects_negative_price()
    {
        var request = new SearchRequestDto(
            Query: "test",
            CategoryId: null,
            MinPrice: -1,
            MaxPrice: null);

        Assert.Contains(
            Validate(request),
            result => result.MemberNames.Contains(nameof(SearchRequestDto.MinPrice)));
    }

    [Fact]
    public void Global_search_rejects_oversized_query()
    {
        var request = new GlobalSearchRequestDto(
            Query: new string('a', 201));

        Assert.Contains(
            Validate(request),
            result => result.MemberNames.Contains(nameof(GlobalSearchRequestDto.Query)));
    }

    [Fact]
    public void Global_search_rejects_oversized_page()
    {
        var request = new GlobalSearchRequestDto(
            Query: "test",
            PageSize: 51);

        Assert.Contains(
            Validate(request),
            result => result.MemberNames.Contains(nameof(GlobalSearchRequestDto.PageSize)));
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
