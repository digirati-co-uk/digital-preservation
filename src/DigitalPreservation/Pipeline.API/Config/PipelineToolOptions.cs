namespace Pipeline.API.Config;

public class PipelineToolOptions
{
    public string? PathToBrunnhilde { get; set; }
    public string? PathToPython { get; set; }
    public string? DirectorySeparator { get; set; }
    public string? ObjectsFolder { get; set; }
    public string? ProcessFolder { get; set; }
    public string? ExifToolLocation { get; set; }
    public int? ReleaseLockAttemptTime { get; set; }
    public string? PipelineMetadataFolders { get; set; }
}