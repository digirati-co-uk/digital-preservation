namespace DigitalPreservation.UI.Infrastructure;

public static class UploadOptions
{
    public const string ConfigKey = "Uploads:MaxUploadBytes";

    // 2 GB: matches the threshold the browser upload warning already used before it became a hard limit.
    public const long DefaultMaxUploadBytes = 2L * 1024 * 1024 * 1024;

    public static long GetMaxUploadBytes(IConfiguration configuration) =>
        configuration.GetValue<long?>(ConfigKey) ?? DefaultMaxUploadBytes;
}
