using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Results;
using MediatR;
using DigitalPreservation.Utils;
using Preservation.API.Mutation;
using Storage.Client;

namespace Preservation.API.Features.Repository.Requests;

public class GetLightweightResource(string pathUnderRoot, string? version) : IRequest<Result<PreservedResource?>>
{
    public string PathUnderRoot { get; } = pathUnderRoot;
    public string? Version { get; } = version;
}

public class GetLightweightResourceHandler(
    IStorageApiClient storageApiClient,
    ResourceMutator resourceMutator) : IRequestHandler<GetLightweightResource, Result<PreservedResource?>>
{
    public async Task<Result<PreservedResource?>> Handle(GetLightweightResource request, CancellationToken cancellationToken)
    {
        var result = await storageApiClient.GetLightweightResource(request.PathUnderRoot, request.Version);
        if (result.Value is not null)
        {
            // As GetResource does: the Storage API's URIs mean nothing to a caller of this API.
            resourceMutator.MutateStorageResource(result.Value);
            if (request.Version.HasText() && result.Value is ArchivalGroup archivalGroup)
            {
                // The IIIF views project the current version only (issue #261).
                archivalGroup.Iiif = null;
                archivalGroup.IiifCs = null;
            }
        }
        return result;
    }
}