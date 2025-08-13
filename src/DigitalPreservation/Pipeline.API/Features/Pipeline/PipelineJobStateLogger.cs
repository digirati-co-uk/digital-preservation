using DigitalPreservation.Common.Model.PipelineApi;
using Pipeline.API.ApiClients;

namespace Pipeline.API.Features.Pipeline;

public class PipelineJobStateLogger(IPreservationApiInterface preservationApiInterface, ILogger<PipelineJobStateLogger> logger) : IPipelineJobStateLogger
{
    public async Task LogJobState(string jobIdentifier, string depositId, string? runUser, string status)
    {
        try
        {
            await preservationApiInterface.MakeHttpRequestAsync<PipelineDeposit, PipelineDeposit>("Deposits/pipeline-status", HttpMethod.Post, new PipelineDeposit { Id = jobIdentifier, DepositId = depositId, Status = status, RunUser = runUser});
        }
        catch (Exception e)
        {
            logger.LogError($"Error logging job state for job id {jobIdentifier} and deposit id {depositId}: {e.Message}");
        }
    }
}
