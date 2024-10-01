using DigitalPreservation.Common.Model.PreservationApi;
using DigitalPreservation.Common.Model.Results;
using MediatR;
using Preservation.API.Data;
using Preservation.API.Mutation;

namespace Preservation.API.Features.Deposits.Requests;

public class CreateDeposit(Deposit deposit) : IRequest<Result<Deposit?>>
{
    public Deposit? Deposit { get; } = deposit;
}

public class CreateDepositHandler(
    ILogger<GetDepositHandler> logger,
    PreservationContext dbContext,
    ResourceMutator resourceMutator,
    IIdentityService identityService,
    IDepositWorkingFileLocationProvider locationProvider) : IRequestHandler<CreateDeposit, Result<Deposit?>>
{
    public Task<Result<Deposit?>> Handle(CreateDeposit request, CancellationToken cancellationToken)
    {
        
    }
}