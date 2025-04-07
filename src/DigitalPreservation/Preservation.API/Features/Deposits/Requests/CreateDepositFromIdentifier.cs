using System.Security.Claims;
using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Mets;
using DigitalPreservation.Common.Model.PreservationApi;
using DigitalPreservation.Common.Model.Results;
using LeedsDlipServices.Identity;
using MediatR;
using Preservation.API.Data;
using Preservation.API.Mutation;
using Storage.Client;
using Storage.Repository.Common;

namespace Preservation.API.Features.Deposits.Requests;

public class CreateDepositFromIdentifier(SchemaAndValue schemaAndValue, ClaimsPrincipal principal) : IRequest<Result<Deposit?>>
{
    public SchemaAndValue SchemaAndValue { get; } = schemaAndValue;
    public ClaimsPrincipal Principal { get; } = principal;
}

public class CreateDepositFromIdentifierHandler(
    ILogger<CreateDepositBase> logger,
    PreservationContext dbContext,
    ResourceMutator resourceMutator,
    IIdentityService identityService,
    IStorageApiClient storageApiClient,
    IStorage storage,
    IMetsManager metsManager) : 
    CreateDepositBase(logger, dbContext, resourceMutator, identityService, storageApiClient, storage, metsManager), 
    IRequestHandler<CreateDepositFromIdentifier, Result<Deposit?>>
{
    private readonly IIdentityService identityService1 = identityService;

    public async Task<Result<Deposit?>> Handle(CreateDepositFromIdentifier request, CancellationToken cancellationToken)
    {
        var identityResult = await identityService1.GetIdentityBySchema(request.SchemaAndValue, cancellationToken);
        if (identityResult is { Success: true, Value: not null })
        {
            var identity = identityResult.Value;
            var deposit = new Deposit
            {
                ArchivalGroup = identity.RepositoryUri,
                ArchivalGroupName = identity.Title,
                UseObjectTemplate = false,
                SubmissionText =
                    $"Deposit created from identity: {request.SchemaAndValue.Schema} = {request.SchemaAndValue.Value}"
            };
            var createDepositRequest = new CreateDeposit(deposit, false, request.Principal);
            var result = await HandleBase(createDepositRequest, cancellationToken);
            return result;
        }
        return Result.Fail<Deposit?>(identityResult.ErrorCode ?? ErrorCodes.UnknownError, identityResult.ErrorMessage);
    }
}