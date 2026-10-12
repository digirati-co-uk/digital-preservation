namespace Pipeline.API.Config;

public class PipelineToolOptions
{
    public string? PathToBrunnhilde { get; set; }
    public string? PathToPython { get; set; }
    public string DirectorySeparator { get; set; } = "/";
    public string? ObjectsFolder { get; set; }
    public string? ProcessFolder { get; set; }
    public string? ExifToolLocation { get; set; }
    public string? PathToClamScan { get; set; }
    public int? ReleaseLockAttemptTime { get; set; }

    // Default matches the shipped appsettings.json value, so a missing setting still uploads the
    // known tool-output folders rather than silently uploading nothing (issue #275 item 1).
    public string PipelineMetadataFolders { get; set; } =
        "metadata/brunnhilde,metadata/exif,metadata/virus-definition";

    // Parsed once here rather than calling Split(",") at each use site.
    public string[] PipelineMetadataFoldersList =>
        PipelineMetadataFolders.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public string? ProcessFolderBagit { get; set; }
    public string? BagitScript { get; set; }
    public string? BagitProcessFilename { get; set; }

}