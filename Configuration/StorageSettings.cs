namespace CraftoraApi.Configuration;

public sealed class StorageSettings
{
    public string ServiceUrl { get; set; } = string.Empty;

    public string PublicServiceUrl { get; set; } = string.Empty;

    public string AccessKey { get; set; } = string.Empty;

    public string SecretKey { get; set; } = string.Empty;

    public bool ForcePathStyle { get; set; } = true;

    public string[] CorsAllowedOrigins { get; set; } = [];
}
