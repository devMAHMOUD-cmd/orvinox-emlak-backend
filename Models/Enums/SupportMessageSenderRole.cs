using NpgsqlTypes;

namespace CraftoraApi.Models.Enums;

public enum SupportMessageSenderRole
{
    [PgName("user")]
    User,

    [PgName("admin")]
    Admin
}
