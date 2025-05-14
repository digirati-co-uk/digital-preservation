using System.Security.Claims;
using DigitalPreservation.Common.Model.Mets;
using DigitalPreservation.Common.Model.PreservationApi;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Workspace;
using LeedsDlipServices.Identity;
using MediatR;
using Preservation.API.Data;
using Preservation.API.Mutation;
using Storage.Client;
using Storage.Repository.Common;

namespace Preservation.API.Features.Deposits.Requests;

public class CreateDeposit(Deposit deposit, bool export, ClaimsPrincipal principal) : IRequest<Result<Deposit?>>
{
    public Deposit? Deposit { get; } = deposit;
    public bool Export { get; } = export;
    public ClaimsPrincipal Principal { get; } = principal;
}

public class CreateDepositHandler(
    ILogger<CreateDepositBase> logger,
    PreservationContext dbContext,
    ResourceMutator resourceMutator,
    IIdentityService identityService,
    IStorageApiClient storageApiClient,
    IStorage storage,
    IMetsManager metsManager,
    WorkspaceManagerFactory workspaceManagerFactory
    ) : 
        CreateDepositBase(
            logger, 
            dbContext, 
            resourceMutator, 
            identityService, 
            storageApiClient, 
            storage, 
            metsManager, 
            workspaceManagerFactory), 
        IRequestHandler<CreateDeposit, Result<Deposit?>>
{
    public async Task<Result<Deposit?>> Handle(CreateDeposit request, CancellationToken cancellationToken)
    {
        var result = await HandleBase(request, cancellationToken);
        return result;
    }
}