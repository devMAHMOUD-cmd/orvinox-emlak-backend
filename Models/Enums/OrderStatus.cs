using NpgsqlTypes;

namespace CraftoraApi.Models.Enums;

public enum OrderStatus
{
    [PgName("pending")]
    Pending,

    [PgName("completed")]
    Completed,

    [PgName("failed")]
    Failed,

    [PgName("refunded")]
    Refunded
}
