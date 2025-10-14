using DigitalPreservation.Common.Model.Results;
using MediatR;
using Preservation.API.Data;
using DigitalPreservation.Common.Model;
using Microsoft.EntityFrameworkCore;
using DigitalPreservation.Common.Model.PipelineApi;
using Preservation.API.Mutation;

namespace Preservation.API.Features.PipelineRunJobs.Requests;

public class GetPipelineJobResult(string depositId, string jobId) : IRequest<Result<ProcessPipelineResult?>>
{
    public string DepositId { get; } = depositId;
    public string JobId { get; } = jobId;
}

public class GetPipelineJobResultHandler(
    PreservationContext dbContext,
    ResourceMutator resourceMutator) : IRequestHandler<GetPipelineJobResult, Result<ProcessPipelineResult?>>
{
    public async Task<Result<ProcessPipelineResult?>> Handle(GetPipelineJobResult request, CancellationToken cancellationToken)
    {
        var pipelineJobEntity = await dbContext.PipelineRunJobs
            .SingleOrDefaultAsync(j => j.Id == request.JobId && j.Deposit == request.DepositId, cancellationToken: cancellationToken);

        if (pipelineJobEntity == null)
        {
            return Result.Fail<ProcessPipelineResult>(ErrorCodes.NotFound, 
                $"Job {request.JobId} not found for deposit {request.DepositId}");
        }
        
        var result = resourceMutator.MutatePipelineRunJob(pipelineJobEntity);
        return Result.Ok(result);
    }
}