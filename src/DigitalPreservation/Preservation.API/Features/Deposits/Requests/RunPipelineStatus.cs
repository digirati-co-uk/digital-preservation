using System.Security.Claims;
using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Preservation.API.Data;
using DigitalPreservation.Common.Model.PipelineApi;
using DigitalPreservation.Core.Auth;
using DigitalPreservation.Utils;

namespace Preservation.API.Features.Deposits.Requests;

public class RunPipelineStatus(PipelineDeposit pipelineDeposit, ClaimsPrincipal user) : IRequest<Result>
{
    public PipelineDeposit PipelineDeposit { get; } = pipelineDeposit;
    public ClaimsPrincipal User { get; } = user;
}

public class RunPipelineStatusHandler(
    ILogger<RunPipelineStatusHandler> logger,
    PreservationContext dbContext) : IRequestHandler<RunPipelineStatus, Result>
{
    public async Task<Result> Handle(RunPipelineStatus request, CancellationToken cancellationToken)
    {
        var deposit = await dbContext.Deposits.SingleOrDefaultAsync(
            d => d.MintedId == request.PipelineDeposit.DepositId, cancellationToken);

        if (deposit == null)
        {
            return Result.Fail(ErrorCodes.NotFound, "No deposit for deposit id " + request.PipelineDeposit.DepositId);
        }
        var entity = await dbContext.PipelineRunJobs.SingleAsync(
            d => d.Deposit == request.PipelineDeposit.DepositId && d.Id == request.PipelineDeposit.Id, cancellationToken);

        switch (request.PipelineDeposit.Status)
        {
            case PipelineJobStates.Waiting:
                // The PipelineRunJob must already exist
                break;
            case PipelineJobStates.Running:
                entity.DateBegun = DateTime.UtcNow;
                break;
            case PipelineJobStates.MetadataCreated:
                break;
            case PipelineJobStates.Completed:
                entity.DateFinished = DateTime.UtcNow;
                entity.VirusDefinition = request.PipelineDeposit.VirusDefinition;
                break;
            case PipelineJobStates.CompletedWithErrors:
                entity.DateFinished = DateTime.UtcNow;
                entity.Errors = request.PipelineDeposit.Errors;
                break;
        }
        if (request.PipelineDeposit.Status.HasText())
        {
            entity.Status = request.PipelineDeposit.Status;
        }
        dbContext.PipelineRunJobs.Update(entity);

        try
        {
            logger.LogInformation("Saving Pipeline Job entity " + entity.Id + " to DB for deposit " + deposit.MintedId);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Issue saving the Pipeline run job state.");
            return Result.Fail(ErrorCodes.UnknownError, e.Message);
        }

        var callerIdentity = request.User.GetCallerIdentity();
        logger.LogInformation("Pipeline job " + entity.Id + " was updated by " + callerIdentity);
        return Result.Ok();
    }
}