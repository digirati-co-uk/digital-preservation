using System.Security.Claims;
using DigitalPreservation.Core.Auth;
using DigitalPreservation.Mets;
using Storage.Repository.Common.Mets;
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
    ILogger<CreateDepositHandler> logger,
    PreservationContext dbContext,
    ResourceMutator resourceMutator,
    IIdentityService identityService,
    IStorageApiClient storageApiClient,
    IStorage storage,
    IMetsManager metsManager,
    MetsFromArchivalGroup metsFromArchivalGroup,
    WorkspaceManagerFactory workspaceManagerFactory,
    IMetsParser metsParser,
    IClientDirectory clientDirectory
    ) :
        CreateDepositBase(
            logger,
            dbContext,
            resourceMutator,
            identityService,
            storageApiClient,
            storage,
            metsManager,
            metsFromArchivalGroup,
            workspaceManagerFactory,
            metsParser,
            clientDirectory),
        IRequestHandler<CreateDeposit, Result<Deposit?>>
{
    public async Task<Result<Deposit?>> Handle(CreateDeposit request, CancellationToken cancellationToken)
    {
        var result = await HandleBase(request, cancellationToken);
        return result;
    }
}