using CraftoraApi.DTOs.Course;
using CraftoraApi.Validators;
using Xunit;

namespace CraftoraApi.Tests;

public sealed class CourseProgressRequestValidatorTests
{
    private readonly UpdateLessonProgressDtoValidator _validator = new();

    [Fact]
    public void Progress_requires_lesson_id()
    {
        var result = _validator.Validate(new UpdateLessonProgressDto
        {
            CourseLessonId = Guid.Empty,
            WatchedSeconds = 0
        });

        Assert.Contains(
            result.Errors,
            error => error.PropertyName == nameof(UpdateLessonProgressDto.CourseLessonId));
    }

    [Fact]
    public void Progress_rejects_negative_watch_time()
    {
        var result = _validator.Validate(new UpdateLessonProgressDto
        {
            CourseLessonId = Guid.NewGuid(),
            WatchedSeconds = -1
        });

        Assert.Contains(
            result.Errors,
            error => error.PropertyName == nameof(UpdateLessonProgressDto.WatchedSeconds));
    }

    [Fact]
    public void Progress_accepts_partial_watch()
    {
        var result = _validator.Validate(new UpdateLessonProgressDto
        {
            CourseLessonId = Guid.NewGuid(),
            WatchedSeconds = 30,
            IsCompleted = false
        });

        Assert.True(result.IsValid);
    }
}
