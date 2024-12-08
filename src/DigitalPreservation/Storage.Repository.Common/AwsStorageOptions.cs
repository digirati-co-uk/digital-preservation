namespace Storage.Repository.Common;

public class AwsStorageOptions
{
    public const string AwsStorage = "AwsStorage";
    
    public required string DefaultWorkingBucket { get; set; }
    public string? S3HealthCheckKey { get; set; }
}