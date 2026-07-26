using CraftoraApi.DTOs.Course;
using FluentValidation;

namespace CraftoraApi.Validators;

public sealed class UpdateLessonProgressDtoValidator : AbstractValidator<UpdateLessonProgressDto>
{
    public UpdateLessonProgressDtoValidator()
    {
        RuleFor(request => request.CourseLessonId)
            .NotEmpty()
            .WithMessage("Ders zorunludur.");

        RuleFor(request => request.WatchedSeconds)
            .GreaterThanOrEqualTo(0)
            .WithMessage("Izleme suresi negatif olamaz.");
    }
}
