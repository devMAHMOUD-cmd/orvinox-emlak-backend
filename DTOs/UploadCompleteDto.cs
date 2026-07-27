using System.ComponentModel.DataAnnotations;

namespace CraftoraApi.DTOs;

public sealed record UploadCompleteDto(
    [property: Required(ErrorMessage = "Object key zorunludur.")]
    [property: StringLength(1024, ErrorMessage = "Object key en fazla 1024 karakter olabilir.")]
    string ObjectKey,

    [property: Required(ErrorMessage = "Entity type zorunludur.")]
    [property: StringLength(30, ErrorMessage = "Entity type en fazla 30 karakter olabilir.")]
    string EntityType,

    [property: Required(ErrorMessage = "Entity ID zorunludur.")]
    Guid EntityId);
