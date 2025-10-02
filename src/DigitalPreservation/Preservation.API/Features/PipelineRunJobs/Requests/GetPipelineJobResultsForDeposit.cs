using DigitalPreservation.Common.Model.Results;
using MediatR;
using Preservation.API.Data;
using Microsoft.EntityFrameworkCore;
using DigitalPreservation.Common.Model.PipelineApi;
using Preservation.API.Mutation;

namespace Preservation.API.Features.PipelineRunJobs.Requests;

public class GetPipelineJobResultsForDeposit(string depositId) : IRequest<Result<List<ProcessPipelineResult>>>
{
    public string DepositId { get; } = depositId;
}

public class GetPipelineJobResultsForDepositHandler(
    PreservationContext dbContext,
    ResourceMutator resourceMutator) : IRequestHandler<GetPipelineJobResultsForDeposit, Result<List<ProcessPipelineResult>>>
{
    public async Task<Result<List<ProcessPipelineResult>>> Handle(GetPipelineJobResultsForDeposit request, CancellationToken cancellationToken)
    {
        var pipelineJobEntities = await dbContext.PipelineRunJobs
            .Where(j => j.Deposit == request.DepositId)
            .OrderBy(j => j.DateSubmitted)
            .ToListAsync(cancellationToken);

        var results = resourceMutator.MutatePipelineRunJobs(pipelineJobEntities);
        return Result.OkNotNull(results);
    }
}