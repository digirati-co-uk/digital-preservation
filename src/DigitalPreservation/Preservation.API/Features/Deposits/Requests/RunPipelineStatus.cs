using System.Security.Claims;
using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Core.Auth;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Preservation.API.Data;
using System.Text.Json;
using DigitalPreservation.Common.Model.PipelineApi;
using Preservation.API.Data.Entities;

namespace Preservation.API.Features.Deposits.Requests;

public class RunPipelineStatus(string id, string? depositId, string status, ClaimsPrincipal user, string? runUser, string? errors) : IRequest<Result>
{
    public readonly ClaimsPrincipal User = user;
    public string Id { get; } = id;
    public string? DepositId { get; } = depositId;
    public string Status { get; set; } = status;
    public string? RunUser { get; set; } = runUser;
    public string? Errors { get; set; } = errors;
}

public class RunPipelineStatusHandler(
    ILogger<RunPipelineStatusHandler> logger,
    PreservationContext dbContext) : IRequestHandler<RunPipelineStatus, Result>
{
    public async Task<Result> Handle(RunPipelineStatus request, CancellationToken cancellationToken)
    {
        var deposit =
            await dbContext.Deposits.SingleOrDefaultAsync(d => d.MintedId == request.DepositId, cancellationToken);

        if (deposit == null)
        {
            return Result.Fail(ErrorCodes.NotFound, "No deposit for deposit id " + request.DepositId);
        }
        var callerIdentity = request.User.GetCallerIdentity();

        var entity = await dbContext.PipelineRunJobs.SingleOrDefaultAsync(
            d => d.Deposit == request.DepositId && d.Id == request.Id, cancellationToken) ?? new PipelineRunJob
        {
            ArchivalGroup = deposit.ArchivalGroupName,
            PipelineJobJson = JsonSerializer.Serialize(request),
            Id = request.Id,
            Status = request.Status,
            Deposit = deposit.MintedId,
            LastUpdated = DateTime.UtcNow,
            RunUser = request.RunUser,
            Errors = request.Errors
        };


        switch (request.Status)
        {
            case PipelineJobStates.Waiting:
                entity.DateSubmitted = DateTime.UtcNow;
                dbContext.PipelineRunJobs.Add(entity);
                break;
            case PipelineJobStates.Running:
                entity.DateBegun = DateTime.UtcNow;
                entity.Status = request.Status;
                dbContext.PipelineRunJobs.Update(entity);
                break;
            case PipelineJobStates.Completed:
                entity.DateFinished = DateTime.UtcNow;
                entity.Status = request.Status;
                dbContext.PipelineRunJobs.Update(entity);
                break;
            case PipelineJobStates.CompletedWithErrors:
                entity.DateFinished = DateTime.UtcNow;
                entity.Status = request.Status;
                entity.Errors = request.Errors;
                dbContext.PipelineRunJobs.Update(entity);
                break;
        }

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

        logger.LogInformation("Pipeline job " + entity.Id + " was run by " + callerIdentity);
        return Result.Ok();
    }
}