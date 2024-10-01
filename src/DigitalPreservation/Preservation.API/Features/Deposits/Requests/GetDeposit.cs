using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.PreservationApi;
using DigitalPreservation.Common.Model.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Preservation.API.Data;
using Preservation.API.Mutation;

namespace Preservation.API.Features.Deposits.Requests;

public class GetDeposit(string id) : IRequest<Result<Deposit?>>
{
    public string Id { get; } = id;
}

public class GetDepositHandler(
    ILogger<GetDepositHandler> logger,
    PreservationContext dbContext,
    ResourceMutator resourceMutator) : IRequestHandler<GetDeposit, Result<Deposit?>>
{
    public async Task<Result<Deposit?>> Handle(GetDeposit request, CancellationToken cancellationToken)
    {
        try
        {
            var entity = await dbContext.Deposits.SingleOrDefaultAsync(d => d.MintedId == request.Id, cancellationToken);
            if (entity != null)
            {
                return Result.Ok(resourceMutator.MutateDeposit(entity));
            }
            return Result.Fail<Deposit?>(ErrorCodes.NotFound, $"Deposit {request.Id} not found");
        }
        catch (Exception e)
        {
            logger.LogError(e, e.Message);
            return Result.Fail<Deposit?>(ErrorCodes.UnknownError, $"Deposit {request.Id} error: {e.Message}");
        }
    }
}