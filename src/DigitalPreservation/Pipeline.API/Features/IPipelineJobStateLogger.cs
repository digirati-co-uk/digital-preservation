namespace Pipeline.API.Features;

public interface IPipelineJobStateLogger
{
    Task LogJobState(string depositId, string status);
}
