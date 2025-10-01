using DigitalPreservation.Common.Model.Results;
using MediatR;
using Preservation.API.Data;
using DigitalPreservation.Common.Model;
using Microsoft.EntityFrameworkCore;
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
            .OrderBy(j => j.DateSubmitted)
            .ToListAsync(cancellationToken);

        var results = new List<ProcessPipelineResult>();
        

        foreach (var pipelineJob in pipelineJobEntities)
        {
            var errors = new List<Error>();
            if (!string.IsNullOrEmpty(pipelineJob.Errors))
            {
                errors.Add(new Error
                {
                    Message = pipelineJob.Errors
                });
            }

            results.Add(
                new ProcessPipelineResult
                {
                    JobId = pipelineJob.Id,
                    ArchivalGroup = pipelineJob.ArchivalGroup,
                    Status = pipelineJob.Status,
                    Deposit = pipelineJob.Deposit,
                    DateBegun = pipelineJob.DateSubmitted,
                    DateFinished = pipelineJob.DateFinished,
                    RunUser = pipelineJob.RunUser,
                    Errors = errors.Any() ? errors.ToArray<Error>() : null 
                });
        }

        return Result.OkNotNull(results);
    }
}