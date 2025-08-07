using DigitalPreservation.Common.Model.PipelineApi;
using Pipeline.API.ApiClients;

namespace Pipeline.API.Features.Pipeline;

public class PipelineJobStateLogger(IPreservationApiInterface preservationApiInterface, ILogger<PipelineJobStateLogger> logger) : IPipelineJobStateLogger
{
    public async Task LogJobState(string depositId, string status)
    {
        try
        {
            await preservationApiInterface.MakeHttpRequestAsync<PipelineDeposit, PipelineDeposit>("Deposits/pipeline-status", HttpMethod.Post, new PipelineDeposit { Id = depositId, Status = status });
        }
        catch (Exception e)
        {
            logger.LogError($"Error logging job state: {e.Message}");
        }
    }
}
