using System.Linq.Expressions;
using System.Text.Json;
using CraftoraApi.DTOs.Product;
using CraftoraApi.Models.Enums;
using FluentValidation;

namespace CraftoraApi.Validators;

public sealed class CreateProductDtoValidator : AbstractValidator<CreateProductDto>
{
    public CreateProductDtoValidator()
    {
        ProductRequestValidationRules.Apply(
            this,
            request => request.CategoryId,
            request => request.Title,
            request => request.Description,
            request => request.Price,
            request => request.OriginalPrice,
            request => request.Status,
            request => request.Tags,
            request => request.Type,
            request => request.ImageObjectKeys,
            request => request.Metadata);
    }
}

public sealed class UpdateProductDtoValidator : AbstractValidator<UpdateProductDto>
{
    public UpdateProductDtoValidator()
    {
        ProductRequestValidationRules.Apply(
            this,
            request => request.CategoryId,
            request => request.Title,
            request => request.Description,
            request => request.Price,
            request => request.OriginalPrice,
            request => request.Status,
            request => request.Tags,
            request => request.Type,
            request => request.ImageObjectKeys,
            request => request.Metadata);
    }
}

internal static class ProductRequestValidationRules
{
    internal const decimal MaximumPrice = 99_999_999.99m;

    internal static void Apply<T>(
        AbstractValidator<T> validator,
        Expression<Func<T, string>> categoryId,
        Expression<Func<T, string>> title,
        Expression<Func<T, string>> description,
        Expression<Func<T, decimal>> price,
        Expression<Func<T, decimal?>> originalPrice,
        Expression<Func<T, ProductStatus>> status,
        Expression<Func<T, List<string>>> tags,
        Expression<Func<T, ProductType?>> type,
        Expression<Func<T, IReadOnlyList<string>?>> imageObjectKeys,
        Expression<Func<T, string?>> metadata)
    {
        validator.RuleFor(categoryId)
            .NotEmpty()
            .WithMessage("Kategori zorunludur.");

        validator.RuleFor(title)
            .Must(value => !string.IsNullOrWhiteSpace(value) &&
                           value.Trim().Length is >= 3 and <= 255)
            .WithMessage("Urun basligi 3 ile 255 karakter arasinda olmalidir.");

        validator.RuleFor(description)
            .Must(value => !string.IsNullOrWhiteSpace(value))
            .WithMessage("Urun aciklamasi zorunludur.");

        validator.RuleFor(price)
            .InclusiveBetween(0m, MaximumPrice)
            .WithMessage("Fiyat 0 ile 99999999.99 arasinda olmalidir.");

        validator.RuleFor(originalPrice)
            .Must(value => !value.HasValue ||
                           value.Value is >= 0m and <= MaximumPrice)
            .WithMessage("Orijinal fiyat 0 ile 99999999.99 arasinda olmalidir.");

        validator.RuleFor(status)
            .IsInEnum()
            .WithMessage("Gecersiz urun durumu.");

        validator.RuleFor(tags)
            .NotNull()
            .WithMessage("Etiketler zorunludur.")
            .Must(value => value is not null &&
                           value.Count <= 20 &&
                           value.All(tag => !string.IsNullOrWhiteSpace(tag) && tag.Trim().Length <= 50))
            .WithMessage("En fazla 20 adet ve 50 karakterlik etiket kullanilabilir.");

        validator.RuleFor(type)
            .Must(value => !value.HasValue || value == ProductType.DigitalFile)
            .WithMessage("Bu endpoint yalnizca digital_file urunlerini kabul eder. Kurslar icin kurs endpointini kullanin.");

        validator.RuleFor(imageObjectKeys)
            .Must(value => value is null || value.Count <= 8)
            .WithMessage("Bir urune en fazla 8 gorsel eklenebilir.")
            .Must(value => value is null || value.All(
                key => !string.IsNullOrWhiteSpace(key) && key.Trim().Length <= 1024))
            .WithMessage("Gorsel object key degeri gecersiz.");

        validator.RuleFor(metadata)
            .Must(value => string.IsNullOrWhiteSpace(value) ||
                           (value.Length <= 20_000 && IsValidJson(value)))
            .WithMessage("Metadata gecerli JSON olmali ve 20000 karakteri asmamalidir.");
    }

    private static bool IsValidJson(string value)
    {
        try
        {
            using var _ = JsonDocument.Parse(value);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
