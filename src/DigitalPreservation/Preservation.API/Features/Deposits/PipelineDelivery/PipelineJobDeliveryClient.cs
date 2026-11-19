using System.Net.Http.Json;
using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Results;
using Preservation.API.Features.Deposits.Requests;

namespace Preservation.API.Features.Deposits.PipelineDelivery;

/// <summary>
/// Delivers a pipeline job to the Pipeline API's internal enqueue endpoint (POST /pipeline/jobs)
/// over HTTP, instead of publishing to SNS (issue #352) - local development/testing only. A
/// minimal typed HttpClient rather than a full ADR-0000 *.Client project: this is a dev-only path,
/// and the issue explicitly allows this lighter approach for it.
/// </summary>
public interface IPipelineJobDeliveryClient
{
    Task<Result> EnqueuePipelineJob(PipelineJobMessage pipelineJobMessage, CancellationToken cancellationToken);
}

internal class PipelineJobDeliveryClient(HttpClient httpClient) : IPipelineJobDeliveryClient
{
    public async Task<Result> EnqueuePipelineJob(PipelineJobMessage pipelineJobMessage, CancellationToken cancellationToken)
    {
        var response = await httpClient.PostAsJsonAsync("pipeline/jobs", pipelineJobMessage, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return Result.Ok();
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        return Result.Fail(ErrorCodes.UnknownError,
            $"Pipeline API returned {(int)response.StatusCode} from the local delivery enqueue call: {body}");
    }
}
