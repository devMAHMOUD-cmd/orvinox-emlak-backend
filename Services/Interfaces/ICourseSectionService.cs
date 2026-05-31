using CraftoraApi.DTOs.Course;

namespace CraftoraApi.Services.Interfaces;

public interface ICourseSectionService
{
    Task<CourseSectionResponseDto> CreateSectionAsync(Guid userId, CreateCourseSectionDto dto);

    Task<CourseSectionResponseDto> UpdateSectionAsync(Guid userId, Guid sectionId, UpdateCourseSectionDto dto);

    Task DeleteSectionAsync(Guid userId, Guid sectionId);
}
