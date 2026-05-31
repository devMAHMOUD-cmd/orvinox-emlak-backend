using NpgsqlTypes;

namespace CraftoraApi.Models.Enums;

public enum SubStatus
{
    [PgName("active")]
    Active,

    [PgName("past_due")]
    PastDue,

    [PgName("canceled")]
    Canceled,

    [PgName("unpaid")]
    Unpaid
}
