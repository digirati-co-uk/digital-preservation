using DigitalPreservation.Common.Model.PreservationApi;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Mets;
using DigitalPreservation.Workspace;
using IIIF.Presentation;
using IIIF.Presentation.V3;
using MediatR;
using Preservation.API.Data;
using Preservation.API.Mutation;
using Storage.Client;

namespace Preservation.API.Features.Deposits.Requests;

public class GetDepositAsIIIFManifest(string id, string baseUrl) : IRequest<Result<Manifest>>
{
    public string Id { get; } = id;
    public string BaseUrl { get; } = baseUrl;
}

public class GetDepositAsIIIFManifestHandler(
    IMetsParser metsParser,
    ILogger<GetDepositHandler> logger,
    PreservationContext dbContext,
    IStorageApiClient storageApiClient,
    ResourceMutator resourceMutator,
    WorkspaceManagerFactory workspaceManagerFactory) : 
    GetDepositBase(logger, dbContext, storageApiClient, resourceMutator, workspaceManagerFactory), IRequestHandler<GetDepositAsIIIFManifest, Result<Manifest>>
{
    public async Task<Result<Manifest>> Handle(GetDepositAsIIIFManifest request, CancellationToken cancellationToken)
    {
        var getDepositResult = await GetDeposit(request.Id, cancellationToken);
        if (!getDepositResult.Success)
        {
            return Result.FailNotNull<Manifest>(getDepositResult.ErrorCode!, getDepositResult.ErrorMessage);
        }
        var wrapperResult = await metsParser.GetMetsFileWrapper(getDepositResult.Value!.Files!);
        if (!wrapperResult.Success)
        {
            return Result.FailNotNull<Manifest>(wrapperResult.ErrorCode!, wrapperResult.ErrorMessage);
        }
        var deposit = getDepositResult.Value!;
        var wrapper = wrapperResult.Value;
        var manifest = MakeManifest(deposit, wrapper, request.BaseUrl);
        return Result.OkNotNull(manifest);
    }

    private Manifest MakeManifest(Deposit deposit, MetsFileWrapper? wrapper, string manifestBaseUrl)
    {
        var manifest = new Manifest
        {
            Id = manifestBaseUrl
        };
        
        // label and metadata from AG / path / deposit - whatever is available at time
        
        // flatten files (already flat) into `items`
        //   thumbnails and large derivatives
        //   create on demand (not automatically) - from basic image formats
        //   Where to keep them? In deposit? Bit shaky. Another s3 location?  __thumbnails directory /large /small
        //   
        // turn logical structmap into ranges
        // what else
        
        
        
        manifest.EnsurePresentation3Context();
        return manifest;
    }
}