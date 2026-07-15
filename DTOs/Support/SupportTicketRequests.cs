using System.ComponentModel.DataAnnotations;

namespace CraftoraApi.DTOs.Support;

public sealed record CreateTicketDto(
    [property: Required(ErrorMessage = "Konu zorunludur.")]
    [property: StringLength(200, MinimumLength = 1, ErrorMessage = "Konu 1 ile 200 karakter arasinda olmalidir.")]
    string Subject,

    [property: Required(ErrorMessage = "Mesaj zorunludur.")]
    [property: StringLength(5000, MinimumLength = 1, ErrorMessage = "Mesaj 1 ile 5000 karakter arasinda olmalidir.")]
    string Message);

public sealed record AddMessageDto(
    [property: Required(ErrorMessage = "Mesaj zorunludur.")]
    [property: StringLength(5000, MinimumLength = 1, ErrorMessage = "Mesaj 1 ile 5000 karakter arasinda olmalidir.")]
    string Message);

public sealed record AdminReplyDto(
    [property: Required(ErrorMessage = "Cevap zorunludur.")]
    [property: StringLength(5000, MinimumLength = 1, ErrorMessage = "Cevap 1 ile 5000 karakter arasinda olmalidir.")]
    string Message);

public sealed record UpdateTicketStatusDto(
    [property: Required(ErrorMessage = "Ticket durumu zorunludur.")]
    [property: RegularExpression("^(open|answered|closed)$", ErrorMessage = "Gecersiz ticket durumu.")]
    string Status);
