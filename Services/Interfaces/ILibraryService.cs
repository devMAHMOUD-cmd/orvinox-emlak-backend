using CraftoraApi.DTOs.Library;

namespace CraftoraApi.Services.Interfaces;

public interface ILibraryService
{
    Task<List<LibraryItemDto>> GetMyLibraryAsync(Guid userId);

    Task MarkAsAccessedAsync(Guid userId, Guid productId);
}
