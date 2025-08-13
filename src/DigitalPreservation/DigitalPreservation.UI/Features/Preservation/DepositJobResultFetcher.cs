using DigitalPreservation.Common.Model.Import;
using DigitalPreservation.Common.Model.PipelineApi;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.UI.Features.Preservation.Requests;
using DigitalPreservation.Utils;
using MediatR;

namespace DigitalPreservation.UI.Features.Preservation;

public class DepositJobResultFetcher()
{
    // This gets the preservation API's view, which is not necessarily up to date.
    // TODO: The preservation API should listen for result completion and update these behind the scenes
    public static async Task<Result<List<ImportJobResult>>> GetImportJobResults(string depositId, IMediator mediator)
    {
        var importJobsResult = await mediator.Send(new GetImportJobResults(depositId));
        if (importJobsResult.Failure)
        {
            return importJobsResult;
        }

        var importJobResults = importJobsResult.Value!;
        // If there are not too many, get the full - refreshed - details.
        // see above TODO - ideally they are always up to date because the preservation DB has been updated out of band.
        var incompleteJobCount = importJobResults.Count(ij => ImportJobStates.IsNotComplete(ij.Status));
        if (incompleteJobCount < 5)
        {
            var updatedImportJobResults = new List<ImportJobResult>();
            // There should be only 0 or 1 for UI-launched jobs, but API-launched jobs may have many.
            foreach (var importJobResult in importJobResults)
            {
                if (ImportJobStates.IsNotComplete(importJobResult.Status))
                {
                    var ijrResult = await mediator.Send(new GetImportJobResult(depositId, importJobResult.Id!.GetSlug()!));
                    if (ijrResult.Success)
                    {
                        updatedImportJobResults.Add(ijrResult.Value!);
                        continue;
                    }
                }
                updatedImportJobResults.Add(importJobResult);
            }
            importJobResults = updatedImportJobResults;
        }
        return Result.OkNotNull(importJobResults);

    }

    // This gets the preservation API's view, which is not necessarily up to date.
    // TODO: The preservation API should listen for result completion and update these behind the scenes
    public static async Task<Result<List<ProcessPipelineResult>>> GetPipelineJobResults(string depositId, IMediator mediator)
    {
        var pipelineJobsResult = await mediator.Send(new GetPipelineJobsResults(depositId));
        if (pipelineJobsResult.Failure)
        {
            return pipelineJobsResult;
        }

        var pipelineJobResults = pipelineJobsResult.Value!;
        // If there are not too many, get the full - refreshed - details.
        // see above TODO - ideally they are always up to date because the preservation DB has been updated out of band.
        var incompleteJobCount = pipelineJobResults.Count(ij => ImportJobStates.IsNotComplete(ij.Status));
        if (incompleteJobCount < 5)
        {
            var processPipelineResults = new List<ProcessPipelineResult>();
            // There should be only 0 or 1 for UI-launched jobs, but API-launched jobs may have many.
            foreach (var pipelineJobResult in pipelineJobResults)
            {
                if (ImportJobStates.IsNotComplete(pipelineJobResult.Status))
                {
                    var ijrResult = await mediator.Send(new GetPipelineJobResult(depositId, pipelineJobResult.Id!.GetSlug()!));
                    if (ijrResult.Success)
                    {
                        processPipelineResults.Add(ijrResult.Value!);
                        continue;
                    }
                }
                processPipelineResults.Add(pipelineJobResult);
            }
            pipelineJobResults = processPipelineResults;
        }
        return Result.OkNotNull(pipelineJobResults);

    }
}