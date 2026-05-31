using CraftoraApi.Data;
using CraftoraApi.DTOs.Course;
using CraftoraApi.Middleware;
using CraftoraApi.Models.Entities;
using CraftoraApi.Models.Enums;
using CraftoraApi.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CraftoraApi.Services;

public sealed class CourseService : ICourseService
{
    private static readonly HashSet<string> AllowedLevels = new(StringComparer.OrdinalIgnoreCase)
    {
        "Beginner",
        "Intermediate",
        "Advanced"
    };

    private static readonly HashSet<string> AllowedResourceTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Document",
        "SourceCode",
        "ExternalLink"
    };

    private readonly AppDbContext _dbContext;

    public CourseService(AppDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public async Task<CourseResponseDto> CreateCourseAsync(Guid userId, CreateCourseDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);
        ValidateLevel(dto.Level);
        ValidateResources(dto.Sections.SelectMany(section => section.Lessons).SelectMany(lesson => lesson.Resources));

        var product = await GetOwnedProductAsync(userId, dto.ProductId);

        var courseExists = await _dbContext.Courses.AnyAsync(course => course.ProductId == dto.ProductId);
        if (courseExists)
        {
            throw new ConflictException("Bu urun icin zaten bir egitim kaydi var.");
        }

        product.Type = ProductType.Course;

        var course = new Course
        {
            ProductId = dto.ProductId,
            Level = NormalizeLevel(dto.Level),
            TotalDurationInMinutes = dto.TotalDurationInMinutes,
            IsCertificateIncluded = dto.IsCertificateIncluded
        };

        AddSections(course, dto.Sections);

        _dbContext.Courses.Add(course);
        await _dbContext.SaveChangesAsync();

        return await GetCourseByIdAsync(course.Id);
    }

    public async Task<CourseResponseDto> UpdateCourseAsync(Guid userId, Guid courseId, UpdateCourseDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);
        ValidateLevel(dto.Level);
        ValidateResources(dto.Sections.SelectMany(section => section.Lessons).SelectMany(lesson => lesson.Resources));

        var course = await _dbContext.Courses
            .Include(course => course.CourseSections)
                .ThenInclude(section => section.CourseLessons)
                    .ThenInclude(lesson => lesson.LessonResources)
            .Include(course => course.CourseSections)
                .ThenInclude(section => section.CourseQuizzes)
            .FirstOrDefaultAsync(course => course.Id == courseId);

        if (course is null)
        {
            throw new NotFoundException("Egitim bulunamadi.");
        }

        await EnsureProductOwnershipAsync(userId, course.ProductId);

        course.Level = NormalizeLevel(dto.Level);
        course.TotalDurationInMinutes = dto.TotalDurationInMinutes;
        course.IsCertificateIncluded = dto.IsCertificateIncluded;
        course.UpdatedAt = DateTime.UtcNow;

        _dbContext.CourseSections.RemoveRange(course.CourseSections);
        course.CourseSections.Clear();
        AddSections(course, dto.Sections);

        await _dbContext.SaveChangesAsync();

        return await GetCourseByIdAsync(course.Id);
    }

    public async Task DeleteCourseAsync(Guid userId, Guid courseId)
    {
        var course = await _dbContext.Courses.FirstOrDefaultAsync(course => course.Id == courseId);
        if (course is null)
        {
            throw new NotFoundException("Egitim bulunamadi.");
        }

        await EnsureProductOwnershipAsync(userId, course.ProductId);

        _dbContext.Courses.Remove(course);
        await _dbContext.SaveChangesAsync();
    }

    public async Task<CourseResponseDto> GetCourseByIdAsync(Guid courseId)
    {
        var course = await BuildCourseTreeQuery()
            .FirstOrDefaultAsync(course => course.Id == courseId);

        if (course is null)
        {
            throw new NotFoundException("Egitim bulunamadi.");
        }

        return MapToResponse(course);
    }

    public async Task<CourseResponseDto> GetCourseTreeByProductIdAsync(Guid productId)
    {
        var course = await BuildCourseTreeQuery()
            .FirstOrDefaultAsync(course => course.ProductId == productId);

        if (course is null)
        {
            throw new NotFoundException("Bu urune ait egitim bulunamadi.");
        }

        return MapToResponse(course);
    }

    private IQueryable<Course> BuildCourseTreeQuery()
    {
        return _dbContext.Courses
            .AsNoTracking()
            .Include(course => course.CourseSections)
                .ThenInclude(section => section.CourseLessons)
                    .ThenInclude(lesson => lesson.LessonResources)
            .Include(course => course.CourseSections)
                .ThenInclude(section => section.CourseQuizzes);
    }

    private async Task<Product> GetOwnedProductAsync(Guid userId, Guid productId)
    {
        var product = await _dbContext.Products
            .Include(product => product.Shop)
            .FirstOrDefaultAsync(product =>
                product.Id == productId &&
                product.IsActive == true);

        if (product is null)
        {
            throw new NotFoundException("Urun bulunamadi.");
        }

        if (product.Shop.UserId != userId)
        {
            throw new ForbiddenException("Bu urun icin egitim yonetme yetkiniz yok.");
        }

        return product;
    }

    private async Task EnsureProductOwnershipAsync(Guid userId, Guid productId)
    {
        _ = await GetOwnedProductAsync(userId, productId);
    }

    private static void AddSections(Course course, IEnumerable<CreateCourseSectionDto> sections)
    {
        foreach (var sectionDto in sections)
        {
            var section = new CourseSection
            {
                Title = sectionDto.Title.Trim(),
                SortOrder = sectionDto.SortOrder,
                IsActive = sectionDto.IsActive
            };

            foreach (var lessonDto in sectionDto.Lessons)
            {
                var lesson = new CourseLesson
                {
                    Title = lessonDto.Title.Trim(),
                    VideoUrl = lessonDto.VideoUrl,
                    DurationInSeconds = lessonDto.DurationInSeconds,
                    SortOrder = lessonDto.SortOrder,
                    IsFreePreview = lessonDto.IsFreePreview,
                    IsActive = lessonDto.IsActive
                };

                foreach (var resourceDto in lessonDto.Resources)
                {
                    lesson.LessonResources.Add(new LessonResource
                    {
                        Title = resourceDto.Title.Trim(),
                        FileUrl = resourceDto.FileUrl,
                        ResourceType = NormalizeResourceType(resourceDto.ResourceType)
                    });
                }

                section.CourseLessons.Add(lesson);
            }

            foreach (var quizDto in sectionDto.Quizzes)
            {
                section.CourseQuizzes.Add(new CourseQuiz
                {
                    Title = quizDto.Title.Trim(),
                    PassingScore = quizDto.PassingScore,
                    IsActive = quizDto.IsActive
                });
            }

            course.CourseSections.Add(section);
        }
    }

    private static void AddSections(Course course, IEnumerable<UpdateCourseSectionDto> sections)
    {
        foreach (var sectionDto in sections)
        {
            var section = new CourseSection
            {
                Title = sectionDto.Title.Trim(),
                SortOrder = sectionDto.SortOrder,
                IsActive = sectionDto.IsActive
            };

            foreach (var lessonDto in sectionDto.Lessons)
            {
                var lesson = new CourseLesson
                {
                    Title = lessonDto.Title.Trim(),
                    VideoUrl = lessonDto.VideoUrl,
                    DurationInSeconds = lessonDto.DurationInSeconds,
                    SortOrder = lessonDto.SortOrder,
                    IsFreePreview = lessonDto.IsFreePreview,
                    IsActive = lessonDto.IsActive
                };

                foreach (var resourceDto in lessonDto.Resources)
                {
                    lesson.LessonResources.Add(new LessonResource
                    {
                        Title = resourceDto.Title.Trim(),
                        FileUrl = resourceDto.FileUrl,
                        ResourceType = NormalizeResourceType(resourceDto.ResourceType)
                    });
                }

                section.CourseLessons.Add(lesson);
            }

            foreach (var quizDto in sectionDto.Quizzes)
            {
                section.CourseQuizzes.Add(new CourseQuiz
                {
                    Title = quizDto.Title.Trim(),
                    PassingScore = quizDto.PassingScore,
                    IsActive = quizDto.IsActive
                });
            }

            course.CourseSections.Add(section);
        }
    }

    private static CourseResponseDto MapToResponse(Course course)
    {
        var sections = course.CourseSections
            .OrderBy(section => section.SortOrder)
            .Select(section => new CourseSectionResponseDto(
                Id: section.Id,
                CourseId: section.CourseId,
                Title: section.Title,
                SortOrder: section.SortOrder,
                IsActive: section.IsActive,
                CreatedAt: section.CreatedAt,
                UpdatedAt: section.UpdatedAt,
                Lessons: section.CourseLessons
                    .OrderBy(lesson => lesson.SortOrder)
                    .Select(lesson => new CourseLessonResponseDto(
                        Id: lesson.Id,
                        CourseSectionId: lesson.CourseSectionId,
                        Title: lesson.Title,
                        VideoUrl: lesson.VideoUrl,
                        DurationInSeconds: lesson.DurationInSeconds,
                        SortOrder: lesson.SortOrder,
                        IsFreePreview: lesson.IsFreePreview,
                        IsActive: lesson.IsActive,
                        CreatedAt: lesson.CreatedAt,
                        UpdatedAt: lesson.UpdatedAt,
                        Resources: lesson.LessonResources
                            .OrderBy(resource => resource.Title)
                            .Select(resource => new LessonResourceResponseDto(
                                Id: resource.Id,
                                CourseLessonId: resource.CourseLessonId,
                                Title: resource.Title,
                                FileUrl: resource.FileUrl,
                                ResourceType: resource.ResourceType,
                                CreatedAt: resource.CreatedAt,
                                UpdatedAt: resource.UpdatedAt))
                            .ToList()))
                    .ToList(),
                Quizzes: section.CourseQuizzes
                    .OrderBy(quiz => quiz.Title)
                    .Select(quiz => new CourseQuizResponseDto(
                        Id: quiz.Id,
                        CourseSectionId: quiz.CourseSectionId,
                        Title: quiz.Title,
                        PassingScore: quiz.PassingScore,
                        IsActive: quiz.IsActive,
                        CreatedAt: quiz.CreatedAt,
                        UpdatedAt: quiz.UpdatedAt))
                    .ToList()))
            .ToList();

        return new CourseResponseDto(
            Id: course.Id,
            ProductId: course.ProductId,
            Level: course.Level,
            TotalDurationInMinutes: course.TotalDurationInMinutes,
            IsCertificateIncluded: course.IsCertificateIncluded,
            CreatedAt: course.CreatedAt,
            UpdatedAt: course.UpdatedAt,
            Sections: sections);
    }

    private static void ValidateLevel(string level)
    {
        if (!AllowedLevels.Contains(level))
        {
            throw new ValidationException("Level degeri Beginner, Intermediate veya Advanced olmalidir.");
        }
    }

    private static void ValidateResources(IEnumerable<CreateLessonResourceDto> resources)
    {
        foreach (var resource in resources)
        {
            ValidateResourceType(resource.ResourceType);
        }
    }

    private static void ValidateResources(IEnumerable<UpdateLessonResourceDto> resources)
    {
        foreach (var resource in resources)
        {
            ValidateResourceType(resource.ResourceType);
        }
    }

    private static void ValidateResourceType(string resourceType)
    {
        if (!AllowedResourceTypes.Contains(resourceType))
        {
            throw new ValidationException("ResourceType degeri Document, SourceCode veya ExternalLink olmalidir.");
        }
    }

    private static string NormalizeLevel(string level)
    {
        return AllowedLevels.First(allowedLevel => allowedLevel.Equals(level, StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeResourceType(string resourceType)
    {
        return AllowedResourceTypes.First(allowedType => allowedType.Equals(resourceType, StringComparison.OrdinalIgnoreCase));
    }
}
