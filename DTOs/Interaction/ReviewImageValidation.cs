using System.ComponentModel.DataAnnotations;

namespace CraftoraApi.DTOs.Interaction;

internal static class ReviewImageValidation
{
    private const int MaxImageCount = 5;
    private const int MaxUrlLength = 2048;

    public static IEnumerable<ValidationResult> Validate(IReadOnlyCollection<string>? images)
    {
        if (images is null)
        {
            yield break;
        }

        if (images.Count > MaxImageCount)
        {
            yield return new ValidationResult(
                $"En fazla {MaxImageCount} yorum gorseli eklenebilir.",
                new[] { "Images" });
            yield break;
        }

        foreach (var image in images)
        {
            if (string.IsNullOrWhiteSpace(image) || image.Length > MaxUrlLength ||
                !Uri.TryCreate(image, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                yield return new ValidationResult(
                    "Yorum gorselleri en fazla 2048 karakterlik gecerli bir HTTP/HTTPS URL olmalidir.",
                    new[] { "Images" });
                yield break;
            }
        }
    }
}
