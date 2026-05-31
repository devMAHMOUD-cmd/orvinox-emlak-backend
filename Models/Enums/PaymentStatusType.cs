using NpgsqlTypes;

namespace CraftoraApi.Models.Enums;

public enum PaymentStatusType
{
    [PgName("processing")]
    Processing,

    [PgName("succeeded")]
    Succeeded,

    [PgName("failed")]
    Failed,

    [PgName("refunded")]
    Refunded
}
