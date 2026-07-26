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
                !IsValidImageReference(image))
            {
                yield return new ValidationResult(
                    "Yorum gorselleri public-assets yukleme anahtari veya gecerli bir HTTP/HTTPS URL olmalidir.",
                    new[] { "Images" });
                yield break;
            }
        }
    }

    private static bool IsValidImageReference(string image)
    {
        if (Uri.TryCreate(image, UriKind.Absolute, out var uri))
        {
            return uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
                uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
        }

        var objectKey = image.TrimStart('/');
        if (objectKey.StartsWith("uploads/", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var segments = objectKey.Split('/', 4, StringSplitOptions.RemoveEmptyEntries);
        return segments.Length == 4 &&
            segments[0].Equals("users", StringComparison.OrdinalIgnoreCase) &&
            Guid.TryParse(segments[1], out _) &&
            segments[2].Equals("public", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(segments[3]);
    }
}
