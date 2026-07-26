using System.ComponentModel.DataAnnotations;
using CraftoraApi.Models.Enums;

namespace CraftoraApi.DTOs.Course;

public sealed record SellerCourseListItemDto(
    Guid CourseId,
    Guid ProductId,
    Guid ShopId,
    Guid CategoryId,
    string Title,
    string Description,
    decimal Price,
    decimal? OriginalPrice,
    string? CoverImageUrl,
    string? CoverImagePublicUrl,
    string? PreviewVideoUrl,
    string? PreviewVideoPublicUrl,
    ProductStatus Status,
    List<string> Tags,
    string Level,
    int TotalDurationInMinutes,
    bool IsCertificateIncluded,
    int SectionCount,
    int LessonCount,
    int EnrolledCount,
    int SalesCount,
    DateTime? CreatedAt,
    DateTime? UpdatedAt);

public sealed record SellerCourseListResponseDto(
    IReadOnlyList<SellerCourseListItemDto> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);

public sealed record SellerCourseDetailDto(
    Guid CourseId,
    Guid ProductId,
    Guid ShopId,
    Guid CategoryId,
    string Title,
    string Description,
    decimal Price,
    decimal? OriginalPrice,
    string? CoverImageUrl,
    string? CoverImagePublicUrl,
    string? PreviewVideoUrl,
    string? PreviewVideoPublicUrl,
    ProductStatus Status,
    List<string> Tags,
    string Level,
    int TotalDurationInMinutes,
    bool IsCertificateIncluded,
    int EnrolledCount,
    int SalesCount,
    DateTime? CreatedAt,
    DateTime? UpdatedAt,
    IReadOnlyList<SellerCourseSectionDto> Sections);

public sealed record SellerCourseSectionDto(
    Guid Id,
    Guid CourseId,
    string Title,
    int SortOrder,
    bool IsActive,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    IReadOnlyList<SellerCourseLessonDto> Lessons,
    IReadOnlyList<SellerCourseQuizDto> Quizzes);

public sealed record SellerCourseLessonDto(
    Guid Id,
    Guid CourseSectionId,
    string Title,
    string? VideoUrl,
    string? VideoFileName,
    int DurationInSeconds,
    int SortOrder,
    bool IsFreePreview,
    bool IsActive,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    IReadOnlyList<SellerCourseResourceDto> Resources);

public sealed record SellerCourseResourceDto(
    Guid Id,
    Guid CourseLessonId,
    string Title,
    string FileUrl,
    string? FileName,
    string ResourceType,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

public sealed record SellerCourseQuizDto(
    Guid Id,
    Guid CourseSectionId,
    string Title,
    int PassingScore,
    bool IsActive,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

public sealed record CreateSellerCourseDto
{
    [Required]
    public string CategoryId { get; init; } = null!;

    [Required]
    [StringLength(255, MinimumLength = 3)]
    public string Title { get; init; } = null!;

    [Required]
    [StringLength(20000)]
    public string Description { get; init; } = null!;

    [Range(0d, 99999999.99d)]
    public decimal Price { get; init; }

    [Range(0d, 99999999.99d)]
    public decimal? OriginalPrice { get; init; }

    public ProductStatus Status { get; init; } = ProductStatus.Draft;

    public List<string> Tags { get; init; } = new();

    public string? CoverImageUrl { get; init; }

    public string? PreviewVideoUrl { get; init; }

    [StringLength(20000)]
    public string? Metadata { get; init; }

    [Required]
    [StringLength(50)]
    public string Level { get; init; } = "Beginner";

    [Range(0, int.MaxValue)]
    public int TotalDurationInMinutes { get; init; }

    public bool IsCertificateIncluded { get; init; }

    public List<CreateSellerCourseSectionDto> Sections { get; init; } = new();
}

public sealed record UpdateSellerCourseDto
{
    [Required]
    public string CategoryId { get; init; } = null!;

    [Required]
    [StringLength(255, MinimumLength = 3)]
    public string Title { get; init; } = null!;

    [Required]
    [StringLength(20000)]
    public string Description { get; init; } = null!;

    [Range(0d, 99999999.99d)]
    public decimal Price { get; init; }

    [Range(0d, 99999999.99d)]
    public decimal? OriginalPrice { get; init; }

    public ProductStatus Status { get; init; } = ProductStatus.Draft;

    public List<string> Tags { get; init; } = new();

    public string? CoverImageUrl { get; init; }

    public string? PreviewVideoUrl { get; init; }

    [StringLength(20000)]
    public string? Metadata { get; init; }

    [Required]
    [StringLength(50)]
    public string Level { get; init; } = "Beginner";

    [Range(0, int.MaxValue)]
    public int TotalDurationInMinutes { get; init; }

    public bool IsCertificateIncluded { get; init; }
}

public sealed record CreateSellerCourseSectionDto
{
    [Required]
    [StringLength(255)]
    public string Title { get; init; } = null!;

    public int SortOrder { get; init; }

    public bool IsActive { get; init; } = true;

    public List<CreateSellerCourseLessonDto> Lessons { get; init; } = new();
}

public sealed record UpdateSellerCourseSectionDto
{
    [Required]
    [StringLength(255)]
    public string Title { get; init; } = null!;

    public int SortOrder { get; init; }

    public bool IsActive { get; init; } = true;
}

public sealed record CreateSellerCourseLessonDto
{
    [Required]
    [StringLength(255)]
    public string Title { get; init; } = null!;

    public string? VideoUrl { get; init; }

    [Range(0, int.MaxValue)]
    public int DurationInSeconds { get; init; }

    public int SortOrder { get; init; }

    public bool IsFreePreview { get; init; }

    public bool IsActive { get; init; } = true;

    public List<UpsertSellerCourseResourceDto> Resources { get; init; } = new();
}

public sealed record UpdateSellerCourseLessonDto
{
    [Required]
    [StringLength(255)]
    public string Title { get; init; } = null!;

    public string? VideoUrl { get; init; }

    [Range(0, int.MaxValue)]
    public int DurationInSeconds { get; init; }

    public int SortOrder { get; init; }

    public bool IsFreePreview { get; init; }

    public bool IsActive { get; init; } = true;

    public List<UpsertSellerCourseResourceDto> Resources { get; init; } = new();
}

public sealed record UpsertSellerCourseResourceDto
{
    [Required]
    [StringLength(255)]
    public string Title { get; init; } = null!;

    [Required]
    public string FileUrl { get; init; } = null!;

    [Required]
    [StringLength(50)]
    public string ResourceType { get; init; } = null!;
}
