using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.DepositArchiver;
using DigitalPreservation.Common.Model.Identity;
using DigitalPreservation.Common.Model.PipelineApi;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Core.Auth;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Preservation.API.Data;
using Preservation.API.Data.Entities;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Preservation.API.Features.Deposits.Requests;

public class ArchiveDeposit(ArchiveDepositJob archiveDepositJob) : IRequest<Result>
{
    public ArchiveDepositJob ArchiveDepositJob { get; } = archiveDepositJob;

}

public class ArchiveDepositHandler(
    ILogger<RunPipelineHandler> logger,
    PreservationContext dbContext,
    IAmazonSimpleNotificationService snsClient,
    IOptions<PipelineOptions> pipelineOptions,
    IIdentityMinter identityMinter) : IRequestHandler<ArchiveDeposit, Result>
{
    public async Task<Result> Handle(ArchiveDeposit request, CancellationToken cancellationToken)
    {
        var newArchiveJob = new DepositArchiveJob
        {
            DepositId = request.ArchiveDepositJob.DepositId,
            DepositUri = request.ArchiveDepositJob.DepositUri,
            StartTime = request.ArchiveDepositJob.StartTime!.Value,
            EndTime = request.ArchiveDepositJob.EndTime,
            BatchNumber = request.ArchiveDepositJob.BatchNumber!,
            Id = request.ArchiveDepositJob.Id!,
            DeletedCount = request.ArchiveDepositJob.DeletedCount!.Value,
            Errors = request.ArchiveDepositJob.Errors
        };

        dbContext.DepositArchiveJobs.Add(newArchiveJob);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Result.Ok();
    }
}