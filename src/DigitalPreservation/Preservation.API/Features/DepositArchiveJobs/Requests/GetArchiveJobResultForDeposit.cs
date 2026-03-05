using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.DepositArchiver;
using DigitalPreservation.Common.Model.PipelineApi;
using DigitalPreservation.Common.Model.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Preservation.API.Data;
using Preservation.API.Features.PipelineRunJobs.Requests;
using Preservation.API.Mutation;

namespace Preservation.API.Features.DepositArchiveJobs.Requests;

public class GetArchiveJobResultForDeposit(string depositId) : IRequest<Result<ArchiveJobResult?>>
{
    public string DepositId { get; } = depositId;
}

public class GetArchiveJobResultForDepositHandler(
    PreservationContext dbContext,
    ResourceMutator resourceMutator) : IRequestHandler<GetArchiveJobResultForDeposit, Result<ArchiveJobResult?>>
{
    public async Task<Result<ArchiveJobResult?>> Handle(GetArchiveJobResultForDeposit request, CancellationToken cancellationToken)
    {
        var archiveJobEntity = await dbContext.DepositArchiveJobs
            .SingleOrDefaultAsync(j => j.DepositId == request.DepositId, cancellationToken: cancellationToken);

        if (archiveJobEntity == null)
        {
            return Result.Fail<ArchiveJobResult>(ErrorCodes.NotFound,
                $" deposit {request.DepositId}");
        }

        var result = resourceMutator.MutateDepositArchiveJob(archiveJobEntity); //Mutate ArchiveJobResult
        return Result.Ok(result);
    }
}
