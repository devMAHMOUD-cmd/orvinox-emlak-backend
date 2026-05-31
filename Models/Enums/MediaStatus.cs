using NpgsqlTypes;

namespace CraftoraApi.Models.Enums;

public enum MediaStatus
{
    [PgName("processing")]
    Processing,

    [PgName("ready")]
    Ready,

    [PgName("failed")]
    Failed
}
