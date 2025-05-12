using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.ChangeDiscovery;
using DigitalPreservation.Common.Model.Results;
using MediatR;
using Preservation.API.Data;
using Preservation.API.Data.Entities;
using Storage.Client;

namespace Preservation.API.Features.Activity.Requests;

public class PushArchivalGroupUpdate(DigitalPreservation.Common.Model.ChangeDiscovery.Activity? activity) : IRequest<Result>
{
    public DigitalPreservation.Common.Model.ChangeDiscovery.Activity? Activity { get; } = activity;
}

public class PushArchivalGroupUpdateHandler(
    IStorageApiClient storageApiClient,
    PreservationContext dbContext) : IRequestHandler<PushArchivalGroupUpdate, Result>
{
    public async Task<Result> Handle(PushArchivalGroupUpdate request, CancellationToken cancellationToken)
    {
        if (request.Activity is null)
        {
            return Result.Fail(ErrorCodes.BadRequest, "No activity provided in body");
        }

        if (request.Activity.Type != ActivityTypes.Update)
        {
            return Result.Fail(ErrorCodes.BadRequest, "Only Update activities are supported");
        }

        if (request.Activity.Object is null)
        {
            return Result.Fail(ErrorCodes.BadRequest, "No object provided in body");
        }

        if (request.Activity.Object.Type != nameof(ArchivalGroup))
        {
            return Result.Fail(ErrorCodes.BadRequest, "Only ArchivalGroup objects are supported");
        }
        
        var resourceTypeResult = await storageApiClient.GetResourceType(request.Activity.Object.Id.PathAndQuery);
        if (resourceTypeResult.Success)
        {
            if (resourceTypeResult.Value == nameof(ArchivalGroup))
            {
                var agEvent = new ArchivalGroupEvent
                {
                    EventDate = DateTime.UtcNow,
                    ArchivalGroup = request.Activity.Object.Id,
                    FromVersion = "(push)"
                };
                dbContext.ArchivalGroupEvents.Add(agEvent);
                await dbContext.SaveChangesAsync(cancellationToken);
                return Result.Ok();
            }

            return Result.Fail(ErrorCodes.BadRequest, "Object at path " + request.Activity.Object.Id.PathAndQuery + " is not an ArchivalGroup, it's a " + resourceTypeResult.Value + ".");
        }
        
        return Result.Fail(resourceTypeResult.ErrorCode!, "Only 'ArchivalGroup' objects are supported");
        
    }
}