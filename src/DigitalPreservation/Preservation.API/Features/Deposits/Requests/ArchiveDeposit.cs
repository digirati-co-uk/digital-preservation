using Amazon.SimpleNotificationService;
using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.DepositArchiver;
using DigitalPreservation.Common.Model.Identity;
using DigitalPreservation.Common.Model.PipelineApi;
using DigitalPreservation.Common.Model.Results;
using MediatR;
using Microsoft.Extensions.Options;
using Preservation.API.Data;
using Preservation.API.Data.Entities;

namespace Preservation.API.Features.Deposits.Requests;

public class ArchiveDeposit(ArchiveDepositJob archiveDepositJob) : IRequest<Result>
{
    public ArchiveDepositJob ArchiveDepositJob { get; } = archiveDepositJob;

}

public class ArchiveDepositHandler(
    PreservationContext dbContext
) : IRequestHandler<ArchiveDeposit, Result>
{
    public async Task<Result> Handle(ArchiveDeposit request, CancellationToken cancellationToken)
    {
        if(string.IsNullOrEmpty(request.ArchiveDepositJob.DepositId) || string.IsNullOrEmpty(request.ArchiveDepositJob.DepositUri))
            return Result.Fail(ErrorCodes.BadRequest, "Null or empty deposit id and/or URI");

        var newArchiveJob = new DepositArchiveJob
        {
            DepositId = request.ArchiveDepositJob.DepositId,
            DepositUri = request.ArchiveDepositJob.DepositUri,
            StartTime = request.ArchiveDepositJob.StartTime!.Value,
            EndTime = request.ArchiveDepositJob.EndTime,
            BatchNumber = request.ArchiveDepositJob.BatchNumber!,
            Id = request.ArchiveDepositJob.Id!,
            DeletedCount = request.ArchiveDepositJob.DeletedCount ?? 0,
            Errors = request.ArchiveDepositJob.Errors
        };

        dbContext.DepositArchiveJobs.Add(newArchiveJob);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Result.Ok();
    }
}