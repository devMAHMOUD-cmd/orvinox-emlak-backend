using System.ComponentModel.DataAnnotations;

namespace CraftoraApi.DTOs;

public sealed record GeneratePresignedUrlDto(
    [property: Required(ErrorMessage = "Dosya adı zorunludur.")]
    string FileName,

    [property: Required(ErrorMessage = "İçerik tipi zorunludur.")]
    string ContentType);
