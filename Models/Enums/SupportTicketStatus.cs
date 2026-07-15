using NpgsqlTypes;

namespace CraftoraApi.Models.Enums;

public enum SupportTicketStatus
{
    [PgName("open")]
    Open,

    [PgName("answered")]
    Answered,

    [PgName("closed")]
    Closed
}
