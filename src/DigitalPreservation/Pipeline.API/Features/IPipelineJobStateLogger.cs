namespace Pipeline.API.Features;

public interface IPipelineJobStateLogger
{
    Task LogJobState(string jobIdentifier, string depositId, string? runUser, string status);
}
