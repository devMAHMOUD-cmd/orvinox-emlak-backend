using System.ComponentModel.DataAnnotations;

namespace CraftoraApi.DTOs;

public sealed record GeneratePresignedUrlDto(
    [property: Required(ErrorMessage = "Dosya adı zorunludur.")]
    [property: StringLength(255, ErrorMessage = "Dosya adı en fazla 255 karakter olabilir.")]
    string FileName,

    [property: Required(ErrorMessage = "İçerik tipi zorunludur.")]
    [property: StringLength(100, ErrorMessage = "İçerik tipi en fazla 100 karakter olabilir.")]
    string ContentType);
