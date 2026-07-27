using System.ComponentModel.DataAnnotations;

namespace CraftoraApi.DTOs.Report;

public sealed record CreateReportDto(
    [property: Required(ErrorMessage = "Sikayet hedef tipi zorunludur.")]
    [property: StringLength(50, ErrorMessage = "Sikayet hedef tipi en fazla 50 karakter olabilir.")]
    string TargetType,

    [Required] Guid TargetId,

    [property: Required(ErrorMessage = "Sikayet sebebi zorunludur.")]
    [property: StringLength(50, ErrorMessage = "Sikayet sebebi en fazla 50 karakter olabilir.")]
    string Reason,

    [property: StringLength(5000, ErrorMessage = "Sikayet aciklamasi en fazla 5000 karakter olabilir.")]
    string? Description);

public sealed record ReportCreatedDto(
    Guid Id,
    string Status,
    DateTime CreatedAt);
