using DigitalPreservation.Common.Model.Import;
using DigitalPreservation.Common.Model.Results;
using MediatR;
using Preservation.API.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Preservation.API.Features.ImportJobs.Requests;
using DigitalPreservation.Common.Model.PipelineApi;

namespace Preservation.API.Features.PipelineRunJobs.Requests;

public class GetPipelineJobResultsForDeposit(string depositId) : IRequest<Result<List<ProcessPipelineResult>>>
{
    public string DepositId { get; } = depositId;
}

public class GetPipelineJobResultsForDepositHandler(
    PreservationContext dbContext) : IRequestHandler<GetPipelineJobResultsForDeposit, Result<List<ProcessPipelineResult>>>
{
    public async Task<Result<List<ProcessPipelineResult>>> Handle(GetPipelineJobResultsForDeposit request, CancellationToken cancellationToken)
    {
        var pipelineJobEntities = await dbContext.PipelineRunJobs
            .Where(j => j.Deposit == request.DepositId)
            .ToListAsync(cancellationToken);

        var results = new List<ProcessPipelineResult>();

        foreach (var pipelineJob in pipelineJobEntities)
        {
            results.Add(
                new ProcessPipelineResult
                {
                    JobId = pipelineJob.Id,
                    ArchivalGroup = pipelineJob.ArchivalGroup,
                    Status = pipelineJob.Status,
                    Deposit = pipelineJob.Deposit,
                    DateBegun = pipelineJob.DateSubmitted,
                    DateFinished = pipelineJob.DateFinished,
                    RunUser = pipelineJob.RunUser
                });
        }

        //TODO: map above to process pipeline result
        //var importJobs = pipelineJobEntities
        //    .Select(j => JsonSerializer.Deserialize<ProcessPipelineResult>(j.PipelineJobJson)) //TODO: change type here
        //    .OfType<ProcessPipelineResult>()
        //    .ToList();
        return Result.OkNotNull(results);
    }
}