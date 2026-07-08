using System.ComponentModel.DataAnnotations;

namespace CraftoraApi.DTOs.Report;

public sealed record CreateReportDto(
    [Required] string TargetType,
    [Required] Guid TargetId,
    [Required] string Reason,
    string? Description);

public sealed record ReportCreatedDto(
    Guid Id,
    string Status,
    DateTime CreatedAt);
