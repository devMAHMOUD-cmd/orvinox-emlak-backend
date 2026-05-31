using NpgsqlTypes;

namespace CraftoraApi.Models.Enums;

public enum UserRole
{
    [PgName("user")]
    User,

    [PgName("seller")]
    Seller,

    [PgName("admin")]
    Admin
}
