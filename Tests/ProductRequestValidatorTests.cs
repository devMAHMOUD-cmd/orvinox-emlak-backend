using System.Text.Json;
using System.Text.Json.Serialization;
using CraftoraApi.DTOs.Product;
using CraftoraApi.Models.Enums;
using CraftoraApi.Validators;
using Xunit;

namespace CraftoraApi.Tests;

public sealed class ProductRequestValidatorTests
{
    private readonly CreateProductDtoValidator _createValidator = new();
    private readonly UpdateProductDtoValidator _updateValidator = new();

    [Fact]
    public void Create_rejects_negative_price()
    {
        var result = _createValidator.Validate(Create(price: -0.01m));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(CreateProductDto.Price));
    }

    [Fact]
    public void Create_rejects_price_above_database_limit()
    {
        var result = _createValidator.Validate(Create(price: 100_000_000m));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(CreateProductDto.Price));
    }

    [Fact]
    public void Create_accepts_database_maximum_price()
    {
        var result = _createValidator.Validate(Create(price: 99_999_999.99m));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Create_rejects_long_or_whitespace_title()
    {
        var longTitleResult = _createValidator.Validate(Create(title: new string('x', 300)));
        var whitespaceResult = _createValidator.Validate(Create(title: "   "));

        Assert.Contains(longTitleResult.Errors, error => error.PropertyName == nameof(CreateProductDto.Title));
        Assert.Contains(whitespaceResult.Errors, error => error.PropertyName == nameof(CreateProductDto.Title));
    }

    [Fact]
    public void Update_uses_the_same_price_limits()
    {
        var result = _updateValidator.Validate(Update(price: 100_000_000m));

        Assert.Contains(result.Errors, error => error.PropertyName == nameof(UpdateProductDto.Price));
    }

    [Fact]
    public void Product_endpoint_rejects_course_type()
    {
        var result = _createValidator.Validate(Create(type: ProductType.Course));

        Assert.Contains(result.Errors, error => error.PropertyName == nameof(CreateProductDto.Type));
    }

    [Fact]
    public void Product_endpoint_accepts_omitted_or_digital_file_type()
    {
        var omittedTypeResult = _createValidator.Validate(Create());
        var digitalTypeResult = _createValidator.Validate(Create(type: ProductType.DigitalFile));

        Assert.True(omittedTypeResult.IsValid);
        Assert.True(digitalTypeResult.IsValid);
    }

    [Fact]
    public void Unknown_product_type_fails_json_binding()
    {
        const string payload = """
            {
              "categoryId": "category",
              "title": "Valid title",
              "description": "Valid description",
              "price": 10,
              "originalPrice": null,
              "status": "draft",
              "tags": [],
              "coverImageUrl": null,
              "previewVideoUrl": null,
              "fileUrl": null,
              "metadata": null,
              "type": "physical_product"
            }
            """;

        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<CreateProductDto>(payload, JsonOptions()));
    }

    [Fact]
    public void Digital_file_json_name_matches_mobile_contract()
    {
        const string payload = """
            {
              "categoryId": "category",
              "title": "Valid title",
              "description": "Valid description",
              "price": 10,
              "originalPrice": null,
              "status": "draft",
              "tags": [],
              "coverImageUrl": null,
              "previewVideoUrl": null,
              "fileUrl": null,
              "metadata": null,
              "type": "digital_file"
            }
            """;

        var request = JsonSerializer.Deserialize<CreateProductDto>(payload, JsonOptions());

        Assert.NotNull(request);
        Assert.Equal(ProductType.DigitalFile, request.Type);
        Assert.True(_createValidator.Validate(request).IsValid);
    }

    [Fact]
    public void Product_images_are_limited_to_eight_valid_object_keys()
    {
        var tooMany = Create() with
        {
            ImageObjectKeys = Enumerable.Range(0, 9)
                .Select(index => $"users/test/public/image-{index}.jpg")
                .ToList()
        };
        var invalidKey = Create() with
        {
            ImageObjectKeys = ["", "users/test/public/image.jpg"]
        };
        var valid = Create() with
        {
            ImageObjectKeys = Enumerable.Range(0, 8)
                .Select(index => $"users/test/public/image-{index}.jpg")
                .ToList()
        };

        Assert.Contains(
            _createValidator.Validate(tooMany).Errors,
            error => error.PropertyName == nameof(CreateProductDto.ImageObjectKeys));
        Assert.Contains(
            _createValidator.Validate(invalidKey).Errors,
            error => error.PropertyName == nameof(CreateProductDto.ImageObjectKeys));
        Assert.True(_createValidator.Validate(valid).IsValid);
    }

    [Fact]
    public void Product_rejects_invalid_json_metadata_and_oversized_tags()
    {
        var invalidMetadata = Create() with { Metadata = "{invalid-json" };
        var oversizedTags = Create() with
        {
            Tags = Enumerable.Range(0, 21)
                .Select(index => $"tag-{index}")
                .ToList()
        };

        Assert.Contains(
            _createValidator.Validate(invalidMetadata).Errors,
            error => error.PropertyName == nameof(CreateProductDto.Metadata));
        Assert.Contains(
            _createValidator.Validate(oversizedTags).Errors,
            error => error.PropertyName == nameof(CreateProductDto.Tags));
    }

    private static CreateProductDto Create(
        decimal price = 10m,
        string title = "Valid product",
        ProductType? type = null)
    {
        return new CreateProductDto(
            CategoryId: "category",
            Title: title,
            Description: "Valid description",
            Price: price,
            OriginalPrice: null,
            Status: ProductStatus.Draft,
            Tags: new List<string>(),
            CoverImageUrl: null,
            PreviewVideoUrl: null,
            FileUrl: null,
            Metadata: null,
            Type: type);
    }

    private static UpdateProductDto Update(decimal price)
    {
        return new UpdateProductDto(
            CategoryId: "category",
            Title: "Valid product",
            Description: "Valid description",
            Price: price,
            OriginalPrice: null,
            Status: ProductStatus.Draft,
            Tags: new List<string>(),
            CoverImageUrl: null,
            PreviewVideoUrl: null,
            FileUrl: null,
            Metadata: null);
    }

    private static JsonSerializerOptions JsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}
