using CraftoraApi.DTOs.Course;

namespace CraftoraApi.Services.Interfaces;

public interface ILessonResourceService
{
    Task<LessonResourceResponseDto> AddResourceAsync(Guid userId, CreateLessonResourceDto dto);

    Task RemoveResourceAsync(Guid userId, Guid resourceId);
}
