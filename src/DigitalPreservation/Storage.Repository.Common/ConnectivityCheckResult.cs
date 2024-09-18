namespace Storage.Repository.Common;

public class ConnectivityCheckResult
{
    public const string DigitalPreservationBackEnd = "Digital Preservation Back End";
    public const string StorageApiReadS3 = "Storage API Read S3";
    public const string PreservationApiReadS3 = "Preservation API Read S3";
    public const string PreservationUIReadS3 = "Preservation UI Read S3";
    
    public string? Name { get; set; }
    public bool Success { get; set; }
    public string? Error { get; set; }
}