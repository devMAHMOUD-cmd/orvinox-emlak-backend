using NpgsqlTypes;

namespace CraftoraApi.Models.Enums;

public enum ProductType
{
    [PgName("digital_file")]
    DigitalFile,

    [PgName("course")]
    Course
}
