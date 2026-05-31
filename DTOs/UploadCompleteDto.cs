using System.ComponentModel.DataAnnotations;

namespace CraftoraApi.DTOs;

public sealed record UploadCompleteDto(
    [property: Required(ErrorMessage = "Object key zorunludur.")]
    string ObjectKey,

    [property: Required(ErrorMessage = "Entity type zorunludur.")]
    string EntityType,

    [property: Required(ErrorMessage = "Entity ID zorunludur.")]
    Guid EntityId);
