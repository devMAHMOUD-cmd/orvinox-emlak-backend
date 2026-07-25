using System.Text.Json.Serialization;
using NpgsqlTypes;

namespace CraftoraApi.Models.Enums;

public enum ProductType
{
    [JsonStringEnumMemberName("digital_file")]
    [PgName("digital_file")]
    DigitalFile,

    [JsonStringEnumMemberName("course")]
    [PgName("course")]
    Course
}
